using System;
using UnityEngine;
using ZenKit;

namespace GUZ.Core.Extensions
{
    public static class NumericsExtension
    {
        public static Vector2 ToUnityVector(this System.Numerics.Vector2 vector2)
        {
            return new Vector2
            {
                x = vector2.X,
                y = vector2.Y
            };
        }

        /// <summary>
        /// Transform Vector3 to Unity Vector3.
        /// cmScale - Gothic positions are in cm, but Unity in m. (factor 100). Most of the time we just transform it directly.
        /// </summary>
        public static Vector3 ToUnityVector(this System.Numerics.Vector3 vector3, bool cmScale = true)
        {
            var vector = new Vector3
            {
                x = vector3.X,
                y = vector3.Y,
                z = vector3.Z
            };

            if (cmScale)
                return vector / 100;
            else
                return vector;
        }

        public static System.Numerics.Vector3 ToNumericsVector(this Vector3 vector3)
        {
            return new System.Numerics.Vector3(vector3.x * 100, vector3.y * 100, vector3.z * 100);
        }

        public static Bounds ToUnityBounds(this AxisAlignedBoundingBox bounds)
        {
            var max = bounds.Max.ToUnityVector();
            var min = bounds.Min.ToUnityVector();

            var boundsChord = max - min;
            var unityBounds = new Bounds(min + boundsChord.normalized * boundsChord.magnitude * .5f,
                new Vector3(Mathf.Abs(max.x - min.x),
                    Mathf.Abs(max.y - min.y),
                    Mathf.Abs(max.z - min.z)));

            return unityBounds;
        }

        public static Color ToUnityColor(this System.Numerics.Vector3 vector3, float alpha = 1)
        {
            return new Color
            {
                r = vector3.X,
                g = vector3.Y,
                b = vector3.Z,
                a = alpha
            };
        }

        public static Color ToUnityColor(this Vector3 vector3, float alpha = 1)
        {
            return new Color
            {
                r = vector3.x,
                g = vector3.y,
                b = vector3.z,
                a = alpha
            };
        }

        public static Vector3[] ToUnityArray(this System.Numerics.Vector3[] array)
        {
            return Array.ConvertAll(array, item => new Vector3
            {
                x = item.X,
                y = item.Y,
                z = item.Z
            });
        }

        public static Vector2[] ToUnityArray(this System.Numerics.Vector2[] array)
        {
            return Array.ConvertAll(array, item => new Vector2
            {
                x = item.X,
                y = item.Y
            });
        }
    }
}
