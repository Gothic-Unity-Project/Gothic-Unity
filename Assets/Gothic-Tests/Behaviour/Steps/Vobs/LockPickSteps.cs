using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Tests.Behaviour.Runner;
using NUnit.Framework;
using Reflex.Attributes;

namespace Gothic.Tests.Behaviour.Steps.Vobs
{
    /// <summary>
    /// Lock picking by the game's rules (LockPickService) - the rolls, not the lock's combination.
    /// </summary>
    public class LockPickSteps
    {
        [Inject] private readonly LockPickService _lockPickService;
        [Inject] private readonly NpcService _npcService;


        /// <summary>
        /// Rolls a wrong turn this many times for the hero. Seed the dice for a repeatable count.
        /// </summary>
        [Step("the lock pick breaks on (\\d+)% of (\\d+) wrong turns, give or take (\\d+)")]
        public void TheLockPickBreaksOn(int percent, int turns, int tolerance)
        {
            var hero = _npcService.GetHeroContainer();
            var breaks = 0;
            for (var i = 0; i < turns; i++)
            {
                if (_lockPickService.RollsBreak(hero))
                    breaks++;
            }

            var actual = 100f * breaks / turns;
            Assert.That(actual, Is.EqualTo(percent).Within(tolerance),
                $"The lock pick broke on {breaks} of {turns} wrong turns");
        }
    }
}
