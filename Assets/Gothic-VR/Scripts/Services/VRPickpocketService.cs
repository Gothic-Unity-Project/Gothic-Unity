using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.VR.Services
{
    /// <summary>
    /// DeveloperConfig.EnableVrPickpocket: the VR hand state of pickpocketing (rules in PickpocketService). The grip at
    /// the NPC's purse also grabs the NPC himself (VRNpc: talk) - VRPickpocket marks the NPC here, VRNpc asks before
    /// it starts a talk.
    /// </summary>
    public class VRPickpocketService
    {
        // Seconds a hand that was near the purse can't start a talk with its NPC (the NPC's VrInteractable takes the
        // same grip).
        private const float _talkBlockSeconds = 1f;
        private NpcContainer _talkBlockedNpc;
        private float _talkBlockedUntil;

        /// <summary>
        /// The NPC being robbed right now (his pockets are open in a hand).
        /// </summary>
        public NpcContainer RobbedNpc { get; set; }

        /// <summary>
        /// A hand is at the NPC's purse: a grip there is for the purse, not for talking to him.
        /// </summary>
        public void BlockTalk(NpcContainer npc)
        {
            _talkBlockedNpc = npc;
            _talkBlockedUntil = Time.time + _talkBlockSeconds;
        }

        public bool IsTalkBlocked(NpcContainer npc)
        {
            return npc != null && (npc == RobbedNpc || (npc == _talkBlockedNpc && Time.time < _talkBlockedUntil));
        }
    }
}
