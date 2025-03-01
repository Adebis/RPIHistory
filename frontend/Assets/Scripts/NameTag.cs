﻿using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class NameTag : MonoBehaviour {

	public NameTagSlot follow;
	private Text txt;
	private SpringJoint2D sj;
	private LineRenderer lr;
	private Transform m_marker;
	[SerializeField]private RectTransform m_rectTransform;
	[SerializeField]
	private NameTagContainer m_originalContainer;
	public NameTagContainer m_curContainer;
	[SerializeField] private Color tmpWhite_pnl;
	[SerializeField] private Color tmpRed_pnl;
    [SerializeField] private Color tmpBlue_pnl;
    [SerializeField] private Color tmpWhite_lr;
	[SerializeField] private Color tmpRed_lr;
    [SerializeField] private Color tmpBlue_lr;

    [SerializeField]
	private RectTransform m_childTransform;

	void Awake () {
		sj = GetComponent<SpringJoint2D>();
		txt = GetComponentInChildren<Text>();
		lr = GetComponent<LineRenderer>();
		lr.SetVertexCount(2);
    }

    public void SetNewTarget(Vector3 targetPosition)
	{
		follow.transform.position = targetPosition;
	}

    public Vector3? getMarkerPosition()
    {
        if(m_marker == null)
        {
            return null;
        }

        return m_marker.position;
    }

	public NameTagSlot GetNextSlot()
	{
		return follow;
	}

	void Start() {
		StartCoroutine(_resize());
        lr.material = new Material(Shader.Find("Particles/Additive (Soft)")); // TODO: this is giving a "nullReference" Exception at runtime.
    }

    IEnumerator _resize() {
		//adjust box collider to fit size of text in text box
		//has to execute after first udpate due to content size fitter
		yield return new WaitForEndOfFrame();
	}

	public void setTarget(NameTagSlot target, string s, Transform marker) {
		this.m_marker = marker;
		follow = target;
		txt.text = s;
	}


	void OnCollisionStay2D(Collision2D coll)
	{

	}

	public void reCenter() {
		transform.position = new Vector3(follow.transform.position.x, follow.transform.position.y, 0);
	}

    //void FixedUpdate () // TODO: find proper place to put this call so that lines don't lag behind label positions
    //{
    //    updateLinePosition();
    //}

    //void LateUpdate()
    //{
    //    updateLinePosition();
    //}

    // Update is called once per frame
    void Update () {

        updateLinePosition();

        float zw = Camera.main.orthographicSize/100f;
		lr.SetWidth(zw,zw);

        if (m_marker.GetComponent<timelineNode>().mouseOver)
        {
            this.GetComponentInChildren<Image>().color = tmpBlue_pnl;
            lr.SetColors(tmpBlue_lr, tmpBlue_lr);
        }
        else if (m_marker.GetComponent<timelineNode>().state == timelineNode.focusState.IN)
        {
            this.GetComponentInChildren<Image>().color = tmpRed_pnl;
			lr.SetColors (tmpRed_lr, tmpRed_lr);
        }
        else         {
            this.GetComponentInChildren<Image>().color = tmpWhite_pnl;
			lr.SetColors (tmpWhite_lr, tmpWhite_lr);
        }

    }

    void updateLinePosition()
    {
        //i dont know how on earth the z position screws up when you zoom
        //but it needs to be constantly set to 0 or else it goes out of camera cull
        Vector3 tmp0 = transform.position;
        tmp0.z = 0;
        transform.position = tmp0;

        Vector3 tmp1 = follow.transform.position;
        //		tmp1 = m_marker.position;
        tmp1.z = 0;
        //		tmp1.y += .5f;
        sj.connectedAnchor = tmp1;

        Vector3 tmp2 = m_marker.position;
        tmp2.z = 0;

        lr.SetPosition(0, tmp2);
        lr.SetPosition(1, new Vector3(
                m_rectTransform.position.x + m_rectTransform.lossyScale.x * m_rectTransform.rect.width / 2,
                m_rectTransform.position.y - m_rectTransform.lossyScale.y * 10.0f,
                m_rectTransform.position.z
            )
        );
    }
}
