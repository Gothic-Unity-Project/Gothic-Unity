using System;
using System.Linq;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Services;
using Gothic.Core.Const;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace Gothic.Core.Model.UI.Menu
{
    public class MenuInstanceAdapter : AbstractMenuInstance
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        // Gothic scripts: _intern/Menu.d CONST INT MAX_ITEMS = 150 (G1 + G2).
        private const int _maxMenuItems = 150;

        private MenuInstance _menuInstance;

        public MenuInstanceAdapter(string name, [CanBeNull] AbstractMenuInstance parentAbstractMenu): base(name, parentAbstractMenu)
        {
            _menuInstance = _gameStateService.MenuVm.InitInstance<MenuInstance>(Name);
            
            // We immediately initialize all menu entries as we will later change Index of them (e.g. add a new menu in between).
            Items = new();
            // Like the engine: walk the whole C_MENU_DEF.items[MAX_ITEMS] array and skip empty entries.
            // Scripts can have gaps (e.g. Mroczne Tajemnice's MENU_STATUS comments out items[33]) - stopping at the first
            // empty entry dropped every item after it (all status menu talent rows except the first).
            for (var i = 0; i < _maxMenuItems; i++)
            {
                string itemName;
                try
                {
                    itemName = _menuInstance.GetItem(i);
                }
                catch (Exception)
                {
                    break; // Past the array's end.
                }

                if (itemName.IsNullOrEmpty())
                    continue;

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
