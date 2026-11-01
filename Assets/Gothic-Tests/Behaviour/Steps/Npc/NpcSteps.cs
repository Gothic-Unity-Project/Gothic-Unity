using System.Collections;
using Gothic.Core.Models.Container;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
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
            var position = place.InFrontOfHero(meters);
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
    }
}
