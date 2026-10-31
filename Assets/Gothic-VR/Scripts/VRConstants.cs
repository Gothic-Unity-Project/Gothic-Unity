namespace Gothic.VR
{
    public static class VRConstants
    {
        public static class IniNames
        {
            // Accessibility
            public const string SitStand = "sitStand";
            public const string MoveDirection = "moveDirection";
            public const string RotationType = "rotationType";
            public const string SnapRotationAmount = "snapRotationAmount";
            public const string SmoothRotationSpeed = "smoothRotationSpeed";
            public const string SmoothSpectator = "smoothSpectator";

            // Immersion
            public const string Microphone = "microphone";

            // 1 = hands stop at walls and push objects (VrPhysicsHand), 0 = hands follow the controllers directly.
            public const string PhysicalHands = "physicalHands";

            // 0 = off, 1 = light, 2 = strong: the edges of the view darken while the stick moves or turns the player.
            public const string MoveVignette = "moveVignette";

            // 0 = normal, 1 = large, 2 = larger: size of subtitles and dialog choices.
            public const string TextSize = "textSize";

            // ColorVisionMode: 0 = normal, 1 = red-weak, 2 = green-weak, 3 = blue-weak.
            public const string ColorVision = "colorVision";

            // Controls (VrControlLayout)
            // 0 = right-handed, 1 = left-handed (the controllers' tasks swapped).
            public const string ControllerHand = "controllerHand";

            // 0 = lower button, 1 = upper button, 2 = stick click. Crouching takes the other button.
            public const string JumpButton = "jumpButton";

            // 0 = upper button, 1 = lower button, 2 = menu button.
            public const string MenuButton = "menuButton";
        }

        public const string IniSectionAccessibility = "GOTHIC_VR_ACCESSIBILITY";

        public const string IniSectionImmersion = "GOTHIC_VR_IMMERSION";

        public const string IniSectionControls = "GOTHIC_VR_CONTROLS";

        // 0...10 Ini values
        public const int SmoothRotationSettingAmount = 10;

        // e.g. Ini value of 3 == SmoothTurnSpeedPerSetting * 3
        public const int SmoothRotationMinSpeed = 5;
        public const int SmoothRotationMaxAdditionalSpeed = 90;
        public const float SmoothRotationDefaultValue = 0.5f;

        public const int SnapRotationDefaultValue = 2; // Element index==3
        public const int SnapRotationAmountSettingTickAmount = 5;

        public const int PhysicalHandsDefaultValue = 1;

        public const int MoveVignetteDefaultValue = 0;

        public const int TextSizeDefaultValue = 0;

        public const int ColorVisionDefaultValue = 0;

        public const int ControllerHandDefaultValue = 0;
        public const int JumpButtonDefaultValue = 0;
        public const int MenuButtonDefaultValue = 0;

        // Scale of subtitles and dialog choices per textSize value.
        private static readonly float[] _textSizeScales = { 1f, 1.25f, 1.5f };

        public static float GetTextScale(int textSize)
        {
            return _textSizeScales[System.Math.Clamp(textSize, 0, _textSizeScales.Length - 1)];
        }

        public static float SpectatorSmoothingNone = 0.0f;
        public static float SpectatorSmoothingLow = 0.02f;
        public static float SpectatorSmoothingMedium = 0.08f;
        public static float SpectatorSmoothingHigh = 0.15f;

        // Distance grab (Xri backend, far ray of both hands). HVR: HVRForceGrabber.MaxRayCastDistance = 10.
        // Max distance in meters between hand and object to highlight and pull it.
        public const float DistanceGrabMaxDistance = 5f;

        // Cone angle in degrees of the far ray. The wider, the less precisely you need to point at an object.
        public const float DistanceGrabConeAngle = 3f;
    }
}
