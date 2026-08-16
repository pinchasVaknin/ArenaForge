using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// One lane band: a strip of playfield running the length of the map from one spawn to the
    /// other.
    /// </summary>
    public sealed class ArenaLane
    {
        /// <summary>Readable lane id — <c>lane_mid</c>, <c>lane_north</c> — used as a stable id segment.</summary>
        public string Id { get; }

        /// <summary>Position across the map, counting from the lowest cross-axis coordinate.</summary>
        public int Index { get; }

        /// <summary>The band this lane occupies, in world space.</summary>
        public Rect2 Band { get; }

        /// <summary>Creates a lane.</summary>
        public ArenaLane(string id, int index, Rect2 band)
        {
            Id = id;
            Index = index;
            Band = band;
        }

        /// <inheritdoc />
        public override string ToString() => $"{Id} {Band}";
    }

    /// <summary>
    /// The skeleton of an arena — playfield, grid, lane bands and spawn areas — derived from the
    /// parameters alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rebuilding this from an <see cref="ArenaParams"/> gives the same layout every time, so the
    /// cover placement and analysis stages can reconstruct the lanes a document was generated
    /// against without the document carrying them.
    /// </para>
    /// <para>
    /// Lanes run along the playfield's longer axis with the two spawns at its ends, and are laid
    /// out side by side across the shorter one. Everything below is computed in that (along,
    /// cross) frame and converted to world coordinates by <see cref="ToWorld"/>, so the two
    /// possible orientations do not need two copies of the layout arithmetic.
    /// </para>
    /// </remarks>
    public sealed class ArenaLayout
    {
        /// <summary>Fraction of the cross axis given over to the gaps between lanes.</summary>
        const float GapShareOfCrossAxis = 0.10f;

        /// <summary>Fraction of the long axis each spawn area occupies.</summary>
        const float SpawnShareOfLongAxis = 0.12f;

        /// <summary>Widest a lane may be relative to an even share, before jitter is clamped.</summary>
        const float LaneWidthJitter = 0.1f;

        readonly List<ArenaLane> _lanes;

        ArenaLayout(
            Rect2 playfield,
            ArenaGrid grid,
            bool lanesRunAlongZ,
            List<ArenaLane> lanes,
            Rect2 spawnAreaA,
            Rect2 spawnAreaB)
        {
            Playfield = playfield;
            Grid = grid;
            LanesRunAlongZ = lanesRunAlongZ;
            _lanes = lanes;
            SpawnAreaA = spawnAreaA;
            SpawnAreaB = spawnAreaB;
        }

        /// <summary>Playfield bounds, centred on the world origin.</summary>
        public Rect2 Playfield { get; }

        /// <summary>Placement grid covering the playfield.</summary>
        public ArenaGrid Grid { get; }

        /// <summary>True when the long axis — the one lanes run along — is world Z.</summary>
        public bool LanesRunAlongZ { get; }

        /// <summary>Lane bands, ordered from the lowest cross-axis coordinate upward.</summary>
        public IReadOnlyList<ArenaLane> Lanes => _lanes;

        /// <summary>The lane a map's strong point of interest belongs in.</summary>
        /// <remarks>
        /// With an odd lane count this is the true middle lane. With an even one there is no
        /// middle, and this is the lane on the low side of centre.
        /// </remarks>
        public int MiddleLaneIndex => (_lanes.Count - 1) / 2;

        /// <summary>Spawn area at the low end of the long axis.</summary>
        public Rect2 SpawnAreaA { get; }

        /// <summary>Spawn area at the high end of the long axis.</summary>
        public Rect2 SpawnAreaB { get; }

        /// <summary>Distance between the two spawn areas' centres.</summary>
        public float SpawnSeparation => Vec2.Distance(SpawnAreaA.Center, SpawnAreaB.Center);

        /// <summary>Playfield extent along the axis lanes run down.</summary>
        public float LongExtent => LanesRunAlongZ ? Playfield.Depth : Playfield.Width;

        /// <summary>Playfield extent across the lanes.</summary>
        public float CrossExtent => LanesRunAlongZ ? Playfield.Width : Playfield.Depth;

        /// <summary>Builds the layout the parameters describe.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">The playfield is too small for the requested lanes.</exception>
        public static ArenaLayout Build(ArenaParams parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            Validate(parameters);

            Vec2 size = parameters.PlayfieldSize;
            var playfield = Rect2.FromCenterSize(Vec2.Zero, size);
            var grid = new ArenaGrid(playfield, parameters.GridSize);
            bool lanesRunAlongZ = size.Y >= size.X;

            int longCells = lanesRunAlongZ ? grid.CountZ : grid.CountX;
            int crossCells = lanesRunAlongZ ? grid.CountX : grid.CountZ;

            float longMin = lanesRunAlongZ ? playfield.MinZ : playfield.MinX;
            float crossMin = lanesRunAlongZ ? playfield.MinX : playfield.MinZ;
            float cell = parameters.GridSize;

            int[] laneWidths = DivideIntoLanes(
                crossCells, parameters.LaneCount, new Rng(parameters.Seed).Fork("lanes"), out int gapCells);

            string[] laneIds = BuildLaneIds(parameters.LaneCount);
            var lanes = new List<ArenaLane>(parameters.LaneCount);
            int cursor = 0;
            for (int i = 0; i < parameters.LaneCount; i++)
            {
                float bandMin = crossMin + cursor * cell;
                float bandMax = bandMin + laneWidths[i] * cell;
                lanes.Add(new ArenaLane(
                    laneIds[i], i,
                    RectFrom(lanesRunAlongZ, longMin, longMin + longCells * cell, bandMin, bandMax)));
                cursor += laneWidths[i] + gapCells;
            }

            int spawnCells = Math.Max(1, (int)MathF.Round(longCells * SpawnShareOfLongAxis));
            float crossMax = crossMin + crossCells * cell;
            float longMax = longMin + longCells * cell;
            Rect2 spawnA = RectFrom(lanesRunAlongZ, longMin, longMin + spawnCells * cell, crossMin, crossMax);
            Rect2 spawnB = RectFrom(lanesRunAlongZ, longMax - spawnCells * cell, longMax, crossMin, crossMax);

            return new ArenaLayout(playfield, grid, lanesRunAlongZ, lanes, spawnA, spawnB);
        }

        /// <summary>Converts an (along, cross) coordinate into world space.</summary>
        public Vec2 ToWorld(float along, float cross) =>
            LanesRunAlongZ ? new Vec2(cross, along) : new Vec2(along, cross);

        /// <summary>The along-axis component of a world point.</summary>
        public float AlongOf(Vec2 world) => LanesRunAlongZ ? world.Y : world.X;

        /// <summary>The cross-axis component of a world point.</summary>
        public float CrossOf(Vec2 world) => LanesRunAlongZ ? world.X : world.Y;

        /// <summary>Snaps an along-axis coordinate to the grid.</summary>
        public float SnapAlong(float along) =>
            Grid.SnapCoordinate(along, LanesRunAlongZ ? Playfield.MinZ : Playfield.MinX);

        /// <summary>Snaps a cross-axis coordinate to the grid.</summary>
        public float SnapCross(float cross) =>
            Grid.SnapCoordinate(cross, LanesRunAlongZ ? Playfield.MinX : Playfield.MinZ);

        static void Validate(ArenaParams parameters)
        {
            if (!(parameters.PlayfieldSize.X > 0f) || !(parameters.PlayfieldSize.Y > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.PlayfieldSize, "Playfield size must be positive.");
            }

            if (parameters.LaneCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.LaneCount, "A map needs at least one lane.");
            }

            if (!(parameters.GridSize > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.GridSize, "Grid size must be positive.");
            }

            if (!(parameters.StructureDensity > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.StructureDensity, "Structure density must be positive.");
            }
        }

        // Widths are counted in whole cells rather than metres so the bands tile the cross axis
        // exactly. Accumulating snapped float widths would leave the last band a rounding error
        // short of the playfield edge, and that error would move with the seed.
        static int[] DivideIntoLanes(int crossCells, int laneCount, Rng rng, out int gapCells)
        {
            gapCells = laneCount > 1
                ? Math.Max(1, (int)MathF.Round(crossCells * GapShareOfCrossAxis / (laneCount - 1)))
                : 0;

            int available = crossCells - gapCells * (laneCount - 1);
            if (available < laneCount)
            {
                throw new InvalidOperationException(
                    $"A playfield {crossCells} cells across cannot hold {laneCount} lanes " +
                    $"separated by {gapCells}-cell gaps.");
            }

            var weights = new float[laneCount];
            float total = 0f;
            for (int i = 0; i < laneCount; i++)
            {
                weights[i] = rng.NextRange(1f - LaneWidthJitter, 1f + LaneWidthJitter);
                total += weights[i];
            }

            // Largest-remainder apportionment: floor every share, then hand the cells lost to
            // rounding to the largest remainders. Ties break by lane index, so the result is a
            // function of the weights alone.
            var widths = new int[laneCount];
            var remainders = new float[laneCount];
            int assigned = 0;
            for (int i = 0; i < laneCount; i++)
            {
                float exact = available * (weights[i] / total);
                widths[i] = Math.Max(1, (int)MathF.Floor(exact));
                remainders[i] = exact - MathF.Floor(exact);
                assigned += widths[i];
            }

            for (int round = 0; round < laneCount && assigned < available; round++)
            {
                int best = 0;
                for (int i = 1; i < laneCount; i++)
                {
                    if (remainders[i] > remainders[best])
                    {
                        best = i;
                    }
                }

                widths[best]++;
                remainders[best] = -1f;
                assigned++;
            }

            // Only reachable when the Max(1, ...) floor above inflated a very thin lane.
            for (int i = laneCount - 1; assigned > available && i >= 0; i--)
            {
                if (widths[i] > 1)
                {
                    widths[i]--;
                    assigned--;
                }
            }

            return widths;
        }

        // Lane ids are compass-flavoured because they are read in stable ids and test failures:
        // "map/lane_mid/structure_00" says where the object is, "map/lane_01/structure_00" does
        // not. The side suffix appears only when a side holds more than one lane, so the default
        // three-lane map reads as north / mid / south.
        static string[] BuildLaneIds(int count)
        {
            var ids = new string[count];
            bool hasMiddle = count % 2 == 1;
            int middle = (count - 1) / 2;
            int northCount = hasMiddle ? middle : count / 2;
            int southCount = count - northCount - (hasMiddle ? 1 : 0);

            if (hasMiddle)
            {
                ids[middle] = "lane_mid";
            }

            for (int i = 0; i < northCount; i++)
            {
                int distanceFromCentre = northCount - i;
                ids[i] = northCount == 1
                    ? "lane_north"
                    : "lane_north_" + distanceFromCentre.ToString("00", CultureInfo.InvariantCulture);
            }

            int firstSouth = count - southCount;
            for (int i = 0; i < southCount; i++)
            {
                int distanceFromCentre = i + 1;
                ids[firstSouth + i] = southCount == 1
                    ? "lane_south"
                    : "lane_south_" + distanceFromCentre.ToString("00", CultureInfo.InvariantCulture);
            }

            return ids;
        }

        static Rect2 RectFrom(bool lanesRunAlongZ, float alongMin, float alongMax, float crossMin, float crossMax) =>
            lanesRunAlongZ
                ? new Rect2(crossMin, alongMin, crossMax, alongMax)
                : new Rect2(alongMin, crossMin, alongMax, crossMax);
    }
}
