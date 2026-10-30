using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    /// Contains basic methods only available in Gothic Daedalus module.
    /// </summary>
    public class VmExternalDomain
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private readonly HashSet<string> _externalsCalledWithoutNpc = new();

        // A missing or foreign NPC parameter is neutral, like NpcAiService.ExtGetAttitude (0 is ATT_HOSTILE).
        private const int NeutralAttitude = (int)VmGothicEnums.Attitude.Neutral;
        // Unknown externals are reported once each - scripts call some of them on every perception.
        private readonly HashSet<string> _reportedDefaultExternals = new();

        // (optional) Some messages from Daedalus are quite spammy. Ignore them.
        private static readonly string[] _spammyMessages = new[]
        {
            "-> bodystate",
            "Npc_GetBodyState(slf)"
        };

        private bool _enableZSpyLogs;
        private int _zSpyChannel;
        private bool _zSpyInstantLogging;
        private bool _ignoreSpammyMessages;

        // PrintScreen text -> Time.time of its last log line. Capped: a script can print ever-changing texts.
        private const float _printScreenLogInterval = 10f;
        private const int _maxPrintScreenLogTimes = 256;
        private readonly Dictionary<string, float> _printScreenLogTimes = new();
        private const string _printFont = "FONT_OLD_10_WHITE.TGA";

        public VmExternalDomain()
        {
            this.Inject();
        }

        public void RegisterExternals()
        {
            _enableZSpyLogs = _configService.Dev.EnableZSpyLogs;
            _zSpyInstantLogging = _enableZSpyLogs && _configService.Dev.EnableZSpyInstantLogs;
            _ignoreSpammyMessages = _configService.Dev.IgnoreSpammyZSpyLogs;

            if (!_enableZSpyLogs)
                _zSpyChannel = -1;
            else if (_configService.Dev.AllDebugChannels)
                _zSpyChannel = int.MaxValue;
            else
                // zSpyChannel are bitwise checks. Therefore, we calculate the bit operation like (1 << PD_TA_FRAME(1) - 1)==1000
                _zSpyChannel = _configService.Dev.ZSpyChannels.Value.Sum(i => 1 << ((int)i - 1));

            var vm = new VmExternalGuard(_gameStateService.GothicVm);
            vm.RegisterExternalDefault(DefaultExternal);

            // AI
            vm.RegisterExternal<NpcInstance>("Ai_Attack", Ai_Attack);
            vm.RegisterExternal<NpcInstance>("AI_StandUp", AI_StandUp);
            vm.RegisterExternal<NpcInstance>("AI_StandUpQuick", AI_StandUpQuick);
            vm.RegisterExternal<NpcInstance, int>("AI_SetWalkMode", AI_SetWalkMode);
            vm.RegisterExternal<NpcInstance>("AI_AlignToFP", AI_AlignToFP);
            vm.RegisterExternal<NpcInstance>("AI_AlignToWP", AI_AlignToWP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoFP", AI_GotoFP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoWP", AI_GotoWP);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_GotoNpc", AI_GotoNpc);
            vm.RegisterExternal<NpcInstance, string>("AI_PlayAni", AI_PlayAni);
            vm.RegisterExternal<NpcInstance, int, int, string>("AI_StartState", AI_StartState);
            vm.RegisterExternal<NpcInstance, int, int>("AI_UseItemToState", AI_UseItemToState);
            RegisterIfDeclared(vm, "AI_UseItem", () => vm.RegisterExternal<NpcInstance, int>("AI_UseItem", AI_UseItem));
            vm.RegisterExternal<NpcInstance, float>("AI_Wait", AI_Wait);
            vm.RegisterExternal<NpcInstance, int>("AI_WaitMs", AI_WaitMs);
            vm.RegisterExternal<int, NpcInstance, string, int>("AI_UseMob", AI_UseMob);
            vm.RegisterExternal<NpcInstance, string>("AI_GoToNextFP", AI_GoToNextFP);
            vm.RegisterExternal<NpcInstance>("AI_DrawWeapon", AI_DrawWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyMeleeWeapon", AI_ReadyMeleeWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyRangedWeapon", AI_ReadyRangedWeapon);
            vm.RegisterExternal<NpcInstance>("AI_RemoveWeapon", AI_RemoveWeapon);
            vm.RegisterExternal<NpcInstance>("AI_StopAim", AI_StopAim);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_Output", AI_Output);
            vm.RegisterExternal<NpcInstance>("AI_ProcessInfos", AI_ProcessInfos);
            vm.RegisterExternal<NpcInstance>("AI_StopProcessInfos", AI_StopProcessInfos);
            vm.RegisterExternal<NpcInstance, string>("AI_LookAt", AI_LookAt);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_LookAtNPC", AI_LookAtNPC);
            vm.RegisterExternal<NpcInstance>("AI_StopLookAt", AI_StopLookAt);
            vm.RegisterExternal<NpcInstance>("AI_ContinueRoutine", AI_ContinueRoutine);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_TurnToNPC", AI_TurnToNPC);

            // Engine externals some scripts (mostly mods) call - only registered where the scripts declare them
            // (G1 has no AI_StopFX).
            if (vm.GetSymbolByName("Npc_IsWayBlocked") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_IsWayBlocked", Npc_IsWayBlocked);
            if (vm.GetSymbolByName("AI_WaitTillEnd") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_WaitTillEnd", AI_WaitTillEnd);
            if (vm.GetSymbolByName("AI_AimAt") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_AimAt", AI_AimAt);
            if (vm.GetSymbolByName("AI_StopFX") != null)
                vm.RegisterExternal<NpcInstance, string>("AI_StopFX", AI_StopFX);
            if (vm.GetSymbolByName("Wld_InsertObject") != null)
                vm.RegisterExternal<string, string>("Wld_InsertObject", Wld_InsertObject);
            vm.RegisterExternal<NpcInstance, string, int>("AI_PlayAniBS", AI_PlayAniBS);
            vm.RegisterExternal<NpcInstance>("AI_UnequipArmor", AI_UnequipArmor);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_OutputSVM", AI_OutputSVM);
            RegisterSmallExternals(vm);

            // Apply Options
            // Doc

            // Helper
            vm.RegisterExternal<int, int>("Hlp_Random", Hlp_Random);
            vm.RegisterExternal<int, string, string>("Hlp_StrCmp", Hlp_StrCmp);
            vm.RegisterExternal<int, DaedalusInstance, int>("Hlp_IsItem", (i, index) => AsItem(i, "Hlp_IsItem") is { } item ? Hlp_IsItem(item, index) : 0);
            vm.RegisterExternal<int, DaedalusInstance>("Hlp_IsValidItem", i => AsItem(i, "Hlp_IsValidItem") is { } item ? Hlp_IsValidItem(item) : 0);
            vm.RegisterExternal<int, NpcInstance>("Hlp_IsValidNpc", Hlp_IsValidNpc);
            vm.RegisterExternal<NpcInstance, int>("Hlp_GetNpc", Hlp_GetNpc);
            vm.RegisterExternal<int, DaedalusInstance>("Hlp_GetInstanceID", Hlp_GetInstanceID);

            // Info
            vm.RegisterExternal<int>("InfoManager_HasFinished", InfoManager_HasFinished);
            vm.RegisterExternal<int>("Info_ClearChoices", Info_ClearChoices);
            vm.RegisterExternal<int, string, int>("Info_AddChoice", Info_AddChoice);

            // Log
            vm.RegisterExternal<string, string>("Log_AddEntry", Log_AddEntry);
            vm.RegisterExternal<string, int>("Log_CreateTopic", Log_CreateTopic);
            vm.RegisterExternal<string, int>("Log_SetTopicStatus", Log_SetTopicStatus);

            // Model
            vm.RegisterExternal<NpcInstance, string>("Mdl_SetVisual", Mdl_SetVisual);
            vm.RegisterExternal<NpcInstance, string>("Mdl_ApplyOverlayMds", Mdl_ApplyOverlayMds);
            vm.RegisterExternal<NpcInstance, string, int, int, string, int, int, int>("Mdl_SetVisualBody", Mdl_SetVisualBody);
            vm.RegisterExternal<NpcInstance, float, float, float>("Mdl_SetModelScale", Mdl_SetModelScale);
            vm.RegisterExternal<NpcInstance, float>("Mdl_SetModelFatness", Mdl_SetModelFatness);
            vm.RegisterExternal<NpcInstance, string>("Mdl_RemoveOverlayMDS", Mdl_RemoveOverlayMDS);

            // Mission

            // Mob

            // NPC
            vm.RegisterExternal<NpcInstance, int, int>("Npc_SetTalentValue", Npc_SetTalentValue);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_ChangeAttribute", Npc_ChangeAttribute);
            vm.RegisterExternal<NpcInstance, int>("CreateInvItem", CreateInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("CreateInvItems", CreateInvItems);
            vm.RegisterExternal<NpcInstance, int>("Npc_RemoveInvItem", Npc_RemoveInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_RemoveInvItems", Npc_RemoveInvItems);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_PercEnable", Npc_PercEnable);
            vm.RegisterExternal<NpcInstance, float>("Npc_SetPercTime", Npc_SetPercTime);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetPermAttitude", (a, b) => AsNpc(a, "Npc_GetPermAttitude") is { } first && AsNpc(b, "Npc_GetPermAttitude") is { } second ? Npc_GetPermAttitude(first, second) : NeutralAttitude);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetAttitude", (a, b) => AsNpc(a, "Npc_GetAttitude") is { } first && AsNpc(b, "Npc_GetAttitude") is { } second ? Npc_GetAttitude(first, second) : NeutralAttitude);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetAttitude", Npc_SetAttitude);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetTempAttitude", Npc_SetTempAttitude);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetBodyState", Npc_GetBodyState);
            vm.RegisterExternal<NpcInstance>("Npc_PerceiveAll", Npc_PerceiveAll);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_HasItems", Npc_HasItems);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetStateTime", Npc_GetStateTime);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetStateTime", Npc_SetStateTime);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedArmor", Npc_GetEquippedArmor);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_SetTalentSkill", Npc_SetTalentSkill);
            vm.RegisterExternal<string, NpcInstance>("Npc_GetNearestWP", Npc_GetNearestWP);
            vm.RegisterExternal<int, NpcInstance, string>("Npc_IsOnFP", Npc_IsOnFP);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_WasInState", Npc_WasInState);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_GetInvItem", Npc_GetInvItem);
            vm.RegisterExternal<int, NpcInstance, int, int>("Npc_GetInvItemBySlot", Npc_GetInvItemBySlot);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_RemoveInvItem", Npc_RemoveInvItem);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_RemoveInvItems", Npc_RemoveInvItems);
            vm.RegisterExternal<NpcInstance, int>("EquipItem", EquipItem);
            vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_GetDistToNpc", Npc_GetDistToNpc);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedArmor", Npc_HasEquippedArmor);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedMeleeWeapon", Npc_GetEquippedMeleeWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedMeleeWeapon", Npc_HasEquippedMeleeWeapon);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedRangedWeapon", Npc_GetEquippedRangedWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedRangedWeapon", Npc_HasEquippedRangedWeapon);
            vm.RegisterExternal<int, NpcInstance, string>("Npc_GetDistToWP", Npc_GetDistToWP);
            vm.RegisterExternal<NpcInstance, int>("Npc_PercDisable", Npc_PercDisable);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeNpc", (a, b) => AsNpc(a, "Npc_CanSeeNpc") is { } first && AsNpc(b, "Npc_CanSeeNpc") is { } second ? Npc_CanSeeNpc(first, second) : 0);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeNpcFreeLOS", (a, b) => AsNpc(a, "Npc_CanSeeNpcFreeLOS") is { } first && AsNpc(b, "Npc_CanSeeNpcFreeLOS") is { } second ? Npc_CanSeeNpcFreeLOS(first, second) : 0);
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_CanSeeItem", (n, i) => AsNpc(n, "Npc_CanSeeItem") is { } npc && AsItem(i, "Npc_CanSeeItem") is { } item ? Npc_CanSeeItem(npc, item) : 0);
            vm.RegisterExternal<NpcInstance>("Npc_ClearAiQueue", Npc_ClearAiQueue);
            // G2 only - G1's scripts don't declare it.
            if (vm.GetSymbolByName("Npc_ClearInventory") != null)
                vm.RegisterExternal<NpcInstance>("Npc_ClearInventory", Npc_ClearInventory);
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
            vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_OwnedByNpc", (i, n) => AsItem(i, "Npc_OwnedByNpc") is { } item && AsNpc(n, "Npc_OwnedByNpc") is { } npc ? Npc_OwnedByNpc(item, npc) : 0);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTarget", Npc_GetTarget);
            vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_SetTarget", Npc_SetTarget);
            vm.RegisterExternal<NpcInstance, int, NpcInstance, NpcInstance>("Npc_SendPassivePerc", Npc_SendPassivePerc);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_SetTrueGuild", Npc_SetTrueGuild);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTrueGuild", Npc_GetTrueGuild);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetRefuseTalk", Npc_SetRefuseTalk);
            vm.RegisterExternal<int, NpcInstance>("Npc_RefuseTalk", Npc_RefuseTalk);
            vm.RegisterExternal<int, NpcInstance, int, NpcInstance, NpcInstance>("Npc_HasNews", Npc_HasNews);

            // G2 externals only.
            if (_contextGameVersionService.IsGothic2())
            {
                vm.RegisterExternal<int, DaedalusInstance, DaedalusInstance>("Npc_GetHeightToNpc", (a, b) => AsNpc(a, "Npc_GetHeightToNpc") is { } first && AsNpc(b, "Npc_GetHeightToNpc") is { } second ? Npc_GetHeightToNpc(first, second) : 0);
            }

            RegisterIfDeclared(vm, "Wld_RemoveNpc", () => vm.RegisterExternal<int>("Wld_RemoveNpc", Wld_RemoveNpc));


            // Print
            vm.RegisterExternal<string>("PrintDebug", PrintDebug);
            vm.RegisterExternal<int, string>("PrintDebugCh", PrintDebugCh);
            vm.RegisterExternal<string>("PrintDebugInst", PrintDebugInst);
            vm.RegisterExternal<int, string>("PrintDebugInstCh", PrintDebugInstCh);
            RegisterPrintScreen(vm, "PrintScreen");
            RegisterPrintScreen(vm, "AI_PrintScreen");
            vm.RegisterExternal<string>("Print", Print);

            // Sound
            vm.RegisterExternal<string>("Snd_Play", Snd_Play);

            // Video
            RegisterPlayVideo(vm);


            RegisterIfDeclared(vm, "Npc_GetLookAtTarget",
                () => vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_GetLookAtTarget", Npc_GetLookAtTarget));

            // Day Routine
            vm.RegisterExternal<NpcInstance, int, int, int, int, int, string>("TA_MIN", TA_MIN);
            vm.RegisterExternal<NpcInstance, int, int, int, string>("TA", Ta);
            vm.RegisterExternal<NpcInstance, string>("Npc_ExchangeRoutine", Npc_ExchangeRoutine);

            // World
            vm.RegisterExternal<int, string>("Wld_InsertNpc", Wld_InsertNpc);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsFPAvailable", Wld_IsFPAvailable);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsMobAvailable", Wld_IsMobAvailable);
            vm.RegisterExternal<int, NpcInstance, int, int, int>("Wld_DetectNpc", Wld_DetectNpc);
            vm.RegisterExternal<int, NpcInstance, int, int, int, int>("Wld_DetectNpcEx", Wld_DetectNpcEx);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_IsNextFPAvailable", Wld_IsNextFPAvailable);
            vm.RegisterExternal<int, int>("Wld_SetTime", Wld_SetTime);
            vm.RegisterExternal("Wld_GetDay", Wld_GetDay);
            vm.RegisterExternal<int, int, int, int, int>("Wld_IsTime", Wld_IsTime);
            vm.RegisterExternal<int, NpcInstance, string>("Wld_GetMobState", Wld_GetMobState);
            vm.RegisterExternal<int, string>("Wld_InsertItem", Wld_InsertItem);
            vm.RegisterExternal<string>("Wld_ExchangeGuildAttitudes", Wld_ExchangeGuildAttitudes);
            vm.RegisterExternal<int, int, int>("Wld_SetGuildAttitude", Wld_SetGuildAttitude);
            vm.RegisterExternal<int, int, string, int>("Wld_SetObjectRoutine", Wld_SetObjectRoutine);
            vm.RegisterExternal<int, int, string, int>("Wld_SetMobRoutine", Wld_SetMobRoutine);
            vm.RegisterExternal<string, int>("Wld_AssignRoomToGuild", Wld_AssignRoomToGuild);
            vm.RegisterExternal<int, int, int>("Wld_GetGuildAttitude", Wld_GetGuildAttitude);
            vm.RegisterExternal<string>("Wld_SendTrigger", Wld_SendTrigger);
            vm.RegisterExternal<string>("Wld_SendUntrigger", Wld_SendUntrigger);

            // Misc
            vm.RegisterExternal<int, int>("Perc_SetRange", Perc_SetRange);
            vm.RegisterExternal<string, string, string>("ConcatStrings", ConcatStrings);
            vm.RegisterExternal<string, int>("IntToString", IntToString);
            vm.RegisterExternal<string, float>("FloatToString", FloatToString);
            vm.RegisterExternal<int, float>("FloatToInt", FloatToInt);
            vm.RegisterExternal<float, int>("IntToFloat", IntToFloat);
            vm.RegisterExternal<string, string, string, string, int>("IntroduceChapter", IntroduceChapter);
        }


        public void DefaultExternal(DaedalusVm vm, DaedalusSymbol sym)
        {
            // FIXME: Once Gothic is fully released, we can safely throw an exception as it tells us: The game will not work until you implement this missing function.
            //throw new NotImplementedException("External >" + value + "< not registered but required by DaedalusVM.");
            // ZenKit already took the arguments and pushed the default result - here we only report it, once.
            if (!_reportedDefaultExternals.Add(sym.Name))
                return;

            try
            {
                if (_gameStateService.GothicVm.GlobalSelf == null)
                {
                    Logger.LogWarningEditor($"Method >{sym.Name}< not yet implemented in DaedalusVM.", LogCat.ZenKit);
                }
                else
                {
                    // Add additional log information if existing.
                    var selfUserData = _gameStateService.GothicVm.GlobalSelf.UserData as NpcContainer;
                    var npcName = _multiTypeCacheService.NpcCache.FirstOrDefault(x => x.Instance == selfUserData.Instance)?.Go
                        ?.transform.parent?.name;
                    Logger.LogWarningEditor($"Method >{sym.Name}< not yet implemented in DaedalusVM (called on >{npcName}<).", LogCat.ZenKit);
                }
            }
            catch (Exception e)
            {
                Logger.LogErrorEditor($"Bug in getting Npc. Fix or delete: {e}", LogCat.ZenKit);
            }
        }

        /// <summary>
        /// Logs external method calls with input parameters and output values.
        /// </summary>
        private TResult LogInstantExternal<TResult>(string methodName, TResult result, params object[] parameters)
        {
            if (!_zSpyInstantLogging)
                return result;
                
            Logger.Log($"External {methodName}({LogInstantParameter(parameters)}) => {result?.ToString() ?? "void"}", LogCat.ZenKit);
            
            return result;
        }

        /// <summary>
        /// Logs external method calls with no return value.
        /// </summary>
        private void LogInstantExternal(string methodName, params object[] parameters)
        {
            if (!_zSpyInstantLogging)
                return;

            Logger.Log($"[External] {methodName}({LogInstantParameter(parameters)}) => void", LogCat.ZenKit);
        }

        private string LogInstantParameter(params object[] parameters)
        {
            var paramStr = string.Empty;
            foreach (var param in parameters)
            {
                switch (param)
                {
                    case NpcInstance npcInstance:
                        paramStr += npcInstance.GetName(NpcNameSlot.Slot0);
                        break;
                    case ItemInstance itemInstance:
                        paramStr += itemInstance.Name;
                        break;
                    default:
                        paramStr += param?.ToString() ?? "null";
                        break;
                }

                paramStr += ", ";
            }

            if (parameters.Length != 0)
                paramStr = paramStr.RemoveEnd(", ");

            return paramStr;
        }

        #region AI

        public void Ai_Attack(NpcInstance npc)
        {
            _npcAiService.ExtAttack(npc);
        }

        public void AI_StandUp(NpcInstance npc)
        {
            _npcAiService.ExtAiStandUp(npc);
        }

        public void AI_StandUpQuick(NpcInstance npc)
        {
            _npcAiService.ExtAiStandUpQuick(npc);
        }

        public void AI_SetWalkMode(NpcInstance npc, int walkMode)
        {
            _npcAiService.ExtAiSetWalkMode(npc, (VmGothicEnums.WalkMode)walkMode);
        }

        public void AI_AlignToFP(NpcInstance npc)
        {
            _npcAiService.ExtAiAlignToFp(npc);
        }

        public void AI_AlignToWP(NpcInstance npc)
        {
            _npcAiService.ExtAiAlignToWp(npc);
        }

        public void AI_GotoFP(NpcInstance npc, string freePointName)
        {
            _npcAiService.ExtAiGoToFp(npc, freePointName);
        }

        public void AI_GotoWP(NpcInstance npc, string wayPointName)
        {
            _npcAiService.ExtAiGoToWp(npc, wayPointName);
        }

        public void AI_GotoNpc(NpcInstance self, NpcInstance other)
        {
            _npcAiService.ExtAiGoToNpc(self, other);
        }

        public void AI_PlayAni(NpcInstance npc, string name)
        {
            _npcAiService.ExtAiPlayAni(npc, name);
        }

        public void AI_StartState(NpcInstance npc, int function, int stateBehaviour, string wayPointName)
        {
            _npcAiService.ExtAiStartState(npc, function, Convert.ToBoolean(stateBehaviour), wayPointName);
        }

        /// <summary>
        /// The NPC takes the item out, uses it up (on_state[0]: potions heal) and puts the hand down - like the engine.
        /// NPCs drink a potion after a fight (ZS_HealSelf); unhandled, the queue got stuck there.
        /// </summary>
        public void AI_UseItem(NpcInstance npc, int itemId)
        {
            var resolved = ResolveItemId(itemId);
            if (resolved < 0)
            {
                Logger.LogWarning($"AI_UseItem: the global item of {npc?.GetName(NpcNameSlot.Slot0)} is empty - skipped",
                    LogCat.Ai);
                return;
            }
            _npcAiService.ExtAiUseItem(npc, resolved);
        }

        public void AI_UseItemToState(NpcInstance npc, int itemId, int expectedInventoryCount)
        {
            var resolved = ResolveItemId(itemId);
            if (resolved < 0 || npc?.GetUserData()?.Props == null)
            {
                Logger.LogWarning($"AI_UseItemToState: no NPC or the global item of {npc?.GetName(NpcNameSlot.Slot0)} " +
                                  "is empty - skipped", LogCat.Ai);
                return;
            }

            _npcAiService.ExtAiUseItemToState(npc, resolved, expectedInventoryCount);
        }

        /// <summary>
        /// `AI_UseItem(self, item)` hands over the global `item` variable
        /// (G1 ZS_HealSelf: food found by Npc_GetInvItemBySlot) - its symbol, not an item instance. The engine uses the
        /// item in it. The unknown "item" left Lukis frozen after a fight (the use never finished).
        /// </summary>
        private int ResolveItemId(int itemId)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByIndex(itemId);
            if (symbol == null || !string.Equals(symbol.Name, "ITEM", StringComparison.OrdinalIgnoreCase))
                return itemId;

            // -1: the global item is empty - nothing to use.
            return vm.GlobalItem is ItemInstance item ? item.Index : -1;
        }

        public void AI_Wait(NpcInstance npc, float seconds)
        {
            _npcAiService.ExtAiWait(npc, seconds);
        }

        public void AI_WaitMs(NpcInstance npc, int miliseconds)
        {
            _npcAiService.ExtAiWait(npc, miliseconds / 1000f);
        }

        public int AI_UseMob(NpcInstance npc, string target, int state)
        {
            _npcAiService.ExtAiUseMob(npc, target, state);

            // Hint: It seems the int value is a bug as no G1 Daedalus usage needs it.
            var ret = 0;
            return LogInstantExternal(nameof(AI_UseMob), ret, npc, target, state);
        }

        public void AI_GoToNextFP(NpcInstance npc, string fpNamePart)
        {
            _npcAiService.ExtAiGoToNextFp(npc, fpNamePart);
        }

        public void AI_DrawWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiDrawWeapon(npc);
        }

        public void AI_ReadyMeleeWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiDrawWeapon(npc);
        }

        public void AI_ReadyRangedWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiReadyRangedWeapon(npc);
        }

        public void AI_RemoveWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiUndrawWeapon(npc);
        }

        public void AI_StopAim(NpcInstance npc)
        {
            // Stub — NPC ranged aiming not yet implemented; NPC will sheath via AI_RemoveWeapon.
        }

        public void AI_Output(NpcInstance self, NpcInstance target, string outputName)
        {
            _dialogService.ExtAiOutput(self, target, outputName);
        }

        public void AI_ProcessInfos(NpcInstance npc)
        {
            _dialogService.ExtAiProcessInfos(npc ?? GetHeroForMissingNpc(nameof(AI_ProcessInfos)));
        }

        public void AI_StopProcessInfos(NpcInstance npc)
        {
            _dialogService.ExtAiStopProcessInfos(npc ?? GetHeroForMissingNpc(nameof(AI_StopProcessInfos)));
        }

        /// <summary>
        /// G1's SLEEPABIT_S1 calls AI_ProcessInfos(Hlp_GetNpc(PC_Hero)) - empty when the mod's hero is another instance
        /// (Mroczne Tajemnice: PC_Rockefeller), and the bed's sleep menu threw. The scripts mean the hero there.
        /// </summary>
        private NpcInstance GetHeroForMissingNpc(string externalName)
        {
            if (_externalsCalledWithoutNpc.Add(externalName))
                Logger.LogWarning($"[{externalName}] called with no NPC - using the hero.", LogCat.Dialog);
            return (NpcInstance)_gameStateService.GothicVm.GlobalHero;
        }

        public void AI_LookAt(NpcInstance npc, string waypoint)
        {
            _npcAiService.ExtAiLookAt(npc, waypoint);
        }

        public void AI_LookAtNPC(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiLookAtNpc(npc, target);
        }

        public void AI_StopLookAt(NpcInstance npc)
        {
            _npcAiService.ExtAiStopLookAt(npc);
        }

        public void AI_ContinueRoutine(NpcInstance npc)
        {
            _npcAiService.ExtAiContinueRoutine(npc);
        }

        public void AI_TurnToNPC(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiTurnToNpc(npc, target);
        }

        /// <summary>
        /// DoM asks it on every perception (B_AssessPlayer) to let NPCs step aside. Our walking has no "blocked"
        /// detection yet - the way is free, as before, just without a warning per call.
        /// </summary>
        public int Npc_IsWayBlocked(NpcInstance npc)
        {
            return LogInstantExternal(nameof(Npc_IsWayBlocked), 0, npc);
        }

        public void AI_WaitTillEnd(NpcInstance npc, NpcInstance other)
        {
            _npcAiService.ExtAiWaitTillEnd(npc, other);
        }

        public void AI_AimAt(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiAimAt(npc, target);
        }

        /// <summary>
        /// There are no effects on NPCs to stop yet (AI_PlayFX isn't implemented either).
        /// </summary>
        public void AI_StopFX(NpcInstance npc, string effect)
        {
            LogInstantExternal(nameof(AI_StopFX), new object[] { npc, effect });
        }

        public void AI_PlayAniBS(NpcInstance npc, string name, int bodyState)
        {
            _npcAiService.ExtAiPlayAniBs(npc, name, bodyState);
        }

        public void AI_UnequipArmor(NpcInstance npc)
        {
            _npcAiService.ExtAiUnequipArmor(npc);
        }

        public void AI_OutputSVM(NpcInstance npc, NpcInstance target, string svmname)
        {
            _dialogService.ExtAiOutputSvm(npc, target, svmname);
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

        #endregion

        #region Apply Options

        //

        #endregion

        #region Doc

        //

        #endregion

        #region Helper

        /// <summary>
        /// Item parameters of externals arrive as whatever instance the VM holds: an item of an old VM or another class
        /// failed the cast in ZenKit's Pop and the external left the VM stack broken until it crashed natively
        /// (Mroczne Tajemnice's giant skeleton by Fellan's armor). Taken as DaedalusInstance and converted here.
        /// </summary>
        private ItemInstance AsItem(DaedalusInstance instance, string external)
        {
            if (instance == null)
                return null;
            if (instance is ItemInstance item)
                return item;
            var converted = _vmCacheService.TryGetItemData(instance.Index);
            LogInstanceMismatch(external, instance, "C_ITEM", converted != null);
            return converted;
        }

        private NpcInstance AsNpc(DaedalusInstance instance, string external)
        {
            if (instance == null)
                return null;
            if (instance is NpcInstance npc)
                return npc;
            LogInstanceMismatch(external, instance, "C_NPC", false);
            return null;
        }

        private readonly HashSet<string> _loggedInstanceMismatches = new();

        private void LogInstanceMismatch(string external, DaedalusInstance instance, string expected, bool isConverted)
        {
            if (!_loggedInstanceMismatches.Add($"{external}:{instance.GetType().Name}"))
                return;
            Logger.LogWarning($"[VM] {external}: expected {expected}, got {instance.GetType().Name} (symbol " +
                              $"{instance.Index}) - {(isConverted ? "converted by its symbol" : "skipped")}", LogCat.ZenKit);
        }

        public int Hlp_Random(int n0)
        {
            var rand = Random.Range(0, n0 - 1);
            return LogInstantExternal(nameof(Hlp_Random), rand, n0);
        }


        public int Hlp_StrCmp(string s1, string s2)
        {
            var cmpValue = s1 == s2 ? 1 : 0;
            return LogInstantExternal(nameof(Hlp_StrCmp), cmpValue, s1, s2);
        }

        public int Hlp_IsItem(ItemInstance item, int itemIndexToCheck)
        {
            if (item == null)
                return 0;
            
            var ret = Convert.ToInt32(item.Index == itemIndexToCheck);
            return LogInstantExternal(nameof(Hlp_IsItem), ret);
        }

        public int Hlp_IsValidItem(ItemInstance item)
        {
            var ret = Convert.ToInt32(item != null);
            return LogInstantExternal(nameof(Hlp_IsValidItem), ret, item);
        }

        public int Hlp_IsValidNpc(NpcInstance npc)
        {
            var ret = Convert.ToInt32(npc != null);
            return LogInstantExternal(nameof(Hlp_IsValidNpc), ret, npc);
        }

        public NpcInstance Hlp_GetNpc(int instanceId)
        {
            var npc = _npcService.ExtHlpGetNpc(instanceId);
            return LogInstantExternal(nameof(Hlp_GetNpc), npc, instanceId);
        }

        public int Hlp_GetInstanceID(DaedalusInstance instance)
        {
            if (instance == null)
            {
                return LogInstantExternal(nameof(Hlp_GetInstanceID), -1, instance);
            }

            var ret = instance.Index;
            return LogInstantExternal(nameof(Hlp_GetInstanceID), ret, instance);
        }

        #endregion

        #region Info

        public int InfoManager_HasFinished()
        {
            var ret = Convert.ToInt32(_dialogService.ExtInfoManagerHasFinished());
            return LogInstantExternal(nameof(InfoManager_HasFinished), ret);
        }

        public void Info_ClearChoices(int info)
        {
            _dialogService.ExtInfoClearChoices(info);
        }

        public void Info_AddChoice(int info, string text, int function)
        {
            _dialogService.ExtInfoAddChoice(info, text, function);
        }

        #endregion

        #region Log

        public void Log_AddEntry(string topic, string entry)
        {
            _storyService.ExtLogAddEntry(topic, entry);
        }

        public void Log_CreateTopic(string name, int section)
        {
            _storyService.ExtLogCreateTopic(name, (SaveTopicSection)section);
        }

        public void Log_SetTopicStatus(string name, int status)
        {
            _storyService.ExtLogSetTopicStatus(name, (SaveTopicStatus)status);
        }

        #endregion

        #region Model

        public void Mdl_SetVisual(NpcInstance npc, string visual)
        {
            _npcService.ExtMdlSetVisual(npc, visual);
        }


        public void Mdl_ApplyOverlayMds(NpcInstance npc, string overlayName)
        {
            _npcService.ExtApplyOverlayMds(npc, overlayName);
        }

        public void Mdl_SetVisualBody(NpcInstance npc, string body, int bodyTexNr, int bodyTexColor, string head,
            int headTexNr, int teethTexNr, int armor)
        {
            _npcService.ExtSetVisualBody(new ExtSetVisualBodyData
                {
                    Npc = npc,
                    Body = body,
                    BodyTexNr = bodyTexNr,
                    BodyTexColor = bodyTexColor,
                    Head = head,
                    HeadTexNr = headTexNr,
                    TeethTexNr = teethTexNr,
                    Armor = armor
                }
            );
        }


        public void Mdl_SetModelScale(NpcInstance npc, float x, float y, float z)
        {
            _npcService.ExtMdlSetModelScale(npc, new Vector3(x, y, z));
        }


        public void Mdl_SetModelFatness(NpcInstance npc, float fatness)
        {
            _npcService.ExtSetModelFatness(npc, fatness);
        }

        private bool _isLoggedOnceRemoveOverlayMds;
        public void Mdl_RemoveOverlayMDS(NpcInstance npc, string overlayName)
        {
            if (!_isLoggedOnceRemoveOverlayMds)
                Logger.LogWarning("Method >Mdl_RemoveOverlayMDS< not yet implemented in DaedalusVM. Logging once only!", LogCat.ZenKit);
            _isLoggedOnceRemoveOverlayMds = true;
        }

        #endregion

        #region Print

        /// <summary>
        /// G1/G2: int PlayVideo(string), G2: int PlayVideoEx(string, int screenBlend, int exitSession).
        /// VR plays it in a "cinema" (VRCinema) and pauses the world meanwhile, like the engine blocks the game.
        /// </summary>
        private void RegisterPlayVideo(VmExternalGuard vm)
        {
            try
            {
                var playVideo = vm.GetSymbolByName("PlayVideo");
                if (playVideo != null)
                {
                    // Some script versions declare a return value, some don't. It has to match the DAT symbol.
                    if (playVideo.HasReturn)
                        vm.RegisterExternal<int, string>("PlayVideo", fileName => PlayVideo(fileName));
                    else
                        vm.RegisterExternal<string>("PlayVideo", fileName => PlayVideo(fileName));
                }

                if (vm.GetSymbolByName("PlayVideoEx") != null)
                    vm.RegisterExternal<int, string, int, int>("PlayVideoEx",
                        (fileName, screenBlend, exitSession) => PlayVideo(fileName));
            }
            catch (Exception e)
            {
                Logger.LogWarning($"PlayVideo has an unexpected signature in this game/mod - not registered. " +
                                  $"{e.Message}", LogCat.ZenKit);
            }
        }

        private int PlayVideo(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return 0;

            if (_configService.Dev.SkipVideos)
            {
                Logger.Log($"[PlayVideo] {fileName} skipped (DeveloperConfig.SkipVideos).", LogCat.Misc);
                return 1;
            }

            Logger.Log($"[PlayVideo] {fileName}", LogCat.Misc);
            GlobalEventDispatcher.PlayVideo.Invoke(fileName);
            return 1;
        }

        public NpcInstance Npc_GetLookAtTarget(NpcInstance npc)
        {
            var target = npc?.GetUserData()?.Props.LookAtTarget;
            return LogInstantExternal(nameof(Npc_GetLookAtTarget), target, npc);
        }

        /// <summary>
        /// Real signature in G1 + G2 is (msg, posX, posY, font, seconds) - the externals.d of the MDKs is outdated.
        /// Some script versions declare a return value, some don't. It has to match the DAT symbol, otherwise the
        /// VM stack breaks - so we register based on the symbol.
        /// AI_PrintScreen is shown immediately instead of being queued in the NPC's AI.
        /// </summary>
        private void RegisterPrintScreen(VmExternalGuard vm, string name)
        {
            var symbol = vm.GetSymbolByName(name);
            if (symbol == null)
                return;

            try
            {
                if (symbol.HasReturn)
                {
                    vm.RegisterExternal<int, string, int, int, string, int>(name, (msg, posX, posY, font, seconds) =>
                    {
                        PrintScreen(msg, posX, posY, font, seconds);
                        return 1;
                    });
                }
                else
                {
                    vm.RegisterExternal<string, int, int, string, int>(name,
                        (msg, posX, posY, font, seconds) => PrintScreen(msg, posX, posY, font, seconds));
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"{name} has an unexpected signature in this game/mod - not registered. {e.Message}",
                    LogCat.ZenKit);
            }
        }

        /// <summary>
        /// posX/posY are percent of the screen, -1 centers the text.
        /// </summary>
        private void PrintScreen(string message, int posX, int posY, string font, int seconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            // Scripts may print every AI tick - log a text once per interval.
            if (!_printScreenLogTimes.TryGetValue(message, out var lastLog) ||
                Time.time - lastLog > _printScreenLogInterval)
            {
                if (_printScreenLogTimes.Count >= _maxPrintScreenLogTimes)
                    _printScreenLogTimes.Clear();
                _printScreenLogTimes[message] = Time.time;
                Logger.Log($"[PrintScreen] {message}", LogCat.Dialog);
            }

            GlobalEventDispatcher.PrintScreen.Invoke(new PrintScreenMessage(message, posX, posY, font, seconds));
        }

        /// <summary>
        /// Engine messages like PLAYER_MOB_MISSING_ITEM's "missing item" text. Centered, 3 s.
        /// </summary>
        public void Print(string message)
        {
            PrintScreen(message, PrintScreenMessage.Centered, PrintScreenMessage.Centered, _printFont, 3);
        }

        public void Snd_Play(string soundName)
        {
            _audioService.PlaySound2D(soundName);
        }

        public void PrintDebug(string message)
        {
            if (!_enableZSpyLogs)
                return;

            Logger.Log($"[zspy]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugCh(int channel, string message)
        {
            if (!_enableZSpyLogs || (_zSpyChannel & (1 << (channel - 1))) == 0)
                return;

            Logger.Log($"[zspy,{channel}]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugInst(string message)
        {
            if (!_enableZSpyLogs)
                return;

            Logger.Log($"[zspy]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugInstCh(int channel, string message)
        {
            if (!_enableZSpyLogs || (_zSpyChannel & (1 << (channel - 1))) == 0)
                return;
            
            if (_ignoreSpammyMessages && _spammyMessages.Any(spammy => message.Contains(spammy)))
                return;

            Logger.Log($"[zspy,{channel}]: {message}", LogCat.ZSpy);
        }

        #endregion

        #region Sound

        //

        #endregion

        #region Daily Routine

        //

        #endregion

        #region Mission

        //

        #endregion

        #region Mob

        //

        #endregion

        #region NPC

        public void Npc_SetTalentValue(NpcInstance npc, int talent, int level)
        {
            _npcService.ExtNpcSetTalentValue(npc, (VmGothicEnums.Talent)talent, level);
            LogInstantExternal(nameof(Npc_ChangeAttribute), npc, talent, level);
        }

        public void Npc_ChangeAttribute(NpcInstance npc, int attributeId, int value)
        {
            _npcService.ExtNpcChangeAttribute(npc, attributeId, value);
            LogInstantExternal(nameof(Npc_ChangeAttribute), npc, attributeId, value);
        }

        public void CreateInvItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtCreateInvItems(npc, itemId, 1);
            LogInstantExternal(nameof(CreateInvItem), npc, itemId);
        }

        public void CreateInvItems(NpcInstance npc, int itemId, int amount)
        {
            _npcInventoryService.ExtCreateInvItems(npc, itemId, amount);
            LogInstantExternal(nameof(CreateInvItem), npc, itemId, amount);
        }
        
        public void Npc_RemoveInvItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtRemoveInvItems(npc, itemId, 1);
            LogInstantExternal(nameof(Npc_RemoveInvItem), npc, itemId);
        }
        
        public void Npc_RemoveInvItems(NpcInstance npc, int itemId, int amount)
        {
            _npcInventoryService.ExtRemoveInvItems(npc, itemId, amount);
            LogInstantExternal(nameof(Npc_RemoveInvItems), npc, itemId, amount);
        }


        public void Npc_PercEnable(NpcInstance npc, int perception, int function)
        {
            _npcAiService.ExtNpcPerceptionEnable(npc, (VmGothicEnums.PerceptionType)perception, function);
            LogInstantExternal(nameof(Npc_PercEnable), perception, function);
        }


        public void Npc_SetPercTime(NpcInstance npc, float time)
        {
            _npcAiService.ExtNpcSetPerceptionTime(npc, time);
            LogInstantExternal(nameof(Npc_SetPercTime), npc, time);
        }

        public int Npc_GetPermAttitude(NpcInstance self, NpcInstance other)
        {
            var ret = (int)_npcAiService.ExtGetAttitude(self, other);
            return LogInstantExternal(nameof(Npc_GetPermAttitude), ret, self, other);
        }

        public int Npc_GetAttitude(NpcInstance self, NpcInstance other)
        {
            var ret = (int)_npcAiService.ExtGetAttitude(self, other);
            return LogInstantExternal(nameof(Npc_GetAttitude), ret, self, other);
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

            // Gothic loads all the necessary items into memory to reference them later via Wld_DetectNpc() and Wld_DetectItem().
            // But we don't need to pre-load them and can just load the necessary elements when really needed.
        }


        public int Npc_HasItems(NpcInstance npc, int itemId)
        {
            var count = _npcInventoryService.ExtNpcHasItems(npc, itemId);
            return LogInstantExternal(nameof(Npc_HasItems), count, npc, itemId);
        }


        public int Npc_GetStateTime(NpcInstance npc)
        {
            var stateTime = _npcAiService.ExtNpcGetStateTime(npc);
            return LogInstantExternal(nameof(Npc_GetStateTime), stateTime, npc);
        }
        
        public void Npc_SetStateTime(NpcInstance npc, int seconds)
        {
            _npcAiService.ExtNpcSetStateTime(npc, seconds);
            LogInstantExternal(nameof(Npc_SetStateTime), npc, seconds);
        }
        
        public ItemInstance Npc_GetEquippedArmor(NpcInstance npc)
        {
            var item = _npcAiService.ExtGetEquippedArmor(npc);
            return LogInstantExternal(nameof(Npc_GetEquippedArmor), item, npc);
        }
        
        public void Npc_SetTalentSkill(NpcInstance npc, int talent, int level)
        {
            _npcService.ExtNpcSetTalentSkill(npc, (VmGothicEnums.Talent)talent, level);
        }

        public string Npc_GetNearestWP(NpcInstance npc)
        {
            var wp = _npcService.ExtGetNearestWayPoint(npc);
            return LogInstantExternal(nameof(Npc_GetNearestWP), wp, npc);
        }
        
        public int Npc_IsOnFP(NpcInstance npc, string vobNamePart)
        {
            var res = _npcHelperService.ExtIsNpcOnFp(npc, vobNamePart);
            return LogInstantExternal(nameof(Npc_IsOnFP), Convert.ToInt32(res), npc, vobNamePart);
        }
        
        public int Npc_WasInState(NpcInstance npc, int action)
        {
            var result = _npcAiService.ExtNpcWasInState(npc, (uint)action);
            return LogInstantExternal(nameof(Npc_WasInState), Convert.ToInt32(result), npc, action);
        }

        public void Npc_GetInvItem(IntPtr vmPtr)
        {
            // NpcCreator.ExtGetInvItem();
        }
        
        public int Npc_GetInvItemBySlot(NpcInstance npc, int invType, int slot)
        {
            const int invWeapon = 1;
            const int invArmor = 2;

            if (TryGetInvItemBySlot(npc, invType, slot, out var item, out var amount))
            {
                _gameStateService.GothicVm.GlobalItem = item;
                return LogInstantExternal(nameof(Npc_GetInvItemBySlot), amount, npc, invType, slot);
            }

            var props = npc?.GetUserData()?.Props;
            if (props == null)
            {
                _gameStateService.GothicVm.GlobalItem = null;
                return LogInstantExternal(nameof(Npc_GetInvItemBySlot), 0, npc, invType, slot);
            }
            ItemInstance found = null;

            if (invType == invWeapon)
            {
                found = slot switch
                {
                    1 => props.EquippedItems.FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatNf),
                    2 => props.EquippedItems.FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf),
                    _ => null
                };
            }
            else if (invType == invArmor)
            {
                if (slot == 1)
                    found = props.EquippedItems.FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatArmor);
            }

            _gameStateService.GothicVm.GlobalItem = found;
            var count = found != null ? 1 : 0;
            return LogInstantExternal(nameof(Npc_GetInvItemBySlot), count, npc, invType, slot);
        }

        /// <summary>
        /// Like the engine (OpenGothic Inventory::findByFlags), the slot-th item
        /// (1 based) of the inventory category - our packed inventory is stored per the same INV_* category. Before,
        /// only the equipped weapon/armor was found: B_Plunder (G1) looks for ore in INV_MISC and NPCs who knocked out
        /// the hero said "not even ore" with 1000 in the backpack.
        /// </summary>
        private bool TryGetInvItemBySlot(NpcInstance npc, int invType, int slot, out ItemInstance item, out int amount)
        {
            item = null;
            amount = 0;
            if (npc?.GetUserData() == null || slot < 1 || invType < (int)VmGothicEnums.InvCats.InvWeapon ||
                invType >= (int)VmGothicEnums.InvCats.InvCatMax)
                return false;

            List<Gothic.Core.Models.Vob.ContentItem> items;
            try
            {
                items = _npcInventoryService.GetInventoryItems(npc, (VmGothicEnums.InvCats)invType);
            }
            catch
            {
                // Category slot was never initialized for this NPC - nothing in it.
                return false;
            }

            var found = 0;
            foreach (var contentItem in items)
            {
                if (contentItem.Amount <= 0)
                    continue;
                if (++found < slot)
                    continue;

                item = _vmCacheService.TryGetItemData(contentItem.Name);
                amount = contentItem.Amount;
                return item != null;
            }

            return false;
        }

        public void Npc_RemoveInvItems(IntPtr vmPtr)
        {
            // NpcCreator.ExtRemoveInvItems();
        }

        public void EquipItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtEquipItem(npc, itemId);
        }


        public int Npc_GetDistToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            var dist = _npcAiService.ExtNpcGetDistToNpc(npc1, npc2);
            return LogInstantExternal(nameof(Npc_GetDistToNpc), dist, npc1, npc2);
        }

        public int Npc_GetHeightToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            var height = _npcAiService.ExtNpcGetHeightToNpc(npc1, npc2);
            return LogInstantExternal(nameof(Npc_GetHeightToNpc), height, npc1, npc2);
        }

        public int Npc_HasEquippedArmor(NpcInstance npc)
        {
            var ret = _npcAiService.ExtNpcHasEquippedArmor(npc) ? 1 : 0;
            return LogInstantExternal(nameof(Npc_HasEquippedArmor), ret, npc);
        }

        public ItemInstance Npc_GetEquippedMeleeWeapon(NpcInstance npc)
        {
            var item = _npcHelperService.ExtNpcGetEquippedMeleeWeapon(npc);
            return LogInstantExternal(nameof(Npc_GetEquippedMeleeWeapon), item, npc);
        }

        public int Npc_HasEquippedMeleeWeapon(NpcInstance npc)
        {
            var ret = _npcHelperService.ExtNpcHasEquippedMeleeWeapon(npc) ? 1 : 0;
            return LogInstantExternal(nameof(Npc_HasEquippedMeleeWeapon), ret, npc);
        }

        public ItemInstance Npc_GetEquippedRangedWeapon(NpcInstance npc)
        {
            var item = _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc);
            return LogInstantExternal(nameof(Npc_GetEquippedRangedWeapon), item, npc);
        }

        public int Npc_HasEquippedRangedWeapon(NpcInstance npc)
        {
            var ret = _npcHelperService.ExtNpcHasEquippedRangedWeapon(npc) ? 1 : 0;
            return LogInstantExternal(nameof(Npc_HasEquippedRangedWeapon), ret, npc);
        }

        public int Npc_GetDistToWP(NpcInstance npc, string waypoint)
        {
            var dist = _npcHelperService.ExtNpcGetDistToWp(npc, waypoint);
            return LogInstantExternal(nameof(Npc_GetDistToWP), dist, npc, waypoint);
        }

        public void Npc_PercDisable(NpcInstance npc, int perception)
        {
            _npcAiService.ExtNpcPerceptionDisable(npc, (VmGothicEnums.PerceptionType)perception);
            LogInstantExternal(nameof(Npc_PercDisable), npc, perception);
        }

        public int Npc_CanSeeItem(NpcInstance npc, ItemInstance item)
        {
            // Wld_DetectItem already confirmed the item is within range; this is a simple proximity proxy.
            var result = item != null ? 1 : 0;
            return LogInstantExternal(nameof(Npc_CanSeeItem), result, npc);
        }

        public int Npc_CanSeeNpc(NpcInstance npc, NpcInstance target)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcCanSeeNpc(npc, target, false));
            return LogInstantExternal(nameof(Npc_CanSeeNpc), ret, npc, target);
        }

        public int Npc_CanSeeNpcFreeLOS(NpcInstance npc, NpcInstance target)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcCanSeeNpc(npc, target, true));
            return LogInstantExternal(nameof(Npc_CanSeeNpcFreeLOS), ret, npc, target);
        }

        public void Npc_ClearAiQueue(NpcInstance npc)
        {
            _npcAiService.ExtNpcClearAiQueue(npc);
            LogInstantExternal(nameof(Npc_ClearAiQueue), npc);
        }

        public void Npc_ClearInventory(NpcInstance npc)
        {
            _npcInventoryService.ExtNpcClearInventory(npc);
            LogInstantExternal(nameof(Npc_ClearInventory), npc);
        }

        public string Npc_GetNextWp(NpcInstance npc)
        {
            var wp = _npcService.ExtNpcGetNextWp(npc);
            return LogInstantExternal(nameof(Npc_GetNextWp), wp, npc);
        }

        public int Npc_GetTalentSkill(NpcInstance npc, int skillId)
        {
            var ret = _npcHelperService.ExtNpcGetTalentSkill(npc, skillId);
            return LogInstantExternal(nameof(Npc_GetTalentSkill), ret, npc, skillId);
        }

        public int Npc_GetTalentValue(NpcInstance npc, int skillId)
        {
            var ret = _npcHelperService.ExtNpcGetTalentValue(npc, skillId);
            return LogInstantExternal(nameof(Npc_GetTalentValue), ret, npc, skillId);
        }

        public int Npc_KnowsInfo(NpcInstance npc, int infoInstance)
        {
            var res = _dialogService.ExtNpcKnowsInfo(npc, infoInstance);
            return LogInstantExternal(nameof(Npc_KnowsInfo), Convert.ToInt32(res), npc, infoInstance);
        }

        public int Npc_CheckInfo(NpcInstance npc, int important)
        {
            var ret = Convert.ToInt32(_dialogService.ExtCheckInfo(npc, Convert.ToBoolean(important)));
            return LogInstantExternal(nameof(Npc_CheckInfo), ret, npc, important);
        }

        public int Npc_IsDead(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsDead(npc));
            return LogInstantExternal(nameof(Npc_IsDead), ret, npc);
        }

        public int Npc_IsInState(NpcInstance npc, int state)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsInState(npc, state));
            return LogInstantExternal(nameof(Npc_IsInState), ret, npc, state);
        }

        public void Npc_SetToFistMode(NpcInstance npc)
        {
            _npcService.ExtNpcSetToFistMode(npc);
            LogInstantExternal(nameof(Npc_SetToFistMode), npc);
        }

        public void Npc_SetToFightMode(NpcInstance npc, int itemIndex)
        {
            _npcService.ExtNpcSetToFightMode(npc, itemIndex);
            LogInstantExternal(nameof(Npc_SetToFightMode), npc, itemIndex);
        }

        public int Npc_IsInFightMode(NpcInstance npc, int fightMode)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsInFightMode(npc, (VmGothicEnums.FightMode)fightMode));
            return LogInstantExternal(nameof(Npc_IsInFightMode), ret, npc, fightMode);
        }

        public int Npc_IsPlayer(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcIsPlayer(npc));
            return LogInstantExternal(nameof(Npc_IsPlayer), ret, npc);
        }

        public int Npc_OwnedByNpc(ItemInstance item, NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcOwnedByNpc(item, npc));
            return LogInstantExternal(nameof(Npc_OwnedByNpc), ret, item, npc);
        }

        public int Npc_GetTarget(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtGetTarget(npc));
            return LogInstantExternal(nameof(Npc_GetTarget), ret, npc);
        }

        public void Npc_SetTarget(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtSetTarget(npc, target);
            LogInstantExternal(nameof(Npc_SetTarget), npc, target);

            if (target == null)
                return;
            
            // FIXME - Also implement, that we unset our hero as target. Otherwise it will always play fight music!
            if (target.GetUserData()!.PrefabProps.IsHero())
                GlobalEventDispatcher.SetHeroAsTarget.Invoke(npc.GetUserData(), target.GetUserData());
        }

        public void Npc_SendPassivePerc(NpcInstance npc, int perc, NpcInstance victim, NpcInstance other)
        {
            _npcAiService.Npc_SendPassivePerc(npc, (VmGothicEnums.PerceptionType)perc, victim, other);
            LogInstantExternal(nameof(Npc_SetTarget), npc, perc, victim, other);
        }

        public int Npc_SetTrueGuild(NpcInstance npc, int guild)
        {
            _npcAiService.ExtSetTrueGuild(npc, guild);
            return LogInstantExternal(nameof(Npc_SetTrueGuild), 0, npc, guild);
        }

        public int Npc_GetTrueGuild(NpcInstance npc)
        {
            var ret = _npcAiService.ExtGetTrueGuild(npc);
            return LogInstantExternal(nameof(Npc_GetTrueGuild), ret, npc);
        }

        public void Npc_SetRefuseTalk(NpcInstance npc, int refuseSeconds)
        {
            _npcAiService.ExtSetRefuseTalk(npc, refuseSeconds);
        }

        public int Npc_RefuseTalk(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtRefuseTalk(npc));
            return LogInstantExternal(nameof(Npc_RefuseTalk), ret, npc);
        }

        /// <summary>
        /// No news memory yet. Proxy for NEWS_DEFEAT only: the NPC knows the hero defeated him (AIV_WASDEFEATEDBYSC),
        /// so it's news about himself as the victim.
        /// </summary>
        public int Npc_HasNews(NpcInstance npc, int newsId, NpcInstance offender, NpcInstance victim)
        {
            var vm = _gameStateService.GothicVm;
            var newsDefeat = vm.GetSymbolByName("NEWS_DEFEAT");
            var wasDefeated = vm.GetSymbolByName("AIV_WASDEFEATEDBYSC");
            if (npc == null || newsDefeat == null || wasDefeated == null || newsId != newsDefeat.GetInt(0) ||
                (victim != null && victim.Index != npc.Index))
                return LogInstantExternal(nameof(Npc_HasNews), 0, npc, newsId, offender, victim);

            var ret = npc.GetAiVar(wasDefeated.GetInt(0)) == 1 ? 1 : 0;
            return LogInstantExternal(nameof(Npc_HasNews), ret, npc, newsId, offender, victim);
        }

        #endregion

        #region Day Routine

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

        #endregion

        #region World

        public void Wld_InsertNpc(int npcInstance, string spawnPoint)
        {
            _npcService.ExtWldInsertNpc(npcInstance, spawnPoint);
        }


        public int Wld_IsFPAvailable(NpcInstance npc, string fpName)
        {
            var response = _npcService.ExtWldIsFpAvailable(npc, fpName);
            return LogInstantExternal(nameof(Wld_IsFPAvailable), Convert.ToInt32(response), npc, fpName);
        }


        public int Wld_IsMobAvailable(NpcInstance npc, string vobName)
        {
            var res = _npcHelperService.ExtIsMobAvailable(npc, vobName);
            return LogInstantExternal(nameof(Wld_IsMobAvailable), Convert.ToInt32(res), npc, vobName);
        }

        public int Wld_DetectNpc(NpcInstance npc, int npcInstance, int aiState, int guild)
        {
            var ret = Wld_DetectNpcEx(npc, npcInstance, aiState, guild, 1);
            return LogInstantExternal(nameof(Wld_DetectNpc), ret, npc, npcInstance, aiState, guild);
        }

        public int Wld_DetectNpcEx(NpcInstance npc, int npcInstance, int aiState, int guild, int detectPlayer)
        {
            var res = _npcHelperService.ExtWldDetectNpcEx(npc, npcInstance, aiState, guild, Convert.ToBoolean(detectPlayer));
            var ret = Convert.ToInt32(res);
            return LogInstantExternal(nameof(Wld_DetectNpcEx), ret, npc, npcInstance, aiState, guild, detectPlayer);
        }

        public int Wld_IsNextFPAvailable(NpcInstance npc, string fpNamePart)
        {
            var result = _npcService.ExtIsNextFpAvailable(npc, fpNamePart);
            return LogInstantExternal(nameof(Wld_IsNextFPAvailable), Convert.ToInt32(result), npc, fpNamePart);
        }

        public void Wld_SetTime(int hour, int minute)
        {
            _gameTimeService.SetTime(hour, minute);
        }

        public int Wld_GetDay()
        {
            var ret = _gameTimeService.GetDay();
            return LogInstantExternal(nameof(Wld_GetDay), ret);
        }

        public int Wld_IsTime(int beginHour, int beginMinute, int endHour, int endMinute)
        {
            var begin = new TimeSpan(beginHour, beginMinute, 0);
            var end = new TimeSpan(endHour, endMinute, 0);

            var now = _gameTimeService.GetCurrentTime();

            if (begin <= end && begin <= now && now < end)
            {
                return LogInstantExternal(nameof(Wld_IsTime), 1, beginHour, beginMinute, endHour, endMinute);
            }

            if (begin > end && (begin < now || now <= end)) // begin and end span across midnight
            {
                return LogInstantExternal(nameof(Wld_IsTime), 1, beginHour, beginMinute, endHour, endMinute);
            }

            return LogInstantExternal(nameof(Wld_IsTime), 0, beginHour, beginMinute, endHour, endMinute);
        }

        public int Wld_GetMobState(NpcInstance npc, string scheme)
        {
            var ret = _npcHelperService.ExtWldGetMobState(npc, scheme);
            return LogInstantExternal(nameof(Wld_GetMobState), ret, npc, scheme);
        }

        public void Wld_InsertItem(int itemInstance, string spawnpoint)
        {
            _vobService.ExtWldInsertItem(itemInstance, spawnpoint);
        }

        private static void RegisterIfDeclared(VmExternalGuard vm, string name, System.Action register)
        {
            if (vm.GetSymbolByName(name) != null)
                register();
        }

        public void Wld_RemoveNpc(int npcInstance)
        {
            _npcService.ExtWldRemoveNpc(npcInstance);
        }

        public void Wld_InsertObject(string visual, string spawnpoint)
        {
            _vobService.ExtWldInsertObject(visual, spawnpoint);
        }

        public void Wld_ExchangeGuildAttitudes(string name)
        {
            var humanGguildAttitudes = _gameStateService.GothicVm.GetSymbolByName(name);

            if (humanGguildAttitudes == null)
                return;

            
            // The guild attitudes are in a matrix. Where a row is the first guild and columns are the second one to check against.
            // The first part of this matrix is human guilds. e.g. 16x16 based on the full matrix e.g., 42x42
            // We therefore need to loop through the humanGuilds but fill the values inside allGuilds matrix first area top-left.
            var humanGuildCount = _gameStateService.GuildHumanCount;
            var allGuildCount = _gameStateService.GuildCount;
            for (var row = 0; row < humanGuildCount; ++row)
            {
                for (var column = 0; column < humanGuildCount; ++column)
                {
                    var humanAttitudeIndex = row * humanGuildCount + column;
                    var allGuildAttitudeIndex = row * allGuildCount + column;
                    _gameStateService.GuildAttitudes[allGuildAttitudeIndex] = humanGguildAttitudes.GetInt((ushort)humanAttitudeIndex);
                }
            }
        }

        public void Wld_SetGuildAttitude(int guild1, int attitude, int guild2)
        {
            if (guild1 < 0 || guild2 < 0 || guild1 >= _gameStateService.GuildCount || guild2 >= _gameStateService.GuildCount)
                return;

            _gameStateService.GuildAttitudes[guild1 * _gameStateService.GuildCount + guild2] = attitude;
        }

        public void Wld_SetObjectRoutine(int hour, int minute, string name, int status)
        {
            // FIXME - Do more with these ObjectRoutines.
            _vobService.ObjectRoutines.TryAdd(name, new());
            _vobService.ObjectRoutines[name].Add((hour, minute, status));
        }

        public void Wld_SetMobRoutine(int hour, int minute, string name, int status)
        {
            // FIXME - Do more with these MobRoutines.
            _npcService.MobRoutines.TryAdd(name, new());
            _npcService.MobRoutines[name].Add((hour, minute, status));
        }

        private bool _debugWld_AssignRoomToGuildExecuted;
        public void Wld_AssignRoomToGuild(string room, int guild)
        {
            if (!_debugWld_AssignRoomToGuildExecuted)
            {
                Logger.LogWarningEditor($"Method >Wld_AssignRoomToGuild< not yet implemented in DaedalusVM.", LogCat.ZenKit);
                _debugWld_AssignRoomToGuildExecuted = true;
            }
        }

        public int Wld_GetGuildAttitude(int guild1, int guild2)
        {
            if (guild1 < 0 || guild2 < 0 || guild1 >= _gameStateService.GuildCount || guild2 >= _gameStateService.GuildCount)
                return LogInstantExternal(nameof(Wld_GetGuildAttitude), 0, guild1, guild2);

            var ret = _gameStateService.GuildAttitudes[guild1 * _gameStateService.GuildCount + guild2];
            return LogInstantExternal(nameof(Wld_GetGuildAttitude), ret, guild1, guild2);
        }

        public void Wld_SendTrigger(string vobName)
        {
            _vobService.DispatchTrigger(vobName);
        }

        public void Wld_SendUntrigger(string vobName)
        {
            _vobService.DispatchUntrigger(vobName);
        }

        #endregion

        #region Misc

        public void Perc_SetRange(int perceptionId, int rangeInCm)
        {
            _npcHelperService.ExtPErcSetRange(perceptionId, rangeInCm);
        }

        public string ConcatStrings(string str1, string str2)
        {
            return str1 + str2;
        }

        public string IntToString(int x)
        {
            return x.ToString();
        }

        public string FloatToString(float x)
        {
            return x.ToString();
        }

        public int FloatToInt(float x)
        {
            return (int)x;
        }

        public float IntToFloat(int x)
        {
            return x;
        }

        public void IntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            _storyService.ExtIntroduceChapter(chapter, text, texture, wav, time);
        }

        #endregion
    }
}
