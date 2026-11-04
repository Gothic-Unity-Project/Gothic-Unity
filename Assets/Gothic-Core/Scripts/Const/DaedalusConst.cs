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
    }
}
