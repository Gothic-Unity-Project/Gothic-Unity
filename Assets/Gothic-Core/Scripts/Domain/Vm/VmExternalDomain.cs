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
    /// Contains basic methods only available in Gothic Daedalus module. Registers all externals of the scripts:
    /// the families live in their own domain (Ai, Npc, World, Model, Ui, Utility), this class owns them and
    /// reports the externals nobody implements.
    /// </summary>
    public class VmExternalDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // Unknown externals are reported once each - scripts call some of them on every perception.
        private readonly HashSet<string> _reportedDefaultExternals = new();

        private readonly VmExternalSupport _support;
        private readonly VmAiExternalDomain _aiDomain;
        private readonly VmNpcExternalDomain _npcDomain;
        private readonly VmNpcInventoryExternalDomain _npcInventoryDomain;
        private readonly VmWorldExternalDomain _worldDomain;
        private readonly VmModelExternalDomain _modelDomain;
        private readonly VmUiExternalDomain _uiDomain;
        private readonly VmUtilityExternalDomain _utilityDomain;

        public VmExternalDomain()
        {
            this.Inject();

            _support = new VmExternalSupport();
            _aiDomain = new VmAiExternalDomain(_support);
            _npcDomain = new VmNpcExternalDomain(_support);
            _npcInventoryDomain = new VmNpcInventoryExternalDomain(_support);
            _worldDomain = new VmWorldExternalDomain(_support);
            _modelDomain = new VmModelExternalDomain(_support);
            _uiDomain = new VmUiExternalDomain(_support);
            _utilityDomain = new VmUtilityExternalDomain(_support);
        }

        public void RegisterExternals()
        {
            _support.Init();

            var vm = new VmExternalGuard(_gameStateService.GothicVm);
            vm.RegisterExternalDefault(DefaultExternal);

            _aiDomain.Register(vm);
            _npcDomain.Register(vm);
            _npcInventoryDomain.Register(vm);
            _worldDomain.Register(vm);
            _modelDomain.Register(vm);
            _uiDomain.Register(vm);
            _utilityDomain.Register(vm);
        }

        public void Wld_ExchangeGuildAttitudes(string name)
        {
            _worldDomain.Wld_ExchangeGuildAttitudes(name);
        }

        public void DefaultExternal(DaedalusVm vm, DaedalusSymbol sym)
        {
            // FIXME: Once Gothic is fully released, we can safely throw an exception as it tells us: The game will not
            // work until you implement this missing function.
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
                    var npcName = _multiTypeCacheService.NpcCache
                        .FirstOrDefault(x => x.Instance == selfUserData.Instance)?.Go
                        ?.transform.parent?.name;
                    Logger.LogWarningEditor(
                        $"Method >{sym.Name}< not yet implemented in DaedalusVM (called on >{npcName}<).",
                        LogCat.ZenKit);
                }
            }
            catch (Exception e)
            {
                Logger.LogErrorEditor($"Bug in getting Npc. Fix or delete: {e}", LogCat.ZenKit);
            }
        }
    }
}
