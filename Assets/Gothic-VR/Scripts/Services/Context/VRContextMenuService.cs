#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Vm;
using Gothic.Services.UI;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using HurricaneVR.Framework.Core.Player;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Services.Context
{
    public class VRContextMenuService : IContextMenuService
    {
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly VmService _vmService;
        
        
        /// <summary>
        /// Update menu entries based on VR needs.
        /// --> Replace control menu with VR settings menu
        /// </summary>
        public void UpdateMainMenu(AbstractMenuInstance mainMenu)
        {
            var vrAccessibilityMenu = CreateAccessibilityMenu(mainMenu);

            // Will be used to clone render settings from its items.
            var gameMenu = mainMenu.FindMenuRecursive("MENU_OPT_GAME")!;

            var menuStartY =  _vmService.MenuStartY;
            var menuDY =  _vmService.MenuDY;
            
            vrAccessibilityMenu.Items.Add(CreateAccessibilityHeadline(gameMenu));

            vrAccessibilityMenu.Items.Add(CreateSitStandLabel(gameMenu, menuStartY + menuDY * 0));
            vrAccessibilityMenu.Items.Add(CreateSitStandChoicebox(gameMenu, menuStartY + menuDY * 0));
            vrAccessibilityMenu.Items.Add(CreateMoveDirectionLabel(gameMenu, menuStartY + menuDY * 1));
            vrAccessibilityMenu.Items.Add(CreateMoveDirectionChoicebox(gameMenu, menuStartY + menuDY * 1));
            vrAccessibilityMenu.Items.Add(CreateRotationTypeLabel(gameMenu, menuStartY + menuDY * 2));
            vrAccessibilityMenu.Items.Add(CreateRotationTypeChoicebox(gameMenu, menuStartY + menuDY * 2));
            vrAccessibilityMenu.Items.Add(CreateSmoothRotationSpeedLabel(gameMenu, menuStartY + menuDY * 3));
            vrAccessibilityMenu.Items.Add(CreateSmoothRotationSpeedSlider(gameMenu, menuStartY + menuDY * 3));
            vrAccessibilityMenu.Items.Add(CreateSnapRotationLabel(gameMenu, menuStartY + menuDY * 4));
            vrAccessibilityMenu.Items.Add(CreateSnapRotationChoicebox(gameMenu, menuStartY + menuDY * 4));
            vrAccessibilityMenu.Items.Add(CreateSmoothSpectatorLabel(gameMenu, menuStartY + menuDY * 5));
            vrAccessibilityMenu.Items.Add(CreateSmoothSpectatorChoicebox(gameMenu, menuStartY + menuDY * 5));

            vrAccessibilityMenu.Items.Add(CreateAccessibilityBackButton(mainMenu));
            

            // FIXME - Add - Setting: ItemCollisionWhileDragged
            var vrImmersionMenu = CreateImmersionMenu(mainMenu);
            vrImmersionMenu.Items.Add(CreateImmersionHeadline(gameMenu));
            vrImmersionMenu.Items.Add(CreateMicrophoneLabel(gameMenu, menuStartY + menuDY * 0));
            vrImmersionMenu.Items.Add(CreateMicrophoneChoice(gameMenu, menuStartY + menuDY * 0));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HIT_NUMBERS", "Hit numbers",
                "-X HP / +X MANA over NPC heads", menuStartY + menuDY * 1));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HIT_NUMBERS", Constants.IniNames.HitNumbers,
                menuStartY + menuDY * 1));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HIT_NUMBERS_SIZE", "Hit numbers size",
                "Size of the -X HP / +X MANA numbers", menuStartY + menuDY * 2));
            vrImmersionMenu.Items.Add(CreateHitNumbersSizeSlider(gameMenu, menuStartY + menuDY * 2));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HEALTH_NUMBERS", "HP numbers",
                "HP/MAX on health bars", menuStartY + menuDY * 3));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HEALTH_NUMBERS", Constants.IniNames.HealthNumbers,
                menuStartY + menuDY * 3));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "MARVIN", "Marvin",
                "Show the Marvin panel (needs Marvin mode)", menuStartY + menuDY * 4));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "MARVIN", VRConstants.IniNames.MarvinPanel,
                menuStartY + menuDY * 4, 1));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "LIGHT_REFRESH", "Light refresh",
                "How smoothly a torch's light follows your hand", menuStartY + menuDY * 5));
            vrImmersionMenu.Items.Add(CreateLightRefreshChoice(gameMenu, menuStartY + menuDY * 5));
            vrImmersionMenu.Items.Add(CreateRestartButton(mainMenu, menuStartY + menuDY * 6));
            vrImmersionMenu.Items.Add(CreateImmersionBackButton(mainMenu));
        }

        // Our own options have no localization entries yet - plain English labels.
        private MutableMenuItemInstance CreateToggleLabel(AbstractMenuInstance gameMenu, string id, string label,
            string description, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var item = new MutableMenuItemInstance($"MENUITEM_GOTHIC_OPT_VR_IMMERSION_{id}", subtitlesLabel);

            item.PosY = posY;
            item.SetText(0, label);
            item.SetText(1, description);

            return item;
        }

        private MutableMenuItemInstance CreateToggleChoice(AbstractMenuInstance gameMenu, string id, string iniName,
            int posY, int defaultValue = 0)
        {
            // Gothic's own subtitles choice gives the "no|yes" values in the game's language.
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var item = new MutableMenuItemInstance($"MENUITEM_GOTHIC_OPT_VR_IMMERSION_{id}_CHOICE", subtitlesChoice);

            item.PosY = posY;
            item.SetUserFloat(3, defaultValue); // 0 = off, 1 = on if no INI value exists.
            item.SetText(0, subtitlesChoice?.GetText(0) ?? "Off|On");
            item.OnChgSetOption = iniName;
            item.OnChgSetOptionSection = VRConstants.IniSectionImmersion;

            return item;
        }

        private MutableMenuItemInstance CreateLightRefreshChoice(AbstractMenuInstance gameMenu, int posY)
        {
            var item = CreateToggleChoice(gameMenu, "LIGHT_REFRESH", Constants.IniNames.LightRefresh, posY);
            item.SetText(0, "Low|Medium|High");
            return item;
        }

        private AbstractMenuItemInstance CreateHitNumbersSizeSlider(AbstractMenuInstance gameMenu, int posY)
        {
            var mouseSlider = gameMenu.FindMenuItem("MENUITEM_MSENSITIVITY_SLIDER", out _);
            var slider = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_HIT_NUMBERS_SIZE_SLIDER",
                mouseSlider);

            slider.PosY = posY;
            slider.SetUserFloat(0, VRConstants.SmoothRotationSettingAmount); // Steps.
            // Default = smallest. AbstractMenu turns a default of exactly 0 into 1 (full), hence the tiny value.
            slider.SetUserFloat(3, 0.0001f);
            slider.OnChgSetOption = Constants.IniNames.HitNumbersSize;
            slider.OnChgSetOptionSection = VRConstants.IniSectionImmersion;

            return slider;
        }

        /// <summary>
        /// Starts the game again (GameRestartDomain) - e.g. to pick another game profile.
        /// </summary>
        private MutableMenuItemInstance CreateRestartButton(AbstractMenuInstance mainMenu, int posY)
        {
            var someOptionsMenu = mainMenu.FindMenuRecursive("MENU_OPT_GRAPHICS");
            var buttonReference = someOptionsMenu?.FindMenuItem("MENUITEM_GRA_BACK", out _);
            var restartButton = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_RESTART", buttonReference);

            restartButton.PosY = posY;
            restartButton.SetText(0, "Restart game");
            restartButton.SetOnSelAction(0, MenuItemSelectAction.ExecuteCommand);
            restartButton.SetOnSelActionS(0, VRConstants.MenuCommandRestartGame);

            return restartButton;
        }

        private MutableMenuInstance CreateAccessibilityMenu(AbstractMenuInstance mainMenu)
        {
            // Find MENU_OPT_AUDIO menu item to overwrite
            var controlsMenuParent = mainMenu.FindMenuRecursive("MENU_OPT_AUDIO")!.Parent;
            var controlsMenuItem = controlsMenuParent.FindMenuItem("MENUITEM_OPT_CONTROLS", out var controlsItemIndex);
            
            // Create empty menu
            var vrAccessibilityMenu = new MutableMenuInstance("MENU_GOTHIC_OPT_VR_ACCESSIBILITY", controlsMenuParent);
            
            // Create menu item and replace it where >Keyboard< settings are normally
            var vrAccessibilityMenuItem = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY", controlsMenuItem);
            controlsMenuParent.ReplaceItemAt(controlsItemIndex, vrAccessibilityMenuItem);

            // Add some setting
            vrAccessibilityMenuItem.SetText(0, _localizationService.GetText("menuitem.vr_accessibility"));
            vrAccessibilityMenuItem.SetOnSelAction(0, MenuItemSelectAction.StartMenu);
            vrAccessibilityMenuItem.SetOnSelActionS(0, "MENU_GOTHIC_OPT_VR_ACCESSIBILITY");
            vrAccessibilityMenuItem.MenuInstance = vrAccessibilityMenu;

            return vrAccessibilityMenu;
        }

        private MutableMenuItemInstance CreateAccessibilityHeadline(AbstractMenuInstance gameMenu)
        {
            var gameHeadline = gameMenu.FindMenuItem("MENUITEM_GAME_HEADLINE", out _);
            var vrHeadline = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_HEADLINE", gameHeadline);
            vrHeadline.SetText(0, _localizationService.GetText("menuitem.vr_accessibility.headline"));
            
            return vrHeadline;
        }
        
        private AbstractMenuItemInstance CreateSitStandChoicebox(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var sitStandLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SIT_STAND", subtitlesLabel);
            
            sitStandLabel.PosY = posY;
            
            sitStandLabel.SetText(0, _localizationService.GetText("menuitem.sitStand.label"));
            sitStandLabel.SetText(1, _localizationService.GetText("menuitem.sitStand.description"));
            
            return sitStandLabel;
        }

        private AbstractMenuItemInstance CreateSitStandLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var sitStandSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SIT_STAND_CHOICE", subtitlesChoice);

            sitStandSetting.PosY = posY;
            sitStandSetting.SetUserFloat(3, (int)HVRSitStand.PlayerHeight); // Default value if no INI value exists.

            sitStandSetting.SetText(0, _localizationService.GetText("menuitem.sitStand.value"));
            sitStandSetting.OnChgSetOption = VRConstants.IniNames.SitStand;
            sitStandSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return sitStandSetting;
        }
        
        private AbstractMenuItemInstance CreateMoveDirectionLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var moveDirectionLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_MOVE_DIRECTION", subtitlesLabel);
            
            moveDirectionLabel.PosY = posY;
            
            moveDirectionLabel.SetText(0, _localizationService.GetText("menuitem.moveDirection.label"));
            moveDirectionLabel.SetText(1, _localizationService.GetText("menuitem.moveDirection.description"));
            
            return moveDirectionLabel;
        }
        
        private AbstractMenuItemInstance CreateMoveDirectionChoicebox(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var moveDirectionSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_MOVE_DIRECTION_CHOICE", subtitlesChoice);

            moveDirectionSetting.PosY = posY;

            moveDirectionSetting.SetText(0, _localizationService.GetText("menuitem.moveDirection.value"));
            moveDirectionSetting.OnChgSetOption = VRConstants.IniNames.MoveDirection;
            moveDirectionSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return moveDirectionSetting;
        }
        
        private AbstractMenuItemInstance CreateRotationTypeLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var rotationTypeLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_ROTATION_TYPE", subtitlesLabel);
            
            rotationTypeLabel.PosY = posY;
            
            rotationTypeLabel.SetText(0, _localizationService.GetText("menuitem.rotationType.label"));
            rotationTypeLabel.SetText(1, _localizationService.GetText("menuitem.rotationType.description"));
            
            return rotationTypeLabel;
        }

        private AbstractMenuItemInstance CreateRotationTypeChoicebox(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var rotationTypeSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_ROTATION_TYPE_CHOICE", subtitlesChoice);

            rotationTypeSetting.PosY = posY;
            rotationTypeSetting.SetUserFloat(3, (int)RotationType.Snap); // Default value if no INI value exists.

            rotationTypeSetting.SetText(0, _localizationService.GetText("menuitem.rotationType.value"));
            rotationTypeSetting.OnChgSetOption = VRConstants.IniNames.RotationType;
            rotationTypeSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return rotationTypeSetting;
        }

        private AbstractMenuItemInstance CreateSmoothRotationSpeedLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothRotationLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTH_ROTATION", subtitlesLabel);
            
            smoothRotationLabel.PosY = posY;
            
            smoothRotationLabel.SetText(0, _localizationService.GetText("menuitem.smoothRotation.label"));
            smoothRotationLabel.SetText(1, _localizationService.GetText("menuitem.smoothRotation.description"));
            
            return smoothRotationLabel;
        }
        
        private AbstractMenuItemInstance CreateSmoothRotationSpeedSlider(AbstractMenuInstance gameMenu, int posY)
        {
            var mouseSlider = gameMenu.FindMenuItem("MENUITEM_MSENSITIVITY_SLIDER", out _);
            var smoothRotationSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTH_ROTATION_SLIDER", mouseSlider);

            smoothRotationSetting.PosY = posY;
            smoothRotationSetting.SetUserFloat(3, VRConstants.SmoothRotationDefaultValue); // Default value if no INI value exists.

            smoothRotationSetting.SetUserFloat(0, VRConstants.SmoothRotationSettingAmount);
            smoothRotationSetting.OnChgSetOption = VRConstants.IniNames.SmoothRotationSpeed;
            smoothRotationSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return smoothRotationSetting;
        }
        
        private AbstractMenuItemInstance CreateSnapRotationLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothRotationLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SNAP_ROTATION", subtitlesLabel);
            
            smoothRotationLabel.PosY = posY;
            
            smoothRotationLabel.SetText(0, _localizationService.GetText("menuitem.snapRotation.label"));
            smoothRotationLabel.SetText(1, _localizationService.GetText("menuitem.snapRotation.description"));
            
            return smoothRotationLabel;
        }
        
        private AbstractMenuItemInstance CreateSnapRotationChoicebox(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var snapRotationSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SNAP_ROTATION_CHOICE", subtitlesChoice);

            snapRotationSetting.PosY = posY;
            snapRotationSetting.SetUserFloat(3, VRConstants.SnapRotationDefaultValue); // Default value if no INI value exists.

            snapRotationSetting.SetText(0, _localizationService.GetText("menuitem.snapRotation.value"));
            snapRotationSetting.OnChgSetOption = VRConstants.IniNames.SnapRotationAmount;
            snapRotationSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return snapRotationSetting;
        }

        private MutableMenuItemInstance CreateSmoothSpectatorLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothingLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTHSPECTATOR", subtitlesLabel);
            
            smoothingLabel.PosY = posY;
            
            smoothingLabel.SetText(0, _localizationService.GetText("menuitem.smooth.label"));
            smoothingLabel.SetText(1, _localizationService.GetText("menuitem.smooth.description"));
            
            return smoothingLabel;
        }
        
        private MutableMenuItemInstance CreateSmoothSpectatorChoicebox(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var smoothingSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTHSPECTATOR_CHOICE", subtitlesChoice);

            smoothingSetting.PosY = posY;

            smoothingSetting.SetText(0, _localizationService.GetText("menuitem.smooth.value"));
            smoothingSetting.OnChgSetOption = VRConstants.IniNames.SmoothSpectator;
            smoothingSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return smoothingSetting;
        }

        private MutableMenuItemInstance CreateAccessibilityBackButton(AbstractMenuInstance mainMenu)
        {
            // A mod can rename/restructure MENU_OPT_GRAPHICS itself (not just items inside it) — both
            // lookups fall back to null, and MutableMenuItemInstance/the text below already tolerate that.
            var someOptionsMenu = mainMenu.FindMenuRecursive("MENU_OPT_GRAPHICS");
            var backButtonReference = someOptionsMenu?.FindMenuItem("MENUITEM_GRA_BACK", out _);
            var backButton = new MutableMenuItemInstance("MENU_GOTHIC_OPT_VR_ACCESSIBILITY_BACK", backButtonReference);

            backButton.SetText(0, backButtonReference?.GetText(0) ?? "BACK");
            backButton.SetOnSelAction(0, MenuItemSelectAction.Back);

            return backButton;
        }
        
        
        
        private MutableMenuInstance CreateImmersionMenu(AbstractMenuInstance mainMenu)
        {
            // Find MENU_OPT_AUDIO --> PERF menu item to overwrite
            var optMenuParent = mainMenu.FindMenuRecursive("MENU_OPT_AUDIO")!.Parent;
            var referenceItemMenu = optMenuParent.FindMenuItem("MENUITEM_PERF", out var referenceItemIndex);
            var replace = true;
            
            // e.g., on russian language
            if (referenceItemMenu == null)
            {
                replace = false;
                referenceItemMenu = optMenuParent.FindMenuItem("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY", out referenceItemIndex);
            }
            
            // Create empty menu
            var vrImmersionMenu = new MutableMenuInstance("MENU_GOTHIC_OPT_VR_IMMERSION", optMenuParent);
            
            // Create menu item and replace it where >Keyboard< settings are normally
            var vrImmersionMenuItem = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION", referenceItemMenu);

            if (replace)
            {
                optMenuParent.ReplaceItemAt(referenceItemIndex, vrImmersionMenuItem);
            }
            // Calculate posY based on other 2 elements' diff and use it.
            else
            {
                var optVideoItem = optMenuParent.FindMenuItem("MENUITEM_OPT_VIDEO", out _);
                var optAudioItem = optMenuParent.FindMenuItem("MENUITEM_OPT_AUDIO", out _);

                // A mod can rename/restructure these too — fall back to no offset (item keeps the
                // position it cloned from referenceItemMenu) rather than crashing menu init.
                if (optVideoItem != null && optAudioItem != null)
                    vrImmersionMenuItem.PosY += optAudioItem.PosY - optVideoItem.PosY;

                optMenuParent.Items.Add(vrImmersionMenuItem);
            }
            
            // Add some setting
            vrImmersionMenuItem.SetText(0, _localizationService.GetText("menuitem.vr_immersion"));
            vrImmersionMenuItem.SetOnSelAction(0, MenuItemSelectAction.StartMenu);
            vrImmersionMenuItem.SetOnSelActionS(0, "MENU_GOTHIC_OPT_VR_IMMERSION");
            vrImmersionMenuItem.MenuInstance = vrImmersionMenu;

            return vrImmersionMenu;
        }
        
        private MutableMenuItemInstance CreateImmersionHeadline(AbstractMenuInstance gameMenu)
        {
            var gameHeadline = gameMenu.FindMenuItem("MENUITEM_GAME_HEADLINE", out _);
            var vrHeadline = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_HEADLINE", gameHeadline);
            vrHeadline.SetText(0, _localizationService.GetText("menuitem.vr_immersion.headline"));
            
            return vrHeadline;
        }
        
        private AbstractMenuItemInstance CreateMicrophoneLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var microphoneLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_MICROPHONE", subtitlesLabel);
            
            microphoneLabel.PosY = posY;
            
            microphoneLabel.SetText(0, _localizationService.GetText("menuitem.microphone.label"));
            microphoneLabel.SetText(1, _localizationService.GetText("menuitem.microphone.description"));
            
            return microphoneLabel;
        }
        
        private AbstractMenuItemInstance CreateMicrophoneChoice(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var microphoneSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_MICROPHONE_CHOICE", subtitlesChoice);

            microphoneSetting.PosY = posY;
            microphoneSetting.SetUserFloat(3, 0); // Default value if no INI value exists.

            microphoneSetting.SetText(0, GetMicrophoneList());
            microphoneSetting.OnChgSetOption = VRConstants.IniNames.Microphone;
            microphoneSetting.OnChgSetOptionSection = VRConstants.IniSectionImmersion;
            
            return microphoneSetting;
        }

        private string GetMicrophoneList()
        {
            var result = new List<string>();
            result.Add(_localizationService.GetText("menuitem.microphone.none_value"));
            result.AddRange(Microphone.devices);
            result = result.Select(i => i.Length > 15 ? i.Substring(0, 12) + "..." : i).ToList();

            return string.Join("|", result);
        }
        
        private MutableMenuItemInstance CreateImmersionBackButton(AbstractMenuInstance mainMenu)
        {
            var someOptionsMenu = mainMenu.FindMenuRecursive("MENU_OPT_GRAPHICS");
            var backButtonReference = someOptionsMenu?.FindMenuItem("MENUITEM_GRA_BACK", out _);
            var backButton = new MutableMenuItemInstance("MENU_GOTHIC_OPT_VR_IMMERSION_BACK", backButtonReference);

            backButton.SetText(0, backButtonReference?.GetText(0) ?? "BACK");
            backButton.SetOnSelAction(0, MenuItemSelectAction.Back);

            return backButton;
        }
    }
}
#endif
