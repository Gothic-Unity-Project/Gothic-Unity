using UnityEngine;
using UnityEngine.UI;
using ZenKit.Util;

namespace Gothic.Core.Extensions
{
    public static class UnityExtension
    {
        private static readonly Rect _flippedUv = new(0f, 1f, 1f, -1f);
        private static readonly Rect _uprightUv = new(0f, 0f, 1f, 1f);

        /// <summary>
        /// Gothic textures are uploaded top row first - a RawImage shows them upside down unless flipped. Textures
        /// Unity rendered itself (e.g. ItemIconService) are already upright.
        /// </summary>
        public static void SetUpright(this RawImage image, bool isGothicTexture)
        {
            image.uvRect = isGothicTexture ? _flippedUv : _uprightUv;
        }

        /// <summary>
        /// Transform Vector3 to Unity Vector3.
        /// transformScale - Gothic positions are in cm, but Unity in m. (factor 100). Most of the time we just transform it directly.
        /// </summary>
        public static System.Numerics.Vector3 ToZkVector(this Vector3 vector3, bool transformScale = true)
        {
            var vector = new System.Numerics.Vector3
            {
                X = vector3.x,
                Y = vector3.y,
                Z = vector3.z
            };

            if (transformScale)
                return vector * 100;
            else
                return vector;
        }
        
        // Create back conversion from UnityQuaternion to Matrix3x3
        public static Matrix3x3 ToZkMatrix(this Quaternion quaternion)
        {
            var unityMatrix = Matrix4x4.Rotate(quaternion);

            return new Matrix3x3(
                m11: unityMatrix.m00,
                m12: unityMatrix.m01,
                m13: unityMatrix.m02,

                m21: unityMatrix.m10,
                m22: unityMatrix.m11,
                m23: unityMatrix.m12,

                m31: unityMatrix.m20,
                m32: unityMatrix.m21,
                m33: unityMatrix.m22);
        }
    }
}
