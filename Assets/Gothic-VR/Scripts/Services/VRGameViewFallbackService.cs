using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Config;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Editor only (DeveloperConfig.RenderGameViewWithoutHeadset): while the headset renders nothing (not worn,
    /// asleep, session ended), the main camera renders to the Game view. Unity culls a VR camera only for the
    /// headset's frames - without them culling stands still and no NPC or object around the player gets built.
    /// The headset's next frame switches back.
    /// </summary>
    public class VRGameViewFallbackService
    {
        // Seconds without a headset frame before the Game view takes over. Short gaps (loading, reconnects) don't.
        private const float _secondsWithoutHeadsetFrame = 1f;

        private XRDisplaySubsystem _display;
        private UniversalAdditionalCameraData _cameraData;
        private float _lastHeadsetFrameTime;
        private bool _isRenderingToGameView;
        private bool _isActive;

        public void Init(DeveloperConfig developerConfig)
        {
            if (!Application.isEditor || !developerConfig.RenderGameViewWithoutHeadset || _isActive)
                return;

            _isActive = true;
            _lastHeadsetFrameTime = Time.unscaledTime;
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            // Without a domain reload, the static event would keep this play session's service.
            Application.quitting += Stop;
        }

        private void Stop()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            Application.quitting -= Stop;
            _isActive = false;
        }

        /// <summary>
        /// Called before the cameras render: the headset's frame (its render passes) is known by then.
        /// </summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            var display = GetDisplay();
            // Without XR the camera renders to the Game view anyway.
            if (display == null || !display.running || display.GetRenderPassCount() > 0)
                _lastHeadsetFrameTime = Time.unscaledTime;

            var renderToGameView = Time.unscaledTime - _lastHeadsetFrameTime >= _secondsWithoutHeadsetFrame;
            var cameraData = GetCameraData();
            if (renderToGameView == _isRenderingToGameView || cameraData == null)
                return;

            _isRenderingToGameView = renderToGameView;
            cameraData.allowXRRendering = !renderToGameView;
            Logger.Log(renderToGameView
                    ? "[VR] The headset renders nothing - the camera renders to the Game view " +
                      "(DeveloperConfig.RenderGameViewWithoutHeadset)."
                    : "[VR] The headset renders again - the camera renders to the headset.",
                LogCat.VR);
        }

        private XRDisplaySubsystem GetDisplay()
        {
            if (_display != null && _display.running)
                return _display;

            // XR may have started (or restarted) after the last frame.
            var manager = XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;
            var loader = manager == null ? null : manager.activeLoader;
            _display = loader == null ? null : loader.GetLoadedSubsystem<XRDisplaySubsystem>();
            return _display;
        }

        private UniversalAdditionalCameraData GetCameraData()
        {
            if (_cameraData != null)
                return _cameraData;

            var mainCamera = Camera.main;
            if (mainCamera == null)
                return null;

            // A new camera starts with the current mode.
            _cameraData = mainCamera.GetUniversalAdditionalCameraData();
            _cameraData.allowXRRendering = !_isRenderingToGameView;
            return _cameraData;
        }
    }
}
