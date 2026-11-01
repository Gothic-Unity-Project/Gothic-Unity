using Gothic.Core.Logging;
using Unity.Collections;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Session
{
    /// <summary>
    /// The input system of an input session: the Input System's test fixture swaps in an input system of its own with a
    /// test keyboard and mouse, which the desktop VR simulator (XriHvrStyleSimulator) reads like real ones. The fixture
    /// removes every device, including the simulated headset and controllers the simulator found when it was enabled -
    /// so it is set up before the game boots, and a session that booted without it can't get it later.
    /// Physical keys and mouse don't reach the game meanwhile.
    /// </summary>
    public class InputSession
    {
        public readonly InputTestFixture Fixture;
        public readonly Keyboard Keyboard;
        public readonly Mouse Mouse;


        private InputSession(InputTestFixture fixture, Keyboard keyboard, Mouse mouse)
        {
            Fixture = fixture;
            Keyboard = keyboard;
            Mouse = mouse;
        }

        /// <summary>
        /// Called from inside a running [UnityTest]: the fixture then feeds its events with the player loop.
        /// </summary>
        public static InputSession Start()
        {
            // The fixture records a stack trace for every native allocation, which slows the game down; it is for
            // leaks of the tests themselves.
            var leakDetection = NativeLeakDetection.Mode;
            var fixture = new InputTestFixture();
            fixture.Setup();
            NativeLeakDetection.Mode = leakDetection;

            var session = new InputSession(fixture, InputSystem.AddDevice<Keyboard>(), InputSystem.AddDevice<Mouse>());
            Logger.Log("[BehaviourTests] Input session: the test keyboard and mouse drive the game", LogCat.Debug);
            return session;
        }

        /// <summary>
        /// The editor's own input system and devices are back.
        /// </summary>
        public void End()
        {
            Fixture.TearDown();
        }
    }
}
