using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_ReadySpell: the NPC draws a spell (OpenGothic AiQueue::aiReadySpell, Npc::drawSpell) and raises its hands for
    /// magic (t_Move_2_MagMove). With a spell already drawn, it only switches and takes the new invest - the scripts call
    /// it on every pass of the fight loop (B_DrawSpell).
    /// </summary>
    public class ReadySpell : AbstractAnimationAction
    {
        [Inject] private readonly MagicService _magicService;

        private int _spellId => Action.Int0;
        private int _investMana => Action.Int1;

        public ReadySpell(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var wasMagicDrawn = (VmGothicEnums.WeaponState)Vob.FightMode == VmGothicEnums.WeaponState.Mage;
            if (!_magicService.ReadySpell(NpcContainer, _spellId, _investMana) || wasMagicDrawn)
            {
                IsFinishedFlag = true;
                return;
            }

            const string animationName = "t_Move_2_MagMove";
            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                IsFinishedFlag = true;
                return;
            }

            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
        }
    }
}
