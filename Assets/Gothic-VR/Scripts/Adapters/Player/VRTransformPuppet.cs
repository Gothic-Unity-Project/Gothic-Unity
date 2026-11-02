using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction;
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
    /// - The VR camera goes down to the monster's eye height (IVrRig.CameraYOffset).
    /// - Trigger of the orb hand (empty or holding the transformation scroll) = transform back.
    /// </summary>
    public class VRTransformPuppet : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcWaterService _npcWaterService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly Gothic.Core.Manager.AudioService _audioService;

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
        // Moving more backwards than this (share of the speed) plays the backwards walk instead of a moonwalk.
        private const float _backwardsShare = 0.5f;
        private readonly System.Collections.Generic.Dictionary<string, string> _moveAnimations = new();
        private readonly System.Collections.Generic.Dictionary<VmGothicEnums.WalkMode, bool> _supportedWalkModes = new();
        private VmGothicEnums.WalkMode? _specialWalkMode;

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
        private VrHandSide _orbHand;
        private bool _isReady;
        private bool _isReleased;

        private IVrRig _rig;
        private float _originalCameraYOffset;
        // DeveloperConfig.EnableTransformMonsterSpeed: the player's speeds before, the human run loop to compare with.
        private float _originalMoveSpeed;
        private float _originalRunSpeed;
        private bool _hasSpeedScale;
        // DeveloperConfig.EnableTransformLandSpeedKept: the monster's speeds, set again after the water changed them.
        private float _monsterMoveSpeed;
        private float _monsterRunSpeed;
        private const string _humanRunAnimation = "S_RUNL";
        private const float _waterRayAboveEyes = 1f;
        // DeveloperConfig.EnableTransformAbsoluteSpeed: sprinting as a monster, times its run loop's speed.
        private const float _monsterSprintFactor = 1.5f;
        // DeveloperConfig.EnableHeroWaterBody / EnableHeroSwimSounds.
        private Vector3 _lastPlayerPosition;
        private Vector3 _movedVelocity;
        private const float _movedSmoothing = 0.2f;
        private const float _maxMovedSpeed = 15f;
        private const string _swimSound = "SWIM";
        private const float _swimDriftSeconds = 2.5f;
        private const float _swimStrokeSeconds = 1.4f;
        private const float _swimFastStrokeSeconds = 0.8f;
        private const float _swimDriftVolume = 0.5f;
        private float _nextSwimSoundTime;
        private const string _humanMds = "Humans.mds";
        private const float _minSpeedFactor = 0.3f;
        private const float _maxSpeedFactor = 3f;
        private bool _hasCameraOffset;
        // Camera offset at the monster's eyes (LowerCamera) and its eye height above its feet.
        private float _transformedCameraYOffset;
        private float _eyeHeight;

        // Water: VR swimming starts at human chest depth - a wolf is under water long before. The puppet checks the
        // depth itself: water above this part of its eye height = it swims at the surface, the camera rises with it.
        private const float _swimDepthShare = 0.6f;
        private const float _swimEndDepthShare = 0.4f;
        private const float _wadeDepthShare = 0.25f;
        private float _waterDepth;
        private const float _waterGraceSeconds = 1f;
        private float _lastWaterY;
        private float _lastWaterTime = -999f;
        private const float _swimRootDepthShare = 0.25f;
        private const float _eyeAboveSurface = 0.15f;
        private const float _swimCameraLiftSpeed = 1.5f;
        private bool _isSwimming;
        private float _swimCameraLift;

        private string _currentAnimation;
        private float _attackCooldown;
        private float _attackHitTime = -1f;
        private Bounds _puppetBounds;

        private Camera _pipCamera;
        private RenderTexture _pipTexture;
        private GameObject _orb;
        private Material _orbMaterial;


        public void Init(VRTransformService transformService, NpcContainer monster, GameObject loaderGo,
            VrHandSide castingHand)
        {
            gameObject.Inject();
            _transformService = transformService;
            _monster = monster;
            _loaderGo = loaderGo;
            _setupDeadline = Time.time + _setupTimeoutSeconds;
            _orbHand = castingHand;
            _rig = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<IVrRig>();
        }

        /// <summary>
        /// Transform back: the puppet is parked far below the world (NPC culling disables it - it can't be removed from
        /// the culling group), camera height and PiP are undone.
        /// </summary>
        public void Release()
        {
            _isReleased = true;
            if (_hasCameraOffset && _rig != null)
                _rig.CameraYOffset = _originalCameraYOffset;
            if (_hasSpeedScale && _rig != null)
            {
                _rig.MoveSpeed = _originalMoveSpeed;
                _rig.RunSpeed = _originalRunSpeed;
            }
            if (_isReady)
                _rig?.SetGrabbingEnabled(true);

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

            KeepMonsterSpeed();
            UpdateAnimation();
            UpdateAttack();
            UpdateTransformBackInput();
        }

        private void LateUpdate()
        {
            if (!_isReady || _isReleased || _rig == null)
                return;

            // Body under the head, facing where the head looks.
            var head = Camera.main != null ? Camera.main.transform : null;
            var feetY = _rig.Root.position.y;
            var x = head != null ? head.position.x : _rig.Root.position.x;
            var z = head != null ? head.position.z : _rig.Root.position.z;
            var restHeight = _monster.PrefabProps.AnimationSystem.RestRootHeight;
            var rootY = feetY + restHeight;
            _isSwimming = IsPuppetSwimming(x, z, feetY, out var waterY);
            if (_isSwimming)
            {
                // The swim loops pull the root bone down themselves (like NpcWaterService) - compensated, so the back
                // stays at the surface.
                var rootBoneOffset = _monster.PrefabProps.Bip01 != null ? _monster.PrefabProps.Bip01.localPosition.y : 0f;
                rootY = Mathf.Max(rootY, waterY - restHeight * _swimRootDepthShare - rootBoneOffset);
            }
            _monster.Go.transform.position = new Vector3(x, rootY, z);
            UpdateSwimCamera(feetY, waterY);
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
        /// Deep enough to swim, with hysteresis: starts above 60 % of the eye height, ends below 40 %. A plant or a rock
        /// between the bottom and the surface hides the water for a moment - the last surface is kept for a second.
        /// </summary>
        private bool IsPuppetSwimming(float x, float z, float feetY, out float waterY)
        {
            // DeveloperConfig.EnableHeroWaterBody: the ray starts above the monster's eyes - drifting deeper, a ray from
            // 1 m above its feet started under the surface, found no water and the fall animation played.
            var rayStart = _configService.Dev.EnableHeroWaterBody ? feetY + _eyeHeight + _waterRayAboveEyes : feetY + 1f;
            if (_npcWaterService.TryGetWaterSurface(new Vector3(x, rayStart, z), out var groundY, out waterY))
            {
                _lastWaterY = waterY;
                _lastWaterTime = Time.time;
                var depth = waterY - Mathf.Min(groundY, feetY);
                _waterDepth = depth;
                var limit = _eyeHeight * (_isSwimming ? _swimEndDepthShare : _swimDepthShare);
                return depth > limit;
            }

            waterY = _lastWaterY;
            var isInGrace = Time.time - _lastWaterTime < _waterGraceSeconds;
            if (!isInGrace)
                _waterDepth = 0f;
            return _isSwimming && isInGrace;
        }

        /// <summary>
        /// Swimming: the eyes stay just above the surface (the VR body walks on the bottom until human chest depth).
        /// </summary>
        private void UpdateSwimCamera(float feetY, float waterY)
        {
            if (!_hasCameraOffset)
                return;

            var wantedLift = _isSwimming ? Mathf.Max(0f, waterY + _eyeAboveSurface - (feetY + _eyeHeight)) : 0f;
            if (Mathf.Approximately(wantedLift, _swimCameraLift))
                return;
            _swimCameraLift = Mathf.MoveTowards(_swimCameraLift, wantedLift, _swimCameraLiftSpeed * Time.deltaTime);
            _rig.CameraYOffset = _transformedCameraYOffset + _swimCameraLift;
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
            _rig?.SetGrabbingEnabled(false);
            HideFromVrCameras();
            LowerCamera();
            ScaleSpeedToMonster();
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
            if (_rig == null || Camera.main == null)
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

            var currentEyeHeight = Camera.main.transform.position.y - _rig.Root.position.y;
            var rig = _rig;
            _originalCameraYOffset = rig.CameraYOffset;
            _eyeHeight = Mathf.Clamp(eyeHeight, 0.2f, 3f);
            rig.CameraYOffset += _eyeHeight - currentEyeHeight;
            _transformedCameraYOffset = rig.CameraYOffset;
            _hasCameraOffset = true;
        }

        private void UpdateAnimation()
        {
            var velocity = GetPlayerVelocity();
            var speed = new Vector2(velocity.x, velocity.z).magnitude;
            UpdateSwimSounds(speed);
            var animationSystem = _monster.PrefabProps.AnimationSystem;

            // An attack plays to its end.
            if (_attackCooldown > 0f)
                return;

            // The puppet's own AnimationSystem guesses a water level from its root motion (it swam while wading) - our
            // depth check decides.
            // Swimming without swim loops (wolves, dogs - in Gothic they can't swim): their wading loop (s_*WalkWL)
            // as paddling. Shallower water: wading (Knee), if the model has it.
            var canSwim = HasTrack("S_SWIM") && HasTrack("S_SWIMF");
            _monster.Vob.AiHuman.WaterLevel = (int)(_isSwimming && canSwim
                ? ZenGineConst.WaterLevel.Chest
                : _isSwimming || _waterDepth > _eyeHeight * _wadeDepthShare
                    ? ZenGineConst.WaterLevel.Knee
                    : ZenGineConst.WaterLevel.Normal);

            // Swimming/diving and crouching (sneaking) - if the monster has those loops.
            _specialWalkMode = GetSpecialWalkMode();
            if (_specialWalkMode != null)
                _monster.Vob.AiHuman.WalkMode = (int)_specialWalkMode.Value;
            else if ((VmGothicEnums.WalkMode)_monster.Vob.AiHuman.WalkMode is VmGothicEnums.WalkMode.Swim
                     or VmGothicEnums.WalkMode.Dive or VmGothicEnums.WalkMode.Sneak)
                _monster.Vob.AiHuman.WalkMode = (int)VmGothicEnums.WalkMode.Walk;

            string animation;
            // Jumping/falling (the player isn't on the ground): the monster's fall animation.
            var isOnLand = !IsInWater() && _waterDepth <= 0f;
            var fall = _rig != null && !_rig.IsGrounded && isOnLand
                ? _animationService.GetAnimationName(VmGothicEnums.AnimationType.Fall, _monster)
                : null;
            if (fall != null && _animationService.GetTrack(fall, _monster.Props.MdsNameBase, _monster.Props.MdsNameOverlay) != null)
            {
                StopIdleVariation(animationSystem);
                animation = fall;
            }
            else if (speed < _walkSpeed && Mathf.Abs(_yawSpeed) >= _turnAnimationSpeed && _specialWalkMode == null)
            {
                StopIdleVariation(animationSystem);
                animation = _animationService.GetAnimationName(_yawSpeed < 0f
                    ? VmGothicEnums.AnimationType.RotL
                    : VmGothicEnums.AnimationType.RotR, _monster);
            }
            else if (speed < _walkSpeed)
            {
                // No scratching/pecking in water.
                if (_waterDepth <= 0f && UpdateIdleVariation(animationSystem))
                    return;
                if (_waterDepth > 0f)
                    StopIdleVariation(animationSystem);
                animation = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Idle, _monster);
            }
            else
            {
                StopIdleVariation(animationSystem);
                animation = GetMoveAnimation(velocity, speed);
            }

            if (animation == _currentAnimation && animationSystem.IsPlaying(animation))
                return;

            if (_currentAnimation != null)
                animationSystem.StopAnimation(_currentAnimation);
            animationSystem.PlayAnimation(animation);
            _currentAnimation = animation;
        }

        /// <summary>
        /// Like Gothic: the monster keeps facing where you look - walking backwards plays its backwards walk (or the walk
        /// loop played back to front if the model has none), sideways its strafe loop if it has one.
        /// </summary>
        private string GetMoveAnimation(Vector3 velocity, float speed)
        {
            var local = _monster.Go.transform.InverseTransformDirection(new Vector3(velocity.x, 0f, velocity.z));
            var isBackwards = local.z < -_backwardsShare * speed;
            var isSideways = !isBackwards && Mathf.Abs(local.x) > Mathf.Abs(local.z);

            if (_specialWalkMode != null)
            {
                var loop = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Move, _monster);
                if (!isBackwards || _specialWalkMode != VmGothicEnums.WalkMode.Sneak)
                    return loop;
                return GetCachedMoveAnimation(VmGothicEnums.AnimationType.MoveBack, () =>
                    _animationService.GetReversedAnimationName(loop, _monster.Props.MdsNameBase,
                        _monster.Props.MdsNameOverlay) ?? loop);
            }

            // Nobody runs backwards in Gothic.
            _monster.Vob.AiHuman.WalkMode = (int)(speed >= _runSpeed && !isBackwards
                ? VmGothicEnums.WalkMode.Run
                : VmGothicEnums.WalkMode.Walk);
            var forward = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Move, _monster);

            if (isBackwards)
            {
                return GetCachedMoveAnimation(VmGothicEnums.AnimationType.MoveBack, () =>
                {
                    var back = _animationService.GetAnimationName(VmGothicEnums.AnimationType.MoveBack, _monster);
                    if (HasTrack(back))
                        return back;
                    return _animationService.GetReversedAnimationName(forward, _monster.Props.MdsNameBase,
                        _monster.Props.MdsNameOverlay) ?? forward;
                });
            }

            if (isSideways)
            {
                var type = local.x < 0f ? VmGothicEnums.AnimationType.MoveL : VmGothicEnums.AnimationType.MoveR;
                var strafe = GetCachedMoveAnimation(type, () =>
                {
                    var name = _animationService.GetAnimationName(type, _monster);
                    return HasTrack(name) ? name : null;
                });
                if (strafe != null)
                    return strafe;
            }
            return forward;
        }

        /// <summary>
        /// Resolved once per type and walk mode: a missing animation logs a fallback warning on every lookup.
        /// </summary>
        private string GetCachedMoveAnimation(VmGothicEnums.AnimationType type, System.Func<string> resolve)
        {
            var key = $"{type}|{_monster.Vob.AiHuman.WalkMode}";
            if (!_moveAnimations.TryGetValue(key, out var animation))
            {
                animation = resolve();
                _moveAnimations[key] = animation;
                Logger.Log($"[VRTransform] {type} ({(VmGothicEnums.WalkMode)_monster.Vob.AiHuman.WalkMode}): " +
                           $"{animation ?? "none"}", LogCat.VR);
            }
            return animation;
        }

        private bool IsInWater()
        {
            var bodyState = _npcService.GetHeroContainer()?.Props.BodyState;
            return _isSwimming || bodyState is VmGothicEnums.BodyState.BsSwim or VmGothicEnums.BodyState.BsDive;
        }

        /// <summary>
        /// Swim/Dive while the hero swims/dives, Sneak while crouching - only if the monster has the move loop for it
        /// (most monsters can't swim or sneak), checked once per mode.
        /// </summary>
        private VmGothicEnums.WalkMode? GetSpecialWalkMode()
        {
            var bodyState = _npcService.GetHeroContainer()?.Props.BodyState;
            VmGothicEnums.WalkMode? wanted = bodyState == VmGothicEnums.BodyState.BsDive
                ? VmGothicEnums.WalkMode.Dive
                : _isSwimming || bodyState == VmGothicEnums.BodyState.BsSwim
                    ? VmGothicEnums.WalkMode.Swim
                    : _rig != null && _rig.IsCrouching
                        ? VmGothicEnums.WalkMode.Sneak
                        : null;
            if (wanted == null)
                return null;

            if (!_supportedWalkModes.TryGetValue(wanted.Value, out var isSupported))
            {
                var previous = _monster.Vob.AiHuman.WalkMode;
                _monster.Vob.AiHuman.WalkMode = (int)wanted.Value;
                var loop = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Move, _monster);
                _monster.Vob.AiHuman.WalkMode = previous;
                isSupported = HasTrack(loop);
                _supportedWalkModes[wanted.Value] = isSupported;
                Logger.Log($"[VRTransform] {wanted.Value}: {(isSupported ? loop : "not in the model")}", LogCat.VR);
            }

            // Diving without dive loops: swim instead.
            if (!isSupported && wanted == VmGothicEnums.WalkMode.Dive)
                return _supportedWalkModes.TryGetValue(VmGothicEnums.WalkMode.Swim, out var canSwim) && canSwim
                    ? VmGothicEnums.WalkMode.Swim
                    : null;
            return isSupported ? wanted : null;
        }

        private bool HasTrack(string animation) =>
            _animationService.GetTrack(animation, _monster.Props.MdsNameBase, _monster.Props.MdsNameOverlay) != null;

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
            var attackHand = _orbHand == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left;
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
            var velocity = _rig != null ? _rig.CharacterController.velocity : Vector3.zero;
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

        private bool IsSwinging(VrHandSide side)
        {
            var hand = _vrPlayerService.GetHandModelGo(side);
            if (hand == null || Time.deltaTime <= 0f)
                return false;

            // In the player's own space - running and turning (snap/smooth turn) move the hands too. The simulator's hands
            // hang on the camera - there the camera's space (mouse look moved them).
            var space = _vrPlayerService.IsSimulator && Camera.main != null
                ? Camera.main.transform
                : _rig != null ? _rig.Root : null;
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
            var held = _orbHand == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
            if (held != null && held.GetComponent<Vob.VobItem.VRRuneCaster>() == null)
                return;

            _transformService.TransformBack();
        }

        /// <summary>
        /// DeveloperConfig.EnableTransformMonsterSpeed: the player moves as fast as the monster runs - its run loop's
        /// root motion against the human one (HUMANS S_RUNL). A wolf is faster than a human, a meatbug slower; with the
        /// camera at its eyes the human speed felt like crawling.
        /// </summary>
        private void ScaleSpeedToMonster()
        {
            if (!_configService.Dev.EnableTransformMonsterSpeed || _rig == null)
                return;

            var walkMode = _monster.Vob.AiHuman.WalkMode;
            _monster.Vob.AiHuman.WalkMode = (int)VmGothicEnums.WalkMode.Run;
            var run = _animationService.GetAnimationName(VmGothicEnums.AnimationType.Move, _monster);
            _monster.Vob.AiHuman.WalkMode = walkMode;

            var monsterSpeed = GetHorizontalSpeed(_animationService.GetTrack(run, _monster.Props.MdsNameBase,
                _monster.Props.MdsNameOverlay));
            var hero = _npcService.GetHeroContainer();
            var humanSpeed = GetHorizontalSpeed(_animationService.GetTrack(_humanRunAnimation,
                hero?.Props.MdsNameBase ?? _humanMds, null));
            if (monsterSpeed <= 0f || humanSpeed <= 0f)
            {
                Logger.LogWarning($"[VRTransform] No run speed ({run}: {monsterSpeed:F2} m/s, human: {humanSpeed:F2} " +
                                  "m/s) - the player keeps his speed.", LogCat.VR);
                return;
            }

            var factor = Mathf.Clamp(monsterSpeed / humanSpeed, _minSpeedFactor, _maxSpeedFactor);
            _originalMoveSpeed = _rig.MoveSpeed;
            _originalRunSpeed = _rig.RunSpeed;
            _hasSpeedScale = true;

            // DeveloperConfig.EnableTransformAbsoluteSpeed: the monster's own speeds - moving is its run loop (Gothic's
            // default walk mode, a wolf 4.6 m/s), sprinting half again; a light stick is its walk. Scaling the VR speeds
            // (1.5 m/s moving) left a wolf at 2.1 m/s.
            if (_configService.Dev.EnableTransformAbsoluteSpeed)
            {
                _rig.MoveSpeed = monsterSpeed;
                _rig.RunSpeed = monsterSpeed * _monsterSprintFactor;
                Logger.Log($"[VRTransform] Speed {monsterSpeed:F2} m/s, sprint {_rig.RunSpeed:F2} m/s " +
                           $"({run}).", LogCat.VR);
            }
            else
            {
                _rig.MoveSpeed *= factor;
                _rig.RunSpeed *= factor;
                Logger.Log($"[VRTransform] Speed x{factor:F2} ({run}: {monsterSpeed:F2} m/s, human {humanSpeed:F2} " +
                           "m/s).", LogCat.VR);
            }
            _monsterMoveSpeed = _rig.MoveSpeed;
            _monsterRunSpeed = _rig.RunSpeed;
        }

        /// <summary>
        /// DeveloperConfig.EnableTransformLandSpeedKept: water sets the player's speeds (swimming, wading) and back on
        /// land the human ones it saved at the start - a meatbug left the water faster than it should, a shadowbeast
        /// slower. On land the monster's speeds are set again.
        /// </summary>
        private void KeepMonsterSpeed()
        {
            if (!_hasSpeedScale || !_configService.Dev.EnableTransformLandSpeedKept || _rig == null ||
                IsInWater() || _waterDepth > 0f)
                return;
            if (Mathf.Approximately(_rig.MoveSpeed, _monsterMoveSpeed) &&
                Mathf.Approximately(_rig.RunSpeed, _monsterRunSpeed))
                return;

            _rig.MoveSpeed = _monsterMoveSpeed;
            _rig.RunSpeed = _monsterRunSpeed;
            Logger.Log($"[VRTransform] Back on land - speed {_monsterMoveSpeed:F2} m/s again.", LogCat.VR);
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroWaterBody: swimming moves the player without a controller velocity (~0) - the
        /// movement between frames counts too: the monster drifted while it swam.
        /// </summary>
        private Vector3 GetPlayerVelocity()
        {
            var velocity = _rig != null ? _rig.CharacterController.velocity : Vector3.zero;
            if (!_configService.Dev.EnableHeroWaterBody || _rig == null || Time.deltaTime <= 0f)
                return velocity;

            var position = _rig.Root.position;
            var moved = (position - _lastPlayerPosition) / Time.deltaTime;
            _lastPlayerPosition = position;
            if (moved.magnitude < _maxMovedSpeed)
                _movedVelocity = Vector3.Lerp(_movedVelocity, moved, _movedSmoothing);
            return new Vector2(_movedVelocity.x, _movedVelocity.z).sqrMagnitude >
                   new Vector2(velocity.x, velocity.z).sqrMagnitude
                ? _movedVelocity
                : velocity;
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroSwimSounds: the swimming monster sounds like the swimming hero - drifting little
        /// waves, swimming the strokes (its swim loops have no sounds of their own).
        /// </summary>
        private void UpdateSwimSounds(float speed)
        {
            if (!_configService.Dev.EnableHeroSwimSounds || !_isSwimming || _monster?.PrefabProps?.NpcSound == null)
            {
                _nextSwimSoundTime = 0f;
                return;
            }
            if (Time.time < _nextSwimSoundTime)
                return;

            var isMoving = speed >= _walkSpeed;
            _nextSwimSoundTime = Time.time + (!isMoving ? _swimDriftSeconds : speed >= _runSpeed
                ? _swimFastStrokeSeconds
                : _swimStrokeSeconds);
            var clip = _audioService.GetRandomSoundClip(_swimSound);
            if (clip != null)
                _monster.PrefabProps.NpcSound.PlayOneShot(clip, isMoving ? 1f : _swimDriftVolume);
        }

        private static float GetHorizontalSpeed(AnimationTrack track)
        {
            return track == null ? 0f : new Vector2(track.MovementSpeed.x, track.MovementSpeed.z).magnitude;
        }

        private bool IsTriggerJustPressed(VrHandSide side)
        {
            // Simulator: R = attack, F = transform back.
            if (_vrPlayerService.IsSimulator)
                return side == _orbHand
                    ? Keyboard.current[Key.F].wasPressedThisFrame
                    : Keyboard.current[Key.R].wasPressedThisFrame;
            return VrControls.GetButton(side, VrButton.Trigger).IsJustActivated;
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
