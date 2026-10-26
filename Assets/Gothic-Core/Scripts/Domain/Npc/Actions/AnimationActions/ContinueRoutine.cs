using Reflex.Attributes;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class ContinueRoutine : AbstractAnimationAction
    {
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ConfigService _configService;

        public ContinueRoutine(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var ai = PrefabProps.AiHandler ?? NpcGo.GetComponent<AiHandler>();

            if (ai == null)
            {
                Logger.LogWarning($"[ContinueRoutine] AiHandler null on {NpcGo.name} — skipping routine restart", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            // DeveloperConfig.EnableDeadSkipsRoutine: the dead don't go back to their routine (engine: a dead NPC only
            // runs ZS_Dead) - Ghorri, killed while knocked out, stood up in a T-pose for ZS_StandAround later.
            if (_configService.Dev.EnableDeadSkipsRoutine && Props.BodyState == VmGothicEnums.BodyState.BsDead)
            {
                Logger.Log($"[ContinueRoutine] {NpcGo.name} is dead - no routine", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            ai.ClearState(false);

            // DeveloperConfig.EnableRoutineHolstersWeapon: back to the routine with the weapon away - after
            // ZS_Berzerk_End nothing put it away and the novice meditated with his sword drawn. Queued before the
            // routine's start function.
            if (_configService.Dev.EnableRoutineHolstersWeapon &&
                (VmGothicEnums.WeaponState)Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon)
                _npcAiService.ExtAiUndrawWeapon(NpcInstance);

            var routine = Props.RoutineCurrent;

            // FIXME - Please align logic with StartState.cs handling. (i.e. no call of StartRoutine() directly. Instead handling via ClearState() above.
            // No daily routine (monsters, guards with only start_aistate like the orcs at the orc bridge): back to the
            // start state, like AiHandler.StartNextRoutine - AI_ContinueRoutine threw here.
            if (routine != null)
                ai.StartRoutine(routine.Action, routine.Waypoint);
            else
                ai.StartRoutine(NpcInstance.StartAiState);

            IsFinishedFlag = true;
        }
    }
}
