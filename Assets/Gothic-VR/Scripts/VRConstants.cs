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
            // 0 = Marvin panel hidden, 1 = shown (default) - only while Marvin mode is on (MarvinService.IsMarvinMode).
            public const string MarvinPanel = "marvinPanel";
            // 1 = an unconscious NPC is finished off by a stab and a twist (default), 0 = by any hit.
            public const string StabFinish = "stabFinish";
            // 1 = a crossbow is reloaded by pulling its string (default), 0 = by itself.
            public const string CrossbowReload = "crossbowReload";
            // 1 = the spell ring opens with the gesture (both hands up, both triggers), 0 = off.
            public const string SpellRing = "spellRing";
        }
        
        public const string IniSectionAccessibility = "GOTHIC_VR_ACCESSIBILITY";
        public const string IniSectionImmersion = "GOTHIC_VR_IMMERSION";
        public const string MenuCommandRestartGame = "RESTART_GAME";
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
