using UnityEngine;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// Bends a chain of a model's nodes (a water pipe's hose) so that its tip reaches a point: CCD (cyclic coordinate
    /// descent) - from the last link to the first, each one turns the tip towards the target, a few rounds. Each turn
    /// is limited, so the bend spreads over the whole hose instead of kinking at its end. Plain math on the
    /// transforms, no allocations.
    /// </summary>
    public static class HoseIk
    {
        private const float _minDistanceSqr = 0.000001f;

        /// <param name="links">The turning nodes, first = the one fixed to the model (BIP01 FLEX0).</param>
        /// <param name="tip">The node that should reach target (BIP01 FLEXEND), below the last link.</param>
        public static void Solve(Transform[] links, Transform tip, Vector3 target, int iterations,
            float maxTurnDegrees, float tolerance)
        {
            var toleranceSqr = tolerance * tolerance;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                for (var i = links.Length - 1; i >= 0; i--)
                {
                    var link = links[i];
                    var linkPosition = link.position;
                    var toTip = tip.position - linkPosition;
                    var toTarget = target - linkPosition;
                    if (toTip.sqrMagnitude < _minDistanceSqr || toTarget.sqrMagnitude < _minDistanceSqr)
                        continue;

                    var turn = Quaternion.FromToRotation(toTip, toTarget);
                    link.rotation = Quaternion.RotateTowards(Quaternion.identity, turn, maxTurnDegrees) * link.rotation;
                }

                if ((tip.position - target).sqrMagnitude <= toleranceSqr)
                    return;
            }
        }
    }
}
