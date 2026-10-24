#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrCrossbow): a held crossbow shoots bolts. Aim = the crossbow itself (HVR orients a
    /// two-hand grab from both hands), the trigger of a holding hand fires. Like the engine (OpenGothic Npc::shootBow):
    /// 1 munition item (bolt) per shot from the hero's inventory, 30 m/s with gravity, damage via RangedHit.
    /// DeveloperConfig.EnableVrCrossbowManualReload + the "crossbowReload" option (Immersion menu): after a shot the
    /// string sits at the prod (VRCrossbowString, Gothic's own shapes) - the free hand takes it there (trigger), pulls
    /// it back along the stock and it locks at the end (BowReload, like the NPCs' t_CBowReload). Let go earlier, it snaps
    /// back. A cocked crossbow shows a bolt on the stock if the hero has one - that bolt flies.
    /// Off (or a mesh without the shapes): it reloads by itself after _reloadSeconds.
    /// </summary>
    public class VRCrossbow : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly Gothic.Core.Services.Context.ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly VRRangedService _vrRangedService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly ConfigService _configService;

        private const int _crossbowTalent = 4; // NPC_TALENT_CROSSBOW
        private const int _masterSkill = 2;
        private const int _oneHandPercent = 60;

        private const float _reloadSeconds = 1.5f;
        private const string _shootSfx = "CrossbowShoot";
        private const string _reloadSfx = "CrossbowReload";
        // The NPCs' t_CBowReload plays BowReload.
        private const string _manualReloadSfx = "BowReload";

        // Meters from the string (nock) in which the free hand's trigger takes it, the share of the way that locks it.
        // The trigger, not the grip: the grip near the stock grabbed the crossbow itself.
        private const float _grabStringDistance = 0.12f;
        private const float _lockProgress = 0.97f;
        private const float _hapticStep = 0.1f;
        private const float _ammoCheckSeconds = 0.5f;

        // Local forward axis (towards the prod) per mesh - computed once from the vertices.
        private static readonly Dictionary<string, Vector3> _forwardAxisCache = new();

        private ItemInstance _item;
        private ItemInstance _munition;
        private Vector3 _localForward = Vector3.forward;
        private Vector3 _localMuzzle;
        private bool _isLoaded = true;
        private float _reloadTimer;
        private bool _hasLoggedNoAmmo;

        private VRCrossbowString _string;
        private bool _isManualReload;
        private bool _isPulling;
        private HVRHandSide _pullHand;
        private float _lastHapticProgress;
        private GameObject _bolt;
        private bool _hasAmmo;
        private float _nextAmmoCheck;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _item = GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (_item == null)
            {
                Logger.LogWarning("[VRCrossbow] ItemInstance not found on parent VobLoader", LogCat.VR);
                enabled = false;
                return;
            }

            _munition = _vrRangedService.GetMunition(_item);
            CalculateAxes();
            SetupString();

            _vrWeaponService.SetRangedReadied(VmGothicEnums.WeaponState.CBow);
            Logger.Log($"[VRCrossbow] {_item.Name} readied - munition={_munition?.Name ?? "none"} " +
                       $"forward(local)={_localForward} muzzle(local)={_localMuzzle} loaded={_isLoaded} " +
                       $"manualReload={_isManualReload}", LogCat.VR);
        }

        /// <summary>
        /// DeveloperConfig.EnableVrCrossbowManualReload: the cocked state lives on the item (VRCrossbowString) - a
        /// fired crossbow picked up again stays fired.
        /// </summary>
        private void SetupString()
        {
            if (!_configService.Dev.EnableVrCrossbowManualReload)
                return;

            if (!TryGetComponent(out _string))
                _string = gameObject.AddComponent<VRCrossbowString>();
            _string.Init(_item.Visual);
            if (!_string.IsAnimated)
                return;

            _isLoaded = _string.IsLoaded;
            _isManualReload = _configService.Gothic.GetInt(VRConstants.IniNames.CrossbowReload, 1) == 1;
        }

        private void OnDestroy()
        {
            if (_item != null)
                _vrWeaponService.SetRangedReadied(null);

            // Let go mid-pull: the string snaps back.
            if (_isPulling && _string != null)
                _string.SetProgress(0f);
            DestroyBolt();
        }

        private void Update()
        {
            if (_item == null)
                return;

            if (!_isLoaded)
            {
                if (_isManualReload)
                {
                    UpdateManualReload();
                }
                else
                {
                    _reloadTimer -= Time.deltaTime;
                    if (_reloadTimer <= 0f)
                    {
                        _isLoaded = true;
                        if (_string != null)
                            _string.SetProgress(1f);
                        PlaySfx(_reloadSfx);
                    }
                }
            }

            UpdateBolt();

            if (!IsTriggerPressed(out var handSide))
                return;

            // No shooting while knocked out.
            if (_npcService.GetHeroContainer()?.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            if (!_isLoaded)
                return;

            Shoot(handSide);
        }

        private bool IsTriggerPressed(out HVRHandSide handSide)
        {
            handSide = HVRHandSide.Right;
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                return Keyboard.current[Key.R].wasPressedThisFrame;

            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                var heldItem = side == HVRHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
                if (heldItem != gameObject)
                    continue;

                if (HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated)
                {
                    handSide = side;
                    if (IsTwoHandsRequired() && !_vrPlayerService.IsDualGrabbed)
                    {
                        // Too heavy for one hand - hold it with both (a crossbow master shoots one-handed).
                        _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Warning);
                        return false;
                    }
                    return true;
                }
            }

            return false;
        }

        private void Shoot(HVRHandSide handSide)
        {
            var hero = _npcService.GetHeroContainer();
            if (hero == null || _munition == null)
                return;

            if (!_vrRangedService.HasAmmo(hero, _munition))
            {
                if (!_hasLoggedNoAmmo)
                    Logger.Log($"[VRCrossbow] No {_munition.Name} left in the inventory.", LogCat.VR);
                _hasLoggedNoAmmo = true;
                _hapticsService.Vibrate(handSide, VrHapticsService.VibrationType.Error);
                return;
            }
            _hasLoggedNoAmmo = false;

            _vrRangedService.ConsumeAmmo(hero, _munition);

            var forward = transform.TransformDirection(_localForward).normalized;
            // The bolt on the stock flies from where it lies.
            var origin = _bolt != null ? _bolt.transform.position : transform.TransformPoint(_localMuzzle);
            var bolt = _bolt;
            _bolt = null;
            _vrRangedService.Shoot(hero, _item, _munition, origin, forward, VRRangedService.ProjectileSpeed, 1f, bolt);

            _isLoaded = false;
            _reloadTimer = _reloadSeconds;
            if (_string != null)
                _string.SetProgress(0f);
            PlaySfx(_shootSfx);
            _hapticsService.Vibrate(handSide, VrHapticsService.VibrationType.Success);
        }

        /// <summary>
        /// The free hand grabs the string at the prod and pulls it back along the stock; at the end it locks.
        /// </summary>
        private void UpdateManualReload()
        {
            var nockWorld = transform.TransformPoint(_string.CurrentNockLocal);
            if (!_isPulling)
            {
                if (!TryGetFreeHand(out var side) ||
                    !HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated)
                    return;
                var hand = _vrPlayerService.GetHandModelGo(side);
                if (hand == null || Vector3.Distance(hand.transform.position, nockWorld) > _grabStringDistance)
                    return;
                if (_npcService.GetHeroContainer()?.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                    return;

                _isPulling = true;
                _pullHand = side;
                _lastHapticProgress = _string.Progress;
                _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Info);
                Logger.Log($"[VRCrossbow] {_item.Name}: string grabbed ({side})", LogCat.VR);
                return;
            }

            var pullingHand = _vrPlayerService.GetHandModelGo(_pullHand);
            if (pullingHand == null || !HVRController.GetButtonState(_pullHand, HVRButtons.Trigger).Active)
            {
                // Let go before it locked: the string snaps back to the prod.
                _isPulling = false;
                _string.SetProgress(0f);
                Logger.Log($"[VRCrossbow] {_item.Name}: string let go - not cocked", LogCat.VR);
                return;
            }

            var fired = transform.TransformPoint(_string.FiredNockLocal);
            var cocked = transform.TransformPoint(_string.CockedNockLocal);
            var way = cocked - fired;
            var progress = Mathf.Clamp01(Vector3.Dot(pullingHand.transform.position - fired, way) / way.sqrMagnitude);
            _string.SetProgress(progress >= _lockProgress ? 1f : progress);

            if (Mathf.Abs(progress - _lastHapticProgress) >= _hapticStep)
            {
                _lastHapticProgress = progress;
                _hapticsService.Vibrate(_pullHand, VrHapticsService.VibrationType.Info);
            }

            if (progress < _lockProgress)
                return;

            _isPulling = false;
            _isLoaded = true;
            PlaySfx(_manualReloadSfx);
            _hapticsService.Vibrate(_pullHand, VrHapticsService.VibrationType.Success);
            Logger.Log($"[VRCrossbow] {_item.Name}: cocked", LogCat.VR);
        }

        /// <summary>
        /// The hand not holding the crossbow - empty (both hands on the crossbow: let go with one to reload).
        /// </summary>
        private bool TryGetFreeHand(out HVRHandSide side)
        {
            side = HVRHandSide.Right;
            var isLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isRight = _vrPlayerService.GrabbedItemRight == gameObject;
            if (isLeft == isRight)
                return false;

            side = isLeft ? HVRHandSide.Right : HVRHandSide.Left;
            var otherItem = isLeft ? _vrPlayerService.GrabbedItemRight : _vrPlayerService.GrabbedItemLeft;
            return otherItem == null;
        }

        /// <summary>
        /// A cocked crossbow (with the shapes) shows a bolt on the stock if the hero has one: its tail at the string.
        /// </summary>
        private void UpdateBolt()
        {
            if (_string == null || !_string.IsAnimated || _munition == null)
                return;

            if (Time.time >= _nextAmmoCheck)
            {
                _nextAmmoCheck = Time.time + _ammoCheckSeconds;
                var hero = _npcService.GetHeroContainer();
                _hasAmmo = hero != null && _vrRangedService.HasAmmo(hero, _munition);
            }

            var isShown = _isLoaded && _hasAmmo;
            if (!isShown)
            {
                DestroyBolt();
                return;
            }
            if (_bolt != null)
                return;

            _bolt = _vrRangedService.CreateAmmoVisual(_munition, transform);
            var mesh = _bolt.transform.GetChild(0);
            var bounds = VRRangedService.GetLocalBounds(_bolt);
            mesh.localPosition -= new Vector3(0f, 0f, bounds.min.z);
            _bolt.transform.localScale = Vector3.one / Mathf.Max(transform.lossyScale.x, 0.0001f);
            _bolt.transform.SetLocalPositionAndRotation(_string.CockedNockLocal,
                Quaternion.LookRotation(_localForward, Vector3.up));
        }

        private void DestroyBolt()
        {
            if (_bolt != null)
                Destroy(_bolt);
            _bolt = null;
        }

        /// <summary>
        /// DeveloperConfig.EnableCrossbowMasterOneHand: only a trained crossbowman shoots with one hand (sword in the other).
        /// G1/mods: crossbow talent skill "master" or 60 %+; G2: crossbow hitchance 60 %+.
        /// </summary>
        private bool IsTwoHandsRequired()
        {
            if (!_configService.Dev.EnableCrossbowMasterOneHand)
                return false;

            var hero = _npcService.GetHeroContainer();
            if (hero == null)
                return false;

            var talent = hero.Vob.GetTalent(_crossbowTalent);
            var percent = _contextGameVersionService.IsGothic2()
                ? hero.Instance.GetHitChance((ZenKit.Daedalus.NpcTalent)_crossbowTalent)
                : talent?.Value ?? 0;
            var isMaster = (talent?.Skill ?? 0) >= _masterSkill || percent >= _oneHandPercent;
            return !isMaster;
        }

        /// <summary>
        /// Forward = the barrel axis, pointing to the wider end (prod/arms). Muzzle = that end.
        /// </summary>
        private void CalculateAxes()
        {
            var key = _item.Visual ?? string.Empty;
            if (!_forwardAxisCache.TryGetValue(key, out _localForward))
            {
                _localForward = VRRangedService.GetLongAxis(gameObject, towardsWiderEnd: true, isAsymmetryAxis: true);
                _forwardAxisCache[key] = _localForward;
            }

            var bounds = VRRangedService.GetLocalBounds(gameObject);
            var halfLength = Vector3.Scale(bounds.extents, _localForward).magnitude;
            _localMuzzle = bounds.center + _localForward * halfLength;
        }

        private void PlaySfx(string sfxName)
        {
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
#endif
