using System;
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Services.Game;
using Reflex.Attributes;

namespace Gothic.VR.Adapters.UI.Menus.Settings
{
    /// <summary>
    /// Shared handlers of our VR settings menus (Accessibility, Immersion) - "Restart game" lands here.
    /// </summary>
    public abstract class VRSettingsMenu : AbstractMenu
    {
        [Inject] private readonly GameLifecycleService _gameLifecycleService;

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
            if (commandName == VRConstants.MenuCommandRestartGame)
            {
                _gameLifecycleService.Restart();
                return;
            }

            throw new NotImplementedException();
        }
    }
}
