using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// What the NPCs around notice the hero doing with things, like the engine: taking an item lying in the world
    /// (PERC_ASSESSTHEFT - OpenGothic Npc::takeItem; the scripts check its owner, item guards react to any), and using
    /// a mob such as a chest, a door or a bed (PERC_ASSESSUSEMOB - OpenGothic Interactive; the scripts check the mob's
    /// owner through Npc_IsDetectedMobOwnedByNpc/ByGuild). Platforms report the hero's actions here.
    /// </summary>
    public class HeroPerceptionService
    {
        // Grabbing the same mob again (a door's handle) within this time is one use.
        private const float _mobUseRepeatSeconds = 3f;
        // A weapon the hero never had in his hands (picked up from the ground, out of a chest) counts as drawn only
        // after this time - enough to put it into the backpack.
        private const float _pickUpGraceSeconds = 3f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // The items lying in the world since it was loaded. Taking one of them is what NPCs can see as theft - items
        // the hero carried before (e.g. taken out of his backpack) are his.
        private readonly HashSet<IVirtualObject> _worldItems = new();
        private IVirtualObject _lastUsedMob;
        private float _lastMobUseTime;
        // The weapons the hero had in his hands in this world, and the one just picked up whose grace time runs.
        private readonly HashSet<IVirtualObject> _heldWeapons = new();
        private IVirtualObject _pickedUpWeapon;

        /// <summary>
        /// The mob the hero uses or used last (Npc_GetDetectedMob).
        /// </summary>
        public IInteractiveObject HeroDetectedMob { get; private set; }

        public HeroPerceptionService()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldLoaded);
        }

        /// <summary>
        /// The hero took an item into his hand. Only the first take of an item that lay in the world is noticed.
        /// </summary>
        public void OnHeroTookItem(IVirtualObject itemVob)
        {
            if (itemVob is not IItem itemVobData || !_worldItems.Remove(itemVob))
                return;

            var instanceName = !string.IsNullOrEmpty(itemVobData.Instance) ? itemVobData.Instance : itemVob.Name;
            var item = _vmCacheService.TryGetItemData(instanceName);
            var hero = GetHero();
            if (item == null || hero == null)
                return;

            Logger.Log($"[HeroPerception] The hero takes {item.Name}", LogCat.Npc);
            _npcAiService.SendPassivePerception(hero, VmGothicEnums.PerceptionType.AssessTheft, null, hero, item);
        }

        /// <summary>
        /// The hero started using a mob (chest, door, bed, wheel, ...).
        /// </summary>
        public void OnHeroUsedMob(IVirtualObject mobVob)
        {
            if (mobVob is not IInteractiveObject mob)
                return;

            HeroDetectedMob = mob;
            if (mobVob == _lastUsedMob && Time.time - _lastMobUseTime < _mobUseRepeatSeconds)
                return;

            _lastUsedMob = mobVob;
            _lastMobUseTime = Time.time;
            var hero = GetHero();
            if (hero == null)
                return;

            Logger.Log($"[HeroPerception] The hero uses {mobVob.Name} (owner '{mob.Owner}', guild '{mob.OwnerGuild}')",
                LogCat.Npc);
            _npcAiService.Npc_SendPassivePerc(hero, VmGothicEnums.PerceptionType.AssessUseMob, null, hero);
        }

        /// <summary>
        /// The melee weapon in the hero's hands changed (none: meleeState NoWeapon, weapon null). A weapon he never had
        /// in his hands counts as drawn only after a short time, so picking one up and putting it into the backpack
        /// doesn't alarm anybody. One he had before (dropped, from the belt) is drawn at once.
        /// </summary>
        public void OnHeroMeleeWeaponChanged(VmGothicEnums.WeaponState meleeState, IVirtualObject weapon)
        {
            // Still the one just picked up (e.g. a hand let go while the other holds it): its time runs on.
            if (weapon != null && weapon == _pickedUpWeapon)
                return;

            _pickedUpWeapon = null;
            if (weapon != null && _heldWeapons.Add(weapon))
            {
                _pickedUpWeapon = weapon;
                _unityMonoService.StartCoroutine(DrawAfterPickUp(weapon, meleeState));
                return;
            }

            SetHeroMeleeWeaponState(meleeState);
        }

        private IEnumerator DrawAfterPickUp(IVirtualObject weapon, VmGothicEnums.WeaponState meleeState)
        {
            yield return new WaitForSeconds(_pickUpGraceSeconds);

            if (_pickedUpWeapon != weapon)
                yield break;

            _pickedUpWeapon = null;
            SetHeroMeleeWeaponState(meleeState);
        }

        /// <summary>
        /// The hero readied or put away a melee weapon (found on BoroBongo's branch). Like the engine (oCNpc::
        /// SetWeaponMode, OpenGothic closeWeapon) the fight mode is stored - Npc_IsInFightMode reads it - and the NPCs
        /// around get PERC_DRAWWEAPON or PERC_ASSESSREMOVEWEAPON. Putting the melee weapon away leaves a readied rune
        /// or bow alone. While he's armed, NPCs that see him get PERC_ASSESSFIGHTER (AiHandler).
        /// </summary>
        private void SetHeroMeleeWeaponState(VmGothicEnums.WeaponState meleeState)
        {
            var hero = GetHero();
            var vob = hero?.GetUserData()?.Vob;
            if (vob == null)
                return;

            var oldState = (VmGothicEnums.WeaponState)vob.FightMode;
            var wasMelee = oldState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H;
            if (oldState == meleeState || (meleeState == VmGothicEnums.WeaponState.NoWeapon && !wasMelee))
                return;

            vob.FightMode = (int)meleeState;
            var isArmed = IsArmed(meleeState);
            if (isArmed == IsArmed(oldState))
                return;

            Logger.Log($"[HeroPerception] The hero {(isArmed ? "draws" : "puts away")} his weapon", LogCat.Npc);
            _npcAiService.SendPassivePerception(hero, isArmed
                ? VmGothicEnums.PerceptionType.DrawWeapon
                : VmGothicEnums.PerceptionType.AssessRemoveWeapon, null, hero, null);
        }

        public static bool IsArmed(VmGothicEnums.WeaponState state)
        {
            return state is not (VmGothicEnums.WeaponState.NoWeapon or VmGothicEnums.WeaponState.Fist);
        }

        /// <summary>
        /// Npc_IsDetectedMobOwnedByNpc: the mob the NPC (the hero) uses belongs to the owner.
        /// </summary>
        public bool IsDetectedMobOwnedByNpc(NpcInstance user, NpcInstance owner)
        {
            var mob = GetDetectedMob(user);
            var ownerName = owner != null ? _gameStateService.GothicVm.GetSymbolByIndex(owner.Index)?.Name : null;
            return mob != null && ownerName != null && mob.Owner.EqualsIgnoreCase(ownerName);
        }

        /// <summary>
        /// Npc_IsDetectedMobOwnedByGuild: the mob the NPC (the hero) uses belongs to the guild.
        /// </summary>
        public bool IsDetectedMobOwnedByGuild(NpcInstance user, int guild)
        {
            var ownerGuild = GetDetectedMob(user)?.OwnerGuild;
            if (string.IsNullOrEmpty(ownerGuild))
                return false;

            var symbol = _gameStateService.GothicVm.GetSymbolByName(ownerGuild);
            return symbol != null && symbol.GetInt(0) == guild;
        }

        /// <summary>
        /// Npc_GetDetectedMob: the scheme of the mob the NPC (the hero) uses, e.g. "CHESTBIG".
        /// </summary>
        public string GetDetectedMobScheme(NpcInstance user)
        {
            var mob = GetDetectedMob(user);
            if (mob == null)
                return string.Empty;

            var visual = mob.Visual?.Name ?? string.Empty;
            var schemeEnd = visual.IndexOf('_');
            return (schemeEnd > 0 ? visual.Substring(0, schemeEnd) : visual).ToUpperInvariant();
        }

        private IInteractiveObject GetDetectedMob(NpcInstance user)
        {
            return user != null && user.Index == GetHero()?.Index ? HeroDetectedMob : null;
        }

        private NpcInstance GetHero()
        {
            return _gameStateService.GothicVm?.GlobalHero as NpcInstance;
        }

        private void OnWorldLoaded()
        {
            _worldItems.Clear();
            _heldWeapons.Clear();
            _pickedUpWeapon = null;
            HeroDetectedMob = null;
            _lastUsedMob = null;
            if (_saveGameService.CurrentWorldName == null)
                return;

            AddItems(_saveGameService.CurrentWorldData.Vobs);
            Logger.Log($"[HeroPerception] {_worldItems.Count} items lie in the world", LogCat.Npc);
        }

        private void AddItems(IEnumerable<IVirtualObject> vobs)
        {
            if (vobs == null)
                return;

            foreach (var vob in vobs)
            {
                if (vob is IItem)
                    _worldItems.Add(vob);
                AddItems(vob.Children);
            }
        }
    }
}
