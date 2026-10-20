using System;
using System.Globalization;

namespace HeadTrack.Core
{
    /// <summary>A double-precision 3-vector: the core's only geometric type, free of Unity.</summary>
    public readonly struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0, 0, 0);

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Length => Math.Sqrt(Dot(this, this));

        public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        public static Vec3 operator *(Vec3 a, double k) => new Vec3(a.X * k, a.Y * k, a.Z * k);

        public static Vec3 operator *(double k, Vec3 a) => a * k;

        public static Vec3 operator /(Vec3 a, double k) => new Vec3(a.X / k, a.Y / k, a.Z / k);

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public Vec3 Normalized()
        {
            double length = Length;
            return length > 0 ? this / length : Zero;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", X, Y, Z);

        private static bool IsFiniteValue(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
