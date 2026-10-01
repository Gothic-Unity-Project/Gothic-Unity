#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.HVROverrides;
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
using Gothic.VR.Models.Vob;
using Gothic.Core.Extensions;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Utils;
using HurricaneVR.Framework.Shared;
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
        [Inject] private ConfigService _configService;
        [Inject] private GameStateService _gameStateService;
        [Inject] private NpcInventoryService _npcInventoryService;
        [Inject] private VobService _vobService;

        private readonly VrWeaponAttackDomain _firstAttackDomain = new VrWeaponAttackDomain().Inject();
        private readonly VrWeaponAttackDomain _secondAttackDomain = new VrWeaponAttackDomain().Inject();

        private bool _isRuneReadied;
        private VmGothicEnums.WeaponState? _readiedRangedState;

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

            var socketed = new List<(VRSocket socket, VobContainer container)>();
            foreach (var socket in Object.FindObjectsByType<VRSocket>(FindObjectsSortMode.None))
            {
                if (socket.GrabbedTarget == null || !socket.IsPlayerSocket() ||
                    socket.GetComponentInParent<VRBackpack>(true) != null)
                    continue;

                var container = socket.GrabbedTarget.GetComponentInParent<VobLoader>(true)?.Container;
                if (IsSameItem(container, itemName))
                    socketed.Add((socket, container));
            }

            var held = new List<(HVRHandGrabber hand, VobContainer container)>();
            foreach (var hand in Object.FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None))
            {
                var container = hand.GrabbedTarget?.GetComponentInParent<VobLoader>()?.Container;
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
                foreach (var hand in Object.FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None))
                {
                    if (hand.GrabbedTarget?.GetComponentInParent<VobLoader>()?.Container == container)
                        hand.ForceRelease();
                }
                _vobService.RemoveWorldItem(container);
            }
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

            foreach (var hand in Object.FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None))
            {
                var grabbed = hand.GrabbedTarget;
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
        }

        public void OnGrabbed(HVRHandSide handSide, VobContainer vobContainer, WeaponPhysicsConfig weaponConfig)
        {
            var heroContainer = _playerService.HeroContainer;

            if (!_firstAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer))
            {
                // If we can't handle with the first handler, then it's a second weapon grabbed with another hand.
                _secondAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer);
            }

            UpdateHeroWeaponState();
        }

        public void OnReleased(HVRHandSide handSide, WeaponPhysicsConfig weaponConfig)
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
                     SFXPlayer.Instance.PlaySFX(clipMetal, weapon.Go.transform.position);
                     break;
                 case VmGothicEnums.ItemMaterial.Wood:
                     var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundDrawWood);
                     SFXPlayer.Instance.PlaySFX(clipWood, weapon.Go.transform.position);
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
                    SFXPlayer.Instance.PlaySFX(clipMetal, weapon.Go.transform.position);
                    break;
                case VmGothicEnums.ItemMaterial.Wood:
                    var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawWood);
                    SFXPlayer.Instance.PlaySFX(clipWood, weapon.Go.transform.position);
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
#endif
