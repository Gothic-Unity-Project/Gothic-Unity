using Gothic.Core.Models.Movement;
using UnityEngine;

namespace Gothic.Core.Domain.Movement
{
    /// <summary>
    /// Gothic decides whether a ledge can be climbed from the geometry and the guild values alone - no polygon or VOB
    /// marks it (OpenGothic Npc::tryJump). A ray down from JUMPUP_HEIGHT + JUMPLOW_HEIGHT above the feet finds the
    /// top, which needs room for the body, headroom on the way up and a walkable slope. The height decides the
    /// animation: below JUMPLOW_HEIGHT a big step, below JUMPUP_HEIGHT a pull-up, above it jump up and hang.
    /// </summary>
    public class LedgeProbeDomain
    {
        /// <summary>
        /// OpenGothic MoveAlgo::climbMove: Gothic probes 55 cm in front of the body and stands there after the climb.
        /// </summary>
        private const float _climbMove = 0.55f;

        /// <summary>
        /// The wall below the lip is searched this far below the top.
        /// </summary>
        private const float _lipDepth = 0.08f;

        /// <summary>
        /// The top is searched this far above and below a point near the lip (a hand).
        /// </summary>
        private const float _topSearchRange = 0.35f;

        /// <summary>
        /// The top is searched this far behind a point near the lip, towards the wall.
        /// </summary>
        private const float _topInward = 0.1f;

        /// <summary>
        /// The wall is searched from this far in front of a point near the lip.
        /// </summary>
        private const float _wallSearchBack = 0.4f;

        private const float _edgeSearchStep = 0.1f;
        private const float _edgeSearchMax = 2f;
        private const float _clearance = 0.05f;

        /// <summary>
        /// The jump probe: what Gothic's jump does with a body at feet looking along forward.
        /// </summary>
        public LedgeInfo ProbeAhead(Vector3 feet, Vector3 forward, LedgeBody body, LedgeRules rules)
        {
            var direction = Flatten(forward);
            if (direction == Vector3.zero)
                return LedgeInfo.FreePath;

            // Nothing in the way above a step: a normal jump.
            if (!IsBlocked(feet + Vector3.up * rules.StepHeight, direction, _climbMove, body,
                    body.Height - rules.StepHeight))
                return LedgeInfo.FreePath;

            var rayStart = feet + direction * _climbMove + Vector3.up * rules.MaxClimbHeight;
            if (!Physics.Raycast(rayStart, Vector3.down, out var topHit, rules.MaxClimbHeight, body.SurfaceLayerMask,
                    QueryTriggerInteraction.Ignore))
                return LedgeInfo.NoLedge;

            // Something is in the way, but no top above a step: a wall higher than the probe starts (the ray started
            // inside it) or an obstacle hanging above the floor. Gothic jumps up without climbing.
            var height = topHit.point.y - feet.y;
            if (height <= rules.StepHeight)
                return LedgeInfo.NoLedge;

            if (!IsWalkable(topHit.normal, rules) || !HasRoom(topHit.point, body))
                return LedgeInfo.NoLedge;

            // Headroom on the way up the wall, then room to move from above the feet onto the top.
            if (IsBlocked(feet, Vector3.up, height, body, body.Height))
                return LedgeInfo.NoLedge;

            var abovePosition = new Vector3(feet.x, topHit.point.y, feet.z);
            if (IsBlocked(abovePosition, direction, _climbMove, body, body.Height))
                return LedgeInfo.NoLedge;

            var info = new LedgeInfo
            {
                Band = Classify(height, rules),
                TopPoint = topHit.point,
                StandPoint = topHit.point,
                Height = height,
                WallNormal = -direction,
                EdgePoint = topHit.point
            };

            // The lip: the wall right below the top, in front of the body.
            var wallStart = new Vector3(feet.x, topHit.point.y - _lipDepth, feet.z);
            if (Physics.Raycast(wallStart, direction, out var wallHit, _climbMove + body.Radius, body.SurfaceLayerMask,
                    QueryTriggerInteraction.Ignore))
            {
                var wallNormal = Flatten(wallHit.normal);
                if (wallNormal != Vector3.zero)
                    info.WallNormal = wallNormal;
                info.EdgePoint = new Vector3(wallHit.point.x, topHit.point.y, wallHit.point.z);
            }

            info.EdgeDirection = Vector3.Cross(Vector3.up, info.WallNormal);
            return info;
        }

        /// <summary>
        /// A point near a lip (e.g. a hand), approached along approach. The ledge counts from baseFeetY (the floor
        /// the climb starts from) or, without one, from the floor at the wall's foot.
        /// </summary>
        public LedgeInfo ProbeEdge(Vector3 nearPoint, Vector3 approach, float? baseFeetY, LedgeBody body,
            LedgeRules rules)
        {
            var direction = Flatten(approach);
            if (direction == Vector3.zero)
                return LedgeInfo.NoLedge;

            // The top right behind the point.
            var topStart = nearPoint + direction * _topInward + Vector3.up * _topSearchRange;
            if (!Physics.Raycast(topStart, Vector3.down, out var topHit, _topSearchRange * 2f, body.SurfaceLayerMask,
                    QueryTriggerInteraction.Ignore) || !IsWalkable(topHit.normal, rules))
                return LedgeInfo.NoLedge;

            var topY = topHit.point.y;

            // A wall below the top: without one, the point is above a slope or open floor, not at a lip.
            var wallStart = new Vector3(nearPoint.x, topY - _lipDepth, nearPoint.z) - direction * _wallSearchBack;
            if (!Physics.Raycast(wallStart, direction, out var wallHit, _wallSearchBack + _topInward * 2f,
                    body.SurfaceLayerMask, QueryTriggerInteraction.Ignore) || IsWalkable(wallHit.normal, rules))
                return LedgeInfo.NoLedge;

            var wallNormal = Flatten(wallHit.normal);
            if (wallNormal == Vector3.zero)
                wallNormal = -direction;

            var edgePoint = new Vector3(wallHit.point.x, topY, wallHit.point.z);

            float baseY;
            if (baseFeetY.HasValue)
            {
                baseY = baseFeetY.Value;
            }
            else
            {
                var footStart = edgePoint + wallNormal * (body.Radius + _clearance);
                if (!Physics.Raycast(footStart, Vector3.down, out var footHit, rules.MaxClimbHeight + _clearance,
                        body.SurfaceLayerMask, QueryTriggerInteraction.Ignore))
                    return LedgeInfo.NoLedge;

                baseY = footHit.point.y;
            }

            var height = topY - baseY;
            if (height <= rules.StepHeight || height >= rules.MaxClimbHeight)
                return LedgeInfo.NoLedge;

            // Where the body stands after the climb.
            var standStart = edgePoint - wallNormal * Mathf.Max(_climbMove, body.Radius + _clearance * 2f)
                             + Vector3.up * _topSearchRange;
            if (!Physics.Raycast(standStart, Vector3.down, out var standHit, _topSearchRange * 2f,
                    body.SurfaceLayerMask, QueryTriggerInteraction.Ignore) || !IsWalkable(standHit.normal, rules) ||
                !HasRoom(standHit.point, body))
                return LedgeInfo.NoLedge;

            return new LedgeInfo
            {
                Band = Classify(height, rules),
                TopPoint = topHit.point,
                EdgePoint = edgePoint,
                WallNormal = wallNormal,
                EdgeDirection = Vector3.Cross(Vector3.up, wallNormal),
                StandPoint = standHit.point,
                Height = height
            };
        }

        /// <summary>
        /// A target on top of a ledge (e.g. a teleport target), approached from from. Walks back from the target
        /// towards from until the top ends, then probes that lip from the floor at the wall's foot.
        /// </summary>
        public LedgeInfo ProbeTop(Vector3 target, Vector3 from, LedgeBody body, LedgeRules rules)
        {
            var direction = Flatten(target - from);
            if (direction == Vector3.zero)
                return LedgeInfo.NoLedge;

            for (var distance = _edgeSearchStep; distance <= _edgeSearchMax; distance += _edgeSearchStep)
            {
                var sample = target - direction * distance + Vector3.up * _topSearchRange;
                var isStillOnTop = Physics.Raycast(sample, Vector3.down, out var hit,
                                       _topSearchRange + rules.StepHeight, body.SurfaceLayerMask,
                                       QueryTriggerInteraction.Ignore) &&
                                   Mathf.Abs(hit.point.y - target.y) <= rules.StepHeight;
                if (isStillOnTop)
                    continue;

                // The lip is between this sample and the last one on top.
                var nearLip = target - direction * (distance - _edgeSearchStep * 0.5f);
                var info = ProbeEdge(nearLip, direction, null, body, rules);
                return Mathf.Abs(info.TopPoint.y - target.y) <= rules.StepHeight ? info : LedgeInfo.NoLedge;
            }

            return LedgeInfo.NoLedge;
        }

        private static LedgeBand Classify(float height, LedgeRules rules)
        {
            if (height < rules.JumpLowHeight)
                return LedgeBand.Low;

            // JUMPMID_HEIGHT (205) never decides for humans: OpenGothic checks JUMPUP_HEIGHT (200) first.
            if (height < Mathf.Min(rules.JumpUpHeight, rules.JumpMidHeight))
                return LedgeBand.Mid;

            return height < rules.MaxClimbHeight ? LedgeBand.High : LedgeBand.None;
        }

        /// <summary>
        /// OpenGothic MoveAlgo::testSlide: steeper than SLIDE_ANGLE slides.
        /// </summary>
        private static bool IsWalkable(Vector3 normal, LedgeRules rules)
        {
            return normal.y >= Mathf.Cos(rules.SlideAngle * Mathf.Deg2Rad);
        }

        /// <summary>
        /// The body fits standing at the floor point (a bit above it, so that the floor isn't hit).
        /// </summary>
        private static bool HasRoom(Vector3 floor, LedgeBody body)
        {
            var bottom = floor + Vector3.up * (body.Radius + _clearance);
            var top = floor + Vector3.up * Mathf.Max(body.Height - body.Radius, body.Radius + _clearance * 2f);
            return !Physics.CheckCapsule(bottom, top, body.Radius * 0.95f, body.LayerMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// A capsule of the body's radius, standing at bottom and capsuleHeight high, hits something when moved.
        /// </summary>
        private static bool IsBlocked(Vector3 bottom, Vector3 direction, float distance, LedgeBody body,
            float capsuleHeight)
        {
            var radius = body.Radius * 0.9f;
            var point1 = bottom + Vector3.up * (body.Radius + _clearance);
            var point2 = bottom + Vector3.up * Mathf.Max(capsuleHeight - body.Radius, body.Radius + _clearance * 2f);
            return Physics.CapsuleCast(point1, point2, radius, direction, distance, body.LayerMask,
                QueryTriggerInteraction.Ignore);
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            return vector.sqrMagnitude < 0.0001f ? Vector3.zero : vector.normalized;
        }
    }
}
