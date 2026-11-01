using System.Collections;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using Gothic.VR.Interaction.Models;
using NUnit.Framework;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Steps.Player
{
    /// <summary>
    /// The hero played through the desktop VR simulator with the input session's keyboard and mouse (InputDriver).
    /// Only in @input scenarios - they run in an input session of their own.
    /// </summary>
    public class InputSteps
    {
        // Held for "able to walk"; moving less than this means he can't.
        private const float _walkCheckSeconds = 1.5f;
        private const float _walkCheckMeters = 0.1f;

        private readonly ScenarioScope _scope;
        private Vector3 _walkStart;
        private Vector3 _walkForward;
        private bool _isInputUndoRegistered;


        public InputSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private PlaceDriver Place => _scope.Driver<PlaceDriver>();

        private InputDriver Input
        {
            get
            {
                var input = _scope.Driver<InputDriver>();
                if (!_isInputUndoRegistered)
                {
                    _isInputUndoRegistered = true;
                    _scope.Undo("let go of every key", input.ReleaseAll);
                }

                return input;
            }
        }

        /// <summary>
        /// W - the left stick forward. Walks where the hero looks.
        /// </summary>
        [Step("the hero walks forward for (\\d+(?:\\.\\d+)?) s")]
        public IEnumerator TheHeroWalksForward(float seconds)
        {
            _walkStart = Place.HeroPosition;
            _walkForward = Place.HeroForward;
            yield return Input.Hold(Input.WalkForwardKey, seconds);
        }

        [Step("the hero has walked at least (\\d+(?:\\.\\d+)?) m forward")]
        public void TheHeroHasWalkedForward(float meters)
        {
            var moved = Place.HeroPosition - _walkStart;
            var forward = Vector3.Dot(moved, _walkForward);
            Assert.That(forward, Is.GreaterThanOrEqualTo(meters),
                $"The hero moved {moved.magnitude:0.00} m, {forward:0.00} m of it forward");
        }

        /// <summary>
        /// Holds W for 1.5 s and measures how far the hero got.
        /// </summary>
        [Step("the hero (is|is not) able to walk")]
        public IEnumerator TheHeroIsAbleToWalk(bool isAble)
        {
            var start = Place.HeroPosition;
            yield return Input.Hold(Input.WalkForwardKey, _walkCheckSeconds);

            var moved = Vector3.ProjectOnPlane(Place.HeroPosition - start, Vector3.up).magnitude;
            Assert.That(moved >= _walkCheckMeters, Is.EqualTo(isAble),
                $"The stick held forward for {_walkCheckSeconds} s moved the hero {moved:0.00} m");
        }

        /// <summary>
        /// The crouch key: the head goes down so the hands reach the ground. He stands up again when the scenario ends.
        /// </summary>
        [Step("the hero crouches")]
        public IEnumerator TheHeroCrouches()
        {
            var input = Input;
            _scope.UndoOverFrames("stand up", () => input.StandUp(GetHeadAboveFeet));
            yield return input.Crouch(GetHeadAboveFeet);
        }

        /// <summary>
        /// What the hand holds reacts to its trigger (a torch is lit or put out).
        /// </summary>
        [Step("the hero pulls the trigger of his (left|right) hand")]
        public IEnumerator TheHeroPullsTheTrigger(VrHandSide side)
        {
            yield return Input.PullTrigger(side);
        }

        private float GetHeadAboveFeet()
        {
            return Place.HeadPosition.y - Place.HeroPosition.y;
        }
    }
}
