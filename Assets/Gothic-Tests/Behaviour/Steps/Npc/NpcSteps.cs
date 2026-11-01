using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Steps.Npc
{
    /// <summary>
    /// NPCs the scenario spawns in front of the hero (instances the world doesn't place) and removes again.
    /// </summary>
    public class NpcSteps
    {
        // The game moves a spawned NPC to a free spot (NpcService.GetFreeAreaAtSpawnPoint); farther than this (m) and
        // the scenario's distance doesn't hold.
        private const float _maxPlacementError = 0.3f;
        // The scripts' AI variable of an item guard (G1's B_GuardItemsAssessTheft: any item taken is theft).
        private const string _itemGuardAiVar = "AIV_ITEMSCHWEIN";

        [Inject] private readonly GameStateService _gameStateService;

        private readonly ScenarioScope _scope;


        public NpcSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private NpcDriver Npcs => _scope.Driver<NpcDriver>();

        /// <summary>
        /// Spawned facing the hero, at a free spot near the point (the game moves it off obstacles), built before the
        /// step ends.
        /// </summary>
        [Step("\"(.*)\" stands (\\d+(?:\\.\\d+)?) m in front of the hero")]
        public IEnumerator StandsInFrontOfTheHero(string instance, float meters)
        {
            var place = _scope.Driver<PlaceDriver>();
            var position = place.GroundInFrontOfHero(meters);
            var rotation = Quaternion.LookRotation(-place.HeroForward, Vector3.up);

            NpcContainer spawned = null;
            yield return Npcs.Spawn(instance, position, rotation, npc =>
            {
                spawned = npc;
                _scope.Undo($"remove {instance}", () => Npcs.Remove(npc));
            });

            var distance = Vector3.Distance(spawned.Go.transform.position, place.HeroPosition);
            if (Mathf.Abs(distance - meters) > _maxPlacementError)
                Assert.Inconclusive($"{instance} stands {distance:0.0} m from the hero, not {meters} m - no free " +
                    "spot there (the game moved it); pick another way point for the feature");
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
