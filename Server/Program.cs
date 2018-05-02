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
            string ErrorResponse(long id, string i18n = null) => GenerateResponse(id, $"ERROR{(i18n==null?"":":"+VERBOSE_RESPONSE)}{i18n??""}");

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
                                    return ErrorResponse(id);
                                }
                                Database.User usr = db.GetUser(user);
                                if (usr == null || !usr.Authenticate(pass))
                                {
                                    Output.Error("Authentcation failure for user: "+user);
                                    return ErrorResponse(id);
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
                                    return ErrorResponse(id);
                                }
                                user.accounts.Add(new Database.Account(user, 0, name));
                                db.UpdateUser(user); // Notify database of the update
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
                                    return ErrorResponse(id, error);
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
                                    return ErrorResponse(id, (user==null? "badsession" : account==null? "badacc" : "hasbal"));
                                }
                                break;
                            }
                        case "Reg":
                            {
                                if (!ParseDataPair(cmd[1], out string user, out string pass))
                                {
                                    // Don't print input data to output in case sensitive information was included
                                    Output.Error($"Recieved problematic username or password!");
                                    return ErrorResponse(id, "userpass");
                                }

                                // Cannot register an account with an existing username
                                if (db.ContainsUser(user)) return ErrorResponse(id, "exists");

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
                                    return ErrorResponse(id, "crypterr");
                                }
                            }
                        default:
                            return ErrorResponse(id, "unwn"); // Unknown request
                    }

                    return null;
                }, 
                (c, b) => // Called every time a client connects or disconnects (conn + dc with every command/request)
                {
                    // Output.Info($"Client has {(b ? "C" : "Disc")}onnected");
                    //if(!b && c.assignedValues.ContainsKey("session"))
                    //    manager.Expire(c.assignedValues["session"]);
                });
            server.StartListening();


            bool running = true;

            // Create the command manager
            CommandHandler commands = null;
            commands =
                new CommandHandler(4, "  ", "", "- ")
                .Append(new Command("help").SetAction(() => Output.Raw("Available commands:\n" + commands.GetString())), "Show this help menu")
                .Append(new Command("stop").SetAction(() => running = false), "Stop server")
                .Append(new Command("sess").SetAction(
                    (c, l) => {
                        StringBuilder builder = new StringBuilder();
                        manager.Update(); // Ensure that we don't show expired sessions (artifacts exist until it is necessary to remove them)
                        foreach (var session in manager.Sessions)
                            builder.Append(session.user.Name).Append(" : ").Append(session.sessionID).Append('\n');
                        if (builder.Length == 0) builder.Append("There are no active sessions at the moment");
                        else builder.Length = builder.Length - 1;
                        Output.Raw(builder);
                    }), "Show active client sessions")
                .Append(new Command("list").WithParameter(Parameter.Flag('a')).SetAction(
                    (c, l) => {
                        bool filter = l.HasFlag('a');
                        StringBuilder builder = new StringBuilder();
                        foreach (var user in db.Users(u => !filter || (filter && u.IsAdministrator)))
                            builder.Append(user.Name).Append('\n');
                        if (builder.Length != 0)
                        {
                            builder.Length = builder.Length - 1;
                            Output.Raw(builder);
                        }
                    }), "Show registered users. Add \"-a\" to only list admins")
                .Append(new Command("admin")
                    .WithParameter("username", 'u', Parameter.ParamType.STRING) // Guaranteed to appear in the list passed in the action
                    .WithParameter("true/false", 's', Parameter.ParamType.BOOLEAN, true) // Might show up
                    .SetAction(
                        (c, l) =>
                        {
                            bool set = l.HasFlag('s');
                            string username = l.GetFlag('u');
                            Database.User user = db.GetUser(username);
                            if (user == null) {
                                Output.RawErr($"User \"{username}\" could not be found in the databse!");
                                return;
                            }
                            if (set)
                            {
                                bool admin = bool.Parse(l.GetFlag('s'));
                                if (user.IsAdministrator == admin) Output.Info("The given administrator state was already set");
                                else if (admin) Output.Raw("User is now an administrator");
                                else Output.Raw("User is no longer an administrator");
                                user.IsAdministrator = admin;
                                db.AddUser(user);
                            }
                            else Output.Raw(user.IsAdministrator);
                        }), "Show or set admin status for a user");

            // Set up a persistent terminal-esque input design
            Output.OnNewLine = () => Output.WriteOverwritable(">> ");
            Output.OnNewLine();

            // Server command loop
            while (running)
            {
                // Handle command input
                if (!commands.HandleCommand(Output.ReadLine()))
                    Output.Error("Unknown command. Enter 'help' for a list of supported commands.", true, false);
            }

            server.StopRunning();
        }
    }
}
