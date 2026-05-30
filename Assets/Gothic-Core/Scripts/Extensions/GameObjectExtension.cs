using System;
using System.Linq;
using GUZ.Core.Logging;
using JetBrains.Annotations;
using Reflex.Injectors;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Extensions
{
    public static class GameObjectExtension
    {
        /// <summary>
        /// Execute on newly created GameObject and its children to execute DI injection.
        /// Please use it only, when needed as it causes some CPU cycles when done.
        ///
        /// Checks for [Inject] properties and methods.
        /// </summary>
        public static GameObject Inject(this GameObject instance)
        {
            GameObjectInjector.InjectRecursive(instance, ReflexProjectInstaller.DIContainer);
            return instance;
        }

        public static void SetParent(this GameObject obj, GameObject parent, bool resetLocation = false,
            bool resetRotation = false, bool worldPositionStays = false)
        {
            if (parent != null)
            {
                obj.transform.SetParent(parent.transform, worldPositionStays);
            }

            // FIXME - I don't know why, but Unity adds location, rotation, and scale to newly attached sub elements.
            // This is how we clean it up right now.
            if (resetLocation)
            {
                obj.transform.localPosition = Vector3.zero;
            }

            if (resetRotation)
            {
                obj.transform.localRotation = Quaternion.identity;
            }
        }

        [CanBeNull]
        public static Transform FindChildRecursively(this Transform transform, string name)
        {
            return transform.gameObject.FindChildRecursively(name)?.transform;
        }

        public static GameObject FindChildRecursively(this GameObject go, string name)
        {
            if (go == null)
            {
                Logger.LogError("Empty GameObject provided.", LogCat.Misc);
                return null;
            }
            
            Transform result;
            try
            {
                result = go.transform.Find(name);
            }
            catch (Exception)
            {
                Logger.LogError($"Couldn't find GameObject with name >{name}< in parent >{go.name}<", LogCat.Misc);
                return null;
            }

            // The child object was found and isn't ourself
            if (result != null && result != go.transform)
            {
                return result.gameObject;
            }

            // Search recursively in the children of the current object
            foreach (Transform child in go.transform)
            {
                var resultGo = child.gameObject.FindChildRecursively(name);

                // The child object was found in a recursive call
                if (resultGo != null)
                {
                    return resultGo;
                }
            }

            // The child object was not found
            return null;
        }

        public static void ToggleActive(this GameObject go)
        {
            go.SetActive(!go.activeSelf);
        }

        /// <summary>
        /// Returns direct Children of a GameObject. Non-recursive! 
        /// </summary>
        public static GameObject[] GetAllDirectChildren(this GameObject go)
        {
            return Enumerable
                .Range(0, go.transform.childCount)
                .Select(i => go.transform.GetChild(i).gameObject)
                .ToArray();
        }

        /// <summary>
        /// Either add or get existing component on GameObject.
        /// </summary>
        public static T TryAddComponent<T>(this GameObject go) where T : Component
        {
            if (go.TryGetComponent<T>(out var component))
            {
                return component;
            }

            return go.AddComponent<T>();
        }
    }
}
