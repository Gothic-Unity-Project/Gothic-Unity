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
    /// Helpers shared by all Vm*ExternalDomain classes: converting the VM's instance parameters (AsNpc, AsItem),
    /// the hero check, the registration guard for externals a script may not declare, and the zSpy logging state.
    /// </summary>
    public class VmExternalSupport
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;

        public bool EnableZSpyLogs { get; private set; }
        public int ZSpyChannel { get; private set; }
        public bool IgnoreSpammyMessages { get; private set; }

        private bool _zSpyInstantLogging;

        private readonly System.Collections.Generic.HashSet<string> _loggedInstanceMismatches = new();

        public VmExternalSupport()
        {
            this.Inject();
        }

        /// <summary>
        /// Re-reads the zSpy settings of the DeveloperConfig. Called before every registration round.
        /// </summary>
        public void Init()
        {
            EnableZSpyLogs = _configService.Dev.EnableZSpyLogs;
            _zSpyInstantLogging = EnableZSpyLogs && _configService.Dev.EnableZSpyInstantLogs;
            IgnoreSpammyMessages = _configService.Dev.IgnoreSpammyZSpyLogs;

            if (!EnableZSpyLogs)
                ZSpyChannel = -1;
            else if (_configService.Dev.AllDebugChannels)
                ZSpyChannel = int.MaxValue;
            else
                // zSpyChannel are bitwise checks. Therefore, we calculate the bit operation like (1 << PD_TA_FRAME(1) -
                // 1)==1000
                ZSpyChannel = _configService.Dev.ZSpyChannels.Value.Sum(i => 1 << ((int)i - 1));
        }

        /// <summary>
        /// Logs external method calls with input parameters and output values.
        /// </summary>
        public TResult LogInstantExternal<TResult>(string methodName, TResult result, params object[] parameters)
        {
            if (!_zSpyInstantLogging)
                return result;
                
            Logger.Log($"External {methodName}({LogInstantParameter(parameters)}) => {result?.ToString() ?? "void"}",
                LogCat.ZenKit);
            
            return result;
        }

        /// <summary>
        /// Logs external method calls with no return value.
        /// </summary>
        public void LogInstantExternal(string methodName, params object[] parameters)
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

        /// <summary>
        /// Item parameters of externals arrive as whatever instance the VM holds: an item of an old VM or another class
        /// failed the cast in ZenKit's Pop and the external left the VM stack broken until it crashed natively
        /// (Mroczne Tajemnice's giant skeleton by Fellan's armor). Taken as DaedalusInstance and converted here.
        /// </summary>
        public ItemInstance AsItem(DaedalusInstance instance, string external)
        {
            if (instance == null)
                return null;
            if (instance is ItemInstance item)
                return item;
            var converted = _vmCacheService.TryGetItemData(instance.Index);
            LogInstanceMismatch(external, instance, "C_ITEM", converted != null);
            return converted;
        }

        public NpcInstance AsNpc(DaedalusInstance instance, string external)
        {
            if (instance == null)
                return null;
            if (instance is NpcInstance npc)
                return npc;
            LogInstanceMismatch(external, instance, "C_NPC", false);
            return null;
        }

        private void LogInstanceMismatch(string external, DaedalusInstance instance, string expected, bool isConverted)
        {
            if (!_loggedInstanceMismatches.Add($"{external}:{instance.GetType().Name}"))
                return;
            Logger.LogWarning($"[VM] {external}: expected {expected}, got {instance.GetType().Name} (symbol " +
                              $"{instance.Index}) - {(isConverted ? "converted by its symbol" : "skipped")}",
                              LogCat.ZenKit);
        }

        public static void RegisterIfDeclared(VmExternalGuard vm, string name, System.Action register)
        {
            if (vm.GetSymbolByName(name) != null)
                register();
        }

        public bool IsHero(NpcInstance npc)
        {
            return npc != null && _gameStateService.GothicVm.GlobalHero is NpcInstance hero && hero.Index == npc.Index;
        }
    }
}
