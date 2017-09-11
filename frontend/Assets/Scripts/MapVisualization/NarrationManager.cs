using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using JsonConstructs;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine.UI;





[RequireComponent(typeof(LoadXML))]
public class NarrationManager : MonoBehaviour
{
    public Camera main_camera;
    public Text journaltext;
    private LoadXML lxml;
    private UnityAction<string> narrationNodeSelectListener;
    private UnityAction<string> labelCollisionCheckListener;
    private UnityAction<string> listener;
    public List<Vector3> pastNarrationNodeTransforms;
    //TODO: Part of hack for demo. Remove later?
    public List<timelineNode> pastNarrationNodes;

    // The search textbox
    public InputField search_field;

    private IEnumerator current_narration;
    private bool user_can_take_turn;
    private bool narration_reset;
    private bool loaded_xml_backend;

    public static bool progressNarrationSwitch;
    public static bool firstPassNarration;

    private static bool first_flag;//set to false after first node has been expanded

    public TimeLineBar tlb;

    public timelineNode current_node;

    public Dictionary<int, AudioClip> audio_clips_by_id;

    public AudioClip clip_17;
    public AudioClip clip_22;
    public AudioClip clip_41;

    public GameObject ui1;
    public List<timelineNode> allNodes;
    private TextToSpeechWatson TTS;
    // public GameObject ui2;
    private NodeData nd;
    private NodeData nd1;
    
    //delegate void mydelegate(string data);
    //mydelegate listener;
    private string input_text;
    //public NarrationJournal a;
    void Awake()
    {
        foreach (timelineNode i in allNodes)
        {
            print(i.node_name.ToLower());
        }

        tlb = GameObject.Find("TimeLine").GetComponent<TimeLineBar>();
        progressNarrationSwitch = false;
        listener = delegate (string data)
        {
            
            nd1 = JsonUtility.FromJson<NodeData>(data);
            
            //print("NodeData text: " + nd.text);

        };
        narrationNodeSelectListener = delegate (string data) {
            if (user_can_take_turn)
            {
                
                nd = JsonUtility.FromJson<NodeData>(data);
             
                user_can_take_turn = false;
                Narrate(nd.id, 9);
            }
            else
            {
                nd = JsonUtility.FromJson<NodeData>(data);

                // Call narrative with turns = 0 to request that the given
                // node be added to the existing story.
                Narrate(nd.id, 0);
            }
        };

        labelCollisionCheckListener = delegate (string data) {
            labelCollisionCheckCount = 0;
        };

        lxml = GetComponent<LoadXML>();
    }

    IEnumerator Start()
    {
        previous_node_id = -1;
        labelCollisionCheckMax = 3;
        labelCollisionCheckCount = 0;
        user_can_take_turn = true;
        narration_reset = false;
        loaded_xml_backend = false;

        progressNarrationSwitch = false;
        firstPassNarration = true;
        first_flag = true;//set to false after first node has been expanded

        pastNarrationNodeTransforms = new List<Vector3>();
        //TODO: Part of hack for demo. Remove later?
        pastNarrationNodes = new List<timelineNode>();

        DebugMode.startTimer("NarrationManager.Start()");

        lxml.Initialize();

        print("NarrationManager.Reset_Narration() :: started");

        Reset_Narration();
        while (!narration_reset)
        {
            yield return null;
        }
        narration_reset = false;

        print("NarrationManager.Reset_Narration() :: ended");
        print("NarrationManager.Load_XML() :: started");

        Load_XML();
        while (!loaded_xml_backend)
        {
            yield return null;
        }
        loaded_xml_backend = false;

        print("NarrationManager.Load_XML() :: ended");
        //EventManager.StartListening(EventManager.EventType.NARRATION_MACHINE_TURN, narrationNodeSelectListener);
        EventManager.StartListening(EventManager.EventType.NARRATION_MACHINE_TURN, listener);
        EventManager.StartListening(EventManager.EventType.INTERFACE_NODE_SELECT, narrationNodeSelectListener);
        EventManager.StartListening(EventManager.EventType.LABEL_COLLISION_CHECK, labelCollisionCheckListener);
        EventManager.StartListening(EventManager.EventType.INTERFACE_ZOOM_OUT, labelCollisionCheckListener);
        EventManager.StartListening(EventManager.EventType.INTERFACE_ZOOM_IN, labelCollisionCheckListener);

        //start on diocletian
        user_can_take_turn = false;

        //Bring each node out of focus.
        foreach (timelineNode tn in lxml.nodeList)
        {
            //Change its color
            //temp.GetComponent<SpriteRenderer>().color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            //Set it to not display information on mouseover
            //temp.GetComponent<timelineNode>().display_info = false;
            tn.Unfocus();
        }//end foreach

        // TODO: make this selection UI-driven
        // for now: comment out all lines exept the one you'd like to have execute
        Scene scene = SceneManager.GetActiveScene();
        Debug.Log("NarrationManager.Start() :: Active scene is '" + scene.name + "'.");
        switch (scene.name)
        {
            case "timelineTest4_RomanEmpire":
                Narrate(17, 9); // "Roman Empire: Diocletian (node 13)" narration start
                break;
            case "timelineTest4_WWII":
                Narrate(17, 9); // "WWII: American Theater (node 17)" narration start
                //Narrate(495, 9); // "WWII: Linden Cameron (node 495)" narration start
                break;
            case "timelineTest4_Roman_WWII_Analogy":
                //Narrate(1, 2); // "Roman_WWII_Analogy: WW II (node 1)" narration start
                //Narrate(44, 102); // "Roman_WWII_Analogy: Battle of Actium (node 44) :: Mediterranean and Middle East theatre of World War II (node 102)" narration start
                //Narrate(129, 33); // "Roman_WWII_Analogy: Adolf Hitler (node 129) :: Augustus (node 33)" narration start
                Narrate(1, 216); // "Roman_WWII_Analogy: WW II (node 1) :: Charles Crombie (node 216)" narration start
                break;
            // The scene for the first RPI history story
            case "rpi_history":
                Debug.Log("NarrationManager.Start() :: rpi_history scene, Narrate(22, 9)");
                Narrate(22, 9);
                break;
            // The scene for the second RPI history story
            case "rpi_history_2":
                Debug.Log("NarrationManager.Start() :: rpi_history scene, Narrate(45, 9)");
                Narrate(45, 9);
                break;
            default:
                Debug.Log("NarrationManager.Start() :: unhandled scene name");
                break;
        }

        DebugMode.stopTimer("NarrationManager.Start()");

    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            if (!user_can_take_turn)
            {
                progressNarration();
            }

        }

        // handle Data Loading keypresses
        bool shiftDown = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        // SHIFT + D + L
        bool loadDataSet = (shiftDown && Input.GetKey(KeyCode.D) && Input.GetKey(KeyCode.L));
        // map keys 1, 2, and 3 to level indexes 0, 1, and 2
        int levelID = Input.GetKey(KeyCode.Alpha1) ? 0 : Input.GetKey(KeyCode.Alpha2) ? 1 : Input.GetKey(KeyCode.Alpha3) ? 2 : -1;
        if (loadDataSet && levelID >= 0)
        {
            //TODO: could this possibly be spammed? Should we figure out a way to only do this once every x seconds?
            first_flag = true;
            SceneManager.LoadScene(levelID);
        }

        labelCollisionCheck();
    }

    private int labelCollisionCheckCount;
    public int labelCollisionCheckMax;
    void labelCollisionCheck()
    {
        if (labelCollisionCheckCount < 0) return;

        if (labelCollisionCheckCount < labelCollisionCheckMax)
        {
            CameraController.CollisionDetection();
            labelCollisionCheckCount += 1;
        }
        else if (labelCollisionCheckCount < labelCollisionCheckMax * 2)
        {
            labelCollisionCheckCount += 1;
        }
        else
        {
            CameraController.CollisionDetection();
            labelCollisionCheckCount = labelCollisionCheckMax;
        }
    }


    public void Reset_Narration()
    {
        //resets narration history
        StartCoroutine(_Reset_Narration());
    }

    public void Load_XML()
    {
        //loads a new file
        StartCoroutine(_Load_XML());
    }

    //Call this to progress the story turn
    public void progressNarration()
    {
        progressNarrationSwitch = true;
        Debug.Log("progressing narration from event manager");
    }

    IEnumerator _Load_XML(string url = "")
    {
        if (url == "")
        {
            //url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/load_xml:" + lxml.xml_location;
            url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/load_xml";
        }

        print("NarrationManager._Load_XML(), xml location: " + lxml.xml_location);

        string data = JsonUtility.ToJson(new LoadXMLRequest(lxml.xml_location));

        WWW www = new WWW(url, Encoding.UTF8.GetBytes(data));

        print("NarrationManager._Load_XML() :: url = " + url);
        print("                               data = " + data);

        yield return new WaitForSeconds(5);
        print("NarrationManager._Load_XML(), done waiting for backend.");
        loaded_xml_backend = true;

        //Now that the XML is done loading in the backend, categorize nodes.
        lxml.CategorizeNodes();

        //yield return www;
        //if (www.error == null)
        //{
        //    print("LOADED NEW XML IN BACKEND");
        //    print(url);
        //    loaded_xml_backend = true;
        //}
    }

    IEnumerator _Reset_Narration()
    {
        string url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/chronology/reset";

        print("NarrationManager._Reset_Narration() :: url =  " + url);

        WWW www = new WWW(url);
        yield return www;
        if (www.error == null)
        {
            print("NARRATION RESET");
            narration_reset = true;
        }
    }

    public void Narrate(int node_id, int turns)
    {
        //narrate about node for N turns
        print("Narrate attempt");
        if (current_narration != null)
        {
            StopCoroutine(current_narration);
        }
        current_narration = _Narrate(node_id, turns);
        StartCoroutine(current_narration);
    }

    // Searches for a node to select based on the text in the search_field.
    public void SearchForNode()
    {
        string text_to_search = search_field.text;
        print ("Searching for " + text_to_search + ". allNodes size: " + allNodes.Count);
        // Pad the text to search. This will let us search for single words without tokenization.
        text_to_search = " " + text_to_search + " ";
        int node_id_found = -1;
        // Get the first node that contains the text we're searching for.
        foreach (timelineNode current_node in lxml.nodeList)
        {
            // Pad the name to check. This will let us search for single words without tokenization.
            string name_to_check = " " + current_node.node_name + " ";
            print ("Checking " + name_to_check);
            if (name_to_check.Contains(text_to_search))
            {
                node_id_found = current_node.node_id;
                // Select the node found.
                if (user_can_take_turn)
                {
                    user_can_take_turn = false;
                    Narrate(node_id_found, 9);
                }
                else
                {
                    // Call narrative with turns = 0 to request that the given
                    // node be added to the existing story.
                    Narrate(node_id_found, 0);
                }
                break;
            }//end if
        }//end foreach
    }//end method SearchForNode

    //Present the given node given the previous nodes presented
    void Present(timelineNode node_to_present, List<timelineNode> node_history)
    {
        print("Current node: " + node_to_present.text);
        //Bring this node into focus
        node_to_present.Focus();

        // TODO: move map camera whenever new node is presented -- data type below isn't working
        // EventManager.TriggerEvent(EventManager.EventType.NARRATION_LOCATION_CHANGE, node_to_present.GetComponent<timelineNode>().node_id.ToString());

        /*//Some nodes may be layered on top of each other. Displace this node in the y if any other
		//nodes in the history share a position with it.
		bool layered = true;
		while (layered)
		{
			layered = false;
			//Check to see if the node we wish to present is layered on any node in the history.
			foreach (GameObject past_node in node_history)
			{
				if (past_node.transform.position.Equals(node_to_present.transform.position))
				{
					layered = true;
				}//end if
			}//end foreach

			//If it is layered, displace it in the y
			if (layered)
				node_to_present.transform.position = new Vector3(node_to_present.transform.position.x
					, node_to_present.transform.position.y + 1
					, node_to_present.transform.position.z);
		}//end while*/

    }//end method Present
    int previous_node_id;
    //Narrate a sequence of nodes
    IEnumerator _Narrate(int node_id, int turns)
    {
        string url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/chronology";
        string data = JsonUtility.ToJson(new ChronologyRequest(node_id, turns));
        // If we are starting a new narration, check if the turn count is 0.
        // If so, then the user has clicked on a node outside of the allotted switch point.
        // Instead of the normal url, call for an add_to_chronology request with the node_id to add
        // and the previous node id in the story.
        if (turns == 0)
        {
            //Ask the backend for a node sequence
            print("===== ADD TO NARRATION =====");
            url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/add_to_chronology";

          
            data = JsonUtility.ToJson(new AddToChronologyRequest(node_id, previous_node_id));
        }//end if
        else
        {
            //Ask the backend for a node sequence
            print("===== NEW NARRATION =====");
            url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/chronology";
            data = JsonUtility.ToJson(new ChronologyRequest(node_id, turns));
        }//end else

        //string url = "http://" + AppConfig.Settings.Backend.ip_address + ":" + AppConfig.Settings.Backend.port + "/test";


        //Debug.Log("request: " + data);

        WWW www = new WWW(url, Encoding.UTF8.GetBytes(data));
        //WWW www = new WWW(url);
        yield return www;

        // check for errors
        if (www.error == null)
        {
            Debug.Log("WWW Ok!: " + www.text);
        }
        else
        {
            Debug.Log("WWW Error: " + www.error);
            yield break;
        }

        //ChronologyResponse response = JsonUtility.FromJson<ChronologyResponse>(www.text);

        TestSequence response = JsonUtility.FromJson<TestSequence>(www.text);

        //The nodes themselves
        List<KeyValuePair<timelineNode, string>> sequence_by_node = new List<KeyValuePair<timelineNode, string>>();
        List<List<StoryAct>> sequence_acts = new List<List<StoryAct>>();
        timelineNode temp_node = null;

        //foreach (StoryNode sn in response.Sequence) {
        foreach (StoryNode sn in response.StorySequence[0].Sequence)
        {
            int id = sn.graph_node_id;
            temp_node = null;
            lxml.idMap.TryGetValue(id, out temp_node);

            //NOTE: need to use storynode text here for annotations
            sequence_by_node.Add(new KeyValuePair<timelineNode, string>(temp_node, sn.text));
            sequence_acts.Add(sn.story_acts);
        }//end foreach

        bool tmp_flag = true;
        List<timelineNode> node_history = new List<timelineNode>();
        for (int ix = 0; ix < sequence_by_node.Count; ix++)
        {
           
            //foreach (KeyValuePair<GameObject,string> kvp in sequence_by_node) {
            if (first_flag)
            {//wait for keypress before presenting first node
                first_flag = false;
                yield return StartCoroutine(WaitForKeyDown());
            }
            else if (!tmp_flag)
            {//dont wait for keypress on first node
                yield return StartCoroutine(WaitForKeyDown());
            }
            
            KeyValuePair<timelineNode, string> kvp = sequence_by_node[ix];
            timelineNode node_to_present = kvp.Key;
            
            //Bring the previous node into past-focus
            if (node_history.Count >= 1)
            {
                node_to_present.pastStoryNodeTransform = node_history[node_history.Count - 1].transform;
            }

            //Present this node
            this.current_node = node_to_present;

            Present(node_to_present, node_history);

            //TODO: Hack for timeline scaling during demo, remove later!
            if (node_to_present.node_name.Equals("Battle of Actium"))
            {
                float moveTime = 1.5f;
                ///*
                tlb.setData(0, 722700, 1, moveTime);
                //*/

                //TODO: create a function in TimeLineBar to animate these
                /*
				TimeLineBar.minDays = 0;
				TimeLineBar.maxDays = 722700;
				TimeLineBar.zoomDivisor = 1;

                TimeLineBar.minDaysTarget = 0;
                TimeLineBar.maxDaysTarget = 722700;
                TimeLineBar.zoomDivisorTarget = 1;

                //*/
                //Reset timeline positions of all timeline nodes and redo all past narration lines.
                this.pastNarrationNodeTransforms = new List<Vector3>();
                foreach (timelineNode tn in lxml.nodeList)
                {
                   
                    tn.pastNarrationLineRenderer.SetVertexCount(0);
                    tn.reset_timeline_position(moveTime);
                    //Reset past narration node positions
                    //pastNarrationNodeTransforms = new List<Vector3>();
                    //foreach (timelineNode tn2 in pastNarrationNodes)
                    //{
                    //	pastNarrationNodeTransforms.Add(tn2.transform.position);
                    //}//end foreach

                    //if (tn.state == timelineNode.focusState.PAST)
                    //	tn.drawLines();
                }//end foreach
            }//end if


            NodeData dataObj = new NodeData(node_to_present.node_id, kvp.Value, node_to_present.pic_urls, node_to_present.pic_labels);

            string json = JsonUtility.ToJson(dataObj);

            EventManager.TriggerEvent(EventManager.EventType.NARRATION_MACHINE_TURN, json);

            //always trigger a location change
            EventManager.TriggerEvent(EventManager.EventType.NARRATION_LOCATION_CHANGE, json);

            //trigger events for all current story acts

            foreach (StoryAct sa in sequence_acts[ix])
            {
                switch (sa.Item1)
                {
                    case "lead-in":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_LEAD_IN, sa.Item2.ToString());
                        break;
                    case "tie-back":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_TIE_BACK, sa.Item2.ToString());
                        break;
                    case "relationship":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_RELATIONSHIP, sa.Item2.ToString());
                        break;
                    case "novel-lead-in":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_NOVEL_LEAD_IN, sa.Item2.ToString());
                        break;
                    case "location-change":
                        //EventManager.TriggerEvent(EventManager.EventType.NARRATION_LOCATION_CHANGE, sa.Item2.ToString());
                        break;
                    case "hint-at":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_HINT_AT, sa.Item2.ToString());
                        break;
                    case "analogy":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_ANALOGY, sa.Item2.ToString());
                        break;
                    case "user-turn":
                    case "switch-point":
                        EventManager.TriggerEvent(EventManager.EventType.NARRATION_USER_TURN, sa.Item2.ToString());
                        break;
                }
            }

            //Add it to the history
            node_history.Add(node_to_present);

            // Note the node that was just presented as the previous node id in the global variable,
            // in case this function is stopped and we need to know where in the story we are.
            previous_node_id = node_history[node_history.Count - 1].node_id;

            progressNarrationSwitch = false;
            tmp_flag = false;

            //TODO: debug why this isn't getting the desired label placement
            // (zooming collision detection / placement seems more desired, although buggy)
            //Call camera collision detection method
            EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");

        }//end foreach
        user_can_take_turn = true;


        //need to manually yield a turn at end because backend bugged
        EventManager.TriggerEvent(EventManager.EventType.NARRATION_USER_TURN, node_id.ToString());




        print("STORY ARC COMPLETE");
    }

    //Wait asynchronously for a key press
    IEnumerator WaitForKeyDown()
    {
        do
        {
            yield return null;
        } while (!progressNarrationSwitch);

    }
    public void play()
    {
        GameObject narration_journal = GameObject.Find("JournalCanvas (1)");
        TTS = narration_journal.GetComponent<TextToSpeechWatson>();

        audio_clips_by_id = new Dictionary<int, AudioClip>();
        audio_clips_by_id.Add(17, clip_17);
        audio_clips_by_id.Add(22, clip_22);
        audio_clips_by_id.Add(41, clip_41);
        string potential_file_name = Application.dataPath + "/Resources/voice_files/" + current_node.node_id.ToString() + ".mp3";

        if (main_camera.GetComponent<AudioSource>().isPlaying)
            main_camera.GetComponent<AudioSource>().Stop();
        TTS.StopSpeaking();

        if (System.IO.File.Exists(potential_file_name))
        {
            Debug.Log("NarrationJournal.NARRATION_MACHINE_TURN listener() :: mp3 voice file found");
            Vector3 audio_source_location = main_camera.transform.position;
            Debug.Log("NarrationJournal.NARRATION_MACHINE_TURN listener() :: playing clip for " + current_node.node_id.ToString());
            print("NodeData id: " + current_node.node_id.ToString());
            AudioClip clip_to_play = audio_clips_by_id[current_node.node_id];

            //   if (playswitch)
            //   {
            main_camera.GetComponent<AudioSource>().PlayOneShot(clip_to_play, 1.5f);
            //      playswitch = false;
            //   }

        }//end if
        else
        {
            readText(nd1.text);
        }
    }
    public void readText(string data)
    {
        //remove rich text tags
        string result = Regex.Replace(data, @"<[^>]*>", string.Empty);

        TTS.TextToSynth = result;
        TTS.Speak();
    }





}
