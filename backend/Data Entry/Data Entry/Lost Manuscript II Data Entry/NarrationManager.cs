using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIMLbot;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dialogue_Data_Entry
{
	//Narration Manager manages the flow of narration.
	//It keeps all memory of the conversation, such as what topics have been visited. 
	//It also has functions that provide what to say under different circumstances.
	class NarrationManager
	{
		private FeatureGraph feature_graph;     //The data structure holding every feature in the knowledge base.
		private Bot aiml_bot;                   //The AIML bot being used to help answer queries.
		private User user;                      //A user to make requests of the AIML bot.
		private Feature topic;                  //The current topic of conversation.

        //Anchor Node variables
        public List<Feature> anchor_nodes;      //The list of anchor nodes that must be visited.
        private List<Feature> anchor_nodes_visited; //The list of anchor nodes that have already been visited.
        private Feature current_anchor_node;    //The anchor node that was most recently talked about.
        private int current_anchor_turn_count;  //How many turns we have been talking about the current anchor node.
        private double current_transition_score;
        private int turns_per_anchor;           //The maximum number of turns we can talk about a single anchor node.

        // Target node variables
        private int current_target_id;
        private List<int> target_node_ids;
        private List<Tuple<int, string, int>> target_node_constraints;

        private int turn_limit;                 //The maximum number of turns this narration is allowed to go for.
                                                //If maximum_turn_count <= 0, then assume no turn limit.

		private int turn;                       //A count of what turn of the conversation we are on.
		private List<Feature> topic_history;    //The history of topics in this conversation. Last item is always the topic.

        private bool narrating;                 //Whether or not the narration manager is in the middle of an ongoing narration.

		private List<TemporalConstraint> temporal_constraint_list;  //The list for temporal constraint checking. Does not change after init.

		//An buffer of things to say. Can be pulled from for outputs, and is reset
		//whenever the topic changes.
		private string[] _buffer;
		private string[] buffer { get { return _buffer; } set { _buffer = value; b = 0; } }
		private int b;  // buffer index. Gets reset when buffer does.

		public NarrationCalculator calculator;  //The calculator, for score/novelty values and constraints

		private const string IDK = "I'm afraid I don't know anything about that topic." + "##" + "对不起，我不知道。" + "##";

		public NarrationManager(FeatureGraph fg, List<TemporalConstraint> tcl)
		{
			feature_graph = fg;

			//Initialize the AIML chat bot
			this.aiml_bot = new Bot();
			aiml_bot.loadSettings();
			aiml_bot.isAcceptingUserInput = false;
			aiml_bot.loadAIMLFromFiles();
			aiml_bot.isAcceptingUserInput = true;
			this.user = new User("user", this.aiml_bot);

			//Default initializations
			topic = null;
			turn = 1;
			topic_history = new List<Feature>();
			temporal_constraint_list = tcl;

            //Anchor node initializations
            anchor_nodes = new List<Feature>();
            anchor_nodes_visited = new List<Feature>();
            current_anchor_node = null;
            current_anchor_turn_count = 0;
            turns_per_anchor = 0;
            turn_limit = 0;

            narrating = false;

			calculator = new NarrationCalculator(feature_graph, tcl);

			//The first topic should be the root node of the feature graph
			if (this.topic == null)
				SetNextTopic(this.feature_graph.Root);
		}//end method DialogueManager
        public NarrationManager(FeatureGraph fg, List<int> target_id_list, List<Tuple<int, string, int>> target_constraint_list)
        {
            feature_graph = fg;

            //Initialize the AIML chat bot
            this.aiml_bot = new Bot();
            aiml_bot.loadSettings();
            aiml_bot.isAcceptingUserInput = false;
            aiml_bot.loadAIMLFromFiles();
            aiml_bot.isAcceptingUserInput = true;
            this.user = new User("user", this.aiml_bot);

            //Default initializations
            topic = null;
            turn = 1;
            topic_history = new List<Feature>();
            temporal_constraint_list = new List<TemporalConstraint>();

            //Anchor node initializations
            anchor_nodes = new List<Feature>();
            anchor_nodes_visited = new List<Feature>();
            current_anchor_node = null;
            current_anchor_turn_count = 0;
            turns_per_anchor = 0;
            turn_limit = 0;

            narrating = false;

            target_node_ids = target_id_list;
            target_node_constraints = target_constraint_list;

            calculator = new NarrationCalculator(feature_graph, temporal_constraint_list);

            // The default first node is the feature graph's root node.
            int first_node_id = feature_graph.Root.Id;
            // The first topic should be a node from the target list.
            foreach (int node_id in target_node_ids)
            {
                // Check if this node should be after any other node. If so, it cannot be the first node.
                bool clear_for_first = true;
                foreach (Tuple<int, string, int> constraint in target_node_constraints)
                {
                    int source = constraint.Item1;
                    string op = constraint.Item2;
                    int target = constraint.Item3;
                    if (node_id == source)
                    {
                        // Check for the greater-than operator.
                        if (op.Equals(">"))
                        {
                            clear_for_first = false;
                            break;
                        }//end if
                    }//end if
                    else if (node_id == target)
                    {
                        //Check for the less-than operator.
                        if (op.Equals("<"))
                        {
                            clear_for_first = false;
                            break;
                        }//end if
                    }//end if
                }//end foreach
                if (clear_for_first)
                {
                    first_node_id = node_id;
                    break;
                }//end if
            }//end for
            current_target_id = first_node_id;
            SetNextTopic(this.feature_graph.getFeature(first_node_id));

        }//end method DialogueManager

		//ACCESSIBLE FUNCTIONS 
        //Present the given feature
        public string PresentFeature(Feature feature_to_present)
        {
            Feature nextTopic = this.topic;
            string[] newBuffer;
            string return_string = "";

            //The next topic is given.
            nextTopic = feature_to_present;
            //Gets a list of things to say about the topic (speak values or neighbor relations)
            newBuffer = FindStuffToSay(nextTopic);

            //Set the next topic and reset the buffer to the list
            //of output strings from function FindStuffToSay
            SetNextTopic(nextTopic, newBuffer);

            //Get an output string from the buffer.
            return_string = PullOutputFromBuffer();

            //Adorn the answer, which sends it through speak transforms.
            return_string = SpeakWithAdornments(this.topic, return_string);

            //Update turn count
            turn += 1;
            
            return return_string;
        }//end function DefaultNextTopic

        private Story AddNodeToStory(Feature graph_node, Story story)
        {
            //These are for the manager, but each story has its own history and turn count.
            SetNextTopic(graph_node);
            turn += 1;

            StoryNode new_story_node = new StoryNode(graph_node.Id);

            //Transform the story node using the story passed in.
            //Check for relationship between this node and the previous node.
            //If this is the first node and there are no previous nodes, skip this step.
            Feature previous_node = null;
            if (story.StorySequence.Count > 0)
            {
                previous_node = feature_graph.getFeature(story.GetLastNode().graph_node_id);

                bool introduction_handled = false;
                //Decide whether to state the relationship between this node and the previous one.
                if (graph_node.getNeighbor(previous_node.Id) != null)
                {
                    new_story_node.story_acts.Add(new Tuple<string, int>(Constant.RELATIONSHIP, previous_node.Id));
                    introduction_handled = true;
                }//end if

                //Decide whether or not this node is placed in a new geographic location.
                if (graph_node.Geodata.Count > 0)
                {
                    Tuple<double, double, int> last_location_info = story.GetLastLocation(feature_graph);
                    if (last_location_info != null)
                    {
                        double geo_threshold = 0.0;
                        if (Math.Abs(graph_node.Geodata[0].Item1 - last_location_info.Item1) + Math.Abs(graph_node.Geodata[0].Item2 - last_location_info.Item2) > geo_threshold)
                        {
                            new_story_node.story_acts.Add(new Tuple<string, int>(Constant.LOCATIONCHANGE, last_location_info.Item3));
                            introduction_handled = true;
                        }//end if
                    }//end if
                }//end if

                //If there is nothing introducing this node, add a generic lead-in
                if (!introduction_handled)
                {
                    new_story_node.story_acts.Add(new Tuple<string, int>(Constant.LEADIN, graph_node.Id));
                }//end else

            }//end if

            story.AddStoryNode(new_story_node);

            return story;
        }//end method AddNodeToStory

        public Story MakeStoryFromList(List<Feature> story_features)
        {
            Story return_story = new Story();

            foreach (Feature to_add in story_features)
            {
                AddNodeToStory(to_add, return_story);
            }//end foreach

            return_story = GenerateStoryText(return_story);

            return return_story;
        }//end method MakeStoryFromList

        //Generate a single segment of the story starting from the an anchor node.
        //If there is a starting story, this arc will be the next segment of the story.
        public Story GenerateStorySegment(Feature anchor_node, int turn_limit, Story starting_story = null)
        {
            return null;
        }//end method GenerateStory

        public Story GenerateChronology(Feature anchor_node, int turn_limit, Story starting_story = null, bool user_story = false)
        {
            Story chronology = new Story();
            /*if (starting_story != null)
            {
                chronology = starting_story;
            }//end if*/

            //1. Create the entire story as one segment.
            //Add the anchor node to the story.
            AddNodeToStory(anchor_node, chronology);
            //Add the closest start neighbor to the story.
            //AddNodeToStory(closest_start_neighbor, chronology);

            Feature current_feature = null;
            int local_turn = 0;
            Feature last_node = anchor_node;
            while (local_turn < turn_limit - 1)
            {
                //Find the next best topic for the chronology.
                current_feature = getNextChronologicalTopic(last_node, anchor_node.start_date, anchor_node.end_date);
                //Add it to the story.
                if (current_feature != null)
                {
                    AddNodeToStory(current_feature, chronology);
                    last_node = current_feature;
                }//end if
                local_turn += 1;
            }//end while

            if (!user_story)
            {
                //2. Identify the switch point
                //TODO: Hack for demo, change back to the commented out line later!
                int switch_point_turn = 2; // calculator.IdentifySwitchPointTurn(chronology);

                //3. Split the story at the switch point
                chronology.SplitSegment(switch_point_turn);

                //4. Place a user turn and switch-point narrative event at the last node of the first segment.
                chronology.GetNodeAtTurn(switch_point_turn).AddStoryAct(Constant.SWITCHPOINT
                    , chronology.GetNodeAtTurn(switch_point_turn).graph_node_id);
                chronology.GetNodeAtTurn(switch_point_turn).AddStoryAct(Constant.USERTURN
                    , chronology.GetNodeAtTurn(switch_point_turn).graph_node_id);
            }//end if
            else if (user_story)
            {
                foreach (StoryNode temp_node in chronology.GetNodeSequence())
                {
                    Feature temp_feat = feature_graph.getFeature(temp_node.graph_node_id);
                    foreach (StoryNode prev_node in starting_story.StorySequence[0].GetSequence())
                    {
                        Feature prev_feat = feature_graph.getFeature(prev_node.graph_node_id);
                        if (temp_feat.getNeighbor(prev_feat.Id) != null)
                        {
                            //If the node in the secondary storyline is related to a node in the
                            //first half of the primary storyline, make a tie back.
                            temp_node.AddStoryAct(Constant.TIEBACK, prev_feat.Id);
                            //Add only the first one of these instances per secondary story node.
                            break;
                        }//end if
                    }//end foreach
                }//end foreach
                chronology.AppendStorySegment(starting_story.GetLastSegment());
            }//end else if

            //5. Go through each node and generate text.
            chronology = GenerateStoryText(chronology);

            return chronology;
        }//end method GenerateChronology

        // Generate a story using the target nodes.
        public Story GenerateTargetStory(int turn_limit, Story starting_story = null, bool user_story = false)
        {
            Story chronology = new Story();
            /*if (starting_story != null)
            {
                chronology = starting_story;
            }//end if*/

            //1. Create the entire story as one segment.
            //Add the anchor node to the story.
            Feature current_target = feature_graph.getFeature(current_target_id);
            AddNodeToStory(current_target, chronology);
            Feature last_node = current_target;

            // Change the current target
            current_target_id = DetermineNextTarget(chronology, current_target);
            if (feature_graph.hasNode(current_target_id))
                current_target = feature_graph.getFeature(current_target_id);
            else
                current_target = null;

            Feature current_feature = null;
            int local_turn = 0;
            while (local_turn < turn_limit - 1)
            {
                //Find the next best topic for the chronology.
                current_feature = getNextTargetTopic(last_node, current_target, target_node_ids);
                //Add it to the story.
                if (current_feature != null)
                {
                    AddNodeToStory(current_feature, chronology);
                    last_node = current_feature;
                    // Was this node the next target?
                    if (current_feature.Id == current_target_id)
                    {
                        // Change the current target
                        current_target_id = DetermineNextTarget(chronology, current_target);
                        current_target = feature_graph.getFeature(current_target_id);
                    }//end if
                }//end if
                local_turn += 1;
            }//end while

            if (!user_story)
            {
                //2. Identify the switch point
                //TODO: Hack for demo, change back to the commented out line later!
                int switch_point_turn = 2; // calculator.IdentifySwitchPointTurn(chronology);

                //3. Split the story at the switch point
                chronology.SplitSegment(switch_point_turn);

                //4. Place a user turn and switch-point narrative event at the last node of the first segment.
                chronology.GetNodeAtTurn(switch_point_turn).AddStoryAct(Constant.SWITCHPOINT
                    , chronology.GetNodeAtTurn(switch_point_turn).graph_node_id);
                chronology.GetNodeAtTurn(switch_point_turn).AddStoryAct(Constant.USERTURN
                    , chronology.GetNodeAtTurn(switch_point_turn).graph_node_id);
            }//end if
            else if (user_story)
            {
                foreach (StoryNode temp_node in chronology.GetNodeSequence())
                {
                    Feature temp_feat = feature_graph.getFeature(temp_node.graph_node_id);
                    foreach (StoryNode prev_node in starting_story.StorySequence[0].GetSequence())
                    {
                        Feature prev_feat = feature_graph.getFeature(prev_node.graph_node_id);
                        if (temp_feat.getNeighbor(prev_feat.Id) != null)
                        {
                            //If the node in the secondary storyline is related to a node in the
                            //first half of the primary storyline, make a tie back.
                            temp_node.AddStoryAct(Constant.TIEBACK, prev_feat.Id);
                            //Add only the first one of these instances per secondary story node.
                            break;
                        }//end if
                    }//end foreach
                }//end foreach
                chronology.AppendStorySegment(starting_story.GetLastSegment());
            }//end else if

            //5. Go through each node and generate text.
            chronology = GenerateStoryText(chronology);

            return chronology;
        }//end method GenerateTargetStory

        // Determine the next target based on the story and the previous target.
        private int DetermineNextTarget(Story current_story, Feature previous_target)
        {
            int next_target_id = -1;
            bool next_target_found = false;

            // First, start at the previous target. Check for any hard constraints.
            foreach (Tuple<int, string, int> constraint in previous_target.constraints)
            {
                // If the previous target is the source and it is a hard constraint, follow it.
                if (constraint.Item1 == previous_target.Id && constraint.Item2.Equals("=>"))
                {
                    next_target_id = constraint.Item3;
                    next_target_found = true;
                }//end if
            }//end foreach

            // Look for a target node whose constraints are met and which hasn't been already mentioned.
            if (!next_target_found)
            {
                foreach (int node_id in target_node_ids)
                {
                    // Check that the node does not appear in the story already.
                    bool constraints_satisfied = true;
                    if (current_story.HasNode(node_id))
                    {
                        constraints_satisfied = false;
                        continue;
                    }//end if
                    if (!current_story.HasNode(node_id))
                    {

                        // Go through its constraints and make sure they are satisfied.
                        foreach (Tuple<int, string, int> constraint in feature_graph.getFeature(node_id).constraints)
                        {
                            int source_id = constraint.Item1;
                            string op = constraint.Item2;
                            int target_id = constraint.Item3;

                            // For the less-than operator, if this node is the source then the target must
                            // not appear in the story. If this node is the target then the source must
                            // appear in the story.
                            if (op.Equals("<"))
                            {
                                if (source_id == node_id)
                                {
                                    if (current_story.HasNode(target_id))
                                    {
                                        constraints_satisfied = false;
                                        continue;
                                    }//end if
                                }//end if
                                else if (target_id == node_id)
                                {
                                    if (!current_story.HasNode(source_id))
                                    {
                                        constraints_satisfied = false;
                                        continue;
                                    }//end if
                                }//end else if
                            }//end if
                        }//end foreach
                    }//end if

                    if (constraints_satisfied)
                    {
                        next_target_id = node_id;
                        next_target_found = true;
                    }//end if

                }//end foreach
            }//end if

            return next_target_id;
        }//end method DetermineNextTarget

        private Story GenerateStoryText(Story story_in)
        {
            Feature graph_node = null;
            Feature target_node = null;
            string node_text = "";
            foreach (StorySegment temp_segment in story_in.StorySequence)
            {
                foreach (StoryNode temp_node in temp_segment.Sequence)
                {
                    graph_node = feature_graph.getFeature(temp_node.graph_node_id);
                    node_text = graph_node.getSpeak(0);
                    foreach (Tuple<string, int> story_act in temp_node.story_acts)
                    {
                        target_node = feature_graph.getFeature(story_act.Item2);
                        if (story_act.Item1.Equals(Constant.LEADIN))
                        {
                            //node_text = LeadIn(graph_node) + node_text;
                        }//end if
                        else if (story_act.Item1.Equals(Constant.RELATIONSHIP))
                        {
                            node_text = "<color=#0000ffff>" + Relationship(graph_node, target_node) + "</color>" + node_text;
                        }//end else if
                        else if (story_act.Item1.Equals(Constant.SWITCHPOINT))
                        {
                            //Get both halves of the story.
                            StorySegment first_half = temp_segment;
                            int asdasd = story_in.StorySequence.IndexOf(first_half);
                            StorySegment second_half = story_in.StorySequence[story_in.StorySequence.IndexOf(first_half) + 1];

                            //Build a list of tuples identifying nodes in the first half that relate to nodes in the second half.
                            List<Tuple<Feature, Feature>> hint_ats = new List<Tuple<Feature, Feature>>();
                            foreach (StoryNode first_half_node in first_half.Sequence)
                            {
                                Feature f_feature = feature_graph.getFeature(first_half_node.graph_node_id);
                                foreach (StoryNode second_half_node in second_half.Sequence)
                                {
                                    Feature s_feature = feature_graph.getFeature(second_half_node.graph_node_id);
                                    if ((!f_feature.getRelationshipNeighbor(s_feature.Id).Equals("")
                                        && !(f_feature.getRelationshipNeighbor(s_feature.Id) == null))
                                        || (!s_feature.getRelationshipNeighbor(f_feature.Id).Equals("")
                                        && !(s_feature.getRelationshipNeighbor(f_feature.Id) == null)))
                                    {
                                        hint_ats.Add(new Tuple<Feature, Feature>(f_feature, s_feature));
                                    }//end if
                                }//end foreach
                            }//end foreach

                            int max_hit_ats = 4;
                            node_text = node_text + "<color=#800080ff> We'll hear more about";
                            List<string> hint_at_text = new List<string>();
                            //Pick node pairs to hint at until we run out or we reach the maximum.
                            for (int i = 0; i < Math.Min(max_hit_ats, hint_ats.Count); i++)
                            {
                                Feature f_feature = hint_ats[hint_ats.Count - 1 - i].Item1;
                                Feature s_feature = hint_ats[hint_ats.Count - 1 - i].Item2;
                                //Make the hint
                                string rel = "";
                                if (!f_feature.getRelationshipNeighbor(s_feature.Id).Equals("")
                                        && !(f_feature.getRelationshipNeighbor(s_feature.Id) == null))
                                {
                                    rel = f_feature.getRelationshipNeighbor(s_feature.Id);
                                }//end if
                                else if (!s_feature.getRelationshipNeighbor(f_feature.Id).Equals("")
                                        && !(s_feature.getRelationshipNeighbor(f_feature.Id) == null))
                                {
                                    rel = s_feature.getRelationshipNeighbor(f_feature.Id);
                                }//end else if
                                hint_at_text.Add(f_feature.Name + " " + rel);
                                //node_text = node_text + ", " + f_feature.Name + " " + rel;

                                //Make sure the node in the second half of the storyline resolves back to the node
                                //in the first half of the storyline.
                                story_in.GetNodeByGraphId(s_feature.Id).AddStoryAct(Constant.RESOLVE, f_feature.Id);
                            }//end for
                            for (int i = 0; i < hint_at_text.Count; i++)
                            {
                                if (i == hint_at_text.Count - 1
                                    && hint_at_text.Count > 1)
                                {
                                    node_text = node_text + ", and " + hint_at_text[i];
                                }//end if
                                else if (i == 0)
                                {
                                    node_text = node_text + " " + hint_at_text[i];
                                }//end else if
                                else
                                {
                                    node_text = node_text + ", " + hint_at_text[i];
                                }//end else
                            }//end for
                            for (int i = 0; i < hint_at_text.Count; i++)
                            {
                                if (i == hint_at_text.Count - 1
                                    && hint_at_text.Count > 1)
                                {
                                    node_text = node_text + ", and " + hint_at_text[i];
                                }//end if
                                else if (i == 0)
                                {
                                    node_text = node_text + " " + hint_at_text[i];
                                }//end else if
                                else
                                {
                                    node_text = node_text + ", " + hint_at_text[i];
                                }//end else
                            }//end for
                            node_text = node_text + " later. But for now, let's talk about something else.</color> ";
                        }//end else if
                        else if (story_act.Item1.Equals(Constant.USERTURN))
                        {
                            node_text = node_text + "What would you like to hear about? ";
                        }//end else if
                        else if (story_act.Item1.Equals(Constant.RESOLVE))
                        {
                            node_text = node_text + "<color=#ff00ffff>" + Resolve(graph_node, target_node) + "</color>";
                        }//end else if
                        else if (story_act.Item1.Equals(Constant.TIEBACK))
                        {
                            node_text = node_text + "<color=#b8860bff>" + TieBack(graph_node, target_node) + "</color>";
                        }//end else if
                    }//end foreach
                    temp_node.text = node_text;
                }//end foreach

            }//end foreach

            return story_in;
        }//end method GenerateNodeText

        private string TieBack(Feature current_feature, Feature past_feature)
        {
            string return_string = "";
            // Check both directions for a non-blank relationship to use
            if (!current_feature.getRelationshipNeighbor(past_feature.Id).Equals("")
                && !(current_feature.getRelationshipNeighbor(past_feature.Id) == null))
            {
                return_string = " Do you remember " + past_feature.Name + "? Well, " + current_feature.Name + " " + current_feature.getRelationshipNeighbor(past_feature.Id) + " "
                    + past_feature.Name + ". ";
            }//end if
            else if (!past_feature.getRelationshipNeighbor(current_feature.Id).Equals("")
                && !(past_feature.getRelationshipNeighbor(current_feature.Id) == null))
            {
                return_string = " And do you remember " + past_feature.Name + "? Well, " + past_feature.Name + " " + past_feature.getRelationshipNeighbor(current_feature.Id) + " "
                    + current_feature.Name + ". ";
            }//end if

            return return_string;
        }//end method TieBack

        private string Resolve(Feature current_feature, Feature past_feature)
        {
            string return_string = "";

            if (!current_feature.getRelationshipNeighbor(past_feature.Id).Equals("")
                 && !(current_feature.getRelationshipNeighbor(past_feature.Id) == null))
            {
                return_string = " And as it turns out, " + current_feature.Name + " " + current_feature.getRelationshipNeighbor(past_feature.Id) + " "
                    + past_feature.Name + ". ";
            }//end if
            else if (!past_feature.getRelationshipNeighbor(current_feature.Id).Equals("")
                && !(past_feature.getRelationshipNeighbor(current_feature.Id) == null))
            {
                return_string = " If you recall " + past_feature.Name + ", it turns out that " + past_feature.Name + " " + past_feature.getRelationshipNeighbor(current_feature.Id) + " "
                    + current_feature.Name + ". ";
            }//end if

            return return_string;
        }//end method Resolve

        private string LeadIn(Feature node)
        {
            string return_string = "";

            string node_name = node.Name;

            //A set of lead-in statements for non-novel nodes
            List<string> non_novel_lead_in_statements = new List<string>();
            non_novel_lead_in_statements.Add("{Have you heard of " + node_name + "?} ");
            non_novel_lead_in_statements.Add("{Let's talk about " + node_name + ".} ");
            non_novel_lead_in_statements.Add("{I'll mention " + node_name + " real quick.} ");
            non_novel_lead_in_statements.Add("{So, about " + node_name + ".} ");
            non_novel_lead_in_statements.Add("{Now then, about " + node_name + ".} ");
            non_novel_lead_in_statements.Add("{Let's talk about " + node_name + " for a moment.} ");
            non_novel_lead_in_statements.Add("{Have I mentioned " + node_name + "?} ");
            non_novel_lead_in_statements.Add("{Now, about " + node_name + ".} ");
            non_novel_lead_in_statements.Add("{Now, let's talk about " + node_name + ".} ");
            non_novel_lead_in_statements.Add("{I should touch on " + node_name + ".} ");

            //Randomly choose a lead-in statement for the given node.
            Random rand = new Random();
            return_string = non_novel_lead_in_statements[rand.Next(non_novel_lead_in_statements.Count)];

            return return_string;
        }//end method LeadIn
        private string Relationship(Feature current_node, Feature target_node)
        {
            string relationship_statement = "";

            if (current_node.getRelationshipNeighbor(target_node.Id) != null
                && current_node.getRelationshipNeighbor(target_node.Id) != "")
            {
                relationship_statement = current_node.Name + " " + current_node.getRelationshipNeighbor(target_node.Id)
                    + " " + target_node.Name + ". ";
            }//end if
            else if (target_node.getRelationshipNeighbor(current_node.Id) != null
                && target_node.getRelationshipNeighbor(current_node.Id) != "")
            {
                relationship_statement = target_node.Name + " " + target_node.getRelationshipNeighbor(current_node.Id)
                    + " " + current_node.Name + ". ";
            }//end else if

            return relationship_statement;
        }//end method Relationship


        public Feature getNextChronologicalTopic(Story story_in, DateTime start_date, DateTime end_date)
        {
            //
            List<Feature> history_list = new List<Feature>();
            foreach (int graph_node_id in story_in.GetHistory())
            {
                history_list.Add(feature_graph.getFeature(graph_node_id));
            }//end foreach

            //Gets the next topic that should be visited whose date lies between the given start and end dates.
            return calculator.GetNextTopic(feature_graph.getFeature(story_in.last_anchor_id), story_in.current_turn, history_list, start_date, end_date);
        }//end method getNextChronologicalTopic
        public Feature getNextChronologicalTopic(Feature previous_topic, DateTime start_date, DateTime end_date)
        {
            //Gets the next topic that should be visited whose date lies between the given start and end dates.
            return calculator.GetNextTopic(previous_topic, this.turn, this.topic_history, start_date, end_date);
        }//end method getNextChronologicalTopic

        // Get the next best topic while generating a target-constrained story.
        public Feature getNextTargetTopic(Feature previous_topic, Feature current_target, List<int> target_ids)
        {
            return calculator.GetNextTopicWithTarget(previous_topic, this.turn, this.topic_history, current_target, target_ids);
        }//end method getNextChronologicalTopic

        public Feature getNextBestStoryTopic(Story story_in)
        {
            Feature last_topic = feature_graph.getFeature(story_in.GetLastNode().graph_node_id);
            List<Feature> history_list = new List<Feature>();
            foreach (int graph_node_id in story_in.GetHistory())
            {
                history_list.Add(feature_graph.getFeature(graph_node_id));
            }//end foreach

            return calculator.GetNextTopic(last_topic, "", story_in.current_turn, history_list);
        }//end method getNextBestStoryTopic

        /// <summary>
        /// Begins a narration.
        /// The goal of a narration is to visit every anchor node within the set turn limit.
        /// Returns whether narration was successfully started or not.
        /// </summary>
        public bool StartNarration()
        {
            //Check that there are anchor nodes to visit
            //Check that there is a turn limit
            if (anchor_nodes.Count < 1 || turn_limit <= 0)
                return false;
            else
            {
                //Start narration
                narrating = true;

                //Clear history and reset turn count
                topic_history = new List<Feature>();
                anchor_nodes_visited = new List<Feature>();
                turn = 0;

                //Pick the first anchor node.
                //For now, just use the first in the anchor node list.
                current_anchor_node = anchor_nodes[0];

                //Based on the number of anchor nodes and the turn limit, set the number of
                //turns we can talk about each anchor node.
                //Split turns evenly amongst each anchor node.
                double turns_per_anchor_decimal = (double)(turn_limit - turn) / (double)anchor_nodes.Count;
                turns_per_anchor = (int)Math.Floor(turns_per_anchor_decimal);


                //Initialize turn counts
                //We start on turn 1.
                turn = 1;
                current_anchor_turn_count = 1;

                return true;
            }//end else
        }//end method StartNarration
        public string Narrate()
        {
            string return_string = "";

            //Do not narrate if we are above the turn limit or if we're out of anchor points
            if ((turn >= turn_limit) || (anchor_nodes.Count <= 0))
            {
                return "End narration. Turn: " + turn;
            }//end if

            //Generate a sequence from the current anchor node.
            int sequence_1_start_turn = turn;
            List<Feature> sequence_1 = GenerateSequence(current_anchor_node);
            //How the last node of this sequence and the first node of the next one
            //scores according to the narration calculator
            double sequence_1_transition_score = current_transition_score;
            int sequence_1_end_turn = turn;
            /*//Check the turn count against the turns per anchor.
            if (current_anchor_turn_count <= turns_per_anchor)
            {
                //If the sequence ended with fewer turns than alotted, then the
                //sequence ran into another anchor node and stopped early.
            }//end if
             * */

            //Generate another sequence from the next anchor node identified by the last sequence.
            int sequence_2_start_turn = turn;
            List<Feature> sequence_2 = GenerateSequence(current_anchor_node);
            double sequence_2_transition_score = current_transition_score;
            int sequence_2_end_turn = turn;

            //Decide whether or not to interweave the two sequences.
            //Decide based on the transition score from the first sequence to the second
            double transition_threshold = 0;
            //If the score is worst than the threshold, try to interweave.
            if (sequence_1_transition_score < transition_threshold)
            {
                //Identify the best switchpoint for the first sequence
                Feature switch_point = IdentifySwitchPoint(sequence_1);
                //Split the first sequence at the switch point.
                List<Feature> first_part_sequence_1 = new List<Feature>();
                List<Feature> second_part_sequence_1 = new List<Feature>();
                foreach (Feature sequence_node in sequence_1)
                {
                    //Past the switch point, place the feature in the second part.
                    if (sequence_1.IndexOf(sequence_node) > sequence_1.IndexOf(switch_point))
                        second_part_sequence_1.Add(sequence_node);
                    else
                        first_part_sequence_1.Add(sequence_node);
                }//end foreach

                //The history list up until we created either sequence.
                List<Feature> sequence_history = topic_history.GetRange(0, sequence_1_start_turn - 1);
                //Present the first part of the first sequence
                foreach (Feature sequence_node in first_part_sequence_1)
                {
                    //Reset the buffer with output for this feature
                    string[] new_buffer;
                    new_buffer = FindStuffToSay(sequence_node);
                    this.buffer = new_buffer;

                    string to_add = PullOutputFromBuffer();
                    to_add = SpeakWithAdornments(sequence_node, to_add, sequence_history);

                    return_string += " " + to_add + "\n";

                    //If this is the first node in the sequence, then it is the anchor node.
                    //Try to relate it to the anchor node presented before this one.
                    /*if (sequence_node.Equals(first_part_sequence_1[0]))
                    {
                        return_string += RelateAnchorNodeToPrevious(sequence_node);
                    }//end if*/

                    //If this is an anchor node, try to relate it to the previous one.
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);

                    //If this is the switch point, additionally foreshadow the second half of the first sequence.
                    if (sequence_node.Id == switch_point.Id)
                    {
                        foreach (Feature first_part_node in first_part_sequence_1)
                        {
                            return_string += " " + Foreshadow(first_part_node, second_part_sequence_1) + "\n";
                        }//end foreach
                    }//end if

                    //Add the node to the sequence history
                    sequence_history.Add(sequence_node);
                }//end foreach
                //Present the second sequence
                foreach (Feature sequence_node in sequence_2)
                {
                    //Reset the buffer with output for this feature
                    string[] new_buffer;
                    new_buffer = FindStuffToSay(sequence_node);
                    this.buffer = new_buffer;

                    string to_add = PullOutputFromBuffer();
                    to_add = SpeakWithAdornmentsReference(sequence_node, to_add, first_part_sequence_1, sequence_history);

                    //If this is the first node of sequence 2, then add a storyline transition phrase to the beginning.
                    if (sequence_node.Equals(sequence_2[0]))
                    {
                        to_add = "{But now, let's talk about something else.} " + to_add;
                    }//end if

                    //If this is an anchor node, try to relate it to the previous one.
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);

                    return_string += " " + to_add + "\n";

                    //Add the node to the sequence history
                    sequence_history.Add(sequence_node);
                }//end foreach
                //Present the second part of the first sequence
                foreach (Feature sequence_node in second_part_sequence_1)
                {
                    //Reset the buffer with output for this feature
                    string[] new_buffer;
                    new_buffer = FindStuffToSay(sequence_node);
                    this.buffer = new_buffer;

                    string to_add = PullOutputFromBuffer();
                    to_add = SpeakWithAdornments(sequence_node, to_add, sequence_history);

                    //If this is the first node of the second part of sequence 1, then add a storyline transition phrase.
                    if (sequence_node.Equals(second_part_sequence_1[0]))
                    {
                        to_add = "{Let's get back to what we were discussing before with " + sequence_1[0].Name 
                            + ". When we left off, we were talking about " + switch_point.Name + ".}"
                            + to_add;
                    }//end if

                    //If this is an anchor node, try to relate it to the previous one.
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);

                    return_string += " " + to_add + "\n";
                    //Try to tie back to the first part of the first sequence
                    return_string += " " + TieBack(sequence_node, first_part_sequence_1, sequence_history[sequence_history.Count - 1]) + "\n";

                    //Add the node to the sequence history
                    sequence_history.Add(sequence_node);
                }//end foreach
            }//end if
            //If not interweaving, present each story one after the other.
            else
            {
                //The history list up until we created either sequence.
                List<Feature> sequence_history = topic_history.GetRange(0, sequence_1_start_turn - 1);
                //Present the first sequence
                foreach (Feature sequence_node in sequence_1)
                {
                    //Reset the buffer with output for this feature
                    string[] new_buffer;
                    new_buffer = FindStuffToSay(sequence_node);
                    this.buffer = new_buffer;

                    string to_add = PullOutputFromBuffer();
                    to_add = SpeakWithAdornments(sequence_node, to_add, sequence_history);

                    /*//Check if this is the anchor node
                    if (sequence_node.Equals(sequence_1[0]))
                    {
                        //Try to relate it to the previous anchor node
                        to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);
                    }//end if*/

                    //If this is an anchor node, try to relate it to the previous one.
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);

                    return_string += " " + to_add + "\n";

                    //Add the node to the sequence history
                    sequence_history.Add(sequence_node);
                }//end foreach

                //Present the second sequence
                foreach (Feature sequence_node in sequence_2)
                {
                    //Reset the buffer with output for this feature
                    string[] new_buffer;
                    new_buffer = FindStuffToSay(sequence_node);
                    this.buffer = new_buffer;

                    string to_add = PullOutputFromBuffer();
                    to_add = SpeakWithAdornments(sequence_node, to_add, sequence_history);

                    //Check if this is the anchor node
                    /*if (sequence_node.Equals(sequence_2[0]))
                    {
                        //Try to relate it to the previous anchor node
                        to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);
                    }//end if*/

                    //If this is an anchor node, try to relate it to the previous one.
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);

                    return_string += " " + to_add + "\n";

                    //Add the node to the sequence history
                    sequence_history.Add(sequence_node);
                }//end foreach
            }//end else

            /*
            //At each step of narration, look at the previous steps, decide which topic
            //to talk about next, then present that topic. Then, update history and check turn limits.
            Feature next_topic = null;
            string[] buffer;

            //Check the current anchor node. If we have not talked about it yet, it is the next topic.
            if (!anchor_nodes_visited.Contains(current_anchor_node))
            {
                next_topic = current_anchor_node;
            }//end if
            */

            return return_string;
        }//end method Narrate

        //Try to relate the given anchor node to the anchor node visited before it.
        //If the given node is not an anchor node, will return the empty string.
        private string RelateAnchorNodeToPrevious(Feature anchor_node)
        {
            //If this is not an anchor node, return the empty string.
            if (!anchor_nodes.Contains(anchor_node) && !anchor_nodes_visited.Contains(anchor_node))
                return "";
            //If there are no anchor nodes previously visited, return nothing.
            if (anchor_nodes_visited.Count == 0)
            {
                return "";
            }//end if
            //Check if the anchor node appears in the list of anchor nodes previously visited.
            else if (anchor_nodes_visited.Contains(anchor_node))
            {
                //If so, then relate it to the anchor node before it in the list (if there is one)
                if (anchor_nodes_visited.IndexOf(anchor_node) != 0)
                {
                    return RelateAnchorNodes(anchor_node, anchor_nodes_visited[anchor_nodes_visited.IndexOf(anchor_node) - 1]);
                }//end if
                else
                {
                    return "";
                }//end else
            }//end if
            //If not, then try to relate it to the last item in the list of anchor nodes previously visited
            else
            {
                return RelateAnchorNodes(anchor_node, anchor_nodes_visited[anchor_nodes_visited.Count - 1]);
            }//end else
        }//end method RelateAnchorNodeToPrevious
        //Try to relate the given anchor node to the given previous anchor node.
        private string RelateAnchorNodes(Feature anchor_node, Feature previous_anchor_node)
        {
            string return_string = "";

            //Try to make a remote analogy between one anchor node and the other.
            SpeakTransform temp_transform = new SpeakTransform();
            string analogy = temp_transform.RemoteAnalogy(anchor_node, previous_anchor_node);
            //If the analogy is not blank, relate the anchor nodes with it
            if (!analogy.Equals(""))
            {
                return_string += " We were talking about " + previous_anchor_node.Name
                    + " earlier, and I want to highlight it again. " + analogy + "\n";
            }//end if

            return return_string;
        }//end method PresentAnchorNode

        //Generate a story sequence based on a given anchor node.
        public List<Feature> GenerateSequence(Feature input_anchor_node)
        {
            List<Feature> sequence = new List<Feature>();
            //Re-calculate turns per anchor
            double turns_per_anchor_decimal = (double)(turn_limit - (turn + 1)) / (double)(anchor_nodes.Count);
            turns_per_anchor = (int)Math.Floor(turns_per_anchor_decimal);
            //Look at current anchor node.
            Feature current_topic = input_anchor_node;
            //Add it to the start of the sequence. Also acts as local history.
            sequence.Add(current_topic);
            //Add it as our next topic. Updates graph and global history.
            SetNextTopic(current_topic);

            //Now that it's in a story sequence, remove it from the list of anchor nodes
            //and add it to the list of anchor nodes visited.
            anchor_nodes.Remove(input_anchor_node);
            anchor_nodes_visited.Add(input_anchor_node);

            //We are now on turn 1 for this anchor node.
            current_anchor_turn_count = 1;

            Feature next_topic = null;

            for (current_anchor_turn_count = 1; current_anchor_turn_count < turns_per_anchor; current_anchor_turn_count++)
            {
                //For each turn for this anchor node, use the calculator to find the next topic.
                next_topic = calculator.GetNextTopic(current_topic, "", current_anchor_turn_count, sequence);

                //If the next topic is another anchor node, we should stop the current sequence.
                if (anchor_nodes.Contains(next_topic))
                {
                    //Note the anchor node we found as the current anchor node
                    current_anchor_node = next_topic;
                    //Calculate the score of this transition
                    current_transition_score = calculator.CalculateRelatedness(sequence[sequence.Count - 1], current_anchor_node, turn, topic_history);
                    //Return the current sequence as is.
                    return sequence;
                }//end if

                //Add it to the sequence
                sequence.Add(next_topic);
                SetNextTopic(next_topic);
                //Set it as the current topic
                current_topic = next_topic;
                //Increment total turn count
                turn += 1;
            }//end for

            //The sequence is done. Decide on the next anchor node.
            double highest_score = -double.MaxValue;
            Feature next_best_anchor = null;
            double current_score = 0;
            Feature last_sequence_node = sequence[sequence.Count - 1];
            foreach (Feature potential_next_anchor in anchor_nodes)
            {
                //Find score between last sequence node and this potential next anchor
                current_score = calculator.CalculateRelatedness(last_sequence_node, potential_next_anchor, turn, topic_history);
                if (current_score > highest_score)
                {
                    highest_score = current_score;
                    next_best_anchor = potential_next_anchor;
                }//end if
            }//end for

            //Set the next current anchor node to the one found in the loop above
            current_anchor_node = next_best_anchor;
            current_transition_score = highest_score;
            //Return the sequence
            return sequence;
        }//end method GenerateSequence

        //Generate a story sequence of nodes related to the given anchor
        public List<Feature> GenerateSequenceRelated(Feature input_anchor_node)
        {
            List<Feature> sequence = new List<Feature>();
            //Re-calculate turns per anchor
            double turns_per_anchor_decimal = (double)(turn_limit - (turn + 1)) / (double)(anchor_nodes.Count);
            turns_per_anchor = (int)Math.Floor(turns_per_anchor_decimal);
            //Look at current anchor node.
            Feature current_topic = input_anchor_node;
            //Add it to the start of the sequence. Also acts as local history.
            sequence.Add(current_topic);
            //Add it as our next topic. Updates graph and global history.
            SetNextTopic(current_topic);

            //Now that it's in a story sequence, remove it from the list of anchor nodes
            //and add it to the list of anchor nodes visited.
            anchor_nodes.Remove(input_anchor_node);
            anchor_nodes_visited.Add(input_anchor_node);

            //We are now on turn 1 for this anchor node.
            current_anchor_turn_count = 1;

            Feature next_topic = null;

            for (current_anchor_turn_count = 1; current_anchor_turn_count < turns_per_anchor; current_anchor_turn_count++)
            {
                //For each turn for this anchor node, use the calculator to find the next topic.
                //Use the anchor node as the current topic each time.
                next_topic = calculator.GetNextTopic(input_anchor_node, "", current_anchor_turn_count, sequence);

                //If the next topic is another anchor node, we should stop the current sequence.
                if (anchor_nodes.Contains(next_topic))
                {
                    //Note the anchor node we found as the current anchor node
                    current_anchor_node = next_topic;
                    //Calculate the score of this transition
                    current_transition_score = calculator.CalculateRelatedness(sequence[sequence.Count - 1], current_anchor_node, turn, topic_history);
                    //Return the current sequence as is.
                    return sequence;
                }//end if

                //Add it to the sequence
                sequence.Add(next_topic);
                SetNextTopic(next_topic);
                //Set it as the current topic
                current_topic = next_topic;
                //Increment total turn count
                turn += 1;
            }//end for

            //The sequence is done. Decide on the next anchor node.
            double highest_score = -double.MaxValue;
            Feature next_best_anchor = null;
            double current_score = 0;
            Feature last_sequence_node = sequence[sequence.Count - 1];
            foreach (Feature potential_next_anchor in anchor_nodes)
            {
                //Find score between last sequence node and this potential next anchor
                current_score = calculator.CalculateRelatedness(last_sequence_node, potential_next_anchor, turn, topic_history);
                if (current_score > highest_score)
                {
                    highest_score = current_score;
                    next_best_anchor = potential_next_anchor;
                }//end if
            }//end for

            //Set the next current anchor node to the one found in the loop above
            current_anchor_node = next_best_anchor;
            current_transition_score = highest_score;
            //Return the sequence
            return sequence;
        }//end method GenerateSequenceRelated

        public string NextTopicResponse()
        {
            if (narrating)
                return NextTopicNarration();
            else
                return DefaultNextTopicResponse();
        }//end method NextTopicResponse
        public string NextTopicNarration()
        {
            string return_string = "";

            return_string = Narrate();

            return return_string;
        }//end method NextTopicNarration
		/// <summary>
		/// Decides on the next topic automatically.
		/// Returns something to say about the next topic, with the addition of speak adornments (metaphor, lead-in statements, etc.)
		/// </summary>
		public string DefaultNextTopicResponse()
		{
			Feature nextTopic = this.topic;
			string[] newBuffer;
			string return_string = "";
			// Can't guarantee it'll actually move on to anything...

			//Have the calculator decide on the next topic.
			nextTopic = calculator.GetNextTopic(nextTopic, "", this.turn, this.TopicHistory);
			//Gets a list of things to say about the topic (speak values or neighbor relations)
			newBuffer = FindStuffToSay(nextTopic);

			//Set the next topic and reset the buffer to the list
			//of output strings from function FindStuffToSay
			SetNextTopic(nextTopic, newBuffer);

			//Get an output string from the buffer.
			return_string = PullOutputFromBuffer();

			//Adorn the answer, which sends it through speak transforms.
			//The answer will be used later on in the function.
			return_string = SpeakWithAdornments(this.topic, return_string);

			return return_string;
		}//end function DefaultNextTopic
        //Input a list of nodes that this narration can refer back to, outside
        //of the history.
        public string DefaultNextTopicResponse(List<Feature> reference_nodes)
        {
            Feature nextTopic = this.topic;
            string[] newBuffer;
            string return_string = "";
            // Can't guarantee it'll actually move on to anything...

            //Have the calculator decide on the next topic.
            nextTopic = calculator.GetNextTopic(nextTopic, "", this.turn, this.TopicHistory);
            //Gets a list of things to say about the topic (speak values or neighbor relations)
            newBuffer = FindStuffToSay(nextTopic);

            //Set the next topic and reset the buffer to the list
            //of output strings from function FindStuffToSay
            SetNextTopic(nextTopic, newBuffer);

            //Get an output string from the buffer.
            return_string = PullOutputFromBuffer();

            //Adorn the answer, which sends it through speak transforms.
            //The answer will be used later on in the function.
            return_string = SpeakWithAdornmentsReference(this.topic, return_string, reference_nodes);

            return return_string;
        }//end function DefaultNextTopic

		/// <summary>
		/// Returns the next thing to say about the current topic. Does not change topics,
		/// clear buffer, or update history list, but increments discussed_amount for current topic.
		/// </summary>
		public string TalkMoreAboutTopic()
		{
			string return_string = "";

			//ZEV: Change this in feature graph
			//Increment current topic discuss amount
			this.topic.DiscussedAmount += 1;
			this.feature_graph.setFeatureDiscussedAmount(this.topic.Id, this.topic.DiscussedAmount);

			//Get the next item from the output buffer.
			return_string = PullOutputFromBuffer();

			return return_string;
		}//end method TalkMoreAboutTopic

        public string TalkFromQuery(Query query)
        {
            if (!narrating)
                return DefaultTalkFromQuery(query);
            else if (narrating)
                return TalkFromQueryNarration(query);

            return "";
        }//end method TalkFromQuery

		/// <summary>
		/// Uses the given query to get the next topic. Returns something to say about
		/// the topic in the query.
		/// </summary>
		public string DefaultTalkFromQuery(Query query)
		{
			string return_string = "";

			//From a null query, return an "I don't know" response
			if (query == null)
			{
				return "I'm afraid I don't know the answer to that.";
			}//end if

			bool topic_switch = false;
			Console.WriteLine("Query main topic before: " + query.MainTopic.Id);
			Feature topic_before = query.MainTopic;
			Feature next_topic;
			string[] new_buffer;

			//The buffer should be filled with the output strings from ParseQuery
			new_buffer = ParseQuery(query);

			//The next topic is the topic identified by the query.
			next_topic = query.MainTopic;

			//Detect a topic switch
			if (!next_topic.Equals(topic_before))
			{
				topic_switch = true;
				Console.WriteLine("Query main topic changed to: " + query.MainTopic.Id);
			}//end if

			//Set the next topic and refill the buffer.
			SetNextTopic(next_topic, new_buffer);

			//The return_string, right now, is the result from ParseQuery.

			//If there is a topic change, make sure to introduce the new topic with its speak value.
			if (topic_switch)
			{
				//Get the current topic's speak value and adorn it
				String[] temp_buffer = FindStuffToSay(this.topic);
				String topic_speak = temp_buffer[0];
				//If there is no topic switch, use relationships during adornment. If there is, don't use relationships.
                //Zev: 6/10/16 use_relationships parameter removed
				topic_speak = SpeakWithAdornments(this.topic, topic_speak);

				return_string = return_string + " " + topic_speak;
			}//end if

			//ZEV: Change this in feature graph
			//Increment current topic discuss amount
			this.topic.DiscussedAmount += 1;
			this.feature_graph.setFeatureDiscussedAmount(this.topic.Id, this.topic.DiscussedAmount);

			//Get the next item from the newly reset output buffer.
			return_string = PullOutputFromBuffer();

			return return_string;
		}//end method DefaultTalkFromQuery

        //Talk from a query in the middle of a narration
        private string TalkFromQueryNarration(Query query)
        {
            string return_string = "";

            //From a null query, return an "I don't know" response
            if (query == null)
            {
                return "I'm afraid I don't know the answer to that.";
            }//end if

            bool topic_switch = false;
            Console.WriteLine("Query main topic before: " + query.MainTopic.Id);
            Feature topic_before = query.MainTopic;
            Feature next_topic;
            string[] new_buffer;

            //The buffer should be filled with the output strings from ParseQuery
            new_buffer = ParseQuery(query);

            //The next topic is the topic identified by the query.
            next_topic = query.MainTopic;

            //Add the topic of the query as an anchor node in the front of the list.
            anchor_nodes.Insert(0, next_topic);
            //Using the topic from the query, generate a sequence
            int sequence_start_turn = turn;
            //Generate a sequence related to this main node.
            List<Feature> query_sequence = GenerateSequenceRelated(next_topic);

            //Present the sequence
            //The history list up until we created either sequence.
            List<Feature> sequence_history = topic_history.GetRange(0, sequence_start_turn - 1);
            foreach (Feature sequence_node in query_sequence)
            {
                //Reset the buffer with output for this feature
                new_buffer = FindStuffToSay(sequence_node);
                this.buffer = new_buffer;

                string to_add = PullOutputFromBuffer();
                to_add = SpeakWithAdornments(sequence_node, to_add, sequence_history);

                //Check if this is the anchor node
                if (sequence_node.Equals(query_sequence[0]))
                {
                    //Try to relate it to the previous anchor node
                    to_add = to_add + RelateAnchorNodeToPrevious(sequence_node);
                }//end if

                return_string += " " + to_add + "\n";

                //Add the node to the sequence history
                sequence_history.Add(sequence_node);
            }//end foreach

            return return_string;
        }//end method TalkFromQueryNarration

        /// <summary>
        /// Adds the given feature to the list of Anchor nodes
        /// if it isn't already an anchor node.
        /// </summary>
        public void AddAnchorNode(Feature new_anchor_node)
        {
            //Don't add the feature if it's already an anchor node.
            if (!anchor_nodes.Contains(new_anchor_node))
                anchor_nodes.Add(new_anchor_node);
        }//end method AddAnchorNode
        /// <summary>
        /// Sets the conversation turn limit to the given number.
        /// If set to 0 or less, turn limit is infinite.
        /// </summary>
        public void SetTurnLimit(int input_limit)
        {
            turn_limit = input_limit;
        }//end method SetTurnLimit

		public List<Feature> ForwardProjection(Feature currentTopic, int forwardTurn)
		{
			//remember internal variables for forward projection
			List<Feature> internalTopicHistory = new List<Feature>(this.TopicHistory);
			int internalTurn = this.Turn;
			Feature internalTopic = this.Topic;
			Feature tempCurrentTopic = currentTopic;
			List<TemporalConstraint> temp_temporal_constraint_list = new List<TemporalConstraint>();
			for (int x = 0; x < temporal_constraint_list.Count(); x++)
			{
				temp_temporal_constraint_list.Add(new TemporalConstraint(temporal_constraint_list[x].FirstArgument,
					temporal_constraint_list[x].SecondArgument, temporal_constraint_list[x].ThirdArgument,
					temporal_constraint_list[x].FourthArgument, temporal_constraint_list[x].FifthArgument));
			}

			//Forward Projection
			List<Feature> topicList = new List<Feature>();
			//topicList.Add(currentTopic);
			//Progress 'forwardTurn' number of turns
			for (int x = 0; x < forwardTurn; x++)
			{
				//update Internal variables
				tempCurrentTopic = calculator.GetNextTopic(tempCurrentTopic, "", this.Turn, this.TopicHistory);
				this.SetNextTopic(tempCurrentTopic);
				this.Turn += 1;
				topicList.Add(tempCurrentTopic);
			}//end for

			//recover all old variables
			this.TopicHistory = internalTopicHistory;
			this.Turn = internalTurn;
			this.temporal_constraint_list = temp_temporal_constraint_list;
			this.Topic = internalTopic;
			for (int x = 0; x < forwardTurn; x++)
			{
				topicList[x].DiscussedAmount -= 1;
			}//end for

			return topicList;
		}

		//PUBLIC UTILITY FUNCTIONS
		//Uses the calculator to get the components of the score calculated between
		//the two given features.
		public double[] GetScoreComponents(Feature current_feature, Feature previous_feature)
		{
			return calculator.CalculateScoreComponents(current_feature, previous_feature
														, this.Turn, this.TopicHistory);
		}//end function GetScoreComponents

		/// <summary>
		/// Returns a string consisting of the name and score of the first 'amount'
		/// number of nodes with the highest score calculated against the given feature.
		/// </summary>
		public string ListMostProximalFeatures(Feature current_feature, int amount = 5)
		{
			string answer = "";

			List<Tuple<Feature, double>> listScore = calculator.GetMostProximalFeatures(current_feature, this.Turn, this.TopicHistory, amount);

			//The string returned will consist of the ID and calculated score of the first amount nodes
			for (int x = 0; x < amount; x++)
			{
				answer += feature_graph.getFeatureIndex(listScore[x].Item1.Id) + " " + listScore[x].Item2 + " ";
			}//end for

			return answer;
		}//end ListMostNovelFeatures

		/// <summary>
		/// Returns a string consisting of the name and novelty value of the first 'amount'
		/// number of most novel nodes calculated against the given feature.
		/// </summary>
		public string ListMostNovelFeatures(Feature current_feature, int amount = 5)
		{
			string answer = "";

			List<Tuple<Feature, double>> listScore = calculator.GetMostNovelFeatures(current_feature, this.Turn, this.TopicHistory, amount);

			//The string returned will consist of the ID and calculated score of the first amount nodes
			for (int x = 0; x < amount; x++)
			{
				answer += feature_graph.getFeatureIndex(listScore[x].Item1.Id) + " " + listScore[x].Item2 + " ";
			}//end for

			return answer;
		}//end ListMostNovelFeatures

		/// <summary>
		/// Tells the input to the AIML chat bot. Returns the response from the chat bot.
		/// </summary>
		public string TellChatBot(string input)
		{
			string output = "";
			//Create a request, which can be passed to the chatbot.
			Request request = new Request(input, this.user, this.aiml_bot);
			Console.WriteLine("Chatbot Input: " + input);
			//Get the response from the chatbot
			Result result = aiml_bot.Chat(request);
			output = result.Output;
			Console.WriteLine("Chatbot Output: " + output);
			//<set name="it"><set name="that"><set name="this">
			//<think><set name="topic"><star/></set></think>
			return output;
		}//end method SayToChatBot

        public Story Interweave(Story story_1, Story story_2)
        {
            Story interwoven_story = new Story();

            StorySegment story_1_part_1 = new StorySegment();
            StorySegment story_1_part_2 = new StorySegment();
            int switchpoint_turn = IdentifySwitchPointTurn(story_1);
            StoryNode switchpoint_node = story_1.GetNodeAtTurn(switchpoint_turn);
            switchpoint_node.AddStoryAct(Constant.SWITCHPOINT, switchpoint_node.graph_node_id);
            Feature switchpoint_feature = feature_graph.getFeature(switchpoint_node.graph_node_id);

            for (int i = 0; i <= switchpoint_turn; i++)
            {
                story_1_part_1.AddStoryNode(story_1.GetNodeSequence()[i]);
            }//end for
            for (int i = switchpoint_turn + 1; i < story_1.GetNodeSequence().Count; i++)
            {
                story_1_part_2.AddStoryNode(story_1.GetNodeSequence()[i]);
            }//end for

            //Check for any HINT-ATs we can add, as well as their corresponding resolutions.
            foreach (StoryNode temp_node in story_1_part_1.Sequence)
            {
                Feature temp_node_feature = feature_graph.getFeature(temp_node.graph_node_id);
                foreach (StoryNode compare_node in story_1_part_2.Sequence)
                {
                    Feature compare_node_feature = feature_graph.getFeature(compare_node.graph_node_id);
                    //Check if there is a relationship in either direction between the two.
                    if ((!temp_node_feature.getRelationshipNeighbor(compare_node_feature.Id).Equals("")
                        && !(temp_node_feature.getRelationshipNeighbor(compare_node_feature.Id) == null)))
                    {
                        temp_node.AddStoryAct(Constant.HINTAT, compare_node.graph_node_id);
                        compare_node.AddStoryAct(Constant.RESOLVE, temp_node.graph_node_id);
                    }//end if
                    else if (!compare_node_feature.getRelationshipNeighbor(temp_node_feature.Id).Equals("")
                        && !(compare_node_feature.getRelationshipNeighbor(temp_node_feature.Id) == null))
                    {
                        temp_node.AddStoryAct(Constant.HINTAT, compare_node.graph_node_id);
                        compare_node.AddStoryAct(Constant.RESOLVE, temp_node.graph_node_id);
                    }//end else if
                }//end foreach
            }//end foreach

            //Check for any TIE-BACKs we can add.
            foreach (StoryNode temp_node in story_2.GetNodeSequence())
            {
                Feature temp_node_feature = feature_graph.getFeature(temp_node.graph_node_id);
                foreach (StoryNode compare_node in story_1_part_1.Sequence)
                {
                    Feature compare_node_feature = feature_graph.getFeature(compare_node.graph_node_id);
                    //Check if there is a relationship in either direction between the two.
                    if ((!temp_node_feature.getRelationshipNeighbor(compare_node_feature.Id).Equals("")
                        && !(temp_node_feature.getRelationshipNeighbor(compare_node_feature.Id) == null)))
                    {
                        temp_node.AddStoryAct(Constant.TIEBACK, compare_node.graph_node_id);
                    }//end if
                    else if (!compare_node_feature.getRelationshipNeighbor(temp_node_feature.Id).Equals("")
                        && !(compare_node_feature.getRelationshipNeighbor(temp_node_feature.Id) == null))
                    {
                        temp_node.AddStoryAct(Constant.TIEBACK, compare_node.graph_node_id);
                    }//end else if
                }//end foreach
            }//end foreach

            //Put stories together
            foreach (StoryNode temp_node in story_1_part_1.Sequence)
            {
                interwoven_story.AddStoryNode(temp_node);
            }//end foreach
            foreach (StoryNode temp_node in story_2.GetNodeSequence())
            {
                interwoven_story.AddStoryNode(temp_node);
            }//end foreach
            foreach (StoryNode temp_node in story_1_part_2.Sequence)
            {
                interwoven_story.AddStoryNode(temp_node);
            }//end foreach

            return interwoven_story;
        }//end method Interweave

        public int IdentifySwitchPointTurn(Story story)
        {
            return calculator.IdentifySwitchPointTurn(story);
        }//end method IdentifySwitchPointTurn
        //Returns the feature that would serve best as a switch point given
        // the input storyline.
        public Feature IdentifySwitchPoint(List<Feature> storyline)
        {
            return calculator.IdentifySwitchPoint(storyline);
        }//end method IdentifySwitchPoint

        public string Foreshadow(Feature feature_to_foreshadow, List<Feature> reference_list)
        {
            SpeakTransform temp_transform = new SpeakTransform(topic_history, topic_history[topic_history.Count - 1]);
            return temp_transform.Foreshadow(feature_to_foreshadow, reference_list);
        }//end method Foreshadow

        public string TieBack(Feature feature_to_tie_back, List<Feature> reference_list, Feature previous_feature)
        {
            SpeakTransform temp_transform = new SpeakTransform(topic_history, topic_history[topic_history.Count - 1]);
            return temp_transform.TieBack(feature_to_tie_back, reference_list, previous_feature);
        }//end method TieBack

		//PRIVATE UTILITY FUNCTIONS
		//Returns the speak value passed in with adornments according to the feature passed in, such as topic lead-ins and analogies.
        private string SpeakWithAdornments(Feature feat, string speak, List<Feature> input_history = null)
		{
            String to_speak = "";
            bool use_relationships = true;
            //Treat the feature passed in as the current topic
            Feature current_topic = feat;

            Feature previous_topic = null;

            //If there is no input history, use the narration manager's history
            if (input_history == null)
            {
                if (topic_history.Count < 2)
                    previous_topic = null;
                else
                    previous_topic = topic_history[topic_history.Count - 2];
                //Create the speak transform object, initialized with history list and the previous topic
                SpeakTransform transform = new SpeakTransform(topic_history, previous_topic);
                //Pass in the given feature and speak value to be transformed.
                to_speak = transform.TransformSpeak(feat, speak);
            }//end if
            else
            {
                Feature input_previous_topic;
                if (input_history.Count < 1)
                    input_previous_topic = null;
                else
                    input_previous_topic = input_history[input_history.Count - 1];
                //Create the speak transform object, initialized with the input history list and the previous topic
                SpeakTransform transform = new SpeakTransform(input_history, input_previous_topic);
                //Pass in the given feature and speak value to be transformed.
                to_speak = transform.TransformSpeak(feat, speak);
            }//end if

            return to_speak;
        }//end method AdornMessage
        private string SpeakWithAdornmentsReference(Feature feat, string speak, List<Feature> reference_list, List<Feature> input_history = null)
        {
            String to_speak = "";
            bool use_relationships = true;
            //Treat the feature passed in as the current topic
            Feature current_topic = feat;

            Feature previous_topic = null;

            //If there is no input history, use the narration manager's history
            if (input_history == null)
            {
                if (topic_history.Count < 2)
                    previous_topic = null;
                else
                    previous_topic = topic_history[topic_history.Count - 2];
                //Create the speak transform object, initialized with history list and the previous topic
                SpeakTransform transform = new SpeakTransform(topic_history, previous_topic, reference_list);
                //Pass in the given feature and speak value to be transformed.
                to_speak = transform.TransformSpeak(feat, speak);
            }//end if
            else
            {
                Feature input_previous_topic;
                if (input_history.Count < 1)
                    input_previous_topic = null;
                else
                    input_previous_topic = input_history[input_history.Count - 1];
                //Create the speak transform object, initialized with the input history list and the previous topic
                SpeakTransform transform = new SpeakTransform(input_history, input_previous_topic, reference_list);
                //Pass in the given feature and speak value to be transformed.
                to_speak = transform.TransformSpeak(feat, speak);
            }//end if

			return to_speak;
		}//end method AdornMessage

		/// <summary>
		/// Finds one or more things to say about the given feature
		/// and returns them as an array of strings.
		/// </summary>
		/// <param name="feature">The feature to finds something to say about.</param>
		private string[] FindStuffToSay(Feature feature)
		{
			//Check for null input
			if (feature == null)
				return null;

			List<string> stuff = new List<string>();
			//Get all of this feature's speak values
			string[] speaks = feature.Speaks.ToArray();
			//Add each of the feature's speak values to the list of things to say about it
			if (speaks.Length > 0)
			{
				stuff.AddRange(speaks);
			}// end if

			stuff.AddRange(SpeakNeighborRelations(feature.Name, FindAllDirectionalNeighbors(feature)));

			//If nothing else can be found, simply speak the feature's name
			if (stuff.Count() == 0)
			{
				stuff.Add(feature.Name);
			}//end if
			return stuff.ToArray();
		}// end function FindStuffToSay

		/// <summary>
		/// Takes a Query object and builds a list of output strings
		/// to talk about the query's MainTopic, taking its specified question
		/// words and direction words, if any, into consideration.
		/// </summary>
		/// <param name="query"></param>
		private string[] ParseQuery(Query query)
		{
			if (query == null)
				return new string[] { "I don't know." };

		   //Related to spatial constraint. Relationships that can be used to describe the location of something.
		   string[] locational_words = { "is north of", "is northwest of", "is east of", "is south of"
												, "is in", "is southwest of", "is west of", "is northeast of"
												, "is southeast of", "took place at", "was held by"
												, "was partially held by" };

			List<string> output = new List<string>();

			if (query.IsQuestion)
			{
				switch (query.QuestionType)
				{
					case Question.WHAT:
						if (query.HasDirection)
						{
							// e.g. What is Direction of Topic?
							// Find names of features that is DIRECTION of MainTopic`
							// Get list of <neighbor> tags
							string dir = query.Direction.ToString().ToLower();
							if (query.Direction == Direction.WON)
							{
								Feature[] temp_neighbors = query.MainTopic.GetNeighborsByRelationship(dir);
								// If the topic has no "won" links, then it is the event
								if (temp_neighbors.Length == 0)
								{
									// So find the winner among its available neighbors
									temp_neighbors = query.MainTopic.GetNeighborsByRelationship("");
									foreach (Feature temp_neighbor in temp_neighbors)
									{
										// Look at ITS neighbors and see if there is a "won" whose name matches this one
										foreach (var triple in temp_neighbor.Neighbors)
										{
											if (triple.Item1.Id == query.MainTopic.Id && triple.Item3 == "won")
												output.Add(string.Format("{0} won {1}.", temp_neighbor.Name, query.MainTopic.Id));
										}// end foreach
									}// end foreach
								}// end if
								// Otherwise it is the winner
								else
								{
									List<string> neighbor_names = new List<string>();
									foreach (Feature temp_neighbor in temp_neighbors)
										neighbor_names.Add(temp_neighbor.Name);

									output.Add(string.Format("{0} won {1}.", query.MainTopic.Id, neighbor_names.JoinAnd()));
								}// end else
							}//end if

							//Directional What is question (e.g., What is south of...?)
							else if (query.Direction == Direction.NORTH
								|| query.Direction == Direction.SOUTH
								|| query.Direction == Direction.EAST
								|| query.Direction == Direction.WEST
								|| query.Direction == Direction.NORTHEAST
								|| query.Direction == Direction.SOUTHEAST
								|| query.Direction == Direction.NORTHWEST
								|| query.Direction == Direction.SOUTHWEST)
							{
								//Relationships to answer these question have the form "is <direction> of".
								//From the topic of the query, look for such a relationship.
								Feature query_topic = query.MainTopic;

								foreach (Tuple<Feature, double, string> temp_neighbor in query_topic.Neighbors)
								{
									//The main topic's neighbor
									Feature temp_feature = temp_neighbor.Item1;
									if (temp_feature.getRelationshipNeighbor(query_topic.Id).ToLower().Contains(query.DirectionWord.ToLower()))
									{
										output.Add(string.Format("{0} " + temp_feature.getRelationshipNeighbor(query_topic.Id) + " {1}.", temp_feature.Id, query_topic.Id));
										break;
									}//end if
								}//end foreach
							}//end else if

							//Handles question like "what did x host?"
							else if (query.Direction == Direction.HOSTED)
							{
								//string[] neighbors = FindNeighborsByRelationship(query.MainTopic, dir);
								//These relationships signal that something was hosted
								string[] hosted_words = { "held", "partially held" };
								Feature query_topic = query.MainTopic;
								string for_output = "";
								//if (neighbors.Length > 0)
								//    output.Add(string.Format("{0} hosted {1}.", query.MainTopic.Id, neighbors.ToList().JoinAnd()));

								//Check from topic to neighbors
								for_output = ConstructQueryOutputByRelationship(query, hosted_words.ToList<string>());

								if (!for_output.Equals(""))
								{
									output.Add(for_output);
								}//end if
							}//else if

							//Questions like "What is inside x?"
							else if (query.Direction == Direction.INSIDE)
							{
								//These relationships signal that something was inside something else (e.g., venues inside the olympic green)
								string[] inside_words = { "is in" };
								Feature query_topic = query.MainTopic;
								string for_output = "";
								//if (neighbors.Length > 0)
								//    output.Add(string.Format("{0} hosted {1}.", query.MainTopic.Id, neighbors.ToList().JoinAnd()));

								foreach (Tuple<Feature, double, string> temp_neighbor in query_topic.Neighbors)
								{
									foreach (string inside_word in inside_words)
									{
										/*if (temp_feature.getRelationshipNeighbor(query_topic.Id).ToLower().Contains(query.DirectionWord.ToLower()))
										{
											output.Add(string.Format("{0} " + temp_feature.getRelationshipNeighbor(query_topic.Id) + " {1}.", temp_feature.Id, query_topic.Id));
											break;
										}//end if*/
										//Checking from Neighbor to Topic
										if (temp_neighbor.Item1.getRelationshipNeighbor(query_topic.Id).ToLower().Contains(query.DirectionWord.ToLower()))
										{
											//DEBUG
											Console.Out.WriteLine("Inside word " + inside_word + " found.");
											//END DEBUG
											if (for_output.Equals(""))
												for_output = string.Format("{0} " + temp_neighbor.Item3 + " {1}", temp_neighbor.Item1.Id, query_topic.Id);
											else
												for_output += string.Format(", {0} " + temp_neighbor.Item3 + " {1}", temp_neighbor.Item1.Id, query_topic.Id);
											//for_output.Add(string.Format("{0} " + temp_neighbor.Item3 + " {1}.", query_topic.Id, temp_neighbor.Item1.Id));
										}//end if
									}//end foreach
								}//end foreach
								if (!for_output.Equals(""))
								{
									for_output += ".";
									output.Add(for_output);
								}//end if
							}//end else if

							else
							{
								Feature[] neighbors = query.MainTopic.GetNeighborsByRelationship(dir);
								List<string> neighbor_names = new List<string>();
								foreach (Feature temp_neighbor in neighbors)
								{
									neighbor_names.Add(temp_neighbor.Name);
								}//end foreach

								if (neighbors.Length > 0)
									output.Add(string.Format("{0} of {1} {2} {3}", dir.ToUpperFirst(), query.MainTopic.Id,
										(neighbors.Length > 1) ? "are" : "is", neighbor_names.JoinAnd()));
							}//end else
						}// end if
						//Otherwise, the WHAT question has no direction.
						else
						{
							// e.g. What is Topic?
							// Get the <speak> attribute, if able
							string[] speak = FindStuffToSay(query.MainTopic);
							if (speak.Length > 0)
							{
								//Addorn the speak value
								speak[0] = SpeakWithAdornments(query.MainTopic, speak[0]);

								output.AddRange(speak);
							}//end if
						}//end else
						break;
					case Question.WHERE:
						if (false)
						{

						}
						else
						{
							// e.g. Where is Topic?
							// Get all the neighbors from this feature and the "opposite" directions
							//output.AddRange((SpeakNeighborRelations(query.MainTopic.Id, FindAllNeighbors(query.MainTopic))));

							//Where is the main topic
							Feature query_topic = query.MainTopic;

							string for_output = "";

							//Check from topic to neighbors
							for_output = ConstructQueryOutputByRelationship(query, locational_words.ToList<string>());

							if (!for_output.Equals(""))
							{
								output.Add(for_output);
							}//end if
						}
						break;
					case Question.WHEN:
						// e.g. When was Topic made/built/etc.?
						break;
				}
			}//end if
			else
			{
				// e.g.:
				// Tell me about Topic.
				// Topic.
				output.Add(SpeakWithAdornments(query.MainTopic, FindStuffToSay(query.MainTopic)[0]));
			}//end else

			return output.Count() > 0 ? output.ToArray() : new string[] { IDK };
		}// end function ParseQuery

		//Constructs an output to a given query by examining the list of words to check against the relationships
		//that the query's main topic has with its neighbors.
		//Last optional parameter decides whether we are checking the relationships from the topic to its neighbors or 
		//the relationships from the neighbors to the topic.
		private string ConstructQueryOutputByRelationship(Query query, List<string> words_to_check, bool from_topic_to_neighbors = true)
		{
			string output_string = "";

			//Where is the main topic
			Feature query_topic = query.MainTopic;
			//What topic should we change to
			Feature topic_change = query.MainTopic;

			if (from_topic_to_neighbors)
				//Look for one of the locational words in the main topic's relationships
				foreach (Tuple<Feature, double, string> temp_neighbor in query_topic.Neighbors)
				{
					foreach (string word_to_check in words_to_check)
					{
						if (temp_neighbor.Item3.ToLower().Contains(word_to_check.ToLower()))
						{
							//DEBUG
							Console.Out.WriteLine("Word to check " + word_to_check + " found.");
							//END DEBUG
							if (output_string.Equals(""))
							{
								//For now, just take the first matching feature and (potentially) change the topic to that.
								topic_change = temp_neighbor.Item1;
								output_string = string.Format("{0} " + temp_neighbor.Item3 + " {1}", query_topic.Id, temp_neighbor.Item1.Id);
							}//end if
							else
								output_string += string.Format(", " + temp_neighbor.Item3 + " {0}", temp_neighbor.Item1.Id);
							//for_output.Add(string.Format("{0} " + temp_neighbor.Item3 + " {1}.", query_topic.Id, temp_neighbor.Item1.Id));
						}//end if
					}//end foreach
				}//end foreach

			if (!output_string.Equals(""))
			{
				//Find the last comma and put an "and" after it
				if (output_string.LastIndexOf(",") > 0)
				{
					output_string = output_string.Insert(output_string.LastIndexOf(","), " and");
				}//end if
				output_string += ". ";

				//If the query topic and the current topic are the same, avoid
				//repeating the current topic by changing the query topic.
				if (query_topic.Equals(this.topic))
				{
					//Change the main topic
					query.MainTopic = topic_change;
				}//end if

				//Whether or not the topic has changed, say something about the main topic at the end.
				//output_string += FindStuffToSay(query.MainTopic)[0];
			}//end if
			return output_string;
		}//end method ConstructQueryOutputByRelationship

		//Creates a list of strings that say the featureName passed in and its relationship to each neighbor.
		private string[] SpeakNeighborRelations(string featureName, Tuple<string, Direction>[] neighbors)
		{
			string[] neighborRelations = new string[neighbors.Length];
			if (neighborRelations.Length == 0)
				return new string[] { };
			for (int i = 0; i < neighborRelations.Length; i++)
				//Creates strings of the form "(featureName or It) is (relationship) of (neighbor node)
				neighborRelations[i] = string.Format("{0} is {1} of {2}.",
					(i == 0) ? featureName : "It",
					neighbors[i].Item2.Invert().ToString().ToLower(),
					neighbors[i].Item1);
			return neighborRelations;
		}//end function SpeakNeighborRelations

		/// <summary>
		/// Finds all neighbors to the given feature which have a directional relationship
		/// to the feature. Returns them in a tuple comprised of the neighbor's name and its direction.
		/// </summary>
		private Tuple<string, Direction>[] FindAllDirectionalNeighbors(Feature feature)
		{
			string[] directionWords = {"inside", "contain", "north", "east", "west", "south",
									  "northeast", "northwest", "southeast", "southwest",
									  "hosted", "was_hosted_at", "won"};

			var _neighbors = feature.Neighbors;
			var neighbors = new List<Tuple<string, Direction>>();
			foreach (var triple in _neighbors)
			{
				string neighborName = triple.Item1.Name;
				string relationship = triple.Item3;
				if (directionWords.Contains(relationship))
					neighbors.Add(new Tuple<string, Direction>(neighborName,
						((Direction)Enum.Parse(typeof(Direction), relationship.ToUpper().Replace(' ', '_')))));
			}
			return neighbors.ToArray();
		}//end function FindAllNeighbors

		/// <summary>
		/// Pulls an output string from the buffer according to the buffer index.
		/// Increments the buffer index, so the next pull will be from the next item in the buffer.
		/// Getting to the end of the buffer will result in an "out of responses" reply.
		/// </summary>
		public string PullOutputFromBuffer()
		{
			if (b > this.buffer.Length - 1)
				return "I've said all I can about that topic!" + "##" + "我已经把我知道的都说完了。" + "##";
			else
				return this.buffer[b++];
		}//end function PullOutputFromBuffer

		/// <summary>
		/// Sets the current topic feature to the given feature, incrementing the new topic's
		/// discussed amount. Updates topic history. Does not reset the output buffer.
		/// </summary>
		/// <param name="next_topic">The next topic feature, which will become the current topic.</param>
		private void SetNextTopic(Feature next_topic)
		{
			//Place the next topic in the history list
			UpdateTopicHistory(next_topic);

		    next_topic.DiscussedAmount += 1;
			this.feature_graph.setFeatureDiscussedAmount(next_topic.Id, next_topic.DiscussedAmount);
			this.topic = next_topic;
			//Set the topic in the AIML chatbot
			string temp = TellChatBot("SETTOPIC " + this.topic.Name.Split(new string[] { "##" }, StringSplitOptions.None)[0]);
			//string temp = TellChatBot("SETTOPIC");
		}//end method ChangeTopic
		/// <summary>
		/// Sets the current topic feature to the given topic feature, incrementing the next topic's
		/// discussed amount. Updates topic history. Also resets the output string buffer to the given array. 
		/// Reseting the buffer resets the buffer index, b, to 0.
		/// </summary>
		/// <param name="next_topic">The next topic feature, which will become the current topic.</param>
		/// <param name="new_buffer">The string array that the output buffer will be set to.</param>
		public void SetNextTopic(Feature next_topic, string[] new_buffer)
		{
			//Set the topic
			SetNextTopic(next_topic);
			//Fill the passed in buffer
			this.buffer = new_buffer;
		}//end method ChangeTopic

		/// <summary>
		/// Adds the given feature to the end of the topic history list and updates any relevant
		/// other information. Currently, updates spatial and temporal constraint information.
		/// </summary>
		private void UpdateTopicHistory(Feature new_topic)
		{
			///ZEV: Remove?
			/*string[] Directional_Words = { "is southwest of", "is southeast of"
				, "is northeast of", "is north of", "is west of", "is east of", "is south of", "is northwest of" };

			//update spatial constraint information
			bool spatialExist = false;
			if (topic_history.Count() > 0)
			{
				Feature prevTopic = topic_history[topic_history.Count() - 1];
				if (prevTopic.getNeighbor(new_topic.Id) != null)
				{
					foreach (string str in Directional_Words)
					{
						//Check whether there was a directional word
						if (str == prevTopic.getRelationshipNeighbor(new_topic.Id))
						{
							previous_directional = str;
							spatialExist = true;
							break;
						}
					}//end foreach
				}
			}//end if
			if (!spatialExist)
			{
				previous_directional = "";
			}//end if*/

			//update temporal constraint information
			/*NarrationManager temp = new NarrationManager(this.feature_graph, temporal_constraint_list);
			List<int> temporalIndex = temp.TemporalConstraint(new_topic, this.Turn, topic_history);
			for (int x = 0; x < temporalIndex.Count(); x++)
			{
				temporal_constraint_list[temporalIndex[x]].Satisfied = true;
			}//end for*/
			//Place the new topic at the end of the topic history
			topic_history.Add(new_topic);
		}//end method UpdateTopicHistory

		//ACCESSORS/MUTATORS
		/// <summary>
		/// The feature which is currently the main topic of narration/conversation.
		/// </summary>
		public Feature Topic
		{
			get
			{
				return this.topic;
			}//end get
			set
			{
				this.topic = value;
			}//end set
		}

		/// <summary>
		/// A count of which turn of conversation we are on.
		/// </summary>
		public int Turn
		{
			get
			{
				return this.turn;
			}//end get
			set
			{
				this.turn = value;
			}//end set
		}

		/// <summary>
		/// A history list of which features have been the topic of narration/conversation.
		/// In chronological order.
		/// </summary>
		public List<Feature> TopicHistory
		{
			get
			{
				return this.topic_history;
			}//end get
			set
			{
				this.topic_history = value;
			}//end set
		}

	}//end class NarrationManager
}//end namespace
