namespace GUZ.VR
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
        }
        
        public const string IniSectionAccessibility = "UNZENITY_VR_ACCESSIBILITY"; // [UNZENITY_VR_ACCESSIBILITY]
        public const string IniSectionImmersion = "UNZENITY_VR_IMMERSION"; // [UNZENITY_VR_IMMERSION]

        // 0...10 Ini values
        public const int SmoothRotationSettingAmount = 10;
        // e.g. Ini value of 3 == SmoothTurnSpeedPerSetting * 3
        public const int SmoothRotationMinSpeed = 5;
        public const int SmoothRotationMaxAdditionalSpeed = 90;
        public const float SmoothRotationDefaultValue = 0.5f;

        public const int SnapRotationDefaultValue = 2; // Element index==3
        public const int SnapRotationAmountSettingTickAmount = 5;
        
        public static float SpectatorSmoothingNone = 0.0f;
        public static float SpectatorSmoothingLow = 0.02f;
        public static float SpectatorSmoothingMedium = 0.08f;
        public static float SpectatorSmoothingHigh = 0.15f;
    }
}
