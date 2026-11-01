using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Tests.Behaviour.Steps.Vobs
{
    /// <summary>
    /// Fires (oCMobFire) around the hero. A fire burns while its fire tree - the one object MobRoutineService switches,
    /// put on the fire's slot by VobInitializerDomain.CreateFire - is active. Fires are named by their mob scheme
    /// (GetMobScheme: FIREPLACE, ...), like the scripts' Wld_SetMobRoutine does.
    /// </summary>
    public class FireSteps
    {
        // The object VobInitializerDomain.CreateFire hangs the fire's look (flame, smoke, light, sound) under.
        private const string _fireTreeName = "FireTree";

        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private readonly ScenarioScope _scope;


        public FireSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        [Step("every \"(.*)\" fire within (\\d+) m is (burning|out)")]
        public void EveryFireIs(string scheme, int meters, string state)
        {
            var problem = CheckFires(scheme, meters, state == "burning");
            if (problem != null)
                Assert.Fail(problem);
        }

        [Step("every \"(.*)\" fire within (\\d+) m is (burning|out) within (\\d+) s")]
        public IEnumerator EveryFireIsWithin(string scheme, int meters, string state, int seconds)
        {
            string problem = null;
            yield return Wait.Until(() => (problem = CheckFires(scheme, meters, state == "burning")) == null,
                seconds, $"every {scheme} fire within {meters} m {state}", () => problem);
        }

        /// <summary>
        /// The fire's look (its fire tree's top objects: the flame and what belongs to it) sits on the fire's slot -
        /// a wall fire's flame once floated 20 m above its bowl.
        /// </summary>
        [Step("the flame of every fire within (\\d+) m sits within (\\d+) cm of its slot")]
        public void TheFlameOfEveryFireSitsOnItsSlot(int meters, int centimeters)
        {
            var fires = FindFires(meters).Where(fire => fire.Tree != null).ToList();
            if (fires.Count == 0)
                Assert.Fail($"No built fire with a fire tree within {meters} m. {DescribeFires(meters)}");

            var misplaced = new List<string>();
            foreach (var (container, tree) in fires)
            {
                var slot = tree.parent;
                foreach (Transform part in tree)
                {
                    var distance = Vector3.Distance(part.position, slot.position);
                    if (distance * 100f > centimeters)
                        misplaced.Add($"{container.Go.name}: {part.name} {distance:0.00} m from {slot.name}");
                }
            }

            Assert.That(misplaced, Is.Empty, $"Fire parts farther than {centimeters} cm from their slot " +
                $"({fires.Count} fires checked)");
        }

        /// <returns>Null if every fire of the scheme is in the state, else what isn't.</returns>
        private string CheckFires(string scheme, int meters, bool isBurning)
        {
            var fires = FindFires(meters)
                .Where(fire => fire.Container.Vob.GetMobScheme().Equals(scheme.ToUpperInvariant()))
                .ToList();
            if (fires.Count == 0)
                return $"no built {scheme} fire within {meters} m. {DescribeFires(meters)}";

            var withoutTree = fires.Where(fire => fire.Tree == null).Select(fire => fire.Container.Go.name).ToList();
            if (withoutTree.Count > 0)
                return $"{string.Join(", ", withoutTree)} have no fire (no fire tree)";

            var wrong = fires.Where(fire => fire.Tree.gameObject.activeSelf != isBurning)
                .Select(fire => fire.Container.Go.name).ToList();
            return wrong.Count == 0
                ? null
                : $"{wrong.Count} of {fires.Count} are {(isBurning ? "out" : "burning")}: {string.Join(", ", wrong)}";
        }

        /// <summary>
        /// The built fires within meters of the hero and their fire trees (null without one). Fires farther than the
        /// VOB culling distance aren't built.
        /// </summary>
        private List<(VobContainer Container, Transform Tree)> FindFires(int meters)
        {
            var center = _scope.Driver<PlaceDriver>().HeroPosition;
            return _multiTypeCacheService.VobCache
                .Where(container => container.Vob is Fire && container.Go != null)
                .Where(container => Vector3.Distance(container.Go.transform.position, center) <= meters)
                .Where(IsBuilt)
                .Select(container => (container, FindFireTree(container)))
                .ToList();
        }

        private static bool IsBuilt(VobContainer container)
        {
            return !container.Go.TryGetComponent(out VobLoader loader) || loader.IsLoaded;
        }

        private static Transform FindFireTree(VobContainer container)
        {
            var fire = (Fire)container.Vob;
            var slot = string.IsNullOrEmpty(fire.Slot) ? null : container.Go.FindChildRecursively(fire.Slot);
            var tree = slot != null ? slot.transform.Find(_fireTreeName) : null;
            return tree != null
                ? tree
                : container.Go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == _fireTreeName);
        }

        /// <summary>
        /// What is there instead, so a scheme can be named right in the feature.
        /// </summary>
        private string DescribeFires(int meters)
        {
            var center = _scope.Driver<PlaceDriver>().HeroPosition;
            var near = _multiTypeCacheService.VobCache
                .Where(container => container.Vob is Fire && container.Go != null)
                .Where(container => Vector3.Distance(container.Go.transform.position, center) <= meters)
                .ToList();
            var schemes = near.GroupBy(container => container.Vob.GetMobScheme())
                .Select(group => $"{group.Key} x{group.Count()} ({group.Count(IsBuilt)} built)");
            return $"Fires within {meters} m: {(near.Count == 0 ? "none" : string.Join(", ", schemes))}";
        }
    }
}
