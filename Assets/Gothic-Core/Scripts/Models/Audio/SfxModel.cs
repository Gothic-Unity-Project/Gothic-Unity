using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Models.Audio
{
    /// <summary>
    /// As there is a potential for multiple instances per key (e.g., BreathBubbles, BreathBubbles_A1, BreathBubbles_A2),
    /// we need to retrieve a container holding all of them.
    /// </summary>
    public class SfxModel
    {
        // TODO - Injecting in a Model wasn't intended. Try to use it without injecting.
        [Inject] private readonly GameStateService _gameStateService;

        // C_SFX_DEF vol is 0..127 (the prototype's default 127).
        private const float _maxGothicVolume = 127f;

        private string soundKey;
        private SoundEffectInstance[]  _soundEffects;
        // Gothic's volume of each instance (vol, 0..1), same index as _soundEffects.
        private float[] _volumes;

        public int Count => _soundEffects.Length;

        
        public SfxModel(string preparedKey)
        {
            this.Inject();
            soundKey = preparedKey;
        }

        [CanBeNull]
        public SoundEffectInstance GetFirstSound()
        {
            if (_soundEffects == null)
                LoadSoundEffects();

            return _soundEffects!.FirstOrDefault();
        }
        
        [CanBeNull]
        public SoundEffectInstance GetRandomSound()
        {
            return GetRandomSound(out _);
        }

        /// <summary>
        /// Like GetRandomSound(), with Gothic's volume of the picked instance (vol as 0..1): a random variant
        /// (BreathBubbles_A1) has its own. 1 if there's none.
        /// </summary>
        [CanBeNull]
        public SoundEffectInstance GetRandomSound(out float volume)
        {
            if (_soundEffects == null)
                LoadSoundEffects();

            volume = 1f;
            if (_soundEffects.IsEmpty())
                return null;

            var index = _soundEffects!.Length == 1 ? 0 : UnityEngine.Random.Range(0, _soundEffects.Length);
            volume = _volumes[index];
            return _soundEffects[index];
        }

        private void LoadSoundEffects()
        {
            var sounds = new List<SoundEffectInstance>();

            try
            {
                var firstSound = _gameStateService.SfxVm.InitInstance<SoundEffectInstance>(soundKey);
                sounds.Add(firstSound);
            }
            catch (Exception)
            {
                // SFX symbol missing from Gothic's SFX scripts — expected for some broken VOBs (e.g. Wood_Night2 was never defined).
                Logger.LogWarning($"SFX symbol not found in VM: '{soundKey}' — no sound will play.", LogCat.Audio);
                _soundEffects = Array.Empty<SoundEffectInstance>();
                _volumes = Array.Empty<float>();
                return;
            }

            // Check if we have additional sounds which will be picked randomly at runtime.
            var randomIndex = 1;
            do
            {
                // e.g., BreathBubbles_A2
                var nextKey = $"{soundKey}_A{randomIndex}";
                try
                {
                    var nextSound = _gameStateService.SfxVm.InitInstance<SoundEffectInstance>(nextKey);
                    
                    // Hint: We also add nosound.wav entries. In G1, e.g., MOL_Ambient_A4 which is randomly picked, sometimes do not yell a sound - intended.
                    sounds.Add(nextSound);
                    randomIndex++;
                }
                catch (Exception)
                {
                    // Ignore
                    break;
                }
            } while (true);

            // The pain screams are numbered instead (SVM_15_AARGH, SVM_15_AARGH_1 ... _4 -> svm_15_aargh_1/2/3.wav) -
            // only the first one was ever heard.
            if (sounds.Count == 1)
            {
                for (var number = 1; ; number++)
                {
                    var nextKey = $"{soundKey}_{number}";
                    if (_gameStateService.SfxVm.GetSymbolByName(nextKey) == null)
                        break;
                    sounds.Add(_gameStateService.SfxVm.InitInstance<SoundEffectInstance>(nextKey));
                }
            }

            _soundEffects = sounds.ToArray();

            _volumes = new float[_soundEffects.Length];
            for (var i = 0; i < _soundEffects.Length; i++)
                _volumes[i] = UnityEngine.Mathf.Clamp01(_soundEffects[i].Volume / _maxGothicVolume);
        }
    }
}
