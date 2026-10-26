using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Services.Caches;
using Gothic.Core.Models.Audio;
using ZenKit.Daedalus;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Services;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.Context;
using Gothic.VR.Services.Context;
using Gothic.VR.Adapters.Player;
using ZenKit.Vobs;
using Gothic.Core.Adapters.Vob.Item;
using Gothic.Core.Const;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.VR.Domain.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models.Vob;
using Gothic.Core.Extensions;
#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Backends.Hvr;
#endif
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Logic goes like this:
    /// * At first, our logic is executed in _firstAttackDomain. Either one handed or two handed.
    /// * If we grab another weapon which is not handled by the first Domain already, then let's handle it by the _second one.
    /// </summary>
    public class VRWeaponService
    {
        /// Disable sounds when Backpack is currently being refilled.
        public bool DrawSoundsActive = true;

        [Inject] private AudioService _audioService;
        [Inject] private PlayerService _playerService;
        [Inject] private NpcAiService _npcAiService;
        [Inject] private NpcHelperService _npcHelperService;
        [Inject] private ConfigService _configService;
        [Inject] private GameStateService _gameStateService;
        [Inject] private NpcInventoryService _npcInventoryService;
        [Inject] private VobService _vobService;
        [Inject] private VmCacheService _vmCacheService;
        [Inject] private ResourceCacheService _resourceCacheService;
        [Inject] private IVrSfxPlayer _vrSfxPlayer;
        [Inject] private IVrInput _vrInput;
        [Inject] private IVrHaptics _vrHaptics;
        [Inject] private ContextInteractionService _contextInteractionService;

        private readonly VrWeaponAttackDomain _firstAttackDomain = new VrWeaponAttackDomain().Inject();
        private readonly VrWeaponAttackDomain _secondAttackDomain = new VrWeaponAttackDomain().Inject();

        private bool _isRuneReadied;
        private VmGothicEnums.WeaponState? _readiedRangedState;

        // DeveloperConfig.EnableVrFists: a fist is an empty hand with grip + trigger held; it punches like a weapon swing.
        private const float _fistPunchVelocity = 2f;
        private const float _fistPunchCooldownSeconds = 0.5f;
        private readonly List<(IVrGrabber Hand, VRFistHitbox Hitbox)> _fists = new();
        private readonly Dictionary<VrHandSide, float> _lastPunchTime = new();
        private readonly Dictionary<VrHandSide, bool> _isFistSwinging = new();
        private bool _isFistReadied;
        private SfxModel _fistSwingSound;
        private bool _isFistSwingSoundResolved;
        private const string _fistAttackAnimName = "S_FISTATTACK";
        private const float _fistSwingHapticAmplitude = 0.25f;
        private const float _fistSwingHapticSeconds = 0.05f;
        private const float _fistHitHapticAmplitude = 0.8f;
        private const float _fistHitHapticSeconds = 0.12f;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.FightWindowAttack.AddListener(OnAttackWindowStart);
            GlobalEventDispatcher.FightWindowInitial.AddListener(OnAttackWindowEnd);
            GlobalEventDispatcher.HeroKnockedOut.AddListener(OnHeroKnockedOut);
            GlobalEventDispatcher.ScriptRemovedInvItems.AddListener(OnScriptRemovedInvItems);
        }

        /// <summary>
        /// V1 (DeveloperConfig.EnableScriptRemovesHeldItems): items in VR hands and body holsters count as hero
        /// inventory. When a script takes them (B_GiveInvItems in a dialog, Npc_RemoveInvItems), the physical copies
        /// must vanish too - otherwise the hero keeps e.g. Lobart's clothes in a holster after handing them over.
        /// Only surplus copies (more physical than the inventory still has) are removed. Backpack sockets are skipped:
        /// the backpack refills from the inventory anyway.
        /// </summary>
        private void OnScriptRemovedInvItems(NpcContainer npc, int itemIndex, int amount)
        {
            if (!_configService.Dev.EnableScriptRemovesHeldItems)
                return;
            var hero = _playerService.HeroContainer;
            if (npc == null || hero == null || npc != hero)
                return;

            var itemName = _gameStateService.GothicVm.GetSymbolByIndex(itemIndex)?.Name;
            if (itemName == null)
                return;

            var remaining = _npcInventoryService.ExtNpcHasItems(hero.Instance, itemIndex);

            var socketed = new List<(IVrSocket socket, VobContainer container)>();
            foreach (var socket in GetPlayerHolsterSockets())
            {
                var container = socket.Held.GetComponentInParent<VobLoader>(true)?.Container;
                if (IsSameItem(container, itemName))
                    socketed.Add((socket, container));
            }

            var held = new List<(IVrGrabber hand, VobContainer container)>();
            foreach (var hand in GetHands())
            {
                var container = hand.Held?.GetComponentInParent<VobLoader>()?.Container;
                if (IsSameItem(container, itemName) && !held.Exists(h => h.container == container))
                    held.Add((hand, container));
            }

            var surplus = socketed.Sum(s => GetAmount(s.container)) + held.Sum(h => GetAmount(h.container)) - remaining;
            if (surplus <= 0)
                return;

            Logger.Log($"[VRWeaponService] Script took {amount}x '{itemName}' - removing {surplus} physical cop(y/ies) " +
                       $"from holsters/hands (inventory left: {remaining}).", LogCat.VR);

            // Holsters first - a script taking an item rarely means the one the player is actively holding.
            foreach (var (socket, container) in socketed)
            {
                if (surplus <= 0)
                    return;
                surplus -= GetAmount(container);
                socket.ForceRelease();
                _vobService.RemoveWorldItem(container);
            }

            foreach (var (_, container) in held)
            {
                if (surplus <= 0)
                    return;
                surplus -= GetAmount(container);

                // Releasing from the hand subtracts the item from the inventory (VRPlayerService.UnsetGrab), but the
                // script already did that. Add it back first so the release doesn't remove it twice.
                _playerService.AddItem(itemName, GetAmount(container));
                foreach (var hand in GetHands())
                {
                    if (hand.Held?.GetComponentInParent<VobLoader>()?.Container == container)
                        hand.ForceRelease();
                }
                _vobService.RemoveWorldItem(container);
            }
        }

        private IVrRig GetRig()
        {
            return _contextInteractionService.GetImpl<VRContextInteractionService>()?.Rig;
        }

        /// <summary>
        /// The direct grabbers of both hands (a distance grab ends in the hand too).
        /// </summary>
        private IEnumerable<IVrGrabber> GetHands()
        {
            var rig = GetRig();
            return rig == null
                ? Enumerable.Empty<IVrGrabber>()
                : rig.HandGrabbers.Where(i => i.Kind == VrGrabberKind.Hand);
        }

        /// <summary>
        /// Occupied sockets on the player (hips, shoulders) - not the backpack's, which refills from the inventory.
        /// </summary>
        private IEnumerable<IVrSocket> GetPlayerHolsterSockets()
        {
            var rig = GetRig();
            if (rig == null)
                return Enumerable.Empty<IVrSocket>();

            var playerRoot = rig.Root.root;
            return Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<IVrSocket>()
                .Where(i => i.Held != null && i.Transform.root == playerRoot &&
                            i.Transform.GetComponentInParent<VRBackpack>(true) == null)
                .ToList();
        }

        private static bool IsSameItem(VobContainer container, string itemName)
        {
            if (container?.Vob is not IItem item)
                return false;
            var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
            return instanceName.EqualsIgnoreCase(itemName);
        }

        private static int GetAmount(VobContainer container)
        {
            return Mathf.Max(1, ((IItem)container.Vob).Amount);
        }

        /// <summary>
        /// MVP (DeveloperConfig.EnableHeroDropsWeaponsOnKnockout): vanilla drops the hero's readied weapon when knocked out.
        /// In VR we let the hands drop every melee weapon they hold. Release events handle the rest (attack domain, flags).
        /// </summary>
        private void OnHeroKnockedOut(NpcContainer hero)
        {
            if (!_configService.Dev.EnableHeroDropsWeaponsOnKnockout)
                return;

            foreach (var hand in GetHands())
            {
                var grabbed = hand.Held;
                if (grabbed == null)
                    continue;

                var item = grabbed.GetComponentInParent<VobLoader>()?.Container?.GetItemInstance();
                if (item == null || item.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatNf)
                    continue;

                Logger.Log($"[VRWeaponService] Hero knocked out - dropping '{item.Name}' from {hand.HandSide} hand.", LogCat.VR);
                hand.ForceRelease();
            }
        }

        public void FixedUpdate()
        {
            _firstAttackDomain.FixedUpdate();
            _secondAttackDomain.FixedUpdate();
            UpdateFists();
        }

        /// <summary>
        /// DeveloperConfig.EnableVrFists: an empty hand with grip + trigger held is a fist - the hero is in fist mode
        /// (NPCs react like to drawn fists) and its knuckle hitbox punches.
        /// </summary>
        private void UpdateFists()
        {
            if (!_configService.Dev.EnableVrFists)
                return;

            if (_fists.Count == 0 || _fists.Any(f => f.Hand == null || f.Hitbox == null))
                CreateFistHitboxes();

            var isAnyFist = false;
            foreach (var (hand, hitbox) in _fists)
            {
                var side = hand.HandSide;
                var isFist = hand.Held == null &&
                             _vrInput.GetButton(side, VrButton.Grip).IsActive &&
                             _vrInput.GetButton(side, VrButton.Trigger).IsActive;
                if (hitbox.gameObject.activeSelf != isFist)
                    hitbox.gameObject.SetActive(isFist);
                isAnyFist |= isFist;

                if (isFist)
                {
#if GOTHIC_HVR_INSTALLED
                    KeepFistPose(side);
#endif
                    TickFistSwing(hand, hitbox);
                    hitbox.SetDebugVisible(_configService.Dev.ShowNpcColliders);
                }
                else
                {
                    _isFistSwinging[side] = false;
                }
            }

            if (isAnyFist == _isFistReadied)
                return;
            _isFistReadied = isAnyFist;
            Logger.Log($"[VRFist] Fists {(isAnyFist ? "up" : "down")}", LogCat.Fight);
            UpdateHeroWeaponState();
        }

        /// <summary>
        /// The held grip + trigger win over the hover pose: HVR opens the hand over a highlighted NPC/item, which is
        /// always the case when punching. The highlight itself stays. NPCs and items further away are hovered by the
        /// force grabber, with a hover pose of its own - only the hand grabber's was reset and the fist still opened.
        /// </summary>
#if GOTHIC_HVR_INSTALLED
        private void KeepFistPose(VrHandSide side)
        {
            // HVR's hand poser only - the block hands of the other backends close with the grip by themselves.
            var rig = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<HvrRig>();
            var hand = rig == null ? null : side == VrHandSide.Left ? rig.LeftHand : rig.RightHand;
            if (hand == null)
                return;

            var animator = hand.HandAnimator;
            if (animator == null || animator.CurrentPoser == null)
                return;

            var current = animator.CurrentPoser;
            var force = hand.ForceGrabber;
            var isHoverPose = current == hand.HoverPoser ||
                              (force != null && (current == force.HoverPoser || current == force.GrabPoser));
            if (!isHoverPose)
                return;

            animator.ResetToDefault();
            hand.ResetCloneAnimator();
        }
#endif

        /// <summary>
        /// A swing (hand faster than a punch needs) is heard and felt like a weapon swing: the fist attack's sound from
        /// Humans.mds (S_FISTATTACK) and a short vibration - once per swing.
        /// </summary>
        private void TickFistSwing(IVrGrabber hand, VRFistHitbox hitbox)
        {
            var side = hand.HandSide;
            var speed = _vrInput.GetControllerVelocity(side).magnitude;
            var isSwinging = speed >= _fistPunchVelocity;
            _isFistSwinging.TryGetValue(side, out var wasSwinging);
            _isFistSwinging[side] = isSwinging;
            if (!isSwinging || wasSwinging)
                return;

            _vrHaptics.Vibrate(side, _fistSwingHapticAmplitude, _fistSwingHapticSeconds);
            var sound = GetFistSwingSound();
            if (sound != null)
                _vrSfxPlayer.Play(_audioService.CreateAudioClip(sound.GetRandomSound()), hitbox.transform.position);
        }

        private SfxModel GetFistSwingSound()
        {
            if (_isFistSwingSoundResolved)
                return _fistSwingSound;
            _isFistSwingSoundResolved = true;

            var mds = _resourceCacheService.TryGetModelScript("HUMANS", false);
            var anim = mds?.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase(_fistAttackAnimName));
            var soundName = anim?.SoundEffects.FirstOrDefault()?.Name;
            _fistSwingSound = soundName != null ? _vmCacheService.TryGetSfxData(soundName) : null;
            if (_fistSwingSound == null)
                Logger.LogWarning($"[VRFist] Humans.mds: {_fistAttackAnimName} has no sound - fist swings are silent", LogCat.Fight);
            return _fistSwingSound;
        }

        private void CreateFistHitboxes()
        {
            foreach (var (_, hitbox) in _fists)
            {
                if (hitbox != null)
                    Object.Destroy(hitbox.gameObject);
            }
            _fists.Clear();

            var rig = GetRig();
            foreach (var hand in GetHands())
            {
                var handVisual = rig.GetHandVisual(hand.HandSide);
                if (handVisual == null)
                    continue;
                var hitbox = VRFistHitbox.Create(handVisual, hand.HandSide);
                hitbox.OnNpcEntered = OnFistEnteredNpc;
                _fists.Add((hand, hitbox));
            }
        }

        /// <summary>
        /// A punch needs a swing (hand speed) like a weapon attack, and one punch per hand per moment. The damage is the
        /// engine's fist damage (FightService: strength, blunt).
        /// </summary>
        private void OnFistEnteredNpc(VRFistHitbox hitbox, NpcLoader npcLoader, Vector3 position)
        {
            var hero = _playerService.HeroContainer;
            var target = npcLoader.Container;
            if (hero == null || target == null || target == hero)
                return;

            var speed = _vrInput.GetControllerVelocity(hitbox.HandSide).magnitude;
            if (speed < _fistPunchVelocity)
            {
                Logger.Log($"[VRFist] {hitbox.HandSide} fist touched {target.Instance.GetName(NpcNameSlot.Slot0)} - too slow ({speed:F1} m/s)", LogCat.Fight);
                return;
            }
            if (_lastPunchTime.TryGetValue(hitbox.HandSide, out var last) && Time.time - last < _fistPunchCooldownSeconds)
                return;

            _lastPunchTime[hitbox.HandSide] = Time.time;
            _vrHaptics.Vibrate(hitbox.HandSide, _fistHitHapticAmplitude, _fistHitHapticSeconds);
            Logger.Log($"[VRFist] {hitbox.HandSide} punch at {speed:F1} m/s → {target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            GlobalEventDispatcher.FightHit.Invoke(hero, target, position);
        }

        public void OnGrabbed(VrHandSide handSide, VobContainer vobContainer, WeaponPhysicsConfig weaponConfig)
        {
            var heroContainer = _playerService.HeroContainer;

            if (!_firstAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer))
            {
                // If we can't handle with the first handler, then it's a second weapon grabbed with another hand.
                _secondAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer);
            }

            UpdateHeroWeaponState();
        }

        public void OnReleased(VrHandSide handSide, WeaponPhysicsConfig weaponConfig)
        {
            if (!_firstAttackDomain.TryUnHandle(weaponConfig, handSide))
            {
                // If we can't handle with the first handler, then it's a second weapon released from another hand.
                _secondAttackDomain.TryUnHandle(weaponConfig, handSide);
            }

            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Called by VRRuneCaster (rune dual-grabbed / released). Can't be derived from hero.ActiveSpell,
        /// as spell ID 0 (SPL_LIGHT) is the same as "no spell".
        /// </summary>
        public void SetRuneReadied(bool isReadied)
        {
            _isRuneReadied = isReadied;
            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Called by VRCrossbow/VRBow (held in any hand) - null when released.
        /// </summary>
        public void SetRangedReadied(VmGothicEnums.WeaponState? rangedState)
        {
            _readiedRangedState = rangedState;
            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Gothic knows only one readied weapon at a time. VR hands can hold several, so we pick:
        /// melee weapon in any hand > held crossbow/bow (VRCrossbow/VRBow) > readied rune (VRRuneCaster sets hero.ActiveSpell) > nothing.
        /// NPCs react to the result via PERC_DRAWWEAPON/PERC_ASSESSFIGHTER/PERC_ASSESSREMOVEWEAPON.
        /// </summary>
        public void UpdateHeroWeaponState()
        {
            var state = _firstAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon)
                state = _secondAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon && _readiedRangedState.HasValue)
                state = _readiedRangedState.Value;
            if (state == VmGothicEnums.WeaponState.NoWeapon && _isRuneReadied)
                state = VmGothicEnums.WeaponState.Mage;
            if (state == VmGothicEnums.WeaponState.NoWeapon && _isFistReadied)
                state = VmGothicEnums.WeaponState.Fist;

            // Which melee weapon is readied - Npc_GetReadiedWeapon (orcs tolerate the Ulu-Mulu in the hero's hand).
            var meleeWeapon = _firstAttackDomain.WeaponVobContainer ?? _secondAttackDomain.WeaponVobContainer;
            _npcHelperService.HeroReadiedMeleeWeapon = meleeWeapon?.GetItemInstance();

            _npcAiService.ExtSetHeroWeaponState(state);
        }

        public void PlayDrawSound(VobContainer weapon)
        {
            if (!DrawSoundsActive)
                return;

            switch ((VmGothicEnums.ItemMaterial)weapon.GetItemInstance()!.Material)
            {
                 case VmGothicEnums.ItemMaterial.Metal:
                     var clipMetal = _audioService.CreateAudioClip(DaedalusConst.SoundDrawMetal);
                     _vrSfxPlayer.Play(clipMetal, weapon.Go.transform.position);
                     break;
                 case VmGothicEnums.ItemMaterial.Wood:
                     var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundDrawWood);
                     _vrSfxPlayer.Play(clipWood, weapon.Go.transform.position);
                     break;
                 // All others will be ignored as they're e.g., a bow.
                 default:
                     break;
            }
        }

        public void PlayUndrawSound(VobContainer weapon)
        {
            if (!DrawSoundsActive)
                return;

            switch ((VmGothicEnums.ItemMaterial)weapon.GetItemInstance()!.Material)
            {
                case VmGothicEnums.ItemMaterial.Metal:
                    var clipMetal = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawMetal);
                    _vrSfxPlayer.Play(clipMetal, weapon.Go.transform.position);
                    break;
                case VmGothicEnums.ItemMaterial.Wood:
                    var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawWood);
                    _vrSfxPlayer.Play(clipWood, weapon.Go.transform.position);
                    break;
                // All others will be ignored as they're e.g., a bow.
                default:
                    break;
            }
        }

        public bool IsWeaponInAttackWindow(VobContainer vobContainer)
        {
            if (vobContainer == _firstAttackDomain.WeaponVobContainer)
                return _firstAttackDomain.IsInAttackState();
            else if (vobContainer == _secondAttackDomain.WeaponVobContainer)
                return _secondAttackDomain.IsInAttackState();
            else
                return false;
        }

        /// <summary>
        /// Returns the NpcContainer of the player who owns the weapon, or null if not found.
        /// </summary>
        public NpcContainer GetWeaponOwner(VobContainer vobContainer)
        {
            if (vobContainer == _firstAttackDomain.WeaponVobContainer)
                return _firstAttackDomain.GetOwner();
            else if (vobContainer == _secondAttackDomain.WeaponVobContainer)
                return _secondAttackDomain.GetOwner();
            else
                return null;
        }

        /// <summary>
        /// Returns the HandSide for a combatant's weapon, for haptics feedback.
        /// Returns None if the combatant is not the owner of any active weapon domain.
        /// </summary>
        public GlobalEventDispatcher.HandSide GetHandSideForCombatant(NpcContainer combatant)
        {
            if (_firstAttackDomain.GetOwner() == combatant)
                return _firstAttackDomain.GetHandSide();
            else if (_secondAttackDomain.GetOwner() == combatant)
                return _secondAttackDomain.GetHandSide();
            else
                return GlobalEventDispatcher.HandSide.None;
        }

        private void OnAttackWindowStart(NpcContainer combatant)
        {
            ChangeWeaponTrail(combatant, true);
        }

        private void OnAttackWindowEnd(NpcContainer combatant)
        {
            ChangeWeaponTrail(combatant, false);
        }

        private void ChangeWeaponTrail(NpcContainer combatant, bool enable)
        {
            VobContainer weapon = null;
            if (_firstAttackDomain.GetOwner() == combatant)
                weapon = _firstAttackDomain.WeaponVobContainer;
            else if (_secondAttackDomain.GetOwner() == combatant)
                weapon = _secondAttackDomain.WeaponVobContainer;

            // Go can already be destroyed (Unity null). Throwing here would abort HVR's release chain.
            if (weapon?.Go == null)
                return;

            var weaponAdapter = weapon.Go.GetComponentInChildren<WeaponAdapter>();
            if (weaponAdapter == null)
                return;

            if (enable)
                weaponAdapter.StartTrail();
            else
                weaponAdapter.EndTrail();
        }

        private void OnHit(NpcContainer _, NpcContainer __, Vector3 ___)
        {
            // Find which domain's weapon caused this hit and advance its state.
            // We check based on the attacker being the owner of one of our domains.
            if (_firstAttackDomain.GetOwner() != null && _firstAttackDomain.IsInAttackState())
                _firstAttackDomain.AdvanceStateAfterAttack();
            else if (_secondAttackDomain.GetOwner() != null && _secondAttackDomain.IsInAttackState())
                _secondAttackDomain.AdvanceStateAfterAttack();
        }
    }
}
