using System;
using System.Collections.Generic;
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
        [Tooltip("Pre-select a mod. Editor only - builds use GameSettings.json ModPath/ModIni or GameProfiles.")]
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
            "ModIni = INI filename inside ModPath/system/ (empty = GothicGame.ini).")]
        public List<JsonGameProfile> GameProfiles = new();

        [Tooltip("Language used when auto-detection fails and the mod ini has no unityFallbackLanguage (mods often " +
            "rename MOBNAME_CRATE, the probe constant - e.g. G2 Renovation uses 'Skrzynka'). One of: cs, pl, ru, de, " +
            "en, es, fr, it. Empty = fail the boot like before.")]
        public string FallbackLanguage = string.Empty;

        public Controls GameControls = Controls.VR;

        [ConditionalField(fieldToCheck: nameof(GameControls), compareValues: Controls.VR)]
        [Tooltip("VR interaction framework. Hvr falls back to Xri if HVR isn't installed (no GOTHIC_HVR_INSTALLED).")]
        public VrInteractionBackend VrInteractionBackend = VrInteractionBackend.Hvr;

        [Separator("Debug")]
        [ConditionalField(fieldToCheck: nameof(GameControls), compareValues: Controls.VR)]
        public bool EnableVRDeviceSimulator;

        [Tooltip("Keys of the VR device simulator on the XRI backend: XRI's own or like the HVR simulator (W/A/S/D, " +
                 "right mouse turns, hold Q/E + mouse moves a hand, G grabs/lets go, left click = trigger).")]
        public DeveloperConfigEnums.VrSimulatorControls VrSimulatorControls =
            DeveloperConfigEnums.VrSimulatorControls.XriSimulator;

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
        [Tooltip("Enable 'Load Game' in the main menu.")]
        public bool EnableLoadFeature;

        [Tooltip("Enable 'Save Game' in the main menu.")]
        public bool EnableSaveFeature;

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

        [Tooltip("V1: Unnamed zCVobs without a visual only group their children - the children become VOBs of their " +
                 "own. As lazy loaders without bounds these groups never loaded, nothing below them appeared (The " +
                 "Chronicles Of Myrtana: the intro ship and ~8000 VOBs).")]
        public bool EnableEmptyVobContainerChildren = true;

        [Tooltip("V1: Child VOBs are placed at their own position (a world position, like the root's). They stayed at " +
                 "their parent's origin - The Chronicles Of Myrtana's intro ship had its decoration in one pile.")]
        public bool EnableWorldSpaceChildVobs = true;

        [Tooltip("Debug: logs creation, init and destruction of VOBs whose visual contains this text ([VobTrace]). " +
                 "Empty = off.")]
        public string DebugTraceVobVisual = string.Empty;

        [Separator("Save/Load System (WIP)")]
        [Tooltip("Enable save/load system. OFF = main-branch behavior: no UNITYNPCINIT snapshot, no merged-snapshot NPC restore, no NpcCulling tracking. Turn OFF to diagnose monster/NPC init regressions.")]
        public bool EnableSaveLoadSystem;

        [Separator("Combat (WIP)")]
        [Tooltip("Enable NPC ranged combat (bow/crossbow). OFF = NPCs fall back to their best melee weapon regardless of range.")]
        public bool EnableNpcRangedCombat;

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

        [Separator("Movers")]
        [Tooltip("V1: Mover keyframes are relative to where the VOB stands, like in the engine - only matters for movers " +
                 "whose VOB doesn't stand at the first keyframe.")]
        public bool EnableMoverRelativeKeyframes = true;

        [Tooltip("V1: zCMessageFilter Enable/Disable/Toggle and startEnabled=false work on movers - a disabled mover " +
                 "ignores triggers (G2 Irdorath's large gates).")]
        public bool EnableTriggerEnableState = true;

        [Tooltip("V1: zCTriggerList ALL: each target's delay counts from the previous target, like in the engine.")]
        public bool EnableTriggerListSummedDelays = true;

        [Tooltip("V1: zCCodeMaster like the engine: every press counts, an ordered code is judged after as many presses " +
                 "as slaves and a wrong one fires the failure target (G2 Irdorath's touchplate rooms).")]
        public bool EnableEngineCodeMaster = true;

        [Tooltip("V1: Movers (keyframe, open, disabled) and trigger activation counts are saved per world and restored " +
                 "on load and when coming back to a world - before every mover was closed again after loading.")]
        public bool EnableLogicVobSaveState = true;

        [Tooltip("V1: Items lying in a world (taken, dropped, NPC drops, arrows, moved) and chest contents/locks are saved " +
                 "as the difference to the world file - a beer taken before saving lay in the world again after loading.")]
        public bool EnableWorldItemSaveState = true;

        [Tooltip("V1: NPC inventories are saved and restored - a looted NPC got everything back from INIT after loading.")]
        public bool EnableNpcInventorySaveState = true;

        [Tooltip("V1: Saving puts what the hands hold into the backpack first - it was neither in the inventory nor in " +
                 "the world.")]
        public bool EnableSaveStoresHeldItems = true;

        [Tooltip("V1: Animation combinations (aniComb) play their middle variant - the ore vein's dig loop exists only as " +
                 "c_Ore_S1_1..3, miners stood frozen in front of it.")]
        public bool EnableAnimationCombines = true;

        [Tooltip("V1: A new game clears a spawn position/level change left from a loaded save or portal - Mroczne " +
                 "Tajemnice's new game started in the middle of the Old Camp.")]
        public bool EnableNewGameSpawnReset = true;

        [Tooltip("V1: A mob whose slots are all used by other NPCs isn't free (AI_UseMob looks for another one) - two " +
                 "miners spawned next to each other hacked at the same ore vein.")]
        public bool EnableMobOccupancy = true;

        [Tooltip("V1: Free points and waypoints are cleared before a world is loaded - the previous world's START won and " +
                 "a new game after loading an Old Mine save started in the middle of the Old Camp.")]
        public bool EnableWorldPointsReset = true;

        [Tooltip("V1: A spawned NPC waits (kinematic, max 10 s) until the world's VOBs are created before it snaps to the " +
                 "ground and gets gravity - Old Mine miners fell through their scaffolds after loading.")]
        public bool EnableNpcWaitsForWorldVobs = true;

        [Tooltip("V1: AI_StartState(..., 1, ...) runs the current state's _End function (it never ran) and, in a dialog, " +
                 "waits for the queued lines - a dialog ending in a fight left the hero invincible (ZS_Talk_End skipped): " +
                 "nobody talked to or fought him anymore.")]
        public bool EnableQueuedStateEnd = true;

        [Tooltip("V1: A knocked out NPC gets no perceptions but magic until he stands up - while lying he drew his " +
                 "weapon, fled or joined fights. A knockout no longer runs the old state's _End (EnableQueuedStateEnd).")]
        public bool EnableDownedNpcPerceptions = true;

        [Tooltip("V1: A mover below another mover moves along with it, like in the engine - G2's Jharkendar portal petals " +
                 "(children of moving parents) stayed in the middle after loading a save.")]
        public bool EnableNestedMovers = true;

        [Tooltip("V1: A mob with a trigger target and useWithItem only works when the player has the item, like in the " +
                 "engine - G2's Jharkendar portal opened without the ornament ring.")]
        public bool EnableMobRequiredItem = true;

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

        [Tooltip("WIP - Not production ready.")]
        [ConditionalField(fieldToCheck: nameof(EnableNpcs), compareValues: true)]
        public bool EnableNpcEyeBlinking;

        [Tooltip("V1: NPC faces like the engine - the mouth moves while talking (VISEME), Mdl_StartFaceAni moods " +
                 "(S_FRIENDLY, S_ANGRY, S_FRIGHTENED...) and blinking, blended on top of each other with the MMB's " +
                 "blend in/out. Before, talking showed nothing and each morph replaced the whole face.")]
        public bool EnableFaceAnimations = true;
        
        [Separator("Distances")]
        [Tooltip("How close an NPC stops when walking toward the player for dialog (GoToNpc). Default 1.3m.")]
        [Range(0.1f, 5f)]
        public float NpcDialogStopDistance = 1.3f;

        [Tooltip("Attack approach: offset from enemy center each attacker targets, spreading them out. Default 1.3m.")]
        [Range(0f, 3f)]
        public float NpcAttackApproachSpread = 1.3f;

        [Tooltip("Attack approach: how close an attacker must get to the offset target before stopping the chase. Default 0.2m.")]
        [Range(0.05f, 2f)]
        public float NpcAttackArrivalThreshold = 0.2f;

        [Separator("WIP")]
        [Tooltip("Enable looting dead NPCs/monsters: grab a dead NPC to open a loot panel with their Daedalus inventory. WIP.")]
        public bool EnableNpcLooting;

        [Tooltip("The hero can move during dialogs (inside Constants.DialogMaxDistance); walking away chooses END if offered.")]
        public bool EnableDialogFreeMovement = true;

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

        [Tooltip("Some mod music compositions embed automatic dmusic segues (e.g. a boss-fight transition) to a segment we never see or validate. If its referenced instruments are incomplete, dmusic double-frees natively — an unrecoverable AccessViolationException no amount of C# try/catch can stop. Turn music off entirely to sidestep it for mods that hit this.")]
        public bool EnableMusic = true;

        [Tooltip("Some mods (e.g. New Balance) ship dubbing as Ogg Vorbis with a '.wav' extension slapped on. We sniff real content and decode via NVorbis when detected — vanilla Gothic never ships this, so this only ever applies to mod audio. Escape hatch in case a specific file misbehaves: turn off to skip decoding it gracefully (silence + text-length fallback) instead of failing loudly.")]
        public bool EnableOggAudio = true;


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

        [Tooltip("Show 'Level +5' button in MarvinMode panel. Adds 5 levels (+50 LP, +60 HP max). Repeatable.")]
        public bool EnableLevel5Cheat;

        [Tooltip("Show 'Guild → Novice' button in MarvinMode panel. Sets hero guild to GIL_NOV.")]
        public bool EnableGuildCheat;

        [Tooltip("Show 'Guild → Scavenger' button in MarvinMode panel. Sets hero guild to GIL_SCAVENGER (24) for NPC combat testing.")]
        public bool EnableScavengerGuildCheat;

        [Tooltip("Show 'Skip Time +30min' button in MarvinMode panel. Advances game time and recalculates NPC routines.")]
        public bool EnableTimeSkip;

        [Tooltip("Waypoint name for 'Teleport to WP' Marvin button. E.g. GRYD_072 or START. Leave empty to disable.")]
        public string MarvinTeleportWaypoint;

        [Tooltip("Show 'TP +4 m' button in MarvinMode panel (Gothic's Marvin key K): 4 m forward where the hero looks, " +
                 "horizontally only - through doors and walls.")]
        public bool EnableMarvinBlink = true;

        [Tooltip("V1: Marvin's \"kill\" like Gothic's - the NPC you look at dies (not immortal ones, e.g. Diego).")]
        public bool EnableMarvinKill = true;

        [Tooltip("MarvinMode only: no floor below the hero for 2 s (fell out of the world) - back to the world's START " +
                 "point.")]
        public bool EnableMarvinFallRescue = true;

        [Tooltip("Show 'God mode' toggle in MarvinMode panel: hits and falls don't hurt the hero.")]
        public bool EnableMarvinGodMode = true;

        [Tooltip("Daedalus symbol name of NPC to spawn next to player via Marvin 'Spawn NPC' button. E.g. SH, PC_THIEF. Leave empty to disable.")]
        public string MarvinSpawnNpcSymbol = string.Empty;

        [Tooltip("Daedalus symbols of NPCs the Marvin 'Irdo team' button spawns at once in front of the player, comma " +
                 "separated. E.g. PC_THIEF_DI, PC_FIGHTER_DI, PC_MAGE_DI, PC_PSIONIC_DI, SLD_801_TORLOF_DI. Leave empty to " +
                 "hide the button.")]
        public string MarvinFriendsNpcSymbols = string.Empty;

        [Tooltip("Daedalus symbols of NPCs the Marvin 'Cheat friends' button spawns, comma separated - the scripts' test " +
                 "helpers SH (story helper) and CH (character helper). Leave empty to hide the button.")]
        public string MarvinCheatFriendsNpcSymbols = string.Empty;

        [Tooltip("Marvin console commands in its recent list from the start, separated by ';' (e.g. 'cheat god; goto " +
                 "forward'). Only shown for one click, not run. Leave empty for none.")]
        public string MarvinStartCommands = string.Empty;

        [Tooltip("Items for the Marvin command 'give items', comma separated, optional amount: " +
                 "ITRU_FIREBOLT, ITSC_ICEBOLT:3, ITPO_MANA_03:5. Leave empty to hide the button.")]
        [TextArea(2, 6)]
        public string MarvinGiveItems = string.Empty;

        [Header("NPC Combat (WIP)")]
        [Tooltip("Fire ZS_Attack_Loop early when combo window opens so the next attack chains before the animation ends.")]
        public bool EnableNpcCombatCombos = true;

        [Tooltip("Enable proximity + FOV hit detection in AttackPlayAni. Disable to watch animations without dealing damage.")]
        public bool EnableNpcHitDetection = true;

        [Tooltip("V1: NPC archers shoot real arrows/bolts like the engine - aimed ballistically from the hand, flying " +
                 "with gravity, hitting whatever they meet (dodge, take cover). The hit chance (G1 dexterity, G2 talent, " +
                 "by distance) aims a miss past the target. Before, a bow shot hit instantly when the target was in front.")]
        public bool EnableNpcPhysicalProjectiles = true;

        [Tooltip("V1: Flying arrows and bolts (hero and NPCs) leave a short streak with the weapon trail's look.")]
        public bool EnableProjectileTrail = true;

        [Tooltip("V1: Npc_HasRangedWeaponWithAmmo counts the munition like the engine - an archer out of arrows/bolts " +
                 "switches to the melee weapon (B_SelectWeapon) instead of shooting nothing.")]
        public bool EnableRangedWeaponNeedsAmmo = true;

        [Tooltip("V1: An archer out of arrows/bolts keeps chasing with his melee weapon (G1's AIV_LASTHITBYRANGEDWEAPON) " +
                 "instead of giving up at 10 m and drawing/putting away his sword in a loop.")]
        public bool EnableOutOfAmmoKeepsChasing = true;

        [Tooltip("V1: The fight's turn move ends once the enemy is in focus (20 of the 30 degrees) or after 2 s - " +
                 "Ratford turned forever towards a scavenger circling him and never hit it.")]
        public bool EnableFightTurnTolerance = true;

        [Tooltip("V1: An archer facing the hero counts him as in focus even behind cover (a pillar) - he steps aside " +
                 "to a free line of shot instead of only turning and standing still.")]
        public bool EnableRangedFocusIgnoresCover = true;

        [Tooltip("V1: An archer's step aside / run around cover ends as soon as his shot is free; the run no longer " +
                 "stops after one frame at the ranged reach (shaking forward in place).")]
        public bool EnableBlockedShotMoveInterrupt = true;

        [Tooltip("Hero attacks deal max HP damage — one hit kills any non-immortal NPC.")]
        public bool EnableOneHitKill;

        [Tooltip("Hero attacks deal currentHP-1 damage — one hit knocks out any NPC without killing them.")]
        public bool EnableOneHitKnockout;

        [Tooltip("Scales Wld_SpawnNpcRange's spawn radius (summon spells). Gothic's cm ranges feel too spread out against our Unity units — 0.8 keeps summons visibly near their caster.")]
        [Range(0.1f, 2f)]
        public float SummonSpawnRangeMultiplier = 0.8f;

        [Tooltip("Scales NPC ranged/magic engagement distances — how far a mage/archer will attack and connect from (vanilla HAI_DIST_RANGED = 30m). Gothic's ranges feel too far against our Unity units; 0.8 = 24m.")]
        [Range(0.1f, 2f)]
        public float RangedCombatRangeMultiplier = 0.8f;

        [Tooltip("Hide the health bar for the player character.")]
        public bool HideHealthBar { get; internal set; }


        [Separator("VR gameplay", true)]
        [Tooltip("While the backpack is held in a hand, its bottom opening gently pulls nearby world items. " +
                 "An item staying inside the opening for BackpackVacuumStoreSeconds gets stored.")]
        public bool EnableBackpackVacuum = true;

        [Tooltip("How an NPC's loot is shown: Sockets - the row of loot sockets at the NPC, Backpack - the NPC's backpack " +
                 "lying next to it (a tinted copy of the hero's, categories and pages), Both - in sync. Gone when the " +
                 "loot closes.")]
        public DeveloperConfigEnums.NpcLootMode NpcLootMode = DeveloperConfigEnums.NpcLootMode.Both;

        [Tooltip("Seconds until an NPC's loot backpack nobody picked up disappears (the loot closes like clicking the " +
                 "body again).")]
        public float NpcLootBackpackTimeout = 20f;

        [Tooltip("Pull radius around the backpack opening in meters.")]
        [Range(0.2f, 3f)]
        public float BackpackVacuumRadius = 1.2f;

        [Tooltip("Seconds an item has to stay inside the backpack opening before it's stored.")]
        [Range(0.5f, 10f)]
        public float BackpackVacuumStoreSeconds = 3f;

        [Tooltip("When the hero gets knocked out, weapons held in VR hands are dropped (vanilla: hero drops readied weapon).")]
        public bool EnableHeroDropsWeaponsOnKnockout = true;

        [Tooltip("Clear an NPC's perceptions when its routine/state changes, like the engine (changes AI of all NPCs). " +
                 "Fixes NPCs keeping ZS_AssessFighter perceptions after AI_ContinueRoutine ($WISEMOVE on every " +
                 "weapon/rune removal). The new state's ZS_ init re-enables its own perceptions.")]
        public bool EnableRoutinePerceptionReset = true;

        [Tooltip("When the hero starts casting (mana investment), nearby NPCs get PERC_ASSESSCASTER like in the engine. " +
                 "B_AssessCaster only reacts to offensive (SPELL_BAD) spells.")]
        public bool EnableCasterPerception = true;

        [Tooltip("When a script takes an item from the hero (B_GiveInvItems in a dialog), surplus physical copies " +
                 "in VR hands/holsters are removed too (not only the inventory count).")]
        public bool EnableScriptRemovesHeldItems = true;

        [Tooltip("Swampweed joints (scheme JOINT) can be smoked at the VR mouth: smoke sound + LIGHTSMOKE puffs, " +
                 "on_state[0] like the engine, joint used up.")]
        public bool EnableSmoking = true;

        [Tooltip("Smoke puffs of one joint (at least 1), spread over the smoking sound.")]
        [Min(1)]
        public int SmokePuffs = 4;

        [Tooltip("Seconds from putting the joint to the mouth until the smoking sound and the first puff (taking a drag).")]
        [Min(0f)]
        public float SmokeStartDelay = 1.5f;

        [Tooltip("AI_EquipArmor / AI_UnequipArmor / AI_EquipBestArmor + armor (un)equips at runtime swap the NPC's body " +
                 "mesh (Greg in Lobart's clothes, Pedro's robe, ...). Also kept after save/load. Read at VM start.")]
        public bool EnableRuntimeArmorVisuals = true;

        [Tooltip("V1: Only treasure X marks (B_SCGetTreasure) are dug by hitting them, with their own bounds. MT's anvil " +
                 "(used with the sledgehammer) counted as a dig spot: its smithing dialog never opened, the hero stayed " +
                 "invincible (NPCs ignored him) and nested VOBs made its bounds cover the whole Old Camp.")]
        public bool EnableDigSpotTreasureOnly = true;

        [Tooltip("V1: A mob dialog's box is placed next to the mob's own mesh - VOBs nested below it (MT's anvils) put it " +
                 "hundreds of meters away and the dialog didn't start (\"hero farther than 5 m\").")]
        public bool EnableMobDialogOwnBounds = true;

        [Tooltip("V1: The hero's HP/mana bars with their numbers and the chapter popup are never cut by the world " +
                 "(VRUiOnTop copied from the menu's). Dialog, subtitles, menus and PrintScreen are always on top (main).")]
        public bool EnableHudAlwaysOnTop = true;

        [Tooltip("Crossbows (ITEM_CROSSBOW) shoot in VR: aim with the crossbow (one or both hands), trigger fires a " +
                 "bolt (munition from the inventory, 1 per shot, 30 m/s + gravity). Engine damage (G2 DEX + damage - " +
                 "protection). Automatic reload after 1.5 s for now.")]
        public bool EnableVrCrossbow = true;

        [Tooltip("Items without pre-cached colliders (morph meshes: bows, crossbows) get a box collider from their " +
                 "mesh bounds. Before they had no collider: fell through the ground, no force grab, flew away in hands.")]
        public bool EnableItemColliderFallback = true;

        [Tooltip("V1: Bows and crossbows get boxes fitted to their parts (stock, prod, limbs - turned along them, no " +
                 "string) instead of the cached capsules - a curved bow got three fat capsules full of air, hard to " +
                 "grab and hold.")]
        public bool EnableRangedFittedColliders = true;

        [Tooltip("V1: Melee weapons get boxes fitted to their parts too (blade, guard, grip, pommel, axe head - turned " +
                 "along them) instead of the cached boxes along the mesh axes - a slanted cross guard got a box of " +
                 "28 x 20 cm, axe and hammer heads boxes full of air. They are the hitboxes of the hero's swings.")]
        public bool EnableMeleeFittedColliders = true;

        [Tooltip("Npc_ClearAIQueue no longer restarts an NPC's running idle animation (engine: only the queue is " +
                 "cleared). Summoned demons flapped their wings every 0.5 s (B_FullStop on each ASSESSPLAYER).")]
        public bool EnableKeepIdleOnClearAiQueue = true;

        [Tooltip("V1: \"(Trigger) skip\" under the video cinema's screen (Gothic-UI localization key cinema.skip).")]
        public bool EnableCinemaSkipHint = true;

        [Tooltip("V1: After the ending's videos (G1 ExitGame after the Sleeper, G2 ExitSession after the captain's 'It's " +
                 "over') the game restarts to the main menu / game profiles instead of closing.")]
        public bool EnableEndingReturnsToMenu = true;

        [Tooltip("The VR hero's body state follows its movement on land (BS_STAND/BS_WALK/BS_RUN) like the " +
                 "engine. Scripts need it: ZS_Attack gives up a chase ('$RUNCOWARD') only while the target runs.")]
        public bool EnableHeroMoveBodyState = true;

        [Tooltip("A crossbow needs both hands on it to shoot - from 60 % crossbow (G1: also talent master) it " +
                 "shoots one-handed (e.g. sword in the other hand).")]
        public bool EnableCrossbowMasterOneHand = true;

        [Tooltip("NPCs give up chasing the running hero twice as fast (~15 s instead of 30 s): ZS_Attack_Loop's " +
                 "state time runs double while it chases the running hero. Needs EnableHeroMoveBodyState.")]
        public bool EnableFasterChaseGiveUp = true;


        [Separator("TODO TEST ME V1s and MVPs", true)]
        [Tooltip("V2: Backpack in one hand + item in the other hand (or hovering one in the backpack) shows a popup " +
                 "with the item's name, amount and Gothic inventory description (C_Item text[]/count[]).")]
        public bool EnableItemDetailsPopup = true;

        [Tooltip("V1: Grab armor/amulet/ring/belt with both hands + trigger (R) -> it's stored in the backpack and equipped " +
                 "(protection + on_equip). Equipped items are listed first in the backpack with an [E] badge; " +
                 "taking the last one out of the backpack unequips it.")]
        public bool EnableEquipItems = true;

        [Tooltip("V1: Daedalus EquipItem() applies protection[] + on_equip like the engine. Without it, NPC armor " +
                 "(and the hero's script-equipped starting armor) protects nothing. Changes NPC combat balance!")]
        public bool EnableScriptEquipEffects = true;

        [Tooltip("V1: NPCs/monsters in water like the engine: wading (WALKW) above the guild's knee depth, swimming at " +
                 "the surface above chest depth if the model has swim animations - otherwise deep water is a wall " +
                 "(monsters stop at the shore). Hero: water level resets out of water (teleport, save load) and the " +
                 "hero is BS_SWIM/BS_DIVE for scripts (monsters stop chasing a swimming hero).")]
        public bool EnableNpcWater = true;

        [Tooltip("V1: Bows (ITEM_BOW) in VR: the Gothic string is replaced by our own. Hold the bow in one hand, put the " +
                 "other empty hand to the string and hold its GRIP to draw (an arrow from the inventory is nocked), " +
                 "release to shoot - speed/damage grow with the draw. Missed arrows/bolts lie in the world to pick up.")]
        public bool EnableVrBows = true;

        [Tooltip("V1: Morph animations of items (a bow's draw S_SHOOT) find their vertices by position - bows never " +
                 "bent (their mesh was built once without the morph mesh, no vertex mapping).")]
        public bool EnableMorphByPosition = true;

        [Tooltip("V1 (VR): After a shot the crossbow's string sits at the prod (Gothic's own S_RELAX shape). Grab it " +
                 "with the free hand (grip), pull it back along the stock until it locks - then it's cocked and shows " +
                 "a bolt. Out of a holster/backpack it's cocked. The 'Crossbow' Immersion option turns it off " +
                 "(automatic reload).")]
        public bool EnableVrCrossbowManualReload = true;

        [Tooltip("V1: Our (VR) options are written into their own Gothic.ini section, stray copies in other VR sections " +
                 "are removed - a new key went to the end of the file (the last section) and every change added " +
                 "another line; the last one in the file wins when loading.")]
        public bool EnableIniWriteInSection = true;

        [Tooltip("V1 (VR): Gothic's spell selection - both empty hands above the head + both triggers (1 s) open a " +
                 "carousel of the equipped runes/scrolls (max 8) in front of you. Grip takes a spell, trigger + move " +
                 "turns it, a trigger click unequips; a rune/scroll held into the empty slot is equipped. 'Favorites " +
                 "ring' Immersion option (ini spellRing); with EnableRingWeaponSlots also the equipped weapons.")]
        public bool EnableVrSpellRing = true;

        [Tooltip("V1: Projectile spells (VISUALFX CAST key with a TARGET trajectory: Firebolt, Fireball, Thunderbolt, " +
                 "Icecube, ...) are thrown: hold the rune in one hand, hold trigger to charge, swing + release trigger to " +
                 "throw. Soft homing towards the NPC closest to the throw direction. Other spells stay as they are.")]
        public bool EnableThrowableSpells = true;

        [Tooltip("V1: Fire spells (damageType DAM_FIRE: Firebolt, Fireball, fire rain, ...) set NPCs on fire for a few " +
                 "seconds - VOB_BURN flames out of their whole body, light, sound and the s_Fire_Victim dance. " +
                 "No burn damage.")]
        public bool EnableBurning = true;

        [Tooltip("V1: An NPC that gave up a chase/fight with the hero (hero not down) ignores him for 5 s " +
                 "(PERC_ASSESSENEMY / a comrade's ASSESSWARN); hitting it still starts a fight - vanilla has no " +
                 "hysteresis, the NPC gave up and came back endlessly inside its 20 m senses range.")]
        public bool EnableChaseGiveUpCooldown = true;

        [Tooltip("V1: The VR hero's BS_RUN stays 1.5 s after running slows down, and a teleport counts as running - " +
                 "ZS_Attack's pursuit counter reset in every turn, NPCs never gave up a chase.")]
        public bool EnableStickyHeroRun = true;

        [Tooltip("V1: NPC senses like in the engine instead of all around: sight in the full senses range but only in " +
                 "front (FOV), hearing only while the hero walks/runs and within half the range, smell only 3 m around " +
                 "- orcs sensed a hero standing still behind them 20 m away.")]
        public bool EnableQuietSenses = true;

        [Tooltip("V1: Orcs threaten (turn, T_WARN, war cry) before they start chasing the hero in ZS_ProclaimAndPunish, " +
                 "and an NPC giving up a chase without a $RUNCOWARD line (orc voice) says $GETOUTOFHERE.")]
        public bool EnablePursuitWarn = true;

        [Tooltip("V1: NPCs and monsters running at the hero sometimes attack out of the run (t_<weapon>AttackMove: " +
                 "1h/fist humans, orcs, wolves, scavengers, ...) instead of stopping and striking from a stand.")]
        public bool EnableNpcRunAttack = true;

        [Tooltip("V1: An archer/crossbowman/mage gives up when the hero gets out of the reach of its shots (24 m with " +
                 "RangedCombatRangeMultiplier 0.8): weapon away, back to the routine (vanilla: only at 35 m).")]
        public bool EnableRangedGiveUp = true;

        [Tooltip("V1: A fire storm's burning area damages everyone within 2 m of the impact (also on the ground), not " +
                 "only the NPC hit directly.")]
        public bool EnableSpellSpreadDamage = true;

        [Tooltip("V1: Like the engine, a hit never does less than NPC_MINIMAL_DAMAGE (Balancing.d, G1: 1) unless the " +
                 "target is immune to the damage type (protection -1). Read at the first hit.")]
        public bool EnableMinimalDamage = true;

        [Tooltip("V1: After the hero slept (PC_Sleep's Wld_SetTime, Marvin's time skip) every living NPC starts over " +
                 "like after culling: routine waypoint of the new time, no aggro, weapon away.")]
        public bool EnableTimeSkipResetsNpcs = true;

        [Tooltip("V1: A trigger/mover you shoot (reactToOnDamage) that shows nothing (no mesh or it didn't load) gets " +
                 "a green X in the middle of its zone - where to shoot with a bow, crossbow or spell.")]
        public bool EnableDamageTargetMarkers = true;

        [Tooltip("V1: Arrows, bolts and spell projectiles home on a VOB that reacts to hits (trigger/mover with " +
                 "reactToOnDamage) within 12 degrees of the flight direction - they are often invisible.")]
        public bool EnableDamageTargetAutoAim = true;

        [Tooltip("V1: Triggers, trigger lists, code masters, movers, ... below another VOB are registered for trigger " +
                 "dispatch and get their own children created - before only root ones were found (G2 Irdorath's last door).")]
        public bool EnableChildLogicVobs = true;

        [Tooltip("V1: Mobs collect their grab colliders again once their mesh exists - mobs with the mesh on a child " +
                 "bone (G2 Irdorath's touchplates) couldn't be hovered or used.")]
        public bool EnableMobColliderRefresh = true;

        [Tooltip("V1: NPC bodies never come closer than 1 m to the VR hero's head - their models went into the " +
                 "camera and pushed the hands with the held weapon. Position only, the AI isn't changed.")]
        public bool EnableHeroPersonalSpace = true;

        [Tooltip("V1: The personal space also counts from the NPC's animated body, not only its root - a shadowbeast's " +
                 "root is at its hind legs and the head went into the camera, orcs leaning into a swing too.")]
        public bool EnableBodyPersonalSpace = true;

        [Tooltip("V1: The NPC's weapon hitbox follows its animated body (bones + skin) instead of a fixed box on the " +
                 "root bone - a shadowbeast's box lay sideways over its hind half, the front with the head couldn't be hit.")]
        public bool EnableAnimatedNpcHitbox = true;

        [Tooltip("V1: An NPC's attack hits once - the next attack with the same animation saw the old track still " +
                 "blending out: its hit frame counted as reached and the action ended at once (a shadowbeast took the " +
                 "hero's HP 3 times in one swing). A combo only restarts an attack with another hit frame (engine).")]
        public bool EnableSingleHitPerNpcAttack = true;

        [Tooltip("V1: NPC melee combos like the engine: an attack ends at its hit end, a combo continues the same " +
                 "animation to its next hit frame (a master's s_1hAttack: 4 hits). Before, the whole animation played " +
                 "and only its first swing did damage.")]
        public bool EnableEngineNpcCombo = true;

        [Tooltip("V1: Npc_GetInvItemBySlot returns the slot-th item of an inventory category like the engine - only the " +
                 "equipped weapon/armor was found: an NPC plundering the knocked out hero (G1 B_Plunder) said he had " +
                 "no ore with 1000 in the backpack.")]
        public bool EnableInvItemBySlot = true;

        [Tooltip("V1: The VR UI pointer's line is drawn above the on-top menu/dialog like the hands - it ended at the " +
                 "menu's edge and you couldn't see where you pointed.")]
        public bool EnablePointerLineOnTop = true;

        [Tooltip("V1: An NPC drawing his weapon in a dialog only closes it when no more lines follow - the dialog lasts " +
                 "until AI_StopProcessInfos like in the engine. Before, the last lines were talked without the dialog " +
                 "bubble and couldn't be skipped (G1 Lukis).")]
        public bool EnableDialogUntilStopProcessInfos = true;

        [Tooltip("V1: In a dialog the NPC follows you with his head; walk behind him (beyond what his neck can turn) " +
                 "and he turns his body towards you.")]
        public bool EnableDialogBodyTurn = true;

        [Tooltip("V1: Sound effects (C_SFX) are files of _WORK/DATA/SOUND/SFX like in the engine - by name only, a newer " +
                 "archive's speech file with the same name won (G1: the hero's death scream and the pain sounds came " +
                 "from Speech.vdf, not sound.VDF).")]
        public bool EnableSfxFromSfxFolder = true;

        [Tooltip("V1: The hero makes a pain sound when hit (SVM_<voice>_AARGH) like the NPCs.")]
        public bool EnableHeroHurtSound = true;

        [Tooltip("V1: Sound effects with numbered variants (SVM_x_AARGH, SVM_x_AARGH_1 ... _4) pick one at random like " +
                 "the _A1, _A2 variants - the pain screams only ever played the first one.")]
        public bool EnableSfxNumberedVariants = true;

        [Tooltip("V1: Npc_GetLookAtTarget (G2 scripts) returns the NPC set by AI_LookAtNpc. Read at VM start.")]
        public bool EnableNpcLookAtTarget = true;

        [Tooltip("V1: Hitting your own summon (demon, skeleton, ...) damages it, but it doesn't attack you back and " +
                 "nobody else reacts to it - a training dummy only for its summoner.")]
        public bool EnableSummonIgnoresMasterHits = true;

        [Tooltip("V1: Chasing NPCs run along a NavMesh (built at runtime around the hero from world + vob colliders, " +
                 "Gothic STEP_HEIGHT/SLIDE_ANGLE): around obstacles, and they stop at ledges/drops instead of running " +
                 "off the world. Routines still walk the waynet.")]
        public bool EnableNpcNavMesh = true;

        [Tooltip("V1: NPCs fall off ledges (S_FALLDN/S_FALL, no steering in the air, landing animation, fall damage " +
                 "from FALLDOWN_HEIGHT/FALLDOWN_DAMAGE) and chasing NPCs climb ledges up to JUMPMID_HEIGHT with the " +
                 "JumpUpLow/JumpUpMid animations (only models that have them).")]
        public bool EnableNpcJumpAndFall = true;

        [Tooltip("V1: NPCs slide along walls/rocks instead of walking into them (sphere probe at hip height). Fast " +
                 "monsters ran through rocks and fell below the world.")]
        public bool EnableNpcWallCollision = true;

        [Tooltip("V1: A state started via AI_StartState without a _Loop function ends at once like in the engine " +
                 "(ZS_HealSelf after a fight) - NPCs walk back to their routine after giving up a chase instead of " +
                 "standing where they stopped.")]
        public bool EnableLooplessStatesEnd = true;

        [Tooltip("V1: Small missing externals: Npc_HasReadiedWeapon (G1 orc AI sees drawn weapons), " +
                 "Npc_GetGuildAttitude, Npc_IsDrawingWeapon (G2), ExitGame/ExitSession (endings: quit after the " +
                 "credits), Mdl_ApplyOverlayMDSTimed (speed potions make the VR hero faster). Only registered if the " +
                 "scripts declare them.")]
        public bool EnableQuickWinExternals = true;

        [Tooltip("V1: Documents that are used up when read (MAPSEALED - the G1 sealed letter to the fire mages) vanish " +
                 "when the reading ends (one hand lets go). Before, the sealed letter stayed in the hand.")]
        public bool EnableDocConsumeOnClose = true;

        [Tooltip("V1: Transformation scrolls: the hero becomes the monster - it follows the player without AI (walk/run/" +
                 "attack sounds), the VR camera goes down to its eyes, the hero's guild is the monster's. The casting hand " +
                 "holds an orb with a live picture of the body; its trigger transforms back. The other trigger attacks.")]
        public bool EnableVrTransformations = true;

        [Tooltip("V1: The hero's own body (current armor) is visible under the VR head - torso, legs and arms reaching " +
                 "for the VR hands (simple IK). The model's hands and head are hidden, the HVR hands stay.")]
        public bool EnableVrHeroBody = true;

        [Tooltip("V1: Particle effects of animations (*eventPFX/*eventPFXStop in the MDS): joint smoke of smoking NPCs, " +
                 "bubbles, teleport rings of scrolls, ... attached to their bone. Before they were only logged.")]
        public bool EnableAnimationPfx = true;

        [Tooltip("V1: Spells show their VISUALFX effects around the caster: invest effects on the body/hand " +
                 "(emFXInvestOrigin_S, teleport ring, transformation glow) and the cast effect (emCreateFXID of the CAST " +
                 "key: fire rain, teleport flash, transformation cloud).")]
        public bool EnableSpellBodyFx = true;

        [Tooltip("V1: Particle emission like Gothic. One-shot effects (explosions, spell hits, pickaxe sparks) emit " +
                 "ppsValue per second for ppsScaleKeys/ppsFps seconds and live until their particles are gone (they were " +
                 "/100 and destroyed after 1 s - nothing visible). Looping effects emit at least min(ppsValue, 10) per " +
                 "second (LIGHTSMOKE: 5 pps gave nothing). Sub emitters (ppsCreateEm_S: fireball sparks, fire rain " +
                 "ground) are created too.")]
        public bool EnablePfxMinimumEmission = true;

        [Tooltip("V1: NPCs holding an item in a state (joint, beer, potion, ...) play its random animations now and then " +
                 "like the engine (t_<SCHEME>_Random_1..n: taking a drag with smoke, a sip, ...).")]
        public bool EnableItemRandomAnis = true;

        [Tooltip("V1: Important dialogs are checked in their nr order like the engine (Baal Cadar: SleepSpell nr 1 " +
                 "before NoTalk nr 2 - he walked up for the sleep spell, then offered NoTalk, forever).")]
        public bool EnableImportantInfoOrder = true;

        [Tooltip("V1: Dialog distance and voice range grow with the NPC's body (measured from its edge) - G2's undead " +
                 "dragon's lines were inaudible, his head is meters away. Dialog lines play on their own AudioSource.")]
        public bool EnableDialogRangeByBodySize = true;

        [Tooltip("V1: Npc_ClearAIQueue on the hero's dialog partner keeps its queued AI_Output lines (and the current " +
                 "one) - G2's dragons cleared their queue during the dialog, only the hero's lines were heard.")]
        public bool EnableDialogKeepsOutputs = true;

        [Tooltip("V1: Dialog lines tell the hero apart by instance, not by id 0 - monsters have id 0 too, the undead " +
                 "dragon's lines went to the hero's queue and were never said.")]
        public bool EnableDialogHeroByInstance = true;

        [Tooltip("V1: A dying NPC runs ZS_Dead_Loop once after ZS_Dead, like the engine - G2 counts killed dragons " +
                 "there (undead dragon: EXTRO video, teleport, UndeadDragonIsDead).")]
        public bool EnableDeadStateLoop = true;

        [Tooltip("V1: Npc_SetTarget is the NPC's enemy (AI_Attack attacks it) and Npc_GetTarget without a target sets " +
                 "other to NULL like the engine. Berzerk victims attacked the caster instead of the NPC they picked.")]
        public bool EnableNpcTargetIsEnemy = true;

        [Tooltip("V1: AI_Attack without a drawn weapon draws the melee weapon first like the engine (berzerk victims " +
                 "fought with fists).")]
        public bool EnableAiAttackDrawsWeapon = true;

        [Tooltip("V1: AI_ContinueRoutine puts a drawn weapon away first (after berzerk an NPC kept it drawn).")]
        public bool EnableRoutineHolstersWeapon = true;

        [Tooltip("V1: Wld_DetectNpc(Ex) never returns a dead NPC like the engine (berzerk attacked a corpse).")]
        public bool EnableDetectNpcSkipsDead = true;

        [Tooltip("V1 (G1): guards near a berzerk NPC that hits someone attack it to stop it - knocked out, not killed. " +
                 "Vanilla let it rage (friendly fire between friends).")]
        public bool EnableGuardsStopBerzerk = true;

        [Tooltip("V1: Npc_SendPassivePerc reaches the NPCs around the sender like the engine (a guard's ASSESSWARN " +
                 "calls the other guards). Off: the old behavior - the sender perceived it itself.")]
        public bool EnablePassivePercBroadcast = true;

        [Tooltip("V1: NPCs spot enemies and bodies even when the hero is far away (Milten vs. an orc). " +
                 "Off: an NPC perceives nothing while the hero is outside its senses_range.")]
        public bool EnableNpcPerceptionsWithoutHero = true;

        [Tooltip("V1: PERC_ASSESSBODY - the closest sensed body (G1: monsters eat corpses, B_MM_AssessBody).")]
        public bool EnableAssessBodyPerception = true;

        [Tooltip("V1: PERC_ASSESSMURDER / PERC_ASSESSDEFEAT to the witnesses around a killed / knocked out NPC.")]
        public bool EnableMurderDefeatPerceptions = true;

        [Tooltip("V1: An NPC's started swing isn't cut short by another NPC's hit (it still takes the damage). Two " +
                 "attackers taking turns kept their victim from ever landing a blow. The hero's hits still interrupt.")]
        public bool EnableNpcSwingKeepsOnNpcHit = true;

        [Tooltip("V1: Like the engine, a human beaten in melee is knocked out unless hostile to the attacker " +
                 "(permanent attitude to the hero, guild attitude between NPCs) - then he dies. Arrows kill. " +
                 "Off: only the hero and his party knock out, NPCs kill each other.")]
        public bool EnableEngineUnconsciousRule = true;

        [Tooltip("V1: An NPC's melee hit lands at its animation's DEF_OPT_FRAME (like the engine), or at its end when " +
                 "it has none - wolves' bites never hit anything (no DEF_WINDOW).")]
        public bool EnableNpcHitAtOptimalFrame = true;

        [Tooltip("V1 (G2): NPCs with AIV_EnemyOverride (road bandits below Xardas' tower) don't pick the hero as their " +
                 "next target after a fight (Npc_GetNextTarget) - they left him alone before it.")]
        public bool EnableEnemyOverrideNextTarget = true;

        [Tooltip("V1: The hero's summons and NPCs treat each other like that NPC treats the hero - enemies fight them, " +
                 "everyone else ignores them. Vanilla G2 makes humans hostile to summons (guards attack them).")]
        public bool EnableNpcsIgnoreHeroSummons = true;

        [Tooltip("V1: Crouching (real or the crouch button) is sneaking for the scripts (BS_SNEAK): NPCs that see " +
                 "you ask why you sneak, footsteps make no noise.")]
        public bool EnableHeroSneakBodyState = true;

        [Tooltip("V1: PERC_ASSESSUSEMOB when the hero grabs a mob (chest lid, door, ...) or picks its lock - owners " +
                 "and their guild react (\"Hands off!\", attack).")]
        public bool EnableUseMobPerception = true;

        [Tooltip("V1: Every footstep of the hero is a quiet sound (PERC_ASSESSQUIETSOUND) unless he sneaks - like " +
                 "OpenGothic. G1's scripts only react to noises of items, G2 differs.")]
        public bool EnableFootstepQuietSound = true;

        [Tooltip("V1: Portal rooms (huts) from the world's BSP sectors: PERC_ASSESSENTERROOM when the hero walks in or " +
                 "out, Wld_GetPlayerPortalGuild knows whose room it is (Wld_AssignRoomToGuild).")]
        public bool EnableEnterRoomPerception = true;

        [Tooltip("V1: A position's room (hut) is the room floor under the feet, like OpenGothic (the sector of the " +
                 "floor polygon the ground ray hits). Off: any room polygon around the body - walls and the roof's " +
                 "overhang counted too, NPCs said 'get out' a step before the door.")]
        public bool EnableFloorRoomLookup = true;

        [Tooltip("V1: Trading in VR - a dialog choice with trade != 0 opens a counter between the hero and the trader: " +
                 "his goods, your offer, his offer, the price difference in gold (G2) / ore (G1), accept / close.")]
        public bool EnableVrTrade = true;

        [Tooltip("V1: An invisible table under the trader's goods backpack - put down, it lands there instead of on " +
                 "the floor (no bending down). Only that backpack touches it; hands, items and NPCs pass through.")]
        public bool EnableTradeGoodsTable = true;

        [Tooltip("V1: Spells invest mana at their own C_Spell.time_per_mana like the engine (G2 charge spells: 30 ms " +
                 "per mana, 40 per level). Off: one mana every 0.5 s for all spells.")]
        public bool EnableSpellTimePerMana = true;

        [Tooltip("Seconds an instant spell (G2 summons, fire rain, transformations, heal...) glows in the hand before it " +
                 "goes off - to enjoy the effects. 0 = at once like the engine. Throwable spells aren't affected.")]
        public float MinSpellCastSeconds = 2f;

        [Tooltip("V1: Fire (trigger) before a spell was cast: like the engine Spell_ProcessMana_Release decides - charge " +
                 "spells go off at their level, the rest (fire rain, summons, ...) is stopped. Before every spell went off.")]
        public bool EnableSpellReleaseCheck = true;

        [Tooltip("V1: A knocked out hero can't walk but can still turn with the stick (snap/smooth turn) - seated " +
                 "players couldn't look around.")]
        public bool EnableKnockoutTurning = true;

        [Tooltip("V1: NPCs spawned mid-game (Marvin, a script's Wld_InsertNpc after the world loaded) are saved and come " +
                 "back on load and when returning to a world. Summons aren't. Needs EnableSaveLoadSystem.")]
        public bool EnableSpawnedNpcSaves = true;

        [Tooltip("V1: Every visited world keeps its NPCs (position, HP, dead, spawned ones) - coming back to a world in the " +
                 "same game or after loading. Saved for all worlds. Needs EnableSaveLoadSystem.")]
        public bool EnableWorldRevisitState = true;

        [Tooltip("V1: Music zones like the engine - picked from the player's position every 2 s (current zone stays, else " +
                 "the last zone containing us), theme fallbacks NGT->DAY, THR->FGT->STD, default zone; night from the " +
                 "game time, fight music ends with the fight. G2's pirate camp kept the bandit music before.")]
        public bool EnableMusicZonesByPosition = true;

        [Tooltip("V1: Loot backpacks of dead NPCs and of containers without a lid (mummies) get a green X above the right " +
                 "page arrow - it moves everything into the hero's inventory.")]
        public bool EnableLootTakeAll = true;

        [Tooltip("V1: Particle controllers show their particle visual like the engine (pfxName is mostly an empty Spacer " +
                 "leftover) - G2's active teleport platforms and cauldrons showed nothing. Needs EnableParticleEffects.")]
        public bool EnablePfxControllerVisuals = true;

        [Tooltip("V1: A knocked out NPC falls down like in the engine (T_Stand_2_Wounded -> S_Wounded) - the scripts only " +
                 "drop a third of the tough guys (Mdl_ApplyRandomAni), the rest stood or T-posed while unconscious.")]
        public bool EnableKnockoutFallAnimation = true;

        [Tooltip("V1: The hero's summons act for him - their knockouts/kills count as his (Buster's duel, XP), witnesses " +
                 "blame the hero for their hits, and they stop hitting a downed NPC like the scripts.")]
        public bool EnableSummonActsForMaster = true;

        [Tooltip("V1: Crafting mobs move their useWithItem like the engine's animation events (HUMANS.MDS ItemInsert/" +
                 "Exchange/Remove/Destroy) - G2's forge turns raw steel into hot steel, the anvil takes it for smithing.")]
        public bool EnableMobItemEvents = true;

        [Tooltip("V1: NPCs at mobs whose animations have no FRONT/BACK variant (saw, repair spot, roast spit) play the plain " +
                 "ones like OpenGothic - they stood next to the mob before.")]
        public bool EnableMobAnimationWithoutPosition = true;

        [Tooltip("V1: A mob that shows a page with Doc_Show (G2 bookstands) shows it above the mob like a held document - " +
                 "touch again or walk away to close. Before, touching a bookstand showed nothing.")]
        public bool EnableMobDocuments = true;

        [Tooltip("V1: Dialogs (C_Info) are initialized on every game's fresh VM, not only on the boot VM - what scripts " +
                 "write into them during the game (the anvil's weapon names, ambient infos' npc) was lost before.")]
        public bool EnableDialogsPerGameVm = true;

        [Tooltip("V1: Meshes on texture arrays get one material per texture array group, matching their sub meshes - " +
                 "palm leaves and fishing nets (G2 addon, mods) showed stone textures without alpha before.")]
        public bool EnableTextureArraySubmeshMaterials = true;

        [Tooltip("V1: Like the engine, only polygons of materials without noCollDet collide - trees block at the trunk, " +
                 "not the leaves, and bushes on the level mesh can be walked through. Before, one leaf material " +
                 "left a whole tree without a collider and level mesh bushes blocked.")]
        public bool EnableMaterialCollisionFilter = true;

        [Tooltip("V1: A fireplace without a fire tree keeps its child VOBs - they were dropped (Mroczne Tajemnice's " +
                 "smithy on the smugglers' beach).")]
        public bool EnableFireVobChildren = true;

        [Tooltip("V1: NPC/monster combos are as long as the attack animation's DEF_WINDOW pairs like the engine, then the " +
                 "attack plays to its end - a lurker bit 4 times a second without end before.")]
        public bool EnableNpcComboLimit = true;

        [Tooltip("V1: Summon spells have no target like the engine (TARGET_COLLECT_NONE) - aimed at an NPC they hit him " +
                 "and turned him hostile before.")]
        public bool EnableSummonSpellWithoutTarget = true;

        [Tooltip("V1: Only SPELL_BAD spells deal damage like the engine - sleep, charm, ... (SPELL_NEUTRAL/GOOD) hurt their " +
                 "target before (damage_per_level = 1 from C_Spell_Proto).")]
        public bool EnableOnlyBadSpellsDamage = true;

        [Tooltip("V1: Thrown spells fly out of the hand like a ball - direction and speed from the swing, a weak throw arcs, " +
                 "no swing drops it; only a real throw homes on targets. Off (and in the simulator): towards the camera's view.")]
        public bool EnableSpellThrowPhysics = true;

        [Tooltip("Speed of thrown spells relative to the spell's own speed (VISUALFX emTrjEaseVel) - 1 = engine speed; " +
                 "1.25 felt right in VR (a full swing at 1 was a little weak).")]
        [Range(0.1f, 2f)]
        public float SpellThrowSpeedMultiplier = 1.25f;

        [Tooltip("Hand speed (m/s) of a full spell throw (EnableSpellThrowPhysics). Slower swings throw weaker and arc " +
                 "down - at 3 m/s a light flick was already a strong throw.")]
        [Range(1f, 12f)]
        public float SpellThrowFullSwingSpeed = 6f;

        [Tooltip("V1: Guides/followers (ZS_GuidePC, ZS_Guide_Player, ...) keep the engine's senses (smell all around their " +
                 "senses range) - for the hero and for enemies: with the quiet senses they never waited and only noticed " +
                 "monsters once bitten.")]
        public bool EnableEscortSensesHero = true;

        [Tooltip("V1: A hit interrupts an NPC's walking like the engine's stumble - the damage reaction queued behind a " +
                 "guide's long walk and never ran (Baal Parvez froze under a wolf's bites).")]
        public bool EnableHitStopsWalking = true;

        [Tooltip("V1: Waypoint paths use Dijkstra with a binary heap and stop at the destination - the old search was " +
                 "quadratic on G1's waynet and froze the game whenever a guide resumed its walk.")]
        public bool EnableFastWaypointPath = true;

        [Tooltip("V1: Fire trees (oCMobFire vobTree) place their nested VOBs relative to the tree's root - the smoke under a " +
                 "fire's light kept the fire world's absolute position and piled up hundreds of meters away (Old Camp).")]
        public bool EnableFireTreeRelativePositions = true;

        [Tooltip("V1: An NPC's own attitude (Npc_SetAttitude) only counts towards the hero like the engine - between two " +
                 "NPCs the guild table decides. A guide friendly to the hero was friendly to every monster too.")]
        public bool EnablePersonalAttitudeOnlyForHero = true;

        [Tooltip("V1: Fireplaces and torches (oCMobFire) burn by the scripts' mob routine (G1: lit 22:00-05:00), always " +
                 "indoors (houses, mines), can be lit/put out by touching them and show no effects when far away. A " +
                 "fire that is out gives no light, smoke nor sound.")]
        public bool EnableFireStates = true;

        [Tooltip("V1: Rain falls around the player instead of at the world's origin.")]
        public bool EnableRainFollowsPlayer = true;

        [Tooltip("V1: A magically frozen NPC (ZS_MagicFreeze sets BS_UNCONSCIOUS) isn't down like in the scripts - monsters " +
                 "see and keep attacking it and a hit doesn't kill it at once (Mroczne Tajemnice's crawler web).")]
        public bool EnableFrozenIsNotDown = true;

        [Tooltip("V1: An NPC's spell is visible - a flying spell (fire ball, ice block, crawler web) flies to its target " +
                 "as its PFX and hits on arrival, the others show their cast and hit effects. Off: an invisible instant hit.")]
        public bool EnableNpcSpellFx = true;

        [Tooltip("How much an NPC's flying spell follows its target (0 = flies to where the target was at the cast, 1 = " +
                 "always hits). At 0.2 a step aside dodges it - fully homing nobody could dodge a crawler's web.")]
        [Range(0f, 1f)]
        public float NpcSpellHoming = 0.2f;

        [Tooltip("V1: Sleep and freeze spells (also Mroczne Tajemnice's crawler web) work on the hero - can't move for " +
                 "SPL_TIME_SLEEP/FREEZE, damage wakes up, and the caster's scripts see the state (no endless re-casting).")]
        public bool EnableHeroSpellStates = true;

        [Tooltip("V1: Dialog subtitles (OU.BIN/OU.CSL) packed in a mod's VDF/.mod are read from the archives - no manual " +
                 "VDFS extraction to VDFS/_WORK/DATA/scripts/content/CUTSCENE needed.")]
        public bool EnableSubtitlesFromVfs = true;

        [Tooltip("V1: Light mapped world polygons (caves, mines, houses) show Gothic's lightmaps like the engine - before " +
                 "they had only their dim/black vertex light. Static lights then light VOBs and NPCs only.")]
        public bool EnableWorldLightmaps = true;

        [Tooltip("Brightness of the world's lightmaps (1 = as compiled by Gothic).")]
        [Range(0.25f, 4f)]
        public float WorldLightmapBrightness = 1f;

        [Tooltip("V1: Indoor worlds (BSP indoor: Old Mine, Free Mine, Orc Temple, mod caves) get no sun and no fog - " +
                 "dark like the night, lit by their lights and lightmaps only.")]
        public bool EnableIndoorWorldDarkness = true;

        [Tooltip("V1: Mroczne Tajemnice's web crawler casts from afar, but bites (its melee fight table) when its target is " +
                 "frozen or right in front of it - it kept freezing the hero again and again (a freeze lock).")]
        public bool EnableSpellMonsterMelee = true;

        [Tooltip("V1: Non-static (dynamic) VOB lights that are switched on shine like the engine's - they were skipped, " +
                 "Mroczne Tajemnice's abandoned mine (lit only by them) was pitch black.")]
        public bool EnableDynamicVobLights = true;

        [Tooltip("V1: VOBs in light mapped areas (caves, mines, houses) take the lightmap light of the floor below them - " +
                 "stairs and planks shone like the day next to the dark walls.")]
        public bool EnableVobWorldLight = true;

        [Tooltip("V1: Meshes are cached separately for texture array and single texture use - a mod item with a world " +
                 "object's visual (Mroczne Tajemnice's planks) handed its mesh to the world VOBs: stone textures, black parts.")]
        public bool EnableMeshCachePerTextureMode = true;

        [Tooltip("V1: Textures above the texture array size without enough mips (Mroczne Tajemnice's 1024x1024 ones) are " +
                 "downscaled with a generated mip chain, and uv scale/mip limit follow what the slice holds - their slices " +
                 "stayed black/garbage and big textures tiled twice as often.")]
        public bool EnableTextureArrayMipFix = true;

        [Tooltip("How much the world's lights color the VR hands (0..1, 1 = fully lit like before). The rest is their own " +
                 "skin color - fully lit they turned purple, white or orange.")]
        [Range(0f, 1f)]
        public float HandLightInfluence = 0.3f;

        [Tooltip("Skin color of the VR hands (multiplies the hand texture) - like the hero body's arm.")]
        public Color HandSkinTint = new(0.78f, 0.58f, 0.48f, 1f);

        [Tooltip("V1: Loading a game (a new GOTHIC VM) resets the cached item/SVM/spell instances of the old VM - " +
                 "Wld_DetectItem handed scripts a dead item and the VM crashed natively (Mroczne Tajemnice's abandoned mine).")]
        public bool EnableVmCacheResetOnLoad = true;

        [Tooltip("V1: A world ZenKit can't load is retried with its materials archive header fixed - ZenKit skips the " +
                 "material count's first byte when it's a newline (266 materials: Mroczne Tajemnice's ANCIENTTEMPLE.ZEN).")]
        public bool EnableZenMaterialCountFix = true;

        [Tooltip("V1: A mod's world whose mesh chunks have a wrong stored length gets it fixed before ZenKit loads it - " +
                 "ZenKit hung on it (Renovation 2026's DRAGONISLAND.ZEN: the materials chunk leaves out its last " +
                 "material), PreCaching froze.")]
        public bool EnableZenChunkLengthFix = true;

        [Tooltip("V1: Items looked up by their symbol index are initialized from the symbol, not by name - ZenKit matches " +
                 "names case-insensitively only for ASCII: Renovation's ITAR_ŃASTOFFS wasn't found and the world stopped loading " +
                 "(Mdl_SetVisualBody of Alrik).")]
        public bool EnableItemDataBySymbol = true;

        [Tooltip("Opacity of the weapon swing trail (1 = Gothic's ZWEAPONTRAIL alpha). In VR the full trail right in front " +
                 "of the eyes on every swing was too much.")]
        [Range(0f, 1f)]
        public float WeaponTrailOpacity = 0.5f;

        [Tooltip("V1: NPCs react to the hero's swing like to an attack animation's prehit (OpenGothic FightAlgo): in " +
                 "his range and facing each other they pick FA_ENEMY_PREHIT / FA_ENEMY_STORMPREHIT (parry, jump back, " +
                 "strafe). A wait or strafe is cut short by the swing so they can react in time.")]
        public bool EnableNpcPrehitReaction = true;

        [Tooltip("V1 (balance): the hero's swing also stops an NPC's own swing before its hit frame and drops its queued " +
                 "combo swings - tougher NPCs with long combos never parried or jumped back otherwise.")]
        public bool EnableNpcPrehitCancelsAttack = true;

        [Tooltip("V1: A parrying NPC facing the hero (30 degrees) or one jumping back (90 degrees) takes no damage from his " +
                 "hit - weapons clash (CS_IAI_ sound, CPFX_IAI_ sparks). Before, a parry was only an animation.")]
        public bool EnableNpcParadeBlocksHits = true;

        [Tooltip("V1: AI_TurnAway turns the NPC's back to the other one (ZS_TurnAway of a beaten guard) - it was a stub.")]
        public bool EnableAiTurnAway = true;

        [Tooltip("V1: AI_WhirlAround plays the startled jump around (t_surprise_CW/CCW) like the engine - G1's " +
                 "ZS_AssessFighter uses it when the hero draws a weapon/rune behind an NPC - and Npc_ClearAIQueue " +
                 "keeps it (B_FullStop before drawing a sword dropped it). Before: a plain turn.")]
        public bool EnableAiWhirlAround = true;

        [Tooltip("V1 (opinion): Guards (G1 ZS_Guard, ZS_GuardPalisade) don't turn to the hero sneaking up behind them - " +
                 "their routine turned to him within 8 m by distance alone (also in Gothic), sneaking up was pointless. " +
                 "Seeing him or hearing his steps (not sneaking) still works.")]
        public bool EnableSneakBehindGuards = true;

        [Tooltip("V1 (opinion): Sprinting while sneaking (crouched, BS_SNEAK) is half as fast - a full sprint behind " +
                 "an NPC's back looked silly.")]
        public bool EnableSneakSlowSprint = true;

        [Tooltip("V1: G1 pickpocketing in VR (the engine's steal window; G2 does it in dialogs). Sneaking with the " +
                 "pickpocket talent, a purse shows on the right hip of humans who can't see you - grip it and their " +
                 "pockets open as a backpack in that hand. Every item taken can get you caught (the talent value: " +
                 "50 % trained, 10 % master): the backpack is gone, the victim gets PERC_CATCHTHIEF, witnesses " +
                 "PERC_ASSESSTHEFT.")]
        public bool EnableVrPickpocket = true;

        [Tooltip("V1: Blocking in VR like Gothic's parry: both hands on a melee weapon (one- or two-handed) and both " +
                 "triggers held. Hits of humans (fists too) and of monsters with a drawn weapon (orcs, skeletons) from " +
                 "the front (45 degrees around the head's direction) do no damage, the weapons clash. Animals, spells, " +
                 "projectiles and hits from the side or back go through; while blocking the hero's own hits don't count.")]
        public bool EnableVrHeroBlock = true;

        [Tooltip("V1: Melee damage like the engine (OpenGothic DamageCalculator): per damage type strength + damage - " +
                 "protection, G1 critical hits by the 1H/2H talent value (x2), G2 near misses by the hit chance, " +
                 "monsters with their own damage type (wolves bite DAM_EDGE, not blunt). The hero's VR weapon counts " +
                 "- it hit with his strength alone. Spells use their own damage type instead of fire.")]
        public bool EnableEngineMeleeDamage = true;

        [Tooltip("V1 (opinion): The hero can swing a weapon without its requirements (e.g. 50 strength) but every hit " +
                 "does only 1 damage - in Gothic he couldn't equip it at all. Bows and crossbows without them don't " +
                 "shoot. Grabbing such a weapon shows the engine's message (G_CanNotUse: \"missing ... points\").")]
        public bool EnableWeaponRequirementCap = true;

        [Tooltip("V1: An NPC's item function (on_equip) can't take the hero's armor off - Mroczne Tajemnice's helmet " +
                 "and armor functions check and undress `hero` instead of `self`, NPCs created after the hero undressed " +
                 "PC_Rockefeller at the start of a new game.")]
        public bool EnableNpcItemsSpareHeroArmor = true;

        [Tooltip("V1: The on-screen keyboard (save names) has a Clear key between backspace and OK - it empties the " +
                 "text at once.")]
        public bool EnableKeyboardClearButton = true;

        [Tooltip("V1: An empty hand at an armor, amulet, ring or belt in the hero's backpack and a trigger click (quick, " +
                 "without moving) puts it on or takes it off - like a click in the spell ring.")]
        public bool EnableBackpackEquipToggle = true;

        [Tooltip("V1 (opinion): An NPC chasing the hero to proclaim (ZS_ProclaimAndPunish: \"Now wait...\") stops within " +
                 "12 m and proclaims, and gives the chase up after HAI_TIME_PURSUIT - its loop only looked again once " +
                 "it caught the hero, who ran away right after his attack (Lukis chased forever).")]
        public bool EnableProclaimFromAfar = true;

        [Tooltip("V1: AI_UseItem(self, item) with the global `item` (G1 ZS_HealSelf eats food found by " +
                 "Npc_GetInvItemBySlot) uses the item in it - it was the symbol ITEM, the use never finished and the NPC " +
                 "froze after a fight until something woke him (talking, a drawn weapon).")]
        public bool EnableUseItemGlobalItem = true;

        [Tooltip("V1: Immersion option \"Ranged knockout\": off (like OpenGothic) a lethal arrow, bolt or spell kills a " +
                 "human, on it knocks him out and the next one (or a sword) finishes him off. Area spells (fire rain) hit " +
                 "the knocked out too. Before: ranged and magic always left humans and the hero at 1 HP.")]
        public bool EnableRangedKnockoutOption = true;

        [Tooltip("The \"Ranged knockout\" option's value when Gothic.ini has none yet.")]
        public bool RangedKnockoutByDefault;

        [Tooltip("V1: Blood only for a melee hit that landed - a parried hit or one on the blocking hero showed sparks and " +
                 "blood together (OpenGothic: the hit effect only plays when the hit isn't blocked).")]
        public bool EnableBloodOnlyOnHit = true;

        [Tooltip("V1: A block (EnableVrHeroBlock) stops only a hit whose weapon touched the hero's held weapon - held " +
                 "above the NPC's head it blocked everything from the front. A hit reaching the body first waits 0.5 s " +
                 "for the touch.")]
        public bool EnableBlockWeaponContact = true;

        [Tooltip("V1: The first hand on a weapon (a sword or bow from a holster or the backpack, force grabbed) closes " +
                 "around its grip - the item's origin, where Gothic's NPCs hold it - not the nearest part of the blade.")]
        public bool EnableWeaponGripGrab = true;

        [Tooltip("V1: A backpack (the hero's, an NPC's loot backpack) collides with the world and vobs only - items " +
                 "(a held potion, a sword, items lying around) pass through it instead of pushing it.")]
        public bool EnableBackpackItemPassThrough = true;

        [Tooltip("V1: The favorites ring (EnableVrSpellRing) also holds the equipped melee weapon and bow/crossbow - at " +
                 "most 8 spells + 2 weapons. Grip takes the weapon into the hand, a weapon held into the empty slot (or " +
                 "onto the equipped one of its kind) is equipped, a trigger click unequips.")]
        public bool EnableRingWeaponSlots = true;

        [Tooltip("V1: The favorites ring (EnableVrSpellRing) also holds up to 3 favorite items (a torch, a key, potions, " +
                 "food) and the money (G1 ore, G2 gold) - let go at its empty slot to put one in, grabbed out one at " +
                 "a time (grip held at a stack takes more and more), a trigger click takes one off the ring. Saved in " +
                 "UNITYSAVE.json.")]
        public bool EnableRingItemSlots = true;

        [Tooltip("V1: EquipItem gives the NPC the item if he doesn't have it (externals.d: \"gibt dem Nsc direkt das " +
                 "angegebene Item an den Guertel\") - orc shamans' staffs, Mroczne Tajemnice's Rockefeller's warhammer " +
                 "were equipped without being in the inventory.")]
        public bool EnableEquipItemCreatesItem = true;

        [Tooltip("V1: A weapon taken out of the favorites ring stays its equipped weapon only if it goes back into the " +
                 "backpack (opened or over the shoulder) - dropped or put into another quick slot it comes off.")]
        public bool EnableRingWeaponLeavesOnDrop = true;

        [Tooltip("V1: A game profile whose ModIni is \"*\" asks which of the mod's game inis to " +
                 "start - found in its System folder by their layout ([INFO] title, [FILES] game/vdf), e.g. Mroczne " +
                 "Tajemnice's DM_E/DM_N/DM_T; \"A.INI;B.INI\" offers those. Saves stay per ini like in Gothic.")]
        public bool EnableGameProfileIniChoice = true;

        [Tooltip("V1: Immersion option \"Single-use scrolls\" (ini scrollsSingleUse, on by default): a scroll's cast uses " +
                 "it up like in Gothic - held stacks and scrolls from the favorites ring. Scrolls were cast forever.")]
        public bool EnableScrollUseOption = true;

        [Tooltip("V1: Spell effects light up like in Gothic (OpenGothic Effect::setupLight): a VISUALFX lightPresetName " +
                 "from LIGHTPRESETS.ZEN with its color/range animation - summoning glows red around the caster while " +
                 "charging (the key's lightRange, growing per level) and flashes when cast, fire rain's glow and rain " +
                 "light the area. Lights the world too (pooled stationary lights).")]
        public bool EnableVisualFxLights = true;

        [Tooltip("V1: FX_EarthQuake (summoning's 3rd charge level, fire rain while charging) plays its sound and rumbles " +
                 "both VR controllers for its seconds within its radius - it was skipped (no camera shake in VR).")]
        public bool EnableVisualFxEarthquake = true;

        [Tooltip("V1: A spell's charging effect around the body sits on the ground: Gothic places it at BIP01 with a " +
                 "downward offset (summoning's fire ring: -120 cm) - from our fixed body height it ended under the " +
                 "ground, only a few sparks showed.")]
        public bool EnableBodyFxAtGround = true;

        [Tooltip("V1: One log file per session (a build's run, an Editor play mode too): Logs/Gothic-Unity_<date>_<time>" +
                 ".log.txt in the persistent data folder, every line starting with the local time - stream VODs can be " +
                 "matched to it (chapters). The newest 30 are kept. Off: builds write Gothic-Unity.log.txt, the Editor " +
                 "no file.")]
        public bool EnableSessionLogFiles = true;

        [Tooltip("V1: Magic circles like in Gothic - a rune of a higher circle than the hero's (mag_circle vs " +
                 "NPC_TALENT_MAGE) can't be equipped (favorites ring) nor cast; the scripts' G_CanNotCast says how many " +
                 "circles are missing. Scrolls have no circle.")]
        public bool EnableRuneCircleRequirement = true;

        [Tooltip("V1: Windfist and Stormfist throw their victims away (DAM_FLY, OpenGothic accessDamFly) - they fly back, " +
                 "land lying and get up; thrown off a ledge, the fall hurts. Not the hero.")]
        public bool EnableWindfistThrow = true;

        [Tooltip("V1: While the virtual keyboard is open, the UI lasers are drawn over it - they ended under its canvas " +
                 "(sorting order 100), aiming at a key was guesswork.")]
        public bool EnableKeyboardPointerOnTop = true;

        [Tooltip("V1: A spell taken from the favorites ring into a hand that already has a ring spell replaces it - the " +
                 "old one stayed with its own caster: both charged and cast at once, a waiting transformation cast " +
                 "again (and used up a scroll) right after transforming back.")]
        public bool EnableRingSpellReplacesHandSpell = true;

        [Tooltip("V1: A transformation that can't happen (the monster has no visual - Mroczne Tajemnice's lurker scroll " +
                 "turns into BLACKDRAGON) keeps its scroll and gives the mana back; the never-built monster leaves the " +
                 "NPC culling (it threw building it).")]
        public bool EnableTransformCancelKeepsScroll = true;

        [Tooltip("V1: Transformed, the player moves as fast as the monster runs (its run loop's root motion against the " +
                 "human one, x0.3-x3) - a wolf runs faster than a human, a meatbug slower.")]
        public bool EnableTransformMonsterSpeed = true;

        [Tooltip("V1: A transformation scroll whose script passes a monster without a visual turns into the monster the " +
                 "spell is named after (SPL_TRF_LURKER -> LURKER) - Mroczne Tajemnice's lurker scroll passes BLACKDRAGON.")]
        public bool EnableTransformFallbackInstance = true;

        [Tooltip("V1: A spell's effect in the hand and in the favorites ring keeps Gothic's own motion and emission - " +
                 "every particle stood still at 50/s (the transformation's lightning didn't show, summoning glowed a tenth). " +
                 "In the ring the whole effect is scaled down to fit its slot (a fireball's flames grow to 64 cm).")]
        public bool EnableSpellEffectGothicMotion = true;

        [Tooltip("V1: Particles with visOrientation VELO face the camera with their length along the flight (OpenGothic) " +
                 "- they turned their face into the flight direction and were thin lines or invisible from the side " +
                 "(transformation glow, sparks).")]
        public bool EnablePfxVeloFacesCamera = true;

        [Tooltip("V1: A particle texture the game doesn't have draws nothing (only the emitter's trails) - plain white " +
                 "squares showed (G1 transformation: WAVEOFINSANITY_A0.TGA).")]
        public bool EnablePfxMissingTextureHidden = true;

        [Tooltip("V1: The favorites ring opens around the player at eye level, 2.2 times the size (radius ~0.6 m), the " +
                 "first slot in front - the slot the head looks at is the selected one (its name always shown); the " +
                 "trigger + hand still turns it. Off: a small ring in front of the chest.")]
        public bool EnableRingAroundPlayer = true;

        [Tooltip("V1: In the favorites ring the front spell's effect doesn't hide its rune/scroll: the rune stays half " +
                 "transparent behind it (some effects are huge), a hand at it shows the rune whole and the effect half.")]
        public bool EnableRingSpellPreviewAlpha = true;

        [Tooltip("V1: The favorites ring around the player is a carousel - the hand turns it around the player (the slot " +
                 "under the hand follows it from any side; sideways in the ring's space was mirrored behind him), let go " +
                 "it spins on and slows down, a trigger press stops it. Off: it snaps to the nearest slot.")]
        public bool EnableRingFreeSpin = true;

        [Tooltip("V1: While the favorites ring is open the right stick up/down (simulator: mouse wheel) makes it bigger " +
                 "or smaller (x0.5-x2) - like the menu's distance. Kept for the session.")]
        public bool EnableRingZoom = true;

        [Tooltip("V1: Spells are cast like Gothic's cast key: press = aim and charge at once, hold = charge, let go " +
                 "early = Spell_ProcessMana_Release decides (fireball goes off, the rest stops). Off: one press to aim, " +
                 "a second to charge, a third to fire.")]
        public bool EnableSpellHoldToCast = true;

        [Tooltip("V1: G1 chain lightning like the engine - the NPC in front of the eyes is zapped (ZS_Zapped hurts him " +
                 "every second), every invest level it jumps on to the nearest NPC, with nobody left it zaps the hero. " +
                 "The release frees them (PERC_ASSESSSTOPMAGIC). Before: one hit on everyone around.")]
        public bool EnableVrChainLightning = true;

        [Tooltip("V1: A particle trail is as wide as the PFX's trlWidth (cm) - Unity multiplied it by the particle's size, " +
                 "the transformation's lightning trails were 0.2 mm (2 cm x 1 cm particles).")]
        public bool EnablePfxTrailAbsoluteWidth = true;

        [Tooltip("V1: A script taking an NPC's last hit points (Npc_ChangeAttribute) kills him like the engine - G1 " +
                 "summons lose 1 HP every few seconds and stood on at 0 until hit once more. With the scripts' other as " +
                 "the attacker it counts as his (death or Ranged knockout); a summon weakening by itself gives no XP.")]
        public bool EnableScriptHitPointsDeath = true;

        [Tooltip("V1: ZS_Dead gets the real killer as other like the engine - XP only for the hero's, his summons' and " +
                 "his party's kills (Mordrag leading him); every death anywhere gave XP. Immersion option \"XP for every " +
                 "death\" (ini xpForAnyKill, off by default) brings that back as a cheat.")]
        public bool EnableKillerXpOnly = true;

        [Tooltip("V1: G1 critical hits for bows and crossbows (OpenGothic): the bow/crossbow talent value is the chance, " +
                 "the damage is multiplied by DAM_CRITICAL_MULTIPLIER. G2 has none (its hit chance is the talent).")]
        public bool EnableRangedCriticalHits = true;

        [Tooltip("V1 (Mroczne Tajemnice): critical hits count the weapon percent its teachers raise (B_GiveprocentSkill " +
                 "keeps 1H/2H/bow/crossbow in the SNEAK/REGENERATE/FIREMASTER/ACROBAT values - what its status menu " +
                 "shows as the critical hit chance); the higher of that and the weapon talent's value. In the engine " +
                 "only the weapon talent counted - trained heroes never hit critically.")]
        public bool EnableModPercentTalents = true;

        [Tooltip("V1: A teacher's choices stay complete - like the engine the chosen choice leaves the list before its " +
                 "function runs; the function clears the choices and adds \"+5\"/\"+1\" again, removing the chosen one " +
                 "afterwards took the new \"+5\" away.")]
        public bool EnableChoiceRemovedBeforeCall = true;

        [Tooltip("V1: The game profile menu's titles shrink to fit their button (16-34 pt) - a mod ini's long title " +
                 "wrapped into three lines over the next button.")]
        public bool EnableProfileMenuTextFit = true;

        [Tooltip("V1: Crouching is sneaking (BS_SNEAK) only with the sneak talent like the engine (OpenGothic " +
                 "Npc::canSneak) - without it the hero just walks: guards turn to him, NPCs notice him coming.")]
        public bool EnableSneakNeedsTalent = true;

        [Tooltip("V1: Sounds echo in mines, caves and temples - under a ceiling of the world mesh a reverb zone at the " +
                 "listener (cave, or stone corridor when the walls are near). Gothic's zCZoneReverb isn't read.")]
        public bool EnableEnvironmentReverb = true;

        [Tooltip("V1: Footsteps - the animations' ground sounds (*eventSFXGrnd, walk/run loops of NPCs, monsters and the " +
                 "hero's body) play with the ground's suffix like the engine (RUN_EARTH, RUN_STONE, RUN_WOOD, " +
                 "RUN_WATER); the material is guessed (wading, a VOB below, under a ceiling). They were skipped.")]
        public bool EnableFootstepSounds = true;

        [Tooltip("V1: A step on a texture whose material group is UNDEF (G1's world: 2246 of 2263 materials - the engine " +
                 "played stone everywhere) guesses the ground from the texture's name: grass/earth/moss = earth, wood/" +
                 "plank/barrel = wood, sand, metal, stone/rock/wall = stone.")]
        public bool EnableGroundTextureGuess = true;

        [Tooltip("V1: The VR hero body's animations sound like an NPC's - its steps (*eventSFXGrnd, by the ground below) " +
                 "and swim strokes (the crawl once you pick up speed: Swim, SwimWaves) come from its walk/run/swim " +
                 "animations. Off (or without the animated body): a step every stride walked.")]
        public bool EnableHeroBodySounds = true;

        [Tooltip("V1: The VR hero body (and a transformation's monster) looks at the water itself - deep water is " +
                 "swimming, any water below is no fall: drifting at the surface played the fall animation (the swim " +
                 "state flickered while bobbing; the monster's water ray started under the surface).")]
        public bool EnableHeroWaterBody = true;

        [Tooltip("V1: Swimming sounds by how you swim - drifting the waves (SwimWaves), slowly the strokes (Swim), the " +
                 "crawl faster strokes, backwards SwimBack.")]
        public bool EnableHeroSwimSounds = true;

        [Tooltip("V1: Transformed you move at the monster's own speeds - its run loop's root motion (Gothic's default " +
                 "walk mode: a wolf 4.6 m/s), sprinting half again, a light stick its walk. Off: the VR speeds scaled by " +
                 "monster run / human run (a wolf moved 2.1 m/s).")]
        public bool EnableTransformAbsoluteSpeed = true;

        [Tooltip("V1: Transformed, back on land the monster's speeds are set again - leaving the water restored the " +
                 "human ones (a meatbug too fast, a shadowbeast too slow).")]
        public bool EnableTransformLandSpeedKept = true;

        [Tooltip("V1: A rune or scroll is cast with the trigger of the hand holding it - it was always the right " +
                 "trigger: a scroll in the left hand took both hands.")]
        public bool EnableCastWithHoldingHand = true;

        [Tooltip("V1 (VR only): Charged spells (fireball, fire storm, fire rain, wind/storm fist, thunderball, G2's " +
                 "charged ones) grow only while both hands are together (30 cm) - a kamehameha. Apart, the charge waits.")]
        public bool EnableTwoHandSpellCharge = true;

        [Tooltip("V1: Npc_GetActiveSpellIsScroll is true for a held scroll - G2 lets a scroll cast for SPL_Cost_Scroll " +
                 "mana, a rune needs the spell's full cost; scrolls counted as runes and a new hero's fireball scroll " +
                 "stopped at once.")]
        public bool EnableActiveSpellIsScroll = true;

        [Tooltip("V1: Wld_InsertItem with an unknown item or spawn point logs a warning and skips it (like OpenGothic) - " +
                 "the exception aborted G2's STARTUP_OLDWORLD, the Valley of Mines stayed half empty.")]
        public bool EnableWldInsertItemSkipMissing = true;

        [Tooltip("V1: An oCMobDoor vob whose visual isn't a DOOR (G2's Old Camp beds) is an AI_UseMob mob like any " +
                 "other - Parcival and the paladins slept standing next to their beds.")]
        public bool EnableMobDoorNonDoorVisuals = true;

        [Tooltip("V1: An empty VR hand touching a locked door/chest makes the hero say why it doesn't open, like the " +
                 "engine ($KEYMISSING, $PICKLOCKMISSING, $NEVEROPEN).")]
        public bool EnableLockedTouchComment = true;

        [Tooltip("V1: The backpack returns to its shoulder when it fell through the world, got deactivated or lies " +
                 "30 m away and no hand holds it - it was gone for good.")]
        public bool EnableLinkedSocketReturn = true;

        [Tooltip("V1: While the backpack is on the shoulder, its hand grab volumes are this many times larger - it " +
                 "had to be grabbed very precisely. 1 = off.")]
        public float BackpackShoulderGrabScale = 2f;

        [Tooltip("V1: The Marvin panel released in the air (not in the hand dock) floats there relative to the head and " +
                 "lazily follows it (turns after it only when looking away further) - it stayed where it was put in the " +
                 "player's space.")]
        public bool EnableMarvinHeadFollow = true;

        [Tooltip("V1: The VR hero's body plays T_SWIMTURNL/R (T_DIVETURNL/R under water) while drifting and turning - " +
                 "it floated without any turn animation.")]
        public bool EnableSwimTurnAnimation = true;

        [Tooltip("V1: Melee fighters can't hit an enemy above their body (hero up a rock above wolves) or far below " +
                 "them (orcs at a castle wall) - they turn and wait, take an equipped ranged weapon, and give up after " +
                 "8 s. Wolves bit a climbing hero, a crossbow guard ran off the wall into the orcs.")]
        public bool EnableMeleeHeightReach = true;

        [Tooltip("V1: G2 NPCs keep all their talents when saved (level changes too) - they were cut to G1's 12, the hero " +
                 "lost pickpocketing (12), smithing, runes, alchemy... A missing talent slot is added when set again.")]
        public bool EnableG2KeepAllTalents = true;

        [Tooltip("V1: The hero taking an item lying in the world sends PERC_ASSESSTHEFT like the engine - the scripts " +
                 "call it theft by the item's/room's owner (Lares' hut in G1, citizens' houses in G2). Nothing out of an " +
                 "inventory, only the first take.")]
        public bool EnableItemTheftPerception = true;

        [Tooltip("V1: The VR purse (EnableVrPickpocket) in G2 too, in addition to the pickpocket dialog (untouched): " +
                 "like G1 - sneak, open his pockets, each item can get the hero caught (100 - dexterity %, 5-90).")]
        public bool EnableG2VrPickpocket = true;

        [Tooltip("V1: An item lookup of a symbol that isn't an instance gives no item instead of initializing it - G2's " +
                 "ItemHoshi (Marvin 'insert itemhoshi') has ITWR_ADDON_LPBOOK, the native VM error crashed Unity.")]
        public bool EnableItemInitSymbolCheck = true;

        [Tooltip("V1: With the hero's fists as the VR hands, the arms draw over menus/dialogs like the block hands " +
                 "(VRHandsOnTop) - the fist pointing at a menu entry was behind the menu.")]
        public bool EnableHeroHandsOnTop = true;

        [Tooltip("V1: The favorites ring's open gesture keeps its hold time through a short break (0.3 s: a hand dipping " +
                 "below the eyes, a trigger flicker) - it took much longer than its 1 s.")]
        public bool EnableRingGestureGrace = true;

        [Tooltip("V1: XRI: the hands' pointer lines fade to light red while pointing at something (item, mob, NPC, UI) " +
                 "and back.")]
        public bool EnableXriPointerHoverColor = true;

        [Tooltip("V1: The game version / profile scene puts the player onto its floor like the logo and menu scenes - the " +
                 "XRI player fell from the floorless Player scene before the floor existed and kept falling.")]
        public bool EnableGameVersionScenePlacement = true;

        [Tooltip("V1: Immersion option \"Hero's hands\" (ini heroHands, off by default): with the VR hero body, the hero's " +
                 "own fists (gloves of his armor) are the VR hands - turned like the controllers, the VR hand models " +
                 "hidden. Before a world is loaded (menus) the VR hands stay.")]
        public bool EnableHeroHands = true;

        [Tooltip("Turn of the hero fists' thumbs around their fingers, outwards (degrees, EnableHeroHands). 0 = thumb up " +
                 "like the VR hand's. Read every frame - tune it while playing.")]
        [Range(-180f, 180f)]
        public float HeroHandsRollDegrees;

        [Tooltip("V1 (XRI physical hands): the hand holding the backpack ignores its whole hull - hand and backpack " +
                 "pushed each other and the backpack shook in the hand.")]
        public bool EnableBackpackHandPassThrough = true;

        [Tooltip("V1 (opinion): a human beaten by the hero before fights back when the hero attacks him again - the " +
                 "scripts made him look away (\"I didn't see anything\"), and his hostile attitude restarted it over and " +
                 "over: weapon drawn, put away, looking away in a loop. As a witness he still looks away.")]
        public bool EnableDefeatedNpcFightsBack = true;

        [Tooltip("V1: An NPC looking away (ZS_TurnAway: a beaten guard, \"I didn't see anything\") ignores the hero for " +
                 "its 20 s and then forgets his hostility (temporary attitude back to the permanent one) - the enemy " +
                 "perception has no view angle, he drew his weapon, put it away and looked away in a loop.")]
        public bool EnableTurnAwayIgnoresEnemyBehind = true;

        [Tooltip("V1: A level change whose start VOB isn't a free point spawns at the waypoint/VOB of that name, or next to " +
                 "the trigger back to the old world - Mroczne Tajemnice's temple sends you to a start VOB the mine lacks.")]
        public bool EnableLevelChangeSpawnFallback = true;

        [Tooltip("V1: Hlp_GetNpc resolves instance variables like SELF/OTHER to their NPC like the engine - Mroczne " +
                 "Tajemnice's dragon (B_KillNpc(self)) vanished instead of turning into Algirion.")]
        public bool EnableHlpGetNpcVariables = true;

        [Tooltip("V1: The NPC who knocks the hero out gets AIV_HASDEFEATEDSC like G1's ZS_Unconscious sets it - Mroczne " +
                 "Tajemnice's Sheer'Ghar won the duel and had nothing to say afterwards.")]
        public bool EnableHeroDefeatedAivar = true;

        [Tooltip("V1: AI_FinishingMove like the engine - the NPC walks to the unconscious target, turns, draws a melee " +
                 "weapon and plays t_1hSFinish/t_2hSFinish; the target dies with the stab. Before, it died at once.")]
        public bool EnableEngineFinishingMove = true;

        [Tooltip("V1: The finishing move kills with the stab's sound (t_1hSFinish has no hit frame) - the target died, " +
                 "and the hero screamed, only when the animation was over.")]
        public bool EnableFinishWithStabSound = true;

        [Tooltip("V1 (VR): The hero finishes off an unconscious NPC with a stab and a twist of the wrist (like the lock " +
                 "picks) instead of any slash - the 'Finishing' option in the Immersion menu (off: any hit).")]
        public bool EnableVrStabFinish = true;

        [Tooltip("V1 (VR): The stab finish kills when the blade stays in the body for 3 seconds (a pulse every second) " +
                 "instead of the twist of the wrist - the twist was too finicky.")]
        public bool EnableStabFinishHold = true;

        [Tooltip("V1: An unconscious NPC finished off dies where he lies (T_WOUNDED_2_DEAD / S_DEAD) with his death " +
                 "scream (SVM_x_DEAD, like the engine) - he got up into a stand and fell on his back (T_DEADB).")]
        public bool EnableFinishedNpcLyingDeath = true;

        [Tooltip("V1: A dead NPC has 0 hit points like in the engine - a finished off NPC kept the knockout's 1 HP " +
                 "(shown on his bar) and stood in a T-pose when he was culled in again.")]
        public bool EnableDeathZeroHitPoints = true;

        [Tooltip("V1: A dead NPC doesn't go back to his routine (AI_ContinueRoutine left in his queue) - Ghorri, killed " +
                 "while knocked out, stood up in a T-pose later.")]
        public bool EnableDeadSkipsRoutine = true;

        [Tooltip("V1: A dropped item (also a missed arrow/bolt lying in the ground) rests on the world but doesn't block " +
                 "the hero or stop arrows - an archer's missed bolts blocked the hero's way and piled up in one spot.")]
        public bool EnableDroppedItemColliderLayer = true;

        [Tooltip("V1: An NPC finishing off the knocked out hero kills him like in Gothic (the 'Death' option in the " +
                 "Immersion menu can turn it off - then he only stays knocked out).")]
        public bool EnableHeroDeath = true;

        [Tooltip("V1 (VR): Knocked out / dead, the VR hero body falls (T_Stand_2_WoundedB, T_DeadB) and the camera goes " +
                 "down with its head and up again when he stands up - where you look is never taken from you.")]
        public bool EnableHeroFallCamera = true;

        [Tooltip("V1: Menus with more options than fit scroll (drag, mouse wheel, arrows) - the Immersion menu.")]
        public bool EnableMenuScrolling = true;

        [Tooltip("V1 (VR): The Marvin panel and the on-screen keyboard (save names, Marvin) have a bar under them like " +
                 "SteamVR's windows - point at it, hold the trigger and move them. The Marvin panel then stays where you " +
                 "put it (moving with you), 'Hand' docks it back to the left hand.")]
        public bool EnableUiGrabBars = true;

        [Tooltip("V1: A rune with a throwable spell can't be grabbed by the throwing hand - grip + trigger (like a fist) " +
                 "grabbed it, both hands on the rune left none to throw with and fire balls never came.")]
        public bool EnableThrowRuneOneHand = true;

        [Tooltip("V1: The backpack vacuum takes an item that is marked as held by the player but is neither in a hand nor " +
                 "in a socket (a rune from a holster that was dropped) and clears the stale mark.")]
        public bool EnableVacuumIgnoresStaleHeld = true;

        [Tooltip("V1: An empty VR hand with grip + trigger held is a fist - the hero is in fist mode and punches with a " +
                 "swing (hand speed), the damage is the engine's fist damage (strength, blunt).")]
        public bool EnableVrFists = true;

        [Tooltip("V1: Mdl_SetModelScale scales the NPC like the engine - the Shrink spell (G1 troll) and NPCs whose " +
                 "scripts set a size. Off: the scale is only stored.")]
        public bool EnableModelScale = true;

        [Tooltip("V1: Maps of the current world show the hero (U.TGA arrow, turned like him) like the engine - " +
                 "Doc_SetLevel/Doc_SetLevelCoords, G1 maps use the world's bounds.")]
        public bool EnableMapPlayerMarker = true;

        [Tooltip("G1 world map (WORLD.ZEN) edges in Gothic cm: x = left, y = top, z = right, w = bottom. G1 maps have no " +
                 "Doc_SetLevelCoords - fit by two spots in VR (start, Swamp Camp). " +
                 "All 0 = the world mesh bounds.")]
        public Vector4 G1WorldMapBounds = new(-84056f, 67222f, 84451f, -61217f);

        [Tooltip("V1: Looping particle effects (torches, camp fires, fog, magic) emit at Gothic's own rate (max 500/s) " +
                 "instead of ~10 particles/s. Off if the frame rate suffers.")]
        public bool EnablePfxFullRateLoops = true;

        [Tooltip("V1: Taking pieces off a held item stack: the empty hand near the stack presses its trigger (T in the " +
                 "simulator) - one piece jumps into it, holding the trigger takes more and more quickly.")]
        public bool EnableStackSplit = true;

        [Tooltip("Seconds until the second piece while the split trigger is held (EnableStackSplit).")]
        public float StackSplitRepeatDelay = 0.45f;

        [Tooltip("Each next piece comes this factor faster (0.1-1, 1 = constant pace) (EnableStackSplit).")]
        public float StackSplitAcceleration = 0.8f;

        [Tooltip("V1: AI_Flee - NPCs run away from their enemy (fear spell, ZS_Flee). Before they stood in a T-pose.")]
        public bool EnableAiFlee = true;

        [Tooltip("V1: Teleports put what the VR hands hold into the backpack (a held rune stayed at the old place " +
                 "and the hero stayed in magic mode). A rune more than 1.5 m away from both hands is released.")]
        public bool EnableTeleportKeepsHeldItems = true;

        [Tooltip("V1: Torches (ITEM_TORCH) are lit and put out with the trigger of the hand holding them (R in the " +
                 "simulator): fire PFX on the tip + light, like ItLsTorchBurning. Not saved (they start unlit).")]
        public bool EnableVrTorch = true;

        [Tooltip("V1: Wld_PlayEffect (scripts play VISUALFX at an NPC/vob - G2 uses it a lot, G1 the Sleeper's " +
                 "fireball): the effect, its sound and emFXCreate_S chain at the origin.")]
        public bool EnableWldPlayEffect = true;
    }
}
