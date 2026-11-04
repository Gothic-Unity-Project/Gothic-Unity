namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// Which controller buttons do what - the VR counterpart of Gothic's key bindings (game menu, Gothic.ini).
    /// By default the left controller moves, sprints (stick click) and opens the menu (upper button), the right one
    /// turns, teleports, jumps (lower button) and crouches (upper button).
    /// </summary>
    public readonly struct VrControlLayout
    {
        // Indexed by the Gothic.ini values. Never change the order.
        private static readonly VrButton[] _jumpButtons =
            { VrButton.Primary, VrButton.Secondary, VrButton.JoystickButton };
        private static readonly VrButton[] _menuButtons = { VrButton.Secondary, VrButton.Primary, VrButton.Menu };

        /// <summary>
        /// Mirrored: all of the left controller's tasks are on the right one and the other way round.
        /// </summary>
        public readonly bool IsLeftHanded;

        /// <summary>
        /// Primary, Secondary or JoystickButton.
        /// </summary>
        public readonly VrButton JumpButton;

        /// <summary>
        /// Secondary, Primary or Menu.
        /// </summary>
        public readonly VrButton MenuButton;

        public VrControlLayout(bool isLeftHanded, VrButton jumpButton, VrButton menuButton)
        {
            IsLeftHanded = isLeftHanded;
            JumpButton = jumpButton;
            MenuButton = menuButton;
        }

        /// <summary>
        /// Crouching takes the face button that jumping doesn't.
        /// </summary>
        public VrButton CrouchButton => JumpButton == VrButton.Secondary ? VrButton.Primary : VrButton.Secondary;

        /// <summary>
        /// From the Gothic.ini values (VRConstants.IniNames.ControllerHand, JumpButton, MenuButton).
        /// </summary>
        public static VrControlLayout FromSettings(int controllerHand, int jumpButton, int menuButton)
        {
            return new VrControlLayout(controllerHand == 1, _jumpButtons[System.Math.Clamp(jumpButton, 0, 2)],
                _menuButtons[System.Math.Clamp(menuButton, 0, 2)]);
        }
    }
}
