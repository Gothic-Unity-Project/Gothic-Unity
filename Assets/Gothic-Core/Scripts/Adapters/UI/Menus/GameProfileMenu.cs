using System;
using System.Collections.Generic;
using Gothic.Core.Models.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit;

namespace Gothic.Core.Adapters.UI.Menus
{
    /// <summary>
    /// World-space menu of the found game profiles (game + game ini) shown at start when there is more than one.
    /// One column per game (Gothic 1 left, Gothic 2 right) with its title and its profiles below. The layout lives in
    /// the prefab; one entry prefab per profile is created at runtime, the VR laser / flat mouse clicks it.
    /// </summary>
    public class GameProfileMenu : MonoBehaviour
    {
        [Tooltip("Created once per found game profile. Its child TMP_Text shows the profile's name.")]
        [SerializeField] private Button _entryPrefab;

        [Tooltip("Gothic 1 column (title + entries). Hidden when no Gothic 1 profile was found.")]
        [SerializeField] private GameObject _gothic1Column;

        [Tooltip("Parent of the Gothic 1 entries; its layout group stacks them.")]
        [SerializeField] private RectTransform _gothic1Entries;

        [Tooltip("Gothic 2 column (title + entries). Hidden when no Gothic 2 profile was found.")]
        [SerializeField] private GameObject _gothic2Column;

        [Tooltip("Parent of the Gothic 2 entries; its layout group stacks them.")]
        [SerializeField] private RectTransform _gothic2Entries;

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

            var hasGothic1 = false;
            var hasGothic2 = false;
            foreach (var profile in profiles)
            {
                var isGothic1 = profile.Version == GameVersion.Gothic1;
                hasGothic1 |= isGothic1;
                hasGothic2 |= !isGothic1;

                var entry = Instantiate(_entryPrefab, isGothic1 ? _gothic1Entries : _gothic2Entries);
                entry.name = $"Profile_{profile.Name}";
                entry.GetComponentInChildren<TMP_Text>().text = profile.Name;
                entry.onClick.AddListener(() => Select(profile));
            }

            _gothic1Column.SetActive(hasGothic1);
            _gothic2Column.SetActive(hasGothic2);

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
