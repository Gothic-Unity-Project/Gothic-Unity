using System;
using UnityEngine;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// DeveloperConfig.EnableMusicZonesByPosition: calls MusicDomain's zone check every few seconds, like the engine
    /// (OpenGothic WorldSound::tickSoundZone checks every 5 s). Sits on the background music GameObject.
    /// </summary>
    public class MusicZoneTicker : MonoBehaviour
    {
        public Action OnTick;
        public float Interval = 2f;

        private float _nextTick;

        private void Update()
        {
            if (Time.unscaledTime < _nextTick)
                return;

            _nextTick = Time.unscaledTime + Interval;
            OnTick?.Invoke();
        }
    }
}
