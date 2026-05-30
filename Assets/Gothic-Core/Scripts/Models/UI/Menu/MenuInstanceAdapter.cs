using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Model.UI.MenuItem;
using GUZ.Core.Services;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace GUZ.Core.Model.UI.Menu
{
    public class MenuInstanceAdapter : AbstractMenuInstance
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        private MenuInstance _menuInstance;

        public MenuInstanceAdapter(string name, [CanBeNull] AbstractMenuInstance parentAbstractMenu): base(name, parentAbstractMenu)
        {
            _menuInstance = _gameStateService.MenuVm.InitInstance<MenuInstance>(Name);
            
            // We immediately initialize all menu entries as we will later change Index of them (e.g. add a new menu in between).
            Items = new();
            for (var i = 0;; i++)
            {
                var itemName = _menuInstance.GetItem(i);

                // We passed the last element.
                if (itemName.IsNullOrEmpty())
                    break;

                var instance = _gameStateService.MenuVm.InitInstance<MenuItemInstance>(itemName);
                Items.Add(new MenuItemInstanceAdapter(instance, itemName, this));
            }
        }

        public void InsertItemAt(int index, AbstractMenuItemInstance menuItemInstance)
        {
            Items.Insert(index, menuItemInstance);
        }
        
        public AbstractMenuItemInstance GetMenuItemInstance(string menuItemName)
        {
            return Items.First(i => i.Name == menuItemName);
        }

        public override string GetItem(int i)
        {
            return _menuInstance.GetItem(i);
        }

        public override string BackPic
        {
            get => _menuInstance.BackPic;
            set => _menuInstance.BackPic = value;
        }

        public override string BackWorld
        {
            get => _menuInstance.BackWorld;
            set => _menuInstance.BackWorld = value;
        }

        public override int PosX
        {
            get => _menuInstance.PosX;
            set => _menuInstance.PosX = value;
        }

        public override int PosY
        {
            get => _menuInstance.PosY;
            set => _menuInstance.PosY = value;
        }

        public override int DimX
        {
            get => _menuInstance.DimX;
            set => _menuInstance.DimX = value;
        }

        public override int DimY
        {
            get => _menuInstance.DimY;
            set => _menuInstance.DimY = value;
        }

        public override int Alpha
        {
            get => _menuInstance.Alpha;
            set => _menuInstance.Alpha = value;
        }

        public override string MusicTheme
        {
            get => _menuInstance.MusicTheme;
            set => _menuInstance.MusicTheme = value;
        }

        public override int EventTimerMsec
        {
            get => _menuInstance.EventTimerMsec;
            set => _menuInstance.EventTimerMsec = value;
        }

        public override MenuFlag Flags
        {
            get => _menuInstance.Flags;
            set => _menuInstance.Flags = value;
        }

        public override int DefaultOutGame
        {
            get => _menuInstance.DefaultOutGame;
            set => _menuInstance.DefaultOutGame = value;
        }

        public override int DefaultInGame
        {
            get => _menuInstance.DefaultInGame;
            set => _menuInstance.DefaultInGame = value;
        }
    }
}
