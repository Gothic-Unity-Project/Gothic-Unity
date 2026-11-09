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
    /// Daedalus helper externals: Hlp_*, Mob_*, Perc_*, Snd_Play, string/number conversions and the game exit.
    /// </summary>
    public class VmUtilityExternalDomain
    {
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VobService _vobService;

        private readonly VmExternalSupport _support;

        public VmUtilityExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            vm.RegisterExternal<int, int>("Hlp_Random", Hlp_Random);
            vm.RegisterExternal<int, string, string>("Hlp_StrCmp", Hlp_StrCmp);
            vm.RegisterExternal<int, DaedalusInstance, int>("Hlp_IsItem",
                (i, index) => _support.AsItem(i, "Hlp_IsItem") is { } item ? Hlp_IsItem(item, index) : 0);
            vm.RegisterExternal<int, DaedalusInstance>("Hlp_IsValidItem",
                i => _support.AsItem(i, "Hlp_IsValidItem") is { } item ? Hlp_IsValidItem(item) : 0);
            vm.RegisterExternal<int, NpcInstance>("Hlp_IsValidNpc", Hlp_IsValidNpc);
            vm.RegisterExternal<NpcInstance, int>("Hlp_GetNpc", Hlp_GetNpc);
            vm.RegisterExternal<int, DaedalusInstance>("Hlp_GetInstanceID", Hlp_GetInstanceID);
            vm.RegisterExternal<int, string, int>("Mob_HasItems", Mob_HasItems);
            vm.RegisterExternal<string>("Snd_Play", Snd_Play);
            vm.RegisterExternal<int, int>("Perc_SetRange", Perc_SetRange);
            vm.RegisterExternal<string, string, string>("ConcatStrings", ConcatStrings);
            vm.RegisterExternal<string, int>("IntToString", IntToString);
            vm.RegisterExternal<string, float>("FloatToString", FloatToString);
            vm.RegisterExternal<int, float>("FloatToInt", FloatToInt);
            vm.RegisterExternal<float, int>("IntToFloat", IntToFloat);
            // Endings: G1 DIA_EXTRO_AVI (Extro/PlayerOut/Credits videos + ExitGame), G2 ExitSession.
            RegisterExitExternal(vm, "ExitGame");
            RegisterExitExternal(vm, "ExitSession");
        }

        //



        //



        public int Hlp_Random(int n0)
        {
            var rand = Random.Range(0, n0 - 1);
            return _support.LogInstantExternal(nameof(Hlp_Random), rand, n0);
        }

        public int Hlp_StrCmp(string s1, string s2)
        {
            var cmpValue = s1 == s2 ? 1 : 0;
            return _support.LogInstantExternal(nameof(Hlp_StrCmp), cmpValue, s1, s2);
        }

        public int Hlp_IsItem(ItemInstance item, int itemIndexToCheck)
        {
            if (item == null)
                return 0;
            
            var ret = Convert.ToInt32(item.Index == itemIndexToCheck);
            return _support.LogInstantExternal(nameof(Hlp_IsItem), ret);
        }

        public int Hlp_IsValidItem(ItemInstance item)
        {
            var ret = Convert.ToInt32(item != null);
            return _support.LogInstantExternal(nameof(Hlp_IsValidItem), ret, item);
        }

        public int Hlp_IsValidNpc(NpcInstance npc)
        {
            var ret = Convert.ToInt32(npc != null);
            return _support.LogInstantExternal(nameof(Hlp_IsValidNpc), ret, npc);
        }

        public NpcInstance Hlp_GetNpc(int instanceId)
        {
            var npc = _npcService.ExtHlpGetNpc(instanceId);
            return _support.LogInstantExternal(nameof(Hlp_GetNpc), npc, instanceId);
        }

        public int Hlp_GetInstanceID(DaedalusInstance instance)
        {
            if (instance == null)
            {
                return _support.LogInstantExternal(nameof(Hlp_GetInstanceID), -1, instance);
            }

            var ret = instance.Index;
            return _support.LogInstantExternal(nameof(Hlp_GetInstanceID), ret, instance);
        }

        private void RegisterExitExternal(VmExternalGuard vm, string name)
        {
            var symbol = vm.GetSymbolByName(name);
            if (symbol == null)
                return;

            if (symbol.HasReturn)
                vm.RegisterExternal<int>(name, () =>
                {
                    ExitGame(name);
                    return 0;
                });
            else
                vm.RegisterExternal(name, () => ExitGame(name));
        }

        private void ExitGame(string externalName)
        {
            Logger.Log($"[{externalName}] The game ends after the queued videos.", LogCat.Dialog);
            GlobalEventDispatcher.ScriptExitGame.Invoke();
        }

        public void Snd_Play(string soundName)
        {
            _audioService.PlaySound2D(soundName);
        }

        //



        //



        //



        public int Mob_HasItems(string mobName, int itemInstance)
        {
            var count = _vobService.ExtMobHasItems(mobName, itemInstance);
            return _support.LogInstantExternal(nameof(Mob_HasItems), count, mobName, itemInstance);
        }

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
    }
}
