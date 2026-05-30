using System;
using GUZ.Core.Const;
using GUZ.Core.Services.Vm;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace GUZ.Core.Model.UI.MenuItem
{
    public class MutableMenuItemInstance : AbstractMenuItemInstance
    {
        [Inject] private readonly VmService _vmService;
        
        /// <summary>
        /// Copy constructor. Mostly copying dimensions and look.
        /// </summary>
        public MutableMenuItemInstance(string menuItemName, AbstractMenuItemInstance reference): base(menuItemName)
        {
            FontName = reference.FontName;
            BackPic = reference.BackPic;
            AlphaMode = reference.AlphaMode;
            Alpha = reference.Alpha;
            MenuItemType = reference.MenuItemType;
            OnChgSetOption = reference.OnChgSetOption;
            OnChgSetOptionSection = reference.OnChgSetOptionSection;
            PosX = reference.PosX;
            PosY = reference.PosY;
            DimX = reference.DimX;
            DimY = reference.DimY;
            SizeStartScale = reference.SizeStartScale;
            Flags = reference.Flags;
            OpenDelayTime = reference.OpenDelayTime;
            OpenDuration = reference.OpenDuration;
            FramePosX = reference.FramePosX;
            FramePosY = reference.FramePosY;
            FrameSizeX = reference.FrameSizeX;
            FrameSizeY = reference.FrameSizeY;
            HideIfOptionSectionSet = reference.HideIfOptionSectionSet;
            HideIfOptionSet = reference.HideIfOptionSet;
            HideOnValue = reference.HideOnValue;
            
            // for Slider: .tga of slider button
            SetUserString(0, reference.GetUserString(0));
        }

        private string[] _texts;
        public override string GetText(int i)
        {
            if (_texts == null)
                _texts = new string[_vmService.MaxUserStrings];
            
            return _texts[i];
        }

        public override void SetText(int i, string text)
        {
            if (_texts == null)
                _texts = new string[_vmService.MaxUserStrings];
            
            _texts[i] = text;
        }

        private MenuItemSelectAction[] _onSelActions;
        public override MenuItemSelectAction GetOnSelAction(int i)
        {
            if (_onSelActions == null)
                _onSelActions = new MenuItemSelectAction[ _vmService.MaxSelActions];
            
            return _onSelActions[i];
        }

        public override void SetOnSelAction(int i, MenuItemSelectAction action)
        {
            if (_onSelActions == null)
                _onSelActions = new MenuItemSelectAction[ _vmService.MaxSelActions];
            _onSelActions[i] = action;
        }

        private string[] _onSelActionStrings;
        public override string GetOnSelActionS(int i)
        {
            if (_onSelActionStrings == null)
                _onSelActionStrings = new string[_vmService.MaxSelActions];
            
            return _onSelActionStrings[i];
        }

        public override void SetOnSelActionS(int i, string actionS)
        {
            if (_onSelActionStrings == null)
                _onSelActionStrings = new string[_vmService.MaxSelActions];
            
            _onSelActionStrings[i] = actionS;
        }

        public override int GetOnEventAction(MenuItemEventAction i)
        {
            throw new NotImplementedException();
        }

        private float[] _userFloats;
        public override float GetUserFloat(int i)
        {
            if (_userFloats == null)
                _userFloats = new float[_vmService.MaxUserVars];
            
            return _userFloats[i];
        }

        public override void SetUserFloat(int i, float value)
        {
            if (_userFloats == null)
                _userFloats = new float[_vmService.MaxUserVars];
            
            _userFloats[i] = value;
        }

        private string[] _userStrings;
        public override string GetUserString(int i)
        {
            if (_userStrings == null)
                _userStrings = new string[_vmService.MaxUserVars];
            
            return _userStrings[i];
        }

        public override void SetUserString(int i, string text)
        {
            if (_userStrings == null)
                _userStrings = new string[_vmService.MaxUserVars];
            
            _userStrings[i] = text;
        }
    }
}
