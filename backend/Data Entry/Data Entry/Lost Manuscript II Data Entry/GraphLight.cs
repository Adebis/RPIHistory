using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dialogue_Data_Entry
{
    class GraphLight
    {
        public List<GraphNodeLight> graph_nodes;
        public GraphLight()
        {
            graph_nodes = new List<GraphNodeLight>();
        }//end constructor GraphLight
        public GraphLight(FeatureGraph base_graph)
        {
            graph_nodes = new List<GraphNodeLight>();
            foreach (Feature temp_feature in base_graph.Features)
            {
                graph_nodes.Add(new GraphNodeLight(temp_feature));
            }//end foreach
        }//end constructor GraphLight
    }
}
