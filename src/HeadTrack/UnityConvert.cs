using HeadTrack.Core;
using UnityEngine;

namespace HeadTrack
{
    /// <summary>Between the core's double-precision types and Unity's.</summary>
    internal static class UnityConvert
    {
        public static Vec3 ToVec3(this Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static Vector3 ToVector3(this Vec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>
        /// A row-major double[16] as a Matrix4x4. Unity's constructor takes
        /// columns, so the fields are set one by one (mRC = row R, column C).
        /// </summary>
        public static Matrix4x4 ToMatrix(double[] m)
        {
            var result = new Matrix4x4
            {
                m00 = (float)m[0], m01 = (float)m[1], m02 = (float)m[2], m03 = (float)m[3],
                m10 = (float)m[4], m11 = (float)m[5], m12 = (float)m[6], m13 = (float)m[7],
                m20 = (float)m[8], m21 = (float)m[9], m22 = (float)m[10], m23 = (float)m[11],
                m30 = (float)m[12], m31 = (float)m[13], m32 = (float)m[14], m33 = (float)m[15],
            };
            return result;
        }

        /// <summary>The frustum a projection renders, whatever produced it.</summary>
        public static FrustumTangents Tangents(Matrix4x4 p) => FrustumTangents.FromProjection(p.m00, p.m02, p.m11, p.m12);
    }
}
