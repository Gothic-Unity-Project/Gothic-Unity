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
    }
}
