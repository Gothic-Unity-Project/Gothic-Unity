using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The raw actuation layer: synthetic keyboard and mouse devices, pressed and released with frame accurate
    /// timing. (ADR-0001 D2, §3.11 "Raw")
    ///
    /// This is what makes the whole suite feasible without a headset. The HVR device simulator sets
    /// >HVRPlayerInputs.UseWASD<, which routes locomotion, jump, sprint, crouch and the menu button through
    /// >Keyboard.current<, and turns the rig with the right mouse button plus mouse delta. So a CI run needs no
    /// XR runtime and no mock HMD - only the devices created here.
    ///
    /// Scenarios must not use this class. Its methods are key codes, and a scenario written in key codes breaks
    /// on the first rebind. Scenarios talk to <see cref="GameDriver"/>; this layer is for the driver itself and,
    /// later, for the V1 recorder.
    ///
    /// Note that InputTestFixture takes over the input system globally while it is set up, so a fixture must own
    /// exactly one InputDriver - never one here and another fixture of its own in a test base class.
    /// </summary>
    public class InputDriver : IDisposable
    {
        private const string _keyEventType = "input.key";
        private const string _mouseEventType = "input.mouse";

        private const float _fallbackFrameSeconds = 1f / 30f;

        private readonly InputTestFixture _fixture = new();
        private readonly HashSet<Key> _pressedKeys = new();
        private readonly HashSet<MouseButton> _pressedMouseButtons = new();

        private bool _isSetUp;


        public Keyboard Keyboard { get; private set; }
        public Mouse Mouse { get; private set; }

        /// <summary>
        /// Raised for every simulated input. The trace recorder writes these as its >input.*< lines, and the
        /// input stream kept for crash reproduction and frame time comparison (ADR-0001 D16) has the same source.
        /// </summary>
        public event Action<InputActuation> Actuated;


        /// <summary>
        /// Replaces the real input system with a synthetic one and adds the two devices the simulator reads.
        /// </summary>
        public void Setup()
        {
            if (_isSetUp)
                return;
            _isSetUp = true;

            _fixture.Setup();

            Keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse = InputSystem.AddDevice<Mouse>();
        }

        public void Dispose()
        {
            if (!_isSetUp)
                return;

            ReleaseAll();
            _isSetUp = false;

            _fixture.TearDown();

            Keyboard = null;
            Mouse = null;
            Actuated = null;
        }


        public void PressKey(Key key)
        {
            EnsureSetUp();

            if (!_pressedKeys.Add(key))
                return;

            _fixture.Press(Keyboard[key]);
            Actuated?.Invoke(new InputActuation(_keyEventType, key.ToString(), "down"));
        }

        public void ReleaseKey(Key key)
        {
            EnsureSetUp();

            if (!_pressedKeys.Remove(key))
                return;

            _fixture.Release(Keyboard[key]);
            Actuated?.Invoke(new InputActuation(_keyEventType, key.ToString(), "up"));
        }

        /// <summary>
        /// Hold a key for a duration. >onFrame< is invoked once per frame while the key is held - the harness
        /// passes the watchdog check in there, so a hold into a stalled game aborts instead of running its full
        /// six seconds against a frozen world.
        /// </summary>
        public IEnumerator HoldKey(Key key, float seconds, Action onFrame = null)
        {
            PressKey(key);

            try
            {
                yield return WaitFrames(FramesFor(seconds), onFrame);
            }
            finally
            {
                ReleaseKey(key);
            }
        }

        /// <summary>
        /// Press and release across a single frame - long enough for a >wasPressedThisFrame< check to see it.
        /// </summary>
        public IEnumerator TapKey(Key key)
        {
            PressKey(key);
            yield return null;
            ReleaseKey(key);
        }

        public void PressMouseButton(MouseButton button)
        {
            EnsureSetUp();

            if (!_pressedMouseButtons.Add(button))
                return;

            _fixture.Press(ControlFor(button));
            Actuated?.Invoke(new InputActuation(_mouseEventType, button.ToString(), "down"));
        }

        public void ReleaseMouseButton(MouseButton button)
        {
            EnsureSetUp();

            if (!_pressedMouseButtons.Remove(button))
                return;

            _fixture.Release(ControlFor(button));
            Actuated?.Invoke(new InputActuation(_mouseEventType, button.ToString(), "up"));
        }

        /// <summary>
        /// Set the mouse delta for the coming frame. It has to be set again every frame: the input system resets
        /// delta controls on each update, which is exactly what a real mouse reports when it stops moving.
        /// </summary>
        public void SetMouseDelta(Vector2 delta)
        {
            EnsureSetUp();

            _fixture.Set(Mouse.delta, delta);
            Actuated?.Invoke(new InputActuation(_mouseEventType, "delta", "set", delta));
        }

        /// <summary>
        /// Move the pointer to a screen position, delta included. Needed by the pointing verbs of the interaction
        /// gate; locomotion turning uses <see cref="SetMouseDelta"/> instead.
        /// </summary>
        public void MoveMouseTo(Vector2 position)
        {
            EnsureSetUp();

            _fixture.Move(Mouse.position, position);
            Actuated?.Invoke(new InputActuation(_mouseEventType, "position", "set", position));
        }

        /// <summary>
        /// Let go of everything. Called at session end and whenever a step is left through an exception, so a
        /// failed step cannot leave a key held down into the next one.
        /// </summary>
        public void ReleaseAll()
        {
            if (!_isSetUp)
                return;

            foreach (var key in new List<Key>(_pressedKeys))
                ReleaseKey(key);

            foreach (var button in new List<MouseButton>(_pressedMouseButtons))
                ReleaseMouseButton(button);

            SetMouseDelta(Vector2.zero);
        }

        /// <summary>
        /// How many frames a duration is worth. With >Time.captureDeltaTime< set per session (ADR-0001 §3.5) this
        /// is exact and identical on every machine - which is what capture pacing is for.
        /// </summary>
        public static int FramesFor(float seconds)
        {
            var step = Time.captureDeltaTime;

            if (step <= 0f)
                step = Time.deltaTime;
            if (step <= 0f)
                step = _fallbackFrameSeconds;

            return Mathf.Max(1, Mathf.RoundToInt(seconds / step));
        }

        public static IEnumerator WaitFrames(int frames, Action onFrame = null)
        {
            for (var i = 0; i < frames; i++)
            {
                yield return null;
                onFrame?.Invoke();
            }
        }

        private ButtonControl ControlFor(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right:
                    return Mouse.rightButton;
                case MouseButton.Middle:
                    return Mouse.middleButton;
                case MouseButton.Forward:
                    return Mouse.forwardButton;
                case MouseButton.Back:
                    return Mouse.backButton;
                default:
                    return Mouse.leftButton;
            }
        }

        private void EnsureSetUp()
        {
            if (!_isSetUp)
                throw new InvalidOperationException($"{nameof(InputDriver)} was used before {nameof(Setup)}() ran.");
        }
    }
}
