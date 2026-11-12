using System;
using System.Threading.Tasks;
using Gothic.Core.Domain.Audio;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Manager
{
    public class AudioService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly GameTimeService _gameTimeService;

        // #503: the last music situation, logged when it changes.
        private MusicDomain.MusicTagsMode _lastMusicMode;
        private MusicDomain.MusicTagsTime _lastMusicTime;

        public const string NoSoundName = "nosound.wav";

        private readonly MusicDomain _musicDomain = new MusicDomain().Inject();

        // Like the engine, day music plays from 4:00 to 21:00.
        private static readonly TimeSpan _musicDayStart = TimeSpan.FromHours(4);
        private static readonly TimeSpan _musicDayEnd = TimeSpan.FromHours(21);

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
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldLoaded);

            GlobalEventDispatcher.MusicZoneEntered.AddListener(_musicDomain.MusicZoneEntered);
            GlobalEventDispatcher.MusicZoneExited.AddListener(_musicDomain.MusicZoneExited);
            
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener((key, _) =>
            {
                if (key == "musicVolume" || key == "musicEnabled")
                {
                    _musicDomain.UpdateMusicValuesFromIni();
                }
            });
        }

        /// <summary>
        /// Like the engine (OpenGothic WorldSound::tickSoundZone), checked every few seconds (BackgroundMusic): night
        /// themes at night, and while an NPC attacks the hero the fight theme - the threat theme as long as he has no
        /// weapon drawn. Ends with the fight.
        /// </summary>
        public void UpdateMusicSituation()
        {
            var hero = _gameStateService.GothicVm.GetHeroContainer();
            if (hero?.Vob == null || hero.Props == null)
                return;

            var timeTag = GetMusicTime();
            var modeTag = MusicDomain.MusicTagsMode.Standard;
            if (hero.Props.IsDead || IsHeroAttacked(hero))
                modeTag = hero.Vob.FightMode == (int)VmGothicEnums.WeaponState.NoWeapon
                    ? MusicDomain.MusicTagsMode.Thrill
                    : MusicDomain.MusicTagsMode.Fight;

            // #503: which situation the music follows - logged when it changes.
            if (modeTag != _lastMusicMode || timeTag != _lastMusicTime)
            {
                Logger.Log($"[Music] {timeTag} {modeTag} (hero weapon state {hero.Vob.FightMode}, " +
                           $"dead {hero.Props.IsDead})", LogCat.Audio);
                _lastMusicMode = modeTag;
                _lastMusicTime = timeTag;
            }

            // Picks the zone too: a new zone and a new situation switch the theme once.
            _musicDomain.SetSituation(timeTag, modeTag);
        }

        private void OnWorldLoaded()
        {
            _musicDomain.OnWorldLoaded(GetMusicTime());
        }

        private MusicDomain.MusicTagsTime GetMusicTime()
        {
            var time = _gameTimeService.GetCurrentTime();
            return time >= _musicDayStart && time <= _musicDayEnd
                ? MusicDomain.MusicTagsTime.Day
                : MusicDomain.MusicTagsTime.Night;
        }

        /// <summary>
        /// The engine's check (OpenGothic WorldObjects::isTargetedBy): an NPC has the hero as its target, a weapon drawn
        /// and is in its attack state. Only awake NPCs count, like the engine's near ones (AiNormal): a culled attacker
        /// keeps ZS_ATTACK and its target until it wakes up again, and the fight music never ended after running away
        /// (#503).
        /// </summary>
        private bool IsHeroAttacked(NpcContainer hero)
        {
            foreach (var npc in _npcMeshCullingService.AwakeNpcs)
            {
                if (npc == hero || npc.Vob == null || npc.Props?.TargetNpc == null || npc.Props.IsDead ||
                    npc.Props.TargetNpc.Index != hero.Instance.Index ||
                    npc.Vob.FightMode == (int)VmGothicEnums.WeaponState.NoWeapon)
                    continue;

                var state = npc.Vob.CurrentStateName;
                if (state.EqualsIgnoreCase("ZS_ATTACK") || state.EqualsIgnoreCase("ZS_MM_ATTACK"))
                    return true;
            }

            return false;
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
            return CreateSfxAudioClip(soundEffectInstance.File);
        }

        /// <summary>
        /// The file of a sound effect instance (C_SFX) - from _WORK/DATA/SOUND/SFX first like the engine (a speech file
        /// with the same name doesn't win).
        /// </summary>
        [CanBeNull]
        public AudioClip CreateSfxAudioClip(string fileName)
        {
            return _soundDomain.CreateAudioClip(fileName, _sfxFolder);
        }

        /// <summary>
        /// Like CreateSfxAudioClip(), but decodes on a worker thread (see SoundDomain.CreateAudioClipAsync()).
        /// </summary>
        public Task<AudioClip> CreateSfxAudioClipAsync(string fileName)
        {
            return _soundDomain.CreateAudioClipAsync(fileName, _sfxFolder);
        }

        private const string _sfxFolder = "SFX";

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
