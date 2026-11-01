using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Steps.Npc
{
    /// <summary>
    /// NPCs the scenario spawns in front of the hero (instances the world doesn't place) and removes again.
    /// </summary>
    public class NpcSteps
    {
        // The scripts' AI variable of an item guard (G1's B_GuardItemsAssessTheft: any item taken is theft).
        private const string _itemGuardAiVar = "AIV_ITEMSCHWEIN";

        [Inject] private readonly GameStateService _gameStateService;

        private readonly ScenarioScope _scope;
        // Where each NPC was put, by instance.
        private readonly Dictionary<string, Vector3> _spawnSpots = new();


        public NpcSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private NpcDriver Npcs => _scope.Driver<NpcDriver>();

        /// <summary>
        /// Spawned facing the hero at this distance - straight ahead, or turned to the side as little as needed for a
        /// spot the game wouldn't move it off - and built before the step ends. Where it ends up is the game's: check it
        /// with "... is N m from the hero" where the distance matters.
        /// </summary>
        [Step("\"(.*)\" stands (\\d+(?:\\.\\d+)?) m in front of the hero")]
        public IEnumerator StandsInFrontOfTheHero(string instance, float meters)
        {
            var place = _scope.Driver<PlaceDriver>();
            if (!place.TryFindFreeSpotInFrontOfHero(meters, out var position))
                Assert.Inconclusive($"No free spot {meters} m from the hero for {instance} - pick another way point " +
                    "for the feature");

            var toHero = place.HeroPosition - position;
            toHero.y = 0;
            var rotation = Quaternion.LookRotation(toHero, Vector3.up);

            _spawnSpots[instance] = position;
            NpcContainer spawned = null;
            yield return Npcs.Spawn(instance, position, rotation, npc =>
            {
                spawned = npc;
                _scope.Undo($"remove {instance}", () => Npcs.Remove(npc));
            });

            var standsAt = spawned.Go.transform.position;
            Logger.Log($"[BehaviourTests] {instance} spawned at {position}, built at {standsAt} " +
                $"({Vector3.Distance(position, standsAt):0.00} m off), {GetDistanceToHero(spawned):0.00} m from the hero",
                LogCat.Debug);
        }

        [Step("\"(.*)\" is built where he was put, give or take (\\d+) cm")]
        public void IsBuiltWhereHeWasPut(string instance, int centimeters)
        {
            if (!_spawnSpots.TryGetValue(instance, out var spot))
                throw new AssertionException($"The scenario didn't put {instance} anywhere");

            var standsAt = GetNpc(instance).Go.transform.position;
            Assert.That(Vector3.Distance(spot, standsAt) * 100f, Is.LessThanOrEqualTo(centimeters),
                $"{instance} was put at {spot} and stands at {standsAt} - cm apart");
        }

        /// <summary>
        /// Measured like the game's dialog range: from the NPC to the ground point under the hero's head.
        /// </summary>
        [Step("\"(.*)\" is (\\d+(?:\\.\\d+)?) m from the hero, give or take (\\d+(?:\\.\\d+)?) m")]
        public void IsMetersFromTheHero(string instance, float meters, float tolerance)
        {
            Assert.That(GetDistanceToHero(GetNpc(instance)), Is.EqualTo(meters).Within(tolerance),
                $"{instance}'s distance to the hero (m)");
        }

        [Step("\"(.*)\" guards the items around him")]
        public void GuardsTheItemsAroundHim(string instance)
        {
            var npc = GetNpc(instance);
            var index = _gameStateService.GothicVm.GetSymbolByName(_itemGuardAiVar)?.GetInt(0) ??
                        throw new AssertionException($"The scripts have no {_itemGuardAiVar}");

            var previous = npc.Instance.GetAiVar(index);
            npc.Instance.SetAiVar(index, 1);
            _scope.Undo($"{instance} {_itemGuardAiVar} back to {previous}",
                () => npc.Instance.SetAiVar(index, previous));
        }

        /// <summary>
        /// The state the NPC's AI runs (ZS_*), as AiHandler starts it - a routine's or one AI_StartState started.
        /// </summary>
        [Step("\"(.*)\" is in state \"(.*)\" within (\\d+) s")]
        public IEnumerator IsInStateWithin(string instance, string state, int seconds)
        {
            var npc = GetNpc(instance);
            CheckState(state);
            yield return Wait.Until(() => IsInState(npc, state), seconds, $"{instance} in {state}",
                () => $"it is in {npc.Vob.CurrentStateName}");
        }

        [Step("\"(.*)\" is not in state \"(.*)\" for (\\d+) s")]
        public IEnumerator IsNotInStateFor(string instance, string state, int seconds)
        {
            var npc = GetNpc(instance);
            CheckState(state);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                if (IsInState(npc, state))
                    throw new AssertionException(
                        $"{instance} went into {state} after {watch.Elapsed.TotalSeconds:0.0} s");

                yield return null;
            }
        }

        private float GetDistanceToHero(NpcContainer npc)
        {
            return Vector3.Distance(npc.Go.transform.position, _scope.Driver<PlaceDriver>().HeroPosition);
        }

        private NpcContainer GetNpc(string instance)
        {
            return Npcs.Find(instance) ?? throw new AssertionException($"{instance} isn't in the world");
        }

        private void CheckState(string state)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(state) == null)
                throw new AssertionException($"The scripts have no state {state}");
        }

        private static bool IsInState(NpcContainer npc, string state)
        {
            return npc.Vob.CurrentStateName.EqualsIgnoreCase(state);
        }
    }
}
