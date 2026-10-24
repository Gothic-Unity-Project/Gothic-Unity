using System.Collections.Generic;
using Gothic.Core;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Adapters.UI;

namespace Gothic.VR.Services
{
    /// <summary>
    /// VR hands above on-top UI (VRUiOnTop), so we see them when pointing at a dialog option or menu entry. Only while
    /// a dialog or the game menu is open - otherwise they covered letters/documents held in front of them.
    /// Each UI requests it separately: closing the menu during a dialog keeps the hands on top until the dialog ends.
    /// </summary>
    public class VRUiOnTopService
    {
        private static readonly object _menuRequester = new();

        private readonly HashSet<object> _handsOnTopRequesters = new();
        private VRHandsOnTop _hands;

        public VRUiOnTopService()
        {
            GlobalEventDispatcher.MenuOpened.AddListener(_ => SetHandsOnTop(_menuRequester, true));
            GlobalEventDispatcher.MenuClosed.AddListener(() => SetHandsOnTop(_menuRequester, false));

            // A scene change in the middle of a dialog never calls EndDialog - put the hands back.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() =>
            {
                _handsOnTopRequesters.Clear();
                UpdateHands();
                VRUiOnTop.ReleaseDeadMaterials();
            });
        }

        public void RegisterHands(VRHandsOnTop hands)
        {
            _hands = hands;
            UpdateHands();
        }

        /// <param name="requester">The UI which needs the hands on top, e.g. the dialog box.</param>
        public void SetHandsOnTop(object requester, bool isOnTop)
        {
            if (isOnTop)
                _handsOnTopRequesters.Add(requester);
            else
                _handsOnTopRequesters.Remove(requester);

            UpdateHands();
        }

        private void UpdateHands()
        {
            if (_hands != null)
                _hands.SetOnTop(_handsOnTopRequesters.Count > 0);
        }
    }
}
