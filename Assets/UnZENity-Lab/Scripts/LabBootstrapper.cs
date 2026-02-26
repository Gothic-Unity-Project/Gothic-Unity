using System;
using System.Globalization;
using System.Threading.Tasks;
using GUZ.Core;
using GUZ.Core.Domain;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Config;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.Npc;
using GUZ.Core.Services.Player;
using GUZ.Core.Services.StaticCache;
using GUZ.Core.Services.Vobs;
using GUZ.Core.Services.World;
using GUZ.Lab.Handler;
using GUZ.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Lab
{
    public class LabBootstrapper : MonoBehaviour
    {
        public DeveloperConfig DeveloperConfig;
        
        [SerializeField] private bool _fillBackpack;
        [SerializeField] private bool _spawnItems;

        public LabMusicHandler LabMusicHandler;
        public LabSoundHandler LabSoundHandler;
        public LabVideoHandler LabVideoHandler;
        public LabNpcDialogHandler NpcDialogHandler;
        public LabInteractableHandler InteractableHandler;
        public LabFightHandler FightHandler;
        public LabLadderLabHandler LadderLabHandler;
        public LabVobItemHandler VobItemHandler;
        public LabNpcAnimationHandler LabNpcAnimationHandler;
        public LabLockHandler LabLockHandler;


        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly SkyService _skyService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly ParticleService _particleService;

        private BootstrapDomain _bootstrapDomain;

        private void Awake()
        {
            _bootstrapDomain = new BootstrapDomain().Inject();
            _configService.SetDeveloperConfig(DeveloperConfig);

            // In lab, we can safely say: VR only!
            GlobalEventDispatcher.RegisterControlsService.Invoke(DeveloperConfig.GameControls);
            GlobalEventDispatcher.RegisterGameVersionService.Invoke(DeveloperConfig.GameVersion);
            _contextInteractionService.SetupPlayerController(DeveloperConfig);

            // We need to set culture to this, otherwise e.g. polish numbers aren't parsed correct.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            _unityMonoService.SetMonoBehaviour(this);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            BootLab().AwaitAndLog();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        /// <summary>
        /// It's easiest to wait for Start() to initialize all the MonoBehaviours first.
        /// </summary>
        private async Task BootLab()
        {
            // We need to wait for e.g., 0.05 seconds to ensure, that Unity properly set up all MonoBehavior components and their properties.
            await Task.Delay(50);

            InitManager();
            await InitLab().AwaitAndLog();
        }

        private void InitManager()
        {
            _configService.LoadRootJson();
            _configService.LoadGothicInis(DeveloperConfig.GameVersion);

            _skyService.InitWorld(); // Add lighting to world. (otherwise VOBs/NPCs are black).

            ZenKit.Logger.Set(_configService.Dev.ZenKitLogLevel, Logger.OnZenKitLogMessage);
            DirectMusic.Logger.Set(_configService.Dev.DirectMusicLogLevel, Logger.OnDirectMusicLogMessage);

            _resourceCacheService.Init(DeveloperConfig.GameVersion == GameVersion.Gothic1
                ? _configService.Root.Gothic1Path : _configService.Root.Gothic2Path);

            _audioService.InitMusic();
            _staticCacheService.Init();
            _textureService.Init();
            _npcService.Init();
            _vobService.Init();
            _fightService.Init();
            _vrWeaponService.Init();
            _particleService.Init();

            _videoService.InitVideos();
            _saveGameService.LoadNewGame();
            _saveGameService.ChangeWorld("world.zen"); // Needed for some VOB settings for a "fake" save game when interacting with VOBs and e.g., Backpack.
        }

        private async Task InitLab()
        {
            if (!_staticCacheService.DoGlobalCacheFilesExist())
            {
                Logger.LogErrorEditor("Please load game once to create global cache first!", LogCat.Debug);
                throw new SystemException("Please load game once to create global cache first!");
            }
            await _staticCacheService.LoadGlobalCache().AwaitAndLog();
            await _meshService.CreateTextureArray().AwaitAndLog();

            _bootstrapDomain.Boot();

            _npcService.CacheHero();
            BootstrapPlayer();

            LabNpcAnimationHandler.Bootstrap();
            LabMusicHandler.Bootstrap();
            LabSoundHandler.Bootstrap();
            LabVideoHandler.Bootstrap();
            NpcDialogHandler.Bootstrap();

            InteractableHandler.SpawnItems = _spawnItems;
            InteractableHandler.Bootstrap();
            FightHandler.Bootstrap();
            LadderLabHandler.Bootstrap();
            VobItemHandler.Bootstrap();
            LabLockHandler.Bootstrap();

            _contextInteractionService.InitUIInteraction(); // For (e.g.) QuestLog to enable hand pointer.
        }

        private void BootstrapPlayer()
        {
            // Items - from PC_Rockefeller ;-)
            if (_fillBackpack)
            {
                //----------------------------------------
                //Armor.d
                //----------------------------------------
                _playerService.AddItem("ORE_ARMOR_H");
	            _playerService.AddItem("ORE_ARMOR_M");
	            _playerService.AddItem("CRW_ARMOR_H");
	            _playerService.AddItem("DMB_ARMOR_M");
	            _playerService.AddItem("EBR_ARMOR_M");
	            _playerService.AddItem("EBR_ARMOR_H");
	            _playerService.AddItem("EBR_ARMOR_H2");
	            _playerService.AddItem("GRD_ARMOR_L");
	            _playerService.AddItem("GRD_ARMOR_M");
	            _playerService.AddItem("GRD_ARMOR_H");
	            _playerService.AddItem("GUR_ARMOR_H");
	            _playerService.AddItem("GUR_ARMOR_M");
	            _playerService.AddItem("KDF_ARMOR_L");
	            _playerService.AddItem("KDF_ARMOR_H");
	            _playerService.AddItem("KDW_ARMOR_L");
	            _playerService.AddItem("KDW_ARMOR_H");
	            _playerService.AddItem("NOV_ARMOR_L");
	            _playerService.AddItem("NOV_ARMOR_M");
	            _playerService.AddItem("NOV_ARMOR_H");
	            _playerService.AddItem("ORG_ARMOR_L");
	            _playerService.AddItem("ORG_ARMOR_M");
	            _playerService.AddItem("ORG_ARMOR_H");
	            _playerService.AddItem("SFB_ARMOR_L");
	            _playerService.AddItem("SLD_ARMOR_L");
	            _playerService.AddItem("SLD_ARMOR_M");
	            _playerService.AddItem("SLD_ARMOR_H");
	            _playerService.AddItem("STT_ARMOR_M");
	            _playerService.AddItem("STT_ARMOR_H");
	            _playerService.AddItem("TPL_ARMOR_L");
	            _playerService.AddItem("TPL_ARMOR_M");
	            _playerService.AddItem("TPL_ARMOR_H");
	            _playerService.AddItem("VLK_ARMOR_M");
	            _playerService.AddItem("VLK_ARMOR_L");

                //Magic Runes
	            _playerService.AddItem("ItArRuneLight");
	            _playerService.AddItem("ItArRuneFirebolt");
	            _playerService.AddItem("ItArRuneFireball");
	            _playerService.AddItem("ItArRuneFirestorm");
	            _playerService.AddItem("ItArRuneFireRain");
	            _playerService.AddItem("ItArRuneTeleport1");
	            _playerService.AddItem("ItArRuneTeleport2");
	            _playerService.AddItem("ItArRuneTeleport3");
	            _playerService.AddItem("ItArScrollTeleport4", 20);
	            _playerService.AddItem("ItArRuneTeleport5");
	            _playerService.AddItem("ItArRuneHeal");
	            _playerService.AddItem("ItArRuneChainLightning");
	            _playerService.AddItem("ItArRuneThunderbolt");
	            _playerService.AddItem("ItArRuneThunderball");
	            _playerService.AddItem("ItArRuneIceCube");
	            _playerService.AddItem("ItArRuneIceWave");
                _playerService.AddItem("ItArRuneDestroyUndead");

                //Magic Scrolls
                _playerService.AddItem("ItArScrollTrfBloodfly", 10);
	            _playerService.AddItem("ItArScrollTrfCrawler", 10);
	            _playerService.AddItem("ItArScrollTrfLurker", 10);
	            _playerService.AddItem("ItArScrollTrfMeatbug", 10);
	            _playerService.AddItem("ItArScrollTrfMolerat", 10);
	            _playerService.AddItem("ItArScrollTrfOrcdog", 10);
	            _playerService.AddItem("ItArScrollTrfScavenger", 10);
	            _playerService.AddItem("ItArScrollTrfShadowbeast", 10);
	            _playerService.AddItem("ItArScrollTrfSnapper", 10);
	            _playerService.AddItem("ItArScrollTrfWaran", 10);
	            _playerService.AddItem("ItArScrollTrfWolf", 10);
	            _playerService.AddItem("ItArScrollSummonDemon", 10);
	            _playerService.AddItem("ItArScrollSummonSkeletons", 10);
	            _playerService.AddItem("ItArScrollSummonGolem", 10);
	            _playerService.AddItem("ItArScrollArmyOfDarkness", 10);

                //Psi-Runes
	            _playerService.AddItem("ItArRuneWindfist");
	            _playerService.AddItem("ItArRuneStormfist");
	            _playerService.AddItem("ItArRuneTelekinesis");
	            _playerService.AddItem("ItArRuneCharm");
	            _playerService.AddItem("ItArRuneSleep");
	            _playerService.AddItem("ItArRunePyrokinesis");
	            _playerService.AddItem("ItArRuneControl");
	            _playerService.AddItem("ItArRuneBreathOfDeath");

                //Psi-Scrolls
	            _playerService.AddItem("ItArScrollFear", 10);
	            _playerService.AddItem("ItArScrollBerzerk", 10);
	            _playerService.AddItem("ItArScrollShrink", 10);

                //----------------------------------------
                //Food.d
                //----------------------------------------
	            _playerService.AddItem("ItFo_mutton_01", 20);
	            _playerService.AddItem("ItFoApple", 5);
	            _playerService.AddItem("ItFoLoaf", 5);
	            _playerService.AddItem("ItFoMutton", 10);
	            _playerService.AddItem("ItFoMuttonRaw", 20);
	            _playerService.AddItem("ItFoCheese", 5);
	            _playerService.AddItem("ItFoRice", 5);
	            _playerService.AddItem("ItFoSoup", 5);
	            _playerService.AddItem("ItFoMeatbugragout", 5);
	            _playerService.AddItem("ItFoCrawlersoup", 5);
	            _playerService.AddItem("ItFoBooze", 10);
	            _playerService.AddItem("ItFoWine", 5);
	            _playerService.AddItem("ItFo_wineberrys_01", 5);
	            _playerService.AddItem("ItFoBeer", 5);

                //PFLANZEN
	            _playerService.AddItem("ItFo_Plants_Trollberrys_01", 5);
	            _playerService.AddItem("ItFo_Plants_Flameberry_01", 5);
	            _playerService.AddItem("ItFo_Plants_Nightshadow_01", 5);
	            _playerService.AddItem("ItFo_Plants_Nightshadow_02", 5);
	            _playerService.AddItem("ItFo_Plants_OrcHerb_01", 5);
	            _playerService.AddItem("ItFo_Plants_OrcHerb_02", 5);
	            _playerService.AddItem("ItFo_Plants_mushroom_01", 5);
	            _playerService.AddItem("ItFo_Plants_mushroom_02", 5);
	            _playerService.AddItem("ItFo_Plants_Stoneroot_01", 5);
	            _playerService.AddItem("ItFo_Plants_Stoneroot_02", 5);
	            _playerService.AddItem("ItFo_Plants_RavenHerb_01", 5);
	            _playerService.AddItem("ItFo_Plants_RavenHerb_02", 5);
	            _playerService.AddItem("ItFo_Plants_mountainmoos_01", 5);
	            _playerService.AddItem("ItFo_Plants_mountainmoos_02", 5);
	            _playerService.AddItem("ItFo_Plants_Berrys_01", 5);
	            _playerService.AddItem("ItFo_Plants_Bloodwood_01", 5);
	            _playerService.AddItem("ItFo_Plants_Towerwood_01", 5);
	            _playerService.AddItem("ItFo_Plants_Seraphis_01", 5);
	            _playerService.AddItem("ItFo_Plants_Velayis_01", 5);
	            _playerService.AddItem("ItFo_Plants_Herb_03", 5);
	            _playerService.AddItem("ItFo_Plants_Herb_02", 5);
	            _playerService.AddItem("ItFo_Plants_Herb_01", 5);
	            _playerService.AddItem("ItMi_Plants_Swampherb_01", 5);


                //----------------------------------------
                //MISC //Schlüssel
                //----------------------------------------
	            _playerService.AddItem("ItKeKey1");
	            _playerService.AddItem("ItKeKey2");
	            _playerService.AddItem("ItKeKey3");
	            _playerService.AddItem("ItKeKey4");
	            _playerService.AddItem("ItKeLockpick", 100);
                //----------------------------------------
                //MISC /Light_sources
                //----------------------------------------
                _playerService.AddItem("ItLsTorch", 50);

                //----------------------------------------
                //misc.d
                //----------------------------------------
	            _playerService.AddItem("ItMiHammer", 5);
	            _playerService.AddItem("ItMiScoop", 5);
                _playerService.AddItem("ItMiNugget", 1000);
	            _playerService.AddItem("ItMiAlarmhorn");
	            _playerService.AddItem("ItMiSwordraw", 5);
	            _playerService.AddItem("ItMiLute");
	            _playerService.AddItem("ItMiStomper", 5);
	            _playerService.AddItem("ItMiFlask", 5);
                //---------------------------------------------------
                //MISC STUFF
                //-------------------------------------------------
	            _playerService.AddItem("ItMi_Stuff_Pipe_01");
	            _playerService.AddItem("ItMi_Stuff_Barbknife_01");
	            _playerService.AddItem("ItMi_Stuff_OldCoin_01");
	            _playerService.AddItem("ItMi_Stuff_Plate_01");
	            _playerService.AddItem("ItMi_Stuff_Candel_01");
	            _playerService.AddItem("ItMi_Stuff_Cup_01");
	            _playerService.AddItem("ItMi_Stuff_Cup_02");
	            _playerService.AddItem("ItMi_Stuff_Silverware_01");
	            _playerService.AddItem("ItMi_Stuff_Pan_01");
	            _playerService.AddItem("ItMi_Stuff_Mug_01");
	            _playerService.AddItem("ItMi_Stuff_Amphore_01");
	            _playerService.AddItem("ItMi_Stuff_Idol_Sleeper_01");
	            _playerService.AddItem("ItMi_Stuff_Idol_Ogront_01");
	            _playerService.AddItem("ItMiJoint_1", 5);
	            _playerService.AddItem("ItMiJoint_2", 5);
	            _playerService.AddItem("ItMiJoint_3", 5);
                //----------------------------------------
                //Ranged_weapons.d
                //----------------------------------------
	            //Kurzbogen
	            _playerService.AddItem("ItRw_Bow_Small_01");
	            _playerService.AddItem("ItRw_Bow_Small_02");
	            _playerService.AddItem("ItRw_Bow_Small_03");
	            _playerService.AddItem("ItRw_Bow_Small_04");
	            _playerService.AddItem("ItRw_Bow_Small_05");
	            //Langbogen
	            _playerService.AddItem("ItRw_Bow_Long_01");
	            _playerService.AddItem("ItRw_Bow_Long_02");
	            _playerService.AddItem("ItRw_Bow_Long_03");
	            _playerService.AddItem("ItRw_Bow_Long_04");
	            _playerService.AddItem("ItRw_Bow_Long_05");
	            _playerService.AddItem("ItRw_Bow_Long_06");
	            _playerService.AddItem("ItRw_Bow_Long_07");
	            _playerService.AddItem("ItRw_Bow_Long_08");
	            _playerService.AddItem("ItRw_Bow_Long_09");
	            //Kriegsbogen
	            _playerService.AddItem("ItRw_Bow_War_01");
	            _playerService.AddItem("ItRw_Bow_War_02");
	            _playerService.AddItem("ItRw_Bow_War_03");
	            _playerService.AddItem("ItRw_Bow_War_04");
	            _playerService.AddItem("ItRw_Bow_War_05");
	            //Armbrust
	            _playerService.AddItem("ItRw_Crossbow_01");
	            _playerService.AddItem("ItRw_Crossbow_02");
	            _playerService.AddItem("ItRw_Crossbow_03");
	            _playerService.AddItem("ItRw_Crossbow_04");

                //----------------------------------------
                //Ammunition.d
                //----------------------------------------
	            _playerService.AddItem("ItAmArrow", 50);
	            _playerService.AddItem("ItAmBolt", 50);

                //----------------------------------------
                //Written.d
                //----------------------------------------
	            _playerService.AddItem("ItWrWorldmap");
	            _playerService.AddItem("ItWrWorldmap_Orc");
	            _playerService.AddItem("ItWrOMmap");
	            _playerService.AddItem("ItWrFocusmapPsi");
	            _playerService.AddItem("ItWrFocimap");
	            _playerService.AddItem("ItWrOCmap");
	            _playerService.AddItem("ItWrNCmap");
	            _playerService.AddItem("ItWrPSImap");

	            _playerService.AddItem("Goettergabe");
	            _playerService.AddItem("Geheimnisse_der_Zauberei");
	            _playerService.AddItem("Machtvolle_Kunst");
	            _playerService.AddItem("Elementare_Arcanei");
	            _playerService.AddItem("Wahre_Macht");
	            _playerService.AddItem("Das_magische_Erz");
	            _playerService.AddItem("Schlacht_um_Varant1");
	            _playerService.AddItem("Schlacht_um_Varant2");
	            _playerService.AddItem("Myrtanas_Lyrik");
	            _playerService.AddItem("Lehren_der_Goetter1");
	            _playerService.AddItem("Lehren_der_Goetter2");
	            _playerService.AddItem("Lehren_der_Goetter3");
	            _playerService.AddItem("Jagd_und_Beute");
	            _playerService.AddItem("Kampfkunst");
	            _playerService.AddItem("Astronomie");
	            _playerService.AddItem("Rezepturen");
	            _playerService.AddItem("Rezepturen2");

	            _playerService.AddItem("ItWr_Book_Circle_01");
	            _playerService.AddItem("ItWr_Book_Circle_02");
	            _playerService.AddItem("ItWr_Book_Circle_03");
	            _playerService.AddItem("ItWr_Book_Circle_04");
	            _playerService.AddItem("ItWr_Book_Circle_05");
	            _playerService.AddItem("ItWr_Book_Circle_06");

                //----------------------------------------
                //Melee_weapons.d
                //----------------------------------------
	            _playerService.AddItem("ItMw_1H_Club_01");
	            _playerService.AddItem("ItMw_1H_Poker_01");
	            _playerService.AddItem("ItMw_1H_Sickle_01");
	            _playerService.AddItem("ItMw_1H_Mace_Light_01");
	            _playerService.AddItem("ItMw_1H_Sledgehammer_01");
	            _playerService.AddItem("ItMw_1H_Warhammer_01");
	            _playerService.AddItem("ItMw_1H_Warhammer_02");
	            _playerService.AddItem("ItMw_1H_Warhammer_03");
	            _playerService.AddItem("ItMw_1H_Hatchet_01");
	            _playerService.AddItem("ItMw_1H_Sword_Old_01");
	            _playerService.AddItem("ItMw_1H_Nailmace_01");
	            _playerService.AddItem("ItMw_1H_Sword_Short_01");
	            _playerService.AddItem("ItMw_1H_Sword_Short_02");
	            _playerService.AddItem("ItMw_1H_Sword_Short_03");
	            _playerService.AddItem("ItMw_1H_Sword_Short_04");
	            _playerService.AddItem("ItMw_1H_Sword_Short_05");
	            _playerService.AddItem("ItMw_1H_Axe_Old_01");
	            _playerService.AddItem("ItMw_1H_Scythe_01");
	            _playerService.AddItem("ItMw_2H_Staff_01");
	            _playerService.AddItem("ItMw_2H_Staff_02");
	            _playerService.AddItem("ItMw_2H_Staff_03");
	            _playerService.AddItem("ItMw_1H_Mace_01");
	            _playerService.AddItem("ItMw_1H_Mace_02");
	            _playerService.AddItem("ItMw_1H_Mace_03");
	            _playerService.AddItem("ItMw_1H_Mace_04");
	            _playerService.AddItem("ItMw_1H_Sword_01");
	            _playerService.AddItem("ItMw_1H_Sword_02");
	            _playerService.AddItem("ItMw_1H_Sword_03");
	            _playerService.AddItem("ItMw_1H_Sword_04");
	            _playerService.AddItem("ItMw_1H_Sword_05");
	            _playerService.AddItem("ItMw_1H_Sword_01");
	            _playerService.AddItem("ItMw_1H_Mace_War_01");
	            _playerService.AddItem("ItMw_1H_Mace_War_02");
	            _playerService.AddItem("ItMw_1H_Mace_War_03");
	            _playerService.AddItem("ItMw_1H_Mace_War_04");
	            _playerService.AddItem("ItMw_1H_Sword_Long_01");
	            _playerService.AddItem("ItMw_1H_Sword_Long_02");
	            _playerService.AddItem("ItMw_1H_Sword_Long_03");
	            _playerService.AddItem("ItMw_1H_Sword_Long_04");
	            _playerService.AddItem("ItMw_1H_Sword_Long_05");
	            _playerService.AddItem("ItMw_1H_Axe_01");
	            _playerService.AddItem("ItMw_1H_Axe_02");
	            _playerService.AddItem("ItMw_1H_Axe_03");
	            _playerService.AddItem("ItMw_1H_Sword_Broad_01");
	            _playerService.AddItem("ItMw_1H_Sword_Broad_02");
	            _playerService.AddItem("ItMw_1H_Sword_Broad_03");
	            _playerService.AddItem("ItMw_1H_Sword_Broad_04");
	            _playerService.AddItem("ItMw_2H_Sword_Old_01");
	            _playerService.AddItem("ItMw_1H_Sword_Bastard_01");
	            _playerService.AddItem("ItMw_1H_Sword_Bastard_02");
	            _playerService.AddItem("ItMw_1H_Sword_Bastard_03");
	            _playerService.AddItem("ItMw_1H_Sword_Bastard_04");
	            _playerService.AddItem("ItMw_2H_Axe_Old_01");
	            _playerService.AddItem("ItMw_2H_Axe_Old_02");
	            _playerService.AddItem("ItMw_2H_Axe_Old_03");
	            _playerService.AddItem("ItMw_2H_Sword_Light_01");
	            _playerService.AddItem("ItMw_2H_Sword_Light_02");
	            _playerService.AddItem("ItMw_2H_Sword_Light_03");
	            _playerService.AddItem("ItMw_2H_Sword_Light_04");
	            _playerService.AddItem("ItMw_2H_Sword_Light_05");
	            _playerService.AddItem("ItMw_2H_Axe_light_01");
	            _playerService.AddItem("ItMw_2H_Axe_light_02");
	            _playerService.AddItem("ItMw_2H_Axe_light_03");
	            _playerService.AddItem("ItMw_2H_Sword_01");
	            _playerService.AddItem("ItMw_2H_Sword_02");
	            _playerService.AddItem("ItMw_2H_Sword_03");
	            _playerService.AddItem("ItMw_2H_Sword_Heavy_01");
	            _playerService.AddItem("ItMw_2H_Sword_Heavy_02");
	            _playerService.AddItem("ItMw_2H_Sword_Heavy_03");
	            _playerService.AddItem("ItMw_2H_Sword_Heavy_04");
	            _playerService.AddItem("ItMw_2H_Axe_Heavy_01");
	            _playerService.AddItem("ItMw_2H_Axe_Heavy_02");
	            _playerService.AddItem("ItMw_2H_Axe_Heavy_03");
	            _playerService.AddItem("ItMw_2H_Axe_Heavy_04");
	            _playerService.AddItem("ItMw2hOrcSword01");
	            _playerService.AddItem("ItMw2hOrcAxe01");
	            _playerService.AddItem("ItMw2hOrcAxe02");
	            _playerService.AddItem("ItMw2hOrcAxe03");
	            _playerService.AddItem("ItMw2hOrcAxe04");
	            _playerService.AddItem("ItMw2hOrcMace01");

                //-----------------------------------------------------------
                //Amulette
                //-----------------------------------------------------------
	            _playerService.AddItem("ItMi_Amulet_Psi_01");
	            _playerService.AddItem("Schutzamulett_Waffen");
	            _playerService.AddItem("Schutzamulett_Feuer");
	            _playerService.AddItem("Schutzamulett_Geschosse");
	            _playerService.AddItem("Schutzamulett_Magie");
	            _playerService.AddItem("Schutzamulett_Magie_Feuer");
	            _playerService.AddItem("Schutzamulett_Waffen_Geschosse");
	            _playerService.AddItem("Schutzamulett_Total");
	            _playerService.AddItem("Gewandtheitsamulett");
	            _playerService.AddItem("Gewandtheitsamulett2");
	            _playerService.AddItem("Staerkeamulett");
	            _playerService.AddItem("Staerkeamulett2");
	            _playerService.AddItem("Lebensamulett");
	            _playerService.AddItem("Amulett_der_Magie");
	            _playerService.AddItem("Amulett_der_Macht");
	            _playerService.AddItem("Amulett_der_Erleuchtung");

                //------------------------------------------------------------
                //Ringe
                //------------------------------------------------------------
	            _playerService.AddItem("Schutzring_Feuer1");
	            _playerService.AddItem("Schutzring_Feuer2");
	            _playerService.AddItem("Schutzring_Geschosse1");
	            _playerService.AddItem("Schutzring_Geschosse2");
	            _playerService.AddItem("Schutzring_Waffen1");
	            _playerService.AddItem("Schutzring_Waffen2");
	            _playerService.AddItem("Schutzring_Magie1");
	            _playerService.AddItem("Schutzring_Magie2");
	            _playerService.AddItem("Schutzring_Magie1_Fire1");
	            _playerService.AddItem("Schutzring_Magie2_Fire2");
	            _playerService.AddItem("Schutzring_Geschosse1_Waffen1");
	            _playerService.AddItem("Schutzring_Geschosse1_Waffen1");
	            _playerService.AddItem("Schutzring_Geschosse2_Waffen2");
	            _playerService.AddItem("Schutzring_Total1");
	            _playerService.AddItem("Schutzring_Total2");
	            _playerService.AddItem("Ring_des_Geschicks");
	            _playerService.AddItem("Ring_des_Geschicks2");
	            _playerService.AddItem("Ring_des_Lebens");
	            _playerService.AddItem("Ring_des_Lebens2");
	            _playerService.AddItem("Staerkering");
	            _playerService.AddItem("Staerkering2");
	            _playerService.AddItem("Ring_der_Magie");
	            _playerService.AddItem("Ring_der_Erleuchtung");
	            _playerService.AddItem("Machtring");

                //---------------------------------------------------------------------
                //Potions
                //---------------------------------------------------------------------
	            _playerService.AddItem("ItFo_Potion_Mana_01");
	            _playerService.AddItem("ItFo_Potion_Mana_02");
	            _playerService.AddItem("ItFo_Potion_Mana_03");
	            _playerService.AddItem("ItFo_Potion_Health_01");
	            _playerService.AddItem("ItFo_Potion_Health_02");
	            _playerService.AddItem("ItFo_Potion_Health_03");
	            _playerService.AddItem("ItFo_Potion_Elixier");
	            _playerService.AddItem("ItFo_Potion_Elixier_Egg");
	            _playerService.AddItem("ItFo_Potion_Strength_01");
	            _playerService.AddItem("ItFo_Potion_Strength_02");
	            _playerService.AddItem("ItFo_Potion_Strength_03");
	            _playerService.AddItem("ItFo_Potion_Dex_01");
	            _playerService.AddItem("ItFo_Potion_Dex_02");
	            _playerService.AddItem("ItFo_Potion_Dex_03");
	            _playerService.AddItem("ItFo_Potion_Health_Perma_01");
	            _playerService.AddItem("ItFo_Potion_Health_Perma_02");
	            _playerService.AddItem("ItFo_Potion_Health_Perma_03");
	            _playerService.AddItem("ItFo_Potion_Mana_Perma_01");
	            _playerService.AddItem("ItFo_Potion_Mana_Perma_02");
	            _playerService.AddItem("ItFo_Potion_Mana_Perma_03");
	            _playerService.AddItem("ItFo_Potion_Master_01");
	            _playerService.AddItem("ItFo_Potion_Master_02");
	            _playerService.AddItem("ItFo_Potion_Water_01");
	            _playerService.AddItem("ItFo_Potion_Haste_01");

                //---------------------------------------------------------------------
                // Animaltropy
                //---------------------------------------------------------------------
	            _playerService.AddItem("ItAt_Teeth_01", 5);
	            _playerService.AddItem("ItAt_Wolf_01", 5);
	            _playerService.AddItem("ItAt_Wolf_02", 5);
	            _playerService.AddItem("ItAt_Waran_01", 5);
	            _playerService.AddItem("ItAt_Claws_01", 5);
	            _playerService.AddItem("ItAt_Crawler_02", 5);
	            _playerService.AddItem("ItAt_Crawler_01", 5);
	            _playerService.AddItem("ItAt_Shadow_01", 5);
	            _playerService.AddItem("ItAt_Shadow_02", 5);
	            _playerService.AddItem("ItAt_Lurker_01", 5);
	            _playerService.AddItem("ItAt_Lurker_02", 5);
	            _playerService.AddItem("ItAt_Troll_02", 5);
	            _playerService.AddItem("ItAt_Troll_01", 5);
	            _playerService.AddItem("ItAt_Swampshark_02", 5);
	            _playerService.AddItem("ItAt_Swampshark_01", 5);
	            _playerService.AddItem("ItAt_Bloodfly_02", 5);
                _playerService.AddItem("ItAt_Bloodfly_01", 5);
	            _playerService.AddItem("ItAt_Meatbug_01", 5);
            }

            // Add Missions and Notes
            {
                string topic;
                topic = "0 - The Lost Artifact";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "I venture into the ruins of an ancient temple to recover a powerful artifact believed to be hidden within.");
                    _storyService.ExtLogAddEntry(topic, "I must beware of the traps and guardians that protect it.");
                    _storyService.ExtLogAddEntry(topic, "As I explore, I uncover ancient writings that hint at the artifact's true power.");
                    _storyService.ExtLogAddEntry(topic, "I encounter rival treasure hunters who will stop at nothing to claim the artifact for themselves.");
                    _storyService.ExtLogAddEntry(topic, "I must solve intricate puzzles that guard the inner sanctum of the temple.");
                    _storyService.ExtLogAddEntry(topic, "The deeper I go, the more I feel the weight of history pressing down on me.");
                    _storyService.ExtLogAddEntry(topic, "Each encounter reveals different perspectives on what heroism truly means.");
                    _storyService.ExtLogAddEntry(topic, "Ultimately, my search becomes not just about finding one person but rediscovering what it means to be brave.");
                    _storyService.ExtLogAddEntry(topic, "In confronting challenges along the way, I may find my own path as a hero in this world.");
                    _storyService.ExtLogAddEntry(topic, "I aim to uncover the truth behind these ghostly echoes and their connection to the camp's dark history.");
                    _storyService.ExtLogAddEntry(topic, "I speak with locals who share chilling tales of past events that haunt the area.");
                    _storyService.ExtLogAddEntry(topic, "As night falls, I set up camp to witness the phenomena firsthand.");
                    _storyService.ExtLogAddEntry(topic, "I discover a hidden chamber that holds secrets long forgotten by time.");
                    _storyService.ExtLogAddEntry(topic, "The whispers grow louder, leading me to confront a lingering spirit seeking closure.");
                    _storyService.ExtLogAddEntry(topic, "I face moral dilemmas when encountering creatures that guard these rare items.");
                    _storyService.ExtLogAddEntry(topic, "My choices could either empower him or lead to disastrous consequences for the camp.");
                    _storyService.ExtLogAddEntry(topic, "Ultimately, I must weigh my own desires against the potential fallout of his spell.");
                }

                topic = "1 - Echoes of the Past";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "I investigate strange occurrences in the Old Camp, where villagers report hearing whispers at night.");
                }

                topic = "2 - The Price of Power";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "A local mage seeks my help in gathering rare ingredients for a spell that promises great power.");
                    _storyService.ExtLogAddEntry(topic, "I must decide whether to assist him or expose his dangerous ambitions to the camp leaders.");
                    _storyService.ExtLogAddEntry(topic, "As I gather ingredients, I learn about the mage's troubled past and his obsession with power.");
                }

                topic = "3 - Beneath the Surface";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "I explore the depths of a long-abandoned mine rumored to be haunted.");
                    _storyService.ExtLogAddEntry(topic, "I need to discover what lies beneath and confront the malevolent force that has kept it hidden for so long.");
                    _storyService.ExtLogAddEntry(topic, "As I descend, I encounter remnants of miners who vanished without a trace.");
                    _storyService.ExtLogAddEntry(topic, "Strange noises echo through the tunnels, heightening my sense of dread.");
                    _storyService.ExtLogAddEntry(topic, "I find clues suggesting that dark rituals were once performed here.");
                    _storyService.ExtLogAddEntry(topic, "Confronting whatever lurks in the shadows will test my courage and resolve.");
                }

                topic = "4 - A Thief's Redemption";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "I help a reformed thief prove his innocence after being accused of stealing from the camp's treasury.");
                    _storyService.ExtLogAddEntry(topic, "I gather evidence and confront those who seek to frame him.");
                    _storyService.ExtLogAddEntry(topic, "My investigation leads me through back alleys and hidden corners of the camp.");
                    _storyService.ExtLogAddEntry(topic, "Along the way, I meet other characters who have their own motives regarding the theft.");
                    _storyService.ExtLogAddEntry(topic, "Each piece of evidence brings me closer to uncovering a larger conspiracy at play.");
                    _storyService.ExtLogAddEntry(topic, "In a final confrontation, I must decide whether justice or mercy prevails.");
                }

                topic = "5 - The Beast Within";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "A series of brutal attacks on livestock has left the villagers terrified.");
                    _storyService.ExtLogAddEntry(topic, "I hunt down the creature responsible and uncover whether it's merely a beast or something more sinister.");
                    _storyService.ExtLogAddEntry(topic, "Villagers share their encounters with fear in their eyes, painting a vivid picture of terror.");
                    _storyService.ExtLogAddEntry(topic, "As I track the creature, I discover signs that suggest it may be more than just an animal attack.");
                    _storyService.ExtLogAddEntry(topic, "Clues lead me into dark woods where shadows seem to move on their own.");
                    _storyService.ExtLogAddEntry(topic, "When I finally confront the beast, its true nature reveals a tragic story that challenges my perception.");
                }

                topic = "6 - Allies in Shadows";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, "I join forces with a secretive faction operating in the shadows.");
                    _storyService.ExtLogAddEntry(topic, "I complete tasks that test my loyalty and skills, ultimately deciding whether to support their cause or betray them.");
                    _storyService.ExtLogAddEntry(topic, "The faction’s goals are shrouded in mystery, making me question their true intentions.");
                    _storyService.ExtLogAddEntry(topic, "As missions unfold, I uncover secrets about powerful figures within the camp that they aim to undermine.");
                    _storyService.ExtLogAddEntry(topic, "My actions can either strengthen or weaken their influence in our world.");
                    _storyService.ExtLogAddEntry(topic, "In a climactic moment, I'm faced with a choice that could change everything for me and those around me.");
                }

                for (int i = 7; i < 40; i++)
                {
                    topic = $"{i} - Mission";
                    _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Running);
                    _storyService.ExtLogAddEntry(topic, $"This is a placeholder for mission {i}.");
                }

                topic = "1 - Trial by Fire";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Failure);
                    _storyService.ExtLogAddEntry(topic, "I participate in a series of trials set by the camp's leaders to prove my worth as a warrior.");
                    _storyService.ExtLogAddEntry(topic, "I must overcome challenges that will test my combat skills and strategic thinking.");
                    _storyService.ExtLogAddEntry(topic, "Each trial is designed not only for strength but also for wisdom and resilience under pressure.");
                    _storyService.ExtLogAddEntry(topic, "Fellow challengers become both allies and rivals as we compete for honor and recognition.");
                    _storyService.ExtLogAddEntry(topic, "The outcome of these trials will determine my standing within the camp’s hierarchy.");
                    _storyService.ExtLogAddEntry(topic, "Ultimately, my performance could lead to unexpected alliances or bitter enmities.");
                }

                topic = "2 - The Forgotten Heirloom";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Failure);
                    _storyService.ExtLogAddEntry(topic, "I assist an elderly woman in recovering a family heirloom lost during her youth.");
                    _storyService.ExtLogAddEntry(topic, "The journey takes me through treacherous terrain and reveals long-buried family secrets.");
                    _storyService.ExtLogAddEntry(topic, "Clues lead us to forgotten places tied to her family's history, each revealing more about her past.");
                    _storyService.ExtLogAddEntry(topic, "Along our journey, we encounter obstacles that test our resolve and resourcefulness together.");
                    _storyService.ExtLogAddEntry(topic, "The heirloom itself holds significance beyond its material value; it symbolizes lost connections and memories.");
                    _storyService.ExtLogAddEntry(topic, "In recovering it, we not only restore her legacy but also heal old wounds.");
                }

                topic = "1 - The Call of Adventure";
                _storyService.ExtLogCreateTopic(topic, SaveTopicSection.Missions);
                {
                    _storyService.ExtLogSetTopicStatus(topic, SaveTopicStatus.Obsolete);
                    _storyService.ExtLogAddEntry(topic, "I embark on a quest to find a legendary hero who vanished years ago.");
                    _storyService.ExtLogAddEntry(topic, "Following clues across various camps leads me into uncharted territories filled with danger.");
                    _storyService.ExtLogAddEntry(topic, "Along my journey, stories about this hero inspire hope among those who have lost faith.");
                }

                _storyService.ExtLogCreateTopic("Note number 1", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 1", "Note number 1 entry 1");
                    _storyService.ExtLogAddEntry("Note number 1", "Note number 1 entry 2");
                    _storyService.ExtLogAddEntry("Note number 1", "Note number 1 entry 3");
                    _storyService.ExtLogAddEntry("Note number 1", "Note number 1 entry 4");
                    _storyService.ExtLogAddEntry("Note number 1", "Note number 1 entry 5");
                }
                _storyService.ExtLogCreateTopic("Note number 2", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 2", "Note number 2 entry 1");
                    _storyService.ExtLogAddEntry("Note number 2", "Note number 2 entry 2");
                    _storyService.ExtLogAddEntry("Note number 2", "Note number 2 entry 3");
                }
                _storyService.ExtLogCreateTopic("Note number 3", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 3", "Note number 3 entry 1");
                    _storyService.ExtLogAddEntry("Note number 3", "Note number 3 entry 2");
                }
                _storyService.ExtLogCreateTopic("Note number 4", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 4", "Note number 4 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 5", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 5", "Note number 5 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 6", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 6", "Note number 6 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 7", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 7", "Note number 7 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 8", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 8", "Note number 8 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 9", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 9", "Note number 9 entry 1");
                }
                _storyService.ExtLogCreateTopic("Note number 10", SaveTopicSection.Notes);
                {
                    _storyService.ExtLogAddEntry("Note number 10", "Note number 10 entry 1");
                }
            }
        }

        private void OnDestroy()
        {
            _gameStateService.Dispose();
            _vmCacheService.Dispose();
            _textureCacheService.Dispose();
            _multiTypeCacheService.Dispose();
            _morphMeshCacheService.Dispose();
        }
    }
}
