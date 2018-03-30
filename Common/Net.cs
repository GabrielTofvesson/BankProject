using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tofvesson.Crypto
{
    public delegate string OnMessageRecieved(string request, Dictionary<string, string> associations, ref bool stayAlive);
    public delegate void OnClientConnectStateChanged(NetClient client, bool connect);
    public sealed class NetServer
    {
        private readonly short port;
        private readonly object state_lock = new object();
        private readonly List<ClientStateObject> clients = new List<ClientStateObject>();
        private readonly OnMessageRecieved callback;
        private readonly OnClientConnectStateChanged onConn;
        private readonly IPAddress ipAddress;
        private Socket listener;
        private readonly RSA crypto;
        private readonly byte[] ser_cache;
        private readonly int bufSize;

        private bool state_running = false;
        private Thread listenerThread;


        public int Count
        {
            get
            {
                return clients.Count;
            }
        }

        public bool Running
        {
            get
            {
                lock (state_lock) return state_running;
            }

            private set
            {
                lock (state_lock) state_running = value;
            }
        }

        public NetServer(RSA crypto, short port, OnMessageRecieved callback, OnClientConnectStateChanged onConn, int bufSize = 16384)
        {
            this.callback = callback;
            this.onConn = onConn;
            this.bufSize = bufSize;
            this.crypto = crypto;
            this.port = port;
            this.ser_cache = crypto.Serialize(); // Keep this here so we don't wastefully re-serialize every time we get a new client

            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            this.ipAddress = ipHostInfo.GetIPV4();
            if (ipAddress == null)
                ipAddress = IPAddress.Parse("127.0.0.1"); // If there was no IPv4 result in dns lookup, use loopback address
        }

        public void StartListening()
        {
            bool isAlive = false;
            object lock_await = new object();
            if(!Running && (listenerThread==null || !listenerThread.IsAlive))
            {
                Running = true;
                listenerThread = new Thread(() =>
                {

                    this.listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        Blocking = false // When calling Accept() with no queued sockets, listener throws an exception
                    };
                    IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);
                    listener.Bind(localEndPoint);
                    listener.Listen(100);

                    byte[] buffer = new byte[bufSize];
                    lock (lock_await) isAlive = true;
                    Stopwatch limiter = new Stopwatch();
                    while (Running)
                    {
                        limiter.Start();
                        // Accept clients
                        try
                        {
                            Socket s = listener.Accept();
                            s.Blocking = false;
                            clients.Add(new ClientStateObject(new NetClient(s, crypto, callback, onConn), buffer));
                        }
                        catch (Exception)
                        {
                            if(clients.Count==0)
                                Thread.Sleep(25); // Wait a bit before trying to accept another client
                        }

                        // Update clients
                        foreach (ClientStateObject cli in clients.ToArray())
                            // Ensure we are still connected to client
                            if (!(cli.IsConnected() && !cli.Update()))
                            {
                                cli.client.onConn(cli.client, false);
                                clients.Remove(cli);
                                continue;
                            }
                        limiter.Stop();
                        if (limiter.ElapsedMilliseconds < 125) Thread.Sleep(250); // If loading data wasn't heavy, take a break
                        limiter.Reset();
                    }
                })
                {
                    Priority = ThreadPriority.Highest,
                    Name = $"NetServer-${port}"
                };
                listenerThread.Start();
            }

            bool rd;
            do
            {
                Thread.Sleep(25);
                lock (lock_await) rd = isAlive;
            } while (!rd);
        }

        public Task<object> StopRunning()
        {
            Running = false;
            
            return new TaskFactory().StartNew<object>(() =>
            {
                listenerThread.Join();
                return null;
            });
        }

        private class ClientStateObject
        {
            internal NetClient client;
            private bool hasCrypto = false;                  // Whether or not encrypted communication has been etablished
            private Queue<byte> buffer = new Queue<byte>();  // Incoming data buffer
            private int expectedSize = 0;                    // Expected size of next message
            private readonly byte[] buf;

            public ClientStateObject(NetClient client, byte[] buf)
            {
                this.client = client;
                this.buf = buf;
            }

            public bool Update()
            {
                bool stop = client.SyncListener(ref hasCrypto, ref expectedSize, out bool read, buffer, buf);
                return stop;
            }
            public bool IsConnected() => client.IsConnected;
        }
    }
    
    public class NetClient
    {
        private static readonly RandomProvider rp = new CryptoRandomProvider();

        // Thread state lock for primitive values
        private readonly object state_lock = new object();

        // Primitive state values
        private bool state_running = false;

        // Socket event listener
        private Thread eventListener;

        // Communication parameters
        protected readonly Queue<byte[]> messageBuffer = new Queue<byte[]>();
        public readonly Dictionary<string, string> assignedValues = new Dictionary<string, string>();
        protected readonly OnMessageRecieved handler;
        protected internal readonly OnClientConnectStateChanged onConn;
        protected readonly IPAddress target;
        protected readonly int bufSize;
        protected readonly RSA decrypt;
        protected internal long lastComm = DateTime.Now.Ticks; // Latest comunication event (in ticks)
        public RSA RemoteCrypto { get => decrypt; }

        // Connection to peer
        protected Socket Connection { get; private set; }

        // State/connection parameters
        protected Rijndael128 Crypto { get; private set; }
        protected GenericCBC CBC { get; private set; }
        public short Port { get; }
        protected bool Running
        {
            get
            {
                lock (state_lock) return state_running;
            }
            private set
            {
                lock (state_lock) state_running = value;
            }
        }

        protected internal bool IsConnected
        {
            get
            {
                return Connection != null && Connection.Connected && !(Connection.Poll(1, SelectMode.SelectRead) && Connection.Available == 0);
            }
        }

        public bool IsAlive
        {
            get
            {
                return Running || (Connection != null && Connection.Connected) || (eventListener != null && eventListener.IsAlive);
            }
        }

        protected bool ServerSide { get; private set; }


        public NetClient(Rijndael128 crypto, IPAddress target, short port, OnMessageRecieved handler, OnClientConnectStateChanged onConn, int bufSize = 16384)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (target.AddressFamily==AddressFamily.InterNetwork && target.Address == 16777343)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                IPAddress addr = Dns.GetHostEntry(Dns.GetHostName()).GetIPV4();
                if (addr != null) target = addr;
            }
            this.target = target;
            Crypto = crypto;
            if(crypto!=null) CBC = new PCBC(crypto, rp);
            this.bufSize = bufSize;
            this.handler = handler;
            this.onConn = onConn;
            Port = port;
            ServerSide = false;
        }

        internal NetClient(Socket sock, RSA crypto, OnMessageRecieved handler, OnClientConnectStateChanged onConn)
            : this(null, ((IPEndPoint)sock.RemoteEndPoint).Address, (short) ((IPEndPoint)sock.RemoteEndPoint).Port, handler, onConn, -1)
        {
            decrypt = crypto;
            Connection = sock;
            Running = true;
            ServerSide = true;

            // Initiate crypto-handshake by sending public keys
            Connection.Send(NetSupport.WithHeader(crypto.Serialize()));
        }

        public virtual void Connect()
        {
            if (ServerSide) throw new SystemException("Serverside socket cannot connect to a remote peer!");
            NetSupport.DoStateCheck(IsAlive || (eventListener != null && eventListener.IsAlive), false);
            Connection = new Socket(SocketType.Stream, ProtocolType.Tcp);
            Connection.Connect(target, Port);
            Running = true;
            eventListener = new Thread(() =>
            {
                bool cryptoEstablished = false;
                int mLen = 0;
                Queue<byte> ibuf = new Queue<byte>();
                byte[] buffer = new byte[bufSize];
                Stopwatch limiter = new Stopwatch();
                while (Running)
                {
                    limiter.Start();
                    if (SyncListener(ref cryptoEstablished, ref mLen, out bool _, ibuf, buffer))
                        break;
                    if (cryptoEstablished && DateTime.Now.Ticks >= lastComm + (5 * TimeSpan.TicksPerSecond))
                        try
                        {
                            Connection.Send(NetSupport.WithHeader(new byte[0])); // Send a test packet. (Will just send an empty header to the peer)
                            lastComm = DateTime.Now.Ticks;
                        }
                        catch
                        {
                            break; // Connection died
                        }
                    limiter.Stop();
                    if (limiter.ElapsedMilliseconds < 125) Thread.Sleep(250); // If loading data wasn't heavy, take a break
                    limiter.Reset();
                }
                if (ibuf.Count != 0) Debug.WriteLine("Client socket closed with unread data!");
                onConn(this, false);
            })
            {
                Priority = ThreadPriority.Highest,
                Name = $"NetClient-${target}:${Port}"
            };
            eventListener.Start();
        }

        protected internal bool SyncListener(ref bool cryptoEstablished, ref int mLen, out bool acceptedData, Queue<byte> ibuf, byte[] buffer)
        {
            if (cryptoEstablished)
            {
                lock (messageBuffer)
                {
                    foreach (byte[] message in messageBuffer) Connection.Send(NetSupport.WithHeader(message));
                    if(messageBuffer.Count > 0) lastComm = DateTime.Now.Ticks;
                    messageBuffer.Clear();
                }
            }
            if (acceptedData = Connection.Available > 0)
            {
                int read = Connection.Receive(buffer);
                ibuf.EnqueueAll(buffer, 0, read);
                if (read > 0) lastComm = DateTime.Now.Ticks;
            }
            if (mLen == 0 && ibuf.Count >= 4)
                mLen = Support.ReadInt(ibuf.Dequeue(4), 0);
            if (mLen != 0 && ibuf.Count >= mLen)
            {
                // Got a full message. Parse!
                byte[] message = ibuf.Dequeue(mLen);
                lastComm = DateTime.Now.Ticks;

                if (!cryptoEstablished)
                {
                    if (ServerSide)
                    {
                        var nonceText = new string(Encoding.UTF8.GetChars(message));
                        byte[] sign;
                        if(nonceText.StartsWith("Nonce:") && BigInteger.TryParse(nonceText.Substring(6), out BigInteger parse) && (sign=parse.ToByteArray()).Length <= 512)
                        {
                            Connection.Send(NetSupport.WithHeader(decrypt.Encrypt(parse.ToByteArray(), null, true)));
                            Disconnect();
                            return true;
                        }

                        if (Crypto == null)
                        {
                            byte[] m = decrypt.Decrypt(message);
                            if (m.Length == 0) return false;
                            Crypto = Rijndael128.Deserialize(m, out int _);
                        }
                        else
                        {
                            byte[] m = decrypt.Decrypt(message);
                            if (m.Length == 0) return false;
                            CBC = new PCBC(Crypto, m);
                            onConn(this, true);
                        }
                    }
                    else
                    {
                        // Reconstruct RSA object from remote public keys and use it to encrypt our serialized AES key/iv
                        RSA asymm = RSA.Deserialize(message, out int _);
                        Connection.Send(NetSupport.WithHeader(asymm.Encrypt(Crypto.Serialize())));
                        Connection.Send(NetSupport.WithHeader(asymm.Encrypt(CBC.IV)));
                        onConn(this, true);
                    }
                    if (CBC != null)
                        cryptoEstablished = true;
                }
                else
                {
                    // Decrypt the incoming message
                    byte[] read = Crypto.Decrypt(message);

                    // Read the decrypted message length
                    int mlenInner = Support.ReadInt(read, 0);
                    if (mlenInner == 0) return false; // Got a ping packet

                    // Send the message to the handler and get a response
                    bool live = true;
                    string response = handler(read.SubArray(4, 4+mlenInner).ToUTF8String(), assignedValues, ref live);
                    
                    // Send the response (if given one) and drop the connection if the handler tells us to
                    if (response != null) Connection.Send(NetSupport.WithHeader(Crypto.Encrypt(NetSupport.WithHeader(response.ToUTF8Bytes()))));
                    if (!live)
                    {
                        Running = false;
                        try
                        {
                            Connection.Close();
                        }
                        catch (Exception) { }
                        return true;
                    }
                }

                // Reset expexted message length
                mLen = 0;
            }
            return false;
        }

        /// <summary>
        /// Disconnect from server
        /// </summary>
        /// <returns></returns>
        public virtual async Task<object> Disconnect()
        {
            NetSupport.DoStateCheck(IsAlive, true);
            Running = false;
            

            return await new TaskFactory().StartNew<object>(() => { eventListener.Join(); return null; });
        }

        // Methods for sending data to the server
        public bool TrySend(string message) => TrySend(Encoding.UTF8.GetBytes(message));
        public bool TrySend(byte[] message)
        {
            try
            {
                Send(message);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        public virtual void Send(string message) => Send(Encoding.UTF8.GetBytes(message));
        public virtual void Send(byte[] message) {
            NetSupport.DoStateCheck(IsAlive, true);
            lock (messageBuffer) messageBuffer.Enqueue(Crypto.Encrypt(NetSupport.WithHeader(message)));
        }

        public static RSA CheckServerIdentity(string host, short port, RandomProvider provider, long timeout = 10000)
        {
            Socket sock = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                ReceiveTimeout = 5000,
                SendTimeout = 5000
            };
            sock.Blocking = false;
            sock.Connect(host, port);
            List<byte> read = new List<byte>();
            byte[] buf = new byte[1024];

            if (!Read(sock, read, buf, timeout)) return null;
            read.RemoveRange(0, 4);
            RSA remote;
            try
            {
                remote = RSA.Deserialize(read.ToArray(), out int _);
            }
            catch { return null; }
            BigInteger cmp;
            sock.Send(NetSupport.WithHeader(Encoding.UTF8.GetBytes("Nonce:"+(cmp=BigInteger.Abs(new BigInteger(provider.GetBytes(128)))))));
            Thread.Sleep(250); // Give the server ample time to compute the signature
            read.Clear();
            if (!Read(sock, read, buf, timeout)) return null;
            read.RemoveRange(0, 4);
            try
            {
                if (!cmp.Equals(new BigInteger(remote.Encrypt(read.ToArray())))) return null;
            }
            catch { return null; }
            return remote; // Passed signature check
        }

        private static bool Read(Socket sock, List<byte> read, byte[] buf, long timeout)
        {
            Stopwatch sw = new Stopwatch();
            int len = -1;
            sw.Start();
            while ((len == -1 || read.Count < 4) && (sw.ElapsedTicks / 10000) < timeout)
            {
                if (len == -1 && read.Count > 4)
                    len = Support.ReadInt(read, 0);
                
                try
                {
                    int r = sock.Receive(buf);
                    read.AddRange(buf.SubArray(0, r));
                }
                catch { }
            }
            sw.Stop();
            return read.Count - 4 == len && len>0;
        }
    }

    // Helper methods. WithHeader() should really just be in Support.cs
    public static class NetSupport
    {
        public static byte[] WithHeader(string message) => WithHeader(Encoding.UTF8.GetBytes(message));
        public static byte[] WithHeader(byte[] message)
        {
            byte[] nmsg = new byte[message.Length + 4];
            Support.WriteToArray(nmsg, message.Length, 0);
            Array.Copy(message, 0, nmsg, 4, message.Length);
            return nmsg;
        }

        public static byte[] FromHeaded(byte[] msg, int offset) => msg.SubArray(offset + 4, offset + 4 + Support.ReadInt(msg, offset));

        internal static void DoStateCheck(bool state, bool target) {
            if (state != target) throw new InvalidOperationException("Bad state!");
        }
    }
}
