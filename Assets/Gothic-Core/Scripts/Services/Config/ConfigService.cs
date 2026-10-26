using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Domain.Config;
using Gothic.Core.Logging;
using Gothic.Core.Models.Config;
using MyBox;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Config
{
    /// <summary>
    /// Combines three sources of configuration:
    /// 1. Gothic.ini and GothicGame.ini from Gothic installation directory containing original Gothic settings
    /// 2. GameSettings.json from Gothic-Unity/StreamingAssets path for root configuration (e.g. log level)
    /// 3. DeveloperConfig ScriptableObject for developer settings
    /// </summary>
    public class ConfigService
    {
        public JsonRootConfig Root { get; private set; }
        public DeveloperConfig Dev { get; private set; }
        public GothicIniConfig Gothic { get; private set; }
        public GothicModIniConfig GothicMod { get; private set; }

        /// <summary>
        /// Profile picked in the game profile menu (GameSettings.json "EnableGameProfiles"). When set, its ModPath/ModIni
        /// replace the normal ones - also in the Editor - and no ModPath means vanilla. Null = normal config.
        /// </summary>
        public JsonGameProfile SelectedGameProfile { get; private set; }

        public bool IsGameProfileSelectionEnabled => GetConfiguredGameProfiles().Length > 0;

        /// <summary>
        /// Editor: DeveloperConfig EnableGameProfiles/GameProfiles when switched on, otherwise GameSettings.json.
        /// Builds: GameSettings.json only. Empty when the feature is off.
        /// </summary>
        private JsonGameProfile[] GetConfiguredGameProfiles()
        {
#if UNITY_EDITOR
            if (Dev.EnableGameProfiles)
                return JsonRootLoader.SanitizeGameProfiles(Dev.GameProfiles);
#endif
            return Root.EnableGameProfiles && Root.GameProfiles != null
                ? Root.GameProfiles
                : Array.Empty<JsonGameProfile>();
        }

        public string EffectiveModPath
        {
            get
            {
                if (SelectedGameProfile != null)
                    return SelectedGameProfile.ModPath;
#if UNITY_EDITOR
                return Dev.PreSelectMod && !Dev.ModPath.IsNullOrEmpty() ? Dev.ModPath : null;
#else
                return Root.ModPath;
#endif
            }
        }

        /// <summary>
        /// The game to start without the game selection. Editor: DeveloperConfig PreselectGameVersion + GameVersion.
        /// Builds: GameSettings.json "GameVersion" (Gothic1/Gothic2) when set, so one build runs G1, G2 and their
        /// mods by editing the JSON (like ModPath/ModIni); otherwise the build's DeveloperConfig decides.
        /// </summary>
        public bool TryGetPreselectedGameVersion(out GameVersion version)
        {
#if !UNITY_EDITOR
            if (TryParseGameVersion(Root.GameVersion, out version))
                return true;
#endif
            version = Dev.GameVersion;
            return Dev.PreselectGameVersion;
        }

        public bool TryGetGameProfileVersion(JsonGameProfile profile, out GameVersion version)
        {
            return TryParseGameVersion(profile?.GameVersion, out version);
        }

        /// <summary>
        /// Profiles that can be started: known GameVersion, that game is installed, and the mod folder (if any) exists.
        /// </summary>
        public List<JsonGameProfile> GetAvailableGameProfiles()
        {
            var available = new List<JsonGameProfile>();
            foreach (var profile in GetConfiguredGameProfiles())
            {
                if (!TryGetGameProfileVersion(profile, out var version))
                {
                    Logger.LogWarning($"Game profile >{profile.Name}< skipped: GameVersion >{profile.GameVersion}< " +
                        "is not Gothic1/Gothic2.", LogCat.Loading);
                    continue;
                }
                if (!CheckIfGothicInstallationExists(version))
                {
                    Logger.LogWarning($"Game profile >{profile.Name}< skipped: no {version} installation found.",
                        LogCat.Loading);
                    continue;
                }
                if (profile.ModPath != null && !Directory.Exists(profile.ModPath))
                {
                    Logger.LogWarning($"Game profile >{profile.Name}< skipped: ModPath >{profile.ModPath}< not found.",
                        LogCat.Loading);
                    continue;
                }

                available.Add(profile);
            }

            return available;
        }

        /// <summary>
        /// DeveloperConfig.EnableGameProfileIniChoice: a profile's ModIni "*" lists the mod's game inis found in its System
        /// folder (ModIniScanner), "A.INI;B.INI" lists those - one profile per ini to pick from (Mroczne Tajemnice's
        /// easy/normal/hard). Empty when there's nothing to choose (one fixed ini, or none = GothicGame.ini as before).
        /// </summary>
        public List<JsonGameProfile> GetGameProfileIniChoices(JsonGameProfile profile)
        {
            var choices = new List<JsonGameProfile>();
            if (!Dev.EnableGameProfileIniChoice || profile?.ModPath == null)
                return choices;

            var modIni = profile.ModIni?.Trim();
            if (string.IsNullOrEmpty(modIni))
                return choices;
            var isScan = modIni == "*";
            if (!isScan && modIni.IndexOfAny(new[] { ';', ',' }) < 0)
                return choices;

            var found = ModIniScanner.FindGameInis(profile.ModPath);
            IEnumerable<ModIniScanner.ModIni> inis = found;
            if (!isScan)
            {
                // A fixed list keeps its order; its titles come from the inis found.
                inis = modIni.Split(';', ',')
                    .Select(i => i.Trim())
                    .Where(i => i.Length > 0)
                    .Select(i => found.FirstOrDefault(f => f.FileName.Equals(i, StringComparison.OrdinalIgnoreCase))
                                 ?? new ModIniScanner.ModIni { FileName = i, Title = i });
            }

            foreach (var ini in inis)
            {
                choices.Add(new JsonGameProfile
                {
                    Name = $"{ini.Title} ({ini.FileName})",
                    GameVersion = profile.GameVersion,
                    ModPath = profile.ModPath,
                    ModIni = ini.FileName
                });
            }
            return choices;
        }

        public void SelectGameProfile(JsonGameProfile profile)
        {
            SelectedGameProfile = profile;
            Logger.Log($"Game profile selected: >{profile.Name}< ({profile.GameVersion}, " +
                $"mod: {profile.ModPath ?? "none"}, ini: {profile.ModIni ?? "GothicGame.ini"})", LogCat.Loading);
        }

        private static bool TryParseGameVersion(string value, out GameVersion version)
        {
            switch (value?.Trim().ToUpperInvariant())
            {
                case "GOTHIC1":
                case "G1":
                case "1":
                    version = GameVersion.Gothic1;
                    return true;
                case "GOTHIC2":
                case "G2":
                case "2":
                    version = GameVersion.Gothic2;
                    return true;
                default:
                    version = GameVersion.Gothic1;
                    return false;
            }
        }

        public string EffectiveModIni
        {
            get
            {
                if (SelectedGameProfile != null)
                    return SelectedGameProfile.ModIni;
#if UNITY_EDITOR
                return Dev.PreSelectMod && !Dev.ModIni.IsNullOrEmpty() ? Dev.ModIni : null;
#else
                return Root.ModIni;
#endif
            }
        }

        /// <summary>
        /// EffectiveModIni with the same "GothicGame.ini" fallback LoadGothicInis applies — a mod
        /// (e.g. New Balance) can be active with no ModIni set at all, relying purely on the default.
        /// </summary>
        public string EffectiveModIniFileName => EffectiveModIni.IsNullOrEmpty() ? "GothicGame.ini" : EffectiveModIni;

        /// <summary>
        /// Whether a mod is active right now, i.e. ModPath (mod in its own game root) or ModIni (mod installed
        /// into the Gothic folder, selected by its ini) resolved to something. This is the single source of
        /// truth for "is a mod active" - use it instead of Dev.PreSelectMod directly, which only means
        /// "use DeveloperConfig's ModPath/ModIni" and ignores any other mod source.
        /// </summary>
        public bool IsModActive => !EffectiveModPath.IsNullOrEmpty() || !EffectiveModIni.IsNullOrEmpty();

        /// <summary>
        /// Folder-safe suffix identifying the active mod, so StaticCacheService keeps a separate cache
        /// per (GameVersion, mod) instead of one shared per GameVersion — switching between vanilla and
        /// several mods (or between mods) would otherwise stomp on each other's cached VOB bounds/texture
        /// arrays/world chunks, forcing a full recreate every time. Naming uses the mod folder (only if the mod
        /// has its own ModPath) and the resolved ini file (several mods use the default GothicGame.ini), e.g.
        /// "_mod_MroczneTajemnice_GothicGame" or "_mod_DM_E" for a mod inside the Gothic folder.
        /// Empty when running vanilla.
        /// </summary>
        public string ModCacheSuffix
        {
            get
            {
                if (!IsModActive)
                    return string.Empty;

                var iniName = ToFolderSafeName(Path.GetFileNameWithoutExtension(EffectiveModIniFileName));
                if (EffectiveModPath.IsNullOrEmpty())
                    return $"_mod_{iniName}";

                var folderName = ToFolderSafeName(Path.GetFileName(EffectiveModPath.TrimEnd('/', '\\')));
                return $"_mod_{folderName}_{iniName}";
            }
        }

        private static string ToFolderSafeName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !char.IsWhiteSpace(c) && !invalidChars.Contains(c)).ToArray());
        }

        /// <summary>
        /// Some mod music compositions embed a native dmusic segue we can't intercept (see
        /// MusicDomain.PCMReaderCallback) — turning music off entirely is the only workaround.
        /// Standalone builds read this from GameSettings.json/.dev.json so one build can be
        /// repointed at a different mod (and its music requirement) without a recompile; the
        /// Editor uses the DeveloperConfig Inspector toggle instead for quick iteration.
        /// </summary>
        public bool EffectiveEnableMusic
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableMusic;
#else
                return Root.EnableMusic;
#endif
            }
        }

        /// <summary>
        /// ZSpy (Daedalus PrintDebug/PrintDebugInst) logging, same Editor/build split as EffectiveEnableMusic —
        /// lets a standalone build be toggled on/off via GameSettings.json/.dev.json without a recompile,
        /// instead of only being reachable via the DeveloperConfig Inspector toggle in the Editor.
        /// </summary>
        public bool EffectiveEnableZSpyLogs
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableZSpyLogs;
#else
                return Root.EnableZSpyLogs;
#endif
            }
        }


        /// <summary>
        /// Some mods (e.g. New Balance) ship dubbing as Ogg Vorbis mislabeled with a ".wav" extension.
        /// SoundDomain sniffs real content and decodes via NVorbis when detected — this is an escape
        /// hatch in case a specific mod's Ogg files misbehave, same Editor/build split as the others.
        /// </summary>
        public bool EffectiveEnableOggAudio
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableOggAudio;
#else
                return Root.EnableOggAudio;
#endif
            }
        }

        /// <summary>
        /// HVR keyboard/mouse simulator instead of a headset, same Editor/build split as the others - a PC build can be
        /// played and shown without a headset. The simulator turns itself off on mobile, so it is always off there.
        /// </summary>
        public bool EffectiveEnableVRDeviceSimulator
        {
            get
            {
#if UNITY_EDITOR
                return Dev.EnableVRDeviceSimulator;
#else
                return Root.EnableVRDeviceSimulator && !UnityEngine.Application.isMobilePlatform;
#endif
            }
        }

        /// <summary>
        /// First one to load.
        /// Root, as it contains only a few Gothic specific bootstrap data like
        /// installation directory of Gothic1/2 and LogLevel.
        /// </summary>
        public void LoadRootJson()
        {
            Root = JsonRootLoader.Load();
        }

        /// <summary>
        /// Config provided from caller (basically GameManager or LabManager).
        /// </summary>
        public void SetDeveloperConfig(DeveloperConfig config)
        {
            // We simply reference the ScriptableObject from GameManager component.
            Dev = config;
        }

        /// <summary>
        /// Last one to be loaded. Whenever GameVersion is set already.
        /// </summary>
        public void LoadGothicInis(GameVersion version)
        {
            var baseRootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
            var rootPath = EffectiveModPath ?? baseRootPath;
            var gothicIniPath = Path.Combine(baseRootPath, "system/Gothic.ini");

            var gothicModIniPath = Path.Combine(rootPath, "system", EffectiveModIniFileName);

            Gothic = new GothicIniConfig(IniLoader.LoadFile(gothicIniPath), gothicIniPath);
            GothicMod = new GothicModIniConfig(IniLoader.LoadFile(gothicModIniPath), gothicModIniPath);
            
            if (!GothicMod.IsLoaded)
                throw new ArgumentException($"GothicMod Ini file not found: {gothicModIniPath}");
            
            GlobalEventDispatcher.GothicInisInitialized.Invoke();
        }

        public bool CheckIfGothicInstallationExists(GameVersion version)
        {
            var rootPath = version == GameVersion.Gothic1 ? Root.Gothic1Path : Root.Gothic2Path;
            return Directory.Exists($"{rootPath}/_work") && Directory.Exists($"{rootPath}/Data");
        }
    }
}
