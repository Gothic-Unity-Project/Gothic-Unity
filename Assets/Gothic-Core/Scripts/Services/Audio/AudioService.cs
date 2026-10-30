using System.Threading.Tasks;
using Gothic.Core.Domain.Audio;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Manager
{
    public class AudioService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;

        public const string NoSoundName = "nosound.wav";

        private readonly MusicDomain _musicDomain = new MusicDomain().Inject();
        private readonly SoundDomain _soundDomain = new SoundDomain().Inject();

        private AudioSource _scriptSoundSource;

        public SoundEffectInstance InvOpen => _vmCacheService.TryGetSfxData("INV_OPEN").GetFirstSound();
        public SoundEffectInstance InvClose => _vmCacheService.TryGetSfxData("INV_CLOSE").GetFirstSound();
        
        //
        // Music
        //

        public void SetBackgroundMusic(AudioSource audioSource, AudioReverbFilter reverbFilter)
        {
            _musicDomain.SetBackgroundMusic(audioSource, reverbFilter);
        }

        public void InitMusic()
        {
            // Skipping Init() leaves _themes empty, so every Play*/Zone* call below safely no-ops and
            // dmusic's native engine is never touched (see GothicModIniConfig.UnitySkipMusicPlayback).
            if (!_configService.GothicMod.UnitySkipMusicPlayback)
                _musicDomain.Init();

            GlobalEventDispatcher.MainMenuSceneLoaded.AddListener(OnMainMenuLoaded);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(_musicDomain.OnWorldLoaded);

            GlobalEventDispatcher.MusicZoneEntered.AddListener(_musicDomain.MusicZoneEntered);
            GlobalEventDispatcher.MusicZoneExited.AddListener(_musicDomain.MusicZoneExited);
            
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener((key, _) =>
            {
                if (key == "musicVolume" || key == "musicEnabled")
                {
                    _musicDomain.UpdateMusicValuesFromIni();
                }
            });
            
            GlobalEventDispatcher.SetHeroAsTarget.AddListener((enemy, hero) =>
            {
                _musicDomain.ModeTag = hero.Vob.FightMode == (int)VmGothicEnums.WeaponState.NoWeapon ? MusicDomain.MusicTagsMode.Thrill : MusicDomain.MusicTagsMode.Fight;
                _musicDomain.Play();
            });
        }

        public void PlayMusic(string musicInstanceName)
        {
            _musicDomain.Play(musicInstanceName);
        }

        private void OnMainMenuLoaded()
        {
            PlayMusic("SYS_MENU");
        }

        private void OnLoadingSceneLoaded()
        {
            PlayMusic("SYS_LOADING");
        }


        //
        // Sound
        //

        public void SetScriptSoundSource(AudioSource audioSource)
        {
            _scriptSoundSource = audioSource;
        }

        /// <summary>
        /// Non-positional sound, e.g. Daedalus Snd_Play("LogEntry").
        /// </summary>
        public void PlaySound2D(string soundName)
        {
            if (_scriptSoundSource == null)
                return;

            var clip = GetRandomSoundClip(soundName);
            if (clip != null)
                _scriptSoundSource.PlayOneShot(clip);
        }

        [CanBeNull]
        public AudioClip CreateAudioClip(SoundEffectInstance soundEffectInstance)
        {
            return CreateAudioClip(soundEffectInstance.File);
        }

        /// <summary>
        /// Create AudioClip from a file inside .vdf containers.
        /// Usage: ToAudioClip("fileName"):
        /// </summary>
        [CanBeNull]
        public AudioClip CreateAudioClip(string fileName)
        {
            return _soundDomain.CreateAudioClip(fileName);
        }

        /// <summary>
        /// Like CreateAudioClip(), but decodes on a worker thread (see SoundDomain.CreateAudioClipAsync()).
        /// The result is null if the file is missing or broken.
        /// </summary>
        public Task<AudioClip> CreateAudioClipAsync(string fileName)
        {
            return _soundDomain.CreateAudioClipAsync(fileName);
        }
        
        /// <summary>
        /// Hint: If you want to fetch sounds randomly, do not cache them on e.g., MonoBehavior, but fetch them each time you want to run it.
        ///       This method caches the AudioClips itself automatically. No performance penalty when re-running this method.
        /// </summary>
        public AudioClip GetRandomSoundClip(string soundName)
        {
            AudioClip clip;

            if (soundName.EqualsIgnoreCase(NoSoundName))
            {
                //instead of decoding nosound.wav which might be decoded incorrectly, just return null
                return null;
            }

            // Bugfix - Normally the data is to get C_SFX_DEF entries from VM. But sometimes there might be the real .wav file stored.
            if (soundName.EndsWithIgnoreCase(".wav"))
            {
                clip = CreateAudioClip(soundName);
            }
            else
            {
                var sfxContainer = _vmCacheService.TryGetSfxData(soundName);

                if (sfxContainer == null)
                    return null;

                // Instead of decoding nosound.wav which might be decoded incorrectly, just return null.
                if (sfxContainer.GetFirstSound() == null || sfxContainer.GetFirstSound()!.File.EqualsIgnoreCase(NoSoundName))
                    return null;

                clip = CreateAudioClip(sfxContainer.GetRandomSound());
            }

            return clip;
        }
    }
}
