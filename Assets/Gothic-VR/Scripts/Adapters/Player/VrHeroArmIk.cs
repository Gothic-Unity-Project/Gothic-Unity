using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Analytic two-bone IK: upper arm + forearm reach for the VR hand, the elbow bends towards a hint below and to
    /// the outside. The wrist always ends at the VR wrist (the model's hands are cut out of the mesh): out of reach
    /// the arm is stretched, tapered - 40 % of the missing length in the upper arm, 60 % in the forearm, so the most
    /// stretch sits near the wrist (skinned vertices between the moved joints stretch, the shoulder stays).
    /// The forearm rolls around its axis like the VR hand (palms of both models aligned).
    /// </summary>
    public class VrHeroArmIk
    {
        private const float _upperStretchShare = 0.4f;

        private readonly string _side;
        private Transform _upper;
        private Transform _fore;
        private Transform _hand;
        private Vector3 _foreRestLocal;
        private Vector3 _handRestLocal;
        private Vector3 _upperRestScale = Vector3.one;
        private Vector3 _handRestScale = Vector3.one;

        // Palm side of the model's hand (towards its item slot ZS_*HAND, across the fingers) in the forearm's space.
        private Vector3 _modelPalmLocal;
        private bool _hasModelPalm;
        // HVR hand model: its pivot sits at the knuckles - the wrist is the bone the fingers hang on ("RHand 1"),
        // the palm transform's forward points out of the palm.
        private GameObject _hvrHand;
        private Transform _hvrWrist;
        private Transform _hvrPalm;

        // DeveloperConfig.EnableHeroHands: the model's fist is the VR hand - it sits where the VR hand is and turns
        // like the controller (fingers along its forward, thumb along its up).
        private Transform _finger;
        // Fingers (forward) and thumb (up) in the hand's local space.
        private Quaternion _handAxesLocal = Quaternion.identity;
        // The item slot's side (_modelPalmLocal) in the hand's local space - for the forearm's roll.
        private Vector3 _slotSideLocal;
        private bool _hasHandAxes;
        private float _fistLength;
        // The fist's cross-section must be clearly longer from the thumb to the little finger than across.
        private const float _minFistAnisotropy = 1.5f;

        public bool IsFist { get; set; }

        /// <summary>
        /// The arm's root bone (BIP01 * UPPERARM), null before Bind.
        /// </summary>
        public Transform Upper => _upper;

        /// <summary>
        /// Reaching farther than the arm, the whole arm gets longer (scaled,
        /// the hand kept its size) - moving the bones apart stretched only the skin between them, the wrist.
        /// </summary>

        public VrHeroArmIk(string side)
        {
            _side = side;
        }

        public void Bind(Dictionary<string, Transform> bones,
            List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes)
        {
            bones.TryGetValue($"BIP01 {_side} UPPERARM", out _upper);
            bones.TryGetValue($"BIP01 {_side} FOREARM", out _fore);
            bones.TryGetValue($"BIP01 {_side} HAND", out _hand);
            if (_upper == null || _fore == null || _hand == null)
                return;

            _foreRestLocal = _fore.localPosition;
            _handRestLocal = _hand.localPosition;
            _upperRestScale = _upper.localScale;
            _handRestScale = _hand.localScale;
            BindModelPalm(bones, handMeshes);
        }

        /// <summary>
        /// Gothic hands have one finger bone (FINGER0) and the item slot in the palm (ZS_RIGHTHAND/ZS_LEFTHAND).
        /// </summary>
        private void BindModelPalm(Dictionary<string, Transform> bones,
            List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes)
        {
            _hasModelPalm = false;
            _hasHandAxes = false;
            _finger = null;
            var slotName = _side == "L" ? Constants.SlotLeftHand : Constants.SlotRightHand;
            if (!bones.TryGetValue($"BIP01 {_side} FINGER0", out var finger) ||
                !bones.TryGetValue(slotName, out var slot))
            {
                Logger.LogWarning($"[VRHeroBody] {_side} hand: no FINGER0/{slotName} - the forearm won't roll.",
                    LogCat.VR);
                return;
            }

            var palm = Vector3.ProjectOnPlane(slot.position - _hand.position, finger.position - _hand.position);
            if (palm.sqrMagnitude < 1e-8f)
                return;
            _modelPalmLocal = _fore.InverseTransformDirection(palm.normalized);
            _hasModelPalm = true;

            // The hand's own axes (fingers, thumb) in its local space - to turn it like the controller.
            _finger = finger;
            var fingerDirection = finger.position - _hand.position;
            _fistLength = fingerDirection.magnitude;
            var toHand = Quaternion.Inverse(_hand.rotation);
            var fingersLocal = (toHand * fingerDirection).normalized;
            _slotSideLocal = (toHand * palm).normalized;
            var thumbLocal = FindThumbAxis(handMeshes, fingersLocal, _slotSideLocal) ?? _slotSideLocal;
            _handAxesLocal = Quaternion.LookRotation(fingersLocal, thumbLocal);
            _hasHandAxes = _fistLength > 1e-4f;
        }

        /// <summary>
        /// The fist's thumb axis in the hand's local space: across the fingers, the fist is long from the thumb to
        /// the little finger (principal axis of the hand's vertices). The item slot's side only picks the thumb's
        /// end - ZS_LEFTHAND isn't the mirrored ZS_RIGHTHAND (48° off), the left fist was turned aslant.
        /// </summary>
        private Vector3? FindThumbAxis(
            List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes,
            Vector3 fingersLocal, Vector3 slotSideLocal)
        {
            var across = Vector3.Cross(fingersLocal, slotSideLocal).normalized;
            var side = Vector3.Cross(across, fingersLocal).normalized;
            foreach (var (renderer, withHands, _) in handMeshes)
            {
                var handIndex = renderer == null || withHands == null
                    ? -1
                    : System.Array.IndexOf(renderer.bones, _hand);
                if (handIndex < 0 || handIndex >= withHands.bindposes.Length)
                    continue;

                var vertices = withHands.vertices;
                var weights = withHands.boneWeights;
                if (weights.Length != vertices.Length)
                    continue;

                // Hand vertices in the hand's local space, on the plane across the fingers.
                var bindPose = withHands.bindposes[handIndex];
                var points = new List<Vector2>();
                for (var i = 0; i < vertices.Length; i++)
                {
                    if (weights[i].boneIndex0 != handIndex ||
                        weights[i].weight0 <= VrHeroBodyMeshCutter.ArmCutWeight)
                        continue;
                    var local = bindPose.MultiplyPoint3x4(vertices[i]);
                    points.Add(new Vector2(Vector3.Dot(local, across), Vector3.Dot(local, side)));
                }
                if (points.Count < 3)
                    continue;

                var mean = Vector2.zero;
                foreach (var point in points)
                    mean += point;
                mean /= points.Count;
                float xx = 0f, yy = 0f, xy = 0f;
                foreach (var point in points)
                {
                    var d = point - mean;
                    xx += d.x * d.x;
                    yy += d.y * d.y;
                    xy += d.x * d.y;
                }

                var spread = Mathf.Sqrt(0.25f * (xx - yy) * (xx - yy) + xy * xy);
                var longest = 0.5f * (xx + yy) + spread;
                var shortest = 0.5f * (xx + yy) - spread;
                if (shortest <= 0f || longest / shortest < _minFistAnisotropy)
                {
                    Logger.LogWarning($"[VRHeroBody] {_side} fist: no clear thumb axis ({renderer.name}, " +
                                      $"{longest / Mathf.Max(shortest, 1e-9f):F2}) - using the item slot's side.",
                        LogCat.VR);
                    continue;
                }

                var angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
                var thumb = Mathf.Cos(angle) * across + Mathf.Sin(angle) * side;
                if (Vector3.Dot(thumb, slotSideLocal) < 0f)
                    thumb = -thumb;
                Logger.Log($"[VRHeroBody] {_side} fist: thumb axis {Vector3.Angle(thumb, slotSideLocal):F0}° from " +
                           $"the item slot's side ({renderer.name}).", LogCat.VR);
                return thumb;
            }
            return null;
        }

        private void FindHvrBones(GameObject target)
        {
            if (_hvrHand == target)
                return;

            _hvrHand = target;
            _hvrWrist = null;
            _hvrPalm = null;
            foreach (var child in target.GetComponentsInChildren<Transform>(true))
            {
                var name = child.name.ToLowerInvariant();
                if (_hvrWrist == null && name.Contains("finger") && child.parent != null)
                    _hvrWrist = child.parent;
                if (_hvrPalm == null && name.Contains("palm"))
                    _hvrPalm = child;
            }
            Logger.Log($"[VRHeroBody] {_side} HVR hand: wrist={_hvrWrist?.name ?? "none"}, " +
                       $"palm={_hvrPalm?.name ?? "none"}", LogCat.VR);
        }

        public void Solve(GameObject target, Transform body, Transform controller, Pose? gripPose = null)
        {
            if (target == null || _upper == null || _fore == null || _hand == null)
                return;

            if (IsFist && _hasHandAxes && controller != null)
            {
                if (gripPose.HasValue)
                    SolveFist(gripPose.Value.position, body, gripPose.Value.rotation);
                else
                    SolveFist(target.transform.position, body, controller.rotation);
                return;
            }

            // Last frame's stretch must not add up.
            ResetStretch();

            // The body is scaled to the player's height - lengths from the current pose.
            var upperLength = Vector3.Distance(_upper.position, _fore.position);
            var foreLength = Vector3.Distance(_fore.position, _hand.position);
            if (upperLength <= 0f || foreLength <= 0f)
                return;

            FindHvrBones(target);
            var shoulder = _upper.position;
            var wrist = _hvrWrist != null ? _hvrWrist.position : target.transform.position;
            var toTarget = wrist - shoulder;
            var distance = Mathf.Max(toTarget.magnitude, 0.05f);
            var direction = toTarget / distance;

            Stretch(distance, ref upperLength, ref foreLength);

            // Elbow plane from a hint below, outside and a bit behind the shoulder; the elbow is rotated TOWARDS it.
            var outward = _side == "L" ? -body.right : body.right;
            var hint = Vector3.down * 0.5f + outward * 0.35f - body.forward * 0.2f;
            var bendNormal = Vector3.Cross(direction, hint);
            if (bendNormal.sqrMagnitude < 0.0001f)
                bendNormal = Vector3.Cross(direction, Vector3.down);
            bendNormal.Normalize();

            var cosShoulder = (upperLength * upperLength + distance * distance - foreLength * foreLength) /
                              (2f * upperLength * distance);
            var shoulderAngle = Mathf.Acos(Mathf.Clamp(cosShoulder, -1f, 1f)) * Mathf.Rad2Deg;
            var elbow = shoulder + Quaternion.AngleAxis(shoulderAngle, bendNormal) * direction * upperLength;

            _upper.rotation = Quaternion.FromToRotation(_fore.position - shoulder, elbow - shoulder) *
                              _upper.rotation;
            _fore.position = elbow;
            _fore.rotation = Quaternion.FromToRotation(_hand.position - elbow, wrist - elbow) * _fore.rotation;
            RollForearm(elbow, wrist);
            _hand.position = wrist;
        }

        /// <summary>
        /// Twist around the forearm axis so the model's palm faces like the HVR palm - the wrist follows the
        /// hand's rotation.
        /// </summary>
        /// <summary>
        /// The fist where the VR hand is (its middle at the hand's position), turned like the controller: fingers
        /// along the controller's forward, the thumb along its up.
        /// </summary>
        private void SolveFist(Vector3 handPosition, Transform body, Quaternion handRotation)
        {
            ResetStretch();

            var fingers = handRotation * Vector3.forward;
            var thumb = handRotation * Vector3.up;
            var rotation = Quaternion.LookRotation(fingers, thumb) * Quaternion.Inverse(_handAxesLocal);
            var scale = _hand.lossyScale.x;
            var wrist = handPosition - fingers * (_fistLength * scale * 0.5f);

            SolveElbow(wrist, body);
            RollForearmTo(_fore.position, wrist, rotation * _slotSideLocal);
            _hand.position = wrist;
            _hand.rotation = rotation;
        }

        /// <summary>
        /// Upper arm and forearm reach for the wrist (two-bone IK, see Solve).
        /// </summary>
        private void SolveElbow(Vector3 wrist, Transform body)
        {
            var upperLength = Vector3.Distance(_upper.position, _fore.position);
            var foreLength = Vector3.Distance(_fore.position, _hand.position);
            if (upperLength <= 0f || foreLength <= 0f)
                return;

            var shoulder = _upper.position;
            var toTarget = wrist - shoulder;
            var distance = Mathf.Max(toTarget.magnitude, 0.05f);
            var direction = toTarget / distance;
            Stretch(distance, ref upperLength, ref foreLength);

            var outward = _side == "L" ? -body.right : body.right;
            var hint = Vector3.down * 0.5f + outward * 0.35f - body.forward * 0.2f;
            var bendNormal = Vector3.Cross(direction, hint);
            if (bendNormal.sqrMagnitude < 0.0001f)
                bendNormal = Vector3.Cross(direction, Vector3.down);
            bendNormal.Normalize();

            var cosShoulder = (upperLength * upperLength + distance * distance - foreLength * foreLength) /
                              (2f * upperLength * distance);
            var shoulderAngle = Mathf.Acos(Mathf.Clamp(cosShoulder, -1f, 1f)) * Mathf.Rad2Deg;
            var elbow = shoulder + Quaternion.AngleAxis(shoulderAngle, bendNormal) * direction * upperLength;

            _upper.rotation = Quaternion.FromToRotation(_fore.position - shoulder, elbow - shoulder) *
                              _upper.rotation;
            _fore.position = elbow;
            _fore.rotation = Quaternion.FromToRotation(_hand.position - elbow, wrist - elbow) * _fore.rotation;
        }

        private void ResetStretch()
        {
            _fore.localPosition = _foreRestLocal;
            _hand.localPosition = _handRestLocal;
            _upper.localScale = _upperRestScale;
            _hand.localScale = _handRestScale;
        }

        /// <summary>
        /// The arm is too short for the distance: the whole arm scaled up (the hand scaled back, so the fist keeps
        /// its size) - or, for a zero-length arm, the bones moved apart (the skin between them stretches).
        /// </summary>
        private void Stretch(float distance, ref float upperLength, ref float foreLength)
        {
            var reach = (upperLength + foreLength) * 0.99f;
            var missing = distance - reach;
            if (missing <= 0f)
                return;

            if (reach > 0.0001f)
            {
                var factor = distance / reach;
                _upper.localScale = _upperRestScale * factor;
                _hand.localScale = _handRestScale / factor;
                upperLength *= factor;
                foreLength *= factor;
                return;
            }

            upperLength += missing * _upperStretchShare;
            foreLength += missing * (1f - _upperStretchShare);
        }

        private void RollForearm(Vector3 elbow, Vector3 wrist)
        {
            if (!_hasModelPalm || _hvrPalm == null)
                return;

            RollForearmTo(elbow, wrist, _hvrPalm.forward);
        }

        private void RollForearmTo(Vector3 elbow, Vector3 wrist, Vector3 wantedPalm)
        {
            if (!_hasModelPalm)
                return;

            var axis = wrist - elbow;
            if (axis.sqrMagnitude < 1e-6f)
                return;
            axis.Normalize();

            var current = Vector3.ProjectOnPlane(_fore.TransformDirection(_modelPalmLocal), axis);
            var wanted = Vector3.ProjectOnPlane(wantedPalm, axis);
            if (current.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
                return;

            var angle = Vector3.SignedAngle(current, wanted, axis);
            _fore.rotation = Quaternion.AngleAxis(angle, axis) * _fore.rotation;
        }
    }
}
