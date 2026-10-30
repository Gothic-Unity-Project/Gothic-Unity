using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// The NPC casts its drawn spell at its enemy, like the engine (OpenGothic Npc::beginCastSpell, tickCast): the invest
    /// animation (T_MAGRUN_2_FBTCAST for a firebolt) while the mana goes in, then the cast (T_MAGRUN_2_FBTSHOOT or
    /// S_FBTSHOOT) and the spell goes off at the right hand. The caster keeps facing its enemy. Without mana it fails
    /// (T_CASTFAIL). Queued by AI_Attack's attack moves while a spell is drawn.
    /// </summary>
    public class CastSpell : AbstractAnimationAction
    {
        private const float _turnSpeedDegrees = 270f;
        // A cast the scripts never end doesn't block the NPC's AI for good.
        private const float _maxInvestSeconds = 10f;

        [Inject] private readonly MagicService _magicService;

        private NpcContainer _enemy => Action.Instance0.GetUserData();

        private SpellCast _cast;
        private string _letters;
        private string _playedAnimation;
        private bool _isCommitted;

        public CastSpell(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            base.Start();

            var item = Props.ActiveSpellItem;
            var enemy = _enemy;
            if (item == null || enemy?.Go == null || enemy.IsDown())
            {
                IsFinishedFlag = true;
                return;
            }

            _cast = _magicService.BeginCast(NpcContainer, enemy, item, Props.ExpectedSpellInvest);
            if (_cast == null)
            {
                PlayToEnd("T_CASTFAIL");
                return;
            }

            _letters = _magicService.GetAnimationLetters(_cast.SpellId);
            if (_cast.Phase == SpellCastPhase.Investing)
                Play($"T_MAGRUN_2_{_letters}CAST");
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag || _cast == null || _isCommitted)
                return;

            var enemy = _enemy;
            if (enemy?.Go != null)
                FaceEnemy(enemy);

            var phase = _magicService.TickCast(_cast);
            if (phase == SpellCastPhase.Stopped || ActionTime > _maxInvestSeconds)
            {
                _magicService.Stop(_cast);
                StopPlayedAnimation();
                IsFinishedFlag = true;
                return;
            }

            if (phase != SpellCastPhase.Casting)
                return;

            _isCommitted = true;
            StopPlayedAnimation();
            if (!Play($"T_MAGRUN_2_{_letters}SHOOT"))
                Play($"S_{_letters}SHOOT");

            var origin = GetHandPosition();
            var aimPoint = enemy?.Go != null ? FightService.GetAimPoint(enemy) : origin + NpcGo.transform.forward;
            _magicService.CommitCast(_cast, origin, aimPoint - origin);

            var duration = _playedAnimation != null
                ? PrefabProps.AnimationSystem.GetAnimationDuration(_playedAnimation)
                : 0f;
            if (duration > 0f)
                ActionEndEventTime = ActionTime + duration;
            else
                IsFinishedFlag = true;
        }

        protected override void AnimationEnd()
        {
            StopPlayedAnimation();
            // The hands come down (T_FBTSHOOT_2_STAND) instead of staying up in the cast pose.
            if (_isCommitted && _letters != null)
                PrefabProps.AnimationSystem.PlayAnimation($"T_{_letters}SHOOT_2_STAND");
            base.AnimationEnd();
        }

        public override void StopImmediately()
        {
            if (_cast != null && !_isCommitted)
                _magicService.Stop(_cast);
            StopPlayedAnimation();
            base.StopImmediately();
        }

        private bool Play(string animationName)
        {
            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
                return false;

            _playedAnimation = animationName;
            return true;
        }

        private void PlayToEnd(string animationName)
        {
            if (Play(animationName))
                ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
            else
                IsFinishedFlag = true;
        }

        private void StopPlayedAnimation()
        {
            if (_playedAnimation == null)
                return;

            PrefabProps.AnimationSystem.StopAnimation(_playedAnimation);
            _playedAnimation = null;
        }

        private Vector3 GetHandPosition()
        {
            var hand = NpcGo.FindChildRecursively("ZS_RIGHTHAND");
            return hand != null
                ? hand.transform.position + NpcGo.transform.forward * 0.3f
                : NpcGo.transform.position + Vector3.up * 1.4f + NpcGo.transform.forward * 0.5f;
        }

        private void FaceEnemy(NpcContainer enemy)
        {
            var direction = enemy.GetFightPosition() - NpcGo.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            NpcGo.transform.rotation = Quaternion.RotateTowards(NpcGo.transform.rotation,
                Quaternion.LookRotation(direction), _turnSpeedDegrees * Time.deltaTime);
        }
    }
}
