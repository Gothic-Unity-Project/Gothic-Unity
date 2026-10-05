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
    /// Reload is automatic for now - see vr-ranged-spells-plan.md (reload mechanic TODO).
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

        // Local forward axis (towards the prod) per mesh - computed once from the vertices.
        private static readonly Dictionary<string, Vector3> _forwardAxisCache = new();

        private ItemInstance _item;
        private ItemInstance _munition;
        private Vector3 _localForward = Vector3.forward;
        private Vector3 _localMuzzle;
        private bool _isLoaded = true;
        private float _reloadTimer;
        private bool _hasLoggedNoAmmo;


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

            _vrWeaponService.SetRangedReadied(VmGothicEnums.WeaponState.CBow);
            Logger.Log($"[VRCrossbow] {_item.Name} readied - munition={_munition?.Name ?? "none"} " +
                       $"forward(local)={_localForward} muzzle(local)={_localMuzzle}", LogCat.VR);
        }

        private void OnDestroy()
        {
            if (_item != null)
                _vrWeaponService.SetRangedReadied(null);
        }

        private void Update()
        {
            if (_item == null)
                return;

            if (!_isLoaded)
            {
                _reloadTimer -= Time.deltaTime;
                if (_reloadTimer <= 0f)
                {
                    _isLoaded = true;
                    PlaySfx(_reloadSfx);
                }
            }

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
            var muzzle = transform.TransformPoint(_localMuzzle);
            _vrRangedService.Shoot(hero, _item, _munition, muzzle, forward, VRRangedService.ProjectileSpeed, 1f);

            _isLoaded = false;
            _reloadTimer = _reloadSeconds;
            PlaySfx(_shootSfx);
            _hapticsService.Vibrate(handSide, VrHapticsService.VibrationType.Success);
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
