using System.Collections;
using GUZ.Core.Logging;
using UnityEngine;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace GUZ.Core.Adapters.Vob
{
    public class SoundHandler : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource;
        
        private ISound _vob;

        // We need to avoid starting the Coroutine twice.
        private bool _isCoroutineRunning;

        /// <summary>
        /// Sounds are in LevelCompo and also as sub-VOBs inside Fire.
        /// We therefore set Vob data directly instead of relying on GetCompInParent(VobLoader) which might deliver IFire instead of ISound.
        /// </summary>
        public void Init(ISound vob)
        {
            _vob = vob;
            if (_vob == null)
            {
                Logger.LogError("VobSoundProperties.soundData not set. Can't register random sound play!", LogCat.Audio);
                return;
            }

            StartCoroutine();
        }
        
        private void OnEnable()
        {
            StartCoroutine();
        }

        private void OnDisable()
        {
            // Coroutines are stopped when GameObject gets disabled. But we need to restart during OnEnable() manually.
            _isCoroutineRunning = false;
        }

        private void StartCoroutine()
        {
            // Either it's not yet initialized (no clip) or it's no random loop
            if (_audioSource.clip == null || _vob.Mode != SoundMode.Random)
            {
                return;
            }

            if (_isCoroutineRunning)
            {
                return;
            }

            StartCoroutine(ReplayRandomSound());
            _isCoroutineRunning = true;
        }

        private IEnumerator ReplayRandomSound()
        {
            while (true)
            {
                var nextRandomPlayTime = _vob.RandomDelay
                                         + Random.Range(0.0f, _vob.RandomDelayVar);
                yield return new WaitForSeconds(nextRandomPlayTime);

                _audioSource.Play();
                yield return new WaitForSeconds(_audioSource.clip.length);
            }
        }
    }
}
