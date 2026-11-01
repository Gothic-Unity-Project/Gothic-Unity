namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// The monster a transformation spell turned the hero into: how fast it walks and runs compared with the human hero
    /// (its walk and run loops' root motion) and how high it looks around (m).
    /// </summary>
    public class HeroTransformation
    {
        public string MonsterName;
        public int Guild;
        public float WalkSpeedFactor = 1f;
        public float RunSpeedFactor = 1f;
        public float EyeHeight;
    }
}
