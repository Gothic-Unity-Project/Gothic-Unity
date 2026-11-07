using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// A fire hit (GlobalEventDispatcher.NpcIgnited) sets the NPC on fire: the "dance" s_Fire_Victim and the
    /// NpcBurnEffect adapter (flames, light, sound). A new fire hit restarts the time.
    /// </summary>
    public class NpcBurnService
    {
        [Inject] private readonly INpcBurnEffectFactory _npcBurnEffectFactory;

        private const string _victimAnimation = "S_FIRE_VICTIM";

        public void Init()
        {
            GlobalEventDispatcher.NpcIgnited.AddListener(Ignite);
        }

        private void Ignite(NpcContainer npc)
        {
            var burn = _npcBurnEffectFactory.Get(npc);
            if (burn == null)
                return;

            PlayVictimAnimation(npc);
            burn.Burn(npc);
        }

        /// <summary>
        /// The "dance" - only for the living (a fire hit can kill; the death animation wins).
        /// </summary>
        private static void PlayVictimAnimation(NpcContainer npc)
        {
            if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                return;
            if (npc.Vob.GetAttribute((int)NpcAttribute.HitPoints) <= 0)
                return;

            npc.Props.AnimationQueue.Clear();
            npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(_victimAnimation), npc));
        }
    }
}
