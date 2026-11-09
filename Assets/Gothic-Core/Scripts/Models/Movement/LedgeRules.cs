using ZenKit.Daedalus;

namespace Gothic.Core.Models.Movement
{
    /// <summary>
    /// Gothic's climbing values of a guild (C_GILVALUES), converted to metres. Angles in degrees.
    /// </summary>
    public struct LedgeRules
    {
        /// <summary>
        /// G1 human values (Species.d), used when the scripts don't provide any.
        /// </summary>
        public static readonly LedgeRules GothicHumanDefaults = new()
        {
            StepHeight = 0.6f,
            JumpLowHeight = 1.05f,
            JumpMidHeight = 2.05f,
            JumpUpHeight = 2f,
            SlideAngle = 45f,
            ClimbHeadingAngle = 45f
        };

        /// <summary>
        /// Values at or above this (cm) are no height but a marker (e.g. 999999 water depths of flyers).
        /// </summary>
        private const int _maxCentimetres = 10000;

        public float StepHeight;
        public float JumpLowHeight;
        public float JumpMidHeight;
        public float JumpUpHeight;
        public float SlideAngle;
        public float ClimbHeadingAngle;

        /// <summary>
        /// The highest top Gothic climbs (OpenGothic Npc::tryJump starts its ray down at jumpUp + jumpLow).
        /// </summary>
        public float MaxClimbHeight => JumpUpHeight + JumpLowHeight;

        /// <summary>
        /// The values of a guild's GIL_VALUES slot (see ZenKitExtension.GetValuesGuild). Species.d leaves many values
        /// of monsters at 0 - those (and markers like 999999) fall back to GothicHumanDefaults one by one.
        /// </summary>
        public static LedgeRules FromGuildValues(GuildValuesInstance values, int guild)
        {
            var defaults = GothicHumanDefaults;
            if (values == null)
                return defaults;

            return new LedgeRules
            {
                StepHeight = Metres(values.GetStepHeight(guild), defaults.StepHeight),
                JumpLowHeight = Metres(values.GetJumpLowHeight(guild), defaults.JumpLowHeight),
                JumpMidHeight = Metres(values.GetJumpMidHeight(guild), defaults.JumpMidHeight),
                JumpUpHeight = Metres(values.GetJumpUpHeight(guild), defaults.JumpUpHeight),
                SlideAngle = Degrees(values.GetSlideAngle(guild), defaults.SlideAngle),
                ClimbHeadingAngle = Degrees(values.GetClimbHeadingAngle(guild), defaults.ClimbHeadingAngle)
            };
        }

        private static float Metres(int centimetres, float fallback)
        {
            return centimetres is > 0 and < _maxCentimetres ? centimetres / 100f : fallback;
        }

        private static float Degrees(int degrees, float fallback)
        {
            return degrees > 0 ? degrees : fallback;
        }
    }
}
