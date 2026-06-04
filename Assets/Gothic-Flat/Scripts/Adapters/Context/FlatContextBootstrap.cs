using System;
using Gothic.Core.Adapters.Context;
using Gothic.Core.Models.Context;
using Gothic.Core;
using ZenKit;

namespace Gothic.Flat.Adapters.Context
{
    /// <summary>
    /// Bootstrap class which will register listener to set this module as Active if GameSettings.Controls match.
    /// </summary>
    public class FlatContextBootstrap : AbstractContextBootstrap
    {
        protected override void RegisterControlModule(Controls controls)
        {
            if (controls != Controls.Flat)
                return;

            throw new NotImplementedException("FlatContext needs rework.");
        }

        protected override void RegisterGameVersionModule(GameVersion version)
        {
            // NOP
        }
    }
}
