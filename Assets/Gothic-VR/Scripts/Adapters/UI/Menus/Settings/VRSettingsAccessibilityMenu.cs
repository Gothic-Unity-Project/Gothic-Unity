using System;
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Domain;

namespace Gothic.VR.Adapters.UI.Menus.Settings
{
    public class VRSettingsAccessibilityMenu : AbstractMenu
    {
        protected override void Undefined(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void StartMenu(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void StartItem(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void Close(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void ConsoleCommand(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void PlaySound(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void ExecuteCommand(string itemName, string commandName)
        {
            // The Immersion settings prefab uses this class too - "Restart game" lands here.
            if (commandName == VRConstants.MenuCommandRestartGame)
            {
                GameRestartDomain.Restart();
                return;
            }

            throw new NotImplementedException();
        }
    }
}
