using System.Collections;
using Gothic.Core.Const;
using Gothic.Core.Services.World;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Gothic.Tests.Behaviour.Steps.World
{
    /// <summary>
    /// The world the scenario plays in: which one, where the hero stands, the time of day, the dice. Set by a feature's
    /// Background, so they need no undo.
    /// </summary>
    public class WorldSteps
    {
        [Inject] private readonly GameTimeService _gameTimeService;

        private readonly ScenarioScope _scope;


        public WorldSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

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

        [Step("the hero stands at waypoint \"(.*)\"")]
        public IEnumerator TheHeroStandsAtWayPoint(string wayPoint)
        {
            yield return _scope.Driver<PlaceDriver>().TeleportToWayPoint(wayPoint);
        }

        /// <summary>
        /// The game clock is set, and the step waits for its next minute: what follows the clock (mob routines, NPC
        /// routines) has seen the time.
        /// </summary>
        [Step("it is (\\d{1,2}):(\\d{2})")]
        public IEnumerator ItIs(int hour, int minute)
        {
            yield return SetTime(hour, minute);
        }

        [Step("(\\d+) game minutes? pass(?:es)?")]
        public IEnumerator GameMinutesPass(int minutes)
        {
            var now = _gameTimeService.GetCurrentDateTime();
            var then = now.AddMinutes(minutes);
            var days = (then.Date - now.Date).Days;
            yield return SetTime(then.Hour + days * 24, then.Minute);
        }

        /// <summary>
        /// Sets the clock where its next minute tick is still within the minute set, and waits for that tick.
        /// </summary>
        /// <summary>
        /// UnityEngine.Random is the game's only random source (Hlp_Random included). Each scenario starts seeded by
        /// its name already; this makes a scenario's dependence on the dice visible.
        /// </summary>
        [Step("the dice are seeded with (\\d+)")]
        public void TheDiceAreSeededWith(int seed)
        {
            Random.InitState(seed);
        }

        private IEnumerator SetTime(int hour, int minute)
        {
            yield return Wait.AwayFromGameMinute();
            _gameTimeService.SetTime(hour, minute);
            yield return Wait.GameMinute();

            var now = _gameTimeService.GetCurrentTime();
            Assert.That(now.Hours == hour % 24 && now.Minutes == minute,
                $"The game clock was set to {hour % 24:00}:{minute:00} but is {now} at its next minute");
        }
    }
}
