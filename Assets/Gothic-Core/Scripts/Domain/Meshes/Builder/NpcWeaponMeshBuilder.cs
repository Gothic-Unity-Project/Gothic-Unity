using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Meshes.Builder
{
    public class NpcWeaponMeshBuilder : AbstractMeshBuilder
    {
        private GameObject _npcGo;
        private ItemInstance _itemData;
        private VmGothicEnums.ItemFlags _mainFlag;
        private VmGothicEnums.ItemFlags _flags;
        private bool _isEquipped;

        /// <summary>
        /// Where the NPC carries a weapon it hasn't drawn: 2H on the back, 1H on the belt, bow or crossbow on the back.
        /// Null for an item that is no melee or ranged weapon.
        /// </summary>
        public static string GetHolsterSlotName(ItemInstance item)
        {
            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            var flags = (VmGothicEnums.ItemFlags)item.Flags;
            // HasFlag like AiDrawWeapon: a two-hander with any other flag (temple guards' swords) hung at the belt and
            // drawing it looked for it on the back - the guards attacked empty-handed.
            var isTwoHanded = flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdAxe) ||
                              flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdSwd);
            return mainFlag switch
            {
                VmGothicEnums.ItemFlags.ItemKatNf => isTwoHanded ? Constants.SlotLongsword : Constants.SlotSword,
                VmGothicEnums.ItemFlags.ItemKatFf =>
                    item.IsCrossbow() ? Constants.SlotCrossbow : Constants.SlotBow,
                _ => null
            };
        }

        public void SetWeaponData(GameObject npcGo, ItemInstance itemData, VmGothicEnums.ItemFlags mainFlag,
            VmGothicEnums.ItemFlags flags, bool isEquipped)
        {
            _npcGo = npcGo;
            _itemData = itemData;
            _mainFlag = mainFlag;
            _flags = flags;
            _isEquipped = isEquipped;
        }

        public override GameObject Build()
        {
            switch (_mainFlag)
            {
                case VmGothicEnums.ItemFlags.ItemKatNf:
                    return EquipMeleeWeapon();
                case VmGothicEnums.ItemFlags.ItemKatFf:
                    return EquipRangeWeapon();
                default:
                    Logger.LogError($"WeaponType {_mainFlag} isn't handled yet.", LogCat.Mesh);
                    return null;
            }
        }

        private GameObject EquipMeleeWeapon()
        {
            var slotName = _isEquipped ? Constants.SlotRightHand : GetHolsterSlotName(_itemData);

            var weaponSlotGo = _npcGo.FindChildRecursively(slotName);
            if (weaponSlotGo == null)
            {
                return null;
            }

            GameObject weaponGo;
            if (weaponSlotGo.transform.childCount == 0)
            {
                weaponGo = new GameObject(_itemData.Visual);
                weaponGo.SetParent(weaponSlotGo, true, true);
            }
            else
            {
                weaponGo = weaponSlotGo.transform.GetChild(0).gameObject;
            }

            // Bugfix: e.g. there's a Buddler who has a NailMace and Club equipped at the same time.
            // Therefore we need to check if the Components are already there.
            if (!weaponGo.TryGetComponent<MeshFilter>(out var meshFilter))
            {
                meshFilter = weaponGo.AddComponent<MeshFilter>();
            }

            if (!weaponGo.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                meshRenderer = weaponGo.AddComponent<MeshRenderer>();
            }

            PrepareMeshFilter(meshFilter, Mrm, meshRenderer, 0);
            PrepareMeshRenderer(meshRenderer, Mrm);

            return weaponGo;
        }

        private GameObject EquipRangeWeapon()
        {
            var slotName = GetHolsterSlotName(_itemData);

            var weaponSlotGo = _npcGo.FindChildRecursively(slotName);
            if (weaponSlotGo == null)
            {
                return null;
            }

            GameObject weaponGo;
            if (weaponSlotGo.transform.childCount == 0)
            {
                weaponGo = new GameObject(_itemData.Visual);
                weaponGo.SetParent(weaponSlotGo, true, true);
            }
            else
            {
                weaponGo = weaponSlotGo.transform.GetChild(0).gameObject;
            }

            var meshFilter = weaponGo.AddComponent<MeshFilter>();
            var meshRenderer = weaponGo.AddComponent<MeshRenderer>();

            PrepareMeshFilter(meshFilter, Mmb.Mesh, meshRenderer, 0);
            PrepareMeshRenderer(meshRenderer, Mmb.Mesh);

            return weaponGo;
        }
    }
}
