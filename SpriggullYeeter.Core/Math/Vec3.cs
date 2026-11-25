using System;

namespace SpriggullYeeter.Core.Math
{
    /// <summary>
    /// Lightweight immutable 3D vector value type for engine-independent kinematics and unit tests.
    /// </summary>
    public readonly struct Vec3
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float SqrMagnitude => (X * X) + (Y * Y) + (Z * Z);

        public float Magnitude => (float)System.Math.Sqrt(SqrMagnitude);

        public bool IsFinite =>
            !float.IsNaN(X) && !float.IsInfinity(X) &&
            !float.IsNaN(Y) && !float.IsInfinity(Y) &&
            !float.IsNaN(Z) && !float.IsInfinity(Z);

        public Vec3 Normalized
        {
            get
            {
                float mag = Magnitude;
                return mag > 1e-6f ? new Vec3(X / mag, Y / mag, Z / mag) : Zero;
            }
        }

        public static float Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return new Vec3(
                a.X + ((b.X - a.X) * t),
                a.Y + ((b.Y - a.Y) * t),
                a.Z + ((b.Z - a.Z) * t));
        }

        public static Vec3 ClampMagnitude(Vec3 v, float maxLength)
        {
            if (maxLength <= 0f || !v.IsFinite)
                return Zero;

            float sqr = v.SqrMagnitude;
            if (sqr <= maxLength * maxLength)
                return v;

            float scale = maxLength / (float)System.Math.Sqrt(sqr);
            return new Vec3(v.X * scale, v.Y * scale, v.Z * scale);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);
        public static Vec3 operator *(Vec3 v, float s) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator *(float s, Vec3 v) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator /(Vec3 v, float s) => new Vec3(v.X / s, v.Y / s, v.Z / s);
    }
}
