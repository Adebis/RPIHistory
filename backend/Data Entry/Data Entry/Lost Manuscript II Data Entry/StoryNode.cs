using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dialogue_Data_Entry
{
    class StoryNode
    {
        //Which node in the feature graph this story node is presenting
        public int graph_node_id;
        //A list of tuples representing story acts. First item is the name of the act,
        //second item is the id of the target of the act (relative to the current node).
        public List<Tuple<string, int>> story_acts;
        //The text presentation for this node, based on its story acts.
        public string text;
        public string analogy;

        public StoryNode(int graph_node_id_in)
        {
            graph_node_id = graph_node_id_in;
            story_acts = new List<Tuple<string, int>>();
            text = "";
            analogy = "";
        }//end constructor StoryNode
        public StoryNode()
        {
            graph_node_id = -1;
            story_acts = new List<Tuple<string, int>>();
            text = "";
            analogy = "";
        }//end constructor StoryNode

        public void AddStoryAct(string act_name, int target_id)
        {
            story_acts.Add(new Tuple<string, int>(act_name, target_id));
        }//end method AddStoryAct

        //Returns whether or not this story node contains the given story act.
        public bool HasStoryAct(string act_name)
        {
            foreach (Tuple<string, int> story_act in story_acts)
            {
                if (story_act.Item1.Equals(act_name))
                    return true;
            }//end foreach

            return false;
        }//end method HasStoryAct
    }
}
