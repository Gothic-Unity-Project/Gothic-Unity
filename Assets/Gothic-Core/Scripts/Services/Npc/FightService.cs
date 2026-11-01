using System.Collections;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// What a melee hit does, like the engine (OpenGothic Npc::takeDamage, Npc::checkHealth): damage
    /// (MeleeDamageDomain), a stumble, the reactions of the victim and witnesses, and at 0 hit points a knockout or
    /// death (NpcService.Kill).
    /// </summary>
    public class FightService
    {
        // Seconds the knocked out hero lies down before he can move again.
        public const int DamageFly = 1 << 4;

        private const float _heroKnockoutSeconds = 10f;
        // In m: casters cast from this far (OpenGothic MaxMagRange).
        private const float _magicRange = 35f;
        // Arrows aim at the VR hero's chest, this far below his head.
        private const float _heroChestBelowHead = 0.4f;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly MeshService _meshService;

        private readonly MeleeDamageDomain _meleeDamageDomain = new MeleeDamageDomain().Inject();
        private readonly RangedDamageDomain _rangedDamageDomain = new RangedDamageDomain().Inject();
        private ArrowFlightDomain _arrowFlightDomain;

        // Counts the hero's knockouts and loaded games: a recovery from an older one doesn't unlock the player.
        private int _heroKnockoutId;

        public void Init()
        {
            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.LoadGameStart.AddListener(() => _heroKnockoutId++);
        }

        /// <summary>
        /// How far the NPC's melee weapon (or fists) reaches - the fight AI attacks from there, a swing hits there.
        /// </summary>
        public float GetMeleeReach(NpcContainer npc)
        {
            return _meleeDamageDomain.GetMeleeReach(npc);
        }

        /// <summary>
        /// The distance between the bodies - compared with GetMeleeReach for the fight AI, the run and the hit.
        /// </summary>
        public float GetFightDistance(NpcContainer attacker, NpcContainer target)
        {
            return _meleeDamageDomain.GetFightDistance(attacker, target);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __, ItemInstance weapon)
        {
            if (!CanBeHit(attacker, target) || TryFinishOff(attacker, target))
                return;

            var damage = _meleeDamageDomain.CalculateDamage(attacker, target, weapon);
            ApplyDamage(attacker, target, damage);

            // A troll's blow throws its victim away.
            var damageType = weapon?.DamageType ?? attacker.Instance.DamageType;
            if (damage > 0 && (damageType & DamageFly) != 0)
                ThrowAway(attacker, target);
        }

        /// <summary>
        /// A DAM_FLY hit (Windfist, Stormfist, a troll's blow) throws the victim away from the attacker, like the engine
        /// (OpenGothic Npc::takeDamage -> MoveAlgo::accessDamFly). Not the VR hero, nor the down.
        /// </summary>
        public void ThrowAway(NpcContainer attacker, NpcContainer victim)
        {
            if (victim?.Go == null || attacker?.Go == null || victim.IsHero() || victim.IsDown() ||
                victim.Vob.GetAttribute((int)NpcAttribute.HitPoints) <= 0)
                return;

            var away = victim.Go.transform.position - attacker.Go.transform.position;
            victim.Props.AnimationQueue.Clear();
            victim.Props.AnimationQueue.Enqueue(new FlyBack(new AnimationAction("FlyBack"), victim, away));
            Logger.Log($"[Fight] {victim.Instance.GetName(NpcNameSlot.Slot0)} is thrown away.", LogCat.Fight);
        }

        /// <summary>
        /// How far the NPC attacks from with what it has drawn: a bow or crossbow from its reference range (the fight
        /// AI's FK focus moves, like OpenGothic FightAlgo::weaponRange), a spell from the magic range, anything else from
        /// its melee reach.
        /// </summary>
        public float GetAttackReach(NpcContainer npc)
        {
            var weaponState = (VmGothicEnums.WeaponState)npc.Vob.FightMode;
            if (weaponState == VmGothicEnums.WeaponState.Mage)
                return _magicRange;

            return weaponState.IsRanged() ? _rangedDamageDomain.ReferenceRange : GetMeleeReach(npc);
        }

        /// <summary>
        /// An arrow or bolt reached the target (ArrowFlight). Like the engine, it hits by the shooter's chance at that
        /// distance and always kills instead of knocking out (MeleeDamageDomain.IsKnockedOutNotKilled).
        /// </summary>
        /// <param name="distance">How far the arrow flew, in m.</param>
        public void OnRangedHit(NpcContainer shooter, NpcContainer target, ItemInstance weapon, float distance)
        {
            if (!CanBeHit(shooter, target))
                return;

            if (!_rangedDamageDomain.RollHit(shooter, weapon, distance))
            {
                Logger.Log($"[Fight] {shooter.Instance.GetName(NpcNameSlot.Slot0)} misses " +
                           $"{target.Instance.GetName(NpcNameSlot.Slot0)} at {distance:0.0} m", LogCat.Fight);
                return;
            }

            // Like the engine, an arrow doesn't finish off whoever lies on the ground.
            if (target.IsDown())
                return;

            ApplyDamage(shooter, target, _rangedDamageDomain.CalculateDamage(shooter, target, weapon));
        }

        /// <summary>
        /// A hurtful spell hit the target (MagicService). Like the engine (OpenGothic Npc::takeDamage), a spell kills at
        /// 0 hit points unless the scripts say it doesn't (COLL_DONTKILL: G2's shock spells) - then the knockout rule of
        /// melee hits applies. A spell without damage (fear, ice cube) leaves the reaction to the victim's
        /// PERC_ASSESSMAGIC: its damage reaction attacked the caster instead of fleeing.
        /// </summary>
        public void OnSpellHit(NpcContainer caster, NpcContainer target, int damage, bool canKill)
        {
            if (damage <= 0 || !CanBeHit(caster, target) || target.IsDown())
                return;

            ApplyDamage(caster, target, damage, !canKill);
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::shootBow, World::shootBullet): one arrow or bolt of the ranged weapon's
        /// munition leaves the inventory and flies on a ballistic arc at 30 m/s towards the target (ArrowFlight).
        /// Without munition, nothing is shot.
        /// </summary>
        /// <returns>True if an arrow was shot.</returns>
        public bool Shoot(NpcContainer shooter, NpcContainer target)
        {
            var weapon = _npcHelperService.ExtNpcGetEquippedRangedWeapon(shooter.Instance);
            if (weapon == null || target?.Go == null || shooter.Go == null)
                return false;

            var munition = _vmCacheService.TryGetItemData(weapon.Munition);
            if (munition == null || _npcInventoryService.ExtNpcHasItems(shooter.Instance, weapon.Munition) <= 0)
            {
                Logger.LogWarning($"[Fight] {shooter.Instance.GetName(NpcNameSlot.Slot0)} has no munition for " +
                                  $"{weapon.Name}", LogCat.Fight);
                return false;
            }

            _npcInventoryService.ExtRemoveInvItems(shooter.Instance, weapon.Munition, 1);
            Logger.Log($"[Fight] {shooter.Instance.GetName(NpcNameSlot.Slot0)} shoots at " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)} with {weapon.Name}", LogCat.Fight);

            var start = GetShotStart(shooter);
            var arrow = CreateArrow(munition, start);
            _arrowFlightDomain ??= new ArrowFlightDomain(OnRangedHit).Inject();
            _arrowFlightDomain.Launch(arrow, shooter, target, weapon, start, GetAimPoint(target));
            return true;
        }

        /// <summary>
        /// Where an arrow aims at: the hero's chest, an NPC's body (BIP01).
        /// </summary>
        public static Vector3 GetAimPoint(NpcContainer target)
        {
            if (target.IsHero() && target.PrefabProps?.Head != null)
                return target.PrefabProps.Head.position + Vector3.down * _heroChestBelowHead;

            var body = target.PrefabProps?.Bip01;
            return body != null ? body.position : target.Go.transform.position + Vector3.up * 1f;
        }

        private static Vector3 GetShotStart(NpcContainer shooter)
        {
            var hand = shooter.Go.FindChildRecursively("ZS_RIGHTHAND");
            var forward = shooter.Go.transform.forward;
            return (hand != null ? hand.transform.position : shooter.Go.transform.position + Vector3.up * 1.4f) +
                   forward * 0.3f;
        }

        private GameObject CreateArrow(ItemInstance munition, Vector3 position)
        {
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(munition.Visual, false);
            var arrow = mrm != null
                ? _meshService.CreateVob(munition.Visual, mrm, position, Quaternion.identity, false)
                : null;
            return arrow != null ? arrow : new GameObject(munition.Visual);
        }

        private bool CanBeHit(NpcContainer attacker, NpcContainer target)
        {
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return false;

            // Like the engine, nobody hurts the dialog partners during a dialog. Fights around go on.
            if (IsInDialog(attacker) || IsInDialog(target))
                return false;

            return !(target.IsHero() && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious);
        }

        /// <summary>
        /// A hit on a knocked out NPC finishes it off.
        /// </summary>
        private bool TryFinishOff(NpcContainer attacker, NpcContainer target)
        {
            if (target.IsHero() || !target.IsKnockedOut())
                return false;

            Logger.Log($"[Fight] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            _npcService.Kill(target, attacker);
            return true;
        }

        /// <param name="mayKnockOut">False for spells that kill at 0 hit points.</param>
        private void ApplyDamage(NpcContainer attacker, NpcContainer target, int damage, bool mayKnockOut = true)
        {
            var isHero = target.IsHero();
            if (UpdateHealth(attacker, target, damage))
            {
                // The hero follows the same rule as everybody: monsters and hostile humans kill him.
                var isKnockedOut = mayKnockOut && _meleeDamageDomain.IsKnockedOutNotKilled(attacker, target);
                if (isHero && isKnockedOut)
                    KnockOutHero(target, attacker);
                else if (isKnockedOut)
                    KnockOut(target, attacker);
                else
                    _npcService.Kill(target, attacker);
            }
            else
            {
                // An NPC's swing isn't cut short by another NPC's hit - two bandits taking turns kept their victim from
                // ever landing one (stumble before its hit frame). The hero's hits interrupt like in Gothic.
                if (!isHero && (attacker.IsHero() || target.Props.CurrentAction is not AttackPlayAni))
                    PlayStumble(target);
                PlayHurtSound(target);
            }

            ReactToDamage(attacker, target);
        }

        private bool IsInDialog(NpcContainer npc)
        {
            var dialogs = _gameStateService.Dialogs;
            return dialogs.IsInDialog && (npc.IsHero() || npc == dialogs.CurrentDialogNpc);
        }

        /// <summary>
        /// Takes the damage off. Returns true if the target is at 0 hit points (knocked out or dead).
        /// </summary>
        private bool UpdateHealth(NpcContainer attacker, NpcContainer target, int damage)
        {
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints) - damage;
            Logger.Log($"[Fight] {attacker.Instance.GetName(NpcNameSlot.Slot0)} hits " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: {damage} damage, {hitPoints} HP left",
                LogCat.Fight);

            if (hitPoints <= 0)
                return true;

            // Health bars follow via GlobalEventDispatcher.NpcAttributeChanged.
            _npcService.SetAttribute(target, NpcAttribute.HitPoints, hitPoints);
            return false;
        }

        /// <summary>
        /// Like the engine: 1 hit point left, the NPC falls down and runs ZS_Unconscious (it gets up by itself, the
        /// attacker may plunder it). The state's other is the attacker - the hero's win counts for his experience.
        /// </summary>
        private void KnockOut(NpcContainer npc, NpcContainer attacker)
        {
            _npcService.SetAttribute(npc, NpcAttribute.HitPoints, 1);
            // The engine drops the weapon to the ground, and G1's ZS_Unconscious_End picks it up again. Our NPCs can't
            // pick up items yet - they would stay unarmed - so it goes back on the belt or back.
            UndrawWeapon.SheathAtOnce(npc);

            var zsUnconscious = _gameStateService.GothicVm.GetSymbolByName("ZS_UNCONSCIOUS");
            if (zsUnconscious == null)
            {
                Logger.LogWarning("[Fight] ZS_UNCONSCIOUS not found - the NPC only falls down.", LogCat.Fight);
                npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
                npc.Props.AnimationQueue.Clear();
                npc.PrefabProps.AnimationSystem.StopAllAnimations();
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                npc.PrefabProps.AnimationSystem.PlayAnimation(
                    _animationService.GetAnimationName(VmGothicEnums.AnimationType.UnconsciousA, npc));
                return;
            }

            // An engine state change, not a script call: the old state's _End doesn't run. Starting the state resets
            // the body state - BS_UNCONSCIOUS comes after.
            _npcAiService.StartState(npc.Instance, zsUnconscious.Index, true, "", attacker.Instance, null, false);
            npc.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;

            // The engine drops the NPC itself: G2's ZS_Unconscious only plays a fall for some of the tough guys,
            // everybody else stood while unconscious.
            if (npc.PrefabProps?.AnimationSystem != null)
            {
                _physicsService.DisablePhysicsForNpc(npc.PrefabProps);
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "T_Stand_2_Wounded"), npc));
                npc.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "S_Wounded"), npc));
            }

            Logger.Log($"[Fight] {npc.Instance.GetName(NpcNameSlot.Slot0)} is knocked out by " +
                       $"{attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            _npcAiService.SendPerceptionToWitnesses(npc, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
        }

        /// <summary>
        /// The hero runs no Daedalus states: he is locked in place for a while. His attackers' fight ends like in
        /// Gothic - they calm down (temporary attitude), stop and put their weapons away.
        /// </summary>
        private void KnockOutHero(NpcContainer hero, NpcContainer attacker)
        {
            hero.Props.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            _npcService.SetAttribute(hero, NpcAttribute.HitPoints, 1);
            _contextInteractionService.LockPlayerInPlace();
            Logger.Log($"[Fight] The hero is knocked out by {attacker.Instance.GetName(NpcNameSlot.Slot0)}",
                LogCat.Fight);

            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc.Props?.TargetNpc?.Index != hero.Instance.Index || npc.Vob == null)
                    continue;

                npc.Vob.AttitudeTemp = npc.Vob.Attitude;
                // Their running chase/attack kept the queue busy for seconds, so ZS_Attack_Loop never saw
                // C_NpcIsDown(hero) - they attacked again once the hero stood up. Cleared, the loop ends the fight.
                _npcAiService.ExtNpcClearAiQueue(npc.Instance);
                if ((VmGothicEnums.WeaponState)npc.Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon)
                    _npcAiService.ExtAiUndrawWeapon(npc.Instance);
            }

            _npcAiService.SendPerceptionToWitnesses(hero, attacker, VmGothicEnums.PerceptionType.AssessDefeat);
            _unityMonoService.StartCoroutine(RecoverHero(hero, ++_heroKnockoutId));
            GlobalEventDispatcher.HeroKnockedOut.Invoke(_heroKnockoutSeconds);
        }

        private IEnumerator RecoverHero(NpcContainer hero, int knockoutId)
        {
            yield return new WaitForSeconds(_heroKnockoutSeconds);

            // A game loaded in the meantime: its locks (dialog, lock picking) aren't the knockout's.
            if (knockoutId != _heroKnockoutId || hero != _npcService.GetHeroContainer() ||
                hero.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious)
                yield break;

            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer();
            Logger.Log("[Fight] The hero recovered from the knockout", LogCat.Fight);
            GlobalEventDispatcher.HeroRecovered.Invoke();
        }

        /// <summary>
        /// The victim reacts to the hit (PERC_ASSESSDAMAGE: B_AssessDamage / B_MM_AssessDamage fight back or flee), the
        /// NPCs around who see it react to the fight (PERC_ASSESSOTHERSDAMAGE, PERC_ASSESSFIGHTSOUND).
        /// </summary>
        private void ReactToDamage(NpcContainer attacker, NpcContainer target)
        {
            // A downed victim perceives nothing: its reaction would replace ZS_Unconscious.
            if (!target.IsDown() && !target.IsHero())
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessDamage, target.Props,
                    target.Instance, target.Instance, attacker.Instance);

            _npcAiService.SendPerceptionToWitnesses(target, attacker, VmGothicEnums.PerceptionType.AssessOthersDamage,
                VmGothicEnums.PerceptionType.AssessFightSound);
        }

        private void PlayStumble(NpcContainer target)
        {
            if (target.PrefabProps?.AnimationSystem == null)
                return;

            // Play hurt on top of whatever is currently running — don't interrupt the current action.
            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.StumbleA, target);
            target.PrefabProps.AnimationSystem.PlayAnimation(animName);
        }

        private void PlayHurtSound(NpcContainer target)
        {
            // In G1, Humans needs to have stumble sound called via Aargh svm.
            var clip = _audioService.GetRandomSoundClip($"SVM_{target.Instance.Voice}_AARGH");

            // Monsters will have their stumble sound inside animations itself.
            if (clip == null || target.PrefabProps?.NpcSound == null)
                return;

            target.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
