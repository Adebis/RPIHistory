using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dialogue_Data_Entry
{
    class StorySegment
    {
        //A story is a list of story nodes.
        private List<StoryNode> sequence;
        public int anchor_node_id;
        public int starting_turn;

        public StorySegment()
        {
            sequence = new List<StoryNode>();
            anchor_node_id = -1;
            starting_turn = 0;
        }//end constructor StorySegment
        public StorySegment(StoryNode anchor_node)
        {
            sequence = new List<StoryNode>();
            sequence.Add(anchor_node);
            anchor_node_id = anchor_node.graph_node_id;
            starting_turn = 0;
        }//end constructor StorySegment
        public StorySegment(List<StoryNode> starting_sequence, int start_turn_in)
        {
            sequence = starting_sequence;
            starting_turn = start_turn_in;
        }//end constructor StorySegment

        public void AddStoryNode(StoryNode to_add)
        {
            sequence.Add(to_add);
        }//end method AddStoryNode

        public List<StoryNode> GetSequence()
        {
            return sequence;
        }//end method GetSequence
        public List<int> GetSequenceIds()
        {
            List<int> return_list = new List<int>();

            foreach (StoryNode temp_node in sequence)
            {
                return_list.Add(temp_node.graph_node_id);
            }//end foreach

            return return_list;
        }//end method GetSequence

        public StoryNode GetNodeAtTurn(int turn)
        {
            if (turn < starting_turn || turn > starting_turn + length)
                return null;
            else
                return sequence[turn - starting_turn];
        }//end method GetNodeAtTurn

        public List<StoryNode> Sequence
        {
            get
            {
                return this.sequence;
            }
        }
        public int length
        {
            get
            {
                return this.sequence.Count;
            }//end get
        }
    }
}
