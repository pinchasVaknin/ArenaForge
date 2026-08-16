using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Immutable three-component vector in a left-handed, Y-up coordinate system — the same
    /// convention Unity uses, so the adapter converts component-for-component.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        /// <summary>World X.</summary>
        [JsonProperty("x", Order = 0)]
        public float X { get; }

        /// <summary>World Y — height.</summary>
        [JsonProperty("y", Order = 1)]
        public float Y { get; }

        /// <summary>World Z.</summary>
        [JsonProperty("z", Order = 2)]
        public float Z { get; }

        /// <summary>Creates a vector from its components.</summary>
        [JsonConstructor]
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The zero vector.</summary>
        public static Vec3 Zero => new Vec3(0f, 0f, 0f);

        /// <summary>The vector with all components set to one.</summary>
        public static Vec3 One => new Vec3(1f, 1f, 1f);

        /// <summary>The world up axis.</summary>
        public static Vec3 Up => new Vec3(0f, 1f, 0f);

        /// <summary>Component-wise sum.</summary>
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        /// <summary>Component-wise difference.</summary>
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        /// <summary>Negation.</summary>
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);

        /// <summary>Scales by a scalar.</summary>
        public static Vec3 operator *(Vec3 v, float s) => new Vec3(v.X * s, v.Y * s, v.Z * s);

        /// <summary>Scales by a scalar.</summary>
        public static Vec3 operator *(float s, Vec3 v) => new Vec3(v.X * s, v.Y * s, v.Z * s);

        /// <summary>Divides by a scalar.</summary>
        public static Vec3 operator /(Vec3 v, float s) => new Vec3(v.X / s, v.Y / s, v.Z / s);

        /// <summary>Squared length. Prefer this to <see cref="Length"/> when comparing distances.</summary>
        public float SqrLength => X * X + Y * Y + Z * Z;

        /// <summary>Euclidean length.</summary>
        public float Length => MathF.Sqrt(SqrLength);

        /// <summary>Unit vector in the same direction, or <see cref="Zero"/> if the length is zero.</summary>
        public Vec3 Normalized
        {
            get
            {
                float length = Length;
                return length > 0f ? this / length : Zero;
            }
        }

        /// <summary>Dot product.</summary>
        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Cross product.</summary>
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        /// <summary>Distance between two points.</summary>
        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;

        /// <summary>Squared distance between two points.</summary>
        public static float DistanceSquared(Vec3 a, Vec3 b) => (a - b).SqrLength;

        /// <summary>Component-wise minimum.</summary>
        public static Vec3 Min(Vec3 a, Vec3 b) =>
            new Vec3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));

        /// <summary>Component-wise maximum.</summary>
        public static Vec3 Max(Vec3 a, Vec3 b) =>
            new Vec3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));

        /// <summary>Drops the height component, giving the ground-plane coordinate.</summary>
        public Vec2 Xz => new Vec2(X, Z);

        /// <inheritdoc />
        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        /// <summary>
        /// In-memory hash only. Never persist it or let it reach generated output — see the
        /// determinism rules in ARCHITECTURE.md.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
    }
}
