using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using Gothic.VR.Interaction.Models;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using ZenKit.Vobs;

namespace Gothic.Tests.Behaviour.Steps.Lighting
{
    /// <summary>
    /// Torches in the hero's hands and the hero light (TorchService, HeroLightService): lit and put out like the
    /// trigger does (VRTorchLighter -> TorchService.Toggle), checked through TorchService's own state and the shader
    /// switch HeroLightService owns.
    /// </summary>
    public class TorchSteps
    {
        // The flame, its light and glow appear in the frame after the toggle; a destroyed flame ends the hero light at
        // the next LateUpdate.
        private const int _settleFrames = 2;
        private const string _torchInstance = "ITLSTORCH";
        private const string _burntInstance = "ITLSTORCHBURNED";

        private static readonly GlobalKeyword _heroLightKeyword = GlobalKeyword.Create("_HERO_LIGHT");

        [Inject] private readonly TorchService _torchService;

        private readonly ScenarioScope _scope;
        private readonly Dictionary<string, float> _brightness = new();


        public TorchSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private HandDriver Hands => _scope.Driver<HandDriver>();

        [Step("the hero lights the torch in his (left|right) hand")]
        public IEnumerator TheHeroLightsTheTorch(VrHandSide side)
        {
            var torch = GetHeldTorch(side);
            Assert.That(_torchService.IsBurning(torch), Is.False, $"The torch in his {side} hand burns already");
            Assert.That(_torchService.Toggle(torch), Is.True, $"{Describe(torch)} can't be lit");
            yield return Wait.Frames(_settleFrames);
        }

        [Step("the hero puts out the torch in his (left|right) hand")]
        public IEnumerator TheHeroPutsOutTheTorch(VrHandSide side)
        {
            var torch = GetHeldTorch(side);
            Assert.That(_torchService.IsBurning(torch), Is.True, $"The torch in his {side} hand doesn't burn");
            Assert.That(_torchService.Toggle(torch), Is.True, $"{Describe(torch)} can't be put out");
            yield return Wait.Frames(_settleFrames);
        }

        [Step("the torch in his (left|right) hand is (burning|out|burnt)")]
        public void TheTorchInHisHandIs(VrHandSide side, string state)
        {
            var torch = GetHeldTorch(side);
            var instance = GetInstance(torch);
            var actual = _torchService.IsBurning(torch) ? "burning"
                : instance.Equals(_burntInstance, System.StringComparison.OrdinalIgnoreCase) ? "burnt"
                : instance.Equals(_torchInstance, System.StringComparison.OrdinalIgnoreCase) ? "out"
                : $"something else ({instance})";
            Assert.That(actual, Is.EqualTo(state), $"The torch in his {side} hand");
        }

        [Step("the torch in his (left|right) hand is the hero torch")]
        public void TheTorchInHisHandIsTheHeroTorch(VrHandSide side)
        {
            var torch = GetHeldTorch(side);
            Assert.That(_torchService.TryGetBurning(torch, out var state), Is.True, $"{Describe(torch)} doesn't burn");
            Assert.That(state.IsHeroTorch, Is.True, $"{Describe(torch)} isn't the hero torch");
        }

        /// <summary>
        /// The torch grabbed last in the scenario, in a hand or not.
        /// </summary>
        [Step("the torch's own light is (on|off)")]
        public void TheTorchsOwnLightIs(bool isOn)
        {
            var torch = Hands.LastGrabbed ?? throw new AssertionException("The scenario grabbed no torch");
            Assert.That(_torchService.TryGetBurning(torch, out var state), Is.True, $"{Describe(torch)} doesn't burn");
            Assert.That(state.IsOwnLightOn, Is.EqualTo(isOn), $"The own light of {Describe(torch)}");
        }

        [Step("the hero light is (on|off)")]
        public void TheHeroLightIs(bool isOn)
        {
            Assert.That(IsHeroLightOn, Is.EqualTo(isOn), "The hero light (_HERO_LIGHT) is on");
        }

        [Step("the hero light is (on|off) within (\\d+) s")]
        public IEnumerator TheHeroLightIsWithin(bool isOn, int seconds)
        {
            yield return Wait.Until(() => IsHeroLightOn == isOn, seconds, $"the hero light {(isOn ? "on" : "off")}",
                () => $"it is {(IsHeroLightOn ? "on" : "off")}");
        }

        [Step("the ground (\\d+(?:\\.\\d+)?) m in front of the hero is remembered as \"(.*)\"")]
        public IEnumerator TheGroundIsRememberedAs(float meters, string name)
        {
            yield return MeasureGround(meters, name, value => _brightness[name] = value);
        }

        [Step("the ground (\\d+(?:\\.\\d+)?) m in front of the hero is at least (\\d+)% brighter than \"(.*)\"")]
        public IEnumerator TheGroundIsBrighterThan(float meters, int percent, string name)
        {
            if (!_brightness.TryGetValue(name, out var before))
                throw new AssertionException($"No brightness remembered as \"{name}\" in this scenario");

            var now = 0f;
            yield return MeasureGround(meters, name + " after", value => now = value);
            Assert.That(now, Is.GreaterThanOrEqualTo(before * (1f + percent / 100f)),
                $"The ground {meters} m in front of the hero: {now:0.000} now, {before:0.000} as \"{name}\" " +
                $"(images in {ProbeFolder})");
        }

        private static bool IsHeroLightOn => Shader.IsKeywordEnabled(_heroLightKeyword);

        private static string ProbeFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Logs",
            "BehaviourTests", "probes");

        private IEnumerator MeasureGround(float meters, string name, System.Action<float> result)
        {
            var point = _scope.Driver<PlaceDriver>().GroundInFrontOfHero(meters);
            var file = string.Concat($"{_scope.ScenarioName} - {name}".Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c));
            yield return Probe.Brightness(point, result, Path.Combine(ProbeFolder, file + ".png"));
        }

        private VobContainer GetHeldTorch(VrHandSide side)
        {
            var torch = Hands.GetHeld(side) ?? throw new AssertionException($"His {side} hand holds nothing");
            if (torch.Vob is not IItem)
                throw new AssertionException($"His {side} hand holds {Describe(torch)}, no item");

            return torch;
        }

        private static string GetInstance(VobContainer item)
        {
            var vob = (IItem)item.Vob;
            return !string.IsNullOrEmpty(vob.Instance) ? vob.Instance : vob.Name;
        }

        private static string Describe(VobContainer item)
        {
            return item.Go != null ? item.Go.name : item.Vob.Name;
        }
    }
}
