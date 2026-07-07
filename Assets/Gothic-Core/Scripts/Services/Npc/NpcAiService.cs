using System;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Services.Npc
{
    public class NpcAiService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly ConfigService _configService;


        public void ExtNpcPerceptionEnable(NpcInstance npc, VmGothicEnums.PerceptionType perception, int function)
        {
            npc.GetUserData().Props.Perceptions[perception] = function;
        }

        public void ExtNpcPerceptionDisable(NpcInstance npc, VmGothicEnums.PerceptionType perception)
        {
            npc.GetUserData().Props.Perceptions[perception] = -1;
        }

        /// <summary>
        /// Call an NPC Perception (active like Assess_Player or passive like Assess_Talk are possible).
        /// </summary>
        public void ExecutePerception(VmGothicEnums.PerceptionType type, NpcProperties properties, NpcInstance self, NpcInstance victim, NpcInstance other)
        {
            // Perception isn't set
            if (!properties.Perceptions.TryGetValue(type, out var perceptionFunction))
            {
                return;
            }
            // Perception is disabled
            else if (perceptionFunction < 0)
            {
                return;
            }

            var oldSelf = _gameStateService.GothicVm.GlobalSelf;
            var oldVictim = _gameStateService.GothicVm.GlobalVictim;
            var oldOther = _gameStateService.GothicVm.GlobalOther;

            _gameStateService.GothicVm.GlobalSelf = self;

            if(other != null)
            {
                _gameStateService.GothicVm.GlobalOther = other;
            }

            if(victim != null)
            {
                _gameStateService.GothicVm.GlobalVictim = victim;
            }

            // The finally block ensures a throwing perception function doesn't leave the globals
            // polluted for every subsequent script call of all NPCs.
            try
            {
                _gameStateService.GothicVm.Call(perceptionFunction);
            }
            finally
            {
                _gameStateService.GothicVm.GlobalSelf = oldSelf;
                _gameStateService.GothicVm.GlobalVictim = oldVictim;
                _gameStateService.GothicVm.GlobalOther = oldOther;
            }
        }

        public void CallVmFunctionWithNpcGlobals(string funcName, NpcInstance self, NpcInstance victim, NpcInstance other)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(funcName);
            if (symbol == null) return;

            var oldSelf = _gameStateService.GothicVm.GlobalSelf;
            var oldVictim = _gameStateService.GothicVm.GlobalVictim;
            var oldOther = _gameStateService.GothicVm.GlobalOther;

            _gameStateService.GothicVm.GlobalSelf = self;
            if (other != null) _gameStateService.GothicVm.GlobalOther = other;
            if (victim != null) _gameStateService.GothicVm.GlobalVictim = victim;

            try { _gameStateService.GothicVm.Call(symbol.Index); }
            finally
            {
                _gameStateService.GothicVm.GlobalSelf = oldSelf;
                _gameStateService.GothicVm.GlobalVictim = oldVictim;
                _gameStateService.GothicVm.GlobalOther = oldOther;
            }
        }

        public void ExtNpcSetPerceptionTime(NpcInstance npc, float time)
        {
            npc.GetUserData().Props.PerceptionTime = time;
        }

        public void ExtAiSetWalkMode(NpcInstance npc, VmGothicEnums.WalkMode walkMode)
        {
            npc.GetUserData()!.Vob.AiHuman.WalkMode = (int)walkMode;
        }

        public void ExtAiGoToWp(NpcInstance npc, string wayPointName)
        {
            Logger.Log($"[ExtAiGoToWp] {npc.GetName(NpcNameSlot.Slot0)}: dest={wayPointName}", LogCat.Ai);
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new GoToWp(
                new AnimationAction(wayPointName),
                npc.GetUserData()));
        }

        public void ExtAiAlignToWp(NpcInstance npc)
        {
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new AlignToWp(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiGoToFp(NpcInstance npc, string freePointName)
        {
            Logger.Log($"[ExtAiGoToFp] {npc.GetName(NpcNameSlot.Slot0)}: pattern={freePointName}", LogCat.Ai);
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new GoToFp(
                new AnimationAction(freePointName),
                npc.GetUserData()));
        }

        /// <summary>
        /// freeLOS - Free Line Of Sight == ignoreFOV
        /// fov = 50 - OpenGothic assumes 100 fov for NPCs
        /// fov = 30 - We reuse this for Focus angle during AI_Attack()
        /// </summary>
        public bool ExtNpcCanSeeNpc(NpcInstance self, NpcInstance other, bool freeLOS, float fov = 50f)
        {
            var otherContainer = other?.GetUserData();
            if (otherContainer != null && otherContainer.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return false;

            return _npcHelperService.CanSeeNpc(self, other, freeLOS, fov);
        }

        public void ExtNpcClearAiQueue(NpcInstance npc)
        {
            var container = npc.GetUserData();
            container.Props.AnimationQueue.Clear();

            // When called from inside the AiHandler combo-preload (IsInComboPreload=true), the
            // active AttackPlayAni must NOT be stopped — the combo window needs it alive so it can
            // cut the animation early once the next attack is queued by AI_Attack.
            // In all other contexts (B_FullStop, state transitions, etc.) stop immediately.
            if (container.PrefabProps != null && container.PrefabProps.AiHandler != null && container.PrefabProps.AiHandler.IsInComboPreload)
                return;

            // ZS_Attack_Loop periodically calls Npc_ClearAIQueue(self) + B_SelectWeapon(self, other)
            // every ~2s (Npc_GetStateTime(self) > 2) as a full tactic re-evaluation, even mid-fight.
            // A mage's spell readying (DrawWeapon raising hands) or actual cast (AttackPlayAni, whose
            // hit-frame fires Spell_ProcessMana) can easily take longer than that window — without this
            // exception, the reset kills the cast before it ever completes and the mage loops forever
            // re-readying the same spell. Same protection spirit as the combo-preload check above.
            if (container.Props.CurrentAction is DrawWeapon { IsMagicRequest: true })
                return;
            if (container.Props.CurrentAction is AttackPlayAni &&
                (VmGothicEnums.WeaponState)container.Vob.FightMode == VmGothicEnums.WeaponState.Mage)
                return;

            // If an UndrawWeapon is mid-animation when the queue is cleared, sheath the weapon
            // immediately so the mesh isn't left orphaned in the hand slot.
            if (container.Props.CurrentAction is UndrawWeapon activeUndraw)
                activeUndraw.SheathImmediately();

            container.Props.CurrentAction = new None(new AnimationAction(), container);
            container.PrefabProps?.AnimationSystem?.StopAllAnimations();
        }

        public void ExtAttack(NpcInstance npc)
        {
            var npcContainer = npc.GetUserData()!;
            npcContainer.Props.AnimationQueue.Enqueue(new Attack(
                new AnimationAction(),
                npcContainer));
        }

        public void ExtAiGoToNextFp(NpcInstance npc, string fpNamePart)
        {
            var npcContainer = npc.GetUserData();
            npcContainer.Props.AnimationQueue.Enqueue(new GoToNextFp(
                new AnimationAction(fpNamePart),
                npcContainer));
        }

        public void ExtAiWait(NpcInstance npc, float seconds)
        {
            var npcContainer = npc.GetUserData();
            npcContainer.Props.AnimationQueue.Enqueue(new Wait(
                new AnimationAction(float0: seconds),
                npcContainer));
        }

        public void ExtAiGoToNpc(NpcInstance self, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            self.GetUserData().Props.AnimationQueue.Enqueue(new GoToNpc(
                new AnimationAction(instance0: other),
                self.GetUserData()));
        }

        public void ExtAiPlayAni(NpcInstance npc, string name)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(name), npc.GetUserData()));
        }

        public void PlayAttackAni(NpcInstance npc, string name, FightAiMove move, NpcInstance moveTarget)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new AttackPlayAni(
                new AnimationAction(name, int0: (int)move, instance0: moveTarget),
                npc.GetUserData()));
        }

        public void ExtAiStartState(NpcInstance npc, int action, bool stopCurrentState, string wayPointName)
        {
            var other = (NpcInstance)_gameStateService.GothicVm.GlobalOther;
            var victim = (NpcInstance)_gameStateService.GothicVm.GlobalVictim;

            var container = npc.GetUserData();

            if (stopCurrentState)
            {
                container.Props.StateEnd = 0;
                container.Props.CurrentWayPoint = null;

                if (container.Props.AnimationQueue.OfType<UndrawWeapon>().Any())
                {
                    // UndrawWeapon is already queued (B_RemoveWeapon in ZS_Attack_End).
                    // Let it play the sheath animation; StartState enqueued below will follow it.
                    Logger.LogWarning($"[ExtAiStartState] pending UndrawWeapon — deferring state clear so sheath plays", LogCat.Fight);
                }
                else if (container.Props.AnimationQueue.OfType<StandUp>().Any())
                {
                    // StandUp was just enqueued (e.g. AI_StandUp in ZS_Unconscious_End before AI_StartState).
                    // Don't clear the queue — let the standup animation play; StartState follows it.
                }
                else
                {
                    container.PrefabProps?.AiHandler?.ClearState(false);
                }
            }

            container.Props.AnimationQueue.Enqueue(new StartState(
                new AnimationAction(int0: action, bool0: stopCurrentState, string0: wayPointName, instance0: other, instance1: victim),
                container));
        }

        public void ExtAiLookAt(NpcInstance npc, string wayPointName)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new LookAt(new AnimationAction(wayPointName), npc.GetUserData()));
        }

        public void ExtAiAlignToFp(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new AlignToFp(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiLookAtNpc(NpcInstance npc, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            npc.GetUserData().Props.AnimationQueue.Enqueue(new LookAtNpc(
                new AnimationAction(instance0: other),
                npc.GetUserData()));
        }

        public void ExtAiStopLookAt(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new StopLookAtNpc(
                new AnimationAction(),
                npc.GetUserData()));
        }

        public void ExtAiContinueRoutine(NpcInstance npc)
        {
            if (npc == null || npc.GetUserData() == null)
                return;
            npc.GetUserData().Props.AnimationQueue.Enqueue(new ContinueRoutine(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiUseMob(NpcInstance npc, string target, int state)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UseMob(
                new AnimationAction(target, state),
                npc.GetUserData()));
        }

        public void ExtAiStandUp(NpcInstance npc)
        {
            // FIXME - Implement remaining task from G1 documentation:
            // * Ist der Nsc in einem Animationsstate, wird die passende Rücktransition abgespielt (e.g. item states).
            var container = npc.GetUserData();
            var wasUnconscious = container.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious;
            // Reset immediately (not via queue) so Daedalus C_BodyStateContains checks in the same ZS_*_Loop tick see BsStand.
            container.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            if (wasUnconscious)
                container.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: "T_Wounded_2_Stand"), container));
            container.Props.AnimationQueue.Enqueue(new StandUp(new AnimationAction(), container));
        }

        public void ExtMdlApplyRandomAni(NpcInstance npc, string stateName, string transitionName)
        {
            var container = npc.GetUserData();
            if (container?.PrefabProps == null)
                return;

            _physicsService.DisablePhysicsForNpc(container.PrefabProps);
            // transitionName (T_Wounded_Try) is a "struggle to stand" random — not the fall animation.
            // Map state to the correct fall transition from HUMANS.MDS.
            var fallAni = stateName.Equals("S_WOUNDEDB", System.StringComparison.OrdinalIgnoreCase)
                ? "T_Stand_2_WoundedB"
                : "T_Stand_2_Wounded";
            container.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: fallAni), container));
            container.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(string0: stateName), container));
        }

        public void ExtAiTurnToNpc(NpcInstance npc, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            npc.GetUserData().Props.AnimationQueue.Enqueue(new TurnToNpc(
                new AnimationAction(instance0: other),
                npc.GetUserData()));
        }

        public void ExtAiPlayAniBs(NpcInstance npc, string name, int bodyState)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new PlayAniBs(new AnimationAction(name, bodyState), npc.GetUserData()));
        }

        public void ExtAiUnequipArmor(NpcInstance npc)
        {
            npc.GetUserData().Props.BodyData.Armor = 0;
        }

        /// <summary>
        /// Daedalus needs an int value.
        /// </summary>
        public int ExtNpcGetStateTime(NpcInstance npc)
        {
            // If there is no active running state, we immediately assume the current routine is running since the start of all beings.
            if (!npc.GetUserData().Props.IsStateTimeActive)
            {
                return int.MaxValue;
            }

            return (int)npc.GetUserData().Props.StateTime;
        }

        public void ExtNpcSetStateTime(NpcInstance npc, int seconds)
        {
            npc.GetUserData().Props.StateTime = seconds;
        }

        /// <summary>
        /// State means the final state where the animation shall go to.
        /// example:
        /// * itemId=xyz (ItFoBeer)
        /// * animationState = 0
        /// * ItFoBeer is of visual_scheme = Potion
        /// * expected state is t_Potion_Stand_2_S0 --> s_Potion_S0
        /// </summary>
        public void ExtAiUseItemToState(NpcInstance npc, int itemId, int animationState)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UseItemToState(
                new AnimationAction(int0: itemId, int1: animationState),
                npc.GetUserData()));
        }

        public bool ExtNpcWasInState(NpcInstance npc, uint action)
        {
            return npc.GetUserData().Vob.LastAiState == action;
        }

        public VmGothicEnums.BodyState ExtGetBodyState(NpcInstance npc)
        {
            return npc.GetUserData().Props.BodyState;
        }

        /// <summary>
        /// Return position distance in cm.
        /// </summary>
        public int ExtNpcGetDistToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            if (npc1 == null || npc2 == null)
                return int.MaxValue;

            var npc1Container = npc1.GetUserData();
            if (npc1Container?.Go == null)
                return int.MaxValue;

            var npc1Pos = npc1Container.Go.transform.position;

            // npc2 may be an ItemInstance passed as NpcInstance by Daedalus (e.g. Npc_GetDistToNpc(self, item)
            // in B_FetchWeapon). When GetUserData() returns null, treat as distance 0 so the proximity check passes.
            var npc2Container = npc2.GetUserData();
            if (npc2Container == null)
                return 0;

            Vector3 npc2Pos;
            // If hero: use camera position (VR head position is most accurate)
            if (npc2.Index == _gameStateService.GothicVm.GlobalHero?.Index)
            {
                npc2Pos = Camera.main!.transform.position;
            }
            else
            {
                var go = npc2Container.Go;

                // e.g. Triggered at Grd_214_Torwache_NODUSTY_Condition as Dusty is not yet spawned.
                if (go == null)
                    return int.MaxValue;
                npc2Pos = go.transform.position;
            }

            return (int)(Vector3.Distance(npc1Pos, npc2Pos) * 100);
        }

        /// <summary>
        /// Return height difference in cm.
        /// </summary>
        public int ExtNpcGetHeightToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            if (npc1 == null || npc2 == null)
                return 0;

            var npc1Pos = npc1.GetUserData().Go.transform.position;

            Vector3 npc2Pos;
            // If hero
            if (npc2.Id == 0)
                npc2Pos = Camera.main!.transform.position;
            else
                npc2Pos = npc2.GetUserData().Go.transform.position;

            return (int)((npc2Pos.y - npc1Pos.y) * 100);
        }

        public void ExtAiDrawWeapon(NpcInstance npc)
        {
            var container = npc.GetUserData();
            var fightMode = (VmGothicEnums.WeaponState)container.Vob.FightMode;
            var stateLoop = container.Props.StateLoop;
            var stateName = stateLoop != 0
                ? (_gameStateService.GothicVm.GetSymbolByIndex(stateLoop)?.Name ?? "?")
                : "NoState";
            Logger.LogWarning($"[AI_DrawWeapon] {npc.GetName(NpcNameSlot.Slot0)} fightMode={fightMode} stateLoop={stateName} — enqueueing DrawWeapon", LogCat.Fight);
            container.Props.AnimationQueue.Enqueue(new DrawWeapon(new AnimationAction(), container));
        }

        public void ExtAiReadyRangedWeapon(NpcInstance npc)
        {
            if (!_configService.Dev.EnableNpcRangedCombat)
            {
                // Ranged combat disabled — draw melee instead so the NPC doesn't stand empty-handed.
                ExtAiDrawWeapon(npc);
                return;
            }

            var container = npc.GetUserData();
            // int0 == 1 --> DrawWeapon picks the equipped ranged weapon instead of the melee one.
            container.Props.AnimationQueue.Enqueue(new DrawWeapon(new AnimationAction(int0: 1), container));
        }

        public void ExtAiUndrawWeapon(NpcInstance npc)
        {
            var container = npc.GetUserData();
            var fightMode = (VmGothicEnums.WeaponState)container.Vob.FightMode;
            Logger.LogWarning($"[AI_RemoveWeapon] {npc.GetName(NpcNameSlot.Slot0)} fightMode={fightMode} — enqueueing UndrawWeapon", LogCat.Fight);
            container.Props.AnimationQueue.Enqueue(new UndrawWeapon(new AnimationAction(), container));
        }

        public bool ExtNpcIsDead(NpcInstance npcInstance)
        {
            // FIXME - BodyState is runtime-only and lost on NPC reload (e.g. world reload respawns the NPC alive).
            // A permanent death flag needs to be persisted in SaveGame state and checked here instead.
            //
            // ZenKit's Pop<NpcInstance>() creates a fresh C# wrapper without UserData. We look up the
            // NpcContainer by NpcInstance.Index (Daedalus symbol index, embedded in the native instance).
            if (npcInstance == null) return false;
            // Try direct UserData first (set on original C# wrappers; null on fresh VM-popped ones).
            var container = npcInstance.GetUserData();
            if (container != null)
                return container.Props.BodyState == VmGothicEnums.BodyState.BsDead;

            // Fresh VM wrapper: UserData is null. Typical call site is ZS_Attack_Loop → C_NpcIsDown
            // which checks the current NPC's TargetNpc. GlobalSelf is set to the executing NPC by
            // AiHandler before every loop call, so we can resolve via the stored TargetNpc/EnemyNpc
            // reference (which has UserData). This correctly distinguishes multiple instances that
            // share the same SymbolIndex (e.g. three wolves: killing wolf #2 must not return isDead=false
            // because FirstOrDefault would find alive wolf #1).
            if (_gameStateService.GothicVm.GlobalSelf?.UserData is NpcContainer selfNpc)
            {
                var byTarget = selfNpc.Props.TargetNpc?.GetUserData();
                if (byTarget != null && byTarget.SymbolIndex == npcInstance.Index)
                    return byTarget.Props.BodyState == VmGothicEnums.BodyState.BsDead;
                var byEnemy = selfNpc.Props.EnemyNpc?.GetUserData();
                if (byEnemy != null && byEnemy.SymbolIndex == npcInstance.Index)
                    return byEnemy.Props.BodyState == VmGothicEnums.BodyState.BsDead;
            }

            // Generic fallback — may be incorrect for multi-instance symbols.
            container = _multiTypeCacheService.NpcCache.FirstOrDefault(n => n.SymbolIndex == npcInstance.Index);
            return container?.Props.BodyState == VmGothicEnums.BodyState.BsDead;
        }

        public bool ExtNpcIsInState(NpcInstance npc, int state)
        {
            var container = npc.GetUserData();
            if (container == null) return false;
            if (container.PrefabProps != null && container.PrefabProps.IsHero() &&
                container.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
            {
                // Hero doesn't run the Daedalus state machine — map BsUnconscious to ZS_Unconscious/ZS_MagicSleep.
                var stateName = _gameStateService.GothicVm.GetSymbolByIndex(state)?.Name;
                var result = stateName is "ZS_UNCONSCIOUS" or "ZS_MAGICSLEEP";
                Logger.Log($"[NpcAiService.IsInState] Hero unconscious — queried state={stateName} → {result}", LogCat.Npc);
                return result;
            }
            return container.Vob.CurrentStateIndex == state;
        }

        public bool ExtNpcIsPlayer(NpcInstance npc)
        {
            return npc.Index == _gameStateService.GothicVm.GlobalHero!.Index;
        }

        public ItemInstance ExtGetEquippedArmor(NpcInstance npc)
        {
            var armor = npc.GetUserData().Props.EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatArmor);

            return armor;
        }

        public bool ExtNpcHasEquippedArmor(NpcInstance npc)
        {
            return ExtGetEquippedArmor(npc) != null;
        }

        public bool ExtNpcIsInFightMode(NpcInstance npc, VmGothicEnums.FightMode fightMode)
        {
            var ws = (VmGothicEnums.WeaponState)npc.GetUserData().Vob.FightMode;
            return fightMode switch
            {
                VmGothicEnums.FightMode.None  => ws == VmGothicEnums.WeaponState.NoWeapon,
                VmGothicEnums.FightMode.Fists => ws == VmGothicEnums.WeaponState.Fist,
                VmGothicEnums.FightMode.Melee => ws == VmGothicEnums.WeaponState.W1H || ws == VmGothicEnums.WeaponState.W2H,
                VmGothicEnums.FightMode.Far   => ws == VmGothicEnums.WeaponState.Bow || ws == VmGothicEnums.WeaponState.CBow,
                VmGothicEnums.FightMode.Magic => ws == VmGothicEnums.WeaponState.Mage,
                _ => false
            };
        }

        public void ExtAiReadySpell(NpcInstance npc, int spellId, int investMana)
        {
            var container = npc.GetUserData();
            if (container == null) return;

            // ZS_Attack_Loop's periodic tactic re-evaluation (~every 2s) calls B_SelectWeapon again even
            // when nothing changed, which re-invokes AI_ReadySpell for the exact same spell. Replaying the
            // DrawWeapon "raise hands" animation every time would look like constantly re-switching spells
            // and (combined with the ExtNpcClearAiQueue exception above) still delay the cast — if this
            // spell is already the one in hand, just update the invested mana and leave the cast alone.
            var alreadyReadied = container.ActiveSpell == spellId &&
                (VmGothicEnums.WeaponState)container.Vob.FightMode == VmGothicEnums.WeaponState.Mage;

            container.ActiveSpell = spellId;
            container.ActiveSpellLevel = Math.Max(1, investMana);

            if (alreadyReadied)
            {
                Logger.Log($"[AI_ReadySpell] {npc.GetName(NpcNameSlot.Slot0)} spell={spellId} mana={investMana} — already readied, skipping re-draw", LogCat.Fight);
                return;
            }

            Logger.Log($"[AI_ReadySpell] {npc.GetName(NpcNameSlot.Slot0)} spell={spellId} mana={investMana} — enqueueing DrawWeapon(magic)", LogCat.Fight);
            container.Props.AnimationQueue.Enqueue(new DrawWeapon(new AnimationAction(int0: 2), container));
        }

        public bool ExtNpcOwnedByNpc(ItemInstance item, NpcInstance npc)
        {
            if (item == null)
            {
                return false;
            }

            return item.Owner == npc.Index;
        }

        // Shared by every auto target-acquisition heuristic below — a party member (summon/ally) should
        // never be picked up as a new/replacement target by these, only ever by the deliberate combat
        // flow (ActivatePartyMemberAttack) that mirrors the hero's own target.
        private bool IsPartyMember(NpcInstance npc)
        {
            var partySymbol = _gameStateService.GothicVm.GetSymbolByName("AIV_MM_PARTYMEMBER");
            if (partySymbol == null) return false;
            return npc.GetAiVar(partySymbol.GetInt(0)) == 1;
        }

        public VmGothicEnums.Attitude ExtGetAttitude(NpcInstance self, NpcInstance other)
        {
            var npc1 = self.GetUserData();
            var npc2 = other.GetUserData();
            if (npc1 == null || npc2 == null)
                return VmGothicEnums.Attitude.Neutral;

            return _npcHelperService.GetPersonAttitude(npc1, npc2);
        }

        // Npc_GetPermAttitude must return only the permanent attitude, never the temp override.
        // GetPersonAttitude returns temp when it differs, which breaks ZS_Attack_End's hostile check.
        public VmGothicEnums.Attitude ExtGetPermAttitude(NpcInstance self, NpcInstance other)
        {
            var npc1 = self.GetUserData();
            var npc2 = other.GetUserData();
            if (npc1 == null || npc2 == null)
                return VmGothicEnums.Attitude.Neutral;

            if (npc2.PrefabProps?.IsHero() == true)
                return (VmGothicEnums.Attitude)npc1.Vob.Attitude;

            return _npcHelperService.GetPersonAttitude(npc1, npc2);
        }

        /// <summary>
        /// HINT: These values are only used when checking the attitude towards the player
        /// HINT: for attitudes between NPC we directly use the guild attitude
        /// </summary>
        public void ExtSetAttitude(NpcInstance npc, VmGothicEnums.Attitude value)
        {
            npc.GetUserData().Vob.Attitude = (int)value;
        }
        
        /// <summary>
        /// HINT: These values are only used when checking the attitude towards the player
        /// HINT: for attitudes between NPC we directly use the guild attitude
        /// </summary>
        public void ExtSetTempAttitude(NpcInstance npc, VmGothicEnums.Attitude value)
        {
            npc.GetUserData().Vob.AttitudeTemp = (int)value;
        }

        public bool ExtGetTarget(NpcInstance npc)
        {
            var target = npc.GetUserData().Props.TargetNpc;

            if (target == null)
            {
                return false;
            }

            // Npc_GetTarget() also fills >other< with the target - scripts use it immediately afterwards.
            _gameStateService.GothicVm.GlobalOther = target;
            return true;
        }

        public void ExtSetTarget(NpcInstance npc, NpcInstance target)
        {
            npc.GetUserData().Props.TargetNpc = target;
        }

        public int ExtGetNextTarget(NpcInstance npc)
        {
            var selfNpc = npc.GetUserData();
            var selfPosition = selfNpc.Go.transform.position;

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            var sensesRangeMeters = npc.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate.Props == null || candidate.Go == null)
                    continue;
                if (candidate.Instance.Index == npc.Index)
                    continue;
                if (candidate.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                    continue;

                // Party members (summons/allies) are never a valid auto-acquired target, full stop —
                // not even "unless they already provoked me." That carve-out used to check whether
                // self's OWN target field already pointed at the candidate, which is trivially true
                // once something else has already (wrongly) force-switched onto them, so it let a
                // summon get picked right back up as "next target" after the bug that put it there in
                // the first place. NPCs should always re-focus the hero (the real aggressor), not a
                // summon that's just piling on damage alongside them.
                if (IsPartyMember(candidate.Instance))
                    continue;

                // Same-guild NPCs are never a valid auto-acquired target — vanilla never exercises
                // Npc_GetNextTarget() against a fellow guild member, so an under-specified guild-attitude
                // entry (or a leftover personal attitude flag) resolving to Hostile between two guards
                // has no vanilla behavior to fall back on. Safety net regardless of the attitude table.
                if (candidate.Instance.Guild == npc.Guild)
                    continue;

                // A summon never targets its own summoner, even when their guilds differ (e.g. a
                // SkeletonMage is GIL_DEMON so it "flies" instead of wading, but its GIL_SKELETON
                // summons don't share that guild) — see NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index)
                    continue;

                if (ExtGetAttitude(npc, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                    continue;

                closestSqrDist = sqrDist;
                closestEnemy = candidate;
            }

            if (closestEnemy == null)
            {
                Logger.Log($"[GetNextTarget] {npc.GetName(NpcNameSlot.Slot0)}: no next target", LogCat.Fight);
                return 0;
            }

            selfNpc.Props.TargetNpc = closestEnemy.Instance;
            selfNpc.Props.EnemyNpc = closestEnemy.Instance;
            _gameStateService.GothicVm.GlobalOther = closestEnemy.Instance;
            Logger.Log($"[GetNextTarget] {npc.GetName(NpcNameSlot.Slot0)} → {closestEnemy.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);
            return 1;
        }

        public void Npc_SendPassivePerc(NpcInstance npc,VmGothicEnums.PerceptionType perc, NpcInstance victim, NpcInstance other)
        {
            ExecutePerception(perc, npc.GetUserData().Props, npc, victim, other);
        }

        public void ExtSetTrueGuild(NpcInstance npc, int guild)
        {
            npc.GetUserData().Props.TrueGuild = (VmGothicEnums.Guild) guild;
        }

        public int ExtGetTrueGuild(NpcInstance npc)
        {
            var npcUserData = npc.GetUserData();
            var npcGuild  = npcUserData.Props.TrueGuild;

            return npcGuild == 0 ? // No True Guild
                npc.Guild : (int)npcGuild;
        }
        

        public void UpdateEnemyNpc(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            var selfPosition = selfNpc.Go.transform.position;

            var sensesRangeMeters = self.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            // Keep the current target if it's still a valid hostile, instead of re-scanning "closest
            // hostile" completely from scratch every tick. Without this, any momentary crossover in
            // relative distance between two similarly-far candidates flips the target — and once
            // flipped, the NPC's already-active combat state just keeps pursuing the new one, with no
            // way back. Confirmed via logs: scavengers that first reacted to the hero permanently
            // switched to attacking a nearby bystander NPC once it became marginally closer, and never
            // reconsidered even though the hero (the actual provoker) stayed just as close.
            var currentTarget = selfNpc.Props.EnemyNpc;
            if (currentTarget != null)
            {
                var currentTargetNpc = currentTarget.GetUserData();
                // Unconscious excluded too, not just dead — an incapacitated target isn't an ongoing
                // fight to keep confirming every tick (matches ExtGetNextTarget, which already
                // excludes both states).
                var stillValid = currentTargetNpc != null &&
                                  currentTargetNpc.Go != null &&
                                  currentTargetNpc.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                                  currentTargetNpc.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious &&
                                  currentTarget.Guild != self.Guild &&
                                  !IsPartyMember(currentTarget) &&
                                  (selfNpc.SummonedBy == null || selfNpc.SummonedBy.Index != currentTarget.Index) &&
                                  ExtGetAttitude(self, currentTarget) == VmGothicEnums.Attitude.Hostile &&
                                  (currentTargetNpc.Go.transform.position - selfPosition).sqrMagnitude <= sensesRangeSqr &&
                                  _npcHelperService.CanSenseNpc(self, currentTarget, true);
                if (stillValid)
                {
                    selfNpc.Props.TargetNpc = currentTarget;
                    return;
                }
            }

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            // FIXME - Performance - Can we clean this up to support only spawned and visible NPCs/Monsters?
            //         A spatial lookup (e.g. the culling system's distance buckets) would avoid the full scan.
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                // Fast-fail checks in order of cheapest first
                if (candidate.Props == null || candidate.Go == null)
                    continue;

                if (candidate.Instance.Index == self.Index)
                    continue;

                // Corpses aren't enemies, and neither is someone currently knocked out.
                if (candidate.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;

                // Range and closest-so-far gates before the expensive attitude and senses checks.
                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                    continue;

                // Safety net regardless of the guild-attitude table — see ExtGetNextTarget.
                if (candidate.Instance.Guild == self.Guild)
                    continue;

                // Party members (summons/allies) never a valid auto-acquired target — see ExtGetNextTarget.
                if (IsPartyMember(candidate.Instance))
                    continue;

                // A summon never targets its own summoner — see ExtGetNextTarget / NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index)
                    continue;

                if (ExtGetAttitude(self, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                    continue;

                // Hearing/smell detect through walls (matches vanilla — e.g. a sleeping molerat wakes
                // to nearby footsteps without seeing the source); only SENSE_SEE requires LOS+FOV.
                // CanSenseNpc(freeLOS=true) handles that split internally.
                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                    continue;

                closestSqrDist = sqrDist;
                closestEnemy = candidate;
            }

            // Mirror into TargetNpc too (consumed by ZS_Attack_Loop's Npc_GetTarget()). Must also be
            // cleared to null when no hostile is found — otherwise a stale TargetNpc (e.g. hero, from
            // before this NPC's attitude was changed to Friendly/Neutral) keeps being picked up by the
            // Daedalus loop forever since Npc_GetTarget() never re-checks attitude itself.
            selfNpc.Props.EnemyNpc = closestEnemy?.Instance;
            selfNpc.Props.TargetNpc = closestEnemy?.Instance;
        }

        /// <summary>
        /// Switches focus to a nearby NPC/monster that is actively fighting >self< (has self as their
        /// own TargetNpc/EnemyNpc), even when self's current target is hostile-by-guild-table but out of
        /// sight (e.g. the hero heard through a wall). This covers companions (Cavalorn) and monsters
        /// (wolves) engaging in melee, whose guild relation to self isn't marked Hostile in the guild
        /// table. Unlike <see cref="UpdateEnemyNpc"/>, this is NOT gated behind PERC_ASSESSENEMY being
        /// registered — combat states like ZS_Attack don't register that perception at all, so without
        /// this NPCs already locked onto the hero would never notice someone else engaging them in melee.
        /// </summary>
        public void UpdateActiveAttackerTarget(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null)
                return;

            // Keep the current target if it's still alive and visible — avoids thrashing away from a
            // valid fight once LOS to it is (re)established.
            var currentTarget = selfNpc.Props.TargetNpc;
            if (currentTarget != null)
            {
                var currentTargetNpc = currentTarget.GetUserData();
                if (currentTargetNpc != null &&
                    currentTargetNpc.Props.BodyState != VmGothicEnums.BodyState.BsDead &&
                    ExtNpcCanSeeNpc(self, currentTarget, false))
                    return;
            }

            var selfPosition = selfNpc.Go.transform.position;
            var sensesRangeMeters = self.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            NpcContainer attacker = null;
            var closestSqrDist = float.MaxValue;

            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate.Props == null || candidate.Go == null) continue;
                if (candidate.Instance.Index == self.Index) continue;
                if (candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead) continue;

                // Candidate must currently be fighting self (has self as their own target/enemy).
                var targetIdx = candidate.Props.TargetNpc?.Index ?? -1;
                var enemyIdx = candidate.Props.EnemyNpc?.Index ?? -1;
                if (targetIdx != self.Index && enemyIdx != self.Index) continue;

                // Never auto-switch onto someone explicitly Friendly to self (e.g. an NPC the player
                // pacified/saved earlier) even if a stale target reference points at self. Same-guild
                // is excluded unconditionally too — safety net regardless of the guild-attitude table.
                // Party members excluded unconditionally as well, even though the precondition above
                // already means one is "actively fighting self" — a summon landing incidental hits on
                // an NPC that's really fighting the hero shouldn't steal that NPC's focus (see ExtGetNextTarget).
                if (ExtGetAttitude(self, candidate.Instance) == VmGothicEnums.Attitude.Friendly) continue;
                if (candidate.Instance.Guild == self.Guild) continue;
                if (IsPartyMember(candidate.Instance)) continue;
                // A summon never targets its own summoner — see ExtGetNextTarget / NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index) continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist) continue;
                // Hearing/smell detect through walls, matching the "heard through a wall" case this
                // method is meant to cover (see doc comment above) — only SENSE_SEE requires LOS+FOV.
                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true)) continue;

                closestSqrDist = sqrDist;
                attacker = candidate;
            }

            if (attacker == null)
                return;

            selfNpc.Props.EnemyNpc = attacker.Instance;
            selfNpc.Props.TargetNpc = attacker.Instance;
        }

        // B_AssessFighter.d has "if (!Npc_IsPlayer(other)) return" — so vanilla Daedalus never
        // makes NPCs react to an armed non-player NPC approaching them. We add that missing
        // NPC→NPC armed-threat detection here: if an NPC with drawn weapon is actively engaged
        // in combat (TargetNpc set) and visible within ~4m, self enters ZS_AssessFighter just
        // as it would if the player had drawn a weapon nearby.
        public void CheckForArmedNpcThreat(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null) return;

            const float threatRangeMeters = 4f; // slightly beyond HAI_DIST_MELEE (~3m)
            var selfPos = selfNpc.Go.transform.position;

            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate.Props == null || candidate.Go == null) continue;
                if (candidate.Instance.Index == self.Index) continue;
                if (candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead) continue;

                var weaponState = (VmGothicEnums.WeaponState)candidate.Vob.FightMode;
                if (weaponState == VmGothicEnums.WeaponState.NoWeapon ||
                    weaponState == VmGothicEnums.WeaponState.Fist) continue;

                // Only react to NPCs actively engaged in combat, not patrol NPCs holding ranged weapons.
                if (candidate.Props.TargetNpc == null) continue;

                if (Vector3.Distance(selfPos, candidate.Go.transform.position) > threatRangeMeters) continue;

                // Mirror B_AssessFighter: ignore friendly NPCs (e.g. allies training), react to all others.
                // Same-guild excluded unconditionally too — this mechanic has no vanilla precedent for
                // guild-mates fighting each other, so don't rely solely on the guild-attitude table for it.
                // Party members excluded too — a summon actively fighting nearby shouldn't itself read as
                // an "armed threat" to bystanders; they should still only ever focus the hero.
                if (ExtGetAttitude(self, candidate.Instance) == VmGothicEnums.Attitude.Friendly) continue;
                if (candidate.Instance.Guild == self.Guild) continue;
                if (IsPartyMember(candidate.Instance)) continue;
                // A summon never treats its own summoner as an armed threat — see ExtGetNextTarget / NpcContainer.SummonedBy.
                if (selfNpc.SummonedBy != null && selfNpc.SummonedBy.Index == candidate.Instance.Index) continue;

                if (!ExtNpcCanSeeNpc(self, candidate.Instance, false)) continue;

                var zsAssessFighterSym = _gameStateService.GothicVm.GetSymbolByName("ZS_AssessFighter");
                if (zsAssessFighterSym == null) return;

                selfNpc.Props.EnemyNpc = candidate.Instance;
                selfNpc.Props.TargetNpc = candidate.Instance;
                Logger.Log($"[NpcAiService] {self.GetName(NpcNameSlot.Slot0)} reacts to armed {candidate.Instance.GetName(NpcNameSlot.Slot0)} — ZS_AssessFighter", LogCat.Fight);

                var oldOther = _gameStateService.GothicVm.GlobalOther;
                _gameStateService.GothicVm.GlobalOther = candidate.Instance;
                ExtAiStartState(self, zsAssessFighterSym.Index, false, "");
                _gameStateService.GothicVm.GlobalOther = oldOther;
                break;
            }
        }

        public void ExtSetRefuseTalk(NpcInstance self, int refuseSeconds)
        {
            self.GetUserData().Props.RefuseTalkTimer = refuseSeconds;
        }

        public bool ExtRefuseTalk(NpcInstance self)
        {
            return self.GetUserData().Props.RefuseTalkTimer > 0f;
        }
    }
}
