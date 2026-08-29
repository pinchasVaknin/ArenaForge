using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Stands street furniture along the verges of a road network: the art a workspace files under
    /// <c>road/furniture</c>, put down at the places a road has something to mark and spaced out
    /// through the gaps between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Distance along the road, not position along the polyline.</strong> A carriageway's
    /// centreline is smoothed and string-pulled, so its vertices are dense round a bend and tens of
    /// metres apart on a straight — see <see cref="RoadNetwork"/>. Stepping that list in parameter
    /// space puts six pieces round a corner and none between it and the next one, which is the
    /// one visual defect this stage exists to avoid. So each polyline gets an
    /// <see cref="ArcTable"/> once and every position below is a distance in metres sampled
    /// against it.
    /// </para>
    /// <para>
    /// <strong>The events go down first and the spacing fills what is left.</strong> Furniture
    /// placed at an even interval reads as a generated map more loudly than furniture placed
    /// badly: a row of identical lamp posts at exactly nine metres is a thing no street has and
    /// nothing else on the map does. What a real verge has is pieces at the places that
    /// <em>mean</em> something — the corner of a junction, the apex of a bend, the approach to a
    /// door — and a looser scatter between them. So the events are collected first, and the
    /// stretches between consecutive events are filled at a spacing jittered off this stage's own
    /// stream.
    /// </para>
    /// <para>
    /// <strong>Two collectors, four kinds of event.</strong> A junction cut answers three of them
    /// at once, because in this network a road's class only ever changes where two roads meet: a
    /// branch is a <see cref="RoadJunctionKind.Attachment"/> on the trunk it joins, so the corner
    /// of that junction <em>is</em> the road-class transition, and a
    /// <see cref="RoadJunctionKind.Portal"/> is the same corner set back far enough that a piece
    /// standing on it is on the approach to the door rather than in it. Only the bends need a
    /// second collector. A <see cref="RoadJunctionKind.Terminal"/> gets no event at all — a spawn
    /// is ground deliberately kept clear of props, and <see cref="ConstraintKind.ClearOfSpawn"/>
    /// would refuse what one produced.
    /// </para>
    /// <para>
    /// <strong>A bend is measured off its own chord rather than through an angle.</strong> How far
    /// the road runs off the straight line between the points <see cref="ApexWindow"/> either side
    /// of it is a length in metres, and comparing it against a length is arithmetic; turning a
    /// polyline into an angle is a call into trigonometry, which is not bit-identical across
    /// runtimes. That is the argument <see cref="YawStep"/> makes about rotations, and it applies
    /// to measuring a curve for the same reason. Reading it over a window rather than at a vertex
    /// is what makes a smoothed bend one apex instead of the six small turns it is built from.
    /// </para>
    /// <para>
    /// <strong>Turned by the road and seated beyond the kerb.</strong> The rotation is the run's
    /// own tangent quantised to the nearest <see cref="YawStep"/> — through
    /// <see cref="WallRun.Face.Along"/>, which is where every oblique run in this tool gets its
    /// yaw — so nothing here computes an angle either. Across the run a piece stands clear of the
    /// carriageway by the thickest kerb the catalog holds plus <see cref="Verge"/>, so it is
    /// outside the edging rather than judged against it and refused.
    /// </para>
    /// <para>
    /// <strong>Height is read, never cast.</strong> A piece is stood at
    /// <see cref="TerrainField.HeightAt"/> with <see cref="Placement.AtYawStep"/> lifting it by the
    /// entry's own <see cref="CatalogEntry.BaseOffset"/>, exactly as every other placed object on
    /// the map is. A raycast would answer with whatever colliders a scene happened to have, would
    /// depend on physics settings that are not inputs to the document, and would put the result
    /// outside <see cref="WorldDoc"/> where no override could reach it.
    /// </para>
    /// <para>
    /// <strong>Every candidate is judged, and a bench in a doorway is a crate in a doorway.</strong>
    /// The rules are <see cref="Rules"/>, and the refusals are kept: a stretch of verge with
    /// nothing on it is a stretch something was refused, and the tally says which rule refused it.
    /// </para>
    /// <para>
    /// <strong>A workspace with nothing in the folder gets no furniture.</strong> Not a draw is
    /// taken and not an object is placed, which is the bargain <see cref="RoadKerbs"/> and
    /// <see cref="PerimeterFence"/> both make with an empty folder: the art decides whether a
    /// feature is in a map, and the absence of art is an answer rather than a fault.
    /// </para>
    /// <para>
    /// <strong>Furniture is left out of the router's measure of shelter</strong>, on exactly the
    /// terms the kerbs and the cover are — it is placed <em>from</em> a network, so a rebuild that
    /// counted it would route the next roads round the verges the last ones were given. See
    /// <c>RoadNetwork.IsPlacedAfterTheRoads</c>.
    /// </para>
    /// </remarks>
    public static class RoadFurniture
    {
        /// <summary>Tag a catalog entry must carry to be stood along a verge.</summary>
        /// <remarks>What a workspace files in <c>Props/Road/Furniture</c>.</remarks>
        public const string FurnitureTag = "road/furniture";

        /// <summary>Prefix of the world metadata keys holding this stage's placement statistics.</summary>
        public const string StatsPrefix = "furniture_";

        /// <summary>Stable id prefix every piece sits under — <c>map/</c> plus its road's own id.</summary>
        /// <remarks>
        /// A piece of street furniture belongs to a carriageway rather than to a lane, so it is
        /// filed under the carriageway: <c>map/road/artery_00/furniture_03</c>. The segment ids are
        /// a function of the document, so the same seed numbers the same piece the same way and an
        /// edit made against one survives a regeneration.
        /// </remarks>
        public const string IdPrefix = "map/";

        /// <summary>What a piece's own index is separated from its carriageway's id by.</summary>
        public const string FurnitureSegment = "/furniture_";

        /// <summary>Label the stream this stage's draws are forked under.</summary>
        const string StreamLabel = "road_furniture";

        /// <summary>Clear ground kept between the kerb line and a piece of furniture, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Measured out from the far face of the thickest kerb the catalog holds rather than from
        /// the carriageway, so a workspace that fills <c>Props/Road/Kerb</c> gets furniture behind
        /// its edging instead of furniture refused for standing in it — and a workspace that does
        /// not gets the same verge measured from the road.
        /// </para>
        /// <para>
        /// <strong>It is sized by the reservation, not by the road.</strong> What a piece has to
        /// clear is <see cref="RoadNetwork.Corridors"/>, which is the carriageway as axis-aligned
        /// boxes and so claims ground beside a diagonal road that the road does not cover — and a
        /// piece turned off the axes is itself judged by the box round it, which claims more again.
        /// Two metres is where those two together stop costing anything. Measured over seeds 1..200
        /// of the default map at a road density of 1, the share of candidates the rules accepted
        /// runs 16.7% at no verge at all, 25.3% at three quarters of a metre, 30.8% at a metre and
        /// a half, <strong>32.7% here</strong>, and 33.2% at both two and a half and three — while
        /// the pieces refused for leaving the playfield go 4, 23, 69 over the same three. The
        /// figure flattens above this and the cost does not.
        /// </para>
        /// <para>
        /// <strong>What is left is the braid, and no verge reaches it.</strong> A third of the
        /// network is two carriageways over the same ground by design — see
        /// <see cref="RoadNetwork.UnbraidedLength"/> — and the verge of a road running inside
        /// another road is that other road. Those positions are refused however far out a piece is
        /// pushed, which is why widening this past two metres buys nothing: what it would have to
        /// clear is not the road it is edging.
        /// </para>
        /// <para>
        /// Two metres of pavement behind a kerb is also what a verge is, which is worth saying
        /// plainly: the number is defensible as a measurement and it is not a strange one to look
        /// at.
        /// </para>
        /// </remarks>
        public const float Verge = 2f;

        /// <summary>How far apart the fill stands pieces along a verge, in metres.</summary>
        /// <remarks>
        /// Nine metres, which on the default sixty-metre map is five or six pieces down an artery
        /// and one or two down a branch. Close enough that a road reads as a furnished street and
        /// far enough that the fill is filler: the pieces that carry the meaning are the ones at
        /// the events, and a spacing tight enough to crowd them would bury them.
        /// </remarks>
        public const float Spacing = 9f;

        /// <summary>
        /// How much of <see cref="Spacing"/> a gap may be shortened or lengthened by, as a share of
        /// it.
        /// </summary>
        /// <remarks>
        /// A third. Evenly spaced lamp posts are the loudest signal that a level was generated, and
        /// the fix is not a smaller number of them — it is that no two gaps are the same. A third
        /// is enough that the rhythm is not readable and little enough that the run still reads as
        /// one line of furniture rather than as a scatter that happens to be near a road.
        /// </remarks>
        public const float SpacingJitter = 1f / 3f;

        /// <summary>Half the chord a bend's depth is measured over, in metres.</summary>
        /// <remarks>
        /// Four metres either side, which is about the length of the shortest bend the router's
        /// smoothing produces. Shorter and a smoothed corner reads as several bends with an apex
        /// each; longer and a corner is averaged away against the straights on both sides of it.
        /// </remarks>
        public const float ApexWindow = 4f;

        /// <summary>The loosest bend that still counts as one, as a radius in metres.</summary>
        /// <remarks>
        /// Stated as a radius because that is the legible form of it — a road turning inside
        /// sixteen metres is a corner a player rounds, where one turning inside eighty is a road
        /// that is not quite straight. It is converted to the depth an arc of that radius reaches
        /// off its own chord over <see cref="ApexWindow"/>, which is the number actually compared,
        /// because a depth is a length and comparing lengths needs no trigonometry.
        ///
        /// It fires often enough to be worth having, which is the other half of choosing it:
        /// measured over seeds 1..200 of the default map at a road density of 1, a third of the
        /// carriageways carry at least one apex and a map carries fourteen at the median. Nine in
        /// ten of them end up with a piece within a gap's reach.
        /// </remarks>
        public const float ApexRadius = 16f;

        /// <summary>How finely the bend measure is swept along a polyline, in metres.</summary>
        /// <remarks>
        /// The placement grid's own cell, which is the resolution everything else about a road is
        /// decided at — see <c>RoadNetwork.EndRadius</c>, which is a metre for the same reason.
        /// </remarks>
        const float ApexStep = 1f;

        /// <summary>How many sides a carriageway has furniture on: one each, drawn per piece.</summary>
        /// <remarks>
        /// Which side each piece stands on is a draw rather than both sides being filled, because
        /// two runs at one spacing is a ladder — and a ladder is the same defect as an even
        /// spacing, read across the road instead of along it.
        /// </remarks>
        const int Sides = 2;

        /// <summary>
        /// Stands furniture along every carriageway of <paramref name="roads"/> and returns every
        /// piece it put down, in generation order, so the cover stage can place around it.
        /// </summary>
        /// <param name="doc">The document to add the furniture to. Its parameters drive the draws.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="terrain">The ground, with the carriageways already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="roads">The network to furnish. An empty one is furnished with nothing.</param>
        /// <param name="structures">The structures already committed, with their world footprints.</param>
        /// <param name="anchored">Everything else already standing: the boundary, the dressing and the kerbs.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<Placement> Place(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            Catalog catalog,
            RoadNetwork roads,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored)
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

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
            }

            if (structures == null)
            {
                throw new ArgumentNullException(nameof(structures));
            }

            if (anchored == null)
            {
                throw new ArgumentNullException(nameof(anchored));
            }

            var placed = new List<Placement>();
            if (roads.IsEmpty)
            {
                // No verge to furnish. A map at a road density of zero reaches here and leaves with
                // the document it arrived with.
                return placed;
            }

            IReadOnlyList<CatalogEntry> palette = catalog.Query(TagQuery.All(FurnitureTag));
            if (palette.Count == 0)
            {
                // Nothing filed in the furniture folder. Returning before a single draw is what lets
                // a project that has never used it generate the map it always did.
                return placed;
            }

            // Where the far face of the edging is. A piece stands beyond it rather than being
            // judged against it and refused, and a catalog with no kerb art puts the verge on the
            // carriageway itself.
            float kerb = WallRun.ThickestSegment(
                WallRun.Tileable(catalog.Query(TagQuery.All(RoadKerbs.KerbTag))));

            ConstraintSet constraints = Rules(doc, layout, roads, structures, anchored);
            Rng stream = new Rng(doc.Parameters.Seed).Fork(StreamLabel);
            var stats = new PlacementStats();

            var crossings = new List<Crossing>();
            var spans = new List<Span>();
            var events = new List<float>();
            var positions = new List<float>();

            IReadOnlyList<RoadSegment> segments = roads.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegment carriageway = segments[i];
                var table = new ArcTable(carriageway.Points);
                if (!(table.Length > 0f))
                {
                    continue;
                }

                Crossings(roads, layout, table, crossings);
                Open(table.Length, crossings, spans);
                Events(crossings, spans, table, events);
                Fill(spans, events, positions, ref stream);

                float gap = carriageway.Width * 0.5f + kerb + Verge;
                int stood = 0;

                for (int p = 0; p < positions.Count; p++)
                {
                    // Both draws are taken whether or not the piece is stood up, so a refusal is a
                    // gap in the run rather than a shuffle of everything after it — the bargain
                    // WallRun makes for the same reason.
                    int drawn = stream.NextRange(0, Sides) == 0 ? 1 : -1;
                    CatalogEntry entry = stream.WeightedPick(palette, e => e.Weight);

                    int piece = table.PieceAt(positions[p], out float along);
                    Vec2 from = table.Point(piece);
                    Vec2 to = table.Point(piece + 1);

                    // The drawn verge first and the other one after it, with no second draw. Which
                    // side a piece stands on is a matter of taste and whether it may stand there at
                    // all is not: a road braided along another covers one of its two verges and
                    // leaves the far one open, and giving up the position because the draw named
                    // the covered side would put the hole where the rhythm is rather than where the
                    // obstruction is. It is the retry CoverPlacer makes over its own entries, over
                    // the one thing here there is a second of.
                    for (int side = 0; side < Sides; side++)
                    {
                        WallRun.Face face = WallRun.Face.Along(
                            from, to, side == 0 ? drawn : -drawn);

                        Rect2 local = face.Local(entry);
                        Placement candidate = Placement.AtYawStep(
                            entry,
                            face.Point(face.Centred(local, along), face.Against(local, gap)),
                            face.Steps(entry));

                        ConstraintResult result = constraints.Evaluate(candidate);
                        stats.Record(result);
                        if (!result.IsOk)
                        {
                            continue;
                        }

                        Commit(doc, terrain, constraints, candidate, placed, carriageway, stood);
                        stood++;
                        break;
                    }
                }
            }

            stats.WriteTo(doc.Metadata, StatsPrefix);
            return placed;
        }

        /// <summary>
        /// The stretches of a polyline covered by a junction, in metres along it, merged and in
        /// order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A bench in the middle of a crossroads is the same defect as a kerb laid across one, and
        /// it is cut the same way: the run stops where it enters a junction's own disc — the disc
        /// <see cref="RoadNetwork.GradeInto"/> solves a crossing's height over and
        /// <see cref="RoadKerbs"/> shuts its runs at — and picks up on the far side.
        /// </para>
        /// <para>
        /// <strong>The spawn areas are cut out here rather than left to the rule.</strong> A spawn
        /// spans the full width of the map at its own end and the network runs into both of them by
        /// design, so a third of the long axis of a default map is ground
        /// <see cref="ConstraintKind.ClearOfSpawn"/> was always going to refuse — measured over
        /// seeds 1..200 at a road density of 1, a quarter of every candidate this stage offered.
        /// Offering them anyway would not put a bench in a spawn, because the rule is still what
        /// decides; what it would do is spend the run's positions on ground nothing can stand on and
        /// fill the tally with a rejection that says only that a road reaches a spawn. The sampler
        /// and the rules are kept in step here for the reason <see cref="PlacementGrid"/> keeps them
        /// in step everywhere else.
        /// </para>
        /// <para>
        /// It is filed as a <see cref="RoadJunctionKind.Terminal"/> because that is what it is: a
        /// terminal junction is the centre of a spawn, and the area round it is the same feature at
        /// its true shape rather than at a junction's radius. Which is also why it carries no event
        /// — see <see cref="Events"/>.
        /// </para>
        /// <para>
        /// The ground another carriageway covers is <em>not</em> cut here, where a kerb line cuts
        /// it explicitly. A kerb runs parallel to its road at a fixed offset and is inside a
        /// braided one for its whole length; a piece of furniture stands on the far side of a verge
        /// and is a single footprint, so <see cref="ConstraintKind.OffReservedPath"/> answers the
        /// question exactly and says so in the tally — and the piece is offered the other verge
        /// before the position is given up.
        /// </para>
        /// </remarks>
        static void Crossings(
            RoadNetwork roads, ArenaLayout layout, ArcTable table, List<Crossing> crossings)
        {
            crossings.Clear();

            IReadOnlyList<RoadJunction> junctions = roads.Junctions;
            for (int i = 0; i < junctions.Count; i++)
            {
                RoadJunction junction = junctions[i];
                if (table.Crosses(junction.Position, roads.JunctionRadius, out float from, out float to))
                {
                    crossings.Add(new Crossing(from, to, junction.Kind));
                }
            }

            Reserved(layout.SpawnAreaA, table, crossings);
            Reserved(layout.SpawnAreaB, table, crossings);

            // Ascending, so one sweep merges them below. Two discs over the same stretch merge to
            // the same stretch whichever order they were compared in, so an unstable sort over
            // equal keys cannot change the answer — and the kinds are read off the crossings
            // themselves rather than off the merge, so nothing here has to decide which of two
            // overlapping junctions a merged stretch belonged to.
            crossings.Sort(Nearest);
        }

        /// <summary>Cuts the stretch of a polyline that runs through a spawn area and its apron.</summary>
        static void Reserved(Rect2 spawn, ArcTable table, List<Crossing> crossings)
        {
            if (table.Crosses(
                    spawn.Expanded(CoverPlacer.SpawnClearance), out float from, out float to))
            {
                crossings.Add(new Crossing(from, to, RoadJunctionKind.Terminal));
            }
        }

        /// <summary>What is left of a polyline once the junctions are taken out of it.</summary>
        static void Open(float length, IReadOnlyList<Crossing> crossings, List<Span> spans)
        {
            spans.Clear();

            float open = 0f;
            for (int i = 0; i < crossings.Count && open < length; i++)
            {
                if (crossings[i].To <= open)
                {
                    continue;
                }

                if (crossings[i].From > open)
                {
                    spans.Add(new Span(open, MathF.Min(crossings[i].From, length)));
                }

                open = MathF.Max(open, crossings[i].To);
            }

            if (open < length)
            {
                spans.Add(new Span(open, length));
            }
        }

        /// <summary>
        /// Every place along a polyline that has something to mark, in metres along it and in
        /// order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A junction corner is where the road leaves the crossing</strong> — the two ends
        /// of the stretch the cut above took out — which is the first ground on that approach a
        /// piece may stand on. That is the road-class transition as well: a branch meets a trunk at
        /// an <see cref="RoadJunctionKind.Attachment"/>, so the corner of one is where the trunk
        /// stops being the only road there.
        /// </para>
        /// <para>
        /// <strong>A portal's corner is set back by the clearance its doorway keeps.</strong> The
        /// last few metres of a branch stand inside the ground
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> protects, so a piece on the corner there
        /// would be refused for standing in the door. Set back by
        /// <see cref="CoverPlacer.DoorwayClearance"/> it lands on the approach instead, which is
        /// where a bin beside somebody's front door belongs.
        /// </para>
        /// <para>
        /// <strong>A terminal gets nothing.</strong> A spawn area is ground kept clear of props on
        /// purpose, and <see cref="ConstraintKind.ClearOfSpawn"/> is what says so — an event there
        /// would be a candidate drawn to be refused and a rejection in the tally that meant
        /// nothing.
        /// </para>
        /// <para>
        /// An event outside every open stretch is dropped rather than clamped into one. It is
        /// ground inside a junction that some other junction's disc reaches over, and moving it to
        /// the nearest place it would fit would put a piece somewhere nothing asked for.
        /// </para>
        /// </remarks>
        static void Events(
            IReadOnlyList<Crossing> crossings,
            IReadOnlyList<Span> spans,
            ArcTable table,
            List<float> events)
        {
            events.Clear();

            for (int i = 0; i < crossings.Count; i++)
            {
                Crossing crossing = crossings[i];
                if (crossing.Kind == RoadJunctionKind.Terminal)
                {
                    continue;
                }

                float setback = crossing.Kind == RoadJunctionKind.Portal
                    ? CoverPlacer.DoorwayClearance
                    : 0f;

                Consider(crossing.From - setback, spans, events);
                Consider(crossing.To + setback, spans, events);
            }

            Apexes(table, spans, events);

            events.Sort();
        }

        /// <summary>
        /// The bends of a polyline that turn tightly enough to be worth marking, in metres along
        /// it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The depth the road runs off its own chord, swept at <see cref="ApexStep"/> and kept
        /// where it is a local maximum over the threshold <see cref="ApexRadius"/> states. A local
        /// maximum rather than every sample over the threshold, because a bend is one place: the
        /// whole of a corner reads over the threshold and only its deepest point is the apex.
        /// </para>
        /// <para>
        /// The comparison at each end of the run of samples is deliberately lopsided — at least as
        /// deep as the one before, strictly deeper than the one after — so a stretch of equal
        /// samples resolves to its first rather than to none of them or all of them.
        /// </para>
        /// </remarks>
        static void Apexes(ArcTable table, IReadOnlyList<Span> spans, List<float> events)
        {
            // The depth an arc of the loosest bend that counts reaches off its own chord over the
            // window, which is the sagitta: half the window squared over twice the radius. Comparing
            // depths rather than angles is what keeps this out of trigonometry.
            float threshold = ApexWindow * ApexWindow / (2f * ApexRadius);

            int samples = (int)MathF.Floor(table.Length / ApexStep) + 1;
            if (samples < 3)
            {
                return;
            }

            float previous = table.Depth(0f, ApexWindow);
            float current = table.Depth(ApexStep, ApexWindow);

            for (int i = 1; i < samples - 1; i++)
            {
                float next = table.Depth((i + 1) * ApexStep, ApexWindow);

                if (current >= threshold && current >= previous && current > next)
                {
                    Consider(i * ApexStep, spans, events);
                }

                previous = current;
                current = next;
            }
        }

        /// <summary>Keeps an event if it lands on ground the run is open over.</summary>
        static void Consider(float at, IReadOnlyList<Span> spans, List<float> events)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                if (at >= spans[i].From && at <= spans[i].To)
                {
                    events.Add(at);
                    return;
                }
            }
        }

        /// <summary>
        /// Every position a piece is offered, in metres along the polyline: the events, and the
        /// jittered spacing that fills the stretches between them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Walked once from the low end of each open stretch, taking whichever comes first — the
        /// next event or the next spacing — and then measuring the following gap from wherever that
        /// left the cursor. An event therefore resets the rhythm rather than being squeezed between
        /// two pieces of it, which is what makes the events read as the reason the run is spaced
        /// the way it is.
        /// </para>
        /// <para>
        /// <strong>The jitter is drawn for every gap, including the ones an event closes.</strong>
        /// A draw skipped when an event happened to fall first would make the whole run downstream
        /// of it depend on how many events there were, which is the coupling
        /// <see cref="Rng.Fork"/> exists to prevent — inside one stream instead of between two.
        /// </para>
        /// <para>
        /// A piece is offered no closer to the last one than the jitter's own floor, so two events
        /// a metre apart produce one position and not two. What that costs is an event: a corner
        /// that falls inside a bend already marked is marked once.
        /// </para>
        /// </remarks>
        static void Fill(
            IReadOnlyList<Span> spans, IReadOnlyList<float> events, List<float> positions,
            ref Rng stream)
        {
            positions.Clear();

            // The shortest gap the jitter can produce, which is what any position has to clear
            // against the piece before it.
            float closest = Spacing * (1f - SpacingJitter);

            // The last position kept, over the whole polyline rather than over one stretch of it.
            // A junction is cut out of the run and the fill's rhythm does restart on the far side
            // — but the *gap* may not, because a polyline that only clips the edge of a junction
            // disc has a cut a few centimetres wide, and a rhythm restarting there puts the first
            // piece of the new stretch on top of the last piece of the old one. Measured over
            // seeds 1..200 of the default map that was a fifth of every gap in the map, the
            // closest of them half a metre.
            float last = float.NegativeInfinity;
            int next = 0;

            for (int i = 0; i < spans.Count; i++)
            {
                Span span = spans[i];
                while (next < events.Count && events[next] < span.From)
                {
                    next++;
                }

                float cursor = span.From;

                while (cursor <= span.To)
                {
                    float step = Spacing * (1f + stream.NextRange(-SpacingJitter, SpacingJitter));
                    float spaced = cursor + step;

                    bool marked = next < events.Count &&
                                  events[next] <= span.To &&
                                  events[next] < spaced;
                    float at = marked ? events[next] : spaced;

                    if (at > span.To)
                    {
                        break;
                    }

                    if (at - last >= closest)
                    {
                        positions.Add(at);
                        last = at;
                        cursor = at;
                    }
                    else if (!marked)
                    {
                        // A spaced position too near the piece before it, which only happens just
                        // after a cut. The rhythm carries on from where it reached rather than
                        // trying the same place again.
                        cursor = at;
                    }

                    // Every event up to the position just considered is spent, whether or not that
                    // position was kept: it either put a piece down or stood too close to the piece
                    // before it, and neither gets a second hearing. Spending them by the position
                    // rather than by the cursor is what makes the walk finish — an iteration now
                    // either moves the cursor along or takes an event off the list.
                    while (next < events.Count && events[next] <= at)
                    {
                        next++;
                    }
                }
            }
        }

        static int Nearest(Crossing a, Crossing b)
        {
            int order = a.From.CompareTo(b.From);
            return order != 0 ? order : a.To.CompareTo(b.To);
        }

        /// <summary>
        /// The rules a piece of street furniture is stood under, over everything already on the
        /// ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cheapest first, because evaluation stops at the first rejection and the reason recorded
        /// is that first rule. <see cref="ConstraintKind.InsidePlayfield"/> is two comparisons and
        /// is not on the list this stage was asked for — it is on every other stage's, and a bench
        /// standing outside the edge of the world is not a placement anybody wants recorded as
        /// legal.
        /// </para>
        /// <para>
        /// <strong><see cref="ConstraintKind.OffReservedPath"/> is the one that does the work.</strong>
        /// A carriageway is ground a player moves through and a piece of furniture standing in it is
        /// a bollard in the middle of the road. It is the same rule a room's walkway is kept clear
        /// with and the same one the cover placer names, over the same rectangles — see
        /// <see cref="RoadNetwork.Corridors"/> — so nothing here has a second opinion about where a
        /// road is. What it costs is the conservatism of those rectangles round a diagonal road,
        /// which is what <see cref="Verge"/> is sized against.
        /// </para>
        /// <para>
        /// <see cref="ConstraintKind.NoOverlap"/> takes a zero margin. Everything a piece has to
        /// keep its distance from is already stated by a rule of its own — the carriageway by the
        /// reservation, a door by its clearance, a spawn by its apron — and a margin on top of them
        /// would be a second, unnamed clearance round the kerbs and the boundary that a piece is
        /// meant to be able to stand beside.
        /// </para>
        /// </remarks>
        static ConstraintSet Rules(
            WorldDoc doc,
            ArenaLayout layout,
            RoadNetwork roads,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored)
        {
            var constraints = new ConstraintSet(layout, new[]
            {
                PlacementConstraint.InsidePlayfield(),
                PlacementConstraint.ClearOfSpawn(CoverPlacer.SpawnClearance),
                PlacementConstraint.NotBlockingDoorway(CoverPlacer.DoorwayClearance),
                PlacementConstraint.OffReservedPath(),
                PlacementConstraint.NoOverlap(0f),
            });

            for (int i = 0; i < structures.Count; i++)
            {
                constraints.Commit(structures[i].Ground);
            }

            for (int i = 0; i < anchored.Count; i++)
            {
                constraints.Commit(anchored[i]);
            }

            List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);
            for (int i = 0; i < doorways.Count; i++)
            {
                constraints.AddDoorway(doorways[i]);
            }

            IReadOnlyList<Rect2> corridors = roads.Corridors;
            for (int i = 0; i < corridors.Count; i++)
            {
                constraints.AddReservedPath(corridors[i]);
            }

            return constraints;
        }

        /// <summary>
        /// Accepts a piece: into the constraint set, into the running list the cover stage is
        /// given, and into the document at the height of the ground under it.
        /// </summary>
        /// <remarks>
        /// Stood on the ground rather than planted in it, which is the treatment everything but a
        /// fence gets. The carriageway has been graded into the field by the time this runs and a
        /// verge is the ground beside it, so a run down a hill follows the road down it.
        /// </remarks>
        static void Commit(
            WorldDoc doc,
            TerrainField terrain,
            ConstraintSet constraints,
            Placement candidate,
            List<Placement> placed,
            RoadSegment segment,
            int index)
        {
            constraints.Commit(candidate);
            placed.Add(candidate);

            Pose pose = candidate.Pose.WithPosition(
                candidate.Pose.Position +
                new Vec3(0f, terrain.HeightAt(candidate.Pose.Position.Xz), 0f));

            doc.GeneratedObjects.Add(new PlacedObject(
                // Two digits, where a kerb takes three: a run of edging is laid end to end and
                // carries a hundred-odd pieces a side, where furniture stands a spacing apart and
                // the longest road a playfield can hold takes a few dozen.
                IdPrefix + segment.Id + FurnitureSegment +
                    index.ToString("00", CultureInfo.InvariantCulture),
                candidate.LogicalId,
                pose,
                TagArray(candidate.Tags),
                new Dictionary<string, string>()));
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

        /// <summary>A stretch of a polyline, in metres from its start.</summary>
        readonly struct Span
        {
            public Span(float from, float to)
            {
                From = from;
                To = to;
            }

            /// <summary>Where the stretch starts along the polyline.</summary>
            public float From { get; }

            /// <summary>Where it ends.</summary>
            public float To { get; }
        }

        /// <summary>A stretch of a polyline covered by one junction, and what that junction is.</summary>
        /// <remarks>
        /// The kind travels with the stretch because the two questions asked of it want different
        /// things: cutting the run wants the stretches merged, and marking the corners wants to
        /// know whether the corner belongs to a door, a crossing or a spawn. Measuring the discs
        /// once and answering both from the result is what keeps the two from disagreeing about
        /// where a junction is.
        /// </remarks>
        readonly struct Crossing
        {
            public Crossing(float from, float to, RoadJunctionKind kind)
            {
                From = from;
                To = to;
                Kind = kind;
            }

            /// <summary>Where the polyline enters the disc, in metres along it.</summary>
            public float From { get; }

            /// <summary>Where it leaves.</summary>
            public float To { get; }

            /// <summary>What the junction is the junction of.</summary>
            public RoadJunctionKind Kind { get; }
        }
    }

    /// <summary>
    /// A polyline with the distance along it to each of its own points worked out once, so it can
    /// be asked about a place by how far along it that place is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Distance, not parameter.</strong> The polylines this is built over are smoothed and
    /// string-pulled, so their points are close together round a bend and far apart on a straight.
    /// Stepping the point list, or interpolating a fraction of it, therefore produces positions
    /// that bunch on the curves and stretch on the straights — which for anything laid down at a
    /// spacing is the one thing the spacing was supposed to prevent.
    /// </para>
    /// <para>
    /// <strong>It is exact rather than a sampling of a curve.</strong> A polyline is straight
    /// between its own points, so the distance along it is linear within each piece and the table
    /// holds the whole truth at its vertices: a lookup is a search for the piece and one
    /// interpolation inside it, with no error to trade against a resolution. That is why there is a
    /// table at all and not a step size.
    /// </para>
    /// </remarks>
    readonly struct ArcTable
    {
        readonly Vec2[] _points;

        /// <summary>Distance from the start of the polyline to each point, ascending.</summary>
        readonly float[] _arc;

        /// <summary>Builds the table over a polyline.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
        /// <exception cref="ArgumentException">The polyline has fewer than two points.</exception>
        public ArcTable(IReadOnlyList<Vec2> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Count < 2)
            {
                throw new ArgumentException(
                    $"An arc table needs a polyline of two points or more; this one has " +
                    $"{points.Count}.",
                    nameof(points));
            }

            _points = new Vec2[points.Count];
            _arc = new float[points.Count];

            _points[0] = points[0];
            _arc[0] = 0f;

            for (int i = 1; i < points.Count; i++)
            {
                _points[i] = points[i];
                _arc[i] = _arc[i - 1] + Vec2.Distance(points[i - 1], points[i]);
            }
        }

        /// <summary>How long the polyline is, in metres.</summary>
        public float Length => _arc[_arc.Length - 1];

        /// <summary>How many points it holds.</summary>
        public int Count => _points.Length;

        /// <summary>One of the polyline's own points.</summary>
        public Vec2 Point(int index) => _points[index];

        /// <summary>
        /// Which piece of the polyline a distance falls on, and how far into that piece it is.
        /// </summary>
        /// <remarks>
        /// A binary search, so the cost of a lookup does not grow with how far along the polyline
        /// it lands — a smoothed carriageway on a large map is hundreds of points and a run down it
        /// asks this once per piece of furniture. A distance past the end lands on the last piece,
        /// which is what a caller walking to the end of a run wants: the answer stays on the
        /// polyline rather than becoming a range check every caller has to repeat.
        /// </remarks>
        public int PieceAt(float at, out float along)
        {
            int low = 0;
            int high = _points.Length - 2;

            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (_arc[middle] <= at)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            along = at - _arc[low];
            return low;
        }

        /// <summary>Where the polyline is, a distance along it.</summary>
        public Vec2 At(float distance)
        {
            int piece = PieceAt(distance, out float along);
            float span = _arc[piece + 1] - _arc[piece];

            // A piece of no length has no direction to travel along, so the answer is its own
            // start. Only a polyline carrying a repeated point produces one.
            return span > 0f
                ? _points[piece] + (_points[piece + 1] - _points[piece]) * (along / span)
                : _points[piece];
        }

        /// <summary>
        /// How far the polyline runs off the straight line between the two points
        /// <paramref name="window"/> metres either side of a place on it.
        /// </summary>
        /// <remarks>
        /// The sagitta of the bend there, which for an arc of radius r over a half-chord w is
        /// w squared over 2r — so a threshold on this is a threshold on how tightly the road turns,
        /// stated as a length and compared as one. The window is clipped to the ends of the
        /// polyline, so the measure falls away to nothing there rather than reading a bend off a
        /// chord that leaves the road.
        /// </remarks>
        public float Depth(float at, float window)
        {
            float back = MathF.Max(0f, at - window);
            float ahead = MathF.Min(Length, at + window);

            Vec2 from = At(back);
            Vec2 chord = At(ahead) - from;
            float length = chord.Length;
            if (!(length > 0f))
            {
                return 0f;
            }

            Vec2 offset = At(at) - from;
            return MathF.Abs(offset.X * chord.Y - offset.Y * chord.X) / length;
        }

        /// <summary>
        /// Where the polyline passes through a disc, in metres along it, or false if it misses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The half-chord form rather than the quadratic formula, as
        /// <c>RoadKerbs.Crosses</c> uses on a straight run: the nearest approach and the half-chord
        /// either side of it are both lengths in the piece's own units, so nothing here divides by
        /// a squared length or subtracts two large numbers to get a small one.
        /// </para>
        /// <para>
        /// One stretch rather than every stretch. A polyline that leaves a disc and re-enters it
        /// comes back as the whole run from the first entry to the last exit, which over-claims the
        /// ground between the two passes — and that ground is inside a crossing on both sides of
        /// itself, so a caller cutting a run at a junction wants it gone either way.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Where the polyline passes through a rectangle, in metres along it, or false if it misses.
        /// </summary>
        /// <remarks>
        /// One stretch from the first entry to the last exit, on the same terms as the disc below
        /// and for the same reason: a caller cutting a run out of a reserved area wants the ground
        /// between two passes through it gone as well.
        /// </remarks>
        public bool Crosses(Rect2 rect, out float from, out float to)
        {
            from = float.MaxValue;
            to = float.MinValue;

            for (int i = 1; i < _points.Length; i++)
            {
                Vec2 start = _points[i - 1];
                Vec2 span = _points[i] - start;
                float length = span.Length;
                if (!(length > 0f))
                {
                    continue;
                }

                Vec2 along = span / length;
                float low = 0f;
                float high = length;

                if (Clip(start.X, along.X, rect.MinX, rect.MaxX, ref low, ref high) &&
                    Clip(start.Y, along.Y, rect.MinZ, rect.MaxZ, ref low, ref high) &&
                    high > low)
                {
                    from = MathF.Min(from, _arc[i - 1] + low);
                    to = MathF.Max(to, _arc[i - 1] + high);
                }
            }

            return to > from;
        }

        /// <summary>
        /// Narrows a piece's open stretch to where it lies between two parallel lines, or reports
        /// that it never does.
        /// </summary>
        /// <remarks>
        /// One axis of a slab clip. A piece running exactly along the slab is either inside it for
        /// its whole length or outside it for all of it, which is the case the division would
        /// otherwise be asked to answer — so it is answered first and by a comparison.
        /// </remarks>
        static bool Clip(float start, float direction, float min, float max, ref float low, ref float high)
        {
            if (direction == 0f)
            {
                return start >= min && start <= max;
            }

            float a = (min - start) / direction;
            float b = (max - start) / direction;
            if (a > b)
            {
                float swap = a;
                a = b;
                b = swap;
            }

            low = MathF.Max(low, a);
            high = MathF.Min(high, b);
            return high > low;
        }

        public bool Crosses(Vec2 centre, float radius, out float from, out float to)
        {
            from = float.MaxValue;
            to = float.MinValue;

            for (int i = 1; i < _points.Length; i++)
            {
                Vec2 start = _points[i - 1];
                Vec2 span = _points[i] - start;
                float length = span.Length;
                if (!(length > 0f))
                {
                    continue;
                }

                Vec2 along = span / length;
                Vec2 offset = start - centre;
                float midpoint = -Vec2.Dot(offset, along);
                float square = radius * radius - (offset.SqrLength - midpoint * midpoint);
                if (!(square > 0f))
                {
                    continue;
                }

                float reach = MathF.Sqrt(square);
                float low = MathF.Max(0f, midpoint - reach);
                float high = MathF.Min(length, midpoint + reach);
                if (high <= low)
                {
                    continue;
                }

                from = MathF.Min(from, _arc[i - 1] + low);
                to = MathF.Max(to, _arc[i - 1] + high);
            }

            return to > from;
        }
    }
}
