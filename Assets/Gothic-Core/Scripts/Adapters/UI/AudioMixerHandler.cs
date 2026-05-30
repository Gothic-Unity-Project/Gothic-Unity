using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GUZ.Core.Adapters.UI
{
    public class AudioMixerHandler : MonoBehaviour
    {
        [FormerlySerializedAs("audioMixer")] [SerializeField]
        private AudioMixerGroup _audioMixer;

        [FormerlySerializedAs("audioVolumeSlider")] [SerializeField]
        private Slider _audioVolumeSlider;

        [FormerlySerializedAs("volumePlayerPrefName")] [SerializeField]
        private string _volumePlayerPrefName;

        private void Awake()
        {
            var oldVolume = PlayerPrefs.GetFloat(_volumePlayerPrefName, 1f);
            _audioVolumeSlider.value = oldVolume;
        }

        public void SliderUpdate(float value)
        {
            PlayerPrefs.SetFloat(_volumePlayerPrefName, value);
            // Volume and loudness are not the same, volume can be linear but loudness is logarithmic
            // https://www.msdmanuals.com/home/multimedia/table/measurement-of-loudness
            _audioMixer.audioMixer.SetFloat(_volumePlayerPrefName, Mathf.Log10(value) * 20);
        }
    }
}
