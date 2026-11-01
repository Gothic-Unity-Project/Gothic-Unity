using System;
using System.Collections;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// NPCs a scenario brings into the world and takes out again. Only instances the world doesn't place: a placed NPC
    /// spawned a second time and changed (attacked, robbed, killed) would change what the scripts know about it (guard
    /// counters, AIV_* values) for every scenario after.
    /// </summary>
    public class NpcDriver
    {
        private const string _npcClass = "C_NPC";
        private const int _maxCandidates = 12;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;


        /// <summary>
        /// Spawns the NPC instance (Daedalus name) at the position, facing the direction, and waits until it is built.
        /// </summary>
        /// <param name="spawned">Gets the NPC as soon as it exists, before it is built (to register its removal).
        /// </param>
        public IEnumerator Spawn(string instance, Vector3 position, Quaternion rotation, Action<NpcContainer> spawned)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(instance);
            if (symbol == null || symbol.Type != DaedalusDataType.Instance)
                throw new AssertionException($"The scripts have no instance \"{instance}\"");

            var placed = Find(instance);
            if (placed != null)
                throw new AssertionException($"{instance} is placed in the world already - spawn one it doesn't " +
                    $"place: {string.Join(", ", GetUnplacedCandidates(instance))}");

            if (!_npcService.SpawnNpcByName(symbol.Name, position, rotation))
                throw new AssertionException($"{instance} couldn't be spawned (see the log)");

            var npc = Find(instance) ?? throw new AssertionException($"{instance} was spawned but isn't in the cache");
            spawned(npc);

            // Lazy loaded like every NPC: built once the culling finds it near the camera.
            yield return Wait.Until(() => npc.Go != null && npc.PrefabProps != null, Wait.DefaultSeconds,
                $"{instance} built", () => "it is still waiting to be built");
        }

        public void Remove(NpcContainer npc)
        {
            _npcService.RemoveNpc(npc);
        }

        /// <summary>
        /// The NPC of the instance in the world (the scripts' Hlp_GetNpc), null if there's none.
        /// </summary>
        public NpcContainer Find(string instance)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(instance);
            return symbol == null
                ? null
                : _multiTypeCacheService.NpcCache.FirstOrDefault(npc => npc.Instance.Index == symbol.Index);
        }

        /// <summary>
        /// NPC instances with the same prefix (GRD_ for GRD_233_Bloodwyn) the world doesn't place.
        /// </summary>
        private string[] GetUnplacedCandidates(string instance)
        {
            var prefixEnd = instance.IndexOf('_');
            var prefix = prefixEnd > 0 ? instance[..(prefixEnd + 1)] : instance;
            var placed = _multiTypeCacheService.NpcCache.Select(npc => npc.Instance.Index).ToHashSet();
            return _gameStateService.GothicVm.GetInstanceSymbols(_npcClass)
                .Where(symbol => symbol.Name.StartsWithIgnoreCase(prefix) && !placed.Contains(symbol.Index))
                .Select(symbol => symbol.Name)
                .Take(_maxCandidates)
                .ToArray();
        }
    }
}
