namespace Gothic.Core.Logging
{
    public enum LogCat
    {
        // Startup
        Loading,
        PreCaching,

        // NPC + Monster
        Ai,
        Animation,
        Dialog,
        Npc,
        Fight,

        // Visuals
        Mesh,
        Ui,

        // Zen/Daedalus
        ZenKit,
        ZSpy,

        // Misc
        Misc,
        VR,
        Vob,
        Audio,
        Debug,

        // Functional test harness (ADR-0001) - session and step boundaries, watchdog trips, artifact paths.
        Test,
    }
}
