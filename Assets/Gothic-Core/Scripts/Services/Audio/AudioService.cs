using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gothic.Core.Const;
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

        // Animation sounds that carry further than their *eventSFX range (R:). Finite list, all others keep theirs.
        private static readonly Dictionary<string, float> _sfxRangeOverridesMeters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "HAMMER", 24f }
            };

        private AudioSource _scriptSoundSource;

        // Gothic.ini soundVolume, read on first use and again when the menu changes it. Stays unset while Gothic.ini
        // isn't loaded (the player scene runs before a game was picked): the default would stick otherwise.
        private float? _soundVolume;
        // Sound effect sources that follow the slider (SetSfxVolume): their volume without it.
        private readonly Dictionary<AudioSource, float> _sfxSources = new();
        private readonly List<AudioSource> _destroyedSfxSources = new();

        public SoundEffectInstance InvOpen => _vmCacheService.TryGetSfxData("INV_OPEN").GetFirstSound();
        public SoundEffectInstance InvClose => _vmCacheService.TryGetSfxData("INV_CLOSE").GetFirstSound();

        public AudioService()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnSoundOptionUpdated);
            // The old world's sources are gone by now.
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(RemoveDestroyedSfxSources);
        }

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
        /// The player's sound effects volume (options slider, Gothic.ini [SOUND] soundVolume), 0..1. Voices and music
        /// have their own.
        /// </summary>
        public float SoundVolume
        {
            get
            {
                if (_soundVolume.HasValue)
                    return _soundVolume.Value;

                var volume = Mathf.Clamp01(_configService.GetGothicOption(Constants.IniNames.SoundVolume,
                    Constants.IniNames.SoundVolumeDefaultValue));
                if (_configService.Gothic == null)
                    return volume;

                _soundVolume = volume;
                Logger.Log($"[SoundVolume] Sound effects at {volume:0.##} (Gothic.ini soundVolume).", LogCat.Audio);
                return volume;
            }
        }

        /// <summary>
        /// Sets a sound effect source's volume: the sound's own (Gothic's, 0..1) times the player's SoundVolume. The
        /// source follows the slider from now on - a loop that plays (a VOB's campfire, the forge fire) changes at
        /// once. Call it again when the source plays another sound with another volume.
        /// </summary>
        public void SetSfxVolume(AudioSource source, float soundVolume)
        {
            _sfxSources[source] = soundVolume;
            source.volume = soundVolume * SoundVolume;
        }

        private void OnSoundOptionUpdated(string key, object value)
        {
            if (key != Constants.IniNames.SoundVolume)
                return;

            _soundVolume = null;
            var volume = SoundVolume;
            foreach (var (source, soundVolume) in _sfxSources)
            {
                if (source != null)
                    source.volume = soundVolume * volume;
            }
            RemoveDestroyedSfxSources();
        }

        private void RemoveDestroyedSfxSources()
        {
            foreach (var (source, _) in _sfxSources)
            {
                if (source == null)
                    _destroyedSfxSources.Add(source);
            }

            foreach (var source in _destroyedSfxSources)
                _sfxSources.Remove(source);
            _destroyedSfxSources.Clear();
        }

        /// <summary>
        /// Range (m) in which an animation sound (*eventSFX) is audible: the override of the few sounds that carry
        /// further than Gothic's range, else Gothic's own.
        /// Chosen in play: Fellan's hammering (R:1000 = 10 m) must reach Alwin's sheep pen ~18 m away (his quest asks
        /// whether you hear it); 60 m was heard across the harbour, 24 m reaches the pen. Unity's linear rolloff is
        /// quiet long before Gothic's range is felt; a general rolloff change is a follow-up.
        /// </summary>
        public float GetSfxRangeMeters(string sfxName, float gothicRangeMeters)
        {
            return _sfxRangeOverridesMeters.TryGetValue(sfxName, out var overrideMeters)
                ? overrideMeters
                : gothicRangeMeters;
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
                _scriptSoundSource.PlayOneShot(clip, SoundVolume);
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
            return GetRandomSoundClip(soundName, out _);
        }

        /// <summary>
        /// Like GetRandomSoundClip(), with Gothic's volume of the picked sound (its C_SFX_DEF vol as 0..1 - a random
        /// variant _A1, _A2 has its own, Forge_Sharpen is quiet). A .wav file name has none: 1.
        /// </summary>
        public AudioClip GetRandomSoundClip(string soundName, out float volume)
        {
            AudioClip clip;
            volume = 1f;

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

                clip = CreateAudioClip(sfxContainer.GetRandomSound(out volume));
            }

            return clip;
        }
    }
}
