using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicReinc.BlendFarm.Client
{
    public partial class BlendFarmManager
    {
        /// <summary>Enabled nodes that are connected (usable) or still connecting/reconnecting (pending).</summary>
        public (int Usable, int Pending) CountRenderNodes()
        {
            int usable = 0, pending = 0;
            foreach (var node in Nodes.ToList())
            {
                if (!node.IsEnabled) continue;
                if (node.Connected) usable++;
                else if (node.IsReconnecting || !node.IsIdle) pending++;
            }
            return (usable, pending);
        }

        public List<RenderNode> GetRenderNodes()
        {
            var nodes = Nodes.Where(node => node.IsEnabled && node.Connected).ToList();
            if (nodes.Count == 0)
                throw new InvalidOperationException(NodeConnectionPolicy.NoEnabledNodes);
            return nodes;
        }
    }
}
