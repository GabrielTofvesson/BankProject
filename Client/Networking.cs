using Common;
using Common.Cryptography.KeyExchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Common;
using Tofvesson.Crypto;

namespace Client
{
    public class BankNetInteractor
    {
        protected static readonly CryptoRandomProvider provider = new CryptoRandomProvider();
        protected static readonly Dictionary<long, OnClientConnectStateChanged> changeListeners = new Dictionary<long, OnClientConnectStateChanged>();

        protected Dictionary<long, Promise> promises = new Dictionary<long, Promise>();
        protected NetClient client;
        protected readonly IPAddress addr;
        protected readonly short port;
        protected readonly EllipticDiffieHellman keyExchange;
        public bool IsAlive { get => client != null && client.IsAlive; }
        public bool IsLoggedIn
        {
            get
            {
                if (loginTimeout >= DateTime.Now.Ticks) loginTimeout = -1;
                return loginTimeout != -1;
            }
        }
        protected long loginTimeout = -1;
        protected string sessionID = null;
        

        public BankNetInteractor(string address, short port)
        {
            this.addr = IPAddress.Parse(address);
            this.port = port;
            this.keyExchange = EllipticDiffieHellman.Curve25519(EllipticDiffieHellman.Curve25519_GeneratePrivate(provider));
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
            Promise p = promises[pID];
            promises.Remove(pID);
            PostPromise(p, response);
            if (promises.Count == 0) keepAlive = false;
            return null;
        }

        protected virtual void ClientConnectionHandler(NetClient client, bool connect)
        {
            foreach (var listener in changeListeners.Values)
                listener(client, connect);
        }

        public async virtual Task<Promise> CheckAccountAvailability(string username)
        {
            await Connect();
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Avail", username.ToBase64String(), out long pID));
            return RegisterPromise(pID);
        }

        public async virtual Task<Promise> Authenticate(string username, string password)
        {
            await Connect();
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Auth", username.ToBase64String()+":"+password.ToBase64String(), out long pID));

            return RegisterEventPromise(pID, p =>
            {
                bool b = !p.Value.StartsWith("ERROR");
                PostPromise(p.handler, b);
                if (b)
                {
                    loginTimeout = 280 * TimeSpan.TicksPerSecond;
                    sessionID = p.Value;
                }
                return false;
            });
        }

        public async virtual Task<Promise> CreateAccount(string accountName)
        {
            if (!IsLoggedIn) throw new SystemException("Not logged in");
            await Connect();
            client.Send(CreateCommandMessage("Account_Create", $"{sessionID}:{accountName}", out long PID));
            return RegisterEventPromise(PID, RefreshSession);
        }

        public async virtual Task<Promise> CheckIdentity(RSA check, ushort nonce)
        {
            long pID;
            Task connect = Connect();
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
            await Connect();
            client.Send(CreateCommandMessage("Reg", username.ToBase64String() + ":" + password.ToBase64String(), out long pID));
            return RegisterPromise(pID);
        }

        public async virtual Task Logout(string sessionID)
        {
            if (!IsLoggedIn) return; // No need to unnecessarily trigger a logout that we know will fail
            await Connect();
            client.Send(CreateCommandMessage("Logout", sessionID, out long _));
        }

        protected Promise RegisterPromise(long pID)
        {
            Promise p = new Promise();
            promises[pID] = p;
            return p;
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
            if (!p.Value.StartsWith("ERROR")) loginTimeout = 280 * TimeSpan.TicksPerSecond;
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
        protected string CreateCommandMessage(string command, string message, out long promiseID) => command + ":" + (promiseID = GetNewPromiseUID()) + ":" + message;
    }

    public delegate void Event(Promise p);
    public class Promise
    {
        internal Promise handler = null; // For chained promise management
        private Event evt;
        public string Value { get; internal set; }
        public bool HasValue { get; internal set; }
        public Event Subscribe
        {
            get => evt;
            set
            {
                // Allows clearing subscriptions
                if (evt == null || value == null) evt = value;
                else evt += value;
                if (HasValue)
                    evt(this);
            }
        }
        public static Promise AwaitPromise(Task<Promise> p)
        {
            //if (!p.IsCompleted) p.RunSynchronously();
            p.Wait();
            return p.Result;
        }
    }
}
