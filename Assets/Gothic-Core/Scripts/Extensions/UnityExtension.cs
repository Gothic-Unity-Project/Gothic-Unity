using UnityEngine;
using ZenKit.Util;

namespace GUZ.Core.Extensions
{
    public static class UnityExtension
    {
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
