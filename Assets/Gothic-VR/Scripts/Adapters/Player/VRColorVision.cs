using Gothic.Core;
using Gothic.Core.Models.UI;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.UI;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Applies the Gothic.ini option colorVision (normal, red-, green-, blue-weak) to the headset's camera
    /// (ColorVisionService), once Gothic.ini is loaded and whenever the option changes in the menu.
    /// Sits on the VR player prefab next to VRPlayer.
    /// </summary>
    public class VRColorVision : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ColorVisionService _colorVisionService;

        private IVrRig _rig;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            GlobalEventDispatcher.GothicInisInitialized.AddListener(OnGothicInisInitialized);
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);

            // The rig exists before Gothic.ini is loaded (game version selection): then the event applies it later.
            TryApplySetting();
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.GothicInisInitialized.RemoveListener(OnGothicInisInitialized);
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
        }

        private void OnGothicInisInitialized()
        {
            TryApplySetting();
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.ColorVision)
                TryApplySetting();
        }

        private void TryApplySetting()
        {
            var gothicIni = _configService?.Gothic;
            if (gothicIni == null || _rig?.Camera == null)
                return;

            var mode = (ColorVisionMode)Mathf.Clamp(gothicIni.GetInt(VRConstants.IniNames.ColorVision,
                VRConstants.ColorVisionDefaultValue), 0, (int)ColorVisionMode.Tritanopia);
            _colorVisionService.SetMode(mode, _rig.Camera);
        }
    }
}
