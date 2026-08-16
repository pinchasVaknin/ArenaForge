using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Where a placed object sits: position, rotation and a uniform scale.
    /// </summary>
    /// <remarks>
    /// Scale is uniform rather than per-axis on purpose. ArenaForge composes existing prefabs
    /// instead of authoring geometry, and non-uniform scaling of an art asset is a defect, not
    /// a placement decision.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Pose : IEquatable<Pose>
    {
        /// <summary>World position.</summary>
        [JsonProperty("position", Order = 0)]
        public Vec3 Position { get; }

        /// <summary>World rotation.</summary>
        [JsonProperty("rotation", Order = 1)]
        public Quat Rotation { get; }

        /// <summary>Uniform scale factor.</summary>
        [JsonProperty("scale", Order = 2)]
        public float Scale { get; }

        /// <summary>Creates a pose from its parts.</summary>
        [JsonConstructor]
        public Pose(Vec3 position, Quat rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>Creates an unrotated, unscaled pose at the given position.</summary>
        public static Pose At(Vec3 position) => new Pose(position, Quat.Identity, 1f);

        /// <summary>Creates an unscaled pose at the given position with a yaw rotation.</summary>
        public static Pose At(Vec3 position, float yawDegrees) =>
            new Pose(position, Quat.FromYawDegrees(yawDegrees), 1f);

        /// <summary>The origin pose: no translation, no rotation, unit scale.</summary>
        public static Pose Identity => new Pose(Vec3.Zero, Quat.Identity, 1f);

        /// <summary>Transforms a point expressed in this pose's local space into world space.</summary>
        public Vec3 TransformPoint(Vec3 local) => Position + Rotation.Rotate(local * Scale);

        /// <summary>
        /// Composes a child pose expressed in this pose's local space into world space. Used to
        /// resolve a catalog socket against the pose of the object carrying it.
        /// </summary>
        public Pose Transform(Pose local) => new Pose(
            TransformPoint(local.Position),
            Rotation * local.Rotation,
            Scale * local.Scale);

        /// <summary>Copy of this pose at a different position.</summary>
        public Pose WithPosition(Vec3 position) => new Pose(position, Rotation, Scale);

        /// <inheritdoc />
        public bool Equals(Pose other) =>
            Position.Equals(other.Position) && Rotation.Equals(other.Rotation) && Scale.Equals(other.Scale);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Pose other && Equals(other);

        /// <summary>
        /// In-memory hash only. Never persist it or let it reach generated output — see the
        /// determinism rules in ARCHITECTURE.md.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Scale.GetHashCode();
                return hash;
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(Pose a, Pose b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Pose a, Pose b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "pos {0} rot {1} scale {2}", Position, Rotation, Scale);
    }
}
