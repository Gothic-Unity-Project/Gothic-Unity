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
    }

    public class UnityCustomSave
    {
        public int Version = 2;
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

        // NPC dirty delta — additive (update/add only, never delete)
        public List<NpcSaveEntry> Npcs = new();
    }
}
