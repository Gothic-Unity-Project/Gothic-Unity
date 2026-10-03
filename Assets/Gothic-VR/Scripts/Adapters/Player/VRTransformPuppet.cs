#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The transformed hero's body (VRTransformService): a monster NPC without AI that follows the VR player.
    /// - Layer PlayerPuppet: the VR (and spectator) camera don't render it, only the small PiP camera does - its picture
    ///   is an orb in the casting hand. No colliders, not in the NPC cache (no perception, focus, saving).
    /// - Animations from the player's movement (idle / walk / run), attack = swing (or trigger) of the free hand - the MDS sound
    ///   events of these animations are the only thing the player hears of the body.
    /// - The VR camera goes down to the monster's eye height (HVRCameraRig.CameraYOffset).
    /// - Trigger of the orb hand (empty or holding the transformation scroll) = transform back.
    /// </summary>
    public class VRTransformPuppet : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly NpcService _npcService;

        private const int _puppetLayer = 13; // PlayerPuppet
        private const float _walkSpeed = 0.2f;
        private const float _runSpeed = 1.6f;
        private const float _attackReach = 1.8f;
        private const float _attackHitDelay = 0.35f;
        // A swing of the free hand (like a sword) bites/claws - the other hand holds the spell orb.
        private const float _attackSwingSpeed = 3f;
        private Vector3 _lastAttackHandPosition;
        private bool _hasLastAttackHandPosition;
        private const float _parkingDepth = -10000f;
        // Some monster instances can't be built (G1 Lurker: no visual set) - don't stay transformed into nothing.
        private const float _setupTimeoutSeconds = 5f;
        private float _setupDeadline;
        private GameObject _loaderGo;

        // Standing still: the monster's random routine animations (MDS r_Roam1-3 = scratching, pecking, digging, ...
        // and t_Perception) now and then, like ZS_MM_* routines.
        private static readonly string[] _idleVariations = { "R_ROAM1", "R_ROAM2", "R_ROAM3", "T_PERCEPTION" };
        private const float _idleVariationMinSeconds = 5f;
        private const float _idleVariationMaxSeconds = 12f;
        private float _nextIdleVariation;
        private string _idleVariation;

        // Turning on the spot (snap/smooth turn, head turn) plays the monster's turn animation.
        private const float _turnAnimationSpeed = 45f; // degrees per second
        private float _lastYaw;
        private bool _hasLastYaw;
        private float _yawSpeed;
        // Attack while running: the monster's running attack (MDS t_FistAttackMove), standing: s_FistAttack.
        private const string _runAttackAnimation = "T_FISTATTACKMOVE";

        // PiP: camera above and to the side, whole body in the picture (see research-2026-10-03.md).
        private const int _pipResolution = 256;
        private const float _pipElevation = 35f;
        private const float _pipAzimuth = 30f;
        private const float _pipFov = 30f;
        private const float _pipMargin = 1.2f;
        private const float _orbSize = 0.12f;
        private const float _orbHandOffset = 0.1f;

        private VRTransformService _transformService;
        private NpcContainer _monster;
        private HVRHandSide _orbHand;
        private bool _isReady;
        private bool _isReleased;

        private VRPlayerController _playerController;
        private float _originalCameraYOffset;
        private bool _hasCameraOffset;

        private string _currentAnimation;
        private float _attackCooldown;
        private float _attackHitTime = -1f;
        private Bounds _puppetBounds;

        private Camera _pipCamera;
        private RenderTexture _pipTexture;
        private GameObject _orb;
        private Material _orbMaterial;


        public void Init(VRTransformService transformService, NpcContainer monster, GameObject loaderGo,
            HVRHandSide castingHand)
        {
            gameObject.Inject();
            _transformService = transformService;
            _monster = monster;
            _loaderGo = loaderGo;
            _setupDeadline = Time.time + _setupTimeoutSeconds;
            _orbHand = castingHand;
            _playerController = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
        }

        /// <summary>
        /// Transform back: the puppet is parked far below the world (NPC culling disables it - it can't be removed from
        /// the culling group), camera height and PiP are undone.
        /// </summary>
        public void Release()
        {
            _isReleased = true;
            if (_hasCameraOffset && _playerController != null)
                _playerController.CameraRig.CameraYOffset = _originalCameraYOffset;
            if (_isReady)
                _playerController?.SetGrabbingEnabled(true);

            if (_monster?.Go != null)
            {
                _monster.Go.transform.position = new Vector3(0f, _parkingDepth, 0f);
                _monster.PrefabProps?.AnimationSystem?.StopAllAnimations();
            }
            else if (_loaderGo != null)
            {
                // Never built: park the lazy loader, so culling doesn't try to build it again next to the player.
                _loaderGo.transform.position = new Vector3(0f, _parkingDepth, 0f);
            }
            _multiTypeCacheService.NpcCache.Remove(_monster);

            if (_pipTexture != null)
                _pipTexture.Release();
            if (_orb != null)
                Destroy(_orb);
            if (_pipCamera != null)
                Destroy(_pipCamera.gameObject);
            Destroy(gameObject);
        }

        private void Update()
        {
            if (_monster == null || _isReleased)
                return;

            if (!_isReady)
            {
                UpdateTransformBackInput();
                if (_isReleased)
                    return;
                TrySetup();
                if (!_isReady && Time.time > _setupDeadline)
                {
                    Logger.LogWarning("[VRTransform] The monster couldn't be built - transformation cancelled.", LogCat.VR);
                    _transformService.TransformBack();
                }
                return;
            }

            // Re-enabled by NPC culling/OnEnable - the puppet never thinks on its own.
            var aiHandler = _monster.PrefabProps.AiHandler;
            if (aiHandler != null && aiHandler.enabled)
                aiHandler.enabled = false;
            _monster.Props.AnimationQueue.Clear();

            UpdateAnimation();
            UpdateAttack();
            UpdateTransformBackInput();
        }

        private void LateUpdate()
        {
            if (!_isReady || _isReleased || _playerController == null)
                return;

            // Body under the head, facing where the head looks.
            var head = Camera.main != null ? Camera.main.transform : null;
            var feetY = _playerController.transform.position.y;
            var x = head != null ? head.position.x : _playerController.transform.position.x;
            var z = head != null ? head.position.z : _playerController.transform.position.z;
            var restHeight = _monster.PrefabProps.AnimationSystem.RestRootHeight;
            _monster.Go.transform.position = new Vector3(x, feetY + restHeight, z);
            if (head != null)
            {
                var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude > 0.001f)
                    _monster.Go.transform.rotation = Quaternion.LookRotation(forward);
            }

            var yaw = _monster.Go.transform.eulerAngles.y;
            _yawSpeed = _hasLastYaw && Time.deltaTime > 0f
                ? Mathf.Lerp(_yawSpeed, Mathf.DeltaAngle(_lastYaw, yaw) / Time.deltaTime, 0.3f)
                : 0f;
            _lastYaw = yaw;
            _hasLastYaw = true;

            UpdatePip();
        }

        /// <summary>
        /// The monster's mesh is created lazily (NPC culling) - wait for it, then cut it off from the world.
        /// </summary>
        private void TrySetup()
        {
            var animationSystem = _monster.PrefabProps?.AnimationSystem;
            if (_monster.Go == null || animationSystem == null || animationSystem.RestRootHeight <= 0f)
                return;

            _multiTypeCacheService.NpcCache.Remove(_monster);
            if (_monster.PrefabProps.AiHandler != null)
                _monster.PrefabProps.AiHandler.enabled = false;
            _monster.Props.Perceptions.Clear();
            _monster.Props.AnimationQueue.Clear();
            _monster.Instance.NoFocus = 1;

            foreach (var child in _monster.Go.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = _puppetLayer;
            foreach (var collider in _monster.Go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (var body in _monster.Go.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;

            _puppetBounds = GetBounds();
            _playerController?.SetGrabbingEnabled(false);
            HideFromVrCameras();
            LowerCamera();
            CreatePip();

            animationSystem.StopAllAnimations();
            _isReady = true;
            Logger.Log($"[VRTransform] Puppet ready: height {_puppetBounds.size.y:F2} m.", LogCat.VR);
        }

        private Bounds GetBounds()
        {
            var renderers = _monster.Go.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(_monster.Go.transform.position, Vector3.one * 0.5f);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
            }
            return bounds;
        }

        private static void HideFromVrCameras()
        {
            foreach (var camera in Camera.allCameras)
                camera.cullingMask &= ~(1 << _puppetLayer);
        }

        /// <summary>
        /// Eye height of the monster: its head bone (or 85 % of its height) above its feet.
        /// </summary>
        private void LowerCamera()
        {
            if (_playerController == null || Camera.main == null)
                return;

            var feetY = _monster.Go.transform.position.y - _monster.PrefabProps.AnimationSystem.RestRootHeight;
            var eyeHeight = _puppetBounds.size.y * 0.85f;
            foreach (var bone in _monster.Go.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.ContainsIgnoreCase("HEAD"))
                {
                    eyeHeight = bone.position.y - feetY;
                    break;
                }
            }

            var currentEyeHeight = Camera.main.transform.position.y - _playerController.transform.position.y;
            var rig = _playerController.CameraRig;
            _originalCameraYOffset = rig.CameraYOffset;
            rig.CameraYOffset += Mathf.Clamp(eyeHeight, 0.2f, 3f) - currentEyeHeight;
            _hasCameraOffset = true;
        }

        private void UpdateAnimation()
        {
            var velocity = _playerController != null ? _playerController.CharacterController.velocity : Vector3.zero;
            var speed = new Vector2(velocity.x, velocity.z).magnitude;
            var animationSystem = _monster.PrefabProps.AnimationSystem;

            // An attack plays to its end.
            if (_attackCooldown > 0f)
                return;

            string animation;
            // Jumping/falling (the player isn't on the ground): the monster's fall animation.
            var fall = _playerController != null && !_playerController.IsGrounded
                ? _animationService.GetAnimationName(VmGothicEnums.AnimationType.Fall, _monster)
                : null;
            if (fall != null && _animationService.GetTrack(fall, _monster.Props.MdsNameBase, _monster.Props.MdsNameOverlay) != null)
            {
                StopIdleVariation(animationSystem);
                animation = fall;
            }
            else if (speed < _walkSpeed && Mathf.Abs(_yawSpeed) >= _turnAnimationSpeed)
            {
                StopIdleVariation(animationSystem);
                animation = _animationService.GetAnimationName(_yawSpeed < 0f
                    ? VmGothicEnums.AnimationType.RotL
                    : VmGothicEnums.AnimationType.RotR, _monster);
            }
            else if (speed < _walkSpeed)
            {
                if (UpdateIdleVariation(animationSystem))
                    return;
                animation = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Idle, _monster);
            }
            else
            {
                StopIdleVariation(animationSystem);
                _monster.Vob.AiHuman.WalkMode = (int)(speed >= _runSpeed ? VmGothicEnums.WalkMode.Run : VmGothicEnums.WalkMode.Walk);
                animation = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Move, _monster);
            }

            if (animation == _currentAnimation && animationSystem.IsPlaying(animation))
                return;

            if (_currentAnimation != null)
                animationSystem.StopAnimation(_currentAnimation);
            animationSystem.PlayAnimation(animation);
            _currentAnimation = animation;
        }

        /// <summary>
        /// True while a random idle animation plays (the normal idle waits).
        /// </summary>
        private bool UpdateIdleVariation(Gothic.Core.Adapters.Animations.AnimationSystem animationSystem)
        {
            if (_idleVariation != null)
            {
                if (animationSystem.IsPlaying(_idleVariation))
                    return true;
                _idleVariation = null;
                _currentAnimation = null;
            }

            if (_nextIdleVariation <= 0f)
                _nextIdleVariation = Time.time + Random.Range(_idleVariationMinSeconds, _idleVariationMaxSeconds);
            if (Time.time < _nextIdleVariation)
                return false;

            _nextIdleVariation = 0f;
            var candidate = _idleVariations[Random.Range(0, _idleVariations.Length)];
            if (_animationService.GetTrack(candidate, _monster.Props.MdsNameBase, _monster.Props.MdsNameOverlay) == null)
                return false;

            if (_currentAnimation != null)
                animationSystem.StopAnimation(_currentAnimation);
            if (!animationSystem.PlayAnimation(candidate))
                return false;
            _idleVariation = candidate;
            _currentAnimation = candidate;
            return true;
        }

        private void StopIdleVariation(Gothic.Core.Adapters.Animations.AnimationSystem animationSystem)
        {
            _nextIdleVariation = 0f;
            if (_idleVariation == null)
                return;
            animationSystem.StopAnimation(_idleVariation);
            _idleVariation = null;
            _currentAnimation = null;
        }

        /// <summary>
        /// Swing (or trigger) of the hand without the orb: the monster attacks (bite/claw) - hits the closest NPC in front.
        /// </summary>
        private void UpdateAttack()
        {
            var attackHand = _orbHand == HVRHandSide.Left ? HVRHandSide.Right : HVRHandSide.Left;
            var isSwing = IsSwinging(attackHand);

            if (_attackCooldown > 0f)
            {
                _attackCooldown -= Time.deltaTime;
                if (_attackHitTime >= 0f)
                {
                    _attackHitTime -= Time.deltaTime;
                    if (_attackHitTime < 0f)
                        TryHit();
                }
                return;
            }

            if (!isSwing && !IsTriggerJustPressed(attackHand))
                return;

            var animationSystem = _monster.PrefabProps.AnimationSystem;
            var velocity = _playerController != null ? _playerController.CharacterController.velocity : Vector3.zero;
            var isRunning = new Vector2(velocity.x, velocity.z).magnitude >= _runSpeed;
            var attack = isRunning &&
                         _animationService.GetTrack(_runAttackAnimation, _monster.Props.MdsNameBase, _monster.Props.MdsNameOverlay) != null
                ? _runAttackAnimation
                : _animationService.GetAnimationName(VmGothicEnums.AnimationType.Attack, _monster);
            if (_currentAnimation != null)
                animationSystem.StopAnimation(_currentAnimation);
            if (!animationSystem.PlayAnimation(attack))
                return;

            _currentAnimation = attack;
            _attackCooldown = Mathf.Max(0.5f, animationSystem.GetAnimationDuration(attack));
            _attackHitTime = _attackHitDelay;
        }

        private bool IsSwinging(HVRHandSide side)
        {
            var hand = _vrPlayerService.GetHandModelGo(side);
            if (hand == null || Time.deltaTime <= 0f)
                return false;

            // In the player's own space - running and turning (snap/smooth turn) move the hands too. The simulator's hands
            // hang on the camera - there the camera's space (mouse look moved them).
            var space = _vrPlayerService.VRPlayerInputs.UseWASD && Camera.main != null
                ? Camera.main.transform
                : _playerController != null ? _playerController.transform : null;
            var position = space != null ? space.InverseTransformPoint(hand.transform.position) : hand.transform.position;
            var isSwing = _hasLastAttackHandPosition &&
                          Vector3.Distance(position, _lastAttackHandPosition) / Time.deltaTime >= _attackSwingSpeed;
            _lastAttackHandPosition = position;
            _hasLastAttackHandPosition = true;
            return isSwing;
        }

        private void TryHit()
        {
            var hero = _npcService.GetHeroContainer();
            var origin = _monster.Go.transform.position;
            var forward = _monster.Go.transform.forward;
            NpcContainer best = null;
            var bestDistance = _attackReach;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc == _monster || npc?.Go == null || !npc.Go.activeInHierarchy)
                    continue;
                if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead)
                    continue;

                var toNpc = npc.Go.transform.position - origin;
                toNpc.y = 0f;
                if (toNpc.magnitude > bestDistance || Vector3.Angle(forward, toNpc) > 60f)
                    continue;
                bestDistance = toNpc.magnitude;
                best = npc;
            }

            if (best != null)
                GlobalEventDispatcher.FightHit.Invoke(hero, best, best.Go.transform.position);
        }

        private void UpdateTransformBackInput()
        {
            if (!IsTriggerJustPressed(_orbHand))
                return;

            // Only an empty hand or the transformation scroll/rune casts the way back.
            var held = _orbHand == HVRHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
            if (held != null && held.GetComponent<Vob.VobItem.VRRuneCaster>() == null)
                return;

            _transformService.TransformBack();
        }

        private bool IsTriggerJustPressed(HVRHandSide side)
        {
            // Simulator: R = attack, F = transform back.
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                return side == _orbHand
                    ? Keyboard.current[Key.F].wasPressedThisFrame
                    : Keyboard.current[Key.R].wasPressedThisFrame;
            return HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated;
        }

        private void CreatePip()
        {
            _pipTexture = new RenderTexture(_pipResolution, _pipResolution, 16) { name = "TransformPip" };

            var cameraGo = new GameObject("_TransformPipCamera");
            _pipCamera = cameraGo.AddComponent<Camera>();
            _pipCamera.cullingMask = 1 << _puppetLayer;
            _pipCamera.clearFlags = CameraClearFlags.SolidColor;
            _pipCamera.backgroundColor = new Color(0.05f, 0.03f, 0.08f, 1f);
            _pipCamera.fieldOfView = _pipFov;
            _pipCamera.nearClipPlane = 0.05f;
            _pipCamera.farClipPlane = 50f;
            _pipCamera.targetTexture = _pipTexture;
            _pipCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _pipCamera.allowMSAA = false;
            _pipCamera.allowHDR = false;

            _orb = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _orb.name = "_TransformOrb";
            Destroy(_orb.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            _orbMaterial = new Material(shader) { mainTexture = _pipTexture };
            _orb.GetComponent<MeshRenderer>().sharedMaterial = _orbMaterial;
            _orb.transform.localScale = Vector3.one * _orbSize;
        }

        /// <summary>
        /// Camera above and to the side of the puppet, distance from its size (whole body visible). The orb floats above
        /// the casting hand's palm, facing the head.
        /// </summary>
        private void UpdatePip()
        {
            if (_pipCamera == null)
                return;

            _puppetBounds = GetBounds();
            var center = _puppetBounds.center;
            var size = Mathf.Max(_puppetBounds.size.y, _puppetBounds.size.x, _puppetBounds.size.z);
            var distance = size * 0.5f * _pipMargin / Mathf.Tan(_pipFov * 0.5f * Mathf.Deg2Rad);
            var direction = Quaternion.Euler(-_pipElevation, _pipAzimuth, 0f) * _monster.Go.transform.forward;
            _pipCamera.transform.position = center + direction.normalized * distance;
            _pipCamera.transform.LookAt(center);

            var hand = _vrPlayerService.GetHandModelGo(_orbHand);
            if (hand == null || _orb == null || Camera.main == null)
                return;
            _orb.transform.position = hand.transform.position + Vector3.up * _orbHandOffset;
            _orb.transform.rotation = Quaternion.LookRotation(_orb.transform.position - Camera.main.transform.position);
        }

        private void OnDestroy()
        {
            if (_orbMaterial != null)
                Destroy(_orbMaterial);
        }
    }
}
#endif
