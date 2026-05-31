using Gothic.Core.Adapters.UI.StatusBars;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Npc
{
    public class FightService
    {
        [Inject] private AudioService _audioService;
        [Inject] private VmService _vmService;
        [Inject] private AnimationService _animationService;
        [Inject] private PhysicsService _physicsService;
        [Inject] private NpcHelperService _npcHelperService;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __)
        {
            if (OnHitUpdateHealth(attacker, target))
            {
                target.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                OnDyingChangeAnimation(target);
            }
            else
            {
                OnHitChangeAnimation(target);
                OnHitPlaySound(target);
            }
        }

        /// <summary>
        /// Handles health changes.
        /// Returns true if Npc/Monster is dead.
        /// </summary>
        private bool OnHitUpdateHealth(NpcContainer attacker, NpcContainer target)
        {
            // FIXME - We need to handle this via power and skill level of attacker, not weapon alone.
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);

            var equippedWeapon = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            // FIXME - Instead of 0, use fist value
            // FIXME - Instead of DamageTotal, use calculated NPC/Hero value
            var damage = equippedWeapon?.DamageTotal ?? 0;

            hitPoints -= damage;

            target.Vob.SetAttribute((int)NpcAttribute.HitPoints, hitPoints);

            target.Go.GetComponentInChildren<StatusBarAdapter>(true)?.SetFillAmount(hitPoints, target.Vob.GetAttribute((int)NpcAttribute.HitPointsMax));

            return hitPoints <= 0;
        }

        private void OnDyingChangeAnimation(NpcContainer target)
        {
            // Stop current (attack) animation.
            target.Props.CurrentAction.StopImmediately();
            _physicsService.DisablePhysicsForNpc(target.PrefabProps);

            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.DeadB, target);
            target.Props.AnimationQueue.Enqueue(new PlayAni(new(animName), target));
        }

        private void OnHitChangeAnimation(NpcContainer target)
        {
            // Stop current (attack) animation.
            target.Props.CurrentAction.StopImmediately();

            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.StumbleA, target);
            target.Props.AnimationQueue.Enqueue(new PlayAni(new(animName), target));
        }

        private void OnHitPlaySound(NpcContainer target)
        {
            // In G1, Humans needs to have stumble sound called via Aargh svm.
            var clip = _audioService.GetRandomSoundClip($"SVM_{target.Instance.Voice}_AARGH");

            // Monsters will have their stumble sound inside animations itself.
            if (clip == null)
                return;

            target.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
