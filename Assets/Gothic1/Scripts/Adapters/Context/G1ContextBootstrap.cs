using Gothic.Core.Adapters.Context;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Services;
using Gothic.G1.Services.Fight;
using Reflex.Attributes;
using ZenKit;

namespace Gothic.G1.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class G1ContextBootstrap : AbstractContextBootstrap
    {
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ContextMeleeDamageRuleService _contextMeleeDamageRuleService;
        [Inject] private readonly GameStateService _gameStateService;

        private G1MeleeDamageRule _meleeDamageRule;

        protected override void RegisterControlModule(Controls controls)
        {
            // NOP
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            if (version != GameVersion.Gothic1)
                return;

            _contextGameVersionService.SetImpl(new G1ContextService().Inject());

            _meleeDamageRule = new G1MeleeDamageRule().Inject();
            _contextMeleeDamageRuleService.SetImpl(_meleeDamageRule);
            // The scripts are known once ZenKit is booted - only then we know if a mod brings its percent system.
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(RegisterModMeleeDamageRule);
        }

        private void RegisterModMeleeDamageRule()
        {
            if (!G1ModPercentTalentMeleeDamageRule.IsUsedBy(_gameStateService.GothicVm))
                return;

            _contextMeleeDamageRuleService.SetImpl(
                new G1ModPercentTalentMeleeDamageRule(_meleeDamageRule).Inject());
        }
    }
}
