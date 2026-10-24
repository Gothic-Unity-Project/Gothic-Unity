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
    /// The canvas follows the head while it's shown (VRFollowCamera on the same GameObject).
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

        private Material _chapterMaterial;

        private void OnDestroy()
        {
            Destroy(_chapterMaterial);
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
            // Set texture for cover. One material for all chapters - only the texture changes.
            if (_chapterMaterial == null)
                _chapterMaterial = _textureService.GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            _textureService.SetTexture(texture, _chapterMaterial);
            _chapterImage.material = _chapterMaterial;

            _chapterTitle.text = chapter;
            _chapterSubtitle.text = text;
        }

        private IEnumerator DisableDelayed(int milliseconds)
        {
            // The AudioSource lives on this GO - never hide (and cut off the jingle) before the sound is done.
            // G2's chapter times are often shorter than their WAVs.
            var clipSeconds = _audioSource.clip != null ? _audioSource.clip.length : 0f;
            yield return new WaitForSeconds(Mathf.Max(milliseconds / 1000f, clipSeconds));

            gameObject.SetActive(false);
        }
    }
}
