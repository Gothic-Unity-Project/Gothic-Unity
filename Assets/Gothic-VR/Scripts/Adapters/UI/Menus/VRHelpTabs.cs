using System;
using Gothic.Core.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// Tabs of the help panel above the quest log: topics on the left, the chosen topic's page on the right (like the
    /// quest log). Each time the panel is shown, it starts with the first topic.
    /// </summary>
    public class VRHelpTabs : MonoBehaviour
    {
        [Tooltip("The topic buttons, top to bottom. Same order as the pages.")]
        [SerializeField] private Button[] _tabs = Array.Empty<Button>();

        [Tooltip("The page of each topic. Same order as the tabs.")]
        [SerializeField] private GameObject[] _pages = Array.Empty<GameObject>();

        [Tooltip("Text color of the chosen topic's button.")]
        [SerializeField] private Color _activeTabColor = new(1f, 0.85f, 0.4f);

        [Tooltip("Text color of the other topics' buttons.")]
        [SerializeField] private Color _inactiveTabColor = Color.white;

        private TMP_Text[] _tabLabels;

        private void Awake()
        {
            if (_tabs.Length != _pages.Length)
                Logger.LogWarning($"[HelpPanel] {name}: {_tabs.Length} tabs but {_pages.Length} pages.", LogCat.VR);

            _tabLabels = new TMP_Text[_tabs.Length];
            for (var i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null)
                    continue;

                var index = i;
                _tabs[i].onClick.AddListener(() => Show(index));
                _tabLabels[i] = _tabs[i].GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void OnEnable()
        {
            Show(0);
        }

        /// <summary>
        /// Shows the page of one topic and marks its tab.
        /// </summary>
        public void Show(int index)
        {
            for (var i = 0; i < _pages.Length; i++)
            {
                if (_pages[i] != null)
                    _pages[i].SetActive(i == index);
            }

            if (_tabLabels == null)
                return;

            for (var i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = i == index ? _activeTabColor : _inactiveTabColor;
            }
        }
    }
}
