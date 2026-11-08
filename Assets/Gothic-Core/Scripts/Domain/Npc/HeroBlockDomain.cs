using System;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Blocked melee hits (OpenGothic Npc::takeDamage): an NPC's parry or jump back from the hero's hit, the hero's
    /// own block (IContextHeroBlockService: the platform knows whether he blocks), and the weapons' clash.
    /// </summary>
    public class HeroBlockDomain
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly ContextHeroBlockService _contextHeroBlockService;


        /// <summary>
        /// A parry facing the attacker (30 degrees) or a jump back (90 degrees) takes no damage, weapons clash
        /// (CS_IAI_ sound + CPFX_IAI_ effect). The whole parry/jump back animation counts (the engine: its DEF_WINDOW
        /// frames).
        /// </summary>
        public bool TryBlockNpcHit(NpcContainer attacker, NpcContainer target)
        {
            if (target.Go == null || attacker.Go == null ||
                target.Props.CurrentAction is not AttackPlayAni defence)
                return false;

            var isParry = defence.Move == FightAiMove.Parry &&
                          Attack.IsInAngle(target.Go.transform, attacker.Go.transform.position, FightConst.FocusAngle);
            var isJumpBack = defence.Move == FightAiMove.JumpBack &&
                             Attack.IsInAngle(target.Go.transform, attacker.Go.transform.position,
                                 FightConst.JumpBackAngle);
            if (!isParry && !isJumpBack)
                return false;

            Logger.Log($"[HeroBlockDomain] {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"{(isParry ? "parries" : "jumps back from")} " +
                       $"{attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit",
                LogCat.Fight);
            if (isParry)
                PlayWeaponBlockEffect(attacker, target);
            return true;
        }

        /// <summary>
        /// A parry is its own state like the engine's - no hits while blocking.
        /// </summary>
        public bool IsHeroBlocking(NpcContainer hero)
        {
            return _contextHeroBlockService.IsBlocking(hero);
        }

        /// <summary>
        /// The blocking hero takes no damage from a human's hit (fists too) or a
        /// monster's with a drawn weapon (orcs, skeletons). Animals, spells and projectiles always hit (they don't come
        /// this way). The platform decides whether the weapons touched - maybe a moment later; a hit that missed the
        /// block lands then through onMissed.
        /// </summary>
        public bool TryBlockHeroHit(NpcContainer attacker, NpcContainer hero, Action onMissed)
        {
            if (attacker.Go == null || hero.Go == null)
                return false;

            var attackerWeapon = (VmGothicEnums.WeaponState)attacker.Vob.FightMode;
            if (!attacker.IsHuman() &&
                attackerWeapon is not (VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H))
                return false;

            return _contextHeroBlockService.TryBlock(attacker, hero, () => BlockHit(attacker, hero), onMissed);
        }

        /// <summary>
        /// The platform reports that an NPC's weapon (or fist) touched the hero's held weapon while he blocks.
        /// </summary>
        public void ReportBlockContact(NpcContainer attacker)
        {
            _contextHeroBlockService.ReportBlockContact(attacker);
        }

        private void BlockHit(NpcContainer attacker, NpcContainer hero)
        {
            Logger.Log($"[HeroBlockDomain] The hero blocks {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit",
                LogCat.Fight);
            PlayWeaponBlockEffect(attacker, hero);
            GlobalEventDispatcher.HeroBlockedHit.Invoke(attacker);
        }

        /// <summary>
        /// OpenGothic World::addWeaponBlkEffect: "CS_IAI_" + both weapons' materials (item attacks item), the particle
        /// effect CPFX_IAI_ the same or by the defender's material. Without a weapon on either side there's nothing.
        /// G1 has only CS_IAI_ME_ME - other pairs take the generic CS_IAI.
        /// </summary>
        private void PlayWeaponBlockEffect(NpcContainer attacker, NpcContainer target)
        {
            var attackerMaterial = GetAttackerMaterial(attacker);
            var defenderMaterial = GetDefenderMaterial(target);
            if (attackerMaterial < 0 || defenderMaterial < 0)
            {
                Logger.Log($"[HeroBlockDomain] Parry without a clash: attacker material {attackerMaterial}, " +
                           $"defender material {defenderMaterial}", LogCat.Fight);
                return;
            }

            var from = GetMaterialTag(attackerMaterial);
            var to = GetMaterialTag(defenderMaterial);
            var clip = _audioService.GetRandomSoundClip($"CS_IAI_{from}_{to}") ??
                       _audioService.GetRandomSoundClip("CS_IAI");
            if (clip != null && target.PrefabProps?.NpcSound != null)
                target.PrefabProps.NpcSound.PlayOneShot(clip);

            var pfxName = $"CPFX_IAI_{from}_{to}";
            if (_vmCacheService.TryGetPfxData(pfxName) == null)
                pfxName = to switch { "WO" => "CPFX_IAI_WOOD", "ST" => "CPFX_IAI_STONE", _ => "CPFX_IAI_METAL" };
            if (_vmCacheService.TryGetPfxData(pfxName) == null)
                return;

            var position = target.Go.transform.position + Vector3.up * 1.3f +
                           (attacker.Go.transform.position - target.Go.transform.position).normalized * 0.4f;
            _meshService.CreateVobPfx(pfxName, position, Quaternion.identity, destroyAfterPlay: true);
        }

        /// <summary>
        /// The hero's held weapon isn't equipped in VR - the material of the swing (-1: fists).
        /// </summary>
        private int GetAttackerMaterial(NpcContainer attacker)
        {
            return attacker.PrefabProps != null && attacker.PrefabProps.IsHero()
                ? attacker.Props.MeleeSwingMaterial
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(attacker.Instance)?.Material ?? -1;
        }

        /// <summary>
        /// The blocking hero's held weapon isn't equipped either.
        /// </summary>
        private int GetDefenderMaterial(NpcContainer target)
        {
            if (target.PrefabProps != null && target.PrefabProps.IsHero())
                return _contextHeroBlockService.GetBlockWeaponMaterial(target);

            return (_npcHelperService.ExtNpcGetEquippedMeleeWeapon(target.Instance) ??
                    (target.Props.CurrentItem >= 0 ? _vmCacheService.TryGetItemData(target.Props.CurrentItem) : null))
                ?.Material ?? -1;
        }

        private static string GetMaterialTag(int material)
        {
            return (VmGothicEnums.ItemMaterial)material switch
            {
                VmGothicEnums.ItemMaterial.Wood => "WO",
                VmGothicEnums.ItemMaterial.Stone => "ST",
                VmGothicEnums.ItemMaterial.Leather => "LE",
                VmGothicEnums.ItemMaterial.Clay => "CL",
                VmGothicEnums.ItemMaterial.Glass => "GL",
                _ => "ME"
            };
        }
    }
}
