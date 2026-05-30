using GUZ.Core.Model.UI.Menu;

namespace GUZ.Core.Services.Context
{
    public class ContextMenuService : IContextMenuService
    {
        private IContextMenuService _impl;

        public void SetImpl(IContextMenuService impl)
        {
            _impl = impl;
        }

        public T GetImpl<T>() where T : IContextMenuService
        {
            return (T)_impl;
        }

        public void UpdateMainMenu(AbstractMenuInstance mainMenu)
        {
            _impl.UpdateMainMenu(mainMenu);
        }
    }
}
