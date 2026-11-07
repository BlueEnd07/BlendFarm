using LogicReinc.BlendFarm.Server;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    [TestClass]
    public class PortableNetworkTests
    {
        private static int FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
        private static int FreeUdpPort()
        {
            using UdpClient u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)u.Client.LocalEndPoint).Port;
        }

        [TestMethod]
        public async Task Node_AnswersDiscoveryQuery_WithItsAnnouncement()
        {
            int tcpPort = FreeTcpPort();
            int udpPort = FreeUdpPort();
            RenderServer server = new RenderServer(tcpPort, udpPort, true);
            server.Start();
            try
            {
                await Task.Delay(300);
                //Unbound client socket, exactly like DiscoveryClient
                using UdpClient client = new UdpClient(AddressFamily.InterNetwork);
                byte[] query = Encoding.UTF8.GetBytes(DiscoveryClient.QUERY);

                string reply = null;
                using CancellationTokenSource cts = new CancellationTokenSource(5000);
                while (reply == null && !cts.IsCancellationRequested)
                {
                    client.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, udpPort));
                    try
                    {
                        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        wait.CancelAfter(500);
                        //The node's own periodic broadcasts can arrive too, wait for the direct answer
                        UdpReceiveResult result = await client.ReceiveAsync(wait.Token);
                        string msg = Encoding.UTF8.GetString(result.Buffer);
                        if (msg.StartsWith(DiscoveryClient.ANNOUNCE_PREFIX))
                            reply = msg;
                    }
                    catch (OperationCanceledException) { }
                }

                Assert.IsNotNull(reply, "Node did not answer the discovery query");
                string[] parts = reply.Split("||||");
                Assert.AreEqual(Environment.MachineName, parts[1]);
                Assert.AreEqual(tcpPort.ToString(), parts[2]);
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public async Task LocalOnlyNode_AcceptsLocalhost_ButNotNetworkAddress()
        {
            int tcpPort = FreeTcpPort();
            RenderServer server = new RenderServer(tcpPort, -1, true, IPAddress.Loopback);
            Assert.IsTrue(server.LocalOnly);
            server.Start();
            try
            {
                await Task.Delay(300);
                using (TcpClient local = new TcpClient())
                {
                    await local.ConnectAsync("localhost", tcpPort);
                    Assert.IsTrue(local.Connected);
                }

                IPAddress lan = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                    .Select(x => x.Address)
                    .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
                if (lan == null)
                    Assert.Inconclusive("No network address to test against");

                using TcpClient remote = new TcpClient();
                await Assert.ThrowsExceptionAsync<SocketException>(() => remote.ConnectAsync(lan, tcpPort));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void PortableData_ResolvesRelativePathsIntoDataFolder()
        {
            string root = PortableData.Root;
            Assert.IsTrue(System.IO.Directory.Exists(root));
            Assert.AreEqual(System.IO.Path.Combine(root, "BlenderData"), PortableData.Resolve("BlenderData"));
            string rooted = System.IO.Path.GetFullPath("C:/somewhere/else");
            Assert.AreEqual(rooted, PortableData.Resolve(rooted));
        }
    }
}
