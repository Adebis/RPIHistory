using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dialogue_Data_Entry
{
    class Constant
    {
        //An array of weights, for use in calculations.
        //The indices are as follows:
        //  0 - discuss amount weight
        //  1 - novelty weight
        //  2 - spatial constraint weight
        //  3 - hierarchy constraint weight
        public const int DiscussAmountWeightIndex = 0;
        public const int NoveltyWeightIndex = 1;
        public const int SpatialWeightIndex = 2;
        public const int HierarchyWeightIndex = 3;
        public const int TemporalWeightIndex = 4;
        public const int JointWeightIndex = 5;
        public const int AnchorWeightIndex = 6;
        public const int WeightArraySize = 7;

        //Store score components, and score, in return array.
        //Indices are as follows:
        //0 = score
        //1 = novelty
        //2 = discussed amount
        //3 = expected dramatic value
        //4 = spatial constraint value
        //5 = hierarchy constraint value
        public const int ScoreArrayScoreIndex = 0;
        public const int ScoreArrayNoveltyIndex = 1;
        public const int ScoreArrayDiscussedAmountIndex = 2;
        public const int ScoreArrayExpectedDramaticIndex = 3;
        public const int ScoreArraySpatialIndex = 4;
        public const int ScoreArrayHierarchyIndex = 5;
        public const int ScoreArraySize = 6;

        //Language mode constants
        //  0 = English
        //  1 = Chinese
        public const int EnglishMode = 0;
        public const int ChineseMode = 1;

        //Tag values
        public const string SPATIAL = "spatial";
        public const string HIERACHY = "hierachy";
        public const string FUN_FACT = "Fun Fact";

        //Story acts
        public const string LEADIN = "lead-in";
        public const string RELATIONSHIP = "relationship";
        public const string USERTURN = "user-turn";
        public const string LOCATIONCHANGE = "location-change";
        public const string HINTAT = "hint-at";
        public const string TIEBACK = "tie-back";
        public const string RESOLVE = "resolve";
        public const string SWITCHPOINT = "switch-point";
        public const string ANALOGY = "analogy";

        //Entity types
        public const string CHARACTER = "character";
        public const string LOCATION = "location";
        public const string EVENT = "event";
    }
}
