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
        [Tooltip("Override GameSettings.json ModPath/ModIni for quick in-editor testing.")]
        public bool EnableMod;
        [ConditionalField(fieldToCheck: nameof(EnableMod), compareValues: true)]
        public string ModPath = string.Empty;
        [ConditionalField(fieldToCheck: nameof(EnableMod), compareValues: true)]
        [Tooltip("INI filename inside ModPath/system/ (e.g. DM_E.ini)")]
        public string ModIni = string.Empty;

        [Tooltip("Language used when auto-detection fails (mods often rename MOBNAME_CRATE, the probe constant — e.g. G2 Renovation uses 'Skrzynka'). One of: cs, pl, ru, de, en, es, fr, it. Empty = fail the boot like before.")]
        public string FallbackLanguage = string.Empty;

        public Controls GameControls = Controls.VR;

        [Separator("Debug")]
        [ConditionalField(fieldToCheck: nameof(GameControls), compareValues: Controls.VR)]
        public bool EnableVRDeviceSimulator;

        [Tooltip("Show Marvin Mode menu next to left hand in VR.")]
        public bool ActivateMarvinMode;

        
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

        [Separator("Save/Load System (WIP)")]
        [Tooltip("Enable save/load system. OFF = main-branch behavior: no UNITYNPCINIT snapshot, no merged-snapshot NPC restore, no NpcCulling tracking. Turn OFF to diagnose monster/NPC init regressions.")]
        public bool EnableSaveLoadSystem;

        [Separator("Combat (WIP)")]
        [Tooltip("Enable NPC ranged combat (bow/crossbow). OFF = NPCs fall back to their best melee weapon regardless of range.")]
        public bool EnableNpcRangedCombat;

        [Separator("Debug")]
        [Tooltip("Ignore frame skipping during loading.")]
        public bool SpeedUpLoading;

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
        [Tooltip("Multiplies zCMover animation speed. 1 = original, 3 = three times faster. Useful when a world mover has a very low speed value in Gothic data.")]
        [Range(0.1f, 10f)]
        public float MoverSpeedMultiplier = 1f;

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

        [Tooltip("Daedalus symbol name of NPC to spawn next to player via Marvin 'Spawn NPC' button. E.g. SH, PC_THIEF. Leave empty to disable.")]
        public string MarvinSpawnNpcSymbol = string.Empty;

        [Header("NPC Combat (WIP)")]
        [Tooltip("Fire ZS_Attack_Loop early when combo window opens so the next attack chains before the animation ends.")]
        public bool EnableNpcCombatCombos = true;

        [Tooltip("Enable proximity + FOV hit detection in AttackPlayAni. Disable to watch animations without dealing damage.")]
        public bool EnableNpcHitDetection = true;

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

        [Tooltip("PrintScreen/AI_PrintScreen (\"New log entry\", \"1 item received\", ...) shown as a HUD in front of " +
                 "the VR head + Snd_Play 2D sounds (\"LogEntry\"). Read at VM start - restart the game after toggling.")]
        public bool EnableScreenMessages = true;

        [Tooltip("NPCs using a mob (sitting on a bench, ...) don't turn (AI_TurnToNpc etc.), like the engine. " +
                 "Before, a sitting NPC turned his back to the hero when talking.")]
        public bool EnableNoTurnWhileUsingMob = true;

        [Tooltip("A seated NPC's repeated AI_UseMob (routines call it again and again) no longer snaps it back to the " +
                 "slot in front of the bench/chair - it stays where the sit animation moved it (on the seat).")]
        public bool EnableMobSeatFix = true;

        [Tooltip("AI_EquipArmor / AI_UnequipArmor / AI_EquipBestArmor + armor (un)equips at runtime swap the NPC's body " +
                 "mesh (Greg in Lobart's clothes, Pedro's robe, ...). Also kept after save/load. Read at VM start.")]
        public bool EnableRuntimeArmorVisuals = true;

        [Tooltip("V1: Mobs used with a melee tool + onStateFunc (G2 treasure X marks, pickaxe) are dug up by hitting " +
                 "them 3 times with a swung melee weapon -> calls [onStateFunc]_S1 like the engine.")]
        public bool EnableDigSpots = true;

        [Tooltip("Dig spots only react to their own tool (vanilla: pickaxe). Off = any melee weapon (easier testing).")]
        public bool DigSpotsRequireTool;

        [Tooltip("V1: The dialog box is never cut by walls/benches/the NPC (ignores depth). VR hands render after " +
                 "transparents so they stay visible above it for pointing.")]
        public bool EnableDialogAlwaysOnTop = true;

        [Tooltip("V1: Grabbing a mob with an onStateFunc and no mover target (G2 shrines, alchemy/rune tables, " +
                 "bookstands, ...) calls [onStateFunc]_S1 like the engine. MOBSI dialogs (AI_ProcessInfos(hero)) open " +
                 "next to the mob.")]
        public bool EnableMobsiDialogs = true;

        [Tooltip("Crossbows (ITEM_CROSSBOW) shoot in VR: aim with the crossbow (one or both hands), trigger fires a " +
                 "bolt (munition from the inventory, 1 per shot, 30 m/s + gravity). Engine damage (G2 DEX + damage - " +
                 "protection). Automatic reload after 1.5 s for now.")]
        public bool EnableVrCrossbow = true;

        [Tooltip("Child vobs (doors/chests/mobs/items placed below another vob) get their own VobLoader - their VR " +
                 "adapters read the parent's data before (InvalidCastException, 'No door or container found', broken " +
                 "doors/chests in G2).")]
        public bool EnableChildVobLoaders = true;

        [Tooltip("Items without pre-cached colliders (morph meshes: bows, crossbows) get a box collider from their " +
                 "mesh bounds. Before they had no collider: fell through the ground, no force grab, flew away in hands.")]
        public bool EnableItemColliderFallback = true;

        [Tooltip("Npc_ClearAIQueue no longer restarts an NPC's running idle animation (engine: only the queue is " +
                 "cleared). Summoned demons flapped their wings every 0.5 s (B_FullStop on each ASSESSPLAYER).")]
        public bool EnableKeepIdleOnClearAiQueue = true;

        [Tooltip("PlayVideo/PlayVideoEx (intro/chapter videos, G1 ending) play in a dark 'cinema' in front of the VR " +
                 "head. Original .bik files are decoded by BinkPlayer (an MP4 with the same name wins). The world is " +
                 "paused meanwhile. Skip: any trigger or A/X (keyboard: Space/Escape). Read at VM start.")]
        public bool EnableScriptVideos = true;

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

        [Tooltip("V1: Projectile spells (VISUALFX CAST key with a TARGET trajectory: Firebolt, Fireball, Thunderbolt, " +
                 "Icecube, ...) are thrown: hold the rune in one hand, hold trigger to charge, swing + release trigger to " +
                 "throw. Soft homing towards the NPC closest to the throw direction. Other spells stay as they are.")]
        public bool EnableThrowableSpells = true;

        [Tooltip("V1: Npc_GetLookAtTarget (G2 scripts) returns the NPC set by AI_LookAtNpc. Read at VM start.")]
        public bool EnableNpcLookAtTarget = true;

        [Tooltip("V1: Hitting your own summon (demon, skeleton, ...) damages it, but it doesn't attack you back and " +
                 "nobody else reacts to it - a training dummy only for its summoner.")]
        public bool EnableSummonIgnoresMasterHits = true;

        [Tooltip("V1: Chasing NPCs run along a NavMesh (built at runtime around the hero from world + vob colliders, " +
                 "Gothic STEP_HEIGHT/SLIDE_ANGLE): around obstacles, and they stop at ledges/drops instead of running " +
                 "off the world. Routines still walk the waynet.")]
        public bool EnableNpcNavMesh = true;
    }
}
