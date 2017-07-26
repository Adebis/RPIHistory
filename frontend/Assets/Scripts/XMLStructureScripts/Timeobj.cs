using UnityEngine;
using System.Collections;

using System.Xml;
using System.Xml.Serialization;

public class Timeobj 
{
	[XmlAttribute("label")]
	public string label;

	[XmlAttribute("value")]
	public string value;
}
