#if GUZ_HVR_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Audio;
using GUZ.Core.Models.Marvin;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.Player;
using GUZ.Core.Services.Vm;
using GUZ.VR.Adapters.HVROverrides;
using HurricaneVR.Framework.Core.Utils;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.VR.Adapters.Player
{
    [RequireComponent(typeof(VRPlayerController), typeof(VRPlayerInputs))]
    public class VRSwimDive : MonoBehaviour, IMarvinPropertyCollector
    {
        [SerializeField] private VRPlayerController _playerController;
        [SerializeField] private VRPlayerInputs _playerInputs;
        
        private INpc _playerVob;
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

        private void Start()
        {
            Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);

			GlobalEventDispatcher.ZenKitBootstrapped.AddListener(() =>
            {
                if (_contextGameVersionService.IsGothic2())
                {
                    Logger.LogError("Swimming is not yet implemented for G2. Skipping...", LogCat.VR);
                    return;
                }
                
                var mds = _resourceCacheService.TryGetModelScript("Humans")!;
                
                // FIXME - In G1, there are different sounds for SwimBack, Sideways, and Forward
                var swimAnim = mds.Animations.First(i => i.Name.EqualsIgnoreCase("s_SwimF"));
                var swimSfxName = swimAnim.SoundEffects.First().Name;
                _sfxSwimSound = _vmCacheService.TryGetSfxData(swimSfxName)!;
                
                var diveAnim = mds.Animations.First(i => i.Name.EqualsIgnoreCase("s_DiveF"));
                var diveSfxName = diveAnim.SoundEffects.First().Name;
                _sfxDiveSound = _vmCacheService.TryGetSfxData(diveSfxName)!;


                var swim2DiveAnim = mds.Animations.First(i => i.Name.EqualsIgnoreCase("t_Swim_2_Dive"));
                var swim2DiveSfxName = swim2DiveAnim.SoundEffects.First().Name;
                _sfxSwim2DiveSound = _vmCacheService.TryGetSfxData(swim2DiveSfxName)!;
                
                var swim2HangAnim = mds.Animations.First(i => i.Name.EqualsIgnoreCase("t_Swim_2_Hang"));
                var swim2HangSfxName = swim2HangAnim.SoundEffects.First().Name;
                _sfxSwim2HangSound = _vmCacheService.TryGetSfxData(swim2HangSfxName);

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
                _playerVob = ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData()!.Vob;
                _playerAi = (IAiHuman)_playerVob.Ai;
            });
            
            _initialGravity = _playerController.Gravity;
            _initialMoveSpeed = _playerController.MoveSpeed;
            _initialRunSpeed = _playerController.RunSpeed;
            _initialFallSpeed = _playerController.MaxFallSpeed;
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
            // else
            // FIXME - We need to add a possiblity to reset water IF the hero is teleported away from water or falling through a waterfall, ...
            //         so that the water level is reset and we can walk again normally.
        }
        
        /// <summary>
        /// Debug only
        /// </summary>
        private void OnValidate()
        {
            if (_debugStartDive)
            {
                _debugStartDive = false;

                if (_mode == VmGothicEnums.WalkMode.Swim)
                {
                    _playerController.CharacterController.Move(Vector3.down * 5);
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
                    _playerController.Gravity = _initialGravity;
                    _playerController.MoveSpeed = _initialMoveSpeed;
                    _playerController.RunSpeed = _initialRunSpeed;
                    _playerController.CanSprint = true;
                    _playerController.MaxFallSpeed = _initialFallSpeed;
                    
                    _playerController.SetWalkingControls();
                    break;
                case ZenGineConst.WaterLevel.Knee:
                    // Dive -> Swim (sometimes when pulling hard up, we are briefly knee deep and need to call the sound in here once)
                    if (_mode == VmGothicEnums.WalkMode.Dive)
                    {
                        _playerService.StopDiving();
                        _playerController.ScreenFader.Fade(0, float.MaxValue); // Immediately fade the screen back to normal (no fade)

                        var clip = _audioService.CreateAudioClip(_sfxSwim2HangSound.GetRandomSound());
                        SFXPlayer.Instance.PlaySFX(clip, Camera.main!.transform.position);

                        // FIXME - Bubbles are too big on screen. Needs more love.
                        // _diveBubbles.SetActive(false);
                    }
                    
                    // FIXME - Or is it "WalkMode.Water"?
                    _mode = VmGothicEnums.WalkMode.Walk;
                    Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);

                    _playerController.Gravity = _initialGravity;
                    _playerController.MoveSpeed = _initialMoveSpeed / 2;
                    _playerController.RunSpeed = _initialMoveSpeed / 2; // No running, but it might be, that we run into deep water, then we need to slow it down like walking.
                    _playerController.CanSprint = false;
                    _playerController.MaxFallSpeed = _initialFallSpeed;
                    
                    _playerController.SetWaterWalkingControls();
                    break;
                case ZenGineConst.WaterLevel.Chest:
                    // Dive -> Swim
                    if (_mode == VmGothicEnums.WalkMode.Dive)
                    {
                        _playerService.StopDiving();
                        _playerController.ScreenFader.Fade(0, float.MaxValue); // Immediately fade the screen back to normal (no fade)

                        var clip = _audioService.CreateAudioClip(_sfxSwim2HangSound.GetRandomSound());
                        SFXPlayer.Instance.PlaySFX(clip, Camera.main!.transform.position);
                        
                        // FIXME - Bubbles are too big on screen. Needs more love.
                        // _diveBubbles.SetActive(false);
                    }

                    Shader.SetGlobalInt(Constants.ShaderPropertyWaterEffectToggle, 0);
                    _mode = VmGothicEnums.WalkMode.Swim;
                    _playerController.Gravity = 0f;
                    _playerController.MaxFallSpeed = 0f;

                    _playerController.SetSwimmingControls();
                    
                    if (_waterBobbingCoroutine == null)
                        _waterBobbingCoroutine = StartCoroutine(WaterBobbing());
                    break;
                default:
                    throw new Exception($"Unknown value {_playerAi.WaterLevel} for water.");
            }
        }

        private IEnumerator WaterBobbing()
        {
            var time = 0f;
            while (true)
            {
                time += Time.deltaTime;
                var offset = Mathf.Sin(time * _waterBobFrequency) * _waterBobAmplitude;
                _playerController.CharacterController.Move(Vector3.down * offset);
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
            
            _playerController.Gravity = 0;
            _playerController.MaxFallSpeed = 0;

            _playerController.SetDivingControls();

            _playerService.StartDiving();
            _playerController.ScreenFader.Fade(0.8f, 1/_playerService.CurrentAir); // We fade to black based on our start level of air (in seconds)
            
            SFXPlayer.Instance.PlaySFX(_audioService.CreateAudioClip(_sfxSwim2DiveSound.GetRandomSound()), Camera.main!.transform.position);
            
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
        /// var rotatedVelocity = _playerController.transform.TransformDirection(combinedVelocity);
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
        /// var rotatedVelocity = _playerController.CameraRig.transform.TransformDirection(combinedVelocity);
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
            if (_playerInputs.IsLeftGrabActivated)
                _isSwimDiveForceLeftJustStarted = true;
            if (_playerInputs.IsRightGrabActivated)
                _isSwimDiveForceRightJustStarted = true;

            // Use force to pull yourself (per hand). Only include hands whose Grip is currently held.
            if (_playerInputs.IsLeftGripHoldActive || _playerInputs.IsRightGripHoldActive)
            {
                var desiredLocalVelocity = Vector3.zero;

                if (_playerInputs.IsLeftGripHoldActive)
                {
                    if (IsHandBelowWater(_playerController.LeftControllerTransform))
                    {
                        var leftHandVelocity = _playerInputs.LeftController.Velocity;
                        desiredLocalVelocity += leftHandVelocity;
                    }
                }

                if (_playerInputs.IsRightGripHoldActive)
                {
                    if (IsHandBelowWater(_playerController.RightControllerTransform))
                    {
                        var rightHandVelocity = _playerInputs.RightController.Velocity;
                        desiredLocalVelocity += rightHandVelocity;
                    }
                }

                desiredLocalVelocity *= _swimHandMovementMultiplier;

                // Transform velocity to world space based on player rotation
                var rotatedVelocity = _playerController.CameraRig.transform.TransformDirection(desiredLocalVelocity);

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
                var clip = _audioService.CreateAudioClip(_sfxSwimSound.GetRandomSound())!;
                SFXPlayer.Instance.PlaySFXCooldown(clip, Camera.main!.transform.position, _swimDiveSfxUuid, cooldownTime: clip.length);
                _isSwimDiveForceLeftJustStarted = false;
            }
            if (_isSwimDiveForceRightJustStarted && _currentVelocity.magnitude > 1f)
            {
                var clip = _audioService.CreateAudioClip(_sfxSwimSound.GetRandomSound())!;
                SFXPlayer.Instance.PlaySFXCooldown(clip, Camera.main!.transform.position, _swimDiveSfxUuid, cooldownTime: clip.length);
                _isSwimDiveForceRightJustStarted = false;
            }

            // Move the character - Horizontal only
            var rotatedHorizontalVelocity = new Vector3(_currentVelocity.x, 0, _currentVelocity.z);
            _playerController.CharacterController.Move(rotatedHorizontalVelocity * Time.deltaTime);

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
            return Physics.Raycast(handTransform.position, Vector3.up, out hit, _playerController.CameraHeight,
                1 << Constants.WaterLayer);
        }

        private void HandleDive()
        {
            if (_playerInputs.IsLeftGrabActivated)
                _isSwimDiveForceLeftJustStarted = true;
            if (_playerInputs.IsRightGrabActivated)
                _isSwimDiveForceRightJustStarted = true;

            // Use force to pull yourself (per hand). Only include hands whose Grip is currently held.
            if (_playerInputs.IsLeftGripHoldActive || _playerInputs.IsRightGripHoldActive)
            {
                var desiredLocalVelocity = Vector3.zero;

                if (_playerInputs.IsLeftGripHoldActive)
                {
                    var leftHandVelocity = _playerInputs.LeftController.Velocity;
                    desiredLocalVelocity += leftHandVelocity;
                }

                if (_playerInputs.IsRightGripHoldActive)
                {
                    var rightHandVelocity = _playerInputs.RightController.Velocity;
                    desiredLocalVelocity += rightHandVelocity;
                }

                desiredLocalVelocity *= _diveHandMovementMultiplier;

                // Transform velocity to world space based on player rotation
                var rotatedVelocity = _playerController.CameraRig.transform.TransformDirection(desiredLocalVelocity);

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
                var clip = _audioService.CreateAudioClip(_sfxDiveSound.GetRandomSound())!;
                SFXPlayer.Instance.PlaySFXCooldown(clip, Camera.main!.transform.position, _swimDiveSfxUuid, cooldownTime: clip.length);
                _isSwimDiveForceLeftJustStarted = false;
            }
            if (_isSwimDiveForceRightJustStarted && _currentVelocity.magnitude > 1f)
            {
                var clip = _audioService.CreateAudioClip(_sfxDiveSound.GetRandomSound())!;
                SFXPlayer.Instance.PlaySFXCooldown(clip, Camera.main!.transform.position, _swimDiveSfxUuid, cooldownTime: clip.length);
                _isSwimDiveForceRightJustStarted = false;
            }

            // Move the character
            _playerController.CharacterController.Move(_currentVelocity * Time.deltaTime);
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
#endif
