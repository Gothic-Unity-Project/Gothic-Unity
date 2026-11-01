using System.Collections;
using Gothic.Core.Services.Vobs;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using Gothic.VR.Interaction.Models;
using NUnit.Framework;
using Reflex.Attributes;

namespace Gothic.Tests.Behaviour.Steps.Player
{
    /// <summary>
    /// What the hero holds in his hands - grabbed through the VR layer's own grab events (HandDriver).
    /// </summary>
    public class HandSteps
    {
        // VRTorchLighter tells TorchService what the hands hold in its next Update.
        private const int _settleFrames = 2;

        [Inject] private readonly VobService _vobService;

        private readonly ScenarioScope _scope;


        public HandSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private HandDriver Hands => _scope.Driver<HandDriver>();

        /// <summary>
        /// A new item of the instance (Daedalus name, e.g. ITLSTORCH) grabbed by the hand, like one taken out of the
        /// backpack. It is gone again when the scenario ends.
        /// </summary>
        [Step("the hero holds (?:a|an) \"(.*)\" in his (left|right) hand")]
        public IEnumerator TheHeroHolds(string instance, VrHandSide side)
        {
            var item = Hands.CreateItem(instance, side);
            _scope.Undo($"remove the {instance}", () => _vobService.RemoveWorldItem(item));

            Hands.Grab(side, item);
            _scope.Undo($"release the {side} hand", () => Hands.Release(side));
            yield return Wait.Frames(_settleFrames);
        }

        [Step("the hero lets go of the item in his (left|right) hand")]
        public IEnumerator TheHeroLetsGo(VrHandSide side)
        {
            if (!Hands.Release(side))
                throw new AssertionException($"The {side} hand holds nothing the scenario put there");

            yield return Wait.Frames(_settleFrames);
        }
    }
}
