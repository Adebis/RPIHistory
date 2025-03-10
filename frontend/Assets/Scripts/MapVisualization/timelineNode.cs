﻿using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using JsonConstructs;

public class timelineNode : MonoBehaviour
{
	public int node_id;
	public long dateticks;
	private float zeroRef = 0.0f;
	private float floatOffset;
	public string node_name;
	public string text;
	public string text_to_display = "";
	public string datevalue;
	public DateTime date;
	private bool Moveable;
	public bool mouseOver = false;
	public bool known_location = false;
    public bool location_interpolated = false;
	public bool known_date = false;
	public bool date_interpolated = false;
	public bool active = false; //Whether this node is active and interactable.
	public Vector2 location;
	public Vector3 baseSize;
	public Vector3 timelinePosition; //where the node should be on the timeline
	public Vector3 mapPosition; //where the node should be on the map
	
    public List<Vector3> pastNarrationNodePositions;
	public List<timelineNode> pastNarrationNodes;
	
	private Vector3 target_position;
	public Vector3 startPosition;
	public List<KeyValuePair<string, timelineNode>> neighbors = new List<KeyValuePair<string, timelineNode>>();//use kvp because no tuple support in unity
    public List<timelineNode> neighbors_incoming = new List<timelineNode>();
	public List<timelineNode> allNodes;
	public SpriteRenderer sr;
	public List<UnityAction<string>> callback = null;
	public Transform pastStoryNodeTransform;
	public GameObject nametagprefab;
	public GameObject nametag;
	private NameTagContainer ntContainer;
    public LineRenderer pastNarrationLineRenderer;
	private IEnumerator moveCoroutine;//reference to movement
	public nodeCategory category = nodeCategory.UNKNOWN;

	private ParticleSystem particle;
	[SerializeField]
	private GameObject m_lineRenderer;
	private GameObject ms_curvedLineParent;
	[SerializeField]
	private GameObject m_point_prefab;

	public List<string> pic_urls = new List<string>();
	public List<string> pic_labels = new List<string>();
	private const float ms_scaleFactor = 4.0f;

	public bool proxyflag = false;

	private Color base_color = Color.white;
	public Color half_focus_color = Color.white;
	public Color focus_color = Color.red;
	public Color unfocus_color = new Color(0.5f, 0.5f, 0.5f, 0.2f);
	public Color past_focus_color = Color.blue;
	public Color hover_color = Color.cyan;

	public Color line_out_focus_color = new Color(1f, 1f, 1f, 0.1f);
    public string input_text;
    NarrationManager a = new NarrationManager();
    //OUT  = Out of focus. Node does not respond to mouse, is transparent, and does not draw lines to neighboring nodes.
    //IN   = In focus. Node responds to mouse, is the focus color (red), always displays information, and draws lines to neighboring nodes.
    //HALF = Half-Focus. Node responds to mouse, is the half-focus color (white), displays information on mouse-over, and does not draw lines to neighboring nodes.
    //PAST = Past-focus. Node responds to mouse, is the past-focus color (blue), displays information on mouse-over, and draws lines to neighboring nodes.
    public enum focusState
	{
		OUT = 1,
		IN = 2,
		HALF = 4,
		PAST = 8
	}

	[SerializeField]//display for debug purposes'
					//private focusState state = focusState.OUT;
	private focusState _state = focusState.OUT;
	public focusState state
	{
		get { return _state; }
		set
		{
			switch (value)
			{
				case focusState.HALF:
					base_color = half_focus_color;
					ChangeColor(half_focus_color);
					break;
				case focusState.IN:
					base_color = focus_color;
					ChangeColor(focus_color);
					break;
				case focusState.OUT:
					base_color = unfocus_color;
					ChangeColor(unfocus_color);
					break;
				case focusState.PAST:
					base_color = past_focus_color;
					ChangeColor(past_focus_color);
					break;
			}
			_state = value;
		}
	}

	public enum nodeCategory
	{
		CHARACTER = 1,
		LOCATION = 2,
		EVENT = 4,
		UNKNOWN = 8,
		EMPEROR = 16,
		BATTLE = 32,
		CAPITOL = 64
	}


    void Awake()
	{

		if (ms_curvedLineParent == null)
		{
			ms_curvedLineParent = GameObject.FindGameObjectWithTag("CurvedLineParent");
		}
	}

	void Start()
	{
        callback = new List<UnityAction<string>>();
        particle = GetComponent<ParticleSystem>();
		sr = gameObject.GetComponent<SpriteRenderer>();
		Moveable = false;
		//transform.Rotate(Vector3.forward * UnityEngine.Random.Range(0f, 80f)); //add some random initial rotation to offset angle from other nodes
		floatOffset = UnityEngine.Random.Range(0f, 3f);
		baseSize = gameObject.transform.localScale;
		HalfFocus();
		if (transform.childCount > 0)
			pastNarrationLineRenderer = transform.GetChild(0).gameObject.GetComponent<LineRenderer>();
		pastStoryNodeTransform = null;

		//make nametag
		if (nametagprefab != null)
		{
			GameObject tag = Instantiate(nametagprefab) as GameObject;
			ntContainer = tag.GetComponent<NameTagContainer>();
			ntContainer.SetTarget(transform, node_name);
			tag.transform.SetParent(GameObject.FindGameObjectWithTag("Overlay").transform, false);
			nametag = tag;
			disable_tag();
		}//end if
	}

	public void setCategory(string cat) {

		// TODO: update this to include setting sprite art per category
		switch (cat) {
		case "character":
			category = timelineNode.nodeCategory.CHARACTER;
			break;
		case "location":
			category = timelineNode.nodeCategory.LOCATION;
			break;
		case "event":
			category = timelineNode.nodeCategory.EVENT;
			break;
		case "emperor":
			category = timelineNode.nodeCategory.EMPEROR;
			break;
		case "battle":
			category = timelineNode.nodeCategory.BATTLE;
			break;
		case "capital":
			category = timelineNode.nodeCategory.CAPITOL;
			break;
		default:
			category = timelineNode.nodeCategory.UNKNOWN;
			break;
		}

		reset_timeline_position ();
	}

	public void reset_timeline_position(float moveTime = 1.5f)
	{
		int totaldays = 365 * date.Year + date.DayOfYear;
		float ypos = 0;
		switch (category)
		{
		case nodeCategory.LOCATION:
//			ypos = 20; // TODO: make these numbers relative to a larger system rather than hard-coded constants like this
			ypos = 28;
			break;
		case nodeCategory.CAPITOL:
//			ypos = 10;
			ypos = 20;
			break;
		case nodeCategory.CHARACTER:
			ypos = 8;
			break;
		case nodeCategory.EMPEROR:
			ypos = 0;
//          ypos = -10;
//          ypos = -20;
			break;
		case nodeCategory.EVENT:
			ypos = -20;
//          ypos = -40;
			break;
		case nodeCategory.BATTLE:
			ypos = -20;
//          ypos = -60;
			break;
		case nodeCategory.UNKNOWN:
			ypos = -40;
//          ypos = -80;
			break;
		}
        // Is this where I would interpolate XPosition?
		timelinePosition = new Vector3(TimeLineBar.dateToPosition(totaldays), ypos + (node_id % 15) - 15, 0); //deterministic random for horizontal stretch
		moveToPosition(timelinePosition, moveTime);
	}

	public void enable_tag()
	{
		nametag.SetActive(true);
		ntContainer.ReCenter();

		if (state == focusState.IN /*|| state == focusState.PAST*/) // TODO: reimplement this once we figure out a way to get nameTags to avoid the mouse position
		{
			foreach (KeyValuePair<string, timelineNode> neighbor_node in neighbors)
			{
				neighbor_node.Value.nametag.SetActive(true);
				neighbor_node.Value.ntContainer.ReCenter();
				if (state == focusState.IN) neighbor_node.Value.proxyflag = true;
			}
		}

        EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");
    }

    public void disable_tag(bool _override = false)
	{
		if (nametag != null && !proxyflag)
		{
			nametag.SetActive(false);
		}
		if (state != focusState.IN)
		{
			foreach (KeyValuePair<string, timelineNode> neighbor_node in neighbors)
			{
				if (neighbor_node.Value.nametag != null && neighbor_node.Value.state != focusState.IN)
				{
                    if (state == focusState.PAST && _override)
                    {
                        neighbor_node.Value.proxyflag = false;
                    }
                    if (neighbor_node.Value.proxyflag != true)
                    {
                        neighbor_node.Value.nametag.SetActive(false);
                    }
                }
			}
		}

        EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");
    }

	public void moveToPosition(Vector3 position, float moveTime)
	{
		if (moveCoroutine != null) StopCoroutine(moveCoroutine);
		moveCoroutine = _move(moveTime);
		target_position = position;
		StartCoroutine(moveCoroutine);
	}

    public AnimationCurve ease = new AnimationCurve();
	private IEnumerator _move(float movetime)
	{
		Vector3 currentPos = transform.position;
		float t = 0f;
		while (t < 1)
		{
			t += Time.deltaTime / movetime;

			transform.position = Vector3.Lerp(currentPos, target_position, ease.Evaluate(t));
			//Each time this node moves, if it is in an appropriate state, re-draw its lines
			if (state == focusState.IN || state == focusState.PAST)
				drawLines();
			yield return null;
		}
        //Each time this node moves, if it is in an appropriate state, re-draw its lines
        if (state == focusState.IN || state == focusState.PAST) drawLines();
        
        Moveable = true;
		startPosition = transform.position;
		
		//TODO: Part of hack for demo for rescalable timeline. Remove later!
		//Center labels again.
		ntContainer.UpdateTarget(this.transform);
		ntContainer.ReCenter();
	}

	private void Float()
	{
		Vector3 newPos = new Vector3(transform.position.x, startPosition.y + 0.5f * Mathf.Sin(Time.time + floatOffset), transform.position.z);
		transform.position = newPos;
	}

	private void rotateRight()
	{
		transform.Rotate(Vector3.forward * -2);
	}

	void Update()
	{
	}

	//Bring this node into focus.
	public void Focus()
	{
        pastNarrationNodePositions = GameObject.Find("loader").GetComponent<NarrationManager>().pastNarrationNodeTransforms;
        pastNarrationNodePositions.Add(transform.position);
		
		//TODO: Part of hack for demo.
		pastNarrationNodes = GameObject.Find("loader").GetComponent<NarrationManager>().pastNarrationNodes;
		pastNarrationNodes.Add(this);

		//particle.Play();

		//If any other node is the focus, make it a past-focus
		foreach (timelineNode tn in allNodes)
		{
        //    if (input_text == tn.node_name.ToLower())
        //    {
        //        a.Narrate(tn.node_id, 9);
        //    }
			if (tn.state == focusState.IN)
			{
				tn.PastFocus();
			}
		}//end foreach

		ChangeSize(new Vector3(baseSize.x * 2.5f, baseSize.y * 2.5f, baseSize.z));

		//Mark this node as active
		active = true;
		state = focusState.IN;
		drawLines();
		enable_tag();
		//Send OSC packet of position.x and sound trigger
		List<object> newFocus = new List<object>();
		newFocus.AddRange(new object[] { 1, gameObject.transform.position.x });
		OSCHandler.Instance.SendMessageToClient("MaxServer", "/newFocus/", newFocus);

		//Bring its neighbors into half-focus if they aren't a past-focus or a focus.
		foreach (KeyValuePair<string, timelineNode> neighbor_node in this.neighbors)
		{
			if (neighbor_node.Value.state != focusState.PAST && neighbor_node.Value.state != focusState.IN)
				neighbor_node.Value.HalfFocus();
		}//end foreach

		if (callback != null) callback.ForEach(action => action("IN"));
        EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");
    }//end method Focus

	//
	//
	//Half-focus this node.
	public void HalfFocus()
	{
		//Mark this node as active
		active = true;
		state = focusState.HALF;
		//Bring it forward by changing its Z
		gameObject.transform.position = new Vector3(gameObject.transform.position.x
			, gameObject.transform.position.y
			, gameObject.transform.position.z );//changed

		//Send OSC packet of position.x and position.y for neighbors
		List<object> halfFocus = new List<object>();
		halfFocus.AddRange(new object[] { gameObject.transform.position.y, gameObject.transform.position.x });
		OSCHandler.Instance.SendMessageToClient("MaxServer", "/halfFocus/", halfFocus);
		if (callback != null) callback.ForEach(action => action("HALF"));

        EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");
    }//end method HalfFocus

	//Bring this node out of full focus, but remember it used to be the focus.
	public void PastFocus()
	{
		print("Past Focus " + name);
		//Mark this node as active
		ChangeSize(new Vector3(baseSize.x, baseSize.y, baseSize.z));
		active = true;
		state = focusState.PAST;
		//Bring it forward by changing its Z
		gameObject.transform.position = new Vector3(gameObject.transform.position.x
			, gameObject.transform.position.y
			, gameObject.transform.position.z );
       
		ColorLines(line_out_focus_color);
		if (callback != null) callback.ForEach(action => action("PAST"));

        disable_tag(true);
    }//end method PastFocus

	//Bring this node out of focus
	public void Unfocus()
	{
        if (state == timelineNode.focusState.IN)
        {
            PastFocus();
            return;
        }

        //Mark this node as inactive
        active = false;
        state = focusState.OUT;
		//Remove lines from this node to its neighbors
		//Bring it to the back by changing its Z
		gameObject.transform.position = new Vector3(gameObject.transform.position.x
			, gameObject.transform.position.y
			, gameObject.transform.position.z +5);
		ColorLines(line_out_focus_color);
		if (callback != null) callback.ForEach(action => action("OUT"));

        EventManager.TriggerEvent(EventManager.EventType.LABEL_COLLISION_CHECK, "");
    }//end method Unfocus

    private IEnumerator tmplcr = null;
	//past-focus == 8
	//if the node is a previous story node, draw the line thicker and 
	public void drawLines()
	{
		if (tmplcr != null)
		{
			StopCoroutine(tmplcr);
		}
		tmplcr = _drawLines();
		StartCoroutine(tmplcr);
	}

	public List<LineRenderer> m_renderers = new List<LineRenderer>();

	//Assign Line Renderer vertcies
	private IEnumerator _drawLines()
	{

	    drawPastNarrationLines();
        
        foreach (LineRenderer lr in m_renderers)
		{//need to clear old lines
			Destroy(lr.gameObject);
		}
		m_renderers.Clear();
		//yield return new WaitForSeconds(1);
		Vector3 startPoint = transform.position; // this is the central node position
		GameObject midpoint_go;
		for (int i = 2, nCount = 0; i < Mathf.Max(neighbors.Count * 3, 1); i += 3, nCount += 1)
		{
			GameObject renderer = Instantiate(m_lineRenderer) as GameObject;
			m_renderers.Add(renderer.GetComponent<LineRenderer>());
			ColorLines(focus_color);
			Vector3 midpoint_vec;

            //Vector3 endPoint;


			//draw the past story node in it the child-object's Line Renderer
			/*if (pastStoryNodeTransform != null)
			{

				//rotate vec 90 degrees
				midpoint_vec = timelineNode.ComputeMidpoint(pastStoryNodeTransform.position, startPoint);
				GameObject.Instantiate(m_point_prefab, pastStoryNodeTransform.position, this.transform.rotation, renderer.transform);
				midpoint_go = GameObject.Instantiate(m_point_prefab, midpoint_vec, this.transform.rotation, renderer.transform) as GameObject;

				midpoint_go.tag = "Midpoint";

				GameObject.Instantiate(m_point_prefab, startPoint, this.transform.rotation, renderer.transform);
			}*/
			midpoint_vec = ComputeMidpoint(startPoint, neighbors[nCount].Value.transform.position);

			Instantiate(m_point_prefab, startPoint, this.transform.rotation, renderer.transform);
			midpoint_go = Instantiate(m_point_prefab, midpoint_vec, this.transform.rotation, renderer.transform) as GameObject;
			midpoint_go.tag = "Midpoint";

			Instantiate(m_point_prefab, neighbors[nCount].Value.transform.position, this.transform.rotation, renderer.transform);
			ms_curvedLineParent.transform.SetParent(ms_curvedLineParent.transform);
			yield return null;
		}
		
		tmplcr = null;
	}

    public void drawPastNarrationLines() {
        //Uses a public list of transforms from NarrationManager. Focus() adds a node to the list.

        //TODO: Part of demo hack for rescalable timeline. Remove later!
        //nametag.GetComponent<NameTag>().reCenter();
        //Redo past narration lines
      
        pastNarrationNodePositions = new List<Vector3>();
        pastNarrationNodePositions = GameObject.Find("loader").GetComponent<NarrationManager>().pastNarrationNodeTransforms;

       
        if (this.node_name.Equals("Battle of Actium"))
		{
 
			pastNarrationNodes = GameObject.Find("loader").GetComponent<NarrationManager>().pastNarrationNodes;
			pastNarrationNodePositions = new List<Vector3>();
			foreach (timelineNode tn in pastNarrationNodes)
			{
				pastNarrationNodePositions.Add(tn.transform.position);
			}//end foreach
		}//end if

        pastNarrationLineRenderer.SetVertexCount(pastNarrationNodePositions.Count);
        pastNarrationLineRenderer.SetPositions(pastNarrationNodePositions.ToArray());
        
        /*print(pastNarrationNodePositions.Count);
		pastNarrationLineRenderer.SetVertexCount(pastNarrationNodePositions.Count);
		pastNarrationLineRenderer.SetPositions(pastNarrationNodePositions.ToArray());
        */
        // Old, clunky method. 
        /*//straight-line rendering for past story nodes
        Vector3 centralNodePos = transform.position;
        Vector3[] points = new Vector3[Mathf.Max(neighbors.Count * 2, 1)];
        Vector3[] pastNarrationPoints = new Vector3[6];
        points[0] = centralNodePos;
        pastNarrationPoints[0] = centralNodePos;
        int pastPointsCount = 1;
        int coun = 0;
        for (int i = 1; i < points.Length; i += 2) {
            if (neighbors[coun].Value.state == focusState.PAST) {
                pastNarrationPoints[pastPointsCount] = neighbors[coun].Value.transform.position;
                pastNarrationPoints[pastPointsCount + 1] = centralNodePos;
                pastPointsCount += 1;
            }
            points[i - 1] = centralNodePos;
            points[i] = neighbors[coun].Value.transform.position;
            coun++;
        }
        if (pastPointsCount == 1) {
            pastNarrationLineRenderer.SetVertexCount(pastPointsCount);
            pastNarrationLineRenderer.SetPositions(new Vector3[1] { centralNodePos } );
        }
        else {
            pastNarrationLineRenderer.SetVertexCount(pastPointsCount);
            pastNarrationLineRenderer.SetPositions(pastNarrationPoints);
        }*/
    }


	private static Vector3 ComputeMidpoint(Vector3 positionA, Vector3 positionB)
	{
		float dist = Vector3.Distance(positionA, positionB) / ms_scaleFactor;

		//Vector3 nearestMidpointPosition;

		float closestDist = Mathf.Infinity;
		Vector3? closestPos = null;

		Vector3 vec = Vector3.Lerp(positionA, positionB, .5f) + new Vector3(UnityEngine.Random.Range(-dist, dist), UnityEngine.Random.Range(-dist, dist), 0);

		foreach (GameObject midpoint in GameObject.FindGameObjectsWithTag("Midpoint"))
		{
			float d = Vector3.Distance(midpoint.transform.position, vec);
			if (d < closestDist)
			{
				closestDist = d;
				closestPos = midpoint.transform.position;
			}
		}

		if (closestPos == null)
		{
			return vec;
		}

		Vector3 difVector = ((Vector3)closestPos) - vec;

		vec += difVector.normalized * dist;


		return vec;

	}


	public void ChangeSize(Vector3 final_size)
	{
		end_size = final_size;
		smooth_time = 0.2f;

		//Change Collider accordingly
		if (gameObject.GetComponent<SpriteRenderer>().sprite != null)
		{
			float new_width = gameObject.GetComponent<SpriteRenderer>().sprite.bounds.size.x; //base_size.x;
			float new_height = gameObject.GetComponent<SpriteRenderer>().sprite.bounds.size.y; //base_size.y;
			float desired_collider_radius = Mathf.Min(new_width, new_height);
			gameObject.GetComponent<CircleCollider2D>().radius = desired_collider_radius * 0.75f;
			if (gameObject.activeInHierarchy)
				StartCoroutine("ChangeNodeSize");
		} //end if
	} //end method ChangeNodeSize

	private float smooth_time;
	private Vector3 end_size;

	IEnumerator ChangeNodeSize()
	{
		float distance_to_final = Vector3.Distance(gameObject.GetComponent<RectTransform>().localScale, end_size);
		while (distance_to_final > 0.01f)
		{
			distance_to_final = Vector3.Distance(gameObject.GetComponent<RectTransform>().localScale, end_size);
			if (distance_to_final <= 0.01f)
			{
				gameObject.GetComponent<RectTransform>().localScale = end_size;
				break;
			}

			gameObject.GetComponent<RectTransform>().localScale =
				new Vector3(
					Mathf.SmoothDamp(gameObject.GetComponent<RectTransform>().localScale.x, end_size.x, ref zeroRef, smooth_time),
					Mathf.SmoothDamp(gameObject.GetComponent<RectTransform>().localScale.y, end_size.y, ref zeroRef, smooth_time),
					gameObject.GetComponent<RectTransform>().localScale.z);
			yield return null;
		}
	}

	public void ColorLines(Color c)
	{
		foreach (LineRenderer lr in m_renderers)
		{
			lr.SetColors(c, c);
		}
	}

	private static Vector3 mpos = new Vector3(-1, -1, -1); //hack for stupid bug

	public void OnMouseEnter()
	{
		if (Input.mousePosition == mpos)
		{
			return;
		}
		else {
			mpos = Input.mousePosition;
		}
		Moveable = false;
		//Only trigger mouse effects if this node is active
		if (active && !mouseOver)
		{
			mouseOver = true;
			enable_tag();

            ChangeSize(new Vector3(baseSize.x * 2f, baseSize.y * 2f, baseSize.z));
			ChangeColor(Color.cyan);
			if (callback != null) callback.ForEach(action => action("RE"));

			//Send OSC packet of posiion.x and position.y of moused over node
			List<object> moused = new List<object>();
			moused.AddRange(new object[] { gameObject.transform.position.y, gameObject.transform.position.x });
			OSCHandler.Instance.SendMessageToClient("MaxServer", "/mouseOver/", moused);

		}//end if
		ColorLines(hover_color);
    }

    public void OnMouseExit()
	{
		if (Input.mousePosition == mpos)
		{
			return;
		}
		Moveable = true;
		//Only trigger mouse effects if this node is active
		if (active)
		{
			if (state != focusState.IN)
			{
				ChangeSize(new Vector3(baseSize.x, baseSize.y, baseSize.z));
			}
			mouseOver = false;
			ChangeColor(base_color);
			if (callback != null) callback.ForEach(action => action("BACK"));
		}//end if

		if (state != focusState.IN)
		{
			foreach (LineRenderer lr in gameObject.GetComponent<timelineNode>().m_renderers)
			{
				lr.SetColors(line_out_focus_color, line_out_focus_color);
			}

			disable_tag(); // TODO: figure out why this is hidng labels to nodes that are adgacent to current node
        }
        else
		{
			foreach (LineRenderer lr in gameObject.GetComponent<timelineNode>().m_renderers)
			{
				lr.SetColors(focus_color, focus_color);
			}
		}
    }

    public void ChangeColor(Color newColor)
	{
		if (sr != null)
			sr.color = newColor;
	}

	void OnMouseDown()
	{
		if ((state & (focusState.IN | focusState.HALF | focusState.PAST)) != 0)
		{
			EventManager.TriggerEvent(EventManager.EventType.INTERFACE_NODE_SELECT, serializeNode());
		}
        mouseOver = false;
    }

    void OnMouseUp()
    {
        mouseOver = true;
    }

    void OnMouseOut()
    {
        mouseOver = false;
    }


    public string serializeNode() {
		//serialize a node's text data for message passing
		//returns a JSON construct
		NodeData dataObj = new NodeData(node_id, text, pic_urls, pic_labels);
		return JsonUtility.ToJson(dataObj);
	}

    /*
	public void OnMouseDrag() {
		//Only trigger mouse effects if this node is active
		if (active) {
			if (state == 1 || state == 3)
				drawLines ();
			//TODO: MAKE EACH NODE ONLY REDRAW THE LINE TO THIS NODE
			foreach (timelineNode tn in allNodes) {
				//Only redraw if the other node is a focus or past-focus node (states 1 or 3)
				if (tn.state == 1 || tn.state == 3) {
					tn.drawLines();
				}
			}
			transform.position = Camera.main.ScreenToWorldPoint ((new Vector3 (Input.mousePosition.x, Input.mousePosition.y, 10)));
		}//end if
	}
	*/
    public void text_change(string newtext)
    {
        
        
        input_text = newtext.ToLower();
        print(input_text);
        
       // Focus();
      /*  foreach (timelineNode i in allNodes)
        {
         
            if (i.node_name.ToLower() == input_text)
            {
                print("ggggggggggggg");
               
            }
        }
        */
    }
}