using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Components;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrHeroBody): the hero's own body (current armor) under the VR head - torso, legs and
    /// arms. The hands stay the VR hands: the model's hands and head are hidden (bones scaled to ~0), its arms reach
    /// for the VR hands with a two-bone IK (elbows down and out), the forearms roll with the hands. Legs and torso play
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly Gothic.Core.Manager.AudioService _audioService;
        [Inject] private readonly Gothic.Core.Services.Audio.GroundSoundService _groundSoundService;

        // DeveloperConfig.EnableHeroBodySounds: the body's animations sound like an NPC's - steps by the ground below
        // (*eventSFXGrnd), the swim strokes when the crawl leads (Swim, SwimWaves). VRHeroMovement's stride steps
        // are off then.
        public static bool IsBodySounding { get; private set; }
        private const float _bodySoundVolume = 1f;
        private AudioSource _bodySoundSource;

        // DeveloperConfig.EnableHeroWaterBody: the body looks at the water itself - the swim state flickered while
        // bobbing at the surface (chest -> knee deep for a moment = walking, not grounded = the fall animation).
        private const float _swimWaterDepth = 1.1f;
        private const float _waterSurfaceRayLength = 3f;
        private const float _bottomRayStart = 0.1f;
        private const float _floatAboveBottom = 0.15f;

        // DeveloperConfig.EnableHeroSwimSounds: Gothic's swim sounds by how you swim - drifting the waves (SwimWaves),
        // swimming slowly or the crawl the strokes (Swim, faster), backwards SwimBack.
        private const string _swimDriftSound = "SWIMWAVES";
        private const string _swimStrokeSound = "SWIM";
        private const string _swimBackSound = "SWIMBACK";
        private const float _swimDriftSeconds = 2.5f;
        private const float _swimSlowStrokeSeconds = 1.4f;
        private const float _swimCrawlStrokeSeconds = 0.8f;
        private const float _swimCrawlSoundWeight = 0.5f;
        private const float _swimDriftFallbackVolume = 0.5f;
        private Vector3 _lastPlayerPosition;
        private Vector3 _movedVelocity;
        private const float _movedSmoothing = 0.2f;
        private const float _maxMovedSpeed = 15f;
        private float _nextSwimSoundTime;
        private bool _isSwimSounding;

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
        private VrInteractable _backpack;
        private IVrRig _rig;
        private readonly List<Renderer> _renderers = new();

        // Rolled-up sleeves: armor meshes without forearms + bare forearms (naked body mesh) on the armor skeleton.
        private readonly List<(SkinnedMeshRenderer renderer, Mesh full, Mesh sleeveless)> _sleeveMeshes = new();
        private readonly List<SkinnedMeshRenderer> _bareForearms = new();
        // The bare forearms' meshes without and with the naked hands (DeveloperConfig.EnableHeroHands).
        private readonly Dictionary<SkinnedMeshRenderer, (Mesh forearms, Mesh withHands)> _bareForearmMeshes = new();
        private readonly List<Mesh> _createdMeshes = new();
        private bool _areSleevesRolledUp;
        // Triangles weighted on average more than this to a part (hand, forearm) belong to it (see CutArms).
        private const float _armCutWeight = 0.5f;
        private const float _bareForearmWeight = 0.3f;
        // Armor triangles reaching this far into the forearm stay (a short overlap with the bare forearm).
        private const float _sleeveKeepWeight = 0.75f;

        private readonly ArmIk _leftArm = new("L");
        private readonly ArmIk _rightArm = new("R");

        // DeveloperConfig.EnableHeroHands + Immersion option "Hero's hands": the model's own hands (fists, with the
        // armor's gloves) are the VR hands - the backend's hand visuals are hidden meanwhile.
        private readonly List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> _handMeshes = new();
        private readonly List<Renderer> _hiddenVrHandRenderers = new();
        private readonly HashSet<Transform> _hiddenVrHandVisuals = new();
        private bool _isHeroHandsOption;
        private bool _areHeroHandsShown;

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

        // DeveloperConfig.EnableHeroFallCamera: knocked out / dead body and camera (UpdateDown).
        private enum DownPhase
        {
            None,
            Knockout,
            Dead,
            Rising
        }

        private const float _downFloorMargin = 0.03f;
        private const float _minDownEyeHeight = 0.2f;
        private const float _deathMenuDelay = 4f;
        private DownPhase _downPhase;

        /// <summary>
        /// The body lies or stands up (EnableHeroFallCamera) - the lowered camera isn't crouching (VRHeroMovement).
        /// </summary>
        public static bool IsHeroDown { get; private set; }
        private readonly Queue<string> _downAnimations = new();
        private string _downAnimation;
        private Vector3 _downForward;
        private float _downBaseCameraOffset;
        private float _downStartEyeHeight = -1f;
        private float _deathTime;
        private bool _isDeathMenuOpened;
        private bool _wasMenuOpenWhileDead;
        private Transform[] _bodyBones;


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
            ReadHeroHandsOption();
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
            SetHeroHandsShown(false);
            EndDown();
            DestroyCreatedMeshes();
        }

        private void OnDisable()
        {
            SetHeroHandsShown(false);
            EndDown();
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.HeroHands)
                ReadHeroHandsOption();
        }

        private void ReadHeroHandsOption()
        {
            _isHeroHandsOption = _configService.Dev.EnableHeroHands && _configService.Gothic != null &&
                                 _configService.Gothic.GetInt(VRConstants.IniNames.HeroHands, 0) == 1;
        }

        /// <summary>
        /// The model's hands in the body mesh or cut out, the backend's hand visuals hidden or shown.
        /// </summary>
        private void SetHeroHandsShown(bool isShown)
        {
            if (isShown == _areHeroHandsShown)
            {
                if (isShown)
                    HideVrHands(); // A hand visual can switch its renderers on again (hover/grab poses).
                return;
            }

            _areHeroHandsShown = isShown;
            ApplyArmMeshes();
            _leftArm.IsFist = isShown;
            _rightArm.IsFist = isShown;

            if (isShown)
            {
                HideVrHands();
            }
            else
            {
                foreach (var vrRenderer in _hiddenVrHandRenderers)
                {
                    if (vrRenderer != null)
                        vrRenderer.enabled = true;
                }
                _hiddenVrHandRenderers.Clear();
                _hiddenVrHandVisuals.Clear();
            }
            Logger.Log($"[VRHeroBody] Hero's hands {(isShown ? "shown - VR hands hidden" : "hidden - VR hands back")}",
                LogCat.VR);
        }

        /// <summary>
        /// Only the hand model's own renderers, collected once per hand visual. Whatever is attached later (spell VFX -
        /// thrown on as the projectile, item popups, the held item) stays visible: hiding them every frame left the
        /// charged spell and the flying projectile without their effect, just the light.
        /// </summary>
        private void HideVrHands()
        {
            foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
            {
                var handVisual = _rig?.GetHandVisual(side);
                if (handVisual == null || !_hiddenVrHandVisuals.Add(handVisual))
                    continue;
                foreach (var vrRenderer in handVisual.GetComponentsInChildren<Renderer>(true))
                {
                    if (vrRenderer.enabled && IsVrHandModelRenderer(vrRenderer) &&
                        !_hiddenVrHandRenderers.Contains(vrRenderer))
                        _hiddenVrHandRenderers.Add(vrRenderer);
                }
            }

            // A hand visual can switch its renderers on again (hover/grab poses).
            foreach (var vrRenderer in _hiddenVrHandRenderers)
            {
                if (vrRenderer != null && vrRenderer.enabled)
                    vrRenderer.enabled = false;
            }
        }

        private static bool IsVrHandModelRenderer(Renderer vrRenderer)
        {
            // Not what the hand holds (items, the ring's spell rune), no effects or texts.
            return vrRenderer is MeshRenderer or SkinnedMeshRenderer &&
                   vrRenderer.GetComponent<TMPro.TMP_Text>() == null &&
                   vrRenderer.GetComponentInParent<Gothic.Core.Adapters.Vob.VobLoader>() == null;
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroFallCamera: knocked out (BsUnconscious) or dead (BsDead) the body falls on its back
        /// (T_STAND_2_WOUNDEDB, S_WOUNDEDB / T_DEADB, S_DEADB) and stands up again (T_WOUNDEDB_2_STAND). The camera
        /// follows the animated head down and up - only its height (IVrRig.CameraYOffset, like a transformation);
        /// the body lies out from under it, so the view never moves sideways and where you look stays yours. Dead, the
        /// game menu opens after a moment (load a save, like Gothic). True while it drives the body.
        /// </summary>
        private bool UpdateDown(NpcContainer hero, Transform head, float feetY)
        {
            var bodyState = hero.Props.BodyState;
            // DeveloperConfig.EnableHeroDeath: dead until a save is loaded - after closing the death menu (Esc) the hero
            // stood up again (body state reset), could walk, took hits and recovered from "knockouts" with 1 HP.
            if (_configService.Dev.EnableHeroDeath && _downPhase == DownPhase.Dead)
            {
                if (bodyState != VmGothicEnums.BodyState.BsDead)
                {
                    Logger.LogWarning($"[VRHeroBody] Dead hero's body state was set to {bodyState} - stays dead",
                        LogCat.VR);
                    hero.Props.BodyState = bodyState = VmGothicEnums.BodyState.BsDead;
                }

                var menu = GetMenuHandler();
                var isMenuOpen = menu != null && menu.gameObject.activeSelf;
                if (_wasMenuOpenWhileDead && !isMenuOpen)
                    _contextInteractionService.LockPlayerInPlace(_configService.Dev.EnableKnockoutTurning);
                _wasMenuOpenWhileDead = isMenuOpen;
            }
            var isDown = bodyState is VmGothicEnums.BodyState.BsUnconscious or VmGothicEnums.BodyState.BsDead;
            if (!_configService.Dev.EnableHeroFallCamera || _animator == null || !_animator.IsValid ||
                _headBone == null || _standingScale <= 0f)
            {
                EndDown();
                return false;
            }

            if (_downPhase == DownPhase.None)
            {
                if (!isDown)
                    return false;
                StartDown(head);
            }

            if (bodyState == VmGothicEnums.BodyState.BsDead && _downPhase != DownPhase.Dead)
            {
                SetDownAnimations(_downPhase == DownPhase.Knockout
                    ? new[] { "T_WOUNDEDB_2_DEADB", "S_DEADB" }
                    : new[] { "T_DEADB", "S_DEADB" });
                _downPhase = DownPhase.Dead;
                _deathTime = Time.time;
            }
            else if (bodyState == VmGothicEnums.BodyState.BsUnconscious && _downPhase == DownPhase.Rising)
            {
                SetDownAnimations("T_STAND_2_WOUNDEDB", "S_WOUNDEDB");
                _downPhase = DownPhase.Knockout;
            }
            else if (!isDown && _downPhase == DownPhase.Knockout)
            {
                SetDownAnimations("T_WOUNDEDB_2_STAND");
                _downPhase = DownPhase.Rising;
            }

            if (_downAnimation != null && _animator.IsDone(_downAnimation) && _downAnimations.Count > 0)
            {
                _downAnimation = _downAnimations.Dequeue();
                _animator.Play(_downAnimation);
            }
            if (_downPhase == DownPhase.Rising && (_downAnimation == null || _animator.IsDone(_downAnimation)))
            {
                EndDown();
                return false;
            }

            // The pose: the body keeps its size and direction, lies on the floor (lowest bone), the head under the eyes.
            var scale = _standingScale;
            _body.transform.localScale = Vector3.one * scale;
            _body.transform.SetPositionAndRotation(new Vector3(head.position.x, feetY, head.position.z),
                Quaternion.LookRotation(_downForward));
            _animator.Update(Time.deltaTime);

            if (_bodyBones == null || _bodyBones.Length == 0 || _bodyBones[0] == null)
                _bodyBones = _body.GetComponentsInChildren<Transform>();
            var lowestY = float.MaxValue;
            foreach (var bone in _bodyBones)
            {
                if (bone != null && bone != _body.transform)
                    lowestY = Mathf.Min(lowestY, bone.position.y);
            }
            var headPosition = _headBone.position;
            _body.transform.position += new Vector3(head.position.x - headPosition.x,
                lowestY < float.MaxValue ? feetY + _downFloorMargin * scale - lowestY : 0f,
                head.position.z - headPosition.z);

            // The eyes go with the animated head, relative to where they were when he fell.
            var eyeHeight = _headBone.position.y + _neckBelowEyes * scale - feetY;
            if (_downStartEyeHeight < 0f)
                _downStartEyeHeight = eyeHeight;
            if (_rig != null)
                _rig.CameraYOffset = _downBaseCameraOffset +
                                     Mathf.Max(eyeHeight, _minDownEyeHeight) - _downStartEyeHeight;

            SolveArms();

            if (_downPhase == DownPhase.Dead && !_isDeathMenuOpened && Time.time - _deathTime >= _deathMenuDelay)
            {
                _isDeathMenuOpened = true;
                var menu = GetMenuHandler();
                if (menu != null && !menu.gameObject.activeSelf)
                    menu.ToggleVisibility();
                Logger.Log("[VRHeroBody] Hero is dead - game menu opened", LogCat.VR);
            }
            return true;
        }

        private Gothic.Core.Adapters.UI.Menus.MenuHandler GetMenuHandler()
        {
            var player = _contextInteractionService.GetCurrentPlayerController();
            var vrPlayer = player != null ? player.GetComponent<VRPlayer>() : null;
            return vrPlayer != null ? vrPlayer.MenuHandler : null;
        }

        private void StartDown(Transform head)
        {
            _downForward = Vector3.ProjectOnPlane(_body.transform.forward, Vector3.up);
            if (_downForward.sqrMagnitude < 0.0001f)
                _downForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            _downForward = _downForward.sqrMagnitude < 0.0001f ? Vector3.forward : _downForward.normalized;

            _downBaseCameraOffset = _rig != null ? _rig.CameraYOffset : 0f;
            _downStartEyeHeight = -1f;
            _isDeathMenuOpened = false;
            _wasMenuOpenWhileDead = false;
            _bodyBones = null;
            _downPhase = DownPhase.Knockout;
            IsHeroDown = true;
            SetDownAnimations("T_STAND_2_WOUNDEDB", "S_WOUNDEDB");
            Logger.Log("[VRHeroBody] Hero down - body falls, camera follows its head", LogCat.VR);
        }

        private void SetDownAnimations(params string[] animations)
        {
            _downAnimations.Clear();
            foreach (var animation in animations)
            {
                if (_animator.HasAnimation(animation))
                    _downAnimations.Enqueue(animation);
            }
            _downAnimation = _downAnimations.Count > 0 ? _downAnimations.Dequeue() : null;
            if (_downAnimation != null)
                _animator.Play(_downAnimation);
        }

        /// <summary>
        /// Back to the normal body; the camera height as before the fall.
        /// </summary>
        private void EndDown()
        {
            if (_downPhase == DownPhase.None)
                return;

            _downPhase = DownPhase.None;
            IsHeroDown = false;
            _downAnimation = null;
            _downAnimations.Clear();
            if (_rig != null)
                _rig.CameraYOffset = _downBaseCameraOffset;
            Logger.Log("[VRHeroBody] Hero up again - camera height restored", LogCat.VR);
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
            _rig ??= _contextInteractionService.GetCurrentPlayerController()?.GetComponent<IVrRig>();
            SetSleevesRolledUp(isVisible && IsBackpackInHand());
            SetHeroHandsShown(isVisible && _isHeroHandsOption && _handMeshes.Count > 0);
            if (!isVisible)
                return;

            // Under the head, a bit behind it - looking down shows the chest, not the inside of the neck. Scaled to the
            // player's eye height above the floor, so the feet stay on the ground (sitting/standing mode, any height).
            var head = Camera.main.transform;
            var forward = GetBodyForward(head);

            var feetY = _rig != null ? _rig.Root.position.y : head.position.y - _modelHeadHeight;
            if (UpdateDown(hero, head, feetY))
                return;
            // Crouching (sneaking) lowers the head, not the body size - keep the standing scale then.
            var isCrouching = _rig != null && _rig.IsCrouching;
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

            SolveArms();
        }

        private void SolveArms()
        {
            _leftArm.FistRoll = _configService.Dev.HeroHandsRollDegrees;
            _rightArm.FistRoll = _configService.Dev.HeroHandsRollDegrees;
            _leftArm.Solve(_vrPlayerService.GetHandModelGo(VrHandSide.Left), _body.transform, _rig?.LeftHand,
                GetSecondHandGripPose(VrHandSide.Left));
            _rightArm.Solve(_vrPlayerService.GetHandModelGo(VrHandSide.Right), _body.transform, _rig?.RightHand,
                GetSecondHandGripPose(VrHandSide.Right));
        }

        /// <summary>
        /// The second hand on an item held with both hands is shown on its grip right before rendering (XRI physical
        /// hands) - after this body is skinned. The fist goes there now, not where the controller is.
        /// </summary>
        private Pose? GetSecondHandGripPose(VrHandSide side)
        {
            var hands = _vrPlayerService.VRContextInteractionService?.PhysicsHands;
            if (hands == null)
                return null;

            foreach (var hand in hands)
            {
                if (hand.Side == side && hand.TryGetSecondHandGripPose(out var position, out var rotation))
                    return new Pose(position, rotation);
            }
            return null;
        }

        /// <summary>
        /// Legs and torso like the hero in Gothic: idle, walk/run (played at the real speed - no sliding feet), walking
        /// backwards, strafing, turning on the spot, falling.
        /// </summary>
        private void UpdateAnimation(float scale, bool isCrouching)
        {
            if (_animator == null || !_animator.IsValid)
                return;
            _animator.OverlayOverride = VRHeroMovement.SpeedOverlay;

            var yaw = _body.transform.eulerAngles.y;
            if (Time.deltaTime > 0f)
                _bodyYawSpeed = Mathf.Lerp(_bodyYawSpeed, Mathf.DeltaAngle(_lastBodyYaw, yaw) / Time.deltaTime, 0.3f);
            _lastBodyYaw = yaw;

            var velocity = _rig != null ? _rig.CharacterController.velocity : Vector3.zero;
            // DeveloperConfig.EnableHeroWaterBody: swimming moves the player without a controller velocity (~0) - the
            // movement between frames counts too (no crawl, no strokes while swimming with the stick/WASD).
            if (_configService.Dev.EnableHeroWaterBody && _rig != null && Time.deltaTime > 0f)
            {
                var position = _rig.Root.position;
                var moved = (position - _lastPlayerPosition) / Time.deltaTime;
                _lastPlayerPosition = position;
                // A teleport isn't a speed.
                if (moved.magnitude < _maxMovedSpeed)
                    _movedVelocity = Vector3.Lerp(_movedVelocity, moved, _movedSmoothing);
                if (new Vector2(_movedVelocity.x, _movedVelocity.z).sqrMagnitude >
                    new Vector2(velocity.x, velocity.z).sqrMagnitude)
                    velocity = _movedVelocity;
            }
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var speed = horizontal.magnitude;
            var local = _body.transform.InverseTransformDirection(horizontal);

            var bodyState = _npcService.GetHeroContainer()?.Props.BodyState;
            var isMoving = speed >= _idleSpeed;
            var isBackwards = local.z < -_backwardsShare * speed;

            // DeveloperConfig.EnableHeroWaterBody: deep water is swimming even while the state flickers, any water
            // below is no fall.
            var isInWater = false;
            if (_configService.Dev.EnableHeroWaterBody && _rig != null)
            {
                // Own rays from the feet: the water service's ray from the bottom up hit the player's own capsule while
                // floating above the bottom (no water found - the body stood upright in deep water).
                var feet = _rig.Root.position;
                if (Physics.Raycast(feet, Vector3.up, out var surfaceHit, _waterSurfaceRayLength,
                        1 << Gothic.Core.Const.Constants.WaterLayer, QueryTriggerInteraction.Collide))
                {
                    isInWater = true;
                    var bottom = Physics.Raycast(feet + Vector3.up * _bottomRayStart, Vector3.down, out var bottomHit,
                        _swimWaterDepth, 1 << Gothic.Core.Const.Constants.DefaultLayer, QueryTriggerInteraction.Ignore)
                        ? bottomHit.distance - _bottomRayStart
                        : _swimWaterDepth;
                    var isFloating = bottom > _floatAboveBottom;
                    if (isFloating && surfaceHit.distance + bottom >= _swimWaterDepth &&
                        bodyState != VmGothicEnums.BodyState.BsDive)
                        bodyState = VmGothicEnums.BodyState.BsSwim;
                }
            }

            string animation;
            // Swimming: floating upright (s_Swim), the crawl (s_SwimF) only blended in with the speed - slowly, so the
            // legs come up towards the surface instead of jumping behind the camera.
            var swimCrawl = bodyState == VmGothicEnums.BodyState.BsSwim && isMoving && !isBackwards
                ? Mathf.Clamp01(speed / _swimCrawlFullSpeed) * _swimCrawlMaxWeight
                : 0f;
            _animator.SetOverlay("S_SWIMF", swimCrawl);
            UpdateSwimSounds(bodyState == VmGothicEnums.BodyState.BsSwim, isMoving, isBackwards, swimCrawl);

            if (bodyState == VmGothicEnums.BodyState.BsSwim)
                animation = isMoving && isBackwards ? "S_SWIMB" : "S_SWIM";
            else if (bodyState == VmGothicEnums.BodyState.BsDive)
                animation = isMoving ? "S_DIVEF" : "S_DIVE";
            else if (_rig != null && !_rig.IsGrounded && velocity.y < -1f && !isInWater &&
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
                var backpack = System.Linq.Enumerable.FirstOrDefault(
                    FindObjectsByType<VRBackpack>(FindObjectsSortMode.None), b => !b.IsNpcBackpack);
                if (backpack == null)
                    return false;
                _backpack = backpack.GetComponentInParent<VrInteractable>() ?? VrInteractable.Find(backpack);
                if (_backpack == null)
                    return false;
            }
            if (_rig == null)
                return false;
            foreach (var hand in _rig.HandGrabbers)
            {
                if (hand.Held == _backpack)
                    return true;
            }
            return false;
        }

        private void SetSleevesRolledUp(bool isRolledUp)
        {
            isRolledUp &= _bareForearms.Count > 0;
            if (isRolledUp == _areSleevesRolledUp)
                return;

            _areSleevesRolledUp = isRolledUp;
            ApplyArmMeshes();
        }

        /// <summary>
        /// The armor's and the bare forearms' meshes for both states: rolled-up sleeves (backpack in a hand) and the
        /// hero's own hands (DeveloperConfig.EnableHeroHands). With both the naked hands come with the bare forearms -
        /// the sleeveless armor has none, the fists vanished while the backpack was held.
        /// </summary>
        private void ApplyArmMeshes()
        {
            foreach (var (renderer, withHands, withoutHands) in _handMeshes)
            {
                if (renderer != null)
                    renderer.sharedMesh = _areHeroHandsShown ? withHands : withoutHands;
            }
            foreach (var (renderer, full, sleeveless) in _sleeveMeshes)
            {
                if (renderer == null)
                    continue;
                if (_areSleevesRolledUp)
                    renderer.sharedMesh = sleeveless;
                else if (!_handMeshes.Exists(i => i.renderer == renderer))
                    renderer.sharedMesh = full;
            }
            foreach (var forearm in _bareForearms)
            {
                if (forearm == null)
                    continue;
                forearm.enabled = _areSleevesRolledUp;
                if (_bareForearmMeshes.TryGetValue(forearm, out var meshes))
                    forearm.sharedMesh = _areHeroHandsShown && meshes.withHands != null ? meshes.withHands : meshes.forearms;
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

            SetHeroHandsShown(false);
            if (_body != null)
                Destroy(_body);
            DestroyCreatedMeshes();
            _handMeshes.Clear();
            _renderers.Clear();
            _sleeveMeshes.Clear();
            _bareForearms.Clear();
            _bareForearmMeshes.Clear();
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

            // The VR hands replace the model's hands: cut out of the mesh. Collapsing the hand bone pulled the wrist
            // ring into one point - the forearm became a spike.
            foreach (var skinned in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var withHands = skinned.sharedMesh;
                var withoutHands = CutArms(withHands, skinned.bones, ArmCut.WithoutHands);
                if (withoutHands == null)
                    continue;
                skinned.sharedMesh = withoutHands;
                // DeveloperConfig.EnableHeroHands: the full mesh shows the model's hands as the VR hands.
                if (withoutHands.triangles.Length != withHands.triangles.Length)
                    _handMeshes.Add((skinned, withHands, withoutHands));
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
            BuildSleeves(props, mdhName, bones);

            _animator = new VRHeroBodyAnimator(_animationService, props.MdsNameBase, props.MdsNameOverlay, bones);
            if (_configService.Dev.EnableHeroBodySounds && _configService.Dev.EnableFootstepSounds && _animator.IsValid)
            {
                _animator.SoundEvent += OnBodySoundEvent;
                IsBodySounding = true;
            }
            Logger.Log($"[VRHeroBody] Built {props.MdmName} (head height {_modelHeadHeight:F2} m, " +
                       $"animated: {_animator.IsValid}, bare forearms: {_bareForearms.Count}).", LogCat.VR);
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroSwimSounds: drifting, swimming slowly, the crawl and backwards each sound their own.
        /// </summary>
        private void UpdateSwimSounds(bool isSwimming, bool isMoving, bool isBackwards, float crawlWeight)
        {
            _isSwimSounding = isSwimming && _configService.Dev.EnableHeroSwimSounds;
            if (!_isSwimSounding)
            {
                _nextSwimSoundTime = 0f;
                return;
            }
            if (Time.time < _nextSwimSoundTime)
                return;

            string sound;
            float seconds;
            if (!isMoving)
                (sound, seconds) = (_swimDriftSound, _swimDriftSeconds);
            else if (isBackwards)
                (sound, seconds) = (_swimBackSound, _swimSlowStrokeSeconds);
            else if (crawlWeight >= _swimCrawlSoundWeight)
                (sound, seconds) = (_swimStrokeSound, _swimCrawlStrokeSeconds);
            else
                (sound, seconds) = (_swimStrokeSound, _swimSlowStrokeSeconds);
            _nextSwimSoundTime = Time.time + seconds;
            // SwimWaves isn't in every game's sounds (Mroczne Tajemnice) - drifting is then the strokes' little waves,
            // quieter.
            if (!PlayBodySound(sound) && sound == _swimDriftSound)
                PlayBodySound(_swimStrokeSound, _swimDriftFallbackVolume);
        }

        private bool PlayBodySound(string name, float volume = _bodySoundVolume)
        {
            var clip = _audioService.GetRandomSoundClip(name);
            if (clip == null)
                return false;
            if (_bodySoundSource == null)
            {
                _bodySoundSource = gameObject.AddComponent<AudioSource>();
                _bodySoundSource.playOnAwake = false;
                // The player's own body: right with him, no distance falloff.
                _bodySoundSource.spatialBlend = 0f;
            }
            _bodySoundSource.PlayOneShot(clip, volume);
            return true;
        }

        private void OnBodySoundEvent(ZenKit.IEventSoundEffect sfx)
        {
            var name = sfx.Name;
            // Swimming sounds by UpdateSwimSounds - no steps or strokes of the animations on top.
            if (string.IsNullOrEmpty(name) || _isSwimSounding)
                return;
            var feet = _rig != null ? _rig.Root.position : transform.position;
            if (name.StartsWith(Gothic.Core.Domain.Audio.GroundSoundDomain.GroundEventPrefix))
                name = _groundSoundService.GetSoundName(
                    name.Substring(Gothic.Core.Domain.Audio.GroundSoundDomain.GroundEventPrefix.Length), feet);

            PlayBodySound(name);
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
                _bareForearmMeshes[forearm] = (forearmMesh,
                    CutArms(nakedRenderer.sharedMesh, nakedRenderer.bones, ArmCut.ForearmsAndHands));
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
            // The whole body without the model's hands (the VR hands are shown).
            WithoutHands,
            // Only the forearms (naked body: rolled-up sleeves).
            Forearms,
            // The forearms and the hands (rolled-up sleeves with the hero's own hands, EnableHeroHands).
            ForearmsAndHands,
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
                        ArmCut.ForearmsAndHands => forearm + hand > _bareForearmWeight,
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
        /// Analytic two-bone IK: upper arm + forearm reach for the VR hand, the elbow bends towards a hint below and to
        /// the outside. The wrist always ends at the VR wrist (the model's hands are cut out of the mesh): out of reach
        /// the arm is stretched, tapered - 40 % of the missing length in the upper arm, 60 % in the forearm, so the most
        /// stretch sits near the wrist (skinned vertices between the moved joints stretch, the shoulder stays).
        /// The forearm rolls around its axis like the VR hand (palms of both models aligned).
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
            /// DeveloperConfig.HeroHandsRollDegrees: turn of the fist's thumb around its fingers, outwards (0 = up).
            /// </summary>
            public float FistRoll { get; set; }

            public ArmIk(string side)
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
                BindModelPalm(bones, handMeshes);
            }

            /// <summary>
            /// Gothic hands have one finger bone (FINGER0) and the item slot in the palm (ZS_RIGHTHAND/ZS_LEFTHAND).
            /// </summary>
            private void BindModelPalm(Dictionary<string, Transform> bones,
                List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes)
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
            private Vector3? FindThumbAxis(List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes,
                Vector3 fingersLocal, Vector3 slotSideLocal)
            {
                var across = Vector3.Cross(fingersLocal, slotSideLocal).normalized;
                var side = Vector3.Cross(across, fingersLocal).normalized;
                foreach (var (renderer, withHands, _) in handMeshes)
                {
                    var handIndex = renderer == null || withHands == null ? -1 : System.Array.IndexOf(renderer.bones, _hand);
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
                        if (weights[i].boneIndex0 != handIndex || weights[i].weight0 <= _armCutWeight)
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
                                          $"{longest / Mathf.Max(shortest, 1e-9f):F2}) - using the item slot's side.", LogCat.VR);
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
            /// <summary>
            /// The fist where the VR hand is (its middle at the hand's position), turned like the controller: fingers
            /// along the controller's forward, the thumb along its up (DeveloperConfig.HeroHandsRollDegrees turns it).
            /// </summary>
            private void SolveFist(Vector3 handPosition, Transform body, Quaternion handRotation)
            {
                _fore.localPosition = _foreRestLocal;
                _hand.localPosition = _handRestLocal;

                var fingers = handRotation * Vector3.forward;
                var thumb = Quaternion.AngleAxis(_side == "L" ? FistRoll : -FistRoll, fingers) *
                            (handRotation * Vector3.up);
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
                var missing = distance - (upperLength + foreLength) * 0.99f;
                if (missing > 0f)
                {
                    upperLength += missing * _upperStretchShare;
                    foreLength += missing * (1f - _upperStretchShare);
                }

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
}
