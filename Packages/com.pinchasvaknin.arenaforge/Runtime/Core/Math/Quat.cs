using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Immutable unit quaternion, component-compatible with <c>UnityEngine.Quaternion</c>.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Quat : IEquatable<Quat>
    {
        /// <summary>X component of the vector part.</summary>
        [JsonProperty("x", Order = 0)]
        public float X { get; }

        /// <summary>Y component of the vector part.</summary>
        [JsonProperty("y", Order = 1)]
        public float Y { get; }

        /// <summary>Z component of the vector part.</summary>
        [JsonProperty("z", Order = 2)]
        public float Z { get; }

        /// <summary>Scalar part.</summary>
        [JsonProperty("w", Order = 3)]
        public float W { get; }

        /// <summary>Creates a quaternion from its raw components.</summary>
        [JsonConstructor]
        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>The identity rotation.</summary>
        public static Quat Identity => new Quat(0f, 0f, 0f, 1f);

        /// <summary>
        /// Rotation of <paramref name="degrees"/> about the world up axis.
        /// </summary>
        /// <remarks>
        /// This is the only rotation the generator produces: arena props are yaw-only. The
        /// result depends on the runtime's <see cref="MathF.Sin"/> implementation, which is
        /// deterministic for a given runtime but not guaranteed identical to the last ulp
        /// across runtimes. Placement therefore prefers whole quarter turns.
        /// </remarks>
        public static Quat FromYawDegrees(float degrees)
        {
            float halfRadians = degrees * (MathF.PI / 360f);
            return new Quat(0f, MathF.Sin(halfRadians), 0f, MathF.Cos(halfRadians));
        }

        /// <summary>Yaw in degrees, in the range (-180, 180].</summary>
        public float YawDegrees => MathF.Atan2(2f * (W * Y), 1f - 2f * (Y * Y)) * (180f / MathF.PI);

        /// <summary>Composes two rotations: <paramref name="a"/> applied after <paramref name="b"/>.</summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotates a vector by this rotation.</summary>
        public Vec3 Rotate(Vec3 v)
        {
            // v + 2w(q x v) + 2(q x (q x v)), with q the vector part.
            float cx = Y * v.Z - Z * v.Y;
            float cy = Z * v.X - X * v.Z;
            float cz = X * v.Y - Y * v.X;

            float ccx = Y * cz - Z * cy;
            float ccy = Z * cx - X * cz;
            float ccz = X * cy - Y * cx;

            return new Vec3(
                v.X + 2f * (W * cx + ccx),
                v.Y + 2f * (W * cy + ccy),
                v.Z + 2f * (W * cz + ccz));
        }

        /// <summary>The inverse rotation, assuming this quaternion is a unit quaternion.</summary>
        public Quat Conjugate => new Quat(-X, -Y, -Z, W);

        /// <summary>Length of the quaternion as a four-component vector.</summary>
        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z + W * W);

        /// <summary>Renormalised copy, or <see cref="Identity"/> if the length is zero.</summary>
        public Quat Normalized
        {
            get
            {
                float length = Length;
                if (length <= 0f)
                {
                    return Identity;
                }

                float inv = 1f / length;
                return new Quat(X * inv, Y * inv, Z * inv, W * inv);
            }
        }

        /// <inheritdoc />
        public bool Equals(Quat other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Quat other && Equals(other);

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
                hash = (hash * 397) ^ W.GetHashCode();
                return hash;
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(Quat a, Quat b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Quat a, Quat b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2}, {3})", X, Y, Z, W);
    }
}
