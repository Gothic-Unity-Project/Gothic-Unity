using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrHeroBody: the hero's own body (current armor) under the VR head - torso, legs and
    /// arms. The hands stay the VR hands: the model's hands and head are hidden (bones scaled to ~0), its arms reach
    /// for the VR hands with a two-bone IK (elbows down and out, VrHeroArmIk), the forearms roll with the hands. Legs
    /// and torso play the hero's Gothic animations (VRHeroBodyAnimator). Rebuilt when the armor changes
    /// (NpcVisualBodyChanged). No colliders. Not saved (rebuilt from the hero's visual).
    /// Backpack in a hand (BackpackInHandChanged): the sleeves are "rolled up" - the armor's forearms are cut off
    /// (VrHeroBodyMeshCutter) and the bare forearms of the hero's naked body mesh are skinned to the same skeleton
    /// instead, so the backpack's slots aren't hidden.
    /// Workers: VrHeroHands (the hero's fists as the VR hands, held items, arms on top), VrHeroDownPose (knocked out /
    /// dead / asleep body and camera, Accessibility option heroFallMode), VrHeroLocomotion (legs/torso animation, with
    /// VrHeroSwimPose for the water), VrHeroSleeves (rolled-up sleeves, arm meshes), VrHeroBodyMeshCutter (mesh
    /// cuts), VrHeroArmIk (arms). VRHeroBody places the body and orchestrates them. The crouch button lowers the
    /// camera to the eyes of its sneak pose (IVrRig.CrouchHeightOverride).
    /// </summary>
    public class VRHeroBody : MonoBehaviour
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VRTransformService _vrTransformService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly VRUiOnTopService _uiOnTopService;

        private const float _torsoBackOffset = 0.15f;
        private const float _hiddenBoneScale = 0.001f;
        private const string _headBoneName = "BIP01 HEAD";
        private const float _minScale = 0.5f;
        private const float _maxScale = 1.3f;

        private GameObject _body;
        private string _builtVisual;
        // The hero's visual changed (and at the start): LateUpdate (re)builds the body once his props are set.
        private bool _isVisualDirty = true;
        // Rest pose: head bone above the lowest foot bone (the root isn't at the feet without an animation system).
        private float _modelHeadHeight = 1.6f;
        private Transform _headBone;
        // The neck sits this far below the eyes (scaled with the body).
        private const float _neckBelowEyes = 0.12f;

        // The body turns with the head only beyond this angle (looking around/down doesn't twist the arms), smoothly.
        private const float _bodyTurnDeadzone = 35f;
        private const float _bodyTurnSpeed = 540f;
        private Vector3 _bodyForward = Vector3.forward;
        private bool _hasBodyForward;
        private IVrRig _rig;
        private readonly List<Renderer> _renderers = new();

        // Rolled-up sleeves (backpack in a hand), the arm meshes with/without the model's hands.
        private VrHeroSleeves _sleeves;
        // The last BackpackInHandChanged - a rebuilt body rolls its sleeves up too.
        private bool _isBackpackInHand;
        private readonly VrHeroBodyMeshCutter _meshCutter = new();

        private readonly VrHeroArmIk _leftArm = new("L");
        private readonly VrHeroArmIk _rightArm = new("R");

        // DeveloperConfig.EnableHeroHands + Immersion option "Hero's hands": the model's own hands (fists, with the
        // armor's gloves) are the VR hands - the backend's hand visuals are hidden meanwhile (VrHeroHands).
        private readonly List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> _handMeshes = new();
        // The arms drawn on top of the UI with the fists: the hand meshes' renderers and the bare forearms.
        private readonly List<SkinnedMeshRenderer> _armRenderers = new();
        private VrHeroHands _hands;

        // Legs/torso animation: the animator per built body, which animation it plays (VrHeroLocomotion).
        private VRHeroBodyAnimator _animator;
        private VrHeroLocomotion _locomotion;
        private float _standingScale;

        // Knocked out / dead / asleep body and camera (Accessibility option heroFallMode).
        private VrHeroDownPose _downPose;

        // The eye height of the sneak pose at scale 1 (S_SNEAK, measured once per built body), -1 = not yet.
        private float _sneakEyeHeight = -1f;
        // Feet and toes - the floor under the sneak pose (the arms can reach lower).
        private readonly List<Transform> _footBones = new();
        private const string _sneakAnimation = "S_SNEAK";

        private SkinnedMeshRenderer _bodyRenderer;

        /// <summary>
        /// The body's skinned mesh while it's shown - spell effects with shpType MESH emit from it (teleport
        /// silhouette). Found once when the body is built.
        /// </summary>
        public SkinnedMeshRenderer BodyRenderer =>
            _bodyRenderer != null && _bodyRenderer.enabled ? _bodyRenderer : null;

        private void Awake()
        {
            gameObject.Inject();
            _hands = new VrHeroHands().Inject();
            _hands.Initialize();
            _downPose = new VrHeroDownPose(_neckBelowEyes, SolveArms).Inject();
            _downPose.Initialize();
            _locomotion = new VrHeroLocomotion().Inject();
            _sleeves = new VrHeroSleeves(_meshCutter, _handMeshes).Inject();
            GlobalEventDispatcher.NpcVisualBodyChanged.AddListener(OnNpcVisualBodyChanged);
            GlobalEventDispatcher.BackpackInHandChanged.AddListener(OnBackpackInHandChanged);
        }

        private void OnEnable()
        {
            _downPose?.SetEnabled(true);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.NpcVisualBodyChanged.RemoveListener(OnNpcVisualBodyChanged);
            GlobalEventDispatcher.BackpackInHandChanged.RemoveListener(OnBackpackInHandChanged);
            SetHeroHandsShown(false);
            _downPose.Dispose();
            _hands.RestoreHeldItemAttachPoints();
            _meshCutter.DestroyCreatedMeshes();
            _hands.Dispose();
            if (_rig != null)
                _rig.CrouchHeightOverride = null;
        }

        private void OnDisable()
        {
            SetHeroHandsShown(false);
            _downPose.SetEnabled(false);
            _hands.RestoreHeldItemAttachPoints();
        }

        private void OnNpcVisualBodyChanged(NpcContainer npc)
        {
            if (npc != null && npc == _npcService.GetHeroContainer())
                _isVisualDirty = true;
        }

        private void OnBackpackInHandChanged(bool isInHand)
        {
            _isBackpackInHand = isInHand;
            _sleeves.SetRolledUp(isInHand, _hands.AreShown);
        }

        /// <summary>
        /// The model's hands in the body mesh or cut out, the backend's hand visuals hidden or shown.
        /// </summary>
        private void SetHeroHandsShown(bool isShown)
        {
            if (!_hands.SetShown(isShown, _rig))
                return;

            _sleeves.ApplyArmMeshes(_hands.AreShown);
            _leftArm.IsFist = isShown;
            _rightArm.IsFist = isShown;
        }

        /// <summary>
        /// The player's rig, again after it was destroyed (the cached one is a dead Unity object then).
        /// </summary>
        private IVrRig ResolveRig()
        {
            if (_rig is Object rigObject && rigObject == null)
                _rig = null;
            _rig ??= _contextInteractionService.GetCurrentPlayerController()?.GetComponent<IVrRig>();
            return _rig;
        }

        private void LateUpdate()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero?.Props == null)
                return;
            var rig = ResolveRig();
            var camera = rig?.Camera;
            if (camera == null)
                return;

            if (_isVisualDirty)
                EnsureBody(hero);
            if (_body == null)
                return;

            // Hidden while the hero is transformed (VRTransformService) - the transformation's puppet is the body then.
            var isVisible = !_vrTransformService.IsTransformed;
            foreach (var bodyRenderer in _renderers)
            {
                if (bodyRenderer != null && bodyRenderer.enabled != isVisible)
                    bodyRenderer.enabled = isVisible;
            }
            SetHeroHandsShown(isVisible && _hands.IsOptionOn && _handMeshes.Count > 0);
            _hands.SetArmsOnTop(isVisible && _hands.AreShown && _uiOnTopService != null &&
                                _uiOnTopService.AreHandsOnTop, _armRenderers);
            if (!isVisible)
            {
                _hands.UpdateHeldItemAttachPoints(false);
                return;
            }

            // Under the head, a bit behind it - looking down shows the chest, not the inside of the neck. Scaled to the
            // player's eye height above the floor, so the feet stay on the ground (sitting/standing mode, any height).
            var head = camera.transform;
            var forward = GetBodyForward(head);

            var feetY = rig.Root.position.y;
            if (_downPose.UpdateDown(head, feetY, _standingScale, rig))
                return;
            // Crouching (sneaking) lowers the head, not the body size - keep the standing scale then. The crouch
            // button's camera offset doesn't count: standing up the camera rises slowly, the body shrank and grew
            // with it.
            var isCrouching = rig.IsCrouching;
            if (!isCrouching || _standingScale <= _minScale)
            {
                var headHeight = head.position.y - feetY + Mathf.Max(0f, rig.StandingCameraHeight - rig.CameraHeight);
                _standingScale = Mathf.Clamp(headHeight / _modelHeadHeight, _minScale, _maxScale);
            }
            var scale = _standingScale;
            _body.transform.localScale = Vector3.one * scale;
            _body.transform.SetPositionAndRotation(
                new Vector3(head.position.x, feetY, head.position.z) - forward * (_torsoBackOffset * scale),
                Quaternion.LookRotation(forward));

            _locomotion.Update(hero, _body.transform, rig, scale, isCrouching);
            UpdateCrouchHeight(rig, scale);

            // The neck right under the goggles (and a bit behind them), whatever the model's root/bone layout or
            // animation pose is - sneaking leans the torso forward, it must not come into the view.
            if (_headBone != null)
            {
                var neck = head.position - Vector3.up * (_neckBelowEyes * scale) - forward * (_torsoBackOffset * scale);
                _body.transform.position += neck - _headBone.position;
            }

            SolveArms();
        }

        /// <summary>
        /// The crouch button takes the camera to the eyes of the sneak pose (S_SNEAK): lower, the neck under the
        /// goggles pushed the body's feet into the floor. Measured from the body's own root once the pose is fully
        /// blended in - whatever the camera does meanwhile. The first crouch of a body starts at the rig's own height.
        /// </summary>
        private void UpdateCrouchHeight(IVrRig rig, float scale)
        {
            if (_headBone == null || _animator == null || !_animator.IsValid)
                return;

            if (_sneakEyeHeight < 0f && _animator.IsPlaying(_sneakAnimation) && _footBones.Count > 0)
            {
                // Like _modelHeadHeight: the head above the lowest foot bone, in the body's own units (scale 1) - the
                // body's origin isn't at its feet.
                var bodyTransform = _body.transform;
                var lowestFoot = float.MaxValue;
                foreach (var foot in _footBones)
                    lowestFoot = Mathf.Min(lowestFoot, bodyTransform.InverseTransformPoint(foot.position).y);
                var headHeight = bodyTransform.InverseTransformPoint(_headBone.position).y - lowestFoot;
                _sneakEyeHeight = headHeight + _neckBelowEyes;
                Logger.Log($"[VRHeroBody] Sneak pose: eyes {_sneakEyeHeight:F2} m above the feet (scale 1) - the " +
                           "crouch button's camera height.", LogCat.VR);
            }
            if (_sneakEyeHeight < 0f)
                return;

            var height = _sneakEyeHeight * scale;
            if (!rig.CrouchHeightOverride.HasValue || Mathf.Abs(rig.CrouchHeightOverride.Value - height) > 0.01f)
                rig.CrouchHeightOverride = height;
        }

        private void SolveArms()
        {
            GetSecondHandGripPoses(out var leftGrip, out var rightGrip);
            _leftArm.Solve(_vrPlayerService.GetHandModelGo(VrHandSide.Left), _body.transform, _rig?.LeftHand,
                leftGrip);
            _rightArm.Solve(_vrPlayerService.GetHandModelGo(VrHandSide.Right), _body.transform, _rig?.RightHand,
                rightGrip);
            _hands.UpdateHeldItemAttachPoints(_hands.AreShown);
        }

        /// <summary>
        /// The second hand on an item held with both hands is shown on its grip right before rendering (XRI physical
        /// hands) - after this body is skinned. The fist goes there now, not where the controller is. Both sides in
        /// one pass (the first hand of a side with a grip pose).
        /// </summary>
        private void GetSecondHandGripPoses(out Pose? left, out Pose? right)
        {
            left = null;
            right = null;
            var hands = _vrPlayerService.VRContextInteractionService?.PhysicsHands;
            if (hands == null)
                return;

            for (var i = 0; i < hands.Count; i++)
            {
                var hand = hands[i];
                if (hand == null)
                    continue;
                var isLeft = hand.Side == VrHandSide.Left;
                if ((isLeft ? left : right).HasValue ||
                    !hand.TryGetSecondHandGripPose(out var position, out var rotation))
                    continue;
                if (isLeft)
                    left = new Pose(position, rotation);
                else
                    right = new Pose(position, rotation);
            }
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

        /// <summary>
        /// (Re)builds the body when the hero's visual (armor) changed (_isVisualDirty: NpcVisualBodyChanged).
        /// </summary>
        private void EnsureBody(NpcContainer hero)
        {
            var props = hero.Props;
            if (string.IsNullOrEmpty(props.MdmName) || string.IsNullOrEmpty(props.BodyData.Body))
                return; // not set yet - stays dirty
            _isVisualDirty = false;

            var visualKey = $"{props.MdmName}|{props.BodyData.Body}|{props.BodyData.BodyTexNr}|" +
                            $"{props.BodyData.BodyTexColor}|{props.BodyData.Armor}";
            if (visualKey == _builtVisual && _body != null)
                return;

            SetHeroHandsShown(false);
            _hands.SetArmsOnTop(false, _armRenderers);
            if (_body != null)
                Destroy(_body);
            _meshCutter.DestroyCreatedMeshes();
            _handMeshes.Clear();
            _armRenderers.Clear();
            _renderers.Clear();
            _bodyRenderer = null;
            _sneakEyeHeight = -1f;
            _footBones.Clear();
            _sleeves.Clear();

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
                _body = null;
                _builtVisual = visualKey; // don't retry (only on the next visual change)
                _downPose.Bind(null, null, null, null, null);
                return;
            }

            _builtVisual = visualKey;
            _body ??= root;
            _body.transform.SetParent(transform, false);

            foreach (var collider in _body.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            _renderers.AddRange(_body.GetComponentsInChildren<Renderer>(true));
            foreach (var r in _renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (_bodyRenderer == null && r is SkinnedMeshRenderer skinnedRenderer)
                    _bodyRenderer = skinnedRenderer;
            }

            var bones = new Dictionary<string, Transform>();
            foreach (var bone in _body.GetComponentsInChildren<Transform>(true))
                bones.TryAdd(bone.name.ToUpperInvariant(), bone);

            // The VR hands replace the model's hands: cut out of the mesh. Collapsing the hand bone pulled the wrist
            // ring into one point - the forearm became a spike.
            foreach (var skinned in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var withHands = skinned.sharedMesh;
                var withoutHands = _meshCutter.CutArms(withHands, skinned.bones, VrHeroArmCut.WithoutHands);
                if (withoutHands == null)
                    continue;
                skinned.sharedMesh = withoutHands;
                // DeveloperConfig.EnableHeroHands: the full mesh shows the model's hands as the VR hands.
                if (withoutHands.triangles.Length != withHands.triangles.Length)
                    _handMeshes.Add((skinned, withHands, withoutHands));
            }

            foreach (var (boneName, bone) in bones)
            {
                if (boneName.Contains(" FOOT") || boneName.Contains(" TOE"))
                    _footBones.Add(bone);
            }

            // Height of the model in its rest pose: head bone above its lowest foot bone.
            if (bones.TryGetValue(_headBoneName, out _headBone))
            {
                var lowestY = _headBone.position.y;
                foreach (var bone in bones.Values)
                    lowestY = Mathf.Min(lowestY, bone.position.y);
                _modelHeadHeight = Mathf.Max(0.5f, _headBone.position.y - lowestY + _neckBelowEyes);
                _headBone.localScale = Vector3.one * _hiddenBoneScale;
            }

            _leftArm.Bind(bones, _handMeshes);
            _rightArm.Bind(bones, _handMeshes);
            _hands.SetPalmSlots(bones);
            _sleeves.Build(_body, props, mdhName, bones);
            foreach (var (renderer, _, _) in _handMeshes)
                _armRenderers.Add(renderer);
            _armRenderers.AddRange(_sleeves.BareForearms);

            _animator = new VRHeroBodyAnimator(_animationService, props.MdsNameBase, props.MdsNameOverlay, bones);
            _locomotion.Bind(_animator);
            _downPose.Bind(_body, _animator, _headBone, _leftArm, _rightArm);
            _sleeves.SetRolledUp(_isBackpackInHand, _hands.AreShown);
            Logger.Log($"[VRHeroBody] Built {props.MdmName} (head height {_modelHeadHeight:F2} m, " +
                       $"animated: {_animator.IsValid}, bare forearms: {_sleeves.BareForearms.Count}).", LogCat.VR);
        }
    }
}
