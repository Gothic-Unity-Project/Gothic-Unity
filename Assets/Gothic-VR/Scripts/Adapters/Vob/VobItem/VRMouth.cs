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
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
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
        private const float _smokePuffSeconds = 1.5f;
        private const float _smokeFadeSeconds = 9f;
        private const float _smokeExhaleSpeed = 0.35f;
        private const float _smokeRiseSpeed = 0.1f;
        private const int _smokePuffParticles = 12;

        [SerializeField] private AudioSource _mouthAudio;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly MeshService _meshService;

        
        // Do not eat them twice during destroy time.
        private List<GameObject> _objectsInDestroyGracePeriod = new();


        private void OnTriggerEnter(Collider other)
        {
            var go = other.gameObject;

            if (!TryGetItemToEat(go, out var item))
                return;

#if GOTHIC_HVR_INSTALLED
            // Unpaid trader goods (trade counter) can't be eaten, drunk or smoked.
            if (go.GetComponentInParent<Gothic.VR.Adapters.Trade.VRTradeGoods>() is { IsSettled: false })
                return;
#endif

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
            // Smoking: a drag first, then the sound and the smoke (DeveloperConfig.SmokeStartDelay).
            var startDelay = IsSmokable(item) ? Mathf.Max(0f, _configService.Dev.SmokeStartDelay) : 0f;
            StartCoroutine(ConsumeObject(rootGo, clip, destroyTime, startDelay));

            if (IsSmokable(item))
                StartCoroutine(ExhaleSmoke(item, destroyTime, startDelay));
        }

        /// <summary>
        /// V1 (DeveloperConfig.EnableSmoking): swampweed joints (scheme JOINT, ITEM_KAT_NONE) are smoked at the mouth like
        /// food is eaten: the SFX of t_JOINT_S0_2_Stand (SMOKE_JOINT) plays, on_state[0] runs (XP/effects) and the joint is
        /// used up (that animation ends with DEF_DESTROY_ITEM). Data driven - mods with own JOINT items work the same.
        /// </summary>
        private bool IsSmokable(ItemInstance item)
        {
            return _configService.Dev.EnableSmoking && item.SchemeName.EqualsIgnoreCase("JOINT");
        }

        /// <summary>
        /// The animation's own particle effect (LIGHTSMOKE) puffed out in front of the VR head.
        /// </summary>
        private IEnumerator ExhaleSmoke(ItemInstance item, float inhaleSeconds, float startDelay)
        {
            var puffs = Mathf.Max(1, _configService.Dev.SmokePuffs);
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            var pfxName = TryGetUseAnimation(item)?.ParticleEffects.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(pfxName))
            {
                Logger.LogWarning($"[VRMouth] No smoke PFX in 't_{item.SchemeName}_S0_2_Stand'.", LogCat.VR);
                yield break;
            }

            // Puffs spread over the smoking sound - the first one right with it (it came only after a pause).
            for (var puff = 0; puff < puffs; puff++)
            {
                if (puff > 0)
                    yield return new WaitForSeconds(inhaleSeconds / puffs);

                var head = Camera.main != null ? Camera.main.transform : transform;
                var pos = transform.position + head.forward * 0.2f;
                var pfx = _meshService.CreateVobPfx(pfxName, pos, Quaternion.LookRotation(head.forward),
                    destroyAfterPlay: true);
                if (pfx == null)
                {
                    Logger.LogWarning($"[VRMouth] Smoke PFX '{pfxName}' couldn't be created.", LogCat.VR);
                    continue;
                }

                // LIGHTSMOKE loops - one puff, then it drifts away (like the animation's *eventPFXStop). Blown forward
                // and a bit up - straight up it left the view at once.
                var smokeRoot = pfx.transform.parent != null ? pfx.transform.parent.gameObject : pfx;
                foreach (var particleSystem in smokeRoot.GetComponentsInChildren<ParticleSystem>())
                {
                    var exhale = head.forward * _smokeExhaleSpeed + Vector3.up * _smokeRiseSpeed;
                    var velocity = particleSystem.velocityOverLifetime;
                    velocity.enabled = true;
                    velocity.space = ParticleSystemSimulationSpace.World;
                    velocity.x = new ParticleSystem.MinMaxCurve(exhale.x * 0.7f, exhale.x);
                    velocity.y = new ParticleSystem.MinMaxCurve(exhale.y * 0.7f, exhale.y);
                    velocity.z = new ParticleSystem.MinMaxCurve(exhale.z * 0.7f, exhale.z);
                    particleSystem.Emit(_smokePuffParticles);
                }
                StartCoroutine(StopSmoke(smokeRoot));
            }
        }

        private IEnumerator StopSmoke(GameObject smoke)
        {
            yield return new WaitForSeconds(_smokePuffSeconds);
            if (smoke == null)
                yield break;
            foreach (var particleSystem in smoke.GetComponentsInChildren<ParticleSystem>())
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(smoke, _smokeFadeSeconds);
        }

        private bool TryGetItemToEat(GameObject go, out ItemInstance item)
        {
            item = (go.GetComponentInParent<VobLoader>()?.Container?.Props as VobItemProperties2)?.Instance;
            
            if (item == null || item.Type != DaedalusInstanceType.Item)
                return false;

            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (mainFlag != VmGothicEnums.ItemFlags.ItemKatFood && mainFlag != VmGothicEnums.ItemFlags.ItemKatPotions &&
                !IsSmokable(item))
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

            var animationName = string.Format(_animationSchemeWithSfx, item.SchemeName);
            var anim = TryGetUseAnimation(item);
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

        [CanBeNull]
        private IAnimation TryGetUseAnimation(ItemInstance item)
        {
            var mds = _resourceCacheService.TryGetModelScript("Humans");
            var animationName = string.Format(_animationSchemeWithSfx, item.SchemeName);
            return mds?.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));
        }

        private IEnumerator ConsumeObject(GameObject go, [CanBeNull] AudioClip clip, float destroyDelay,
            float startDelay = 0f)
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);
            if (clip != null)
                _mouthAudio.PlayOneShot(clip);

            yield return new WaitForSeconds(destroyDelay);

            CallOnState(go);

            _objectsInDestroyGracePeriod.Remove(go);

            // Items held in a hand count as inventory (VRPlayerService.SetGrab/UnsetGrab): added with the full amount on
            // grab, removed with the then-current amount on release. Keep that in sync, or eaten food stays as a ghost.
#if GOTHIC_HVR_INSTALLED
            var grabbable = go.GetComponentInChildren<HurricaneVR.Framework.Core.HVRGrabbable>();
            var isHeld = grabbable != null && grabbable.IsBeingHeld;
#else
            var isHeld = false;
#endif

            var vobItem = go.GetComponent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem != null && vobItem.Amount > 1)
            {
                vobItem.Amount--;
                if (isHeld)
                    _playerService.RemoveItem(!string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name, 1);

                Logger.Log($"[VRMouth] Stack decremented: {go.name} remaining={vobItem.Amount}", LogCat.VR);
                yield break;
            }

#if GOTHIC_HVR_INSTALLED
            // Release first - that removes it from the inventory like any dropped item.
            if (isHeld)
                grabbable.ForceRelease();
#endif

            Destroy(go);
        }

        private void CallOnState(GameObject go)
        {
            var item = (go.GetComponentInParent<VobLoader>()?.Container?.Props as VobItemProperties2)?.Instance;
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
