using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GUZ.Core.Extensions
{
    public static class SceneExtension
    {
        [CanBeNull]
        public static T GetComponentInChildren<T>(this Scene scene, bool includeInactive = false)
        {
            return scene.GetRootGameObjects().Select(i => i.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(i => i != null);
        }

        [CanBeNull]
        public static GameObject FindChildRecursively(this Scene scene, string name)
        {
            return scene.GetRootGameObjects().Select(i => i.FindChildRecursively(name)).FirstOrDefault(i => i != null);
        }
    }
}
