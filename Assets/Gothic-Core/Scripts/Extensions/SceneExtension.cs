using Gothic.Core.Const;
﻿using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gothic.Core.Extensions
{
    public static class SceneExtension
    {
        [CanBeNull]
        public static T GetComponentInChildren<T>(this Scene scene, bool includeInactive = false)
        {
            return scene.GetRootGameObjects().Select(i => i.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(i => i != null);
        }

        /// <summary>
        /// A world is loaded and played in it - not boot, menus, loading or videos.
        /// </summary>
        public static bool IsWorldScene(this Scene scene)
        {
            return scene.name is not (Constants.SceneBootstrap or Constants.ScenePlayer or Constants.SceneGameVersion
                or Constants.ScenePreCaching or Constants.SceneLogo or Constants.SceneMainMenu or Constants.SceneLoading);
        }

        [CanBeNull]
        public static GameObject FindChildRecursively(this Scene scene, string name)
        {
            return scene.GetRootGameObjects().Select(i => i.FindChildRecursively(name)).FirstOrDefault(i => i != null);
        }
    }
}
