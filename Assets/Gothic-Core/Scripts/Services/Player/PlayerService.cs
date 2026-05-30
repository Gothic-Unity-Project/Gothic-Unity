using System;
using System.Collections;
using System.Collections.Generic;
using GUZ.Core.Manager;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Models.Vob;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.Core.Services.Player
{
    public class PlayerService
    {
        public Vector3 HeroSpawnPosition;
        public Quaternion HeroSpawnRotation;
        
        public string LastLevelChangeTriggerVobName;
        public NpcContainer HeroContainer { get; private set; }
        
        
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly UnityMonoService _unityMonoService;


        // Air in lungs isn't stored in G1 savegame, therefore setting it here temporarily at runtime.
        [NonSerialized] public bool IsDiving;
        [NonSerialized] public float MaxAir;
        [NonSerialized] public bool HasMaxAir; // Faster check for other components. Rather than calculating value on their own each frame.
        [NonSerialized] public float CurrentAir = float.MaxValue;
        
        
        public PlayerService()
        {
            GlobalEventDispatcher.LockPickComboBroken.AddListener(
                (lockPick, _, _) => RemoveItem(lockPick.VobAs<IItem>().Instance, 1));
            
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(OnZenKitBootstrapped);
        }

        private void OnZenKitBootstrapped()
        {
            MaxAir = _gameStateService.GuildValues.GetDiveTime((int)VmGothicEnums.Guild.GIL_HUMAN);
            CurrentAir = MaxAir; // Isn't stored in SaveGame, therefore setting it to full now.

            _unityMonoService.StartCoroutine(HandleStatusEffects());
        }
        
        public void SetHero(NpcContainer heroContainer)
        {
            HeroContainer = heroContainer;
        }
        
        public void ResetSpawn()
        {
            HeroSpawnPosition = default;
            HeroSpawnRotation = default;
        }

        public List<ContentItem> GetInventory(VmGothicEnums.InvCats category)
        {
            return _npcInventoryService.GetInventoryItems(HeroContainer.Instance, category);
        }

        public void AddItem(string itemInstanceName, int amount = 1)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName)!;
            _npcInventoryService.ExtCreateInvItems(HeroContainer.Instance, item.Index, amount);
        }

        public void RemoveItem(string itemInstanceName, int amount = 1)
        {
            var item = _vmCacheService.TryGetItemData(itemInstanceName)!;
            _npcInventoryService.ExtRemoveInvItems(HeroContainer.Instance, item.Index, amount);
        }

        public void StartDiving()
        {
            IsDiving = true;
        }

        public void StopDiving()
        {
            IsDiving = false;
        }

        private IEnumerator HandleStatusEffects()
        {
            while (true)
            {
                if (IsDiving)
                {
                    HasMaxAir = false;
                    
                    if (CurrentAir > 0)
                        CurrentAir -= Time.deltaTime;
                }
                else if (!HasMaxAir)
                {
                    if (CurrentAir < MaxAir)
                        CurrentAir += Time.deltaTime;
                    else
                    {
                        CurrentAir = MaxAir;
                        HasMaxAir = true;
                    }
                }
                
                yield return null;
            }
            // ReSharper disable once IteratorNeverReturns
        }
    }
}
