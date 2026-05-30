#if GUZ_HVR_INSTALLED
using System;
using GUZ.Core;
using GUZ.Core.Logging;
using GUZ.Core.Models.Config;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.VR.Adapters.HVROverrides;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.VR.Adapters.Player
{
    public class VRSpectatorCamera : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        public enum SmoothingLevel
        {
            None,
            Low,
            Medium,
            High
        }
        
        [Tooltip("Reference to the VR camera/head transform that will be followed")]
        [SerializeField]
        private Transform _vrCameraTransform;

        [Tooltip("Reference to the camera that will be used for the spectator view")]
        [SerializeField]
        private Camera _spectatorCamera;


        private float _selectedSmoothingValue;

        // Internal variables for smoothing calculations
        private Vector3 _positionVelocity;

        
        private void Start()
        {
            // Disable the whole feature if we're not on Windows or Editor build.
            // Save some CPU cycles for Android builds as we don't have a second screen there.
#if !UNITY_EDITOR && !UNITY_STANDALONE
            DisableSpectatorCamera();
            return;
#endif

            // If we have Device Simulator AND Spectator camera active, then the camera won't move at all in GameView.
            // And to be honest: We don't need the Spectator camera at that time.
            if (_configService.Dev.EnableVRDeviceSimulator)
            {
                DisableSpectatorCamera();
                return;
            }

            // All settings say: Yes, we want the spectator mode. Then let's wait for Gothic Inis to be loaded.
            GlobalEventDispatcher.GothicInisInitialized.AddListener(GothicStart);
        }

        /// <summary>
        /// We need to delay the setup until Gothic version is selected.
        /// Otherwise, Ini is not yet initialized, and we can't use smootheness value from it.
        /// </summary>
        private void GothicStart()
        {
            var playerController = _contextInteractionService.GetCurrentPlayerController().GetComponent<VRPlayerController>();
            playerController.Teleporter.PositionUpdate.AddListener(SetTeleportPosition);

            SetSmoothness();
            SetRenderDistance(_configService.Gothic.IniVisualRange);

            if (_vrCameraTransform != null)
            {
                // Initialize position to avoid jumps at startup
                transform.position = _vrCameraTransform.position;
                transform.rotation = _vrCameraTransform.rotation;
            }
            
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener((key, value) =>
            {
                if (key == VRConstants.IniNames.SmoothSpectator)
                {
                    SetSmoothness();
                }
                else if (key == GothicIniConfig.IniKeyVisualRange)
                {
                    SetRenderDistance(int.Parse((string)value));
                }
            });
        }

        private void DisableSpectatorCamera()
        {
            GetComponent<Camera>().enabled = false;
            this.enabled = false;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_vrCameraTransform == null)
                return;

            transform.position = _vrCameraTransform.position;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                _vrCameraTransform.rotation,
                // Use an exponential ease-in/out for more natural-feeling camera movement
                1.0f - Mathf.Exp(-Time.deltaTime / _selectedSmoothingValue)
            );
        }

        /// <summary>
        /// If we - e.g. - teleport to a new position, we need to set the position once.
        /// Otherwise, the camera would fly to this position slowly.
        /// </summary>
        private void SetTeleportPosition(Vector3 position)
        {
            // 1.7 == ~normal person size rather than at the bottom of the screen.
            transform.position = position + new Vector3(0, 1.7f, 0);
            transform.rotation = _vrCameraTransform.rotation;
        }

        private void SetSmoothness()
        {
            var smoothSetting = _configService.Gothic.GetInt(VRConstants.IniNames.SmoothSpectator, (int)SmoothingLevel.None);
            
            _selectedSmoothingValue = (SmoothingLevel)smoothSetting switch
            {
                SmoothingLevel.None => VRConstants.SpectatorSmoothingNone,
                SmoothingLevel.Low => VRConstants.SpectatorSmoothingLow,
                SmoothingLevel.Medium => VRConstants.SpectatorSmoothingMedium,
                SmoothingLevel.High => VRConstants.SpectatorSmoothingHigh,
                _ => throw new ArgumentOutOfRangeException()
            };
            
            Logger.Log($"Setting Spectator Camera Smoothness factor to {smoothSetting}:{_selectedSmoothingValue}.", LogCat.VR);
        }

        private void SetRenderDistance(int value)
        {
            // Starting with value=0 (20%) and ending with value=14 (300%)
            _spectatorCamera.farClipPlane = GothicIniConfig.IniVisualRangeFactor * (value + 1);
        }
    }
}
#endif
