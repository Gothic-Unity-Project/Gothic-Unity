using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Magic like the engine (OpenGothic Npc::beginCastSpell, tickCast, endCastSpell, commitSpell). The scripts decide
    /// how much mana a spell takes and when it goes off: Spell_ProcessMana for each portion invested, every
    /// C_Spell.time_per_mana, and Spell_ProcessMana_Release when the hero lets go. G1's engine takes 1 mana per portion,
    /// G2's Spell_Cast_[name] takes the mana when the spell goes off. Projectiles fly (SpellFlightDomain) and hurt by
    /// SpellDamageDomain; other spells show at the caster and work on the target through the scripts (sleep, fear:
    /// PERC_ASSESSMAGIC) or on the caster (heal, teleport). Scrolls are used up, runes stay.
    /// NPCs cast through AI_ReadySpell and AI_Attack (CastSpell), the VR hero with a held rune or scroll.
    /// </summary>
    public class MagicService
    {
        private const int _maxLevel = 15;
        // G2's charge spells count their level in AIV_SpellLevel, which the engine clears on each cast.
        private const int _aivSpellLevelG2 = 88;
        private const int _spellGood = 0;
        private const int _spellBad = 2;
        private const int _talentMageFallback = 7;
        // C_Spell.targetCollectAlgo: TARGET_COLLECT_NONE, TARGET_COLLECT_CASTER.
        private const int _targetCollectNone = 0;
        private const int _targetCollectCaster = 1;
        // How long the particles of a hit stay.
        private const float _impactSeconds = 2f;
        private const float _castEffectSeconds = 3f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private readonly SpellDataDomain _spellData = new SpellDataDomain().Inject();
        private readonly SpellEffectDomain _spellEffects = new SpellEffectDomain().Inject();
        private readonly SpellDamageDomain _spellDamage = new SpellDamageDomain().Inject();
        private SpellFlightDomain _spellFlight;

        /// <summary>
        /// Npc_GetActiveSpell: the spell of the rune or scroll drawn, -1 without one (0 is a spell: G1's Light).
        /// </summary>
        public int GetActiveSpell(NpcInstance npc)
        {
            return GetNpc(npc)?.Props?.ActiveSpellItem?.Spell ?? -1;
        }

        /// <summary>
        /// Npc_GetActiveSpellCat: SPELL_GOOD, SPELL_NEUTRAL or SPELL_BAD of the spell drawn, SPELL_GOOD without one.
        /// </summary>
        public int GetActiveSpellCategory(NpcInstance npc)
        {
            return GetCategory(GetActiveSpell(npc));
        }

        /// <summary>
        /// Npc_GetActiveSpellLevel: the level the cast reached, 1-based, 0 while not casting.
        /// </summary>
        public int GetActiveSpellLevel(NpcInstance npc)
        {
            var cast = GetNpc(npc)?.Props?.SpellCast;
            return cast is { Phase: SpellCastPhase.Investing or SpellCastPhase.Casting } ? cast.Level + 1 : 0;
        }

        public bool IsActiveSpellScroll(NpcInstance npc)
        {
            return IsScroll(GetNpc(npc)?.Props?.ActiveSpellItem);
        }

        /// <summary>
        /// Npc_IsDrawingSpell (G2): the scroll's item instance while one is drawn, else 0 (OpenGothic, as the engine).
        /// </summary>
        public int GetDrawnScroll(NpcInstance npc)
        {
            var item = GetNpc(npc)?.Props?.ActiveSpellItem;
            return IsScroll(item) ? item.Index : 0;
        }

        public bool HasSpell(NpcInstance npc, int spellId)
        {
            return FindSpellItem(npc, spellId) != null;
        }

        /// <summary>
        /// Npc_SetActiveSpellInfo: what transform spells turn the caster into. Kept, not used yet.
        /// </summary>
        public void SetActiveSpellInfo(NpcInstance npc, int info)
        {
            var props = GetNpc(npc)?.Props;
            if (props != null)
                props.SpellInfo = info;
        }

        public int GetLastHitSpell(NpcInstance npc)
        {
            return GetNpc(npc)?.Props?.LastHitSpell ?? -1;
        }

        public int GetLastHitSpellCategory(NpcInstance npc)
        {
            return GetCategory(GetLastHitSpell(npc));
        }

        public static bool IsSpellItem(ItemInstance item)
        {
            return item != null && (item.MainFlag & (int)VmGothicEnums.ItemFlags.ItemKatRune) != 0;
        }

        /// <summary>
        /// A scroll is used up by its cast, a rune isn't (OpenGothic Item::isSpell).
        /// </summary>
        public static bool IsScroll(ItemInstance item)
        {
            return IsSpellItem(item) && (item.Flags & (int)VmGothicEnums.ItemFlags.ItemMulti) != 0;
        }

        /// <summary>
        /// The rune or scroll of that spell in the NPC's inventory, null without one.
        /// </summary>
        public ItemInstance FindSpellItem(NpcInstance npc, int spellId)
        {
            List<ContentItem> items;
            try
            {
                items = _npcInventoryService.GetInventoryItems(npc, VmGothicEnums.InvCats.InvRune);
            }
            catch (Exception)
            {
                // The NPC never had anything of the category.
                return null;
            }

            foreach (var content in items)
            {
                if (content.Amount <= 0)
                    continue;

                var item = _vmCacheService.TryGetItemData(content.Name);
                if (IsSpellItem(item) && item.Spell == spellId)
                    return item;
            }

            return null;
        }

        /// <summary>
        /// AI_ReadySpell: the NPC draws the spell's rune or scroll. It casts once this much mana went in.
        /// </summary>
        public bool ReadySpell(NpcContainer npc, int spellId, int investMana)
        {
            var item = FindSpellItem(npc.Instance, spellId);
            if (item == null)
            {
                Logger.LogWarning($"[Magic] {npc.Instance.GetName(NpcNameSlot.Slot0)} has no rune or scroll of spell " +
                                  $"{spellId} ({_spellData.GetName(spellId)})", LogCat.Fight);
                return false;
            }

            npc.Props.ActiveSpellItem = item;
            npc.Props.ExpectedSpellInvest = Mathf.Max(investMana, 1);
            npc.Props.SpellCast = null;
            npc.Vob.FightMode = (int)VmGothicEnums.WeaponState.Mage;
            return true;
        }

        /// <summary>
        /// The animation letters of the spell (FBT, HEA, ...) for T_MAGRUN_2_[letters]CAST and S_[letters]SHOOT.
        /// </summary>
        public string GetAnimationLetters(int spellId)
        {
            return _spellData.GetAniLetters(spellId);
        }

        /// <summary>
        /// Like the engine (OpenGothic Item::checkCondRune): a rune needs the caster's magic circle (TALENT_MAGE) to
        /// reach its own. If not, the scripts tell why (G_CanNotCast).
        /// </summary>
        public bool CanCast(NpcContainer caster, ItemInstance item)
        {
            var circle = _npcHelperService.ExtNpcGetTalentSkill(caster.Instance, GetMageTalent());
            if (item.MagCircle <= circle)
                return true;

            var canNotCast = _gameStateService.GothicVm.GetSymbolByName("G_CanNotCast");
            if (canNotCast is { Type: DaedalusDataType.Function, IsExternal: false })
                CallAsCaster(caster, null, () =>
                    _gameStateService.GothicVm.Call(canNotCast.Index, caster.IsHero() ? 1 : 0, item.MagCircle, circle));
            return false;
        }

        /// <summary>
        /// Starts a cast like the engine (OpenGothic Npc::beginCastSpell): nothing without mana or when the scripts
        /// refuse (Spell_ProcessMana with nothing invested), else the invest begins - or the spell goes off at once.
        /// </summary>
        /// <param name="target">The scripts' other, null without one.</param>
        /// <param name="expectedInvest">NPCs: cast after this much mana. 0 for the hero, who lets go to cast.</param>
        /// <returns>The cast, null if it failed.</returns>
        public SpellCast BeginCast(NpcContainer caster, NpcContainer target, ItemInstance item, int expectedInvest)
        {
            if (!IsSpellItem(item))
                return null;

            var spell = _spellData.GetSpell(item.Spell);
            if (spell == null)
            {
                Logger.LogWarning($"[Magic] Spell {item.Spell} of {item.Name} has no C_Spell instance", LogCat.Fight);
                return null;
            }

            // Spells for the caster or nobody (heal, light, teleport) have no victim.
            if (spell.TargetCollectAlgo is _targetCollectNone or _targetCollectCaster)
                target = null;

            caster.Props.ActiveSpellItem = item;
            if (caster.Vob.GetAttribute((int)NpcAttribute.Mana) <= 0)
            {
                Logger.Log($"[Magic] {caster.Instance.GetName(NpcNameSlot.Slot0)} has no mana for {item.Name}",
                    LogCat.Fight);
                return null;
            }

            var cast = new SpellCast
            {
                Caster = caster,
                Target = target,
                Item = item,
                Spell = spell,
                SpellId = item.Spell,
                ExpectedInvest = expectedInvest,
                NextInvestTime = Time.time,
                Phase = SpellCastPhase.Investing
            };
            caster.Props.SpellCast = cast;
            if (_contextGameVersionService.IsGothic2())
                caster.Instance.SetAiVar(_aivSpellLevelG2, 0);

            var code = ProcessMana(cast, "Spell_ProcessMana");
            switch (code)
            {
                case SpellCode.SendCast:
                    cast.Phase = SpellCastPhase.Casting;
                    break;
                case SpellCode.ReceiveInvest or SpellCode.NextLevel or SpellCode.CanInvestNoManaDec:
                    cast.ManaInvested = 1;
                    break;
                default:
                    Stop(cast);
                    Logger.Log($"[Magic] {caster.Instance.GetName(NpcNameSlot.Slot0)} can't cast {item.Name} " +
                               $"({code})", LogCat.Fight);
                    return null;
            }

            Logger.Log($"[Magic] {caster.Instance.GetName(NpcNameSlot.Slot0)} casts {item.Name}" +
                       (target != null ? $" at {target.Instance.GetName(NpcNameSlot.Slot0)}" : ""), LogCat.Fight);
            return cast;
        }

        /// <summary>
        /// One frame of the invest (OpenGothic Npc::tickCast): each time_per_mana the scripts learn the mana invested so
        /// far, and G1's engine takes 1 mana. An NPC casts when its AI_ReadySpell mana is in, the hero holds a spell the
        /// scripts want no more mana for until he lets go.
        /// </summary>
        public SpellCastPhase TickCast(SpellCast cast)
        {
            if (cast.Phase != SpellCastPhase.Investing)
                return cast.Phase;

            if (cast.Caster.IsDown())
            {
                Stop(cast);
                return cast.Phase;
            }

            if (Time.time < cast.NextInvestTime)
                return cast.Phase;

            var code = ProcessMana(cast, "Spell_ProcessMana");
            if (_contextGameVersionService.IsGothic1())
                ChangeMana(cast.Caster, -1);

            var isHero = cast.ExpectedInvest <= 0;
            if (!isHero && cast.ExpectedInvest <= cast.ManaInvested)
            {
                cast.Phase = SpellCastPhase.Casting;
                return cast.Phase;
            }

            switch (code)
            {
                case SpellCode.ReceiveInvest or SpellCode.NextLevel or SpellCode.CanInvestNoManaDec:
                    if (code == SpellCode.NextLevel && cast.Level < _maxLevel)
                        cast.Level++;
                    cast.NextInvestTime += Mathf.Max(cast.Spell.TimePerMana, 1f) / 1000f;
                    cast.ManaInvested++;
                    break;
                case SpellCode.DontInvest when isHero:
                    break;
                default:
                    // Like the engine, any other answer ends the invest with the spell going off.
                    cast.Phase = SpellCastPhase.Casting;
                    break;
            }

            // G1: out of mana, the hero's spell goes off as if he let go.
            if (isHero && cast.Phase == SpellCastPhase.Investing && _contextGameVersionService.IsGothic1() &&
                cast.Caster.Vob.GetAttribute((int)NpcAttribute.Mana) <= 0)
                return ReleaseCast(cast);

            return cast.Phase;
        }

        /// <summary>
        /// The hero let go (OpenGothic Npc::endCastSpell): Spell_ProcessMana_Release says whether the spell goes off.
        /// </summary>
        public SpellCastPhase ReleaseCast(SpellCast cast)
        {
            if (cast.Phase != SpellCastPhase.Investing)
                return cast.Phase;

            if (ProcessMana(cast, "Spell_ProcessMana_Release") == SpellCode.SendCast)
                cast.Phase = SpellCastPhase.Casting;
            else
                Stop(cast);

            return cast.Phase;
        }

        /// <summary>
        /// The spell goes off (OpenGothic Npc::commitSpell): G2's Spell_Cast_[name] takes the mana, a projectile flies
        /// from the origin, any other spell shows at the caster and works on its target through the scripts. An NPC's
        /// scroll is used up - the hero's is the one in his hand (VR).
        /// </summary>
        /// <param name="origin">Where a projectile starts: the caster's hand.</param>
        /// <param name="direction">Where it flies.</param>
        public void CommitCast(SpellCast cast, Vector3 origin, Vector3 direction)
        {
            if (cast.Phase != SpellCastPhase.Casting)
                return;

            var level = cast.Level + 1;
            if (_contextGameVersionService.IsGothic2())
                CallSpellCastFunction(cast, level);
            cast.Phase = SpellCastPhase.Done;

            var effect = GetSpellEffect(cast.SpellId);
            if (_spellData.IsProjectile(cast.SpellId))
            {
                var rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
                var visual = _spellEffects.CreateProjectile(effect, cast.Spell, origin, rotation);
                _spellFlight ??= new SpellFlightDomain(OnProjectileHit).Inject();
                _spellFlight.Launch(cast, visual, origin, direction);
            }
            else
            {
                _spellEffects.CreateParticles(effect, origin, Quaternion.identity, _castEffectSeconds);
                if (cast.Target != null)
                    HitWithSpell(cast, cast.Target, 1);
            }

            Logger.Log($"[Magic] {cast.Caster.Instance.GetName(NpcNameSlot.Slot0)}'s {cast.Item.Name} goes off at " +
                       $"level {level} after {cast.ManaInvested} mana", LogCat.Fight);

            if (IsScroll(cast.Item) && !cast.Caster.IsHero())
                UseUpScroll(cast.Caster, cast.Item);
            if (cast.Caster.Props.SpellCast == cast)
                cast.Caster.Props.SpellCast = null;
        }

        /// <summary>
        /// Ends the cast without effect.
        /// </summary>
        public void Stop(SpellCast cast)
        {
            cast.Phase = SpellCastPhase.Stopped;
            if (cast.Caster.Props.SpellCast == cast)
                cast.Caster.Props.SpellCast = null;
        }

        private void OnProjectileHit(SpellCast cast, NpcContainer victim, Vector3 position)
        {
            var effect = GetSpellEffect(cast.SpellId);
            var impact = _spellEffects.GetEffect(victim != null ? effect?.EmFxCollDynS : effect?.EmFxCollStatS);
            _spellEffects.CreateParticles(impact, position, Quaternion.identity, _impactSeconds);

            if (victim != null)
                HitWithSpell(cast, victim, cast.Level + 1);
        }

        /// <summary>
        /// Like the engine (OpenGothic Npc::takeDamage with a spell): the victim learns which spell hit it and reacts
        /// (PERC_ASSESSMAGIC, B_AssessMagic). Hurtful spells take their damage off and make it fight back.
        /// </summary>
        private void HitWithSpell(SpellCast cast, NpcContainer victim, int level)
        {
            if (victim.Props == null || victim.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                return;

            var mask = _spellDamage.GetCollisionMask(cast.Caster, victim, cast.SpellId, cast.Spell);
            if (mask == SpellDamageDomain.CollDoNothing)
                return;

            victim.Props.LastHitSpell = cast.SpellId;
            SendAssessMagic(cast, victim);

            if (cast.Spell.SpellType != _spellBad)
                return;

            var damage = _spellDamage.CalculateDamage(victim, cast.Spell, level, mask);
            _fightService.OnSpellHit(cast.Caster, victim, damage, (mask & SpellDamageDomain.CollDontKill) == 0);
        }

        /// <summary>
        /// B_AssessMagic asks for the caster's active spell: the hero may have let go of his rune meanwhile.
        /// </summary>
        private void SendAssessMagic(SpellCast cast, NpcContainer victim)
        {
            if (victim.IsHero())
                return;

            var casterProps = cast.Caster.Props;
            var activeItem = casterProps.ActiveSpellItem;
            casterProps.ActiveSpellItem = cast.Item;
            try
            {
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessMagic, victim.Props,
                    victim.Instance, null, cast.Caster.Instance);
            }
            finally
            {
                casterProps.ActiveSpellItem = activeItem;
            }
        }

        private SpellCode ProcessMana(SpellCast cast, string functionName)
        {
            var vm = _gameStateService.GothicVm;
            var function = vm.GetSymbolByName(functionName);
            if (function is not { Type: DaedalusDataType.Function, IsExternal: false })
                return SpellCode.SendCast;

            var result = (int)SpellCode.SendStop;
            CallAsCaster(cast.Caster, cast.Target,
                () => result = vm.Call<int, int>(function.Index, cast.ManaInvested));
            // The upper bits are flags (SPL_FORCEINVEST).
            return (SpellCode)(result & 0xFFFF);
        }

        /// <summary>
        /// G2: Spell_Cast_[name] does the spell's work and takes its mana. With a parameter it gets the level.
        /// </summary>
        private void CallSpellCastFunction(SpellCast cast, int level)
        {
            var vm = _gameStateService.GothicVm;
            var function = vm.GetSymbolByName($"Spell_Cast_{_spellData.GetName(cast.SpellId)}");
            if (function is not { Type: DaedalusDataType.Function, IsExternal: false })
                return;

            CallAsCaster(cast.Caster, cast.Target, () =>
            {
                if (function.Size == 1)
                    vm.Call(function.Index, level);
                else
                    vm.Call(function.Index);
            });
        }

        /// <summary>
        /// The engine calls spell scripts with self = caster and other = target (OpenGothic GameScript::invokeMana).
        /// </summary>
        private void CallAsCaster(NpcContainer caster, NpcContainer target, Action call)
        {
            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = caster.Instance;
            if (target != null)
                vm.GlobalOther = target.Instance;
            try
            {
                call();
            }
            catch (Exception e)
            {
                Logger.LogError($"[Magic] Spell script of {caster.Instance.GetName(NpcNameSlot.Slot0)} failed: " +
                                e.Message, LogCat.Fight);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
        }

        /// <summary>
        /// Like the engine, an NPC's last scroll puts its magic away.
        /// </summary>
        private void UseUpScroll(NpcContainer caster, ItemInstance item)
        {
            _npcInventoryService.ExtRemoveInvItems(caster.Instance, item.Index, 1);
            if (_npcInventoryService.ExtNpcHasItems(caster.Instance, item.Index) > 0)
                return;

            caster.Props.ActiveSpellItem = null;
            _npcAiService.ExtAiUndrawWeapon(caster.Instance);
        }

        private void ChangeMana(NpcContainer npc, int change)
        {
            var mana = npc.Vob.GetAttribute((int)NpcAttribute.Mana);
            _npcService.SetAttribute(npc, NpcAttribute.Mana, Mathf.Max(mana + change, 0));
        }

        private EffectBaseInstance GetSpellEffect(int spellId)
        {
            var name = _spellData.GetName(spellId);
            return string.IsNullOrEmpty(name) ? null : _spellEffects.GetEffect($"spellFX_{name}");
        }

        private int GetCategory(int spellId)
        {
            return spellId < 0 ? _spellGood : _spellData.GetSpell(spellId)?.SpellType ?? _spellGood;
        }

        private int GetMageTalent()
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName("NPC_TALENT_MAGE");
            return symbol is { Type: DaedalusDataType.Int } ? symbol.GetInt(0) : _talentMageFallback;
        }

        private static NpcContainer GetNpc(NpcInstance npc)
        {
            return npc?.GetUserData();
        }
    }
}
