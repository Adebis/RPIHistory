using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dialogue_Data_Entry;
using AIMLbot;
using System.Collections;
using System.Diagnostics;
using Newtonsoft.Json;

using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Dialogue_Data_Entry
{
	enum Direction : int
	{
		NORTH = 1, SOUTH = -1,
		EAST = 2, WEST = -2,
		NORTHEAST = 3, SOUTHWEST = -3,
		NORTHWEST = 4, SOUTHEAST = -4,
		CONTAIN = 5, INSIDE = -5,
		HOSTED = 6, WAS_HOSTED_AT = -6,
		WON = 0
	}
	enum Question : int
	{
		WHAT = 0, WHERE = 1, WHEN = 2
	}

	/// <summary>
	/// A data structure to hold information about a query
	/// </summary>
	class Query
	{
		// The name of the Feature that the user asked about
		public Feature MainTopic { get; set; }
		// Whether or not the input was an explicit question
		public bool IsQuestion { get { return QuestionType != null; } }
		// The type of Question
		public Question? QuestionType { get; private set; }
		// The direction/relationship asked about.
		public Direction? Direction { get; private set; }
		public string DirectionWord { get; private set; }
		public bool HasDirection { get { return Direction != null; } }

		public Query(Feature mainTopic, Question? questionType, Direction? directions, string direction_word = "")
		{
			MainTopic = mainTopic;
			QuestionType = questionType;
			Direction = directions;
			DirectionWord = direction_word;
		}
		public override string ToString()
		{
			string s = "Topic: " + MainTopic.Id;
			s += "\nQuestion type: " + QuestionType ?? "none";
			s += "\nDirection specified: " + Direction ?? "none";
			s += "\nDirection word: " + DirectionWord ?? "none";
			return s;
		}
	}

	/// <summary>
	/// A utility class to parse natural input into a Query and a Query into natural output.
	/// </summary>
	class QueryHandler
	{
		private const string FORMAT = "FORMAT:";
		private const string IDK = "I'm afraid I don't know anything about that topic." + "##" + "对不起，我不知道。" + "##";
		private string[] punctuation = { ",", ";", ".", "?", "!", "\'", "\"", "(", ")", "-" };
		private string[] questionWords = { "?", "what", "where", "when" };

		private string[] directionWords = {"inside", "contain", "north", "east", "west", "south",
									  "northeast", "northwest", "southeast", "southwest",
									  "hosted", "was_hosted_at", "won"};

		private string[] Directional_Words = { "is southwest of", "is southeast of"
				, "is northeast of", "is north of", "is west of", "is east of", "is south of", "is northwest of" };

		//Related to spatial constraint. Relationships that can be used to describe the location of something.
		private string[] locational_words = { "is north of", "is northwest of", "is east of", "is south of"
												, "is in", "is southwest of", "is west of", "is northeast of"
												, "is southeast of", "took place at", "was held by"
												, "was partially held by" };

		
		// "is in" -> contains?
		private int iterations;
		private Bot bot;
		private User user;
		private FeatureGraph graph;

		private List<string> features;

		private int noveltyAmount = 5;
		private List<TemporalConstraint> temporalConstraintList;
		//private List<int> topicHistory = new List<int>();
		private string prevSpatial;

		private NarrationManager narration_manager;

		public LinkedList<Feature> prevCurr = new LinkedList<Feature>();

		//A list of all the features that have been chosen as main topics
		public LinkedList<Feature> feature_history = new LinkedList<Feature>();
		//The topic before the current one
		public Feature previous_topic;

		public int countFocusNode = 0;
		public double noveltyValue = 0.0;

        public Form1 parent_form1;

		//A list of string lists, each of which represents a set of relationship
		//words which may be interchangeable when used to find analogies.
		public List<List<string>> equivalent_relationships = new List<List<string>>();

		//FILTERING:
		//A list of nodes to filter out of mention.
		//Nodes in this list won't be spoken explicitly unless they
		//are directly queried for.
		//These nodes are still included in traversals, but upon traveling to
		//one of these nodes the next step in the traversal is automatically taken.
		public List<string> filter_nodes = new List<string>();
		//A list of relationships which should not be used for analogies.
		public List<String> no_analogy_relationships = new List<string>();

		//JOINT MENTIONS:
		//A list of feature lists, each of which represent
		//nodes that should be mentioned together
		public List<List<Feature>> joint_mention_sets = new List<List<Feature>>();

		//Which language we are operating in.
		//Default is English.
		public int language_mode_display = Constant.EnglishMode;
		public int language_mode_tts = Constant.EnglishMode;

		//A string to be used for text-to-speech
		public string buffered_tts = "";

        public Story main_story;
        public List<Story> stories;

        // The file where the constraints are encoded
        private List<string> constraint_list_filenames = new List<string>();
        private string constraint_list_filename = "constraint.txt";
        //private string constraint_list_filename = "rpi_early_growth_constraints.txt";
        private List<int> target_id_list = new List<int>();
        List<Tuple<int, string, int>> target_constraint_list;

		/// <summary>
		/// Create a converter for the specified XML file
		/// </summary>
		/// <param name="xmlFilename"></param>
		public QueryHandler(FeatureGraph graph, List<TemporalConstraint> myTemporalConstraintList, Form1 parent_f1)
		{
            File.Delete("output.txt");
            parent_form1 = parent_f1;
			// Load the AIML Bot
			//this.bot = new Bot();
			this.temporalConstraintList = myTemporalConstraintList;
			/*bot.loadSettings();
			bot.isAcceptingUserInput = false;
			bot.loadAIMLFromFiles();
			bot.isAcceptingUserInput = true;
			this.user = new User("user", this.bot);*/
            //constraint_list_filenames.Add("rpi_early_growth_constraints.txt");
            //constraint_list_filenames.Add("ricketts_expansion_constraints.txt");

			// Load the Feature Graph
			this.graph = graph;

			this.iterations = 0;

			// Feature Names, with which to index the graph
			this.features = graph.getFeatureNames();

            // Read the constraint list.
            System.IO.StreamReader file = new System.IO.StreamReader(constraint_list_filename);
            string file_line = "";
            target_id_list = new List<int>();
            // The constraint list consists of constraints; tuples of source node id, operator, target node id.
            target_constraint_list = new List<Tuple<int, string, int>>();
            while ((file_line = file.ReadLine()) != null)
            {
                // Separate this line by spaces.
                string[] separated_file_line = file_line.Split(' ');
                int current_index = 0;
                string current_item = separated_file_line[0];
                // Check if the first item is an integer.
                int node_id = -1;
                bool parse_success = false;
                parse_success = int.TryParse(current_item, out node_id);
                // If it is an integer, then this is a node id.
                if (parse_success)
                {
                    // We need to find out if there is an operator following this node.
                    // Peak at the next two items in the line.
                    if (current_index + 1 < separated_file_line.Length)
                    {
                        // There is at least an operator following the current node.
                        string next_item = separated_file_line[current_index + 1];

                        // Check if there is another node ID following the operator.
                        if (current_index + 2 < separated_file_line.Length)
                        {
                            int next_node_id = -1;
                            string next_next_item = separated_file_line[current_index + 2];
                            parse_success = int.TryParse(next_next_item, out next_node_id);

                            // Two items down is a node id.
                            if (parse_success)
                            {
                                // Since we have a node id, an operator, and a node id, add it
                                // to the list of constraints.
                                target_constraint_list.Add(new Tuple<int, string, int>(node_id, next_item, next_node_id));
                            }//end if
                        }//end if
                    }//end if
                    // If there is no next item, then this node id only item in the line
                    else
                    {
                        // Add it to the target id list.
                        target_id_list.Add(node_id);
                    }//end else
                }//end if
            }//end while
            file.Close();

			//Initialize the dialogue manager
			//narration_manager = new NarrationManager(this.graph, myTemporalConstraintList);
            // Make sure the constraints are added to each feature in the feature graph.

            // If the node appears in the target id list, add a constraint with the node id as the 
            // source, the empty string as the operand, and -1 as the target.
            foreach (int node_id in target_id_list)
            {
                this.graph.getFeature(node_id).constraints.Add(new Tuple<int, string, int>(node_id, "", -1));
            }//end foreach
            // All other constraints should be added as-is, for both the source and target nodes
            foreach (Tuple<int, string, int> constraint in target_constraint_list)
            {
                if (this.graph.hasNode(constraint.Item1))
                    this.graph.getFeature(constraint.Item1).constraints.Add(constraint);
                if (this.graph.hasNode(constraint.Item3))
                    this.graph.getFeature(constraint.Item3).constraints.Add(constraint);
            }//end foreach

            narration_manager = new NarrationManager(this.graph, target_id_list, target_constraint_list);

            main_story = null;
            stories = new List<Story>();

			//Build lists of equivalent relationships
			//is, are, was, is a kind of, is a
			equivalent_relationships.Add(new List<string>() { "is", "are", "was", "is a kind of", "is a" });
			//was a member of, is a member of
			equivalent_relationships.Add(new List<string>() { "was a member of", "is a member of" });
			//won a gold medal in, won
			equivalent_relationships.Add(new List<string>() { "won a gold medal in", "won" });
			//is one of, was one of the, was one of
			equivalent_relationships.Add(new List<string>() { "is one of", "was one of the", "was one of" });
			//include, includes, included
			equivalent_relationships.Add(new List<string>() { "include", "includes", "included" });
			//took place on
			equivalent_relationships.Add(new List<string>() { "took place on" });
			//took place at
			equivalent_relationships.Add(new List<string>() { "took place at" });
			//has, had
			equivalent_relationships.Add(new List<string>() { "has", "had" });
			//includes event
			equivalent_relationships.Add(new List<string>() { "includes event" });
			//includes member, included member
			equivalent_relationships.Add(new List<string>() { "includes member", "included member" });
			//include athlete
			equivalent_relationships.Add(new List<string>() { "include athlete" });
			//is southwest of, is southeast of, is northeast of, is north of,
			//is west of, is east of, is south of, is northwest of
			equivalent_relationships.Add(new List<string>() { "is southwest of", "is southeast of"
				, "is northeast of", "is north of", "is west of", "is east of", "is south of", "is northwest of" });

			//Build list of filter nodes.
			//Each filter node is identified by its Data values in the XML
			filter_nodes.Add("Male");
			filter_nodes.Add("Female");
			filter_nodes.Add("Cities");
			filter_nodes.Add("Sports");
			filter_nodes.Add("Gold Medallists");
			filter_nodes.Add("Venues");
			filter_nodes.Add("Time");
			filter_nodes.Add("Aug. 8th, 2008");
			filter_nodes.Add("Aug. 24th, 2008");
			filter_nodes.Add("Aug. 9th, 2008");
			filter_nodes.Add("Aug. 10th, 2008");
			filter_nodes.Add("Aug. 11th, 2008");
			filter_nodes.Add("Aug. 12th, 2008");
			filter_nodes.Add("Aug. 13th, 2008");
			filter_nodes.Add("Aug. 14th, 2008");
			filter_nodes.Add("Aug. 15th, 2008");
			filter_nodes.Add("Aug. 16th, 2008");
			filter_nodes.Add("Aug. 17th, 2008");
			filter_nodes.Add("Aug. 18th, 2008");
			filter_nodes.Add("Aug. 19th, 2008");
			filter_nodes.Add("Aug. 20th, 2008");
			filter_nodes.Add("Aug. 21st, 2008");
			filter_nodes.Add("Aug. 22nd, 2008");
			filter_nodes.Add("Aug. 23rd, 2008");


			//Build list of relationships which should not be used in analogies.
			no_analogy_relationships.Add("occurred before");
			no_analogy_relationships.Add("occurred after");
			no_analogy_relationships.Add("include");
			no_analogy_relationships.Add("includes");
			no_analogy_relationships.Add("included");
			no_analogy_relationships.Add("has");
			no_analogy_relationships.Add("had");
		}//end constructor QueryHandler
			
		private string MessageToServer(Feature feat, string speak, string noveltyInfo, string proximalInfo = "", bool forLog = false, bool out_of_topic_response = false)
		{
			String return_message = "";

			String to_speak = speak; //SpeakWithAdornments(feat, speak);

			//Add adjacent node info to the end of the message.
			//
			//to_speak += AdjacentNodeInfo(feat, last);

			if (out_of_topic_response)
			{
				//"I'm afraid I don't know anything about ";
				to_speak = "I'm sorry, I'm afraid I don't understand what you are asking. But here's something I do know about. "
				   + "##" + "对不起，我不知道您在说什么。但我知道这些。" + "##" + to_speak;
			}//end if

			string tts = ParseOutput(to_speak, language_mode_tts);
			buffered_tts = tts;
			to_speak = ParseOutput(to_speak, language_mode_display);

			if (forLog)
				return_message = to_speak + "\r\n";
			else
			{
				return_message = " ID:" + feat.Id + ":Speak:" + to_speak + ":Novelty:" + noveltyInfo + ":Proximal:" + proximalInfo;
				//return_message += "##" + tts;
			}//end else
				

			//Console.WriteLine("to_speak: " + to_speak);

			return return_message;
		}//end function MessageToServer

        public string ParseInputJSON(string input)
        {
            int story_section_size = 3;
            string json_string = "";

            //First, do an initial pass for any commands.
            String[] split_input = input.Trim().Split(':');
            json_string = ParseInputJSON(split_input);
            if (!json_string.Equals(""))
            {
                return json_string;
            }//end if

            //Next, try to start or continue the chronology.
            //Check for feature names in input.
            Feature input_feature = FindFeature(input);
            if (input_feature != null)
            {
                //If we have found a feature, then there is an explicitly requested anchor node for the next storyline.
                //Update user interest values.
                if (user_interest_mode)
                {
                    if (this.main_story == null)
                        this.graph.UpdateInterestInternal(input_feature.Id, 0);
                    else
                        this.graph.UpdateInterestInternal(input_feature.Id, this.main_story.current_turn);
                }//end if
                //Generate the next section of the chronology.
                input = "CHRONOLOGY:" + input + ":" + story_section_size;
            }//end if
            //Otherwise, pass in the empty string as input to the chronology command.
            //This will get the default next best story node.
            else
            {
                input = "CHRONOLOGY:" + "" + ":" + story_section_size;
            }//end else

            split_input = input.Trim().Split(':');
            json_string = ParseInputJSON(split_input);

            return json_string;
        }//end method ParseInputJSON
        public string ParseInputJSON(string[] split_input)
        {
            string json_string = "";

            if (split_input[0].ToLower().Equals("chronology"))
            {
                Feature anchor_node = null;
                //TODO: Remove later; only for demo in November 2016
                if (graph.file_name.Equals("roman_ww2_analogy.xml") || graph.file_name.Equals("roman_ww2_analogy_2.xml"))
                {
                    Console.Out.WriteLine("Roman ww2 analogy");
                    List<Feature> story_features = new List<Feature>();
                    int node_id = 0;
                    Feature node = null;
                    for (int i = 1; i < split_input.Length; i++)
                    {
                        bool parse_success = int.TryParse(split_input[i], out node_id);
                        if (parse_success)
                        {
                            node = graph.getFeature(node_id);
                            story_features.Add(node);
                        }//end if
                    }//end for
                    story_features.Add(graph.getFeature(6));
                    story_features.Add(graph.getFeature(992));
                    if (story_features.Count > 0)
                    {
                        //Make a story using the nodes given in the input.
                        string make_story_text = "make_story";
                        foreach (Feature n in story_features)
                        {
                            make_story_text = make_story_text + ":" + n.Id;
                        }//end foreach
                        ParseInputJSON(make_story_text);

                        //Grab the story that was just made
                        Story new_story = stories[stories.Count - 1];
                        //Add an analogy to the end of the second node
                        //1. First, make the analogy.
                        //string analogy = MakeAnalogy(node_id_1, node_id_2);
                        //2. Add an analogy event to the second node.
                        //new_story.GetLastNode().AddStoryAct(Constant.ANALOGY, node_id_1);
                        //3. Append the analogy description to the text of the second node.
                        //JObject json_response = JObject.Parse(analogy);
                        string analogy_json = "{'asserts': {'combatant': 'place of military conflict', 'event': 'partof', 'commander': 'commander', 'battle': 'is part of military conflict', 'battles': 'battle'}, 'confidence': 1.0, 'explanation': '', 'mapping': {('OUTGOING', 'combatant', 'Ptolemaic Kingdom'): ('place of military conflict', 'World War II', 1.0, 1.0),('OUTGOING', 'event', 'Roman Republic'): ('partof', 'World War II', 1.0, 1.0),('OUTGOING', 'commander', 'Marcus Vipsanius Agrippa'): ('commander', 'Benito Mussolini', 1.0, 1.0),('OUTGOING', 'battle', 'Legio XI Claudia'): ('is part of military conflict', 'North African Campaign', 1.0, 1.0),('OUTGOING', 'battles', 'Legio XI Fretensis'): ('battle', 'Charles Crombie', 1.0, 1.0)}, 'rating': 1.0, 'src': 'Battle of Actium', 'target': 'Mediterranean and Middle East theatre of World War II', 'total_score': 1.0}";
                        //string analogy_text = "Remember the Battle of Actium? Well, the Mediterranean and Middle East theatre of World War II is like the Battle of Actium. This is because Battle of Actium combatant Ptolemaic Kingdom in the same way that World War II place of military conflict Mediterranean and Middle East theatre of World War II, Roman Republic event Battle of Actium in the same way that Mediterranean and Middle East theatre of World War II part of World War II, Battle of Actium commander Marcus Vipsanius Agrippa in the same way that Mediterranean and Middle East theatre of World War II commander Benito Mussolini, Legio XI Claudia battle Battle of Actium in the same way that North African Campaign is part of military conflict Mediterranean and Middle East theatre of World War II, and Legio X Fretensis battles Battle of Actium in the same way that Charles Crombie battle Mediterranean and Middle East theatre of World War II.";
                        string analogy_text = "Let’s compare the Battle of Actium to the Mediterranean and Middle East theatre. Marcus Vipsanius Agrippa was a commander in the Battle of Actium; similarly, Benito Mussolini was a commander in the Mediterranean and Middle East theatre. And, like how the Battle of Actium was an important event in the Roman Republic, the Mediterranean and Middle East theatre was an important part of World War II.";
						
                        //ParseInputJSON("read_story:" + (stories.Count - 1));
                        new_story.GetLastNode().text = "<color=#228b22ff>" + analogy_text + "</color> " + new_story.GetLastNode().text;
                        new_story.GetLastNode().analogy = analogy_json;

                        if (json_mode)
                            json_string = JsonConvert.SerializeObject(new_story);
                        else
                        {
                            json_string = JsonConvert.SerializeObject(new_story);
                        }//end else
                    }//end if
                }//end if
                //Get the anchor node specified in this command 
                else if (split_input[1] != null)
                {
                    String string_topic = split_input[1];
                    //First, check if the topic is the empty string.
                    //If so, try the "default" anchor node.
                    if (string_topic.Equals(""))
                    {
                        //If there is not yet a story, get the root node as the anchor node.
                        if (main_story == null)
                        {
                            anchor_node = graph.Root;
                        }//end if
                        //If there is an ongoing story, get the next best topic based on the story.
                        else
                        {
                            anchor_node = narration_manager.getNextBestStoryTopic(main_story);
                        }//end else
                    }//end if
                    else
                    {
                        //Try to convert the topic to an int to check if it's an id.
                        int int_topic = -1;
                        bool parse_success = int.TryParse(string_topic, out int_topic);
                        if (parse_success)
                        {
                            //Check that the new integer topic is a valid id.
                            anchor_node = graph.getFeature(int_topic);
                        }//end if
                        else
                        {
                            anchor_node = FindFeature(string_topic);
                        }//end else
                    }//end else
                    if (anchor_node != null)
                    {   
                        //check to see if there's an existing story. If not, make a new one.
                        if (main_story == null)
                        {
                            // For the first story we make: If we found an anchor node, first check if 
                            // any of the constraint files has the anchor node as the first node. If so, 
                            // load the constraint list.
                            LoadConstraintList(anchor_node.Id);

                            //Get the turn limit
                            int turn_limit = 0;
                            if (split_input[2] != null)
                            {
                                bool parse_success = int.TryParse(split_input[2], out turn_limit);
                                if (parse_success)
                                {
                                    Console.Out.WriteLine("Turn limit set to " + turn_limit);
                                }//end if
                                else
                                    Console.Out.WriteLine("Could not set turn limit.");
                            }//end if

                            //Make a temporary graph to create the chronology's order before presenting it.
                            //FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);

                            json_string = "";
                            // ZEV: THIS IS WHERE THE CHRONOLOGY/CONSTRAINT BASED STORY IS MADE
                            NarrationManager temp_manager = new NarrationManager(this.graph, target_id_list, target_constraint_list);
                            //Story chronology = temp_manager.GenerateChronology(anchor_node, turn_limit);
                            Story chronology = temp_manager.GenerateTargetStory(turn_limit);

                            //The chronology is generated in segments, separated by user turns.
                            //Create its text.
                            //SpeakTransform t = new SpeakTransform(graph);
                            //t.SpeakStorySegment(last_segment);
                            //Story temp_story = new Story(chronology.GetLastSegment());

                            if (json_mode)
                                json_string = JsonConvert.SerializeObject(chronology);
                            else
                            {
                                json_string = JsonConvert.SerializeObject(chronology);
                            }//end else

                            // Write out this story segment to file.

                            string lines = chronology.ToString(this.graph);

                            // Write the string to a file.
                            System.IO.StreamWriter file = new System.IO.StreamWriter("output.txt");
                            //file = File.AppendText("output.txt");
                            //File.AppendAllText(lines, "output.txt");
                            file.WriteLine(lines);
                            file.Close();

                            main_story = chronology;
                        }//end if
                        else
                        {
                            //Get the turn limit
                            int turn_limit = 0;
                            if (split_input[2] != null)
                            {
                                bool parse_success = int.TryParse(split_input[2], out turn_limit);
                                if (parse_success)
                                {
                                    Console.Out.WriteLine("Turn limit set to " + turn_limit);
                                }//end if
                                else
                                    Console.Out.WriteLine("Could not set turn limit.");
                            }//end if

                            NarrationManager temp_manager = new NarrationManager(this.graph, target_id_list, target_constraint_list);
                            Story chronology = temp_manager.GenerateChronology(anchor_node, 3, starting_story: main_story, user_story: true);
                            if (json_mode)
                                json_string = JsonConvert.SerializeObject(chronology);
                            else
                            {
                                json_string = JsonConvert.SerializeObject(chronology);
                            }//end else
                            main_story = chronology;
                            // Write out this story segment to file.
                            string lines = chronology.ToString(this.graph);

                            // Write the string to a file.
                            //File.AppendAllText(lines, "output.txt");
                            System.IO.StreamWriter file = new System.IO.StreamWriter("output.txt");
                            //file = File.AppendText("output.txt");
                            file.WriteLine(lines);
                            file.Close();

                        }//end else
                    }//end if
                }//end if
            }//end if
            if (split_input[0].ToLower().Equals("add_to_chronology"))
            {
                int node_to_add_id = -1;
                int previous_node_id = -1;
                Feature node_to_add = null;
                Feature previous_node = null;

                bool parse_success = int.TryParse(split_input[1], out node_to_add_id);
                if (parse_success)
                {
                    node_to_add = graph.getFeature(node_to_add_id);
                    Console.Out.WriteLine("Node to add: " + node_to_add.Name);
                }//end if
                parse_success = int.TryParse(split_input[2], out previous_node_id);
                if (parse_success)
                {
                    previous_node = graph.getFeature(previous_node_id);
                    Console.Out.WriteLine("Previous node: " + previous_node.Name);
                }//end if
                // Get the story up to the last occurrence of the previous node.
                this.main_story.TrimAfter(previous_node_id);
                // Pass it into the normal chronology maker.
                NarrationManager temp_manager = new NarrationManager(graph, temporalConstraintList);
                Story chronology = temp_manager.GenerateChronology(node_to_add, 5, starting_story: this.main_story, user_story: true);
                if (json_mode)
                    json_string = JsonConvert.SerializeObject(chronology);
                else
                {
                    json_string = JsonConvert.SerializeObject(chronology);
                }//end else

            }//end if
            //Make a story using a list of nodes, identified by node ID or node name.
            if (split_input[0].ToLower().Equals("make_story"))
            {
                Feature anchor_node = null;
                List<Feature> input_features = new List<Feature>();
                //Resolve the node IDs or names, and get the list of their corresponding features.
                foreach (string split_input_item in split_input)
                {
                    if (split_input_item.Equals("make_story"))
                        continue;

                    int input_id = -1;
                    bool parse_success = int.TryParse(split_input_item, out input_id);
                    if (parse_success)
                        input_features.Add(graph.getFeature(input_id));
                    else
                        input_features.Add(FindFeature(split_input_item));
                }//end foreach
                if (input_features.Count > 0)
                    anchor_node = input_features[0];
                if (anchor_node != null)
                {
                    //Assemble the story.
                    FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);

                    json_string = "";

                    NarrationManager temp_manager = new NarrationManager(temp_graph, temporalConstraintList);

                    Story result_story = temp_manager.MakeStoryFromList(input_features);
                    
                    stories.Add(result_story);

                    if (json_mode)
                        json_string = JsonConvert.SerializeObject(result_story);
                    else
                    {
                        json_string = JsonConvert.SerializeObject(result_story);
                    }//end else
                }//end if
            }//end if
            //List the anchor node of each story in the list of stories
            if (split_input[0].ToLower().Equals("list_stories"))
            {
                json_string = "Stories: ";
                int story_index = 0;
                foreach (Story list_story in stories)
                {
                    json_string += "Story[" + story_index + "]=" + graph.getFeature(list_story.StorySequence[0].anchor_node_id).Name + "\n ";
                    story_index += 1;
                }//end foreach
            }//end if
            //Read a story from the list of stories, by index.
            if (split_input[0].ToLower().Equals("read_story"))
            {
                json_string = "";
                int input_int = -1;
                bool parse_success = int.TryParse(split_input[1], out input_int);
                if (parse_success && stories.Count > input_int)
                {
                    FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);
                    Story to_read = stories[input_int];
                    SpeakTransform temp_transform = new SpeakTransform(temp_graph);
                    json_string = temp_transform.SpeakStorySegment(to_read.GetLastSegment());
                }//end if
                else
                    json_string = "No valid story at given index.";
            }//end if
            //Interweave two stories from the list of stories, by index.
            if (split_input[0].ToLower().Equals("interweave_stories"))
            {
                json_string = "";
                int index_1 = -1;
                int index_2 = -1;
                bool parse_success = int.TryParse(split_input[1], out index_1);
                if (parse_success)
                {
                    parse_success = int.TryParse(split_input[2], out index_2);
                }//end if
                if (parse_success)
                {
                    Story story_1 = stories[index_1];
                    Story story_2 = stories[index_2];

                    FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);
                    //Find the turn in the first storyline where the switch point should occur
                    NarrationManager temp_manager = new NarrationManager(temp_graph, temporalConstraintList);

                    Story interwoven_story = temp_manager.Interweave(story_1, story_2);

                    SpeakTransform t = new SpeakTransform(graph);
                    json_string = t.SpeakStory(interwoven_story);
                    stories.Add(interwoven_story);

                    if (json_mode)
                        json_string = JsonConvert.SerializeObject(interwoven_story);
                }//end if
                else
                    json_string = "No valid story at given indices.";
            }//end if
            if (split_input[0].ToLower().Equals("test_sequence"))
            {
                ParseInputJSON("make_story:13:552:576:531:551");
                ParseInputJSON("make_story:582:583:584:585:408");
                ParseInputJSON("interweave_stories:0:1");
                json_string = ParseInputJSON("read_last_story_no_acts");
            }//end if
            if (split_input[0].ToLower().Equals("read_last_story_no_acts"))
            {
                Story last_story = stories[stories.Count - 1];
                SpeakTransform temp_transform = new SpeakTransform(graph);
                json_string = temp_transform.SpeakStoryNoActs(last_story);
                if (json_mode)
                    json_string = JsonConvert.SerializeObject(last_story);
            }//end if
            //Load an XML by file name
            else if (split_input[0].ToLower().Equals("load_xml"))
            {
                string filename = "";
                if (split_input.Count() > 1)
                {
                    filename = split_input[1];
                    //load_xml = true;
                    //xml_to_load = filename;
                    return "load_xml:" + filename;
                }//end if
                else
                {
                    json_string = "No file specified.";
                }
            }//end else if
            else if (split_input[0].ToLower().Equals("analogy"))
            {
                int id_1 = -1;
                bool success = int.TryParse(split_input[1], out id_1);
                int id_2 = -1;
                success = int.TryParse(split_input[2], out id_2);

                json_string = MakeAnalogy(id_1, id_2);
            }//end else if
            //Toggle JSON response outputs on or off.
            else if (split_input[0].ToLower().Equals("toggle_json"))
            {
                if (json_mode)
                {
                    json_mode = false;
                    json_string = "JSON responses toggled off";
                }//end if
                else
                {
                    json_mode = true;
                    json_string = "JSON responses toggled on";
                }//end elses
            }//end else if
            //Toggle user interest mode on or off.
            else if (split_input[0].ToLower().Equals("toggle_user_interest"))
            {
                if (user_interest_mode)
                {
                    user_interest_mode = false;
                    json_string = "User interest mode toggled off";
                }//end if
                else
                {
                    user_interest_mode = true;
                    json_string = "User interest mode toggled on";
                }//end elses
            }//end else if
            //Reset both the main story and the feature graph.
            else if (split_input[0].ToLower().Equals("restart_narration"))
            {
                main_story = null;
                graph.ResetNodes();
                json_string = "narration restarted";
            }//end else if
            //GRAPH_INFO command.
            //List information about the knowledge graph.
            else if (split_input[0].ToLower().Equals("graph_info"))
            {
                //Count the total number of edges in the graph.
                //Count pairs of forward and backward edges as one.

                bool verbose = false;
                if (split_input.Count() > 1)
                    if (split_input[1].ToLower().Equals("verbose"))
                        verbose = true;

                //Nodes that have already been checked
                List<Feature> features_checked = new List<Feature>();
                //Relationships that have been seen
                List<string> relationships = new List<string>();
                int connection_count = 0;
                foreach (Feature feat_to_check in graph.Features)
                {
                    features_checked.Add(feat_to_check);
                    foreach (Tuple<Feature, double, string> temp_neighbor in feat_to_check.Neighbors)
                    {
                        //If this neighbor has already been checked, don't count the connection.
                        if (features_checked.Contains(temp_neighbor.Item1))
                        {
                            continue;
                        }//end if
                        //If the relationship has not been seen, add it to the list of relationships
                        if (!relationships.Contains(temp_neighbor.Item3))
                        {
                            relationships.Add(temp_neighbor.Item3);
                        }//end if
                        connection_count += 1;
                    }//end foreach
                }//end foreach

                json_string = "Number of edges: " + connection_count + ", unique relationships: " + relationships.Count;

                if (verbose)
                {
                    json_string += " Relationships: \n";

                    foreach (string relationship in relationships)
                    {
                        json_string += " (" + relationship + ") \n";
                    }//end foreach
                }//end if

                List<Feature> characters = new List<Feature>();
                List<Feature> locations = new List<Feature>();
                List<Feature> events = new List<Feature>();
                List<Feature> uncategorized = new List<Feature>();

                bool categorized = false;
                foreach (Feature temp_feature in graph.Features)
                {
                    categorized = false;
                    if (temp_feature.entity_type.Contains(Constant.CHARACTER))
                    {
                        characters.Add(temp_feature);
                        categorized = true;
                    }//end if
                    if (temp_feature.entity_type.Contains(Constant.LOCATION))
                    {
                        locations.Add(temp_feature);
                        categorized = true;
                    }//end if
                    if (temp_feature.entity_type.Contains(Constant.EVENT))
                    {
                        events.Add(temp_feature);
                        categorized = true;
                    }//end if
                    if (!categorized)
                        uncategorized.Add(temp_feature);
                }//end foreach

                int emperor_count = 0;
                foreach (Feature character in characters)
                {
                    if (character.HasEntityType("emperor"))
                        emperor_count += 1;
                }//end foreach
                json_string += " Characters (" + characters.Count + ") - emperors (" + emperor_count + "): \n";

                if (verbose)
                    foreach (Feature character in characters)
                    {
                        if (character.HasEntityType("emperor"))
                            json_string += "[emperor]";
                        json_string += " [C] " + character.Name + " \n";
                    }//end foreach

                int capital_count = 0;
                foreach (Feature location in locations)
                {
                    if (location.HasEntityType("capital"))
                        capital_count += 1;
                }//end foreach
                json_string += " Locations (" + locations.Count + ") - capitals (" + capital_count + "): \n";

                if (verbose)
                    foreach (Feature location in locations)
                    {
                        if (location.HasEntityType("capital"))
                            json_string += "[capital]";
                        json_string += " [L] " + location.Name + " \n";
                    }//end foreach

                int battle_count = 0;
                foreach (Feature temp_event in events)
                {
                    if (temp_event.HasEntityType("battle"))
                        battle_count += 1;
                }//end foreach
                json_string += " Events (" + events.Count + ") - battles (" + battle_count + "): \n";

                if (verbose)
                    foreach (Feature temp_event in events)
                    {
                        if (temp_event.HasEntityType("battle"))
                            json_string += "[battle]";
                        json_string += " [E] " + temp_event.Name + " \n";
                    }//end foreach

                json_string += " Uncategorized (" + uncategorized.Count + "): \n";

                if (verbose)
                    foreach (Feature temp_uncategorized in uncategorized)
                    {
                        json_string += " [U] " + temp_uncategorized.Name + " \n";
                    }//end foreach
            }//end else if
            //get_graph command
            else if (split_input[0].ToLower().Equals("get_graph"))
            {
                GraphLight temp_graph = new GraphLight(graph);
                if (json_mode)
                    json_string = JsonConvert.SerializeObject(temp_graph);
                else
                    json_string = JsonConvert.SerializeObject(temp_graph);
            }//end else if
            //set_anchors command.
            // Add a set of anchor nodes to the narration manager by either feature name or ID.
            else if (split_input[0].ToLower().Equals("set_anchors"))
            {
                Feature new_anchor_node = null;
                json_string = "Added anchor nodes: ";

                for (int i = 1; i < split_input.Length; i++)
                {
                    String string_topic = split_input[i];
                    //Try to convert the topic to an int to check if it's an id.
                    int int_topic = -1;
                    bool parse_success = int.TryParse(string_topic, out int_topic);
                    if (parse_success)
                    {
                        //Check that the new integer topic is a valid id.
                        new_anchor_node = graph.getFeature(int_topic);
                    }//end if
                    else
                    {
                        new_anchor_node = FindFeature(string_topic);
                    }//end else
                    if (new_anchor_node != null)
                    {
                        narration_manager.AddAnchorNode(new_anchor_node);
                        json_string += new_anchor_node.Name + " (" + new_anchor_node.Id + ")" + ", ";
                    }//end if

                }//end for

            }//end else if
            //LIST_ANCHORS command.
            //  Returns the list of anchor nodes, by name, to the chat window.
            else if (split_input[0].ToLower().Equals("list_anchors"))
            {
                json_string = "Anchor nodes: ";
                foreach (Feature anchor_node in narration_manager.anchor_nodes)
                {
                    json_string += anchor_node.Name += " (" + anchor_node.Id + "), ";
                }//end foreach
            }//end else if
            //analogical_story command
            else if (split_input[0].Equals("analogical_story"))
            {
                Feature anchor_node = null;
                //Get the anchor node specified in this command 
                if (split_input[1] != null)
                {
                    String string_topic = split_input[1];
                    //First, check if the topic is the empty string.
                    //If so, try the "default" anchor node.
                    if (string_topic.Equals(""))
                    {
                        //If there is not yet a story, get the root node as the anchor node.
                        if (main_story == null)
                        {
                            anchor_node = graph.Root;
                        }//end if
                        //If there is an ongoing story, get the next best topic based on the story.
                        else
                        {
                            anchor_node = narration_manager.getNextBestStoryTopic(main_story);
                        }//end else
                    }//end if
                    else
                    {
                        //Try to convert the topic to an int to check if it's an id.
                        int int_topic = -1;
                        bool parse_success = int.TryParse(string_topic, out int_topic);
                        if (parse_success)
                        {
                            //Check that the new integer topic is a valid id.
                            anchor_node = graph.getFeature(int_topic);
                        }//end if
                        else
                        {
                            anchor_node = FindFeature(string_topic);
                        }//end else
                    }//end else
                    if (anchor_node != null)
                    {
                        //If we found an anchor node with this command, assemble the chronology.

                        //Get the turn limit
                        int turn_limit = 0;
                        if (split_input[2] != null)
                        {
                            bool parse_success = int.TryParse(split_input[2], out turn_limit);
                            if (parse_success)
                            {
                                Console.Out.WriteLine("Turn limit set to " + turn_limit);
                            }//end if
                            else
                                Console.Out.WriteLine("Could not set turn limit.");
                        }//end if

                        //Make a temporary graph to create the chronology's order before presenting it.
                        FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);

                        json_string = "";

                        NarrationManager temp_manager = new NarrationManager(graph, temporalConstraintList);

                    }//end if

                }//end else if
            }//end if

            return json_string;
        }//end method ParseInputJSON

        private string MakeAnalogy(int id_1, int id_2)
        {
            string analogy_string = "";
            string feature_name_1 = graph.getFeature(id_1).Name;
            string feature_name_2 = graph.getFeature(id_2).Name;

            try
            {
                using (var client = new HttpClient())
                {
                    string url = "http://localhost:5000/get_analogy";
                    //string url_parameters = "?file=" + file_name;

                    client.BaseAddress = new Uri(url);
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    Dictionary<string, string> content = new Dictionary<string, string>
                    {
                        {"file1", graph.file_name},
                        {"file2", graph.file_name},
                        {"feature1", feature_name_1},
                        {"feature2", feature_name_2}
                    };

                    var http_content = new FormUrlEncodedContent(content);
                    HttpResponseMessage response = client.PostAsync(url, http_content).Result;

                    //Read the jsons tring from the http response
                    Task<string> read_string_task = response.Content.ReadAsStringAsync();
                    read_string_task.Wait(100000);

                    string content_string = read_string_task.Result;
                    analogy_string = content_string;
                }//end using
            }//end try
            catch (Exception e)
            {
                Console.WriteLine("Error contacting analogy server: " + e.Message);
            }//end catch

            return analogy_string;
        }//end function MakeAnalogy

        bool json_mode = true;
        bool user_interest_mode = false;
		//Form2 calls this function
		//input is the input to be parsed.
		//messageToServer indicates whether or not we are preparing a response to the front-end.
		//forLog indicates whether or not we are preparing a response for a log output.
		//outOfTopic indicates whether or not we are continuing out-of-topic handling.
		//projectAsTopic true means we use forward projection to choose the next node to traverse to based on
		//  how well the nodes in the n-length path from the current node relate to the current node.
		public string ParseInput(string input, bool messageToServer = false, bool forLog = false, bool outOfTopic = false, bool projectAsTopic = false)
		{
            return ParseInputJSON(input);

			string answer = IDK;
			string noveltyInfo = "";
			// Pre-processing

			//Console.WriteLine("parse input " + input);

			//The input may be delimited by colons. Try to split it.
			String[] split_input = input.Trim().Split(':');
			//Console.WriteLine("split input " + split_input[0]);

			// Lowercase for comparisons
			input = input.Trim().ToLower();
			//Console.WriteLine("trimmed lowered input " + input);

			//Check for an explicit command in the input.
			if (split_input.Length != 0 || messageToServer)
			{
				string command_result = CommandResponse(split_input);
				//Only stop and return the result of a command here if there
				//is a command result to return.
				if (!command_result.Equals(""))
					return command_result;
			}//end else if

			// Check to see if the AIML Bot has anything to say.
			if (!string.IsNullOrEmpty(input))
			{
				//Call the AIML Chat Bot in NarrationManager and give it the input ParseInput was given.
				string output = narration_manager.TellChatBot(input);
				
				//If the chatbot has a feedback response, it will begin its
				//response with "FORMAT"
				if (output.Length > 0)
				{
					//If the word "FORMAT" is not found, the response from the chatbot
					//is not a feedback response. Return it.
					if (!output.StartsWith(FORMAT))
						return output;
					
					//MessageBox.Show("Converted output reads: " + output);
					//Otherwise, remove the word FORMAT and continue with
					//the chatbot's output as the new input.
					input = output.Replace(FORMAT, "").ToLower();
				}//end if
			}//end if

			// Remove punctuation
			input = RemovePunctuation(input);

			// CASE: Nothing / Move on to next topic
			if (string.IsNullOrEmpty(input))
			{
				answer = narration_manager.NextTopicResponse();
			}//end if
			// CASE: Tell me more / Continue speaking
			else if (input.Contains("more") && input.Contains("tell"))
			{
				answer = narration_manager.TalkMoreAboutTopic();
			}//end else if
			// CASE: New topic/question
			//If the input was neither the empty string nor "Tell me more," assume it
			//is an entirely new topic/question that requires a Query
			else
			{
				//Construct a query using the input.
				Query query = BuildQuery(input);
				//Find what to say with it.
				answer = narration_manager.TalkFromQuery(query);
			}//end else

			//Gets the first noveltyAmount number of nodes with the highest novelty.
			noveltyInfo = narration_manager.ListMostNovelFeatures(narration_manager.Topic, noveltyAmount);
			//Gets the first noveltyAmount number of nodes with the highest score.
			string proximal_info = narration_manager.ListMostProximalFeatures(narration_manager.Topic, noveltyAmount);
			//Increment conversation turn
			narration_manager.Turn += 1;

			//At this point, we have either automatically moved on (blank input),
			//talked more about the current topic ("tell me more"),
			//or built and parsed a query out of the input.
			//answer holds the result of one of these three.

			//If there is no answer, then return the I Don't Know response
			if (answer.Length == 0)
			{
				return IDK;
			}//end if
			else
			{
				//If this was a message from the front-end to the back-end, send the
				//answer through additional formatting before returning it to the front-end.
				if (messageToServer)
				{
					//Return message to Unity front-end with both novel and proximal nodes
					return MessageToServer(narration_manager.Topic, answer, noveltyInfo, proximal_info, forLog, outOfTopic);
				}//end if

				if (outOfTopic)
					answer += ParseInput("", false, false);

				if (forLog)
					return answer;
				else
				{
					return answer;
				}//end else
			}//end else
		}//end if

		//PARSE INPUT UTILITY FUNCTIONS

		/// <summary>
		/// ParseInput utility function. Looks for an explicit command word in the given input and tries to carry
		/// out the command. Returns the result of the command if any valid command is found.
		/// </summary>
		/// <param name="split_input">A string of input split into an array by the character ":"</param>
		private string CommandResponse(string[] split_input)
		{
			string return_string = "";

				//LIST_COMMANDS command from query window
				//  Lists the commands that can be typed in to the query window
				if (split_input[0].Equals("LIST_COMMANDS"))
				{

				}//end if
				//Step-through command from Query window.
				// Calls ParseInput with the empty string several times, stepping
				// through with default responses.
				else if (split_input[0].Equals("STEP"))
				{
					//Step through the program with blank inputs a certain number of times, 
					//specified by the second argument in the command
					//Console.WriteLine("step_count " + split_input[1]);
					int step_count = int.Parse(split_input[1]);

					//Create a response by calling the ParseInput function step_count times.
					for (int s = 0; s < step_count; s++)
					{
						return_string += ParseInput("", true, true, false, false);
						return_string += "\n";
					}//end for
				}//end if
				// GET_NODE_VALUES command from Unity front-end
				// Uses NarrationCalculator to calculate the score between two nodes, specified
				// by the input. Returns individual components of that score.
				else if (split_input[0].Equals("GET_NODE_VALUES"))
				{
					Console.WriteLine("In get node values");
					//Get the node we wish to get a set of values for, by id.
					//"id" is represented by each node's data field in the XML.
					//In the split input string, index 1 is the id of the node we want
					//to get values for.
					//Index 2 is the id of the node we are getting values relative to.
					string current_node_id = split_input[1];
					string old_node_id = split_input[2];
					//Get the features for these two nodes
					Feature current_feature = this.graph.getFeature(current_node_id);
					Feature old_feature = this.graph.getFeature(old_node_id);
					//If EITHER feature is null, return an error message.
					if (current_feature == null || old_feature == null)
						return "no feature found";
					double[] return_node_values = narration_manager.GetScoreComponents(current_feature, old_feature);
					//Turn them into a colon-separated string, headed by
					//the key-phrase "RETURN_NODE_VALUES"
					return_string = return_node_values[Constant.ScoreArrayScoreIndex] + ":"
						+ return_node_values[Constant.ScoreArrayNoveltyIndex] + ":" 
						+ return_node_values[Constant.ScoreArrayDiscussedAmountIndex] + ":"
						+ return_node_values[Constant.ScoreArrayExpectedDramaticIndex] + ":" 
						+ return_node_values[Constant.ScoreArraySpatialIndex] + ":"
						+ return_node_values[Constant.ScoreArrayHierarchyIndex] + ":";
				}//end if
				// GET_WEIGHT command from Unity front-end
				// Returns the value of each weight from the feature graph
				else if (split_input[0].Equals("GET_WEIGHT"))
				{
					//Return a colon-separated string of every weight value
					return_string = "Weights: ";
					double[] weight_array = this.graph.getWeightArray();
					for (int i = 0; i < weight_array.Length; i++)
					{
						if (i != 0)
							return_string += ":";
						return_string += weight_array[i];
					}//end for
				}//end else if
				// SET_WEIGHT command from Unity front-end
				// Sets the value of each weight in the feature graph, specified
				// in the input array, then returns each weight.
				else if (split_input[0].Equals("SET_WEIGHT"))
				{
					//For each pair,
					//Index 1 is the index of the weight we wish to adjust.
					//Index 2 is the new weight value.
					for (int m = 1; m < split_input.Length; m += 2)
					{
						this.graph.setWeight(int.Parse(split_input[m]), double.Parse(split_input[m + 1]));
					}//end for

					//Return the new weight values right away.
					return_string = "Weights: ";
					double[] weight_array = this.graph.getWeightArray();
					for (int i = 0; i < weight_array.Length; i++)
					{
						if (i != 0)
							return_string += ":";
						return_string += weight_array[i];
					}//end for
				}//end else if
				//GET_RELATED command from Unity front-end.
				//Returns a message containing a list of most novel and most proximal nodes.
				else if (split_input[0].Equals("GET_RELATED"))
				{
					//GET_RELATED only gets related nodes for the current topic.
					string noveltyInfo = narration_manager.ListMostNovelFeatures(narration_manager.Topic, noveltyAmount);
					string proximalInfo = narration_manager.ListMostProximalFeatures(narration_manager.Topic, noveltyAmount);
					return_string = "Novelty:" + noveltyInfo + ":Proximal:" + proximalInfo;
				}//end else if
				//SET_LANGUAGE command from Unity front-end.
				//Sets which language text and TTS will be in, according to values
				// in the input array.
				else if (split_input[0].Equals("SET_LANGUAGE"))
				{
					//Index 1 is the new language display mode.
					language_mode_display = int.Parse(split_input[1]);
					//Index 2 is the new language TTS mode.
					language_mode_tts = int.Parse(split_input[2]);
					return_string = "Language to display set to " + language_mode_display + ": Language of TTS set to " + language_mode_tts;
				}//end else if
				//BEGIN_TTS command from Unity front-end.
				// Returns the string to be spoken by TTS that has been buffered, if any.
				// Also appends TTS_COMPLETE to signal TTS to start on the string, then
				// resets the TTS buffer.
				else if (split_input[0].Equals("BEGIN_TTS"))
				{
					if (buffered_tts.Equals(""))
					{
						return_string = "-1";
					}//end if
					else
					{
						return_string = "TTS_COMPLETE##" + buffered_tts;
						buffered_tts = "";
					}//end else
				}//end else if
				//GET_TTS command from Unity front-end.
				// Returns the buffered TTS string, if any, without
				// triggering TTS.
				else if (split_input[0].Equals("GET_TTS"))
				{
					if (buffered_tts.Equals(""))
					{
						return_string = "-1";
					}//end if
					else
					{
						return_string = buffered_tts;
					}//end else
				}//end else if
				//FORWARD_PROJECTION command.
				// Returns the names of the sequence of topics
				// found by Forward Projection.
				else if (split_input[0].Equals("FORWARD_PROJECTION"))
				{
					//The second index of the command is the number of turns to
					//perform forward projection with.
					List<Feature> result_list = narration_manager.ForwardProjection(narration_manager.Topic, int.Parse(split_input[1]));
					return_string = "Forward Projection result:";
					foreach (Feature temp_feature in result_list)
					{
						return_string = return_string + " --> " + temp_feature.Name;
					}//end foreach
				}//end else if
				//ADD_ANCHOR command.
				// Add a set of anchor nodes to the narration manager by either feature name or ID.
				else if (split_input[0].Equals("ADD_ANCHOR"))
				{
					Feature new_anchor_node = null;
					return_string = "Added anchor nodes: ";

					for (int i = 1; i < split_input.Length; i++)
					{
						String string_topic = split_input[i];
						//Try to convert the topic to an int to check if it's an id.
						int int_topic = -1;
						bool parse_success = int.TryParse(string_topic, out int_topic);
						if (parse_success)
						{
							//Check that the new integer topic is a valid id.
							new_anchor_node = graph.getFeature(int_topic);
						}//end if
						else
						{
							new_anchor_node = FindFeature(string_topic);
						}//end else
						if (new_anchor_node != null)
						{
							narration_manager.AddAnchorNode(new_anchor_node);
							return_string += new_anchor_node.Name + " (" + new_anchor_node.Id + ")" + ", ";
						}//end if

					}//end for

				}//end else if
				//LIST_ANCHORS command.
				//  Returns the list of anchor nodes, by name, to the chat window.
				else if (split_input[0].Equals("LIST_ANCHORS"))
				{
					return_string = "Anchor nodes: ";
					foreach (Feature anchor_node in narration_manager.anchor_nodes)
					{
						return_string += anchor_node.Name += " (" + anchor_node.Id + "), ";
					}//end foreach
				}//end else if
				//SET_TURN_LIMIT command.
				//  Sets the maximum number of turns the conversation can go for.
				else if (split_input[0].Equals("SET_TURN_LIMIT"))
				{
					return_string = "";

					int turn_limit = 0;
					bool parse_success = int.TryParse(split_input[1], out turn_limit);
					if (parse_success)
					{
						narration_manager.SetTurnLimit(turn_limit);
						return_string = "Turn limit set to " + turn_limit;
					}//end if
					else
						return_string = "Could not set turn limit.";
				}//end else if
				//CHRONOLOGY_PLANNED command.
				//  Generates a story based on a single anchor node.
				//  The story should be chronological, but can start at the anchor node.
				//  Generating a chronology leaves no changes in the feature graph.
				//  To change the feature graph, the UPDATE command should be called
				//  each time a chronology node is presented in the front-end.
				else if (split_input[0].Equals("CHRONOLOGY_PLANNED"))
				{
					return_string = "Chronology for: ";

					Feature anchor_node = null;
					//Get the anchor node specified in this command 
					if (split_input[1] != null)
					{
						String string_topic = split_input[1];
						//Try to convert the topic to an int to check if it's an id.
						int int_topic = -1;
						bool parse_success = int.TryParse(string_topic, out int_topic);
						if (parse_success)
						{
							//Check that the new integer topic is a valid id.
							anchor_node = graph.getFeature(int_topic);
						}//end if
						else
						{
							anchor_node = FindFeature(string_topic);
						}//end else
						if (anchor_node != null)
						{
							//If we found an anchor node with this command, assemble the chronology.

							//For certain story roles, relationships match up with start and end dates.
							//For characters, transfer start and end dates to birth and death places.
							if (anchor_node.story_role == 1)
							{
								//Look for a birth place by looking for the word "birth" in any neighbor relationship
								Feature birth_place = null;
								foreach (Tuple<Feature, double, string> neighbor_tuple in anchor_node.Neighbors)
								{
									if (neighbor_tuple.Item3.Contains("birth"))
									{
										birth_place = neighbor_tuple.Item1;
										break;
									}//end if
								}//end foreach
								//If the birth place is not null, update its date
								if (birth_place != null)
								{
									birth_place.start_date = anchor_node.start_date;
									birth_place.end_date = anchor_node.start_date;
								}//end if
								//Look for a death place by looking for the word "death" in any neighbor relationship
								Feature death_place = null;
								foreach (Tuple<Feature, double, string> neighbor_tuple in anchor_node.Neighbors)
								{
									if (neighbor_tuple.Item3.Contains("death"))
									{
										death_place = neighbor_tuple.Item1;
										break;
									}//end if
								}//end foreach
								//If the death place is not null, update its date
								if (death_place != null)
								{
									death_place.start_date = anchor_node.end_date;
									death_place.end_date = anchor_node.end_date;
								}//end if
							}//end if

							//Find the neighboring node whose date most closely matches the anchor node's start date.
							Feature closest_start_neighbor = null;
							TimeSpan closest_time_difference = TimeSpan.MaxValue;
							foreach (Tuple<Feature, double, string> neighbor_tuple in anchor_node.Neighbors)
							{
								TimeSpan time_difference = neighbor_tuple.Item1.start_date - anchor_node.start_date;
								TimeSpan time_difference_2 = neighbor_tuple.Item1.end_date - anchor_node.start_date;

								if (time_difference_2.Duration() < time_difference.Duration())
									time_difference = time_difference_2;

								if (time_difference.Duration() < closest_time_difference.Duration())
								{
									closest_start_neighbor = neighbor_tuple.Item1;
									closest_time_difference = time_difference;
								}//end if
							}//end foreach

							//Find the neighboring node whose date most closely matches the anchor node's end date.
							Feature closest_end_neighbor = null;
							closest_time_difference = TimeSpan.MaxValue;
							foreach (Tuple<Feature, double, string> neighbor_tuple in anchor_node.Neighbors)
							{
								TimeSpan time_difference = neighbor_tuple.Item1.end_date - anchor_node.end_date;
								TimeSpan time_difference_2 = neighbor_tuple.Item1.start_date - anchor_node.end_date;

								if (time_difference_2.Duration() < time_difference.Duration())
									time_difference = time_difference_2;

								if (time_difference.Duration() < closest_time_difference.Duration())
								{
									closest_end_neighbor = neighbor_tuple.Item1;
									closest_time_difference = time_difference;
								}//end if
							}//end foreach

							//Get the turn limit
							int turn_limit = 0;
							if (split_input[2] != null)
							{
								parse_success = int.TryParse(split_input[2], out turn_limit);
								if (parse_success)
								{
									Console.Out.WriteLine("Turn limit set to " + turn_limit);
								}//end if
								else
									Console.Out.WriteLine("Could not set turn limit.");
							}//end if

							//Make a temporary graph to create the chronology's order before presenting it.
							FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(graph);

							//Calculate the effective date of every other feature relative to the anchor node.
							foreach (Feature temp_feat in temp_graph.Features)
							{
								temp_feat.calculateEffectiveDate(anchor_node.start_date, anchor_node.end_date);
							}//end foreach

							return_string = "";

							//Turns should be spent talking about nodes between the start and end dates of the anchor node.
							NarrationManager temp_manager = new NarrationManager(temp_graph, temporalConstraintList);

							//The manager's history will be used as the order of narration.
							//Talk about the anchor node first.
							temp_manager.TopicHistory.Add(anchor_node);
							//Talk about the start neighbor second.
							temp_manager.TopicHistory.Add(closest_start_neighbor);
							//Plan for the remaining turns.
							temp_manager.Turn = 2;


							Feature last_feature = closest_start_neighbor;
							Feature current_feature = closest_start_neighbor;

							//Take up the rest of the turns talking about items in between the start and end dates.
							while (temp_manager.Turn < turn_limit)
							{
								//Determine the next feature from the previous one
								//current_feature = temp_manager.getNextChronologicalTopic(last_feature, anchor_node.start_date, anchor_node.end_date);
								//Determine the next feature from the anchor node every time
								current_feature = temp_manager.getNextChronologicalTopic(anchor_node, anchor_node.start_date, anchor_node.end_date);
								//Present it
								return_string += temp_manager.PresentFeature(current_feature);
								//Update last feature
								last_feature = current_feature;
							}//end while
							//When we have reached the turn limit, present the ending node.

							return_string += temp_manager.PresentFeature(closest_end_neighbor);
							//return_string += anchor_node.Name + " (" + anchor_node.Id + ")" + ", ";

							//7/6/2016: For integration, send back a double-colon delineated list of node names consisting of the nodes given here.
							List<Feature> chronology = temp_manager.TopicHistory;
							if (split_input.Length < 4 || split_input[3] == null) {
								return_string = "";
								foreach (Feature feat in chronology)
								{
									return_string += feat.Name + "::";
								}//end foreach
							}//end if
						}//end if
					}//end if
				}//end else if
				//CHRONOLOGY command. Uses greedy method and does not do planning for optimal path.
				//  Generates a story based on a single anchor node.
				//  The story should be chronological, but can start at the anchor node.
				//  Generating a chronology leaves no changes in the feature graph.
				//  To change the feature graph, the UPDATE command should be called
				//  each time a chronology node is presented in the front-end.
				else if (split_input[0].Equals("CHRONOLOGY"))
				{
                    return ParseInputJSON(split_input);
				}//end else if
				//UPDATE command.
				//  Update the feature graph by telling the narration manager
				//  to blankly present a node. Should be called each time a node
				//  is presented in the front end from a chronology.
				else if (split_input[0].Equals("UPDATE"))
				{
					return_string = "";

					if (split_input[1] != null)
					{
						int node_id = -1;
						bool parse_success = int.TryParse(split_input[1], out node_id);
						if (parse_success)
						{
							string result = narration_manager.PresentFeature(graph.getFeature(node_id));
							Console.Out.WriteLine("Updating: " + result);
						}//end if
						else
							Console.Out.WriteLine("No valid node given to update");
					}//end if
				}//end else if
				//START_NARRATION command.
				//  Makes the system narrate. A turn limit may be specified after the command.
				//  It tries to visit all anchor nodes within the turn limit. 
				else if (split_input[0].Equals("START_NARRATION"))
				{
					return_string = "";

					if (split_input[1] != null)
					{
						int turn_limit = 0;
						bool parse_success = int.TryParse(split_input[1], out turn_limit);
						if (parse_success)
						{
							narration_manager.SetTurnLimit(turn_limit);
							Console.Out.WriteLine("Turn limit set to " + turn_limit);
						}//end if
						else
							Console.Out.WriteLine("Could not set turn limit.");
					}//end if

					//Check if the narration manager has any anchor nodes.
					if (narration_manager.anchor_nodes.Count <= 0)
					{
						//If not, initialize some default anchor nodes.
						string temp_input = "";
						temp_input = "ADD_ANCHOR:78:1:117:115";
						ParseInput(temp_input);
					}//end if

					bool start_success = narration_manager.StartNarration();
					if (start_success)
						return_string = "Narration started.";
					else
						return_string = "Failed to start narration.";
				}//end else if

				//INTERWEAVE command.
				// Creates two interwoven storylines.
				else if (split_input[0].Equals("INTERWEAVE"))
				{
					List<String> return_string_1_components = new List<String>();
					List<String> return_string_2_components = new List<String>();
					string return_string_1 = "";
					string return_string_2 = "";

					int storyline_length = 10;
					//1 Arctic exploration, 20 Desert exploration
					int story_1_root = 200;
					int story_2_root = 1;

					//Create the first story in its entirety
					//Set the node that the story will start at
					graph.Root = graph.getFeature(story_1_root);
					NarrationManager manager_1 = new NarrationManager(graph, temporalConstraintList);
					for (int i = 0; i < storyline_length; i++)
					{
						return_string_1_components.Add(" " + manager_1.DefaultNextTopicResponse() + "\n");
						manager_1.Turn += 1;
					}//end for
					//Get the topic history from the first narration as the reference list for the second narration.
					List<Feature> storyline_1 = manager_1.TopicHistory;
					//Remove 1st node, it is a duplicate.
					storyline_1.RemoveAt(0);

					//Ask the manager for the first narration which node would be best as a switch point.
					Feature switch_point = manager_1.IdentifySwitchPoint(storyline_1);


					//Create the reference list from the first storyline up through the switch point.
					List<Feature> reference_list = new List<Feature>();
					foreach (Feature story_feature in storyline_1)
					{
						reference_list.Add(story_feature);
						if (story_feature.Id.Equals(switch_point.Id))
							break;
					}//end foreach

					//Create the second story in its entirety
					//Set the node that the story will start at
					graph.Root = graph.getFeature(story_2_root);
					NarrationManager manager_2 = new NarrationManager(graph, temporalConstraintList);
					for (int i = 0; i < storyline_length; i++)
					{
						return_string_2_components.Add(" " + manager_2.DefaultNextTopicResponse(reference_list) + "\n");
						manager_2.Turn += 1;
					}//end for
					List<Feature> storyline_2 = manager_2.TopicHistory;
					//Remove 1st node, it is a duplicate
					storyline_2.RemoveAt(0);

					bool after_switch_point = false;
					//Compile both return strings from their components
					int switch_point_index = storyline_1.IndexOf(switch_point);
					//Get the part of storyline 1 up to the switch point
					List<Feature> storyline_1_first_half = storyline_1.GetRange(0, switch_point_index + 1);
					//Get part of storyline 1 after the switch point
					List<Feature> storyline_1_second_half = storyline_1.GetRange(switch_point_index + 1, storyline_1.Count - switch_point_index - 1);
					foreach (string component_1 in return_string_1_components)
					{
						//At the switch point, add all of the second storyline.
						int component_index = return_string_1_components.IndexOf(component_1);
						if (component_index == switch_point_index)
						{
							//Foreshadow future switch point information.
							foreach (Feature first_half_node in storyline_1_first_half)
							{
								return_string += " " + manager_1.Foreshadow(first_half_node, storyline_1_second_half) + "\n";
							}//end foreach
							//return_string += " " + manager_1.Foreshadow(switch_point, storyline_1_second_half) + "\n";

							return_string += " SWITCH TO STORYLINE 2 \n {But now, let's talk about something else.}";
							foreach (string component_2 in return_string_2_components)
							{
								return_string += component_2;
							}//end foreach
							return_string += " SWITCH TO STORYLINE 1 \n";
							after_switch_point = true;
						}//end if
						return_string += component_1;
						if (after_switch_point && component_index < storyline_1.Count)
						{
							//List<Feature> temp_list = new List<Feature>();
							//temp_list.Add(switch_point);
							return_string += " " + manager_1.TieBack(storyline_1.ElementAt(component_index), storyline_1_first_half, storyline_1.ElementAt(component_index - 1)) + "\n";
						}//end if
					}//end foreach

					return_string += " : switch point: " + switch_point.Name + " \n";
				}//end else if
				else if (split_input[0].Equals("COUNT_CONNECTIONS"))
				{
					//Count the total number of edges in the graph.
					//Count pairs of forward and backward edges as one.

					//Nodes that have already been checked
					List<Feature> features_checked = new List<Feature>();
					//Relationships that have been seen
					List<string> relationships = new List<string>();
					int connection_count = 0;
					foreach (Feature feat_to_check in graph.Features)
					{
						features_checked.Add(feat_to_check);
						foreach (Tuple<Feature, double, string> temp_neighbor in feat_to_check.Neighbors)
						{
							//If this neighbor has already been checked, don't count the connection.
							if (features_checked.Contains(temp_neighbor.Item1))
							{
								continue;
							}//end if
							//If the relationship has not been seen, add it to the list of relationships
							if (!relationships.Contains(temp_neighbor.Item3))
							{
								relationships.Add(temp_neighbor.Item3);
							}//end if
							connection_count += 1;
						}//end foreach
					}//end foreach

					return_string = "Number of connections: " + connection_count + ", unique relationships: " + relationships.Count;
				}//end else if
				else if (split_input[0].Equals("FIND_WELL_CONNECTED_ROOTS"))
				{
					FeatureGraph original_feature_graph = graph;

					//Root node, root node id, switch point, switch point id.
					List<Tuple<Feature, int, Feature, int>> interesting_roots = new List<Tuple<Feature, int, Feature, int>>();
					foreach (Feature feat_to_check in original_feature_graph.Features)
					{
						//Make a deep copy of the original feature graph so we can make changes
						//without changing the original.
						FeatureGraph temp_graph = DeepClone.DeepCopy<FeatureGraph>(original_feature_graph);
						NarrationManager temp_manager = new NarrationManager(temp_graph, temporalConstraintList);

						//Create the first story in its entirety
						//Set the node that the story will start at
						graph.Root = graph.getFeature(feat_to_check.Id);
						NarrationManager manager_1 = new NarrationManager(graph, temporalConstraintList);
						for (int i = 0; i < 10; i++)
						{
							manager_1.DefaultNextTopicResponse();
							manager_1.Turn += 1;
						}//end for
						//Get the topic history from the first narration as the refernce list for the second narration.
						List<Feature> storyline_1 = manager_1.TopicHistory;
						//Remove 1st node, it is a duplicate.
						storyline_1.RemoveAt(0);

						//Ask the manager for the first narration which node would be best as a switch point.
						Feature switch_point = manager_1.IdentifySwitchPoint(storyline_1);

						//If switch point is neither the first nor last nodes, mark this as an interesting root.
						if (!(switch_point.Id == storyline_1[0].Id) && !(switch_point.Id == storyline_1[storyline_1.Count - 1].Id))
						{
							interesting_roots.Add(new Tuple<Feature, int, Feature, int>(feat_to_check, feat_to_check.Id, switch_point, switch_point.Id));
						}//end if
					}//end foreach

					//return_string = "Number of connections: " + connection_count + ", unique relationships: " + relationships.Count;
				}//end else if
				else if (split_input[0].Equals("VALID_TIME_GEO"))
				{
					List<Feature> valid_nodes = new List<Feature>();
					return_string = "Valid next nodes: ";

					String string_topic = split_input[1];

					Feature source_node = FindFeature(string_topic);

					foreach (Tuple<Feature, double, string> neighbor in source_node.Neighbors)
					{
						if (neighbor.Item1.Timedata.Count > 0 && neighbor.Item1.Geodata.Count > 0)
							valid_nodes.Add(neighbor.Item1);
					}//end foreach
					foreach (Feature valid_node in valid_nodes)
					{
						return_string += source_node.getRelationshipNeighbor(valid_node.Name) + " " + valid_node.Name + " (" + valid_node.Id + ") " + " : ";
					}//end foreach
				}//end else if
				else if (split_input[0].Equals("RESPONSE"))
				{

				}//end else if
                //Toggle JSON response outputs on or off.
                else if (split_input[0].Equals("TOGGLE_JSON"))
                {
                    if (json_mode)
                    {
                        json_mode = false;
                        return_string = "JSON responses toggled off";
                    }//end if
                    else
                    {
                        json_mode = true;
                        return_string = "JSON responses toggled on";
                    }//end elses
                }//end else if

			return return_string;
		}//end function CommandResponse

		//END OF PARSE INPUT UTILITY FUNCTIONS

		/// <summary>
		/// Convert a regular string to a Query object,
		/// identifying the MainTopic and any question and direction words
		/// </summary>
		/// <param name="input">A string of input, asking about a topic</param>
		/// <returns>A Query object that can be passed to ParseQuery for output.</returns>
		public Query BuildQuery(string input)
		{
			//DEBUG
			Console.Out.WriteLine("Building query from: " + input);
			//END DEBUG

			string mainTopic;
			Question? questionType = null;
			Direction? directionType = null;
			string directionWord = "";

			// Find the main topic!
			Feature f = FindFeature(input);
			if (f == null)
			{
				//MessageBox.Show("FindFeature returned null for input: " + input);
				return null;
			}

			this.iterations++;

			//update interest profile based on analogy

			Console.Out.WriteLine("Start handling interest");
			this.graph.update_interest_analogy(f.Id, this.iterations);
			
			//narration_manager.Topic = f;
			mainTopic = f.Name;
			if (string.IsNullOrEmpty(mainTopic))
			{
				//MessageBox.Show("mainTopic IsNullOrEmpty");
				return null;
			}

			//DEBUG
			Console.Out.WriteLine("Topic of query: " + mainTopic);
			//END DEBUG

			// Is the input a question?
			if (input.Contains("where"))
			{
				//DEBUG
				Console.Out.WriteLine("Where question");
				//END DEBUG
				questionType = Question.WHERE;
				//if (input.Contains("was_hosted_at"))
				//{
				//    directionType = Direction.WAS_HOSTED_AT;
				//}
			}
			else if (input.Contains("when"))
			{
				questionType = Question.WHEN;
			}
			else if (input.Contains("what") || input.Contains("?"))
			{
				//DEBUG
				Console.Out.WriteLine("What question");
				//END DEBUG
				questionType = Question.WHAT;

				// Check for direction words
				//if (input.Contains("direction"))
				//{
					foreach (string direction in directionWords)
					{
						// Ideally only one direction is specified
						if (input.Contains(direction))
						{
							directionType = (Direction)Enum.Parse(typeof(Direction), direction, true);
							directionWord = direction;
							// Don't break. If "northwest" is asked, "north" will match first
							// but then get replaced by "northwest" (and so on).
						}//end if
					}//end foreach

					//DEBUG
				if (directionType != null)
					Console.Out.WriteLine("Input contained direction: " + directionType.ToString());
					//END DEBUG

				//}//end if
			}//end else if
			else
			{
				int t = input.IndexOf("tell"), m = input.IndexOf("me"), a = input.IndexOf("about");
				if (0 <= t && t < m && m < a)
				{
					// "Tell me about" in that order, with any words or so in between
					// TODO:  Anything?  Should just talk about the topic, then.
				}//end if
			}//end else
			return new Query(f, questionType, directionType, directionWord);
		}//end function BuildQuery

		private string PadPunctuation(string s)
		{
			foreach (string p in punctuation)
			{
				s = s.Replace(p, " " + p);
			}//end foreach
			return s;
		}//end function PadPunctuation
		private string RemovePunctuation(string s)
		{
			foreach (string p in punctuation)
			{
				s = s.Replace(p, "");
			}
			string[] s0 = s.Split(' ');
			return string.Join(" ", s0);
		}//end function RemovePunctuation

		//Identifies the feature in the given input
		/// <summary>
		/// Takes a string and identifies which
		/// feature, if any, appears in it. Returns the feature.
		/// </summary>
		/// <param name="input">A string for the function to look for a feature in.</param>
		private Feature FindFeature(string input)
		{
			Feature target = null;
			int targetLen = 0;
			input = input.ToLower();
			foreach (string item in this.features)
			{
				string parse_item = item;
				parse_item = parse_item.Split(new string[] { "##" }, StringSplitOptions.None)[0];
				if (input.Contains(RemovePunctuation(parse_item.ToLower())))
				{
					if (parse_item.Length > targetLen)
					{
						target = this.graph.getFeature(item);
						targetLen = target.Name.Length;
					}
				}
				/*
				// original
				if (input.Contains(RemovePunctuation(item.ToLower())))
				{
					if (item.Length > targetLen)
					{
						target = this.graph.getFeature(item);
						targetLen = target.Id.Length;
					}
				}
				*/
			}
			//If the target is still null, check for 'that' or 'this'
			if (input.Contains("this") || input.Contains("that") || input.Contains("it") || input.Contains("something"))
				target = narration_manager.Topic;

			return target;
		}//end function FindFeature

		//Parses a bilingual output based on the language_mode passed in
		public string ParseOutput(string to_parse, int language_mode)
		{
			string answer = "";
			string[] answers = to_parse.Split(new string[] { "##" }, StringSplitOptions.None);

			for (int i = 0; i < answers.Length; i++)
			{
				if (language_mode == Constant.EnglishMode && i % 2 == 0)
				{
					answer += answers[i];
				}
				if (language_mode == Constant.ChineseMode && i % 2 == 1)
				{
					answer += answers[i];
				}
			}
			return answer;
		}

		private string[] FindSpeak(Feature feature)
		{
			return feature.Speaks.ToArray();
		}//end function FindSpeak

        // ZEV: LETS US PICK A CONSTRAINT LIST BASED ON THE FIRST NODE
        // WHEN THE FIRST STORY IS MADE.
        // Try to load a constraint list with the given id as the first item in the list.
        private void LoadConstraintList(int first_item_id)
        {
            // Go through each of the constraint lists named in the list of constraint files.
            // Check each file, one at a time.
            foreach (string constraint_list_filename in constraint_list_filenames)
            {
                // Read the constraint list.
                System.IO.StreamReader file = new System.IO.StreamReader(constraint_list_filename);
                string file_line = "";
                // The constraint list consists of constraints; tuples of source node id, operator, target node id.
                target_constraint_list = new List<Tuple<int, string, int>>();
                bool first_line = true;
                bool abandon_file = false;
                while ((file_line = file.ReadLine()) != null)
                {
                    // Separate this line by spaces.
                    string[] separated_file_line = file_line.Split(' ');
                    int current_index = 0;
                    string current_item = separated_file_line[0];
                    // Check if the first item is an integer.
                    int node_id = -1;
                    bool parse_success = false;
                    parse_success = int.TryParse(current_item, out node_id);
                    // If it is an integer, then this is a node id.
                    if (parse_success)
                    {
                        // Check if this is the first line of this file that we are reading.
                        if (first_line)
                        {
                            first_line = false;
                            // If so, then compare it to the first item id passed in.
                            // If they match, then we're free to continue with filling the constraints in the graph
                            // using this file.
                            // If they do not match, check the next file.
                            if (!node_id.Equals(first_item_id))
                                abandon_file = true;
                            else
                                target_id_list = new List<int>();
                        }//end if
                        if (abandon_file)
                            break;
                        // We need to find out if there is an operator following this node.
                        // Peak at the next two items in the line.
                        if (current_index + 1 < separated_file_line.Length)
                        {
                            // There is at least an operator following the current node.
                            string next_item = separated_file_line[current_index + 1];

                            // Check if there is another node ID following the operator.
                            if (current_index + 2 < separated_file_line.Length)
                            {
                                int next_node_id = -1;
                                string next_next_item = separated_file_line[current_index + 2];
                                parse_success = int.TryParse(next_next_item, out next_node_id);

                                // Two items down is a node id.
                                if (parse_success)
                                {
                                    // Since we have a node id, an operator, and a node id, add it
                                    // to the list of constraints.
                                    target_constraint_list.Add(new Tuple<int, string, int>(node_id, next_item, next_node_id));
                                }//end if
                            }//end if
                        }//end if
                        // If there is no next item, then this node id only item in the line
                        else
                        {
                            // Add it to the target id list.
                            target_id_list.Add(node_id);
                        }//end else
                    }//end if
                }//end while
                file.Close();
                if (abandon_file)
                    continue;

                //Initialize the dialogue manager
                //narration_manager = new NarrationManager(this.graph, myTemporalConstraintList);
                // Make sure the constraints are added to each feature in the feature graph.

                // Remove all existing constraints from nodes in the feature graph.
                this.graph.RemoveAllConstraints();

                // If the node appears in the target id list, add a constraint with the node id as the 
                // source, the empty string as the operand, and -1 as the target.
                foreach (int node_id in target_id_list)
                {
                    this.graph.getFeature(node_id).constraints.Add(new Tuple<int, string, int>(node_id, "", -1));
                }//end foreach
                // All other constraints should be added as-is, for both the source and target nodes
                foreach (Tuple<int, string, int> constraint in target_constraint_list)
                {
                    if (this.graph.hasNode(constraint.Item1))
                        this.graph.getFeature(constraint.Item1).constraints.Add(constraint);
                    if (this.graph.hasNode(constraint.Item3))
                        this.graph.getFeature(constraint.Item3).constraints.Add(constraint);
                }//end foreach

                narration_manager = new NarrationManager(this.graph, target_id_list, target_constraint_list);
            }//end foreach

        }//end method LoadConstraintList

	}//end class QueryHandler

	static class ExtensionMethods
	{
		public static Direction Invert(this Direction d)
		{
			return (Direction)(-(int)d);
		}

		public static string ToUpperFirst(this string s)
		{
			return s.Substring(0, 1).ToUpper() + s.Substring(1);
		}

		public static string JoinAnd(this List<string> items)
		{
			switch (items.Count())
			{
				case 0:
					return "";
				case 1:
					return items.ElementAt(0);
				case 2:
					return items.ElementAt(0) + " and " + items.ElementAt(1);
				default:
					return string.Join(", ", items.GetRange(0, items.Count - 1))
						+ ", and " + items[items.Count - 1];
			}
		}
	}
}
