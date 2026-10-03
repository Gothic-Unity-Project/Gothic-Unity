using System;
using Gothic.Core.Const;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
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
        [Inject] private readonly ConfigService _configService;

        private NpcInstance _enemy => Props.EnemyNpc ?? Props.StateOther;

        private FightAiMove _move;
        
        // e.g., when a Zombie is spawned away, it won't fight, but instead start to walk again. We need to say to the game: you're about 30cm closer than the center of NPC/Monster/Hero.
        // TODO - We might need a more mature alternative in the future if other monsters need different distances.
        private const float _npcMonsterVolumina = 0.3f;

        
        public Attack(AnimationAction action, NpcContainer npcData) : base(action, npcData)
        {
        }

        public override void Start()
        {
            if (_enemy == null)
            {
                Logger.LogWarning($"AI_Attack(): no enemy target for NPC guild={Vob.GuildTrue}, skipping.", LogCat.Ai);
                IsFinishedFlag = true;
                return;
            }

            // DeveloperConfig.EnableAiAttackDrawsWeapon: like the engine (OpenGothic Npc::implAttack, "vanilla behavior,
            // required for orcs in G1 orcgraveyard"), AI_Attack without a drawn weapon draws the melee weapon first -
            // a berzerk victim fought with fists. The next AI_Attack of the loop fights with it.
            if (_configService.Dev.EnableAiAttackDrawsWeapon &&
                (VmGothicEnums.WeaponState)Vob.FightMode == VmGothicEnums.WeaponState.NoWeapon)
            {
                _npcAiService.ExtAiDrawWeapon(NpcInstance);
                IsFinishedFlag = true;
                return;
            }

            var aiFunctionTemplate = FindAiFunctionTemplate();
            // Null when the FIGHT VM couldn't be loaded at all (e.g. a mod DAT ZenKit can't parse and
            // no loose fallback existed — see ResourceCacheService.TryGetDaedalusVm). Plain Attack
            // keeps combat functional without move tables, instead of NRE-ing the whole AiHandler.
            var fightAi = VmCacheService.TryGetFightAiData(aiFunctionTemplate, Vob.FightTactic);
            _move = fightAi?.GetRandomMove() ?? FightAiMove.Attack;
            Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} move={_move} fightMode={(VmGothicEnums.WeaponState)Vob.FightMode} tactic={Vob.FightTactic}", LogCat.Ai);
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

            // Bow, CBow, Mage: range values are overridden in GetAttackRange() — fight logic is identical.

            // NoWeapon behaves like Fist: an NPC attacked before its AI_DrawWeapon finished still needs a fight move.
            if (isInWRange)
                return isInFocus ? FightConst.AttackActions.MyWFocus : FightConst.AttackActions.MyWNoFocus;

            // Per vanilla FAI_Human_Mage.d: G-range and plain FK-range tables are Turn-only "close the
            // gap to melee" buffers — they contain no Attack moves. A unit that is itself in a ranged
            // fight mode (Bow/CBow/Mage) instead casts/shoots from FK_FOCUS_FAR, which is the only
            // table with real Attack entries for ranged combat. Without this, a mage/archer beyond
            // W-Range gets stuck cycling Turn/Strafe forever and can never actually fire.
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            var isRangedWeapon = weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow or VmGothicEnums.WeaponState.Mage;
            if (isRangedWeapon)
                return isInFocus ? FightConst.AttackActions.MyFkFocusFar : FightConst.AttackActions.MyFkNoFocusFar;

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
                // Combo attacks chain multiple hit windows in sequence, matching the original Gothic engine.
                // Each PlayAttackAni call enqueues one swing; they play back-to-back before the next AI_Attack loop.
                case FightAiMove.AttackFront:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    break;
                case FightAiMove.AttackTriple:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Attack), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    break;
                case FightAiMove.AttackWhirl:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    break;
                case FightAiMove.AttackMaster:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Attack), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackL), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.AttackR), FightAiMove.Attack, _enemy);
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Attack), FightAiMove.Attack, _enemy);
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
            return Vector3.Distance(NpcGo.transform.position, _enemy.GetUserData().Go.transform.position) - _npcMonsterVolumina;
        }

        /// Fight range is calculated by base range + weapon attack range.
        private float GetAttackRange()
        {
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;

            // Magic and ranged use fixed engagement ranges instead of guild melee values. These are
            // the W-range boundary only (inside it the close-quarters W tables are used; beyond it
            // ranged units fire from the FK_FOCUS_FAR tables) — scaled by the same config multiplier
            // as the hit-connect range so both shrink together.
            var rangedMultiplier = _configService.Dev.RangedCombatRangeMultiplier;
            if (weaponState == VmGothicEnums.WeaponState.Mage)
                return 12f * rangedMultiplier;
            if (weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
                return 20f * rangedMultiplier;

            var baseRange = GameStateService.GuildValues.GetFightRangeBase(Vob.GuildTrue);

            // If NPC has a weapon drawn, use its range; otherwise fall back to fist range (CurrentItem can be the
            // last used item, e.g. a joint).
            var isMeleeWeaponDrawn = weaponState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H;
            var item = isMeleeWeaponDrawn ? VmCacheService.TryGetItemData(Props.CurrentItem) : null;
            float weaponRange;
            if (item != null)
            {
                weaponRange = item.Range;
            }
            else
            {
                switch (weaponState)
                {
                    case VmGothicEnums.WeaponState.NoWeapon:
                    case VmGothicEnums.WeaponState.Fist:
                    case VmGothicEnums.WeaponState.W1H:
                    case VmGothicEnums.WeaponState.W2H:
                        weaponRange = GameStateService.GuildValues.GetFightRangeFist(Vob.GuildTrue);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Minimum 1.5m — prevents NPCs with zero guild fight values from looping in G-Range forever.
            return Mathf.Max((baseRange + weaponRange) / 100f, 1.5f);
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
