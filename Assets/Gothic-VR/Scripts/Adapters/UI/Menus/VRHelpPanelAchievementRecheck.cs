using Gothic.Core.Services.Achievements;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// On the help panel: each time it opens, achievements re-check the story variables, so one changed by a script we
    /// don't hook counts now at the latest (a fallback moment, Docs/achievements.md).
    /// </summary>
    public class VRHelpPanelAchievementRecheck : MonoBehaviour
    {
        [Inject] private readonly AchievementService _achievementService;

        private void OnEnable()
        {
            // Not injected on the panel's first enable, before the player prefab is injected - there is no world yet.
            if (_achievementService != null)
                _achievementService.RecheckNow();
        }
    }
}
