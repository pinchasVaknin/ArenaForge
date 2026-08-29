using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Edges the roads: a run of kerbing laid down each side of every carriageway, tiled from the
    /// art a workspace files under <c>road/kerb</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The tool composes prefabs and does not author geometry</strong> — ARCHITECTURE.md
    /// section 3 — so the edge of a road is a row of objects and not a ribbon of mesh. What the
    /// heightfield writer is allowed does not extend to here: handing a Unity terrain a grid of
    /// numbers is asking a component to do the job it exists for, and building a strip of triangles
    /// down a polyline is authoring a model. So the surface is painted into a splat map and
    /// everything that stands up beside it is ordinary art placed under ordinary rules.
    /// </para>
    /// <para>
    /// <strong>It is <see cref="WallRun"/> with a different line in it.</strong> A kerb is the same
    /// problem as a hedge and a boundary — art laid end to end so each piece starts where the last
    /// one stopped — and the cursor that does that already exists, already hands corners over
    /// rather than fighting for them, already closes what a walk left bare, and already tests every
    /// piece through <see cref="ConstraintSet"/>. What is different is the line: a carriageway is a
    /// polyline at whatever angle the router found it, where every other run in the tool walks the
    /// side of a rectangle. See <see cref="WallRun.Face.Along"/>, which is that line, and what it
    /// costs to judge a turned piece by the box round it.
    /// </para>
    /// <para>
    /// <strong>Half a carriageway out, on both sides.</strong> The run is seated against the
    /// centreline at half the segment's own width — see <see cref="WallRun.Face.Against"/> — so a
    /// kerb's inner face lands exactly on the edge of the carriageway and the piece grows outward
    /// from there. That is <see cref="WallRun.Flush"/>'s arithmetic with a gap in it rather than a
    /// new way of seating art, and it is why the carriageway itself stays clear: the reservation
    /// <see cref="RoadNetwork.Corridors"/> hands the cover stage is untouched by anything this puts
    /// down.
    /// </para>
    /// <para>
    /// <strong>Runs stop at the junction discs, so no two of them want the same metre.</strong>
    /// Two carriageways that meet at a junction meet at an angle, and their kerbs would cross where
    /// the roads do — which is a rejection at the one place a player reads the network as a
    /// network. Every run is therefore cut where it enters a junction's own radius, the same disc
    /// <see cref="RoadNetwork.GradeInto"/> solves a crossing's height over, and the crossing is
    /// left open. A run is cut back half a piece at each bend of its own polyline as well, for the
    /// reason <see cref="ExteriorPlacer"/> shortens the four sides of a yard: two runs meeting at
    /// an angle either overlap or leave a hole, and half a thickness each is what hands the corner
    /// over instead.
    /// </para>
    /// <para>
    /// <strong>What is left is the inside of a bend, and it is left on purpose.</strong> A run on
    /// the concave side of a corner is nearer the far arm of its own polyline than half a
    /// carriageway, so the last piece before a bend clips the road it is edging. Measured over
    /// forty default seeds it is nine kerbs in a thousand, all but two of them under 40 cm in and
    /// the worst 59 cm — and cutting it would mean either a mitre setback computed through a
    /// tangent, which is a transcendental in the middle of a deterministic placement, or shutting
    /// a run against its own polyline, whose collinear stretches then sit exactly on the boundary
    /// of the test and are decided by the last bit of a float. A kerb that follows the inside of a
    /// corner is what a kerb does at a corner; neither of those two is worth what it costs.
    /// </para>
    /// <para>
    /// <strong>A workspace with nothing in the folder gets no kerbs.</strong> Not a draw is taken
    /// and not an object is placed, so a project that has never filled <c>Props/Road/Kerb</c>
    /// generates exactly the map it generated before this stage existed — the bargain
    /// <see cref="PerimeterFence"/> makes with an empty stone fence folder, and for the same
    /// reason: the art decides whether a feature is in a map, and the absence of art is an answer
    /// rather than a fault.
    /// </para>
    /// <para>
    /// <strong>Kerbs are left out of the router's measure of shelter.</strong> A network is a
    /// function of the document, and these are placed <em>from</em> a network — so a rebuild that
    /// counted them would route the roads round the kerbs the last routing put down, and the
    /// reservation the cover was placed against would move under it. That is the bargain cover
    /// already makes, in the same place and for the same reason.
    /// </para>
    /// </remarks>
    public static class RoadKerbs
    {
        /// <summary>Tag a catalog entry must carry to be laid along a carriageway.</summary>
        /// <remarks>What a workspace files in <c>Props/Road/Kerb</c>.</remarks>
        public const string KerbTag = "road/kerb";

        /// <summary>Prefix of the world metadata keys holding this stage's placement statistics.</summary>
        public const string StatsPrefix = "kerb_";

        /// <summary>Stable id prefix every kerb sits under — <c>map/</c> plus its segment's own id.</summary>
        /// <remarks>
        /// A kerb belongs to a carriageway rather than to a lane, so it is filed under the
        /// carriageway: <c>map/road/artery_00/kerb_004</c>. The segment ids are a function of the
        /// document, so the same seed numbers the same piece the same way and an edit made against
        /// one survives a regeneration.
        /// </remarks>
        public const string IdPrefix = "map/";

        /// <summary>What a kerb's own index is separated from its carriageway's id by.</summary>
        public const string KerbSegment = "/kerb_";

        /// <summary>Label the stream the kerbs' art is drawn from is forked under.</summary>
        const string StreamLabel = "road/kerb";

        /// <summary>
        /// Lays kerbing along every carriageway of <paramref name="roads"/> and returns every piece
        /// it stood up, in generation order, so the cover stage can keep off it.
        /// </summary>
        /// <param name="doc">The document to add the kerbs to. Its parameters drive the draws.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="terrain">The ground, with the carriageways already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="roads">The network to edge. An empty one is edged with nothing.</param>
        /// <param name="structures">The structures already committed, with their world footprints.</param>
        /// <param name="anchored">Everything else already standing: the boundary and the dressing.</param>
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
                // No carriageway to edge. A map at a road density of zero reaches here and leaves
                // with the document it arrived with.
                return placed;
            }

            IReadOnlyList<CatalogEntry> palette =
                WallRun.Tileable(catalog.Query(TagQuery.All(KerbTag)));

            if (palette.Count == 0)
            {
                // Nothing filed in the kerb folder. Returning before a single draw is what lets a
                // project that has never used it generate the map it always did.
                return placed;
            }

            ConstraintSet constraints = Rules(layout, structures, anchored);
            Rng stream = new Rng(doc.Parameters.Seed).Fork(StreamLabel);
            var stats = new PlacementStats();

            // How far the middle of a piece of kerbing sits beyond the edge of the carriageway,
            // which is the line the cut below is measured along, and how much of a bend a run gives
            // up at each end. Half a piece each is what hands a corner over to the run on the other
            // side of it rather than reaching into ground that one can occupy; the slack on top is
            // what keeps that a fact about the geometry rather than about which way two float sums
            // rounded.
            float reach = WallRun.ThickestSegment(palette) * 0.5f;
            float shoulder = reach + WallRun.CornerSlack;

            IReadOnlyList<RoadSegment> segments = roads.Segments;
            var spans = new List<Span>();
            var shut = new List<Disc>();

            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegment carriageway = segments[i];
                float half = carriageway.Width * 0.5f;

                // Where a piece sits across this run: its inner face exactly on the edge of the
                // carriageway, growing outward. WallRun.Flush is this with a gap of zero.
                WallRun.Seat seat = (face, local) => face.Against(local, half);

                Shut(roads, i, reach, shut);

                IReadOnlyList<Vec2> points = carriageway.Points;
                int stood = 0;

                for (int step = 1; step < points.Count; step++)
                {
                    Vec2 from = points[step - 1];
                    Vec2 to = points[step];

                    Vec2 run = to - from;
                    float length = run.Length;
                    if (!(length > 0f))
                    {
                        continue;
                    }

                    Vec2 along = run / length;
                    var cross = new Vec2(-along.Y, along.X);

                    // Both sides of the same stretch, and each asked separately where it may go.
                    // A road running alongside another covers the kerb line on the side it is on
                    // and leaves the far one alone, so cutting both at once would give up twice the
                    // edging the braid actually costs.
                    for (int side = 0; side < Sides; side++)
                    {
                        int outward = side == 0 ? 1 : -1;
                        Vec2 offset = cross * (outward * (half + reach));

                        Clear(
                            from + offset, to + offset,
                            step > 1 ? shoulder : 0f,
                            step < points.Count - 1 ? shoulder : 0f,
                            shut, spans);

                        for (int stretch = 0; stretch < spans.Count; stretch++)
                        {
                            // Back onto the centreline. A parallel offset preserves distance along
                            // a straight run, so a stretch of the kerb line is the same stretch of
                            // the road it edges — and the face is built from the centreline
                            // because that is what the seat measures out from.
                            RoadSegment owner = carriageway;
                            stood += WallRun.Tile(
                                WallRun.Face.Along(
                                    from + along * spans[stretch].From,
                                    from + along * spans[stretch].To,
                                    outward),
                                palette, WallRun.NoGates, constraints, stats, seat, stood,
                                (candidate, index) => Commit(
                                    doc, terrain, constraints, candidate, placed, owner, index),
                                ref stream);
                        }
                    }
                }
            }

            stats.WriteTo(doc.Metadata, StatsPrefix);
            return placed;
        }

        /// <summary>How many runs a carriageway has: one each side.</summary>
        const int Sides = 2;

        /// <summary>
        /// How far apart the discs that shut a braided stretch are stepped, as a fraction of their
        /// own radius.
        /// </summary>
        /// <remarks>
        /// A chain of discs is how a strip of ground is described here rather than a swept polygon,
        /// because a disc is what <see cref="Crosses"/> already cuts a run at and a polygon would
        /// need an intersection test that exists nowhere in this project. Half a radius is close
        /// enough stepping that growing each disc by what the stepping loses — see
        /// <see cref="Shut"/> — costs under a per cent of extra reach, so the chain covers exactly
        /// the carriageway rather than nearly it.
        /// </remarks>
        const float DiscStep = 0.5f;

        /// <summary>
        /// Every disc a kerb line along one carriageway has to stop at: the network's junctions, and
        /// the ground every other carriageway covers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A kerb may not stand in a road.</strong> The router's whole cost decay exists to
        /// make a branch join a trunk that is already there rather than lay a second one beside it —
        /// see <see cref="RoadNetwork"/> — so two carriageways over the same ground is the ordinary
        /// case and not a defect. What is a defect is edging the one that is inside the other: a
        /// branch running along an artery is a metre inside the artery's own carriageway, so its
        /// kerbing comes out as a line of stone laid across a road. Measured over forty default
        /// seeds, that was one kerb in nine.
        /// </para>
        /// <para>
        /// The discs are the carriageways themselves, at their own half-width, and what is tested
        /// against them is the <em>kerb line</em> rather than the road's centre — see
        /// <see cref="Place"/>. That is what makes the cut exact rather than conservative: a road
        /// running along one side of another covers the kerb line on that side and leaves the far
        /// one alone, and asking about the centreline instead would give up both. On the default map
        /// the difference is a third of all the kerbing.
        /// </para>
        /// <para>
        /// Every other segment, with no precedence between them, because the question is about the
        /// ground rather than about the roads: the surface a player sees is the union of the
        /// carriageways, and its edge is not on any one centreline where two of them merge. Where
        /// that leaves a stretch unedged, unedged is what a merge looks like.
        /// </para>
        /// </remarks>
        static void Shut(RoadNetwork roads, int segment, float reach, List<Disc> shut)
        {
            shut.Clear();

            for (int i = 0; i < roads.Junctions.Count; i++)
            {
                shut.Add(new Disc(roads.Junctions[i].Position, roads.JunctionRadius));
            }

            for (int i = 0; i < roads.Segments.Count; i++)
            {
                if (i == segment)
                {
                    continue;
                }

                RoadSegment other = roads.Segments[i];
                float half = other.Width * 0.5f + reach;
                float step = half * DiscStep;

                // Grown by exactly what the stepping loses. A chain of discs of radius r stepped s
                // apart covers a line out to sqrt(r squared minus a quarter of s squared), which is
                // short of r — so the discs are laid out at the radius that makes that sum come to
                // the carriageway's own half-width, and the cover is exact rather than nearly so.
                float radius = MathF.Sqrt(half * half + step * step * 0.25f);

                for (int point = 1; point < other.Points.Count; point++)
                {
                    Vec2 start = other.Points[point - 1];
                    Vec2 span = other.Points[point] - start;
                    float length = span.Length;
                    if (!(length > 0f))
                    {
                        continue;
                    }

                    Vec2 along = span / length;
                    for (float at = 0f; at < length; at += step)
                    {
                        shut.Add(new Disc(start + along * at, radius));
                    }

                    shut.Add(new Disc(other.Points[point], radius));
                }
            }
        }

        /// <summary>
        /// The stretches of one straight run of centreline a kerb may be laid along: the run cut
        /// back at each end, with every disc it passes through taken out of what is left.
        /// </summary>
        /// <remarks>
        /// Measured in metres from <paramref name="from"/> rather than as a fraction of the run,
        /// because what is being cut out is a distance — a junction radius and a piece's thickness
        /// are both lengths, and a fraction would make them mean different things on a five-metre
        /// stretch and a fifty-metre one.
        /// </remarks>
        static void Clear(
            Vec2 from,
            Vec2 to,
            float back,
            float ahead,
            IReadOnlyList<Disc> shut,
            List<Span> spans)
        {
            spans.Clear();

            Vec2 span = to - from;
            float length = span.Length;
            if (!(length > back + ahead))
            {
                return;
            }

            Vec2 along = span / length;
            float open = back;
            float end = length - ahead;

            var blocked = new List<Span>();
            for (int i = 0; i < shut.Count; i++)
            {
                if (Crosses(from, along, length, shut[i].At, shut[i].Radius, out Span disc))
                {
                    blocked.Add(disc);
                }
            }

            // Ascending, so one sweep merges them. Two discs that land on the same stretch merge to
            // the same stretch whichever order they were compared in, so an unstable sort over
            // equal keys cannot change the answer.
            blocked.Sort(Nearest);

            for (int i = 0; i < blocked.Count && open < end; i++)
            {
                if (blocked[i].To <= open)
                {
                    continue;
                }

                if (blocked[i].From > open)
                {
                    spans.Add(new Span(open, MathF.Min(blocked[i].From, end)));
                }

                open = MathF.Max(open, blocked[i].To);
            }

            if (open < end)
            {
                spans.Add(new Span(open, end));
            }
        }

        /// <summary>
        /// Where a line crosses a disc, in metres along the line, or false if it does not reach one.
        /// </summary>
        /// <remarks>
        /// The half-chord form rather than the quadratic formula: the nearest approach and the
        /// half-chord either side of it are both lengths in the line's own units, so nothing here
        /// divides by a squared length or subtracts two large numbers to get a small one.
        /// </remarks>
        static bool Crosses(
            Vec2 from, Vec2 along, float length, Vec2 centre, float radius, out Span disc)
        {
            disc = default;

            Vec2 offset = from - centre;
            float midpoint = -Vec2.Dot(offset, along);
            float square = radius * radius - (offset.SqrLength - midpoint * midpoint);
            if (!(square > 0f))
            {
                return false;
            }

            float reach = MathF.Sqrt(square);
            float low = MathF.Max(0f, midpoint - reach);
            float high = MathF.Min(length, midpoint + reach);
            if (high <= low)
            {
                return false;
            }

            disc = new Span(low, high);
            return true;
        }

        static int Nearest(Span a, Span b)
        {
            int order = a.From.CompareTo(b.From);
            return order != 0 ? order : a.To.CompareTo(b.To);
        }

        /// <summary>
        /// The two rules a kerb is laid under, over everything already on the ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same two <see cref="PerimeterFence"/> uses, and the absences are the same absences.
        /// <see cref="ConstraintKind.ClearOfSpawn"/> is not asked because a spawn area spans the
        /// full width of the map and the network runs into both of them by design — a road that
        /// stops short of a spawn is a road that does not reach it.
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> is not asked because a path is laid
        /// <em>to</em> a doorway: the last few metres of one stand inside the very clearance that
        /// rule protects, and a kerb along that stretch is the edge of the path to the door rather
        /// than something standing in front of it.
        /// </para>
        /// <para>
        /// <strong><see cref="ConstraintKind.OffReservedPath"/> is not asked either, and that one
        /// would refuse nearly everything.</strong> The corridors are axis-aligned boxes round
        /// stretches of a polyline, grown by half a carriageway and deliberately conservative — see
        /// <see cref="RoadNetwork.Corridors"/> — so a kerb standing exactly on the edge of a
        /// diagonal carriageway is inside the box round it every time. What the kerb actually has
        /// to keep to is that it does not stand in the carriageway, and where it is seated is what
        /// keeps it there.
        /// </para>
        /// <para>
        /// The margin on the overlap rule is zero, for the reason every tiled run uses zero: two
        /// rectangles that merely touch do not overlap, so a zero margin is what lets one piece
        /// start exactly where the last one stopped.
        /// </para>
        /// </remarks>
        static ConstraintSet Rules(
            ArenaLayout layout,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> anchored)
        {
            var constraints = new ConstraintSet(layout, new[]
            {
                PlacementConstraint.InsidePlayfield(),
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

            return constraints;
        }

        /// <summary>
        /// Accepts a piece: into the constraint set, into the running list the cover stage is
        /// given, and into the document at the height of the ground under it.
        /// </summary>
        /// <remarks>
        /// Stood on the ground rather than planted in it, which is the treatment everything but a
        /// fence gets. The carriageway has been graded into the field by the time this runs and a
        /// kerb is seated on the edge of it, so the ground it reads is the road's own surface and a
        /// run down a hill follows the road down it.
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
                // Three digits, as the boundary uses: a long artery across a large map carries a
                // hundred-odd pieces a side, and an id running from kerb_98 to kerb_100 would sort
                // into an order nobody reading it expects.
                IdPrefix + segment.Id + KerbSegment +
                    index.ToString("000", CultureInfo.InvariantCulture),
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

        /// <summary>Ground no run may be laid across: a junction, or a stretch of another road.</summary>
        readonly struct Disc
        {
            public Disc(Vec2 at, float radius)
            {
                At = at;
                Radius = radius;
            }

            /// <summary>Where it is centred.</summary>
            public Vec2 At { get; }

            /// <summary>How far it reaches.</summary>
            public float Radius { get; }
        }

        /// <summary>A stretch of one straight run of centreline, in metres from its start.</summary>
        readonly struct Span
        {
            public Span(float from, float to)
            {
                From = from;
                To = to;
            }

            /// <summary>Where the stretch starts along the run.</summary>
            public float From { get; }

            /// <summary>Where it ends.</summary>
            public float To { get; }
        }
    }
}
