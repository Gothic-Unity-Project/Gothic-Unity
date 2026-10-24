using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_GotoItem: walks to a world item (the one Wld_DetectItem found - e.g. G2's wisp leads the hero to items).
    /// </summary>
    public class GoToItem : AbstractWalkAnimationAction2
    {
        private readonly GameObject _itemGo;
        private readonly Vector3 _fallbackPosition;

        public GoToItem(AnimationAction action, NpcContainer npcContainer, GameObject itemGo) : base(action, npcContainer)
        {
            _itemGo = itemGo;
            _fallbackPosition = itemGo != null ? itemGo.transform.position : Vector3.zero;
        }

        public override void Start()
        {
            if (_itemGo == null)
            {
                Logger.Log($"[GoToItem] {NpcInstance.GetName(NpcNameSlot.Slot0)}: item is gone - skipping", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            base.Start();
        }

        // The item can be picked up meanwhile - then the NPC finishes the walk to where it was.
        protected override Vector3 GetWalkDestination()
        {
            return _itemGo != null ? _itemGo.transform.position : _fallbackPosition;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            IsFinishedFlag = true;
        }
    }
}
