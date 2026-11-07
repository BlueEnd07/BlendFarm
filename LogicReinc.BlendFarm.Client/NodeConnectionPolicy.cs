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
        public const string NoEnabledNodes = "No enabled connected nodes. Connect a node and turn on 'Use for rendering'.";

        public static TimeSpan ReconnectDelay(int attempt)
            => TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, Math.Min(attempt - 1, 4)))));

        public static string DescribeFailure(Exception error)
        {
            if (error is AggregateException aggregate)
                error = aggregate.Flatten().InnerExceptions[0];
            if (error is NodeProtocolException)
                return error.Message;
            if (error is SocketException socket)
                return socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.NoData || socket.SocketErrorCode == SocketError.TryAgain
                    ? "Unknown host" : Unreachable;
            if (error is TimeoutException)
                return Unreachable;
            if (error is InvalidDataException && error.Message.IndexOf("Authentication failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Wrong password";
            if (error.InnerException != null)
                return DescribeFailure(error.InnerException);
            return error.Message;
        }
    }
}
