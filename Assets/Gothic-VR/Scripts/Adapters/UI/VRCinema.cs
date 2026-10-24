#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Video;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Services.UI;
using Gothic.VR.Adapters.HVROverrides;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Videos (PlayVideo from Daedalus, the logo videos) in a dark "cinema": the VR camera renders only a screen
    /// (UI layer) on black, the world is muted. Lives as Cinema.prefab on the player (inactive), fed by VRCinemaService.
    /// It activates itself for the first video and deactivates after the last one.
    /// Several videos in a row (G1 ending: 3 videos, logos: 2) are queued.
    /// The player is locked meanwhile: no movement, turning, crouching, jumping or menu.
    /// Skip the current video: any trigger or A/X (keyboard Space/Escape).
    /// The original Bink videos (.bik) are decoded by BinkPlayer.
    /// The world is paused (Time.timeScale 0) like in the engine, where the video blocks the game - otherwise dialog
    /// lines queued meanwhile would all play at once after the video.
    /// </summary>
    public class VRCinema : MonoBehaviour
    {
        private const float _skipInputDelay = 0.5f;
        private const float _skipHintGap = 0.2f;
        private const float _skipHintScale = 0.2f;
        private const float _skipHintAlpha = 0.6f;
        private const float _skipHintFadeDelay = 0.5f;
        private const float _skipHintFadeSeconds = 1f;
        // Gothic's font has no square brackets.
        private const string _skipHintButton = "(Trigger) ";
        private const string _skipHintFallback = "skip";

        [Tooltip("Screen the video is rendered on (UI layer, as the camera renders only this layer meanwhile).")]
        [SerializeField]
        private MeshRenderer _screen;

        [Tooltip("Places the screen in front of the head when a video starts.")] [SerializeField]
        private VRPlaceInFrontOfCamera _screenPlacement;

        [Tooltip("Audio of the video. Plays while the world's audio is paused.")] [SerializeField]
        private AudioSource _audioSource;

        [Tooltip("Distance of the screen in front of the head in meters.")] [SerializeField]
        private float _screenDistance = 3f;

        [Tooltip("Width of the screen in meters. The height follows the video's aspect ratio.")] [SerializeField]
        private float _screenWidth = 3.2f;

        private readonly Queue<string> _queue = new();

        private Camera _camera;
        private int _savedCullingMask;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackground;
        // The spectator camera (the monitor/stream view) - it kept showing the world, without the screen.
        private readonly List<(Camera camera, int cullingMask, CameraClearFlags clearFlags, Color background)>
            _savedSpectatorCameras = new();
        private float _savedTimeScale = 1f;

        private GameObject _skipHint;
        private TMP_Text _skipHintText;
        private float _skipHintStartTime;

        private VRPlayerController _playerController;
        private bool _savedMovementEnabled;
        private bool _savedRotationEnabled;
        private bool _savedCanCrouch;
        private bool _savedCanJump;
        private bool _savedTeleporterEnabled;
        private bool _savedMenuButtonEnabled;

        private BinkPlayer _binkPlayer;
        private float _playStartTime;
        private bool _isRunning;
        private int _closedFrame = -1;


        /// <summary>
        /// True while a video plays and in the frame the cinema closed. The input that skipped the last video (e.g.
        /// Escape) must not open the menu in the same frame, after the menu button got unlocked again.
        /// </summary>
        public bool IsBlockingInput => _isRunning || Time.frameCount == _closedFrame;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Queues a Bink video file. Only the first one starts the cinema, the others follow.
        /// </summary>
        public void Enqueue(string path)
        {
            _queue.Enqueue(path);
            if (!_isRunning)
                PlayNext();
        }

        private void PlayNext()
        {
            if (_queue.Count == 0)
            {
                Close();
                return;
            }

            if (!_isRunning && !Open())
            {
                _queue.Clear();
                GlobalEventDispatcher.VideoPlaybackFinished.Invoke();
                return;
            }

            _isRunning = true;
            var path = _queue.Dequeue();
            Logger.Log($"[VRCinema] Playing {path}", LogCat.VR);
            _playStartTime = Time.unscaledTime;
            ResetSkipHintFade();
            StopBink();
            PlayBink(path);
        }

        private void PlayBink(string path)
        {
            _binkPlayer = gameObject.AddComponent<BinkPlayer>();
            if (!_binkPlayer.Play(path, _screen, _audioSource, PlayNext, out var aspect))
            {
                PlayNext();
                return;
            }

            _screen.transform.localScale = new Vector3(_screenWidth, _screenWidth / aspect, 1f);
        }

        private void StopBink()
        {
            if (_binkPlayer == null)
                return;
            _binkPlayer.Stop();
            Destroy(_binkPlayer);
            _binkPlayer = null;
        }

        private bool Open()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                Logger.LogWarning("[VRCinema] No main camera - video skipped.", LogCat.VR);
                return false;
            }

            gameObject.SetActive(true);

            // Dark room: only the screen (UI layer) on black.
            _savedCullingMask = _camera.cullingMask;
            _savedClearFlags = _camera.clearFlags;
            _savedBackground = _camera.backgroundColor;
            _camera.cullingMask = 1 << Constants.UILayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            DarkenSpectatorCameras();
            AudioListener.pause = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            LockPlayer();

            // Stays where it is (no head following). PlayVideo often comes before the script teleports the hero - the
            // screen is placed anew then.
            _screenPlacement.Configure(_camera.transform, _screenDistance);

            // The world is paused via AudioListener.pause - the video's own source ignores it. Not serialized by Unity.
            _audioSource.ignoreListenerPause = true;

            var configService = ReflexProjectInstaller.DIContainer.Resolve<ConfigService>();
            if (configService.Dev.EnableCinemaSkipHint && _skipHint == null)
                CreateSkipHint();
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableCinemaSkipHint: "(Trigger) skip" under the screen's right corner - the word from the
        /// Gothic-UI table (key cinema.skip), in the dark room's UI layer.
        /// </summary>
        private void CreateSkipHint()
        {
            _skipHint = new GameObject("_CinemaSkipHint");
            _skipHint.layer = Constants.UILayer;
            _skipHint.transform.SetParent(transform, false);
            _skipHint.transform.localScale = Vector3.one * _skipHintScale;

            var text = _skipHint.AddComponent<TextMeshPro>();
            VRGothicText.Apply(text);
            var localized = ReflexProjectInstaller.DIContainer.Resolve<LocalizationService>().GetText("cinema.skip");
            text.text = _skipHintButton + (string.IsNullOrEmpty(localized) || localized.Contains("cinema.skip")
                ? _skipHintFallback
                : localized);
            text.fontSize = 12;
            text.alignment = TextAlignmentOptions.Right;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // Pivot at the right edge, so the text ends at the screen's corner.
            text.rectTransform.pivot = new Vector2(1f, 0.5f);
            _skipHintText = text;
            ResetSkipHintFade();
        }

        private void ResetSkipHintFade()
        {
            if (_skipHintText == null)
                return;
            // Fades in (UpdateSkipHint) - the world is paused, so unscaled time.
            _skipHintText.color = new Color(1f, 1f, 1f, 0f);
            _skipHintStartTime = Time.unscaledTime;
        }

        private void LateUpdate()
        {
            if (!_isRunning || _skipHint == null)
                return;

            // Under the screen's right corner, its right edge in line with the screen's.
            var screenTransform = _screen.transform;
            var corner = screenTransform.position + screenTransform.right * (screenTransform.lossyScale.x / 2f) -
                         Vector3.up * (screenTransform.lossyScale.y / 2f + _skipHintGap);
            _skipHint.transform.SetPositionAndRotation(corner, screenTransform.rotation);

            var progress = Mathf.Clamp01((Time.unscaledTime - _skipHintStartTime - _skipHintFadeDelay) /
                                         _skipHintFadeSeconds);
            var color = _skipHintText.color;
            color.a = _skipHintAlpha * progress * progress;
            _skipHintText.color = color;
        }

        /// <summary>
        /// The monitor shows VRSpectatorCamera, not the VR camera - the same dark room there, or OBS/the monitor showed
        /// the world without the video.
        /// </summary>
        private void DarkenSpectatorCameras()
        {
            _savedSpectatorCameras.Clear();
            foreach (var spectator in FindObjectsByType<Player.VRSpectatorCamera>(FindObjectsSortMode.None))
            {
                if (!spectator.TryGetComponent<Camera>(out var spectatorCamera) || spectatorCamera == _camera)
                    continue;

                _savedSpectatorCameras.Add((spectatorCamera, spectatorCamera.cullingMask, spectatorCamera.clearFlags,
                    spectatorCamera.backgroundColor));
                spectatorCamera.cullingMask = 1 << Constants.UILayer;
                spectatorCamera.clearFlags = CameraClearFlags.SolidColor;
                spectatorCamera.backgroundColor = Color.black;
            }
        }

        private void RestoreSpectatorCameras()
        {
            foreach (var (spectatorCamera, cullingMask, clearFlags, background) in _savedSpectatorCameras)
            {
                if (spectatorCamera == null)
                    continue;
                spectatorCamera.cullingMask = cullingMask;
                spectatorCamera.clearFlags = clearFlags;
                spectatorCamera.backgroundColor = background;
            }
            _savedSpectatorCameras.Clear();
        }

        private void Update()
        {
            if (!_isRunning || Time.unscaledTime - _playStartTime < _skipInputDelay)
                return;

            if (IsSkipPressed())
            {
                Logger.Log("[VRCinema] Skipped", LogCat.VR);
                StopBink();
                PlayNext();
            }
        }

        private static bool IsSkipPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
                return true;

            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                if (HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated ||
                    HVRController.GetButtonState(side, HVRButtons.Primary).JustActivated)
                    return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            // Destroyed while running (the player is destroyed, e.g. on quit): the camera might survive - never leave
            // it dark or the game paused.
            if (_isRunning)
                RestoreWorld();
            StopBink();
        }

        private void Close()
        {
            if (_isRunning)
                RestoreWorld();
            StopBink();

            _isRunning = false;
            _closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            Logger.Log("[VRCinema] Closed", LogCat.VR);
            GlobalEventDispatcher.VideoPlaybackFinished.Invoke();
        }

        private void RestoreWorld()
        {
            RestoreCamera();
            RestoreSpectatorCameras();
            UnlockPlayer();
            AudioListener.pause = false;
            Time.timeScale = _savedTimeScale;
        }

        private void RestoreCamera()
        {
            if (_camera == null)
                return;

            _camera.cullingMask = _savedCullingMask;
            _camera.clearFlags = _savedClearFlags;
            _camera.backgroundColor = _savedBackground;
            _camera = null;
        }

        /// <summary>
        /// The camera only shows the screen, but HVR still moves/crouches the player and the menu button would open
        /// the menu. The previous values are kept, as a dialog or seat might have locked the player already.
        /// </summary>
        private void LockPlayer()
        {
            // The cinema is inactive until the first video - no Awake to cache it in before.
            if (_playerController == null)
                _playerController = GetComponentInParent<VRPlayerController>(true);
            if (_playerController == null)
                return;

            _savedMovementEnabled = _playerController.MovementEnabled;
            _savedRotationEnabled = _playerController.RotationEnabled;
            _savedCanCrouch = _playerController.CanCrouch;
            _savedCanJump = _playerController.CanJump;
            _savedTeleporterEnabled = _playerController.Teleporter.enabled;
            _savedMenuButtonEnabled = _playerController.VrInputs.IsMenuButtonEnabled;

            _playerController.MovementEnabled = false;
            _playerController.RotationEnabled = false;
            _playerController.CanCrouch = false;
            _playerController.CanJump = false;
            _playerController.Teleporter.enabled = false;
            _playerController.VrInputs.IsMenuButtonEnabled = false;
        }

        private void UnlockPlayer()
        {
            if (_playerController == null)
                return;

            _playerController.MovementEnabled = _savedMovementEnabled;
            _playerController.RotationEnabled = _savedRotationEnabled;
            _playerController.CanCrouch = _savedCanCrouch;
            _playerController.CanJump = _savedCanJump;
            _playerController.Teleporter.enabled = _savedTeleporterEnabled;
            _playerController.VrInputs.IsMenuButtonEnabled = _savedMenuButtonEnabled;
        }
    }
}
#endif
