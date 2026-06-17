using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Adapters.Vob;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    [RequireComponent(typeof(AudioSource))]
    public class VRMouth : MonoBehaviour
    {
        // e.g. t_Potion_S0_2_Stand
        private const string _animationSchemeWithSfx = "t_{0}_S0_2_Stand";

        [SerializeField] private AudioSource _mouthAudio;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;

        
        // Do not eat them twice during destroy time.
        private List<GameObject> _objectsInDestroyGracePeriod = new();


        private void OnTriggerEnter(Collider other)
        {
            var go = other.gameObject;

            if (_objectsInDestroyGracePeriod.Contains(go))
                return;

            if (!TryGetItemToEat(go, out var item))
                return;

            Logger.Log($"Eating item: {go.name}", LogCat.VR);

            // Defines after which time period the object will be destroyed in hand.
            var destroyTime = 1f;
            if (TryExtractSfx(item, out var clip))
                destroyTime = clip.length;
            else
                Logger.LogWarning("No SFX for eating/drinking item found. Removing item anyways after 1 second.", LogCat.VR);

            // Can be set now already. It's sufficient to use this children instead of root.
            _objectsInDestroyGracePeriod.Add(go);

            GameObject rootGo = go;
            var vobLoaderComp = go.GetComponentInParent<VobLoader>();

            // Only use the VobLoader root if it belongs to this specific item (not a parent chest/container).
            if (vobLoaderComp != null && vobLoaderComp.Container.Vob.Type == VirtualObjectType.oCItem)
                rootGo = vobLoaderComp.gameObject;

            StartCoroutine(ConsumeObject(rootGo, clip, destroyTime));
        }

        private bool TryGetItemToEat(GameObject go, out ItemInstance item)
        {
            item = go.GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            
            if (item == null || item.Type != DaedalusInstanceType.Item)
                return false;

            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (mainFlag != VmGothicEnums.ItemFlags.ItemKatFood && mainFlag != VmGothicEnums.ItemFlags.ItemKatPotions)
                return false;

            return true;
        }

        private bool TryExtractSfx(ItemInstance item, out AudioClip clip)
        {
            clip = null;

            var mds = _resourceCacheService.TryGetModelScript("Humans")!;
            var animationName = string.Format(_animationSchemeWithSfx, item.SchemeName);
            var anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));
            if (anim == null)
            {
                return false;
            }

            var sfx = anim.SoundEffects.FirstOrDefault();
            if (sfx == null)
            {
                return false;
            }

            var sfxContainer = _vmCacheService.TryGetSfxData(sfx.Name);
            if (sfxContainer == null)
                return false;

            clip = _audioService.CreateAudioClip(sfxContainer.GetRandomSound());
            if (clip == null)
                return false;

            return true;
        }

        // FIXME- Handle also inventory state in the future. Currently only mesh is gone, but not object from save game and inventory.
        private IEnumerator ConsumeObject(GameObject go, [CanBeNull] AudioClip clip, float destroyDelay)
        {
            if (clip != null)
                _mouthAudio.PlayOneShot(clip);

            yield return new WaitForSeconds(destroyDelay);

            CallOnState(go);

            _objectsInDestroyGracePeriod.Remove(go);
            Destroy(go);
        }

        private void CallOnState(GameObject go)
        {
            var item = go.GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null)
                return;

            var onStateIndex = item.GetOnState(0);
            if (onStateIndex == 0)
                return;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(onStateIndex);
                Logger.Log($"[VRMouth] Called on_state[0] (idx={onStateIndex}) for {item.Name}", LogCat.VR);
            }
            catch (Exception e)
            {
                Logger.LogError($"[VRMouth] on_state[0] call failed: {e.Message}", LogCat.VR);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }

            RefreshHeroStatusBar();
        }

        private void RefreshHeroStatusBar()
        {
            var hero = _npcService.GetHeroContainer();
            var hp = hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var maxHp = hero.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);
            var statusBar = hero.Go.GetComponentInChildren<StatusBarAdapter>(true);
            statusBar?.SetFillAmount(hp, maxHp);
        }
    }
}
