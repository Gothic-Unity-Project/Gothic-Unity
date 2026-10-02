#if GOTHIC_HVR_INSTALLED
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Adapters.Vob;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Core.Bags;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Added dynamically to a rune/scroll GO when dual-grabbed.
    /// Right trigger (or R on WASD) casts via Spell_ProcessMana() — Daedalus handles all spell logic.
    /// Targeting: raycast from rune forward axis to set vm.GlobalOther before each Spell_ProcessMana call.
    /// Telekinesis: 2-trigger system — trigger 1 extends ForceGrab range, trigger 2 pulls hovered item.
    /// </summary>
    public class VRRuneCaster : MonoBehaviour
    {
        // spells_params.d constants
        private const int _splSendcast = 2;
        private const int _splSendstop = 3;
        private const int _splNextlevel = 4;

        // Raycast range for NPC targeting
        private const float _targetRange = 2000f;

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly VrHapticsService _hapticsService;

        private const string _telekinesisName = "Telekinesis";
        private const float _telekinesisRange = 5000f;

        private const string _lightName = "Light";
        private const float _lightRange = 6f;
        private const float _lightIntensity = 1.5f;
        private const float _lightDurationSeconds = 90f;
        private static readonly Color _lightColor = new(1f, 0.85f, 0.6f);

        private ItemInstance _item;
        private bool _isCasting;
        private bool _castThisGrab;
        private int _manaInvested;
        private NpcContainer _spellTarget;
        private StatusBarAdapter _manaBar;
        private AudioSource _investAudioSource;
        private bool _telekinesisPrepped;
        private bool _isTargeting; // combat spells: trigger 1 pressed, waiting for target + trigger 2
        private HVRHandSide _runeHandSide;
        private GameObject _spellVfxGo;
        public bool IsTargetingActive => _isTargeting;

        // Throwable spells (DeveloperConfig.EnableThrowableSpells) - see vr-ranged-spells-plan.md section 3.
        public bool IsThrowable => _throwCastKey != null;
        private ParticleEffectEmitKeyInstance _throwCastKey;
        private bool _isThrowCharged;
        private HVRHandSide _throwHandSide;
        // Invest level (Spell_ProcessMana returned SPL_NEXTLEVEL, Fireball at 1/2/4 of 5 mana): like the engine the
        // VISUALFX key spellFX_<name>_KEY_INVEST_<level> replaces the effect in the hand (MFX_Fireball_INVEST_L2, ...),
        // plays its sound (MFX_Fireball_invest1..4) and its emCreateFXID burst; the charge also grows a bit.
        private int _investLevel;
        private const float _investLevelScale = 0.25f;
        private const int _maxInvestLevel = 4;
        // Throw speed: the spell's own speed (emtrjeasevel) scaled by the swing - a lazy flick is slow.
        private const float _fullSwingSpeed = 3f;
        private const float _minSwingFactor = 0.4f;
        private const float _maxSwingFactor = 1.5f;
        private float _throwSpeedFactor = 1f;
        private readonly Queue<(float time, Vector3 position)> _handSamples = new();
        private const float _handVelocityWindow = 0.1f;
        private const float _minThrowSpeed = 1.5f;
        private const float _homingConeDegrees = 12f;
        private const float _homingDegreesPerSecond = 90f;
        private const float _defaultThrowRange = 30f;
        private const float _collideFxSeconds = 3f;
        private float _manaTickTimer;
        private const float _manaTickInterval = 0.5f; // seconds per mana invested; tunes cast speed

        private void Awake()
        {
            this.Inject();
            _investAudioSource = gameObject.AddComponent<AudioSource>();
            _investAudioSource.loop = true;
            _investAudioSource.spatialBlend = 1f;
        }

        private void Start()
        {
            _item = GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (_item == null)
            {
                Logger.LogWarning("[VRRuneCaster] ItemInstance not found on parent VobLoader", LogCat.VR);
                return;
            }

            var hero = _npcService.GetHeroContainer();
            if (hero != null)
                hero.ActiveSpell = _item.Spell;

            // Readied rune == FMODE_MAGIC for Daedalus (B_AssessFighter checks Npc_IsInFightMode + Npc_GetActiveSpellCat).
            _vrWeaponService.SetRuneReadied(true);

            if (_configService.Dev.EnableThrowableSpells)
                _throwCastKey = GetThrowableCastKey(_item.Spell);

            ShowManaBar();
        }

        private void OnDestroy()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero != null)
                hero.ActiveSpell = 0;

            // Only if Start() readied it (no item -> never readied).
            if (_item != null)
                _vrWeaponService.SetRuneReadied(false);

            _isTargeting = false;
            _vrPlayerService.DeactivateSpellTargeting();
            _isCasting = false;

            DestroySpellVfx();
            StopInvestSound();
            if (_investAudioSource != null)
                Destroy(_investAudioSource);
            _vrPlayerService.TelekinesisDeactivated -= OnTelekinesisEnded;

            // If rune is still held by one hand, telekinesis stays active so the freed hand can grab.
            // If both hands released, reset range immediately.
            var runeStillHeld = _vrPlayerService.GrabbedItemLeft == gameObject
                             || _vrPlayerService.GrabbedItemRight == gameObject;
            if (!runeStillHeld)
                _vrPlayerService.DeactivateTelekinesis();

            HideManaBar();
        }

        private void Update()
        {
            if (_item == null || _castThisGrab) return;

            // No casting while knocked out.
            if (_npcService.GetHeroContainer()?.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            bool triggered;
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                triggered = Keyboard.current[Key.R].wasPressedThisFrame;
            else
                triggered = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Trigger).JustActivated;

            // Telekinesis uses a 2-trigger system and skips Daedalus entirely.
            if (IsTelekinesisSpell())
            {
                if (triggered)
                    HandleTelekinesisInput();
                return;
            }

            if (IsThrowable)
            {
                UpdateThrowable();
                return;
            }

            UpdateSpellTarget();

            if (triggered && !_isTargeting && !_isCasting)
            {
                // Trigger 1: enter targeting mode — extend rune-hand bags, SFX starts
                _isTargeting = true;
                _runeHandSide = (_vrPlayerService.GrabbedItemLeft == gameObject) ? HVRHandSide.Left : HVRHandSide.Right;
                _vrPlayerService.ActivateSpellTargeting(_runeHandSide, _targetRange);
                StartInvestSound(_item.Spell);
                SpawnSpellVfx();
                Logger.Log($"[VRRuneCaster] Targeting — spell {_item.Spell} ({_item.Name}), aim rune hand at NPC and press trigger to fire", LogCat.VR);
            }
            else if (triggered && _isTargeting && !_isCasting)
            {
                // Trigger 2: confirm target and start mana investment. The effect stays in the hand while charging.
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
                _isCasting = true;
                _manaInvested = 0;
                _manaTickTimer = _manaTickInterval;

                // Like the engine: NPCs notice the casting (B_AssessCaster -> reacts to offensive spells only).
                if (_configService.Dev.EnableCasterPerception)
                    _npcAiService.SendHeroCasterPerception();
                Logger.Log($"[VRRuneCaster] Cast confirmed — target={_spellTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none"}", LogCat.VR);
            }
            else if (triggered && _isCasting && _manaInvested > 0)
            {
                // Trigger 3+: fire now at whatever level has been charged so far. Chargeable spells
                // (Fireball, Firestorm, Windfist, Stormfist, Thunderball, Firerain) only auto-fire via
                // Daedalus once mana hits the max threshold — without this, we'd always charge to max
                // (or fizzle if mana runs out first) and never let the player release early for a
                // quicker, weaker cast, exactly like releasing CTRL early in the original engine.
                Logger.Log($"[VRRuneCaster] Fire-now triggered at level {_manaInvested}", LogCat.VR);
                FinalizeCast(applyEffect: true);
                return;
            }

            if (!_isCasting) return;

            if (!IsManaTickDue())
                return;

            if (!TryInvestMana(out var result))
            {
                Logger.Log("[VRRuneCaster] Out of mana — spell cancelled", LogCat.VR);
                StopInvestSound();
                DestroySpellVfx();
                _isCasting = false;
                _castThisGrab = true;
                return;
            }

            if (result == _splSendcast || result == _splSendstop)
            {
                Logger.Log($"[VRRuneCaster] result={result} after {_manaInvested} ticks — spell fired", LogCat.VR);
                FinalizeCast(applyEffect: result == _splSendcast);
            }
        }

        /// <summary>
        /// Throttle to one mana tick per interval — mirrors Gothic's C++ magic tick rate.
        /// </summary>
        private bool IsManaTickDue()
        {
            _manaTickTimer += Time.deltaTime;
            if (_manaTickTimer < _manaTickInterval)
                return false;
            _manaTickTimer -= _manaTickInterval;
            return true;
        }

        /// <summary>
        /// Spends one mana and asks Daedalus (Spell_ProcessMana) - false if the hero has no mana left.
        /// </summary>
        private bool TryInvestMana(out int result)
        {
            result = 0;
            var hero = _npcService.GetHeroContainer();
            var currentMana = hero.Vob.GetAttribute((int)NpcAttribute.Mana);
            if (currentMana <= 0)
                return false;

            hero.Vob.SetAttribute((int)NpcAttribute.Mana, currentMana - 1);
            // Keep the NpcInstance in sync too — SyncHeroInstanceToVob() (called elsewhere e.g. on XP
            // grant) blindly copies every attribute FROM the instance INTO Vob, so a Vob-only write here
            // would get silently reverted the next time that runs (this was the "mana snaps back" bug).
            hero.Instance.SetAttribute(NpcAttribute.Mana, currentMana - 1);
            RefreshManaFill();

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = vm.GlobalHero;
            vm.GlobalOther = _spellTarget?.Instance ?? vm.GlobalHero;
            try
            {
                result = vm.Call<int, int>("Spell_ProcessMana", ++_manaInvested);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
            return true;
        }

        /// <summary>
        /// Ends the current cast — either because Daedalus auto-fired at max charge, sent an explicit
        /// stop, or the player released early (trigger 3+) to fire at whatever level was reached.
        /// </summary>
        private void FinalizeCast(bool applyEffect)
        {
            StopInvestSound();
            DestroySpellVfx();
            PlayCastSound(_item.Spell);

            if (applyEffect)
            {
                var mfxName = GetSpellMfxName(_item.Spell);

                // Light has no Daedalus-side hook at all (Spell_Logic_Light only gates the cast) —
                // in the original engine it's a hardcoded C++ toggle, so it's the one spell effect
                // we implement directly rather than routing through Spell_ProcessMana/ASSESSMAGIC.
                if (mfxName == _lightName)
                {
                    ToggleLightSpell();
                }
                else
                {
                    var spellDmg = GetSpellDamage(_item.Spell) * _manaInvested;
                    var hero = _npcService.GetHeroContainer();
                    var isAoe = mfxName != null && SpellConst.AoeEffectNames.Contains(mfxName);

                    if (_spellTarget != null && _spellTarget.Go != null)
                    {
                        // SpellHit both applies direct damage (if any) and fires PERC_ASSESSMAGIC on the
                        // target, so Daedalus content (ZS_MagicFreeze, ZS_MagicSleep, ZS_Zapped, Fear/
                        // Charm/Berzerk...) drives the actual reaction — nothing per-spell to hardcode here.
                        // For AOE spells FightService.OnSpellHit ignores this single target anyway and
                        // fans out to everyone in range of the caster.
                        GlobalEventDispatcher.SpellHit.Invoke(hero, _spellTarget, _spellTarget.Go.transform.position, spellDmg);
                    }
                    else if (isAoe)
                    {
                        // AOE spells (Icewave, ChainLightning...) hit everyone around the caster, not a
                        // single aimed target — don't require one to be selected. hero/heroPos here are
                        // just placeholders; FightService.OnSpellHit re-resolves the real affected NPCs.
                        GlobalEventDispatcher.SpellHit.Invoke(hero, hero, hero.Go.transform.position, spellDmg);
                    }
                    else if (spellDmg == 0 && mfxName != "Heal" && !SpellConst.SummonEffectNames.Contains(mfxName ?? string.Empty))
                    {
                        Logger.LogWarning($"[VRRuneCaster] spell effect '{mfxName}' has no target and no known self-effect handling in C#/Daedalus — likely a no-op", LogCat.VR);
                    }
                }
            }

            // TODO scrolls: consume one from stack, destroy if last
            // var isScroll = (_item.Flags & ItemFlags.Multi) != 0;
            // if (isScroll) ConsumeScroll();

            _isCasting = false;
            // Allow recasting without re-grabbing: _castThisGrab stays false
        }

        /// <summary>
        /// Light has no Daedalus-side effect to invoke — it's a hardcoded engine toggle in the
        /// original game too. State lives on the hero NpcContainer (not this component) so the light
        /// survives ungrabbing the rune; casting Light again is what turns it back off.
        /// </summary>
        private void ToggleLightSpell()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null) return;

            if (hero.ActiveLightGo != null)
            {
                Destroy(hero.ActiveLightGo);
                hero.ActiveLightGo = null;
                _stationaryLightsService.ClearDynamicLight();
                Logger.Log("[VRRuneCaster] Light spell OFF", LogCat.VR);
                return;
            }

            var lightGo = new GameObject("SpellLight");
            lightGo.transform.SetParent(hero.Go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.6f, 0f); // roughly head height

            // StationaryLight (not a plain Light) so the world's baked-lighting shader — which never
            // samples ordinary Unity/URP realtime lights, only its own static per-renderer index array
            // — also picks this up via the one slot reserved for a runtime-moving light. The attached
            // Light component (added by [RequireComponent]) still covers items/hands/NPCs normally.
            var stationaryLight = lightGo.AddComponent<StationaryLight>();
            stationaryLight.Inject();
            stationaryLight.Type = LightType.Point;
            stationaryLight.Color = _lightColor;
            stationaryLight.Range = _lightRange;
            stationaryLight.Intensity = _lightIntensity;
            stationaryLight.Index = _stationaryLightsService.DynamicLightIndex;
            stationaryLight.Init();

            _meshService.CreateVobPfx("MFX_LIGHT_INIT", parent: lightGo);

            hero.ActiveLightGo = lightGo;
            Logger.Log("[VRRuneCaster] Light spell ON", LogCat.VR);
            _unityMonoService.StartCoroutine(AutoTurnOffLight(hero, lightGo));
            _unityMonoService.StartCoroutine(DriveDynamicLight(lightGo, stationaryLight));
        }

        /// <summary>
        /// Periodically pushes the light's current (moving-with-the-player) world position into the
        /// global shader arrays and re-gathers nearby renderers, so the world-lighting effect actually
        /// follows the player instead of staying fixed at the cast position. Not done every frame —
        /// each update re-uploads the whole global light array — a few times a second is plenty for a
        /// walking-speed light.
        /// </summary>
        private IEnumerator DriveDynamicLight(GameObject lightGo, StationaryLight stationaryLight)
        {
            const float refreshInterval = 0.3f;
            var linearColor = _lightColor.linear;

            while (lightGo != null)
            {
                _stationaryLightsService.UpdateDynamicLight(lightGo.transform.position, _lightRange, linearColor);
                stationaryLight.Refresh();
                yield return new WaitForSeconds(refreshInterval);
            }
        }

        /// <summary>
        /// Compares against the current ActiveLightGo (not a cancelled-coroutine flag) so it's a no-op
        /// if the player already toggled the light off — or back on again — before this fires.
        /// </summary>
        private IEnumerator AutoTurnOffLight(NpcContainer hero, GameObject lightGo)
        {
            yield return new WaitForSeconds(_lightDurationSeconds);
            if (hero.ActiveLightGo != lightGo) yield break;

            Destroy(lightGo);
            hero.ActiveLightGo = null;
            _stationaryLightsService.ClearDynamicLight();
            Logger.Log("[VRRuneCaster] Light spell expired", LogCat.VR);
        }

        private void SpawnSpellVfx() => SpawnSpellVfx(_runeHandSide);

        private void SpawnSpellVfx(HVRHandSide handSide, string pfxNameOverride = null)
        {
            DestroySpellVfx();
            var mfxName = GetSpellMfxName(_item.Spell);
            if (mfxName == null) return;
            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (handGo == null) return;
            var pfxName = pfxNameOverride ?? $"MFX_{mfxName.ToUpper()}_INIT";
            var pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            if (pfxLeaf == null)
            {
                pfxName = "MFX_FIREBALL_INIT";
                pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            }
            if (pfxLeaf == null)
            {
                pfxName = "MFX_FIREBOLT_INIT";
                pfxLeaf = _meshService.CreateVobPfx(pfxName, parent: handGo);
            }

            if (pfxLeaf != null)
            {
                // Build() returns pfxGo (child of RootGo). Store RootGo so DestroySpellVfx
                // cleans up the whole tree, not just the inner leaf.
                var parent = pfxLeaf.transform.parent;
                _spellVfxGo = (parent != null && parent.gameObject != handGo) ? parent.gameObject : pfxLeaf;

                // All systems loop: the effect stays in the hand as long as the spell is charged (it vanished before).
                foreach (var ps in _spellVfxGo.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = true;
                    // Default (Local) ignores the parent's scale - the invest level size wouldn't show.
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
                    // Gothic INIT spell PFX use ppsValue=500 but our builder divides by 100 → only 5/s.
                    // With 0.15s lifetime that's <1 particle visible — boost emission for VR first-person.
                    var emission = ps.emission;
                    emission.rateOverTime = 50f;
                    ps.Play();
                }
                var parentName = _spellVfxGo.transform.parent != null ? _spellVfxGo.transform.parent.name : "none";
                Logger.Log($"[VRRuneCaster] spell VFX '{pfxName}' on {parentName}", LogCat.VR);
            }
        }

        private void DestroySpellVfx()
        {
            if (_spellVfxGo == null) return;
            Destroy(_spellVfxGo);
            _spellVfxGo = null;
        }

        /// <summary>
        /// Projectile spells are thrown: the VISUALFX CAST key flies to a target (emtrjmode_s TARGET) with a speed and
        /// collision - Firebolt, Fireball, Thunderbolt, Icecube, ... (G2/mod spells automatically). Null = cast as before.
        /// </summary>
        private ParticleEffectEmitKeyInstance GetThrowableCastKey(int spellId)
        {
            var mfxName = GetSpellMfxName(spellId);
            if (mfxName == null)
                return null;

            var castKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_CAST");
            var isThrowable = castKey != null &&
                              castKey.EmTrjModeS.Contains("TARGET", System.StringComparison.OrdinalIgnoreCase) &&
                              castKey.EmTrjEaseVel > 0f && castKey.EmCheckCollision != 0;

            Logger.Log($"[VRRuneCaster] spellFX_{mfxName}: throwable={isThrowable} " +
                       $"(trj={castKey?.EmTrjModeS ?? "-"}, vel={castKey?.EmTrjEaseVel ?? 0}, " +
                       $"coll={castKey?.EmCheckCollision ?? 0}, vis={castKey?.VisNameS ?? "-"})", LogCat.VR);
            return isThrowable ? castKey : null;
        }

        /// <summary>
        /// Throwable spells: the rune is held in one hand, the other (empty) hand holds its trigger to charge (spell PFX
        /// in that palm, one mana per tick, growing with each invest level), swings and releases to throw it.
        /// Max charge (SPL_SENDCAST) waits in the hand instead of firing. Letting go of the rune cancels the spell.
        /// </summary>
        private void UpdateThrowable()
        {
            if (!TryGetThrowHand(out var handSide))
            {
                if (_isCasting)
                    CancelThrow("no free throwing hand");
                return;
            }

            TrackHandPosition(handSide);

            var isWasd = _vrPlayerService.VRPlayerInputs.UseWASD;
            var button = isWasd ? default : HVRController.GetButtonState(handSide, HVRButtons.Trigger);
            var isJustPressed = isWasd ? Keyboard.current[Key.R].wasPressedThisFrame : button.JustActivated;
            var isHeld = isWasd ? Keyboard.current[Key.R].isPressed : button.Active;

            if (isJustPressed && !_isCasting)
                StartThrowCharge(handSide);

            if (!_isCasting)
                return;

            if (!isHeld)
            {
                ReleaseThrow(handSide, isWasd);
                return;
            }

            if (_isThrowCharged || !IsManaTickDue())
                return;

            if (!TryInvestMana(out var result))
            {
                // Out of mana: throw what's charged so far, nothing charged = nothing to throw.
                if (_manaInvested == 0)
                    CancelThrow("out of mana");
                else
                    _isThrowCharged = true;
                return;
            }

            if (result == _splSendstop)
            {
                CancelThrow("SPL_SENDSTOP");
            }
            else if (result == _splNextlevel)
            {
                NextInvestLevel();
            }
            else if (result == _splSendcast)
            {
                _isThrowCharged = true;
                Logger.Log($"[VRRuneCaster] Throw fully charged at level {_manaInvested}", LogCat.VR);
            }
        }

        /// <summary>
        /// The hand that doesn't hold the rune - and holds nothing else.
        /// </summary>
        private bool TryGetThrowHand(out HVRHandSide handSide)
        {
            var isRuneLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isRuneRight = _vrPlayerService.GrabbedItemRight == gameObject;
            handSide = isRuneLeft ? HVRHandSide.Right : HVRHandSide.Left;
            if (isRuneLeft == isRuneRight)
                return false; // both hands on the rune (or none)

            var otherItem = isRuneLeft ? _vrPlayerService.GrabbedItemRight : _vrPlayerService.GrabbedItemLeft;
            return otherItem == null;
        }

        private void StartThrowCharge(HVRHandSide handSide)
        {
            _throwHandSide = handSide;
            _runeHandSide = handSide == HVRHandSide.Left ? HVRHandSide.Right : HVRHandSide.Left;
            _isCasting = true;
            _isThrowCharged = false;
            _manaInvested = 0;
            _investLevel = 0;
            _spellTarget = null;
            _manaTickTimer = _manaTickInterval; // first mana right away

            StartInvestSound(_item.Spell);
            SpawnSpellVfx(handSide);

            if (_configService.Dev.EnableCasterPerception)
                _npcAiService.SendHeroCasterPerception();
            Logger.Log($"[VRRuneCaster] Charging throwable spell {_item.Spell} ({_item.Name}) in {handSide} hand", LogCat.VR);
        }

        /// <summary>
        /// SPL_NEXTLEVEL: the charge in the hand grows, VISUALFX spellFX_<name>_KEY_INVEST_<level> sound (Fireball: 4).
        /// </summary>
        private void NextInvestLevel()
        {
            if (_investLevel >= _maxInvestLevel)
                return;

            _investLevel++;
            var mfxName = GetSpellMfxName(_item.Spell);
            var levelKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INVEST_{_investLevel}");

            // The level's own effect in the hand (falls back to the current one if the key has none).
            if (!string.IsNullOrEmpty(levelKey?.VisNameS))
                SpawnSpellVfx(_throwHandSide, levelKey.VisNameS);
            if (_spellVfxGo != null)
                _spellVfxGo.transform.localScale = Vector3.one * (1f + _investLevelScale * _investLevel);

            // The level's charge sound loops while charging (sfxIsAmbient), the burst plays once.
            if (!string.IsNullOrEmpty(levelKey?.SfxId))
            {
                var clip = _audioService.GetRandomSoundClip(levelKey.SfxId);
                if (clip != null)
                {
                    _investAudioSource.clip = clip;
                    _investAudioSource.Play();
                }
            }
            SpawnInvestBurst(levelKey?.EmCreateFxId);

            _hapticsService.Vibrate(_throwHandSide, VrHapticsService.VibrationType.Info);
            Logger.Log($"[VRRuneCaster] {mfxName} invest level {_investLevel} (mana {_manaInvested})", LogCat.VR);
        }

        /// <summary>
        /// emCreateFXID of an invest key (spellFX_Fireball_InVEST_BLAST1..4): a one-shot burst in the hand.
        /// </summary>
        private void SpawnInvestBurst(string fxId)
        {
            if (string.IsNullOrEmpty(fxId))
                return;

            var fx = _vmCacheService.TryGetVfxData(fxId);
            var handGo = _vrPlayerService.GetHandModelGo(_throwHandSide);
            if (fx == null || handGo == null)
                return;

            if (!string.IsNullOrEmpty(fx.VisNameS))
            {
                var pfxGo = _meshService.CreateVobPfx(fx.VisNameS, handGo.transform.position, handGo.transform.rotation,
                    destroyAfterPlay: true);
                if (pfxGo != null)
                    Destroy(pfxGo.transform.parent != null ? pfxGo.transform.parent.gameObject : pfxGo, _collideFxSeconds);
            }
            if (!string.IsNullOrEmpty(fx.SfxId))
                PlayOneShot(fx.SfxId);
        }

        private void CancelThrow(string reason)
        {
            Logger.Log($"[VRRuneCaster] Throw cancelled ({reason}) at level {_manaInvested}", LogCat.VR);
            StopInvestSound();
            DestroySpellVfx();
            _isCasting = false;
            _isThrowCharged = false;
        }

        /// <summary>
        /// Released the trigger: thrown along the hand's velocity with the spell's own speed (emtrjeasevel, cm/s).
        /// Too slow = fizzles (mana is spent, like an interrupted cast).
        /// </summary>
        private void ReleaseThrow(HVRHandSide handSide, bool isWasd)
        {
            if (_manaInvested == 0)
            {
                CancelThrow("released before the first mana");
                return;
            }

            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (handGo == null)
            {
                CancelThrow("no hand");
                return;
            }

            // A swing throws along the hand's movement; without one (or in the simulator) it flies where you look.
            var handVelocity = isWasd ? Vector3.zero : GetHandVelocity();
            var direction = handVelocity.magnitude >= _minThrowSpeed || Camera.main == null
                ? handVelocity.normalized
                : Camera.main.transform.forward;
            // The simulator can't swing - full speed there.
            _throwSpeedFactor = isWasd
                ? 1f
                : Mathf.Clamp(handVelocity.magnitude / _fullSwingSpeed, _minSwingFactor, _maxSwingFactor);
            if (direction.sqrMagnitude < 0.01f)
                direction = handGo.transform.forward;

            StopInvestSound();
            PlayCastSound(_item.Spell);

            var origin = handGo.transform.position + direction * 0.15f;
            SpawnSpellProjectile(origin, direction, _manaInvested);

            _isCasting = false;
            _isThrowCharged = false;
        }

        private void TrackHandPosition(HVRHandSide handSide)
        {
            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (handGo == null)
                return;

            _handSamples.Enqueue((Time.time, handGo.transform.position));
            while (_handSamples.Count > 2 && Time.time - _handSamples.Peek().time > _handVelocityWindow)
                _handSamples.Dequeue();
        }

        private Vector3 GetHandVelocity()
        {
            if (_handSamples.Count < 2)
                return Vector3.zero;

            var oldest = _handSamples.Peek();
            var newest = _handSamples.Last();
            var deltaTime = newest.time - oldest.time;
            return deltaTime > 0f ? (newest.position - oldest.position) / deltaTime : Vector3.zero;
        }

        private void SpawnSpellProjectile(Vector3 origin, Vector3 direction, int level)
        {
            var hero = _npcService.GetHeroContainer();
            var mfxName = GetSpellMfxName(_item.Spell);
            var damage = GetSpellDamage(_item.Spell) * level;
            var investLevel = _investLevel;
            var speed = _throwCastKey.EmTrjEaseVel / 100f * _throwSpeedFactor;
            var range = GetThrowRange(mfxName);

            var projectileGo = new GameObject($"SpellProjectile ({mfxName})");
            projectileGo.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));

            // The charge the player saw in the hand is what flies (with its invest level size).
            GameObject pfx = null;
            if (_spellVfxGo != null)
            {
                _spellVfxGo.transform.SetParent(projectileGo.transform, false);
                _spellVfxGo.transform.localPosition = Vector3.zero;
                pfx = _spellVfxGo;
                _spellVfxGo = null;
            }
            else
            {
                var castPfxName = string.IsNullOrEmpty(_throwCastKey.VisNameS) ? $"MFX_{mfxName}_INIT" : _throwCastKey.VisNameS;
                pfx = _meshService.CreateVobPfx(castPfxName, parent: projectileGo);
            }
            if (pfx != null)
            {
                foreach (var particles in projectileGo.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.loop = true;
                    particles.Play();
                }
            }

            var light = projectileGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = 2f;
            light.color = GetSpellLightColor(mfxName);

            var homingTarget = FindHomingTarget(hero, origin, direction, range);
            var projectile = projectileGo.AddComponent<VRProjectile>();
            projectile.Owner = hero;
            projectile.Velocity = direction * speed;
            projectile.UseGravity = false;
            projectile.Radius = 0.15f;
            projectile.MaxDistance = range;
            projectile.HomingTarget = homingTarget;
            projectile.HomingDegreesPerSecond = _homingDegreesPerSecond;

            // Captured: the rune (and this component) can be gone before the spell lands.
            var meshService = _meshService;
            var vmCacheService = _vmCacheService;
            var audioService = _audioService;
            var baseFx = vmCacheService.TryGetVfxData($"spellFX_{mfxName}");

            projectile.OnNpcHit = (npc, position, normal) =>
            {
                Logger.Log($"[VRRuneCaster] {mfxName} hit {npc.Instance.GetName(NpcNameSlot.Slot0)} (level {level}, dmg {damage})", LogCat.VR);
                GlobalEventDispatcher.SpellHit.Invoke(hero, npc, position, damage);
                SpawnCollideFx(meshService, vmCacheService, audioService, baseFx?.EmFxCollDynS, investLevel, position, normal);
                Destroy(projectileGo);
            };
            projectile.OnWorldHit = (position, normal) =>
            {
                SpawnCollideFx(meshService, vmCacheService, audioService, baseFx?.EmFxCollStatS, investLevel, position, normal);
                Destroy(projectileGo);
            };
            projectile.OnExpired = () => Destroy(projectileGo);

            Logger.Log($"[VRRuneCaster] Threw {mfxName} mana {level} level {investLevel} at {speed:F1} m/s, " +
                       $"homing={homingTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none"}", LogCat.VR);
        }

        /// <summary>
        /// Collision effect of the spell (emFXCollDyn_S on NPCs, emFXCollStat_S on the world) - per invest level if it
        /// has keys (spellFX_Fireball_COLLIDE_KEY_INVEST_1..4).
        /// </summary>
        private static void SpawnCollideFx(MeshService meshService, VmCacheService vmCacheService,
            AudioService audioService, string fxName, int level, Vector3 position, Vector3 normal)
        {
            if (string.IsNullOrEmpty(fxName))
                return;

            var levelKey = vmCacheService.TryGetVfxEmitKey($"{fxName}_KEY_INVEST_{Mathf.Clamp(level, 1, 4)}");
            var fx = vmCacheService.TryGetVfxData(fxName);
            var pfxName = !string.IsNullOrEmpty(levelKey?.VisNameS) ? levelKey.VisNameS : fx?.VisNameS;
            var sfxName = !string.IsNullOrEmpty(levelKey?.SfxId) ? levelKey.SfxId : fx?.SfxId;

            if (!string.IsNullOrEmpty(pfxName))
            {
                var rotation = normal.sqrMagnitude > 0f ? Quaternion.LookRotation(normal) : Quaternion.identity;
                var pfxGo = meshService.CreateVobPfx(pfxName, position, rotation, destroyAfterPlay: true);
                if (pfxGo != null)
                {
                    var root = pfxGo.transform.parent != null ? pfxGo.transform.parent.gameObject : pfxGo;
                    Destroy(root, _collideFxSeconds);
                }
            }

            if (!string.IsNullOrEmpty(sfxName))
            {
                var clip = audioService.GetRandomSoundClip(sfxName);
                if (clip != null)
                    AudioSource.PlayClipAtPoint(clip, position);
            }
        }

        /// <summary>
        /// Gothic's TARGET trajectory homes on the focus NPC. VR: the living NPC closest to the throw direction
        /// within a small cone - helps without taking the aiming away.
        /// </summary>
        private NpcContainer FindHomingTarget(NpcContainer hero, Vector3 origin, Vector3 direction, float range)
        {
            NpcContainer best = null;
            var bestAngle = _homingConeDegrees;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc?.Go == null || !npc.Go.activeInHierarchy)
                    continue;
                if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;

                var toNpc = npc.Go.transform.position + Vector3.up * 0.4f - origin;
                if (toNpc.magnitude > range)
                    continue;

                var angle = Vector3.Angle(direction, toNpc);
                if (angle >= bestAngle)
                    continue;

                bestAngle = angle;
                best = npc;
            }
            return best;
        }

        private float GetThrowRange(string mfxName)
        {
            var targetRange = _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.EmTrjTargetRange ?? 0f;
            // emTrjTargetRange is in meters in VISUALFX (Fireball: 20).
            return targetRange > 0f ? Mathf.Max(targetRange, _defaultThrowRange) : _defaultThrowRange;
        }

        private static Color GetSpellLightColor(string mfxName)
        {
            if (mfxName == null)
                return new Color(1f, 0.6f, 0.3f);
            if (mfxName.Contains("Ice", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.6f, 0.8f, 1f);
            if (mfxName.Contains("Thunder", System.StringComparison.OrdinalIgnoreCase) ||
                mfxName.Contains("Zap", System.StringComparison.OrdinalIgnoreCase) ||
                mfxName.Contains("Lightning", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.7f, 0.7f, 1f);
            if (mfxName.Contains("Death", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.5f, 1f, 0.5f);
            return new Color(1f, 0.6f, 0.3f);
        }

        private bool IsTelekinesisSpell() =>
            string.Equals(GetSpellMfxName(_item.Spell), _telekinesisName, System.StringComparison.OrdinalIgnoreCase);

        private void HandleTelekinesisInput()
        {
            if (!_telekinesisPrepped)
            {
                // Trigger 1: extend ForceGrab range so VRFocus highlights far items, loop SFX
                _telekinesisPrepped = true;
                Logger.Log("[VRRuneCaster] Telekinesis PREP — ForceGrab range extended, aim and trigger to pull", LogCat.VR);
                StartInvestSound(_item.Spell);
                _vrPlayerService.TelekinesisDeactivated += OnTelekinesisEnded;
                _vrPlayerService.ActivateTelekinesis(_telekinesisRange);
            }
            else
            {
                // Trigger 2: pull whatever the ForceGrabber is hovering (SFX stops via Grabbed event)
                Logger.Log("[VRRuneCaster] Telekinesis PULL", LogCat.VR);
                PlayCastSound(_item.Spell);
                _vrPlayerService.TryTelekinesisGrab();
                _castThisGrab = true;
            }
        }

        private void OnTelekinesisEnded()
        {
            Logger.Log("[VRRuneCaster] Telekinesis ended — stopping SFX", LogCat.VR);
            StopInvestSound();
            _vrPlayerService.TelekinesisDeactivated -= OnTelekinesisEnded;
        }

        private void UpdateSpellTarget()
        {
            if (!_isTargeting) return;

            var fg = _vrPlayerService.GetForceGrabber(_runeHandSide);
            if (fg == null) return;

            NpcContainer found = null;
            foreach (var bag in fg.GrabBags)
            {
                if (bag.ClosestGrabbable == null) continue;
                var npcLoader = bag.ClosestGrabbable.GetComponentInParent<NpcLoader>();
                if (npcLoader != null) { found = npcLoader.Npc.GetUserData(); break; }
            }

            var prev = _spellTarget;
            _spellTarget = found;
            if (_spellTarget != prev)
            {
                var name = _spellTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none";
                Logger.Log($"[VRRuneCaster] Target → {name}", LogCat.VR);
                if (_spellTarget != null) PlayOneShot("TMAG_INIT");
            }
        }

        private int GetSpellDamage(int spellId)
        {
            var mfxName = GetSpellMfxName(spellId);
            if (mfxName == null) return 0;
            var sym = _gameStateService.GothicVm.GetSymbolByName($"SPL_DAMAGE_{mfxName.ToUpper()}");
            return sym?.GetInt(0) ?? 0;
        }

        private string GetSpellMfxName(int spellId)
        {
            if (spellId < 0) return null;
            var sym = _gameStateService.GothicVm.GetSymbolByName("spellFXInstanceNames");
            if (sym == null) return null;
            var name = sym.GetString((ushort)spellId);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private void StartInvestSound(int spellId)
        {
            var mfxName = GetSpellMfxName(spellId);
            if (mfxName == null) return;
            var clip = _audioService.GetRandomSoundClip($"MFX_{mfxName}_Invest");
            if (clip == null) return;
            _investAudioSource.clip = clip;
            _investAudioSource.Play();
        }

        private void StopInvestSound()
        {
            if (_investAudioSource.isPlaying)
                _investAudioSource.Stop();
        }

        private void PlayCastSound(int spellId)
        {
            var mfxName = GetSpellMfxName(spellId);
            if (mfxName == null) return;
            var sfxName = $"MFX_{mfxName}_Cast";
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip == null)
                clip = _audioService.GetRandomSoundClip("MFX_Thunderbolt_Cast"); // fallback for spells with no dedicated cast SFX
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        private void PlayOneShot(string sfxName)
        {
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }

        private void ShowManaBar()
        {
            _manaBar = FindManaBar();
            if (_manaBar == null) return;
            RefreshManaFill();
            _manaBar.FadeIn();
        }

        private void HideManaBar()
        {
            _manaBar?.FadeOut();
        }

        private void RefreshManaFill()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero == null) return;
            var mana = hero.Vob.GetAttribute((int)NpcAttribute.Mana);
            var manaMax = hero.Vob.GetAttribute((int)NpcAttribute.ManaMax);
            if (manaMax > 0)
                _manaBar.SetFillAmount(mana, manaMax);
        }

        private StatusBarAdapter FindManaBar()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null) return null;
            return hero.Go
                .GetComponentsInChildren<StatusBarAdapter>(includeInactive: true)
                .FirstOrDefault(b => b.Type == StatusBarAdapter.StatusType.Mana);
        }
    }
}
#endif
