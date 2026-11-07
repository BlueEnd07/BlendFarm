using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    [TestClass]
    public class NodeManagementTests
    {
        [DataTestMethod]
        [DataRow(1, 5)]
        [DataRow(2, 10)]
        [DataRow(3, 20)]
        [DataRow(4, 40)]
        [DataRow(5, 60)]
        [DataRow(100, 60)]
        [DataRow(0, 5)]
        public void BackoffIsBounded(int attempt, int seconds)
            => Assert.AreEqual(seconds, NodeConnectionPolicy.ReconnectDelay(attempt).TotalSeconds);

        [DataTestMethod]
        [DataRow(SocketError.ConnectionRefused)]
        [DataRow(SocketError.TimedOut)]
        [DataRow(SocketError.NetworkUnreachable)]
        public void NetworkFailuresExplainFirewall(SocketError error)
            => Assert.AreEqual(NodeConnectionPolicy.Unreachable, NodeConnectionPolicy.DescribeFailure(new SocketException((int)error)));

        [TestMethod]
        public void FailuresPreserveUsefulClassification()
        {
            Assert.AreEqual("Unknown host", NodeConnectionPolicy.DescribeFailure(new SocketException((int)SocketError.HostNotFound)));
            Assert.AreEqual("Wrong password", NodeConnectionPolicy.DescribeFailure(new InvalidDataException("Authentication failed (rejected)")));
            Assert.AreEqual(NodeConnectionPolicy.Unreachable, NodeConnectionPolicy.DescribeFailure(new InvalidOperationException("Protocol check failed", new TimeoutException())));
            var mismatch = new NodeProtocolException("7", "8");
            StringAssert.Contains(NodeConnectionPolicy.DescribeFailure(mismatch), "node runs protocol 7, client protocol 8");
            Assert.AreEqual("Unexpected", NodeConnectionPolicy.DescribeFailure(new Exception("Unexpected")));
        }

        private sealed class PreparedNode : RenderNode
        {
            public string PreparedVersion;
            public override Task<PrepareResponse> PrepareVersion(string version)
            {
                PreparedVersion = version;
                UpdatePreparedStatus(true);
                return Task.FromResult(new PrepareResponse { Success = true });
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitUntil(Func<bool> condition, int milliseconds = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.IsTrue(condition(), "Condition did not become true before timeout");
        }

        [TestMethod]
        public async Task UnexpectedDisconnect_ReconnectsAndPreparesCurrentVersion()
        {
            int port = FreePort();
            var server = new RenderServer(port, -1, true, IPAddress.Loopback);
            var node = new PreparedNode { Name = "reconnect", Address = $"127.0.0.1:{port}" };
            string version = "initial";
            node.ConfigureReconnect(true, () => version);
            server.Start();
            try
            {
                await Task.Delay(300);
                await node.ConnectAndPrepare(version);
                Assert.IsNotNull(node.ConnectedSince);
                server.Stop();
                await WaitUntil(() => !node.Connected && node.ReconnectStatus.Length > 0);
                Assert.IsFalse(node.IsPrepared);
                version = "current";
                server = new RenderServer(port, -1, true, IPAddress.Loopback);
                server.Start();
                await WaitUntil(() => node.Connected && node.PreparedVersion == "current");
                await WaitUntil(() => node.ReconnectStatus == "");
                Assert.IsTrue(node.IsPrepared);
            }
            finally { node.Disconnect(); server.Stop(); }
        }

        [TestMethod]
        public async Task ExplicitDisconnect_DoesNotReconnect()
        {
            int port = FreePort();
            var server = new RenderServer(port, -1, true, IPAddress.Loopback);
            var node = new PreparedNode { Name = "manual", Address = $"127.0.0.1:{port}" };
            node.ConfigureReconnect(true, () => "test");
            server.Start();
            try
            {
                await Task.Delay(300);
                await node.Connect();
                node.Disconnect();
                await Task.Delay(5500);
                Assert.IsFalse(node.Connected);
                Assert.AreEqual("", node.ReconnectStatus);
            }
            finally { node.Disconnect(); server.Stop(); }
        }

        [TestMethod]
        public async Task PendingReconnect_CanBeCancelledBySetting()
        {
            int port = FreePort();
            var server = new RenderServer(port, -1, true, IPAddress.Loopback);
            var node = new PreparedNode { Name = "cancel-retry", Address = $"127.0.0.1:{port}" };
            node.ConfigureReconnect(true, () => "test");
            server.Start();
            try
            {
                await Task.Delay(300);
                await node.Connect();
                server.Stop();
                await WaitUntil(() => node.IsReconnecting);
                node.ConfigureReconnect(false, null);
                server = new RenderServer(port, -1, true, IPAddress.Loopback);
                server.Start();
                await Task.Delay(5500);
                Assert.IsFalse(node.Connected);
                Assert.IsFalse(node.IsReconnecting);
            }
            finally { node.Disconnect(); server.Stop(); }
        }

        [TestMethod]
        public async Task InitialFailure_IsClassifiedAndDoesNotStartReconnect()
        {
            var node = new RenderNode { Name = "refused", Address = $"127.0.0.1:{FreePort()}" };
            node.ConfigureReconnect(true, () => "test");
            string reported = null;
            node.ConnectionFailed += (sender, message) => reported = message;
            await Assert.ThrowsExceptionAsync<SocketException>(() => node.Connect());
            Assert.AreEqual(NodeConnectionPolicy.UnreachableMessage(node.Address), node.Exception);
            Assert.AreEqual(node.Exception, reported);
            Assert.AreEqual("", node.ReconnectStatus);
        }
    }
}
