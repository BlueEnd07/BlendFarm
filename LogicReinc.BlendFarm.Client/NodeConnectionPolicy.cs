using System;
using System.IO;
using System.Net.Sockets;

namespace LogicReinc.BlendFarm.Client
{
    public sealed class NodeProtocolException : InvalidOperationException
    {
        public NodeProtocolException(string nodeVersion, string clientVersion)
            : base($"Version mismatch: node runs protocol {nodeVersion}, client protocol {clientVersion} — use the same BlendFarm version") { }
    }

    public static class NodeConnectionPolicy
    {
        public const string Unreachable = "Can't reach node — is BlendFarm Server running and TCP 15000 open in its firewall?";
        public const int DefaultPort = 15000;

        /// <summary>The "can't reach" message naming the port the node is actually configured with.</summary>
        public static string UnreachableMessage(string address)
            => Unreachable.Replace("TCP " + DefaultPort, "TCP " + PortOf(address));

        public static int PortOf(string address)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                int colon = address.LastIndexOf(':');
                if (colon >= 0 && int.TryParse(address.Substring(colon + 1).Trim(), out int port) && port > 0 && port <= 65535)
                    return port;
            }
            return DefaultPort;
        }
        public const string NoEnabledNodes = "No enabled connected nodes. Connect a node and turn on 'Use for rendering'.";

        public static TimeSpan ReconnectDelay(int attempt)
            => TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, Math.Min(attempt - 1, 4)))));

        public static string DescribeFailure(Exception error, string address = null)
        {
            if (error is AggregateException aggregate)
                error = aggregate.Flatten().InnerExceptions[0];
            if (error is NodeProtocolException)
                return error.Message;
            if (error is SocketException socket)
                return socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.NoData || socket.SocketErrorCode == SocketError.TryAgain
                    ? "Unknown host" : UnreachableMessage(address);
            if (error is TimeoutException)
                return UnreachableMessage(address);
            if (error is InvalidDataException && error.Message.IndexOf("Authentication failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Wrong password";
            if (error.InnerException != null)
                return DescribeFailure(error.InnerException, address);
            return error.Message;
        }
    }
}
