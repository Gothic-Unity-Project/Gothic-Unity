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

            // VrLedgeClimbMode: 0 = pull yourself up, 1 = fade onto the ledge.
            public const string LedgeClimbMode = "ledgeClimbMode";

            // Immersion
            public const string Microphone = "microphone";

            // 1 = hands stop at walls and push objects (VrPhysicsHand), 0 = hands follow the controllers directly.
            public const string PhysicalHands = "physicalHands";

            // VrLedgeHint: 0 = off, 1 = touch, 2 = near (glow), 3 = assist.
            public const string LedgeHints = "ledgeHints";
        }

        public const string IniSectionAccessibility = "GOTHIC_VR_ACCESSIBILITY";

        public const string IniSectionImmersion = "GOTHIC_VR_IMMERSION";

        // 0...10 Ini values
        public const int SmoothRotationSettingAmount = 10;

        // e.g. Ini value of 3 == SmoothTurnSpeedPerSetting * 3
        public const int SmoothRotationMinSpeed = 5;
        public const int SmoothRotationMaxAdditionalSpeed = 90;
        public const float SmoothRotationDefaultValue = 0.5f;

        public const int SnapRotationDefaultValue = 2; // Element index==3
        public const int SnapRotationAmountSettingTickAmount = 5;

        public const int PhysicalHandsDefaultValue = 1;

        public const int LedgeClimbModeDefaultValue = 0;
        public const int LedgeHintsDefaultValue = 2;

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
