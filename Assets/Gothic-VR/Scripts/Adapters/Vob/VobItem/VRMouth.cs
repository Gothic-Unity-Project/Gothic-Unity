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
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Trade;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Services;
using Gothic.Core.Services.World;
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
        // A drag of something not used up (water pipe): its puffs are spread over this time.
        // A water pipe: smoke and the breath out come when the mouthpiece leaves the mouth after at least a short drag.
        private const float _smokableMinDragSeconds = 0.8f;
        private const float _smokableExhaleSeconds = 0.6f;
        private const int _smokableExhalePuffs = 2;
        // The smoke is held for a moment before it is blown out.
        private const float _smokableExhaleDelay = 1f;
        // The joint's smoking sound (SfxInst SMOKE_JOINT, smoke_joint.wav) - the snoring breath-out isn't in
        // every game.
        private const string _exhaleSound = "SMOKE_JOINT";

        [SerializeField] private AudioSource _mouthAudio;

        [Tooltip("Smoke puffs of one joint (at least 1), spread over the smoking sound.")]
        [Min(1)]
        [SerializeField] private int _smokePuffs = 4;

        [Tooltip("Seconds from putting the joint to the mouth until the smoking sound and the first puff (taking a " +
                 "drag).")]
        [Min(0f)]
        [SerializeField] private float _smokeStartDelay = 1.5f;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly ItemUseService _itemUseService;

        
        // Do not eat them twice during destroy time.
        private List<GameObject> _objectsInDestroyGracePeriod = new();

        // The part smoked at the mouth right now (a water pipe's mouthpiece) and its drag.
        private IVrMouthSmokable _smokable;
        private Collider _smokedPart;
        private Coroutine _smokableExhale;
        private float _smokableExhaleStart;


        private void OnTriggerEnter(Collider other)
        {
            if (TryStartSmoking(other))
                return;

            var go = other.gameObject;

            if (!TryGetItemToEat(go, out var item))
                return;

            // Unpaid trader goods (trade counter) can't be eaten, drunk or smoked.
            if (VRTradeGoods.IsUnpaid(go.transform))
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
            // Smoking: a drag first, then the sound and the smoke (_smokeStartDelay).
            var startDelay = IsSmokable(item) ? Mathf.Max(0f, _smokeStartDelay) : 0f;
            StartCoroutine(ConsumeObject(rootGo, clip, destroyTime, startDelay));

            if (IsSmokable(item))
                TryExhaleSmoke(TryGetUseAnimation(item), string.Format(_animationSchemeWithSfx, item.SchemeName),
                    destroyTime, startDelay);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == _smokedPart)
                StopSmoking();
        }

        /// <summary>
        /// A part of something smoked without being used up (IVrMouthSmokable: a water pipe's mouthpiece) touched the
        /// mouth: after the drag delay its smoke is blown out. true: the collider belongs to such a thing - it's no
        /// food, whether it is smoked now or not.
        /// </summary>
        private bool TryStartSmoking(Collider other)
        {
            var smokable = other.GetComponentInParent<IVrMouthSmokable>();
            if (smokable == null)
                return false;

            // In again without having left: its exit was missed (culled at the mouth).
            if (other == _smokedPart)
                StopSmoking();

            if (!smokable.TryStartSmoking(other))
                return true;

            // Another part was still at the mouth.
            StopSmoking();
            _smokable = smokable;
            _smokedPart = other;
            _smokableExhaleStart = Time.time;
            // While at the mouth only the pipe works (it bubbles, the bowl smokes); the smoke comes out of the mouth
            // when the mouthpiece is taken away (StopSmoking).
            Logger.Log($"[VRMouth] Smoking {other.name}.", LogCat.VR);
            return true;
        }

        /// <summary>
        /// The smoked part left the mouth. Taken away before the drag ended, no smoke comes; puffs already blown out
        /// drift on. Destroyed or culled at the mouth (its exit never came): the mouth is free again, without smoke.
        /// </summary>
        private void StopSmoking(bool isExhaling = true)
        {
            if (_smokable == null)
                return;

            // Cleared first: nothing below may leave the mouth stuck on this part.
            var smokable = _smokable;
            var part = _smokedPart;
            var dragStart = _smokableExhaleStart;
            _smokable = null;
            _smokedPart = null;
            _smokableExhale = null;

            if (IsGone(smokable, part))
            {
                Logger.Log("[VRMouth] The smoked part is gone - nothing is breathed out.", LogCat.VR);
                return;
            }

            // Taken from the mouth after a drag: a breath out and a cloud of smoke in front of the face.
            if (isExhaling && Time.time >= dragStart + _smokableMinDragSeconds)
            {
                TryExhaleSmoke(TryGetHumansAnimation(smokable.ExhaleAnimation), smokable.ExhaleAnimation,
                    _smokableExhaleSeconds, _smokableExhaleDelay, _smokableExhalePuffs);
                StartCoroutine(PlayExhaleSound(_smokableExhaleDelay));
                Logger.Log($"[VRMouth] Breathing out the smoke of {part.name} in {_smokableExhaleDelay} s.", LogCat.VR);
            }

            smokable.StopSmoking(part);
        }

        /// <summary>
        /// The part or its smokable was destroyed or deactivated (culled, world left). Unity's null check first: `?.`
        /// doesn't do it, and .name of a destroyed collider throws.
        /// </summary>
        private static bool IsGone(IVrMouthSmokable smokable, Collider part)
        {
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy)
                return true;

            return smokable is Behaviour behaviour && (behaviour == null || !behaviour.gameObject.activeInHierarchy);
        }

        private IEnumerator PlayExhaleSound(float delay)
        {
            yield return new WaitForSeconds(delay);

            var exhaleClip = _audioService.GetRandomSoundClip(_exhaleSound);
            if (exhaleClip != null)
                _mouthAudio.PlayOneShot(exhaleClip);
            else
                Logger.LogWarning($"[VRMouth] No sound '{_exhaleSound}' for breathing out the smoke.", LogCat.VR);
        }

        private void OnDisable()
        {
            // Coroutines end with the component; the smoked thing must still hear that it left the mouth.
            StopSmoking(isExhaling: false);
        }

        /// <summary>
        /// V1: swampweed joints (scheme JOINT, ITEM_KAT_NONE) are smoked at the mouth like
        /// food is eaten: the SFX of t_JOINT_S0_2_Stand (SMOKE_JOINT) plays, on_state[0] runs (XP/effects) and the joint is
        /// used up (that animation ends with DEF_DESTROY_ITEM). Data driven - mods with own JOINT items work the same.
        /// </summary>
        private bool IsSmokable(ItemInstance item)
        {
            return item.SchemeName.EqualsIgnoreCase("JOINT");
        }

        /// <summary>
        /// The smoke of a Humans.mds animation - its first particle effect (LIGHTSMOKE) - puffed out in front of the VR
        /// head after startDelay. animationName only names it in the warning when it has none.
        /// </summary>
        [CanBeNull]
        private Coroutine TryExhaleSmoke([CanBeNull] IAnimation smokeAnimation, string animationName,
            float inhaleSeconds, float startDelay, int puffCount = 0)
        {
            var pfxName = smokeAnimation?.ParticleEffects.FirstOrDefault()?.Name;
            if (string.IsNullOrEmpty(pfxName))
            {
                Logger.LogWarning($"[VRMouth] No smoke PFX in '{animationName}'.", LogCat.VR);
                return null;
            }

            return StartCoroutine(ExhaleSmoke(pfxName, inhaleSeconds, startDelay, puffCount));
        }

        /// <summary>
        /// The particle effect pfxName puffed out in front of the VR head.
        /// </summary>
        private IEnumerator ExhaleSmoke(string pfxName, float inhaleSeconds, float startDelay, int puffCount = 0)
        {
            var puffs = Mathf.Max(1, puffCount > 0 ? puffCount : _smokePuffs);
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

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

            // Only eat what's held in a hand. Items in the backpack or the NPC loot panel sit in sockets
            // and can pass the mouth when those open near the head. Any backend: the items carry the components of
            // all of them - asking HVR's grabbable on XRI said "not in a hand" and nothing was ever eaten.
            var interactable = go.GetComponentInParent<VrInteractable>();
            if (interactable != null && !IsInHand(interactable))
                return false;

            return true;
        }

        private bool TryExtractSfx(ItemInstance item, out AudioClip clip)
        {
            clip = null;

            var animationName = string.Format(_animationSchemeWithSfx, item.SchemeName);
            var anim = TryGetUseAnimation(item);
            if (anim == null)
            {
                Logger.LogWarning($"Humans.mds: '{animationName}' animation not found. Eating/drinking sound skipped.",
                    LogCat.VR);
                return false;
            }

            var sfx = anim.SoundEffects.FirstOrDefault();
            if (sfx == null)
            {
                Logger.LogWarning($"Humans.mds: '{animationName}' has no sound effect. Eating/drinking sound skipped.",
                    LogCat.VR);
                return false;
            }

            var sfxContainer = _vmCacheService.TryGetSfxData(sfx.Name);
            if (sfxContainer == null)
            {
                Logger.LogWarning($"SFX '{sfx.Name}' referenced by '{animationName}' not found. Eating/drinking " +
                                  "sound skipped.",
                    LogCat.VR);
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
            return TryGetHumansAnimation(string.Format(_animationSchemeWithSfx, item.SchemeName));
        }

        [CanBeNull]
        private IAnimation TryGetHumansAnimation(string animationName)
        {
            var mds = _resourceCacheService.TryGetModelScript("Humans");
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
            var interactable = go.GetComponentInChildren<VrInteractable>();
            var isHeld = interactable != null && IsInHand(interactable);

            var vobItem = go.GetComponent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem != null && vobItem.Amount > 1)
            {
                vobItem.Amount--;
                if (isHeld)
                    _playerService.RemoveItem(vobItem.GetSymbolName(), 1);

                Logger.Log($"[VRMouth] Stack decremented: {go.name} remaining={vobItem.Amount}", LogCat.VR);
                yield break;
            }

            // Release first - that removes it from the inventory like any dropped item.
            if (isHeld)
                ReleaseFromHands(interactable);

            // Eaten, it leaves the world's item list too - destroying only the GameObject left it there.
            var container = go.GetComponent<VobLoader>()?.Container;
            if (container != null)
            {
                _vobService.RemoveWorldItem(container);
                yield break;
            }

            Destroy(go);
        }

        private bool IsInHand(VrInteractable interactable)
        {
            return _vrPlayerService.GrabbedItemLeft == interactable.gameObject ||
                   _vrPlayerService.GrabbedItemRight == interactable.gameObject;
        }

        private void ReleaseFromHands(VrInteractable interactable)
        {
            var rig = _vrPlayerService.VRContextInteractionService?.Rig;
            if (rig == null)
                return;

            foreach (var hand in rig.HandGrabbers)
            {
                if (hand.Held == interactable)
                    hand.ForceRelease();
            }
        }

        private void CallOnState(GameObject go)
        {
            var item = (go.GetComponentInParent<VobLoader>()?.Container?.Props as VobItemProperties2)?.Instance;
            if (item == null)
                return;

            var onStateIndex = item.GetOnState(0);
            if (onStateIndex == 0)
                return;

            _itemUseService.UseItem(_gameStateService.GothicVm.GlobalHero as NpcInstance, item);
        }
    }
}
