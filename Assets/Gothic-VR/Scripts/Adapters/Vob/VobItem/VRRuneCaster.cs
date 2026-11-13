using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Domain.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Components;
using Reflex.Attributes;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Added dynamically to a rune/scroll GO when dual-grabbed.
    /// Right trigger (or R on WASD) casts via Spell_ProcessMana() — Daedalus handles all spell logic.
    /// Targeting: raycast from rune forward axis to set vm.GlobalOther before each Spell_ProcessMana call.
    /// Workers: VRSpellFx (effects, lights, sounds), VRSpellThrow (throwable spells), VRTelekinesisSpell.
    /// Map: Docs/vr-magic.md.
    /// </summary>
    public class VRRuneCaster : MonoBehaviour
    {
        private const string _chainLightningName = "Chainlightning";
        // The lightning while it's invested.
        private VRChainLightning _chainLightning;

        // Raycast range for NPC targeting
        private const float _targetRange = 2000f;

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly VRSpellTransformService _vrSpellTransformService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly InventoryTransferService _inventoryTransferService;
        private const string _transformName = "Transform";
        private const string _lightName = "Light";

        private ItemInstance _item;
        // Workers (created in Start): effects/sounds, throwing (throwable spells only), the throw's lock-on (throwable
        // spells in a hand, not ring/simulator), telekinesis (that spell only).
        private VRSpellFx _fx;
        private VRSpellThrow _throw;
        private VRSpellLock _lock;
        private VRTelekinesisSpell _telekinesis;
        private bool _isCasting;

        // VRConstants.Magic.MinSpellCastSeconds: an instant spell (G2 summons, fire rain, transformations) waits with
        // its effects in the hand until the minimum time is over.
        private float _castStartTime;
        private bool _isSendcastPending;
        private bool _castThisGrab;
        private int _manaInvested;
        private NpcContainer _spellTarget;
        private bool _isTargeting; // combat spells: trigger 1 pressed, waiting for target + trigger 2
        private VrHandSide _runeHandSide;
        public bool IsTargetingActive => _isTargeting;

        /// <summary>
        /// The favorites ring: a spell taken out of the ring (an invisible rune attached to this hand,
        /// not grabbed) - this hand alone casts it: its trigger aims/casts, throwable spells are charged and thrown
        /// with it.
        /// </summary>
        public VrHandSide? RingHand { get; set; }

        /// <summary>
        /// The hand whose trigger casts: the ring's hand, else the hand
        /// holding the rune/scroll - always the right one before: a scroll in the left hand was cast with the right
        /// trigger, it took both hands.
        /// </summary>
        private VrHandSide CastHand => RingHand ??
                                        (_vrPlayerService.GrabbedItemLeft == gameObject &&
                                         _vrPlayerService.GrabbedItemRight != gameObject
                                            ? VrHandSide.Left
                                            : VrHandSide.Right);

        /// <summary>
        /// Aiming, charging or casting - the ring's idle effect in the hand hides meanwhile (VRFavoritesRingSpell).
        /// </summary>
        public bool IsBusy => _isTargeting || _isCasting;

        // Throwable spells (Immersion option "throwableSpells") - see vr-ranged-spells-plan.md section 3.
        public bool IsThrowable => _throw != null;
        private bool _isThrowCharged;
        private VrHandSide _throwHandSide;
        // Invest level (Spell_ProcessMana returned SPL_NEXTLEVEL, Fireball at 1/2/4 of 5 mana): like the engine the
        // VISUALFX key spellFX_<name>_KEY_INVEST_<level> replaces the effect in the hand (MFX_Fireball_INVEST_L2, ...),
        // plays its sound (MFX_Fireball_invest1..4) and its emCreateFXID burst; the charge also grows a bit.
        private int _investLevel;
        private const float _investLevelScale = 0.25f;
        private float _manaTickTimer;
        private const float _manaTickInterval = 0.5f; // seconds per mana invested; tunes cast speed

        private void Awake()
        {
            this.Inject();
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
            {
                hero.ActiveSpell = _item.Spell;
                // DeveloperConfig.EnableActiveSpellIsScroll: scrolls are ITEM_MULTI spell items, runes aren't.
                hero.IsActiveSpellScroll = ((VmGothicEnums.ItemFlags)_item.Flags & VmGothicEnums.ItemFlags.ItemMulti) != 0;
            }

            // Readied rune == FMODE_MAGIC for Daedalus (B_AssessFighter checks Npc_IsInFightMode + Npc_GetActiveSpellCat).
            _vrWeaponService.SetRuneReadied(true);

            var mfxName = GetSpellMfxName(_item.Spell);
            _fx = new VRSpellFx(gameObject, mfxName).Inject();
            var throwCastKey = IsIniOn(VRConstants.IniNames.ThrowableSpells, VRConstants.ThrowableSpellsDefaultValue)
                ? VRSpellThrow.GetCastKey(_vmCacheService, mfxName)
                : null;
            if (throwCastKey != null)
                _throw = new VRSpellThrow(throwCastKey, mfxName, _item.Spell, _fx).Inject();
            // The rune hand's trigger locks the target - ring spells have no rune hand, the simulator aims by looking.
            if (_throw != null && !RingHand.HasValue && !_vrInput.IsSimulator)
                _lock = new VRSpellLock(_throw.Range, _fx).Inject();
            if (string.Equals(mfxName, VRTelekinesisSpell.SpellName, StringComparison.OrdinalIgnoreCase))
                _telekinesis = new VRTelekinesisSpell(_fx).Inject();

            // The throwing hand (grip + trigger) can't grab the rune.
            if (IsThrowable && !RingHand.HasValue && TryGetComponent<VrInteractable>(out var interactable) && !interactable.IsOneHandOnly)
            {
                interactable.IsOneHandOnly = true;
                _isOneHandOnlySet = true;
            }

            ShowManaBar();
        }

        // This caster made the rune one-hand-only (undone on destroy).
        private bool _isOneHandOnlySet;

        private void OnDestroy()
        {
            if (_isOneHandOnlySet && TryGetComponent<VrInteractable>(out var interactable))
                interactable.IsOneHandOnly = false;

            var hero = _npcService.GetHeroContainer();
            if (hero != null)
            {
                hero.ActiveSpell = 0;
                hero.IsActiveSpellScroll = false;
            }

            // Only if Start() readied it (no item -> never readied).
            if (_item != null)
                _vrWeaponService.SetRuneReadied(false);

            _lock?.Clear();
            _isTargeting = false;
            _vrPlayerService.DeactivateSpellTargeting();
            _isCasting = false;
            StopChainLightning();

            _telekinesis?.Dispose();
            _fx?.Dispose();

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
            ReleaseIfLostFromHands();
            if (_item == null || _castThisGrab) return;

            // No casting while knocked out, asleep or frozen (HeroSpellStateDomain).
            if (HeroSpellStateDomain.IsCastBlocked(_npcService.GetHeroContainer()))
                return;

            bool triggered;
            if (_vrInput.IsSimulator)
                triggered = Keyboard.current[Key.R].wasPressedThisFrame;
            else
                triggered = _vrInput.GetButton(CastHand, VrButton.Trigger)
                    .IsJustActivated;

            // Transformed: the orb hand's trigger transforms back (VRSpellTransformPuppet) - no second transformation.
            if (_vrSpellTransformService.IsTransformed && IsTransformSpell())
                return;

            // A rune of a higher circle than the hero's doesn't cast - a
            // trigger press says how many circles are missing (G_CanNotCast).
            if (!_fightService.CheckHeroRuneCircle(_item, false))
            {
                if (triggered || _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsJustActivated ||
                    _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated)
                {
                    _fightService.CheckHeroRuneCircle(_item, true);
                    _hapticsService.Vibrate(CastHand, VrHapticPreset.Error);
                }
                return;
            }

            // Telekinesis uses a 2-trigger system and skips Daedalus entirely.
            if (_telekinesis != null)
            {
                if (triggered && _telekinesis.OnTrigger())
                    _castThisGrab = true;
                return;
            }

            if (IsThrowable)
            {
                UpdateThrowable();
                return;
            }

            UpdateSpellTarget();

            // Like Gothic's cast key - pressed it aims and charges at once, held
            // it charges, let go early Spell_ProcessMana_Release decides. Was: one press to aim, a second to charge.
            UpdateHoldToCast(triggered);
            if (!_isCasting)
                return;

            if (_isSendcastPending)
            {
                if (Time.time - _castStartTime < VRConstants.Magic.MinSpellCastSeconds)
                    return;
                _isSendcastPending = false;
                FinalizeCast(applyEffect: true);
                return;
            }

            // Immersion option "twoHandSpellCharge": a charged spell grows only while both hands are together.
            if (!IsChargeAllowed())
                return;

            if (!IsManaTickDue())
                return;

            if (!TryInvestMana(out var result))
            {
                StopChainLightning();
                Logger.Log("[VRRuneCaster] Out of mana — spell cancelled", LogCat.VR);
                _fx.StopInvestSound();
                _fx.DestroyHandVfx();
                _fx.DestroyBodyFx();
                _isCasting = false;
                _castThisGrab = true;
                // Targeting stayed on: NPC grabs were ignored and the rune hand kept its long distance grab.
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
                return;
            }

            // Every level the lightning jumps on.
            if (result == DaedalusConst.SplNextLevel)
            {
                _chainLightning?.AddLink();
                _fx.NextBodyInvestLevel();
            }

            // Ready at once (instant spell): its effects stay in the hand for the minimum cast time first.
            if (result == DaedalusConst.SplSendCast && Time.time - _castStartTime < VRConstants.Magic.MinSpellCastSeconds)
            {
                _isSendcastPending = true;
                return;
            }

            if (result == DaedalusConst.SplSendCast || result == DaedalusConst.SplSendStop)
            {
                Logger.Log($"[VRRuneCaster] result={result} after {_manaInvested} ticks — spell fired", LogCat.VR);
                // The script refused on the first tick (no valid target, e.g. Shrink on a human): nothing happens, the
                // mana comes back (G1 takes it per tick before asking the script). Later, SENDSTOP ends a spell that
                // worked (G1 Heal at full HP, Pyrokinesis on a dead victim) - its mana stays spent like the engine.
                if (result == DaedalusConst.SplSendStop && _manaInvested <= 1)
                    RefundInvestedMana();
                FinalizeCast(applyEffect: result == DaedalusConst.SplSendCast);
            }
        }

        /// <summary>
        /// Mana investment starts. The effect stays in the hand while charging.
        /// </summary>
        private void StartCasting()
        {
            _isCasting = true;
            _castStartTime = Time.time;
            _isSendcastPending = false;
            _manaInvested = 0;
            _manaTickTimer = GetManaTickInterval();
            _isChargeWaitLogged = false;

            _fx.SpawnInvestFx(true);
            TryBeginChainLightning();

            // Like the engine: NPCs notice the casting (B_AssessCaster -> reacts to offensive spells only).
            _npcAiService.SendHeroCasterPerception();
            Logger.Log($"[VRRuneCaster] Cast confirmed — target={_spellTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none"}", LogCat.VR);
        }

        /// <summary>
        /// The press starts aiming (the target follows the hand while charging)
        /// and charging together; letting go before the spell is cast works like the third trigger press did - the
        /// spells Spell_ProcessMana_Release allows fire at the level reached, the rest stop.
        /// </summary>
        private void UpdateHoldToCast(bool isPressed)
        {
            // The cast ended (cast, stopped, out of mana) - aiming ends with it.
            if (_isTargeting && !_isCasting)
            {
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
            }

            if (isPressed && !_isCasting)
            {
                _isTargeting = true;
                var isRuneLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
                _runeHandSide = RingHand ?? (isRuneLeft ? VrHandSide.Left : VrHandSide.Right);
                _vrPlayerService.ActivateSpellTargeting(_runeHandSide, _targetRange);
                _fx.HandSide = _runeHandSide;
                _fx.StartInvestSound();
                _fx.SpawnHandVfx(_runeHandSide);
                StartCasting();
                return;
            }

            if (!_isCasting || _isSendcastPending || !IsCastButtonReleased())
                return;

            if (_manaInvested <= 0 || !IsEarlyReleaseCastAllowed())
            {
                CancelCast("released early - Spell_ProcessMana_Release: SPL_SENDSTOP");
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
                return;
            }

            Logger.Log($"[VRRuneCaster] Released at level {_manaInvested} - cast", LogCat.VR);
            FinalizeCast(applyEffect: true);
        }

        private bool IsIniOn(string iniName, int defaultValue)
        {
            return _configService.Gothic == null || _configService.Gothic.GetInt(iniName, defaultValue) == 1;
        }

        private bool IsCastButtonReleased()
        {
            if (_vrInput.IsSimulator)
                return Keyboard.current[Key.R].wasReleasedThisFrame;
            return _vrInput.GetButton(CastHand, VrButton.Trigger).IsJustDeactivated;
        }

        /// <summary>
        /// Immersion option "twoHandSpellCharge": like a kamehameha - a charged spell (fireball, fire rain, wind
        /// fist, ...) invests mana only while the other hand is next to the casting one; apart, the charge waits (a
        /// pulse reminds). VR only - the simulator moves one hand at a time.
        /// </summary>
        private bool IsChargeAllowed()
        {
            if (!IsIniOn(VRConstants.IniNames.TwoHandSpellCharge, VRConstants.TwoHandSpellChargeDefaultValue) ||
                _vrInput.IsSimulator ||
                !SpellConst.ChargeEffectNames.Contains(GetSpellMfxName(_item.Spell) ?? string.Empty))
                return true;

            var left = _vrPlayerService.GetHandModelGo(VrHandSide.Left);
            var right = _vrPlayerService.GetHandModelGo(VrHandSide.Right);
            if (left == null || right == null ||
                Vector3.Distance(left.transform.position, right.transform.position) <= _twoHandChargeDistance)
            {
                _isChargeWaitLogged = false;
                return true;
            }

            if (!_isChargeWaitLogged)
            {
                _isChargeWaitLogged = true;
                _hapticsService.Vibrate(_runeHandSide == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left,
                    VrHapticPreset.Info);
                Logger.Log("[VRRuneCaster] Charging waits - bring both hands together", LogCat.VR);
            }
            return false;
        }

        // Immersion option "twoHandSpellCharge": the hands' distance (m) that still counts as together.
        private const float _twoHandChargeDistance = 0.3f;
        private bool _isChargeWaitLogged;

        /// <summary>
        /// A rune flung away from the hands (teleport, physics) still
        /// counted as held - the hero stayed in magic mode and NPCs answered it. Released, the normal release path
        /// readies it off.
        /// </summary>
        private void ReleaseIfLostFromHands()
        {
            var left = _vrPlayerService.GetHandModelGo(VrHandSide.Left);
            var right = _vrPlayerService.GetHandModelGo(VrHandSide.Right);
            var nearest = Mathf.Min(
                left != null ? Vector3.Distance(left.transform.position, transform.position) : float.MaxValue,
                right != null ? Vector3.Distance(right.transform.position, transform.position) : float.MaxValue);
            if (nearest < _maxHeldDistance)
                return;

            var grabbable = GetComponent<VrInteractable>();
            if (grabbable == null || !grabbable.IsHeld)
                return;
            Logger.LogWarning($"[VRRuneCaster] Rune {nearest:F1} m away from the hands - released.", LogCat.VR);
            grabbable.ForceRelease();
        }

        private const float _maxHeldDistance = 1.5f;

        /// <summary>
        /// The engine calls Spell_ProcessMana_Release when the cast key is let
        /// go before the spell was cast or stopped - SPL_SENDCAST fires it at the level reached (Fireball, Windfist,
        /// ChargeZap), anything else stops it: Firerain, summons, transformations, ... We fired every spell at the third
        /// trigger - Firerain and summon armies could be spammed. The invested mana isn't given back (G1 took it per
        /// tick, the engine keeps it; G2 pays on the cast only).
        /// </summary>
        private bool IsEarlyReleaseCastAllowed()
        {
            var vm = _gameStateService.GothicVm;
            if (vm.GetSymbolByName("Spell_ProcessMana_Release") == null)
                return true;

            int result;
            using (new VmGlobalsScope(vm, vm.GlobalHero, _spellTarget?.Instance ?? vm.GlobalHero, vm.GlobalVictim))
                result = vm.Call<int, int>("Spell_ProcessMana_Release", _manaInvested);
            Logger.Log($"[VRRuneCaster] Spell_ProcessMana_Release({_manaInvested}) = {result}", LogCat.VR);
            return result == DaedalusConst.SplSendCast;
        }

        /// <summary>
        /// G1's chain lightning zaps the NPC in front of the eyes, then jumps on
        /// every invest level (VRChainLightning) - it was a one-time hit on everyone around.
        /// </summary>
        private void TryBeginChainLightning()
        {
            if (_chainLightning != null ||
                !string.Equals(GetSpellMfxName(_item.Spell), _chainLightningName, StringComparison.OrdinalIgnoreCase))
                return;

            _chainLightning = gameObject.AddComponent<VRChainLightning>();
            _chainLightning.Begin(_runeHandSide);
            // The first level comes after a full time_per_mana (charging) - it zapped at once.
            _manaTickTimer = 0f;
        }

        private bool StopChainLightning()
        {
            if (_chainLightning == null)
                return false;

            _chainLightning.Stop();
            _chainLightning = null;
            return true;
        }

        /// <summary>
        /// The spell stops without effect - casting again is possible right away.
        /// </summary>
        private void CancelCast(string reason)
        {
            StopChainLightning();
            Logger.Log($"[VRRuneCaster] Spell {_item.Spell} cancelled ({reason}) after {_manaInvested} mana", LogCat.VR);
            _fx.StopInvestSound();
            _fx.DestroyHandVfx();
            _fx.DestroyBodyFx();
            _isCasting = false;
            _isSendcastPending = false;
            _manaInvested = 0;
        }

        /// <summary>
        /// Throttle to one mana tick per interval — mirrors Gothic's C++ magic tick rate.
        /// </summary>
        private bool IsManaTickDue()
        {
            var interval = GetManaTickInterval();
            _manaTickTimer += Time.deltaTime;
            if (_manaTickTimer < interval)
                return false;
            _manaTickTimer -= interval;
            return true;
        }

        /// <summary>
        /// The spell's own C_Spell.time_per_mana (ms) like the engine - G2's
        /// ChargeFireball invests one mana every 30 ms (40 per level); at our fixed 0.5 s a level took 20 s and the
        /// spell always went off at level 1.
        /// </summary>
        private float GetManaTickInterval()
        {
            if (_item == null)
                return _manaTickInterval;
            var timePerMana = _vmCacheService.TryGetSpellData(_item.Spell)?.TimePerMana ?? 0f;
            return timePerMana > 0f ? Mathf.Max(0.01f, timePerMana / 1000f) : _manaTickInterval;
        }

        /// <summary>
        /// Spends one mana and asks Daedalus (Spell_ProcessMana) - false if the hero has no mana left.
        /// </summary>
        private bool TryInvestMana(out int result)
        {
            result = 0;
            var hero = _npcService.GetHeroContainer();
            if (hero == null)
            {
                Logger.LogWarning("[VRRuneCaster] No hero while casting - spell stopped", LogCat.VR);
                return false;
            }
            var currentMana = hero.Vob.GetAttribute((int)NpcAttribute.Mana);
            if (currentMana <= 0)
                return false;

            // G1: the engine takes one mana per invest tick. G2: the spell's script pays on the cast (Spell_Cast_*) -
            // taking it here too left too little for Spell_Logic's check (SPL_SENDSTOP, nothing fired, mana gone).
            if (!IsScriptPaidSpell())
            {
                // Vob and instance both (NpcService.SetAttribute) - a vob-only write was reverted by the next
                // SyncHeroInstanceToVob() (the "mana snaps back" bug). No "-1 MANA" per tick.
                _npcService.SetAttribute(hero, NpcAttribute.Mana, currentMana - 1, cause: AttributeChangeCause.Charging);
            }

            var vm = _gameStateService.GothicVm;
            using (new VmGlobalsScope(vm, vm.GlobalHero, _spellTarget?.Instance ?? vm.GlobalHero, vm.GlobalVictim))
            {
                ++_manaInvested;
                // Summons can't be shrunk (the script only checks for a monster) - refused like an invalid target.
                result = IsShrinkOnSummon()
                    ? DaedalusConst.SplSendStop
                    : vm.Call<int, int>("Spell_ProcessMana", _manaInvested);
            }
            SyncManaFromInstance(hero);
            return true;
        }

        private bool IsShrinkOnSummon()
        {
            return _spellTarget?.SummonedBy != null &&
                   string.Equals(GetSpellMfxName(_item.Spell), "Shrink", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The scripts take mana on the instance (G2 Spell_Logic_Trf*, ChargeFireball per level, Spell_Cast_*): the VOB
        /// (mana bar, the next tick's check) follows. Before, the next tick wrote VOB - 1 and undid the script's cost.
        /// </summary>
        private void SyncManaFromInstance(NpcContainer hero)
        {
            var mana = hero.Instance.GetAttribute(NpcAttribute.Mana);
            if (hero.Vob.GetAttribute((int)NpcAttribute.Mana) == mana)
                return;
            _npcService.SetAttribute(hero, NpcAttribute.Mana, mana, cause: AttributeChangeCause.Charging);
        }

        /// <summary>
        /// G1 took one mana per invest tick - a refused spell gives it back.
        /// </summary>
        private void RefundInvestedMana()
        {
            if (IsScriptPaidSpell() || _manaInvested <= 0)
                return;
            var hero = _npcService.GetHeroContainer();
            var maxMana = hero.Vob.GetAttribute((int)NpcAttribute.ManaMax);
            var mana = Mathf.Min(maxMana, hero.Vob.GetAttribute((int)NpcAttribute.Mana) + _manaInvested);
            _npcService.SetAttribute(hero, NpcAttribute.Mana, mana, cause: AttributeChangeCause.Charging);
            Logger.Log($"[VRRuneCaster] Spell refused - {_manaInvested} mana back", LogCat.VR);
            _manaInvested = 0;
        }

        /// <summary>
        /// G2 (and mods on it): Spell_Cast_[name] exists - the script pays the mana on the cast. G1 has none.
        /// </summary>
        private bool IsScriptPaidSpell()
        {
            var mfxName = GetSpellMfxName(_item.Spell);
            return mfxName != null && _gameStateService.GothicVm.GetSymbolByName($"Spell_Cast_{mfxName}") != null;
        }

        /// <summary>
        /// Like the engine on a cast (OpenGothic GameScript::invokeSpell): Spell_Cast_[name] with self = hero,
        /// other = target - G2 pays the mana there (scroll or rune cost). Leveled spells get their invest level.
        /// </summary>
        private void InvokeSpellCast()
        {
            var mfxName = GetSpellMfxName(_item.Spell);
            var vm = _gameStateService.GothicVm;
            var castSymbol = mfxName != null ? vm.GetSymbolByName($"Spell_Cast_{mfxName}") : null;
            var hero = _npcService.GetHeroContainer();
            if (castSymbol == null)
            {
                if (hero != null)
                    SyncManaFromInstance(hero);
                return;
            }

            using (new VmGlobalsScope(vm, vm.GlobalHero, _spellTarget?.Instance ?? vm.GlobalHero, vm.GlobalVictim))
            {
                try
                {
                    if (castSymbol.Size == 1)
                        vm.Call(castSymbol.Index, Mathf.Max(1, _investLevel));
                    else
                        vm.Call(castSymbol.Index);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[VRRuneCaster] Spell_Cast_{mfxName} failed: {e.Message}", LogCat.VR);
                }
            }

            // The script changed the instance's mana - the vob (mana bar, saves) follows.
            SyncManaFromInstance(hero);
        }

        /// <summary>
        /// Ends the current cast — either because Daedalus auto-fired at max charge, sent an explicit
        /// stop, or the player released early (trigger 3+) to fire at whatever level was reached.
        /// </summary>
        private void FinalizeCast(bool applyEffect)
        {
            // The chain lightning did its work while invested (ZS_Zapped) - nothing more on the cast.
            if (StopChainLightning())
                applyEffect = false;
            _fx.StopInvestSound();
            _fx.DestroyHandVfx();
            _fx.DestroyBodyFx();
            _fx.PlayCastSound();
            var isScrollKept = false;
            if (applyEffect)
            {
                _fx.SpawnCastFx(_spellTarget);
                InvokeSpellCast();
            }

            if (applyEffect)
            {
                var mfxName = GetSpellMfxName(_item.Spell);

                // Light has no Daedalus-side hook at all (Spell_Logic_Light only gates the cast) —
                // in the original engine it's a hardcoded C++ toggle, so it's the one spell effect
                // we implement directly rather than routing through Spell_ProcessMana/ASSESSMAGIC.
                if (mfxName == _lightName)
                {
                    _fx.ToggleLightSpell();
                }
                // Spell_Logic_Trf_X: Npc_SetActiveSpellInfo(self, <monster>) right before SPL_SENDCAST.
                else if (mfxName == _transformName)
                {
                    var caster = _npcService.GetHeroContainer();
                    if (caster != null && caster.ActiveSpellInfo > 1)
                    {
                        var monster = caster.ActiveSpellInfo;
                        var isTransformed = _vrSpellTransformService.Transform(monster, _runeHandSide);
                        caster.ActiveSpellInfo = 0;
                        // The script's monster has no visual - the
                        // one the spell is named after (SPL_TRF_LURKER -> LURKER).
                        if (!isTransformed)
                        {
                            var named = _vrSpellTransformService.GetSpellNamedMonster(_item.Spell);
                            if (named >= 0 && named != monster)
                                isTransformed = _vrSpellTransformService.Transform(named, _runeHandSide);
                        }
                        // No monster to become (no visual) - the
                        // scroll stays and the mana comes back.
                        if (!isTransformed)
                        {
                            RefundInvestedMana();
                            isScrollKept = true;
                        }
                    }
                }
                else
                {
                    // Per level like the engine (OpenGothic damage_per_level * level) - per mana tick a held
                    // charge spell did thousands.
                    var spellDmg = GetSpellDamage(_item.Spell) * Mathf.Clamp(_fx.BodyInvestLevel, 1, VRSpellFx.MaxInvestLevel);
                    var hero = _npcService.GetHeroContainer();
                    var isAoe = mfxName != null && SpellConst.AoeEffectNames.Contains(mfxName);
                    // Summons have no target like the engine
                    // (targetCollectAlgo = TARGET_COLLECT_NONE) - aimed at somebody, the summon hit him for
                    // DamagePerLevel x mana and his magic perception turned him against the hero.
                    var isUntargetedSummon = SpellConst.SummonEffectNames.Contains(mfxName ?? string.Empty);

                    if (_spellTarget != null && _spellTarget.Go != null && !isUntargetedSummon)
                    {
                        // SpellHit both applies direct damage (if any) and fires PERC_ASSESSMAGIC on the
                        // target, so Daedalus content (ZS_MagicFreeze, ZS_MagicSleep, ZS_Zapped, Fear/
                        // Charm/Berzerk...) drives the actual reaction — nothing per-spell to hardcode here.
                        // For AOE spells FightService.OnSpellHit ignores this single target anyway and
                        // fans out to everyone in range of the caster.
                        GlobalEventDispatcher.SpellHit.Invoke(hero, _spellTarget, _spellTarget.Go.transform.position, spellDmg);
                        // Not thrown - the hit effect (emFXCollDyn_S: ice block, fear, ...) appears on the target.
                        _fx.SpawnCollideFx(_vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.EmFxCollDynS,
                            _manaInvested, _spellTarget.Go.transform.position, Vector3.zero);
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

            if (applyEffect && !isScrollKept)
                TryUseUpScroll();

            _isCasting = false;
            // Allow recasting without re-grabbing: _castThisGrab stays false
        }

        /// <summary>
        /// The "scrollsSingleUse" option (Immersion, on by default): like Gothic a
        /// scroll (a spell item with ITEM_MULTI - runes have none) is used up by its cast. A held scroll stack loses one
        /// (the last one is gone from the hand), a scroll from the favorites ring one from the backpack (the last one
        /// unequips it, VRFavoritesRingSpell puts the hand's spell away). Scrolls were cast forever.
        /// </summary>
        private void TryUseUpScroll()
        {
            if (_item == null ||
                ((VmGothicEnums.ItemFlags)_item.Flags & VmGothicEnums.ItemFlags.ItemMulti) == 0 ||
                _configService.Gothic == null ||
                _configService.Gothic.GetInt(VRConstants.IniNames.ScrollsSingleUse, 1) != 1)
                return;

            var container = GetComponentInParent<VobLoader>()?.Container;
            var vobItem = container?.VobAs<IItem>();
            if (vobItem == null)
            {
                Logger.LogWarning($"[VRRuneCaster] Scroll {_item.Description} has no item vob - not used up",
                    LogCat.VR);
                return;
            }
            var instanceName = vobItem.GetSymbolName();

            // The ring's spell is an invisible item outside the inventory - the scroll comes out of the backpack.
            if (RingHand.HasValue)
            {
                _inventoryTransferService.GetHeroOwner().Remove(instanceName, 1);
                Logger.Log($"[VRRuneCaster] Scroll {_item.Description} used up (favorites ring)", LogCat.VR);
                return;
            }

            // A held stack counts in the inventory (VRPlayerService.SetGrab): one less in both.
            _playerService.RemoveItem(instanceName, 1);
            vobItem.Amount = Mathf.Max(0, vobItem.Amount - 1);
            Logger.Log($"[VRRuneCaster] Scroll {_item.Description} used up ({vobItem.Amount} left in the hand)",
                LogCat.VR);
            if (vobItem.Amount > 0)
                return;

            var grabbable = GetComponent<VrInteractable>();
            if (grabbable != null && grabbable.IsHeld)
                grabbable.ForceRelease();
            _vobService.RemoveWorldItem(container);
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
                _lock?.Clear();
                if (_isCasting)
                    CancelThrow("no free throwing hand");
                else
                    LogBlockedThrow();
                return;
            }

            _throw.TrackHand(handSide);

            // The lock-on owns the target: Daedalus' other while charging (Spell_ProcessMana) and the throw's target.
            if (_lock != null)
            {
                _lock.Tick(handSide == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left);
                _spellTarget = _lock.Target;
            }

            var isWasd = _vrInput.IsSimulator;
            var button = isWasd ? default : _vrInput.GetButton(handSide, VrButton.Trigger);
            var isJustPressed = isWasd ? Keyboard.current[Key.R].wasPressedThisFrame : button.IsJustActivated;
            var isHeld = isWasd ? Keyboard.current[Key.R].isPressed : button.IsActive;

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

            if (result == DaedalusConst.SplSendStop)
            {
                CancelThrow("SPL_SENDSTOP");
            }
            else if (result == DaedalusConst.SplNextLevel)
            {
                NextInvestLevel();
            }
            else if (result == DaedalusConst.SplSendCast)
            {
                _isThrowCharged = true;
                Logger.Log($"[VRRuneCaster] Throw fully charged at level {_manaInvested}", LogCat.VR);
            }
        }

        /// <summary>
        /// A trigger pressed while no hand is free to throw - why the spell doesn't come (logged once per press).
        /// </summary>
        private void LogBlockedThrow()
        {
            if (_vrInput.IsSimulator ||
                (!_vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsJustActivated &&
                 !_vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated))
                return;

            var left = _vrPlayerService.GrabbedItemLeft;
            var right = _vrPlayerService.GrabbedItemRight;
            Logger.LogWarning($"[VRRuneCaster] Throw of {_item.Name} blocked - no free hand " +
                              $"(left: {(left == null ? "-" : left == gameObject ? "rune" : left.name)}, " +
                              $"right: {(right == null ? "-" : right == gameObject ? "rune" : right.name)})", LogCat.VR);
        }

        /// <summary>
        /// The hand that doesn't hold the rune - and holds nothing else.
        /// </summary>
        private bool TryGetThrowHand(out VrHandSide handSide)
        {
            // A ring spell: the hand holding it charges and throws.
            if (RingHand.HasValue)
            {
                handSide = RingHand.Value;
                return true;
            }

            var isRuneLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isRuneRight = _vrPlayerService.GrabbedItemRight == gameObject;
            handSide = isRuneLeft ? VrHandSide.Right : VrHandSide.Left;
            if (isRuneLeft == isRuneRight)
                return false; // both hands on the rune (or none)

            var otherItem = isRuneLeft ? _vrPlayerService.GrabbedItemRight : _vrPlayerService.GrabbedItemLeft;
            return otherItem == null;
        }

        private void StartThrowCharge(VrHandSide handSide)
        {
            _throwHandSide = handSide;
            _fx.HandSide = handSide;
            _runeHandSide = RingHand ?? (handSide == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left);
            _isCasting = true;
            _isThrowCharged = false;
            _manaInvested = 0;
            _investLevel = 0;
            _spellTarget = _lock?.Target;
            _manaTickTimer = GetManaTickInterval(); // first mana right away

            // Spamming the trigger: whatever the last charge left in the hand goes first - effects stacked up.
            _fx.DestroyHandVfx();
            _fx.DestroyBodyFx();

            _fx.StartInvestSound();
            _fx.SpawnHandVfx(handSide);
            _fx.SpawnInvestFx(false);

            _npcAiService.SendHeroCasterPerception();
            Logger.Log($"[VRRuneCaster] Charging throwable spell {_item.Spell} ({_item.Name}) in {handSide} hand", LogCat.VR);
        }

        /// <summary>
        /// SPL_NEXTLEVEL: the charge in the hand grows, VISUALFX spellFX_<name>_KEY_INVEST_<level> sound (Fireball: 4).
        /// </summary>
        private void NextInvestLevel()
        {
            if (_investLevel >= VRSpellFx.MaxInvestLevel)
                return;

            _investLevel++;
            var mfxName = GetSpellMfxName(_item.Spell);
            var levelKey = _vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INVEST_{_investLevel}");

            // The level's own effect in the hand (falls back to the current one if the key has none).
            if (!string.IsNullOrEmpty(levelKey?.VisNameS))
                _fx.SpawnHandVfx(_throwHandSide, levelKey.VisNameS);
            if (_fx.HandVfx != null)
                _fx.HandVfx.transform.localScale = Vector3.one * (1f + _investLevelScale * _investLevel);

            // The level's charge sound loops while charging (sfxIsAmbient), the burst plays once.
            _fx.PlayInvestLevelSound(levelKey?.SfxId);
            _fx.SpawnInvestBurst(levelKey?.EmCreateFxId);

            _hapticsService.Vibrate(_throwHandSide, VrHapticPreset.Info);
            Logger.Log($"[VRRuneCaster] {mfxName} invest level {_investLevel} (mana {_manaInvested})", LogCat.VR);
        }

        private void CancelThrow(string reason)
        {
            Logger.Log($"[VRRuneCaster] Throw cancelled ({reason}) at level {_manaInvested}", LogCat.VR);
            _fx.StopInvestSound();
            _fx.DestroyHandVfx();
            _fx.DestroyBodyFx();
            _isCasting = false;
            _isThrowCharged = false;
        }

        /// <summary>
        /// Released the trigger: thrown along the hand's velocity with the spell's own speed (emtrjeasevel, cm/s).
        /// Too slow = fizzles (mana is spent, like an interrupted cast).
        /// </summary>
        private void ReleaseThrow(VrHandSide handSide, bool isWasd)
        {
            if (_manaInvested == 0)
            {
                CancelThrow("released before the first mana");
                return;
            }

            // Not fully charged: only spells the script lets go early are thrown.
            if (!_isThrowCharged && !IsEarlyReleaseCastAllowed())
            {
                CancelThrow("released early - Spell_ProcessMana_Release: SPL_SENDSTOP");
                return;
            }

            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (handGo == null)
            {
                CancelThrow("no hand");
                return;
            }

            var lockedTarget = _lock?.Target;
            var direction = _throw.Aim(handGo, isWasd, lockedTarget);

            _fx.StopInvestSound();
            _fx.PlayCastSound();

            var origin = handGo.transform.position + direction * 0.15f;
            // Per level like the engine (castLevel + 1): per mana tick a held fireball did thousands.
            var level = Mathf.Clamp(_investLevel + 1, 1, VRSpellFx.MaxInvestLevel);
            _throw.Throw(origin, direction, level, _investLevel, GetSpellDamage(_item.Spell) * level, lockedTarget);
            InvokeSpellCast();
            // A thrown scroll is used up like a cast one - Firebolt/Fireball scrolls were thrown forever.
            TryUseUpScroll();

            // The charging effects (hand/body, looping) end with the throw - only a cancel removed them before, so
            // every throw left its fire in the hand and the next charge added more.
            _fx.DestroyBodyFx();

            _isCasting = false;
            _isThrowCharged = false;
        }

        private bool IsTransformSpell() =>
            string.Equals(GetSpellMfxName(_item.Spell), _transformName, StringComparison.OrdinalIgnoreCase);

        private void UpdateSpellTarget()
        {
            if (!_isTargeting) return;

            var found = _vrPlayerService.GetSpellTarget(_runeHandSide);

            var prev = _spellTarget;
            _spellTarget = found;
            if (_spellTarget != prev)
            {
                var name = _spellTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none";
                Logger.Log($"[VRRuneCaster] Target → {name}", LogCat.VR);
                if (_spellTarget != null) _fx.PlayOneShot("TMAG_INIT");
            }
        }

        private int GetSpellDamage(int spellId)
        {
            return _vmCacheService.GetSpellDamagePerLevel(spellId, GetSpellMfxName(spellId));
        }

        private string GetSpellMfxName(int spellId)
        {
            return _vmCacheService.TryGetSpellMfxName(spellId, out var mfxName) ? mfxName : null;
        }

        // The bar follows the mana itself (NpcAttributeChanged from NpcService.SetAttribute).
        private static void ShowManaBar() => GlobalEventDispatcher.HeroManaBarRequested.Invoke(true);

        private static void HideManaBar() => GlobalEventDispatcher.HeroManaBarRequested.Invoke(false);
    }
}
