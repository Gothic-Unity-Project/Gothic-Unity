using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Models.Caches;
using GUZ.Core.Models.Vm;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Lab.Handler
{
    public class LabVobItemHandler : AbstractLabHandler
    {
        public TMP_Dropdown VobCategoryDropdown;

        public TMP_Dropdown VobItemDropdown;

        public GameObject ItemSpawnSlot;

        private string _currentItemName;

        private Dictionary<string, ItemInstance> _items = new();

        public override void Bootstrap()
        {
            /*
             * 1. Load Vdfs
             * 2. Load VobItemAttachPoints json
             * 3. Load Vob name list
             * 4. Fill dropdown
             */
            var itemNames = GameStateService.GothicVm.GetInstanceSymbols("C_Item").Select(i => i.Name).ToList();

            _items = itemNames
                .ToDictionary(itemName => itemName, VmCacheService.TryGetItemData);

            VobCategoryDropdown.options = _items
                .Select(item => ((VmGothicEnums.ItemFlags)item.Value.MainFlag).ToString())
                .Distinct()
                .Select(flag => new TMP_Dropdown.OptionData(flag))
                .ToList();

            CategoryDropdownValueChanged();

            CreateItem("ItFo_Plants_mushroom_01");
        }

        public void CategoryDropdownValueChanged()
        {
            Enum.TryParse<VmGothicEnums.ItemFlags>(VobCategoryDropdown.options[VobCategoryDropdown.value].text,
                out var category);
            var items = _items.Where(item => item.Value.MainFlag == (int)category).ToList();
            VobItemDropdown.options = items.Select(item => new TMP_Dropdown.OptionData(item.Key)).ToList();
        }

        public void LoadVobOnClick()
        {
            // We want to have one element only.
            if (ItemSpawnSlot.transform.childCount != 0)
            {
                Destroy(ItemSpawnSlot.transform.GetChild(0).gameObject);
            }

            StartCoroutine(LoadVobOnClickDelayed());
        }

        private IEnumerator LoadVobOnClickDelayed()
        {
            // Wait 1 frame for GOs to be destroyed.
            yield return null;

            _currentItemName = VobItemDropdown.options[VobItemDropdown.value].text;
            var item = CreateItem(_currentItemName);
        }

        private GameObject CreateItem(string itemName)
        {
            var itemPrefab = ResourceCacheService.TryGetPrefabObject(PrefabType.VobItem);
            var item = VmCacheService.TryGetItemData(itemName);
            var mrm = ResourceCacheService.TryGetMultiResolutionMesh(item.Visual);
            var itemGo = MeshService.CreateVob(item.Visual, mrm, default, default, true,
                rootGo: itemPrefab, parent: ItemSpawnSlot, useTextureArray: false);

            return gameObject;
        }
    }
}
