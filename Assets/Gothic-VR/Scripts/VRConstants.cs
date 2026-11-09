using UnityEngine;

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

            // The favorites ring's size: a 0...1 slider value like heightOffset, MinRingZoom...MaxRingZoom (set with
            // the right stick while the ring is open, or in the menu).
            public const string FavoritesRingZoom = "favoritesRingZoom";

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
            // 1 = a crossbow is reloaded by pulling its string (default), 0 = by itself.
            public const string CrossbowReload = "crossbowReload";
            // 1 = a scroll is used up by its cast like in Gothic (default), 0 = scrolls are cast forever.
            public const string ScrollsSingleUse = "scrollsSingleUse";
            // 1 = projectile spells (firebolt, ...) are thrown with a swing (default), 0 = they are cast like other spells.
            public const string ThrowableSpells = "throwableSpells";
            // 1 = a charged spell grows only while both hands are together (default), 0 = one hand is enough.
            public const string TwoHandSpellCharge = "twoHandSpellCharge";
            // 1 = the favorites ring opens with the gesture (both hands up, both triggers), 0 = off.
            public const string FavoritesRing = "favoritesRing";
            // 1 = G2: sneaking, NPCs whose pickpocket dialog option would show carry a purse; grabbing it is that
            // option (default), 0 = G2 only has the dialog. G1's purse is the engine's steal window, always on.
            public const string PickpocketPurse = "pickpocketPurse";
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

        // Favorites ring zoom: the ring's radius factor (1 = normal), 30 slider steps of 0.05 (Min to MaxRingZoom).
        public const float FavoritesRingZoomDefaultValue = 1f;
        public const int FavoritesRingZoomSettingAmount = 30;

        public const int FavoritesRingDefaultValue = 1;

        public const int PickpocketPurseDefaultValue = 1;

        public const int PhysicalHandsDefaultValue = 1;

        public const int HeroHandsDefaultValue = 1;

        public const int HeroBlockDefaultValue = 1;

        public const int StabFinishDefaultValue = 1;

        public const int CrossbowReloadDefaultValue = 1;

        public const int ThrowableSpellsDefaultValue = 1;

        public const int TwoHandSpellChargeDefaultValue = 1;

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
        /// VR melee tunables: hero fists, blocking and the stab finish. Serialized prefab fields stay on the prefab.
        /// </summary>
        public static class Melee
        {
            // Fist punch: hand speed (m/s) needed to hit. Same rule as the weapon prefab's _weaponVelocityThreshold
            // (2); the prefab field is serialized and not wired to this constant - keep both in step.
            public const float FistPunchVelocity = 2f;

            // Seconds before the same fist can hit again.
            public const float FistPunchCooldownSeconds = 0.5f;

            // Seconds between tries to find the fist hitbox while it is missing.
            public const float FistHitboxRetrySeconds = 2f;

            // Block: an NPC weapon within this distance (m) of the held weapon touches it.
            public const float BlockContactDistance = 0.08f;

            // Block: NPCs further away than this (m) from the hero are not checked.
            public const float BlockContactRange = 4f;

            // Stab finish: hand speed (m/s) along the blade for a stab, and the share of the whole speed along it.
            public const float StabThrustSpeed = 1.2f;
            public const float StabThrustAlignment = 0.85f;

            // Stab finish: seconds the blade has to stay in the body to kill, and the seconds between hold pulses.
            public const float StabHoldSeconds = 2f;
            public const float StabHoldPulseSeconds = 1f;

            // Stab finish: meters back out of the body (against the tip) / sideways that let go.
            public const float StabMaxPullBackDistance = 0.15f;
            public const float StabMaxSidewaysDistance = 0.25f;
        }

        /// <summary>
        /// VR ranged tunables: bows (VrBowMeshCalibration) and crossbows (VRCrossbow).
        /// </summary>
        public static class Ranged
        {
            // Bow: a mesh whose limb tips are closer than this (m) is no bow to draw.
            public const float BowMinLength = 0.1f;

            // Bow: the max draw (m) is this share of the bow's length, clamped to the range below.
            public const float BowDrawPerLength = 0.55f;
            public const float BowMinDraw = 0.45f;
            public const float BowMaxDraw = 0.72f;

            // Crossbow: seconds it reloads by itself with the Immersion option "Crossbow" (crossbowReload) off.
            public const float CrossbowReloadSeconds = 1.5f;
        }

        /// <summary>
        /// VR magic tunables: instant spells (VRRuneCaster) and thrown spells (VRSpellThrow).
        /// </summary>
        public static class Magic
        {
            // Seconds an instant spell (G2 summons, fire rain, transformations, heal...) glows in the hand before it
            // goes off - to enjoy the effects. 0 = at once like the engine. Throwable spells aren't affected.
            public const float MinSpellCastSeconds = 2f;

            // Speed of thrown spells relative to the spell's own speed (VISUALFX emTrjEaseVel) - 1 = engine speed;
            // 1.25 felt right in VR (a full swing at 1 was a little weak).
            public const float SpellThrowSpeedMultiplier = 1.25f;

            // Hand speed (m/s) of a full spell throw. Slower swings throw weaker and arc down - at 3 m/s a light flick
            // was already a strong throw.
            public const float SpellThrowFullSwingSpeed = 6f;

            // Swing factor (hand speed / SpellThrowFullSwingSpeed) is capped here, so a wild swing throws at most 1.5x
            // the spell's speed. Self-tuned for VR: faster than that overshoots and feels out of control.
            public const float MaxSwingFactor = 1.5f;

            // Seconds of hand movement the throw velocity is averaged over. Self-tuned for VR: short enough to catch the
            // flick at release, long enough to smooth out the tracking jitter.
            public const float HandVelocityWindow = 0.1f;

            // Minimum swing factor for a throw to home on NPCs (and ignore gravity). Self-tuned for VR: weaker swings
            // just arc down instead of being pulled to a target.
            public const float MinHomingSwingFactor = 0.5f;

            // Minimum hand speed (m/s) to take the swing as the throw direction; slower drops the spell straight down.
            // Self-tuned for VR: below this the hand direction is just noise.
            public const float MinThrowDirectionSpeed = 0.2f;

            // Minimum speed (m/s) of a thrown spell, so a throw without a swing still leaves the hand and falls.
            // Self-tuned for VR (the engine has no such drop).
            public const float DroppedSpellSpeed = 0.1f;

            // Half angle (degrees) of the cone around the throw direction in which a homing spell picks an NPC.
            // Self-tuned for VR: helps aiming without taking it away - Gothic's focus targeting is much wider.
            public const float HomingConeDegrees = 12f;

            // How fast (degrees/s) a homing spell turns toward its target. Self-tuned for VR: a clear curve without
            // making the spell chase around corners.
            public const float HomingDegreesPerSecond = 90f;

            // Throw range (m) at least; the spell's own emTrjTargetRange (e.g. Fireball: 20) is raised to it.
            // Self-tuned for VR: the hand throws at long distances, vanilla ranges ended too early.
            public const float DefaultThrowRange = 30f;
        }

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

        /// <summary>
        /// The VR favorites ring (VRFavoritesRing, its slot views and spells, VrFavoritesGestureDomain): sizes in
        /// meters, times in seconds. Self-tuned for VR.
        /// </summary>
        public static class FavoritesRing
        {
            // Meters the hands must be above the eyes for the gesture.
            public const float GestureHeightAboveEyes = 0.1f;

            // Seconds both triggers are held for the open/close gesture.
            public const float GestureSeconds = 1f;

            // A broken gesture shorter than this (s) keeps the hold time.
            public const float GestureGraceSeconds = 0.3f;

            // Seconds the front spell rests before its effect preview shows.
            public const float PreviewSeconds = 1f;

            // The ring's radius (m) before zoom and the around-player scale.
            public const float RingRadius = 0.28f;

            // Scale of the slot a hand is at.
            public const float HoverScale = 1.4f;

            // Meters a hand may be from a slot to hover it.
            public const float HoverDistance = 0.09f;

            // A held item closer than this to a slot (any part of it - a sword's blade, not its grip in the hand).
            public const float PutInDistance = 0.1f;

            // Let go this long after the held item touched a slot still puts it in.
            public const float PutInGraceSeconds = 0.2f;

            // Meters a hand moves with the trigger held before it turns the carousel.
            public const float DragStartDistance = 0.03f;

            // Degrees per second the carousel turns when stepped.
            public const float SpinSpeed = 30f;

            // World scale of a slot's name label.
            public const float LabelWorldScale = 0.013f;

            // The ring circles the player at eye level, 2.2 times the size (radius
            // ~0.6 m) - the slot looked at is the selected one.
            public const float AroundPlayerScale = 2.2f;

            // An item grabbed out of the ring that no hand ended up holding after this goes back.
            public const float TakeTimeoutSeconds = 1f;

            // Stacks: grip held at the slot this long takes more - first TransferStartRate per second, doubling every
            // second. The hand this far from the slot ends it.
            public const float TransferDelaySeconds = 1.5f;

            // Stack transfer: items per second at the start.
            public const float TransferStartRate = 2f;

            // Stack transfer: items per second at most.
            public const float TransferMaxRate = 500f;

            // Stack transfer: ends when the hand is this far (m) from the slot.
            public const float TransferEndDistance = 0.15f;

            // A weapon taken out of the ring and let go: after this it's in the backpack, a holster or on the ground.
            public const float TakenWeaponCheckSeconds = 0.5f;

            // Meters the ring hangs below the eyes.
            public const float AroundPlayerBelowEyes = 0.25f;

            // Zoom limits and speed (ini favoritesRingZoom).
            public const float MinRingZoom = 0.5f;

            public const float MaxRingZoom = 2f;

            public const float RingZoomSpeed = 1f;

            public const float RingZoomPerWheelNotch = 0.05f;

            // The ini value (slider, 0...1) of a zoom factor and back.
            public static float ZoomToIni(float zoom) => Mathf.InverseLerp(MinRingZoom, MaxRingZoom, zoom);

            public static float IniToZoom(float iniValue) =>
                Mathf.Lerp(MinRingZoom, MaxRingZoom, Mathf.Clamp01(iniValue));

            // Stick deflection below this doesn't zoom.
            public const float ZoomStickDeadZone = 0.2f;

            // Carousel: how fast a spin slows down.
            public const float SpinDamping = 1.5f;

            // Carousel: slower spins (degrees/s) stop at once.
            public const float MinSpinVelocity = 5f;

            // Carousel: a drag faster than this (degrees/s) keeps spinning when let go.
            public const float StopSpinVelocity = 20f;

            // Carousel: a hand closer than this (m) to the ring's axis can't drag it.
            public const float MinDragAxisDistance = 0.1f;

            // Seconds a trigger press of the other hand may last to count as a click.
            public const float ClickSeconds = 0.6f;

            // Size (m) of a slot's rune/scroll.
            public const float SpellSize = 0.09f;

            // Weapons stand upright (blade up) and are longer than the runes.
            public const float WeaponSize = 0.16f;

            // The effect's radius in the slot, a share of the rune's size.
            public const float EffectFitShare = 0.7f;

            // The rune behind its effect / the effect while a hand is at it.
            public const float PreviewModelAlpha = 0.5f;

            public const float PreviewEffectHoverAlpha = 0.5f;

            // Size (m) of a favorite item in its slot.
            public const float ItemSize = 0.1f;

            // Seconds between looks for the colliders a real weapon/item in a slot has built (they ignore the body).
            public const float ColliderCheckSeconds = 0.5f;
        }
    }
}
