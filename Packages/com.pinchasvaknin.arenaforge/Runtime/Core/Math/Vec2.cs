using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Immutable two-component vector.
    /// </summary>
    /// <remarks>
    /// Used for the XZ ground plane throughout ArenaForge: <see cref="X"/> is world X and
    /// <see cref="Y"/> is world Z. Core carries no engine types, so the Unity adapter converts
    /// to <c>Vector2</c>/<c>Vector3</c> at the boundary.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        /// <summary>First component — world X.</summary>
        [JsonProperty("x", Order = 0)]
        public float X { get; }

        /// <summary>Second component — world Z when this is a ground-plane coordinate.</summary>
        [JsonProperty("y", Order = 1)]
        public float Y { get; }

        /// <summary>Creates a vector from its components.</summary>
        [JsonConstructor]
        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>The zero vector.</summary>
        public static Vec2 Zero => new Vec2(0f, 0f);

        /// <summary>The vector with both components set to one.</summary>
        public static Vec2 One => new Vec2(1f, 1f);

        /// <summary>Component-wise sum.</summary>
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        /// <summary>Component-wise difference.</summary>
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        /// <summary>Negation.</summary>
        public static Vec2 operator -(Vec2 v) => new Vec2(-v.X, -v.Y);

        /// <summary>Scales by a scalar.</summary>
        public static Vec2 operator *(Vec2 v, float s) => new Vec2(v.X * s, v.Y * s);

        /// <summary>Scales by a scalar.</summary>
        public static Vec2 operator *(float s, Vec2 v) => new Vec2(v.X * s, v.Y * s);

        /// <summary>Divides by a scalar.</summary>
        public static Vec2 operator /(Vec2 v, float s) => new Vec2(v.X / s, v.Y / s);

        /// <summary>Squared length. Prefer this to <see cref="Length"/> when comparing distances.</summary>
        public float SqrLength => X * X + Y * Y;

        /// <summary>Euclidean length.</summary>
        public float Length => MathF.Sqrt(SqrLength);

        /// <summary>Unit vector in the same direction, or <see cref="Zero"/> if the length is zero.</summary>
        public Vec2 Normalized
        {
            get
            {
                float length = Length;
                return length > 0f ? this / length : Zero;
            }
        }

        /// <summary>Dot product.</summary>
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Distance between two points.</summary>
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        /// <summary>Squared distance between two points.</summary>
        public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).SqrLength;

        /// <summary>Component-wise minimum.</summary>
        public static Vec2 Min(Vec2 a, Vec2 b) => new Vec2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));

        /// <summary>Component-wise maximum.</summary>
        public static Vec2 Max(Vec2 a, Vec2 b) => new Vec2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

        /// <summary>Lifts this ground-plane coordinate to 3D at the given height.</summary>
        public Vec3 ToVec3(float height) => new Vec3(X, height, Y);

        /// <inheritdoc />
        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        /// <summary>
        /// In-memory hash only. Never persist it or let it reach generated output — see the
        /// determinism rules in ARCHITECTURE.md.
        /// </summary>
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());

        /// <summary>Value equality.</summary>
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
    }
}
