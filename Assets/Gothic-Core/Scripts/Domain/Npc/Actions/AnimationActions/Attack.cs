using System;
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

        private NpcInstance _enemy => Props.EnemyNpc;
        
        private FightAiMove _move;
        
        // e.g., when a Zombie is spawned away, it won't fight, but instead start to walk again. We need to say to the game: you're about 30cm closer than the center of NPC/Monster/Hero.
        // TODO - We might need a more mature alternative in the future if other monsters need different distances.
        private const float _npcMonsterVolumina = 0.3f;

        
        public Attack(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
            if (Vob.GuildTrue < (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM)
            {
                Logger.Log($"AI_Attack() on human NPC (guild={Vob.GuildTrue}) — not yet implemented, skipping.", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            var aiFunctionTemplate = FindAiFunctionTemplate();
            _move = VmCacheService.TryGetFightAiData(aiFunctionTemplate, Vob.FightTactic).GetRandomMove();
            StartAttackAction();
        }

        private string FindAiFunctionTemplate()
        {
            var isInFocus = _npcAiService.ExtNpcCanSeeNpc(NpcInstance, _enemy, false, 30f); // 60° is also assumed by Open Gothic.
            var distance = GetDistance();
            var attackRange = GetAttackRange();
            var isInWRange = distance <= attackRange; // W-Range == Weapon range
            var isInGRange = !isInWRange && distance <= attackRange * 3; // G-Range == Goto range
            // FIXME - We need to handle an "isRunning" state for >MyGRunTo<

            switch ((VmGothicEnums.WeaponState)Vob.FightMode)
            {
                case VmGothicEnums.WeaponState.Bow:
                case VmGothicEnums.WeaponState.CBow:
                case VmGothicEnums.WeaponState.Mage:
                    // Ranged/magic fight AI isn't implemented yet. Behave like a melee fighter so fights continue.
                    Logger.LogWarning($"Ai_Attack() with {(VmGothicEnums.WeaponState)Vob.FightMode} not yet implemented. Using melee behavior.", LogCat.Ai);
                    break;
            }

            // NoWeapon behaves like Fist: an NPC attacked before its AI_DrawWeapon finished still needs a fight move.
            if (isInWRange)
                return isInFocus ? FightConst.AttackActions.MyWFocus : FightConst.AttackActions.MyWNoFocus;
            if (isInGRange)
                return isInFocus ? FightConst.AttackActions.MyGFocus : FightConst.AttackActions.MyGFkNoFocus;

            // FK-Range == Fernkampf range. In G1 nothing is assumed to be farther away than 30m.
            return isInFocus ? FightConst.AttackActions.MyFkFocus : FightConst.AttackActions.MyGFkNoFocus;
        }

        // FIXME - In the future, we need to handle more information than just playing the attack animations. But fine for the first iteration.
        private void StartAttackAction()
        {
            switch (_move)
            {
                case FightAiMove.Wait:
                    // We reuse this flag and close the attack action after 200ms.
                    _npcAiService.ExtAiWait(NpcInstance, 0.2f);
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
                    _npcAiService.ExtAiTurnToNpc(NpcInstance, _enemy);
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
                // The combo attacks (triple/whirl/master) are chained hit windows of the base swing in the
                // original engine. Until attack combos are implemented, the base swing is the closest match.
                case FightAiMove.AttackFront:
                case FightAiMove.AttackTriple:
                case FightAiMove.AttackWhirl:
                case FightAiMove.AttackMaster:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Attack), _move, _enemy);
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

        private float GetDistance()
        {
            return Vector3.Distance(NpcGo.transform.position, _enemy.GetUserData()!.Go.transform.position) - _npcMonsterVolumina;
        }

        /// Fight range is calculated by base range + weapon attack range.
        private float GetAttackRange()
        {
            var baseRange = GameStateService.GuildValues.GetFightRangeBase(Vob.GuildTrue);

            // By default, use Fist range.
            float weaponRange = GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);

            // If NPC has a weapon equipped, then use it's length in G1 (as FIGHT_RANGE_1HA and FIGHT_RANGE_1HS aren't set. Same for 2H).
            // FIXME - Check how G2 is handling ranges. Also via weapon range or guild values?
            var item = VmCacheService.TryGetItemData(Props.CurrentItem);
            if (item != null)
            {
                weaponRange = item.Range;
            }
            else
            {
                switch ((VmGothicEnums.WeaponState)Vob.FightMode)
                {
                    case VmGothicEnums.WeaponState.NoWeapon:
                    case VmGothicEnums.WeaponState.Fist:
                        weaponRange = GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
                        break;
                    case VmGothicEnums.WeaponState.W1H:
                    case VmGothicEnums.WeaponState.W2H:
                    case VmGothicEnums.WeaponState.Bow:
                    case VmGothicEnums.WeaponState.CBow:
                    case VmGothicEnums.WeaponState.Mage:
                        weaponRange = GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
                        Logger.LogWarning($"WeaponState attackrange not yet handled for {(VmGothicEnums.WeaponState)Vob.FightMode}. Assuming fist range.", LogCat.Npc);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return (baseRange + weaponRange) / 100f; // cm -> m
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
