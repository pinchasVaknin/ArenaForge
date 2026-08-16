using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Immutable axis-aligned bounding box. Used where an object's height matters as well as its
    /// ground footprint — occlusion, for instance, cares whether a prop clears eye height.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Aabb : IEquatable<Aabb>
    {
        /// <summary>Lower corner.</summary>
        [JsonProperty("min", Order = 0)]
        public Vec3 Min { get; }

        /// <summary>Upper corner.</summary>
        [JsonProperty("max", Order = 1)]
        public Vec3 Max { get; }

        /// <summary>Creates a box from its corners. The caller is responsible for min &lt;= max.</summary>
        [JsonConstructor]
        public Aabb(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>Creates a box from two arbitrary corners, ordering the bounds for the caller.</summary>
        public static Aabb FromCorners(Vec3 a, Vec3 b) => new Aabb(Vec3.Min(a, b), Vec3.Max(a, b));

        /// <summary>Creates a box centred on a point.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Any size component is negative.</exception>
        public static Aabb FromCenterSize(Vec3 center, Vec3 size)
        {
            if (size.X < 0f || size.Y < 0f || size.Z < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Aabb size must not be negative.");
            }

            Vec3 half = size * 0.5f;
            return new Aabb(center - half, center + half);
        }

        /// <summary>Creates a box by extruding a ground footprint from <paramref name="baseY"/> upwards.</summary>
        public static Aabb FromFootprint(Rect2 footprint, float baseY, float height) => new Aabb(
            new Vec3(footprint.MinX, baseY, footprint.MinZ),
            new Vec3(footprint.MaxX, baseY + height, footprint.MaxZ));

        /// <summary>Centre point.</summary>
        public Vec3 Center => (Min + Max) * 0.5f;

        /// <summary>Extents along each axis.</summary>
        public Vec3 Size => Max - Min;

        /// <summary>Extent along world Y.</summary>
        public float Height => Max.Y - Min.Y;

        /// <summary>The box projected onto the ground plane.</summary>
        public Rect2 Footprint => new Rect2(Min.X, Min.Z, Max.X, Max.Z);

        /// <summary>True if the point lies inside or on the boundary.</summary>
        public bool Contains(Vec3 point) =>
            point.X >= Min.X && point.X <= Max.X &&
            point.Y >= Min.Y && point.Y <= Max.Y &&
            point.Z >= Min.Z && point.Z <= Max.Z;

        /// <summary>
        /// True if the two boxes share interior volume. Boxes that merely touch on a face do not
        /// overlap, matching <see cref="Rect2.Overlaps"/>.
        /// </summary>
        public bool Overlaps(Aabb other) =>
            Min.X < other.Max.X && other.Min.X < Max.X &&
            Min.Y < other.Max.Y && other.Min.Y < Max.Y &&
            Min.Z < other.Max.Z && other.Min.Z < Max.Z;

        /// <summary>Copy grown to include <paramref name="point"/>.</summary>
        public Aabb Encapsulate(Vec3 point) => new Aabb(Vec3.Min(Min, point), Vec3.Max(Max, point));

        /// <summary>Copy grown by <paramref name="margin"/> on every side. A negative margin shrinks.</summary>
        public Aabb Expanded(float margin)
        {
            Vec3 m = new Vec3(margin, margin, margin);
            return new Aabb(Min - m, Max + m);
        }

        /// <inheritdoc />
        public bool Equals(Aabb other) => Min.Equals(other.Min) && Max.Equals(other.Max);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Aabb other && Equals(other);

        /// <summary>
        /// In-memory hash only. Never persist it or let it reach generated output — see the
        /// determinism rules in ARCHITECTURE.md.
        /// </summary>
        public override int GetHashCode() =>
            unchecked((Min.GetHashCode() * 397) ^ Max.GetHashCode());

        /// <summary>Value equality.</summary>
        public static bool operator ==(Aabb a, Aabb b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Aabb a, Aabb b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0} .. {1}", Min, Max);
    }
}
