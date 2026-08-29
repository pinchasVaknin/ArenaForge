using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// One graded pad: a rectangle held at a single height, blending back into the ground around
    /// it.
    /// </summary>
    public readonly struct Foundation
    {
        /// <summary>Creates a pad.</summary>
        public Foundation(Rect2 pad, float apron, float height)
        {
            Pad = pad;
            Apron = apron;
            Height = height;
        }

        /// <summary>The rectangle held dead flat.</summary>
        public Rect2 Pad { get; }

        /// <summary>How far out from the pad the ground takes to reach its own height again, in metres.</summary>
        public float Apron { get; }

        /// <summary>The height the pad is held at, in metres.</summary>
        public float Height { get; }
    }

    /// <summary>
    /// One graded corridor: a polyline held to a height per vertex, blending back into the ground
    /// either side of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second graded feature, and it is a different shape from <see cref="Foundation"/> because
    /// it is grading a different thing. A building wants one height over a rectangle — it is a stack
    /// of level storeys and there is no sense in which it could follow a slope. A road is the
    /// opposite: holding a run at its start height is a plateau, and over sixty metres it leaves the
    /// far end floating or buried. So a corridor carries a height at every vertex of its centreline
    /// and reads between them, which is a road that climbs.
    /// </para>
    /// <para>
    /// A single-vertex corridor is a disc held at one height — what a junction is, where two roads
    /// have to meet the ground at the same place.
    /// </para>
    /// </remarks>
    public readonly struct Corridor
    {
        readonly Vec2[] _points;
        readonly float[] _heights;

        /// <summary>Creates a corridor from a centreline and the height of the road at each vertex.</summary>
        /// <param name="points">The centreline, at least one point.</param>
        /// <param name="heights">One height per point, in the same order.</param>
        /// <param name="halfWidth">Half the carriageway, held at the profile exactly.</param>
        /// <param name="apron">How far past the carriageway the ground takes to recover, in metres.</param>
        /// <exception cref="ArgumentNullException">Either list is null.</exception>
        /// <exception cref="ArgumentException">The lists are empty or of different lengths.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The half width is not positive, or the apron is negative.</exception>
        public Corridor(
            IReadOnlyList<Vec2> points, IReadOnlyList<float> heights, float halfWidth, float apron)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (heights == null)
            {
                throw new ArgumentNullException(nameof(heights));
            }

            if (points.Count == 0)
            {
                throw new ArgumentException("A corridor needs a centreline.", nameof(points));
            }

            if (heights.Count != points.Count)
            {
                throw new ArgumentException(
                    $"A corridor of {points.Count} point(s) needs {points.Count} height(s), not " +
                    $"{heights.Count}.", nameof(heights));
            }

            if (!(halfWidth > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfWidth), halfWidth, "A corridor half width must be positive.");
            }

            if (apron < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(apron), apron, "A corridor apron must not be negative.");
            }

            // Copied rather than held, on the same terms every other value type here takes: a field
            // rebuilt from a document has to come back identical, and a caller that went on editing
            // the list it handed over would move the ground under a map already generated on it.
            _points = new Vec2[points.Count];
            _heights = new float[heights.Count];
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < points.Count; i++)
            {
                _points[i] = points[i];
                _heights[i] = heights[i];
                minX = MathF.Min(minX, points[i].X);
                minZ = MathF.Min(minZ, points[i].Y);
                maxX = MathF.Max(maxX, points[i].X);
                maxZ = MathF.Max(maxZ, points[i].Y);
            }

            HalfWidth = halfWidth;
            Apron = apron;
            Reach = new Rect2(minX, minZ, maxX, maxZ).Expanded(halfWidth + apron);
        }

        /// <summary>The centreline, from one end to the other.</summary>
        public IReadOnlyList<Vec2> Points => _points;

        /// <summary>The height the road stands at, one per point of <see cref="Points"/>.</summary>
        public IReadOnlyList<float> Heights => _heights;

        /// <summary>How far either side of the centreline is held at the profile exactly, in metres.</summary>
        public float HalfWidth { get; }

        /// <summary>How far past the carriageway the ground takes to reach its own height again.</summary>
        public float Apron { get; }

        /// <summary>
        /// Everything this corridor can change the height of: the centreline's own box grown by the
        /// carriageway and the apron.
        /// </summary>
        /// <remarks>
        /// Kept so that a point can be dismissed by four comparisons rather than by walking every
        /// vertex of every road on the map. <see cref="TerrainField.HeightAt"/> is asked for one
        /// height per heightmap texel, so what it costs per corridor it does not touch is most of
        /// what it costs.
        /// </remarks>
        public Rect2 Reach { get; }
    }

    /// <summary>
    /// The ground a map is laid on: a height for every point of the playfield, a flat pad under
    /// everything that has to stand square on it, and a graded corridor under every road.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A function rather than a stored heightmap. The field is fully described by the seed, two
    /// parameters and the lists of pads and corridors that have been graded into it, so it can be
    /// rebuilt exactly wherever it is needed — by the generator while it places, by the realiser
    /// while it drives a Unity terrain, and by a test — without a document carrying a grid of
    /// floats. That is the same bargain <see cref="ArenaLayout"/> makes and for the same reason.
    /// </para>
    /// <para>
    /// <strong>The grading is here and nowhere else.</strong> The road stage does not carve a Unity
    /// terrain: <c>TerrainWriter</c> rebuilds the whole heightmap out of this field on every
    /// realise, so a carve applied to the terrain asset is erased by the next regenerate or
    /// deepened by every one. A feature graded here is a feature the field is a function of, which
    /// is the only kind that survives.
    /// </para>
    /// <para>
    /// The shape is value noise summed over three octaves. Not Perlin and not simplex: those need
    /// a gradient table, and what a small arena wants from its ground is a couple of rises and a
    /// hollow, not a landscape. Three octaves of value noise gives a hill you can take cover
    /// behind at the largest feature size and enough roughness at the smallest that the ground
    /// does not read as an inflated rubber sheet.
    /// </para>
    /// <para>
    /// Every draw is a hash of a lattice coordinate rather than a walk of a stream, so a height
    /// can be asked for in any order and any number of times and always comes back the same — a
    /// stream would make the answer depend on how many samples had been taken before it, which is
    /// exactly what a field must not do.
    /// </para>
    /// </remarks>
    public sealed class TerrainField
    {
        /// <summary>How many octaves of value noise the ground is summed from.</summary>
        const int Octaves = 3;

        /// <summary>How many samples across a rectangle <see cref="MeanHeightOn"/> averages.</summary>
        const int MeanSamples = 5;

        readonly ulong[] _octaveSeeds = new ulong[Octaves];
        readonly List<Foundation> _foundations = new List<Foundation>();
        readonly List<Corridor> _corridors = new List<Corridor>();
        readonly float _amplitude;
        readonly float _featureSize;

        TerrainField(Rect2 bounds, float amplitude, float featureSize, ulong seed)
        {
            Bounds = bounds;
            _amplitude = amplitude;
            _featureSize = featureSize;

            var rng = new Rng(seed);
            for (int i = 0; i < Octaves; i++)
            {
                _octaveSeeds[i] = rng.Fork("octave_" + i.ToString(CultureInfo.InvariantCulture)).Seed;
            }
        }

        /// <summary>The area the field describes. Heights outside it are still defined.</summary>
        public Rect2 Bounds { get; }

        /// <summary>Peak-to-trough height variation of the ungraded ground, in metres.</summary>
        public float Amplitude => _amplitude;

        /// <summary>The pads graded into the ground, in the order they were graded.</summary>
        /// <remarks>
        /// Order is part of the answer: two pads that overlap disagree about the height of the
        /// ground they share, and the later one wins. Structures are kept clear of one another so
        /// that does not arise in a generated map, but a field rebuilt from a document has to
        /// replay them in the same order to come back the same.
        /// </remarks>
        public IReadOnlyList<Foundation> Foundations => _foundations;

        /// <summary>The corridors graded into the ground, in the order they were graded.</summary>
        /// <remarks>
        /// Order is part of the answer for the reason it is for <see cref="Foundations"/>, and here
        /// it is load-bearing rather than incidental: the junction discs are graded after the roads
        /// that meet at them precisely so that a crossing comes out at one height instead of at
        /// whichever of two profiles happened to be asked. See <see cref="HeightAt"/>.
        /// </remarks>
        public IReadOnlyList<Corridor> Corridors => _corridors;

        /// <summary>Builds the ungraded ground the parameters describe.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A terrain parameter is outside its supported range.</exception>
        public static TerrainField Build(ArenaParams parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (parameters.TerrainAmplitude < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.TerrainAmplitude,
                    "Terrain amplitude must not be negative.");
            }

            if (!(parameters.TerrainFeatureSize > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.TerrainFeatureSize,
                    "Terrain feature size must be positive.");
            }

            return new TerrainField(
                Rect2.FromCenterSize(Vec2.Zero, parameters.PlayfieldSize),
                parameters.TerrainAmplitude,
                parameters.TerrainFeatureSize,
                new Rng(parameters.Seed).Fork("terrain").Seed);
        }

        /// <summary>Height of the ground at a point, in metres, pads and corridors included.</summary>
        /// <remarks>
        /// <para>
        /// A pad beats an apron, whatever order the two were graded in. Without that rule a
        /// building close enough to another for its apron to reach across would tilt the ground
        /// under its neighbour, which is exactly the failure the pads exist to prevent — and it
        /// would only happen on some seeds, which is the worst way for it to happen. Two pads that
        /// overlap still disagree, and there the later one wins; the arena generator keeps its
        /// structures far enough apart that they cannot.
        /// </para>
        /// <para>
        /// <strong>The same rule, extended to the roads, decides the whole order.</strong> A pad
        /// beats a corridor: a building is a stack of level storeys and a road may not tilt the
        /// ground under one, where a road crossing a pad has a doorstep's worth of blend to give.
        /// A corridor beats a pad's apron, for the reason a pad does: a carriageway a neighbouring
        /// building tilted is a road with a camber nobody asked for. A corridor beats a corridor's
        /// apron, so a crossing is road rather than the average of two verges. And a solved junction
        /// height beats either polyline — the junction discs are graded last and the later corridor
        /// wins, which is what stops two roads arriving at one crossing at two heights.
        /// </para>
        /// </remarks>
        public float HeightAt(Vec2 point)
        {
            float height = Ground(point);
            float pad = 0f;
            bool onPad = false;
            float road = 0f;
            bool onRoad = false;

            for (int i = 0; i < _foundations.Count; i++)
            {
                Foundation foundation = _foundations[i];
                float weight = Weight(foundation, point);

                if (weight >= 1f)
                {
                    pad = foundation.Height;
                    onPad = true;
                    continue;
                }

                if (weight > 0f)
                {
                    height += (foundation.Height - height) * weight;
                }
            }

            for (int i = 0; i < _corridors.Count; i++)
            {
                float weight = Weight(_corridors[i], point, out float profile);

                if (weight >= 1f)
                {
                    road = profile;
                    onRoad = true;
                    continue;
                }

                if (weight > 0f)
                {
                    height += (profile - height) * weight;
                }
            }

            return onPad ? pad : onRoad ? road : height;
        }

        /// <summary>
        /// The pose a piece takes when its pivot is driven into the ground at
        /// <paramref name="at"/> rather than stood on top of it.
        /// </summary>
        /// <param name="pose">The pose to seat. Its rotation and scale come back untouched.</param>
        /// <param name="at">Where on the ground the piece is being placed.</param>
        /// <remarks>
        /// <para>
        /// What a fence panel wants and what a crate does not. Everything else this tool puts on
        /// the ground is <em>stood</em> on it — <see cref="Placement.AtQuarterTurn"/> lifts a piece
        /// by <see cref="CatalogEntry.BaseOffset"/> so the underside of the art lands on the
        /// surface, and the caller adds the height of that surface on the way out. A fence panel
        /// modelled with a deep foundation under it has a base offset a metre or more long, and
        /// standing that on the ground is a fence hovering a metre over its own footings.
        /// </para>
        /// <para>
        /// So the pivot goes exactly on the ground and whatever the art has below the pivot goes
        /// under it, which is what the foundation is modelled for: it fills the wedge of open
        /// ground a panel leaves when the slope changes under the run.
        /// </para>
        /// <para>
        /// <strong>The rotation is not touched, and that is the point of doing this rather than
        /// tilting.</strong> A run laid across a hillside could be pitched to the ground's normal
        /// panel by panel, and the result is a fence that leans — every post out of plumb, every
        /// join between two panels a wedge, and a gate frame no longer square. Held upright and
        /// seated one height per panel, the same run steps down the hill like a staircase, which
        /// is how a real fence is built and the only shape a foundation can hide the gaps under.
        /// </para>
        /// </remarks>
        public Pose Planted(Pose pose, Vec2 at) => pose.WithPosition(
            new Vec3(pose.Position.X, HeightAt(at), pose.Position.Z));

        /// <summary>
        /// The average height over a rectangle, sampled on a fixed lattice.
        /// </summary>
        /// <remarks>
        /// A fixed lattice rather than an integral, because what this is for is choosing the
        /// height of a pad and the answer only has to be the same every time it is asked. The
        /// average rather than the highest or the lowest corner: a pad at the high corner leaves a
        /// building perched on a plinth, one at the low corner buries its ground floor, and the
        /// average is the cut-and-fill a person would do.
        /// </remarks>
        public float MeanHeightOn(Rect2 area)
        {
            float total = 0f;
            for (int z = 0; z < MeanSamples; z++)
            {
                for (int x = 0; x < MeanSamples; x++)
                {
                    total += HeightAt(new Vec2(
                        Lerp(area.MinX, area.MaxX, x / (float)(MeanSamples - 1)),
                        Lerp(area.MinZ, area.MaxZ, z / (float)(MeanSamples - 1))));
                }
            }

            return total / (MeanSamples * MeanSamples);
        }

        /// <summary>
        /// Grades a flat pad into the ground and records it.
        /// </summary>
        /// <param name="pad">The rectangle to hold dead flat.</param>
        /// <param name="apron">How far out of the pad the ground takes to recover, in metres.</param>
        /// <param name="height">The height to hold the pad at.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="apron"/> is negative.</exception>
        public void AddFoundation(Rect2 pad, float apron, float height)
        {
            if (apron < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(apron), apron, "A foundation apron must not be negative.");
            }

            _foundations.Add(new Foundation(pad, apron, height));
        }

        /// <summary>
        /// Grades a road into the ground and records it.
        /// </summary>
        /// <param name="points">The centreline, at least one point. A single point is a disc.</param>
        /// <param name="heights">The height of the road at each point, in the same order.</param>
        /// <param name="halfWidth">Half the carriageway, held at the profile exactly.</param>
        /// <param name="apron">How far past the carriageway the ground takes to recover, in metres.</param>
        /// <exception cref="ArgumentNullException">Either list is null.</exception>
        /// <exception cref="ArgumentException">The lists are empty or of different lengths.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The half width is not positive, or the apron is negative.</exception>
        public void AddCorridor(
            IReadOnlyList<Vec2> points, IReadOnlyList<float> heights, float halfWidth, float apron) =>
            _corridors.Add(new Corridor(points, heights, halfWidth, apron));

        /// <summary>How much of a point's height a pad decides: 1 on the pad, 0 past its apron.</summary>
        static float Weight(Foundation foundation, Vec2 point)
        {
            float distance = Rect2.Distance(foundation.Pad, new Rect2(point.X, point.Y, point.X, point.Y));
            if (distance <= 0f)
            {
                return 1f;
            }

            if (!(distance < foundation.Apron))
            {
                return 0f;
            }

            return Smooth(1f - distance / foundation.Apron);
        }

        /// <summary>
        /// How much of a point's height a corridor decides — 1 on the carriageway, 0 past its
        /// apron — and the height the road stands at where the point is nearest to it.
        /// </summary>
        /// <remarks>
        /// The same smoothstep <see cref="Weight(Foundation, Vec2)"/> uses, measured to the
        /// centreline instead of to a rectangle. The profile comes back from the same walk that
        /// found the distance, because they are the same question asked of the same nearest point:
        /// finding it twice would be the walk done twice and would let the two answers come from
        /// different segments where a bend brings two of them equally close.
        /// </remarks>
        static float Weight(Corridor corridor, Vec2 point, out float profile)
        {
            profile = 0f;
            if (!corridor.Reach.Contains(point))
            {
                return 0f;
            }

            IReadOnlyList<Vec2> points = corridor.Points;
            IReadOnlyList<float> heights = corridor.Heights;

            float nearest = float.MaxValue;
            profile = heights[0];

            if (points.Count == 1)
            {
                nearest = Vec2.Distance(points[0], point);
            }

            for (int i = 1; i < points.Count; i++)
            {
                Vec2 from = points[i - 1];
                Vec2 along = points[i] - from;
                float lengthSquared = along.SqrLength;

                float t = lengthSquared > 0f
                    ? Vec2.Dot(point - from, along) / lengthSquared
                    : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;

                float distance = Vec2.Distance(point, from + along * t);
                if (distance < nearest)
                {
                    nearest = distance;
                    profile = Lerp(heights[i - 1], heights[i], t);
                }
            }

            if (nearest <= corridor.HalfWidth)
            {
                return 1f;
            }

            float beyond = nearest - corridor.HalfWidth;
            if (!(beyond < corridor.Apron))
            {
                return 0f;
            }

            return Smooth(1f - beyond / corridor.Apron);
        }

        /// <summary>The ungraded ground: three octaves of value noise, centred on zero.</summary>
        float Ground(Vec2 point)
        {
            if (!(_amplitude > 0f))
            {
                return 0f;
            }

            float total = 0f;
            float weights = 0f;
            float cell = _featureSize;
            float gain = 1f;

            for (int octave = 0; octave < Octaves; octave++)
            {
                total += gain * Octave(octave, point, cell);
                weights += gain;
                cell *= 0.5f;
                gain *= 0.5f;
            }

            // Centred so the mean ground level is zero: a map's objects then sit around y = 0
            // whatever the amplitude is, and turning the terrain up does not lift the whole arena.
            return (total / weights - 0.5f) * _amplitude;
        }

        /// <summary>One octave: bilinear interpolation over a lattice of hashed corner values.</summary>
        float Octave(int octave, Vec2 point, float cell)
        {
            float fx = (point.X - Bounds.MinX) / cell;
            float fz = (point.Y - Bounds.MinZ) / cell;

            int x0 = (int)MathF.Floor(fx);
            int z0 = (int)MathF.Floor(fz);

            float tx = Smooth(fx - x0);
            float tz = Smooth(fz - z0);

            float low = Lerp(Corner(octave, x0, z0), Corner(octave, x0 + 1, z0), tx);
            float high = Lerp(Corner(octave, x0, z0 + 1), Corner(octave, x0 + 1, z0 + 1), tx);
            return Lerp(low, high, tz);
        }

        /// <summary>The value of one lattice corner, in [0, 1).</summary>
        float Corner(int octave, int x, int z)
        {
            long key = unchecked(((long)x << 32) | (uint)z);
            return new Rng(StableHash.Combine(_octaveSeeds[octave], key)).NextFloat();
        }

        /// <summary>Cubic smoothstep. Value noise interpolated linearly shows its lattice.</summary>
        static float Smooth(float t) => t * t * (3f - 2f * t);

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
