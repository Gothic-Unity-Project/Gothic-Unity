using GUZ.Core.Extensions;
using GUZ.Core.Model.UI.Menu;
using ZenKit.Daedalus;

namespace GUZ.Core.Model.UI.MenuItem
{
    public class MenuItemInstanceAdapter : AbstractMenuItemInstance
    {
        private readonly MenuItemInstance _menuItemInstance;
        
        public MenuItemInstanceAdapter(MenuItemInstance menuItemInstance, string menuItemName, AbstractMenuInstance parentAbstractMenu):
            base(menuItemName)
        {
            _menuItemInstance = menuItemInstance;

            if (_menuItemInstance.GetOnSelAction(0) == MenuItemSelectAction.StartMenu)
            {
                MenuInstance = new MenuInstanceAdapter(_menuItemInstance.GetOnSelActionS(0), parentAbstractMenu);
            }
        }


        public override string FontName
        {
            get => _menuItemInstance.FontName;
            set => _menuItemInstance.FontName = value;
        }

        public override string BackPic
        {
            get => _menuItemInstance.BackPic;
            set => _menuItemInstance.BackPic = value;
        }

        public override string AlphaMode
        {
            get => _menuItemInstance.AlphaMode;
            set => _menuItemInstance.AlphaMode = value;
        }

        public override int Alpha
        {
            get => _menuItemInstance.Alpha;
            set => _menuItemInstance.Alpha = value;
        }

        public override  MenuItemType MenuItemType
        {
            get => _menuItemInstance.MenuItemType;
            set => _menuItemInstance.MenuItemType = value;
        }

        public override  string OnChgSetOption
        {
            get => _menuItemInstance.OnChgSetOption;
            set => _menuItemInstance.OnChgSetOption = value;
        }

        public override  string OnChgSetOptionSection
        {
            get => _menuItemInstance.OnChgSetOptionSection;
            set => _menuItemInstance.OnChgSetOptionSection = value;
        }

        public override  int PosX
        {
            get => _menuItemInstance.PosX;
            set => _menuItemInstance.PosX = value;
        }

        public override  int PosY
        {
            get => _menuItemInstance.PosY;
            set => _menuItemInstance.PosY = value;
        }

        public override  int DimX
        {
            get => _menuItemInstance.DimX;
            set => _menuItemInstance.DimX = value;
        }

        public override  int DimY
        {
            get => _menuItemInstance.DimY;
            set => _menuItemInstance.DimY = value;
        }

        public override  float SizeStartScale
        {
            get => _menuItemInstance.SizeStartScale;
            set => _menuItemInstance.SizeStartScale = value;
        }

        public override  MenuItemFlag Flags
        {
            get => _menuItemInstance.Flags;
            set => _menuItemInstance.Flags = value;
        }

        public override  float OpenDelayTime
        {
            get => _menuItemInstance.OpenDelayTime;
            set => _menuItemInstance.OpenDelayTime = value;
        }

        public override  float OpenDuration
        {
            get => _menuItemInstance.OpenDuration;
            set => _menuItemInstance.OpenDuration = value;
        }

        public override  int FramePosX
        {
            get => _menuItemInstance.FramePosX;
            set => _menuItemInstance.FramePosX = value;
        }

        public override  int FramePosY
        {
            get => _menuItemInstance.FramePosY;
            set => _menuItemInstance.FramePosY = value;
        }

        public override  int FrameSizeX
        {
            get => _menuItemInstance.FrameSizeX;
            set => _menuItemInstance.FrameSizeX = value;
        }

        public override  int FrameSizeY
        {
            get => _menuItemInstance.FrameSizeY;
            set => _menuItemInstance.FrameSizeY = value;
        }

        public override  string HideIfOptionSectionSet
        {
            get => _menuItemInstance.HideIfOptionSectionSet;
            set => _menuItemInstance.HideIfOptionSectionSet = value;
        }

        public override  string HideIfOptionSet
        {
            get => _menuItemInstance.HideIfOptionSet;
            set => _menuItemInstance.HideIfOptionSet = value;
        }

        public override  int HideOnValue
        {
            get => _menuItemInstance.HideOnValue;
            set => _menuItemInstance.HideOnValue = value;
        }

        public override string GetText(int i)
        {
            return _menuItemInstance.GetText(i);
        }

        public override void SetText(int i, string text)
        {
            _menuItemInstance.SetText(i, text);
        }

        public override MenuItemSelectAction GetOnSelAction(int i)
        {
            return _menuItemInstance.GetOnSelAction(i);
        }

        public override void SetOnSelAction(int i, MenuItemSelectAction action)
        {
            _menuItemInstance.SetOnSelAction(i, (int)action);
        }

        public override string GetOnSelActionS(int i)
        {
            return _menuItemInstance.GetOnSelActionS(i);
        }

        public override void SetOnSelActionS(int i, string actionS)
        {
            _menuItemInstance.SetOnSelActionS(i, actionS);
        }

        public override int GetOnEventAction(MenuItemEventAction i)
        {
            return _menuItemInstance.GetOnEventAction(i);
        }

        public override float GetUserFloat(int i)
        {
            return _menuItemInstance.GetUserFloat(i);
        }

        public override void SetUserFloat(int i, float value)
        {
            _menuItemInstance.SetUserFloat(i, value);
        }

        public override string GetUserString(int i)
        {
            return _menuItemInstance.GetUserString(i);
        }

        public override void SetUserString(int i, string text)
        {
            _menuItemInstance.SetUserString(i, text);
        }
    }
}
