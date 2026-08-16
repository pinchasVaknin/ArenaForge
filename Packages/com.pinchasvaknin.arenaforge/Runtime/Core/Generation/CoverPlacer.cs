using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Scatters cover through the lanes of a map that already has its structures and spawns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One lane at a time, from its own forked stream. The lane's open floor decides how many
    /// props it should hold; Poisson-disk sampling over that floor decides where they may go; the
    /// catalog and the low-to-high ratio decide what each one is; and the constraint set decides
    /// whether the result is legal. Nothing is placed that a rule has not agreed to.
    /// </para>
    /// <para>
    /// The sampler deliberately produces more positions than the target needs, and the positions
    /// are then shuffled before being tried. Bridson's algorithm grows outward from its first
    /// sample, so taking the first N of its output in generation order would crowd the props
    /// around wherever that first sample landed; a shuffled subset of a Poisson set is spread the
    /// way the set is.
    /// </para>
    /// </remarks>
    public static class CoverPlacer
    {
        /// <summary>Tag every piece of cover carries.</summary>
        public const string CoverTag = "cover";

        /// <summary>Tag for cover a player can shoot over.</summary>
        public const string LowCoverTag = "cover/low";

        /// <summary>Tag for cover a player cannot shoot over.</summary>
        public const string HighCoverTag = "cover/high";

        /// <summary>Tag a socket must carry for a prop to be attached to it.</summary>
        public const string PropSurfaceTag = "prop_surface";

        /// <summary>Prefix of the world metadata keys holding the placement statistics.</summary>
        public const string StatsPrefix = "cover_";

        /// <summary>World metadata key holding how many props the density asked for.</summary>
        public const string TargetKey = StatsPrefix + "target";

        /// <summary>Clear floor kept around a structure, in metres.</summary>
        public const float StructureClearance = 1.5f;

        /// <summary>Clear floor kept around a spawn area, in metres.</summary>
        public const float SpawnClearance = 3f;

        /// <summary>Clear floor kept around a doorway rectangle, in metres.</summary>
        public const float DoorwayClearance = 1.5f;

        /// <summary>Walking space kept between two props, in metres.</summary>
        public const float PropMargin = 0.75f;

        /// <summary>Props per square metre of open floor at a <c>CoverDensity</c> of 1.</summary>
        /// <remarks>
        /// One prop per thirty square metres — a piece of cover every five or six metres, which is
        /// about the spacing a lane of this kind of map is built around. Open floor, not lane
        /// area: the figure is meant to describe how busy the space a player moves through feels,
        /// and the ground under a building is not part of that space.
        /// </remarks>
        const float BaselineCoverPerSquareMetre = 1f / 30f;

        /// <summary>
        /// Rough number of Poisson samples a unit area yields at unit radius, used to turn a
        /// wanted sample count back into a radius. Measured from the sampler, not derived: the
        /// theoretical packing bound is never reached in practice.
        /// </summary>
        const float PoissonYield = 0.7f;

        /// <summary>How many more positions to sample than the target needs.</summary>
        const float SampleSurplus = 1.6f;

        /// <summary>Different catalog entries tried at one position before it is abandoned.</summary>
        const int EntriesPerPosition = 4;

        /// <summary>Candidate evaluations a lane may spend per prop it was asked for.</summary>
        const int AttemptsPerTargetProp = 8;

        /// <summary>Largest footprint that may be attached to a socket, in square metres.</summary>
        const float SocketPropMaxArea = 1.2f;

        /// <summary>Share of prop surfaces that end up carrying something.</summary>
        const float SocketFillChance = 0.5f;

        /// <summary>
        /// Places cover into <paramref name="doc"/> and records what happened in its metadata.
        /// </summary>
        /// <param name="doc">The document to add cover to. Its parameters drive the placement.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="structures">Structures already committed, with their world footprints.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A cover parameter is outside its supported range.</exception>
        public static void Place(
            WorldDoc doc, ArenaLayout layout, Catalog catalog, IReadOnlyList<Placement> structures)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (structures == null)
            {
                throw new ArgumentNullException(nameof(structures));
            }

            ArenaParams parameters = doc.Parameters;
            Validate(parameters);

            var stats = new PlacementStats();
            IReadOnlyList<CatalogEntry> low = catalog.Query(TagQuery.All(CoverTag, LowCoverTag));
            IReadOnlyList<CatalogEntry> high = catalog.Query(TagQuery.All(CoverTag, HighCoverTag));

            int target = 0;
            if (low.Count > 0 || high.Count > 0)
            {
                // What the sampler draws is a pivot, and what has to stay clear of things is the
                // footprint around it — plus the half cell the pivot may move when it snaps to the
                // grid. Claims are grown by that much and lanes shrunk by it, so a position on
                // offer is one where at least something in the catalog fits.
                //
                // The *smallest* piece of cover, deliberately. Sizing the margin for the largest
                // would keep a crate out of every gap only a crate fits, and would leave the
                // constraint set with nothing left to reject: it is the rules, not the sampler,
                // that decide whether the entry actually drawn belongs at a position, and the
                // retry below is what turns a barrier that does not fit into a crate that does.
                float propRadius = SmallestPropRadius(low, high);
                float margin = propRadius + layout.Grid.CellSize * 0.5f;
                float minSpacing = 2f * propRadius + PropMargin;

                List<Rect2> doorways = ReadDoorways(doc);
                PlacementGrid grid = BuildGrid(layout, structures, doorways, margin);

                for (int i = 0; i < layout.Lanes.Count; i++)
                {
                    target += PlaceInLane(
                        doc, layout, layout.Lanes[i], structures, doorways, grid, low, high,
                        minSpacing, margin, stats);
                }

                PlaceSocketProps(doc, catalog, parameters);
            }

            doc.Metadata[TargetKey] = target.ToString(CultureInfo.InvariantCulture);
            stats.WriteTo(doc.Metadata, StatsPrefix);
        }

        // How many props a stretch of open floor is worth at a given density. Rounded, so a lane
        // with almost no floor left asks for nothing rather than for a fraction of a crate.
        static int TargetCount(float openArea, float coverDensity)
        {
            if (openArea <= 0f || coverDensity <= 0f)
            {
                return 0;
            }

            return (int)MathF.Round(
                openArea * BaselineCoverPerSquareMetre * coverDensity, MidpointRounding.AwayFromZero);
        }

        static void Validate(ArenaParams parameters)
        {
            if (!(parameters.CoverDensity >= 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.CoverDensity, "Cover density must not be negative.");
            }

            if (!(parameters.LowToHighCoverRatio >= 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.LowToHighCoverRatio,
                    "The low-to-high cover ratio must not be negative.");
            }
        }

        /// <summary>
        /// Claims everything cover has to keep out of, so the sampler spends its candidates on
        /// floor that stands a chance.
        /// </summary>
        static PlacementGrid BuildGrid(
            ArenaLayout layout,
            IReadOnlyList<Placement> structures,
            IReadOnlyList<Rect2> doorways,
            float margin)
        {
            var grid = new PlacementGrid(layout.Grid);

            grid.Claim(layout.SpawnAreaA.Expanded(SpawnClearance + margin));
            grid.Claim(layout.SpawnAreaB.Expanded(SpawnClearance + margin));

            for (int i = 0; i < structures.Count; i++)
            {
                grid.Claim(structures[i].Footprint.Expanded(StructureClearance + margin));
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                grid.Claim(doorways[i].Expanded(DoorwayClearance + margin));
            }

            return grid;
        }

        /// <summary>
        /// Reads back the doorway rectangles the structures declared in their own metadata.
        /// </summary>
        /// <remarks>
        /// Through the document rather than passed along from the structure stage on purpose: the
        /// declared metadata is the contract, and reading it here is what the validator and the
        /// editor overlay will do too.
        /// </remarks>
        static List<Rect2> ReadDoorways(WorldDoc doc)
        {
            var doorways = new List<Rect2>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!placed.Metadata.TryGetValue(ArenaLayoutGenerator.DoorwayCountKey, out string countText) ||
                    !int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    continue;
                }

                for (int d = 0; d < count; d++)
                {
                    string key = ArenaLayoutGenerator.DoorwayKeyPrefix +
                                 d.ToString("00", CultureInfo.InvariantCulture);
                    if (placed.Metadata.TryGetValue(key, out string rect))
                    {
                        doorways.Add(RectMetadata.Parse(rect));
                    }
                }
            }

            return doorways;
        }

        /// <summary>Returns the number of props this lane was asked for.</summary>
        static int PlaceInLane(
            WorldDoc doc,
            ArenaLayout layout,
            ArenaLane lane,
            IReadOnlyList<Placement> structures,
            IReadOnlyList<Rect2> doorways,
            PlacementGrid grid,
            IReadOnlyList<CatalogEntry> low,
            IReadOnlyList<CatalogEntry> high,
            float minSpacing,
            float margin,
            PlacementStats stats)
        {
            ArenaParams parameters = doc.Parameters;

            Rect2 band = lane.Band.Expanded(-margin);
            if (band.Width <= 0f || band.Depth <= 0f)
            {
                // A lane narrower than the props it would hold. Nothing legal fits in it.
                return 0;
            }

            var openCells = new List<Vec2>();
            grid.CollectOpenCentres(band, openCells);
            float openArea = openCells.Count * layout.Grid.CellArea;

            int target = TargetCount(openArea, parameters.CoverDensity);
            if (target <= 0)
            {
                return 0;
            }

            Rng stream = new Rng(parameters.Seed).Fork("cover/" + lane.Id);
            ConstraintSet constraints = BuildConstraints(layout, lane, structures, doorways);

            float radius = MathF.Max(minSpacing, MathF.Sqrt(PoissonYield * openArea / (target * SampleSurplus)));

            // The cap is a safety valve, not a target: it sits far enough above what the radius
            // yields that it never truncates the sample set, because truncating Bridson's output
            // would leave the survivors clustered around its first sample.
            List<Vec2> positions = PoissonDisk.Sample(grid, band, radius, target * 4, ref stream);
            Shuffle(positions, ref stream);

            int budget = target * AttemptsPerTargetProp;
            int spent = 0;
            int placed = 0;

            for (int i = 0; i < positions.Count && placed < target && spent < budget; i++)
            {
                Vec2 position = layout.Grid.Snap(positions[i]);

                for (int attempt = 0; attempt < EntriesPerPosition && spent < budget; attempt++)
                {
                    CatalogEntry entry = PickCover(low, high, parameters.LowToHighCoverRatio, ref stream);
                    Placement candidate = parameters.FineCoverRotation
                        ? Placement.AtYawStep(entry, position, stream.NextRange(0, YawStep.Count))
                        : Placement.AtQuarterTurn(entry, position, stream.NextRange(0, QuarterTurn.Count));

                    ConstraintResult result = constraints.Evaluate(candidate);
                    stats.Record(result);
                    spent++;

                    if (!result.IsOk)
                    {
                        continue;
                    }

                    constraints.Commit(candidate);
                    doc.GeneratedObjects.Add(new PlacedObject(
                        $"map/{lane.Id}/cover_{placed.ToString("00", CultureInfo.InvariantCulture)}",
                        candidate.LogicalId,
                        candidate.Pose,
                        TagArray(candidate.Tags),
                        new Dictionary<string, string> { { ArenaLayoutGenerator.LaneKey, lane.Id } }));
                    placed++;
                    break;
                }
            }

            return target;
        }

        /// <summary>
        /// The rules cover is placed under, cheapest first: evaluation stops at the first
        /// rejection, and the reason recorded is that first rule.
        /// </summary>
        /// <remarks>
        /// One set per lane, because <c>WithinLane</c> names the lane. Structures are committed
        /// into every lane's set — a building may overhang the band it belongs to — while cover
        /// only ever meets cover from its own lane, which is exactly what <c>WithinLane</c>
        /// guarantees by keeping every footprint inside a band, and the bands do not touch.
        /// </remarks>
        static ConstraintSet BuildConstraints(
            ArenaLayout layout,
            ArenaLane lane,
            IReadOnlyList<Placement> structures,
            IReadOnlyList<Rect2> doorways)
        {
            var constraints = new ConstraintSet(layout, new[]
            {
                PlacementConstraint.OnGrid(layout.Grid.CellSize),
                PlacementConstraint.InsidePlayfield(),
                PlacementConstraint.WithinLane(lane.Id),
                PlacementConstraint.ClearOfSpawn(SpawnClearance),
                PlacementConstraint.NotBlockingDoorway(DoorwayClearance),
                PlacementConstraint.MinDistanceFrom(ArenaLayoutGenerator.StructureTag, StructureClearance),
                PlacementConstraint.NoOverlap(PropMargin),
            });

            for (int i = 0; i < structures.Count; i++)
            {
                constraints.Commit(structures[i]);
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                constraints.AddDoorway(doorways[i]);
            }

            return constraints;
        }

        /// <summary>
        /// Chooses what to put down: the class first, from the low-to-high ratio, then the entry,
        /// from the catalog weights within that class.
        /// </summary>
        /// <remarks>
        /// Two stages rather than one weighted pick over all cover, so the two knobs stay
        /// independent. Catalog weights say which crate; the ratio parameter says how often a
        /// crate rather than a barrier, and it keeps saying it when the art pack gains a dozen new
        /// low-cover entries.
        /// </remarks>
        static CatalogEntry PickCover(
            IReadOnlyList<CatalogEntry> low, IReadOnlyList<CatalogEntry> high, float ratio, ref Rng rng)
        {
            float lowShare = ratio / (ratio + 1f);
            bool wantsLow = rng.NextFloat() < lowShare;

            IReadOnlyList<CatalogEntry> chosen = wantsLow ? low : high;
            if (chosen.Count == 0)
            {
                chosen = wantsLow ? high : low;
            }

            return rng.WeightedPick(chosen, e => e.Weight);
        }

        /// <summary>
        /// How far the smallest piece of cover reaches from its own pivot, whichever way it is
        /// turned. Two positions sampled twice this far apart plus the margin can always be filled
        /// by something, which is what makes it the floor on the Poisson radius: below it, even
        /// the smallest prop would not fit between its neighbours.
        /// </summary>
        static float SmallestPropRadius(IReadOnlyList<CatalogEntry> low, IReadOnlyList<CatalogEntry> high)
        {
            float smallest = float.MaxValue;
            smallest = SmallestRadius(low, smallest);
            smallest = SmallestRadius(high, smallest);
            return smallest;
        }

        static float SmallestRadius(IReadOnlyList<CatalogEntry> entries, float smallest)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                // The corner furthest from the pivot, which is what any rotation sweeps out. A
                // catalog footprint is usually centred on its pivot but is not required to be.
                Rect2 footprint = entries[i].Footprint;
                float x = MathF.Max(MathF.Abs(footprint.MinX), MathF.Abs(footprint.MaxX));
                float z = MathF.Max(MathF.Abs(footprint.MinZ), MathF.Abs(footprint.MaxZ));
                smallest = MathF.Min(smallest, MathF.Sqrt(x * x + z * z));
            }

            return smallest;
        }

        /// <summary>
        /// Attaches a small prop to some of the prop surfaces the placed objects declare.
        /// </summary>
        /// <remarks>
        /// The child's stable id nests under its parent's — <c>.../structure_00/socket_02/prop_00</c>
        /// — so it survives a regeneration for exactly as long as the object it sits on does, and
        /// a diff shows where it came from. Nothing recurses: a prop on a surface does not get a
        /// surface of its own.
        /// </remarks>
        static void PlaceSocketProps(WorldDoc doc, Catalog catalog, ArenaParams parameters)
        {
            List<CatalogEntry> props = SmallProps(catalog);
            if (props.Count == 0)
            {
                return;
            }

            Rng stream = new Rng(parameters.Seed).Fork("cover/sockets");

            // Snapshot the count: the loop appends to the same list it is walking, and a prop is
            // never a parent.
            int parents = doc.GeneratedObjects.Count;
            for (int i = 0; i < parents; i++)
            {
                PlacedObject parent = doc.GeneratedObjects[i];
                CatalogEntry entry = catalog.Find(parent.LogicalId);
                if (entry == null)
                {
                    continue;
                }

                for (int s = 0; s < entry.Sockets.Count; s++)
                {
                    CatalogSocket socket = entry.Sockets[s];
                    if (!socket.HasTag(PropSurfaceTag) || stream.NextFloat() >= SocketFillChance)
                    {
                        continue;
                    }

                    CatalogEntry prop = stream.WeightedPick(props, e => e.Weight);
                    var metadata = new Dictionary<string, string>();
                    if (parent.Metadata.TryGetValue(ArenaLayoutGenerator.LaneKey, out string lane))
                    {
                        metadata[ArenaLayoutGenerator.LaneKey] = lane;
                    }

                    doc.GeneratedObjects.Add(new PlacedObject(
                        $"{parent.StableId}/socket_{s.ToString("00", CultureInfo.InvariantCulture)}/prop_00",
                        prop.LogicalId,
                        parent.Pose.Transform(socket.LocalPose),
                        TagArray(prop.Tags),
                        metadata));
                }
            }
        }

        static List<CatalogEntry> SmallProps(Catalog catalog)
        {
            IReadOnlyList<CatalogEntry> lowCover = catalog.Query(TagQuery.All(CoverTag, LowCoverTag));
            var props = new List<CatalogEntry>(lowCover.Count);
            for (int i = 0; i < lowCover.Count; i++)
            {
                if (lowCover[i].Footprint.Area <= SocketPropMaxArea)
                {
                    props.Add(lowCover[i]);
                }
            }

            return props;
        }

        // Fisher-Yates from the lane's own stream. In place, walking downward, so the result is a
        // function of the draws alone.
        static void Shuffle(List<Vec2> items, ref Rng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                Vec2 swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        static string[] TagArray(IReadOnlyList<string> tags)
        {
            var copy = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                copy[i] = tags[i];
            }

            return copy;
        }
    }
}
