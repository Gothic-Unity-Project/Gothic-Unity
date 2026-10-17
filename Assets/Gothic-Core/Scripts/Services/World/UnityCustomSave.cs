using System.Collections.Generic;

namespace Gothic.Core.Services.World
{
    public class NpcInitEntry
    {
        public int InstanceId;
        public int SymbolIndex;
        public string GoName;           // Unity GO name — e.g. "Diego (1)", "Chrząszcz (241)"
        public string WaypointName;
        public string NpcInstance;
        public int[] Attributes; // saved post-startup-scripts so dead NPCs have HP=0
        public bool IsRuntimeSpawned; // spawned mid-game (Marvin, Wld_InsertNpc) - added to the world's init list on load
    }

    public class UnityNpcInit
    {
        public int Version = 1;
        public string WorldName;
        public List<NpcInitEntry> Npcs = new();
    }

    public class NpcSaveEntry
    {
        public string Key;              // Unity GO name (e.g. "Diego (1)") — unique, stable, matches scene hierarchy
        public string NpcInstance;      // human-readable, not used as key
        public float[] Position;        // [x, y, z]
        public float[] Rotation;        // [x, y, z, w]
        public int[] Attributes;        // NpcAttribute indices 0-7 (HP, HP_MAX, Mana, etc.)
        public string CurrentStateName;
        public string CurrentRoutine;
        public string CurrentFreePointName; // name of the FP the NPC held when last snapshotted
        public bool IsDead;             // true if HP was 0 at snapshot time
        public int[] Protection;        // PROT_* 0-7 incl. worn armor. Null in older saves.
        // Daedalus symbol names. On load the NPC's INIT re-equips its default gear (EquipItem/Mdl_SetVisualBody),
        // this list replaces it - e.g. a looted sword stays gone. Null in older saves.
        public List<string> EquippedItems;
        // DeveloperConfig.EnableNpcInventorySaveState: the whole inventory (Daedalus symbol names) - a looted NPC stays
        // looted, INIT gave it everything back on load. Null in older saves.
        public List<HeroInventoryEntry> Inventory;
    }

    /// <summary>
    /// DeveloperConfig.EnableWorldItemSaveState: what a world's items and chests differ from its world file (.zen) - the
    /// world is built from the .zen on load and this is applied on top.
    /// </summary>
    public class WorldItemsSaveEntry
    {
        // WorldItemSaveDomain keys (instance|x|y|z in cm) of world file items that are gone (taken) or moved.
        public List<string> RemovedItems = new();
        // Items lying in the world that the world file doesn't have there (dropped, NPC drops, arrows, moved items).
        public List<WorldItemSaveEntry> AddedItems = new();
        // Chests whose contents or lock changed.
        public List<ContainerSaveEntry> Containers = new();
    }

    public class WorldItemSaveEntry
    {
        public string Instance;         // Daedalus symbol name, e.g. "ITFO_BEER"
        public int Amount;
        public float[] Position;        // [x, y, z] in ZenKit units (cm)
        public float[] Rotation;        // Unity quaternion [x, y, z, w]
    }

    public class ContainerSaveEntry
    {
        public string Key;              // WorldItemSaveDomain key (name|x|y|z in cm)
        public string Contents;         // "ITMI_GOLD:20,ITFO_APPLE:2"
        public bool IsLocked;
    }

    /// <summary>
    /// A mover's or trigger's runtime state (LogicVobSaveState). Only changed ones are saved.
    /// </summary>
    public class LogicVobSaveEntry
    {
        public string Key;              // LogicVobSaveState.MakeKey: type|name|position from the world file
        public int KeyIndex;            // mover: keyframe it stands at
        public bool IsOpen;             // mover
        public bool IsDisabled;         // mover: zCMessageFilter Disable
        public int ActivationCount;     // trigger, trigger list, trigger script: times fired (maxActivationCount)
        public bool[] CodeMasterKeys;   // code master: slaves already counted (engine CodeMaster keys)
        public int CodeMasterCount;     // code master: presses so far
    }

    public class HeroInventoryEntry
    {
        public string Name;             // Daedalus symbol name, e.g. "ITFO_APPLE"
        public int Amount;
    }

    public class UnityCustomSave
    {
        public int Version = 4;
        public string WorldName;

        // Hero transform
        public float[] HeroPosition;
        public float[] HeroRotation;

        // Hero stats
        public int[] HeroAttributes;
        public int HeroGuild;
        public int HeroLevel;
        public int HeroXp;
        public int HeroExpNext;
        public int HeroLp;

        // Hero inventory — full packed inventory snapshot (all categories)
        public List<HeroInventoryEntry> HeroInventory;

        // Hero protection (PROT_* indices 0-7) - the total incl. worn armor/amulets. Null in older saves.
        public int[] HeroProtection;

        // Daedalus symbol names of the hero's equipped items (Props.EquippedItems isn't part of the ZenKit save).
        // Restored without re-applying effects, as HeroProtection/HeroAttributes already contain them. Null in older saves.
        public List<string> HeroEquippedItems;

        // Guild attitude matrix — flat int[] indexed as [guild1 * GuildCount + guild2]
        public int[] GuildAttitudes;

        // NPC dirty delta — additive (update/add only, never delete)
        public List<NpcSaveEntry> Npcs = new();

        // NPC state (position, HP, dead, routine, ...) of every visited world (upper case world file name) - the flat Npcs
        // list mixed worlds (the same NPC key in two worlds). Null in older saves.
        public Dictionary<string, List<NpcSaveEntry>> NpcsByWorld;

        // NPCs spawned mid-game per world (upper case world file name): added to the world's init list on load, their
        // position/HP come from Npcs like every other NPC. Null in older saves.
        public Dictionary<string, List<NpcInitEntry>> SpawnedNpcsByWorld;

        // Movers and triggers per world (upper case world file name). Null in older saves.
        public Dictionary<string, List<LogicVobSaveEntry>> LogicVobsByWorld;

        // Items and chests per world (upper case world file name), only what differs from the world file. Null in older
        // saves.
        public Dictionary<string, WorldItemsSaveEntry> ItemsByWorld;
    }
}
