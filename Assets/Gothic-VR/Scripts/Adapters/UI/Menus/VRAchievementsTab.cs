using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// The "Achievements" page of the help panel: loads the Core achievements page (PrefabType.UiAchievementsPage)
    /// into this page the first time it's shown. The page refreshes itself whenever it's shown.
    /// </summary>
    public class VRAchievementsTab : MonoBehaviour
    {
        [Tooltip("Scale of the Core page (Canvas units) inside this help page.")]
        [SerializeField] private float _pageScale = 0.01f;

        private bool _isCreated;

        private void OnEnable()
        {
            if (_isCreated)
                return;
            _isCreated = true;

            var resourceCacheService = ReflexProjectInstaller.DIContainer.Resolve<ResourceCacheService>();
            var page = resourceCacheService.TryGetPrefabObject(PrefabType.UiAchievementsPage, parent: gameObject);
            if (page == null)
            {
                Logger.LogWarning("[VRAchievementsTab] No achievements page prefab.", LogCat.VR);
                return;
            }

            page.transform.localScale = new Vector3(_pageScale, _pageScale, 1f);
        }
    }
}
