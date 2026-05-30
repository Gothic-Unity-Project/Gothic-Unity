using GUZ.Core.Adapters.Npc;
using GUZ.Core.Adapters.Properties;
using GUZ.Core.Models.Proxy;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Models.Container
{
    /// <summary>
    /// Data object containing references to information needed on ZenKit and Unity side.
    ///
    /// # NpcInstance
    /// - General: (1) Initialized with data during New Game Start / switching to a world for the first time. (2) Holds data during Daedalus execution e.g. self.wp
    /// 
    /// - int Id
    /// - string Slot
    /// - string Effect
    /// - NpcType NpcType
    /// - int Flags
    /// - int DamageType
    /// - int Guild
    /// - int Level
    /// - int FightTactic
    /// - int Weapon
    /// - int Voice
    /// - int VoicePitch
    /// - int BodyMass
    /// - int DailyRoutine		- NPC routine. Need to be stored into INpc.CurrentRoutine before saving a game.
    /// - int StartAiState		- Monster's routine. Need to be stored into INpc.CurrentRoutine before saving a game. (Hint: In G1, NPC 888 also uses this state instead of a routine)
    /// - string SpawnPoint
    /// - int SpawnDelay
    /// - int Senses
    /// - int SensesRange
    /// - string Wp
    /// - int Exp
    /// - int ExpNext
    /// - int Lp
    /// - int BodyStateInterruptableOverride
    /// - int NoFocus
    /// - string[] Name
    /// - int[] Missions
    /// - int[] Attributes
    /// - int[] HitChances
    /// - int[] Protections
    /// - int[] Damages
    /// - int[] AiVars
    ///
    /// 
    /// # Vobs.Npc : INpc, IVirtualObject
    /// - General: Contains data stored and retrieved from SaveGames. Some data needs to be moved from NpcInstance to this instance before saving.
    ///
    /// - ##INpc
    /// - string NpcInstance
    /// - Vector3 ModelScale
    /// - float ModelFatness
    /// - int Flags
    /// - int Guild
    /// - int GuildTrue
    /// - int Level
    /// - int Xp
    /// - int XpNextLevel
    /// - int Lp
    /// - int FightTactic
    /// - int FightMode
    /// - bool Wounded
    /// - bool Mad
    /// - int MadTime
    /// - bool Player
    /// - string StartAiState
    /// - string ScriptWaypoint
    /// - int Attitude
    /// - int AttitudeTemp
    /// - int NameNr
    /// - bool MoveLock
    /// - bool Respawn
    /// - int RespawnTime

    /// ### Ai properties
    /// - string CurrentRoutine         - Name of current Routine (index would be unsafe between languages and game versions). Once loaded, place into NpcInstance.DailyRoutine or NpcInstance.StartAiState.
    /// - bool CurrentStateValid        - If set to invalid, Next State is checked and used or routines get recalculated and current one is fetched.
    /// - string CurrentStateName       - Stable across Gothic versions and languages. Index could change based on used game installation (Daedalus differences).
    /// - int CurrentStateIndex         - e.g., index of ZS_Guard - Always the start function of a routine.
    /// - bool CurrentStateIsRoutine    - Normal NPC Routine start=true, calling a Perception=false
    /// - bool NextStateValid           - Checked when CurrentStateValid==false. e.g., returning back to normal routine after perception routine (e.g., ZS_Talk) got executed.
    /// - string NextStateName          - Stable across Gothic versions and languages. Index could change based on used game installation (Daedalus differences).
    /// - int NextStateIndex            - e.g., index of ZS_Guard - Always the start function of a routine.
    /// - bool NextStateIsRoutine       - Normal NPC Routine start=true, calling a Perception=false 
    /// - int LastAiState               - Used for Npc_WasInState(self, ZS_GuardPassage). Should be set each time a routine changes. Will be filled with the START method, not loop etc.
    /// - bool HasRoutine               - Set when any Routine is read from Daedalus.
    /// - bool RoutineOverlay			- Used for TA_BeginOverlay() only. (Called 5x in G1)
    /// - int RoutineOverlayCount		- Count of remaining elements still executed between TA_BeginOverlay() and TA_EndOverlay(). Used 5x in G1.
    /// - bool RoutineChanged           - Unused in G1
    /// - bool StartNewRoutine          - Unused in G1
    /// - int AiStateDriven             - Unused in G1
    /// - int WalkmodeRoutine			- Unused in G1
    /// - bool WeaponmodeRoutine		- Unused in G1
    /// - Vector3 AiStatePos            - Unused in G1 (Positions from NPCs are stored and used inside IVob.Position)
    
    /// - int BsInterruptableOverride
    /// - int NpcType
    /// - int SpellMana
    /// - IVirtualObject? CarryVob
    /// - IVirtualObject? Enemy
    /// - int OverlayCount
    /// - string[] Overlays
    /// - int TalentCount
    /// - ITalent[] Talents
    /// - int ItemCount
    /// - IItem[] Items
    /// - int SlotCount
    /// - ISlot[] Slots
    /// - int NewsCount
    /// - INews[] News
    /// - string[] Packed
    /// - string[] Overlays
    /// - ITalent[] Talents
    /// - IItem[] GetItems
    /// - ISlot[] Slots
    /// - INews[] News
    /// - int[] Missions            - Copy from NpcInstance
    /// - int[] Attributes          - Copy from NpcInstance
    /// - int[] HitChances          - Copy from NpcInstance
    /// - int[] Protections         - Copy from NpcInstance
    /// - int[] AiVars              - Copy from NpcInstance
    ///
    /// - ##IVirtualObject
    /// - VirtualObjectType Type
    /// - int Id
    /// - AxisAlignedBoundingBox BoundingBox
    /// - Vector3 Position
    /// - Matrix3x3 Rotation
    /// - bool ShowVisual
    /// - SpriteAlignment SpriteCameraFacingMode
    /// - bool CdStatic
    /// - bool CdDynamic
    /// - bool Static
    /// - ShadowType DynamicShadows
    /// - bool PhysicsEnabled
    /// - AnimationType AnimationType
    /// - int Bias
    /// - bool Ambient
    /// - float AnimationStrength
    /// - float FarClipScale
    /// - string PresetName
    /// - string Name
    /// - IVisual? Visual
    /// - byte SleepMode
    /// - float NextOnTimer
    /// - IAi? Ai
    /// - IEventManager? EventManager
    /// - int ChildCount
    /// - IVirtualObject[] Children
    /// </summary>
    public class NpcContainer
    {
        // ZenKit data
        public NpcInstance Instance;
        public NpcProxy Vob;

        // Unity Data
        public GameObject Go;
        /// <summary>
        /// Unity Properties which are loaded from Daedalus and won't be stored on ZenKit data.
        /// </summary>
        public NpcProperties Props;

        // Cache objects from Prefab
        public NpcPrefabProperties PrefabProps;
    }
}
