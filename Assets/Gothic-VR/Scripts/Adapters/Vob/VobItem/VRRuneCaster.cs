#if GOTHIC_HVR_INSTALLED
using System.Collections;
using System.Linq;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Adapters.Vob;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
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

        private const string _telekinesisName = "Telekinesis";
        private const float _telekinesisRange = 5000f;

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

            ShowManaBar();
        }

        private void OnDestroy()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero != null)
                hero.ActiveSpell = 0;

            _isTargeting = false;
            _vrPlayerService.DeactivateSpellTargeting();
            _isCasting = false;

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
                Logger.Log($"[VRRuneCaster] Targeting — spell {_item.Spell} ({_item.Name}), aim rune hand at NPC and press trigger to fire", LogCat.VR);
            }
            else if (triggered && _isTargeting && !_isCasting)
            {
                // Trigger 2: confirm target and start mana investment
                _isTargeting = false;
                _vrPlayerService.DeactivateSpellTargeting();
                _isCasting = true;
                _manaInvested = 0;
                _manaTickTimer = _manaTickInterval;
                Logger.Log($"[VRRuneCaster] Cast confirmed — target={_spellTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none"}", LogCat.VR);
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
                    StopInvestSound();
                    PlayCastSound(_item.Spell);

                    var spellDmg = GetSpellDamage(_item.Spell) * _manaInvested;
                    if (_spellTarget != null && result == _splSendcast && spellDmg > 0)
                        StartCoroutine(ApplySpellHitDelayed(_spellTarget, _npcService.GetHeroContainer(), spellDmg));

                    _isCasting = false;
                    _castThisGrab = true;
                }
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
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

        private static IEnumerator ApplySpellHitDelayed(NpcContainer target, NpcContainer caster, int damage)
        {
            Logger.LogWarning($"[VRRuneCaster] FIXME: no spell VFX — SpellHit {target.Instance.GetName(NpcNameSlot.Slot0)} dmg={damage} in 1s", LogCat.VR);
            yield return new WaitForSeconds(1f);
            if (target.Go == null) yield break;
            GlobalEventDispatcher.SpellHit.Invoke(caster, target, target.Go.transform.position, damage);
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
            PlayOneShot($"MFX_{mfxName}_Cast");
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
