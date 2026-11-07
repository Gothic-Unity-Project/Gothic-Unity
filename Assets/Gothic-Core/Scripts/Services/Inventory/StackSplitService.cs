using UnityEngine;

namespace Gothic.Core.Services.Inventory
{
    /// <summary>
    /// Taking pieces off a held item stack with the other hand. How many and how fast - the VR adapter only reads the
    /// input and moves the items.
    /// </summary>
    public class StackSplitService
    {
        private const float _minRepeatDelay = 0.1f;
        // Seconds until the second piece while the split trigger is held.
        private const float _repeatDelay = 0.45f;
        // Each next piece comes this factor faster (0.1-1, 1 = constant pace).
        private const float _acceleration = 0.8f;
        // Speeding up gently (square root of the factor) - the full factor raced.
        private static readonly float _gentleAcceleration = Mathf.Sqrt(_acceleration);

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
            return Mathf.Max(_minRepeatDelay,
                _repeatDelay * Mathf.Pow(_gentleAcceleration, Mathf.Max(0, piecesSplit - 1)));
        }
    }
}
