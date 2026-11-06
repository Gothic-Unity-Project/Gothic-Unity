using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// The meshes of what an NPC wears or carries on its body, besides the body itself: the helmet on the head, and
    /// weapon meshes leaving their holster when the weapon leaves the NPC.
    /// Slot names are Gothic's (runtime meshes), so looking them up by name is the reference here.
    /// </summary>
    public class NpcArmorMeshBuilder
    {
        private const string _helmetHolderName = "_EquippedHelmet";
        private static readonly string[] _helmetSlotNames = { Constants.SlotHelmet, "BIP01 HEAD" };
        private static readonly string[] _weaponSlotNames =
        {
            Constants.SlotSword, Constants.SlotLongsword, Constants.SlotBow, Constants.SlotCrossbow
        };

        [Inject] private readonly VobService _vobService;


        /// <summary>
        /// Shows the helmet (or nothing, if null) on the NPC's head. Idempotent - an old helmet mesh is removed first.
        /// </summary>
        public void BuildHelmet(NpcContainer container, ItemInstance helmetOrNull)
        {
            var oldHolder = container.Go.FindChildRecursively(_helmetHolderName);
            if (oldHolder != null)
                Object.Destroy(oldHolder);

            if (helmetOrNull == null || string.IsNullOrEmpty(helmetOrNull.Visual))
                return;

            GameObject slot = null;
            foreach (var slotName in _helmetSlotNames)
            {
                slot = container.Go.FindChildRecursively(slotName);
                if (slot != null)
                    break;
            }
            if (slot == null)
            {
                Logger.LogWarning($"[ArmorVisual] No head slot on {container.Instance.GetName(NpcNameSlot.Slot0)} " +
                                  $"for '{helmetOrNull.Visual}'.", LogCat.Npc);
                return;
            }

            var holder = new GameObject(_helmetHolderName);
            holder.transform.SetParent(slot.transform, false);
            _vobService.CreateItemMesh(helmetOrNull.Index, holder);
            Logger.Log($"[ArmorVisual] {container.Instance.GetName(NpcNameSlot.Slot0)} wears helmet " +
                       $"'{helmetOrNull.Visual}' at {slot.name}", LogCat.Npc);
        }

        /// <summary>
        /// Removes a weapon's mesh from the NPC's body: its holster slot, or the hand for NPCs that went down mid-draw.
        /// </summary>
        public void DestroyWeaponMesh(NpcContainer container, ItemInstance weapon)
        {
            var holsterSlot = NpcWeaponMeshBuilder.GetHolsterSlotName(weapon);
            if (holsterSlot == null || container.Go == null)
                return;

            foreach (var slotName in new[] { holsterSlot, Constants.SlotRightHand })
            {
                var slotGo = container.Go.FindChildRecursively(slotName);
                if (slotGo == null || slotGo.transform.childCount == 0)
                    continue;
                Object.Destroy(slotGo.transform.GetChild(0).gameObject);
                return;
            }
        }

        /// <summary>
        /// All weapon meshes leave the NPC's holsters, and the hand slot's too if given (a drawn weapon).
        /// </summary>
        public void DestroyAllWeaponMeshes(NpcContainer container, string handSlotNameOrNull)
        {
            foreach (var slotName in _weaponSlotNames)
                DestroySlotChildren(container, slotName);

            if (handSlotNameOrNull != null)
                DestroySlotChildren(container, handSlotNameOrNull);
        }

        private static void DestroySlotChildren(NpcContainer container, string slotName)
        {
            var slot = container.Go.FindChildRecursively(slotName);
            if (slot == null)
                return;

            for (var i = slot.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(slot.transform.GetChild(i).gameObject);
        }
    }
}
