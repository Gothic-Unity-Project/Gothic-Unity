using Gothic.VR.Interaction.Models;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// On-screen key list for the HVR-style desktop simulator (Xri backend), next to the simulator component.
    /// Shows the body and hand keys, the Gothic shortcuts of GothicVR.inputactions ("Gothic" map, group "Simulator")
    /// and what the simulator currently does. F1 hides/shows it.
    /// </summary>
    [RequireComponent(typeof(XriHvrStyleSimulator))]
    public class XriSimulatorControlsGUI : MonoBehaviour
    {
        private const string _bodyKeys =
            "Walk (left stick) -> W/A/S/D, sprint -> F\n" +
            "Look -> hold right mouse + move\n" +
            "Jump -> Space, Crouch -> X, Menu -> Esc\n" +
            "Snap turn -> J/L (right stick), I/K = stick up/down\n" +
            "Teleport -> hold K (stick back), aim with the mouse, release\n" +
            "Laser click -> point with the mouse, left click";

        private const string _handKeys =
            "Hold Q (left) / E (right), then:\n" +
            "  Move hand -> mouse, forward/back -> scroll\n" +
            "  Rotate hand -> middle mouse + move, roll -> + scroll\n" +
            "  Grip -> G (a grabbed item stays, G again lets go; nothing\n" +
            "    grabbed or an NPC/mob used -> lets go with Q/E)\n" +
            "  Trigger -> left click\n" +
            "  Primary (A/X) -> 1, Secondary (B/Y) -> 2, Stick click -> 3\n" +
            "  Precise -> hold Shift\n" +
            "Without Q/E: left click, 1, 2, 3 go to the right hand\n" +
            "Reset hands -> R, Cursor aim (right hand) on/off -> C\n" +
            "Help on/off -> F1";

        [Tooltip("Show the key list when the simulator starts.")]
        public bool IsVisible = true;

        private XriHvrStyleSimulator _simulator;
        private GUIStyle _style;

        private void Awake()
        {
            _simulator = GetComponent<XriHvrStyleSimulator>();
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

            var text = $"<b>Body</b>\n{_bodyKeys}\n\n<b>Hands</b>\n{_handKeys}\n\n<b>Active:</b> {GetStateText()}";
            var content = new GUIContent(text);
            var size = _style.CalcSize(content);
            GUI.Box(new Rect(4, Screen.height - size.y - 4, size.x, size.y), content, _style);
        }

        private string GetStateText()
        {
            if (_simulator == null)
                return "-";

            var isLeftHeld = _simulator.IsHandHeld(VrHandSide.Left);
            var isRightHeld = _simulator.IsHandHeld(VrHandSide.Right);
            var target = isLeftHeld && isRightHeld ? "both hands"
                : isLeftHeld ? "left hand"
                : isRightHeld ? "right hand"
                : _simulator.IsLooking ? "look"
                : "-";
            var leftGrip = _simulator.IsGripToggled(VrHandSide.Left) ? "on" : "off";
            var rightGrip = _simulator.IsGripToggled(VrHandSide.Right) ? "on" : "off";
            var aim = _simulator.IsCursorAimEnabled ? "on" : "off";

            return $"{target} | grip L {leftGrip}, R {rightGrip} | cursor aim {aim}";
        }
    }
}
