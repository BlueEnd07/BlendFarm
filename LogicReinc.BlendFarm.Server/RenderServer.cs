using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Top-level server for receiving clients
    /// </summary>
    public class RenderServer
    {
        private const int BROADCAST_INTERVAL = 1500;

        /// <summary>
        /// Blender Manager
        /// </summary>
        public BlenderManager Blender { get; private set; }

        /// <summary>
        /// TCP Port for RenderNode
        /// </summary>
        public int Port { get; private set; } = 15000;
        /// <summary>
        /// UDP Port for RenderNode broadcasting
        /// </summary>
        public int BroadcastPort { get; private set; } = 16342;
        /// <summary>
        /// If the server should also listen for other render node broadcasts
        /// </summary>
        public bool NoBroadcastListen { get; private set; }
        /// <summary>
        /// Address the TCP listener binds to. Loopback keeps the node private to this machine,
        /// which needs no firewall rule (and so no admin rights)
        /// </summary>
        public IPAddress BindAddress { get; private set; } = IPAddress.Any;
        /// <summary>
        /// Only reachable from this machine: no broadcasting or broadcast listening either
        /// </summary>
        public bool LocalOnly => IPAddress.IsLoopback(BindAddress);
        /// <summary>
        /// If the server is active
        /// </summary>
        public bool Active { get; set; } = false;
        /// <summary>
        /// TCP Listener for RenderNode
        /// </summary>
        public TcpListener Listener { get; private set; } = null;
        /// <summary>
        /// UDP Listener for RenderNodes
        /// </summary>
        public UdpClient ListenerUDP { get; private set; } = null;
        /// <summary>
        /// UDP Client for broadcasting
        /// </summary>
        public UdpClient BroadcasterUDP { get; private set; } = null;

        //Background threads
        private Thread _listenerThread = null;
        private Thread _listenerUDPThread = null;
        private Thread _broadcastThread = null;

        private List<RenderServerClientTcp> Clients { get; } = new List<RenderServerClientTcp>();

        /// <summary>
        /// Event whenever an exception occurs in the server
        /// </summary>
        public event Action<RenderServer, Exception> OnServerException;
        /// <summary>
        /// Event whenever an exception occurs while broadcasting
        /// </summary>
        public event Action<RenderServer, Exception> OnBroadcastException;
        /// <summary>
        /// Event whenever a server is discovered from a broadcast
        /// </summary>
        public event Action<string, string, int> OnServerDiscovered;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="port">communication tcp port</param>
        /// <param name="broadcastPort"><=0 means no broadcasting</param>
        /// <param name="bindAddress">address to listen on, null for all interfaces</param>
        public RenderServer(int port, int broadcastPort, bool noBroadcastListen, IPAddress bindAddress = null)
        {
            Port = port;
            BroadcastPort = broadcastPort;
            NoBroadcastListen = noBroadcastListen;
            BindAddress = bindAddress ?? IPAddress.Any;
            Blender = new BlenderManager();

            Console.WriteLine("Server OS: " + SystemInfo.GetOSName());

            //AddWebSocket<RenderServerClient>("", "RenderClients");
        }

        /// <summary>
        /// Start all relevant listeners and clients
        /// </summary>
        public void Start()
        {
            if (_listenerThread != null || Active)
                return;
            Active = true;

            StartRenderListener();

            if (BroadcastPort > 0 && !LocalOnly)
            {
                if (!NoBroadcastListen)
                    StartBroadcastListener();
                StartBroadcaster();
            }
        }

        /// <summary>
        /// Starts the Listener background thread for rendering
        /// </summary>
        private void StartRenderListener()
        {
            _listenerThread = new Thread(async () =>
            {
                try
                {
                    TcpListener listener = new TcpListener(BindAddress, Port);
                    Listener = listener;
                    listener.Start();
                    while (Active)
                    {
                        //A failure handling one connection must not stop the node from listening
                        try
                        {
                            TcpClient client = await listener.AcceptTcpClientAsync();

                            RenderServerClientTcp rClient = new RenderServerClientTcp(Blender, client);
                            lock (Clients)
                                Clients.Add(rClient);
                            rClient.OnDisconnect += (client) =>
                            {
                                lock (Clients)
                                    Clients.Remove(client);
                            };

                            Thread.Sleep(100);
                        }
                        catch (ObjectDisposedException)
                        {
                            //Listener was stopped
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!Active)
                                break;
                            Console.WriteLine("TCP accept exception (still listening):" + ex.Message);
                            await Task.Delay(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("TCP Exception:" + ex.Message);
                    OnServerException?.Invoke(this, ex);
                }
                finally
                {
                    Active = false;
                }
            });
            _listenerThread.Start();
        }
        /// <summary>
        /// Starts the broadcast listener for discovery
        /// </summary>
        private void StartBroadcastListener()
        {
            _listenerUDPThread = new Thread(async () =>
                {
                    try
                    {
                        ListenerUDP = new UdpClient();
                        ListenerUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        ListenerUDP.ExclusiveAddressUse = false;
                        ListenerUDP.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));
                        IPEndPoint broadcastAddress = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
                        string myIP = Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork).ToString();
                        while (Active)
                        {
                            try
                            {
                                UdpReceiveResult received = await ListenerUDP.ReceiveAsync();
                                string ip = received.RemoteEndPoint.ToString();
                                if (ip.Contains(":"))
                                    ip = ip.Substring(0, ip.IndexOf(':'));

                                if (ip != myIP)
                                {
                                    string msg = Encoding.UTF8.GetString(received.Buffer);
                                    if (msg.StartsWith("BLENDFARM||||"))
                                    {
                                        string[] broadcastParts = msg.Split("||||");
                                        string name = broadcastParts[1];
                                        int port = int.Parse(broadcastParts[2]);
                                        OnServerDiscovered?.Invoke(name, ip, port);
                                    }
                                }
                                Thread.Sleep(100);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to receive broadcast due to:" + ex.Message);
                                Thread.Sleep(1000);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnBroadcastException?.Invoke(this, ex);
                    }
                });
            _listenerUDPThread.Start();
        }
        /// <summary>
        /// Starts the broadcaster background thread for discovery
        /// </summary>
        private void StartBroadcaster()
        {
            _broadcastThread = new Thread(async () =>
            {
                try
                {
                    BroadcasterUDP = new UdpClient();
                    BroadcasterUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    BroadcasterUDP.ExclusiveAddressUse = false;
                    BroadcasterUDP.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));
                    IPEndPoint broadcastAddress = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);
                    byte[] broadcastMsg = Encoding.UTF8.GetBytes($"{DiscoveryClient.ANNOUNCE_PREFIX}{Environment.MachineName}||||{Port}");
                    StartDiscoveryResponder(BroadcasterUDP, broadcastMsg);
                    while (Active)
                    {
                        try
                        {
                            BroadcasterUDP.Send(broadcastMsg, broadcastMsg.Length, broadcastAddress);
                            Thread.Sleep(BROADCAST_INTERVAL);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send broadcast due to:" + ex.Message);
                            Thread.Sleep(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnBroadcastException?.Invoke(this, ex);
                }
            });
            _broadcastThread.Start();
        }


        /// <summary>
        /// Answers discovery queries (see DiscoveryClient) on the broadcast socket by sending the
        /// announcement straight back, so clients find this node without listening on a port themselves
        /// </summary>
        private void StartDiscoveryResponder(UdpClient socket, byte[] announcement)
        {
            Thread responder = new Thread(async () =>
            {
                while (Active)
                {
                    try
                    {
                        UdpReceiveResult received = await socket.ReceiveAsync();
                        if (Encoding.UTF8.GetString(received.Buffer).StartsWith(DiscoveryClient.QUERY))
                            socket.Send(announcement, announcement.Length, received.RemoteEndPoint);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!Active)
                            break;
                        //ICMP errors from earlier sends show up here on Windows, keep answering
                        Console.WriteLine($"Discovery responder: {ex.Message}");
                        Thread.Sleep(1000);
                    }
                }
            });
            responder.IsBackground = true;
            responder.Start();
        }

        /// <summary>
        /// Stops all listeners and threads
        /// </summary>
        public void Stop()
        {
            Active = false;
            Listener?.Stop();
            _listenerThread.Join();
            Listener = null;

            List<RenderServerClientTcp> clients = null;
            lock (Clients)
                clients = Clients.ToList();
            foreach (RenderServerClientTcp client in clients)
            {
                try
                {
                    client.Disconnect();
                }
                catch { }
            }
        }
    }
}
