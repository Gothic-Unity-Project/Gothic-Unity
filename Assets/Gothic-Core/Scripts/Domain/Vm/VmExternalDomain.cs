using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
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
        [Inject] private readonly NpcRandomAniService _npcRandomAniService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly MagicService _magicService;
        [Inject] private readonly WorldEffectService _worldEffectService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly MobRoutineService _mobRoutineService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly SkyService _skyService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly RoomService _roomService;
        [Inject] private readonly HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly VmCacheService _vmCacheService;

        private readonly HashSet<string> _externalsCalledWithoutNpc = new();

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

            var vm = _gameStateService.GothicVm;
            vm.RegisterExternalDefault(DefaultExternal);

            // AI
            vm.RegisterExternal<NpcInstance>("Ai_Attack", Ai_Attack);
            vm.RegisterExternal<NpcInstance>("AI_StandUp", AI_StandUp);
            vm.RegisterExternal<NpcInstance>("AI_Flee", AI_Flee);
            vm.RegisterExternal<NpcInstance>("AI_StandUpQuick", AI_StandUpQuick);
            vm.RegisterExternal<NpcInstance, int>("AI_SetWalkMode", AI_SetWalkMode);
            vm.RegisterExternal<NpcInstance>("AI_AlignToFP", AI_AlignToFP);
            vm.RegisterExternal<NpcInstance>("AI_AlignToWP", AI_AlignToWP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoFP", AI_GotoFP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoWP", AI_GotoWP);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_GotoNpc", AI_GotoNpc);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_FinishingMove", AI_FinishingMove);
            vm.RegisterExternal<NpcInstance, string>("AI_PlayAni", AI_PlayAni);
            vm.RegisterExternal<NpcInstance, int, int, string>("AI_StartState", AI_StartState);
            vm.RegisterExternal<NpcInstance, int, int>("AI_UseItemToState", AI_UseItemToState);
            vm.RegisterExternal<NpcInstance, float>("AI_Wait", AI_Wait);
            vm.RegisterExternal<NpcInstance, int>("AI_WaitMs", AI_WaitMs);
            vm.RegisterExternal<int, NpcInstance, string, int>("AI_UseMob", AI_UseMob);
            vm.RegisterExternal<NpcInstance, string>("AI_GoToNextFP", AI_GoToNextFP);
            vm.RegisterExternal<NpcInstance>("AI_DrawWeapon", AI_DrawWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyMeleeWeapon", AI_ReadyMeleeWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyRangedWeapon", AI_ReadyRangedWeapon);
            vm.RegisterExternal<NpcInstance>("AI_RemoveWeapon", AI_RemoveWeapon);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_Output", AI_Output);
            vm.RegisterExternal<NpcInstance>("AI_ProcessInfos", AI_ProcessInfos);
            vm.RegisterExternal<NpcInstance>("AI_StopProcessInfos", AI_StopProcessInfos);
            vm.RegisterExternal<NpcInstance, string>("AI_LookAt", AI_LookAt);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_LookAtNPC", AI_LookAtNPC);
            vm.RegisterExternal<NpcInstance>("AI_StopLookAt", AI_StopLookAt);
            vm.RegisterExternal<NpcInstance>("AI_ContinueRoutine", AI_ContinueRoutine);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_TurnToNPC", AI_TurnToNPC);
            if (vm.GetSymbolByName("AI_WhirlAround") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_WhirlAround", AI_WhirlAround);
            if (vm.GetSymbolByName("AI_TurnAway") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_TurnAway", AI_TurnAway);
            if (vm.GetSymbolByName("AI_UseItem") != null)
                vm.RegisterExternal<NpcInstance, int>("AI_UseItem", AI_UseItem);
            if (vm.GetSymbolByName("AI_UnequipWeapons") != null)
                vm.RegisterExternal<NpcInstance>("AI_UnequipWeapons", AI_UnequipWeapons);
            vm.RegisterExternal<NpcInstance, string, int>("AI_PlayAniBS", AI_PlayAniBS);
            vm.RegisterExternal<NpcInstance>("AI_Dodge", AI_Dodge);
            vm.RegisterExternal<NpcInstance>("AI_UnequipArmor", AI_UnequipArmor);
            if (vm.GetSymbolByName("AI_EquipArmor") != null)
                vm.RegisterExternal<NpcInstance, int>("AI_EquipArmor", AI_EquipArmor);
            if (vm.GetSymbolByName("AI_EquipBestArmor") != null)
                vm.RegisterExternal<NpcInstance>("AI_EquipBestArmor", AI_EquipBestArmor);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_OutputSVM", AI_OutputSVM);
            RegisterSmallExternals(vm);

            // Apply Options
            // Doc
            // Helper
            vm.RegisterExternal<int, int>("Hlp_Random", Hlp_Random);
            vm.RegisterExternal<int, string, string>("Hlp_StrCmp", Hlp_StrCmp);
            vm.RegisterExternal<int, ItemInstance, int>("Hlp_IsItem", Hlp_IsItem);
            vm.RegisterExternal<int, ItemInstance>("Hlp_IsValidItem", Hlp_IsValidItem);
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
            // Declared with a float time, but the scripts pass int constants (Time_Haste1) - popping a float fails.
            if (vm.GetSymbolByName("Mdl_ApplyOverlayMDSTimed") != null)
                vm.RegisterExternal<NpcInstance, string, int>("Mdl_ApplyOverlayMDSTimed", Mdl_ApplyOverlayMdsTimed);
            if (vm.GetSymbolByName("Mdl_ApplyRandomAni") != null)
                vm.RegisterExternal<NpcInstance, string, string>("Mdl_ApplyRandomAni", Mdl_ApplyRandomAni);
            if (vm.GetSymbolByName("Mdl_ApplyRandomAniFreq") != null)
                vm.RegisterExternal<NpcInstance, string, float>("Mdl_ApplyRandomAniFreq", Mdl_ApplyRandomAniFreq);
            vm.RegisterExternal<NpcInstance, string, int, int, string, int, int, int>("Mdl_SetVisualBody", Mdl_SetVisualBody);
            vm.RegisterExternal<NpcInstance, float, float, float>("Mdl_SetModelScale", Mdl_SetModelScale);
            vm.RegisterExternal<NpcInstance, float>("Mdl_SetModelFatness", Mdl_SetModelFatness);
            vm.RegisterExternal<NpcInstance, string>("Mdl_RemoveOverlayMDS", Mdl_RemoveOverlayMDS);
            if (vm.GetSymbolByName("Mdl_StartFaceAni") != null)
                vm.RegisterExternal<NpcInstance, string, float, float>("Mdl_StartFaceAni", Mdl_StartFaceAni);
            if (vm.GetSymbolByName("Mdl_ApplyRandomFaceAni") != null)
                vm.RegisterExternal<NpcInstance, string, float, float, float, float, float>("Mdl_ApplyRandomFaceAni",
                    Mdl_ApplyRandomFaceAni);

            // Mission

            // Mob
            vm.RegisterExternal<int, string, int>("Mob_HasItems", Mob_HasItems);

            // NPC
            vm.RegisterExternal<NpcInstance, int, int>("Npc_SetTalentValue", Npc_SetTalentValue);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_ChangeAttribute", Npc_ChangeAttribute);
            vm.RegisterExternal<NpcInstance, int>("CreateInvItem", CreateInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("CreateInvItems", CreateInvItems);
            vm.RegisterExternal<NpcInstance, int>("Npc_RemoveInvItem", Npc_RemoveInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_RemoveInvItems", Npc_RemoveInvItems);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_PercEnable", Npc_PercEnable);
            vm.RegisterExternal<NpcInstance, float>("Npc_SetPercTime", Npc_SetPercTime);
            vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_GetPermAttitude", Npc_GetPermAttitude);
            vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_GetAttitude", Npc_GetAttitude);
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
            vm.RegisterExternal<int, NpcInstance>("Npc_HasRangedWeaponWithAmmo", Npc_HasRangedWeaponWithAmmo);
            vm.RegisterExternal<NpcInstance>("AI_EquipBestRangedWeapon", AI_EquipBestRangedWeapon);
            vm.RegisterExternal<NpcInstance>("AI_EquipBestMeleeWeapon", AI_EquipBestMeleeWeapon);
            vm.RegisterExternal<NpcInstance>("AI_StopAim", AI_StopAim);
            vm.RegisterExternal<int, NpcInstance, string>("Npc_GetDistToWP", Npc_GetDistToWP);
            vm.RegisterExternal<NpcInstance, int>("Npc_PercDisable", Npc_PercDisable);
            vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_CanSeeNpc", Npc_CanSeeNpc);
            vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_CanSeeNpcFreeLOS", Npc_CanSeeNpcFreeLOS);
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
            vm.RegisterExternal<int, ItemInstance, NpcInstance>("Npc_OwnedByNpc", Npc_OwnedByNpc);
            if (vm.GetSymbolByName("Npc_GetDetectedMob") != null)
                vm.RegisterExternal<string, NpcInstance>("Npc_GetDetectedMob", Npc_GetDetectedMob);
            if (vm.GetSymbolByName("Npc_IsDetectedMobOwnedByNpc") != null)
                vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_IsDetectedMobOwnedByNpc",
                    Npc_IsDetectedMobOwnedByNpc);
            if (vm.GetSymbolByName("Npc_IsDetectedMobOwnedByGuild") != null)
                vm.RegisterExternal<int, NpcInstance, int>("Npc_IsDetectedMobOwnedByGuild",
                    Npc_IsDetectedMobOwnedByGuild);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTarget", Npc_GetTarget);
            vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_SetTarget", Npc_SetTarget);
            RegisterFightExternals(vm);
            RegisterMagicExternals(vm);
            vm.RegisterExternal<NpcInstance, int, NpcInstance, NpcInstance>("Npc_SendPassivePerc", Npc_SendPassivePerc);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_SetTrueGuild", Npc_SetTrueGuild);
            vm.RegisterExternal<int, NpcInstance>("Npc_GetTrueGuild", Npc_GetTrueGuild);
            vm.RegisterExternal<NpcInstance, int>("Npc_SetRefuseTalk", Npc_SetRefuseTalk);
            vm.RegisterExternal<int, NpcInstance>("Npc_RefuseTalk", Npc_RefuseTalk);

            // G2 externals only: G1's scripts (and mods) that don't declare them would make the registration throw.
            if (IsDeclared(vm, "Npc_GetHeightToNpc"))
                vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_GetHeightToNpc", Npc_GetHeightToNpc);
            if (IsDeclared(vm, "Npc_StopAni"))
                vm.RegisterExternal<NpcInstance, string>("Npc_StopAni", Npc_StopAni);
            if (IsDeclared(vm, "Wld_IsRaining"))
                vm.RegisterExternal("Wld_IsRaining", Wld_IsRaining);


            RegisterDocExternals(vm);

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
            if (vm.GetSymbolByName("Snd_Play3d") != null)
                vm.RegisterExternal<NpcInstance, string>("Snd_Play3d", Snd_Play3d);
            RegisterItemExternals(vm);

            // Video
            RegisterPlayVideo(vm);

            // Day Routine
            vm.RegisterExternal<NpcInstance, int, int, int, int, int, string>("TA_MIN", TA_MIN);
            vm.RegisterExternal<NpcInstance, int, int, int, string>("TA", Ta);
            vm.RegisterExternal<NpcInstance, string>("Npc_ExchangeRoutine", Npc_ExchangeRoutine);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_IsInRoutine", Npc_IsInRoutine);

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
            RegisterRoomExternals(vm);
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

        public void AI_Flee(NpcInstance npc)
        {
            _npcAiService.ExtAiFlee(npc);
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

        public void AI_FinishingMove(NpcInstance self, NpcInstance other)
        {
            _npcAiService.ExtAiFinishingMove(self, other);
        }

        public void AI_PlayAni(NpcInstance npc, string name)
        {
            _npcAiService.ExtAiPlayAni(npc, name);
        }

        public void AI_StartState(NpcInstance npc, int function, int stateBehaviour, string wayPointName)
        {
            _npcAiService.ExtAiStartState(npc, function, Convert.ToBoolean(stateBehaviour), wayPointName);
        }

        public void AI_UseItemToState(NpcInstance npc, int itemId, int expectedInventoryCount)
        {
            _npcAiService.ExtAiUseItemToState(npc, ResolveItemId(itemId), expectedInventoryCount);
        }

        /// <summary>
        /// `AI_UseItem(self, item)` hands over the global `item` variable (G1 ZS_HealSelf: the food found by
        /// Npc_GetInvItemBySlot) - its symbol, not an item instance. The engine uses the item in it. Initializing the
        /// variable as an item can abort the process (found on BoroBongo's branch: Lukis froze after a fight).
        /// </summary>
        private int ResolveItemId(int itemId)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByIndex(itemId);
            if (symbol == null || !string.Equals(symbol.Name, "ITEM", StringComparison.OrdinalIgnoreCase))
                return itemId;

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

        public int Npc_HasRangedWeaponWithAmmo(NpcInstance npc)
        {
            return _npcHelperService.ExtNpcHasRangedWeaponWithAmmo(npc) ? 1 : 0;
        }

        public void AI_EquipBestRangedWeapon(NpcInstance npc)
        {
            _npcHelperService.ExtAiEquipBestWeapon(npc, VmGothicEnums.ItemFlags.ItemKatFf);
        }

        public void AI_EquipBestMeleeWeapon(NpcInstance npc)
        {
            _npcHelperService.ExtAiEquipBestWeapon(npc, VmGothicEnums.ItemFlags.ItemKatNf);
        }

        /// <summary>
        /// Aiming is part of each shot (ShootRanged) - there is nothing to stop between shots.
        /// </summary>
        public void AI_StopAim(NpcInstance npc)
        {
        }

        public void AI_RemoveWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiUndrawWeapon(npc);
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

        public void AI_Dodge(NpcInstance npc)
        {
            _npcAiService.ExtAiDodge(npc);
        }

        public void AI_ContinueRoutine(NpcInstance npc)
        {
            _npcAiService.ExtAiContinueRoutine(npc);
        }

        /// <summary>
        /// Like the engine, the startled jump around to the other one (OpenGothic plays t_surprise_CW/CCW).
        /// </summary>
        public void AI_WhirlAround(NpcInstance npc, NpcInstance other)
        {
            _npcAiService.ExtAiWhirlAround(npc, other);
        }

        public void AI_TurnAway(NpcInstance npc, NpcInstance other)
        {
            _npcAiService.ExtAiTurnAway(npc, other);
        }

        public void AI_UseItem(NpcInstance npc, int itemId)
        {
            itemId = ResolveItemId(itemId);
            if (itemId >= 0)
                _npcAiService.ExtAiUseItem(npc, itemId);
        }

        /// <summary>
        /// The NPC takes off its melee and ranged weapons (into the inventory) - their meshes leave the belt and back
        /// too, a drawn one the hand (found on BoroBongo's branch).
        /// </summary>
        public void AI_UnequipWeapons(NpcInstance npc)
        {
            var container = npc.GetUserData();
            if (container == null)
                return;

            container.Props.EquippedItems.RemoveAll(i =>
                i.MainFlag is (int)VmGothicEnums.ItemFlags.ItemKatNf or (int)VmGothicEnums.ItemFlags.ItemKatFf);

            // Like after AI_RemoveWeapon, a drawn weapon is gone and follow-up animations are weaponless. Fists stay.
            var weaponState = (VmGothicEnums.WeaponState)container.Vob.FightMode;
            var isWeaponDrawn = weaponState is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H or
                VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow;
            if (isWeaponDrawn)
                container.Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;
            if (container.Go == null)
                return;

            var slotNames = new List<string> { Constants.SlotSword, Constants.SlotLongsword, Constants.SlotBow,
                Constants.SlotCrossbow };
            if (isWeaponDrawn)
                slotNames.Add(Npc.Actions.AnimationActions.DrawWeapon.GetHandSlotName(weaponState));
            foreach (var slotName in slotNames)
            {
                var slot = container.Go.FindChildRecursively(slotName);
                if (slot == null)
                    continue;

                for (var i = slot.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(slot.transform.GetChild(i).gameObject);
            }
        }

        public void AI_TurnToNPC(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiTurnToNpc(npc, target);
        }

        public void AI_PlayAniBS(NpcInstance npc, string name, int bodyState)
        {
            _npcAiService.ExtAiPlayAniBs(npc, name, bodyState);
        }

        public void AI_UnequipArmor(NpcInstance npc)
        {
            _npcInventoryService.ExtAiUnequipArmor(npc);
        }

        public void AI_EquipArmor(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtAiEquipArmor(npc, itemId);
        }

        public void AI_EquipBestArmor(NpcInstance npc)
        {
            _npcInventoryService.ExtAiEquipBestArmor(npc);
        }

        public void AI_OutputSVM(NpcInstance npc, NpcInstance target, string svmname)
        {
            _dialogService.ExtAiOutputSvm(npc, target, svmname);
        }

        /// <summary>
        /// The reference engine's externals the scripts call and we lacked: remarks while busy, pointing, body flags,
        /// single perceptions... Only the ones the game's scripts declare.
        /// </summary>
        private void RegisterSmallExternals(DaedalusVm vm)
        {
            bool IsDeclared(string name) => vm.GetSymbolByName(name) != null;

            if (IsDeclared("AI_OutputSVM_Overlay"))
                vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_OutputSVM_Overlay",
                    (npc, target, svm) => _dialogService.ExtAiOutputSvmOverlay(npc, target, svm));
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
            if (IsDeclared("Npc_GetLookAtTarget"))
                vm.RegisterExternal<NpcInstance, NpcInstance>("Npc_GetLookAtTarget",
                    _npcAiService.ExtNpcGetLookAtTarget);
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
            if (IsDeclared("Npc_CanSeeSource"))
                vm.RegisterExternal<int, NpcInstance>("Npc_CanSeeSource",
                    npc => Convert.ToInt32(_npcAiService.ExtNpcCanSeeSource(npc)));
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
            {
                Logger.LogError("Hlp_IsItem called with a null item", LogCat.ZenKit);
                return 0;
            }
            
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


        /// <summary>
        /// While the state animation loops, the random animation plays now and then (NpcRandomAniService).
        /// </summary>
        public void Mdl_ApplyRandomAni(NpcInstance npc, string stateAnimation, string randomAnimation)
        {
            _npcRandomAniService.AddRandomAni(npc, stateAnimation, randomAnimation);
        }

        /// <param name="seconds">Average time between the random animations.</param>
        public void Mdl_ApplyRandomAniFreq(NpcInstance npc, string stateAnimation, float seconds)
        {
            _npcRandomAniService.SetRandomAniFrequency(npc, stateAnimation, seconds);
        }

        public void Mdl_ApplyOverlayMds(NpcInstance npc, string overlayName)
        {
            _npcService.ExtApplyOverlayMds(npc, overlayName);
        }

        /// <param name="milliseconds">How long the overlay stays.</param>
        public void Mdl_ApplyOverlayMdsTimed(NpcInstance npc, string overlayName, int milliseconds)
        {
            _npcService.ExtApplyOverlayMdsTimed(npc, overlayName, milliseconds);
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

        public void Mdl_RemoveOverlayMDS(NpcInstance npc, string overlayName)
        {
            _npcService.ExtRemoveOverlayMds(npc, overlayName);
        }

        /// <summary>
        /// A face animation on the NPC's head (MMB), e.g. S_FRIGHTENED, S_ANGRY of B_SetFaceExpression. holdTime -1 =
        /// until another mood replaces it.
        /// </summary>
        public void Mdl_StartFaceAni(NpcInstance npc, string name, float intensity, float holdTime)
        {
            // Not built yet (out of range) or without a head (monsters).
            var prefabProps = npc.GetUserData()?.PrefabProps;
            if (prefabProps == null || prefabProps.HeadMorph == null)
                return;

            prefabProps.HeadMorph.StartFaceAnimation(name, intensity, holdTime);
        }

        /// <summary>
        /// A face animation played again and again (the scripts' commented R_EyesBlink example).
        /// </summary>
        public void Mdl_ApplyRandomFaceAni(NpcInstance npc, string name, float timeMin, float timeMinVar, float timeMax,
            float timeMaxVar, float probMin)
        {
            var prefabProps = npc.GetUserData()?.PrefabProps;
            if (prefabProps == null || prefabProps.HeadMorph == null)
                return;

            prefabProps.HeadMorph.ApplyRandomFaceAnimation(name, timeMin, timeMinVar, timeMax, timeMaxVar, probMin);
        }

        #endregion

        #region Print

        /// <summary>
        /// G1/G2: int PlayVideo(string), G2: int PlayVideoEx(string, int screenBlend, int exitSession).
        /// VR plays it in a "cinema" (VRCinema) and pauses the world meanwhile, like the engine blocks the game.
        /// </summary>
        private void RegisterPlayVideo(DaedalusVm vm)
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

        /// <summary>
        /// Whether the loaded scripts declare the external. Registering one they don't declare throws.
        /// </summary>
        private static bool IsDeclared(DaedalusVm vm, string name)
        {
            return vm.GetSymbolByName(name) != null;
        }

        /// <summary>
        /// Real signature in G1 + G2 is (msg, posX, posY, font, seconds) - the externals.d of the MDKs is outdated.
        /// Some script versions declare a return value, some don't. It has to match the DAT symbol, otherwise the
        /// VM stack breaks - so we register based on the symbol.
        /// AI_PrintScreen is shown immediately instead of being queued in the NPC's AI.
        /// </summary>
        private void RegisterPrintScreen(DaedalusVm vm, string name)
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

        /// <summary>
        /// NPCs finding and taking world items: a knocked out fighter his weapon (G1), a monster food (G1), the wisp
        /// what it digs up (G2). Only the ones the game's scripts declare.
        /// </summary>
        private void RegisterItemExternals(DaedalusVm vm)
        {
            bool IsDeclared(string name) => vm.GetSymbolByName(name) != null;

            if (IsDeclared("Wld_DetectItem"))
                vm.RegisterExternal<int, NpcInstance, int>("Wld_DetectItem", Wld_DetectItem);
            if (IsDeclared("AI_TakeItem"))
                vm.RegisterExternal<NpcInstance, DaedalusInstance>("AI_TakeItem", AI_TakeItem);
            if (IsDeclared("AI_GotoItem"))
                vm.RegisterExternal<NpcInstance, DaedalusInstance>("AI_GotoItem", AI_GotoItem);
            if (IsDeclared("Npc_GetDistToItem"))
                vm.RegisterExternal<int, NpcInstance, DaedalusInstance>("Npc_GetDistToItem", Npc_GetDistToItem);
            if (IsDeclared("Npc_GetHeightToItem"))
                vm.RegisterExternal<int, NpcInstance, DaedalusInstance>("Npc_GetHeightToItem", Npc_GetHeightToItem);
            if (IsDeclared("Npc_CanSeeItem"))
                vm.RegisterExternal<int, NpcInstance, DaedalusInstance>("Npc_CanSeeItem", Npc_CanSeeItem);
            if (IsDeclared("Wld_RemoveItem"))
                vm.RegisterExternal<int, DaedalusInstance>("Wld_RemoveItem", Wld_RemoveItem);
        }

        public int Wld_DetectItem(NpcInstance npc, int flags)
        {
            var result = _npcHelperService.ExtWldDetectItem(npc, flags);
            return LogInstantExternal(nameof(Wld_DetectItem), Convert.ToInt32(result), npc, flags);
        }

        public void AI_TakeItem(NpcInstance npc, DaedalusInstance item)
        {
            var container = _npcHelperService.GetDetectedItem(npc, item as ItemInstance);
            var npcContainer = npc?.GetUserData();
            if (container == null || npcContainer == null)
            {
                LogItemActionSkipped(nameof(AI_TakeItem), npc, item, npcContainer);
                return;
            }

            npcContainer.Props.AnimationQueue.Enqueue(new TakeItem(new AnimationAction(), npcContainer, container));
        }

        public void AI_GotoItem(NpcInstance npc, DaedalusInstance item)
        {
            var container = _npcHelperService.GetDetectedItem(npc, item as ItemInstance);
            var npcContainer = npc?.GetUserData();
            if (container == null || npcContainer == null)
            {
                LogItemActionSkipped(nameof(AI_GotoItem), npc, item, npcContainer);
                return;
            }

            npcContainer.Props.AnimationQueue.Enqueue(new GoToItem(new AnimationAction(), npcContainer,
                NpcHelperService.GetItemTransform(container)));
        }

        private static void LogItemActionSkipped(string external, NpcInstance npc, DaedalusInstance item,
            NpcContainer npcContainer)
        {
            var reason = npcContainer == null ? "NPC not spawned" : "item not detected or gone (Wld_DetectItem)";
            Logger.LogWarning($"{external}: {npc?.GetName(NpcNameSlot.Slot0)} skipped for " +
                $"{(item as ItemInstance)?.Name} - {reason}", LogCat.Npc);
        }

        public int Npc_GetDistToItem(NpcInstance npc, DaedalusInstance item)
        {
            return _npcHelperService.ExtNpcGetDistToItem(npc, item as ItemInstance);
        }

        public int Npc_GetHeightToItem(NpcInstance npc, DaedalusInstance item)
        {
            return _npcHelperService.ExtNpcGetHeightToItem(npc, item as ItemInstance);
        }

        public int Npc_CanSeeItem(NpcInstance npc, DaedalusInstance item)
        {
            return Convert.ToInt32(_npcHelperService.ExtNpcCanSeeItem(npc, item as ItemInstance));
        }

        /// <summary>
        /// The item 'self' detected leaves the world.
        /// </summary>
        public int Wld_RemoveItem(DaedalusInstance item)
        {
            _npcHelperService.ExtWldRemoveItem(_gameStateService.GothicVm.GlobalSelf as NpcInstance,
                item as ItemInstance);
            return 0;
        }

        public void Snd_Play(string soundName)
        {
            _audioService.PlaySound2D(soundName);
        }

        /// <summary>
        /// Books, letters and maps (DocService). Only the ones the game's scripts declare (Doc_SetLevelCoords is G2
        /// only) - registering an unknown symbol throws.
        /// </summary>
        private void RegisterDocExternals(DaedalusVm vm)
        {
            bool IsDeclared(string name) => vm.GetSymbolByName(name) != null;

            if (IsDeclared("Doc_Create"))
                vm.RegisterExternal<int>("Doc_Create", Doc_Create);
            if (IsDeclared("Doc_CreateMap"))
                vm.RegisterExternal<int>("Doc_CreateMap", Doc_CreateMap);
            if (IsDeclared("Doc_SetPages"))
                vm.RegisterExternal<int, int>("Doc_SetPages", Doc_SetPages);
            if (IsDeclared("Doc_SetPage"))
                vm.RegisterExternal<int, int, string, int>("Doc_SetPage", Doc_SetPage);
            if (IsDeclared("Doc_SetFont"))
                vm.RegisterExternal<int, int, string>("Doc_SetFont", Doc_SetFont);
            if (IsDeclared("Doc_SetMargins"))
                vm.RegisterExternal<int, int, int, int, int, int, int>("Doc_SetMargins", Doc_SetMargins);
            if (IsDeclared("Doc_PrintLine"))
                vm.RegisterExternal<int, int, string>("Doc_PrintLine", Doc_PrintLine);
            if (IsDeclared("Doc_PrintLines"))
                vm.RegisterExternal<int, int, string>("Doc_PrintLines", Doc_PrintLines);
            if (IsDeclared("Doc_Show"))
                vm.RegisterExternal<int>("Doc_Show", Doc_Show);
            if (IsDeclared("Doc_SetLevel"))
                vm.RegisterExternal<int, string>("Doc_SetLevel", Doc_SetLevel);
            if (IsDeclared("Doc_SetLevelCoords"))
                vm.RegisterExternal<int, int, int, int, int>("Doc_SetLevelCoords", Doc_SetLevelCoords);
        }

        public int Doc_Create()
        {
            return _docService.Create(false);
        }

        public int Doc_CreateMap()
        {
            return _docService.Create(true);
        }

        public void Doc_SetPages(int document, int count)
        {
            _docService.SetPages(document, count);
        }

        /// <param name="scale">Whether the texture is scaled to the page - our viewer always does.</param>
        public void Doc_SetPage(int document, int page, string texture, int scale)
        {
            _docService.SetPage(document, page, texture);
        }

        public void Doc_SetFont(int document, int page, string font)
        {
            _docService.SetFont(document, page, font);
        }

        /// <param name="isInPixels">Margins in pixels of the page texture (1) - the scripts always use pixels.</param>
        public void Doc_SetMargins(int document, int page, int left, int top, int right, int bottom, int isInPixels)
        {
            _docService.SetMargins(document, page, left, top, right, bottom);
        }

        public void Doc_PrintLine(int document, int page, string text)
        {
            _docService.PrintLine(document, page, text);
        }

        /// <summary>
        /// A paragraph that wraps at the page's margins (the viewer wraps every line).
        /// </summary>
        public void Doc_PrintLines(int document, int page, string text)
        {
            _docService.PrintLine(document, page, text);
        }

        public void Doc_Show(int document)
        {
            _docService.Show(document);
        }

        public void Doc_SetLevel(int document, string level)
        {
            _docService.SetLevel(document, level);
        }

        public void Doc_SetLevelCoords(int document, int left, int top, int right, int bottom)
        {
            _docService.SetLevelCoords(document, left, top, right, bottom);
        }

        /// <summary>
        /// A sound at the NPC (G_PickLock's sounds at the hero).
        /// </summary>
        public void Snd_Play3d(NpcInstance npc, string soundName)
        {
            var go = npc?.GetUserData()?.Go;
            if (go != null)
                _audioService.PlaySound3D(soundName, go.transform.position);
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

        public int Mob_HasItems(string mobName, int itemInstance)
        {
            var count = _vobService.ExtMobHasItems(mobName, itemInstance);
            return LogInstantExternal(nameof(Mob_HasItems), count, mobName, itemInstance);
        }

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
            var ret = (int)_npcAiService.ExtGetPermAttitude(self, other);
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
        
        /// <summary>
        /// Like the engine (OpenGothic npc_getinvitembyslot): the slot-th item (counted from 1) of an inventory
        /// category (INV_WEAPON, INV_FOOD, ...) becomes the scripts' global item, its amount is returned - 0 and no item
        /// if there's none. G1's ZS_HealSelf finds food this way, B_Plunder what to take.
        /// </summary>
        public int Npc_GetInvItemBySlot(NpcInstance npc, int category, int slot)
        {
            var vm = _gameStateService.GothicVm;
            vm.GlobalItem = null;
            if (npc?.GetUserData() == null || category <= 0 || category >= (int)VmGothicEnums.InvCats.InvCatMax ||
                slot <= 0)
                return 0;

            List<ContentItem> items;
            try
            {
                items = _npcInventoryService.GetInventoryItems(npc, (VmGothicEnums.InvCats)category);
            }
            catch (Exception e)
            {
                // ZenKit keeps all 9 categories - a never filled one is just empty and returns 0 below without a
                // warning. Only an unexpected error lands here.
                Logger.LogWarning($"Npc_GetInvItemBySlot: {npc.GetName(NpcNameSlot.Slot0)}'s inventory " +
                                  $"category {category} can't be read - no item ({e.Message})", LogCat.Npc);
                return 0;
            }

            if (slot > items.Count)
                return 0;

            var item = _vmCacheService.TryGetItemData(items[slot - 1].Name);
            vm.GlobalItem = item;
            return item != null ? items[slot - 1].Amount : 0;
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

        public void Npc_StopAni(NpcInstance npc, string name)
        {
            _npcAiService.ExtNpcStopAni(npc, name);
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

        public string Npc_GetDetectedMob(NpcInstance npc)
        {
            return _heroPerceptionService.GetDetectedMobScheme(npc);
        }

        public int Npc_IsDetectedMobOwnedByNpc(NpcInstance user, NpcInstance owner)
        {
            return _heroPerceptionService.IsDetectedMobOwnedByNpc(user, owner) ? 1 : 0;
        }

        public int Npc_IsDetectedMobOwnedByGuild(NpcInstance user, int guild)
        {
            return _heroPerceptionService.IsDetectedMobOwnedByGuild(user, guild) ? 1 : 0;
        }

        public int Npc_OwnedByNpc(ItemInstance item, NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtNpcOwnedByNpc(item, npc));
            return LogInstantExternal(nameof(Npc_OwnedByNpc), ret, item, npc);
        }

        /// <summary>
        /// The fight scripts check these before and during a fight (B_DrawWeapon, ZS_Attack, orc AI). Unregistered,
        /// they returned 0: nobody had a weapon drawn or a next target. Only registered if the game declares them.
        /// </summary>
        private void RegisterFightExternals(DaedalusVm vm)
        {
            if (vm.GetSymbolByName("Npc_HasReadiedWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedWeapon", Npc_HasReadiedWeapon);
            if (vm.GetSymbolByName("Npc_HasReadiedMeleeWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedMeleeWeapon", Npc_HasReadiedMeleeWeapon);
            if (vm.GetSymbolByName("Npc_HasReadiedRangedWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedRangedWeapon", Npc_HasReadiedRangedWeapon);
            if (vm.GetSymbolByName("Npc_IsDrawingWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_IsDrawingWeapon", Npc_IsDrawingWeapon);
            if (vm.GetSymbolByName("Npc_GetReadiedWeapon") != null)
                vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetReadiedWeapon", Npc_GetReadiedWeapon);
            if (vm.GetSymbolByName("Npc_GetNextTarget") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_GetNextTarget", Npc_GetNextTarget);
            if (vm.GetSymbolByName("Npc_GetGuildAttitude") != null)
                vm.RegisterExternal<int, NpcInstance, NpcInstance>("Npc_GetGuildAttitude", Npc_GetGuildAttitude);
        }

        /// <summary>
        /// Spells (MagicService). Only the ones the game's scripts declare: the scroll and last-hit externals are G2's.
        /// </summary>
        private void RegisterMagicExternals(DaedalusVm vm)
        {
            bool IsDeclared(string name) => vm.GetSymbolByName(name) != null;

            if (IsDeclared("AI_ReadySpell"))
                vm.RegisterExternal<NpcInstance, int, int>("AI_ReadySpell", AI_ReadySpell);
            if (IsDeclared("AI_UnreadySpell"))
                vm.RegisterExternal<NpcInstance>("AI_UnreadySpell", AI_UnreadySpell);
            if (IsDeclared("Npc_GetActiveSpell"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpell", Npc_GetActiveSpell);
            if (IsDeclared("Npc_GetActiveSpellCat"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellCat", Npc_GetActiveSpellCat);
            if (IsDeclared("Npc_GetActiveSpellLevel"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellLevel", Npc_GetActiveSpellLevel);
            if (IsDeclared("Npc_GetActiveSpellIsScroll"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetActiveSpellIsScroll", Npc_GetActiveSpellIsScroll);
            if (IsDeclared("Npc_SetActiveSpellInfo"))
                vm.RegisterExternal<int, NpcInstance, int>("Npc_SetActiveSpellInfo", Npc_SetActiveSpellInfo);
            if (IsDeclared("Npc_HasSpell"))
                vm.RegisterExternal<int, NpcInstance, int>("Npc_HasSpell", Npc_HasSpell);
            if (IsDeclared("Npc_IsDrawingSpell"))
                vm.RegisterExternal<int, NpcInstance>("Npc_IsDrawingSpell", Npc_IsDrawingSpell);
            if (IsDeclared("Npc_GetLastHitSpellID"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetLastHitSpellID", Npc_GetLastHitSpellID);
            if (IsDeclared("Npc_GetLastHitSpellCat"))
                vm.RegisterExternal<int, NpcInstance>("Npc_GetLastHitSpellCat", Npc_GetLastHitSpellCat);
            // What spells do through the scripts: teleport runes, summons, fear.
            if (IsDeclared("AI_Teleport"))
                vm.RegisterExternal<NpcInstance, string>("AI_Teleport", AI_Teleport);
            if (IsDeclared("Wld_SpawnNpcRange"))
                vm.RegisterExternal<NpcInstance, int, int, float>("Wld_SpawnNpcRange", Wld_SpawnNpcRange);
            if (IsDeclared("AI_SetNpcsToState"))
                vm.RegisterExternal<NpcInstance, int, int>("AI_SetNpcsToState", AI_SetNpcsToState);
            // Effects the scripts play: G2's teleports, Dementors, stone guardians, G1's Sleeper fireball.
            if (IsDeclared("Wld_PlayEffect"))
                vm.RegisterExternal<string, DaedalusInstance, DaedalusInstance, int, int, int, int>("Wld_PlayEffect",
                    Wld_PlayEffect);
            if (IsDeclared("Wld_StopEffect"))
                vm.RegisterExternal<string>("Wld_StopEffect", Wld_StopEffect);
        }

        /// <summary>
        /// Wld_PlayEffect(effect, origin, target, level, damage, damageType, isProjectile). The level picks the
        /// strength of a spell effect - played effects show their cast at once.
        /// </summary>
        public void Wld_PlayEffect(string effect, DaedalusInstance origin, DaedalusInstance target, int level,
            int damage, int damageType, int isProjectile)
        {
            _worldEffectService.Play(effect, origin, target, damage, damageType, isProjectile != 0);
        }

        public void Wld_StopEffect(string effect)
        {
            _worldEffectService.Stop(effect);
        }

        public void AI_Teleport(NpcInstance npc, string pointName)
        {
            _magicService.Teleport(npc, pointName);
        }

        /// <summary>
        /// The last parameter is a float in the scripts' declaration (the summons' lifetime).
        /// </summary>
        public void Wld_SpawnNpcRange(NpcInstance npc, int npcInstance, int count, float lifeTime)
        {
            _magicService.SpawnAround(npc, npcInstance, count, lifeTime);
        }

        public void AI_SetNpcsToState(NpcInstance npc, int state, int radius)
        {
            _magicService.SetNpcsToState(npc, state, radius);
        }

        public void AI_ReadySpell(NpcInstance npc, int spellId, int investMana)
        {
            _npcAiService.ExtAiReadySpell(npc, spellId, investMana);
        }

        /// <summary>
        /// Like the engine, putting the spell away is putting the weapon away.
        /// </summary>
        public void AI_UnreadySpell(NpcInstance npc)
        {
            _npcAiService.ExtAiUndrawWeapon(npc);
        }

        public int Npc_GetActiveSpell(NpcInstance npc)
        {
            var ret = _magicService.GetActiveSpell(npc);
            return LogInstantExternal(nameof(Npc_GetActiveSpell), ret, npc);
        }

        public int Npc_GetActiveSpellCat(NpcInstance npc)
        {
            var ret = _magicService.GetActiveSpellCategory(npc);
            return LogInstantExternal(nameof(Npc_GetActiveSpellCat), ret, npc);
        }

        public int Npc_GetActiveSpellLevel(NpcInstance npc)
        {
            var ret = _magicService.GetActiveSpellLevel(npc);
            return LogInstantExternal(nameof(Npc_GetActiveSpellLevel), ret, npc);
        }

        public int Npc_GetActiveSpellIsScroll(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_magicService.IsActiveSpellScroll(npc));
            return LogInstantExternal(nameof(Npc_GetActiveSpellIsScroll), ret, npc);
        }

        public int Npc_SetActiveSpellInfo(NpcInstance npc, int info)
        {
            _magicService.SetActiveSpellInfo(npc, info);
            return 0;
        }

        public int Npc_HasSpell(NpcInstance npc, int spellId)
        {
            var ret = Convert.ToInt32(_magicService.HasSpell(npc, spellId));
            return LogInstantExternal(nameof(Npc_HasSpell), ret, npc, spellId);
        }

        public int Npc_IsDrawingSpell(NpcInstance npc)
        {
            var ret = _magicService.GetDrawnScroll(npc);
            return LogInstantExternal(nameof(Npc_IsDrawingSpell), ret, npc);
        }

        public int Npc_GetLastHitSpellID(NpcInstance npc)
        {
            var ret = _magicService.GetLastHitSpell(npc);
            return LogInstantExternal(nameof(Npc_GetLastHitSpellID), ret, npc);
        }

        public int Npc_GetLastHitSpellCat(NpcInstance npc)
        {
            var ret = _magicService.GetLastHitSpellCategory(npc);
            return LogInstantExternal(nameof(Npc_GetLastHitSpellCat), ret, npc);
        }

        /// <summary>
        /// Engine: any weapon in the hands - melee, ranged or magic (not fists).
        /// </summary>
        public int Npc_HasReadiedWeapon(NpcInstance npc)
        {
            var ret = Convert.ToInt32(GetFightMode(npc).IsWeaponReadied());
            return LogInstantExternal(nameof(Npc_HasReadiedWeapon), ret, npc);
        }

        public int Npc_HasReadiedMeleeWeapon(NpcInstance npc)
        {
            var ret = Convert.ToInt32(GetFightMode(npc).IsMelee());
            return LogInstantExternal(nameof(Npc_HasReadiedMeleeWeapon), ret, npc);
        }

        public int Npc_HasReadiedRangedWeapon(NpcInstance npc)
        {
            var ret = Convert.ToInt32(GetFightMode(npc).IsRanged());
            return LogInstantExternal(nameof(Npc_HasReadiedRangedWeapon), ret, npc);
        }

        /// <summary>
        /// G2: the NPC is drawing its weapon right now (AI_DrawWeapon running).
        /// </summary>
        public int Npc_IsDrawingWeapon(NpcInstance npc)
        {
            var ret = Convert.ToInt32(npc?.GetUserData()?.Props.CurrentAction is DrawWeapon);
            return LogInstantExternal(nameof(Npc_IsDrawingWeapon), ret, npc);
        }

        public ItemInstance Npc_GetReadiedWeapon(NpcInstance npc)
        {
            var fightMode = GetFightMode(npc);
            var item = fightMode.IsMelee() ? _npcHelperService.ExtNpcGetEquippedMeleeWeapon(npc)
                : fightMode.IsRanged() ? _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc)
                : null;
            return LogInstantExternal(nameof(Npc_GetReadiedWeapon), item, npc);
        }

        public int Npc_GetNextTarget(NpcInstance npc)
        {
            var ret = Convert.ToInt32(_npcAiService.ExtGetNextTarget(npc));
            return LogInstantExternal(nameof(Npc_GetNextTarget), ret, npc);
        }

        /// <summary>
        /// The attitude between the guilds of both NPCs (= Wld_GetGuildAttitude(self.guild, other.guild)).
        /// </summary>
        public int Npc_GetGuildAttitude(NpcInstance self, NpcInstance other)
        {
            var ret = self == null || other == null
                ? (int)VmGothicEnums.Attitude.Neutral
                : Wld_GetGuildAttitude(self.Guild, other.Guild);
            return LogInstantExternal(nameof(Npc_GetGuildAttitude), ret, self, other);
        }

        private static VmGothicEnums.WeaponState GetFightMode(NpcInstance npc)
        {
            return (VmGothicEnums.WeaponState)(npc?.GetUserData()?.Vob.FightMode ?? 0);
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

        public int Npc_IsInRoutine(NpcInstance npc, int routineFunction)
        {
            var res = _npcRoutineService.ExtNpcIsInRoutine(npc, routineFunction);
            return LogInstantExternal(nameof(Npc_IsInRoutine), Convert.ToInt32(res), npc, routineFunction);
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
            if (_gameTimeService.SetTime(hour, minute))
                _npcService.ResetNpcsToRoutines();
        }

        public int Wld_GetDay()
        {
            var ret = _gameTimeService.GetDay();
            return LogInstantExternal(nameof(Wld_GetDay), ret);
        }

        /// <summary>
        /// G2's NPCs complain about the weather in their greetings while it rains.
        /// </summary>
        public int Wld_IsRaining()
        {
            var ret = Convert.ToInt32(_skyService.IsRaining);
            return LogInstantExternal(nameof(Wld_IsRaining), ret);
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
            _mobRoutineService.AddRoutine(hour, minute, name, status);
        }

        public void Wld_AssignRoomToGuild(string room, int guild)
        {
            _roomService.AssignRoomToGuild(room, guild);
        }

        /// <summary>
        /// Rooms (RoomService), like OpenGothic: registered where the game's scripts declare them.
        /// </summary>
        private void RegisterRoomExternals(DaedalusVm vm)
        {
            if (vm.GetSymbolByName("Wld_GetPlayerPortalGuild") != null)
                vm.RegisterExternal("Wld_GetPlayerPortalGuild", Wld_GetPlayerPortalGuild);
            if (vm.GetSymbolByName("Wld_GetFormerPlayerPortalGuild") != null)
                vm.RegisterExternal("Wld_GetFormerPlayerPortalGuild", Wld_GetFormerPlayerPortalGuild);
            if (vm.GetSymbolByName("Npc_GetPortalGuild") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_GetPortalGuild", Npc_GetPortalGuild);
            if (vm.GetSymbolByName("Npc_IsInPlayersRoom") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_IsInPlayersRoom", Npc_IsInPlayersRoom);
        }

        public int Wld_GetPlayerPortalGuild()
        {
            return _roomService.GetGuildOfRoom(_roomService.HeroRoom);
        }

        public int Wld_GetFormerPlayerPortalGuild()
        {
            return _roomService.GetGuildOfRoom(_roomService.HeroFormerRoom);
        }

        public int Npc_GetPortalGuild(NpcInstance npc)
        {
            return _roomService.GetGuildOfRoom(_roomService.GetRoomOf(npc?.GetUserData()));
        }

        /// <summary>
        /// Like OpenGothic: the NPC's room is the hero's - outdoors counts as one room too.
        /// </summary>
        public int Npc_IsInPlayersRoom(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            if (container?.Go == null)
                return 0;

            return _roomService.GetRoomOf(container).EqualsIgnoreCase(_roomService.HeroRoom) ? 1 : 0;
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
