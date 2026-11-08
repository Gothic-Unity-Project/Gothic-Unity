namespace Gothic.VR
{
    public static class VRConstants
    {
        public static class IniNames
        {
            // Accessibility
            public const string SitStand = "sitStand";
            public const string HeightOffset = "heightOffset";
            public const string MoveDirection = "moveDirection";
            public const string RotationType = "rotationType";
            public const string SnapRotationAmount = "snapRotationAmount";
            public const string SmoothRotationSpeed = "smoothRotationSpeed";
            public const string SmoothSpectator = "smoothSpectator";

            // VrLedgeClimbMode: 0 = pull yourself up, 1 = fade onto the ledge.
            public const string LedgeClimbMode = "ledgeClimbMode";

            // VrHeroFallMode: knocked out, dead or asleep - 0 = the view goes down with the hero's body, 1 = fade.
            public const string HeroFallMode = "heroFallMode";

            // Immersion
            public const string Microphone = "microphone";

            // 1 = hands stop at walls and push objects (VrPhysicsHand), 0 = hands follow the controllers directly.
            public const string PhysicalHands = "physicalHands";

            // 1 = the hero's own fists are the VR hands (with the VR hero body, default), 0 = the VR hand models.
            public const string HeroHands = "heroHands";
            // 1 = both hands on a melee weapon and both triggers held parry like Gothic (VRContextHeroBlockService),
            // 0 = no blocking.
            public const string HeroBlock = "heroBlock";
            // 1 = an unconscious NPC is finished off by a stab kept in the body (default), 0 = by any hit.
            public const string StabFinish = "stabFinish";
            // VrLedgeHint: 0 = off (hand half closes and ticks), 1 = glow (and a strip on the lip glows).
            public const string LedgeHints = "ledgeHints";

            // 1 = backpack in one hand + item in the other shows the item's Gothic inventory text (VRItemDetailsPopup).
            public const string ItemDetailsPopup = "itemDetailsPopup";
            // 1 = the held backpack's opening pulls nearby world items and stores them (VRBackpackVacuum).
            public const string BackpackVacuum = "backpackVacuum";
            // 1 = loot backpacks of dead NPCs and lidless containers show a green "X" that takes all (VRBackpack).
            public const string LootTakeAll = "lootTakeAll";

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

        // Height offset slider: 0...1 Ini value, 0.5 == no offset. 40 steps from -1m to +1m, i.e. 5cm each.
        public const int HeightOffsetSettingAmount = 40;
        public const float HeightOffsetMaxMeters = 1f;
        public const float HeightOffsetDefaultValue = 0.5f;

        public const int PhysicalHandsDefaultValue = 1;

        public const int HeroHandsDefaultValue = 1;

        public const int HeroBlockDefaultValue = 1;

        public const int LedgeClimbModeDefaultValue = 0;
        public const int HeroFallModeDefaultValue = 0;
        public const int LedgeHintsDefaultValue = 1;

        public const int ItemDetailsPopupDefaultValue = 1;
        public const int BackpackVacuumDefaultValue = 1;
        public const int LootTakeAllDefaultValue = 1;

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

        // Render queue of the VR hands and the hero's arms while a menu or dialog wants the hands on top: after the
        // on-top UI (UiOnTop-Text: 3901). VRHandsOnTop and VrHeroHands.
        public const int HandsOnTopRenderQueue = 3950;

        /// <summary>
        /// VR swim values. Deliberately not the guild's water depths (GetWaterDepthKnee/Chest from Daedalus): those
        /// decide whether the hero walks, wades or swims (VRSwimDive). The values here only decide how the VR hero body
        /// looks in the water (VrHeroSwimPose) and how far VRSwimDive looks for the surface.
        /// </summary>
        public static class Swim
        {
            // Water at least this deep (m, bottom to surface) and the feet off the bottom: the VR hero body swims, even
            // while the hero's swim state flickers at the surface (chest -> knee deep for a moment = walking).
            public const float BodySwimWaterDepth = 1.1f;

            // The feet at least this far (m) above the bottom count as floating (the body swims, doesn't stand).
            public const float BodyFloatAboveBottom = 0.15f;

            // VRSwimDive: the ray for the water surface goes this far (m) past the guild's chest depth, so the surface
            // is still found when the player is a bit deeper than chest deep.
            public const float WaterSurfaceRayBuffer = 2f;
        }
    }
}
