using System;
using Gothic.Core.Const;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Gothic.Core.Adapters.Properties;
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
        [Inject] private readonly ProjectileService _projectileService;
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

            if (TryGiveUpOutOfRangedReach())
                return;

            var tactic = UpdateSpellMonsterMelee(out var meleeTactic) ? meleeTactic : Vob.FightTactic;
            // DeveloperConfig.EnableNpcPrehitReaction: the hero starts a hit - the FA_ENEMY_(STORM)PREHIT table (parry,
            // jump back, strafe). A tactic without one fights on with its normal table.
            var prehitTemplate = FindPrehitTemplate();
            var prehitAi = prehitTemplate != null ? VmCacheService.TryGetFightAiData(prehitTemplate, tactic) : null;
            var prehitMove = prehitAi?.GetRandomMove() ?? FightAiMove.Nop;
            var aiFunctionTemplate = prehitMove != FightAiMove.Nop ? prehitTemplate : FindAiFunctionTemplate();
            // Null when the FIGHT VM couldn't be loaded at all (e.g. a mod DAT ZenKit can't parse and
            // no loose fallback existed — see ResourceCacheService.TryGetDaedalusVm). Plain Attack
            // keeps combat functional without move tables, instead of NRE-ing the whole AiHandler.
            var fightAi = prehitMove != FightAiMove.Nop ? prehitAi : VmCacheService.TryGetFightAiData(aiFunctionTemplate, tactic);
            _move = prehitMove != FightAiMove.Nop ? prehitMove : fightAi?.GetRandomMove() ?? FightAiMove.Attack;
            if (prehitMove != FightAiMove.Nop)
                Logger.Log($"[Prehit] {NpcInstance.GetName(NpcNameSlot.Slot0)} reacts to the hero's swing: " +
                           $"{string.Format(prehitTemplate, tactic)} -> {_move}", LogCat.Fight);
            _move = AvoidBlockedShot(_move);
            Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} move={_move} fightMode={(VmGothicEnums.WeaponState)Vob.FightMode} tactic={tactic}" +
                       (NpcContainer.IsSpellMonsterInMelee ? " (spell monster in melee)" : ""), LogCat.Ai);
            StartAttackAction();
        }

        /// <summary>
        /// DeveloperConfig.EnableRangedGiveUp: an archer/crossbowman/mage whose target, the hero, got out of the reach
        /// of its shots (RangedAttackReachMeters * RangedCombatRangeMultiplier) gives up - the attack state ends like
        /// the script's own "...Gegner außerhalb Fernkampfreichweite" (HAI_DIST_ABORT_RANGED, 35 m - beyond our reach,
        /// so it stood there shooting into nothing). ZS_Attack_End puts the weapon away (B_RemoveWeapon) and the NPC
        /// goes back to its routine; the give-up cooldown (EnableChaseGiveUpCooldown) keeps it from coming back at once.
        /// </summary>
        private bool TryGiveUpOutOfRangedReach()
        {
            if (!_configService.Dev.EnableRangedGiveUp || Props.CurrentLoopState != NpcProperties.LoopState.Loop)
                return false;

            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            if (weaponState is not (VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow or
                VmGothicEnums.WeaponState.Mage))
                return false;

            var hero = GameStateService.GothicVm.GlobalHero as NpcInstance;
            if (hero == null || _enemy.Index != hero.Index)
                return false;

            var reach = FightConst.RangedAttackReachMeters * _configService.Dev.RangedCombatRangeMultiplier;
            var distance = GetDistance();
            if (distance <= reach)
                return false;

            Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)}: hero out of {weaponState} reach " +
                       $"({distance:F1} m > {reach:F1} m) - giving up, weapon away, back to the routine", LogCat.Ai);
            Props.AnimationQueue.Clear();
            Props.CurrentLoopState = NpcProperties.LoopState.End;
            IsFinishedFlag = true;
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableSpellMonsterMelee: Mroczne Tajemnice's crawlers freeze with their web, then bite
        /// (B_SelectWeapon picks the mage or the melee crawler once). The mage crawler kept casting at a frozen hero -
        /// a freeze lock. With its target frozen or right in front of it, it fights with its melee table
        /// (FAI_MINECRAWLER) and bites; farther away it casts again. Mods with such monsters: the finite list below.
        /// </summary>
        private bool UpdateSpellMonsterMelee(out int meleeTactic)
        {
            meleeTactic = -1;
            var wasInMelee = NpcContainer.IsSpellMonsterInMelee;
            NpcContainer.IsSpellMonsterInMelee = false;
            if (!_configService.Dev.EnableSpellMonsterMelee ||
                (VmGothicEnums.WeaponState)Vob.FightMode != VmGothicEnums.WeaponState.Mage)
                return false;

            var vm = GameStateService.GothicVm;
            var crawlerGuild = vm.GetSymbolByName("GIL_MINECRAWLER");
            var crawlerTactic = vm.GetSymbolByName("FAI_MINECRAWLER");
            if (crawlerGuild == null || crawlerTactic == null || NpcInstance.Guild != crawlerGuild.GetInt(0))
                return false;

            var target = _enemy.GetUserData();
            var isFrozen = NpcHelperService.IsMagicFrozen(target);
            var meleeRange = GetAttackRange(isMelee: true);
            if (!isFrozen && GetDistance() > meleeRange * _spellMonsterMeleeRangeFactor)
            {
                if (wasInMelee)
                    Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} back to casting", LogCat.Ai);
                return false;
            }

            meleeTactic = crawlerTactic.GetInt(0);
            NpcContainer.IsSpellMonsterInMelee = true;
            if (!wasInMelee)
                Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} closes in to bite " +
                           $"({(isFrozen ? "target frozen" : "target close")})", LogCat.Ai);
            return true;
        }

        private const float _spellMonsterMeleeRangeFactor = 2f;

        /// <summary>
        /// OpenGothic FightAlgo::fillQueue: the enemy is in his "prehit" (an attack started, before its hit frame), in
        /// his weapon range, both face each other -> FA_ENEMY_STORMPREHIT while he runs, else FA_ENEMY_PREHIT.
        /// Only the hero has a prehit here - his swing in VR (GlobalEventDispatcher.HeroMeleeSwing).
        /// </summary>
        private string FindPrehitTemplate()
        {
            if (!_configService.Dev.EnableNpcPrehitReaction)
                return null;

            var enemy = _enemy?.GetUserData();
            if (enemy?.Go == null || enemy.PrefabProps == null || !enemy.PrefabProps.IsHero() ||
                Time.time > enemy.Props.MeleePrehitUntil)
                return null;

            if (GetDistance() > FightConst.HeroPrehitRangeMeters)
                return null;

            var heroFacing = enemy.PrefabProps.Head != null ? enemy.PrefabProps.Head : enemy.Go.transform;
            if (!IsInAngle(NpcGo.transform, enemy.Go.transform.position, FightConst.FocusAngle) ||
                !IsInAngle(heroFacing, NpcGo.transform.position, FightConst.HeroFocusAngle))
                return null;

            return enemy.Props.BodyState == VmGothicEnums.BodyState.BsRun
                ? FightConst.AttackActions.EnemyStormPrehit
                : FightConst.AttackActions.EnemyPrehit;
        }

        /// <summary>
        /// Horizontal angle between where the transform looks and the target.
        /// </summary>
        public static bool IsInAngle(Transform from, Vector3 target, float maxAngle)
        {
            var forward = Vector3.ProjectOnPlane(from.forward, Vector3.up);
            var direction = Vector3.ProjectOnPlane(target - from.position, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f || direction.sqrMagnitude < 0.0001f)
                return true;
            return Vector3.Angle(forward, direction) <= maxAngle;
        }

        private string FindAiFunctionTemplate()
        {
            var isInFocus = _npcAiService.ExtNpcCanSeeNpc(NpcInstance, _enemy, false, 30f); // 60° is also assumed by Open Gothic.
            var distance = GetDistance();
            var attackRange = GetAttackRange(NpcContainer.IsSpellMonsterInMelee);
            var isInWRange = distance <= attackRange; // W-Range == Weapon range
            var isInGRange = !isInWRange && distance <= attackRange * 3; // G-Range == Goto range
            // FIXME - We need to handle an "isRunning" state for >MyGRunTo<

            // Bow, CBow, Mage: range values are overridden in GetAttackRange() — fight logic is identical.

            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            var isRangedWeapon = !NpcContainer.IsSpellMonsterInMelee &&
                                 weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow or VmGothicEnums.WeaponState.Mage;

            // DeveloperConfig.EnableRangedFocusIgnoresCover: an archer facing the hero behind a pillar counts him as in
            // focus - the head-to-head line is blocked, so he only turned (already facing him) and froze. In focus, the
            // attack move reaches AvoidBlockedShot, which steps aside to a free line of shot. (Before the W-Range check:
            // bows/crossbows have a 20 m W-Range, so a close archer uses the W tables.)
            if (weaponState is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow && !isInFocus &&
                !NpcContainer.IsSpellMonsterInMelee && _configService.Dev.EnableRangedFocusIgnoresCover &&
                _configService.Dev.EnableNpcPhysicalProjectiles && _enemy?.GetUserData()?.Go != null &&
                IsInAngle(NpcGo.transform, _enemy.GetUserData().Go.transform.position, _rangedFocusAngle))
                isInFocus = true;

            // NoWeapon behaves like Fist: an NPC attacked before its AI_DrawWeapon finished still needs a fight move.
            if (isInWRange)
                return isInFocus ? FightConst.AttackActions.MyWFocus : FightConst.AttackActions.MyWNoFocus;

            // Per vanilla FAI_Human_Mage.d: G-range and plain FK-range tables are Turn-only "close the
            // gap to melee" buffers — they contain no Attack moves. A unit that is itself in a ranged
            // fight mode (Bow/CBow/Mage) instead casts/shoots from FK_FOCUS_FAR, which is the only
            // table with real Attack entries for ranged combat. Without this, a mage/archer beyond
            // W-Range gets stuck cycling Turn/Strafe forever and can never actually fire.
            if (isRangedWeapon)
                return isInFocus ? FightConst.AttackActions.MyFkFocusFar : FightConst.AttackActions.MyFkNoFocusFar;

            if (isInGRange)
                return isInFocus ? FightConst.AttackActions.MyGFocus : FightConst.AttackActions.MyGFkNoFocus;

            // FK-Range == Fernkampf range. In G1 nothing is assumed to be farther away than 30m.
            return isInFocus ? FightConst.AttackActions.MyFkFocus : FightConst.AttackActions.MyGFkNoFocus;
        }

        // FIXME - In the future, we need to handle more information than just playing the attack animations. But fine for the first iteration.
        // Shots in a row without a free line before the archer runs closer instead of stepping aside.
        private const int _maxBlockedShotStrafes = 2;
        // Fight turns stop inside the focus angle (30) with this tolerance.
        private const float _fightTurnToleranceDegrees = 20f;
        // Same angle as the focus check in FindAiFunctionTemplate (ExtNpcCanSeeNpc's fov).
        private const float _rangedFocusAngle = 30f;
        // -1 left, 1 right, 0 random - the side a blocked archer steps to.
        private int _strafeSide;

        /// <summary>
        /// DeveloperConfig.EnableNpcPhysicalProjectiles: an archer without a free line to the target (the hero behind a
        /// lantern post) steps aside to get one, after a few tries runs closer - instead of shooting into the post.
        /// </summary>
        private FightAiMove AvoidBlockedShot(FightAiMove move)
        {
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            if (!_configService.Dev.EnableNpcPhysicalProjectiles ||
                weaponState is not (VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow) ||
                move is not (FightAiMove.Attack or FightAiMove.AttackSide or FightAiMove.AttackFront or
                    FightAiMove.AttackTriple or FightAiMove.AttackWhirl or FightAiMove.AttackMaster))
                return move;

            var enemy = _enemy?.GetUserData();
            if (_projectileService.HasLineOfShot(NpcContainer, enemy))
            {
                Props.BlockedShotCount = 0;
                return move;
            }

            Props.BlockedShotCount++;
            _strafeSide = _projectileService.FindClearStrafeSide(NpcContainer, enemy);
            var newMove = _strafeSide != 0 && Props.BlockedShotCount <= _maxBlockedShotStrafes
                ? FightAiMove.Strafe
                : FightAiMove.Run;
            Logger.Log($"[Attack] {NpcInstance.GetName(NpcNameSlot.Slot0)} has no free line of shot " +
                       $"({Props.BlockedShotCount}x) - {newMove}" +
                       (newMove == FightAiMove.Strafe ? (_strafeSide < 0 ? " left" : " right") : "") + " instead",
                LogCat.Fight);
            return newMove;
        }

        private void StartAttackAction()
        {
            // DeveloperConfig.EnableEngineNpcCombo: the combo isn't continued - its animation stops (the engine ends an
            // attack at its hit end).
            if (Props.ComboContinueAnimation != null && _move != FightAiMove.Attack)
            {
                PrefabProps.AnimationSystem.StopAnimation(Props.ComboContinueAnimation);
                Props.ComboContinueAnimation = null;
            }

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
                    // A blocked archer steps to the side with a free line of shot (AvoidBlockedShot).
                    if (_strafeSide < 0 || (_strafeSide == 0 && Random.Range(0, 2) == 0))
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.MoveL), _move, _enemy);
                    else
                        _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.MoveR), _move, _enemy);
                    break;
                case FightAiMove.Run:
                    _npcAiService.PlayAttackAni(NpcInstance, GetAnimName(VmGothicEnums.AnimationType.Move), _move, _enemy);
                    break;
                case FightAiMove.Turn:
                case FightAiMove.TurnToHit:
                    if (_configService.Dev.EnableFightTurnTolerance)
                        _npcAiService.ExtAiTurnToNpcInFight(NpcInstance, _enemy, _fightTurnToleranceDegrees);
                    else
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
        private float GetAttackRange(bool isMelee = false)
        {
            // A spell monster in melee (EnableSpellMonsterMelee) bites with its fist range.
            var weaponState = isMelee ? VmGothicEnums.WeaponState.Fist : (VmGothicEnums.WeaponState)Vob.FightMode;

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
