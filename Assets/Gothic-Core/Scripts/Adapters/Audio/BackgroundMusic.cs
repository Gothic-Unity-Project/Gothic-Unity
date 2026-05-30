using GUZ.Core.Manager;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Adapters.Audio
{
    public class BackgroundMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioReverbFilter _audioReverbFilter;

        [Inject] private readonly AudioService _audioService;

        private void Start()
        {
            _audioService.SetBackgroundMusic(_audioSource, _audioReverbFilter);
        }
    }
}
