using System;   
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dialogue_Data_Entry
{
    [Serializable]
    public class Feature
    {
        private float interest_value;                    // This is the user interest value.
        private float discussedAmount;                   // This is the "ammount" that this feature has beed the topic of the conversation
        private float discussedThreshold;                // This is the threshold that when reached the topic will have beed exhausted
        private string name;                             // This is the name of this feature.
        private int id;                                  // The id of the feature, as it appears in the xml.
        private List<Tuple<Feature, double, string>> neighbors;  // This is a list of tuples that contain all of the features that can be reached from this topic and a weight that defines how distanced they are from the parent feature (this feature).
                                                                    //Item 1 is the neighbor feature, Item 2 is the weight between it and this feature, and item 3 is the string relationship between the two.
        public Dictionary<int, Feature> neighbor_dictionary;   // A list of all neighbors in dictionary form, indexed by feature id, for quick lookups.
        private List<Tuple<Feature, double, string>> parents;    // This is a HashSet of features that can be reached to this feature node.
        //Item 1 is the neighbor feature, Item 2 is the weight between it and this feature, and item 3 is the string relationship between the two.
        private List<Tuple<string, string, string>>  tags;       // This is a list of tuples that are used to store the tags (generic, single use pices of information). The first element is the key, and the second element is the id. This will simply operate as a map.
        private List<string> speaks;

        private List<Tuple<string, string>> timedata;       //A node's time data is a list of timeobj tags in the xml.
                                                            //timeobj is a string relationship and a string value.
        private List<Tuple<double, double>> geodata;        //A node's geospatial data is a list of coordinates.
                                                            //First member of tuple is latitude, second is longitude.
        public DateTime start_date;                         //Start date for this feature if it has time data.
        public DateTime end_date;                           //End date for this feature if it has time data.
                                                            //If the feature only has a single date, start and end dates will be equal.
        public DateTime effective_date;                     //If a single date is required for this feature, the effective date should be used.
                                                            //Effective date can be changed according to need (e.g., effective date relative to a certain feature)
        public List<Feature> local_history_list;

        public int story_role;                              //Integer flag identifying what role this feature plays in a story.
                                                             //  0 = Concept (default)
                                                             //  1 = Character
                                                             //  2 = Location
                                                             //  3 = Event
                                                             //  4 = Political/Social Entity
        public List<string> entity_type;                    //What type of entity this feature represents.
                                                            //Redundant with story role, remove story role in future.
        
        
        private Dictionary<double, double> shortestDistance;         //list of shortestDistance to all nodes (index is id)
        private int level, dist;
        public bool flag;                                // This is a public general use flag that can be used for things like traversals and stuff like that

        public List<Tuple<int, string, int>> constraints;

        public Feature(string name)
        {
            this.interest_value = 0;
            this.speaks = new List<string>();
            this.name = name;
            this.id = 0;
            this.neighbors = new List<Tuple<Feature, double, string>>();
            this.neighbor_dictionary = new Dictionary<int, Feature>();
            this.tags = new List<Tuple<string, string, string>>();
            this.flag = false;
            this.parents = new List<Tuple<Feature, double, string>>();
            this.level = 0;
            this.dist = 0;
            this.shortestDistance = new Dictionary<double, double>();
            this.timedata = new List<Tuple<string, string>>();
            this.geodata = new List<Tuple<double, double>>();

            this.start_date = new DateTime();
            this.end_date = new DateTime();
            this.effective_date = new DateTime();
            this.story_role = 0;
            this.entity_type = new List<string>();

            this.local_history_list = new List<Feature>();

            this.constraints = new List<Tuple<int, string, int>>();
        }//end constructor Feature
        public Feature(string name, int id)
        {
            this.interest_value = 0;
            this.speaks = new List<string>();
            this.name = name;
            this.id = id;
            this.neighbors = new List<Tuple<Feature, double, string>>();
            this.neighbor_dictionary = new Dictionary<int, Feature>();
            this.tags = new List<Tuple<string, string, string>>();
            this.flag = false;
            this.parents = new List<Tuple<Feature, double, string>>();
            this.level = 0;
            this.dist = 0;
            this.shortestDistance = new Dictionary<double, double>();
            this.timedata = new List<Tuple<string, string>>();
            this.geodata = new List<Tuple<double, double>>();

            this.start_date = new DateTime();
            this.end_date = new DateTime();
            this.effective_date = new DateTime();
            this.story_role = 0;
            this.entity_type = new List<string>();

            this.local_history_list = new List<Feature>();

            this.constraints = new List<Tuple<int, string, int>>();
        }//end constructor Feature

        //Calculate start and end dates for this feature, and, if possible, assign it
        //the appropriate story role.
        public void calculateDate()
        {
            //Gather possible start and end dates.
            List<string> start_dates = new List<string>();
            List<string> end_dates = new List<string>();
            //Look through each piece of time data. First item is relationship, second is value.
            //This first pass is to try and determine the story role this feature plays based on its time data.
            foreach (Tuple<string, string> data_piece in timedata)
            {
                //Characters usually have birth and/or death dates
                //Check for birth date
                if (data_piece.Item1.Contains("birth"))
                {
                    //If this feature is still considered a general concept, mark it as a character
                    if (story_role == 0)
                        story_role = 1;
                    //Add this date to the list of start dates
                    start_dates.Add(data_piece.Item2);
                }//end if
                //Check for death date
                else if (data_piece.Item1.Contains("death"))
                {
                    //If this feature is still considered a general concept, mark it as a character
                    if (story_role == 0)
                        story_role = 1;
                    //Add this date to the list of end dates
                    end_dates.Add(data_piece.Item2);
                }//end else if

                //Political and Social entities usually have founding and dissolution dates
                //Check for founding date
                else if (data_piece.Item1.Contains("founding"))
                {
                    //If this feature is still considered a general concept, mark it as a political/social entity
                    if (story_role == 0)
                        story_role = 4;
                    //Add this to the list of start dates
                    start_dates.Add(data_piece.Item2);
                }//end if
                //Check for dissolution date
                else if (data_piece.Item1.Contains("dissolution"))
                {
                    //If this feature is still considered a general concept, mark it as a political/social entity
                    if (story_role == 0)
                        story_role = 4;
                    //Add this to the list of end dates
                    end_dates.Add(data_piece.Item2);
                }//end if
            }//end foreach
            //If start or end dates are empty, make a second pass checking the general cases for start and end dates.
            if (start_dates.Count <= 0)
            {
                foreach (Tuple<string, string> data_piece in timedata)
                {
                    //Check for start date
                    if (data_piece.Item1.Contains("start"))
                    {
                        //Add this to the list of start dates
                        start_dates.Add(data_piece.Item2);
                    }//end if
                }//end foreach
            }//end if
            if (end_dates.Count <= 0)
            {
                foreach (Tuple<string, string> data_piece in timedata)
                {
                    //Check for end date
                    if (data_piece.Item1.Contains("end"))
                    {
                        //Add this to the list of end dates
                        end_dates.Add(data_piece.Item2);
                    }//end if
                }//end foreach
            }//end if
            //If neither start nor end dates have been found, make a third pass looking for any dates.
            //These dates will be stored only as start dates.
            if (start_dates.Count <= 0 && end_dates.Count <= 0)
            {
                foreach (Tuple<string, string> data_piece in timedata)
                {
                    start_dates.Add(data_piece.Item2);
                }//end foreach
            }//end if


            //Choose the most informative start and end dates as this feature's start and end dates.
            //First pass, try to convert each string to a datetime.
            foreach (string temp_start in start_dates)
            {
				if(DateTime.TryParse(temp_start, out start_date)) {
					//If this succeeds, we are done.
					break;
				}                   
            }//end foreach
            foreach (string temp_end in end_dates)
            {
				if (DateTime.TryParse(temp_end, out end_date)) {
					//If this succeeds, we are done.
					break;
                }
            }//end foreach
            //If there is still no start date, perform a second pass
            if (start_date == default(DateTime))
            {
                //Second pass, try to extract digits and get a year
                foreach (string temp_start in start_dates)
                {
                    int start_year = 1;
                    if (!int.TryParse(System.Text.RegularExpressions.Regex.Match(temp_start, @"\d+").Value, out start_year))
                    {
                        //If this parse doesn't work, the start date is the default datetime value.
                        start_date = new DateTime();
                    }//end if
                    else
                    {
                        //If the year is 0, default it to 1.
                        if (start_year == 0)
                            start_year = 1;

						//for when DBpedia screws up and joins two years, take part of it
						if(start_year > 10000000) {
							start_year = start_year % 10000;
						}

                        //If the parse does work, the start date is the first day of the year identified.
                        start_date = new DateTime(start_year, 1, 1);
                        //Now that we have a start date, we are done.
                        break;
                    }//end else
                }//end foreach
            }//end if
            //If there is still no end date, perform a second pass
            if (end_date == default(DateTime))
            {
                //Second pass, try to extract digits and get a year
                foreach (string temp_end in end_dates)
                {
                    int end_year = 0;
                    if (!int.TryParse(System.Text.RegularExpressions.Regex.Match(temp_end, @"\d+").Value, out end_year))
                    {
                        //If this parse doesn't work, the end date is the default datetime value.
                        end_date = new DateTime();
                    }//end if
                    else
                    {
                        //If the parse does work, the end date is the first day of the year identified.
                        end_date = new DateTime(end_year, 1, 1);
                        //Now that we have a nend date, we are done.
                        break;
                    }//end else
                }//end foreach
            }//end if

            //If either start or end date is the default value and the other isn't, make them match
            if (start_date != default(DateTime) && end_date == default(DateTime))
            {
                end_date = start_date;
            }//end if
            else if (start_date == default(DateTime) && end_date != default(DateTime))
            {
                start_date = end_date;
            }//end else if


/*
			    tn.datevalue = tn.date.ToShortDateString();
			    tn.dateticks = tn.date.Ticks;
			    nodeList.Add(tmp_obj);
			    nodeDict[f.data] = tmp_obj;
		    }
		    long maxdays = int.MinValue;
		    long mindays = int.MaxValue;
		    foreach (GameObject node in nodeList) {
			    timelineNode tn = node.GetComponent<timelineNode>();
			    int totaldays = 365 * tn.date.Year + tn.date.DayOfYear;
			    if (totaldays > maxdays) maxdays = totaldays;
			    if (totaldays < mindays) mindays = totaldays;
		    }
		    foreach (GameObject node in nodeList) {
			    timelineNode tn = node.GetComponent<timelineNode>();
			    int totaldays = 365 * tn.date.Year + tn.date.DayOfYear;
			    tn.transform.position = new Vector3(map(totaldays,mindays,maxdays,0,100), 0, 0);
		    }*/
        }//end method calculateDate

        //Calculate the effective date of this feature from the given start and end dates and the
        //feature's dates
        public void calculateEffectiveDate(DateTime start, DateTime end)
        {
            //The effective date is the earliest date for this node that lies within
            //the given start and end dates.
            List<Tuple<string, string>> data_to_parse = new List<Tuple<string, string>>();
            List<DateTime> feature_dates = new List<DateTime>();
            DateTime temp_date = new DateTime();

            //First pass, try to convert each date string in this feature's timedata to a datetime.
            //Item 1 of the tuple is the timedata's relationship, Item 2 is the date string.
            foreach (Tuple<string, string> data_piece in timedata)
            {
                try
                {
                    //Try to convert to a datetime
                    temp_date = Convert.ToDateTime(data_piece.Item2);
                    //If this succeeds, add it to the list of dates for this feature
                    feature_dates.Add(temp_date);
                }//end try
                catch
                {
                    //If this fails, place this tuple in the list of tuples for a second pass.
                    data_to_parse.Add(data_piece);
                }//end catch
            }//end foreach
            //Second pass, try to extract digits and get a year.
            if (data_to_parse.Count > 0)
            {
                foreach (Tuple<string, string> data_piece in data_to_parse)
                {
                    int temp_year = 1;
                    if (!int.TryParse(System.Text.RegularExpressions.Regex.Match(data_piece.Item2, @"\d+").Value, out temp_year))
                    {
                        //If this parse doesn't work, don't add the date to the list of dates.
                    }//end if
                    else
                    {
                        //If the year is 0, default it to 1.
                        if (temp_year == 0)
                            temp_year = 1;
                        //If the parse does work, the date is the first day of the year identified.
                        temp_date = new DateTime(temp_year, 1, 1);
                        feature_dates.Add(temp_date);
                    }//end else
                }//end foreach
            }//end if

            DateTime result_date = DateTime.MaxValue;
            foreach (DateTime feature_date in feature_dates)
            {
                //Find the earliest date after the start date and before the end date.
                if (feature_date >= start && feature_date <= end && feature_date < result_date)
                    result_date = feature_date;
            }//end foreach

            //The date we calculate above is the effective date.
            effective_date = result_date;
        }//end method calculateEffectiveDate

        //Build the neighbor dictionary
        public void buildNeighborDictionary()
        {
            foreach (Tuple<Feature, double, string> neighbor_tuple in neighbors)
            {
                neighbor_dictionary.Add(neighbor_tuple.Item1.id, neighbor_tuple.Item1);
            }//end foreach
        }//end method buildNeighborDictionary

        // This function is used to get a Feature that is a neighbor of this Feature, it takes an string name and preforms a binary search over the list
        public Feature getNeighbor(string name)
        {
            int imax = neighbors.Count - 1;
            int imin = 0;
            while (imax >= imin)
            {
                int imid = (imax + imin) / 2;
                if (String.Compare(neighbors[imid].Item1.Name, name) < 0)
                {
                    imin = imid + 1;
                }
                else if (String.Compare(neighbors[imid].Item1.Name, name) > 0)
                {
                    imax = imid - 1;
                }
                else
                {
                    return neighbors[imin].Item1;
                }
            }
            return null;
        }
        // This function is used to get a Feature that is a neighbor of this Feature.
        public Feature getNeighbor(int neighbor_id)
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i].Item1.Id == neighbor_id)
                    return neighbors[i].Item1;
            }//end for
            return null;
        }//end function getNeighbor

        //Finds all the neighbors of this feature that have the given relationship
        //with this feature.
        public Feature[] GetNeighborsByRelationship(string relationship)
        {
            List<Feature> temp_neighbors = new List<Feature>();
            var neighbors = this.Neighbors;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var triple = neighbors[i];
                Feature neighbor = triple.Item1;
                string relation = triple.Item3;
                if (relation.ToLower().Replace(' ', '_') == relationship.ToLower())
                    temp_neighbors.Add(neighbor);
            }
            return temp_neighbors.ToArray();
        }//end function FindNeighborsByRelationship

        // This function will get the respective edge weight along the connection between this feature and the feature with the given id

        public void update_interest_value(float value)
        {
            this.interest_value = value;
        }

        public float getInterestValue()
        {
            return this.interest_value;
        }//end method getInterestValue

        public double getNeighborWeight(int id)
        {
            if (neighbors.Count == 0) { return -1.0; }
            int checkIndex = neighbors.Count / 2;
            int tmp = checkIndex;
            Feature check = neighbors[checkIndex].Item1;
            if (check.Id == id) { return neighbors[checkIndex].Item2; }
            while (check.Id != id)
            {
                tmp = checkIndex;
                if (neighbors[checkIndex].Item1.Id == id) { return neighbors[checkIndex].Item2; }
                    //ZEV: Check that this still actually works!!!
                else if (neighbors[checkIndex].Item1.id > id) { checkIndex += (checkIndex / 2) - 1; }
                else { checkIndex -= (checkIndex / 2) + 1; }
                if (tmp == checkIndex) { return -1; }
                check = neighbors[checkIndex].Item1;
            }
            if (check.Id == id)
            {
                return neighbors[checkIndex].Item2;
            }
            return -1.0;
        }
        // This function will get the respective edge weight along the connection between this feature and the specific one at the passed index
        /*public double getNeighborWeight(int index)
        {
            return this.neighbors[index].Item2;
        }*/
        // This function will return the number of neighboring features that are connected to this feature
        public int getNeighborCount()
        {
            return this.neighbors.Count;
        }
        // This function will add a new feature to the neighbors given a new feature and a weight (weight is defaulted to one). 
        // This function will add a neighbor in numerical order (from lowest ID to highest)
        public bool addNeighbor(Feature neighbor, double weight = 1.0, string relationship="")
        {
            if (neighbors.Count == 0)
            {
                neighbors.Add(new Tuple<Feature, double, string>(neighbor, weight, relationship));
                return true;
            }
            for (int x = 0; x < neighbors.Count; x++)
            {
                //Don't allow any duplicate entries
                if (neighbor.Id == neighbors[x].Item1.Id) { return false; }

                if (neighbor.Id < neighbors[x].Item1.Id)
                {
                    neighbors.Insert(x, new Tuple<Feature, double, string>(neighbor, weight,relationship));
                    return true;
                }
            }
            neighbors.Add(new Tuple<Feature, double, string>(neighbor, weight, relationship));
            return true;
        }

        public bool addParent(Feature parent, double weight = 1.0, string relationship = "")
        {
            if (parents.Count == 0)
            {
                parents.Add(new Tuple<Feature, double, string>(parent, weight, relationship));
                return true;
            }
            for (int x = 0; x < parents.Count; x++)
            {
                if (parent.Id == parents[x].Item1.Id) { return false; }
                if (parent.Id < parents[x].Item1.Id)
                {
                    parents.Insert(x, new Tuple<Feature, double, string>(parent, weight, relationship));
                    return true;
                }
            }
            this.parents.Add(new Tuple<Feature, double, string>(parent, weight, relationship));
            return true;
        }

        //Gets the relationship between this feature and a neighbor from the relationship list by name
        public string getRelationshipNeighbor(string neighbor_name)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Name == neighbor_name)
                {
                    return neighbors[x].Item3;
                }
            }
            return "";
        }//end function getRelationshipNeighbor
        //Gets the relationship between this feature and a neighbor from the relationship list by id
        public string getRelationshipNeighbor(int neighbor_id)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Id == neighbor_id)
                {
                    return neighbors[x].Item3;
                }
            }
            return "";
        }//end function getRelationshipNeighbor

        /// <summary>
        /// Quantifies how much this feature's neighbors have been discussed.
        /// </summary>
        public double getNeighborDiscussAmount()
        {
            double sumTalk = 0.0;
            double sumNotTalk = 0.0;
            for (int x = 0; x < this.Parents.Count; x++)
            {
                List<Tuple<Feature, double, string>> neighbors = this.Parents[x].Item1.Neighbors;
                for (int y = 0; y < neighbors.Count; y++)
                {
                    //check all other nodes except itself
                    if (neighbors[y].Item1.Id != this.Id)
                    {
                        if (neighbors[y].Item1.DiscussedAmount >= 1)
                        {
                            sumTalk++;
                        }
                        else
                        {
                            sumNotTalk++;
                        }
                    }
                }
            }
            //about itself
            if (this.DiscussedAmount >= 1)
            {
                sumTalk++;
            }
            else
            {
                sumNotTalk++;
            }

            return sumNotTalk / (sumTalk + sumNotTalk);
        }//end function getNeighborDiscussAmount

        public string getRelationshipParent(int parent_id)
        {
            for (int x = 0; x < parents.Count; x++)
            {
                if (parents[x].Item1.Id == parent_id)
                {
                    return parents[x].Item3;
                }
            }
            return "";
        }//end function getRelationshipParent
        public string getRelationshipParent(string parent_name)
        {
            for (int x = 0; x < parents.Count; x++)
            {
                if (parents[x].Item1.Name == parent_name)
                {
                    return parents[x].Item3;
                }
            }
            return "";
        }//end function getRelationshipParent

        //Gets the weight between this feature and the neighbor with the given id.
        public string getWeight(int neighbor_id)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Id == neighbor_id)
                {
                    return Convert.ToString(neighbors[x].Item2);
                }
            }
            return "";
        }//end function getWeight
        public string getWeight(string neighbor_name)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Name == neighbor_name)
                {
                    return Convert.ToString(neighbors[x].Item2);
                }
            }//end for
            return "";
        }//end function getWeight

        //set relationship between this feature and the given neighbor
        public bool setNeighbor(Feature neighbor, double weight, string relationship)
        {
            //removeNeighbor(neighbor.Id);
            //addNeighbor(neighbor, 0.0, relationship);
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Id == neighbor.Id)
                {
                    neighbors[x] = new Tuple<Feature, double, string>(neighbors[x].Item1, weight, relationship);
                    return true;
                }
            }
            return false;
        }

        //set relationship between this feature and the given parent
        public bool setParent(Feature parent, double weight, string relationship)
        {
            for (int x = 0; x < parents.Count; x++)
            {
                if (parents[x].Item1.Id == parent.Id)
                {
                    parents[x] = new Tuple<Feature, double, string>(parents[x].Item1, weight, relationship);
                    return true;
                }
            }
            return false;
        }

        // This function will remove a neighbor that has the given id
        public bool removeNeighbor(int id)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Id == id)
                {
                    neighbors.RemoveAt(x);
                    return true;
                }
            }
            return false;
        }//end function removeNeighbor
        public bool removeNeighbor(string name)
        {
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.Name == name)
                {
                    neighbors.RemoveAt(x);
                    return true;
                }
            }
            return false;
        }//end function removeNeighbor

        public bool removeParent(int id)
        {
            for (int x = 0; x < parents.Count; x++)
            {
                if (parents[x].Item1.Id == id)
                {
                    parents.RemoveAt(x);
                    return true;
                }
            }
            return false;
        }

        //Add a new piece of time data to this feature
        public void addTimeData(string relationship, string value)
        {
            timedata.Add(new Tuple<string, string>(relationship, value));
        }//end method setTimeData

        //Add a new piece of geospatial data to this feature
        public void addGeoData(double latitude, double longitude)
        {
            geodata.Add(new Tuple<double, double>(latitude, longitude));
        }//end method addSpatialData

        //Add an entity type
        public void AddEntityType(string entity_type_to_add)
        {
            //Prevent duplicate entries
            if (!entity_type.Contains(entity_type_to_add))
            {
                entity_type.Add(entity_type_to_add);
            }//end if
        }//end method AddEntityType
        //Check for an entity type
        public bool HasEntityType(string entity_type_to_find)
        {
            if (entity_type.Contains(entity_type_to_find))
                return true;
            else
                return false;
        }//end method HasEntityType

        // This function will check through all of the features that can be reached from its neighbors and if it finds the one that we are looking for it return true, false otherwise
        private bool canReachHelper(int dest_id,bool checkLevel)
        {
            this.flag = true;
            if (this.id == dest_id) { return true; }
            for (int x = 0; x < neighbors.Count; x++)
            {
                // checkLevel -> (this.Level < neighbors[x].Item1.Level) {material condition}
                if (neighbors[x].Item1.flag == false && (!checkLevel ||(this.Level < neighbors[x].Item1.Level)) )
                {
                    if (neighbors[x].Item1.canReachHelper(dest_id, checkLevel)) 
                    {
                        return true; 
                    }
                }
            }
            return false;
        }
        // This function will call and return the canReachHelper method, but it will also rest all of the flags before it is done
        public bool canReachFeature(int dest_id,bool checkLevel=false)
        {
            bool tmp = canReachHelper(dest_id, checkLevel);
            resetReachableFlags();
            return tmp;
        }

        public string getSpeak(int index)
        {
            return this.speaks[index];
        }
        public void addSpeak(string newSpeak)
        {
            this.speaks.Add(newSpeak);
        }
        public void editSpeak(int toEdit, string edit)
        {
            if (toEdit < 0 || toEdit >= this.speaks.Count)
            {
                return;
            }
            this.speaks[toEdit] = edit;
        }
        public void removeSpeak(int index)
        {
            this.speaks.RemoveAt(index);
        }

        // This function will return a tuple that represents a tag that is stored in the topic, it does this by linear search, it will then update the lodations of the tags so that 
        //      the most recently accessed tags are at the top of the list and the ones that are older will begin to percolate down, basically this list is sorted by which tag was
        //      most recently accessed. This runs in O(2n) or O(n) where n is the number of elements in the list
        public Tuple<string, string, string> getTag(string key)
        {
            for (int x = tags.Count - 1; x >= 0; x--)                                // So for each tag that we have starting at the bottom and working to the top
            {
                if (tags[x].Item1 == key)                                            // If the current element is what we are looking for
                {
                    if (tags.Count == 1 || x == tags.Count - 1) { return tags[x]; }   // And if we only have one tag, or the tag that we have found is already at the bottom, return it
                    Tuple<string, string, string> tmp = tags[x];                              // Otherwise, store the tag that we have in a temp
                    for (int y = x; y < tags.Count - 1; y++)                          // And for each element that is below the element that we have found
                    {
                        tags[y] = tags[y + 1];                                        // Increase the index of each element below it by one, overwriting the element that we were looking
                                                                                      //      for, and leaving the bottom element unchanged
                    }
                    tags[tags.Count - 1] = tmp;                                       // Then set the last element to the value that we saved
                    return tmp;                                                   // lastly return it
                }
            }
            return null;                                                              // If we never did find it, then return null
        }
        //find the tag type from the input
        //return the tuple of that tag if exist, otherwise null
        public Tuple<string,string,string> findTagType(string type)
        {
            for (int x = 0; x < this.tags.Count();x++)
            {
                if (tags[x].Item3 == type)
                {
                    return tags[x];
                }
            }
            return null;
        }

        // This function will add a new tag to the list of tags, it does NOT do this in order and simply appends this to the end of the list.
        public void addTag(string key, string value, string type)
        {
            if (getTag(key) != null) { throw new Exception("Cannot have two tags with the same keys - Error occured in Feature: " + id + " for the key " + key + " and the value " + value); }
            tags.Add(new Tuple<string, string, string>(key, value, type));
        }
        // This function will either edit an already existing tag, or it will create a new tag based on the key and id that was passed
        public bool editExistingTag(string key, string new_id, string type, bool debug = false)
        {
            bool success = removeTag(key);
            if (debug && !success)
            {
                System.Console.WriteLine("When editing the tag " + key + ", there was no tag found with that key. A new key has been added with the passed information");
            }
            addTag(key, new_id, type);
            return true;
        }
        // This function will remove the respective tag that is using the key that was passed
        public bool removeTag(string key)
        {
            for (int x = 0; x < tags.Count; x++)
            {
                if (tags[x].Item1 == key)
                {
                    tags.RemoveAt(x);
                    return true;
                }
            }
            return false;
        }
        // This function will return a list of all of the keys that are tags for this feature
        public List<string> getTagKeys()
        {
            List<string> toReturn = new List<string>();
            for (int x = 0; x < tags.Count; x++)
            {
                toReturn.Add(Tags[x].Item1);
            }
            return toReturn;
        }
        // This function will return true if there is already a tag with the passed key and false otherwise
        public bool hasTagWithKey(string key)
        {
            for (int x = 0; x < tags.Count; x++)
            {
                if (tags[x].Item1 == key) { return true; }
            }
            return false;
        }
        // This function will remove all of the tags that are on this topic and delete them
        public void clearTags()
        {
            tags.Clear();
        }

        //Accessors and Mutators
        public float DiscussedAmount
        {
            get{return this.discussedAmount;}
            set
            {
                if (value >= 0)
                {
                    this.discussedAmount = value;
                    return;
                }
                this.discussedAmount = 0;
                System.Console.WriteLine("You cannot set the varaible discussedAmount to a negative value, it has been defaulted to zero (0)");
            }
        }
        public float DiscussedThreshold
        {
            get { return this.discussedThreshold; }
            set
            {
                if (value >= 0)
                {
                    this.discussedThreshold = value;
                    return;
                }
                this.discussedThreshold = 0;
                System.Console.WriteLine("You cannot set the varaible discussedThreshold to a negative value, it has been defaulted to zero (0)");
            }
        }
        public int Id
        {
            get { return this.id; }
            set
            {
                this.id = value;
            }
        }
        public string Name
        {
            get { return this.name; }
            set
            {
                this.name = value;
            }
        }
        public int Dist
        {
            get
            {
                return this.dist;
            }
            set
            {
                this.dist = value;
            }
        }

        public List<Tuple<Feature, double, string>> Neighbors
        {
            get { return this.neighbors; }
            set { this.neighbors = value; }
        }
        public List<Tuple<string, string, string>> Tags
        {
            get
            {
                return this.tags;
            }
            set
            {
                this.tags = value;
            }
        }

        public List<Tuple<string, string>> Timedata
        {
            get
            {
                return this.timedata;
            }
        }
        public List<Tuple<double, double>> Geodata
        {
            get
            {
                return this.geodata;
            }
        }

        public List<string> Speaks
        {
            get
            {
                return this.speaks;
            }
            set
            {
                this.speaks = value;
            }
        }

        public int Level
        {
            get
            {
                return this.level;
            }
            set
            {
                this.level = value;
            }
        }

        public Dictionary<double, double> ShortestDistance
        {
            get
            {
                return this.shortestDistance;
            }
            set
            {
                this.shortestDistance = value;
            }
        }

        public List<Tuple<Feature, double, string>> Parents
        {
            get
            {
                return this.parents;
            }
            set
            {
                this.parents = value;
            }
        }

        public Feature NearestNeighbor
        {
            get
            {
                double best = Double.MaxValue;
                int bestIndex = -1;
                for (int x = 0; x < neighbors.Count; x++)
                {
                    if (neighbors[x].Item2 <= best)
                    {
                        bestIndex = x;
                        best = neighbors[x].Item2;
                    }
                }
                if (bestIndex < 0){return null;}
                return neighbors[bestIndex].Item1;
            }
        }

        // This function will look at how long this feature has been discussed and compare it to the threshold value. If the current amount of discussion is greater than or equal to the threshold value, it will return true otherwise it will return false 
        public bool doneDiscussing()
        {
            return (discussedThreshold >= discussedAmount);
        }
        // This function will look at the current value for how much it has been discussed, and it it has been touched on at all (ie. its value is greater than zero), it will return true
        public bool beenDiscussed()
        {
            return (discussedAmount > 0);
        }
        // This function will look through all of the features that can be reached from this feature and reset the flag value to false
        public void resetReachableFlags()
        {
            this.flag = false;
            for (int x = 0; x < neighbors.Count; x++)
            {
                if (neighbors[x].Item1.flag) { neighbors[x].Item1.resetReachableFlags(); }
            }
        }
        // This function will print out a literal representation of this node and all of the nodes that it can reach (i.e. this function will recurse over the elements in the graph
        public void print()
        {
            System.Console.WriteLine("================  FEATURE  ================");
            System.Console.WriteLine("================ Variables ================");
            System.Console.WriteLine("\tId:               " + this.id);
            System.Console.WriteLine("\tName:               " + this.name);
            System.Console.WriteLine("\tDiscussedAmmount:   " + this.DiscussedAmount);
            System.Console.WriteLine("\tDiscussedThreshold: " + this.DiscussedThreshold);
            System.Console.WriteLine("================ Tags      ================");
            for (int x = 0; x < Tags.Count; x++)
            {
                System.Console.WriteLine("\t" + Tags[x].Item1 + ":" + Tags[x].Item2);
            }
            System.Console.WriteLine("================ Neighbors ================");
            for (int x = 0; x < neighbors.Count; x++)
            {
                System.Console.WriteLine("\t" + Neighbors[x].Item2 + "\t" + Neighbors[x].Item1.Id); 
            }
            System.Console.WriteLine("===========================================\n");
        }

        public static bool operator ==(Feature x, Feature y)
        {
            if((object)x == null && (object)y == null){return true;}
            try{return x.Equals(y) && y.Equals(x); }
            catch{ return false; }
        }
        public static bool operator !=(Feature x, Feature y)
        {
            return !(x == y);
        }
        public override bool Equals(object obj)
        {
            if (obj == null) { return false; }
            try
            {
                Feature x = (Feature)obj;
                Feature y = this;
                if (x.Id != y.Id) { return false; }
                if (x.DiscussedThreshold != y.DiscussedThreshold) { return false; }
                if (x.DiscussedAmount != y.DiscussedAmount) { return false; }
                if (x.Tags != y.Tags) { return false; }
                if (x.Neighbors != y.Neighbors) { return false; }
                return true;
            }
            catch { return false; }
        }

        public Feature deepCopy()
        {
            Feature copy = DeepClone.DeepCopy<Feature>(this);
            return copy;
        }
    }
}
