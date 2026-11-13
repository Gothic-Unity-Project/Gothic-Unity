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
        /// Right-handed, jump on the lower button, menu on the upper one - Gothic.ini without the VR settings.
        /// </summary>
        public static readonly VrControlLayout Default = FromSettings(0, 0, 0);

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
        /// The controller that moves, sprints and (mostly) opens the menu.
        /// </summary>
        public VrHandSide MoveSide => IsLeftHanded ? VrHandSide.Right : VrHandSide.Left;

        /// <summary>
        /// The controller that turns, teleports, jumps and crouches.
        /// </summary>
        public VrHandSide TurnSide => IsLeftHanded ? VrHandSide.Left : VrHandSide.Right;

        /// <summary>
        /// The menu opens on the moving hand - except on the menu button: the right controller's menu button is the
        /// system button on Quest and Pico, which apps never get. Mirrored, the menu (and the setting to undo it)
        /// couldn't be opened, so it stays on the left controller.
        /// </summary>
        public VrHandSide MenuSide => MenuButton == VrButton.Menu ? VrHandSide.Left : MoveSide;

        /// <summary>
        /// What a stick or face button of one controller does in this layout. Grip and trigger: None.
        /// </summary>
        public VrControlRole GetRole(VrHandSide side, VrControlSlot slot)
        {
            if (side == MenuSide && slot == ToSlot(MenuButton))
                return VrControlRole.Menu;

            if (side == MoveSide)
            {
                return slot switch
                {
                    VrControlSlot.Stick => VrControlRole.Move,
                    VrControlSlot.JoystickButton => VrControlRole.Sprint,
                    _ => VrControlRole.None
                };
            }

            if (slot == VrControlSlot.Stick)
                return VrControlRole.Turn;
            if (slot == ToSlot(JumpButton))
                return VrControlRole.Jump;
            if (slot == ToSlot(CrouchButton))
                return VrControlRole.Crouch;

            return VrControlRole.None;
        }

        /// <summary>
        /// From the Gothic.ini values (VRConstants.IniNames.ControllerHand, JumpButton, MenuButton).
        /// </summary>
        public static VrControlLayout FromSettings(int controllerHand, int jumpButton, int menuButton)
        {
            return new VrControlLayout(controllerHand == 1, _jumpButtons[System.Math.Clamp(jumpButton, 0, 2)],
                _menuButtons[System.Math.Clamp(menuButton, 0, 2)]);
        }

        private static VrControlSlot ToSlot(VrButton button)
        {
            return button switch
            {
                VrButton.Grip => VrControlSlot.Grip,
                VrButton.Trigger => VrControlSlot.Trigger,
                VrButton.Primary => VrControlSlot.Primary,
                VrButton.Secondary => VrControlSlot.Secondary,
                VrButton.Menu => VrControlSlot.Menu,
                _ => VrControlSlot.JoystickButton
            };
        }
    }
}
