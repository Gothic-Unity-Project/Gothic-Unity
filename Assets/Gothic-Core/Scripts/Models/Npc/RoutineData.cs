using System;

namespace Gothic.Core.Models.Npc
{
    public class RoutineData
    {
        public int StartH;
        public int StartM;
        public int NormalizedStart;
        public int StopH;
        public int StopM;
        public int NormalizedEnd;
        public int Action;
        public string Waypoint;

        /// <summary>
        /// The same state at the same waypoint: for the NPC nothing changes between the two entries (Bilgot sits at the
        /// campfire with TA_Sit_Campfire 08-23 and 23-08).
        /// </summary>
        public bool IsSameActivity(RoutineData other)
        {
            return other != null && Action == other.Action &&
                   string.Equals(Waypoint, other.Waypoint, StringComparison.OrdinalIgnoreCase);
        }
    }
}
