using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class NpcWeaponMeshBuilder : AbstractMeshBuilder
    {
        private GameObject _npcGo;
        private ItemInstance _itemData;
        private VmGothicEnums.ItemFlags _mainFlag;
        private VmGothicEnums.ItemFlags _flags;
        private bool _isEquipped;

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
            string slotName;

            if (_isEquipped)
            {
                slotName = "ZS_RIGHTHAND";
            }
            else
            {
                switch ((VmGothicEnums.ItemFlags)_itemData.Flags)
                {
                    case VmGothicEnums.ItemFlags.Item2HdAxe:
                    case VmGothicEnums.ItemFlags.Item2HdSwd:
                        slotName = "ZS_LONGSWORD";
                        break;
                    default:
                        slotName = "ZS_SWORD";
                        break;
                }
            }
            
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
            string slotName;
            switch ((VmGothicEnums.ItemFlags)_itemData.Flags)
            {
                case VmGothicEnums.ItemFlags.ItemCrossbow:
                    slotName = "ZS_CROSSBOW";
                    break;
                default:
                    slotName = "ZS_BOW";
                    break;
            }

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
