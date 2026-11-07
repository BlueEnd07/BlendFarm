using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Finds render nodes by asking for them instead of listening for their broadcasts.
    /// It sends a query broadcast from an unbound (ephemeral) socket and nodes reply directly to it.
    /// Nothing listens on a fixed port, so Windows Firewall doesn't ask for admin rights,
    /// and replies to our own broadcast are let through by the firewall by default.
    /// </summary>
    public class DiscoveryClient : IDisposable
    {
        /// <summary>
        /// Query sent to the broadcast port, render nodes answer with their announcement
        /// </summary>
        public const string QUERY = "BLENDFARM-DISCOVER";
        /// <summary>
        /// Announcement prefix sent by render nodes, followed by name and port
        /// </summary>
        public const string ANNOUNCE_PREFIX = "BLENDFARM||||";

        //Windows reports ICMP "port unreachable" as a failed receive on UDP sockets unless this is turned off
        private const int SIO_UDP_CONNRESET = -1744830452;

        private readonly int _port;
        private UdpClient _udp = null;
        private CancellationTokenSource _cancel = null;

        /// <summary>
        /// Time between queries
        /// </summary>
        public int IntervalMs { get; set; } = 5000;

        /// <summary>
        /// Name, address and port of a node that answered
        /// </summary>
        public event Action<string, string, int> OnServerDiscovered;

        public DiscoveryClient(int broadcastPort)
        {
            _port = broadcastPort;
        }

        public void Start()
        {
            if (_cancel != null || _port <= 0)
                return;
            _cancel = new CancellationTokenSource();
            CancellationToken token = _cancel.Token;

            //No Bind: the socket gets an ephemeral port on its first send, which is not a "listen"
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.EnableBroadcast = true;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }
            }

            Task.Run(() => QueryLoop(token));
        }

        private async Task QueryLoop(CancellationToken token)
        {
            byte[] query = Encoding.UTF8.GetBytes(QUERY);
            bool receiving = false;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    foreach (IPAddress address in GetBroadcastAddresses())
                    {
                        try
                        {
                            _udp.Send(query, query.Length, new IPEndPoint(address, _port));
                        }
                        catch (SocketException ex)
                        {
                            Console.WriteLine($"Discovery query to {address} failed: {ex.Message}");
                        }
                    }
                    //Receiving needs a bound socket, which the first send provides
                    if (!receiving)
                    {
                        receiving = true;
                        _ = Task.Run(() => ReceiveLoop(token));
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Discovery query failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(IntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult received = await _udp.ReceiveAsync(token);
                    string msg = Encoding.UTF8.GetString(received.Buffer);
                    if (!msg.StartsWith(ANNOUNCE_PREFIX))
                        continue;

                    string[] parts = msg.Split("||||");
                    if (parts.Length < 3 || !int.TryParse(parts[2], out int port))
                        continue;

                    IPAddress from = received.RemoteEndPoint.Address;
                    if (from.IsIPv4MappedToIPv6)
                        from = from.MapToIPv4();
                    //Nodes on this machine are reached through localhost
                    if (IPAddress.IsLoopback(from) || GetLocalAddresses().Contains(from))
                        continue;

                    OnServerDiscovered?.Invoke(parts[1], from.ToString(), port);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                        break;
                    Console.WriteLine($"Discovery receive failed: {ex.Message}");
                    try
                    {
                        await Task.Delay(1000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 255.255.255.255 plus the directed broadcast address of every active IPv4 network,
        /// since Windows only sends the limited broadcast out of one interface
        /// </summary>
        private static List<IPAddress> GetBroadcastAddresses()
        {
            List<IPAddress> result = new List<IPAddress>() { IPAddress.Broadcast };
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask == null)
                            continue;
                        byte[] ip = info.Address.GetAddressBytes();
                        byte[] mask = info.IPv4Mask.GetAddressBytes();
                        if (mask.All(x => x == 0) || mask.All(x => x == 255))
                            continue;
                        byte[] broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        IPAddress address = new IPAddress(broadcast);
                        if (!result.Contains(address))
                            result.Add(address);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to list network interfaces: {ex.Message}");
            }
            return result;
        }

        private static HashSet<IPAddress> GetLocalAddresses()
        {
            HashSet<IPAddress> result = new HashSet<IPAddress>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                        result.Add(info.Address);
            }
            catch { }
            return result;
        }

        public void Stop()
        {
            try
            {
                _cancel?.Cancel();
            }
            catch { }
            _udp?.Dispose();
            _udp = null;
            _cancel = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
