using ZenKit.Daedalus;

namespace GUZ.Core.Domain.Npc.Actions
{
    public class AnimationAction
    {
        public AnimationAction(string string0 = null, int int0 = 0, int int1 = 0, uint uint0 = 0, float float0 = 0f,
            bool bool0 = false, NpcInstance instance0 = null, NpcInstance instance1 = null)
        {
            String0 = string0;
            Int0 = int0;
            Int1 = int1;
            Float0 = float0;
            Bool0 = bool0;
            Instance0 = instance0;
            Instance1 = instance1;
        }

        public readonly string String0;
        public readonly int Int0;
        public readonly int Int1;
        public readonly float Float0;
        public readonly bool Bool0;
        public readonly NpcInstance Instance0;
        public readonly NpcInstance Instance1;
    }
}
