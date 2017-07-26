using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net;
using System.IO;

using Newtonsoft.Json;

namespace Dialogue_Data_Entry
{
	[Serializable]
	public class FeatureGraph
	{
		private Dictionary<int, float> user_interest;
		private List<Feature> features;
		private Feature root;
		private int maxDepth;
		private double maxDistance;

        public string file_name;

		/*
		//map of specific relation usages and their ID
		private Dictionary<string,RelationUsage> relation_usages;
		*/

		//map of all relations
		public Dictionary<string, HashSet<Tuple<string, string>>> relationMap;
		//inverted map of all relations
		public Dictionary<string, HashSet<Tuple<string, string>>> inverse_relationMap;


		//An array of weights, for use in calculations.
		//The indices are as follows:
		//  0 - discuss amount weight
		//  1 - novelty weight
		//  2 - spatial constraint weight
		//  3 - hierarchy constraint weight
		private double[] weight_array;
		public FeatureGraph()
		{
			user_interest = new Dictionary<int, float>();
			features = new List<Feature>();
			root = null;
			maxDepth = -1;
			maxDistance = -1;
			//Default values for weights
			//double discussAmountW = -3.0;
			//double noveltyW = -1.0;
			//double spatialConstraintW = 1.0;
			//double hierachyConstraintW = 1.0;
			//double temporalConstraintW = 1.0;
			weight_array = new double[Constant.WeightArraySize];
			weight_array[Constant.DiscussAmountWeightIndex] = -100.0;
			weight_array[Constant.NoveltyWeightIndex] = -1.0;
			weight_array[Constant.SpatialWeightIndex] = 1.0;
			weight_array[Constant.HierarchyWeightIndex] = 100.0;
			weight_array[Constant.TemporalWeightIndex] = 0.2;
			//joint weight relates to mentioning nodes together
			weight_array[Constant.JointWeightIndex] = 100.0f;
            // How much do we weight following anchor nodes?
            weight_array[Constant.AnchorWeightIndex] = 50.0f;

			relationMap = new Dictionary<string, HashSet<Tuple<string, string>>>();
			inverse_relationMap = new Dictionary<string, HashSet<Tuple<string, string>>>();
		}

        //Resets discussed amount and user interest for all graph nodes
        public void ResetNodes()
        {
            foreach (Feature current_node in features)
            {
                current_node.DiscussedAmount = 0;
                current_node.update_interest_value(0);
            }//end foreach
        }//end method ResetNodes

		private void helperMaxDepthDSF(Feature current, int depth, bool[] checkEntry)
		{
			if (current.Neighbors.Count == 0)
			{
				if (depth > this.maxDepth)
				{
					this.maxDepth = depth;
				}
			}
			
			int index = this.getFeatureIndex(current.Id);
			if (checkEntry[index])
			{
				return;
			}
			checkEntry[index] = true;
			for (int x = 0; x < current.Neighbors.Count; x++)
			{
				helperMaxDepthDSF(current.Neighbors[x].Item1, depth + 1,checkEntry);
			}
		}
		private void helperMaxDepthBFS()
		{
			Feature current = this.Root;
			bool[] checkEntry = new bool[this.Count];
			List<Feature> queue = new List<Feature>();
			queue.Add(current);
			int index=0;
			while (queue.Count > index)
			{
				current = queue[index]; index++;
				if (current.Level > maxDepth)
				{
					maxDepth = current.Level;
				}
				int ind = this.getFeatureIndex(current.Id);
				if (!checkEntry[ind])
				{
					checkEntry[ind] = true;
					for (int x = 0; x < current.Neighbors.Count; x++)
					{
						current.Neighbors[x].Item1.Level = current.Level + 1;
						queue.Add(current.Neighbors[x].Item1);
					}
				}
			}
		}
		//find max shortest path from the giving node and set shortestDistance 
		private double maxDistBFS(Feature node)
		{
			Feature current = node;
			bool[] checkEntry = new bool[this.Count];
			List<Feature> queue = new List<Feature>();
			queue.Add(current);
			int index = 0;
			double maxDistance = 0;
			//clear dist
			for (int x = 0; x < this.Count;x++ )
			{
				this.Features[x].Dist = 0;
			}
			//initialize node's ShortestDistance to zero
			node.ShortestDistance.Clear();
            foreach (Feature graph_node in features)
            {
                node.ShortestDistance.Add(graph_node.Id, 0.0);
            }//end foreach
			while (queue.Count > index)
			{
				current = queue[index]; index++;
				if (current.Dist > maxDistance)
				{
					maxDistance = current.Dist;
				}
				int ind = this.getFeatureIndex(current.Id);
				if (!checkEntry[ind])
				{
					checkEntry[ind] = true;
                    node.ShortestDistance[current.Id] = current.Dist;
					for (int x = 0; x < current.Neighbors.Count; x++)
					{
						if (current.Neighbors[x].Item1.Dist == 0)
						{
							current.Neighbors[x].Item1.Dist = current.Dist + 1;
							queue.Add(current.Neighbors[x].Item1);
						}
					}
					for (int x = 0; x < current.Parents.Count;x++)
					{
						if (current.Parents[x].Item1.Dist == 0)
						{
							current.Parents[x].Item1.Dist = current.Dist + 1;
							queue.Add(current.Parents[x].Item1);
						}
					}
				}
			}
			return maxDistance;
		}
        //Finds the shortest path between each pair of nodes using BFS
        //O(|V|*(|V| + |E|))
        private void allPairShortestPathBFS()
        {
            //ZEV: Using this.Count + 1 in case root node is not 0
            for (int x = 0; x < this.Count; x++)
            {
                this.Features[x].ShortestDistance.Clear();
            }
            //initialize all distance to infinity
            for (int x = 0; x < this.Count; x++)
            {
                foreach (Feature graph_node in features)
                {
                    this.Features[x].ShortestDistance.Add(graph_node.Id, 2147483646);
                }//end foreach
            }

            /*for (int i = 0; i < this.Count; i++)
            {
                if (Features[i].Id != i)
                {
                    Console.WriteLine("Feature not in Id index");
                }//end if
            }//end for
			*/

                //distance to itself is zero
                for (int x = 0; x < this.Count; x++)
                {
                    this.Features[x].ShortestDistance[Features[x].Id] = 0;
                }

            Queue<Feature> bfs_queue = new Queue<Feature>();
            Feature current_feature = null;

            //For every feature, perform BFS.
            foreach (Feature temp_feat in Features)
            {
                //The flag will be used for BFS to prevent revisiting nodes.
                //Clear the flag for each feature.
                foreach (Feature temp in Features)
                    temp.flag = false;
                current_feature = temp_feat;
                current_feature.flag = true;
                //Clear the queue.
                bfs_queue.Clear();
                //Add all of the feature's neighbors to the queue. Set their distances relative to this feature to 1.
                foreach (Tuple<Feature, double, string> neighbor in temp_feat.Neighbors)
                {
                    temp_feat.ShortestDistance[neighbor.Item1.Id] = 1;
                    neighbor.Item1.flag = true;
                    bfs_queue.Enqueue(neighbor.Item1);
                }//end foreach

                while (bfs_queue.Count > 0)
                {
                    //Dequeue a feature.
                    current_feature = bfs_queue.Dequeue();
                    //Add all of its neighbors to the queue and set their distances relative to
                    //the source feature to the current feature's distance + 1.
                    foreach (Tuple<Feature, double, string> inner_neighbor in current_feature.Neighbors)
                    {
                        //If the neighbor's flag is up, it has already been visited. Skip it.
                        if (inner_neighbor.Item1.flag)
                            continue;
                        //Otherwise, enqueue the neighbor, update its shortest distance, and put the flag up.
                        inner_neighbor.Item1.flag = true;
                        temp_feat.ShortestDistance[inner_neighbor.Item1.Id] = temp_feat.ShortestDistance[current_feature.Id] + 1;
                        bfs_queue.Enqueue(inner_neighbor.Item1);
                    }//end foreach
                }//end while

            }//end foreach
            
        }//end method allPairShortestPathBFS
		//update all features' shortestDistance
        //O(|V|^3)
		//shortestDistance has to be empty list
		private void allPairShortestPath()
		{
			for (int x = 0; x < this.Count;x++ )
			{
				this.Features[x].ShortestDistance.Clear();
			}
			//initialize all distance to infinity
			for (int x = 0; x < this.Count; x++)
			{
                foreach (Feature graph_node in features)
                {
                    this.Features[x].ShortestDistance.Add(graph_node.Id, 2147483646); //maxint -1
                }//end foreach
			}
			//distance to itself is zero
			for (int x = 0; x < this.Count; x++)
			{
				this.Features[x].ShortestDistance[Features[x].Id] = 0;
			}
			//for each edge (u,v) [if there is an edge connect between two nodes)
			for (int x = 0; x < this.Count; x++)
			{
				//Children edge
				for (int y = 0; y < this.Features[x].Neighbors.Count; y++)
				{
					int ind = this.getFeatureIndex(this.Features[x].Neighbors[y].Item1.Id);
					double dist = this.Features[x].Neighbors[y].Item2;
					//if dist = 0, set it to 1. This is because the default weight of edge used to be 0.
					if (dist == 0)
					{
						dist = 1;
					}
                    this.Features[x].ShortestDistance[this.Features[x].Neighbors[y].Item1.Id] = dist;
				}
				//Parent edge
				for (int y = 0; y < this.Features[x].Parents.Count;y++)
				{
					int ind = this.getFeatureIndex(this.Features[x].Parents[y].Item1.Id);
					double dist = this.Features[x].Parents[y].Item2;
                    this.Features[x].ShortestDistance[this.Features[x].Parents[y].Item1.Id] = dist;
				}
			}
			//All-pair shortest path
			for (int k = 0; k < this.Count; k++)
			{
				for (int i = 0; i < this.Count; i++)
				{
					for (int j = 0; j < this.Count; j++)
					{
						if (this.Features[i].ShortestDistance[j] > this.Features[i].ShortestDistance[k] + this.Features[k].ShortestDistance[j])
						{
							this.Features[i].ShortestDistance[j] = this.Features[i].ShortestDistance[k] + this.Features[k].ShortestDistance[j];
						}
					}
				}
			}
		}
		private void printShortestDistance()
		{
			for (int x = 0; x < this.Count; x++)
			{
				for (int y = 0; y < this.Count; y++)
				{
					Console.Write(this.Features[x].ShortestDistance[y] +" ");
				}
				Console.WriteLine();
			}
			Console.WriteLine();
		}

		private void interest_visualization(string json)
		{
			var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:9000/callback/userinterest");
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.Method = "POST";

			using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
			{
				Console.WriteLine(json);
				streamWriter.Write(json);
				streamWriter.Flush();
				streamWriter.Close();
			}

			var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			string result = "";
			using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
			{
				result = streamReader.ReadToEnd();
			}
			//Console.WriteLine(result);
			Console.Out.WriteLine("Write back done");
		}

		private string getanalogy(int id)
		{
			var httpWebRequest = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:5000/get_analogy");
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.Method = "POST";

			using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
			{
				string id_ = id.ToString();
				string json = "{\"filename\":\""+ Uri.EscapeDataString(XMLFilerForFeatureGraph.current_file) + "\"," +
							  "\"id\":\"" + id_ + "\"," +
							  "\"mode\":\"all\"}";
				Console.WriteLine(json);
				streamWriter.Write(json);
				streamWriter.Flush();
				streamWriter.Close();
			}

			var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			string result = "";
			using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
			{
				result = streamReader.ReadToEnd();
			}
			return result;

		}
		public class AnalogyResponse
		{
			public class AnalogyObject
			{
				[JsonProperty(PropertyName = "source")]
				public int sourceID;
				[JsonProperty(PropertyName = "target")]
				public int targetID;
				//[JsonProperty(PropertyName = "evidence")]
				//public string evidence;
				//[JsonProperty(PropertyName = "connections")]
				//public string connections;
				//[JsonProperty(PropertyName = "explanation")]
				//public string explanation;
				[JsonProperty(PropertyName = "n_rating")]
				public float n_rating;
				//[JsonProperty(PropertyName = "rating")]
				//public string rating;
			}
			[JsonProperty(PropertyName = "data")]
			public List<AnalogyObject> response;
			[JsonProperty(PropertyName = "count")]
			public int count;
		}
		private static void parse_json(string json, ref Dictionary<int, float> user_interest)
		{
			//Console.Out.WriteLine(json);
			var data = JsonConvert.DeserializeObject<AnalogyResponse>(json);
			foreach (var ao in data.response)
			{
				if (user_interest.ContainsKey(ao.targetID))
				{
					user_interest[ao.targetID] += ao.n_rating;
				}
				else
				{
					user_interest.Add(ao.targetID, ao.n_rating);
				}
			}
		}
		public void update_interest_analogy(int id, int it_)
		{
			string ans = getanalogy(id);

			parse_json(ans, ref user_interest);

			string json_out = "";
			json_out += "{\"filename\":\"" + Uri.EscapeDataString(XMLFilerForFeatureGraph.current_file) + "\",";
			json_out += "\"total_max_score\":\"" + it_.ToString() + "\",";
			foreach (KeyValuePair<int, float> pair in user_interest)
			{
				json_out += "\"" + pair.Key.ToString() + "\":";
				json_out += "\"" + pair.Value.ToString() + "\",";

				this.getFeature(pair.Key).update_interest_value(pair.Value);

			}
			json_out = json_out.Substring(0, json_out.Length - 1);
			json_out += "}";
			interest_visualization(json_out);
			//Console.Out.WriteLine(json_out);

		}
        public void UpdateInterestInternal(int id, int iterations)
        {
            string analogy_response = getanalogy(id);

            parse_json(analogy_response, ref user_interest);

            foreach (KeyValuePair<int, float> pair in user_interest)
            {
                this.getFeature(pair.Key).update_interest_value(pair.Value);
            }//end foreach

        }//end method UpdateInterestInternal
		public void getMaxDistance()
		{
			//var sw = new Stopwatch();
			//sw.Start();
			
			//allPairShortestPath();
            allPairShortestPathBFS();

			//sw.Stop();
			//Console.WriteLine("All-pair took "+sw.Elapsed);
			
			//find max distance
			for (int x = 0; x < this.Count;x++)
			{
                foreach (Feature graph_node in features)
                {
                    if (this.Features[x].ShortestDistance[graph_node.Id] > this.MaxDistance)
                    {
                        this.maxDistance = this.Features[x].ShortestDistance[graph_node.Id];
                    }
                }//end foreach
			}
			//printShortestDistance();
		}
		public int getMaxDepth()
		{
			if(maxDepth==-1)
			{
				if (root != null)
				{
					//bool[] checkEntry = new bool[this.Count]; 
					//helperMaxDepthDSF(root, 0, checkEntry);
					helperMaxDepthBFS();
				}
			}
			return maxDepth;
		}
		public void setMaxDepth(int h)
		{
			maxDepth = h;
		}
		//Get a single weight from the weight array
		public double getSingleWeight(int weight_index)
		{
			if (weight_index < 0 || weight_index >= weight_array.Length)
				return -1;
			return weight_array[weight_index];
		}//end method getSingleWeight
		//Get the entire weight array
		public double[] getWeightArray()
		{
			return weight_array;
		}//end method getWeightArray
		//Set a single weight in the weight array
		public void setWeight(int weight_index, double weight_to_set)
		{
			if (weight_index < 0 || weight_index >= weight_array.Length)
				return;
			weight_array[weight_index] = weight_to_set;
		}//end method setWeight
		public bool addFeature(Feature toAdd)
		{
            //If there is nothing else in the feature list, just add the feature
            if (features.Count == 0)
                features.Add(toAdd);
            //Otherwise, starting from the front of the list, find the first feature whose id is greater.
            //This will sort the list by increasing order of id as it is created.
            else
            {
                bool feature_added = false;
                foreach (Feature existing_feature in features)
                {
                    if (existing_feature.Id > toAdd.Id)
                    {
                        features.Insert(features.IndexOf(existing_feature), toAdd);
                        feature_added = true;
                        break;
                    }//end if
                }//end foreach
                if (!feature_added)
                    features.Add(toAdd);
            }//end else
			//features.Add(toAdd);
			return true;
		}
		public bool setFeature(int id, Feature change)
		{
			int i = getFeatureIndex(id);
			if (i != -1)
			{
				features[i].Id = change.Id;
				features[i].Neighbors = change.Neighbors;
				features[i].Tags = change.Tags;
				features[i].DiscussedAmount = change.DiscussedAmount;
				features[i].DiscussedThreshold = change.DiscussedThreshold;
				features[i].flag = change.flag;
				features[i].Speaks = change.Speaks;
				return true;
			}
			return false;
		}
		public bool setFeatureDiscussedAmount(int id, float amount)
		{
			int i = getFeatureIndex(id);
			if (i != -1)
			{
				features[i].DiscussedAmount = amount;
				return true;
			}
			return false;
		}
		public bool setFeatureId(int index, int new_id)
		{
			if (index >= 0 && index < features.Count)
			{
				features[index].Id = new_id;
				return true;
			}
			return false;
		}//end function setFeatureId
		public bool setFeatureId(int index, string name)
		{
			if (index >= 0 && index < features.Count)
			{
				features[index].Name = name;
				return true;
			}
			return false;
		}//end function setFeatureName
		public bool setFeatureNeighbors(int index, List<Tuple<Feature, double,string>> newNeighbors)
		{
			if (index >= 0 && index < features.Count)
			{
				features[index].Neighbors = newNeighbors;
				return true;
			}
			return false;
		}
		public bool setFeatureTags(int index, List<Tuple<string, string, string>> newTags)
		{
			if (index >= 0 && index < features.Count)
			{
				features[index].Tags = newTags;
				return true;
			}
			return false;
		}
		public Feature getFeature(string name)
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Name.Equals(name)) { return features[x]; }
			}
			return null;
		}//end function getFeature
		public Feature getFeature(int id)
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Id == id) { return features[x]; }
			}
			return null;
		}//end function getFeature
		/*public Feature getFeature(int index)
		{
			return features[index];
		}*/
		public int getFeatureIndex(int id)
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Id == id)
				{
					return x;
				}
			}
			return -1;
			throw new Exception("If you see this msg when you save the file. Please report and don't close your program.");
		}//end function getFeatureIndex
		public bool hasNode(int id)
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Id == id) { return true; }
			}
			return false;
		}//whether or not the feature graph has this node
		public bool hasNode(string name)
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Name == name) { return true; }
			}
			return false;
		}//whether or not the feature graph has this node
		public bool connect(string A, string B, double weight = 1.0)
		{
			if (A == null || B == null) { throw new Exception("You cannot create a connection between two features if one is null"); }
			if (getFeature(A) == null || getFeature(B) == null) { throw new Exception("You cannot create a connection between two features if one of them is not in this FeatureGraph"); }
			if (A == B) { throw new Exception("You cannot connect a Feature to itself"); }
			getFeature(A).addNeighbor(getFeature(B), weight);
			getFeature(B).addParent(getFeature(A));
			//getFeature(B).addNeighbor(getFeature(A),weight);
			return true;
		}
		public void print()
		{
			for (int x = 0; x < features.Count; x++)
			{
				features[x].print();
				System.Console.WriteLine("\n");
			}
		}
		public void resetAllFlags()
		{
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].flag) { features[x].resetReachableFlags(); }
			}
		}
		public List<string> getFeatureNames()
		{
			List<string> names = new List<string>();
			for (int x = 0; x < features.Count; x++)
			{
				names.Add(features[x].Name);
			}
			return names;
		}
		public bool removeFeature(string name)
		{
			for (int x = 0; x < features.Count; x++)
			{
				features[x].removeNeighbor(name);
			}
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Name == name)
				{
					features.RemoveAt(x);
					return true;
				}
			}
			return false;
		}//end function removeFeature
		public bool removeFeature(int id)
		{
			for (int x = 0; x < features.Count; x++)
			{
				features[x].removeNeighbor(id);
			}
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Id == id)
				{
					features.RemoveAt(x);
					return true;
				}
			}
			return false;
		}//end function removeFeature
		public bool removeDouble(int id)
		{
			bool xyz = false;
			for (int x = 0; x < features.Count; x++)
			{
				if (features[x].Id == id && xyz == false)
				{
					xyz = true;
					features.RemoveAt(x);
					return true;
				}
			}
			return false;
		}
		public double MaxDistance
		{
			get { return this.maxDistance; }
		}
		public int Count
		{
			get { return this.features.Count; }
		}
		public Feature Root
		{
			get
			{
				return this.root;
			}
			set
			{
				if (value == null)
				{
					throw new Exception("You cannot set the root to null");
				}
				else //if (this.root == null)
				{
					this.root = value;
				}
			}
		}
		public List<Feature> Features
		{
			get { return this.features; }
		}
	}
}
