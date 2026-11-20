namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// One fact, already translated from the game event (no ZenKit types), so conditions can be checked in tests.
    /// </summary>
    public readonly struct AchievementFact
    {
        public readonly AchievementFactKind Kind;
        public readonly int IntKey;
        public readonly string StringKey;
        public readonly int Value;
        public readonly bool IsByHero;


        public AchievementFact(AchievementFactKind kind, int intKey = 0, string stringKey = null, int value = 0,
            bool isByHero = false)
        {
            Kind = kind;
            IntKey = intKey;
            StringKey = stringKey;
            Value = value;
            IsByHero = isByHero;
        }
    }
}
