using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using Dialogue_Data_Entry;
using System.Windows.Forms;
using System.Security;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dialogue_Data_Entry
{

	class XMLFilerForFeatureGraph
	{
		public static string current_file;
		public static XmlDocument docOld = new XmlDocument();

		public static string escapeInvalidXML(string s)
		{
			if (s == null)
			{
				return s;
			}
			return SecurityElement.Escape(s);
		}

		public static string unEscapeInvalidXML(string s)
		{
			if (s == null)
			{
				return s;
			}
			string str = s;
			str = str.Replace("&apos;", "'");
			str = str.Replace("&quot;", "\""); 
			str = str.Replace("&gt;", ">");
			str = str.Replace("&lt;", "<");
			str = str.Replace("&amp;", "&");
			return str;
		}

		public static bool writeFeatureGraph(FeatureGraph toWrite, string fileName)
		{
			try
			{
				StreamWriter writer = new StreamWriter(fileName);
				writer.WriteLine("<AIMind>");
				if (toWrite.Root != null)
				{
					writer.WriteLine("<Root id=\"" + toWrite.Root.Id + "\"/>");
				}
				//Start writing the Features block
				writer.WriteLine("<Features>");
				for (int x = 0; x < toWrite.Features.Count; x++)
				{
					Feature tmp = toWrite.Features[x];
					writer.WriteLine("<Feature id=\"" + tmp.Id + "\" data=\"" + escapeInvalidXML(tmp.Name) + "\">");
					//Neighbor
					writer.WriteLine("<neighbors>");
					for (int y = 0; y < tmp.Neighbors.Count; y++)
					{
						int id = toWrite.getFeatureIndex(tmp.Neighbors[y].Item1.Id);
						writer.WriteLine("<neighbor dest=\"" + id + "\" weight=\"" + tmp.Neighbors[y].Item2 + "\" relationship=\"" + escapeInvalidXML(tmp.Neighbors[y].Item3) + "\"/>");
					}//end for
					writer.WriteLine("</neighbors>");
					//Parent 
					writer.WriteLine("<parents>");
					for (int y = 0; y < tmp.Parents.Count; y++)
					{
						int id = toWrite.getFeatureIndex(tmp.Parents[y].Item1.Id);
						writer.WriteLine("<parent dest=\"" + id + "\" weight=\"" + tmp.Parents[y].Item2 + "\" relationship=\"" + escapeInvalidXML(tmp.Parents[y].Item3) + "\"/>");
					}//end for
					writer.WriteLine("</parents>");
					//Tag
					List<Tuple<string, string, string>> tags = tmp.Tags;
					for (int y = 0; y < tags.Count; y++)
					{
						string toWriteTag = "<tag key=\"" + escapeInvalidXML(tags[y].Item1);
						toWriteTag += "\" value=\"" + escapeInvalidXML(tags[y].Item2);
						toWriteTag += "\" type=\"" + escapeInvalidXML(tags[y].Item3) + "\"/>";
						writer.WriteLine(toWriteTag);
					}
					//Speak
					List<string> speaks = tmp.Speaks;
					for (int y = 0; y < speaks.Count; y++)
					{
						writer.WriteLine("<speak value=\"" + escapeInvalidXML(speaks[y]) + "\"/>");
					}
					writer.WriteLine("</Feature>");
				}//end for
				//Stop writing the Features block
				writer.WriteLine("</Features>");

				writer.WriteLine("</AIMind>");
				writer.Close();
				return true;
			}
			catch (Exception e)
			{
				MessageBox.Show(e.ToString());
				return false;
			}
		}

		//Reads an XML file at the given file path and creates a feature graph from it.
		public static FeatureGraph readFeatureGraph(string toReadPath)
		{
			try {
				FeatureGraph result_graph = new FeatureGraph();
				XmlDocument doc = new XmlDocument();
				doc.Load(toReadPath);
				current_file = toReadPath;
                string file_name = "";
                if (toReadPath.Contains("/"))
                    file_name = toReadPath.Substring(toReadPath.LastIndexOf("/") + 1);
                else
                    file_name = toReadPath.Substring(toReadPath.LastIndexOf("\\") + 1);
                result_graph.file_name = file_name;
				docOld = doc;
				//Get the features
				XmlNodeList features = doc.SelectNodes("AIMind");

				//relation code
				//map of Tuple(name,src,dest)
				//Dictionary<string, Tuple<string, string, string>> usage_map = new Dictionary<string, Tuple<string, string, string>>();
				//XmlNodeList relations = features[0].SelectNodes("Relations");
				//relations = relations[0].SelectNodes("Relation");

				features = features[0].SelectNodes("Features");
				features = features[0].SelectNodes("Feature");
				//Get each feature's name ("data" field) and each feature's id. Create a new feature
				//in the backend using the name and id.
				//Each feature must be created with a name and id first for neighbor and
				//parent relationships to be properly made.
				foreach (XmlNode node in features) {
					string name = unEscapeInvalidXML(node.Attributes["data"].Value);
					int id = Convert.ToInt32(node.Attributes["id"].Value);
					result_graph.addFeature(new Feature(name, id));

					//relation code
					//usage_map["f" + node.Attributes["id"].Value] = new Tuple<string, string, string>(name, null, null);


				}//end foreach
				foreach (XmlNode node in features) {
					//Find the current feature in the feature graph by its id.
					Feature tmp = result_graph.getFeature(Convert.ToInt32(node.Attributes["id"].Value));
					//Neighbor
					XmlNodeList neighbors = node.SelectNodes("neighbors");
					neighbors = neighbors[0].SelectNodes("neighbor");
					foreach (XmlNode neighborNode in neighbors) {
						int neighbor_id = Convert.ToInt32(neighborNode.Attributes["dest"].Value);
                        double weight = 0;  // Convert.ToDouble(neighborNode.Attributes["weight"].Value);
						string relationship = "";
						if (neighborNode.Attributes["relationship"] != null) {
							relationship = unEscapeInvalidXML(Convert.ToString(neighborNode.Attributes["relationship"].Value));
						}//end if
						 //Add the neighbor feature according to its id
						tmp.addNeighbor(result_graph.getFeature(neighbor_id), weight, relationship);
					}//end foreach
					 //Tag
					XmlNodeList tags = node.SelectNodes("tag");
					foreach (XmlNode tag in tags) {
						string key = unEscapeInvalidXML(tag.Attributes["key"].Value);
						string val = unEscapeInvalidXML(tag.Attributes["value"].Value);
						string type = unEscapeInvalidXML(tag.Attributes["type"].Value);
						tmp.addTag(key, val, type);
					}
					//Speak
					XmlNodeList speaks = node.SelectNodes("speak");
					foreach (XmlNode speak in speaks) {
						try {
							tmp.addSpeak(unEscapeInvalidXML(speak.InnerText));
						}
						catch {
							//use new format
							tmp.addSpeak(speak.InnerText);
						}
					}
					//Timedata
					XmlNodeList timedata = node.SelectNodes("timedata");
					if (timedata.Count != 0)
					{
						timedata = timedata[0].SelectNodes("timeobj");
						foreach (XmlNode timeobj in timedata)
						{
							string rel = "";
							if (timeobj.Attributes["relationship"] != null)
								rel = unEscapeInvalidXML(Convert.ToString(timeobj.Attributes["relationship"].Value));
							string val = "";
							if (timeobj.Attributes["value"] != null)
								val = unEscapeInvalidXML(Convert.ToString(timeobj.Attributes["value"].Value));
							tmp.addTimeData(rel, val);
						}//end foreach
					}//end if
					//Geodata
					XmlNodeList geodata = node.SelectNodes("geodata");
					if (geodata.Count != 0)
					{
						geodata = geodata[0].SelectNodes("coordinates");
						foreach (XmlNode coordinates in geodata)
						{
                            //Console.Out.WriteLine("lat:" + coordinates.Attributes["lat"].Value + ":lon:" + coordinates.Attributes["lon"].Value);
                            double lat = 0;
                            double lon = 0;
                            bool parse_success = double.TryParse(coordinates.Attributes["lat"].Value, out lat);
                            if (!parse_success)
                                continue;
                            parse_success = double.TryParse(coordinates.Attributes["lon"].Value, out lon);
                            if (!parse_success)
                                continue;
                            tmp.addGeoData(lat, lon);
						}//end foreach
					}//end if

				}//end foreach

                //Have each feature build its neighbor dictionary.
                foreach (Feature temp_feat in result_graph.Features)
                {
                    temp_feat.buildNeighborDictionary();
                    //Have each feature calculate its own start/end dates   
                    temp_feat.calculateDate();
                }//end foreach

				//Connectedness check: check each node's neighbor. If the neighbor does not have
				//this node on its list of neighbors, place it there.
				//This is here because knowledge explorer does not add some inverse relationships.
				//From 5/5/2016, added by Zev Battad.
				//Go over each feature.
				foreach (Feature feature_to_check in result_graph.Features)
				{
					//Go over each neighbor in this feature
					foreach (Tuple<Feature, double, string> neighbor_to_check in feature_to_check.Neighbors)
					{
                        //Check that the feature to check is its neighbor's neighbor
                        bool is_neighbor = false;
                        is_neighbor = neighbor_to_check.Item1.neighbor_dictionary.ContainsKey(feature_to_check.Id);

                        //If no neighbor was found
						//We must add an inverse relationship.
                        if (!is_neighbor)
						{
							//Leave the relationship blank to signal that the relationship going the other direction
							//should be used.
							neighbor_to_check.Item1.addNeighbor(feature_to_check, 0);
						}//end else
					}//end foreach
				}//end foreach

                //If a node has one of the following relationships with a neighbor, the neighbor is a character.
                List<string> outgoing_character_relationships = new List<string>();
                //outgoing_character_relationships.Add("with");
                //outgoing_character_relationships.Add("commander");
                outgoing_character_relationships.Add("designed by");
                outgoing_character_relationships.Add("will become");
                outgoing_character_relationships.Add("was commissioned by");
                outgoing_character_relationships.Add("is the namesake of");
                outgoing_character_relationships.Add("alma mater of");
                //If a node has one of the following relationships with a neighbor, the node is a character.
                List<string> inner_character_relationships = new List<string>();
       
                //inner_character_relationships.Add("title");
                inner_character_relationships.Add("is the eponym of");
                inner_character_relationships.Add("will become");
                inner_character_relationships.Add("was commissioned by");
               // inner_character_relationships.Add("is the namesake of");
                inner_character_relationships.Add("graduated from");
                inner_character_relationships.Add("designed");
                //inner_character_relationships.Add("designed by");
                //If a node has one of the following relationships with a neighbor, the neighbor it has
                //this relationship with is a location.
                List<string> outgoing_location_relationships = new List<string>();
                //outgoing_location_relationships.Add("Capital");
                //outgoing_location_relationships.Add("region");
                //location_relationships.Add("place of military conflict");
                outgoing_location_relationships.Add("is the eponym of");
                //outgoing_location_relationships.Add("is part of");
                outgoing_location_relationships.Add("replaced by");
                //outgoing_location_relationships.Add("is the namesake of");
                outgoing_location_relationships.Add("graduated from");
                outgoing_location_relationships.Add("designed");
                outgoing_location_relationships.Add("replaced");
                //If a node has one of the following relationships with a neighbor, the node is a location.
                List<string> inner_location_relationships = new List<string>();
                inner_location_relationships.Add("includes");
                inner_location_relationships.Add("is the namesake of");
                //inner_location_relationships.Add("is part of");
                inner_location_relationships.Add("is location of");
                inner_location_relationships.Add("replaced");
                //inner_location_relationships.Add("designed by");
                //If a node has one of the following relationships with a neighbor, the neighbor is an event.
                List<string> outgoing_event_relationships = new List<string>();
                outgoing_event_relationships.Add("is part of military conflict");

                //If a node has one of the following relationships with a neighbor, the node is an event.
                List<string> inner_event_relationships = new List<string>();
                inner_event_relationships.Add("combatant");
                inner_event_relationships.Add("is part of military conflict");
                inner_event_relationships.Add("commander");
                inner_event_relationships.Add("place of military conflict");
                inner_event_relationships.Add("countries affected");
                inner_event_relationships.Add("Name");

                //Try to determine what entity each node is.
                foreach (Feature temp_feature in result_graph.Features)
                {
                    //Check time data. If "birth" or "death" is amongst the relationship types, this is a character.
                    foreach (Tuple<string, string> temp_time_data in temp_feature.Timedata)
                    {
                        if (temp_time_data.Item1.ToLower().Contains("birth")
                            || temp_time_data.Item1.ToLower().Contains("death"))
                        {
                            temp_feature.AddEntityType(Constant.CHARACTER);
                            break;
                        }//end if
                        if (temp_time_data.Item1.ToLower().Contains("date"))
                        {
                            temp_feature.AddEntityType(Constant.LOCATION);
                            break;
                        }//end if
                    }//end foreach
                    //Check outgoing relationships.
                    foreach (Tuple<Feature, double, string> temp_neighbor in temp_feature.Neighbors)
                    {
                        if (outgoing_character_relationships.Contains(temp_neighbor.Item3))
                        {
                            temp_neighbor.Item1.AddEntityType(Constant.CHARACTER);
                        }//end if
                        if (inner_character_relationships.Contains(temp_neighbor.Item3))
                        {
                            temp_feature.AddEntityType(Constant.CHARACTER);
                        }//end if
                        //if (temp_neighbor.Item3.Contains("event"))
                        //    temp_neighbor.Item1.AddEntityType(Constant.EVENT);
                        if (outgoing_event_relationships.Contains(temp_neighbor.Item3))
                        {
                            temp_neighbor.Item1.AddEntityType(Constant.EVENT);
                        }//end if
                        if (inner_event_relationships.Contains(temp_neighbor.Item3))
                        {
                            temp_feature.AddEntityType(Constant.EVENT);
                        }//end if
                        if (outgoing_location_relationships.Contains(temp_neighbor.Item3))
                        {
                            temp_neighbor.Item1.AddEntityType(Constant.LOCATION);
                        }//end if
                        if (inner_location_relationships.Contains(temp_neighbor.Item3))
                            temp_feature.AddEntityType(Constant.LOCATION);
                    }//end foreach
                    if (temp_feature.Name.Equals("Auxiliary Dormitories"))
                        Console.Out.WriteLine("Auxiliarydormitories");
                    //Check geodata. If there is any, and this is not another entity type, this is a location.
                    if (temp_feature.Geodata.Count > 0 
                        && !temp_feature.HasEntityType(Constant.EVENT)
                        && !temp_feature.HasEntityType(Constant.CHARACTER))
                        temp_feature.AddEntityType(Constant.LOCATION);

                    //Check for sub-categories.
                    //Capitals
                    foreach (Tuple<Feature, double, string> temp_neighbor in temp_feature.Neighbors)
                    {
                        if (temp_neighbor.Item3.Equals("Capital")
                            || temp_neighbor.Item3.Equals("capital"))
                        {
                            temp_neighbor.Item1.AddEntityType("capital");
                        }//end if
                    }//end foreach
                    if (temp_feature.HasEntityType(Constant.CHARACTER))
                    {
                        foreach (Tuple<Feature, double, string> temp_neighbor in temp_feature.Neighbors)
                        {
                            //Emperors will have the "title" relationship to either Roman emperor (id 5),
                            //List of Roman emperors (id 581), or List of Byzantine emperors (id 562)
                            if (temp_neighbor.Item1.Name.Equals("Roman emperor")
                                || temp_neighbor.Item1.Name.Equals("List of Roman emperors")
                                || temp_neighbor.Item1.Name.Equals("List of Byzantine emperors"))
                            {
                                temp_feature.AddEntityType("emperor");
                                break;
                            }//end if
                        }//end foreach
                    }//end if
                    if (temp_feature.HasEntityType(Constant.EVENT))
                    {
                        if (temp_feature.Name.Contains("Battle"))
                        {
                            temp_feature.AddEntityType("battle");
                        }//end if
                    }//end if
                }//end foreach

                //Get the root node
				int rootId = -1;
				try {
					features = doc.SelectNodes("AIMind");
					rootId = Convert.ToInt32(features[0].SelectNodes("Root")[0].Attributes["id"].Value);
				}
				catch (Exception) { }
				if (rootId != -1) { result_graph.Root = result_graph.getFeature(rootId); }

                try
                {
                    using (var client = new HttpClient())
                    {
                        string url = "http://localhost:5000/check_file";

                        client.BaseAddress = new Uri(url);
                        client.DefaultRequestHeaders.Accept.Clear();
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        Dictionary<string, string> content = new Dictionary<string, string>
						{
							{"file", file_name}
						};

                        var http_content = new FormUrlEncodedContent(content);
                        HttpResponseMessage response = client.PostAsync(url, http_content).Result;

                        //Read the jsons tring from the http response
                        Task<string> read_string_task = response.Content.ReadAsStringAsync();
                        read_string_task.Wait(100000);

                        string content_string = read_string_task.Result;

                        if (content_string.Equals("false"))
                        {
                            Console.WriteLine("WARNING: File " + file_name + " not found in analogy server.");
                            //TODO: Find way to send XML to server. Sending XML text is too long.
                            //Since the file is not there, send it in its entirety as a string to the analogy server.
                            url = "http://localhost:5000/add_file";

                            string file_data = "";
                            //Read the file's data in as text

							file_data = File.ReadAllText(current_file);

                            content = new Dictionary<string, string>
                            {
                                {"file", file_name},
                                {"data", file_data}
                            };

							using (HttpContent httpContent = new StringContent(JsonConvert.SerializeObject(content),
								                                               Encoding.UTF8,
								                                               "application/json")) {
								response = client.PostAsync(url, httpContent).Result;
							}

                            //Read the jsons tring from the http response
                            read_string_task = response.Content.ReadAsStringAsync();
                            read_string_task.Wait(100000);

                            content_string = read_string_task.Result;
                        }//end if
                    }//end using
                }//end try
                catch (Exception e)
                {
                    Console.WriteLine("Error contacting analogy server: " + e.Message);
                }//end catch

				return result_graph;
			}
			catch (Exception e)
			{
				MessageBox.Show(e.ToString());
				return null;
			}
		}


		/* merge two files*/
		public static FeatureGraph readFeatureGraph2(string toReadPath)
		{
			try
			{
				FeatureGraph result = new FeatureGraph();
				XmlDocument doc = new XmlDocument();//doc is the second document, the one selected to merge with after a file has been opened
				doc.Load(toReadPath);
				XmlNodeList features = doc.SelectNodes("AIMind");
				features = features[0].SelectNodes("Features");
				features = features[0].SelectNodes("Feature");
				int countUp = 0;
				int countUp2 = 0;
				int countD = 0;
				XmlNodeList features2 = docOld.SelectNodes("AIMind");



				if (features2[0] != null)
				{//if the first document opened has features{
					features2 = features2[0].SelectNodes("Feature");///this is put here because it would cause a crash outside if there were no features
					foreach (XmlNode node in features2)
					{
						string id = node.Attributes["data"].Value;
						result.addFeature(new Feature(id));
						countD++;
					}
				}
				foreach (XmlNode node in features){
						bool checkifDuplicatesExist = false;
						foreach (XmlNode nodePrime in features2){                      
							string data1 = node.Attributes["data"].Value;                      
							string data2 = nodePrime.Attributes["data"].Value;                       
							if (data1 == data2)                    //if there are two datas with the same name, merge them
							{                         
								checkifDuplicatesExist = true;                       
							}                   
						}
					if (checkifDuplicatesExist == false){//if there doesn't exist a version of the feature, add one
						countUp++;
						string id = node.Attributes["data"].Value;
						result.addFeature(new Feature(id));
						Feature tmp = result.getFeature(node.Attributes["data"].Value);
						XmlNodeList neighbors = node.SelectNodes("neighbors");
						neighbors = neighbors[0].SelectNodes("neighbor");
						foreach (XmlNode neighborNode in neighbors){
								int dest_number = Convert.ToInt32(neighborNode.Attributes["dest"].Value);// +countUp;// + countUp);
								double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
								tmp.addNeighbor(result.Features[dest_number], weight);
								result.Features[dest_number].addParent(tmp);
								//result.Features[id].addNeighbor(tmp, weight);
						}
						XmlNodeList tags = node.SelectNodes("tag");
							foreach (XmlNode tag in tags)
							{
								string key = tag.Attributes["key"].Value;
								string val = tag.Attributes["value"].Value;
								string type = "0";
								if (tag.Attributes["type"].Value == null)
								{
									type = "0";
								}
								else
								{
									type = tag.Attributes["type"].Value;
								}
								tmp.addTag(key, val, type);
							}
							XmlNodeList speaks = node.SelectNodes("speak");
						  
						foreach (XmlNode speak in speaks){
								tmp.addSpeak(speak.Attributes["value"].Value);
							}
						
					}
					else
					{
						countUp++;
						Feature tmp = result.getFeature(node.Attributes["data"].Value);

						XmlNodeList neighbors = node.SelectNodes("neighbors");
						neighbors = neighbors[0].SelectNodes("neighbor");
						foreach (XmlNode neighborNode in neighbors)
						{
							int id = Convert.ToInt32(neighborNode.Attributes["dest"].Value);// +countUp;// + countUp);
							double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
							tmp.addNeighbor(result.Features[id], weight);
							result.Features[id].addParent(tmp);
							//result.Features[id].addNeighbor(tmp,weight);
						}

						XmlNodeList tags = node.SelectNodes("tag");
						foreach (XmlNode tag in tags)
						{
							string key = tag.Attributes["key"].Value;
							string val = tag.Attributes["value"].Value;
							string type = "0";
							if (tag.Attributes["type"].Value == null)
							{
								type = "0";
							}
							else
							{
								type = tag.Attributes["type"].Value;
							}
							tmp.addTag(key, val, type);
						}

					}
					
				}


				docOld = doc;
				//after loading the data from the two documents, run through the nodes found
				foreach (XmlNode node in features2)///add the features from the second file
				{
					Feature tmp = result.getFeature(node.Attributes["data"].Value);
					XmlNodeList neighbors = node.SelectNodes("neighbors");
					neighbors = neighbors[0].SelectNodes("neighbor");

				   string secDet = Convert.ToString(Convert.ToInt32(node.Attributes["id"].Value) + countUp);
				   

					foreach (XmlNode neighborNode in neighbors)
					{
						int id = Convert.ToInt32(neighborNode.Attributes["dest"].Value) +countUp;// +0 + 1;
						double weight = Convert.ToDouble(neighborNode.Attributes["weight"].Value);
						
						  tmp.addNeighbor(result.Features[id], weight);
						  result.Features[id].addParent(tmp);//add neighbors to node
						//result.Features[id].addNeighbor(tmp,weight);
					}
					XmlNodeList tags = node.SelectNodes("tag");
					foreach (XmlNode tag in tags)
					{
						string key = tag.Attributes["key"].Value;
						string val = tag.Attributes["value"].Value;
						string type = "0";
						if (tag.Attributes["type"].Value == null)
						{
							type = "0";
						}
						else
						{
							type = tag.Attributes["type"].Value;
						}
						tmp.addTag(key, val, type);
					}
					XmlNodeList speaks = node.SelectNodes("speak");
					foreach (XmlNode speak in speaks)
					{
						tmp.addSpeak(speak.Attributes["value"].Value);
					}
				}

				foreach (XmlNode node in features2)
				{
		  
				}

				int rootId = -1;
				try
				{
					features = doc.SelectNodes("AIMind");
					rootId = Convert.ToInt32(features[0].SelectNodes("Root")[0].Attributes["id"].Value);
				}
				catch (Exception) { }
				if (rootId != -1) { result.Root = result.getFeature(rootId); }
				return result;
			}
			catch (Exception e)
			{
				MessageBox.Show(e.ToString());
				return null;
			}
		}
	}
}
