using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using DirectMusic;
using Gothic.Core.Adapters.Audio;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
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

        /// <summary>
        /// Whenever we collide with a musicZoneVobGO, it's entry will be added to the list and the most important theme will be played.
        /// </summary>
        private readonly List<GameObject> _musicZones = new();

        // DeveloperConfig.EnableMusicZonesByPosition: zones by their bounding box, in world order (OpenGothic WorldSound).
        private readonly List<(string Name, Bounds Bounds)> _zoneBoxes = new();
        private string _defaultZoneName;
        private string _currentZoneName;
        private string _currentThemeName;
        private MusicZoneTicker _zoneTicker;

        // The hero is somebody's target (Npc_SetTarget) - fight/threat music until it ends.
        private NpcContainer _heroEnemy;
        private float _lastHeroTargetedTime = float.MinValue;
        private const float _fightMusicHoldSeconds = 10f;
        private const float _fightMusicMaxDistance = 25f;


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
            if (_configService.Dev.EnableMusicZonesByPosition)
            {
                TickZones(force: true);
                return;
            }
            PlayInternal();
        }

        /// <summary>
        /// Play a specific music theme without using musicZones and modes.
        /// e.g., for MainMenu music.
        /// </summary>
        public void Play(string musicInstanceName)
        {
            PlayInternal(musicInstanceName);
        }
        
        /// <summary>
        /// Play active theme with segment tags like NGT + FGT
        /// </summary>
        private void PlayInternal()
        {
            var zone = _musicZones
                .Select(i => i.GetComponentInParent<VobLoader>().Container.VobAs<IZoneMusic>())
                .OrderByDescending(i => i.Priority)
                .FirstOrDefault();

            if (zone == null)
                return;

            // e.g., Towerdungeon_TD => TD is needed only
            var formattedZoneName = zone.Name.Substring(zone.Name.IndexOf("_") + 1);

            var timeTag = TimeTag switch
            {
                MusicTagsTime.Day => "DAY",
                MusicTagsTime.Night => "NGT",
                _ => throw new ArgumentOutOfRangeException()
            };

            var modeTag = ModeTag switch
            {
                MusicTagsMode.Standard => "STD",
                MusicTagsMode.Thrill => "THR",
                MusicTagsMode.Fight => "FGT",
                _ => throw new ArgumentOutOfRangeException()
            };

            var musicThemeInstanceName = $"{formattedZoneName}_{timeTag}_{modeTag}";

            PlayInternal(musicThemeInstanceName);
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
        /// DeveloperConfig.EnableMusicZonesByPosition: like the engine (OpenGothic WorldSound::tickSoundZone) - the zone
        /// is picked from the player's position: the current zone stays while we're inside it, otherwise the last zone
        /// containing us (world order), else the default zone. A missing theme falls back NGT->DAY, THR->FGT->STD, then
        /// the default zone. Before, zone triggers with priorities picked a big zone around G2's pirate camp, night and
        /// fight tags never changed back and a missing theme stayed silent.
        /// </summary>
        public void TickZones(bool force = false)
        {
            if (!_configService.Dev.EnableMusicZonesByPosition || _themes.Count == 0 || _zoneBoxes.Count == 0 &&
                _defaultZoneName == null)
                return;

            var player = Camera.main != null ? Camera.main.transform : GameObject.FindWithTag(Constants.PlayerTag)?.transform;
            if (player == null)
                return;
            var position = player.position;

            string zoneName = null;
            if (_currentZoneName != null && _currentZoneName != _defaultZoneName && IsInZone(_currentZoneName, position))
                zoneName = _currentZoneName;
            else
            {
                foreach (var (name, bounds) in _zoneBoxes)
                    if (bounds.Contains(position))
                        zoneName = name;
            }
            zoneName ??= _defaultZoneName;

            UpdateTags();
            var themeName = FindTheme(zoneName);
            if (themeName == null || !force && themeName == _currentThemeName)
                return;

            if (zoneName != _currentZoneName)
                Logger.Log($"[Music] Zone '{_currentZoneName ?? "-"}' -> '{zoneName}'", LogCat.Audio);
            _currentZoneName = zoneName;
            _currentThemeName = themeName;
            Logger.Log($"[Music] Theme {themeName} (zone '{zoneName}', {TimeTag}, {ModeTag})", LogCat.Audio);
            PlayInternal(themeName);
        }

        private bool IsInZone(string zoneName, Vector3 position)
        {
            foreach (var (name, bounds) in _zoneBoxes)
                if (name == zoneName && bounds.Contains(position))
                    return true;
            return false;
        }

        /// <summary>
        /// Day 4:00-21:00 like the engine; fight/threat while somebody targets the hero (Npc_SetTarget) and is up,
        /// close and active.
        /// </summary>
        private void UpdateTags()
        {
            var gameTime = ReflexProjectInstaller.DIContainer.Resolve<Services.World.GameTimeService>();
            var hour = gameTime.GetCurrentDateTime().Hour;
            TimeTag = hour >= 4 && hour < 21 ? MusicTagsTime.Day : MusicTagsTime.Night;

            // The game's VM - _vm is the music VM (MUSIC.DAT): asking it for the hero crashed natively.
            var enemy = _heroEnemy;
            var hero = (NpcInstance)ReflexProjectInstaller.DIContainer.Resolve<Services.GameStateService>().GothicVm?.GlobalHero;
            var heroGo = hero?.GetUserData()?.Go;
            var isFight = enemy?.Go != null && heroGo != null &&
                          Time.time - _lastHeroTargetedTime < _fightMusicHoldSeconds &&
                          enemy.Props.BodyState is not (VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious) &&
                          Vector3.Distance(enemy.Go.transform.position, heroGo.transform.position) < _fightMusicMaxDistance;
            if (!isFight)
            {
                ModeTag = MusicTagsMode.Standard;
                return;
            }
            ModeTag = hero.GetUserData().Vob.FightMode == (int)VmGothicEnums.WeaponState.NoWeapon
                ? MusicTagsMode.Thrill
                : MusicTagsMode.Fight;
        }

        /// <summary>
        /// The zone's theme with OpenGothic's fallbacks: zone then default zone; current then day time; mode, then
        /// THR->FGT / STD.
        /// </summary>
        private string FindTheme(string zoneName)
        {
            var times = TimeTag == MusicTagsTime.Night ? new[] { "NGT", "DAY" } : new[] { "DAY" };
            var mode = ModeTag switch
            {
                MusicTagsMode.Thrill => "THR",
                MusicTagsMode.Fight => "FGT",
                _ => "STD"
            };
            var modes = mode == "THR" ? new[] { "THR", "FGT", "STD" } : mode == "FGT" ? new[] { "FGT", "STD" } : new[] { "STD" };

            foreach (var zone in new[] { zoneName, _defaultZoneName })
            {
                if (string.IsNullOrEmpty(zone))
                    continue;
                // e.g. Towerdungeon_TD => TD
                var separator = zone.IndexOf('_');
                var tag = separator >= 0 ? zone.Substring(separator + 1) : zone;
                foreach (var time in times)
                    foreach (var m in modes)
                    {
                        var name = $"{tag}_{time}_{m}".ToUpperInvariant();
                        if (_themes.ContainsKey(name))
                            return name;
                    }
            }

            Logger.LogWarning($"[Music] No theme for zone '{zoneName}' ({TimeTag}, {ModeTag}) nor the default zone", LogCat.Audio);
            return null;
        }

        /// <summary>
        /// Menu/loading screen: the last world's zones must not take the music back from SYS_MENU/SYS_LOADING.
        /// </summary>
        public void ClearZones()
        {
            _zoneBoxes.Clear();
            _defaultZoneName = null;
            _currentZoneName = null;
            _currentThemeName = null;
            _heroEnemy = null;
        }

        public void OnHeroTargeted(NpcContainer enemy)
        {
            _heroEnemy = enemy;
            _lastHeroTargetedTime = Time.time;
        }

        public void MusicZoneExited(GameObject go)
        {
            if (_configService.Dev.EnableMusicZonesByPosition)
                return;
            RemoveMusicZone(go);
            Play();

        }

        public void MusicZoneEntered(GameObject go)
        {
            // DeveloperConfig.EnableMusicZonesByPosition: TickZones decides from the position instead.
            if (_configService.Dev.EnableMusicZonesByPosition)
                return;
            AddMusicZone(go);
            Play();
        }

        public void AddMusicZone(GameObject newMusicZoneGo)
        {
            // If a collider triggers multiple times or we added the zone manually: Skip as duplicate
            if (_musicZones.Contains(newMusicZoneGo))
                return;

            _musicZones.Add(newMusicZoneGo);
        }

        public void RemoveMusicZone(GameObject newMusicZoneGo)
        {
            _musicZones.Remove(newMusicZoneGo);
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

        /// <summary>
        /// A world's music starts on a fresh DirectMusic performance. dmusic crashes natively (DmPerformance_renderPcm on
        /// the audio thread, editor/game gone) once a world theme plays after gamestart.sgt (menu/loading music) on the
        /// same performance - reproduced outside Unity: gamestart -> ADW_DAY_STD crashes with every transition type,
        /// ADW <-> PIR for 15 rounds without gamestart don't. G2: leaving the pirate camp crashed the editor.
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

        public void OnWorldLoaded()
        {
            _musicZones.Clear();
            ResetPerformance();

            if (_configService.Dev.EnableMusicZonesByPosition)
            {
                CollectZoneBoxes();
                if (_zoneTicker == null && _audioSourceComp != null)
                {
                    _zoneTicker = _audioSourceComp.gameObject.AddComponent<MusicZoneTicker>();
                    _zoneTicker.OnTick = () => TickZones();
                }
                TickZones(force: true);
                return;
            }

            var zones = _multiTypeCacheService.VobCache.Where(i => i.Vob is IZoneMusic); // IZoneMusic | IZoneMusicDefault
            var playerPosition = GameObject.FindWithTag(Constants.PlayerTag).transform.position;

            foreach (var zone in zones)
            {
                // We always set default music as fallback.
                if (zone.Vob.GetType() == typeof(ZoneMusicDefault))
                {
                    AddMusicZone(zone.Go);
                    continue;
                }

                // If it's a normal music, we check if we're standing inside.
                if (zone.Go.GetComponentInChildren<BoxCollider>().bounds.Contains(playerPosition))
                {
                    AddMusicZone(zone.Go);
                }
            }

            Play();
        }

        private void CollectZoneBoxes()
        {
            _zoneBoxes.Clear();
            _defaultZoneName = null;
            _currentZoneName = null;
            _currentThemeName = null;
            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container?.Vob is not IZoneMusic zone || string.IsNullOrEmpty(zone.Name))
                    continue;
                if (zone is ZoneMusicDefault)
                {
                    _defaultZoneName = zone.Name;
                    continue;
                }

                var bounds = new Bounds(zone.BoundingBox.Min.ToUnityVector(), Vector3.zero);
                bounds.Encapsulate(zone.BoundingBox.Max.ToUnityVector());
                _zoneBoxes.Add((zone.Name, bounds));
            }
            Logger.Log($"[Music] {_zoneBoxes.Count} music zone(s), default '{_defaultZoneName ?? "-"}'", LogCat.Audio);
        }
    }
}
