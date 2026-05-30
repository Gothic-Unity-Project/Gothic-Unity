using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Logging;
using GUZ.Core.Model.UI.Menu;
using GUZ.Core.Model.UI.MenuItem;
using GUZ.Core.Models.Caches;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.World;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit;
using ZenKit.Daedalus;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.UI.Menus
{
    /// <summary>
    /// The quest log contains of three major areas:
    /// 1. Left area with clickable elements for e.g. ActiveMissions.
    /// 2. List area where the clickable topics for e.g. ActiveMissions are visible.
    /// 3. Once a list item is clicked on the right, the whole UI changes and shows the topic texts alone.
    ///
    /// |-----------------|----------|            |----------------------------|
    /// | Active Missions | Mission1 |            |  Detail description        |
    /// |-----------------| Mission2 | --click--> |  with all log              |
    /// |                 | ...      |            |  ------------------------  |
    /// |                 |          |            |  entries below each other  |
    /// |-----------------|----------|            |----------------------------|
    ///
    ///
    /// Hint: We need to stick with center for UI elements as setting anchor positions (min=v2(0.5,0.5), max=v2(0.5,0.5))
    ///       Because setting the anchors at runtime (e.g. left-aligned) causes Unity to crash at a certain amount of changes.
    ///       Unfortunately this causes a lot of calculations within this class. But now you know at least. :-)
    /// </summary>
    public class QuestLogMenu : AbstractMenu
    {
        // Create 22 buttons as list elements for all lists
        // TODO - Gothic handled it via FontSize. But for now, we can go with a fixed amount.
        private const int _visibleListItemAmount = 22;
        private const int _listItemHeight = 17;
        private const int _arrowMargin = 12;

        private const string _instanceNameActiveMissions = "MENU_ITEM_LIST_MISSIONS_ACT";
        private const string _instanceNameFailedMissions = "MENU_ITEM_LIST_MISSIONS_FAILED";
        private const string _instanceNameSuccessMissions = "MENU_ITEM_LIST_MISSIONS_OLD";
        private const string _instanceNameLog = "MENU_ITEM_LIST_LOG";
        private const string _instanceContentViewer = "MENU_ITEM_CONTENT_VIEWER";
        private const string _instanceDay = "MENU_ITEM_DAY";
        private const string _instanceTime = "MENU_ITEM_TIME";

        // It's easier to loop through or check .Contains() later.
        private string[] _listItemInstanceNames =
        {
            _instanceNameActiveMissions,
            _instanceNameFailedMissions,
            _instanceNameSuccessMissions,
            _instanceNameLog
        };

        // Sub-elements which need to be disabled initially.
        private static readonly string[] _initiallyDisabledMenuItems =
        {
            _instanceNameActiveMissions, _instanceNameFailedMissions, _instanceNameSuccessMissions,
            _instanceNameLog, _instanceContentViewer
        };

        private Dictionary<string, ListItemContainer> _listMenuCache = new();
        private ListItemContainer _activeListMenu;


        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;


        private class ListItemContainer
        {
            public string InstanceName;
            public AbstractMenuItemInstance Instance;
            public List<SaveLogTopic> LogTopics;
            public GameObject RootGo;
            public GameObject[] ItemGOs;
            public int CurrentListScrollValue; // When we use arrows to move up and down, we store where we are.
            public GameObject ArrowUpGo;
            public GameObject ArrowDownGo;
        }

        private void Awake()
        {
            InitializeMenu(new MenuInstanceAdapter("MENU_LOG", null));
        }

        /// <summary>
        /// When opening the Log for the first time:
        /// 1. Load Daedalus information for menus (UI dimensions, labels, ...)
        /// 3. Use this data to create GameObjects dynamically based on Template elements (button, text, ...)
        /// 3. For lists (e.g. active missions), create the list items and arrows (do not them fill yet)
        /// </summary>
        public override void InitializeMenu(AbstractMenuInstance menuInstance)
        {
            base.InitializeMenu(menuInstance);

            Setup();
            UpdateDayAndTime(_gameTimeService.GetCurrentDateTime());
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(UpdateDayAndTime);
        }

        private void UpdateDayAndTime(DateTime time)
        {
            MenuItemCache[_instanceDay].go.GetComponent<TMP_Text>().SetText(time.Day.ToString());
            MenuItemCache[_instanceTime].go.GetComponent<TMP_Text>().SetText(time.TimeOfDay.ToString(@"hh\:mm"));
        }

        protected override bool IsMenuItemActive(string menuItemName)
        {
            return !_initiallyDisabledMenuItems.Contains(menuItemName);
        }

        /// <summary>
        /// Each time we open the log:
        /// 1. Reset visibility for sub-menus (e.g. if FailedMissions was open last time, we disable it now)
        /// 2. Set current date time (left menu element)
        /// 3. Fill all the mission/note list elements with texts (e.g. all SuccessMissions will get their topic titles)
        /// </summary>
        public override void SetVisible()
        {
            base.SetVisible();
            
            ResetView();
            FillLists();
        }

        private void Setup()
        {
            // Open Gothic also hides it in its screen init
            MenuItemCache[_instanceContentViewer].go.SetActive(false);

            CreateLists();
            CreateContentViewer();
        }

        private void CreateLists()
        {
            foreach (var entryName in _listItemInstanceNames)
            {
                var listEntry = MenuItemCache[entryName];
                var container = new ListItemContainer()
                {
                    InstanceName = entryName,
                    Instance = listEntry.item,
                    RootGo = listEntry.go,
                    ItemGOs = new GameObject[_visibleListItemAmount]
                };

                var halfMainWidth = (float)listEntry.item.DimX / PixelRatioX / 2;
                var halfMainHeight = (float)listEntry.item.DimY / PixelRatioY / 2;

                // Create Item GameObjects (22x)
                for (var i = 0; i < _visibleListItemAmount; i++)
                {
                    var itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButton, name: $"{i}", parent: listEntry.go)!;
                    container.ItemGOs[i] = itemGo;

                    var rect = itemGo.GetComponentInChildren<RectTransform>();
                    var halfItemHeight = (float)_listItemHeight / 2;
                    rect.SetPositionY((halfMainHeight - (i * _listItemHeight) - halfItemHeight));
                    rect.SetHeight(_listItemHeight);
                    rect.SetWidth(listEntry.item.DimX / PixelRatioX);

                    var textRect = itemGo.GetComponentInChildren<TMP_Text>().GetComponent<RectTransform>();
                    textRect.SetWidth((listEntry.item.DimX / PixelRatioX) - _arrowMargin);
                    textRect.SetHeight(_listItemHeight);

                    var button = itemGo.GetComponentInChildren<Button>();

                    var clickIndex = i; // Fixing "Closing over the loop variable" feature. ;-)
                    button.onClick.AddListener(() =>
                    {
                        OnListItemClicked(clickIndex);
                    });
                }

                // Create arrow buttons
                {
                    // UP
                    {
                        var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: "ARROW_UP", parent: listEntry.go)!;
                        var rect = go.GetComponentInChildren<RectTransform>();
                        var rend = go.GetComponentInChildren<MeshRenderer>();
                        var button = go.GetComponentInChildren<Button>();

                        go.transform.localScale = new Vector3(2, 2, 1);
                        container.ArrowUpGo = go;
                        rend.sharedMaterial = TextureService.ArrowUpMaterial;
                        button.onClick.AddListener(OnArrowUpClick);

                        rect.SetWidth(rend.sharedMaterial.mainTexture.width);
                        rect.SetHeight(rend.sharedMaterial.mainTexture.height);
                        rect.SetPositionX(halfMainWidth - _arrowMargin);
                        rect.SetPositionY(halfMainHeight - _arrowMargin);
                    }

                    // DOWN
                    {
                        var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: "ARROW_DOWN", parent: listEntry.go)!;
                        var rect = go.GetComponentInChildren<RectTransform>();
                        var rend = go.GetComponentInChildren<MeshRenderer>();
                        var button = go.GetComponentInChildren<Button>();

                        go.transform.localScale = new Vector3(2, 2, 1);
                        container.ArrowDownGo = go;
                        rend.sharedMaterial = TextureService.ArrowDownMaterial;
                        button.onClick.AddListener(OnArrowDownClick);

                        rect.SetWidth(rend.sharedMaterial.mainTexture.width);
                        rect.SetHeight(rend.sharedMaterial.mainTexture.height);
                        rect.SetPositionX(halfMainWidth - _arrowMargin);
                        rect.SetPositionY(-halfMainHeight + _arrowMargin);
                    }
                }

                _listMenuCache[entryName] = container;
            }
        }

        private void CreateContentViewer()
        {
            var contentViewer = MenuItemCache[_instanceContentViewer];
            var textComp = contentViewer.go.GetComponentInChildren<TMP_Text>();
            var textRect = textComp.GetComponent<RectTransform>();
            var halfTextWidth = textRect.sizeDelta.x / 2;
            var halfTextHeight = textRect.sizeDelta.y / 2;

            textComp.overflowMode = TextOverflowModes.Page;
            textComp.pageToDisplay = 0;

            // UP
            {
                var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: "ARROW_UP", parent: contentViewer.go)!;
                var rect = go.GetComponentInChildren<RectTransform>();
                var rend = go.GetComponentInChildren<MeshRenderer>();
                var button = go.GetComponentInChildren<Button>();

                go.transform.localScale = new Vector3(2, 2, 1);
                rend.sharedMaterial = TextureService.ArrowUpMaterial;
                button.onClick.AddListener(OnContentViewerArrowUpClick);

                rect.SetWidth(rend.sharedMaterial.mainTexture.width);
                rect.SetHeight(rend.sharedMaterial.mainTexture.height);
                rect.SetPositionX(halfTextWidth + rend.sharedMaterial.mainTexture.width);
                rect.SetPositionY(halfTextHeight + rend.sharedMaterial.mainTexture.height);
            }

            // DOWN
            {
                var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: "ARROW_DOWN", parent: contentViewer.go)!;
                var rect = go.GetComponentInChildren<RectTransform>();
                var rend = go.GetComponentInChildren<MeshRenderer>();
                var button = go.GetComponentInChildren<Button>();

                go.transform.localScale = new Vector3(2, 2, 1);
                rend.sharedMaterial = TextureService.ArrowDownMaterial;
                button.onClick.AddListener(OnContentViewerArrowDownClick);

                rect.SetWidth(rend.sharedMaterial.mainTexture.width);
                rect.SetHeight(rend.sharedMaterial.mainTexture.height);
                rect.SetPositionX(halfTextWidth + rend.sharedMaterial.mainTexture.width);
                rect.SetPositionY(-halfTextHeight - rend.sharedMaterial.mainTexture.height);
            }

            // BACK
            {
                var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiButtonTextured, name: "ARROW_BACK", parent: contentViewer.go)!;
                var rect = go.GetComponentInChildren<RectTransform>();
                var rend = go.GetComponentInChildren<MeshRenderer>();
                var button = go.GetComponentInChildren<Button>();

                go.transform.localScale = new Vector3(2, 2, 1);
                rend.sharedMaterial = TextureService.ArrowLeftMaterial;
                button.onClick.AddListener(OnContentViewerBackClick);

                rect.SetWidth(rend.sharedMaterial.mainTexture.width);
                rect.SetHeight(rend.sharedMaterial.mainTexture.height);
                rect.SetPositionX(-halfTextWidth - rend.sharedMaterial.mainTexture.width);
                rect.SetPositionY(halfTextHeight + rend.sharedMaterial.mainTexture.height);
            }
        }

        /// <summary>
        /// Alter visibility: Deactivate ContentViewer and reactivate normal menu + currently active sub-menu (list)
        /// </summary>
        private void OnContentViewerBackClick()
        {
            var contentViewer = MenuItemCache[_instanceContentViewer];
            contentViewer.go.SetActive(false);

            _activeListMenu.RootGo.SetActive(true);
            Background.SetActive(true);

            foreach (var menuItem in MenuItemCache)
            {
                if (_initiallyDisabledMenuItems.Contains(menuItem.Key))
                {
                    continue;
                }

                menuItem.Value.go.SetActive(true);
            }
        }

        private void OnContentViewerArrowUpClick()
        {
            var contentViewer = MenuItemCache[_instanceContentViewer];

            contentViewer.go.GetComponentInChildren<TMP_Text>().pageToDisplay -= 1;
        }

        private void OnContentViewerArrowDownClick()
        {
            var contentViewer = MenuItemCache[_instanceContentViewer];

            contentViewer.go.GetComponentInChildren<TMP_Text>().pageToDisplay += 1;
        }

        private void ResetView()
        {
            foreach (var list in _listMenuCache.Values)
            {
                list.CurrentListScrollValue = 0;
                list.ItemGOs.ForEach(i => i.SetActive(false));
            }

            // Reset visibility for List menus
            foreach (var list in _listMenuCache.Values)
            {
                list.RootGo.SetActive(false);
                list.ArrowUpGo.SetActive(false);
                list.ArrowDownGo.SetActive(false);
            }
        }

        private void FillLists()
        {
            _listMenuCache[_instanceNameActiveMissions].LogTopics = _storyService.GetLogTopics(SaveTopicSection.Missions, SaveTopicStatus.Running);
            _listMenuCache[_instanceNameSuccessMissions].LogTopics = _storyService.GetLogTopics(SaveTopicSection.Missions, SaveTopicStatus.Success);
            _listMenuCache[_instanceNameFailedMissions].LogTopics = _storyService.GetLogTopics(SaveTopicSection.Missions, SaveTopicStatus.Failure);
            _listMenuCache[_instanceNameLog].LogTopics = _storyService.GetLogTopics(SaveTopicSection.Notes, SaveTopicStatus.Free);

            foreach (var list in _listMenuCache.Values)
            {
                FillList(list);
            }
        }

        private void FillList(ListItemContainer list)
        {
            for (var i = 0; i < Math.Min(_visibleListItemAmount, list.LogTopics.Count); i++)
            {
                var logItemOffset = i + list.CurrentListScrollValue;
                list.ItemGOs[i].SetActive(true);
                list.ItemGOs[i].GetComponentInChildren<TMP_Text>().text = list.LogTopics[logItemOffset].Description;
            }

            list.ArrowUpGo.SetActive(list.CurrentListScrollValue > 0);
            list.ArrowDownGo.SetActive(list.LogTopics.Count - _visibleListItemAmount - list.CurrentListScrollValue > 0);
        }

        private void OnMenuItemClicked(MenuItemSelectAction action, string itemName, string commandName)
        {
            switch (action)
            {
                case MenuItemSelectAction.ExecuteCommand:
                    ExecuteCommand(itemName, commandName);
                    break;
                default:
                    Logger.LogError($"Unknown command {commandName}({action})", LogCat.Ui);
                    break;
            }
        }

        private void OnListItemClicked(int index)
        {
            var realIndex = index + _activeListMenu.CurrentListScrollValue;
            var text = _activeListMenu.LogTopics[realIndex].Entries.Aggregate((i, j) => i + "\n---\n" + j);

            // Disable everything...
            foreach (var menuItem in MenuItemCache.Values)
            {
                UIEventsService.SetDefaultFontsForChildren(menuItem.go);
                menuItem.go.SetActive(false);
            }

            // ...including background
            Background.SetActive(false);

            var contentViewer = MenuItemCache[_instanceContentViewer].go;
            contentViewer.GetComponentInChildren<TMP_Text>().text = text;
            contentViewer.SetActive(true);
        }

        private void OnArrowUpClick()
        {
            --_activeListMenu.CurrentListScrollValue;

            FillList(_activeListMenu);
        }

        public void OnArrowDownClick()
        {
            Logger.LogEditor($"OnArrowDownClick()", LogCat.Ui);
            ++_activeListMenu.CurrentListScrollValue;

            FillList(_activeListMenu);
        }

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
            ResetView();
            FillLists();

            var split = commandName.Split(' ');

            // Some commands are named like "EFFECTS MENU_ITEM_XYZ". We remove the unnecessary prefix (e.g. EFFECT).
            var menuItemName = split.Last();

            var itemCache = MenuItemCache[menuItemName];
            itemCache.go.SetActive(true);

            // If the Command==ListMenu, then we set it as currently active.
            _listMenuCache.TryGetValue(menuItemName, out _activeListMenu);
        }
    }
}
