/*using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; 

public class Tooltip : MonoBehaviour
{
	private Text tooltipText;
	private RectTransform backgroundRectTransform;  

    private void Awake(){
    	backgroundRectTransform = transform.Find("background").GetComponent<RectTransform>();
    	tooltipText = transform.Find("text").GetComponent<Text>();
    	ShowTooltip("Random tip"); 
    }
    private void ShowTooltip ( string tooltipString){
    	gameObject.SetActive(true);
    	tooltipText.text = tooltipString; 
    	float textPaddingSize = 4f; 
    	Vector2 backgroundSize = new Vector2(tooltipText.preferredWidth + textPaddingSize * 2f, tooltipText.preferredHeight+ textPaddingSize * 2f ); 
    	backgroundRectTransform.sizeDelta = backgroundSize; 
    }
    private void HideTooltip (){
    	gameObject.SetActive(false);
    }
}*/
