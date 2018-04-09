using Common;
using Common.Cryptography.KeyExchange;
using Server.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.SetError(new TimeStampWriter(Console.Error, "HH:mm:ss.fff"));
            Console.SetOut(new TimeStampWriter(Console.Out, "HH:mm:ss.fff"));

            SessionManager manager = new SessionManager(120 * TimeSpan.TicksPerSecond, 20);

            Database db = new Database("BankDB", "Resources");

            //Database.User me = db.GetUser("Gabriel Tofvesson");//new Database.User("Gabriel Tofvesson", "Hello, World", "NoRainbow", 1337, true, null, true);


            CryptoRandomProvider random = new CryptoRandomProvider();
            //RSA rsa = null;// new RSA(Resources.e_0x200, Resources.n_0x200, Resources.d_0x200);
            //if (rsa == null)
            //{
            //    Console.ForegroundColor = ConsoleColor.Red;
            //    Console.Error.WriteLine("No RSA keys available! Server identity will not be verifiable!");
            //    Console.ForegroundColor = ConsoleColor.Gray;
            //    Console.WriteLine("Generating session-specific RSA-keys...");
            //    rsa = new RSA(64, 8, 8, 5);
            //    Console.WriteLine("Done!");
            //}

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
                                int idx = cmd[1].IndexOf(':');
                                if (idx == -1) return GenerateResponse(id, "ERROR");
                                string user = cmd[1].Substring(0, idx);
                                string pass = cmd[1].Substring(idx + 1);
                                Database.User usr = db.GetUser(user);
                                if (usr == null || !usr.Authenticate(pass))
                                {
                                    Console.WriteLine("Authentcation failure for user: "+user);
                                    return GenerateResponse(id, "ERROR");
                                }

                                string sess = manager.GetSession(usr, "ERROR");
                                Console.WriteLine("Authentication success for user: "+user+"\nSession: "+sess);
                                associations["session"] = sess;
                                return GenerateResponse(id, sess);
                            }
                        case "Logout":
                            manager.Expire(cmd[1]);
                            Console.WriteLine("Prematurely expired session: "+cmd[1]);
                            break;
                        case "Reg":
                            {
                                int idx = cmd[1].IndexOf(':');
                                if (idx == -1) return GenerateResponse(id, "ERROR");
                                string user = cmd[1].Substring(0, idx);
                                string pass = cmd[1].Substring(idx + 1);
                                if (db.ContainsUser(user)) return GenerateResponse(id, "ERROR");
                                Database.User u = new Database.User(user, pass, random.GetBytes(Math.Abs(random.NextShort() % 60) + 20), 0, true);
                                db.AddUser(u);
                                string sess = manager.GetSession(u, "ERROR");
                                Console.WriteLine("Registered account: " + u.Name + "\nSession: "+sess);
                                associations["session"] = sess;
                                return GenerateResponse(id, sess);
                            }
                        default:
                            return GenerateResponse(id, "ERROR");
                    }

                    return null;
                }, 
                (c, b) =>
                {
                    Console.WriteLine($"Client has {(b ? "C" : "Disc")}onnected");
                    //if(!b && c.assignedValues.ContainsKey("session"))
                    //    manager.Expire(c.assignedValues["session"]);
                });
            server.StartListening();
            
            Console.ReadLine();

            server.StopRunning();
        }

        private static string[] ParseCommand(string cmd, out long id)
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

        private static string GenerateResponse(long id, bool b) => GenerateResponse(id, b.ToString());
        private static string GenerateResponse(long id, int b) => GenerateResponse(id, b.ToString());
        private static string GenerateResponse(long id, long b) => GenerateResponse(id, b.ToString());
        private static string GenerateResponse(long id, float b) => GenerateResponse(id, b.ToString());
        private static string GenerateResponse(long id, double b) => GenerateResponse(id, b.ToString());
        private static string GenerateResponse(long id, string response) => id + ":" + response;
    }
}
