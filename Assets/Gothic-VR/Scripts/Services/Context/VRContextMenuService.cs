using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Vm;
using Gothic.Services.UI;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Services.Context
{
    public class VRContextMenuService : IContextMenuService
    {
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly ConfigService _configService;
        
        
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
            vrImmersionMenu.Items.Add(CreatePhysicalHandsLabel(gameMenu, menuStartY + menuDY * 1));
            vrImmersionMenu.Items.Add(CreatePhysicalHandsChoice(gameMenu, menuStartY + menuDY * 1));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HIT_NUMBERS", "Hit numbers",
                "-X HP / +X MANA over NPC heads", menuStartY + menuDY * 2));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HIT_NUMBERS", Constants.IniNames.HitNumbers,
                menuStartY + menuDY * 2));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HIT_NUMBERS_SIZE", "Hit numbers size",
                "Size of the -X HP / +X MANA numbers", menuStartY + menuDY * 3));
            vrImmersionMenu.Items.Add(CreateHitNumbersSizeSlider(gameMenu, menuStartY + menuDY * 3));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HEALTH_NUMBERS", "HP numbers",
                "HP/MAX on health bars", menuStartY + menuDY * 4));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HEALTH_NUMBERS", Constants.IniNames.HealthNumbers,
                menuStartY + menuDY * 4));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "MARVIN", "Marvin",
                "Show the Marvin panel (needs Marvin mode)", menuStartY + menuDY * 5));
            vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "MARVIN", VRConstants.IniNames.MarvinPanel,
                menuStartY + menuDY * 5, 1));
            vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "LIGHT_REFRESH", "Light refresh",
                "How smoothly a torch's light follows your hand", menuStartY + menuDY * 6));
            vrImmersionMenu.Items.Add(CreateLightRefreshChoice(gameMenu, menuStartY + menuDY * 6));
            // DeveloperConfig.EnableHeroDeath: off = an NPC finishing you off only keeps you knocked out.
            if (_configService.Dev.EnableHeroDeath)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HERO_DEATH", "Death",
                    "NPCs can finish you off (off: only knocked out)", menuStartY + menuDY * 7));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HERO_DEATH", Constants.IniNames.HeroDeath,
                    menuStartY + menuDY * 7, 1));
            }
            var nextRow = _configService.Dev.EnableHeroDeath ? 8 : 7;
            // DeveloperConfig.EnableVrStabFinish: off = any hit finishes an unconscious NPC off.
            if (_configService.Dev.EnableVrStabFinish)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "STAB_FINISH", "Finishing",
                    _configService.Dev.EnableStabFinishHold
                        ? "Stab and hold the blade in to finish off (off: any hit)"
                        : "Stab and twist to finish off (off: any hit)", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "STAB_FINISH", VRConstants.IniNames.StabFinish,
                    menuStartY + menuDY * nextRow, 1));
                nextRow++;
            }
            // DeveloperConfig.EnableVrCrossbowManualReload: off = a crossbow reloads by itself.
            if (_configService.Dev.EnableVrCrossbowManualReload)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "CROSSBOW_RELOAD", "Crossbow",
                    "Pull the string to reload (off: automatic)", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "CROSSBOW_RELOAD",
                    VRConstants.IniNames.CrossbowReload, menuStartY + menuDY * nextRow, 1));
                nextRow++;
            }
            // DeveloperConfig.EnableVrSpellRing: Gesture (both hands up, both triggers) or Off.
            if (_configService.Dev.EnableVrSpellRing)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "SPELL_RING", "Favorites ring",
                    "Both hands up + both triggers: equipped spells and weapons", menuStartY + menuDY * nextRow));
                var spellRingChoice = CreateToggleChoice(gameMenu, "SPELL_RING", VRConstants.IniNames.SpellRing,
                    menuStartY + menuDY * nextRow, 1);
                spellRingChoice.SetText(0, "Off|Gesture");
                vrImmersionMenu.Items.Add(spellRingChoice);
                nextRow++;
            }
            // DeveloperConfig.EnableRangedKnockoutOption: off (OpenGothic) = arrows, bolts and spells kill.
            if (_configService.Dev.EnableRangedKnockoutOption)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "RANGED_KNOCKOUT", "Ranged knockout",
                    "Arrows/spells knock out, the next one kills", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "RANGED_KNOCKOUT",
                    VRConstants.IniNames.RangedKnockout, menuStartY + menuDY * nextRow,
                    _configService.Dev.RangedKnockoutByDefault ? 1 : 0));
                nextRow++;
            }
            // DeveloperConfig.EnableScrollUseOption: on (Gothic) a scroll's cast uses it up, off they're cast forever.
            if (_configService.Dev.EnableScrollUseOption)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "SCROLLS_SINGLE_USE", "Single-use scrolls",
                    "A cast uses the scroll up (Gothic)", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "SCROLLS_SINGLE_USE",
                    VRConstants.IniNames.ScrollsSingleUse, menuStartY + menuDY * nextRow, 1));
                nextRow++;
            }
            // DeveloperConfig.EnableHeroHands: the hero's own fists instead of the VR hand models (needs the hero body).
            if (_configService.Dev.EnableHeroHands && _configService.Dev.EnableVrHeroBody)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "HERO_HANDS", "Hero's hands",
                    "Your hands are the hero's fists", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "HERO_HANDS",
                    VRConstants.IniNames.HeroHands, menuStartY + menuDY * nextRow, 0));
                nextRow++;
            }
            // DeveloperConfig.EnableKillerXpOnly: off (Gothic) only the hero's, his summons' and his party's kills give
            // experience, on every death anywhere does.
            if (_configService.Dev.EnableKillerXpOnly)
            {
                vrImmersionMenu.Items.Add(CreateToggleLabel(gameMenu, "XP_FOR_ANY_KILL", "XP for every death",
                    "Cheat: any death gives you XP (Gothic: only your kills)", menuStartY + menuDY * nextRow));
                vrImmersionMenu.Items.Add(CreateToggleChoice(gameMenu, "XP_FOR_ANY_KILL",
                    VRConstants.IniNames.XpForAnyKill, menuStartY + menuDY * nextRow, 0));
                nextRow++;
            }
            var restartRow = nextRow;
            vrImmersionMenu.Items.Add(CreateRestartButton(mainMenu, menuStartY + menuDY * restartRow));
            var backButton = CreateImmersionBackButton(mainMenu);
            vrImmersionMenu.Items.Add(backButton);
            AddScrolling(mainMenu, vrImmersionMenu, menuStartY, backButton.PosY, menuDY);
        }

        /// <summary>
        /// DeveloperConfig.EnableMenuScrolling: the option rows (below the headline, above Back) scroll when there are
        /// more than fit - Up (right, under the headline) / Down (right, next to Back) buttons (AbstractMenu).
        /// </summary>
        private void AddScrolling(AbstractMenuInstance mainMenu, MutableMenuInstance menu, int topY, int bottomY,
            int stepY)
        {
            if (!_configService.Dev.EnableMenuScrolling)
                return;

            menu.ScrollTopY = topY;
            menu.ScrollBottomY = bottomY;
            menu.ScrollStepY = stepY;
            foreach (var item in menu.Items)
                item.IsScrollable = item.PosY >= topY && !item.Name.EndsWith("_BACK");

            if (menu.Items.Max(i => i.IsScrollable ? i.PosY : 0) < bottomY)
                return;
            // Up on the right under the headline, Down on the right next to Back - one above the other.
            menu.Items.Add(CreateScrollButton(mainMenu, menu.Name + "_SCROLL_UP", "Up", Constants.MenuCommandScrollUp,
                _scrollButtonX, topY - stepY));
            menu.Items.Add(CreateScrollButton(mainMenu, menu.Name + "_SCROLL_DOWN", "Down",
                Constants.MenuCommandScrollDown, _scrollButtonX, bottomY + stepY / 3));
        }

        // Virtual menu X (0...8191) of the scroll buttons, at the right edge.
        private const int _scrollButtonX = 6300;
        private const int _scrollButtonWidth = 1600;

        private MutableMenuItemInstance CreateScrollButton(AbstractMenuInstance mainMenu, string name, string text,
            string command, int posX, int posY)
        {
            var someOptionsMenu = mainMenu.FindMenuRecursive("MENU_OPT_GRAPHICS");
            var buttonReference = someOptionsMenu?.FindMenuItem("MENUITEM_GRA_BACK", out _);
            var button = new MutableMenuItemInstance(name, buttonReference);

            button.PosX = posX;
            button.DimX = _scrollButtonWidth;
            button.PosY = posY;
            button.SetText(0, text);
            button.SetOnSelAction(0, MenuItemSelectAction.ExecuteCommand);
            button.SetOnSelActionS(0, command);

            return button;
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
            sitStandSetting.SetUserFloat(3, (int)VrSitStand.PlayerHeight); // Default value if no INI value exists.

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
            rotationTypeSetting.SetUserFloat(3, (int)VrTurnType.Snap); // Default value if no INI value exists.

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

        private AbstractMenuItemInstance CreatePhysicalHandsLabel(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesLabel = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES", out _);
            var physicalHandsLabel =
                new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_PHYSICAL_HANDS", subtitlesLabel);

            physicalHandsLabel.PosY = posY;

            physicalHandsLabel.SetText(0, _localizationService.GetText("menuitem.physicalHands.label"));
            physicalHandsLabel.SetText(1, _localizationService.GetText("menuitem.physicalHands.description"));

            return physicalHandsLabel;
        }

        private AbstractMenuItemInstance CreatePhysicalHandsChoice(AbstractMenuInstance gameMenu, int posY)
        {
            var subtitlesChoice = gameMenu.FindMenuItem("MENUITEM_GAME_SUB_TITLES_CHOICE", out _);
            var physicalHandsSetting =
                new MutableMenuItemInstance("MENUITEM_GOTHIC_OPT_VR_IMMERSION_PHYSICAL_HANDS_CHOICE", subtitlesChoice);

            physicalHandsSetting.PosY = posY;
            // Default value if no INI value exists.
            physicalHandsSetting.SetUserFloat(3, VRConstants.PhysicalHandsDefaultValue);

            physicalHandsSetting.SetText(0, _localizationService.GetText("menuitem.physicalHands.value"));
            physicalHandsSetting.OnChgSetOption = VRConstants.IniNames.PhysicalHands;
            physicalHandsSetting.OnChgSetOptionSection = VRConstants.IniSectionImmersion;

            return physicalHandsSetting;
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
