using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Client
{
    public class BankNetInteractor
    {
        protected static readonly CryptoRandomProvider provider = new CryptoRandomProvider();
        protected static readonly Dictionary<long, OnClientConnectStateChanged> changeListeners = new Dictionary<long, OnClientConnectStateChanged>();

        protected Dictionary<long, Promise> promises = new Dictionary<long, Promise>();
        protected NetClient client;
        private bool authenticating = true, authenticated = false;
        public bool Authenticating { get => authenticating; }
        public bool PeerIsAuthenticated { get => authenticated; }
        public RSA AuthenticatedKeys { get; private set; }
        public bool IsAlive { get => client.IsAlive; }
        
        public BankNetInteractor(string address, short port, bool checkIdentity = true)
        {
            if(checkIdentity)
                new Task(() =>
                {
                    AuthenticatedKeys = NetClient.CheckServerIdentity(address, port, provider);
                    authenticating = false;
                    authenticated = AuthenticatedKeys != null;
                }).Start();
            else
            {
                authenticating = false;
                authenticated = false;
            }
            var addr = System.Net.IPAddress.Parse(address);
            client = new NetClient(
                new Rijndael128(
                    Convert.ToBase64String(provider.GetBytes(64)), // 64-byte key  (converted to base64)
                    Convert.ToBase64String(provider.GetBytes(64))  // 64-byte salt (converted to base64)
                    ),
                addr,
                port,
                MessageRecievedHandler,
                ClientConnectionHandler,
                65536); // 64 KiB buffer
        }

        public virtual Task Connect()
        {
            client.Connect();
            Task t = new Task(() =>
            {
                while (!client.IsAlive) System.Threading.Thread.Sleep(125);
            });
            t.Start();
            return t;
        }
        public async virtual Task<object> Disconnect() => await client.Disconnect();

        public long RegisterListener(OnClientConnectStateChanged stateListener)
        {
            long tkn;
            changeListeners[tkn = GetListenerToken()] = stateListener;
            return tkn;
        }

        public void UnregisterListener(long tkn) => changeListeners.Remove(tkn);

        protected virtual string MessageRecievedHandler(string msg, Dictionary<string, string> associated, ref bool keepAlive)
        {
            string response = HandleResponse(msg, out long pID, out bool err);
            if (err || !promises.ContainsKey(pID)) return null;
            Promise p = promises[pID];
            promises.Remove(pID);
            p.Value = response;
            p.HasValue = true;
            p.Subscribe?.Invoke(p);
            return null;
        }

        protected virtual void ClientConnectionHandler(NetClient client, bool connect)
        {
            foreach (var listener in changeListeners.Values)
                listener(client, connect);
        }

        public virtual Promise CheckAccountAvailability(string username)
        {
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Avail", username, out long pID));
            Promise p = new Promise();
            promises[pID] = p;
            return p;
        }

        public virtual Promise Authenticate(string username, string password)
        {
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Auth", username+":"+password, out long pID));
            Promise p = new Promise();
            promises[pID] = p;
            return p;
        }

        public virtual Promise Register(string username, string password)
        {
            if (username.Length > 60)
                return new Promise
                {
                    HasValue = true,
                    Value = "ERROR"
                };
            client.Send(CreateCommandMessage("Reg", username + ":" + password, out long pID));
            Promise p = new Promise();
            promises[pID] = p;
            return p;
        }

        public virtual void Logout(string sessionID)
            => client.Send(CreateCommandMessage("Logout", sessionID, out long _));

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

        protected static void PostPromise(Promise p, string value)
        {
            p.Value = value;
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
        private Event evt;
        public string Value { get; internal set; }
        public bool HasValue { get; internal set; }
        public Event Subscribe
        {
            get => evt;
            set
            {
                evt = value;
                if (HasValue)
                    evt(this);
            }
        }
    }
}
