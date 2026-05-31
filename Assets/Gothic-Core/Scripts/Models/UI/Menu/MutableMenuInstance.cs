using System;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Services.Vm;
using Gothic.Core.Const;
using Gothic.Core.Services;
using JetBrains.Annotations;
using Reflex.Attributes;

namespace Gothic.Core.Model.UI.Menu
{
    public class MutableMenuInstance : AbstractMenuInstance
    {
        [Inject] private readonly VmService _vmService;

        
        public MutableMenuInstance(string name, [CanBeNull] AbstractMenuInstance parentMenu)
            : base(name, parentMenu)
        {
            BackPic =  _vmService.BackPic;
            Items = new();
            DimX = 8191; // Taken from PROTOTYPE C_MENU_DEF
            DimY = 8191; // Taken from PROTOTYPE C_MENU_DEF
        }

        public AbstractMenuItemInstance GetMenuItemInstance(string menuItemName)
        {
            throw new NotImplementedException();
        }

        public AbstractMenuInstance FindSubMenu(string subMenuName)
        {
            throw new NotImplementedException();
        }

        public override string GetItem(int i)
        {
            throw new NotImplementedException();
        }
    }
}
