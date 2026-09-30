#if GOTHIC_HVR_INSTALLED
using System.Collections;
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
                // Trigger 2: confirm target and start mana investment
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
                DestroySpellVfx();
                _isCasting = true;
                _manaInvested = 0;
                _manaTickTimer = _manaTickInterval;
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

            // Throttle to one mana tick per interval — mirrors Gothic's C++ magic tick rate.
            _manaTickTimer += Time.deltaTime;
            if (_manaTickTimer < _manaTickInterval) return;
            _manaTickTimer -= _manaTickInterval;

            var hero = _npcService.GetHeroContainer();
            var currentMana = hero.Vob.GetAttribute((int)NpcAttribute.Mana);
            if (currentMana <= 0)
            {
                Logger.Log("[VRRuneCaster] Out of mana — spell cancelled", LogCat.VR);
                StopInvestSound();
                _isCasting = false;
                _castThisGrab = true;
                return;
            }

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
                var result = vm.Call<int, int>("Spell_ProcessMana", ++_manaInvested);

                if (result == _splSendcast || result == _splSendstop)
                {
                    Logger.Log($"[VRRuneCaster] result={result} after {_manaInvested} ticks — spell fired", LogCat.VR);
                    FinalizeCast(applyEffect: result == _splSendcast);
                }
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
        }

        /// <summary>
        /// Ends the current cast — either because Daedalus auto-fired at max charge, sent an explicit
        /// stop, or the player released early (trigger 3+) to fire at whatever level was reached.
        /// </summary>
        private void FinalizeCast(bool applyEffect)
        {
            StopInvestSound();
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

        private void SpawnSpellVfx()
        {
            DestroySpellVfx();
            var mfxName = GetSpellMfxName(_item.Spell);
            if (mfxName == null) return;
            var handGo = _vrPlayerService.GetHandModelGo(_runeHandSide);
            if (handGo == null) return;
            var pfxName = $"MFX_{mfxName.ToUpper()}_INIT";
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

                var ps = _spellVfxGo.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    var main = ps.main;
                    main.loop = true;
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
