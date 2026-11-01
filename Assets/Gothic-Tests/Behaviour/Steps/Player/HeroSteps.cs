using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace Gothic.Tests.Behaviour.Steps.Player
{
    /// <summary>
    /// The hero's talents and attributes, set through NpcService like the scripts do and put back when the scenario
    /// ends. Talent names as the game's VmGothicEnums.Talent (Picklock, Sneak, ...).
    /// </summary>
    public class HeroSteps
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly LockPickService _lockPickService;

        private readonly ScenarioScope _scope;


        public HeroSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        [Step("the hero's talent \"(.*)\" has value (\\d+)")]
        public void TheHerosTalentHasValue(VmGothicEnums.Talent talent, int value)
        {
            var hero = _npcService.GetHeroContainer();
            var previous = hero.Vob.GetTalent((int)talent)?.Value ?? 0;
            _npcService.ExtNpcSetTalentValue(hero.Instance, talent, value);
            _scope.Undo($"talent {talent} value back to {previous}",
                () => _npcService.ExtNpcSetTalentValue(hero.Instance, talent, previous));
        }

        [Step("the hero's talent \"(.*)\" has skill (\\d+)")]
        public void TheHerosTalentHasSkill(VmGothicEnums.Talent talent, int skill)
        {
            var hero = _npcService.GetHeroContainer();
            var previous = hero.Vob.GetTalent((int)talent)?.Skill ?? 0;
            _npcService.ExtNpcSetTalentSkill(hero.Instance, talent, skill);
            _scope.Undo($"talent {talent} skill back to {previous}",
                () => _npcService.ExtNpcSetTalentSkill(hero.Instance, talent, previous));
        }

        [Step("the hero's (strength|dexterity|hit points|mana) is (\\d+)")]
        public void TheHerosAttributeIs(NpcAttribute attribute, int value)
        {
            var hero = _npcService.GetHeroContainer();
            var previous = hero.Vob.GetAttribute((int)attribute);
            _npcService.SetAttribute(hero, attribute, value);
            _scope.Undo($"{attribute} back to {previous}", () => _npcService.SetAttribute(hero, attribute, previous));
        }

        [Step("the hero (can|cannot) pick locks")]
        public void TheHeroCanPickLocks(bool can)
        {
            Assert.That(_lockPickService.CanPickLocks(_npcService.GetHeroContainer()), Is.EqualTo(can),
                "The hero can pick locks");
        }
    }
}
