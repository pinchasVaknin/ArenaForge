using System;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Where a placed object sits: position, rotation, a uniform scale, and how far the piece is
    /// stretched vertically on top of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scale is uniform on purpose. ArenaForge composes existing prefabs instead of authoring
    /// geometry, and squashing an art asset out of proportion is a defect rather than a placement
    /// decision — so nothing the generator places is ever fitted by rescaling it.
    /// </para>
    /// <para>
    /// <see cref="VerticalScale"/> is the one exception, and it is narrow. A wall is the only
    /// piece of art whose height is decided by a <em>parameter</em> rather than by the art itself:
    /// what it has to fit under is the floor height less the slab, which is a number the user
    /// types. Refusing to stretch it means the fit is either exact or wrong, and the wrong side is
    /// a strip of daylight between the top of every wall in the building and the ceiling. See
    /// ARCHITECTURE.md section 7.
    /// </para>
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

        /// <summary>
        /// Extra scaling along the object's own Y, on top of <see cref="Scale"/>. One for
        /// everything the generator does not have to fit under a ceiling.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Applied in the object's local space, before the rotation, exactly as a Unity
        /// <c>localScale</c> is — and since every rotation this tool produces is a yaw, the two
        /// commute and this is a stretch along world Y whichever way the piece is turned.
        /// </para>
        /// <para>
        /// The two attributes are what a document written before this field existed reads back as,
        /// and neither is decoration. Newtonsoft fills a constructor parameter the JSON has nothing
        /// for with <c>default(T)</c>, and a C# optional argument does not change that — so the
        /// declared default has to be a <see cref="DefaultValueAttribute"/>, and
        /// <see cref="DefaultValueHandling.Populate"/> is what makes it apply on the way in rather
        /// than only on the way out. Without the pair, every pose in every file this tool has
        /// already written would load with a vertical scale of zero: a map flattened into its own
        /// ground plane. It is verified by
        /// <c>BuildingHeadroomTests.APoseWithNoVerticalScaleInItReadsBackUnstretched</c>, because
        /// it is the kind of thing a Newtonsoft upgrade could quietly take away.
        /// </para>
        /// </remarks>
        [JsonProperty("verticalScale", Order = 3, DefaultValueHandling = DefaultValueHandling.Populate)]
        [DefaultValue(1f)]
        public float VerticalScale { get; }

        /// <summary>Creates a pose from its parts.</summary>
        /// <remarks>
        /// <paramref name="verticalScale"/> is optional for the callers' sake — nearly every pose
        /// in this tool is unstretched. It is <em>not</em> what makes an older document read back
        /// unstretched, which is a thing worth saying because it looks as though it should be: see
        /// <see cref="VerticalScale"/> for what actually does that.
        /// </remarks>
        [JsonConstructor]
        public Pose(Vec3 position, Quat rotation, float scale, float verticalScale = 1f)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
            VerticalScale = verticalScale;
        }

        /// <summary>Creates an unrotated, unscaled pose at the given position.</summary>
        public static Pose At(Vec3 position) => new Pose(position, Quat.Identity, 1f);

        /// <summary>Creates an unscaled pose at the given position with a yaw rotation.</summary>
        public static Pose At(Vec3 position, float yawDegrees) =>
            new Pose(position, Quat.FromYawDegrees(yawDegrees), 1f);

        /// <summary>The origin pose: no translation, no rotation, unit scale.</summary>
        public static Pose Identity => new Pose(Vec3.Zero, Quat.Identity, 1f);

        /// <summary>Transforms a point expressed in this pose's local space into world space.</summary>
        public Vec3 TransformPoint(Vec3 local) => Position + Rotation.Rotate(
            new Vec3(local.X * Scale, local.Y * Scale * VerticalScale, local.Z * Scale));

        /// <summary>
        /// The axis-aligned world bounds this pose gives a footprint expressed in its local space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The box round the oriented rectangle rather than the rectangle itself, which is the
        /// conservative answer and the one every caller wants: a cell a rotated barrier clips is
        /// floor that plays as blocked, ground a rule keeps clear of is ground kept clear of the
        /// whole box, and an edge a drag snaps to is the edge you can see.
        /// </para>
        /// <para>
        /// Here rather than in any one of them because three had written it out: the analyser's
        /// walkable set, the placement rules and the editor's edge snapping. The arithmetic is
        /// unchanged from the copies it replaces, corner for corner and comparison for comparison,
        /// so no map moves by a float.
        /// </para>
        /// </remarks>
        public Rect2 Bounds(Rect2 local)
        {
            Vec3 a = TransformPoint(new Vec3(local.MinX, 0f, local.MinZ));
            Vec3 b = TransformPoint(new Vec3(local.MaxX, 0f, local.MinZ));
            Vec3 c = TransformPoint(new Vec3(local.MaxX, 0f, local.MaxZ));
            Vec3 d = TransformPoint(new Vec3(local.MinX, 0f, local.MaxZ));

            return new Rect2(
                MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)),
                MathF.Min(MathF.Min(a.Z, b.Z), MathF.Min(c.Z, d.Z)),
                MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)),
                MathF.Max(MathF.Max(a.Z, b.Z), MathF.Max(c.Z, d.Z)));
        }

        /// <summary>
        /// Composes a child pose expressed in this pose's local space into world space. Used to
        /// resolve a catalog socket against the pose of the object carrying it.
        /// </summary>
        public Pose Transform(Pose local) => new Pose(
            TransformPoint(local.Position),
            Rotation * local.Rotation,
            Scale * local.Scale,
            VerticalScale * local.VerticalScale);

        /// <summary>Copy of this pose at a different position.</summary>
        public Pose WithPosition(Vec3 position) => new Pose(position, Rotation, Scale, VerticalScale);

        /// <summary>Copy of this pose stretched along its own Y.</summary>
        public Pose WithVerticalScale(float verticalScale) =>
            new Pose(Position, Rotation, Scale, verticalScale);

        /// <summary>
        /// Suppresses the vertical scale in serialised output when the piece is not stretched,
        /// which is what a document written before walls were fitted to their ceiling means.
        /// </summary>
        public bool ShouldSerializeVerticalScale() => VerticalScale != 1f;

        /// <inheritdoc />
        public bool Equals(Pose other) =>
            Position.Equals(other.Position) && Rotation.Equals(other.Rotation) &&
            Scale.Equals(other.Scale) && VerticalScale.Equals(other.VerticalScale);

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
                hash = (hash * 397) ^ VerticalScale.GetHashCode();
                return hash;
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(Pose a, Pose b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Pose a, Pose b) => !a.Equals(b);

        /// <inheritdoc />
        /// <remarks>
        /// The stretch is only mentioned when there is one, so the reading of an ordinary pose —
        /// which is nearly all of them — stays as short as it was.
        /// </remarks>
        public override string ToString() => VerticalScale == 1f
            ? string.Format(
                CultureInfo.InvariantCulture, "pos {0} rot {1} scale {2}", Position, Rotation, Scale)
            : string.Format(
                CultureInfo.InvariantCulture,
                "pos {0} rot {1} scale {2} stretched {3}", Position, Rotation, Scale, VerticalScale);
    }
}
