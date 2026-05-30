using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Logging;
using GUZ.Core.Extensions;
using GUZ.Core.Model.UI.Menu;
using GUZ.Core.Model.UI.MenuItem;
using GUZ.Core.Models.Caches;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.UI;
using GUZ.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZenKit.Daedalus;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.UI.Menus
{
    public abstract class AbstractMenu : MonoBehaviour
    {
        [Inject] protected readonly ConfigService ConfigService;
        [Inject] protected readonly UIEventsService UIEventsService;
        [Inject] protected readonly FontService FontService;
        [Inject] protected readonly TextureService TextureService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        protected MenuHandler MenuHandler;
        protected AbstractMenuInstance MenuInstance;
        [SerializeField] protected GameObject Canvas;
        [SerializeField] protected GameObject Background;
        [SerializeField] protected TMP_Text Comment;

        protected Dictionary<string, (AbstractMenuItemInstance item, GameObject go)> MenuItemCache = new();

        // Pixel ratio of whole menu (Canvas) is based on background picture pixel and virtual pixels named inside Daedalus.
        protected float PixelRatioX;
        protected float PixelRatioY;

        public virtual void InitializeMenu(AbstractMenuInstance menuInstance)
        {
            MenuHandler = transform.parent.GetComponent<MenuHandler>();
            
            MenuInstance = menuInstance;
            
            CreateRootElements();
        }
        
        protected abstract void Undefined(string itemName, string commandName);

        protected virtual void Back(string itemName, string commandName)
        {
            MenuHandler.BackMenu();
        }

        protected abstract void StartMenu(string itemName, string commandName);

        protected abstract void StartItem(string itemName, string commandName);

        protected abstract void Close(string itemName, string commandName);

        protected abstract void ConsoleCommand(string itemName, string commandName);
        protected abstract void PlaySound(string itemName, string commandName);
        protected abstract void ExecuteCommand(string itemName, string commandName);

        protected virtual bool IsMenuItemActive(string menuItemName)
        {
            if ( _vmService.DisabledGothicMenuSettings.Contains(menuItemName))
                return false;
            else
                return true;
        }
        
        public void ToggleVisibility()
        {
            if (gameObject.activeSelf)
            {
                SetInvisible();
            }
            else
            {
                SetVisible();
            }
        }

        public virtual void SetVisible()
        {
            gameObject.SetActive(true);
        }

        public virtual void SetInvisible()
        {
            gameObject.SetActive(false);
        }

        private void CreateRootElements()
        {
            var backPic = TextureService.GetMaterial(MenuInstance.BackPic);
            SetupBackground(backPic);
            SetupCanvasSize(backPic);
            ComputePixelRatios(backPic);
            BuildAllMenuItems();
        }
        
        private void BuildAllMenuItems()
        {
            foreach (var item in MenuInstance.Items)
            {
                CreateMenuItem(item);
            }
        }

        private void CreateMenuItem(AbstractMenuItemInstance item)
        {
            var itemGo = InstantiateItemGameObject(item);
            itemGo.transform.localPosition = Vector3.zero;
            MenuItemCache[item.Name] = (item, itemGo);

            ConfigureRectTransform(itemGo, item, out var itemWidth, out var itemHeight);
            AddBackPicIfAny(itemGo, item, itemWidth, itemHeight);
            SetupTextIfAny(itemGo, item, itemWidth, itemHeight);
            SetupCommentIfAny(itemGo, item);
            ApplyDisabledStateIfNeeded(itemGo, item);
        }

        private void SetupBackground(Material backPic)
        {
            Background.GetComponentInChildren<MeshRenderer>().sharedMaterial = backPic;
        }

        private void SetupCanvasSize(Material backPic)
        {
            var canvasRect = Canvas.GetComponent<RectTransform>();
            canvasRect.SetWidth(backPic.mainTexture.width);
            canvasRect.SetHeight(backPic.mainTexture.height);
        }

        private void ComputePixelRatios(Material backPic)
        {
            var virtualPixelX = MenuInstance.DimX + 1;
            var virtualPixelY = MenuInstance.DimY + 1;
            var realPixelX = backPic.mainTexture.width;
            var realPixelY = backPic.mainTexture.height;

            PixelRatioX = (float)virtualPixelX / realPixelX;
            PixelRatioY = (float)virtualPixelY / realPixelY;
        }

        private GameObject InstantiateItemGameObject(AbstractMenuItemInstance item)
        {
            GameObject itemGo;
            if (item.MenuItemType == MenuItemType.ListBox)
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiEmpty, name: item.Name, parent: Canvas)!;
            }
            else if (item.Flags.HasFlag(MenuItemFlag.Selectable))
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButton, name: item.Name, parent: Canvas)!;
                var button = itemGo.GetComponentInChildren<Button>();
                button.onClick.AddListener(() => HandleMenuItemClick(item));
            }
            else if (item.MenuItemType == MenuItemType.ChoiceBox)
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButton, name: item.Name, parent: Canvas)!;
                var button = itemGo.GetComponentInChildren<Button>();
                button.onClick.AddListener(() => HandleChoiceBoxClick(item, itemGo));
            }
            else if (item.MenuItemType == MenuItemType.Slider)
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiSlider, name: item.Name, parent: Canvas)!;
                SetSliderValues(itemGo, item);
            }
            else if (item.MenuItemType == MenuItemType.Text)
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiText, name: item.Name, parent: Canvas)!;
            }
            else
            {
                itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiEmpty, name: item.Name, parent: Canvas)!;
            }
            return itemGo;
        }

        private void ConfigureRectTransform(GameObject itemGo, AbstractMenuItemInstance item, out float itemWidth, out float itemHeight)
        {
            var rect = itemGo.GetComponent<RectTransform>();
            var halfMainWidth = (float)MenuInstance.DimX / 2;
            var halfMainHeight = (float)MenuInstance.DimY / 2;

            if (item.DimX > 0)
            {
                // As we have anchor positions at the center (0.5), we need to move into a certain direction from the center
                // Hint: We need to stick with center, as setting anchor positions at runtime (e.g. left-aligned) causes Unity to crash at a certain amount of changes.
                itemWidth = item.DimX;
            }
            else
            {
                // We assume the element can be drawn until end of whole UI.
                itemWidth = ((float)MenuInstance.DimX - item.PosX);
            }

            rect.SetPositionX((item.PosX - halfMainWidth + itemWidth / 2) / PixelRatioX);
            rect.SetWidth(itemWidth / PixelRatioX);

            if (item.DimY > 0)
            {
                itemHeight = item.DimY;
            }
            else
            {
                // We assume the element can be drawn until end of whole UI.
                itemHeight = (float)MenuInstance.DimY - item.PosY;
            }

            rect.SetPositionY((halfMainHeight - item.PosY - itemHeight / 2) / PixelRatioY);
            rect.SetHeight(itemHeight / PixelRatioY);
        }

        private void AddBackPicIfAny(GameObject itemGo, AbstractMenuItemInstance item, float itemWidth, float itemHeight)
        {
            if (!item.BackPic.NotNullOrEmpty()) return;

            var backPicGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiTexture, name: item.BackPic, parent: itemGo)!;
            var backPic = TextureService.GetMaterial(item.BackPic);

            if (!item.AlphaMode.IsNullOrEmpty())
            {
                backPic.ToTransparentMode();
                var color = backPic.GetColor("_BaseColor");
                var alpha = item.Alpha / 255f;
                backPic.SetColor("_BaseColor", new Color(color.r, color.g, color.b, alpha));
            }

            backPicGo.transform.localPosition = Vector3.zero;
            var backPictRenderer = backPicGo.GetComponentInChildren<MeshRenderer>();
            backPictRenderer.sharedMaterial = backPic;

            // Apply scale and position (move slightly backwards) from normal background to this one.
            backPictRenderer.transform.localScale = new Vector3(itemWidth / PixelRatioX / 10, 1, itemHeight / PixelRatioY / 10);
        }

        private void SetupTextIfAny(GameObject itemGo, AbstractMenuItemInstance item, float itemWidth, float itemHeight)
        {
            var textComp = itemGo.GetComponentInChildren<TMP_Text>();
            if (textComp == null) return;
            SetTextDimensions(textComp, item, itemWidth, itemHeight);
            SetText(textComp, item);
        }

        private void SetupCommentIfAny(GameObject itemGo, AbstractMenuItemInstance item)
        {
            var eventTrigger = itemGo.GetComponent<EventTrigger>();
            if (eventTrigger == null) return;
            var comment = item.GetText(1);
            if (comment.NotNullOrEmpty())
            {
                SetComment(eventTrigger, comment);
            }
        }

        /// <summary>
        /// Item is disabled == grayed out and not interactable
        /// </summary>
        private void ApplyDisabledStateIfNeeded(GameObject itemGo, AbstractMenuItemInstance item)
        {
            if (IsMenuItemActive(item.Name))
                return;

            var textComp = itemGo.GetComponentInChildren<TMP_Text>();
            var eventTrigger = itemGo.GetComponent<EventTrigger>();
            if (textComp != null)
            {
                textComp.color = Constants.TextDisabledColor;
                var button = itemGo.GetComponent<Button>();
                if (button != null)
                {
                    button.enabled = false;
                }
                if (eventTrigger != null)
                {
                    eventTrigger.enabled = false;
                }
            }
            else
            {
                itemGo.SetActive(false);
            }
        }

        private void SetTextDimensions(TMP_Text textComp, AbstractMenuItemInstance item,
            float itemWidth, float itemHeight)
        {
            // frameSizeX/Y are text paddings from left-right and/or top/bottom.
            var textWidth = itemWidth - 2 * item.FrameSizeX;
            var textHeight = itemHeight - 2 * item.FrameSizeY;

            if (item.Flags.HasFlag(MenuItemFlag.Centered))
            {
                textComp.alignment = TextAlignmentOptions.Center;
            }

            // By default, we let text overflow. This ensures we won't have it being wrapped if we have text >1px too long.
            if (item.Flags.HasFlag(MenuItemFlag.Multiline))
            {
                textComp.textWrappingMode = TextWrappingModes.Normal;
            }

            // Text shall always be rendered from top to bottom. Otherwise, e.g. MENUITEM_OPT_HEADING will be rendered y-centered.
            // VerticalAlignment setting stored on Prefab isn't being used by Unity. We therefore need to set it now.
            textComp.verticalAlignment = VerticalAlignmentOptions.Top;

            textComp.spriteAsset = FontService.TryGetFont(item.FontName);
            textComp.fontSize = item.FontName.ToLowerInvariant().Contains("font_old_10_white") ? 16 : 36;

            // Text component needs to align in dimensions with parent rect.
            var textRect = textComp.GetComponent<RectTransform>();

            textRect.SetWidth(textWidth / PixelRatioX);
            textRect.SetHeight(textHeight / PixelRatioY);
        }

        private void SetText(TMP_Text textComp, AbstractMenuItemInstance item)
        {
            var text0 = item.GetText(0);
            
            if (item.MenuItemType == MenuItemType.ChoiceBox)
            {
                // We try to load setting from Ini file.
                var entries = text0.Split("|");
                var defaultIfNoIniExists = (int)item.GetUserFloat(3);
                var entryIndex = ConfigService.Gothic.GetInt(item.OnChgSetOption, defaultIfNoIniExists);
                // We need to ensure that we're not out-of-bounds.
                text0 = entryIndex >= entries.Length ? entries[0] : entries[entryIndex];
            }

            textComp.text = text0;
        }

        private void SetComment(EventTrigger eventTrigger, string comment)
        {
            eventTrigger.triggers.First(i => i.eventID == EventTriggerType.PointerEnter).callback.AddListener(_ => { Comment.text = comment; });
            eventTrigger.triggers.First(i => i.eventID == EventTriggerType.PointerExit).callback.AddListener(_ => { Comment.text = string.Empty; });
        }

        private void SetSliderValues(GameObject go, AbstractMenuItemInstance item)
        {
            var slider = go.GetComponentInChildren<Slider>();

            // e.g., setting of userFloat[0] == 15 --> 16 steps to display on Slider (1...16; both are inclusive).
            // HINT: We shift scale by +1 from 0...15 to 1...16 to properly map it to ini values.
            slider.minValue = 1;
            slider.maxValue = item.GetUserFloat(0) + 1; // Steps
            slider.wholeNumbers = true;

            var stepAmount = 1 / (item.GetUserFloat(0)); // -1 as we use elements from 0...15
            var defaultIfNoIniExists = item.GetUserFloat(3) != 0 ? item.GetUserFloat(3) : 1;
            var currentIniValue = ConfigService.Gothic.GetFloat(item.OnChgSetOption, defaultIfNoIniExists);
            
            // Convert INI value (0...1) to slider value (1...maxValue)
            var sliderValue = Mathf.Round(currentIniValue / stepAmount) + 1; // +1, as minValue == 1, not zero
            slider.value = sliderValue;
            
            // Handle changes
            slider.onValueChanged.AddListener(value => HandleSliderValueChange(item, stepAmount, value));
            
            // Set image for handle bar.
            var handlebarImage = go.GetComponentInChildren<Image>();
            handlebarImage.material = TextureService.GetMaterial(item.GetUserString(0));
        }

        /// <summary>
        /// This function is used as a callback for menu items when they are clicked.
        /// </summary>
        private void HandleMenuItemClick(AbstractMenuItemInstance item)
        {
            for (var i = 0;; i++)
            {
                MenuItemSelectAction action;
                try
                {
                    action = item.GetOnSelAction(i);
                }
                catch (Exception e)
                {
                    break;
                }
        
                if (action == MenuItemSelectAction.Undefined)
                {
                    break;
                }
        
                string actionName = "";
                try
                {
                    actionName = item.GetOnSelActionS(i);
                }
                catch (Exception e)
                {
                    OnMenuItemClicked(action, item.Name, actionName);
                    break;
                }
        
                OnMenuItemClicked(action, item.Name, actionName);
            }
        }

        private void HandleChoiceBoxClick(AbstractMenuItemInstance item, GameObject itemGo)
        {
            var option = item.OnChgSetOption;
            var optionSection = item.OnChgSetOptionSection;

            var options = item.GetText(0).Split("|");
            var textComp = itemGo.GetComponentInChildren<TMP_Text>();
            var currentText = textComp.text;
            var currentIndex = options.IndexOfItem(currentText);
            
            var nextIndex = currentIndex + 1;

            if (currentIndex == -1 || nextIndex >= options.Length)
            {
                ConfigService.Gothic.SetInt(optionSection, option, 0);
                textComp.text = options[0];
            }
            else
            {
                ConfigService.Gothic.SetInt(optionSection, option, currentIndex+1);
                textComp.text = options[currentIndex+1];
            }
        }

        private void HandleSliderValueChange(AbstractMenuItemInstance item, float stepAmount, float value)
        {
            var iniValue = stepAmount * (value - 1); // -1 as we need to normalize back to 0...1
            iniValue = (float)Math.Round(iniValue, 9);
            
            ConfigService.Gothic.SetFloat(item.OnChgSetOptionSection, item.OnChgSetOption, iniValue);
        }
        
        private void OnMenuItemClicked(MenuItemSelectAction action, string itemName, string commandName)
        {
            switch (action)
            {
                case MenuItemSelectAction.Undefined:
                    Undefined(itemName, commandName);
                    break;
                case MenuItemSelectAction.Back:
                    Back(itemName, commandName);
                    break;
                case MenuItemSelectAction.StartMenu:
                    StartMenu(itemName, commandName);
                    break;
                case MenuItemSelectAction.StartItem:
                    StartItem(itemName, commandName);
                    break;
                case MenuItemSelectAction.Close:
                    Close(itemName, commandName);
                    break;
                case MenuItemSelectAction.ConsoleCommand:
                    ConsoleCommand(itemName, commandName);
                    break;
                case MenuItemSelectAction.PlaySound:
                    PlaySound(itemName, commandName);
                    break;
                case MenuItemSelectAction.ExecuteCommand:
                    ExecuteCommand(itemName, commandName);
                    break;
                default:
                    Logger.LogError($"Unknown command {commandName}({action})", LogCat.Ui);
                    break;
            }
        }
    }
}
