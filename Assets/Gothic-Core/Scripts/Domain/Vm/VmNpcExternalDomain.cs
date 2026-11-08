using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Doc;
using Gothic.Core.Creator;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Daedalus Npc_* externals (state, inventory, perception, fight, routines) and the NPC related ones without the
    /// prefix (CreateInvItem, EquipItem, TA).
    /// </summary>
    public class VmNpcExternalDomain
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly RoomService _roomService;

        private readonly VmExternalSupport _support;

        // A missing or foreign NPC parameter is neutral, like NpcAiService.ExtGetAttitude (0 is ATT_HOSTILE).
        private const int NeutralAttitude = (int)VmGothicEnums.Attitude.Neutral;

        public VmNpcExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            // Engine externals some scripts (mostly mods) call - only registered where the scripts declare them
            // (G1 has no AI_StopFX).
            if (vm.GetSymbolByName("Npc_IsWayBlocked") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_IsWayBlocked", Npc_IsWayBlocked);
            RegisterSmallExternals(vm);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_SetTalentValue", Npc_SetTalentValue);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_ChangeAttribute", Npc_ChangeAttribute);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_PercEnable", Npc_PercEnable);
            vm.RegisterExternal<NpcInstance, float>("Npc_SetPercTime", Npc_SetPercTime);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetPermAttitude",
                (a, b) =>
                    _support.AsNpc(a, "Npc_GetPermAttitude") is { } first &&
                        _support.AsNpc(b, "Npc_GetPermAttitude") is { } second
                    ? Npc_GetPermAttitude(first, second) : NeutralAttitude);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetAttitude",
                (a, b) =>
                    _support.AsNpc(a, "Npc_GetAttitude") is { } first &&
                        _support.AsNpc(b, "Npc_GetAttitude") is { } second
                    ? Npc_GetAttitude(first, second) : NeutralAttitude);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetAttitude", Npc_SetAttitude);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetTempAttitude", Npc_SetTempAttitude);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetBodyState", Npc_GetBodyState);
            vm.RegisterExternal<NpcInstance>("Npc_PerceiveAll", Npc_PerceiveAll);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetStateTime", Npc_GetStateTime);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetStateTime", Npc_SetStateTime);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_SetTalentSkill", Npc_SetTalentSkill);
            vm.RegisterExternal<string, NpcInstance>("Npc_GetNearestWP", Npc_GetNearestWP);
            vm.RegisterExternal<int, NpcInstance, string>("Npc_IsOnFP", Npc_IsOnFP);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_WasInState", Npc_WasInState);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetDistToNpc",
                (a, b) => Npc_GetDistToNpcOrItem(a, b));
            vm.RegisterExternal<int, NpcInstance, int>("Npc_HasSpell", Npc_HasSpell);
            vm.RegisterExternal<int, NpcInstance, string>("Npc_GetDistToWP", Npc_GetDistToWP);
            vm.RegisterExternal<NpcInstance, int>("Npc_PercDisable", Npc_PercDisable);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeNpc",
                (a, b) =>
                    _support.AsNpc(a, "Npc_CanSeeNpc") is { } first && _support.AsNpc(b, "Npc_CanSeeNpc") is { } second
                    ? Npc_CanSeeNpc(first, second) : 0);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeNpcFreeLOS",
                (a, b) =>
                    _support.AsNpc(a, "Npc_CanSeeNpcFreeLOS") is { } first &&
                        _support.AsNpc(b, "Npc_CanSeeNpcFreeLOS") is { } second
                    ? Npc_CanSeeNpcFreeLOS(first, second) : 0);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeItem",
                (n, i) =>
                    _support.AsNpc(n, "Npc_CanSeeItem") is { } npc && _support.AsItem(i, "Npc_CanSeeItem") is { } item
                    ? Npc_CanSeeItem(npc, item) : 0);
            vm.RegisterExternal<NpcInstance>("Npc_ClearAiQueue", Npc_ClearAiQueue);
            vm.RegisterExternal<string, NpcInstance>("Npc_GetNextWp", Npc_GetNextWp);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_GetTalentSkill", Npc_GetTalentSkill);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_GetTalentValue", Npc_GetTalentValue);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_KnowsInfo", Npc_KnowsInfo);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_CheckInfo", Npc_CheckInfo);
            vm.RegisterExternal<int, NpcInstance>("Npc_IsDead", Npc_IsDead);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_IsInState", Npc_IsInState);
            vm.RegisterExternal<NpcInstance>("Npc_SetToFistMode", Npc_SetToFistMode);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetToFightMode", Npc_SetToFightMode);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_IsInFightMode", Npc_IsInFightMode);
            vm.RegisterExternal<int, NpcInstance>("Npc_IsPlayer", Npc_IsPlayer);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpell", Npc_GetActiveSpell);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellLevel", Npc_GetActiveSpellLevel);
            // G1 uses it too (B_AssessFighter, ZS_AssessMagic, ...).
            vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellCat", Npc_GetActiveSpellCat);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_OwnedByNpc",
                (i, n) =>
                    _support.AsItem(i, "Npc_OwnedByNpc") is { } item && _support.AsNpc(n, "Npc_OwnedByNpc") is { } npc
                    ? Npc_OwnedByNpc(item, npc) : 0);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTarget", Npc_GetTarget);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetNextTarget", Npc_GetNextTarget);
            vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_SetTarget", Npc_SetTarget);
            vm.RegisterExternal<NpcInstance, int, NpcInstance, NpcInstance>("Npc_SendPassivePerc", Npc_SendPassivePerc);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_SetTrueGuild", Npc_SetTrueGuild);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTrueGuild", Npc_GetTrueGuild);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetRefuseTalk", Npc_SetRefuseTalk);
            vm.RegisterExternal<int, NpcInstance>("Npc_RefuseTalk", Npc_RefuseTalk);
            vm.RegisterExternal<int, NpcInstance, int, NpcInstance, NpcInstance>("Npc_HasNews", Npc_HasNews);
            // G2 externals only: G1's scripts (and mods) that don't declare them would make the registration throw.
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetHeightToNpc",
                () => vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetHeightToNpc",
                    (a, b) =>
                        _support.AsNpc(a, "Npc_GetHeightToNpc") is { } first &&
                        _support.AsNpc(b, "Npc_GetHeightToNpc") is { } second
                            ? Npc_GetHeightToNpc(first, second) : 0));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_StopAni",
                () => vm.RegisterExternal<NpcInstance, string>("Npc_StopAni", Npc_StopAni));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_IsDrawingSpell",
                () => vm.RegisterExternal<int, NpcInstance>("Npc_IsDrawingSpell", Npc_IsDrawingSpell));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetActiveSpellIsScroll",
                () => vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellIsScroll", Npc_GetActiveSpellIsScroll));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetPortalGuild",
                () => vm.RegisterExternal<int, NpcInstance>("Npc_GetPortalGuild", Npc_GetPortalGuild));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_IsInPlayersRoom",
                () => vm.RegisterExternal<int, NpcInstance>("Npc_IsInPlayersRoom", Npc_IsInPlayersRoom));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetDetectedMob",
                () => vm.RegisterExternal<string, NpcInstance>("Npc_GetDetectedMob", Npc_GetDetectedMob));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_IsDetectedMobOwnedByNpc",
                () => vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_IsDetectedMobOwnedByNpc",
                    (a, b) =>
                        _support.AsNpc(a, "Npc_IsDetectedMobOwnedByNpc") is { } first &&
                        _support.AsNpc(b, "Npc_IsDetectedMobOwnedByNpc") is { } second
                            ? Npc_IsDetectedMobOwnedByNpc(first, second) : 0));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_IsDetectedMobOwnedByGuild",
                () => vm.RegisterExternal<int, NpcInstance, int>("Npc_IsDetectedMobOwnedByGuild",
                    Npc_IsDetectedMobOwnedByGuild));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_CanSeeSource",
                () => vm.RegisterExternal<int, NpcInstance>("Npc_CanSeeSource", Npc_CanSeeSource));
            VmExternalSupport.RegisterIfDeclared(vm, "Snd_IsSourceNpc",
                () => vm.RegisterExternal<int, NpcInstance>("Snd_IsSourceNpc", Snd_IsSourceNpc));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetDistToItem",
                () => vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetDistToItem",
                    (n, i) =>
                        _support.AsNpc(n, "Npc_GetDistToItem") is { } npc &&
                        _support.AsItem(i, "Npc_GetDistToItem") is { } item
                            ? Npc_GetDistToItem(npc, item) : int.MaxValue));
            VmExternalSupport.RegisterIfDeclared(vm, "Npc_GetHeightToItem",
                () => vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetHeightToItem",
                    (n, i) =>
                        _support.AsNpc(n, "Npc_GetHeightToItem") is { } npc &&
                        _support.AsItem(i, "Npc_GetHeightToItem") is { } item
                            ? Npc_GetHeightToItem(npc, item) : int.MaxValue));
            // Transformation scrolls (Spell_Logic_Trf_*): the monster instance the hero becomes (VRTransformService).
            if (vm.GetSymbolByName("Npc_SetActiveSpellInfo") != null)
                vm.RegisterExternal<int, NpcInstance, int>("Npc_SetActiveSpellInfo", Npc_SetActiveSpellInfo);
            // G2 only - registering an external the scripts don't declare throws "Symbol not found" (G1/MT).
            if (vm.GetSymbolByName("Npc_GetLookAtTarget") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_GetLookAtTarget", Npc_GetLookAtTarget);
            vm.RegisterExternal<NpcInstance, int, int, int, int, int, string>("TA_MIN", TA_MIN);
            vm.RegisterExternal<NpcInstance, int, int, int, string>("TA", Ta);
            vm.RegisterExternal<NpcInstance, string>("Npc_ExchangeRoutine", Npc_ExchangeRoutine);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_IsInRoutine", Npc_IsInRoutine);
            // G2: B_AssessFighter etc.
            if (vm.GetSymbolByName("Npc_IsDrawingWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_IsDrawingWeapon", Npc_IsDrawingWeapon);
        }

        /// <summary>
        /// DoM asks it on every perception (B_AssessPlayer) to let NPCs step aside. Our walking has no "blocked"
        /// detection yet - the way is free, as before, just without a warning per call.
        /// </summary>
        public int Npc_IsWayBlocked(NpcInstance npc)
        {
            return _support.LogInstantExternal(nameof(Npc_IsWayBlocked), 0, npc);
        }

        /// <summary>
        /// The reference engine's externals the scripts call and we lacked: remarks while busy, pointing, body flags,
        /// single perceptions... Only the ones the game's scripts declare.
        /// </summary>
        private void RegisterSmallExternals(VmExternalGuard vm)
        {
            bool IsDeclared(string name) => vm.GetSymbolByName(name) != null;

            if (IsDeclared("AI_PointAt"))
                vm.RegisterExternal<NpcInstance, string>("AI_PointAt", _npcAiService.ExtAiPointAt);
            if (IsDeclared("AI_PointAtNpc"))
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_PointAtNpc", _npcAiService.ExtAiPointAtNpc);
            if (IsDeclared("AI_StopPointAt"))
                vm.RegisterExternal<NpcInstance>("AI_StopPointAt", _npcAiService.ExtAiStopPointAt);
            if (IsDeclared("Npc_HasBodyFlag"))
                vm.RegisterExternal<int, NpcInstance, int>("Npc_HasBodyFlag",
                    (npc, flag) => Convert.ToInt32(_npcAiService.ExtNpcHasBodyFlag(npc, flag)));
            if (IsDeclared("Npc_SendSinglePerc"))
                vm.RegisterExternal<NpcInstance, NpcInstance, int>("Npc_SendSinglePerc",
                    _npcAiService.ExtNpcSendSinglePerc);
            if (IsDeclared("Npc_PlayAni"))
                vm.RegisterExternal<NpcInstance, string>("Npc_PlayAni", _npcAiService.ExtNpcPlayAni);
            if (IsDeclared("Npc_HasEquippedWeapon"))
                vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedWeapon",
                    npc => Convert.ToInt32(_npcHelperService.ExtNpcHasEquippedMeleeWeapon(npc) ||
                                           _npcHelperService.ExtNpcHasEquippedRangedWeapon(npc)));
            if (IsDeclared("Npc_GetDistToPlayer"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetDistToPlayer", _npcAiService.ExtNpcGetDistToPlayer);
            if (IsDeclared("Npc_GetInvItem"))
                vm.RegisterExternal<ItemInstance, NpcInstance, int>("Npc_GetInvItem",
                    _npcInventoryService.ExtNpcGetInvItem);
            if (IsDeclared("Npc_IsInCutscene"))
                vm.RegisterExternal<int, NpcInstance>("Npc_IsInCutscene",
                    npc => Convert.ToInt32(_npcAiService.ExtNpcIsInCutscene(npc)));
            if (IsDeclared("Npc_SetKnowsPlayer"))
                vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_SetKnowsPlayer",
                    (npc, player) => _npcAiService.ExtNpcSetKnowsPlayer(npc));
            if (IsDeclared("Npc_KnowsPlayer"))
                vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_KnowsPlayer",
                    (npc, player) => Convert.ToInt32(_npcAiService.ExtNpcKnowsPlayer(npc)));
            // G2's language switches - the strings come from the installed game.
            if (IsDeclared("Game_InitGerman"))
                vm.RegisterExternal("Game_InitGerman", () => { });
            if (IsDeclared("Game_InitEnglish"))
                vm.RegisterExternal("Game_InitEnglish", () => { });
        }

        public int Npc_SetActiveSpellInfo(NpcInstance npc, int info)
        {
            var container = npc?.GetUserData();
            if (container != null)
                container.ActiveSpellInfo = info;
            return _support.LogInstantExternal(nameof(Npc_SetActiveSpellInfo), 0, npc, info);
        }

        /// <summary>
        /// G2: the NPC is drawing its weapon right now (AI_DrawWeapon running).
        /// </summary>
        public int Npc_IsDrawingWeapon(NpcInstance npc)
        {
            var ret = Convert.ToInt32(npc?.GetUserData()?.Props.CurrentAction is
                Gothic.Core.Domain.Npc.Actions.AnimationActions.DrawWeapon);
            return _support.LogInstantExternal(nameof(Npc_IsDrawingWeapon), ret, npc);
        }

        public NpcInstance Npc_GetLookAtTarget(NpcInstance npc)
        {
            var target = npc?.GetUserData()?.Props.LookAtTarget;
            return _support.LogInstantExternal(nameof(Npc_GetLookAtTarget), target, npc);
        }

        public void Npc_SetTalentValue(NpcInstance npc, int talent, int level)
        {
            _npcService.ExtNpcSetTalentValue(npc, (VmGothicEnums.Talent)talent, level);
            _support.LogInstantExternal(nameof(Npc_ChangeAttribute), npc, talent, level);
        }

        public void Npc_ChangeAttribute(NpcInstance npc, int attributeId, int value)
        {
            _npcService.ExtNpcChangeAttribute(npc, attributeId, value);
            _support.LogInstantExternal(nameof(Npc_ChangeAttribute), npc, attributeId, value);
        }

        public void Npc_PercEnable(NpcInstance npc, int perception, int function)
        {
            _npcAiService.ExtNpcPerceptionEnable(npc, (VmGothicEnums.PerceptionType)perception, function);
            _support.LogInstantExternal(nameof(Npc_PercEnable), perception, function);
        }

        public void Npc_SetPercTime(NpcInstance npc, float time)
        {
            _npcAiService.ExtNpcSetPerceptionTime(npc, time);
            _support.LogInstantExternal(nameof(Npc_SetPercTime), npc, time);
        }

        public int Npc_GetPermAttitude(NpcInstance self, NpcInstance other)
        {
            var ret = (int)_npcAiService.ExtGetPermAttitude(self, other);
            return _support.LogInstantExternal(nameof(Npc_GetPermAttitude), ret, self, other);
        }

        public int Npc_GetAttitude(NpcInstance self, NpcInstance other)
        {
            var ret = (int)_npcAiService.ExtGetAttitude(self, other);
            return _support.LogInstantExternal(nameof(Npc_GetAttitude), ret, self, other);
        }

        public void Npc_SetAttitude(NpcInstance self, int attitude)
        {
            _npcAiService.ExtSetAttitude(self, (VmGothicEnums.Attitude)attitude);
        }

        public void Npc_SetTempAttitude(NpcInstance self, int tempAttitude)
        {
            _npcAiService.ExtSetTempAttitude(self, (VmGothicEnums.Attitude)tempAttitude);
        }

        public int Npc_GetBodyState(NpcInstance npc)
        {
            return (int)_npcAiService.ExtGetBodyState(npc);
        }

        public void Npc_PerceiveAll(NpcInstance npc)
        {
            // NOP

            // Gothic loads all the necessary items into memory to reference them later via Wld_DetectNpc() and
            // Wld_DetectItem().
            // But we don't need to pre-load them and can just load the necessary elements when really needed.
        }

        public int Npc_GetStateTime(NpcInstance npc)
        {
            var stateTime = _npcAiService.ExtNpcGetStateTime(npc);
            return _support.LogInstantExternal(nameof(Npc_GetStateTime), stateTime, npc);
        }

        public void Npc_SetStateTime(NpcInstance npc, int seconds)
        {
            _npcAiService.ExtNpcSetStateTime(npc, seconds);
            _support.LogInstantExternal(nameof(Npc_SetStateTime), npc, seconds);
        }

        public void Npc_SetTalentSkill(NpcInstance npc, int talent, int level)
        {
            _npcService.ExtNpcSetTalentSkill(npc, (VmGothicEnums.Talent)talent, level);
        }

        public string Npc_GetNearestWP(NpcInstance npc)
        {
            var wp = _npcService.ExtGetNearestWayPoint(npc);
            return _support.LogInstantExternal(nameof(Npc_GetNearestWP), wp, npc);
        }

        public int Npc_IsOnFP(NpcInstance npc, string vobNamePart)
        {
            var res = _npcHelperService.ExtIsNpcOnFp(npc, vobNamePart);
            return _support.LogInstantExternal(nameof(Npc_IsOnFP), Convert.ToInt32(res), npc, vobNamePart);
        }

        public int Npc_WasInState(NpcInstance npc, int action)
        {
            var result = _npcAiService.ExtNpcWasInState(npc, (uint)action);
            return _support.LogInstantExternal(nameof(Npc_WasInState), Convert.ToInt32(result), npc, action);
        }

        /// <summary>
        /// The scripts pass items too: G1's B_FetchWeapon checks Npc_GetDistToNpc(self, item) after Wld_DetectItem (the
        /// engine measures between any two VOBs). Taken as NpcInstance, the item failed the cast and broke the VM stack
        /// until it crashed natively (Mroczne Tajemnice's giant skeleton fetching a weapon by Fellan's armor).
        /// </summary>
        private int Npc_GetDistToNpcOrItem(DaedalusInstance first, DaedalusInstance second)
        {
            if (_support.AsNpc(first, nameof(Npc_GetDistToNpc)) is not { } npc)
                return int.MaxValue;
            if (second is ItemInstance item)
                return Npc_GetDistToItem(npc, item);
            return _support.AsNpc(second, nameof(Npc_GetDistToNpc)) is { } other
                ? Npc_GetDistToNpc(npc, other) : int.MaxValue;
        }

        public int Npc_GetDistToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            var dist = _npcAiService.ExtNpcGetDistToNpc(npc1, npc2);
            return _support.LogInstantExternal(nameof(Npc_GetDistToNpc), dist, npc1, npc2);
        }

        public int Npc_GetHeightToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            var height = _npcAiService.ExtNpcGetHeightToNpc(npc1, npc2);
            return _support.LogInstantExternal(nameof(Npc_GetHeightToNpc), height, npc1, npc2);
        }

        public int Npc_HasSpell(NpcInstance npc, int spellId)
        {
            try
            {
                var runeItems = _npcInventoryService.GetInventoryItems(npc, VmGothicEnums.InvCats.InvRune);
                foreach (var contentItem in runeItems)
                {
                    var sym = _gameStateService.GothicVm.GetSymbolByName(contentItem.Name);
                    if (sym == null) continue;
                    var itemData = _vmCacheService.TryGetItemData(sym.Index);
                    if (itemData != null && itemData.Spell == spellId)
                        return _support.LogInstantExternal(nameof(Npc_HasSpell), 1, npc, spellId);
                }
            }
            catch { }
            return _support.LogInstantExternal(nameof(Npc_HasSpell), 0, npc, spellId);
        }

        public int Npc_GetDistToWP(NpcInstance npc, string waypoint)
        {
            var dist = _npcHelperService.ExtNpcGetDistToWp(npc, waypoint);
            return _support.LogInstantExternal(nameof(Npc_GetDistToWP), dist, npc, waypoint);
        }

        public void Npc_PercDisable(NpcInstance npc, int perception)
        {
            _npcAiService.ExtNpcPerceptionDisable(npc, (VmGothicEnums.PerceptionType)perception);
            _support.LogInstantExternal(nameof(Npc_PercDisable), npc, perception);
        }

        public int Npc_CanSeeItem(NpcInstance npc, ItemInstance item)
        {
            // Wld_DetectItem already confirmed the item is within range; this is a simple proximity proxy.
            var result = item != null ? 1 : 0;
            return _support.LogInstantExternal(nameof(Npc_CanSeeItem), result, npc);
        }

        public int Npc_CanSeeNpc(NpcInstance npc, NpcInstance target)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcCanSeeNpc(npc, target, false));
            return _support.LogInstantExternal(nameof(Npc_CanSeeNpc), ret, npc, target);
        }

        public int Npc_CanSeeNpcFreeLOS(NpcInstance npc, NpcInstance target)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcCanSeeNpc(npc, target, true));
            return _support.LogInstantExternal(nameof(Npc_CanSeeNpcFreeLOS), ret, npc, target);
        }

        public void Npc_ClearAiQueue(NpcInstance npc)
        {
            _npcAiService.ExtNpcClearAiQueue(npc);
            _support.LogInstantExternal(nameof(Npc_ClearAiQueue), npc);
        }

        public string Npc_GetNextWp(NpcInstance npc)
        {
            var wp = _npcService.ExtNpcGetNextWp(npc);
            return _support.LogInstantExternal(nameof(Npc_GetNextWp), wp, npc);
        }

        public int Npc_GetTalentSkill(NpcInstance npc, int skillId)
        {
            var ret = _npcHelperService.ExtNpcGetTalentSkill(npc, skillId);
            return _support.LogInstantExternal(nameof(Npc_GetTalentSkill), ret, npc, skillId);
        }

        public int Npc_GetTalentValue(NpcInstance npc, int skillId)
        {
            var ret = _npcHelperService.ExtNpcGetTalentValue(npc, skillId);
            return _support.LogInstantExternal(nameof(Npc_GetTalentValue), ret, npc, skillId);
        }

        public int Npc_KnowsInfo(NpcInstance npc, int infoInstance)
        {
            var res = _dialogService.ExtNpcKnowsInfo(npc, infoInstance);
            return _support.LogInstantExternal(nameof(Npc_KnowsInfo), Convert.ToInt32(res), npc, infoInstance);
        }

        public int Npc_CheckInfo(NpcInstance npc, int important)
        {
            var ret = Convert.ToInt32(_dialogService.ExtCheckInfo(npc, Convert.ToBoolean(important)));
            return _support.LogInstantExternal(nameof(Npc_CheckInfo), ret, npc, important);
        }

        public int Npc_IsDead(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsDead(npc));
            return _support.LogInstantExternal(nameof(Npc_IsDead), ret, npc);
        }

        public void Npc_StopAni(NpcInstance npc, string name)
        {
            _npcAiService.ExtNpcStopAni(npc, name);
        }

        public int Npc_IsInState(NpcInstance npc, int state)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsInState(npc, state));
            return _support.LogInstantExternal(nameof(Npc_IsInState), ret, npc, state);
        }

        public void Npc_SetToFistMode(NpcInstance npc)
        {
            _npcService.ExtNpcSetToFistMode(npc);
            _support.LogInstantExternal(nameof(Npc_SetToFistMode), npc);
        }

        public void Npc_SetToFightMode(NpcInstance npc, int itemIndex)
        {
            _npcService.ExtNpcSetToFightMode(npc, itemIndex);
            _support.LogInstantExternal(nameof(Npc_SetToFightMode), npc, itemIndex);
        }

        public int Npc_IsInFightMode(NpcInstance npc, int fightMode)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsInFightMode(npc, (VmGothicEnums.FightMode)fightMode));
            return _support.LogInstantExternal(nameof(Npc_IsInFightMode), ret, npc, fightMode);
        }

        public int Npc_IsPlayer(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsPlayer(npc));
            return _support.LogInstantExternal(nameof(Npc_IsPlayer), ret, npc);
        }

        public int Npc_GetActiveSpell(NpcInstance npc)
        {
            var container = npc.GetUserData();
            var ret = container?.ActiveSpell ?? 0;
            return _support.LogInstantExternal(nameof(Npc_GetActiveSpell), ret, npc);
        }

        public int Npc_GetActiveSpellCat(NpcInstance npc)
        {
            var container = npc.GetUserData();

            // -1 = no spell readied. ActiveSpell can't tell on its own: 0 is both "none" and SPL_LIGHT.
            // FightMode Mage is set by AI_ReadySpell (NPCs) and a readied rune in VR (hero).
            if (container == null ||
                (VmGothicEnums.WeaponState)container.Vob.FightMode != VmGothicEnums.WeaponState.Mage)
                return _support.LogInstantExternal(nameof(Npc_GetActiveSpellCat), -1, npc);

            // SPELL_GOOD=0 / SPELL_NEUTRAL=1 / SPELL_BAD=2 from the spell's C_Spell instance (e.g. Light is neutral).
            // Unknown spell data -> treat as offensive, as before.
            var ret = _vmCacheService.TryGetSpellData(container.ActiveSpell)?.SpellType ?? 2;
            return _support.LogInstantExternal(nameof(Npc_GetActiveSpellCat), ret, npc);
        }

        public int Npc_GetActiveSpellLevel(NpcInstance npc)
        {
            var container = npc.GetUserData();
            var ret = container?.ActiveSpellLevel ?? 1;
            return _support.LogInstantExternal(nameof(Npc_GetActiveSpellLevel), ret, npc);
        }

        public int Npc_IsDrawingSpell(NpcInstance npc)
        {
            var container = npc.GetUserData();
            // Return 1 when FightMode == Mage — draw animation has set the mode and spell is active.
            var ret = Convert.ToInt32(container != null
                && (VmGothicEnums.WeaponState)container.Vob.FightMode == VmGothicEnums.WeaponState.Mage
                && container.ActiveSpell > 0);
            return _support.LogInstantExternal(nameof(Npc_IsDrawingSpell), ret, npc);
        }

        public int Npc_GetActiveSpellIsScroll(NpcInstance npc)
        {
            // A held scroll is one - G2's Spell_Logic_* lets a scroll cast for
            // SPL_Cost_Scroll mana, a rune needs the spell's full cost; every scroll counted as a rune and a new hero's
            // fireball scroll stopped at once (SPL_SENDSTOP). Before: always 0.
            var ret = npc?.GetUserData()?.IsActiveSpellScroll == true
                ? 1
                : 0;
            return _support.LogInstantExternal(nameof(Npc_GetActiveSpellIsScroll), ret, npc);
        }

        public int Npc_OwnedByNpc(ItemInstance item, NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcOwnedByNpc(item, npc));
            return _support.LogInstantExternal(nameof(Npc_OwnedByNpc), ret, item, npc);
        }

        public int Npc_GetTarget(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtGetTarget(npc));
            return _support.LogInstantExternal(nameof(Npc_GetTarget), ret, npc);
        }

        public int Npc_GetNextTarget(NpcInstance npc)
        {
            var ret = _npcAiService.ExtGetNextTarget(npc);
            return _support.LogInstantExternal(nameof(Npc_GetNextTarget), ret, npc);
        }

        public void Npc_SetTarget(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtSetTarget(npc, target);
            _support.LogInstantExternal(nameof(Npc_SetTarget), npc, target);

            if (target == null)
                return;

            // FIXME - Also implement, that we unset our hero as target. Otherwise it will always play fight music!
            // A target that isn't spawned yet has no user data.
            if (target.GetUserData()?.PrefabProps?.IsHero() == true)
                GlobalEventDispatcher.SetHeroAsTarget.Invoke(npc.GetUserData(), target.GetUserData());
        }

        public void Npc_SendPassivePerc(NpcInstance npc, int perc, NpcInstance victim, NpcInstance other)
        {
            _npcAiService.Npc_SendPassivePerc(npc, (VmGothicEnums.PerceptionType)perc, victim, other);
            _support.LogInstantExternal(nameof(Npc_SetTarget), npc, perc, victim, other);
        }

        public int Npc_SetTrueGuild(NpcInstance npc, int guild)
        {
            _npcAiService.ExtSetTrueGuild(npc, guild);
            return _support.LogInstantExternal(nameof(Npc_SetTrueGuild), 0, npc, guild);
        }

        public int Npc_GetTrueGuild(NpcInstance npc)
        {
            var ret = _npcAiService.ExtGetTrueGuild(npc);
            return _support.LogInstantExternal(nameof(Npc_GetTrueGuild), ret, npc);
        }

        public void Npc_SetRefuseTalk(NpcInstance npc, int refuseSeconds)
        {
            _npcAiService.ExtSetRefuseTalk(npc, refuseSeconds);
        }

        public int Npc_RefuseTalk(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtRefuseTalk(npc));
            return _support.LogInstantExternal(nameof(Npc_RefuseTalk), ret, npc);
        }

        public int Npc_HasNews(NpcInstance npc, int newsId, NpcInstance offender, NpcInstance victim)
        {
            var ret = _npcAiService.ExtNpcHasNews(npc, newsId, victim);
            return _support.LogInstantExternal(nameof(Npc_HasNews), ret, npc, newsId, offender, victim);
        }

        public void TA_MIN(NpcInstance npc, int startH, int startM, int stopH, int stopM, int action,
            string waypoint)
        {
            _npcService.ExtTaMin(npc, startH, startM, stopH, stopM, action, waypoint);
        }

        public void Ta(NpcInstance npc, int startH, int stopH, int action,
            string waypoint)
        {
            _npcService.ExtTaMin(npc, startH, 0, stopH, 0, action, waypoint);
        }

        public void Npc_ExchangeRoutine(NpcInstance self, string routineName)
        {
            _npcRoutineService.ExtNpcExchangeRoutine(self, routineName);
        }

        public int Npc_IsInRoutine(NpcInstance npc, int routineFunction)
        {
            var res = _npcRoutineService.ExtNpcIsInRoutine(npc, routineFunction);
            return _support.LogInstantExternal(nameof(Npc_IsInRoutine), Convert.ToInt32(res), npc, routineFunction);
        }

        public int Npc_GetPortalGuild(NpcInstance npc)
        {
            var go = npc?.GetUserData()?.Go;
            if (go == null)
                return 0;

            // Outdoors (no room) is below GIL_NONE - G2's
            // B_AssessEnterRoom only reacts when "!(Npc_GetPortalGuild(self) < GIL_NONE) && !Npc_IsInPlayersRoom(self)"
            // is false: with 0 a guard outside counted as being in another room and never came in (only the comment on
            // leaving was said).
            var room = _roomService.GetRoomAt(go.transform.position);
            if (string.IsNullOrEmpty(room))
                return -1;
            return _roomService.GuildOfRoom(room);
        }

        public int Npc_IsInPlayersRoom(NpcInstance npc)
        {
            var go = npc?.GetUserData()?.Go;
            if (go == null)
                return 0;
            return _roomService.GetRoomAt(go.transform.position).EqualsIgnoreCase(_roomService.HeroRoom) ? 1 : 0;
        }

        /// <summary>
        /// The scheme name of the mob the NPC uses (only the hero's is known - HeroPerceptionService), e.g. "CHESTBIG".
        /// </summary>
        public string Npc_GetDetectedMob(NpcInstance npc)
        {
            var mob = _support.IsHero(npc) ? _heroPerceptionService.HeroDetectedMob : null;
            var visual = mob?.Visual?.Name;
            if (string.IsNullOrEmpty(visual))
                return string.Empty;
            // Like the engine's scheme name: "CHESTBIG_OCCHESTLARGE.MDS" -> "CHESTBIG".
            var scheme = System.IO.Path.GetFileNameWithoutExtension(visual);
            var underscore = scheme.IndexOf('_');
            return (underscore > 0 ? scheme.Substring(0, underscore) : scheme).ToUpperInvariant();
        }

        /// <summary>
        /// The mob the user uses belongs to npc (oCMob owner = the NPC's instance name).
        /// </summary>
        public int Npc_IsDetectedMobOwnedByNpc(NpcInstance user, NpcInstance npc)
        {
            if (!_support.IsHero(user) || npc == null)
                return 0;
            var owner = (_heroPerceptionService.HeroDetectedMob as ZenKit.Vobs.IMovableObject)?.Owner;
            var npcName = _gameStateService.GothicVm.GetSymbolByIndex(npc.Index)?.Name;
            return !string.IsNullOrEmpty(owner) && owner.EqualsIgnoreCase(npcName) ? 1 : 0;
        }

        /// <summary>
        /// The mob the user uses belongs to a guild (oCMob ownerGuild, e.g. "GIL_GRD").
        /// </summary>
        public int Npc_IsDetectedMobOwnedByGuild(NpcInstance user, int guild)
        {
            if (!_support.IsHero(user))
                return 0;
            var ownerGuild = (_heroPerceptionService.HeroDetectedMob as ZenKit.Vobs.IMovableObject)?.OwnerGuild;
            if (string.IsNullOrEmpty(ownerGuild))
                return 0;
            try
            {
                var symbol = _gameStateService.GothicVm.GetSymbolByName(ownerGuild);
                var ownerGuildId = symbol != null ? symbol.GetInt(0) : int.TryParse(ownerGuild, out var id) ? id : -1;
                return ownerGuildId == guild ? 1 : 0;
            }
            catch (Exception e)
            {
                // A mod's owner guild that names no int constant.
                Logger.LogWarning($"[HeroPerc] Owner guild '{ownerGuild}' isn't a guild: {e.Message}", LogCat.Ai);
                return 0;
            }
        }

        /// <summary>
        /// The last noise came from an NPC (only the hero makes noises so far) - like the engine, other = that NPC.
        /// </summary>
        public int Snd_IsSourceNpc(NpcInstance self)
        {
            var source = _heroPerceptionService.SoundSourceNpc;
            if (source?.Instance == null)
                return 0;
            _gameStateService.GothicVm.GlobalOther = source.Instance;
            return 1;
        }

        public int Npc_CanSeeSource(NpcInstance self)
        {
            var source = _heroPerceptionService.SoundSourceNpc;
            if (self == null || source?.Instance == null)
                return 0;
            return _npcHelperService.CanSeeNpc(self, source.Instance, false) ? 1 : 0;
        }

        /// <summary>
        /// Like OpenGothic: no item (e.g. a footstep's noise) - or one we can't place - is infinitely far away.
        /// </summary>
        public int Npc_GetDistToItem(NpcInstance npc, ItemInstance item)
        {
            var npcGo = npc?.GetUserData()?.Go;
            // After Wld_DetectItem 'item' is only the instance data - the detected world item gives the position.
            var itemGo = item == null ? null : _npcHelperService.GetWorldItemContainer(npc, item)?.Go;
            var dist = npcGo == null || itemGo == null
                ? int.MaxValue
                : (int)(UnityEngine.Vector3.Distance(npcGo.transform.position, itemGo.transform.position) * 100f);
            return _support.LogInstantExternal(nameof(Npc_GetDistToItem), dist, npc, item);
        }

        /// <summary>
        /// Height difference (cm) between the NPC and a world item - G2's wisp ignores items more than 5 m above/below.
        /// </summary>
        public int Npc_GetHeightToItem(NpcInstance npc, ItemInstance item)
        {
            var npcGo = npc?.GetUserData()?.Go;
            var itemGo = _npcHelperService.GetWorldItemContainer(npc, item)?.Go;
            var height = npcGo == null || itemGo == null
                ? int.MaxValue
                : (int)(UnityEngine.Mathf.Abs(npcGo.transform.position.y - itemGo.transform.position.y) * 100f);
            return _support.LogInstantExternal(nameof(Npc_GetHeightToItem), height, npc, item);
        }
    }
}
