using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using DirectMusic;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Audio
{
    public class MusicDomain
    {
        public enum MusicTagsTime
        {
            Day,
            Night
        }

        public enum MusicTagsMode
        {
            Standard,
            Thrill,
            Fight
        }

        public MusicTagsTime TimeTag = MusicTagsTime.Day;
        public MusicTagsMode ModeTag = MusicTagsMode.Standard;

        [Inject] private ConfigService _configService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;


        // Depending on speed of track, 2048 == around less than a second
        // If we cache each call to dxMusic synthesizer, we would skip a lot of transition options as the synthesizer assumes we're already ahead.
        // This is due to the fact, that whenever we ask for data from dxMusic, the handler "moves" forward as it assumes we play to the end until asking for more data.
        // But if we ask for numerous seconds and therefore "cache" music way too long, the transition will take place very late which can be heard by gamers.
        private const int _bufferSize = 2048;
        private const int _frequencyRate = 44100;


        private AudioSource _audioSourceComp;
        private AudioReverbFilter _reverbFilterComp;
        // Swapped on world load (ResetPerformance) while the audio thread renders - volatile for PCMReaderCallback.
        private volatile Performance _dxPerformance;
        // The replaced performance stays referenced until the next reset - the audio thread may still be rendering it.
        private Performance _retiredPerformance;

        private Dictionary<string, MusicThemeInstance> _themes = new();
        private MusicThemeInstance _currentTheme;
        private DaedalusVm _vm;

        // Set once RenderPcm faulted (see PCMReaderCallback). Read and written on the audio thread only.
        private bool _isMusicRenderingDisabled;

        // The world's music zones with their boxes in world order, its default zone, the zone playing and the player
        // (OpenGothic WorldSound). Empty in the menu and while loading.
        private readonly List<(IZoneMusic Zone, Bounds Bounds)> _zones = new();
        private IZoneMusic _defaultZone;
        private IZoneMusic _currentZone;
        private Transform _player;


        public void SetBackgroundMusic(AudioSource audioSource, AudioReverbFilter reverbFilter)
        {
            _audioSourceComp = audioSource;
            _reverbFilterComp = reverbFilter;
        }

        public void Init()
        {
            _dxPerformance = Performance.Create(_frequencyRate);

            InitializeUnity();
            InitializeDxMusic();
        }

        /// <summary>
        /// Play active music zone theme with segment tags like NGT + FGT
        /// </summary>
        public void Play()
        {
            PlayInternal();
        }

        /// <summary>
        /// Play a specific music theme without using musicZones and modes.
        /// e.g., for MainMenu music. The last world's zones don't take the music back until the next world is loaded.
        /// </summary>
        public void Play(string musicInstanceName)
        {
            ClearZones();
            PlayInternal(musicInstanceName);
        }

        /// <summary>
        /// Like the engine (OpenGothic WorldSound::tickSoundZone): the zone playing stays while the player is inside it,
        /// else the last zone containing him in world order plays, else the default zone. Zones by priority picked the
        /// big zone around G2's pirate camp instead of the camp's.
        /// </summary>
        public void UpdateZone()
        {
            if (_player == null)
                return;

            var zone = FindZone(_player.position);
            if (zone == _currentZone)
                return;

            Logger.Log($"[Music] Zone '{_currentZone?.Name ?? "-"}' -> '{zone?.Name ?? _defaultZone?.Name ?? "-"}'",
                LogCat.Audio);
            _currentZone = zone;
            Play();
        }
        
        /// <summary>
        /// The time of day and the danger the hero is in changed (AudioService checks them). Plays the matching theme.
        /// </summary>
        public void SetSituation(MusicTagsTime time, MusicTagsMode mode)
        {
            if (TimeTag == time && ModeTag == mode)
                return;

            TimeTag = time;
            ModeTag = mode;
            Play();
        }

        /// <summary>
        /// Play active theme with segment tags like NGT + FGT. Like the engine (OpenGothic
        /// WorldSound::tickSoundZone), a zone that has no theme for this situation falls back: night to day, threat to
        /// fight, both to standard, and at last the zones with lower priority (the world's default zone).
        /// </summary>
        private void PlayInternal()
        {
            var zones = new[] { _currentZone, _defaultZone }.Where(i => i != null);

            var timeTags = TimeTag == MusicTagsTime.Night ? new[] { "NGT", "DAY" } : new[] { "DAY" };
            var modeTags = ModeTag switch
            {
                MusicTagsMode.Standard => new[] { "STD" },
                MusicTagsMode.Thrill => new[] { "THR", "FGT", "STD" },
                MusicTagsMode.Fight => new[] { "FGT", "STD" },
                _ => throw new ArgumentOutOfRangeException()
            };

            foreach (var zone in zones)
            {
                // e.g., Towerdungeon_TD => TD is needed only
                var formattedZoneName = zone.Name.Substring(zone.Name.IndexOf("_") + 1);
                foreach (var timeTag in timeTags)
                {
                    foreach (var modeTag in modeTags)
                    {
                        if (!_themes.TryGetValue($"{formattedZoneName}_{timeTag}_{modeTag}", out var theme))
                            continue;

                        PlayInternal(theme);
                        return;
                    }
                }
            }
        }
        
        private void PlayInternal(string musicInstanceName)
        {
            if (!_themes.TryGetValue(musicInstanceName, out var music))
                return;
            
            PlayInternal(music);
        }

        private void PlayInternal(MusicThemeInstance theme)
        {
            // Do not restart the current theme if already playing.
            // Multiple MusicThemeInstances can reference the same audio. Therefore, checking actual files only.
            if (_currentTheme != null && theme.File == _currentTheme.File)
            {
                return;
            }
            
            var themeFileNode = _resourceCacheService.Vfs.Find(theme.File);
            if (themeFileNode == null)
            {
                Logger.LogWarning($"Music theme >{theme.File}< not found in VFS. Safety skip", LogCat.Audio);
                return;
            }

            // DMusic crashes for some unfinished small themes (<3kB). Therefore, skipping them now with a warning until fixed.
            // Issue report: https://github.com/GothicKit/dmusic-cs/issues/1

            if (themeFileNode.Buffer.Bytes.Length < 1000)
            {
                Logger.LogWarning($"Music theme >{theme.File}< might be broken with less than 1kB size. Safety skip", LogCat.Audio);
                return;
            }

            var segment = _resourceCacheService.TryGetSegment(theme.File);
            if (segment == null)
            {
                Logger.LogWarning($"Music theme >{theme.File}< could not be prepared for playback. Safety skip",
                    LogCat.Audio);
                return;
            }

            var timing = ToTiming(theme.TransSubType);
            var embellishment = ToEmbellishment(theme.TransType);

            Logger.Log($"Changing music theme to: {theme.File}", LogCat.Audio);

            _dxPerformance.PlayTransition(segment, embellishment, timing);

            // Tests sounded feasible like when you stop the music you get somme afterglow hall.
            // TODO - But I have no clue if decayTime is the right timer to set here. Alter if you have better ears than I have. ;-)
            _reverbFilterComp.decayTime = theme.ReverbTime / 1000; // ms in seconds

            _currentTheme = theme;
        }

        /// <summary>
        /// Set values based on Gothic.ini
        /// </summary>
        public void UpdateMusicValuesFromIni()
        {
            // If we disable the music component, we can't re-enable music again. It's better to mute it.
            if (!_configService.Gothic.IniMusicEnabled)
                _audioSourceComp.volume = 0f;
            else
                // A music sound of 1f is way too loud. We therefore turn it down based on some experience.
                _audioSourceComp.volume = _configService.Gothic.IniMusicVolume / 3;
        }

        private void InitializeUnity()
        {
            UpdateMusicValuesFromIni();

            var audioClip = AudioClip.Create("Music", _bufferSize * 2, 2, _frequencyRate, true, PCMReaderCallback);

            _audioSourceComp.priority = 0;
            _audioSourceComp.clip = audioClip;
            _audioSourceComp.loop = true;
            _audioSourceComp.Play();
        }

        private void InitializeDxMusic()
        {
            // Load the VM and initialize all music theme instances
            _vm = _resourceCacheService.TryGetDaedalusVm("MUSIC");
            _vm.GetInstanceSymbols("C_MUSICTHEME").ForEach(v =>
            {
                _themes[v.Name] = _vm.InitInstance<MusicThemeInstance>(v);
            });
        }

        private Timing ToTiming(MusicTransitionType type)
        {
            return type switch
            {
                MusicTransitionType.Measure or MusicTransitionType.Unknown => Timing.Measure,
                MusicTransitionType.Immediate => Timing.Instant,
                MusicTransitionType.Beat => Timing.Beat,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        private Embellishment ToEmbellishment(MusicTransitionEffect effect)
        {
            return effect switch
            {
                // None or Unknown needs to be set to End - otherwise normal transitions won't happen in G1 music.
                MusicTransitionEffect.Unknown or MusicTransitionEffect.None => Embellishment.End,
                MusicTransitionEffect.Groove => Embellishment.Groove,
                MusicTransitionEffect.Fill => Embellishment.Fill,
                MusicTransitionEffect.Break => Embellishment.Break,
                MusicTransitionEffect.Intro => Embellishment.Intro,
                MusicTransitionEffect.End => Embellishment.End,
                MusicTransitionEffect.EndAndInto => Embellishment.EndAndIntro,
                _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
            };
        }

        /// <summary>
        /// The player walked through a zone's border: the zone is picked again at once instead of with the next check.
        /// </summary>
        public void MusicZoneExited(GameObject go)
        {
            UpdateZone();
        }

        public void MusicZoneEntered(GameObject go)
        {
            UpdateZone();
        }

        private IZoneMusic FindZone(Vector3 position)
        {
            if (_currentZone != null && IsInside(_currentZone, position))
                return _currentZone;

            IZoneMusic found = null;
            foreach (var (zone, bounds) in _zones)
            {
                if (bounds.Contains(position))
                    found = zone;
            }

            return found;
        }

        private bool IsInside(IZoneMusic zone, Vector3 position)
        {
            foreach (var (candidate, bounds) in _zones)
            {
                if (candidate == zone && bounds.Contains(position))
                    return true;
            }

            return false;
        }

        private void ClearZones()
        {
            _zones.Clear();
            _defaultZone = null;
            _currentZone = null;
            _player = null;
        }

        /// <summary>
        /// A world's music starts on a fresh DirectMusic performance. dmusic crashes natively (DmPerformance_renderPcm on
        /// the audio thread, editor/game gone) once a world theme plays after gamestart.sgt (menu/loading music) on the
        /// same performance - found by BoroBongo, reproduced outside Unity. G2: leaving the pirate camp crashed the
        /// editor.
        /// </summary>
        private void ResetPerformance()
        {
            if (_dxPerformance == null)
                return;

            _retiredPerformance = _dxPerformance;
            _dxPerformance = Performance.Create(_frequencyRate);
            _currentTheme = null;
            Logger.Log("[Music] Fresh DirectMusic performance for the world's music", LogCat.Audio);
        }

        // Some mod compositions (e.g. Gothic II New Balance's EGE_DAY_Std.sgt) embed an automatic
        // segue to another segment (observed: "Endgegner") that dmusic triggers internally mid-render,
        // entirely inside native code we never see or get to pre-validate via TryGetSegment. If that
        // segment's referenced instruments/patches are incomplete, it hits the same native double-free
        // as a directly-requested broken segment (see ResourceCacheService.TryGetSegment), but this
        // time as a raw AccessViolationException with zero managed frames in between. Ordinarily that's
        // an unrecoverable Corrupted State Exception .NET won't let a try/catch see at all; ONLY the
        // Mono runtime backing the Editor/Player may behave differently, hence trying anyway.
        [HandleProcessCorruptedStateExceptions]
        private void PCMReaderCallback(float[] data)
        {
            // Once RenderPcm has faulted, _dxPerformance's native internals are potentially corrupted —
            // calling into it again next buffer (which this callback would otherwise do, regardless of
            // audio volume) risks repeating the crash. Stop touching it entirely, permanently, for the
            // rest of this session; silence is a fine trade-off for "the game keeps running".
            if (_isMusicRenderingDisabled)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            try
            {
                _dxPerformance.RenderPcm(data, true);
            }
            catch (Exception e)
            {
                _isMusicRenderingDisabled = true;
                Logger.LogError($"RenderPcm threw ({e.GetType().Name}: {e.Message}). Music is disabled for the " +
                                "rest of this session.", LogCat.Audio);
                Array.Clear(data, 0, data.Length);
            }
        }

        public void OnWorldLoaded()
        {
            ClearZones();
            ResetPerformance();
            // A fight of the world before is over. The time follows with AudioService's next check.
            ModeTag = MusicTagsMode.Standard;

            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container?.Vob is not IZoneMusic zone)
                    continue;

                if (zone is ZoneMusicDefault)
                {
                    _defaultZone = zone;
                    continue;
                }

                var bounds = new Bounds(zone.BoundingBox.Min.ToUnityVector(), Vector3.zero);
                bounds.Encapsulate(zone.BoundingBox.Max.ToUnityVector());
                _zones.Add((zone, bounds));
            }

            _player = GameObject.FindWithTag(Constants.PlayerTag)?.transform;
            _currentZone = _player != null ? FindZone(_player.position) : null;
            Play();
        }
    }
}
