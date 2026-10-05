#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Adapters.Video;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Adapters.Player;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Core.Player;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableScriptVideos): PlayVideo from Daedalus (chapter videos, the G1 ending's Extro/Credits)
    /// in a dark "cinema": the VR camera renders only a screen (UI layer) on black, the world is muted.
    /// Several PlayVideo calls in a row (G1 ending: 3 videos) are queued.
    /// Skip the current video: any trigger or A/X (keyboard Space/Escape).
    /// Original Bink videos (.bik) are decoded by BinkPlayer; an MP4 with the same name in _work/DATA/video/ wins.
    /// The world is paused (Time.timeScale 0) like in the engine, where the video blocks the game - otherwise dialog
    /// lines queued meanwhile would all play at once after the video.
    /// </summary>
    public class VRCinema : MonoBehaviour
    {
        private const float _screenDistance = 3f;
        private const float _screenWidth = 3.2f;
        private const float _skipInputDelay = 0.5f;
        // A bigger jump of the camera is a teleport (e.g. the hero is moved to the chapter start) - face it anew.
        private const float _teleportDistance = 5f;

        private static VRCinema _instance;
        private static bool _isQuitAfterVideos;
        private readonly Queue<string> _queue = new();

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private Camera _camera;
        private int _savedCullingMask;
        // DeveloperConfig.EnableCinemaShowsPlayer: hand and body renderers moved to the UI layer, and their old layers.
        private readonly List<(GameObject Go, int Layer)> _playerLayers = new();
        private HVRJointHand[] _jointHands;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackground;
        private float _savedTimeScale = 1f;
        private Vector3 _screenForward = Vector3.forward;
        private Vector3 _lastCameraPosition;

        private GameObject _screen;
        private VideoPlayer _videoPlayer;
        private RenderTexture _renderTexture;
        private Material _videoPlayerMaterial;
        private AudioSource _audioSource;
        private BinkPlayer _binkPlayer;
        private float _playStartTime;
        private bool _isRunning;


        public static void Play(VideoService videoService, string fileName)
        {
            var path = FindVideo(videoService, fileName);
            if (path == null)
            {
                Logger.LogWarning($"[VRCinema] '{fileName}' not found in _work/DATA/video/ (.bik/.mp4) - skipped.", LogCat.VR);
                return;
            }

            if (_instance == null)
                _instance = new GameObject("_Cinema").AddComponent<VRCinema>();

            // Scripts call PlayVideo several times in a row (G1 ending) - only the first one starts the cinema.
            _instance._queue.Enqueue(path);
            if (!_instance._isRunning)
                _instance.PlayNext();
        }

        /// <summary>
        /// ExitGame/ExitSession (ending): quit once the queued videos (Extro, Credits) are over - right away if none play.
        /// </summary>
        public static void QuitAfterVideos()
        {
            if (_instance != null && _instance._isRunning)
            {
                _isQuitAfterVideos = true;
                return;
            }
            Quit();
        }

        private static void Quit()
        {
            Logger.Log("[VRCinema] Game ended (ExitGame/ExitSession) - quitting.", LogCat.VR);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string FindVideo(VideoService videoService, string fileName)
        {
            // An MP4 (converted by the player) wins, otherwise the original Bink file is decoded by BinkPlayer.
            var name = Path.GetFileNameWithoutExtension(fileName);
            return videoService.VideoFilePathsMp4
                       .FirstOrDefault(i => Path.GetFileNameWithoutExtension(i).EqualsIgnoreCase(name)) ??
                   videoService.VideoFilePathsBik
                       .FirstOrDefault(i => Path.GetFileNameWithoutExtension(i).EqualsIgnoreCase(name));
        }

        private void PlayNext()
        {
            if (_queue.Count == 0)
            {
                Close();
                return;
            }

            if (_screen == null && !Open())
                return;

            _isRunning = true;
            var path = _queue.Dequeue();
            Logger.Log($"[VRCinema] Playing {path}", LogCat.VR);
            _playStartTime = Time.unscaledTime;
            StopBink();

            if (Path.GetExtension(path).EqualsIgnoreCase(".bik"))
            {
                PlayBink(path);
                return;
            }

            _screen.GetComponent<MeshRenderer>().sharedMaterial = _videoPlayerMaterial;
            _videoPlayer.url = path;
            _videoPlayer.Prepare();
        }

        private void PlayBink(string path)
        {
            _binkPlayer = gameObject.AddComponent<BinkPlayer>();
            if (!_binkPlayer.Play(path, _screen.GetComponent<MeshRenderer>(), _audioSource, PlayNext, out var aspect))
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
                _queue.Clear();
                return false;
            }

            // Dark room: only the screen (UI layer) on black.
            _savedCullingMask = _camera.cullingMask;
            _savedClearFlags = _camera.clearFlags;
            _savedBackground = _camera.backgroundColor;
            _camera.cullingMask = 1 << Constants.UILayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            if (_configService.Dev.EnableCinemaShowsPlayer)
                ShowPlayer();
            AudioListener.pause = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            _screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _screen.name = "_CinemaScreen";
            _screen.layer = Constants.UILayer;
            Destroy(_screen.GetComponent<Collider>());

            FaceCamera();
            _screen.transform.localScale = new Vector3(_screenWidth, _screenWidth * 9f / 16f, 1f);

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader != null ? shader : Constants.ShaderSingleMeshLit);
            _renderTexture = new RenderTexture(1280, 720, 0);
            material.mainTexture = _renderTexture;
            _videoPlayerMaterial = material;
            _screen.GetComponent<MeshRenderer>().sharedMaterial = material;

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _renderTexture;
            _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;

            // The world is paused via AudioListener.pause - the video's own source ignores it.
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.ignoreListenerPause = true;
            _audioSource.spatialBlend = 0f;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.SetTargetAudioSource(0, _audioSource);

            _videoPlayer.prepareCompleted += OnPrepared;
            _videoPlayer.loopPointReached += _ => PlayNext();
            _videoPlayer.errorReceived += (_, message) =>
            {
                Logger.LogWarning($"[VRCinema] Video error: {message}", LogCat.VR);
                PlayNext();
            };
            return true;
        }

        /// <summary>
        /// The dark room renders only the UI layer - the hands and the hero body go there for the video.
        /// </summary>
        private void ShowPlayer()
        {
            var controller = _contextInteractionService.GetCurrentPlayerController()?.GetComponent<VRPlayerController>();
            _jointHands = controller != null
                ? new[]
                    {
                        controller.LeftJointHand != null ? controller.LeftJointHand
                            : controller.LeftHand != null ? controller.LeftHand.GetComponentInParent<HVRJointHand>() : null,
                        controller.RightJointHand != null ? controller.RightJointHand
                            : controller.RightHand != null ? controller.RightHand.GetComponentInParent<HVRJointHand>() : null
                    }.Where(h => h != null).Distinct().ToArray()
                : new HVRJointHand[0];
            foreach (var hand in _jointHands)
                MoveRenderersToUILayer(hand.gameObject);

            var body = FindFirstObjectByType<VRHeroBody>();
            if (body != null)
                MoveRenderersToUILayer(body.gameObject);
            Logger.Log($"[VRCinema] Showing {_jointHands.Length} hands, body={body != null} " +
                       $"({_playerLayers.Count} renderers)", LogCat.VR);
        }

        private void MoveRenderersToUILayer(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var go = renderer.gameObject;
                if (go.layer == Constants.UILayer)
                    continue;
                _playerLayers.Add((go, go.layer));
                go.layer = Constants.UILayer;
            }
        }

        private void RestorePlayer()
        {
            foreach (var (go, layer) in _playerLayers)
            {
                if (go != null)
                    go.layer = layer;
            }
            _playerLayers.Clear();
            _jointHands = null;
        }

        /// <summary>
        /// HVR hands follow the controllers in FixedUpdate, which doesn't run while the world is paused - put them on
        /// the controllers directly (the body's arms reach for them).
        /// </summary>
        private void MoveHandsToControllers()
        {
            if (_jointHands == null || Time.timeScale > 0f)
                return;
            foreach (var hand in _jointHands)
            {
                if (hand != null && hand.Target != null)
                    hand.transform.SetPositionAndRotation(hand.Target.position, hand.Target.rotation);
            }
        }

        /// <summary>
        /// The screen keeps a fixed direction (turning the head doesn't move it - that would make people sick), but
        /// follows the head's position: PlayVideo often comes before the script teleports the hero.
        /// </summary>
        private void FaceCamera()
        {
            var forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
            _screenForward = forward == Vector3.zero ? Vector3.forward : forward;
            _lastCameraPosition = _camera.transform.position;
            PlaceScreen();
        }

        private void PlaceScreen()
        {
            _screen.transform.position = _camera.transform.position + _screenForward * _screenDistance;
            _screen.transform.rotation = Quaternion.LookRotation(_screenForward);
        }

        private void LateUpdate()
        {
            if (_screen == null || _camera == null)
                return;

            MoveHandsToControllers();
            if (Vector3.Distance(_camera.transform.position, _lastCameraPosition) > _teleportDistance)
                FaceCamera();
            else
                PlaceScreen();
        }

        private void OnPrepared(VideoPlayer player)
        {
            // Original videos are 4:3 (640x480) - keep their aspect.
            if (player.width > 0 && player.height > 0)
            {
                var aspect = (float)player.height / player.width;
                _screen.transform.localScale = new Vector3(_screenWidth, _screenWidth * aspect, 1f);
            }
            player.Play();
        }

        private void Update()
        {
            if (_screen == null || Time.unscaledTime - _playStartTime < _skipInputDelay)
                return;

            if (IsSkipPressed())
            {
                Logger.Log("[VRCinema] Skipped", LogCat.VR);
                _videoPlayer.Stop();
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

        private void Awake()
        {
            gameObject.Inject();
        }

        private void OnDestroy()
        {
            // Destroyed without Close (scene change during a video) - never leave the game paused.
            if (!_isRunning)
                return;
            RestorePlayer();
            Time.timeScale = _savedTimeScale;
            AudioListener.pause = false;
            _instance = null;
        }

        private void Close()
        {
            if (_camera != null)
            {
                _camera.cullingMask = _savedCullingMask;
                _camera.clearFlags = _savedClearFlags;
                _camera.backgroundColor = _savedBackground;
            }
            RestorePlayer();
            AudioListener.pause = false;
            Time.timeScale = _savedTimeScale;
            StopBink();

            if (_screen != null)
                Destroy(_screen);
            if (_renderTexture != null)
                _renderTexture.Release();

            _isRunning = false;
            Destroy(gameObject);
            _instance = null;
            Logger.Log("[VRCinema] Closed", LogCat.VR);

            if (_isQuitAfterVideos)
            {
                _isQuitAfterVideos = false;
                Quit();
            }
        }
    }
}
#endif
