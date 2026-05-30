using System;
using System.Threading.Tasks;
using GUZ.Core.Domain.Audio;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.Player
{
    public class SpeechToTextService : IDisposable
    {
        public bool IsEnabled => _domain.IsInitialized;

        private SpeechToTextDomain _domain;

        public void Init()
        {
            // Check if we have any microphones connected
            if (Microphone.devices.Length <= 0)
            {
                Logger.Log("No microphone detected! Disable feature.", LogCat.Audio);
                return;
            }
            else
            {
                Logger.Log($"Unity detected the following microphones: {string.Join(";", Microphone.devices)}", LogCat.Audio);
            }
            
#pragma warning disable CS4014 // Whisper might take some seconds to initialize. Do not wait.
            InitializeWhisper();
#pragma warning restore CS4014
        }

#pragma warning disable CS1998 // Whisper might take some seconds to initialize. Do not wait.
        private async Task InitializeWhisper()
        {
            _domain = new SpeechToTextDomain().Inject();
            _domain.Init();
        }
#pragma warning restore CS1998
        

        public void StartExec(AudioClip recordedClip)
        {
            _domain.StartExec(recordedClip);
        }

        public bool IsTranscribing()
        {
            return _domain.IsTranscribing;
        }

        public string GetOutputString()
        {
            return _domain.OutputString;
        }


        public void Dispose()
        {
            _domain?.Dispose();
            _domain = null;
        }
    }
}
