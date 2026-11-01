using System;
using System.Collections;
using Gothic.Core.Const;
using Gothic.Core.Services.World;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Steps.World
{
    /// <summary>
    /// The world the scenario plays in: which one, where the hero stands, the time of day. Set by a feature's
    /// Background, so they need no undo.
    /// </summary>
    public class WorldSteps
    {
        // Setting the clock and waiting for its next minute may cross into the minute after the one set.
        private const int _setTimeAttempts = 3;

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

        private IEnumerator SetTime(int hour, int minute)
        {
            var expected = new TimeSpan(hour % 24, minute, 0);
            for (var attempt = 0; attempt < _setTimeAttempts; attempt++)
            {
                _gameTimeService.SetTime(hour, minute);
                yield return Wait.GameMinute();

                var now = _gameTimeService.GetCurrentTime();
                if (now.Hours == expected.Hours && now.Minutes == expected.Minutes)
                    yield break;

                // The minute ticked over right at the set time (or the day changed already): once more, same day.
                hour %= 24;
            }

            Assert.Fail($"The game clock doesn't stay at {expected:hh\\:mm}, it is {_gameTimeService.GetCurrentTime()}");
        }
    }
}
