using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Services;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace GUZ.Core.Models.Audio
{
    /// <summary>
    /// As there is a potential for multiple instances per key (e.g., BreathBubbles, BreathBubbles_A1, BreathBubbles_A2),
    /// we need to retrieve a container holding all of them.
    /// </summary>
    public class SfxModel
    {
        // TODO - Injecting in a Model wasn't intended. Try to use it without injecting.
        [Inject] private readonly GameStateService _gameStateService;

        private string soundKey;
        private SoundEffectInstance[]  _soundEffects;

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
            if (_soundEffects == null)
                LoadSoundEffects();

            if (_soundEffects.IsEmpty())
                return null;
            else if (_soundEffects!.Length == 1)
                return _soundEffects.First();
            else
                return _soundEffects.GetRandom();
        }

        private void LoadSoundEffects()
        {
            var sounds = new List<SoundEffectInstance>();

            try
            {
                var firstSound = _gameStateService.SfxVm.InitInstance<SoundEffectInstance>(soundKey);
                sounds.Add(firstSound);
            }
            catch (Exception e)
            {
                // If the key itself doesn't exist, then we don't need to look further.
                _soundEffects = Array.Empty<SoundEffectInstance>();
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
            
            _soundEffects = sounds.ToArray();
        }
    }
}
