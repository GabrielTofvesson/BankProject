using Tofvesson.Common;
using Tofvesson.Common.Cryptography.KeyExchange;
using Tofvesson.Net;
using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Tofvesson.Crypto;
using System.Text;

namespace Client
{
    public class BankNetInteractor
    {
        protected static readonly CryptoRandomProvider provider = new CryptoRandomProvider();
        protected static readonly Dictionary<long, OnClientConnectStateChanged> changeListeners = new Dictionary<long, OnClientConnectStateChanged>();

        protected Dictionary<long, Tuple<Promise, Common.Proxy<bool>>> promises = new Dictionary<long, Tuple<Promise, Common.Proxy<bool>>>();
        protected NetClient client;
        protected readonly IPAddress addr;
        protected readonly short port;
        protected readonly EllipticDiffieHellman keyExchange;
        public bool IsAlive { get => client != null && client.IsAlive; }
        public bool IsLoggedIn
        {
            get
            {
                if (loginTimeout <= DateTime.Now.Ticks) loginTimeout = -1;
                return loginTimeout != -1;
            }
        }
        protected long loginTimeout = -1;
        protected string sessionID = null;
        public string UserSession { get => sessionID; }
        protected Task sessionChecker;
        public bool RefreshSessions { get; set; }
        

        public BankNetInteractor(string address, short port)
        {
            this.addr = IPAddress.Parse(address);
            this.port = port;
            this.keyExchange = EllipticDiffieHellman.Curve25519(EllipticDiffieHellman.Curve25519_GeneratePrivate(provider));
            RefreshSessions = true; // Default is to auto-refresh sessions
        }

        protected async Task StatusCheck(bool doLoginCheck = false)
        {
            if (doLoginCheck && !IsLoggedIn) throw new SystemException("Not logged in");
            await Connect();
        }
        protected virtual async Task Connect()
        {
            if (IsAlive) return;
            client = new NetClient(
                keyExchange,
                addr,
                port,
                MessageRecievedHandler,
                ClientConnectionHandler,
                65536); // 64 KiB buffer
            client.Connect();
            Task t = new Task(() =>
            {
                while (!client.IsAlive) System.Threading.Thread.Sleep(125);
            });
            t.Start();
            await t;
        }
        public async virtual Task CancelAll()
        {
            if (client == null) return;
            await client.Disconnect();
        }

        public long RegisterListener(OnClientConnectStateChanged stateListener)
        {
            long tkn = GetListenerToken();
            changeListeners[tkn] = stateListener;
            return tkn;
        }

        public void UnregisterListener(long tkn) => changeListeners.Remove(tkn);

        protected virtual string MessageRecievedHandler(string msg, Dictionary<string, string> associated, ref bool keepAlive)
        {
            string response = HandleResponse(msg, out long pID, out bool err);
            if (err || !promises.ContainsKey(pID)) return null;
            var t = promises[pID];
            promises.Remove(pID);
            if (t.Item2) return null; // Promise has been canceled
            var p = t.Item1;
            PostPromise(p, response);
            if (promises.Count == 0) keepAlive = false; // If we aren't awaiting any other promises, disconnect from server
            return null;
        }

        protected virtual void ClientConnectionHandler(NetClient client, bool connect)
        {
            foreach (var listener in changeListeners.Values)
                listener(client, connect);
        }

        public async virtual Task<Promise> CheckAccountAvailability(string username)
        {
            await StatusCheck();
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Avail", username, out long pID));
            return RegisterPromise(pID);
        }

        public async virtual Task<Promise> Authenticate(string username, string password, bool autoRefresh = true)
        {
            await StatusCheck();
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Auth", DataSet(username, password), out long pID));

            return RegisterEventPromise(pID, p =>
            {
                bool b = !p.Value.StartsWith("ERROR");
                if (b) // Set proper state before notifying listener
                {
                    RefreshTimeout();
                    sessionID = p.Value;
                }
                PostPromise(p.handler, b);
                return false;
            });
        }

        public async virtual Task<Promise> UpdatePassword(string newPass)
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("PassUPD", DataSet(sessionID, newPass), out var pID));
            return RegisterEventPromise(pID, p =>
            {
                bool noerror = !p.Value.StartsWith("ERROR");
                if (noerror) // Set proper state before notifying listener
                    RefreshTimeout();
                PostPromise(p.handler, noerror);
                return false;
            });
        }

        public async virtual Task<Promise> ListUserAccounts() => await ListAccounts(sessionID, true);
        public async virtual Task<Promise> ListAccounts(string username) => await ListAccounts(username, false);
        protected async virtual Task<Promise> ListAccounts(string username, bool bySession)
        {
            await StatusCheck();
            client.Send(CreateCommandMessage("Account_List", DataSet(bySession.ToString(), username), out long PID));
            return RegisterPromise(PID);
        }

        public async virtual Task<Promise> UserInfo()
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Info", sessionID, out long PID));
            return RegisterPromise(PID);
        }

        public async virtual Task<Promise> AccountInfo(string accountName)
        {
            await StatusCheck();
            client.Send(CreateCommandMessage("Account_Get", DataSet(sessionID, accountName), out var pID));
            return RegisterPromise(pID);
        }

        public async virtual Task<Promise> CreateTransaction(string fromAccount, string targetUser, string targetAccount, decimal amount, string message = null)
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Account_Transaction_Create", DataSet(sessionID, fromAccount, targetUser, targetAccount, amount.ToString(), message), out var pID));
            RefreshTimeout();
            return RegisterPromise(pID);
        }

        public async virtual Task<Promise> CloseAccount(string accountName)
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Account_Close", DataSet(sessionID, accountName), out var pID));
            RefreshTimeout();
            return RegisterEventPromise(pID, p =>
            {
                p.handler.Value = p.Value.StartsWith("ERROR").ToString();
                return false;
            });
        }

        public async virtual Task<Promise> CreateAccount(string accountName)
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Account_Create", DataSet(sessionID, accountName), out long PID));
            return RegisterEventPromise(PID, p =>
            {
                RefreshSession(p);
                PostPromise(p.handler, !p.Value.StartsWith("ERROR"));
                return false;
            });
        }

        public async virtual Task<Promise> CheckIdentity(RSA check, ushort nonce)
        {
            long pID;
            Task connect = StatusCheck();
            string ser;
            using(BitWriter writer = new BitWriter())
            {
                writer.WriteULong(nonce);
                ser = CreateCommandMessage("Verify", Convert.ToBase64String(writer.Finalize()), out pID);
            }
            await connect;
            client.Send(ser);
            return RegisterEventPromise(pID, manager =>
            {
                BitReader reader = new BitReader(Convert.FromBase64String(manager.Value));
                try
                {
                    RSA remote = RSA.Deserialize(reader.ReadByteArray(), out int _);
                    PostPromise(manager.handler, new BigInteger(remote.Decrypt(reader.ReadByteArray(), null, true)).Equals((BigInteger)nonce) && remote.Equals(check));
                }
                catch
                {
                    PostPromise(manager.handler, false);
                }
                return false;
            });
        }

        public async virtual Task<Promise> Register(string username, string password)
        {
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            await StatusCheck();
            client.Send(CreateCommandMessage("Reg", DataSet(username, password), out long pID));
            return RegisterEventPromise(pID, p =>
            {
                bool b = !p.Value.StartsWith("ERROR");
                if (b) // Set proper state before notifying listener
                {
                    RefreshTimeout();
                    sessionID = p.Value;
                }
                PostPromise(p.handler, b);
                return false;
            });
        }

        public async virtual Task Logout()
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Logout", sessionID, out long _));
        }

        public async virtual Task<Promise> Refresh()
        {
            await StatusCheck(true);
            client.Send(CreateCommandMessage("Refresh", sessionID, out long pid));
            return RegisterPromise(pid);
        }

        protected Promise RegisterPromise(long pID)
        {
            Promise p = new Promise();
            promises[pID] = new Tuple<Promise, Common.Proxy<bool>>(p, false);
            return p;
        }

        public void CancelPromise(Promise p)
        {
            foreach(var entry in promises)
                if (entry.Value.Item1.Equals(p))
                {
                    entry.Value.Item2.Value = true;
                    break;
                }
        }

        protected Promise RegisterEventPromise(long pID, Func<Promise, bool> a)
        {
            Promise p = RegisterPromise(pID);
            p.handler = new Promise();
            p.Subscribe = p1 =>
            {
                // If true, propogate result
                if (a(p1)) PostPromise(p1.handler, p1.Value);
            };
            return p.handler;
        }

        protected bool RefreshSession(Promise p)
        {
            if (!p.Value.StartsWith("ERROR")) RefreshTimeout();
            return true;
        }

        protected long GetNewPromiseUID()
        {
            long l;
            do l = provider.NextLong();
            while (promises.ContainsKey(l));
            return l;
        }

        protected long GetListenerToken()
        {
            long l;
            do l = provider.NextLong();
            while (changeListeners.ContainsKey(l));
            return l;
        }

        protected void SetAutoRefresh(bool doAR)
        {
            if (RefreshSessions == doAR) return;
            if (RefreshSessions = doAR)
            {
                sessionChecker = new Task(DoRefresh);
                sessionChecker.Start();
            }
        }

        private void DoRefresh()
        {
            // Refresher calls refresh 1500ms before expiry (or asap if less time is available)
            Task.Delay((int)((Math.Min(0, loginTimeout - DateTime.Now.Ticks - 1500)) / TimeSpan.TicksPerMillisecond));
            Task<Promise> t = null;
            if (IsLoggedIn)
            {
                t = Refresh();
                if (RefreshSessions)
                {
                    sessionChecker = new Task(DoRefresh);
                    sessionChecker.Start();
                }
            }
        }

        protected void RefreshTimeout() => loginTimeout = 280 * TimeSpan.TicksPerSecond + DateTime.Now.Ticks;
        protected string CreateCommandMessage(string command, string message, out long promiseID) => command + ":" + (promiseID = GetNewPromiseUID()) + ":" + message;
        protected static string DataSet(params dynamic[] data)
        {
            string[] data1 = new string[data.Length];
            for (int i = 0; i < data.Length; ++i) data1[i] = data[i] == null ? "null" : data[i].ToString();
            return DataSet(data1);
        }
        protected static string DataSet(params string[] data)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var datum in data)
                if(datum!=null)
                    builder.Append(datum.ToString().ToBase64String()).Append(':');

            if (builder.Length != 0) --builder.Length;
            return builder.ToString();
        }

        protected static void PostPromise(Promise p, dynamic value)
        {
            p.Value = value?.ToString() ?? "null";
            p.HasValue = true;
            p.Subscribe?.Invoke(p);
        }

        protected static string HandleResponse(string response, out long promiseID, out bool error)
        {
            error = !long.TryParse(response.Substring(0, Math.Max(0, response.IndexOf(':'))), out promiseID);
            return response.Substring(Math.Max(0, response.IndexOf(':') + 1));
        }

        protected static void AwaitTask(Task t)
        {
            if (IsTaskAlive(t)) t.Wait();
        }

        protected static bool IsTaskAlive(Task t) => t != null && !t.IsCompleted && ((t.Status & TaskStatus.Created) == 0);
        public static void Subscribe(Task<Promise> t, Event e)
        {
            new Task(() =>
            {
                Promise.AwaitPromise(t);
                t.Result.Subscribe = e;
            }).Start();
        }
    }
}
