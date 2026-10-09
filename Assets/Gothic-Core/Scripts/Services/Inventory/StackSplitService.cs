using UnityEngine;
using Gothic.Core.Services.Config;
using Reflex.Attributes;

namespace Gothic.Core.Services.Inventory
{
    /// <summary>
    /// DeveloperConfig.EnableStackSplit: taking pieces off a held item stack with the other hand. How many and how
    /// fast - the VR adapter only reads the input and moves the items.
    /// </summary>
    public class StackSplitService
    {
        [Inject] private readonly ConfigService _configService;

        public bool IsEnabled => _configService.Dev.EnableStackSplit;

        /// <summary>
        /// The last piece always stays - the stack itself.
        /// </summary>
        public bool CanSplit(int stackAmount)
        {
            return stackAmount > 1;
        }

        /// <summary>
        /// Seconds until the next piece while the button is held: getting faster with every piece.
        /// </summary>
        public float GetRepeatDelay(int piecesSplit)
        {
            var firstDelay = Mathf.Max(0.02f, _configService.Dev.StackSplitRepeatDelay);
            // Speeding up gently (square root of the factor) - the full factor raced.
            var acceleration = Mathf.Sqrt(Mathf.Clamp(_configService.Dev.StackSplitAcceleration, 0.1f, 1f));
            return Mathf.Max(_minRepeatDelay, firstDelay * Mathf.Pow(acceleration, Mathf.Max(0, piecesSplit - 1)));
        }

        private const float _minRepeatDelay = 0.1f;
    }
}
