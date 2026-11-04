using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Model.UI.MenuItem;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.UI;
using Gothic.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.Menus
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

        // Ini value of each slider the player is dragging that applies on release (IsAppliedOnRelease).
        private readonly Dictionary<AbstractMenuItemInstance, float> _pendingSliderValues = new();

        // Items with a Page (AbstractMenuItemInstance.Page). Only the current page's items are visible.
        private readonly List<(AbstractMenuItemInstance item, GameObject go)> _pagedItems = new();
        private GameObject _pageUpArrow;
        private GameObject _pageDownArrow;
        private int _currentPage;
        private int _pageCount;
        // Items this menu disabled, with their text color to give back when they get enabled again. Without text,
        // they are hidden instead (others may be hidden by their menu for other reasons).
        private readonly Dictionary<GameObject, Color> _disabledItemColors = new();
        private readonly HashSet<GameObject> _hiddenItems = new();

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
        
        /// <summary>
        /// Whether an item can be used depends on the game (e.g. saving only while the hero is alive in a world), so it
        /// is checked each time the menu shows, not only when it is built.
        /// </summary>
        private void OnEnable()
        {
            foreach (var (item, go) in MenuItemCache.Values)
                ApplyItemState(go, item);
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
            if (backPic.mainTexture == null)
            {
                // Some mod menus (e.g. New Balance's MENU_MAIN under Union) define no backPic.
                // The fallback's pixel size also drives the pixel-ratio math below, i.e. item spacing.
                // The 1024x1024 SaveLoad material packs items ~2x tighter than the 2048x2048 main background.
                backPic = TextureService.MainMenuSaveLoadBackgroundMaterial;
            }

            SetupBackground(backPic);
            SetupCanvasSize(backPic);
            ComputePixelRatios(backPic);
            BuildAllMenuItems();
            SetupPaging();
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
            ApplyItemState(itemGo, item);
        }

        /// <summary>
        /// A menu with more option rows than fit gets up/down arrows right of its rows, one arrow width apart, like
        /// the quest log's text viewer.
        /// </summary>
        private void SetupPaging()
        {
            foreach (var cachedItem in MenuItemCache.Values)
            {
                // A disabled item hidden by ApplyItemState() stays hidden on its page too.
                if (cachedItem.item.Page.HasValue && cachedItem.go.activeSelf)
                    _pagedItems.Add(cachedItem);
            }

            _pageCount = _pagedItems.Count == 0 ? 0 : _pagedItems.Max(i => i.item.Page!.Value) + 1;
            if (_pageCount <= 1)
                return;

            var right = float.MinValue;
            var top = float.MinValue;
            var bottom = float.MaxValue;
            foreach (var (_, go) in _pagedItems)
            {
                var rect = go.GetComponent<RectTransform>();
                right = Mathf.Max(right, rect.anchoredPosition.x + rect.sizeDelta.x / 2);
                top = Mathf.Max(top, rect.anchoredPosition.y + rect.sizeDelta.y / 2);
                bottom = Mathf.Min(bottom, rect.anchoredPosition.y - rect.sizeDelta.y / 2);
            }

            _pageUpArrow = CreatePageArrow("ARROW_UP", TextureService.ArrowUpMaterial, right, top, true,
                () => ShowPage(_currentPage - 1));
            _pageDownArrow = CreatePageArrow("ARROW_DOWN", TextureService.ArrowDownMaterial, right, bottom, false,
                () => ShowPage(_currentPage + 1));

            ShowPage(0);
        }

        private GameObject CreatePageArrow(string arrowName, Material material, float rowsRight, float rowsEdgeY,
            bool isTop, UnityAction onClick)
        {
            var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: arrowName,
                parent: Canvas)!;
            var rect = go.GetComponentInChildren<RectTransform>();
            var rend = go.GetComponentInChildren<MeshRenderer>();
            var button = go.GetComponentInChildren<Button>();

            go.transform.localScale = new Vector3(2, 2, 1);
            rend.sharedMaterial = material;
            button.onClick.AddListener(onClick);

            var arrowWidth = material.mainTexture.width * go.transform.localScale.x;
            var halfArrowHeight = material.mainTexture.height * go.transform.localScale.y / 2;
            rect.SetWidth(material.mainTexture.width);
            rect.SetHeight(material.mainTexture.height);
            // One arrow width of space between the rows and the arrow.
            rect.SetPositionX(rowsRight + arrowWidth * 1.5f);
            rect.SetPositionY(isTop ? rowsEdgeY - halfArrowHeight : rowsEdgeY + halfArrowHeight);

            var position = rect.localPosition;
            position.z = -0.03f;
            rect.localPosition = position;

            return go;
        }

        private void ShowPage(int page)
        {
            _currentPage = Mathf.Clamp(page, 0, _pageCount - 1);

            foreach (var (item, go) in _pagedItems)
                go.SetActive(item.Page == _currentPage);

            _pageUpArrow.SetActive(_currentPage > 0);
            _pageDownArrow.SetActive(_currentPage < _pageCount - 1);

            // The hovered item's pointer exit doesn't fire once it's hidden.
            if (Comment != null)
                Comment.text = string.Empty;
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
                // Gothic's BLEND is plain alpha blending. The premultiplied, alpha-tested transparent mode used before
                // needs a shader variant that builds strip - the G1 menu logo looked different in a build than in the
                // Editor (found on BoroBongo's branch). ADD adds the picture's colors.
                if (item.AlphaMode.EqualsIgnoreCase("ADD"))
                    backPic.ToAdditiveMode();
                else
                    backPic.ToFadeMode();
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
        private void ApplyItemState(GameObject itemGo, AbstractMenuItemInstance item)
        {
            var isActive = IsMenuItemActive(item.Name);
            var textComp = itemGo.GetComponentInChildren<TMP_Text>();
            if (textComp == null)
            {
                if (!isActive && _hiddenItems.Add(itemGo))
                    itemGo.SetActive(false);
                else if (isActive && _hiddenItems.Remove(itemGo))
                    itemGo.SetActive(true);
                return;
            }

            if (!isActive && !_disabledItemColors.ContainsKey(itemGo))
            {
                _disabledItemColors[itemGo] = textComp.color;
                textComp.color = Constants.TextDisabledColor;
            }
            else if (isActive && _disabledItemColors.Remove(itemGo, out var enabledColor))
            {
                textComp.color = enabledColor;
            }
            else
            {
                return;
            }

            var button = itemGo.GetComponent<Button>();
            if (button != null)
                button.enabled = isActive;

            var eventTrigger = itemGo.GetComponent<EventTrigger>();
            if (eventTrigger != null)
                eventTrigger.enabled = isActive;
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
            if (item.IsAppliedOnRelease)
                SetApplyOnRelease(slider, item, stepAmount);
            else
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
            ConfigService.Gothic.SetFloat(item.OnChgSetOptionSection, item.OnChgSetOption,
                GetSliderIniValue(stepAmount, value));
        }

        private static float GetSliderIniValue(float stepAmount, float value)
        {
            var iniValue = stepAmount * (value - 1); // -1 as we need to normalize back to 0...1
            return (float)Math.Round(iniValue, 9);
        }

        /// <summary>
        /// The slider moves while dragging, its value is saved on release (pointer up). The EventTrigger sits on the
        /// Slider itself: it handles the pointer, a trigger on a parent wouldn't get the release.
        /// </summary>
        private void SetApplyOnRelease(Slider slider, AbstractMenuItemInstance item, float stepAmount)
        {
            slider.onValueChanged.AddListener(value =>
                _pendingSliderValues[item] = GetSliderIniValue(stepAmount, value));

            var eventTrigger = slider.GetComponent<EventTrigger>();
            if (eventTrigger == null)
                eventTrigger = slider.gameObject.AddComponent<EventTrigger>();

            var pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            pointerUp.callback.AddListener(_ => ApplyPendingSliderValue(item));
            eventTrigger.triggers.Add(pointerUp);
        }

        private void ApplyPendingSliderValue(AbstractMenuItemInstance item)
        {
            if (!_pendingSliderValues.Remove(item, out var iniValue))
                return;

            ConfigService.Gothic.SetFloat(item.OnChgSetOptionSection, item.OnChgSetOption, iniValue);
        }

        /// <summary>
        /// A menu closed while a slider is still held: keep what the player set.
        /// </summary>
        private void OnDisable()
        {
            foreach (var item in _pendingSliderValues.Keys.ToList())
                ApplyPendingSliderValue(item);
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
