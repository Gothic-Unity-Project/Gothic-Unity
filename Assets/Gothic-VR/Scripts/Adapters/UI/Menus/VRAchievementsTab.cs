using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// The "Achievements" page of the help panel: loads the Core achievements page (PrefabType.UiAchievementsPage)
    /// into this page the first time it's shown. The page refreshes itself whenever it's shown.
    /// The in-game menu is drawn on top of the world (VRUiOnTop), but its pass runs before this page exists: the page's
    /// pictures (icons, line backgrounds, scrollbar) are lifted on every show, else the on-top background covers them.
    /// </summary>
    public class VRAchievementsTab : MonoBehaviour
    {
        [Tooltip("Scale of the Core page (Canvas units) inside this help page.")]
        [SerializeField] private float _pageScale = 0.01f;

        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private bool _isCreated;
        private bool _isOnTopPending;

        private void OnEnable()
        {
            // Not injected yet: the help panel's first enable, before the player prefab is injected (no page shown).
            if (_resourceCacheService == null)
                return;

            // In LateUpdate: after the page's own OnEnable, which may clone new lines.
            _isOnTopPending = true;

            if (_isCreated)
                return;
            _isCreated = true;

            var page = _resourceCacheService.TryGetPrefabObject(PrefabType.UiAchievementsPage, parent: gameObject);
            if (page == null)
            {
                Logger.LogWarning("[VRAchievementsTab] No achievements page prefab.", LogCat.VR);
                return;
            }

            page.transform.localScale = new Vector3(_pageScale, _pageScale, 1f);
        }

        private void LateUpdate()
        {
            if (!_isOnTopPending)
                return;
            _isOnTopPending = false;

            // None in the main menu's help panel: it isn't drawn on top of a world.
            var onTop = GetComponentInParent<VRUiOnTop>();
            if (onTop != null)
                onTop.Apply();
        }
    }
}
