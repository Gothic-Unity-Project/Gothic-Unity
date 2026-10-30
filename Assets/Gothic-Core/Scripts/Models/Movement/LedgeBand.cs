namespace Gothic.Core.Models.Movement
{
    /// <summary>
    /// What Gothic's jump does in front of an obstacle (OpenGothic Npc::tryJump's JumpStatus).
    /// </summary>
    public enum LedgeBand
    {
        /// <summary>
        /// Nothing to climb: a wall that is too high or too steep, no room on top or no headroom.
        /// </summary>
        None,

        /// <summary>
        /// Nothing in the way (or only a step): a normal jump.
        /// </summary>
        Free,

        /// <summary>
        /// Below JUMPLOW_HEIGHT: "just a big step" (T_STAND_2_JUMPUPLOW).
        /// </summary>
        Low,

        /// <summary>
        /// Below JUMPUP_HEIGHT: a pull-up with one hand (T_STAND_2_JUMPUPMID).
        /// </summary>
        Mid,

        /// <summary>
        /// Below JUMPUP_HEIGHT + JUMPLOW_HEIGHT: jump up, hang at the edge and pull up (T_STAND_2_JUMPUP, S_HANG).
        /// </summary>
        High
    }
}
