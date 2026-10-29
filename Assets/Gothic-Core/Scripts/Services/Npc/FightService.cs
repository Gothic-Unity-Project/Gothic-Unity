using System.Collections;
using System.Linq;
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
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// What a melee hit does, like the engine (OpenGothic Npc::takeDamage, Npc::checkHealth, DamageCalculator):
    /// damage, a stumble, the reactions of the victim and witnesses, and at 0 hit points a knockout or death.
    /// </summary>
    public class FightService
    {
        // Seconds the knocked out hero lies down before he can move again.
        private const float _heroKnockoutSeconds = 10f;
        private const int _npcFlagImmortal = 2;

        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private int? _minimalDamage;

        public void Init()
        {
            // Script deaths (B_KillNpc) run ZS_Dead as well.
            GlobalEventDispatcher.NpcDied.AddListener(OnNpcDied);

            if (!_configService.Dev.EnableCombatSystem)
                return;

            GlobalEventDispatcher.FightHit.AddListener(OnHit);
        }

        private void OnHit(NpcContainer attacker, NpcContainer target, Vector3 __)
        {
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            // Like the engine, nobody is hurt during a dialog.
            if (_gameStateService.Dialogs.IsInDialog)
                return;

            var isHero = target.PrefabProps != null && target.PrefabProps.IsHero();
            if (isHero && target.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            // A hit on a knocked out NPC finishes it off.
            if (!isHero && NpcHelperService.IsKnockedOut(target))
            {
                Logger.Log($"[Fight] {attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off " +
                           $"{target.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
                _npcService.Kill(target, attacker);
                return;
            }

            if (UpdateHealth(attacker, target))
            {
                if (isHero)
                    KnockOutHero(target, attacker);
                else if (IsKnockedOutNotKilled(attacker, target))
                    KnockOut(target, attacker);
                else
                    _npcService.Kill(target, attacker);
            }
            else
            {
                // An NPC's swing isn't cut short by another NPC's hit - two bandits taking turns kept their victim from
                // ever landing one (stumble before its hit frame). The hero's hits interrupt like in Gothic.
                var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
                if (!isHero && (isHeroAttacker || target.Props.CurrentAction is not AttackPlayAni))
                    PlayStumble(target);
                PlayHurtSound(target);
            }

            ReactToDamage(attacker, target);
        }

        /// <summary>
        /// Takes the damage off. Returns true if the target is at 0 hit points (knocked out or dead).
        /// </summary>
        private bool UpdateHealth(NpcContainer attacker, NpcContainer target)
        {
            var hitPoints = target.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var damage = CalculateMeleeDamage(attacker, target, out var isImmune);

            // Like the engine, a hit never does less than NPC_MINIMAL_DAMAGE unless the target is immune.
            if (!isImmune)
                damage = Mathf.Max(damage, GetMinimalDamage());

            // NPC_FLAG_IMMORTAL: no damage, but the fight plays out normally.
            if (((int)target.Instance.Flags & _npcFlagImmortal) != 0)
                damage = 0;

            hitPoints -= damage;
            Logger.Log($"[Fight] {attacker.Instance.GetName(NpcNameSlot.Slot0)} hits " +
                       $"{target.Instance.GetName(NpcNameSlot.Slot0)}: {damage} damage, {hitPoints} HP left", LogCat.Fight);

            if (hitPoints <= 0)
                return true;

            // Health bars follow via GlobalEventDispatcher.NpcAttributeChanged.
            _npcService.SetAttribute(target, NpcAttribute.HitPoints, hitPoints);
            return false;
        }

        private int GetMinimalDamage()
        {
            _minimalDamage ??= _gameStateService.GothicVm.GetSymbolByName("NPC_MINIMAL_DAMAGE")?.GetInt(0) ?? 0;
            return _minimalDamage.Value;
        }

        /// <summary>
        /// OpenGothic DamageCalculator::swordDamage. Per damage type of the weapon (or the NPC's own damagetype: fists,
        /// claws): strength + the NPC's damage[] + the weapon's damage[] and damageTotal - the target's protection of
        /// that type. Immune types (protection -1) are left out.
        /// G1: a critical hit with the weapon talent's chance doubles the weapon part (DAM_CRITICAL_MULTIPLIER).
        /// G2: a miss by the hit chance (C_NPC.hitchance[1H/2H]) does a tenth; monsters without a weapon always hit.
        /// The hero's weapon is the one in his VR hand (MeleeHitWeapon): it isn't equipped in VR.
        /// </summary>
        private int CalculateMeleeDamage(NpcContainer attacker, NpcContainer target, out bool isImmune)
        {
            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            var weapon = isHeroAttacker && attacker.Props.MeleeHitWeapon != null
                ? attacker.Props.MeleeHitWeapon
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance);
            // Fighting with fists, the sword in the belt doesn't count.
            if ((VmGothicEnums.WeaponState)attacker.Vob.FightMode == VmGothicEnums.WeaponState.Fist)
                weapon = null;

            var mask = weapon?.DamageType ?? attacker.Instance.DamageType;
            if (mask == 0)
                mask = 1 << (int)DamageType.Blunt;

            var vm = _gameStateService.GothicVm;
            var isTwoHanded = weapon != null && ((VmGothicEnums.ItemFlags)weapon.Flags &
                (VmGothicEnums.ItemFlags.Item2HdSwd | VmGothicEnums.ItemFlags.Item2HdAxe)) != 0;
            var roll = Random.Range(0, 100);
            var isG2 = _contextGameVersionService.IsGothic2();
            bool isCritical;
            if (isG2)
            {
                // G2: the "critical" roll is the hit chance - above it the hit is a near miss.
                isCritical = (!IsHuman(attacker) && weapon == null) ||
                             attacker.Instance.GetHitChance(isTwoHanded ? NpcTalent.TwoHanded : NpcTalent.OneHanded) > roll;
            }
            else
            {
                var talentId = vm.GetSymbolByName(isTwoHanded ? "NPC_TALENT_2H" : "NPC_TALENT_1H")?.GetInt(0) ??
                               (isTwoHanded ? 2 : 1);
                isCritical = weapon != null && _npcHelperService.ExtNpcGetTalentValue(attacker.Instance, talentId) > roll;
            }

            var multiplier = vm.GetSymbolByName("DAM_CRITICAL_MULTIPLIER")?.GetInt(0) ?? 2;
            var strength = attacker.Vob.GetAttribute((int)NpcAttribute.Strength);

            var damage = 0;
            isImmune = true;
            for (var i = 0; i < 8; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;

                var protection = target.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                var typeDamage = attacker.Instance.GetDamage((DamageType)i) +
                                 (weapon != null ? weapon.GetDamage((DamageType)i) + weapon.DamageTotal : 0);
                int value;
                if (isG2)
                {
                    value = Mathf.Max(strength + typeDamage - protection, 0);
                    if (!isCritical)
                        value = (value - 1) / 10;
                }
                else
                {
                    value = Mathf.Max(strength + (isCritical ? multiplier * typeDamage : typeDamage) - protection, 0);
                }

                damage += value;
                isImmune = false;
            }

            return damage;
        }

        /// <summary>
        /// The engine's rule (OpenGothic Npc::checkHealth): a human beaten in melee is knocked out unless it's hostile
        /// to its attacker - permanent attitude to the hero, the guilds' attitude between NPCs. Bandits die, citizens
        /// get up again. Monsters and orcs always die, so does anyone shot by an archer.
        /// </summary>
        private bool IsKnockedOutNotKilled(NpcContainer attacker, NpcContainer target)
        {
            if (!IsHuman(target))
                return false;

            var isHeroAttacker = attacker.PrefabProps != null && attacker.PrefabProps.IsHero();
            var attackerWeapon = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            if (!isHeroAttacker && attackerWeapon is VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow)
                return false;

            return _npcAiService.ExtGetPermAttitude(target.Instance, attacker.Instance) != VmGothicEnums.Attitude.Hostile;
        }

        /// <summary>
        /// Like the engine: 1 hit point left, the NPC falls down and runs ZS_Unconscious (it gets up by itself, the
        /// attacker may plunder it). The state's other is the attacker - the hero's win counts for his experience.
        /// </summary>
        private void KnockOut(NpcContainer npc, NpcContainer attacker)
        {
            _npcService.SetAttribute(npc, NpcAttribute.HitPoints, 1);

            var vm = _gameStateService.GothicVm;
            var zsUnconscious = vm.GetSymbolByName("ZS_UNCONSCIOUS");
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

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = npc.Instance;
            vm.GlobalOther = attacker.Instance;
            // Starting the state resets the body state - BS_UNCONSCIOUS comes after.
            _npcAiService.ExtAiStartState(npc.Instance, zsUnconscious.Index, true, "", false);
            vm.GlobalSelf = oldSelf;
            vm.GlobalOther = oldOther;
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
            NotifyWitnesses(VmGothicEnums.PerceptionType.AssessDefeat, npc, attacker);
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
            Logger.Log($"[Fight] The hero is knocked out by {attacker.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);

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

            NotifyWitnesses(VmGothicEnums.PerceptionType.AssessDefeat, hero, attacker);
            _unityMonoService.StartCoroutine(RecoverHero(hero));
        }

        private IEnumerator RecoverHero(NpcContainer hero)
        {
            yield return new WaitForSeconds(_heroKnockoutSeconds);

            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                yield break;

            hero.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            _contextInteractionService.UnlockPlayer();
            Logger.Log("[Fight] The hero recovered from the knockout", LogCat.Fight);
        }

        /// <summary>
        /// Like the engine, ZS_Dead runs for every death (experience, loot, quest variables), with the killer as
        /// other: the experience goes to the hero for his own kills only. Deferred by one frame - G2's ZS_Dead takes
        /// about 280 ms, too much for the frame of the hit. The engine runs its loop once too: G2 counts killed
        /// dragons there (B_DragonKillCounter).
        /// </summary>
        private void OnNpcDied(NpcContainer dead, NpcContainer killer)
        {
            _unityMonoService.StartCoroutine(RunDeadState(dead, killer));
        }

        private IEnumerator RunDeadState(NpcContainer dead, NpcContainer killer)
        {
            yield return null;

            var vm = _gameStateService.GothicVm;
            var zsDead = vm.GetSymbolByName("ZS_Dead");
            if (zsDead == null)
            {
                Logger.LogWarning("[Fight] ZS_Dead not found - nothing happens after a death.", LogCat.Fight);
                yield break;
            }

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead.Instance;
            vm.GlobalOther = killer?.Instance ?? dead.Instance;
            try
            {
                vm.Call(zsDead.Index);

                var zsDeadLoop = vm.GetSymbolByName("ZS_Dead_Loop");
                if (zsDeadLoop is { HasReturn: true })
                    vm.Call<int>(zsDeadLoop.Index);
                else if (zsDeadLoop != null)
                    vm.Call(zsDeadLoop.Index);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }

            // ZS_Dead gives the hero experience on his instance.
            _npcService.SyncHeroInstanceToVob();

            if (killer != null && killer != dead)
                NotifyWitnesses(VmGothicEnums.PerceptionType.AssessMurder, dead, killer);
        }

        /// <summary>
        /// The victim reacts to the hit (PERC_ASSESSDAMAGE: B_AssessDamage / B_MM_AssessDamage fight back or flee), the
        /// NPCs around who see it react to the fight (PERC_ASSESSOTHERSDAMAGE, PERC_ASSESSFIGHTSOUND).
        /// </summary>
        private void ReactToDamage(NpcContainer attacker, NpcContainer target)
        {
            // A downed victim perceives nothing: its reaction would replace ZS_Unconscious.
            if (!NpcHelperService.IsDown(target) && target.PrefabProps != null && !target.PrefabProps.IsHero())
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessDamage, target.Props,
                    target.Instance, target.Instance, attacker.Instance);

            NotifyWitnesses(VmGothicEnums.PerceptionType.AssessOthersDamage, target, attacker);
            NotifyWitnesses(VmGothicEnums.PerceptionType.AssessFightSound, target, attacker);
        }

        /// <summary>
        /// The perception for every NPC who registered it and sees the attacker within its range (Perc_SetRange,
        /// capped by its senses range): victim = the victim, other = the attacker. Not the two themselves, not the hero,
        /// not downed or culled NPCs.
        /// </summary>
        private void NotifyWitnesses(VmGothicEnums.PerceptionType perception, NpcContainer victim,
            NpcContainer attacker)
        {
            if (attacker?.Go == null)
                return;

            var attackerPosition = attacker.Go.transform.position;
            var perceptionRange = _npcHelperService.GetPerceptionRange(perception);

            // Copy: a perception function can insert or remove NPCs.
            foreach (var witness in _multiTypeCacheService.NpcCache.ToArray())
            {
                if (witness == victim || witness == attacker || witness.Props == null || witness.Go == null ||
                    !witness.Go.activeInHierarchy || NpcHelperService.IsDown(witness))
                    continue;
                if (witness.PrefabProps != null && witness.PrefabProps.IsHero())
                    continue;
                if (!witness.Props.Perceptions.TryGetValue(perception, out var function) || function < 0)
                    continue;

                var range = Mathf.Min(perceptionRange, witness.Instance.SensesRange / 100f);
                if ((witness.Go.transform.position - attackerPosition).sqrMagnitude > range * range)
                    continue;

                // NPCs behind walls don't come running.
                if (!_npcAiService.ExtNpcCanSeeNpc(witness.Instance, attacker.Instance, true))
                    continue;

                _npcAiService.ExecutePerception(perception, witness.Props, witness.Instance, victim.Instance,
                    attacker.Instance);
            }
        }

        // Only true humans (guild below GIL_SEPERATOR_HUM) go unconscious. Orcs and monsters die.
        private static bool IsHuman(NpcContainer npc) => npc.Instance.Guild < (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;

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
