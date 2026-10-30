using UnityEngine;

namespace Gothic.Core.Models.Movement
{
    /// <summary>
    /// Result of a ledge probe. Points are in world space.
    /// </summary>
    public struct LedgeInfo
    {
        public static readonly LedgeInfo NoLedge = new() { Band = LedgeBand.None };
        public static readonly LedgeInfo FreePath = new() { Band = LedgeBand.Free };

        public LedgeBand Band;

        /// <summary>
        /// Point on the top surface the probe found.
        /// </summary>
        public Vector3 TopPoint;

        /// <summary>
        /// Point on the lip (the top edge of the wall), at the height of the top.
        /// </summary>
        public Vector3 EdgePoint;

        /// <summary>
        /// Horizontal direction along the lip.
        /// </summary>
        public Vector3 EdgeDirection;

        /// <summary>
        /// Horizontal normal of the wall below the lip. Points away from the wall, towards the climber.
        /// </summary>
        public Vector3 WallNormal;

        /// <summary>
        /// Floor on top where the climber's body fits (Gothic: 55 cm behind the lip).
        /// </summary>
        public Vector3 StandPoint;

        /// <summary>
        /// Height of the top above the floor the climb starts from.
        /// </summary>
        public float Height;

        public bool IsClimbable => Band is LedgeBand.Low or LedgeBand.Mid or LedgeBand.High;
    }
}
