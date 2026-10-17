using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.UI;
using Gothic.Core.Services.Vm;
using Gothic.Services.UI;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain
{
    public class BootstrapDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly VmExternalService _vmExternalService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly FontService _fontService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly VmCacheService _vmCacheService;

        
        public BootstrapDomain()
        {
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
        }
        
        public void Boot()
        {
            LoadGothicVm();
            LoadMiscVMs();
            SetLanguage();
            LoadDialogs();
            LoadSubtitles();
            LoadVideos();
            LoadFonts();
            LoadGuildData();

            GlobalEventDispatcher.ZenKitBootstrapped.Invoke();
        }

        /// <summary>
        /// We set language based on one of the first Daedalus string constants loaded inside DaedalusVm.
        /// We check for expected values with different StringEncoding values. As they're always converted to UTF-8,
        /// we can easily check them for a match.
        /// </summary>
        private void SetLanguage()
        {
            // cs
            if (CheckEncoding(StringEncoding.CentralEurope, "MOBNAME_CRATE", "Bedna"))
                _localizationService.SetLanguage("cs", StringEncoding.CentralEurope);
            // pl - 2x: "Skrzynka" is used by some mods (e.g. G2 Renovation) instead of vanilla "Skrzynia"
            else if (CheckEncoding(StringEncoding.CentralEurope, "MOBNAME_CRATE", "Skrzynia", "Skrzynka"))
                _localizationService.SetLanguage("pl", StringEncoding.CentralEurope);
            // ru - 2x: "Ящик" is used by some mods (e.g. Dolina Zombie) instead of vanilla "Коробка"
            else if (CheckEncoding(StringEncoding.EastEurope, "MOBNAME_CRATE", "Коробка", "Ящик"))
                _localizationService.SetLanguage("ru", StringEncoding.EastEurope);
            // de
            else if (CheckEncoding(StringEncoding.WestEurope, "MOBNAME_CRATE", "Kiste"))
                _localizationService.SetLanguage("de", StringEncoding.WestEurope);
            // en - 2x as G1 vs G2 use a different value for it
            else if (CheckEncoding(StringEncoding.WestEurope, "MOBNAME_CRATE", "Crate", "Box"))
                _localizationService.SetLanguage("en", StringEncoding.WestEurope);
            // es
            else if (CheckEncoding(StringEncoding.WestEurope, "MOBNAME_CRATE", "Caja"))
                _localizationService.SetLanguage("es", StringEncoding.WestEurope);
            // fr
            else if (CheckEncoding(StringEncoding.WestEurope, "MOBNAME_CRATE", "Boite"))
                _localizationService.SetLanguage("fr", StringEncoding.WestEurope);
            // it
            else if (CheckEncoding(StringEncoding.WestEurope, "MOBNAME_CRATE", "Cassa"))
                _localizationService.SetLanguage("it", StringEncoding.WestEurope);
            // Nothing found
            // TODO - Potentially re-enable error label on screen to say: We couldn't identify your language.
            // TODO - Add values for Gothic 2
            else if (!TrySetFallbackLanguage())
                throw new CultureNotFoundException("Language couldn't be identified based on current Gothic " +
                                                   "installation. Set unityFallbackLanguage in the mod's ini " +
                                                   "(or DeveloperConfig.FallbackLanguage) to boot anyway.");
        }

        /// <summary>
        /// Mods frequently rename MOBNAME_CRATE (the auto-detection probe constant), which would
        /// otherwise hard-fail the boot. The unityFallbackLanguage entry in the mod's ini (or else
        /// DeveloperConfig.FallbackLanguage) lets the mod author/user pick the language manually for such installations.
        /// </summary>
        private bool TrySetFallbackLanguage()
        {
            var fallback = _configService.GothicMod?.UnityFallbackLanguage?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(fallback))
                fallback = _configService.Dev.FallbackLanguage?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(fallback))
                return false;

            var encoding = fallback switch
            {
                "cs" or "pl" => StringEncoding.CentralEurope,
                "ru" => StringEncoding.EastEurope,
                "de" or "en" or "es" or "fr" or "it" => StringEncoding.WestEurope,
                _ => (StringEncoding)0
            };
            if (encoding == 0)
            {
                Logger.LogError($"unityFallbackLanguage '{fallback}' is not a known language code " +
                                "(cs, pl, ru, de, en, es, fr, it).", LogCat.ZenKit);
                return false;
            }

            var probeValue = _gameStateService.GothicVm.GetSymbolByName("MOBNAME_CRATE")?.GetString(0) ?? "<missing>";
            Logger.LogWarning($"Language auto-detection failed (MOBNAME_CRATE='{probeValue}') — using " +
                              $"mod ini unityFallbackLanguage '{fallback}'.",
                LogCat.ZenKit);

            StringEncodingController.SetEncoding(encoding);
            _localizationService.SetLanguage(fallback, encoding);
            return true;
        }

        private bool CheckEncoding(StringEncoding encoding, string daedalusConstantToCheck,
            params string[] valuesToCheck)
        {
            StringEncodingController.SetEncoding(encoding);

            var l10nSymbol = _gameStateService.GothicVm.GetSymbolByName(daedalusConstantToCheck)!;
            var l10nString = l10nSymbol.GetString(0);

            return valuesToCheck.Contains(l10nString);
        }

        /// <summary>
        /// Every game start gets a fresh Gothic VM. DeveloperConfig.EnableDialogsPerGameVm: its C_Info instances too -
        /// the dialogs were initialized once at boot on the first VM, so what scripts write into them during the game
        /// (SMITHWEAPON_S1: PC_ItMw_1H_Common.description = NAME_ItMw_1H_Common_01) went to the new VM's unbound symbols
        /// and the anvil's weapon choices stayed empty.
        /// </summary>
        private void OnLoadGameStart()
        {
            LoadGothicVm();

            // DeveloperConfig.EnableVmCacheResetOnLoad: item/SVM/spell instances of the old VM are dead in the new one.
            if (_configService.Dev.EnableVmCacheResetOnLoad)
            {
                _vmCacheService.ResetGothicVmInstances();
                Logger.Log("[Bootstrap] VM instance caches reset for the new VM", LogCat.ZenKit);
            }

            if (_configService.Dev.EnableDialogsPerGameVm)
            {
                _gameStateService.Dialogs.Instances.Clear();
                LoadDialogs();
                Logger.Log($"[Bootstrap] {_gameStateService.Dialogs.Instances.Count} dialogs initialized on the new VM",
                    LogCat.Dialog);
            }
        }

        public void LoadGothicVm()
        {
            _gameStateService.GothicVm = _resourceCacheService.TryGetDaedalusVm("GOTHIC");

            _vmExternalService.RegisterExternals();
            LoadIkarusLeGo();

            _npcHelperService.Init();
        }

        private void LoadIkarusLeGo()
        {
            var ikarusVersionSymbol = _gameStateService.GothicVm.GetSymbolByName("IKARUS_VERSION");

            // No Ikarus in scripts, nothing to do.
            if (ikarusVersionSymbol == null)
                return;

            var version = ikarusVersionSymbol.GetInt(0);

            if (version != 10202)
            {
                Logger.LogWarning("Ikarus handling tested with version 10202 only. Expect anything and nothing.", LogCat.ZenKit);
            }
            
            _vmExternalService.RegisterIkarusLeGo();
        }
        
        private void LoadMiscVMs()
        {
            _gameStateService.FightVm = _resourceCacheService.TryGetDaedalusVm("FIGHT");
            _gameStateService.MenuVm = _resourceCacheService.TryGetDaedalusVm("MENU");
            _gameStateService.SfxVm = _resourceCacheService.TryGetDaedalusVm("SFX");
            _gameStateService.PfxVm = _resourceCacheService.TryGetDaedalusVm("PARTICLEFX");
            _gameStateService.VfxVm = _resourceCacheService.TryGetDaedalusVm("VISUALFX");
        }

        /// <summary>
        /// We load all dialogs once and assign them to the appropriate NPCs once spawned.
        /// </summary>
        private void LoadDialogs()
        {
            var infoInstances = _gameStateService.GothicVm.GetInstanceSymbols("C_Info")
                .Select(symbol => _gameStateService.GothicVm.InitInstance<InfoInstance>(symbol))
                .ToList();

            infoInstances.ForEach(i => _gameStateService.Dialogs.Instances.Add(i));
        }

        private void LoadSubtitles()
        {
            var rootPath = _contextGameVersionService.RootPath;
            var suffixes = new[] { _contextGameVersionService.CutsceneFileSuffix, "BIN", "CSL" };

            // VDFS/ is checked first — it holds the mod's OU.BIN extracted from DM_Speech.mod by hand.
            // ZenKit's CutsceneLibrary has no VFS support, so it needs a file on disk.
            string GetDiskPath(string folder) => suffixes
                .Select(s => Path.Combine(rootPath, folder, "DATA", "scripts", "content", "CUTSCENE", $"OU.{s}"))
                .FirstOrDefault(File.Exists);

            var cutscenePath = GetDiskPath("VDFS/_WORK");
            // DeveloperConfig.EnableSubtitlesFromVfs: a mod's OU.BIN packed in its VDF/.mod (New Balance, Dolina Zombie)
            // is written to a temporary file - no manual VDFS extraction needed any more.
            if (cutscenePath == null && _configService.Dev.EnableSubtitlesFromVfs && _configService.IsModActive)
                cutscenePath = ExtractSubtitlesFromVfs(suffixes);
            cutscenePath ??= GetDiskPath("_work");
            if (cutscenePath == null && _configService.Dev.EnableSubtitlesFromVfs)
                cutscenePath = ExtractSubtitlesFromVfs(suffixes);

            if (cutscenePath == null)
            {
                Logger.LogWarning("OU.BIN/CSL not found — dialog subtitles disabled.", LogCat.Dialog);
                return;
            }

            Logger.Log($"Loading CutsceneLibrary from: {cutscenePath}", LogCat.Dialog);
            _gameStateService.Dialogs.CutsceneLibrary = new(cutscenePath);
        }

        /// <summary>
        /// The mounted archives' OU.BIN/OU.CSL (the mod's wins, VfsOverwriteBehavior.All) as a temporary file. Null if
        /// no archive has one.
        /// </summary>
        private string ExtractSubtitlesFromVfs(string[] suffixes)
        {
            foreach (var suffix in suffixes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fileName = $"OU.{suffix}";
                var bytes = _resourceCacheService.Vfs.Find(fileName)?.Buffer.Bytes;
                if (bytes == null || bytes.Length == 0)
                    continue;

                try
                {
                    var path = Path.Combine(UnityEngine.Application.temporaryCachePath, fileName);
                    File.WriteAllBytes(path, bytes);
                    Logger.Log($"Subtitles {fileName} read from the archives ({bytes.Length / 1024} KB) -> {path}",
                        LogCat.Dialog);
                    return path;
                }
                catch (Exception e)
                {
                    Logger.LogError($"Subtitles {fileName} from the archives couldn't be written: {e.Message}",
                        LogCat.Dialog);
                }
            }
            return null;
        }

        private void LoadVideos()
        {
            _videoService.InitVideos();
        }

        private void LoadFonts()
        {
            _fontService.Create();
        }

        /// <summary>
        /// Only the human block of the guild table comes from the scripts (GIL_ATTITUDES) - everything else stayed 0 =
        /// ATT_HOSTILE: an orc warrior and an orc scout were enemies, and when one drew its weapon against the hero the
        /// others attacked it. Orcs of all castes are friends, and every monster guild is friendly to itself.
        /// Scripts can still change it (Wld_SetGuildAttitude).
        /// </summary>
        private void InitMonsterGuildAttitudes()
        {
            var count = _gameStateService.GuildCount;
            var friendly = (int)VmGothicEnums.Attitude.Friendly;
            // From the scripts - G1 and G2 number their guilds differently.
            var vm = _gameStateService.GothicVm;
            var humanSeparator = vm.GetSymbolByName("GIL_SEPERATOR_HUM")?.GetInt(0) ?? -1;
            var orcSeparator = vm.GetSymbolByName("GIL_SEPERATOR_ORC")?.GetInt(0) ?? -1;
            if (humanSeparator < 0 || orcSeparator < 0)
            {
                Logger.LogWarning("GIL_SEPERATOR_HUM/ORC not found - monster guild attitudes stay hostile.", LogCat.Npc);
                return;
            }
            var firstOrcGuild = orcSeparator + 1;

            for (var guild = humanSeparator + 1; guild < count; guild++)
                _gameStateService.GuildAttitudes[guild * count + guild] = friendly;

            for (var guild1 = firstOrcGuild; guild1 < count; guild1++)
                for (var guild2 = firstOrcGuild; guild2 < count; guild2++)
                    _gameStateService.GuildAttitudes[guild1 * count + guild2] = friendly;
        }

        private void LoadGuildData()
        {
            var guildMax = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.GuildMaxValue);

            _gameStateService.GuildCount = guildMax?.GetInt(0) ?? 0;

            var tablesize = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.GuildHumanAttitudesSize);
            _gameStateService.GuildHumanCount = (int)(tablesize != null ? Math.Sqrt(tablesize.GetInt(0)) : 0);

            _gameStateService.GuildAttitudes = new int[_gameStateService.GuildCount * _gameStateService.GuildCount];
            InitMonsterGuildAttitudes();

            var id = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.GuildValuesInstance);
            if (id == null)
            {
                return;
            }

            _gameStateService.GuildValues = _gameStateService.GothicVm.InitInstance<GuildValuesInstance>(id);

            if (_contextGameVersionService.IsGothic2())
            {
                // In G2, the Daedalus way of setting Human attitudes between each others is commented out with:
                // >>> func void B_InitGuildAttitudes ()
                // >>> ***NICHT machen!***
                // We therefore set these values now.
                _vmExternalService.ExchangeGuildAttitudes(DaedalusConst.GuildHumanAttitudesArray);
            }
            
            // TODO - Can be removed? -> When Gil_Values is instanciated inside G1 Daedalus, all values are already filled. No need to copy Human values into it.
            // for (var i = 0; i < (int)VmGothicEnums.Guild.GIL_PUBLIC; ++i)
            // {
            //     _gameStateService.GuildValues.SetWaterDepthKnee(i, _gameStateService.GuildValues.GetWaterDepthKnee((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetWaterDepthChest(i, _gameStateService.GuildValues.GetWaterDepthChest((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetJumpUpHeight(i, _gameStateService.GuildValues.GetJumpUpHeight((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetSwimTime(i, _gameStateService.GuildValues.GetSwimTime((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetDiveTime(i, _gameStateService.GuildValues.GetDiveTime((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetStepHeight(i, _gameStateService.GuildValues.GetStepHeight((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetJumpLowHeight(i, _gameStateService.GuildValues.GetJumpLowHeight((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetJumpMidHeight(i, _gameStateService.GuildValues.GetJumpMidHeight((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetSlideAngle(i, _gameStateService.GuildValues.GetSlideAngle((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetSlideAngle2(i, _gameStateService.GuildValues.GetSlideAngle2((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetDisableAutoRoll(i, _gameStateService.GuildValues.GetDisableAutoRoll((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetSurfaceAlign(i, _gameStateService.GuildValues.GetSurfaceAlign((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetClimbHeadingAngle(i, _gameStateService.GuildValues.GetClimbHeadingAngle((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetClimbHorizAngle(i, _gameStateService.GuildValues.GetClimbHorizAngle((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetClimbGroundAngle(i, _gameStateService.GuildValues.GetClimbGroundAngle((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRangeBase(i, _gameStateService.GuildValues.GetFightRangeBase((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRangeFist(i, _gameStateService.GuildValues.GetFightRangeFist((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRangeG(i, _gameStateService.GuildValues.GetFightRangeG((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRange1Hs(i, _gameStateService.GuildValues.GetFightRange1Hs((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRange1Ha(i, _gameStateService.GuildValues.GetFightRange1Ha((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRange2Hs(i, _gameStateService.GuildValues.GetFightRange2Hs((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFightRange2Ha(i, _gameStateService.GuildValues.GetFightRange2Ha((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFallDownHeight(i, _gameStateService.GuildValues.GetFallDownHeight((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetFallDownDamage(i, _gameStateService.GuildValues.GetFallDownDamage((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodDisabled(i, _gameStateService.GuildValues.GetBloodDisabled((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodMaxDistance(i, _gameStateService.GuildValues.GetBloodMaxDistance((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodAmount(i, _gameStateService.GuildValues.GetBloodAmount((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodFlow(i, _gameStateService.GuildValues.GetBloodFlow((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetTurnSpeed(i, _gameStateService.GuildValues.GetTurnSpeed((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodEmitter(i, _gameStateService.GuildValues.GetBloodEmitter((int)VmGothicEnums.Guild.GIL_HUMAN));
            //     _gameStateService.GuildValues.SetBloodTexture(i, _gameStateService.GuildValues.GetBloodTexture((int)VmGothicEnums.Guild.GIL_HUMAN,0)); //TODO: PR FIX to zenkitcs, getter should have only 1 param not 2
            // }
        }
    }
}
