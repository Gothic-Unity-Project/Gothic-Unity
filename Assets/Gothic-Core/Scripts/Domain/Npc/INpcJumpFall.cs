using UnityEngine;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// An NPC's falling, jumping and climbing (implemented by the NpcJumpFall adapter, added on first use).
    /// Get it from INpcJumpFallFactory.
    /// </summary>
    public interface INpcJumpFall
    {
        /// <summary>
        /// Falling, landing or climbing - no walk or fight move meanwhile.
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// Windfist/Stormfist throw the NPC away with this speed; it lands lying and gets up.
        /// </summary>
        bool TryThrow(Vector3 velocityH, float velocityY);

        /// <summary>
        /// Climbs (or jumps up) an edge in this direction, if one is in reach.
        /// </summary>
        bool TryStartClimb(Vector3 direction);

        /// <summary>
        /// Climbs a ladder from its start to its end.
        /// </summary>
        bool TryStartLadder(Vector3 ladderStart, Vector3 ladderEnd);

        /// <summary>
        /// A drop ahead in this direction the NPC survives.
        /// </summary>
        bool IsSafeDropAhead(Vector3 direction);
    }
}
