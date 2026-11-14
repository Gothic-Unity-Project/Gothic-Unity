using System;
using System.Collections;
using Gothic.Core.Logging;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace Gothic.Core.Adapters.Vob
{
    public class SoundDaytimeHandler : MonoBehaviour
    {
        [SerializeField] private AudioSource _audioSource1;
        [SerializeField] private AudioSource _audioSource2;

        [Inject] private readonly GameTimeService _gameTimeService;
        
        private ISoundDaytime _vob;

        private DateTime _startSound1 = GameTimeService.MinTime;
        private DateTime _endSound1 = GameTimeService.MaxTime;

        // We need to avoid starting the Coroutine twice.
        private bool _isCoroutineRunning;
        private AudioSource _activeAudio;

        /// <summary>
        /// Set by VobInitializerDomain like SoundHandler.Init: a day/night sound can be a sub-VOB of another VOB (G1's
        /// birds in OW_LOB_TREE_V3), whose loader GetComponentInParent(VobLoader) would deliver instead.
        /// </summary>
        public void Init(ISoundDaytime vob)
        {
            _vob = vob;
            if (_vob == null)
            {
                Logger.LogError($"[SoundDaytime] {name} has no VOB data. It stays silent.", LogCat.Audio);
                return;
            }

            var startTime = _vob.StartTime;
            var endTime = _vob.EndTime;
            if (startTime != (int)startTime || endTime != (int)endTime)
            {
                Logger.LogError(
                    $"Currently fractional times for DayTimeAudio aren't supported. Only full hours are handled. " +
                    $"start={startTime} end={endTime}", LogCat.Audio);
                return;
            }

            _startSound1 = new DateTime(1, 1, 1, (int)startTime, 0, 0);
            _endSound1 = new DateTime(1, 1, 1, (int)endTime, 0, 0);
        }

        private void Start()
        {
            // Set active sound initially
            HourEventCallback(_gameTimeService.GetCurrentDateTime());

            if (gameObject.activeSelf)
                StartCoroutineInternal();
        }

        private void OnEnable()
        {
            HourEventCallback(_gameTimeService.GetCurrentDateTime());

            GlobalEventDispatcher.GameTimeHourChangeCallback.AddListener(HourEventCallback);

            // Reset sounds
            _audioSource1.enabled = false;
            _audioSource2.enabled = false;
            _audioSource1.Stop();
            _audioSource2.Stop();
            
            StartCoroutineInternal();
        }

        /// <summary>
        /// The clips are set once they're decoded (VobInitializerDomain). A random sound starts its loop then.
        /// Before Start(), Start() starts it itself.
        /// </summary>
        public void OnClipLoaded()
        {
            if (isActiveAndEnabled && _vob != null)
                StartCoroutineInternal();
        }

        private void OnDisable()
        {
            // Coroutines are stopped when GameObject gets disabled. But we need to restart during OnEnable() manually.
            _isCoroutineRunning = false;
            GlobalEventDispatcher.GameTimeHourChangeCallback.RemoveListener(HourEventCallback);
        }

        private void StartCoroutineInternal()
        {
            if (_audioSource1 == null)
                Logger.LogError($"Object {gameObject.name} has no audio source 1! Fix it!", LogCat.Audio);
            
            // Either it's not yet initialized (no clip) or it's no random loop
            if (_vob == null || _audioSource1.clip == null || _vob.Mode != SoundMode.Random)
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

        private void HourEventCallback(DateTime currentTime)
        {
            if (currentTime >= _startSound1 && currentTime < _endSound1)
            {
                SwitchToSound1();
            }
            else
            {
                SwitchToSound2();
            }
        }

        private void SwitchToSound1()
        {
            // No need to change anything.
            if (_audioSource1.isActiveAndEnabled)
            {
                return;
            }

            // disable
            _audioSource2.enabled = false;
            _audioSource2.Stop();

            // enable
            _audioSource1.enabled = true;
            _activeAudio = _audioSource1;
        }

        private void SwitchToSound2()
        {
            // No need to change anything.
            if (_audioSource2.isActiveAndEnabled)
            {
                return;
            }

            // disable
            _audioSource1.enabled = false;
            _audioSource1.Stop();

            // enable
            _audioSource2.enabled = true;
            _activeAudio = _audioSource2;
        }

        private IEnumerator ReplayRandomSound()
        {
            while (true)
            {
                var nextRandomPlayTime = _vob.RandomDelay + Random.Range(0.0f, _vob.RandomDelayVar);
                yield return new WaitForSeconds(nextRandomPlayTime);

                _activeAudio.Play();
                yield return new WaitForSeconds(_activeAudio.clip.length);
            }
        }
    }
}
