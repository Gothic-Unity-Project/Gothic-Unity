using System;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// What the engine tells the NPCs about the hero on its own (OpenGothic: Npc/MoveAlgo/Interactive):
    /// - PERC_ASSESSUSEMOB when the hero uses a mob (opens a chest or door, tries a lock),
    /// - PERC_ASSESSQUIETSOUND for his footsteps - not while sneaking,
    /// - PERC_ASSESSENTERROOM when he walks into another portal room (RoomService).
    /// The Daedalus scripts decide what the NPCs do with it (owner's chest, whose hut, ...).
    /// </summary>
    public class HeroPerceptionService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly RoomService _roomService;

        private const float _walkStepInterval = 0.55f;
        private const float _runStepInterval = 0.35f;
        private const float _roomCheckInterval = 0.25f;
        private const float _sameMobRepeatSeconds = 2f;

        private float _nextStepTime;
        private float _nextRoomCheckTime;
        private float _lastUseMobTime;
        private IInteractiveObject _lastUseMob;

        /// <summary>
        /// Npc_GetDetectedMob / Npc_IsDetectedMobOwnedBy*: the mob the hero uses (or used last).
        /// </summary>
        public IInteractiveObject HeroDetectedMob { get; private set; }

        /// <summary>
        /// Snd_IsSourceNpc / Npc_CanSeeSource: who made the last noise.
        /// </summary>
        public NpcContainer SoundSourceNpc { get; private set; }

        public void Update()
        {
            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();
            if (hero?.Go == null || hero.Props == null)
                return;

            UpdateFootsteps(hero);
            UpdateRoom(hero);
        }

        /// <summary>
        /// The hero grabs a mob (chest lid, door, bed, ...) or puts a lock pick in its lock.
        /// </summary>
        public void OnHeroUsesMob(IInteractiveObject mob)
        {
            if (mob == null || !_configService.Dev.EnableUseMobPerception)
                return;

            HeroDetectedMob = mob;
            if (mob == _lastUseMob && Time.time < _lastUseMobTime + _sameMobRepeatSeconds)
                return;
            _lastUseMob = mob;
            _lastUseMobTime = Time.time;

            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var hero = heroInstance.GetUserData();

            var notified = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessUseMob,
                heroInstance, heroInstance);
            Logger.Log($"[HeroPerc] AssessUseMob '{mob.Name}' {mob.Visual?.Name} (owner '{(mob as IMovableObject)?.Owner}', guild " +
                       $"'{(mob as IMovableObject)?.OwnerGuild}') sent to {notified} NPC(s)", LogCat.Ai);
        }

        /// <summary>
        /// Like OpenGothic: every footstep is a quiet sound unless the hero sneaks (BS_SNEAK). G1's B_AssessQuietSound
        /// only reacts to noises with an item (Npc_GetDistToItem), so there footsteps stay without effect - as in the engine.
        /// </summary>
        private void UpdateFootsteps(NpcContainer hero)
        {
            if (!_configService.Dev.EnableFootstepQuietSound)
                return;

            var bodyState = hero.Props.BodyState;
            if (bodyState is not (VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun))
                return;
            if (Time.time < _nextStepTime)
                return;
            _nextStepTime = Time.time + (bodyState == VmGothicEnums.BodyState.BsRun ? _runStepInterval : _walkStepInterval);

            SoundSourceNpc = hero;
            var vm = _gameStateService.GothicVm;
            var oldItem = vm.GlobalItem;
            try
            {
                // No item makes this noise - a stale global item would make NPCs walk to it.
                vm.GlobalItem = null;
                _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessQuietSound,
                    hero.Instance, hero.Instance);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[HeroPerc] Footstep quiet sound failed: {e.Message}", LogCat.Ai);
            }
            finally
            {
                vm.GlobalItem = oldItem;
            }
        }

        private void UpdateRoom(NpcContainer hero)
        {
            if (!_configService.Dev.EnableEnterRoomPerception || Time.time < _nextRoomCheckTime)
                return;
            _nextRoomCheckTime = Time.time + _roomCheckInterval;

            if (!_roomService.UpdateHeroRoom(hero.Go.transform.position))
                return;

            var notified = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessEnterRoom,
                hero.Instance, hero.Instance);
            Logger.Log($"[HeroPerc] AssessEnterRoom ('{_roomService.HeroFormerRoom}' -> '{_roomService.HeroRoom}') " +
                       $"sent to {notified} NPC(s)", LogCat.Ai);
        }
    }
}
