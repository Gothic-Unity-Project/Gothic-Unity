using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Audio;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Marvin;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    [RequireComponent(typeof(VRPlayer))]
    public class VRSwimDive : MonoBehaviour, IMarvinPropertyCollector
    {
        private IVrRig _rig;
        
        private INpc _playerVob;
        private NpcContainer _heroContainer;
        private IAiHuman _playerAi;
        private SfxModel _sfxSwimSound;
        private SfxModel _sfxDiveSound;
        private SfxModel _sfxSwim2DiveSound;
        private SfxModel _sfxSwim2HangSound; // Normally when pulling out of water only, but we also use when dive -> swim.
        
        // FIXME - Bubbles are too big on screen. Needs more love.
        // private string _diveBubbleParticleName;
        
        private float _initialGravity;
        private float _initialMoveSpeed;
        private float _initialRunSpeed;
        private float _initialFallSpeed;

        private VmGothicEnums.WalkMode _mode = VmGothicEnums.WalkMode.Walk;

        // Swim + Dive properties
        private Vector3 _currentVelocity;
        // If we lift the grips, we set it to false again to re-enable sounds later.
        private bool _isSwimDiveForceLeftJustStarted;
        private bool _isSwimDiveForceRightJustStarted;
        // SFX cooldown identifiers (to prevent calling multiple swim sounds at once)
        private readonly Guid _swimDiveSfxUuid = Guid.NewGuid();
        
        // Water Bobbing
        private Coroutine _waterBobbingCoroutine;
        [SerializeField] private float _waterBobAmplitude = 0.001f;
        [SerializeField] private float _waterBobFrequency = 2.5f;

        [Tooltip("No water surface this far (m) above the player while swimming or diving: out of the water.")]
        [SerializeField] private float _outOfWaterCheckHeight = 100f;

        // Swim movement
        [SerializeField] private float _swimStartDiveVerticalVelocity = -5f;
        [SerializeField] private float _swimHandMovementMultiplier = 5f;
        [SerializeField] private float _swimVelocityFadeRate = 0.25f; // e.g., 0.25==75% less velocity with each second
        
        // Dive movement
        [SerializeField] private float _diveHandMovementMultiplier = 2.5f;
        [SerializeField] private float _diveVelocityFadeRate = 0.25f; // 0.25==75% less velocity with each second
        private bool _isDivingForceStarted; // If we lift the grips, we set it to false again to re-enable sounds later.
        
        // FIXME - Bubbles are too big on screen. Needs more love.
        // private GameObject _diveBubbles;
        
        
        [Header("Debugging")]
        [SerializeField] private bool _debugStartDive;


        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrSfxPlayer _vrSfxPlayer;
        [Inject] private readonly ConfigService _configService;

        private void Awake()
        {
            _rig = GetComponent<IVrRig>();
        }

        private void Start()
        {
            Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);

			GlobalEventDispatcher.ZenKitBootstrapped.AddListener(() =>
            {
                // DeveloperConfig.EnableNpcWater: G2 has the same Humans.mds swim animations - sounds are looked up
                // defensively below and played null-safe.
                if (_contextGameVersionService.IsGothic2() && !_configService.Dev.EnableNpcWater)
                {
                    Logger.LogError("Swimming is not yet implemented for G2. Skipping...", LogCat.VR);
                    return;
                }
                
                var mds = _resourceCacheService.TryGetModelScript("Humans");
                if (mds == null)
                {
                    Logger.LogError("Humans.mds failed to load or parse. Swimming/diving sounds won't be available.",
                        LogCat.VR);
                    return;
                }

                // A mod's Humans.mds can be missing an animation entry or its sound effect event entirely
                // (malformed/unsupported MDS syntax) - look it up defensively instead of crashing the
                // ZenKitBootstrapped listener chain (which would also abort every listener after this one).
                SfxModel FindSwimSfx(string animName)
                {
                    var anim = mds.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animName));
                    var sfxName = anim?.SoundEffects.FirstOrDefault()?.Name;
                    if (sfxName == null)
                    {
                        Logger.LogWarning($"Humans.mds: '{animName}' animation or its sound effect is missing.",
                            LogCat.VR);
                        return null;
                    }
                    return _vmCacheService.TryGetSfxData(sfxName);
                }

                // FIXME - In G1, there are different sounds for SwimBack, Sideways, and Forward
                _sfxSwimSound = FindSwimSfx("s_SwimF");
                _sfxDiveSound = FindSwimSfx("s_DiveF");
                _sfxSwim2DiveSound = FindSwimSfx("t_Swim_2_Dive");
                _sfxSwim2HangSound = FindSwimSfx("t_Swim_2_Hang");

                // FIXME - Bubbles are too big on screen. Needs more love.
                // Bubble settings
                // {
                //     _diveBubbleParticleName = diveAnim.ParticleEffects.First().Name;
                //     _diveBubbles = _meshService.CreateVobPfx(_diveBubbleParticleName, parent: _playerController.ScreenFader.gameObject);
                //     _diveBubbles.transform.localPosition = new Vector3(0, 0.1f, 0.2f);
                //     var particleRenderer = _diveBubbles.GetComponentInChildren<ParticleSystemRenderer>();
                //     particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
                //
                //     var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                //     particleRenderer.mesh = go.GetComponent<MeshFilter>().sharedMesh;
                //
                //     go.SetActive(false);
                //     Destroy(go);
                //     
                //     _diveBubbles.SetActive(false);
                // }
            });

            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() =>
            {
                _heroContainer = ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData()!;
                _playerVob = _heroContainer.Vob;
                _playerAi = (IAiHuman)_playerVob.Ai;

                // A loaded save (or new world) starts on land - leftovers of swimming/diving would keep the slow
                // water movement. The next FixedUpdate detects water again if we spawned in it.
                if (_configService.Dev.EnableNpcWater && (_playerAi.WaterLevel != (int)ZenGineConst.WaterLevel.Normal ||
                                                          _mode != VmGothicEnums.WalkMode.Walk))
                    LeaveWater();
            });
            
            _initialGravity = _rig.Gravity;
            _initialMoveSpeed = _rig.MoveSpeed;
            _initialRunSpeed = _rig.RunSpeed;
            _initialFallSpeed = _rig.MaxFallSpeed;
        }

        private void Update()
        {
            if (_playerAi == null)
                return;

            if (_mode == VmGothicEnums.WalkMode.Swim)
                HandleSwim();
            else if (_mode == VmGothicEnums.WalkMode.Dive)
                HandleDive();
        }
        
        private void FixedUpdate()
        {
            /*
             * 1. Check if we have water around us.
             * 2. Check if we need to "walk" as we are knee deep in water.
             * 3. Check if we need to swim as w are chest deep in water.
             */
            
			if (_playerVob == null)
				return;

            // FIXME - As we have different sizes of people in VR, we should use the size based on real heights.
            var kneeDeepHeight = _gameStateService.GuildValues.GetWaterDepthKnee((int)VmGothicEnums.Guild.GIL_HUMAN).ToMeter();
            var chestDeepHeight =  _gameStateService.GuildValues.GetWaterDepthChest((int)VmGothicEnums.Guild.GIL_HUMAN).ToMeter();
            var chestDeepHeightWithBuffer = chestDeepHeight + 2; // Do raycast a little bit longer than it needs to be, to ensure it's working.

            RaycastHit hit;

            // Water between our feet and our head found!
            if (Physics.Raycast(transform.position, Vector3.up, out hit, chestDeepHeightWithBuffer, 1 << Constants.WaterLayer))
            {
                var previousWaterLevel = _playerAi.WaterLevel;
                if (hit.distance < kneeDeepHeight)
                    _playerAi.WaterLevel = (int)ZenGineConst.WaterLevel.Normal;
                else if (hit.distance < chestDeepHeight)
                    _playerAi.WaterLevel = (int)ZenGineConst.WaterLevel.Knee;
                else
                    _playerAi.WaterLevel = (int)ZenGineConst.WaterLevel.Chest;

                if (previousWaterLevel != _playerAi.WaterLevel)
                    ChangeWaterBehavior();
            }
            // No water above us, but still swimming/diving: teleported out of the water (Marvin goto, savegame load,
            // world change) or fell through a waterfall. Walking out of the water passes knee depth instead. Diving
            // deeper than the ray above still has the surface far above us.
            else if (_playerAi.WaterLevel != (int)ZenGineConst.WaterLevel.Normal &&
                     !Physics.Raycast(transform.position, Vector3.up, _outOfWaterCheckHeight, 1 << Constants.WaterLayer))
            {
                LeaveWater();
            }
        }

        /// <summary>
        /// Back to walking from any water level, also from swimming or diving.
        /// </summary>
        private void LeaveWater()
        {
            Logger.Log($"[VRSwimDive] Out of the water while in {_mode} mode (teleported?). Walking again.",
                LogCat.VR);

            if (_mode == VmGothicEnums.WalkMode.Dive)
            {
                _playerService.StopDiving();
                _rig.Fade(0, float.MaxValue); // Immediately fade the screen back to normal (no fade)
            }

            _mode = VmGothicEnums.WalkMode.Walk;
            Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);
            _playerAi.WaterLevel = (int)ZenGineConst.WaterLevel.Normal;
            ChangeWaterBehavior();
        }
        
        /// <summary>
        /// Debug only
        /// </summary>
        private void OnValidate()
        {
            if (_debugStartDive)
            {
                _debugStartDive = false;

                if (_mode == VmGothicEnums.WalkMode.Swim && _rig != null)
                {
                    _rig.CharacterController.Move(Vector3.down * 5);
                    StartDive();
                }
            }
        }

        private void ChangeWaterBehavior()
        {
            if (_waterBobbingCoroutine != null)
            {
                _currentVelocity = Vector3.zero;
                StopCoroutine(_waterBobbingCoroutine);
                _waterBobbingCoroutine = null;
            }

            switch ((ZenGineConst.WaterLevel)_playerAi.WaterLevel)
            {
                case ZenGineConst.WaterLevel.Normal:
                    // Straight from swimming/diving to land (teleport, save load) - the Knee step was skipped.
                    if (_configService.Dev.EnableNpcWater)
                    {
                        if (_mode == VmGothicEnums.WalkMode.Dive)
                        {
                            _playerService.StopDiving();
                            _rig.Fade(0, float.MaxValue);
                        }
                        _mode = VmGothicEnums.WalkMode.Walk;
                        Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);
                    }

                    _rig.Gravity = _initialGravity;
                    _rig.MoveSpeed = _initialMoveSpeed;
                    _rig.RunSpeed = _initialRunSpeed;
                    _rig.CanSprint = true;
                    _rig.MaxFallSpeed = _initialFallSpeed;
                    
                    _contextInteractionService.SetWalkingControls();
                    break;
                case ZenGineConst.WaterLevel.Knee:
                    // Dive -> Swim (sometimes when pulling hard up, we are briefly knee deep and need to call the sound in here once)
                    if (_mode == VmGothicEnums.WalkMode.Dive)
                    {
                        _playerService.StopDiving();
                        _rig.Fade(0, float.MaxValue); // Immediately fade the screen back to normal (no fade)

                        PlaySfx(_sfxSwim2HangSound);

                        // FIXME - Bubbles are too big on screen. Needs more love.
                        // _diveBubbles.SetActive(false);
                    }
                    
                    // FIXME - Or is it "WalkMode.Water"?
                    _mode = VmGothicEnums.WalkMode.Walk;
                    Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);

                    _rig.Gravity = _initialGravity;
                    _rig.MoveSpeed = _initialMoveSpeed / 2;
                    _rig.RunSpeed = _initialMoveSpeed / 2; // No running, but it might be, that we run into deep water, then we need to slow it down like walking.
                    _rig.CanSprint = false;
                    _rig.MaxFallSpeed = _initialFallSpeed;
                    
                    _contextInteractionService.SetWaterWalkingControls();
                    break;
                case ZenGineConst.WaterLevel.Chest:
                    // Dive -> Swim
                    if (_mode == VmGothicEnums.WalkMode.Dive)
                    {
                        _playerService.StopDiving();
                        _rig.Fade(0, float.MaxValue); // Immediately fade the screen back to normal (no fade)

                        PlaySfx(_sfxSwim2HangSound);
                        
                        // FIXME - Bubbles are too big on screen. Needs more love.
                        // _diveBubbles.SetActive(false);
                    }

                    Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);
                    _mode = VmGothicEnums.WalkMode.Swim;
                    _rig.Gravity = 0f;
                    _rig.MaxFallSpeed = 0f;

                    _contextInteractionService.SetSwimmingControls();
                    
                    if (_waterBobbingCoroutine == null)
                        _waterBobbingCoroutine = StartCoroutine(WaterBobbing());
                    break;
                default:
                    throw new Exception($"Unknown value {_playerAi.WaterLevel} for water.");
            }

            UpdateHeroBodyState();
        }

        /// <summary>
        /// Scripts check C_BodyStateContains(other, BS_SWIM/BS_DIVE): monsters stop chasing a swimming hero
        /// (ZS_MM_Attack, AIV_MM_FollowInWater), no sleep/freeze spells in water, ...
        /// Other states (unconscious, ...) are never overwritten.
        /// </summary>
        private void UpdateHeroBodyState()
        {
            if (!_configService.Dev.EnableNpcWater || _heroContainer?.Props == null)
                return;

            var bodyState = _heroContainer.Props.BodyState;
            var isWaterOrMoving = bodyState is VmGothicEnums.BodyState.BsStand or VmGothicEnums.BodyState.BsWalk
                or VmGothicEnums.BodyState.BsRun or VmGothicEnums.BodyState.BsSwim or VmGothicEnums.BodyState.BsDive;
            if (!isWaterOrMoving)
                return;

            _heroContainer.Props.BodyState = _mode switch
            {
                VmGothicEnums.WalkMode.Swim => VmGothicEnums.BodyState.BsSwim,
                VmGothicEnums.WalkMode.Dive => VmGothicEnums.BodyState.BsDive,
                _ => VmGothicEnums.BodyState.BsStand
            };
        }

        private IEnumerator WaterBobbing()
        {
            var time = 0f;
            while (true)
            {
                time += Time.deltaTime;
                var offset = Mathf.Sin(time * _waterBobFrequency) * _waterBobAmplitude;
                _rig.CharacterController.Move(Vector3.down * offset);
                yield return null;
            }
        }
        
        private void StartDive()
        {
            _mode = VmGothicEnums.WalkMode.Dive;
            Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 1);
            
            // Reset velocity so that dive down is smoothed. Otherwise, we are on the ground already. ;-)
            // (After "forcing" diving by moving hands down.)
            _currentVelocity = Vector3.zero;
            
            _rig.Gravity = 0;
            _rig.MaxFallSpeed = 0;

            _contextInteractionService.SetDivingControls();

            _playerService.StartDiving();
            UpdateHeroBodyState();
            _rig.Fade(0.8f, 1/_playerService.CurrentAir); // We fade to black based on our start level of air (in seconds)
            
            PlaySfx(_sfxSwim2DiveSound);
            
            // FIXME - Bubbles are too big on screen. Needs more love.
            // _diveBubbles.SetActive(true);
        }

        /// <summary>
        /// Tested:
        /// Test 1. No translation
        /// var combinedVelocity = (leftHandVelocity + rightHandVelocity) * _swimHandMovementMultiplier;
        /// _currentVelocity = Vector3.Lerp(_currentVelocity, -combinedVelocity, Time.deltaTime);
        ///
        /// [🆗] Moving physical head
        /// [🛑] Rotating player with Thumbstick
        ///
        /// Test 2. Translation with PlayerController (where Thumbstick rotation is applied)
        /// var combinedVelocity = (leftHandVelocity + rightHandVelocity) * _swimHandMovementMultiplier;
        /// var rotatedVelocity = _rig.Root.TransformDirection(combinedVelocity);
        /// _currentVelocity = Vector3.Lerp(_currentVelocity, -rotatedVelocity, Time.deltaTime);
        ///
        /// [🛑] Moving physical head
        /// [🆗] Rotating player with Thumbstick
        ///
        ///
        /// Test 3. Translation with Camera.main!
        /// var combinedVelocity = (leftHandVelocity + rightHandVelocity) * _swimHandMovementMultiplier;
        /// var rotatedVelocity = Camera.main!.transform.TransformDirection(combinedVelocity);
        /// _currentVelocity = Vector3.Lerp(_currentVelocity, -rotatedVelocity, Time.deltaTime);
        ///
        /// [🛑] Moving physical head
        /// [🆗] Rotating player with Thumbstick
        ///
        /// Test 4. Using Hand's Rigidbody.linearVelocity
        /// [🆗] Moving physical head
        /// [🆗] Rotating player with Thumbstick
        /// [🛑] Rigidbody velocity is only tracked at FixedUpdate(). Which makes the movement wonky and I couldn't figure out how to apply multiplier right.
        ///
        /// Test 5. Using CameraRig as TransformDirection
        /// var rotatedVelocity = _rig.TrackingSpace.TransformDirection(combinedVelocity);
        /// Why? Something like: The two rotations are applied like this in HVR Player:
        /// PlayerController - real and synthetic rotation
        ///   - CameraRig    - inverse real rotation
        ///     - Camera     - real rotation
        /// It means that real rotation is calculated out, but the synthetic rotation is counted in when transforming from CameraRig.
        /// [🆗] Moving physical head
        /// [🆗] Rotating player with Thumbstick
        /// </summary>
        private void HandleSwim()
        {
            var leftGrip = _vrInput.GetButton(VrHandSide.Left, VrButton.Grip);
            var rightGrip = _vrInput.GetButton(VrHandSide.Right, VrButton.Grip);

            if (leftGrip.IsJustActivated)
                _isSwimDiveForceLeftJustStarted = true;
            if (rightGrip.IsJustActivated)
                _isSwimDiveForceRightJustStarted = true;

            // Use force to pull yourself (per hand). Only include hands whose Grip is currently held.
            if (leftGrip.IsActive || rightGrip.IsActive)
            {
                var desiredLocalVelocity = Vector3.zero;

                if (leftGrip.IsActive)
                {
                    if (IsHandBelowWater(_rig.LeftHand))
                    {
                        var leftHandVelocity = _vrInput.GetControllerVelocity(VrHandSide.Left);
                        desiredLocalVelocity += leftHandVelocity;
                    }
                }

                if (rightGrip.IsActive)
                {
                    if (IsHandBelowWater(_rig.RightHand))
                    {
                        var rightHandVelocity = _vrInput.GetControllerVelocity(VrHandSide.Right);
                        desiredLocalVelocity += rightHandVelocity;
                    }
                }

                desiredLocalVelocity *= _swimHandMovementMultiplier;

                // Transform velocity to world space based on player rotation
                var rotatedVelocity = _rig.TrackingSpace.TransformDirection(desiredLocalVelocity);

                // Apply opposite force for swimming - Only horizontal!
                _currentVelocity = Vector3.Lerp(_currentVelocity, -rotatedVelocity, Time.deltaTime);
            }
            // Fade out movement when no grip is active
            else
            {
                _currentVelocity *= Mathf.Pow(_swimVelocityFadeRate, Time.deltaTime);
                _isSwimDiveForceLeftJustStarted = false;
                _isSwimDiveForceRightJustStarted = false;
            }

            // Play swim SFX for each hand that just started pulling
            if (_isSwimDiveForceLeftJustStarted && _currentVelocity.magnitude > 1f)
            {
                PlaySfx(_sfxSwimSound, isCooldown: true);
                _isSwimDiveForceLeftJustStarted = false;
            }
            if (_isSwimDiveForceRightJustStarted && _currentVelocity.magnitude > 1f)
            {
                PlaySfx(_sfxSwimSound, isCooldown: true);
                _isSwimDiveForceRightJustStarted = false;
            }

            // Move the character - Horizontal only
            var rotatedHorizontalVelocity = new Vector3(_currentVelocity.x, 0, _currentVelocity.z);
            _rig.CharacterController.Move(rotatedHorizontalVelocity * Time.deltaTime);

            // We seek -y aka "down"
            if (_currentVelocity.y < _swimStartDiveVerticalVelocity)
            {
                StartDive();
            }
        }

        private bool IsHandBelowWater(Transform handTransform)
        {
            RaycastHit hit;

            // Is there water above your hand?
            return Physics.Raycast(handTransform.position, Vector3.up, out hit, _rig.CameraHeight,
                1 << Constants.WaterLayer);
        }

        private void HandleDive()
        {
            var leftGrip = _vrInput.GetButton(VrHandSide.Left, VrButton.Grip);
            var rightGrip = _vrInput.GetButton(VrHandSide.Right, VrButton.Grip);

            if (leftGrip.IsJustActivated)
                _isSwimDiveForceLeftJustStarted = true;
            if (rightGrip.IsJustActivated)
                _isSwimDiveForceRightJustStarted = true;

            // Use force to pull yourself (per hand). Only include hands whose Grip is currently held.
            if (leftGrip.IsActive || rightGrip.IsActive)
            {
                var desiredLocalVelocity = Vector3.zero;

                if (leftGrip.IsActive)
                {
                    var leftHandVelocity = _vrInput.GetControllerVelocity(VrHandSide.Left);
                    desiredLocalVelocity += leftHandVelocity;
                }

                if (rightGrip.IsActive)
                {
                    var rightHandVelocity = _vrInput.GetControllerVelocity(VrHandSide.Right);
                    desiredLocalVelocity += rightHandVelocity;
                }

                desiredLocalVelocity *= _diveHandMovementMultiplier;

                // Transform velocity to world space based on player rotation
                var rotatedVelocity = _rig.TrackingSpace.TransformDirection(desiredLocalVelocity);

                // Apply opposite force for diving (full 3D movement)
                _currentVelocity = Vector3.Lerp(_currentVelocity, -rotatedVelocity, Time.deltaTime);
            }
            // Fade out movement when no grip is active
            else
            {
                _currentVelocity *= Mathf.Pow(_diveVelocityFadeRate, Time.deltaTime);
                _isSwimDiveForceLeftJustStarted = false;
                _isSwimDiveForceRightJustStarted = false;
            }

            // Play dive SFX for each hand that just started pulling, prevent overlap via cooldown
            if (_isSwimDiveForceLeftJustStarted && _currentVelocity.magnitude > 1f)
            {
                PlaySfx(_sfxDiveSound, isCooldown: true);
                _isSwimDiveForceLeftJustStarted = false;
            }
            if (_isSwimDiveForceRightJustStarted && _currentVelocity.magnitude > 1f)
            {
                PlaySfx(_sfxDiveSound, isCooldown: true);
                _isSwimDiveForceRightJustStarted = false;
            }

            // Move the character
            _rig.CharacterController.Move(_currentVelocity * Time.deltaTime);
        }

        /// <summary>
        /// Mods (and G2 before EnableNpcWater) can miss the swim sounds - swimming works without them.
        /// </summary>
        private void PlaySfx(SfxModel sfx, bool isCooldown = false)
        {
            if (sfx == null || Camera.main == null)
                return;

            var clip = _audioService.CreateAudioClip(sfx.GetRandomSound());
            if (clip == null)
                return;

            if (isCooldown)
                _vrSfxPlayer.PlayWithCooldown(clip, Camera.main.transform.position, _swimDiveSfxUuid, clip.length);
            else
                _vrSfxPlayer.Play(clip, Camera.main.transform.position);
        }

        public IEnumerable<object> CollectMarvinInspectorProperties()
        {
            return new List<object>
            {
                new MarvinPropertyHeader("VRSwimDive - Water bobbing"),
                new MarvinProperty<float>(
                    "Amplitude",
                    () => _waterBobAmplitude,
                    value => _waterBobAmplitude = value,
                    0f, 0.01f),
                new MarvinProperty<float>(
                    "Frequency",
                    () => _waterBobFrequency,
                    value => _waterBobFrequency = value,
                    0f, 5f),

                new MarvinPropertyHeader("VRSwimDive - Swimming"),
                new MarvinProperty<float>(
                    "Force Dive - Vertical Velocity",
                    () => _swimStartDiveVerticalVelocity,
                    value => _swimStartDiveVerticalVelocity = value,
                    -10f, 0f),
                new MarvinProperty<float>(
                    "Hand Movement Multiplier",
                    () => _swimHandMovementMultiplier,
                    value => _swimHandMovementMultiplier = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Velocity Fade Rate",
                    () => _swimVelocityFadeRate,
                    value => _swimVelocityFadeRate = value,
                    0f, 1f),

                new MarvinPropertyHeader("VRSwimDive - Diving"),
                new MarvinProperty<float>(
                    "Hand Movement Multiplier",
                    () => _diveHandMovementMultiplier,
                    value => _diveHandMovementMultiplier = value,
                    0f, 10f),
                new MarvinProperty<float>(
                    "Velocity Fade Rate",
                    () => _diveVelocityFadeRate,
                    value => _diveVelocityFadeRate = value,
                    0f, 1f),
            };
        }
    }
}
