using Avalonia;
using LogicReinc.BlendFarm.Server;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LogicReinc.BlendFarm
{
    public static class LocalServer
    {
        private static RenderServer _server = null;
        private static DiscoveryClient _discovery = null;

        public static BlenderManager Manager => _server?.Blender;

        public static bool Available => _server != null;

        public static int ServerPort { get; } = ServerSettings.Instance.Port;
        public static int BroadcastPort { get; } = ServerSettings.Instance.BroadcastPort;

        public static event Action<RenderServer, Exception> OnServerException;
        public static event Action<RenderServer, Exception> OnBroadcastException;
        public static event Action<string, string, int> OnDiscoveredServer;

        public static void Start()
        {
            if (Available)
                return;

            if (_server == null)
            {
                //Not shared: listen on localhost only, so Windows Firewall never asks for admin rights
                bool share = BlendFarmSettings.Instance.ShareLocalNode;
                _server = new RenderServer(ServerPort, BroadcastPort, false, share ? IPAddress.Any : IPAddress.Loopback);
                _server.OnServerException += (a, b) => OnServerException?.Invoke(a, b);
                _server.OnBroadcastException += (a, b) => OnBroadcastException?.Invoke(a, b);
                _server.OnServerDiscovered += (a, b, c) => OnDiscoveredServer?.Invoke(a, b, c);
            }
            _server.Start();

            //Find nodes by asking for them, which doesn't need a listening port (or a firewall rule)
            if (_discovery == null && BroadcastPort > 0)
            {
                _discovery = new DiscoveryClient(BroadcastPort);
                _discovery.OnServerDiscovered += (a, b, c) => OnDiscoveredServer?.Invoke(a, b, c);
                _discovery.Start();
            }

        }

        public static void Stop()
        {
            if(_server != null)
                _server.Stop();
            _discovery?.Stop();
            _discovery = null;
        }
    }
}
