using System;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Immutable axis-aligned rectangle on the XZ ground plane. Footprints, lane bands,
    /// playfield bounds and doorway clearances are all expressed with this type.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public readonly struct Rect2 : IEquatable<Rect2>
    {
        /// <summary>Lowest world X.</summary>
        [JsonProperty("minX", Order = 0)]
        public float MinX { get; }

        /// <summary>Lowest world Z.</summary>
        [JsonProperty("minZ", Order = 1)]
        public float MinZ { get; }

        /// <summary>Highest world X.</summary>
        [JsonProperty("maxX", Order = 2)]
        public float MaxX { get; }

        /// <summary>Highest world Z.</summary>
        [JsonProperty("maxZ", Order = 3)]
        public float MaxZ { get; }

        /// <summary>Creates a rectangle from its bounds. The caller is responsible for min &lt;= max.</summary>
        [JsonConstructor]
        public Rect2(float minX, float minZ, float maxX, float maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        /// <summary>Creates a rectangle from two corners, ordering the bounds for the caller.</summary>
        public static Rect2 FromCorners(Vec2 a, Vec2 b) => new Rect2(
            MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y),
            MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

        /// <summary>Creates a rectangle centred on a point.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Either size component is negative.</exception>
        public static Rect2 FromCenterSize(Vec2 center, Vec2 size)
        {
            if (size.X < 0f || size.Y < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Rect2 size must not be negative.");
            }

            Vec2 half = size * 0.5f;
            return new Rect2(center.X - half.X, center.Y - half.Y, center.X + half.X, center.Y + half.Y);
        }

        /// <summary>The empty rectangle at the origin.</summary>
        public static Rect2 Zero => new Rect2(0f, 0f, 0f, 0f);

        /// <summary>Lower corner.</summary>
        public Vec2 Min => new Vec2(MinX, MinZ);

        /// <summary>Upper corner.</summary>
        public Vec2 Max => new Vec2(MaxX, MaxZ);

        /// <summary>Extent along world X.</summary>
        public float Width => MaxX - MinX;

        /// <summary>Extent along world Z.</summary>
        public float Depth => MaxZ - MinZ;

        /// <summary>Extents along both axes.</summary>
        public Vec2 Size => new Vec2(Width, Depth);

        /// <summary>Centre point.</summary>
        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5f, (MinZ + MaxZ) * 0.5f);

        /// <summary>Area covered.</summary>
        public float Area => Width * Depth;

        /// <summary>True if the point lies inside or on the boundary.</summary>
        public bool Contains(Vec2 point) =>
            point.X >= MinX && point.X <= MaxX && point.Y >= MinZ && point.Y <= MaxZ;

        /// <summary>True if <paramref name="other"/> lies entirely inside or on this rectangle's boundary.</summary>
        public bool Contains(Rect2 other) =>
            other.MinX >= MinX && other.MaxX <= MaxX && other.MinZ >= MinZ && other.MaxZ <= MaxZ;

        /// <summary>
        /// True if the two rectangles share interior area. Rectangles that merely touch along an
        /// edge do not overlap, so props may be placed flush against one another.
        /// </summary>
        public bool Overlaps(Rect2 other) =>
            MinX < other.MaxX && other.MinX < MaxX && MinZ < other.MaxZ && other.MinZ < MaxZ;

        /// <summary>
        /// Shortest distance between two rectangles, or zero if they touch or overlap.
        /// </summary>
        /// <remarks>
        /// Edge to edge rather than centre to centre, because the callers are clearance rules:
        /// "keep a metre and a half of floor clear around this building" is a fact about the
        /// building's walls, and a centre-to-centre measure would mean something different for a
        /// hut and for a warehouse.
        /// </remarks>
        public static float Distance(Rect2 a, Rect2 b)
        {
            float dx = MathF.Max(0f, MathF.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            float dz = MathF.Max(0f, MathF.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Copy grown by <paramref name="margin"/> on every side. A negative margin shrinks.</summary>
        public Rect2 Expanded(float margin) =>
            new Rect2(MinX - margin, MinZ - margin, MaxX + margin, MaxZ + margin);

        /// <summary>Copy shifted by an offset.</summary>
        public Rect2 Translated(Vec2 offset) => new Rect2(
            MinX + offset.X, MinZ + offset.Y, MaxX + offset.X, MaxZ + offset.Y);

        /// <inheritdoc />
        public bool Equals(Rect2 other) =>
            MinX.Equals(other.MinX) && MinZ.Equals(other.MinZ) &&
            MaxX.Equals(other.MaxX) && MaxZ.Equals(other.MaxZ);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is Rect2 other && Equals(other);

        /// <summary>
        /// In-memory hash only. Never persist it or let it reach generated output — see the
        /// determinism rules in ARCHITECTURE.md.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinX.GetHashCode();
                hash = (hash * 397) ^ MinZ.GetHashCode();
                hash = (hash * 397) ^ MaxX.GetHashCode();
                hash = (hash * 397) ^ MaxZ.GetHashCode();
                return hash;
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(Rect2 a, Rect2 b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Rect2 a, Rect2 b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture, "[{0}, {1}] .. [{2}, {3}]", MinX, MinZ, MaxX, MaxZ);
    }
}
