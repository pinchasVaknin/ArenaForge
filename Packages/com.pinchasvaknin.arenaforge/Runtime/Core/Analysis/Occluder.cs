using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// One thing that stops a player seeing through it: a rectangle on the ground plane, possibly
    /// turned off the axes, that spans the eye height the visibility test is run at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Height has already been decided by the time an occluder exists — see
    /// <see cref="TryCreate"/>. What is left is a two-dimensional question, which is why this
    /// carries no height of its own: a sightline is a segment on the XZ plane, and an occluder is a
    /// box it either crosses or does not.
    /// </para>
    /// <para>
    /// The rectangle is oriented rather than axis-aligned because cover may be rotated off the
    /// quarter turns, and the box around a rotated barrier is nearly twice its area — a visibility
    /// model built on those boxes would report a map far more sheltered than it plays. The
    /// separating-axis test below costs a handful of multiplications more than a slab test and
    /// answers the question about the actual rectangle.
    /// </para>
    /// <para>
    /// The state is ten loose floats rather than a <see cref="Vec2"/> centre and a
    /// <see cref="Rect2"/> bounds because <see cref="Blocks"/> and <see cref="MightBlock"/> run a
    /// few hundred million times across the property suite, and reading a component through a
    /// struct-valued property copies the struct on every read. The shaped values are still there as
    /// computed properties for everything that is not the inner loop.
    /// </para>
    /// </remarks>
    public readonly struct Occluder
    {
        readonly float _centerX;
        readonly float _centerZ;
        readonly float _axisX;
        readonly float _axisZ;
        readonly float _halfWidth;
        readonly float _halfDepth;
        readonly float _minX;
        readonly float _minZ;
        readonly float _maxX;
        readonly float _maxZ;

        /// <summary>Creates an occluder from its oriented rectangle.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Either half-extent is negative.</exception>
        public Occluder(Vec2 center, Vec2 axisX, float halfWidth, float halfDepth)
        {
            if (halfWidth < 0f || halfDepth < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfWidth), halfWidth, "Occluder half-extents must not be negative.");
            }

            Vec2 axis = axisX.Normalized;
            if (axis == Vec2.Zero)
            {
                axis = new Vec2(1f, 0f);
            }

            _centerX = center.X;
            _centerZ = center.Y;
            _axisX = axis.X;
            _axisZ = axis.Y;
            _halfWidth = halfWidth;
            _halfDepth = halfDepth;

            // The corner offsets of an oriented rectangle are (+-halfWidth along the axis) plus
            // (+-halfDepth across it), so the box around it reaches as far as the two contributions
            // together do.
            float extentX = MathF.Abs(axis.X) * halfWidth + MathF.Abs(axis.Y) * halfDepth;
            float extentZ = MathF.Abs(axis.Y) * halfWidth + MathF.Abs(axis.X) * halfDepth;
            _minX = center.X - extentX;
            _minZ = center.Y - extentZ;
            _maxX = center.X + extentX;
            _maxZ = center.Y + extentZ;
        }

        /// <summary>Centre of the rectangle in world space.</summary>
        public Vec2 Center => new Vec2(_centerX, _centerZ);

        /// <summary>Unit vector along the rectangle's own X axis, in world space.</summary>
        public Vec2 AxisX => new Vec2(_axisX, _axisZ);

        /// <summary>Half the rectangle's extent along <see cref="AxisX"/>.</summary>
        public float HalfWidth => _halfWidth;

        /// <summary>Half the rectangle's extent across <see cref="AxisX"/>.</summary>
        public float HalfDepth => _halfDepth;

        /// <summary>Axis-aligned bounds of the rectangle.</summary>
        public Rect2 Bounds => new Rect2(_minX, _minZ, _maxX, _maxZ);

        /// <summary>Ground area the rectangle covers.</summary>
        public float Area => 4f * _halfWidth * _halfDepth;

        /// <summary>
        /// Builds the occluder a placed object contributes, or reports that it contributes none
        /// because it does not stand across the eye line.
        /// </summary>
        /// <remarks>
        /// This is the whole of the height model. An object occupies the vertical span from its
        /// pivot to <see cref="CatalogEntry.Height"/> above it, and it blocks a sightline only if
        /// that span contains <paramref name="eyeHeight"/>. For anything standing on the ground
        /// that is exactly "taller than eye height", which is why low cover drops out of the
        /// standing-eye test and high cover does not. Stating it as a span rather than as a height
        /// also gets a crate sitting on a socket a metre up right: its own height is a metre, but
        /// the metre it occupies is the one the eye line runs through.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static bool TryCreate(
            PlacedObject placed, CatalogEntry entry, float eyeHeight, out Occluder occluder)
        {
            if (placed == null)
            {
                throw new ArgumentNullException(nameof(placed));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            occluder = default;

            float scale = placed.Pose.Scale;
            float baseHeight = placed.Pose.Position.Y;
            if (baseHeight > eyeHeight || baseHeight + entry.Height * scale <= eyeHeight)
            {
                return false;
            }

            Rect2 footprint = entry.Footprint;
            if (footprint.Width <= 0f || footprint.Depth <= 0f)
            {
                return false;
            }

            Vec2 axis = placed.Pose.Rotation.Rotate(new Vec3(1f, 0f, 0f)).Xz;
            Vec2 center = placed.Pose.TransformPoint(footprint.Center.ToVec3(0f)).Xz;

            occluder = new Occluder(
                center, axis, footprint.Width * 0.5f * scale, footprint.Depth * 0.5f * scale);
            return true;
        }

        /// <summary>
        /// Cheap rejection: false if the rectangle's bounds lie clear of the box
        /// <paramref name="minX"/>..<paramref name="maxZ"/>, which is the box around a segment.
        /// </summary>
        /// <remarks>
        /// A segment across a finished arena passes near two or three of its twenty-odd occluders.
        /// For the rest, four comparisons settle it, and that is what makes the sweep affordable.
        /// A true answer means only that the real test is worth running.
        /// </remarks>
        public bool MightBlock(float minX, float minZ, float maxX, float maxZ) =>
            _maxX >= minX && _minX <= maxX && _maxZ >= minZ && _minZ <= maxZ;

        /// <summary>
        /// True if the segment from <paramref name="from"/> to <paramref name="to"/> touches the
        /// rectangle, so a player at one end cannot see the other.
        /// </summary>
        /// <remarks>
        /// A separating-axis test over three axes — the rectangle's two, and the one perpendicular
        /// to the segment. Division-free on purpose: the slab form of the same test spends a divide
        /// per axis, and this is the innermost thing the analysis does.
        /// </remarks>
        public bool Blocks(Vec2 from, Vec2 to)
        {
            // Segment as a centre and a half-vector, moved into the rectangle's frame. The
            // rectangle's second axis is the first turned a quarter turn, so it is never stored.
            float midX = (from.X + to.X) * 0.5f - _centerX;
            float midZ = (from.Y + to.Y) * 0.5f - _centerZ;
            float halfX = (to.X - from.X) * 0.5f;
            float halfZ = (to.Y - from.Y) * 0.5f;

            float mx = midX * _axisX + midZ * _axisZ;
            float mz = midZ * _axisX - midX * _axisZ;
            float hx = halfX * _axisX + halfZ * _axisZ;
            float hz = halfZ * _axisX - halfX * _axisZ;

            float absHx = MathF.Abs(hx);
            float absHz = MathF.Abs(hz);

            if (MathF.Abs(mx) > _halfWidth + absHx || MathF.Abs(mz) > _halfDepth + absHz)
            {
                return false;
            }

            // The segment's own perpendicular. The rectangle's reach along it is each half-extent
            // scaled by the segment's component across that axis.
            return MathF.Abs(mx * hz - mz * hx) <= _halfWidth * absHz + _halfDepth * absHx;
        }

        /// <inheritdoc />
        public override string ToString() =>
            $"{Center} {_halfWidth * 2f} x {_halfDepth * 2f} along {AxisX}";
    }
}
