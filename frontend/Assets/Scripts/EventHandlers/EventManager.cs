﻿using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

public class EventManager : MonoBehaviour {

	private class NarrationEvent: UnityEvent<string>{}

	//event types defined here
	public enum EventType {
		NARRATION_RELATIONSHIP,
		NARRATION_ANALOGY,
		NARRATION_LEAD_IN,
		NARRATION_NOVEL_LEAD_IN,
		NARRATION_HINT_AT,
		NARRATION_TIE_BACK,
		NARRATION_LOCATION_CHANGE,

		NARRATION_MACHINE_TURN, //called when the machine takes a turn
		NARRATION_USER_TURN, //called when the user takes a turn

		INTERFACE_NODE_SELECT, //called when the user selects a node
		INTERFACE_ZOOM_IN, //called when the user zooms in
		INTERFACE_ZOOM_OUT, //called when the user zooms out
		INTERFACE_PAN, //called when the user pans the camera

		INTERFACE_PAGE_LEFT, //called when the user steps back in the journal
		INTERFACE_PAGE_RIGHT, //called when the user steps forward in the journal

        OSC_SPEECH_INPUT, //called when OSC receives a speech input event

        LABEL_COLLISION_CHECK // called whenever a label collision check is being requested
	}

	private Dictionary<EventType, NarrationEvent> eventDictionary;

	private static EventManager eventManager;

	public static EventManager instance {
		get {
			if (!eventManager) {
				eventManager = FindObjectOfType(typeof(EventManager)) as EventManager;

				if (!eventManager) {
					Debug.LogError("There needs to be one active EventManger script on a GameObject in your scene.");
				}
				else {
					eventManager.Init();
				}
			}

			return eventManager;
		}
	}

	void Init() {
		if (eventDictionary == null) {
			eventDictionary = new Dictionary<EventType, NarrationEvent>();
		}
	}

	public static void StartListening(EventType eventName, UnityAction<string> listener) {
		NarrationEvent thisEvent = null;
		if (instance.eventDictionary.TryGetValue(eventName, out thisEvent)) {
			thisEvent.AddListener(listener);
		}
		else {
			thisEvent = new NarrationEvent();
			thisEvent.AddListener(listener);
			instance.eventDictionary.Add(eventName, thisEvent);
		}
	}

	public static void StopListening(EventType eventName, UnityAction<string> listener) {
		if (eventManager == null) return;
		NarrationEvent thisEvent = null;
		if (instance.eventDictionary.TryGetValue(eventName, out thisEvent)) {
			thisEvent.RemoveListener(listener);
		}
	}

	public static void TriggerEvent(EventType eventName, string data) {
		NarrationEvent thisEvent = null;
		if (instance.eventDictionary.TryGetValue(eventName, out thisEvent)) {
			thisEvent.Invoke(data);
		}
	}
}