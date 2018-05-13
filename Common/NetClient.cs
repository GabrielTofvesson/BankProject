using Tofvesson.Common.Cryptography.KeyExchange;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tofvesson.Common;
using Tofvesson.Crypto;

namespace Tofvesson.Net
{
    public delegate string OnMessageRecieved(string request, Dictionary<string, string> associations, ref bool stayAlive);
    public delegate void OnClientConnectStateChanged(NetClient client, bool connect);

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
        protected readonly Queue<byte[]> messageBuffer = new Queue<byte[]>();                           // Outbound communication buffer
        public readonly Dictionary<string, string> assignedValues = new Dictionary<string, string>();   // Local connection-related "variables"
        protected readonly OnMessageRecieved handler;
        protected internal readonly OnClientConnectStateChanged onConn;
        protected readonly IPAddress target;                                                            // Remote target IP-address
        protected readonly int bufSize;                                                                 // Communication buffer size
        protected readonly IKeyExchange exchange;                                                       // Cryptographic key exchange algorithm
        protected internal long lastComm = DateTime.Now.Ticks;
        public IPEndPoint Remote
        {
            get => (IPEndPoint) Connection?.RemoteEndPoint;
        }

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


        public NetClient(IKeyExchange exchange, IPAddress target, short port, OnMessageRecieved handler, OnClientConnectStateChanged onConn, int bufSize = 16384)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (target.AddressFamily == AddressFamily.InterNetwork && target.Address == 16777343)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                IPAddress addr = Dns.GetHostEntry(Dns.GetHostName()).GetIPV4();
                if (addr != null) target = addr;
            }
            this.target = target;
            this.exchange = exchange;
            this.bufSize = bufSize;
            this.handler = handler;
            this.onConn = onConn;
            Port = port;
            ServerSide = false;
        }

        internal NetClient(IKeyExchange exchange, Socket sock, OnMessageRecieved handler, OnClientConnectStateChanged onConn)
            : this(exchange, ((IPEndPoint)sock.RemoteEndPoint).Address, (short)((IPEndPoint)sock.RemoteEndPoint).Port, handler, onConn, -1)
        {
            Connection = sock;
            Running = true;
            ServerSide = true;

            // Initiate crypto-handshake by sending public keys
            Connection.Send(NetSupport.WithHeader(exchange.GetPublicKey()));
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
                    foreach (byte[] message in messageBuffer) Connection.Send(NetSupport.WithHeader(Crypto.Encrypt(message)));
                    if (messageBuffer.Count > 0) lastComm = DateTime.Now.Ticks;
                    messageBuffer.Clear();
                }
            }
            if (acceptedData = Connection.Available > 0)
            {
                int read = Connection.Receive(buffer);
                ibuf.EnqueueAll(buffer, 0, read);
                if (read > 0) lastComm = DateTime.Now.Ticks;
            }
            if (mLen == 0 && BinaryHelpers.TryReadVarInt(ibuf, 0, out mLen))
            {
                ibuf.Dequeue(BinaryHelpers.VarIntSize(mLen));
                if(mLen > 65535) // Problematic message size. Just drop connection
                {
                    Running = false;
                    try
                    {
                        Connection.Close();
                    }
                    catch { }
                    return true;
                }
            }
            if (mLen != 0 && ibuf.Count >= mLen)
            {
                // Got a full message. Parse!
                byte[] message = ibuf.Dequeue(mLen);
                lastComm = DateTime.Now.Ticks;

                if (!cryptoEstablished)
                {
                    if (!ServerSide) Connection.Send(NetSupport.WithHeader(exchange.GetPublicKey()));
                    if (message.Length == 0) return false;
                    try
                    {
                        Crypto = new Rijndael128(exchange.GetSharedSecret(message).ToHexString());
                    }
                    catch
                    {
                        Running = false;
                        try
                        {
                            Connection.Close();
                        }
                        catch { }
                        return true;
                    }
                    CBC = new PCBC(Crypto, rp);
                    cryptoEstablished = true;
                    onConn(this, true);
                }
                else
                {
                    // Decrypt the incoming message
                    byte[] read;
                    try
                    {
                        read = Crypto.Decrypt(message);
                    }
                    catch // Presumably, something weird happened that wasn't expected. Just drop it...
                    {
                        Running = false;
                        try
                        {
                            Connection.Close();
                        }
                        catch { }
                        return true;
                    }

                    // Read the decrypted message length
                    int mlenInner = (int) BinaryHelpers.ReadVarInt(read, 0);
                    int size = BinaryHelpers.VarIntSize(mlenInner);
                    if (mlenInner == 0) return false; // Got a ping packet

                    // Send the message to the handler and get a response
                    bool live = true;
                    string response = handler(read.SubArray(size, size + mlenInner).ToUTF8String(), assignedValues, ref live);

                    // Send the response (if given one) and drop the connection if the handler tells us to
                    if (response != null) Connection.Send(NetSupport.WithHeader(Crypto.Encrypt(NetSupport.WithHeader(response.ToUTF8Bytes()))));
                    if (!live)
                    {
                        Running = false;
                        try
                        {
                            Connection.Close();
                        }
                        catch { }
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
        public virtual async Task Disconnect()
        {
            NetSupport.DoStateCheck(IsAlive, true);
            Running = false;

            await new TaskFactory().StartNew(eventListener.Join);
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
        public virtual void Send(byte[] message)
        {
            NetSupport.DoStateCheck(IsAlive, true);
            lock (messageBuffer) messageBuffer.Enqueue(NetSupport.WithHeader(message));
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
            return read.Count - 4 == len && len > 0;
        }
    }
}
