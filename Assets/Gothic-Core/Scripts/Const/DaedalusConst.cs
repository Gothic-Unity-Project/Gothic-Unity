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

        // Daedalus symbols the C# side reads (constants, instances, script functions), by their script name.
        public const string SpellFxInstanceNames = "spellFXInstanceNames";
        public const string SpellBad = "SPELL_BAD";
        public const string AivEnemyOverride = "AIV_ENEMYOVERRIDE";
        public const string AivLastHitByRangedWeapon = "AIV_LASTHITBYRANGEDWEAPON";
        public const string AivMmPartyMember = "AIV_MM_PARTYMEMBER";
        public const string NpcTalentMage = "NPC_TALENT_MAGE";
        public const string NpcAttackFinishDistance = "NPC_ATTACK_FINISH_DISTANCE";
        public const string DamCriticalMultiplier = "DAM_CRITICAL_MULTIPLIER";
        public const string GuildMinecrawler = "GIL_MINECRAWLER";
        public const string FaiMinecrawler = "FAI_MINECRAWLER";
        public const string GuildSeparatorHuman = "GIL_SEPERATOR_HUM";
        public const string GuildSeparatorOrc = "GIL_SEPERATOR_ORC";
        public const string ZsAttackLoop = "ZS_ATTACK_LOOP";
        public const string ZsMmAttack = "ZS_MM_ATTACK";
        public const string BMmReactToOthersDamage = "B_MM_REACTTOOTHERSDAMAGE";
        public const string GCanNotCast = "G_CanNotCast";
        public const string GCanNotUse = "G_CanNotUse";

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
