using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class Attack : AbstractAnimationAction
    {
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly FightService _fightService;

        // AI_Attack runs on every pass of ZS_*_Attack_Loop: its warnings are logged once per NPC.
        private static readonly HashSet<int> _missingEnemyLogged = new();

        // Like the engine, without an enemy set the state's other (ZS_Attack started against it) is fought.
        private NpcInstance _enemy => Props.EnemyNpc ?? Props.StateOther;

        // The fight AI's focus (OpenGothic: 30 degrees each side).
        private const float _focusAngleDegrees = 30f;
        // Fight turns stop inside the focus angle (30) with this tolerance.
        private const float _fightTurnToleranceDegrees = 20f;
        
        private FightAiMove _move;
        

        
        public Attack(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
            // Without an enemy (or one that was never built), the action would never end and the NPC's AI would stand
            // still for good. The state's loop ends or attacks again once it found one.
            if (_enemy?.GetUserData()?.Go == null)
            {
                if (_missingEnemyLogged.Add(NpcInstance.Index))
                    Logger.LogWarning($"AI_Attack() of {Vob.Name} without enemy in the world - skipping.", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            // Like the engine (OpenGothic Npc::implAttack, "vanilla behavior, required for orcs in G1 orcgraveyard"),
            // AI_Attack without a drawn weapon draws the melee weapon first. The next AI_Attack of the loop fights.
            if ((VmGothicEnums.WeaponState)Vob.FightMode == VmGothicEnums.WeaponState.NoWeapon)
            {
                _npcAiService.ExtAiDrawWeapon(NpcInstance);
                IsFinishedFlag = true;
                return;
            }

            var aiFunctionTemplate = FindAiFunctionTemplate();
            // Null when the FIGHT VM couldn't be loaded at all (e.g. a mod DAT ZenKit can't parse and
            // no loose fallback existed - see ResourceCacheService.TryGetDaedalusVm). Plain Attack
            // keeps combat functional without move tables, instead of NRE-ing the whole AiHandler.
            var fightAi = VmCacheService.TryGetFightAiData(aiFunctionTemplate, Vob.FightTactic);
            _move = fightAi?.GetRandomMove() ?? FightAiMove.Attack;
            StartAttackAction();
        }

        private string FindAiFunctionTemplate()
        {
            // 60° is also assumed by OpenGothic.
            var isInFocus = _npcAiService.ExtNpcCanSeeNpc(NpcInstance, _enemy, false, 30f);
            var distance = GetDistance();

            // Like the engine (OpenGothic FightAlgo::fillQueue), archers and crossbowmen use the far ranged moves only:
            // aim and shoot within their weapon's range, else close in or turn.
            // The focus is an angle, not a free line of sight (OpenGothic FightAlgo::isInFocusAngle): behind a pillar
            // the archer froze, only turning (found on BoroBongo's branch).
            if (IsRangedWeaponDrawn())
            {
                return distance <= _fightService.GetAttackReach(NpcContainer) && IsInFocusAngle()
                    ? FightConst.AttackActions.MyFkFocusFar
                    : FightConst.AttackActions.MyFkNoFocusFar;
            }

            var attackRange = _fightService.GetMeleeReach(NpcContainer);
            var isInWRange = distance <= attackRange; // W-Range == Weapon range
            var isInGRange = !isInWRange && distance <= attackRange * 3; // G-Range == Goto range
            // FIXME - We need to handle an "isRunning" state for >MyGRunTo<

            // Magic fight AI isn't implemented yet: casters move like melee fighters, so fights continue
            // (AttackPlayAni logs it once per NPC - here it was every AI_Attack).

            // NoWeapon behaves like Fist: an NPC attacked before its AI_DrawWeapon finished still needs a fight move.
            if (isInWRange)
                return isInFocus ? FightConst.AttackActions.MyWFocus : FightConst.AttackActions.MyWNoFocus;
            if (isInGRange)
                return isInFocus ? FightConst.AttackActions.MyGFocus : FightConst.AttackActions.MyGFkNoFocus;

            // FK-Range == Fernkampf range. In G1 nothing is assumed to be farther away than 30m.
            return isInFocus ? FightConst.AttackActions.MyFkFocus : FightConst.AttackActions.MyGFkNoFocus;
        }

        private void StartAttackAction()
        {
            // The combo isn't continued: its animation stops (the engine ends an attack at its hit end), the next swing
            // starts a new combo.
            var combo = Props.AttackCombo;
            if (combo.ContinueAnimation != null && _move != FightAiMove.Attack)
            {
                PrefabProps.AnimationSystem.StopAnimation(combo.ContinueAnimation);
                combo.ContinueAnimation = null;
                combo.Length = 0;
            }

            switch (_move)
            {
                case FightAiMove.Wait:
                    // We reuse this flag and close the attack action after 200ms.
                    _npcAiService.ExtAiWait(NpcInstance, 0.2f);
                    break;
                // A ranged weapon shoots on every attack move.
                case FightAiMove.Attack or FightAiMove.AttackSide or FightAiMove.AttackFront or FightAiMove.AttackTriple
                    or FightAiMove.AttackWhirl or FightAiMove.AttackMaster when IsRangedWeaponDrawn():
                    Props.AnimationQueue.Enqueue(new ShootRanged(new AnimationAction(instance0: _enemy), NpcContainer));
                    break;
                case FightAiMove.Attack:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Attack), _move, _enemy);
                    break;
                case FightAiMove.Strafe:
                    if (Random.Range(0, 2) == 0)
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.MoveL), _move, _enemy);
                    else
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.MoveR), _move, _enemy);
                    break;
                case FightAiMove.Run:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Move), _move, _enemy);
                    break;
                case FightAiMove.Turn:
                case FightAiMove.TurnToHit:
                    _npcAiService.ExtAiTurnToNpcInFight(NpcInstance, _enemy, _fightTurnToleranceDegrees);
                    break;
                // Some attacks have no action. Therefore TryGetFightAiData() returns Nop as fallback.
                case FightAiMove.Nop:
                    break;
                case FightAiMove.AttackSide:
                    if (Random.Range(0, 2) == 0)
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), _move, _enemy);
                    else
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), _move, _enemy);
                    break;
                // Combo moves chain several swings, played back to back before the next AI_Attack of the loop.
                case FightAiMove.AttackFront:
                    QueueSwings(VmGothicEnums.AnimationType.AttackL, VmGothicEnums.AnimationType.AttackR);
                    break;
                case FightAiMove.AttackTriple:
                    QueueSwings(VmGothicEnums.AnimationType.Attack, VmGothicEnums.AnimationType.AttackL,
                        VmGothicEnums.AnimationType.AttackR);
                    break;
                case FightAiMove.AttackWhirl:
                    QueueSwings(VmGothicEnums.AnimationType.AttackL, VmGothicEnums.AnimationType.AttackR,
                        VmGothicEnums.AnimationType.AttackL, VmGothicEnums.AnimationType.AttackR);
                    break;
                case FightAiMove.AttackMaster:
                    QueueSwings(VmGothicEnums.AnimationType.AttackL, VmGothicEnums.AnimationType.AttackR,
                        VmGothicEnums.AnimationType.Attack, VmGothicEnums.AnimationType.AttackL,
                        VmGothicEnums.AnimationType.AttackR, VmGothicEnums.AnimationType.Attack);
                    break;
                case FightAiMove.Parry:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackBlock), _move, _enemy);
                    break;
                // No run-backwards loop exists in the assets; the parade jump-back is the closest match for both.
                case FightAiMove.RunBack:
                case FightAiMove.JumpBack:
                    _npcAiService.PlayAttackAni(NpcInstance, GetJumpBackAnimName(), _move, _enemy);
                    break;
                case FightAiMove.StandUp:
                    _npcAiService.ExtAiStandUp(NpcInstance);
                    break;
                // Wait durations relative to FightAiMove.Wait (0.2s); the original engine scales them similarly.
                case FightAiMove.WaitLonger:
                    _npcAiService.ExtAiWait(NpcInstance, 0.4f);
                    break;
                case FightAiMove.WaitExt:
                    _npcAiService.ExtAiWait(NpcInstance, 0.8f);
                    break;
                default:
                    Logger.LogError("No action for Ai_Attack() selected. Missing path in logic!", LogCat.Ai);
                    break;
            }
            
            IsFinishedFlag = true;
        }

        private bool IsInFocusAngle()
        {
            var toEnemy = _enemy.GetUserData().Go.transform.position - NpcGo.transform.position;
            toEnemy.y = 0f;
            var forward = NpcGo.transform.forward;
            forward.y = 0f;
            return toEnemy.sqrMagnitude < 0.0001f || Vector3.Angle(forward, toEnemy) <= _focusAngleDegrees;
        }

        private bool IsRangedWeaponDrawn()
        {
            return ((VmGothicEnums.WeaponState)Vob.FightMode).IsRanged();
        }

        private void QueueSwings(params VmGothicEnums.AnimationType[] swings)
        {
            foreach (var swing in swings)
                _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(swing), FightAiMove.Attack, _enemy);
        }

        /// <summary>
        /// The distance between the bodies (the fight ranges are measured from them).
        /// </summary>
        private float GetDistance()
        {
            return _fightService.GetFightDistance(NpcContainer, _enemy.GetUserData());
        }

        /// <summary>
        /// Short cut method
        /// </summary>
        private string GetAnimName(VmGothicEnums.AnimationType type)
        {
            return AnimationService.GetAnimationName(type, NpcContainer);
        }

        private string GetJumpBackAnimName()
        {
            var fightMode = (VmGothicEnums.WeaponState)Vob.FightMode;

            // There is no weaponless jump-back animation - the fist one is used.
            if (fightMode == VmGothicEnums.WeaponState.NoWeapon)
                fightMode = VmGothicEnums.WeaponState.Fist;

            var prefix = AnimationService.GetWeaponAnimationPrefix(fightMode);

            return $"t_{prefix}ParadeJumpB";
        }
    }
}
