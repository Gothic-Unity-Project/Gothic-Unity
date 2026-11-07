namespace Gothic.Core.Const
{
    public class DaedalusConst
    {
        public static string SoundDrawMetal = "DRAWSOUND_ME.WAV";
        public static string SoundDrawWood =  "DRAWSOUND_WO.WAV";
        public static string SoundUndrawMetal = "UNDRAWSOUND_ME.WAV";
        public static string SoundUndrawWood =  "UNDRAWSOUND_WO.WAV";
        
        // Guilds
        public static string GuildValuesInstance = "GIL_Values";
        public static string GuildMaxValue = "GIL_MAX";
        public static string GuildHumanAttitudesSize = "TAB_ANZAHL";
        public static string GuildHumanAttitudesArray = "GIL_ATTITUDES";

        // AI states the engine treats specially - Docs/adr/2026-10-31-npc-perceptions.md
        // Guide/follow states sense a lagging hero (G1 ZS_GuidePC/ZS_FollowPC, G2 ZS_Guide_Player/ZS_Follow_Player).
        public static readonly string[] EscortStatePrefixes = { "ZS_GUIDE", "ZS_FOLLOW" };
        // States in which the NPC looks away from the hero on purpose (decision 5: forgets the temp attitude).
        public static readonly string[] LookingAwayStates = { "ZS_TURNAWAY" };

        // AI states of a downed NPC (knockout, magic sleep, death).
        public const string ZsUnconscious = "ZS_UNCONSCIOUS";
        public const string ZsMagicSleep = "ZS_MAGICSLEEP";
        public const string ZsDead = "ZS_Dead";
        public const string ZsDeadLoop = "ZS_Dead_Loop";

        // Animations of a downed NPC: falling unconscious, lying there, getting up, lying dead.
        public const string AniStandToWounded = "T_Stand_2_Wounded";
        public const string AniWounded = "S_Wounded";
        public const string AniWoundedToStand = "T_Wounded_2_Stand";
        public const string AniDeadB = "S_DEADB";
        // G1 crawler queen, harpy and meatbug have no S_DEADB, only S_DEAD.
        public const string AniDead = "S_DEAD";

        public static class Svm
        {
            // SVM keys the engine's oCMobLockable::CanOpen uses (output via DialogService.HeroSaySvm / OutputSvm).
            public const string PickLockOrKeyMissing = "$PICKLOCKORKEYMISSING";
            public const string KeyMissing = "$KEYMISSING";
            public const string PickLockMissing = "$PICKLOCKMISSING";
            public const string NeverOpen = "$NEVEROPEN";
            public const string NeedKey = "$NEEDKEY";
            public const string NoMorePicks = "$NOMOREPICKS";
            public const string DoesntWork = "$DOESNTWORK";
        }
    }
}
