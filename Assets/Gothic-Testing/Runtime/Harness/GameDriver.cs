using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The vocabulary a scenario is written in. (ADR-0001 §3.2, D10)
    ///
    /// Every verb is an ordinary C# method returning an IEnumerator, so a scenario is compiled, navigable and
    /// rename-refactorable - there is no string grammar and no runtime parser anywhere in this suite.
    ///
    /// This is the V0 subset: what the smoke scenario needs and nothing else. The rest of the vocabulary
    /// (WalkBack, Jump, Sprint, Crouch, OpenMenu, DrawWeapon, Attack, Interact, Teleport, WaitForEvent) is V2 and
    /// is added on top of the state observations it needs to resolve its targets.
    ///
    /// <see cref="WaitUntil"/> is deliberately the only wait. A fixed-delay verb passes locally and flakes on a
    /// loaded CI box, or silently passes for the wrong reason the day an animation runs 300ms longer than it did
    /// when the scenario was written. (ADR-0001 D11)
    /// </summary>
    public class GameDriver
    {
        /// <summary>
        /// Mouse delta units fed per frame while turning, before the loop has learned how far a unit actually
        /// turns the rig. Small enough not to overshoot a short turn on the very first frame.
        /// </summary>
        private const float _initialTurnUnitsPerFrame = 40f;

        /// <summary>
        /// A turn is done when it is this close. Sub-degree precision would be a lie: the rig turns in FixedUpdate
        /// steps, so the last step always overshoots slightly.
        /// </summary>
        private const float _turnToleranceDegrees = 1.5f;

        private readonly TestSession _session;
        private readonly InputDriver _input;
        private readonly PlayerLocator _player;


        internal GameDriver(TestSession session, InputDriver input, PlayerLocator player)
        {
            _session = session;
            _input = input;
            _player = player;
        }


        public bool IsPlayerAvailable => _player.IsAvailable;

        public Vector3 PlayerPosition => _player.Position;

        public float PlayerYaw => _player.Yaw;

        public string ActiveSceneName => SceneManager.GetActiveScene().name;


        /// <summary>
        /// Walk forward for a duration, i.e. hold the simulator's forward key.
        /// </summary>
        public IEnumerator WalkForward(float seconds)
        {
            yield return _input.HoldKey(Key.W, seconds, _session.Watchdog.ThrowIfTripped);
        }

        /// <summary>
        /// Turn the player rig by an angle - positive turns right, negative left.
        ///
        /// Written as a closed loop against the rig's actual heading rather than as "mouse delta times a known
        /// factor": the factor lives in HVRBodySimulator.turnSpeed, is private, and would silently invalidate
        /// every scenario the day somebody retunes it. Measuring what happened is immune to that.
        /// </summary>
        public IEnumerator Turn(float degrees, float timeoutSeconds = 15f)
        {
            if (!_player.IsAvailable)
                throw new InvalidOperationException("Cannot turn: no player rig is loaded yet.");

            var target = Mathf.Abs(degrees);
            if (target <= _turnToleranceDegrees)
                yield break;

            var direction = Mathf.Sign(degrees);
            var deadline = _session.Clock.ElapsedSeconds + timeoutSeconds;
            var previousYaw = _player.Yaw;
            var turned = 0f;

            // Learned on the first frame which actually moved the rig: how many degrees one mouse delta unit is
            // worth. From then on the loop asks for exactly the remainder and lands within tolerance.
            var degreesPerUnit = 0f;

            // The simulator only turns while the right mouse button is down (HVRBodySimulator.IsTurningPressed).
            _input.PressMouseButton(MouseButton.Right);

            try
            {
                while (turned < target - _turnToleranceDegrees)
                {
                    var remaining = target - turned;
                    var units = degreesPerUnit > Mathf.Epsilon
                        ? Mathf.Clamp(remaining / degreesPerUnit, 1f, _initialTurnUnitsPerFrame)
                        : _initialTurnUnitsPerFrame;

                    _input.SetMouseDelta(new Vector2(direction * units, 0f));

                    yield return null;

                    _session.Watchdog.ThrowIfTripped();

                    var yaw = _player.Yaw;
                    var turnedThisFrame = Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw));
                    previousYaw = yaw;
                    turned += turnedThisFrame;

                    if (turnedThisFrame > Mathf.Epsilon)
                        degreesPerUnit = turnedThisFrame / units;

                    if (_session.Clock.ElapsedSeconds > deadline)
                    {
                        throw new WaitTimeoutException(
                            $"turning {degrees:F0} degrees should complete (got {turned * direction:F0} so far)",
                            timeoutSeconds, _session.CurrentStep?.Name);
                    }
                }
            }
            finally
            {
                _input.SetMouseDelta(Vector2.zero);
                _input.ReleaseMouseButton(MouseButton.Right);
            }
        }

        /// <summary>
        /// Wait until a scene is loaded. Scene loading is the one lifecycle signal every scenario starts from.
        /// </summary>
        public IEnumerator WaitForScene(string sceneName, float timeoutSeconds = 180f)
        {
            yield return WaitUntil(() => SceneManager.GetSceneByName(sceneName).IsValid(), timeoutSeconds,
                $"scene >{sceneName}< should be loaded");

            // A scene which is valid this frame is not yet awake in it. One extra frame keeps every caller from
            // having to know that.
            yield return null;
        }

        /// <summary>
        /// The only wait in the vocabulary: poll a predicate until it holds, or fail with the reason it was
        /// supposed to hold. (ADR-0001 D11)
        ///
        /// The timeout is measured on the session clock, i.e. in captured frames, so a scenario's timeouts mean
        /// the same thing on a developer machine and on a loaded CI runner.
        /// </summary>
        public IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds, string because)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            var deadline = _session.Clock.ElapsedSeconds + timeoutSeconds;

            while (!predicate())
            {
                _session.Watchdog.ThrowIfTripped();

                if (_session.Clock.ElapsedSeconds > deadline)
                    throw new WaitTimeoutException(because, timeoutSeconds, _session.CurrentStep?.Name);

                yield return null;
            }
        }
    }
}
