namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// What a burning torch shows right now (TorchService.TryGetBurning).
    /// </summary>
    public readonly struct BurningTorchState
    {
        /// <summary>
        /// Its normal soft light lights the world - off while it is the hero light.
        /// </summary>
        public readonly bool IsOwnLightOn;

        /// <summary>
        /// It lights the world as the hero light (the torch light option, held in the player's hand).
        /// </summary>
        public readonly bool IsHeroTorch;

        /// <summary>
        /// Its flame glows - only while it is the hero light.
        /// </summary>
        public readonly bool IsGlowing;

        public BurningTorchState(bool isOwnLightOn, bool isHeroTorch, bool isGlowing)
        {
            IsOwnLightOn = isOwnLightOn;
            IsHeroTorch = isHeroTorch;
            IsGlowing = isGlowing;
        }
    }
}
