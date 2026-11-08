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

        [Tooltip("Immersion option \"Hero's hands\" (ini heroHands, on by default): with the VR hero body, the hero's " +
                 "own fists (gloves of his armor) are the VR hands - turned like the controllers, the VR hand models " +
                 "hidden. Before a world is loaded (menus) the VR hands stay.")]
        public bool EnableHeroHands = true;



        /**
         * ##########
         * Backpack and Items
         * ##########
         */

        [Foldout("Backpack and Items", true)]
        [Tooltip("How an NPC's loot is shown: Backpack (default) - the NPC's backpack lying next to it (a tinted copy " +
                 "of the hero's, categories and pages), Sockets - the old row of loot sockets at the NPC, Both - in sync " +
                 "(the sockets stand where the backpack lies: the hand takes their items, not the backpack). Gone when " +
                 "the loot closes.")]
        public DeveloperConfigEnums.NpcLootMode NpcLootMode = DeveloperConfigEnums.NpcLootMode.Backpack;


        /**
         * ##########
         * NPC AI and Fight
         * ##########
         */

        [Foldout("NPC AI and Fight", true)]
        [Tooltip("The \"Ranged knockout\" option's value when Gothic.ini has none yet.")]
        public bool RangedKnockoutByDefault;

        [Tooltip("Put to sleep by a spell, the VR hero's body falls asleep (T_STAND_2_VICTIM_SLE, S_VICTIM_SLE, " +
                 "wakes with T_VICTIM_SLE_2_STAND, the camera follows the head) and the sleep effect floats over his head.")]
        public bool EnableHeroSleepBody = true;

        [Tooltip("Chasing NPCs run along a NavMesh (built at runtime around the hero from world + vob colliders, " +
                 "Gothic STEP_HEIGHT/SLIDE_ANGLE): around obstacles, and they stop at ledges/drops instead of running " +
                 "off the world. Routines still walk the waynet.")]
        public bool EnableNpcNavMesh = true;

        [Tooltip("Brightness of the world's lightmaps (1 = as compiled by Gothic).")]
        [Range(0.25f, 4f)]
        public float WorldLightmapBrightness = 1f;

        [Tooltip("G1 chain lightning like the engine - the NPC in front of the eyes is zapped (ZS_Zapped hurts him " +
                 "every second), every invest level it jumps on to the nearest NPC, with nobody left it zaps the hero. " +
                 "The release frees them (PERC_ASSESSSTOPMAGIC). Before: one hit on everyone around.")]
        public bool EnableVrChainLightning = true;

        [Tooltip("Beyond the scripts: after giving up the chase, an NPC ignores the hero's presence for 5 seconds " +
                 "(hitting it still starts a fight). Off: PERC_ASSESSENEMY/ASSESSWARN restart the fight at once, " +
                 "like the scripts.")]
        public bool EnableHeroChaseCooldown = true;

        [Tooltip("Footsteps - the animations' ground sounds (*eventSFXGrnd, walk/run loops of NPCs, monsters and the " +
                 "hero's body) play with the ground's suffix like the engine (RUN_EARTH, RUN_STONE, RUN_WOOD, " +
                 "RUN_WATER); the material is guessed (wading, a VOB below, under a ceiling). They were skipped.")]
        public bool EnableFootstepSounds = true;

        [Tooltip("Hero attacks deal max HP damage — one hit kills any non-immortal NPC.")]
        public bool EnableOneHitKill;

        [Tooltip("Hero attacks deal currentHP-1 damage — one hit knocks out any NPC without killing them.")]
        public bool EnableOneHitKnockout;

        [Tooltip("Immersion option \"Ranged knockout\": off (like OpenGothic) a lethal arrow, bolt or spell kills a " +
                 "human, on it knocks him out and the next one (or a sword) finishes him off. Area spells (fire rain) hit " +
                 "the knocked out too. Before: ranged and magic always left humans and the hero at 1 HP.")]
        public bool EnableRangedKnockoutOption = true;

        [Tooltip("Blocking in VR like Gothic's parry: both hands on a melee weapon (one- or two-handed) and both " +
                 "triggers held. Hits of humans (fists too) and of monsters with a drawn weapon (orcs, skeletons) from " +
                 "the front (45 degrees around the head's direction) do no damage, the weapons clash. Animals, spells, " +
                 "projectiles and hits from the side or back go through; while blocking the hero's own hits don't count.")]
        public bool EnableVrHeroBlock = true;

        [Tooltip("Attack approach: how close an attacker must get to the offset target before stopping the chase. Default 0.2m.")]
        [Range(0.05f, 2f)]
        public float NpcAttackArrivalThreshold = 0.2f;

        [Tooltip("How close an NPC stops when walking toward the player for dialog (GoToNpc). Default 1.3m.")]
        [Range(0.1f, 5f)]
        public float NpcDialogStopDistance = 1.3f;

        [Tooltip("How much an NPC's flying spell follows its target (0 = flies to where the target was at the cast, 1 = " +
                 "always hits). At 0.2 a step aside dodges it - fully homing nobody could dodge a crawler's web.")]
        [Range(0f, 1f)]
        public float NpcSpellHoming = 0.2f;

        [Tooltip("Scales NPC ranged/magic engagement distances — how far a mage/archer will attack and connect from (vanilla HAI_DIST_RANGED = 30m). Gothic's ranges feel too far against our Unity units; 0.8 = 24m.")]
        [Range(0.1f, 2f)]
        public float RangedCombatRangeMultiplier = 0.8f;

        [Tooltip("Scales Wld_SpawnNpcRange's spawn radius (summon spells). Gothic's cm ranges feel too spread out against our Unity units — 0.8 keeps summons visibly near their caster.")]
        [Range(0.1f, 2f)]
        public float SummonSpawnRangeMultiplier = 0.8f;


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
