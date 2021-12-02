using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class SwitchScenes : MonoBehaviour
{
    // Start is called before the first frame update
    /*public void Load{
    	SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex +1 ); 
    }*/
    public GameObject jornal;


    public void Open_jornal() {
        bool isActive = jornal.activeSelf; 
        if (jornal != null) {
            jornal.SetActive(!isActive); 
        }
    }

}
