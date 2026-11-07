using System;
using System.Collections.Generic;
using System.Linq;

namespace LogicReinc.BlendFarm.Client
{
    public partial class BlendFarmManager
    {
        public List<RenderNode> GetRenderNodes()
        {
            var nodes = Nodes.Where(node => node.IsEnabled && node.Connected).ToList();
            if (nodes.Count == 0)
                throw new InvalidOperationException(NodeConnectionPolicy.NoEnabledNodes);
            return nodes;
        }
    }
}
