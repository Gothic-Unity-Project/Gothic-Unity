using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// On-screen key list for the XR Interaction Simulator (Xri backend), next to the simulator component.
    /// Shows the simulator's own keys and the Gothic shortcuts of GothicVR.inputactions ("Gothic" map, group
    /// "Simulator"). F1 hides/shows it.
    /// </summary>
    [RequireComponent(typeof(XRInteractionSimulator))]
    public class XriSimulatorControlsGUI : MonoBehaviour
    {
        private const string _gothicKeys =
            "Menu -> Esc\n" +
            "Jump -> Space\n" +
            "Crouch -> X\n" +
            "Sprint -> F (while walking)\n" +
            "Walk (left stick) -> Shift + I/J/K/L\n" +
            "Turn (right stick) -> J/L\n" +
            "Laser click -> aim with the mouse, T (or left click)\n" +
            "Swim stroke -> ] then hold G and move with W/A/S/D\n" +
            "Both grips (speech) -> [ and ] together, then G";

        private const string _simulatorKeys =
            "Move in room -> W/A/S/D, Q/E (up/down)\n" +
            "Look -> hold right mouse + move, arrow keys\n" +
            "Select device -> [ left, ] right, [+] both\n" +
            "Cycle head/controllers -> Tab, head only -> H\n" +
            "Reset device -> R\n" +
            "Trigger -> T, Grip -> G\n" +
            "Primary (A/X) -> 1, Secondary (B/Y) -> 2\n" +
            "Stick click -> 3, Menu -> M\n" +
            "Buttons go to the right hand, hold Shift for the left\n" +
            "Quick action -> Enter, cycle it -> `\n" +
            "Help on/off -> F1";

        [Tooltip("Show the key list when the simulator starts.")]
        public bool IsVisible = true;

        private XRInteractionSimulator _simulator;
        private GUIStyle _style;

        private void Awake()
        {
            _simulator = GetComponent<XRInteractionSimulator>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
                IsVisible = !IsVisible;
        }

        private void OnGUI()
        {
            if (!IsVisible)
                return;

            _style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                richText = true,
                fontSize = 12,
                wordWrap = false
            };

            var text = $"<b>Gothic</b>\n{_gothicKeys}\n\n<b>XR Interaction Simulator</b>\n{_simulatorKeys}\n\n" +
                       $"<b>Active:</b> {GetTargetText()}";
            var content = new GUIContent(text);
            var size = _style.CalcSize(content);
            GUI.Box(new Rect(4, Screen.height - size.y - 4, size.x, size.y), content, _style);
        }

        private string GetTargetText()
        {
            if (_simulator == null)
                return "-";
            if (_simulator.manipulatingHMD)
                return "head";

            var hands = _simulator.manipulatingLeftDevice && _simulator.manipulatingRightDevice ? "both controllers"
                : _simulator.manipulatingLeftDevice ? "left controller"
                : _simulator.manipulatingRightDevice ? "right controller"
                : "no controller";

            return _simulator.manipulatingFPS ? $"head + {hands}" : hands;
        }
    }
}
