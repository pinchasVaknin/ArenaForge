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
    /// <para>
    /// <strong>The roads are down before this runs, and they change the answer twice.</strong> A
    /// carriageway is ground no prop may stand in — <see cref="ConstraintKind.OffReservedPath"/>,
    /// the same rule a building's floor keeps its walkways with — and it is also the ground cover
    /// most wants to be beside, which is what <see cref="Roadside"/> says by trying the positions
    /// along a road before the rest. Both are silent on a map with no road on it.
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

        /// <summary>
        /// How near a carriageway a sampled position has to be to be tried before the rest, in
        /// metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What makes a road contested rather than decorative. A carriageway with nothing along it
        /// is a corridor players cross at a run and never fight over; the thing that makes it worth
        /// holding is cover beside it — off the road, within a stride of it.
        /// </para>
        /// <para>
        /// <strong>A metre, and it is a preference rather than a rule.</strong> The sampler is asked
        /// for more positions than a lane's target needs, so which ones are tried first is which
        /// ones get filled, and the ground further from a road is still filled with whatever the
        /// target has left. A rule instead of an ordering throws that remainder away: cover far from
        /// a road is rejected rather than deferred, and a lane comes out with a third of what it
        /// asked for.
        /// </para>
        /// <para>
        /// <strong>It pays for the road rather than decorating it.</strong> Measured over seeds
        /// 1..1000 of the default map at a road density of 1: with no preference the median piece of
        /// cover stands 3.7 m from the nearest carriageway, cover coverage runs down to 0.567 and
        /// ten seeds fall under the 0.6 threshold; at this reach the median is 2.8 m, coverage runs
        /// 0.602 to 0.771 and none do. A road's own ground is a fifth of the map, it is the emptiest
        /// ground on it, and it is covered from the verge beside it or not at all.
        /// </para>
        /// <para>
        /// One metre is one cell of the default placement grid, which is what makes it the first row
        /// of positions beside a carriageway rather than a share of a lane. Wider stops being a
        /// preference for anything: at six metres it takes in most of a lane and two seeds fail
        /// again.
        /// </para>
        /// </remarks>
        public const float RoadsideReach = 1f;

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
        /// <param name="terrain">The ground, with the structures' foundations already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="structures">Structures already committed, with their world footprints.</param>
        /// <param name="anchored">
        /// What the anchored stages stood up before this one ran — the dressing round those
        /// structures and the fence round the map — which cover has to place around exactly as it
        /// places around the structures themselves.
        /// </param>
        /// <param name="roads">
        /// The network laid across the map before this one ran. Its corridors are ground no prop
        /// may stand in and the thing every prop is placed beside; an empty network — which is what
        /// a <see cref="ArenaParams.RoadDensity"/> of zero gives — reserves nothing and biases
        /// nothing.
        /// </param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A cover parameter is outside its supported range.</exception>
        public static void Place(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            Catalog catalog,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored,
            RoadNetwork roads)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (structures == null)
            {
                throw new ArgumentNullException(nameof(structures));
            }

            if (anchored == null)
            {
                throw new ArgumentNullException(nameof(anchored));
            }

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
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

                List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);
                IReadOnlyList<Rect2> corridors = roads.Corridors;

                // Two grids over the same floor, because a road makes "how much cover does this
                // lane want" and "where may it go" two different questions. The floor grid answers
                // the first: a carriageway is ground a player moves through, so a lane with a road
                // down it wants exactly the cover it wanted without one. The open grid answers the
                // second, and has the carriageways taken out of it so the sampler spends its
                // candidates on ground a prop can actually stand on.
                //
                // The same split applies to what the road stages stand *on* that ground — see
                // IsRoadside. A kerb and a lamp post are things a player walks past, not ground
                // that has stopped being part of the fight, so neither shrinks the floor the target
                // is counted from; both still take their own footprint out of the grid the sampler
                // draws from, because nothing may be placed inside one.
                PlacementGrid floor = BuildGrid(layout, structures, anchored, doorways, margin, false);
                PlacementGrid open = BuildGrid(layout, structures, anchored, doorways, margin, true);
                for (int i = 0; i < corridors.Count; i++)
                {
                    open.Claim(corridors[i]);
                }

                for (int i = 0; i < layout.Lanes.Count; i++)
                {
                    target += PlaceInLane(
                        doc, layout, terrain, layout.Lanes[i], structures, anchored, doorways,
                        corridors, floor, open, low, high, minSpacing, margin, stats);
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
        /// <param name="claimRoadsideArt">
        /// Whether the kerbing and street furniture the road stages laid take their ground out of
        /// this grid. True for the grid the positions are drawn from and false for the grid the
        /// target is counted off — see <see cref="IsRoadside"/>.
        /// </param>
        static PlacementGrid BuildGrid(
            ArenaLayout layout,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored,
            IReadOnlyList<Rect2> doorways,
            float margin,
            bool claimRoadsideArt)
        {
            var grid = new PlacementGrid(layout.Grid);

            grid.Claim(layout.SpawnAreaA.Expanded(SpawnClearance + margin));
            grid.Claim(layout.SpawnAreaB.Expanded(SpawnClearance + margin));

            for (int i = 0; i < structures.Count; i++)
            {
                grid.Claim(structures[i].Footprint.Expanded(StructureClearance + margin));
            }

            // The dressing round a building is claimed at the sampler's own margin and no more. A
            // structure keeps a metre and a half of floor clear because it is a thing a fight is
            // fought round; a bush growing against its wall is a thing you walk past, and giving
            // that a building's clearance would carve out a lane's worth of floor for a row of
            // shrubs.
            for (int i = 0; i < anchored.Count; i++)
            {
                if (!claimRoadsideArt && IsRoadside(anchored[i]))
                {
                    continue;
                }

                grid.Claim(anchored[i].Footprint.Expanded(margin));
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                grid.Claim(doorways[i].Expanded(DoorwayClearance + margin));
            }

            return grid;
        }

        /// <summary>
        /// True for the art the road stages lay along a carriageway: the kerbing and the street
        /// furniture.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>What a road puts down does not stop the ground being part of the fight.</strong>
        /// A carriageway is already left out of the floor the target is counted from, on the
        /// reasoning that a player moves through it — and a kerb along its edge and a bench on its
        /// verge are the same claim about the same ground. Counting them shrank that floor by
        /// everything a road stage had stood on it, and the target fell with it: measured over seeds
        /// 1..1000 of the default map at a road density of 1, cover coverage ran to a mean of 0.598
        /// with kerbing in the catalog against 0.700 without, and 531 of the thousand seeds fell
        /// under the 0.6 threshold where none had. A road is not supposed to make a map want less
        /// cover.
        /// </para>
        /// <para>
        /// <strong>It is not the same claim as the dressing round a building.</strong> A hedge and a
        /// heap of barrels are placed to be in the way — they are the yard, and the floor they cover
        /// is floor that has become something else. A kerb is a line on the ground and a lamp post
        /// is a post; a lane with either down it is a lane that still wants what it wanted, and a
        /// piece of cover simply stands somewhere else in it.
        /// </para>
        /// <para>
        /// Read off the tags rather than off which stage produced it, because the tags are what a
        /// document carries — the same way <c>RoadNetwork.IsPlacedAfterTheRoads</c> names the same
        /// two stages plus the cover.
        /// </para>
        /// </remarks>
        static bool IsRoadside(Placement placed) =>
            placed.HasTag(RoadKerbs.KerbTag) || placed.HasTag(RoadFurniture.FurnitureTag);

        /// <summary>Returns the number of props this lane was asked for.</summary>
        static int PlaceInLane(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            ArenaLane lane,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> corridors,
            PlacementGrid floor,
            PlacementGrid open,
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

            var cells = new List<Vec2>();
            floor.CollectOpenCentres(band, cells);
            float openArea = cells.Count * layout.Grid.CellArea;

            cells.Clear();
            open.CollectOpenCentres(band, cells);
            float standableArea = cells.Count * layout.Grid.CellArea;

            int target = TargetCount(openArea, parameters.CoverDensity);
            if (target <= 0)
            {
                return 0;
            }

            Rng stream = new Rng(parameters.Seed).Fork("cover/" + lane.Id);
            ConstraintSet constraints = BuildConstraints(
                layout, lane, structures, anchored, doorways, corridors);

            // Sized off the ground a prop may stand on rather than off the floor the target was
            // counted from, because what the radius is for is yielding SampleSurplus times the
            // target in positions worth trying: a lane a road has taken a quarter of would
            // otherwise be sampled as sparsely as one with no road in it and offer a quarter fewer.
            float radius = MathF.Max(
                minSpacing, MathF.Sqrt(PoissonYield * standableArea / (target * SampleSurplus)));

            // The cap is a safety valve, not a target: it sits far enough above what the radius
            // yields that it never truncates the sample set, because truncating Bridson's output
            // would leave the survivors clustered around its first sample.
            List<Vec2> positions = PoissonDisk.Sample(open, band, radius, target * 4, ref stream);
            Shuffle(positions, ref stream);
            Roadside(positions, corridors);

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

                    // The rules are two-dimensional and the ground is not, so the height is added
                    // once a candidate has been accepted — the same division the building
                    // generator makes between a floor's rules and the storey it is on.
                    Pose pose = candidate.Pose.WithPosition(
                        candidate.Pose.Position + new Vec3(0f, terrain.HeightAt(position), 0f));

                    doc.GeneratedObjects.Add(new PlacedObject(
                        $"map/{lane.Id}/cover_{placed.ToString("00", CultureInfo.InvariantCulture)}",
                        candidate.LogicalId,
                        pose,
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
        /// <para>
        /// One set per lane, because <c>WithinLane</c> names the lane. Structures are committed
        /// into every lane's set — a building may overhang the band it belongs to — while cover
        /// only ever meets cover from its own lane, which is exactly what <c>WithinLane</c>
        /// guarantees by keeping every footprint inside a band, and the bands do not touch.
        /// </para>
        /// <para>
        /// <c>OffReservedPath</c> is the carriageway, and it sits where it does because it is the
        /// cheapest of the rules that can reject a candidate outright and the one a road makes
        /// reject most often. On a map with no road on it there is no strip to be off and it never
        /// fires, which is what leaves those maps exactly as they were.
        /// </para>
        /// </remarks>
        static ConstraintSet BuildConstraints(
            ArenaLayout layout,
            ArenaLane lane,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> corridors)
        {
            var constraints = new ConstraintSet(layout, new[]
            {
                PlacementConstraint.OnGrid(layout.Grid.CellSize),
                PlacementConstraint.InsidePlayfield(),
                PlacementConstraint.WithinLane(lane.Id),
                PlacementConstraint.ClearOfSpawn(SpawnClearance),
                PlacementConstraint.NotBlockingDoorway(DoorwayClearance),
                PlacementConstraint.MinDistanceFrom(ArenaLayoutGenerator.StructureTag, StructureClearance),
                PlacementConstraint.OffReservedPath(),
                PlacementConstraint.NoOverlap(PropMargin),
            });

            for (int i = 0; i < structures.Count; i++)
            {
                constraints.Commit(structures[i].Ground);
            }

            for (int i = 0; i < anchored.Count; i++)
            {
                constraints.Commit(anchored[i]);
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                constraints.AddDoorway(doorways[i]);
            }

            // A corridor is reserved ground and nothing else. It is deliberately not committed as a
            // placement, though committing one is the only way a MaxDistanceFrom rule could measure
            // to it: the committed list is what NoOverlap walks too, so a road in front of it gets a
            // second opinion about how wide a road is on top of the one OffReservedPath already has.
            // That is not free. NoOverlap's margin is three quarters of a metre, the placement grid
            // rounds that up to a whole cell, and over seeds 1..1000 of the default map at a road
            // density of 1 it takes cover coverage from 0.700 to 0.658 and puts 86 seeds under the
            // threshold. What a road wants of cover is that it stands near it, and Roadside below is
            // where that is said instead.
            for (int i = 0; i < corridors.Count; i++)
            {
                constraints.AddReservedPath(corridors[i]);
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

        /// <summary>
        /// Moves the positions within <see cref="RoadsideReach"/> of a carriageway to the front,
        /// leaving both halves in the order the shuffle left them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole of the roadside bias, and it is an ordering rather than a rule because a lane
        /// is offered more positions than its target needs: putting the roadside ones first fills
        /// them first and leaves the rest of the lane the remainder, where a rule would have thrown
        /// the remainder away. Measured edge to edge against the corridor rectangles with the same
        /// <see cref="Rect2.Distance"/> the clearance rules use, so "a metre from the road" means
        /// here what it means in <see cref="ConstraintKind.MinDistanceFrom"/>.
        /// </para>
        /// <para>
        /// A stable partition, not a sort: two positions the same distance from a road have to stay
        /// in the order the shuffle put them, or the map depends on how a comparison sort broke a
        /// tie. And it returns untouched when there is no road, which is what makes a map at a road
        /// density of zero the map it was before this stage existed.
        /// </para>
        /// </remarks>
        static void Roadside(List<Vec2> positions, IReadOnlyList<Rect2> corridors)
        {
            if (corridors.Count == 0)
            {
                return;
            }

            var near = new List<Vec2>(positions.Count);
            var far = new List<Vec2>(positions.Count);

            for (int i = 0; i < positions.Count; i++)
            {
                var at = new Rect2(positions[i].X, positions[i].Y, positions[i].X, positions[i].Y);
                bool close = false;
                for (int c = 0; c < corridors.Count && !close; c++)
                {
                    close = Rect2.Distance(at, corridors[c]) <= RoadsideReach;
                }

                (close ? near : far).Add(positions[i]);
            }

            positions.Clear();
            positions.AddRange(near);
            positions.AddRange(far);
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
