using GUZ.Core;
using GUZ.Core.Adapters.UI;
using GUZ.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Adapters.Marvin
{
    public class MarvinRootHandler : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        [SerializeField] ToggleButton _toggleButtonLogs;
        [SerializeField] ToggleButton _toggleButtonMarvin;
        [SerializeField] ToggleButton _toggleButtonInspector;
        
        [SerializeField] private GameObject _tabLogs;
        [SerializeField] private GameObject _tabMarvin;
        [SerializeField] private GameObject _tabInspector;

        private void Start()
        {
            ResetTabs();

            if (!_configService.Dev.ActivateMarvinMode)
                gameObject.SetActive(false);
        }

        public void OnLogsTabClicked()
        {
            ResetTabs();
            _tabLogs.SetActive(true);
            _toggleButtonLogs.SetActive();
        }

        public void OnMarvinTabClicked()
        {
            ResetTabs();
            _tabMarvin.SetActive(true);
            _toggleButtonMarvin.SetActive();
        }

        public void OnTabInspectorClicked()
        {
            ResetTabs();
            _tabInspector.SetActive(true);
            _toggleButtonInspector.SetActive();
        }

        private void ResetTabs()
        {
            _tabLogs.SetActive(false);
            _tabMarvin.SetActive(false);
            _tabInspector.SetActive(false);
            
            _toggleButtonLogs.SetInactive();
            _toggleButtonMarvin.SetInactive();
            _toggleButtonInspector.SetInactive();
        }
    }
}
