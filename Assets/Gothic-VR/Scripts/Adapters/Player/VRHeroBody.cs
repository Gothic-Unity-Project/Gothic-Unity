#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
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
    /// for the HVR hands with a two-bone IK (elbows down and out), the forearms roll with the hands. Legs and torso play
    /// the hero's Gothic animations (VRHeroBodyAnimator). Rebuilt when the armor changes, hidden while the hero is
    /// transformed (VRTransformService). No colliders. Not saved (rebuilt from the hero's visual).
    /// Backpack in a hand: the sleeves are "rolled up" - the armor's forearms are cut off and the bare forearms of the
    /// hero's naked body mesh are skinned to the same skeleton instead, so the backpack's slots aren't hidden.
    /// </summary>
    public class VRHeroBody : MonoBehaviour
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AnimationService _animationService;
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
        private const float _bodyTurnDeadzone = 35f;
        private const float _bodyTurnSpeed = 540f;
        private Vector3 _bodyForward = Vector3.forward;
        private bool _hasBodyForward;
        private HurricaneVR.Framework.Core.HVRGrabbable _backpack;
        private VRPlayerController _playerController;
        private readonly List<Renderer> _renderers = new();

        // Rolled-up sleeves: armor meshes without forearms + bare forearms (naked body mesh) on the armor skeleton.
        private readonly List<(SkinnedMeshRenderer renderer, Mesh full, Mesh sleeveless)> _sleeveMeshes = new();
        private readonly List<SkinnedMeshRenderer> _bareForearms = new();
        private readonly List<Mesh> _createdMeshes = new();
        private bool _areSleevesRolledUp;
        // Triangles weighted on average more than this to a part (hand, forearm) belong to it (see CutArms).
        private const float _armCutWeight = 0.5f;
        private const float _bareForearmWeight = 0.3f;
        // Armor triangles reaching this far into the forearm stay (a short overlap with the bare forearm).
        private const float _sleeveKeepWeight = 0.75f;

        private readonly ArmIk _leftArm = new("L");
        private readonly ArmIk _rightArm = new("R");

        // Legs/torso animation (V2).
        private VRHeroBodyAnimator _animator;
        private float _lastBodyYaw;
        private float _bodyYawSpeed;
        private float _standingScale;
        private const float _swimCrawlFullSpeed = 0.8f;
        private const float _swimCrawlMaxWeight = 0.8f;
        private const float _idleSpeed = 0.15f;
        private const float _runSpeed = 1.6f;
        private const float _turnAnimationSpeed = 60f; // degrees per second
        private const float _backwardsShare = 0.5f;
        private const string _idleAnimation = "S_RUN";
        private const string _walkAnimation = "S_WALKL";
        private const string _runAnimation = "S_RUNL";
        private const string _walkBackAnimation = "S_WALKBL";
        private const string _fallAnimation = "S_FALLDN";


        /// <summary>
        /// The body's skinned mesh - spell effects with shpType MESH emit from it (teleport silhouette).
        /// </summary>
        public SkinnedMeshRenderer BodyRenderer
        {
            get
            {
                foreach (var r in _renderers)
                {
                    if (r is SkinnedMeshRenderer skinned && r.enabled)
                        return skinned;
                }
                return null;
            }
        }

        private void Awake()
        {
            gameObject.Inject();
        }

        private void OnDestroy()
        {
            DestroyCreatedMeshes();
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
            foreach (var r in _renderers)
            {
                if (r != null && r.enabled != isVisible)
                    r.enabled = isVisible;
            }
            SetSleevesRolledUp(isVisible && IsBackpackInHand());
            if (!isVisible)
                return;

            // Under the head, a bit behind it - looking down shows the chest, not the inside of the neck. Scaled to the
            // player's eye height above the floor, so the feet stay on the ground (sitting/standing mode, any height).
            var head = Camera.main.transform;
            var forward = GetBodyForward(head);

            _playerController ??= _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
            var feetY = _playerController != null ? _playerController.transform.position.y : head.position.y - _modelHeadHeight;
            // Crouching (sneaking) lowers the head, not the body size - keep the standing scale then.
            var isCrouching = _playerController != null && _playerController.IsCrouching;
            if (!isCrouching || _standingScale <= 0f)
                _standingScale = Mathf.Clamp((head.position.y - feetY) / _modelHeadHeight, _minScale, _maxScale);
            var scale = _standingScale;
            _body.transform.localScale = Vector3.one * scale;
            _body.transform.SetPositionAndRotation(
                new Vector3(head.position.x, feetY, head.position.z) - forward * (_torsoBackOffset * scale),
                Quaternion.LookRotation(forward));

            UpdateAnimation(scale, isCrouching);

            // The neck right under the goggles (and a bit behind them), whatever the model's root/bone layout or animation
            // pose is - sneaking leans the torso forward, it must not come into the view.
            if (_headBone != null)
            {
                var neck = head.position - Vector3.up * (_neckBelowEyes * scale) - forward * (_torsoBackOffset * scale);
                _body.transform.position += neck - _headBone.position;
            }

            _leftArm.Solve(_vrPlayerService.GetHandModelGo(HVRHandSide.Left), _body.transform);
            _rightArm.Solve(_vrPlayerService.GetHandModelGo(HVRHandSide.Right), _body.transform);
        }

        /// <summary>
        /// Legs and torso like the hero in Gothic: idle, walk/run (played at the real speed - no sliding feet), walking
        /// backwards, strafing, turning on the spot, falling.
        /// </summary>
        private void UpdateAnimation(float scale, bool isCrouching)
        {
            if (_animator == null || !_animator.IsValid)
                return;
            _animator.OverlayOverride = _playerController != null ? _playerController.SpeedOverlay : null;

            var yaw = _body.transform.eulerAngles.y;
            if (Time.deltaTime > 0f)
                _bodyYawSpeed = Mathf.Lerp(_bodyYawSpeed, Mathf.DeltaAngle(_lastBodyYaw, yaw) / Time.deltaTime, 0.3f);
            _lastBodyYaw = yaw;

            var velocity = _playerController != null ? _playerController.CharacterController.velocity : Vector3.zero;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var speed = horizontal.magnitude;
            var local = _body.transform.InverseTransformDirection(horizontal);

            var bodyState = _npcService.GetHeroContainer()?.Props.BodyState;
            var isMoving = speed >= _idleSpeed;
            var isBackwards = local.z < -_backwardsShare * speed;

            string animation;
            // Swimming: floating upright (s_Swim), the crawl (s_SwimF) only blended in with the speed - slowly, so the
            // legs come up towards the surface instead of jumping behind the camera.
            var swimCrawl = bodyState == VmGothicEnums.BodyState.BsSwim && isMoving && !isBackwards
                ? Mathf.Clamp01(speed / _swimCrawlFullSpeed) * _swimCrawlMaxWeight
                : 0f;
            _animator.SetOverlay("S_SWIMF", swimCrawl);

            if (bodyState == VmGothicEnums.BodyState.BsSwim)
                animation = isMoving && isBackwards ? "S_SWIMB" : "S_SWIM";
            else if (bodyState == VmGothicEnums.BodyState.BsDive)
                animation = isMoving ? "S_DIVEF" : "S_DIVE";
            else if (_playerController != null && !_playerController.IsGrounded && velocity.y < -1f &&
                _animator.HasAnimation(_fallAnimation))
                animation = _fallAnimation;
            else if (isCrouching)
                animation = !isMoving ? "S_SNEAK" : isBackwards ? "S_SNEAKBL" : "S_SNEAKL";
            else if (speed < _idleSpeed)
                animation = Mathf.Abs(_bodyYawSpeed) >= _turnAnimationSpeed
                    ? (_bodyYawSpeed < 0f ? "T_RUNTURNL" : "T_RUNTURNR")
                    : _idleAnimation;
            else if (isBackwards)
                animation = _animator.HasAnimation(_walkBackAnimation)
                    ? _walkBackAnimation
                    : _animationService.GetReversedAnimationName(_walkAnimation, _animator.MdsBase, _animator.MdsOverlay);
            else if (Mathf.Abs(local.x) > Mathf.Abs(local.z))
                animation = speed >= _runSpeed
                    ? (local.x < 0f ? "T_RUNSTRAFEL" : "T_RUNSTRAFER")
                    : (local.x < 0f ? "T_WALKSTRAFEL" : "T_WALKSTRAFER");
            else
                animation = speed >= _runSpeed ? _runAnimation : _walkAnimation;

            if (!_animator.HasAnimation(animation))
            {
                // e.g. no sneaking backwards: the forward loop played back to front.
                var forwardLoop = animation != null && animation.EndsWith("BL")
                    ? animation.Substring(0, animation.Length - 2) + "L"
                    : null;
                animation = forwardLoop != null && _animator.HasAnimation(forwardLoop)
                    ? _animationService.GetReversedAnimationName(forwardLoop, _animator.MdsBase, _animator.MdsOverlay)
                    : null;
                animation ??= speed < _idleSpeed ? _idleAnimation : _walkAnimation;
            }

            // Walk/run loops at the real speed, so the feet don't slide.
            var animationSpeed = _animator.GetMovementSpeed(animation) * scale;
            var playbackSpeed = animationSpeed > 0.1f && speed >= _idleSpeed
                ? Mathf.Clamp(speed / animationSpeed, 0.5f, 2f)
                : 1f;
            // Gothic has no strafe/turn loops, only single steps (t_*) - repeated while moving sideways/turning.
            var isStep = animation.StartsWith("T_", System.StringComparison.OrdinalIgnoreCase);
            _animator.Play(animation, playbackSpeed, isStep);
            _animator.Update(Time.deltaTime);
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

        private void SetSleevesRolledUp(bool isRolledUp)
        {
            isRolledUp &= _bareForearms.Count > 0;
            if (isRolledUp == _areSleevesRolledUp)
                return;

            _areSleevesRolledUp = isRolledUp;
            foreach (var (renderer, full, sleeveless) in _sleeveMeshes)
            {
                if (renderer != null)
                    renderer.sharedMesh = isRolledUp ? sleeveless : full;
            }
            foreach (var forearm in _bareForearms)
            {
                if (forearm != null)
                    forearm.enabled = isRolledUp;
            }
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
            DestroyCreatedMeshes();
            _renderers.Clear();
            _sleeveMeshes.Clear();
            _bareForearms.Clear();
            _areSleevesRolledUp = false;

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

            // The HVR hands replace the model's hands: cut out of the mesh. Collapsing the hand bone pulled the wrist
            // ring into one point - the forearm became a spike.
            foreach (var skinned in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var withoutHands = CutArms(skinned.sharedMesh, skinned.bones, ArmCut.WithoutHands);
                if (withoutHands != null)
                    skinned.sharedMesh = withoutHands;
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

            _leftArm.Bind(bones);
            _rightArm.Bind(bones);
            BuildSleeves(props, mdhName, bones);

            _animator = new VRHeroBodyAnimator(_animationService, props.MdsNameBase, props.MdsNameOverlay, bones);
            Logger.Log($"[VRHeroBody] Built {props.MdmName} (head height {_modelHeadHeight:F2} m, " +
                       $"animated: {_animator.IsValid}, bare forearms: {_bareForearms.Count}).", LogCat.VR);
        }

        /// <summary>
        /// Sleeveless copies of the armor meshes and the forearms of the naked body (Mdl_SetVisualBody), skinned to the
        /// armor's skeleton - they stretch and roll with the IK like the armor did.
        /// </summary>
        private void BuildSleeves(Gothic.Core.Adapters.Properties.NpcProperties props, string mdhName,
            Dictionary<string, Transform> armorBones)
        {
            if (props.MdmName.EqualsIgnoreCase(props.BodyData.Body))
                return; // no armor - nothing to roll up

            var nakedRoot = new GameObject("_VRHeroBodyNaked");
            GameObject naked;
            try
            {
                naked = _meshService.CreateNpc(nakedRoot.name, props.BodyData.Body, mdhName, props.BodyData,
                    root: nakedRoot) ?? nakedRoot;
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[VRHeroBody] Can't build the naked body ({props.BodyData.Body}): {e.Message}", LogCat.VR);
                Destroy(nakedRoot);
                return;
            }

            foreach (var nakedRenderer in naked.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var forearmMesh = CutArms(nakedRenderer.sharedMesh, nakedRenderer.bones, ArmCut.Forearms);
                if (forearmMesh == null)
                    continue;

                // Same place relative to the root as in the naked body (the bind poses are relative to it).
                var relative = naked.transform.worldToLocalMatrix * nakedRenderer.transform.localToWorldMatrix;
                var forearmGo = new GameObject("_BareForearms");
                forearmGo.transform.SetParent(_body.transform, false);
                forearmGo.transform.SetLocalPositionAndRotation(relative.GetPosition(), relative.rotation);
                forearmGo.transform.localScale = relative.lossyScale;

                var bones = new Transform[nakedRenderer.bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    var nakedBone = nakedRenderer.bones[i];
                    if (nakedBone != null)
                        armorBones.TryGetValue(nakedBone.name.ToUpperInvariant(), out bones[i]);
                }

                var forearm = forearmGo.AddComponent<SkinnedMeshRenderer>();
                forearm.sharedMesh = forearmMesh;
                forearm.sharedMaterials = nakedRenderer.sharedMaterials;
                forearm.bones = bones;
                if (nakedRenderer.rootBone != null &&
                    armorBones.TryGetValue(nakedRenderer.rootBone.name.ToUpperInvariant(), out var rootBone))
                    forearm.rootBone = rootBone;
                forearm.updateWhenOffscreen = true;
                forearm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                forearm.enabled = false;
                _bareForearms.Add(forearm);
            }
            Destroy(nakedRoot);

            if (_bareForearms.Count == 0)
                return;

            foreach (var armorRenderer in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (_bareForearms.Contains(armorRenderer))
                    continue;
                var sleeveless = CutArms(armorRenderer.sharedMesh, armorRenderer.bones, ArmCut.Sleeveless);
                if (sleeveless != null)
                    _sleeveMeshes.Add((armorRenderer, armorRenderer.sharedMesh, sleeveless));
            }
        }

        private enum ArmCut
        {
            // The whole body without the model's hands (the HVR hands are shown).
            WithoutHands,
            // Only the forearms (naked body: rolled-up sleeves).
            Forearms,
            // Everything but forearms and hands (armor with rolled-up sleeves).
            Sleeveless
        }

        /// <summary>
        /// Copy of the mesh cut by the bone weights of its triangles (average of the 3 vertices, 50 % limit - the bare
        /// forearms and the sleeveless armor meet without gap or overlap). Null if nothing is left or the mesh can't be
        /// read.
        /// </summary>
        private Mesh CutArms(Mesh mesh, Transform[] bones, ArmCut cut)
        {
            if (mesh == null || !mesh.isReadable || bones == null)
                return null;

            var isForearmBone = new bool[bones.Length];
            var isHandBone = new bool[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                var name = bones[i] != null ? bones[i].name.ToUpperInvariant() : string.Empty;
                isForearmBone[i] = name.Contains("FOREARM");
                isHandBone[i] = name.Contains(" HAND") || name.Contains("FINGER");
            }

            var weights = mesh.boneWeights;
            if (weights.Length != mesh.vertexCount)
                return null;

            float WeightOf(int vertex, bool[] isBone)
            {
                var w = weights[vertex];
                return Of(w.boneIndex0, w.weight0) + Of(w.boneIndex1, w.weight1) +
                       Of(w.boneIndex2, w.weight2) + Of(w.boneIndex3, w.weight3);

                float Of(int boneIndex, float weight) =>
                    boneIndex >= 0 && boneIndex < isBone.Length && isBone[boneIndex] ? weight : 0f;
            }

            float SleeveWeight(int vertex) => WeightOf(vertex, isForearmBone) + WeightOf(vertex, isHandBone);

            var copy = Instantiate(mesh);
            copy.name = $"{mesh.name}_{cut}";
            var keptTriangles = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var hand = (WeightOf(triangles[i], isHandBone) + WeightOf(triangles[i + 1], isHandBone) +
                                WeightOf(triangles[i + 2], isHandBone)) / 3f;
                    var forearm = (WeightOf(triangles[i], isForearmBone) + WeightOf(triangles[i + 1], isForearmBone) +
                                   WeightOf(triangles[i + 2], isForearmBone)) / 3f;
                    var isKept = cut switch
                    {
                        ArmCut.WithoutHands => hand <= _armCutWeight,
                        // A bit up into the armor - the cut sleeve overlaps it, no gap at the elbow.
                        ArmCut.Forearms => hand <= _armCutWeight && forearm + hand > _bareForearmWeight,
                        // No triangle with a forearm vertex: it stuck out straight from the bent elbow (spikes).
                        _ => Mathf.Max(SleeveWeight(triangles[i]), SleeveWeight(triangles[i + 1]),
                            SleeveWeight(triangles[i + 2])) <= _sleeveKeepWeight
                    };
                    if (!isKept)
                        continue;
                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                }
                copy.SetTriangles(kept, subMesh);
                keptTriangles += kept.Count / 3;
            }

            if (cut == ArmCut.Sleeveless)
                MoveForearmWeightsToUpperArm(copy, bones, isForearmBone, isHandBone);

            Logger.Log($"[VRHeroBody] {copy.name}: {keptTriangles} of {mesh.triangles.Length / 3} triangles.", LogCat.VR);
            if (keptTriangles == 0)
            {
                Destroy(copy);
                return null;
            }
            _createdMeshes.Add(copy);
            return copy;
        }

        /// <summary>
        /// Rolled-up sleeve: the elbow vertices that stay are partly weighted to the forearm - stretched by the IK they
        /// became spikes towards the wrist. Their forearm/hand weight goes to the bone above (upper arm): the sleeve
        /// ends rigid at the elbow.
        /// </summary>
        private static void MoveForearmWeightsToUpperArm(Mesh mesh, Transform[] bones, bool[] isForearmBone,
            bool[] isHandBone)
        {
            var target = new int[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                target[i] = i;
                if (!isForearmBone[i] && !isHandBone[i])
                    continue;
                // Nearest ancestor that is neither forearm nor hand (BIP01 x UPPERARM).
                for (var parent = bones[i].parent; parent != null; parent = parent.parent)
                {
                    var index = System.Array.IndexOf(bones, parent);
                    if (index >= 0 && !isForearmBone[index] && !isHandBone[index])
                    {
                        target[i] = index;
                        break;
                    }
                }
            }

            int Remap(int boneIndex) => boneIndex >= 0 && boneIndex < target.Length ? target[boneIndex] : boneIndex;
            var weights = mesh.boneWeights;
            for (var v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                w.boneIndex0 = Remap(w.boneIndex0);
                w.boneIndex1 = Remap(w.boneIndex1);
                w.boneIndex2 = Remap(w.boneIndex2);
                w.boneIndex3 = Remap(w.boneIndex3);
                weights[v] = w;
            }
            mesh.boneWeights = weights;
        }

        private void DestroyCreatedMeshes()
        {
            foreach (var mesh in _createdMeshes)
            {
                if (mesh != null)
                    Destroy(mesh);
            }
            _createdMeshes.Clear();
        }

        /// <summary>
        /// Analytic two-bone IK: upper arm + forearm reach for the HVR hand, the elbow bends towards a hint below and to
        /// the outside. The wrist always ends at the HVR wrist (the model's hands are cut out of the mesh): out of reach
        /// the arm is stretched, tapered - 40 % of the missing length in the upper arm, 60 % in the forearm, so the most
        /// stretch sits near the wrist (skinned vertices between the moved joints stretch, the shoulder stays).
        /// The forearm rolls around its axis like the HVR hand (palms of both models aligned).
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

            // Palm side of the model's hand (towards its item slot ZS_*HAND, across the fingers) in the forearm's space.
            private Vector3 _modelPalmLocal;
            private bool _hasModelPalm;
            // HVR hand model: its pivot sits at the knuckles - the wrist is the bone the fingers hang on ("RHand 1"),
            // the palm transform's forward points out of the palm.
            private GameObject _hvrHand;
            private Transform _hvrWrist;
            private Transform _hvrPalm;

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
                BindModelPalm(bones);
            }

            /// <summary>
            /// Gothic hands have one finger bone (FINGER0) and the item slot in the palm (ZS_RIGHTHAND/ZS_LEFTHAND).
            /// </summary>
            private void BindModelPalm(Dictionary<string, Transform> bones)
            {
                _hasModelPalm = false;
                var slotName = _side == "L" ? "ZS_LEFTHAND" : "ZS_RIGHTHAND";
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

            public void Solve(GameObject target, Transform body)
            {
                if (target == null || _upper == null || _fore == null || _hand == null)
                    return;

                // Last frame's stretch must not add up.
                _fore.localPosition = _foreRestLocal;
                _hand.localPosition = _handRestLocal;

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
                RollForearm(elbow, wrist);
                _hand.position = wrist;
            }

            /// <summary>
            /// Twist around the forearm axis so the model's palm faces like the HVR palm - the wrist follows the
            /// hand's rotation.
            /// </summary>
            private void RollForearm(Vector3 elbow, Vector3 wrist)
            {
                if (!_hasModelPalm || _hvrPalm == null)
                    return;

                var axis = wrist - elbow;
                if (axis.sqrMagnitude < 1e-6f)
                    return;
                axis.Normalize();

                var current = Vector3.ProjectOnPlane(_fore.TransformDirection(_modelPalmLocal), axis);
                var wanted = Vector3.ProjectOnPlane(_hvrPalm.forward, axis);
                if (current.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
                    return;

                var angle = Vector3.SignedAngle(current, wanted, axis);
                _fore.rotation = Quaternion.AngleAxis(angle, axis) * _fore.rotation;
            }
        }
    }
}
#endif
