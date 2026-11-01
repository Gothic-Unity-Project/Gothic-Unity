using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Tests.Behaviour.Session;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem.Controls;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// The desktop VR simulator played with the input session's test keyboard and mouse, like a developer does: W
    /// walks (the left stick), Q/E hold a hand, the mouse moves it (scroll: forward and back), G grips, the left button
    /// is the held hand's trigger. Everything after the keys - simulator, XR Interaction Toolkit, the VR layer, the
    /// game - is the real chain. Keys are queued and read by the game in the next frame.
    /// </summary>
    public class InputDriver
    {
        // The simulator's hand moves per mouse pixel and scroll notch (its defaults). Only the step size depends on
        // them: the hand is steered by where it is, frame by frame.
        private const float _metersPerPixel = 0.0015f;
        private const float _metersPerScrollNotch = 0.03f;
        private const float _maxHandStepMeters = 0.04f;
        private const float _handReachSeconds = 5f;
        private const float _grabSeconds = 2f;
        // Crouching moves the camera down to 0.7 m above the floor (XriRig), the hands reach the ground from there.
        private const float _crouchedHeadMeters = 0.9f;
        private const float _crouchSeconds = 3f;
        // A press needs a frame to arrive and one to be read; the simulator skips the mouse movement of the frame the
        // cursor gets locked.
        private const int _settleFrames = 3;

        [Inject] private readonly VRPlayerService _vrPlayerService;

        private readonly HashSet<ButtonControl> _pressed = new();
        private float _standingHeadMeters;


        private InputSession Session => GameSession.Input ??
                                        throw new AssertionException("This step drives the keyboard and mouse - tag " +
                                            "the scenario @input, it then runs in an input session");

        private IVrRig Rig => _vrPlayerService.VRContextInteractionService.Rig ??
                              throw new AssertionException("No VR rig - the scenarios run in VR (the simulator)");

        /// <summary>
        /// Holds the key down for the time, then lets go.
        /// </summary>
        public IEnumerator Hold(ButtonControl key, float seconds)
        {
            Press(key);
            yield return Wait.Seconds(seconds);
            Release(key);
            yield return Wait.Frames(_settleFrames);
        }

        public ButtonControl WalkForwardKey => Session.Keyboard.wKey;

        public bool IsCrouching { get; private set; }

        /// <summary>
        /// X - crouching is a switch. Waits until the head is down.
        /// </summary>
        public IEnumerator Crouch(System.Func<float> headAboveFeet)
        {
            if (IsCrouching)
                yield break;

            _standingHeadMeters = headAboveFeet();
            yield return Click(Session.Keyboard.xKey);
            IsCrouching = true;
            yield return Wait.Until(() => headAboveFeet() < _crouchedHeadMeters, _crouchSeconds,
                $"the head lower than {_crouchedHeadMeters} m", () => $"it is {headAboveFeet():0.00} m above the feet");
        }

        /// <summary>
        /// X again, until the head is up where it was.
        /// </summary>
        public IEnumerator StandUp(System.Func<float> headAboveFeet)
        {
            if (!IsCrouching)
                yield break;

            yield return Click(Session.Keyboard.xKey);
            IsCrouching = false;
            yield return Wait.Until(() => headAboveFeet() > _standingHeadMeters - 0.1f, _crouchSeconds,
                $"the head back up at {_standingHeadMeters:0.00} m", () => $"it is {headAboveFeet():0.00} m");
        }

        /// <summary>
        /// Holds the hand (Q/E) and moves it with the mouse until its controller is within the tolerance of the point.
        /// The hand key stays down - the grip and trigger go to the held hand.
        /// </summary>
        public IEnumerator MoveHandTo(VrHandSide side, Vector3 target, float tolerance)
        {
            Press(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
            // R puts the hands back to their start, pointing ahead: the right hand otherwise points at the mouse
            // cursor, and its ray could grab what the cursor is on instead of what the hand touches.
            yield return Click(Session.Keyboard.rKey);

            var hand = GetHand(side);
            var started = Time.realtimeSinceStartup;
            while ((target - hand.position).magnitude > tolerance)
            {
                if (Time.realtimeSinceStartup - started > _handReachSeconds)
                    throw new AssertionException($"The {side} hand didn't reach {target} within " +
                        $"{_handReachSeconds} s, it is at {hand.position} " +
                        $"({(target - hand.position).magnitude:0.00} m away), the head at {Rig.Head.position}");

                // Mouse moves the hand in the head's level frame (sideways, up), the scroll wheel forward.
                var head = Rig.Head;
                var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, forward);
                var step = Vector3.ClampMagnitude(target - hand.position, _maxHandStepMeters);
                var pixels = new Vector2(Vector3.Dot(step, right), step.y) / _metersPerPixel;
                var notches = Vector3.Dot(step, forward) / _metersPerScrollNotch;
                Session.Fixture.Set(Session.Mouse.delta, pixels);
                Session.Fixture.Set(Session.Mouse.scroll, new Vector2(0f, notches));
                yield return null;
            }
        }

        /// <summary>
        /// G while the hand is held (after MoveHandTo), then the hand is let go of: what the hand grips stays in it.
        /// </summary>
        /// <returns>Fails if the hand holds nothing afterwards.</returns>
        public IEnumerator Grip(VrHandSide side)
        {
            Press(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
            yield return Click(Session.Keyboard.gKey);

            yield return Wait.Until(() => GetHeld(side) != null, _grabSeconds, $"an item in the {side} hand",
                () => $"the grip grabbed no item (hand at {GetHand(side).position}; {DescribeGrabbers(side)})");
            Release(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
        }

        /// <summary>
        /// Lets go of what the hand grips (G again). Nothing happens for an empty hand.
        /// </summary>
        public IEnumerator LetGo(VrHandSide side)
        {
            if (GetHeld(side) == null)
                yield break;

            Press(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
            yield return Click(Session.Keyboard.gKey);
            Release(GetHandKey(side));
            yield return Wait.Until(() => GetHeld(side) == null, _grabSeconds, $"the {side} hand empty",
                () => $"it still holds {GetHeld(side).name}");
        }

        /// <summary>
        /// The trigger of the hand: the left mouse button while its hand key is held.
        /// </summary>
        public IEnumerator PullTrigger(VrHandSide side)
        {
            Press(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
            yield return Click(Session.Mouse.leftButton);
            Release(GetHandKey(side));
            yield return Wait.Frames(_settleFrames);
        }

        /// <summary>
        /// Every key and button this driver pressed is let go of (queued, read in the next frame).
        /// </summary>
        public void ReleaseAll()
        {
            if (GameSession.Input == null)
                return;

            foreach (var key in _pressed)
                GameSession.Input.Fixture.Release(key);
            _pressed.Clear();
        }

        private IEnumerator Click(ButtonControl button)
        {
            Press(button);
            yield return Wait.Frames(_settleFrames);
            Release(button);
            yield return Wait.Frames(_settleFrames);
        }

        private void Press(ButtonControl key)
        {
            Session.Fixture.Press(key);
            _pressed.Add(key);
        }

        private void Release(ButtonControl key)
        {
            Session.Fixture.Release(key);
            _pressed.Remove(key);
        }

        private ButtonControl GetHandKey(VrHandSide side)
        {
            return side == VrHandSide.Left ? Session.Keyboard.qKey : Session.Keyboard.eKey;
        }

        private Transform GetHand(VrHandSide side)
        {
            return side == VrHandSide.Left ? Rig.LeftHand : Rig.RightHand;
        }

        private string DescribeGrabbers(VrHandSide side)
        {
            return string.Join(", ", Rig.HandGrabbers.Where(grabber => grabber.HandSide == side)
                .Select(grabber => $"{grabber.Kind} grabber holds " +
                                   (grabber.Held != null ? grabber.Held.name : "nothing")));
        }

        private GameObject GetHeld(VrHandSide side)
        {
            return side == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
        }
    }
}
