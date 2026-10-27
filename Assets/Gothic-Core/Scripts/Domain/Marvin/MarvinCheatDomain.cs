using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Marvin
{
    /// <summary>
    /// The actions behind the Marvin commands. Every action returns whether it did something.
    /// </summary>
    public class MarvinCheatDomain
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly WayNetService _wayNetService;

        private const int _levelsPerLevelCheat = 5;
        private const int _learnPointsPerLevel = 10;
        private const int _hitPointsMaxPerLevel = 12;
        private const int _manaMaxPerLevelCheat = 100;
        private const int _maxSpawnedNpcs = 10;
        private const float _spawnDistance = 2f;

        /// <summary>
        /// Hit points and mana to the maximum.
        /// </summary>
        public bool Full()
        {
            var hero = _npcService.GetHeroContainer();
            _npcService.SetAttribute(hero, NpcAttribute.HitPoints, GetHeroAttribute(hero, NpcAttribute.HitPointsMax));
            _npcService.SetAttribute(hero, NpcAttribute.Mana, GetHeroAttribute(hero, NpcAttribute.ManaMax));

            Logger.Log("[MarvinCheatDomain] Full: hero healed", LogCat.Ui);
            return true;
        }

        public bool AddLevels()
        {
            var hero = _npcService.GetHeroContainer();
            var oldLevel = hero.Vob.Level;

            _npcService.SetLevel(hero, oldLevel + _levelsPerLevelCheat);
            _npcService.SetLp(hero, hero.Vob.Lp + _levelsPerLevelCheat * _learnPointsPerLevel);

            var hpMax = GetHeroAttribute(hero, NpcAttribute.HitPointsMax)
                        + _levelsPerLevelCheat * _hitPointsMaxPerLevel;
            var manaMax = GetHeroAttribute(hero, NpcAttribute.ManaMax) + _manaMaxPerLevelCheat;
            _npcService.SetAttribute(hero, NpcAttribute.HitPointsMax, hpMax);
            _npcService.SetAttribute(hero, NpcAttribute.HitPoints, hpMax);
            _npcService.SetAttribute(hero, NpcAttribute.ManaMax, manaMax);
            _npcService.SetAttribute(hero, NpcAttribute.Mana, manaMax);

            Logger.Log($"[MarvinCheatDomain] Level: {oldLevel}->{hero.Vob.Level} (LP {hero.Vob.Lp}, " +
                       $"HP {hpMax}/{hpMax}, mana {manaMax}/{manaMax})", LogCat.Ui);
            return true;
        }

        /// <summary>
        /// Guild ids differ between G1 and G2 (e.g. GIL_NOV), so the value is read from the scripts' constant.
        /// Sets guild and true guild like the scripts do when the hero joins (Npc_SetTrueGuild + hero.guild).
        /// </summary>
        public bool SetGuild(string guildSymbolName)
        {
            var guildSymbol = _gameStateService.GothicVm?.GetSymbolByName(guildSymbolName);
            if (guildSymbol == null)
            {
                Logger.LogWarning($"[MarvinCheatDomain] Guild: {guildSymbolName} isn't defined by the scripts.",
                    LogCat.Ui);
                return false;
            }

            var guild = guildSymbol.GetInt(0);
            var hero = _npcService.GetHeroContainer();
            hero.Instance.Guild = guild;
            hero.Vob.Guild = guild;
            hero.Vob.GuildTrue = guild;

            Logger.Log($"[MarvinCheatDomain] Guild: hero guild set to {guildSymbolName} ({guild})", LogCat.Ui);
            return true;
        }

        public bool TeleportToWaypoint(string wpName)
        {
            var wp = _wayNetService.GetWayNetPoint(wpName);
            if (wp == null)
            {
                Logger.LogWarning($"[MarvinCheatDomain] Teleport: WP '{wpName}' not found", LogCat.Ui);
                return false;
            }

            _contextInteractionService.TeleportPlayerTo(wp.Position, wp.Rotation);
            Logger.Log($"[MarvinCheatDomain] Teleported to '{wpName}'", LogCat.Ui);
            return true;
        }

        /// <summary>
        /// Items go into the hero's inventory. NPCs are spawned in front of the hero.
        /// </summary>
        public bool Insert(string instanceName, int amount, bool isItem)
        {
            if (isItem)
            {
                _playerService.AddItem(instanceName, amount);
                Logger.Log($"[MarvinCheatDomain] Inserted item {instanceName} x{amount}", LogCat.Ui);
                return true;
            }

            var heroGo = _npcService.GetHeroContainer()?.Go;
            if (heroGo == null)
            {
                Logger.LogWarning("[MarvinCheatDomain] Insert: hero GameObject not found", LogCat.Ui);
                return false;
            }

            var spawned = 0;
            var count = Mathf.Clamp(amount, 1, _maxSpawnedNpcs);
            for (var i = 0; i < count; i++)
            {
                // Side by side, so they don't spawn into each other.
                var sideOffset = heroGo.transform.right * (i - (count - 1) / 2f);
                var position = heroGo.transform.position + heroGo.transform.forward * _spawnDistance + sideOffset;
                if (_npcService.SpawnNpcByName(instanceName, position, heroGo.transform.rotation))
                    spawned++;
            }

            Logger.Log($"[MarvinCheatDomain] Inserted NPC {instanceName} x{spawned}", LogCat.Ui);
            return spawned > 0;
        }

        /// <summary>
        /// NPC routines follow with the next game minute tick (NpcRoutineService.OnGameTimeMinuteChanged).
        /// </summary>
        public bool SetTime(int hour, int minute)
        {
            _gameTimeService.SetTime(hour, minute);
            Logger.Log($"[MarvinCheatDomain] Time set to {hour:D2}:{minute:D2}", LogCat.Ui);
            return true;
        }

        private static int GetHeroAttribute(NpcContainer hero, NpcAttribute attribute)
        {
            return hero.Vob.GetAttribute((int)attribute);
        }
    }
}
