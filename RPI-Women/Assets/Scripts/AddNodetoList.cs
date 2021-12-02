using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class AddNodetoList : MonoBehaviour
{
    public GameObject itemTemplate;
    public GameObject content;
    public List<GameObject> list = null;
    public List<GameObject> list2 = null;
    int index = 0; 

    public void AddNode() {
        var copy = Instantiate(itemTemplate);
        if (list == null) {
            
            copy.transform.parent = content.transform;
            list.Add(copy);
            list2.Add(copy);
            index++; 
        }
        else {
            if (list.Count < 5)
            {
                
                copy.transform.parent = content.transform;
                list.Add(copy);
       
                index++; 
            }
            else
            {
                Destroy(list[index]);
                list.RemoveAt(index);
               
                index--; 

            }
        }
        int copyofindex = index;
        copy.GetComponent<Button>().onClick.AddListener(
            
                () =>
                {
                    Debug.Log("Index number is" + copyofindex);
                }
            );
        
        //why is it only deleting the last element
        // when you click on the node in the list it makes a copy of its self again ? 

    }
    

}
