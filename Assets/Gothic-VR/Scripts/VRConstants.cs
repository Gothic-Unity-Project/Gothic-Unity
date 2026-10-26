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
            // 1 = a lethal arrow/bolt/spell knocks a human out (the next one finishes him off), 0 = it kills.
            public const string RangedKnockout = Gothic.Core.Const.FightConst.RangedKnockoutIniName;
            // 1 = a scroll is used up by its cast like in Gothic (default), 0 = scrolls are cast forever.
            public const string ScrollsSingleUse = "scrollsSingleUse";
            // 1 = every death gives the hero experience (cheat), 0 = only his, his summons' and his party's kills.
            public const string XpForAnyKill = Gothic.Core.Const.FightConst.XpForAnyKillIniName;

            // 1 = hands stop at walls and push objects (VrPhysicsHand), 0 = hands follow the controllers directly.
            public const string PhysicalHands = "physicalHands";

            // 1 = the hero's own fists are the VR hands (with the VR hero body), 0 = the VR hand models (default).
            public const string HeroHands = "heroHands";
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

        public const int PhysicalHandsDefaultValue = 1;

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
