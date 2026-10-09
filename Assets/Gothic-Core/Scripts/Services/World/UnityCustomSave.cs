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
    }

    public class HeroInventoryEntry
    {
        public string Name;             // Daedalus symbol name, e.g. "ITFO_APPLE"
        public int Amount;
    }

    public class UnityCustomSave
    {
        public int Version = 3;
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
    }
}
