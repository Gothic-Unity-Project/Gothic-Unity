using Gothic.VR.Interaction.Models;
using UnityEngine.InputSystem;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// A VrControlLayout as binding overrides of GothicVR.inputactions: the Gothic actions (menu, jump, crouch, sprint)
    /// and XRI's locomotion (move, turn, teleport, jump). The hand maps ("Gothic Left/Right", "XRI Left/Right ...
    /// Interaction") stay as they are - they are the hands themselves, grabbing and using with grip and trigger. So do
    /// the simulator's keyboard bindings. Overrides aren't saved in the asset.
    /// </summary>
    public static class XriControlLayout
    {
        private const string _gothicMapName = "Gothic";
        private static readonly string[] _mapNames = { _gothicMapName, "XRI Left Locomotion", "XRI Right Locomotion" };

        // Jump is in the Gothic map and XRI's right locomotion map (GothicJumpProvider).
        private const string _jumpActionName = "Jump";
        private const string _crouchActionName = "Crouch";
        private const string _menuActionName = "Menu";

        private const string _controllerPrefix = "<XRController>";
        private const string _leftHand = "{LeftHand}";
        private const string _rightHand = "{RightHand}";
        private const string _swapMarker = "{SwapHand}";

        public static void Apply(InputActionAsset actions, VrControlLayout layout)
        {
            foreach (var mapName in _mapNames)
            {
                var map = actions.FindActionMap(mapName);
                if (map == null)
                    continue;

                foreach (var action in map.actions)
                {
                    action.RemoveAllBindingOverrides();
                    for (var i = 0; i < action.bindings.Count; i++)
                    {
                        var path = action.bindings[i].path;
                        var remapped = Remap(mapName, action.name, path, layout);
                        if (remapped != path)
                            action.ApplyBindingOverride(i, remapped);
                    }
                }
            }
        }

        private static string Remap(string mapName, string actionName, string path, VrControlLayout layout)
        {
            if (path == null || !path.StartsWith(_controllerPrefix))
                return path;

            var hand = path.Contains(_leftHand) ? _leftHand : _rightHand;
            if (actionName == _jumpActionName)
                path = GetButtonPath(hand, layout.JumpButton);
            else if (mapName == _gothicMapName && actionName == _crouchActionName)
                path = GetButtonPath(hand, layout.CrouchButton);
            else if (mapName == _gothicMapName && actionName == _menuActionName)
                path = GetButtonPath(hand, layout.MenuButton);

            return layout.IsLeftHanded ? SwapHands(path) : path;
        }

        private static string GetButtonPath(string hand, VrButton button)
        {
            var usage = button switch
            {
                VrButton.Grip => "GripButton",
                VrButton.Trigger => "TriggerButton",
                VrButton.Secondary => "SecondaryButton",
                VrButton.Menu => "MenuButton",
                VrButton.JoystickButton => "Primary2DAxisClick",
                _ => "PrimaryButton"
            };
            return $"{_controllerPrefix}{hand}/{{{usage}}}";
        }

        private static string SwapHands(string path)
        {
            return path.Replace(_leftHand, _swapMarker).Replace(_rightHand, _leftHand).Replace(_swapMarker, _rightHand);
        }
    }
}
