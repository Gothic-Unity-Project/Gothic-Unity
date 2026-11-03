using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Audio
{
    public class BackgroundMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioReverbFilter _audioReverbFilter;

        [Inject] private readonly AudioService _audioService;

        [Tooltip("Seconds between two checks of time of day and danger, which choose the music theme.")]
        [SerializeField] private float _situationCheckInterval = 2f;

        private float _nextSituationCheck;

        private void Start()
        {
            _audioService.SetBackgroundMusic(_audioSource, _audioReverbFilter);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextSituationCheck)
                return;

            _nextSituationCheck = Time.unscaledTime + _situationCheckInterval;
            _audioService.UpdateMusicSituation();
        }
    }
}
