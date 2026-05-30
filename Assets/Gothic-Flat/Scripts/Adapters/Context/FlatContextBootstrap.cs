using System;
using GUZ.Core;
using GUZ.Core.Adapters.Context;
using GUZ.Core.Models.Context;
using ZenKit;

namespace GUZ.Flat.Adapters.Context
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
