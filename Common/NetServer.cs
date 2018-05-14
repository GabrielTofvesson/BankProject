using Tofvesson.Common.Cryptography.KeyExchange;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tofvesson.Crypto;

namespace Tofvesson.Net
{
    public sealed class NetServer
    {
        private readonly short port;
        private readonly object state_lock = new object();
        private readonly List<ClientStateObject> clients = new List<ClientStateObject>();
        private readonly OnMessageRecieved callback;
        private readonly OnClientConnectStateChanged onConn;
        private readonly IPAddress ipAddress;
        private Socket listener;
        private readonly IKeyExchange exchange;
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

        public NetServer(IKeyExchange exchange, short port, OnMessageRecieved callback, OnClientConnectStateChanged onConn, int bufSize = 16384)
        {
            this.callback = callback;
            this.onConn = onConn;
            this.bufSize = bufSize;
            this.exchange = exchange;
            this.port = port;

            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            this.ipAddress = ipHostInfo.GetIPV4();
            if (ipAddress == null)
                ipAddress = IPAddress.Parse("127.0.0.1"); // If there was no IPv4 result in dns lookup, use loopback address
        }

        public void StartListening()
        {
            bool isAlive = false;
            object lock_await = new object();
            if (!Running && (listenerThread == null || !listenerThread.IsAlive))
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
                            clients.Add(new ClientStateObject(new NetClient(exchange, s, callback, onConn), buffer));
                        }
                        catch (Exception)
                        {
                            if (clients.Count == 0)
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
                bool stop = true;
                try
                {
                    stop = client.SyncListener(ref hasCrypto, ref expectedSize, out bool read, buffer, buf);
                }
                catch { }
                return stop;
            }
            public bool IsConnected() => client.IsConnected;
        }
    }
}
