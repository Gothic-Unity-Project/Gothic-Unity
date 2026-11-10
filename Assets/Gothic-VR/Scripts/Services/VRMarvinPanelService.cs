using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.Marvin;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// The Marvin panel of the VR player (MarvinRootHandler, docked at the left hand): shown while Marvin mode is on
    /// (MarvinService is the only owner of that state) and, with DeveloperConfig.EnableUiGrabBars, movable by its
    /// grab bar and docked back to the hand.
    /// </summary>
    public class VRMarvinPanelService
    {
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly ConfigService _configService;

        private MarvinRootHandler _marvinRoot;

        public void Init()
        {
            GlobalEventDispatcher.MarvinModeChanged.AddListener(_ => ApplyVisibility());
        }

        /// <summary>
        /// Once per new VR player. playerRoot: searched inside our player only (other players are destroyed at the
        /// end of this frame). playerSpace: where the panel floats once taken off the hand.
        /// </summary>
        public void Setup(GameObject playerRoot, Transform playerSpace)
        {
            _marvinRoot = playerRoot.GetComponentInChildren<MarvinRootHandler>(true)!;
            if (_configService.Dev.EnableUiGrabBars)
                AddGrabBar(playerSpace);
            ApplyVisibility();
        }

        /// <summary>
        /// DeveloperConfig.EnableUiGrabBars: grabbed, the Marvin panel leaves the left hand and floats where it's put
        /// (it lazily follows the head) - one element with its keyboard (it follows the panel's KeyboardAnchor), so the
        /// bar is upright at the side. Released in the sphere above the left hand, it docks back.
        /// </summary>
        private void AddGrabBar(Transform playerSpace)
        {
            var panel = _marvinRoot.transform;
            var hand = panel.parent;
            var dockedPosition = panel.localPosition;
            var dockedRotation = panel.localRotation;
            var dockedScale = panel.localScale;

            // Its prefab places it where the panel meets its keyboard (KeyboardAnchor below the panel): one bar for
            // both.
            _marvinRoot.GrabBar.Setup(panel,
                () =>
                {
                    if (panel.parent != playerSpace)
                        panel.SetParent(playerSpace, true);
                },
                hand,
                () =>
                {
                    panel.SetParent(hand, false);
                    panel.SetLocalPositionAndRotation(dockedPosition, dockedRotation);
                    panel.localScale = dockedScale;
                    Logger.Log("[VRMarvinPanelService] Marvin panel docked to the hand", LogCat.VR);
                },
                true);
        }

        /// <summary>
        /// The Marvin panel shows while Marvin mode is on. The panel can't do it itself: it is inactive then.
        /// </summary>
        private void ApplyVisibility()
        {
            if (_marvinRoot == null)
                return;

            var isShown = _marvinService.IsMarvinMode;
            if (_marvinRoot.gameObject.activeSelf == isShown)
                return;
            _marvinRoot.gameObject.SetActive(isShown);
            Logger.Log($"[VRMarvinPanelService] Marvin panel {(isShown ? "shown" : "hidden")}", LogCat.VR);
        }
    }
}
