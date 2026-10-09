using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_EquipArmor / AI_UnequipArmor / AI_EquipBestArmor - queued like in the engine, so the change happens in order
    /// with the dialog lines around it (e.g. Greg putting on Lobart's clothes after talking).
    /// </summary>
    public class ChangeArmor : AbstractAnimationAction
    {
        public enum Mode
        {
            Equip,
            Unequip,
            EquipBest
        }

        [Inject] private readonly NpcInventoryService _npcInventoryService;

        private int ItemIndex => Action.Int0;
        private Mode ArmorMode => (Mode)Action.Int1;


        public ChangeArmor(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            IsFinishedFlag = true;

            switch (ArmorMode)
            {
                case Mode.Equip:
                    _npcInventoryService.EquipArmorFromInventory(NpcInstance, VmCacheService.TryGetItemData(ItemIndex));
                    break;
                case Mode.Unequip:
                    _npcInventoryService.UnequipArmor(NpcInstance);
                    break;
                case Mode.EquipBest:
                    _npcInventoryService.EquipBestArmor(NpcInstance);
                    break;
            }
        }
    }
}
