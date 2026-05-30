using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Caches;
using GUZ.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace GUZ.Lab.Handler
{
    public class LabInteractableHandler : AbstractLabHandler
    {
        [NonSerialized] public bool SpawnItems;

        public GameObject Weapons1HGO;
        public GameObject Weapons2HGO;
        public GameObject RangedWeaponsGO;
        public GameObject MunitionGO;
        public GameObject ArmorGO;
        public GameObject FoodGO;
        public GameObject DocsGO;
        public GameObject PotionsGO;
        public GameObject RunesGO;
        public GameObject MagicGO;
        public GameObject MiscGO;
        
        public GameObject ContainersGO;
        public GameObject DoorsGO;
        public GameObject FiresGO;
        public GameObject BedsGO;
        public GameObject SwitchesGO;
        public GameObject InteractablesGO;
        public GameObject WheelsGO;

        public override void Bootstrap()
        {
            if (!SpawnItems)
                return;

            var itemNames = GameStateService.GothicVm.GetInstanceSymbols("C_Item").Select(i => i.Name).ToList();
            var allItems = itemNames.ToDictionary(itemName => itemName, VmCacheService.TryGetItemData);

            var meleeWeapons = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatNf)
                .ToDictionary(i => i.Key, i => i.Value);
            var oneHandedWeapons = meleeWeapons
                .Where(i => (i.Value.Flags & ((int)VmGothicEnums.ItemFlags.ItemSwd | (int)VmGothicEnums.ItemFlags.ItemAxe)) != 0)
                .ToDictionary(i => i.Key, i => i.Value);
            var twoHandedWeapons = meleeWeapons.Except(oneHandedWeapons)
                .ToDictionary(i => i.Key, i => i.Value);
            
            var rangedWeapons = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf)
                .ToDictionary(i => i.Key, i => i.Value);
            var munition = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatMun)
                .ToDictionary(i => i.Key, i => i.Value);
            var armor = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatArmor)
                .ToDictionary(i => i.Key, i => i.Value);
            var food = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFood)
                .ToDictionary(i => i.Key, i => i.Value);
            var docs = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatDocs)
                .ToDictionary(i => i.Key, i => i.Value);
            var potions = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatPotions)
                .ToDictionary(i => i.Key, i => i.Value);
            var runes = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatRune)
                .ToDictionary(i => i.Key, i => i.Value);
            var magic = allItems.Where(i => i.Value.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatMagic)
                .ToDictionary(i => i.Key, i => i.Value);
            var misc = allItems.Except(meleeWeapons).Except(rangedWeapons).Except(munition).Except(armor).Except(food)
                .Except(docs).Except(potions).Except(runes).Except(magic).ToDictionary(i => i.Key, i => i.Value);
            
            InitItemType(oneHandedWeapons, Weapons1HGO, -90f);
            InitItemType(twoHandedWeapons, Weapons2HGO, -90f);
            InitItemType(rangedWeapons, RangedWeaponsGO, -90f);
            InitItemType(munition, MunitionGO);
            InitItemType(armor, ArmorGO);
            InitItemType(food, FoodGO);
            InitItemType(docs, DocsGO);
            InitItemType(potions,  PotionsGO);
            InitItemType(runes, RunesGO);
            InitItemType(magic, MagicGO);
            InitItemType(misc, MiscGO);
            
            // FIXME - Need to initialize them via VobLoader.LoadNow(IVob) instead of loading mesh. Otherwise we get exceptions in child Start() calls.
            // InitOCMobDoor();
            // StartCoroutine(InitOCMobContainer());
            // InitOCMobFire();
            // InitOCMobBed();
            // InitOCMobSwitch();
            // InitOCMobInter();
            // InitOCMobWheel();
        }

        private void InitItemType(Dictionary<string, ItemInstance> items, GameObject parentGO, float zRotation = 0f)
        {
            var zPosition = 0f;
            foreach (var item in items)
            {
                CreateItem(item.Key, ref zPosition, zRotation, parentGO);
            }
        }

        private void CreateItem(string instanceName, ref float zPosition, float zRotation, GameObject parent)
        {
            var vobContainer = VobService.CreateItem(new Item()
            {
                Name = instanceName,
                Position = new Vector3(0f, 1.5f, zPosition).ToZkVector(),
                Rotation = Quaternion.Euler(new Vector3(0, 0, zRotation)).ToZkMatrix(), // Quaternion.identity.ToZkMatrix(), 
                Visual = new VisualMesh(),
                Instance = instanceName,
                Amount = 1
            });

            vobContainer.Go.SetParent(parent);
            zPosition -= 0.5f;
        }

        private void InitOCMobDoor()
        {
            SpawnInteractable("DOOR_WOODEN", PrefabType.VobDoor, DoorsGO, new Vector3(0, 0.2f, 0));

            // Yes, that's correct. Beds are mostly of type oCMobDoor inside G1. ;-)
            SpawnInteractable("BED_1_OC", PrefabType.VobDoor, DoorsGO, new Vector3(0, 0, -4));
        }

        private IEnumerator InitOCMobContainer()
        {
            var chest1 = SpawnInteractable("CHESTBIG_OCCHESTLARGE", PrefabType.VobContainer, ContainersGO, position: new(0,0,0));
            SpawnInteractable("CHESTBIG_OCCHESTMEDIUM", PrefabType.VobContainer, ContainersGO, position: new(0,0,-2));
            SpawnInteractable("CHESTBIG_OCCRATELARGE", PrefabType.VobContainer, ContainersGO, position: new(0,0,-4));
            SpawnInteractable("CHESTBIG_ORCMUMMY", PrefabType.VobContainer, ContainersGO, position: new(0,0,-6));
            SpawnInteractable("CHESTSMALL_OCCHESTSMALL", PrefabType.VobContainer, ContainersGO, position: new(0,0,-8));
            SpawnInteractable("CHESTSMALL_OCCHESTSMALLLOCKED", PrefabType.VobContainer, ContainersGO, position: new(0,0,-10));
            SpawnInteractable("CHESTSMALL_OCCRATESMALL", PrefabType.VobContainer, ContainersGO, position: new(0,0,0-12));
            SpawnInteractable("CHESTSMALL_OCCRATESMALLLOCKED", PrefabType.VobContainer, ContainersGO, position: new(0,0,-14));
            
            var item1 = SpawnItem("ItMwPickaxe", ContainersGO, new Vector3(1.25f, 0.25f, 0));

            // Wait 1 frame for Sockets to become active.
            yield return null;
        }

        private void InitOCMobFire()
        {
            SpawnInteractable("FIREPLACE_HIGH", PrefabType.VobFire, FiresGO);
        }

        private void InitOCMobBed()
        {
            SpawnInteractable("BEDLOW_PSI", PrefabType.VobBed, BedsGO, new Vector3(0, 0.1f, -1), Quaternion.Euler(0, 90, 0));
        }

        private void InitOCMobSwitch()
        {
            SpawnInteractable("LEVER_1_OC", PrefabType.VobSwitch, SwitchesGO, new Vector3(0, 1, 0), Quaternion.Euler(0, 180, 0));
            SpawnInteractable("TOUCHPLATE_STONE", PrefabType.VobSwitch, SwitchesGO, new Vector3(0,0, -3), Quaternion.Euler(0, 180, 0));
            SpawnInteractable("VWHEEL_1_OC", PrefabType.VobSwitch, SwitchesGO, new Vector3(0,0, -6), Quaternion.Euler(0, 90, 0));
        }

        private void InitOCMobInter()
        {
            SpawnInteractable("SMOKE_WATERPIPE", PrefabType.VobInteractable, InteractablesGO);
            SpawnInteractable("CHAIR_1_OC", PrefabType.VobInteractable, InteractablesGO, new Vector3(0,0, -3));
        }

        private void InitOCMobWheel()
        {
            SpawnInteractable("THRONE_BIG", PrefabType.VobWheel, WheelsGO, rotation: Quaternion.Euler(0, 180, 0));
            SpawnInteractable("BABEBED_1", PrefabType.VobWheel, WheelsGO, new Vector3(0, 0, -4));
        }
    }
}
