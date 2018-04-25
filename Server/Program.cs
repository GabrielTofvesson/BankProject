using Common;
using Common.Cryptography.KeyExchange;
using Server.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Common;
using Tofvesson.Crypto;

namespace Server
{
    class Program
    {
        private const string VERBOSE_RESPONSE = "@string/REMOTE_";
        public static void Main(string[] args)
        {
            // Set up fancy output
            Console.SetError(new TimeStampWriter(Console.Error, "HH:mm:ss.fff"));
            Console.SetOut(new TimeStampWriter(Console.Out, "HH:mm:ss.fff"));

            // Create a client session manager and allow sessions to remain valid for up to 5 minutes of inactivity (300 seconds)
            SessionManager manager = new SessionManager(300 * TimeSpan.TicksPerSecond, 20);

            // Initialize the database
            Database db = new Database("BankDB", "Resources");

            // Create a secure random provider and start getting RSA stuff
            CryptoRandomProvider random = new CryptoRandomProvider();
            Task<RSA> t = new Task<RSA>(() =>
            {
                RSA rsa = new RSA(Resources.e_0x100, Resources.n_0x100, Resources.d_0x100);
                if (rsa == null)
                {
                    Output.Fatal("No RSA keys found! Server identity will not be verifiable!");
                    Output.Info("Generating session-specific RSA-keys...");
                    rsa = new RSA(128, 8, 7, 5);
                    rsa.Save("0x100");
                    Output.Info("Done!");
                }
                return rsa;
            });
            t.Start();

            // Local methods to simplify common operations
            bool ParseDataPair(string cmd, out string user, out string pass)
            {
                int idx = cmd.IndexOf(':');
                user = "";
                pass = "";
                if (idx == -1) return false;
                user = cmd.Substring(0, idx);
                try
                {
                    user = user.FromBase64String();
                    pass = cmd.Substring(idx + 1).FromBase64String();
                }
                catch
                {
                    Output.Error($"Recieved problematic username or password! (User: \"{user}\")");
                    return false;
                }
                return true;
            }

            int ParseDataSet(string cmd, out string[] data)
            {
                List<string> gen = new List<string>();
                int idx;
                while ((idx = cmd.IndexOf(':')) != -1)
                {
                    try
                    {
                        gen.Add(cmd.Substring(0, idx).FromBase64String());
                    }
                    catch
                    {
                        data = null;
                        return -1; // Hard error
                    }
                    cmd = cmd.Substring(idx + 1);
                }
                try
                {
                    gen.Add(cmd.FromBase64String());
                }
                catch
                {
                    data = null;
                    return -1; // Hard error
                }
                data = gen.ToArray();
                return gen.Count;
            }

            string[] ParseCommand(string cmd, out long id)
            {
                int idx = cmd.IndexOf(':'), idx1;
                string sub;
                if (idx == -1 || !(sub = cmd.Substring(idx + 1)).Contains(':') || !long.TryParse(sub.Substring(0, idx1 = sub.IndexOf(':')), out id))
                {
                    id = 0;
                    return null;
                }
                return new string[] { cmd.Substring(0, idx), sub.Substring(idx1 + 1) };
            }

            string GenerateResponse(long id, dynamic d) => id + ":" + d.ToString();

            bool GetUser(string sid, out Database.User user)
            {
                user = manager.GetUser(sid);
                return user != null;
            }

            bool GetAccount(string name, Database.User user, out Database.Account acc)
            {
                acc = user.accounts.FirstOrDefault(a => a.name.Equals(name));
                return acc != null;
            }

            // Create server
            NetServer server = new NetServer(
                EllipticDiffieHellman.Curve25519(EllipticDiffieHellman.Curve25519_GeneratePrivate(random)),
                80,
                (string r, Dictionary<string, string> associations, ref bool s) =>
                {
                    string[] cmd = ParseCommand(r, out long id);

                    // Perform a signature verification by signing a nonce
                    switch (cmd[0])
                    {
                        case "Auth":
                            {
                                if(!ParseDataPair(cmd[1], out string user, out string pass))
                                {
                                    Output.Error($"Recieved problematic username or password! (User: \"{user}\")");
                                    return GenerateResponse(id, "ERROR");
                                }
                                Database.User usr = db.GetUser(user);
                                if (usr == null || !usr.Authenticate(pass))
                                {
                                    Output.Error("Authentcation failure for user: "+user);
                                    return GenerateResponse(id, "ERROR");
                                }

                                string sess = manager.GetSession(usr, "ERROR");
                                Output.Positive("Authentication success for user: "+user+"\nSession: "+sess);
                                associations["session"] = sess;
                                return GenerateResponse(id, sess);
                            }
                        case "Logout":
                            if (manager.Expire(cmd[1])) Output.Info("Prematurely expired session: " + cmd[1]);
                            else Output.Error("Attempted to expire a non-existent session!");
                            break;
                        case "Avail":
                            {
                                try
                                {
                                    string name = cmd[1].FromBase64String();
                                    Output.Info($"Performing availability check on name \"{name}\"");
                                    return GenerateResponse(id, !db.ContainsUser(name));
                                }
                                catch
                                {
                                    Output.Error($"Recieved improperly formatted base64 string: \"{cmd[1]}\"");
                                    return GenerateResponse(id, false);
                                }
                            }
                        case "Account_Create":
                            {
                                if (!ParseDataPair(cmd[1], out string session, out string name) || // Get session id and account name
                                    !GetUser(session, out var user) || // Get user associated with session id
                                    !GetAccount(name, user, out var account))
                                {
                                    // Don't print input data to output in case sensitive information was included
                                    Output.Error($"Recieved problematic session id or account name!");
                                    return GenerateResponse(id, "ERROR");
                                }
                                user.accounts.Add(new Database.Account(user, 0, name));
                                db.AddUser(user); // Notify database of the update
                                return GenerateResponse(id, true);
                            }
                        case "Account_Transaction_Create":
                            {
                                bool systemInsert = false;
                                string error = VERBOSE_RESPONSE;

                                // Default values used here because compiler can't infer their valid parsing further down
                                Database.User user = null;
                                Database.Account account = null;
                                Database.User tUser = null;
                                Database.Account tAccount = null;
                                decimal amount = 0;

                                // Expected data (in order): SessionID, AccountName, TargetUserName, TargetAccountName, Amount, [message]
                                // Do checks to make sure the data we have been given isn't completely silly
                                if (ParseDataSet(cmd[1], out string[] data) < 5 || data.Length > 6)
                                    error += "general";         // General error (parse failed)
                                else if (!GetUser(data[0], out user))
                                    error += "badsession";      // Bad session id (could not get user from session manager)
                                else if (!GetAccount(data[1], user, out account))
                                    error += "badacc";          // Bad source account name
                                else if (!db.ContainsUser(data[2]))
                                    error += "notargetusr";     // Target user could not be found
                                else if (!GetAccount(data[3], tUser = db.GetUser(data[2]), out tAccount))
                                    error += "notargetacc";     // Target account could not be found
                                else if ((!user.IsAdministrator && (systemInsert = (data[2].Equals(user.Name) && account.name.Equals(tAccount.name)))))
                                    error += "unprivsysins";    // Unprivileged request for system-sourced transfer
                                else if (!decimal.TryParse(data[4], out amount) || amount < 0)
                                    error += "badbalance";      // Given sum was not a valid amount
                                else if ((!systemInsert && amount > account.balance))
                                    error += "insufficient";    // Insufficient funds in the source account
                                
                                // Checks if an error ocurred and handles such a situation appropriately
                                if(!error.Equals(VERBOSE_RESPONSE))
                                {
                                    // Don't print input data to output in case sensitive information was included
                                    Output.Error($"Recieved problematic transaction data ({error}): {data?.ToList().ToString() ?? "Data could not be parsed"}");
                                    return GenerateResponse(id, $"ERROR:{error}");
                                }
                                // At this point, we know that all parsed variables above were successfully parsed and valid, therefore: no NREs
                                // Parsed vars: 'user', 'account', 'tUser', 'tAccount', 'amount'
                                // Perform and log the actual transaction
                                return GenerateResponse(id,
                                    db.AddTransaction(
                                        systemInsert ? null : user.Name,
                                        tUser.Name,
                                        amount,
                                        account.name,
                                        tAccount.name,
                                        data.Length == 6 ? data[5] : null
                                    ));
                            }
                        case "Account_Close":
                            {
                                Database.User user = null;
                                Database.Account account = null;
                                if (!ParseDataPair(cmd[1], out string session, out string name) || // Get session id and account name
                                    !GetUser(session, out user) || // Get user associated with session id
                                    !GetAccount(name, user, out account) ||
                                    account.balance != 0)
                                {
                                    // Don't print input data to output in case sensitive information was included
                                    Output.Error($"Recieved problematic session id or account name!");

                                    // Possible errors: bad session id, bad account name, balance in account isn't 0
                                    return GenerateResponse(id, $"ERROR:{VERBOSE_RESPONSE} {(user==null? "badsession" : account==null? "badacc" : "hasbal")}");
                                }
                                break;
                            }
                        case "Reg":
                            {
                                if (!ParseDataPair(cmd[1], out string user, out string pass))
                                {
                                    // Don't print input data to output in case sensitive information was included
                                    Output.Error($"Recieved problematic username or password!");
                                    return GenerateResponse(id, $"ERROR:{VERBOSE_RESPONSE}userpass");
                                }

                                // Cannot register an account with an existing username
                                if (db.ContainsUser(user)) return GenerateResponse(id, $"ERROR:{VERBOSE_RESPONSE}exists");

                                // Create the database user entry and generate a personal password salt
                                Database.User u = new Database.User(user, pass, random.GetBytes(Math.Abs(random.NextShort() % 60) + 20), true);
                                db.AddUser(u);

                                // Generate a session token
                                string sess = manager.GetSession(u, "ERROR");
                                Output.Positive("Registered account: " + u.Name + "\nSession: "+sess);
                                associations["session"] = sess;
                                return GenerateResponse(id, sess);
                            }
                        case "Verify":
                            {
                                BitReader bd = new BitReader(Convert.FromBase64String(cmd[1]));
                                try
                                {
                                    while (!t.IsCompleted) System.Threading.Thread.Sleep(75);
                                    byte[] ser;
                                    using (BitWriter collector = new BitWriter())
                                    {
                                        collector.PushArray(t.Result.Serialize());
                                        collector.PushArray(t.Result.Encrypt(((BigInteger)bd.ReadUShort()).ToByteArray(), null, true));
                                        ser = collector.Finalize();
                                    }
                                    return GenerateResponse(id, Convert.ToBase64String(ser));
                                }
                                catch
                                {
                                    return GenerateResponse(id, $"ERROR:{VERBOSE_RESPONSE}crypterr");
                                }
                            }
                        default:
                            return GenerateResponse(id, $"ERROR:{VERBOSE_RESPONSE}unwn"); // Unknown request
                    }

                    return null;
                }, 
                (c, b) => // Called every time a client connects or disconnects (conn + dc with every command/request)
                {
                    // Output.Info($"Client has {(b ? "C" : "Disc")}onnected");
                    if(!b && c.assignedValues.ContainsKey("session"))
                        manager.Expire(c.assignedValues["session"]);
                });
            server.StartListening();
            
            Console.ReadLine();

            server.StopRunning();
        }
    }
}
