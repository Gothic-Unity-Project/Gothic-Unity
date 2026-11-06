using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.World;
using Gothic.Services.UI;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    public class VRContextMenuService : IContextMenuService
    {
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;
        [Inject] private readonly VmService _vmService;

        private const int _rowsPerPage = 6;
        
        
        /// <summary>
        /// Update menu entries based on VR needs.
        /// --> Replace control menu with VR settings menu
        /// </summary>
        public void UpdateMainMenu(AbstractMenuInstance mainMenu)
        {
            // G2's focus box/highlight and Gothic 1 key controls: VR uses neither, their rows hold the controller
            // options (AddControlOptions). Flat keeps them. The menus are built after this call.
            _vmService.DisableGothicMenuSettings(
                "MENUITEM_GAME_FIGHTFOCUS", "MENUITEM_GAME_FIGHTFOCUS_CHOICE",
                "MENUITEM_GAME_INTERACTFOCUS", "MENUITEM_GAME_INTERACTFOCUS_CHOICE",
                "MENUITEM_GAME_OLDCONTROLS", "MENUITEM_GAME_OLDCONTROLS_CHOICE");

            var vrAccessibilityMenu = CreateAccessibilityMenu(mainMenu);

            // Will be used to clone render settings from its items.
            var gameMenu = mainMenu.FindMenuRecursive("MENU_OPT_GAME")!;

            vrAccessibilityMenu.Items.Add(CreateAccessibilityHeadline(gameMenu));
            AddOptionRows(vrAccessibilityMenu,
                (CreateSitStandLabel(gameMenu), CreateSitStandChoicebox(gameMenu)),
                (CreateHeightOffsetLabel(gameMenu), CreateHeightOffsetSlider(gameMenu)),
                (CreateMoveDirectionLabel(gameMenu), CreateMoveDirectionChoicebox(gameMenu)),
                (CreateRotationTypeLabel(gameMenu), CreateRotationTypeChoicebox(gameMenu)),
                (CreateSmoothRotationSpeedLabel(gameMenu), CreateSmoothRotationSpeedSlider(gameMenu)),
                (CreateSnapRotationLabel(gameMenu), CreateSnapRotationChoicebox(gameMenu)),
                (CreateSmoothSpectatorLabel(gameMenu), CreateSmoothSpectatorChoicebox(gameMenu)),
                (CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_LEDGE_CLIMB_MODE", "ledgeClimbMode"),
                    CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_LEDGE_CLIMB_MODE",
                        "ledgeClimbMode", VRConstants.IniNames.LedgeClimbMode, VRConstants.IniSectionAccessibility,
                        VRConstants.LedgeClimbModeDefaultValue)));
            vrAccessibilityMenu.Items.Add(CreateAccessibilityBackButton(mainMenu));

            // VR hides all of the game menu but its subtitles option: the subtitles' text size and the color vision go
            // below it.
            var menuStartY = _vmService.MenuStartY;
            var menuDY = _vmService.MenuDY;
            gameMenu.FindMenuItem("MENUITEM_GAME_BACK", out var gameBackIndex);
            if (gameBackIndex < 0)
                gameBackIndex = gameMenu.Items.Count;
            AddControlOptions(gameMenu, gameBackIndex, menuStartY, menuDY);
            gameMenu.Items.Insert(gameBackIndex, AtPosY(CreateOptionChoice(gameMenu,
                "MENUITEM_GOTHIC_OPT_GAME_COLOR_VISION", "colorVision", VRConstants.IniNames.ColorVision,
                VRConstants.IniSectionImmersion, VRConstants.ColorVisionDefaultValue), menuStartY + menuDY * 2));
            gameMenu.Items.Insert(gameBackIndex, AtPosY(CreateOptionLabel(gameMenu,
                "MENUITEM_GOTHIC_OPT_GAME_COLOR_VISION", "colorVision"), menuStartY + menuDY * 2));
            gameMenu.Items.Insert(gameBackIndex, AtPosY(CreateOptionChoice(gameMenu,
                "MENUITEM_GOTHIC_OPT_GAME_TEXT_SIZE", "textSize", VRConstants.IniNames.TextSize,
                VRConstants.IniSectionImmersion, VRConstants.TextSizeDefaultValue), menuStartY + menuDY * 1));
            gameMenu.Items.Insert(gameBackIndex, AtPosY(CreateOptionLabel(gameMenu,
                "MENUITEM_GOTHIC_OPT_GAME_TEXT_SIZE", "textSize"), menuStartY + menuDY * 1));

            // FIXME - Add - Setting: ItemCollisionWhileDragged
            var vrImmersionMenu = CreateImmersionMenu(mainMenu);
            vrImmersionMenu.Items.Add(CreateImmersionHeadline(gameMenu));
            var rows = new List<(AbstractMenuItemInstance label, AbstractMenuItemInstance value)>
            {
                (CreateMicrophoneLabel(gameMenu), CreateMicrophoneChoice(gameMenu)),
                (CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_PHYSICAL_HANDS", "physicalHands"),
                    CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_PHYSICAL_HANDS", "physicalHands",
                        VRConstants.IniNames.PhysicalHands, VRConstants.IniSectionImmersion,
                        VRConstants.PhysicalHandsDefaultValue)),
                (CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_LEDGE_HINTS", "ledgeHints"),
                    CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_LEDGE_HINTS", "ledgeHints",
                        VRConstants.IniNames.LedgeHints, VRConstants.IniSectionImmersion,
                        VRConstants.LedgeHintsDefaultValue)),
                (CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_MOVE_VIGNETTE", "moveVignette"),
                    CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_VR_IMMERSION_MOVE_VIGNETTE", "moveVignette",
                        VRConstants.IniNames.MoveVignette, VRConstants.IniSectionImmersion,
                        VRConstants.MoveVignetteDefaultValue))
            };
            // DeveloperConfig.EnableHeroHands: the hero's own fists instead of the VR hand models (needs the hero body).
            if (_configService.Dev.EnableHeroHands && _configService.Dev.EnableVrHeroBody)
                rows.Add((CreateToggleLabel(gameMenu, "HERO_HANDS", "Hero's hands", "Your hands are the hero's fists"),
                    CreateToggleChoice(gameMenu, "HERO_HANDS", VRConstants.IniNames.HeroHands,
                        VRConstants.HeroHandsDefaultValue)));
            // The camera falls with the VR hero body when he's knocked out or dies - off: the view fades instead.
            if (_configService.Dev.EnableVrHeroBody)
                rows.Add((CreateToggleLabel(gameMenu, "HERO_FALL_CAMERA", "Fall with the body",
                        "Knocked out, the view goes down with the hero"),
                    CreateToggleChoice(gameMenu, "HERO_FALL_CAMERA", VRConstants.IniNames.HeroFallCamera,
                        VRConstants.HeroFallCameraDefaultValue)));
            // DeveloperConfig.EnableVrStabFinish: off = any hit finishes an unconscious NPC off.
            if (_configService.Dev.EnableVrStabFinish)
                rows.Add((CreateToggleLabel(gameMenu, "STAB_FINISH", "Finishing",
                        "Stab and hold the blade in to finish off (off: any hit)"),
                    CreateToggleChoice(gameMenu, "STAB_FINISH", VRConstants.IniNames.StabFinish, 1)));
            // DeveloperConfig.EnableVrCrossbowManualReload: off = a crossbow reloads by itself.
            if (_configService.Dev.EnableVrCrossbow && _configService.Dev.EnableVrCrossbowManualReload)
                rows.Add((CreateToggleLabel(gameMenu, "CROSSBOW_RELOAD", "Crossbow",
                        "Pull the string to reload (off: automatic)"),
                    CreateToggleChoice(gameMenu, "CROSSBOW_RELOAD", VRConstants.IniNames.CrossbowReload, 1)));
            // DeveloperConfig.EnableScrollUseOption: off = scrolls are cast forever.
            if (_configService.Dev.EnableScrollUseOption)
                rows.Add((CreateToggleLabel(gameMenu, "SCROLLS_SINGLE_USE", "Single-use scrolls",
                        "A cast uses the scroll up (Gothic)"),
                    CreateToggleChoice(gameMenu, "SCROLLS_SINGLE_USE", VRConstants.IniNames.ScrollsSingleUse, 1)));
            // DeveloperConfig.EnableVrSpellRing: Gesture (both hands up, both triggers) or Off.
            if (_configService.Dev.EnableVrSpellRing)
            {
                var spellRingChoice = CreateToggleChoice(gameMenu, "SPELL_RING", VRConstants.IniNames.SpellRing, 1);
                spellRingChoice.SetText(0, "Off|Gesture");
                rows.Add((CreateToggleLabel(gameMenu, "SPELL_RING", "Favorites ring",
                    "Both hands up + both triggers: equipped spells and weapons"), spellRingChoice));
            }
            AddOptionRows(vrImmersionMenu, rows.ToArray());
            vrImmersionMenu.Items.Add(CreateImmersionBackButton(mainMenu));

            AddWindOption(mainMenu, menuDY);
        }

        /// <summary>
        /// Trees and grass swaying (WindService) as the last row of Gothic's graphics menu - G1 has four rows there, G2
        /// five, both one free. Gothic's own zWindEnabled in [ENGINE]. Off on the Quest by default, for performance.
        /// </summary>
        private void AddWindOption(AbstractMenuInstance mainMenu, int menuDY)
        {
            var graphicsMenu = mainMenu.FindMenuRecursive("MENU_OPT_GRAPHICS");
            if (graphicsMenu == null)
            {
                Logger.LogWarning("[Menu] No MENU_OPT_GRAPHICS - the wind option is left out.", LogCat.Ui);
                return;
            }

            var skyLabel = graphicsMenu.FindMenuItem("MENUITEM_GRA_SKY_EFFECTS", out _);
            var skyChoice = graphicsMenu.FindMenuItem("MENUITEM_GRA_SKY_EFFECTS_CHOICE", out _);
            graphicsMenu.FindMenuItem("MENUITEM_GRA_BACK", out var backIndex);
            if (skyLabel == null || skyChoice == null || backIndex < 0)
            {
                Logger.LogWarning("[Menu] MENU_OPT_GRAPHICS lacks the sky effects or back row - the wind option is " +
                    "left out.", LogCat.Ui);
                return;
            }

            var windLabel = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_GRA_WIND", skyLabel);
            windLabel.PosY = skyLabel.PosY + menuDY;
            windLabel.SetText(0, _localizationService.GetText("menuitem.wind.label"));
            windLabel.SetText(1, _localizationService.GetText("menuitem.wind.description"));

            var windChoice = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_GRA_WIND_CHOICE", skyChoice);
            windChoice.PosY = skyChoice.PosY + menuDY;
            // Default value if no INI value exists.
            windChoice.SetUserFloat(3, WindService.DefaultEnabled);
            // off|on, like the physical hands.
            windChoice.SetText(0, _localizationService.GetText("menuitem.physicalHands.value"));
            windChoice.OnChgSetOption = WindService.IniKeyEnabled;
            windChoice.OnChgSetOptionSection = WindService.IniSection;

            graphicsMenu.Items.Insert(backIndex, windChoice);
            graphicsMenu.Items.Insert(backIndex, windLabel);
        }

        /// <summary>
        /// One row per option (label + value). Gothic's menus fit _rowsPerPage rows, further rows go on the next page,
        /// which the menu shows with up/down arrows.
        /// </summary>
        private void AddOptionRows(AbstractMenuInstance menu,
            params (AbstractMenuItemInstance label, AbstractMenuItemInstance value)[] rows)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var posY = _vmService.MenuStartY + _vmService.MenuDY * (i % _rowsPerPage);
                foreach (var item in new[] { rows[i].label, rows[i].value })
                {
                    item.PosY = posY;
                    item.Page = i / _rowsPerPage;
                    menu.Items.Add(item);
                }
            }
        }

        // Our own options without localization entries yet - plain English labels. AddOptionRows places them.
        private MutableMenuItemInstance CreateToggleLabel(AbstractMenuInstance gameMenu, string id, string label,
            string description)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var item = new MutableMenuItemInstance($"MENUITEM_GOTHIC_OPT_VR_IMMERSION_{id}", subtitlesLabel);

            item.SetText(0, label);
            item.SetText(1, description);

            return item;
        }

        private MutableMenuItemInstance CreateToggleChoice(AbstractMenuInstance gameMenu, string id, string iniName,
            int defaultValue = 0)
        {
            // Gothic's own subtitles choice gives the "no|yes" values in the game's language.
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var item = new MutableMenuItemInstance($"MENUITEM_GOTHIC_OPT_VR_IMMERSION_{id}_CHOICE", subtitlesChoice);

            item.SetUserFloat(3, defaultValue); // 0 = off, 1 = on if no INI value exists.
            item.SetText(0, subtitlesChoice?.GetText(0) ?? "Off|On");
            item.OnChgSetOption = iniName;
            item.OnChgSetOptionSection = VRConstants.IniSectionImmersion;

            return item;
        }

        /// <summary>
        /// The controller layout (VrControlLayout) in rows 3 to 5 of the game menu, where Gothic has its mouse
        /// settings - the VR counterpart of its key bindings. Inserted before the rows above, which go to the same
        /// index.
        /// </summary>
        private void AddControlOptions(AbstractMenuInstance gameMenu, int index, int menuStartY, int menuDY)
        {
            gameMenu.Items.Insert(index, AtPosY(CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_MENU_BUTTON",
                "menuButton", VRConstants.IniNames.MenuButton, VRConstants.IniSectionControls,
                VRConstants.MenuButtonDefaultValue), menuStartY + menuDY * 5));
            gameMenu.Items.Insert(index, AtPosY(CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_MENU_BUTTON",
                "menuButton"), menuStartY + menuDY * 5));
            gameMenu.Items.Insert(index, AtPosY(CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_JUMP_BUTTON",
                "jumpButton", VRConstants.IniNames.JumpButton, VRConstants.IniSectionControls,
                VRConstants.JumpButtonDefaultValue), menuStartY + menuDY * 4));
            gameMenu.Items.Insert(index, AtPosY(CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_JUMP_BUTTON",
                "jumpButton"), menuStartY + menuDY * 4));
            gameMenu.Items.Insert(index, AtPosY(CreateOptionChoice(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_CONTROLLER_HAND",
                "controllerHand", VRConstants.IniNames.ControllerHand, VRConstants.IniSectionControls,
                VRConstants.ControllerHandDefaultValue), menuStartY + menuDY * 3));
            gameMenu.Items.Insert(index, AtPosY(CreateOptionLabel(gameMenu, "MENUITEM_GOTHIC_OPT_GAME_CONTROLLER_HAND",
                "controllerHand"), menuStartY + menuDY * 3));
        }

        /// <summary>
        /// An option row at a fixed height, for Gothic's own menus (no pages).
        /// </summary>
        private static MutableMenuItemInstance AtPosY(MutableMenuItemInstance item, int posY)
        {
            item.PosY = posY;
            return item;
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
        
        private AbstractMenuItemInstance CreateSitStandChoicebox(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var sitStandLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SIT_STAND", subtitlesLabel);
            
            
            sitStandLabel.SetText(0, _localizationService.GetText("menuitem.sitStand.label"));
            sitStandLabel.SetText(1, _localizationService.GetText("menuitem.sitStand.description"));
            
            return sitStandLabel;
        }

        private AbstractMenuItemInstance CreateSitStandLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var sitStandSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SIT_STAND_CHOICE", subtitlesChoice);

            sitStandSetting.SetUserFloat(3, (int)VrSitStand.PlayerHeight); // Default value if no INI value exists.

            sitStandSetting.SetText(0, _localizationService.GetText("menuitem.sitStand.value"));
            sitStandSetting.OnChgSetOption = VRConstants.IniNames.SitStand;
            sitStandSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return sitStandSetting;
        }

        private AbstractMenuItemInstance CreateHeightOffsetLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var heightOffsetLabel =
                new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_HEIGHT_OFFSET", subtitlesLabel);

            heightOffsetLabel.SetText(0, _localizationService.GetText("menuitem.heightOffset.label"));
            heightOffsetLabel.SetText(1, _localizationService.GetText("menuitem.heightOffset.description"));

            return heightOffsetLabel;
        }

        private AbstractMenuItemInstance CreateHeightOffsetSlider(AbstractMenuInstance gameMenu)
        {
            var mouseSlider = gameMenu.FindMenuItem("MENUITEM_MSENSITIVITY_SLIDER", out _);
            var heightOffsetSetting =
                new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_HEIGHT_OFFSET_SLIDER", mouseSlider);

            // Default value if no INI value exists. Middle == no offset.
            heightOffsetSetting.SetUserFloat(3, VRConstants.HeightOffsetDefaultValue);

            heightOffsetSetting.SetUserFloat(0, VRConstants.HeightOffsetSettingAmount);
            heightOffsetSetting.OnChgSetOption = VRConstants.IniNames.HeightOffset;
            heightOffsetSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            // Moving the view on every step moved the player away from the slider they were holding.
            heightOffsetSetting.IsAppliedOnRelease = true;

            return heightOffsetSetting;
        }

        private AbstractMenuItemInstance CreateMoveDirectionLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var moveDirectionLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_MOVE_DIRECTION", subtitlesLabel);
            
            
            moveDirectionLabel.SetText(0, _localizationService.GetText("menuitem.moveDirection.label"));
            moveDirectionLabel.SetText(1, _localizationService.GetText("menuitem.moveDirection.description"));
            
            return moveDirectionLabel;
        }
        
        private AbstractMenuItemInstance CreateMoveDirectionChoicebox(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var moveDirectionSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_MOVE_DIRECTION_CHOICE", subtitlesChoice);

            moveDirectionSetting.SetText(0, _localizationService.GetText("menuitem.moveDirection.value"));
            moveDirectionSetting.OnChgSetOption = VRConstants.IniNames.MoveDirection;
            moveDirectionSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return moveDirectionSetting;
        }
        
        private AbstractMenuItemInstance CreateRotationTypeLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var rotationTypeLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_ROTATION_TYPE", subtitlesLabel);
            
            
            rotationTypeLabel.SetText(0, _localizationService.GetText("menuitem.rotationType.label"));
            rotationTypeLabel.SetText(1, _localizationService.GetText("menuitem.rotationType.description"));
            
            return rotationTypeLabel;
        }

        private AbstractMenuItemInstance CreateRotationTypeChoicebox(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var rotationTypeSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_ROTATION_TYPE_CHOICE", subtitlesChoice);

            rotationTypeSetting.SetUserFloat(3, (int)VrTurnType.Snap); // Default value if no INI value exists.

            rotationTypeSetting.SetText(0, _localizationService.GetText("menuitem.rotationType.value"));
            rotationTypeSetting.OnChgSetOption = VRConstants.IniNames.RotationType;
            rotationTypeSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return rotationTypeSetting;
        }

        private AbstractMenuItemInstance CreateSmoothRotationSpeedLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothRotationLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTH_ROTATION", subtitlesLabel);
            
            
            smoothRotationLabel.SetText(0, _localizationService.GetText("menuitem.smoothRotation.label"));
            smoothRotationLabel.SetText(1, _localizationService.GetText("menuitem.smoothRotation.description"));
            
            return smoothRotationLabel;
        }
        
        private AbstractMenuItemInstance CreateSmoothRotationSpeedSlider(AbstractMenuInstance gameMenu)
        {
            var mouseSlider = gameMenu.FindMenuItem("MENUITEM_MSENSITIVITY_SLIDER", out _);
            var smoothRotationSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTH_ROTATION_SLIDER", mouseSlider);

            smoothRotationSetting.SetUserFloat(3, VRConstants.SmoothRotationDefaultValue); // Default value if no INI value exists.

            smoothRotationSetting.SetUserFloat(0, VRConstants.SmoothRotationSettingAmount);
            smoothRotationSetting.OnChgSetOption = VRConstants.IniNames.SmoothRotationSpeed;
            smoothRotationSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return smoothRotationSetting;
        }
        
        private AbstractMenuItemInstance CreateSnapRotationLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothRotationLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SNAP_ROTATION", subtitlesLabel);
            
            
            smoothRotationLabel.SetText(0, _localizationService.GetText("menuitem.snapRotation.label"));
            smoothRotationLabel.SetText(1, _localizationService.GetText("menuitem.snapRotation.description"));
            
            return smoothRotationLabel;
        }
        
        private AbstractMenuItemInstance CreateSnapRotationChoicebox(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var snapRotationSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SNAP_ROTATION_CHOICE", subtitlesChoice);

            snapRotationSetting.SetUserFloat(3, VRConstants.SnapRotationDefaultValue); // Default value if no INI value exists.

            snapRotationSetting.SetText(0, _localizationService.GetText("menuitem.snapRotation.value"));
            snapRotationSetting.OnChgSetOption = VRConstants.IniNames.SnapRotationAmount;
            snapRotationSetting.OnChgSetOptionSection = VRConstants.IniSectionAccessibility;
            
            return snapRotationSetting;
        }

        private MutableMenuItemInstance CreateSmoothSpectatorLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var smoothingLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTHSPECTATOR", subtitlesLabel);
            
            
            smoothingLabel.SetText(0, _localizationService.GetText("menuitem.smooth.label"));
            smoothingLabel.SetText(1, _localizationService.GetText("menuitem.smooth.description"));
            
            return smoothingLabel;
        }
        
        private MutableMenuItemInstance CreateSmoothSpectatorChoicebox(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var smoothingSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_ACCESSIBILITY_SMOOTHSPECTATOR_CHOICE", subtitlesChoice);

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
        
        private AbstractMenuItemInstance CreateMicrophoneLabel(AbstractMenuInstance gameMenu)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var microphoneLabel= new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_MICROPHONE", subtitlesLabel);
            
            
            microphoneLabel.SetText(0, _localizationService.GetText("menuitem.microphone.label"));
            microphoneLabel.SetText(1, _localizationService.GetText("menuitem.microphone.description"));
            
            return microphoneLabel;
        }
        
        private AbstractMenuItemInstance CreateMicrophoneChoice(AbstractMenuInstance gameMenu)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var microphoneSetting = new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_MICROPHONE_CHOICE", subtitlesChoice);

            microphoneSetting.SetUserFloat(3, 0); // Default value if no INI value exists.

            microphoneSetting.SetText(0, GetMicrophoneList());
            microphoneSetting.OnChgSetOption = VRConstants.IniNames.Microphone;
            microphoneSetting.OnChgSetOptionSection = VRConstants.IniSectionImmersion;
            
            return microphoneSetting;
        }

        /// <summary>
        /// Label of an option, styled like the game menu's subtitles option. Texts: menuitem.{textKey}.label and
        /// .description.
        /// </summary>
        private MutableMenuItemInstance CreateOptionLabel(AbstractMenuInstance gameMenu, string itemName,
            string textKey)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var label = new MutableMenuItemInstance(itemName, subtitlesLabel);

            label.SetText(0, _localizationService.GetText($"menuitem.{textKey}.label"));
            label.SetText(1, _localizationService.GetText($"menuitem.{textKey}.description"));

            return label;
        }

        /// <summary>
        /// Choicebox of an option (itemName + "_CHOICE"), styled like the game menu's subtitles choice, stored in
        /// Gothic.ini [iniSection] iniKey. Choices: menuitem.{textKey}.value ("a|b|c").
        /// </summary>
        private MutableMenuItemInstance CreateOptionChoice(AbstractMenuInstance gameMenu, string itemName,
            string textKey, string iniKey, string iniSection, float defaultValue)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var choice = new MutableMenuItemInstance($"{itemName}_CHOICE", subtitlesChoice);

            choice.SetUserFloat(3, defaultValue); // Default value if no INI value exists.

            choice.SetText(0, _localizationService.GetText($"menuitem.{textKey}.value"));
            choice.OnChgSetOption = iniKey;
            choice.OnChgSetOptionSection = iniSection;

            return choice;
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
