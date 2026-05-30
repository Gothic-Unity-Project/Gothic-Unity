using GUZ.Core.Extensions;
using GUZ.Core.Model.UI.Menu;
using ZenKit.Daedalus;

namespace GUZ.Core.Model.UI.MenuItem
{
    public abstract class AbstractMenuItemInstance
    {
        public string Name { get; set; }
        public AbstractMenuInstance MenuInstance { get; set; }

        protected AbstractMenuItemInstance(string menuItemName)
        {
            // As we will need it for most of the elements.
            this.Inject();
            Name = menuItemName;
        }
        
        public virtual string FontName { get; set; }
        public virtual string BackPic { get; set; }
        public virtual string AlphaMode { get; set; }
        public virtual int Alpha { get; set; }
        public virtual MenuItemType MenuItemType { get; set; }
        public virtual string OnChgSetOption { get; set; }
        public virtual string OnChgSetOptionSection { get; set; }
        public virtual int PosX { get; set; }
        public virtual int PosY { get; set; }
        public virtual int DimX { get; set; }
        public virtual int DimY { get; set; }
        public virtual float SizeStartScale { get; set; }
        public virtual MenuItemFlag Flags { get; set; }
        public virtual float OpenDelayTime { get; set; }
        public virtual float OpenDuration { get; set; }
        public virtual int FramePosX { get; set; }
        public virtual int FramePosY { get; set; }
        public virtual int FrameSizeX { get; set; }
        public virtual int FrameSizeY { get; set; }
        public virtual string HideIfOptionSectionSet { get; set; }
        public virtual string HideIfOptionSet { get; set; }
        public virtual int HideOnValue { get; set; }
        public abstract string GetText(int i);
        public abstract void SetText(int i, string text);
        public abstract MenuItemSelectAction GetOnSelAction(int i);
        public abstract void SetOnSelAction(int i, MenuItemSelectAction action);
        public abstract string GetOnSelActionS(int i);
        public abstract void SetOnSelActionS(int i, string actionS);
        public abstract int GetOnEventAction(MenuItemEventAction i);
        public abstract float GetUserFloat(int i);
        public abstract void SetUserFloat(int i, float value);
        public abstract string GetUserString(int i);
        public abstract void SetUserString(int i, string text);
    }
}
