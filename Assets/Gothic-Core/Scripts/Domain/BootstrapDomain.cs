using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
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
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly VmExternalService _vmExternalService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly FontService _fontService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        
        public BootstrapDomain()
        {
            GlobalEventDispatcher.LoadGameStart.AddListener(LoadGothicVm);
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
            // pl
            else if (CheckEncoding(StringEncoding.CentralEurope, "MOBNAME_CRATE", "Skrzynia"))
                _localizationService.SetLanguage("pl", StringEncoding.CentralEurope);
            // ru
            else if (CheckEncoding(StringEncoding.EastEurope, "MOBNAME_CRATE", "Коробка"))
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
            // TODO - It also might make sense to manually overwrite/define language/Encoding via GameConfiguration.json - but only if needed in the future.
            // TODO - Add values for Gothic 2
            else
                throw new CultureNotFoundException("Language couldn't be identified based on current Gothic installation.");
        }

        private bool CheckEncoding(StringEncoding encoding, string daedalusConstantToCheck,
            params string[] valuesToCheck)
        {
            StringEncodingController.SetEncoding(encoding);

            var l10nSymbol = _gameStateService.GothicVm.GetSymbolByName(daedalusConstantToCheck)!;
            var l10nString = l10nSymbol.GetString(0);

            return valuesToCheck.Contains(l10nString);
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
            var cutsceneSuffix = _contextGameVersionService.CutsceneFileSuffix;
            var cutscenePath = $"{_contextGameVersionService.RootPath}/_work/DATA/scripts/content/CUTSCENE/OU.{cutsceneSuffix}";

            if (_resourceCacheService.ModPath != null)
            {
                foreach (var suffix in new[] { cutsceneSuffix, "BIN", "CSL" })
                {
                    var modOuPath = Path.Combine(_resourceCacheService.ModPath, "VDFS", "_WORK", "DATA", "SCRIPTS", "CONTENT", "CUTSCENE", $"OU.{suffix}");
                    if (!File.Exists(modOuPath)) continue;
                    cutscenePath = modOuPath;
                    Logger.Log($"Loading CutsceneLibrary from mod: {modOuPath}", LogCat.Loading);
                    break;
                }
            }

            _gameStateService.Dialogs.CutsceneLibrary = new(cutscenePath);
        }

        private void LoadVideos()
        {
            _videoService.InitVideos();
        }

        private void LoadFonts()
        {
            _fontService.Create();
        }

        private void LoadGuildData()
        {
            var guildMax = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.GuildMaxValue);

            _gameStateService.GuildCount = guildMax?.GetInt(0) ?? 0;

            var tablesize = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.GuildHumanAttitudesSize);
            _gameStateService.GuildHumanCount = (int)(tablesize != null ? Math.Sqrt(tablesize.GetInt(0)) : 0);

            _gameStateService.GuildAttitudes = new int[_gameStateService.GuildCount * _gameStateService.GuildCount];

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
