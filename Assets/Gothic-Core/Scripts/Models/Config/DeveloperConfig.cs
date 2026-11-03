using System.Collections.Generic;
using System;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.World;
using MyBox;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using static Gothic.Core.Models.Config.DeveloperConfigEnums;


namespace Gothic.Core.Models.Config
{
    [CreateAssetMenu(fileName = "NewDeveloperConfiguration", menuName = "Gothic/ScriptableObjects/DeveloperConfiguration", order = 1)]
    public class DeveloperConfig : ScriptableObject
    {
        /**
         * ##########
         * ConditionalFieldArrayFilter
         * ##########
         *
         * Unity doesn't support custom drawer on Arrays. MyBox came up with a solution by wrapping a list into a wrapper class.
         * @see: https://github.com/Deadcows/MyBox/wiki/Attributes#conditionalfield-with-arrays
         */

        [Serializable]
        public class IntCollection : CollectionWrapper<int> {}

        [Serializable]
        public class VOBTypesCollection : CollectionWrapper<VirtualObjectType> { }

        [Serializable]
        public class MonsterTypesCollection : CollectionWrapper<DeveloperConfigEnums.MonsterId> { }

        [Serializable]
        public class DebugChannelTypesCollection : CollectionWrapper<DeveloperConfigEnums.DebugChannel> { }


        /**
         * ##########
         * Context
         * ##########
         */

        [Foldout("Context", true)]
        [Tooltip("If set, the Gothic version named below will be auto-selected when the game starts.")]
        public bool PreselectGameVersion = true;
        [ConditionalField(fieldToCheck: nameof(PreselectGameVersion), compareValues: true)]
        public GameVersion GameVersion = GameVersion.Gothic1;

        [Separator("Mod")]
        [Tooltip("Pre-select a mod.")]
        public bool PreSelectMod;
        [ConditionalField(fieldToCheck: nameof(PreSelectMod), compareValues: true)]
        [Tooltip("INI filename inside ModPath/system/ (e.g. DM_E.ini)")]
        public string ModIni = string.Empty;
        [ConditionalField(fieldToCheck: nameof(PreSelectMod), compareValues: true)]
        [Tooltip("If Mod is outside Gothic installation, select folder used as the game root " +
                 "(contains Data/, _work/, system/), e.g. C:\\Gothic Mods\\Mroczne Tajemnice")]
        public string ModPath = string.Empty;

        [Separator("Game Profiles")]
        [Tooltip("Show a list at start to pick G1/G2/a mod (overrides PreselectGameVersion and PreSelectMod). " +
            "Editor only - builds use GameSettings.json EnableGameProfiles/GameProfiles.")]
        public bool EnableGameProfiles;
        [Tooltip("GameVersion: Gothic1 or Gothic2. Empty ModPath = vanilla with GothicGame.ini. " +
            "ModIni = INI filename inside ModPath/system/ (empty = GothicGame.ini), \"*\" = pick one of the mod's " +
            "game inis, \"A.INI;B.INI\" = pick one of these.")]
        public List<JsonGameProfile> GameProfiles = new();

        public Controls GameControls = Controls.VR;

        [Separator("Debug")]
        [ConditionalField(fieldToCheck: nameof(GameControls), compareValues: Controls.VR)]
        public bool EnableVRDeviceSimulator;

        [Tooltip("Start with Marvin mode on (shortcut). Without it, Marvin mode can still be activated in game.")]
        public bool PreselectMarvinMode;

        
        /**
         * ##########
         * Logging
         * ##########
         */

        [Foldout("Logging", true)]
        [Separator("ZSpy")]
        [Tooltip("Enable Daedalus logs inside .d scripts.")]
        [OverrideLabel("Enable ZSpy Logs")]
        public bool EnableZSpyLogs;
        [ConditionalField(fieldToCheck: nameof(EnableZSpyLogs))]
        [Tooltip("Overrules specific channel settings")]
        public bool AllDebugChannels;
        [ConditionalField(fieldToCheck: new []{nameof(EnableZSpyLogs), nameof(AllDebugChannels)}, inverse: new[]{false, true})]
        [Tooltip("PrintDebug channels from 1-25.")]
        public DebugChannelTypesCollection ZSpyChannels = new();
        [ConditionalField(fieldToCheck: nameof(EnableZSpyLogs))]
        [Tooltip("Additional logs for instant Daedalus calls like Npc_IsOnFP()")]
        [OverrideLabel("Enable ZSpy Instant Logs")]
        public bool EnableZSpyInstantLogs;
        [ConditionalField(fieldToCheck: nameof(EnableZSpyLogs))]
        [OverrideLabel("Ignore spammy ZSpy Logs like >[zspy,9]: ... -> bodystate&(...)<")]
        public bool IgnoreSpammyZSpyLogs = true;
        
        [Separator("ZenKit")]
        [OverrideLabel("ZenKit Log Level")]
        public LogLevel ZenKitLogLevel = LogLevel.Warning;
        [OverrideLabel("DirectMusic Log Level")]
        public DirectMusic.LogLevel DirectMusicLogLevel = DirectMusic.LogLevel.Warning;
        

        /**
         * ##########
         * Menu and Loading
         * ##########
         */

        [Foldout("Menu and Loading", true)]
        public bool EnableMainMenu = true;

        [ConditionalField(fieldToCheck: nameof(EnableMainMenu), compareValues: false)]
        public bool LoadFromSaveSlot;

        [ConditionalField(useMethod: true, method: nameof(SaveSlotFieldCondition))]
        [Range(1, 15)]
        public int SaveSlotToLoad;

        private bool SaveSlotFieldCondition() => !EnableMainMenu && LoadFromSaveSlot;
        
        [ConditionalField(useMethod: true, method: nameof(SaveSlotFieldCondition), inverse: true)]
        public DeveloperConfigEnums.WorldToSpawn PreselectWorldToSpawn;

        [Tooltip("Covers Free Points and Way Points.")]
        [ConditionalField(useMethod: true, method: nameof(SaveSlotFieldCondition), inverse: true)]
        public string SpawnAtWaypoint = string.Empty;

        [Separator("Debug")]
        [Tooltip("Ignore frame skipping during loading.")]
        public bool SpeedUpLoading;

        [Tooltip("Skips all videos (logo videos and Daedalus PlayVideo/PlayVideoEx, e.g. chapter intros).")]
        public bool SkipVideos;

        /**
         * ##########
         * VOBs
         * ##########
         */

        [Foldout("VOBs", true)]
        [Separator("General")]
        [Tooltip("Enable World objects.")]
        [OverrideLabel("Enable VOBs")]
        public bool EnableVOBs = true;

        [ConditionalField(fieldToCheck: nameof(EnableVOBs), compareValues: true)]
        [Tooltip("Spawn only specific VOBs by naming their types in here.")]
        public VOBTypesCollection SpawnVOBTypes = new();

        [Separator("Culling")]
        public bool EnableVOBMeshCulling = true;

        [ConditionalField(fieldToCheck: nameof(EnableVOBMeshCulling), compareValues: true)]
        public MeshCullingGroup SmallVOBMeshCullingGroup = new() { MaximumObjectSize = 0.2f, CullingDistance = 50 };

        [ConditionalField(fieldToCheck: nameof(EnableVOBMeshCulling), compareValues: true)]
        public MeshCullingGroup MediumVOBMeshCullingGroup = new() { MaximumObjectSize = 5.0f, CullingDistance = 100 };

        [ConditionalField(fieldToCheck: nameof(EnableVOBMeshCulling), compareValues: true)]
        public MeshCullingGroup LargeVOBMeshCullingGroup = new() { MaximumObjectSize = 100, CullingDistance = 200 };


        [Separator("Immersion")]
        [OverrideLabel("Brighten Up Hovered VOBs")]
        public bool BrightenUpHoveredVOBs = true;
        [OverrideLabel("Show Names On Hovered VOBs")]
        public bool ShowNamesOnHoveredVOBs = true;

        [Separator("Debug")]
        [Tooltip("Array like this: C_ITEM_NAME:AMOUNT;... e.g., ItMi_Stuff_OldCoin_01:10;ItFo_Potion_Mana_01:1")]
        public string PlayerInventoryAddition;
        [Tooltip("When activated, add >Gothic.Core.Debugging.VobCullingGizmo< to the GameObject containing >VobLoader<.")]
        public bool ShowVOBMeshCullingGizmos;
        public bool ShowCapsuleOverlapGizmos;


        /**
         * ##########
         * NPCs (+ Monsters)
         * ##########
         */

        [Foldout("NPCs (+ Monsters)", true)]
        [Separator("General")]
        [OverrideLabel("Enable NPCs & Monsters")]
        public bool EnableNpcs;

        [Tooltip("Enable player→NPC melee combat (hit detection, damage, hurt/death animations). WIP - debug damage values only.")]
        public bool EnableCombatSystem;
        
        [ConditionalField(fieldToCheck: nameof(EnableNpcs), compareValues: true)]
        public bool EnableNpcMeshCulling = true;

        [Tooltip("Based on original G1 saves, the distance for NPCs to occur inside VobTree (oCNPC) is about 50m. Please alter at your own risk.")]
        [ConditionalField(useMethod: true, method: nameof(NpcCullingDistanceFieldCondition))]
        [Range(1f, 100f)]
        public float NpcCullingDistance = 50f;
        private bool NpcCullingDistanceFieldCondition() => EnableNpcs && EnableNpcMeshCulling;

        [Separator("NPCs only")]
        [Tooltip("Spawn only specific NPCs by naming their IDs in here.")]
        [ConditionalField(fieldToCheck: nameof(EnableNpcs), compareValues: true)]
        public IntCollection SpawnNpcInstances = new();

        [Separator("Monsters only")]
        [Tooltip("Spawn only specific Monsters by naming their aivar[AIV_MM_REAL_ID] in here.")]
        [ConditionalField(fieldToCheck: nameof(EnableNpcs), compareValues: true)]
        public MonsterTypesCollection SpawnMonsterInstances = new();

        [Separator("WIP")]
        [Tooltip("Enable looting dead NPCs/monsters: grab a dead NPC to open a loot panel with their Daedalus inventory. WIP.")]
        public bool EnableNpcLooting;

        [Tooltip("On: the hero can move during dialogs (inside Constants.DialogFreeMovementRadius around the NPC), " +
                 "walking away chooses END if offered, a dialog only starts within that radius. " +
                 "Off: the hero is pinned where he stood when the dialog opened, until it ends.")]
        public bool EnableDialogFreeMovement;

        [Separator("Debug")]
        [Tooltip("Draw wireframe boxes for all NPC bone colliders. Green = active (attack window), White = inactive. Works in all builds including release.")]
        public bool ShowNpcColliders;


        /**
         * ##########
         * WayNet
         * ##########
         */

        [Foldout("WayNet", true)]
        [OverrideLabel("Show Free Point Meshes")]
        public bool ShowFreePoints;

        [OverrideLabel("Show Way Point Meshes")]
        public bool ShowWayPoints;

        [OverrideLabel("Show Way Point Edge Meshes")]
        public bool ShowWayEdges;


        /**
         * ##########
         * Audio
         * ##########
         */

        [Foldout("Audio", true)]
        public bool EnableGameSounds = true;


        /**
         * ##########
         * Lighting
         * ##########
         */

        [Foldout("Lighting", true)]
        public Color SunLightColor = new(0.69f, 0.69f, 0.69f, 1);

        [Range(0, 1)]
        public float SunLightIntensity = 1;
        public GameTimeService.GameTimeInterval SunUpdateInterval = GameTimeService.GameTimeInterval.EveryGameMinute;
        public Color AmbientLightColor = new(0.1f, 0.1f, 0.1f, 1);


        /**
         * ##########
         * Time
         * ##########
         */

        [Foldout("Time", true)]
        [Range(0, 23)]
        public int StartTimeHour = 8;

        [Range(0, 59)]
        public int StartTimeMinute;

        [Range(0.5f, 1000f)]
        [Tooltip("Speeds up the in game time.")]
        public float TimeSpeedMultiplier = 1;


        /**
         * ##########
         * VR Hero Body
         * ##########
         */

        [Foldout("VR Hero Body", true)]
        [Tooltip("The hero's own body (current armor) is visible under the VR head - torso and legs play his Gothic " +
                 "animations, the arms reach for the VR hands (simple IK). The model's hands and head are hidden.")]
        public bool EnableVrHeroBody = true;

        [Tooltip("Immersion option \"Hero's hands\" (ini heroHands, off by default): with the VR hero body, the hero's " +
                 "own fists (gloves of his armor) are the VR hands - turned like the controllers, the VR hand models " +
                 "hidden. Before a world is loaded (menus) the VR hands stay.")]
        public bool EnableHeroHands = true;

        [Tooltip("Turn of the hero fists' thumbs around their fingers, outwards (degrees, EnableHeroHands). 0 = thumb up " +
                 "like the VR hand's. Read every frame - tune it while playing.")]
        [Range(-180f, 180f)]
        public float HeroHandsRollDegrees;

        [Tooltip("Share of the way from the hand's attach point to the fist's palm slot (ZS_RIGHTHAND/ZS_LEFTHAND) held " +
                 "items are moved, at most 4 cm. 0 = the attach point itself, 1 = the palm slot (too far).")]
        [Range(0f, 1f)]
        public float HeroHandsHoldShift = 0.3f;

        [Tooltip("Knocked out / dead, the VR hero body falls (T_Stand_2_WoundedB, T_DeadB) and the camera goes down " +
                 "with its head and up again when he stands up - where you look is never taken from you.")]
        public bool EnableHeroFallCamera = true;

        [Tooltip("A knocked out hero can't walk but can still turn with the stick (snap/smooth turn) - seated players " +
                 "couldn't look around.")]
        public bool EnableKnockoutTurning = true;

        [Tooltip("How much the world's lights color the VR hands (0..1, 1 = fully lit like before). The rest is their own " +
                 "skin color - fully lit they turned purple, white or orange.")]
        [Range(0f, 1f)]
        public float HandLightInfluence = 0.3f;

        [Tooltip("Skin color of the VR hands (multiplies the hand texture) - like the hero body's arm.")]
        public Color HandSkinTint = new(0.78f, 0.58f, 0.48f, 1f);


        /**
         * ##########
         * Backpack and Items
         * ##########
         */

        [Foldout("Backpack and Items", true)]
        [Tooltip("Grab armor/amulet/ring/belt with both hands + trigger (R) -> it's stored in the backpack and equipped " +
                 "(protection + on_equip). Equipped items are listed first in the backpack with an [E] badge; " +
                 "taking the last one out of the backpack unequips it.")]
        public bool EnableEquipItems = true;

        [Tooltip("An empty hand at an armor, amulet, ring or belt in the hero's backpack and a trigger click (quick, " +
                 "without moving) puts it on or takes it off.")]
        public bool EnableBackpackEquipToggle = true;

        [Tooltip("Backpack in one hand + item in the other hand (or hovering one in the backpack) shows a popup " +
                 "with the item's name, amount and Gothic inventory description (C_Item text[]/count[]).")]
        public bool EnableItemDetailsPopup = true;

        [Tooltip("While the backpack is held in a hand, its bottom opening gently pulls nearby world items. " +
                 "An item staying inside the opening for BackpackVacuumStoreSeconds gets stored.")]
        public bool EnableBackpackVacuum = true;

        [Tooltip("Pull radius around the backpack opening in meters.")]
        [Range(0.2f, 3f)]
        public float BackpackVacuumRadius = 1.2f;

        [Tooltip("Seconds an item has to stay inside the backpack opening before it's stored.")]
        [Range(0.5f, 10f)]
        public float BackpackVacuumStoreSeconds = 3f;

        [Tooltip("While the backpack is on the shoulder, its hand grab volumes are this many times larger - it " +
                 "had to be grabbed very precisely. 1 = off.")]
        public float BackpackShoulderGrabScale = 4f;

        [Tooltip("How an NPC's loot is shown: Sockets - the row of loot sockets at the NPC, Backpack - the NPC's backpack " +
                 "lying next to it (a tinted copy of the hero's, categories and pages), Both - in sync. Gone when the " +
                 "loot closes.")]
        public DeveloperConfigEnums.NpcLootMode NpcLootMode = DeveloperConfigEnums.NpcLootMode.Both;

        [Tooltip("Seconds until an NPC's loot backpack nobody picked up disappears (the loot closes like clicking the " +
                 "body again).")]
        public float NpcLootBackpackTimeout = 20f;

        [Tooltip("Loot backpacks of dead NPCs and of containers without a lid (mummies) get a green X above the right " +
                 "page arrow - it moves everything into the hero's inventory.")]
        public bool EnableLootTakeAll = true;

        [Tooltip("Taking pieces off a held item stack: the empty hand near the stack presses its trigger (T in the " +
                 "simulator) - one piece jumps into it, holding the trigger takes more and more quickly.")]
        public bool EnableStackSplit = true;

        [Tooltip("Seconds until the second piece while the split trigger is held (EnableStackSplit).")]
        public float StackSplitRepeatDelay = 0.45f;

        [Tooltip("Each next piece comes this factor faster (0.1-1, 1 = constant pace) (EnableStackSplit).")]
        public float StackSplitAcceleration = 0.8f;

        [Tooltip("Swampweed joints (scheme JOINT) can be smoked at the VR mouth: smoke sound + LIGHTSMOKE puffs, " +
                 "on_state[0] like the engine, joint used up.")]
        public bool EnableSmoking = true;

        [Tooltip("Smoke puffs of one joint (at least 1), spread over the smoking sound.")]
        [Min(1)]
        public int SmokePuffs = 4;

        [Tooltip("Seconds from putting the joint to the mouth until the smoking sound and the first puff (taking a drag).")]
        [Min(0f)]
        public float SmokeStartDelay = 1.5f;

        [Tooltip("G1 world map (WORLD.ZEN) edges in Gothic cm: x = left, y = top, z = right, w = bottom. G1 maps have no " +
                 "Doc_SetLevelCoords - fit by two spots in VR (start, Swamp Camp). " +
                 "All 0 = the world mesh bounds.")]
        public Vector4 G1WorldMapBounds = new(-84056f, 67222f, 84451f, -61217f);


        /**
         * ##########
         * Misc
         * ##########
         */

        [Foldout("Misc", true)]
        [Separator("Meshes/Visuals")]
        public bool EnableWorldMesh = true;
        public bool EnableBarrierVisual = true;

        [Separator("StaticCache")]
        public bool CompressStaticCacheFiles = true;
        public bool AlwaysRecreateCache = false;
        [Tooltip("If filled, then only the selected world and it's data is calculated. (Speeds up caching when testing for a specific world)")]
        public string OnlyCreateCacheForWorld = string.Empty;

        [Separator("WIP - Not production ready", true)]
        public bool EnableDecalVisuals;
        public bool EnableParticleEffects;

    }
}
