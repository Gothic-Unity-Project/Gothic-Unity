using System;
using System.Numerics;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class Attack : AbstractAnimationAction
    {
        [Inject] private readonly NpcAiService _npcAiService;

        private NpcInstance _enemy => (NpcInstance)GameStateService.GothicVm.GlobalVictim;
        
        private FightAiMove _move;
        
        // e.g., when a Zombie is spawned away, it won't fight, but instead start to walk again. We need to say to the game: you're about 30cm closer than the center of NPC/Monster/Hero.
        // TODO - We might need a more mature alternative in the future if other monsters need different distances.
        private const float _npcMonsterVolumina = 0.3f;

        
        public Attack(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
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
            var isInFKRange = !isInWRange && !isInGRange; // FK-Range == Fernkampf range; Yes, in G1 it's assumed that nothing is farther away than 30 m, but I don't expect any AI_Attack() call to be at this range.
            var isRunning = false; // FIXME - We need to handle this for >MyGRunTo<

            switch ((VmGothicEnums.WeaponState)Vob.FightMode)
            {
                case VmGothicEnums.WeaponState.Fist:
                case VmGothicEnums.WeaponState.W1H:
                case VmGothicEnums.WeaponState.W2H:
                    if (isInWRange)
                        return isInFocus ? FightConst.AttackActions.MyWFocus : FightConst.AttackActions.MyWNoFocus;
                    if (isInGRange)
                        return isInFocus ? FightConst.AttackActions.MyGFocus : FightConst.AttackActions.MyGFkNoFocus;
                    else
                        return isInFocus ? FightConst.AttackActions.MyFkFocus : FightConst.AttackActions.MyGFkNoFocus;
                case VmGothicEnums.WeaponState.NoWeapon:
                case VmGothicEnums.WeaponState.Bow:
                case VmGothicEnums.WeaponState.CBow:
                case VmGothicEnums.WeaponState.Mage:
                default:
                    throw new ArgumentOutOfRangeException();
            }
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
                    _npcAiService.ExtAiTurnToNpc(NpcInstance, _enemy);
                    break;
                // Some attacks have no action. Therefore TryGetFightAiData() returns Nop as fallback.
                case FightAiMove.Nop:
                    break;
                case FightAiMove.RunBack:
                case FightAiMove.JumpBack:
                case FightAiMove.AttackSide:
                case FightAiMove.AttackFront:
                case FightAiMove.AttackTriple:
                case FightAiMove.AttackWhirl:
                case FightAiMove.AttackMaster:
                case FightAiMove.TurnToHit:
                case FightAiMove.Parry:
                case FightAiMove.StandUp:
                case FightAiMove.WaitLonger:
                case FightAiMove.WaitExt:
                    Logger.LogError($"Ai_Attack() type >{_move}< not yet handled. Skipping...", LogCat.Ai);
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
            float weaponRange = GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);;

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

            return (baseRange + weaponRange) / 100f; // m -> cm
        }

        /// <summary>
        /// Short cut method
        /// </summary>
        private string GetAnimName(VmGothicEnums.AnimationType type)
        {
            return AnimationService.GetAnimationName(type, NpcContainer);
        }
    }
}
