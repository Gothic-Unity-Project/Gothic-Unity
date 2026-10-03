#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Services;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrHeroBody): the hero's own body (current armor) under the VR head - torso, legs and
    /// arms. The hands stay the HVR hands: the model's hands and head are hidden (bones scaled to ~0), its arms reach
    /// for the HVR hands with a two-bone IK (elbows down and out). Rebuilt when the armor changes, hidden while the hero
    /// is transformed (VRTransformService). No animations, no colliders. Not saved (rebuilt from the hero's visual).
    /// Backpack in a hand: the sleeves are "rolled up" - the armor's forearms shrink to the elbow and a naked body copy
    /// (same pose, inside the armor) shows its bare forearms, so the backpack's slots aren't hidden by the sleeves.
    /// </summary>
    public class VRHeroBody : MonoBehaviour
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VRTransformService _vrTransformService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private const float _torsoBackOffset = 0.15f;
        private const float _hiddenBoneScale = 0.001f;
        private const float _visualCheckInterval = 1f;
        private const string _headBoneName = "BIP01 HEAD";
        private const float _minScale = 0.5f;
        private const float _maxScale = 1.3f;

        private GameObject _body;
        private string _builtVisual;
        private float _nextVisualCheck;
        // Rest pose: head bone above the lowest foot bone (the root isn't at the feet without an animation system).
        private float _modelHeadHeight = 1.6f;
        private Transform _headBone;
        // The neck sits this far below the eyes (scaled with the body).
        private const float _neckBelowEyes = 0.12f;

        // The body turns with the head only beyond this angle (looking around/down doesn't twist the arms), smoothly.
        private const float _bodyTurnDeadzone = 50f;
        private const float _bodyTurnSpeed = 360f;
        private Vector3 _bodyForward = Vector3.forward;
        private bool _hasBodyForward;
        // The backpack's slots are in front of the chest - the body would hide them.
        private HurricaneVR.Framework.Core.HVRGrabbable _backpack;
        private VRPlayerController _playerController;
        private readonly List<Renderer> _renderers = new();

        // Rolled-up sleeves: naked body copy following the armor skeleton (bone pairs armor -> naked).
        private GameObject _nakedBody;
        private readonly List<Renderer> _nakedRenderers = new();
        private readonly List<(Transform armor, Transform naked)> _nakedBones = new();
        private readonly ArmIk _leftArm = new("L");
        private readonly ArmIk _rightArm = new("R");


        private void Awake()
        {
            gameObject.Inject();
        }

        private void LateUpdate()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Props == null || Camera.main == null)
                return;

            if (Time.time >= _nextVisualCheck)
            {
                _nextVisualCheck = Time.time + _visualCheckInterval;
                EnsureBody(hero);
            }
            if (_body == null)
                return;

            var isVisible = !_vrTransformService.IsTransformed;
            var isSleeveRolledUp = isVisible && _nakedBody != null && IsBackpackInHand();
            SetRenderersEnabled(_renderers, isVisible);
            SetRenderersEnabled(_nakedRenderers, isSleeveRolledUp);
            if (!isVisible)
                return;

            // Under the head, a bit behind it - looking down shows the chest, not the inside of the neck. Scaled to the
            // player's eye height above the floor, so the feet stay on the ground (sitting/standing mode, any height).
            var head = Camera.main.transform;
            var forward = GetBodyForward(head);

            _playerController ??= _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
            var feetY = _playerController != null ? _playerController.transform.position.y : head.position.y - _modelHeadHeight;
            var scale = Mathf.Clamp((head.position.y - feetY) / _modelHeadHeight, _minScale, _maxScale);
            _body.transform.localScale = Vector3.one * scale;
            _body.transform.SetPositionAndRotation(
                new Vector3(head.position.x, feetY, head.position.z) - forward * (_torsoBackOffset * scale),
                Quaternion.LookRotation(forward));

            // The neck right under the goggles, whatever the model's root/bone layout is.
            if (_headBone != null)
                _body.transform.position += Vector3.up *
                                            (head.position.y - _neckBelowEyes * scale - _headBone.position.y);

            _leftArm.Solve(_vrPlayerService.GetHandModelGo(HVRHandSide.Left), _body.transform);
            _rightArm.Solve(_vrPlayerService.GetHandModelGo(HVRHandSide.Right), _body.transform);

            if (isSleeveRolledUp)
                RollUpSleeves();
        }

        private static void SetRenderersEnabled(List<Renderer> renderers, bool isEnabled)
        {
            foreach (var r in renderers)
            {
                if (r != null && r.enabled != isEnabled)
                    r.enabled = isEnabled;
            }
        }

        /// <summary>
        /// The naked copy takes the armor's pose (after the IK), then the armor's forearms collapse to the elbows - only
        /// the bare forearms of the naked body stick out of the armor. Reset by the next ArmIk.Solve.
        /// </summary>
        private void RollUpSleeves()
        {
            _nakedBody.transform.SetPositionAndRotation(_body.transform.position, _body.transform.rotation);
            _nakedBody.transform.localScale = _body.transform.localScale;
            foreach (var (armor, naked) in _nakedBones)
            {
                armor.GetLocalPositionAndRotation(out var position, out var rotation);
                naked.SetLocalPositionAndRotation(position, rotation);
                naked.localScale = armor.localScale;
            }

            _leftArm.CollapseForearm();
            _rightArm.CollapseForearm();
        }

        /// <summary>
        /// Yaw of the head, but stable: looking (almost) straight down/up, the head's forward has no horizontal part -
        /// its up vector points where the body faces then. The body only follows beyond a deadzone.
        /// </summary>
        private Vector3 GetBodyForward(Transform head)
        {
            var lookForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var upForward = Vector3.ProjectOnPlane(head.forward.y < 0f ? head.up : -head.up, Vector3.up);
            var headForward = Vector3.Lerp(upForward, lookForward, Mathf.Clamp01(lookForward.magnitude * 2f));
            if (headForward.sqrMagnitude < 0.0001f)
                return _bodyForward;
            headForward.Normalize();

            if (!_hasBodyForward)
            {
                _bodyForward = headForward;
                _hasBodyForward = true;
            }

            var angle = Vector3.SignedAngle(_bodyForward, headForward, Vector3.up);
            if (Mathf.Abs(angle) > _bodyTurnDeadzone)
            {
                var step = Mathf.Min(Mathf.Abs(angle) - _bodyTurnDeadzone + 1f, _bodyTurnSpeed * Time.deltaTime);
                _bodyForward = Quaternion.AngleAxis(Mathf.Sign(angle) * step, Vector3.up) * _bodyForward;
            }
            return _bodyForward;
        }

        private bool IsBackpackInHand()
        {
            if (_backpack == null)
            {
                var backpack = FindFirstObjectByType<VRBackpack>();
                if (backpack == null)
                    return false;
                _backpack = backpack.GetComponentInParent<HurricaneVR.Framework.Core.HVRGrabbable>() ??
                            backpack.GetComponentInChildren<HurricaneVR.Framework.Core.HVRGrabbable>();
                if (_backpack == null)
                    return false;
            }
            return _backpack.IsHandGrabbed;
        }

        /// <summary>
        /// (Re)builds the body when the hero's visual (armor) changed.
        /// </summary>
        private void EnsureBody(NpcContainer hero)
        {
            var props = hero.Props;
            if (string.IsNullOrEmpty(props.MdmName) || string.IsNullOrEmpty(props.BodyData.Body))
                return;

            var visualKey = $"{props.MdmName}|{props.BodyData.Body}|{props.BodyData.BodyTexNr}|" +
                            $"{props.BodyData.BodyTexColor}|{props.BodyData.Armor}";
            if (visualKey == _builtVisual && _body != null)
                return;

            if (_body != null)
                Destroy(_body);
            if (_nakedBody != null)
                Destroy(_nakedBody);
            _renderers.Clear();
            _nakedRenderers.Clear();
            _nakedBones.Clear();

            var mdhName = string.IsNullOrEmpty(props.MdhNameOverlay) ? props.MdhNameBase : props.MdhNameOverlay;
            var root = new GameObject("_VRHeroBody");
            try
            {
                _body = _meshService.CreateNpc(root.name, props.MdmName, mdhName, props.BodyData, root: root);
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[VRHeroBody] Can't build the hero body ({props.MdmName}): {e.Message}", LogCat.VR);
                Destroy(root);
                _builtVisual = visualKey; // don't retry every second
                return;
            }

            _builtVisual = visualKey;
            _body ??= root;
            _body.transform.SetParent(transform, false);

            foreach (var collider in _body.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            _renderers.AddRange(_body.GetComponentsInChildren<Renderer>(true));
            foreach (var r in _renderers)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var bones = new Dictionary<string, Transform>();
            foreach (var bone in _body.GetComponentsInChildren<Transform>(true))
                bones.TryAdd(bone.name.ToUpperInvariant(), bone);

            // Height of the model in its rest pose: head bone above its lowest foot bone.
            if (bones.TryGetValue(_headBoneName, out _headBone))
            {
                var lowestY = _headBone.position.y;
                foreach (var bone in bones.Values)
                    lowestY = Mathf.Min(lowestY, bone.position.y);
                _modelHeadHeight = Mathf.Max(0.5f, _headBone.position.y - lowestY + _neckBelowEyes);
                _headBone.localScale = Vector3.one * _hiddenBoneScale;
            }

            _leftArm.Bind(bones);
            _rightArm.Bind(bones);
            BuildNakedBody(props, mdhName, bones);
            Logger.Log($"[VRHeroBody] Built {props.MdmName} (head height {_modelHeadHeight:F2} m).", LogCat.VR);
        }

        /// <summary>
        /// The same hero without armor (body mesh of Mdl_SetVisualBody), hidden until the sleeves are rolled up.
        /// </summary>
        private void BuildNakedBody(Gothic.Core.Adapters.Properties.NpcProperties props, string mdhName,
            Dictionary<string, Transform> armorBones)
        {
            if (props.MdmName.EqualsIgnoreCase(props.BodyData.Body))
                return; // no armor - nothing to roll up

            var root = new GameObject("_VRHeroBodyNaked");
            try
            {
                _nakedBody = _meshService.CreateNpc(root.name, props.BodyData.Body, mdhName, props.BodyData, root: root);
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[VRHeroBody] Can't build the naked body ({props.BodyData.Body}): {e.Message}", LogCat.VR);
                Destroy(root);
                return;
            }

            _nakedBody ??= root;
            _nakedBody.transform.SetParent(transform, false);
            foreach (var collider in _nakedBody.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            _nakedRenderers.AddRange(_nakedBody.GetComponentsInChildren<Renderer>(true));
            foreach (var r in _nakedRenderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false;
            }

            foreach (var naked in _nakedBody.GetComponentsInChildren<Transform>(true))
            {
                if (naked != _nakedBody.transform && armorBones.TryGetValue(naked.name.ToUpperInvariant(), out var armor))
                    _nakedBones.Add((armor, naked));
            }
        }

        /// <summary>
        /// Analytic two-bone IK: upper arm + forearm reach for the HVR hand, the elbow bends towards a hint below and to
        /// the outside. The wrist always ends exactly at the HVR hand (the model's hand is hidden): out of reach the arm
        /// is stretched, tapered - 40 % of the missing length in the upper arm, 60 % in the forearm, so the most
        /// stretch sits near the wrist (skinned vertices between the moved joints stretch, the shoulder stays).
        /// </summary>
        private class ArmIk
        {
            private const float _upperStretchShare = 0.4f;

            private readonly string _side;
            private Transform _upper;
            private Transform _fore;
            private Transform _hand;
            private Vector3 _foreRestLocal;
            private Vector3 _handRestLocal;

            public ArmIk(string side)
            {
                _side = side;
            }

            public void Bind(Dictionary<string, Transform> bones)
            {
                bones.TryGetValue($"BIP01 {_side} UPPERARM", out _upper);
                bones.TryGetValue($"BIP01 {_side} FOREARM", out _fore);
                bones.TryGetValue($"BIP01 {_side} HAND", out _hand);
                if (_upper == null || _fore == null || _hand == null)
                    return;

                _foreRestLocal = _fore.localPosition;
                _handRestLocal = _hand.localPosition;
                _hand.localScale = Vector3.one * _hiddenBoneScale;
            }

            /// <summary>
            /// Rolled-up sleeve: the forearm's vertices shrink into the elbow (undone by the next Solve).
            /// </summary>
            public void CollapseForearm()
            {
                if (_fore != null)
                    _fore.localScale = Vector3.one * _hiddenBoneScale;
            }

            public void Solve(GameObject target, Transform body)
            {
                if (target == null || _upper == null || _fore == null || _hand == null)
                    return;

                // Last frame's stretch / rolled-up sleeve must not add up.
                _fore.localScale = Vector3.one;
                _fore.localPosition = _foreRestLocal;
                _hand.localPosition = _handRestLocal;

                // The body is scaled to the player's height - lengths from the current pose.
                var upperLength = Vector3.Distance(_upper.position, _fore.position);
                var foreLength = Vector3.Distance(_fore.position, _hand.position);
                if (upperLength <= 0f || foreLength <= 0f)
                    return;

                var shoulder = _upper.position;
                var wrist = target.transform.position;
                var toTarget = wrist - shoulder;
                var distance = Mathf.Max(toTarget.magnitude, 0.05f);
                var direction = toTarget / distance;

                var missing = distance - (upperLength + foreLength) * 0.99f;
                if (missing > 0f)
                {
                    upperLength += missing * _upperStretchShare;
                    foreLength += missing * (1f - _upperStretchShare);
                }

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

                _upper.rotation = Quaternion.FromToRotation(_fore.position - shoulder, elbow - shoulder) * _upper.rotation;
                _fore.position = elbow;
                _fore.rotation = Quaternion.FromToRotation(_hand.position - elbow, wrist - elbow) * _fore.rotation;
                _hand.position = wrist;
            }
        }
    }
}
#endif
