using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Adapters.UI;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UberLogger;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ILogger = UberLogger.ILogger;
using Logger = UberLogger.Logger;
using PrefabType = GUZ.Core.Models.Caches.PrefabType;

namespace GUZ.VR.Adapters.Marvin
{
    public class MarvinLogsTabHandler : MonoBehaviour, ILogger
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;


        [SerializeField] private ScrollRect _logsRoot;
        [SerializeField] private RectTransform _logContentContainer;
        
        [SerializeField] private Button _activateLoggingButton;
        [SerializeField] private ToggleButton[] _severityFilterButtons;
        
        [SerializeField] private RectTransform _categoriesRoot;
        
        [SerializeField] private Texture2D _messageIcon;
        [SerializeField] private Texture2D _warningIcon;
        [SerializeField] private Texture2D _errorIcon;
        

        private bool _isLoggingActive;
        private List<LogInfo> _logItems = new();

        private bool _isSeverityFiltered;
        private LogSeverity _severityFilter;

        private List<ToggleButton> _categoryFilterButtons = new();
        private bool _isCategoryFiltered;
        private LogCat _categoryFilter;
        private bool _didCategoryFilterChange;

        private const int _categoryFilterMarginTop = 10;
        private const int _logLineHeight = 15;
        private int _logCount;
        
        private void Start()
        {
            // TODO - For now, we can only enable MarvinMode via DevConfig. Once it can be activated at runtime via gesture, we should remove this line.
            if (!_configService.Dev.ActivateMarvinMode)
                return;
            
            Logger.AddLogger(this);
            _severityFilterButtons.ForEach(i => i.SetActive());

            // TODO - Really needed?
            _logsRoot.normalizedPosition = new Vector2(0, 0);

            FillCategories();

            // Debug
            // Logger.LogWarning("SPAM", LogCat.Ai);
            // Logger.LogWarning("SPAM", LogCat.Ai);
            // Logger.LogWarning("SPAM", LogCat.Ai);
            // Logger.LogError("SPAM-Error", LogCat.Debug);
            // Logger.LogError("SPAM-Error", LogCat.Debug);
            // Logger.LogError("SPAM-Error", LogCat.Debug);
        }
      
#if UNITY_EDITOR
        /// <summary>
        /// We need to disable Logging (and especially creating new GameObjects) when we recompile at runtime (Editor only).
        /// Otherwise, Unity will crash.
        /// </summary>
        [InitializeOnLoadMethod]
        static void InitializeEditor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                // Find and remove this logger from UberLogger
                Logger.RemoveLogger(typeof(MarvinLogsTabHandler).FullName);
            };
        }
#endif

        private void Update()
        {
            if (_didCategoryFilterChange)
            {
                _didCategoryFilterChange = false;
                LogItemFilterUpdated();
            }
        }

        public void OnToggleLoggingClick()
        {
            _isLoggingActive = !_isLoggingActive;

            if (_isLoggingActive)
            {
                LogItemFilterUpdated();
                _activateLoggingButton.GetComponentInChildren<TMP_Text>().text = "De-activate";
            }
            else
            {
                _activateLoggingButton.GetComponentInChildren<TMP_Text>().text = "Activate";
                // HINT: We do not clear the list of log items. But basically "stop" them from being updated.
            }
        }

        public void OnSeverityLoggingClick(int severityIndex)
        {
            _severityFilterButtons.ForEach(i => i.SetInactive());
            _severityFilterButtons[severityIndex].SetActive();
            
            _isSeverityFiltered = true;
            _severityFilter = (LogSeverity)severityIndex;
            LogItemFilterUpdated();
        }

        private void FillCategories()
        {
            var defaultCategories = new[] { "Everything" };
            
            var catIndex = 0;
            foreach (var category in defaultCategories.Concat(Enum.GetNames(typeof(LogCat))))
            {
                var categoryGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugToggleButton, name: category, parent: _categoriesRoot.gameObject);
                var categoryTransform = categoryGo!.GetComponent<RectTransform>();
                categoryTransform.localPosition = new Vector2(0, -_categoryFilterMarginTop - (_logLineHeight / 2f) - _logLineHeight * catIndex);
                categoryTransform.sizeDelta = new Vector2(_categoriesRoot.rect.width - 25f, _logLineHeight);
                
                var buttonComp = categoryGo.GetComponentInChildren<ToggleButton>();
                buttonComp.onClick.AddListener(() => OnCategoryClick(category));
                
                var textComp =  categoryGo.GetComponentInChildren<TMP_Text>();
                textComp.text = category;

                _categoryFilterButtons.Add(buttonComp);
                catIndex++;
                
                if (category == "Everything")
                {
                    buttonComp.SetActive();
                    
                    // One empty line to separate "special" categories from others.
                    catIndex++;
                }
            }
        }
        
        private void OnCategoryClick(string category)
        {
            _didCategoryFilterChange = true;
            _categoryFilterButtons.ForEach(i => i.SetInactive());
            
            if (Enum.TryParse<LogCat>(category, out var categoryResult))
            {
                _categoryFilter = categoryResult;
                _isCategoryFiltered = true;
                _categoryFilterButtons[(int)categoryResult + 1].SetActive(); // +1 as the first index is "Everything" 
            }
            // Special type: "Everything"
            else
            {
                _isCategoryFiltered = false;
                _categoryFilterButtons[0].SetActive(); // 0 == Everything 

                // We also reset it as it's sufficient for now to reset all when "Everything" is clicked.
                _isSeverityFiltered = false;
                _severityFilterButtons.ForEach(i => i.SetActive());
            }
        }

        /// <summary>
        /// We always track log elements, but we create GameObjects only if the logging feature is active.
        /// As storing 500 lines of text is no problem, but creating and constantly changing text fields in UI.
        /// </summary>
        public void Log(LogInfo logInfo)
        {
            _logItems.Add(logInfo);
            
            // Remove the oldest entry if we're "full"
            if (_logItems.Count > 500)
                _logItems.RemoveAt(0);
 
            // Only add element if it matches the current filter.
            if (ShouldPrintLogEntry(logInfo))
                AddTextItem(logInfo);
        }

        private bool ShouldPrintLogEntry(LogInfo logInfo)
        {
            if (!_isLoggingActive)
                return false;
            if (_isSeverityFiltered && logInfo.Severity != _severityFilter)
                return false;
            if (_isCategoryFiltered && !logInfo.Channel.EqualsIgnoreCase(_categoryFilter.ToString()))
                return false;

            return true;
        }
        
        private void AddTextItem(LogInfo logInfo)
        {
            var itemGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugLogLine, name: "LogItem", parent: _logContentContainer.gameObject);
            var itemTransform = itemGo!.GetComponent<RectTransform>();
            itemTransform.localScale = Vector3.one; // 0 when instantiating.
            itemTransform.anchorMin = new Vector2(0, 1);
            itemTransform.anchorMax = new Vector2(0, 1);
            // Offset: Half of text height - itemIndex*itemHeight
            itemTransform.localPosition = new Vector2(0, -(_logLineHeight / 2f) - _logLineHeight * _logCount);

            var message = FormatLogText(logInfo);
            
            var textComp = itemGo.GetComponentInChildren<TMP_Text>();
            var textRectComp = textComp.GetComponent<RectTransform>();
            var preferredSize = textComp.GetPreferredValues(message); // Calculate size for message based on 
            textComp.text = message;
            textRectComp.sizeDelta = new Vector2(preferredSize.x, _logLineHeight);
            textRectComp.localPosition = new Vector2(preferredSize.x / 2, 0);

            var image = itemGo.GetComponentInChildren<RawImage>();
            var imageRectComp = image.GetComponent<RectTransform>();
            imageRectComp.localPosition = new Vector2(10, 0);
            image.texture = logInfo.Severity switch
            {
                LogSeverity.Message => _messageIcon,
                LogSeverity.Warning => _warningIcon,
                LogSeverity.Error => _errorIcon,
                _ => throw new ArgumentOutOfRangeException()
            };
            
            // Enlarge content view to show horizontal scroll bar if the entry is bigger than the last one.
            if (_logContentContainer.rect.width < preferredSize.x)
                _logContentContainer.sizeDelta = new Vector2(preferredSize.x, _logContentContainer.rect.height);

            // Height: Half of text height (for offset) + itemCount*itemHeight
            _logContentContainer.sizeDelta = new Vector2(_logContentContainer.sizeDelta.x, (_logLineHeight / 2f) + (_logCount + 1) * _logLineHeight);
            _logCount++;
        }

        private string FormatLogText(LogInfo log)
        {
            var showChannel = !string.IsNullOrEmpty(log.Channel);
            var channelMessage = showChannel ? string.Format("[{0}]", log.Channel) : "";
            var channelTimeSeparator = showChannel ? " " : "";
            var timeMessage = string.Format("{0}", log.GetRelativeTimeStampAsString());
            var prefixMessageSeparator = " : ";
            
            return string.Format("{0}{1}{2}{3}{4}",
                channelMessage,
                channelTimeSeparator,
                timeMessage,
                prefixMessageSeparator,
                log.Message
            );
        }
        
        private void LogItemFilterUpdated()
        {
            // Remove all existing log items
            foreach (Transform child in _logContentContainer)
                Destroy(child.gameObject);

            // Reset counters and size
            _logCount = 0;
            _logContentContainer.sizeDelta = new Vector2(_logContentContainer.sizeDelta.x, 0);

            // Re-add filtered items
            foreach (var logInfo in _logItems)
            {
                if (ShouldPrintLogEntry(logInfo))
                    AddTextItem(logInfo);
            }
        }
    }
}
