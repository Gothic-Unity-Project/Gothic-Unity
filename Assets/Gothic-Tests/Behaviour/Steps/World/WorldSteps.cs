using System.Collections;
using Gothic.Core.Const;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Steps.World
{
    /// <summary>
    /// The world the scenario plays in.
    /// </summary>
    public class WorldSteps
    {
        /// <summary>
        /// Boots the game into the world, or loads it as a new game if another world is loaded. The world name as in
        /// DeveloperConfigEnums.WorldMappings (world.zen, oldmine.zen, ..., newworld.zen); the game version is the
        /// run's.
        /// </summary>
        [Step("the world \"(.*)\" is loaded")]
        public IEnumerator TheWorldIsLoaded(string world)
        {
            yield return GameSession.Ensure(world);

            Assert.That(GameObject.FindWithTag(Constants.PlayerTag), Is.Not.Null, "No hero in the world");
        }
    }
}
