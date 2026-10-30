using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// The last step of AI_FinishingMove: the stab into a knocked out enemy (T_1HSFINISH / T_2HSFINISH). Like the engine
    /// (OpenGothic Npc::finishingMove), the enemy dies as the stab starts - also the hero, which ends the game. Only
    /// with a melee weapon drawn, and only while the enemy is still down. Walking there and turning are the actions
    /// before it.
    /// </summary>
    public class FinishingMove : AbstractAnimationAction
    {
        [Inject] private readonly NpcService _npcService;

        private NpcContainer _enemy => Action.Instance0.GetUserData();

        public FinishingMove(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var enemy = _enemy;
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            var isMelee = weaponState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H;
            if (enemy == null || !enemy.IsKnockedOut() || !isMelee)
            {
                IsFinishedFlag = true;
                return;
            }

            var animationName = weaponState == VmGothicEnums.WeaponState.W2H ? "T_2HSFINISH" : "T_1HSFINISH";
            if (PrefabProps.AnimationSystem.PlayAnimation(animationName))
                ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
            else
                IsFinishedFlag = true;

            Logger.Log($"[Fight] {NpcInstance.GetName(NpcNameSlot.Slot0)} finishes off " +
                       $"{enemy.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            // The witnesses learn about the murder from the death (NpcDeathDomain).
            _npcService.Kill(enemy, NpcContainer);
        }
    }
}
