using System;
using System.Collections.Generic;
using Gothic.Core.Models.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.Core.Adapters.UI.Menus
{
    /// <summary>
    /// World-space list of the found game profiles (game + game ini) shown at start when there is more than one.
    /// The layout lives in the GameVersion scene; one entry prefab per profile is created at runtime, the VR laser /
    /// flat mouse clicks it.
    /// </summary>
    public class GameProfileMenu : MonoBehaviour
    {
        [Tooltip("Created once per found game profile. Its child TMP_Text shows the profile's name.")]
        [SerializeField] private Button _entryPrefab;

        [Tooltip("Parent of the entries; its layout group stacks them.")]
        [SerializeField] private RectTransform _entryContainer;

        [Tooltip("Distance in meters the menu is placed in front of the camera.")]
        [SerializeField] private float _distanceToCamera = 1.2f;

        // The scene's teleport lands a frame or more later (XRI) - the
        // menu was placed in front of the falling camera, below the floor. It follows the camera this long.
        private const float _followCameraSeconds = 0.5f;
        private float _followCameraUntil;

        private Action<GameProfile> _onSelected;
        private bool _isSelected;

        public void Build(IReadOnlyList<GameProfile> profiles, Action<GameProfile> onSelected)
        {
            _onSelected = onSelected;
            GetComponent<Canvas>().worldCamera = Camera.main;

            foreach (var profile in profiles)
            {
                var entry = Instantiate(_entryPrefab, _entryContainer);
                entry.name = $"Profile_{profile.Name}";
                entry.GetComponentInChildren<TMP_Text>().text = profile.Name;
                entry.onClick.AddListener(() => Select(profile));
            }

            PlaceInFrontOfCamera();
            _followCameraUntil = Time.unscaledTime + _followCameraSeconds;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _followCameraUntil)
                PlaceInFrontOfCamera();
        }

        private void Select(GameProfile profile)
        {
            // A second click while the game boots would init everything twice.
            if (_isSelected)
                return;

            _isSelected = true;
            gameObject.SetActive(false);
            _onSelected?.Invoke(profile);
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                transform.SetPositionAndRotation(new Vector3(0f, 1.5f, _distanceToCamera), Quaternion.identity);
                return;
            }

            var forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;

            // A world-space canvas is read along its +Z axis, so it looks away from the camera.
            transform.SetPositionAndRotation(cam.transform.position + forward * _distanceToCamera,
                Quaternion.LookRotation(forward));
        }
    }
}
