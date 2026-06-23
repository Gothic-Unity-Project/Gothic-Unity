using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class TakeItem : AbstractAnimationAction
    {
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        private readonly int _itemIndex;
        private readonly string _itemSymbolName;
        private const string PickupAnim = "T_STAND_2_TAKE";

        public TakeItem(AnimationAction action, NpcContainer npcContainer, int itemIndex, string itemSymbolName)
            : base(action, npcContainer)
        {
            _itemIndex = itemIndex;
            _itemSymbolName = itemSymbolName;
        }

        public override void Start()
        {
            var animFound = PrefabProps.AnimationSystem.PlayAnimation(PickupAnim);
            if (animFound)
            {
                ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(PickupAnim);
            }
            else
            {
                // No pickup animation available — do work immediately
                PerformPickup();
                IsFinishedFlag = true;
            }
        }

        protected override void AnimationEnd()
        {
            PerformPickup();
            IsFinishedFlag = true;
        }

        private void PerformPickup()
        {
            var npcPos = NpcGo != null ? NpcGo.transform.position : UnityEngine.Vector3.zero;

            // Re-verify item is still a world VOB near the NPC (player may have carried it away since Wld_DetectItem ran)
            var container = VobService.FindNearbyWorldItemContainer(_itemSymbolName, npcPos, maxDist: 5f);
            if (container == null)
            {
                Logger.Log($"[TakeItem] {NpcInstance.GetName(NpcNameSlot.Slot0)}: '{_itemSymbolName}' not within 5m — skipping pickup", LogCat.Npc);
                return;
            }

            Logger.Log($"[TakeItem] {NpcInstance.GetName(NpcNameSlot.Slot0)} picks up '{_itemSymbolName}' (world dist={UnityEngine.Vector3.Distance(container.Go.transform.position, npcPos):F1}m)", LogCat.Npc);

            _npcInventoryService.ExtCreateInvItems(NpcInstance, _itemIndex, 1);
            VobService.RemoveWorldItem(_itemSymbolName, npcPos);
            _npcInventoryService.ExtAiEquipBestMeleeWeapon(NpcInstance);
            _npcInventoryService.ExtAiEquipBestRangedWeapon(NpcInstance);
        }
    }
}
