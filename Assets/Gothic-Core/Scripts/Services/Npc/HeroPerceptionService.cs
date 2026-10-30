using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
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
    /// owner through Npc_IsDetectedMobOwnedByNpc/ByGuild), and how the hero moves (body state, footsteps).
    /// Platforms report the hero's actions here.
    /// </summary>
    public class HeroPerceptionService
    {
        // Grabbing the same mob again (a door's handle) within this time is one use.
        private const float _mobUseRepeatSeconds = 3f;
        // Horizontal speed (m/s) from which the hero walks or runs. Moving with the stick is Gothic's run.
        private const float _walkSpeed = 0.2f;
        private const float _runSpeed = 1f;
        // Way (m) per footstep the NPCs around can hear.
        private const float _walkStride = 0.75f;
        private const float _runStride = 1.1f;
        private const string _sneakTalentSymbol = "NPC_TALENT_SNEAK";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcAiService _npcAiService;

        // The items lying in the world since it was loaded. Taking one of them is what NPCs can see as theft - items
        // the hero carried before (e.g. taken out of his backpack) are his.
        private readonly HashSet<IVirtualObject> _worldItems = new();
        private IVirtualObject _lastUsedMob;
        private float _lastMobUseTime;
        private float _strideDistance;

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
        /// How the hero moved this frame (found on BoroBongo's branch). Like the engine his body state is standing,
        /// walking, running, or sneaking - crouched, and only with the sneak talent (OpenGothic Npc::canSneak). The
        /// scripts read it (a running hero gets away, a sneaking one is suspicious). Every step but a sneaking one is
        /// a quiet sound for the NPCs around (PERC_ASSESSQUIETSOUND, OpenGothic tickAnimationTags). Other body
        /// states (sitting, lying, swimming, ...) stay as they are.
        /// </summary>
        public void OnHeroMoved(float movedMeters, float deltaTime, bool isCrouching)
        {
            var hero = GetHero();
            var props = hero?.GetUserData()?.Props;
            if (props == null || props.BodyState is not ((VmGothicEnums.BodyState)0
                    or VmGothicEnums.BodyState.BsStand or VmGothicEnums.BodyState.BsWalk
                    or VmGothicEnums.BodyState.BsRun or VmGothicEnums.BodyState.BsSneak))
                return;

            var speed = deltaTime > 0f ? movedMeters / deltaTime : 0f;
            if (isCrouching && CanSneak(hero))
                props.BodyState = VmGothicEnums.BodyState.BsSneak;
            else if (speed >= _runSpeed)
                props.BodyState = VmGothicEnums.BodyState.BsRun;
            else if (speed >= _walkSpeed)
                props.BodyState = VmGothicEnums.BodyState.BsWalk;
            else
                props.BodyState = VmGothicEnums.BodyState.BsStand;

            if (props.BodyState is not (VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun))
                return;

            _strideDistance += movedMeters;
            var stride = props.BodyState == VmGothicEnums.BodyState.BsRun ? _runStride : _walkStride;
            if (_strideDistance < stride)
                return;

            _strideDistance = 0f;
            _npcAiService.SendPassivePerception(hero, VmGothicEnums.PerceptionType.AssessQuietSound, hero, hero, null);
        }

        private bool CanSneak(NpcInstance hero)
        {
            var talent = _gameStateService.GothicVm.GetSymbolByName(_sneakTalentSymbol);
            if (talent == null)
                return true;

            return hero.GetUserData()?.Vob?.GetTalent(talent.GetInt(0))?.Skill > 0;
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
