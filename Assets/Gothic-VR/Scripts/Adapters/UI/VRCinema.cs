using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Video;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
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
        private float _savedTimeScale = 1f;

        private VRPlayer _player;
        private IVrRig _rig;
        private IVrInput _vrInput;
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
            AudioListener.pause = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            LockPlayer();

            // Stays where it is (no head following). PlayVideo often comes before the script teleports the hero - the
            // screen is placed anew then.
            _screenPlacement.Configure(_camera.transform, _screenDistance);

            // The world is paused via AudioListener.pause - the video's own source ignores it. Not serialized by Unity.
            _audioSource.ignoreListenerPause = true;
            return true;
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

        private bool IsSkipPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
                return true;

            _vrInput ??= ReflexProjectInstaller.DIContainer.Resolve<IVrInput>();
            foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
            {
                if (_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated ||
                    _vrInput.GetButton(side, VrButton.Primary).IsJustActivated)
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
            // It's a sibling of the rig inside the player prefab, not a child of the VRPlayer.
            if (_player == null)
            {
                _player = transform.root.GetComponentInChildren<VRPlayer>(true);
                _rig = _player == null ? null : _player.GetComponent<IVrRig>();
            }
            if (_player == null || _rig == null)
                return;

            _savedMovementEnabled = _rig.MovementEnabled;
            _savedRotationEnabled = _rig.TurnEnabled;
            _savedCanCrouch = _rig.CanCrouch;
            _savedCanJump = _rig.CanJump;
            _savedTeleporterEnabled = _rig.TeleportEnabled;
            _savedMenuButtonEnabled = _player.IsMenuButtonEnabled;

            _rig.MovementEnabled = false;
            _rig.TurnEnabled = false;
            _rig.CanCrouch = false;
            _rig.CanJump = false;
            _rig.TeleportEnabled = false;
            _player.IsMenuButtonEnabled = false;
        }

        /// <summary>
        /// Values enabled by others while the video played are kept: the lock saved at start can be outdated by then
        /// (e.g. the world load or a dialog unlocked the player meanwhile), restoring it would leave the player stuck.
        /// </summary>
        private void UnlockPlayer()
        {
            if (_player == null || _rig == null)
                return;

            _rig.MovementEnabled |= _savedMovementEnabled;
            _rig.TurnEnabled |= _savedRotationEnabled;
            _rig.CanCrouch |= _savedCanCrouch;
            _rig.CanJump |= _savedCanJump;
            _rig.TeleportEnabled |= _savedTeleporterEnabled;
            _player.IsMenuButtonEnabled |= _savedMenuButtonEnabled;
        }
    }
}
