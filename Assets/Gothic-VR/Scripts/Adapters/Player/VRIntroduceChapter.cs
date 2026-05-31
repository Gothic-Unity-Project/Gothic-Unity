#if GUZ_HVR_INSTALLED
using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// UI logic handler for Daedalus call of IntroduceChapter()
    /// </summary>
    public class VRIntroduceChapter : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private Canvas _chapterCanvas;
        [SerializeField] private Image _chapterImage;
        [SerializeField] private TMP_Text _chapterTitle;
        [SerializeField] private TMP_Text _chapterSubtitle;

        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly AudioService _audioService;

        // Data needed for smooth movement of Canvas
        private Transform _cameraTransform;
        private float _canvasMoveSmoothTime = 0.3f;
        private Vector3 _canvasMovementOffset = new(0, 0, 4f); // Expected offset of Canvas in front of Camera view.
        private Vector3 _canvasMoveVelocity = Vector3.zero;

        private void Update()
        {
            if (_cameraTransform == null)
            {
                return;
            }

            // Damp movement of canvas in front of us. Causes less friction when viewed.
            var targetPosition = _cameraTransform.position + _cameraTransform.TransformDirection(_canvasMovementOffset);
            var pos = Vector3.SmoothDamp(transform.position, targetPosition, ref _canvasMoveVelocity, _canvasMoveSmoothTime);
            var rot = Quaternion.LookRotation(transform.position - _cameraTransform.position);
            transform.SetPositionAndRotation(pos, rot);
        }
        
        public void DisplayIntroduction(string chapter, string text, string texture, string wav, int time)
        {
            gameObject.SetActive(true);
            
            PlayAudio(wav);
            ShowChapterCanvas(chapter, text, texture);
            
            StartCoroutine(DisableDelayed(time));
        }

        private void PlayAudio(string wav)
        {
            _audioSource.clip = _audioService.CreateAudioClip(wav);
            _audioSource.Play();
        }

        private void ShowChapterCanvas(string chapter, string text, string texture)
        {
            // Set texture for cover
            _chapterImage.material = _textureService.GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            _textureService.SetTexture(texture, _chapterImage.material);

            _chapterTitle.text = chapter;
            _chapterSubtitle.text = text;

            // Set canvas position to move towards.
            _cameraTransform = Camera.main!.transform;
            
            // Set initial canvas position (otherwise it will fly towards us when shown).
            var pos = _cameraTransform.position + _cameraTransform.TransformDirection(_canvasMovementOffset);
            var rot = Quaternion.LookRotation(transform.position - _cameraTransform.position);
            transform.SetPositionAndRotation(pos, rot);
        }

        private IEnumerator DisableDelayed(int milliseconds)
        {
            yield return new WaitForSeconds(milliseconds / 1000f);

            _canvasMoveVelocity = Vector3.zero;
            gameObject.SetActive(false);
        }
    }
}
#endif
