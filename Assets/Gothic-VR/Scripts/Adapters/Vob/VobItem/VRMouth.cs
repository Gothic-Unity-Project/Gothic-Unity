using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
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

        
        // Do not eat them twice during destroy time.
        private List<GameObject> _objectsInDestroyGracePeriod = new();


        private void OnTriggerEnter(Collider other)
        {
            var go = other.gameObject;

            if (!TryGetItemToEat(go, out var item))
                return;

            // Resolve root before grace-period check so the same object is tracked and removed.
            var rootGo = go;
            var vobLoaderComp = go.GetComponentInParent<VobLoader>();
            if (vobLoaderComp != null && vobLoaderComp.Container.Vob.Type == VirtualObjectType.oCItem)
                rootGo = vobLoaderComp.gameObject;

            if (_objectsInDestroyGracePeriod.Contains(rootGo))
                return;

            Logger.Log($"Eating item: {go.name}", LogCat.VR);

            var destroyTime = 1f;
            if (TryExtractSfx(item, out var clip))
                destroyTime = clip.length;
            else
                Logger.LogWarning("No SFX for eating/drinking item found. Removing item anyways after 1 second.", LogCat.VR);

            _objectsInDestroyGracePeriod.Add(rootGo);
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

#if GOTHIC_HVR_INSTALLED
            // Only eat what's held in a hand. Items in the backpack or the NPC loot panel sit in sockets
            // and can pass the mouth when those open near the head.
            var grabbable = go.GetComponentInParent<HurricaneVR.Framework.Core.HVRGrabbable>();
            if (grabbable != null && !grabbable.IsHandGrabbed)
                return false;
#endif

            return true;
        }

        private bool TryExtractSfx(ItemInstance item, out AudioClip clip)
        {
            clip = null;

            var mds = _resourceCacheService.TryGetModelScript("Humans");
            if (mds == null)
                return false;

            var animationName = string.Format(_animationSchemeWithSfx, item.SchemeName);
            var anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));
            if (anim == null)
            {
                Logger.LogWarning($"Humans.mds: '{animationName}' animation not found. Eating/drinking sound skipped.", LogCat.VR);
                return false;
            }

            var sfx = anim.SoundEffects.FirstOrDefault();
            if (sfx == null)
            {
                Logger.LogWarning($"Humans.mds: '{animationName}' has no sound effect. Eating/drinking sound skipped.", LogCat.VR);
                return false;
            }

            var sfxContainer = _vmCacheService.TryGetSfxData(sfx.Name);
            if (sfxContainer == null)
            {
                Logger.LogWarning($"SFX '{sfx.Name}' referenced by '{animationName}' not found. Eating/drinking sound skipped.", LogCat.VR);
                return false;
            }

            clip = _audioService.CreateAudioClip(sfxContainer.GetRandomSound());
            if (clip == null)
                return false;

            return true;
        }

        private IEnumerator ConsumeObject(GameObject go, [CanBeNull] AudioClip clip, float destroyDelay)
        {
            if (clip != null)
                _mouthAudio.PlayOneShot(clip);

            yield return new WaitForSeconds(destroyDelay);

            CallOnState(go);

            _objectsInDestroyGracePeriod.Remove(go);

            var vobItem = go.GetComponent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem != null && vobItem.Amount > 1)
            {
                vobItem.Amount--;
                Logger.Log($"[VRMouth] Stack decremented: {go.name} remaining={vobItem.Amount}", LogCat.VR);
                yield break;
            }

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
        }
    }
}
