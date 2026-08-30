using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>Which of the two road classes a stretch of carriageway belongs to.</summary>
    /// <remarks>
    /// Two, because two is the smallest hierarchy that reads as one: a network of a single width is
    /// a maze, and a trunk with branches off it is a way through. See
    /// <see cref="ArenaParams.ArteryWidth"/> and <see cref="ArenaParams.PathWidth"/>.
    /// </remarks>
    public enum RoadClass
    {
        /// <summary>A trunk, laid down a lane gap from one end of the map to the other.</summary>
        Artery,

        /// <summary>A branch, joining a doorway or a spawn to the rest of the network.</summary>
        Path,
    }

    /// <summary>What a junction is the junction of.</summary>
    public enum RoadJunctionKind
    {
        /// <summary>The ground a structure's doorway opens onto.</summary>
        Portal,

        /// <summary>The centre of a spawn area.</summary>
        Terminal,

        /// <summary>A point on an artery that a branch was routed to.</summary>
        Attachment,
    }

    /// <summary>One node of a road network: a place the network was built to reach.</summary>
    public sealed class RoadJunction
    {
        /// <summary>Creates a junction.</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> is blank.</exception>
        public RoadJunction(string id, Vec2 position, RoadJunctionKind kind, float height)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A junction id must not be blank.", nameof(id));
            }

            Id = id;
            Position = position;
            Kind = kind;
            Height = height;
        }

        /// <summary>Readable path — <c>road/portal_03</c>, <c>road/terminal_a</c>.</summary>
        public string Id { get; }

        /// <summary>Where the junction stands, on the ground plane.</summary>
        public Vec2 Position { get; }

        /// <summary>What kind of place this is.</summary>
        public RoadJunctionKind Kind { get; }

        /// <summary>The height the ground is held to here, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Solved when the junction is made and never again, which is the whole point of it being a
        /// property of the junction rather than of a road that happens to arrive: two roads meeting
        /// at one crossing have to meet the ground at one height, and a height each polyline worked
        /// out for itself is two.
        /// </para>
        /// <para>
        /// A <see cref="RoadJunctionKind.Portal"/> takes the graded height of the structure whose
        /// doorway it serves, so a path arrives level with the door sill rather than a step below
        /// it. Everything else takes the mean of the ungraded ground over its own disc — the same
        /// cut and fill a pad makes, for the same reason: the crossing settles between the high and
        /// the low ground it replaces instead of perching on one or sinking into the other.
        /// </para>
        /// </remarks>
        public float Height { get; }

        /// <inheritdoc />
        public override string ToString() => $"{Id} {Position}";
    }

    /// <summary>
    /// The ground a carriageway is graded to: the centreline again, sampled fine enough to follow
    /// the ground, with the height of the road at each sample.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Its own polyline, and not <see cref="RoadSegment.Points"/>.</strong> The centreline a
    /// road is drawn as is string-pulled — <see cref="RoadRouter.Smooth"/> throws away every vertex
    /// the one before it can see past — so a straight run across a map is two points fifty metres
    /// apart. That is the right polyline to draw a road along and the wrong one to grade against: a
    /// height at each end and a straight line between them is a ramp that bulldozes every rise and
    /// fills every hollow it crosses, and two roads running side by side over the same rise come out
    /// at two different heights because their vertices fell in different places.
    /// </para>
    /// <para>
    /// So the profile is the same line resampled at the placement grid's own cell, which is the
    /// resolution everything else about a road is decided at. The plan is unchanged and the two are
    /// kept apart on purpose: <see cref="RoadSegment.Points"/> still says where the road goes, and
    /// what it means for a segment to be too steep to route is still a claim about that.
    /// </para>
    /// </remarks>
    public readonly struct RoadProfile
    {
        static readonly Vec2[] NoPoints = Array.Empty<Vec2>();
        static readonly float[] NoHeights = Array.Empty<float>();

        readonly Vec2[] _points;
        readonly float[] _heights;

        internal RoadProfile(Vec2[] points, float[] heights)
        {
            _points = points;
            _heights = heights;
        }

        /// <summary>The centreline, resampled. Empty on a segment that has only been routed.</summary>
        public IReadOnlyList<Vec2> Points => _points ?? NoPoints;

        /// <summary>The height the road stands at, one per point of <see cref="Points"/>.</summary>
        public IReadOnlyList<float> Heights => _heights ?? NoHeights;

        /// <summary>How many samples the profile holds.</summary>
        public int Count => _points == null ? 0 : _points.Length;

        /// <summary>The height where the profile passes nearest to a point, and how near that is.</summary>
        /// <remarks>
        /// The two come back together because they are the same question asked of the same nearest
        /// place: finding it twice would be the walk done twice and would let the answers come from
        /// different samples where a bend brings two of them equally close.
        /// </remarks>
        public float NearestTo(Vec2 at, out float distance)
        {
            distance = float.MaxValue;
            if (_points == null || _points.Length == 0)
            {
                return 0f;
            }

            float height = _heights[0];
            if (_points.Length == 1)
            {
                distance = Vec2.Distance(_points[0], at);
                return height;
            }

            for (int i = 1; i < _points.Length; i++)
            {
                Vec2 from = _points[i - 1];
                Vec2 along = _points[i] - from;
                float lengthSquared = along.SqrLength;
                float t = lengthSquared > 0f ? Vec2.Dot(at - from, along) / lengthSquared : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;

                float nearest = Vec2.Distance(at, from + along * t);
                if (nearest < distance)
                {
                    distance = nearest;
                    height = _heights[i - 1] + (_heights[i] - _heights[i - 1]) * t;
                }
            }

            return height;
        }
    }

    /// <summary>One stretch of carriageway: a classified polyline with a width.</summary>
    public sealed class RoadSegment
    {
        readonly List<Vec2> _points;

        /// <summary>Creates a segment.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is blank, or the polyline has fewer than two points.</exception>
        public RoadSegment(string id, RoadClass roadClass, float width, List<Vec2> points)
            : this(id, roadClass, width, points, default)
        {
        }

        RoadSegment(
            string id, RoadClass roadClass, float width, List<Vec2> points, RoadProfile profile)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A segment id must not be blank.", nameof(id));
            }

            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Count < 2)
            {
                throw new ArgumentException(
                    $"'{id}' is a polyline of {points.Count} point(s); a road needs two.", nameof(points));
            }

            Id = id;
            Class = roadClass;
            Width = width;
            _points = points;
            Profile = profile;

            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vec2.Distance(points[i - 1], points[i]);
            }

            Length = length;
        }

        /// <summary>Readable path — <c>road/artery_00</c>, <c>road/path_07</c>.</summary>
        public string Id { get; }

        /// <summary>Trunk or branch.</summary>
        public RoadClass Class { get; }

        /// <summary>Carriageway width in metres.</summary>
        public float Width { get; }

        /// <summary>The centreline, from one end to the other.</summary>
        public IReadOnlyList<Vec2> Points => _points;

        /// <summary>Length of the centreline in metres.</summary>
        public float Length { get; }

        /// <summary>
        /// What the ground under the carriageway is graded to — see <see cref="RoadProfile"/>.
        /// </summary>
        /// <remarks>
        /// Empty on a segment that has only been routed. <see cref="RoadNetwork.Build"/> sweeps every
        /// segment before it returns one, so a network handed out always has a profile on each.
        /// </remarks>
        public RoadProfile Profile { get; }

        /// <summary>The same segment with a longitudinal profile attached.</summary>
        internal RoadSegment WithProfile(RoadProfile profile) =>
            new RoadSegment(Id, Class, Width, _points, profile);

        /// <inheritdoc />
        public override string ToString() =>
            $"{Id} ({Class}) {_points.Count} points, {Length.ToString("0.0", CultureInfo.InvariantCulture)} m";
    }

    /// <summary>
    /// The roads laid across a map: the junctions they were built to reach, and the classified
    /// polylines that reach them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pure function of its inputs and never serialised, on the same terms as
    /// <see cref="ArenaLayout"/> and <see cref="TerrainField"/> — a document carries the parameters
    /// and the placements, and this is rebuilt on demand from them. Nothing here places an object or
    /// touches a scene. It does grade the ground, through <see cref="GradeInto"/>, and that is a
    /// separate call for a reason: every height it grades was solved while the network was built,
    /// so grading is a replay of a decision already made rather than a decision of its own.
    /// </para>
    /// <para>
    /// <strong>The nodes come from the document, never from the scene.</strong> A doorway is already
    /// a rectangle in a structure's metadata — see <see cref="ArenaLayoutGenerator.DoorwayCountKey"/>
    /// — written by the stage that placed the structure and read back the way the cover placer and
    /// the validator read it. Going to the scene for a marker would be a second reader of one fact,
    /// and Core has no scene to go to.
    /// </para>
    /// <para>
    /// <strong>Topology is decided before anything is routed.</strong> An artery is laid down each
    /// lane gap first, because a lane gap is the one band of a map that is structure-free on every
    /// seed by construction and is therefore where a way through belongs. The branches are then a
    /// Euclidean minimum spanning tree over the portals, the spawns and the point on an artery
    /// nearest to each of them, plus the redundant edges <see cref="ArenaParams.RoadDensity"/> asks
    /// for. Routing that list longest-first is what makes the trunks exist before a branch could
    /// braid into one.
    /// </para>
    /// <para>
    /// <strong>Braiding is the point of the whole design.</strong> Every cell a finished route used
    /// — and every cell within half a carriageway of one — has its cost quartered, so the next route
    /// would rather join the road that is already there than lay a second one alongside it. Without
    /// that, two doors a few metres apart send two paths to the same artery running parallel and
    /// touching for their whole length, which is a spider and not a network. It is the one property
    /// nothing else here would catch, which is why <see cref="CorridorLength"/> and
    /// <see cref="UnbraidedLength"/> are both reported.
    /// </para>
    /// <para>
    /// <strong>Flat A*, not hierarchical.</strong> The default map is 3,600 cells and the largest
    /// supported one 160,000. Cluster abstraction would buy nothing measurable on either and would
    /// be one more thing to keep deterministic.
    /// </para>
    /// <para>
    /// <strong>Nothing here takes a draw.</strong> Where a road goes is a fact about where the
    /// doorways, the lanes and the ground are, so no stream is forked and adding this stage does not
    /// move a single placement on any seed that already exists.
    /// </para>
    /// </remarks>
    public sealed class RoadNetwork
    {
        /// <summary>How far a piece of cover shelters the ground beside it, in metres.</summary>
        /// <remarks>
        /// The same reach <see cref="AnalysisParams.CoverRadius"/> measures cover coverage at.
        /// Stated here because a router is handed the parameters, the layout, the ground and the
        /// placements and no analysis settings — a road stage that asked for them would be borrowing
        /// the measuring apparatus in order to lay out the thing being measured.
        /// </remarks>
        public const float ShelterReach = 6f;

        /// <summary>How far a polyline's end may sit from a junction and still be that junction.</summary>
        /// <remarks>
        /// A millimetre, against a placement grid whose cells are a metre. The two coordinates are
        /// the same arithmetic run twice and ought to be equal to the bit; the slack is what makes
        /// the match a fact about the geometry rather than about which way a float sum rounded.
        /// </remarks>
        const float NodeSlack = 1e-3f;

        /// <summary>
        /// The disc an artery's own end is levelled over, in metres.
        /// </summary>
        /// <remarks>
        /// An artery runs from one edge of the playfield to the other and ends at neither a doorway
        /// nor a spawn, so it has no junction to take a height from and takes the ground's own
        /// instead. Half a carriageway would do; a metre is the placement grid's cell, which is the
        /// resolution everything else about a road is decided at.
        /// </remarks>
        const float EndRadius = 1f;

        static readonly RoadJunction[] NoJunctions = Array.Empty<RoadJunction>();
        static readonly RoadSegment[] NoSegments = Array.Empty<RoadSegment>();
        static readonly Rect2[] NoCorridors = Array.Empty<Rect2>();

        readonly IReadOnlyList<RoadJunction> _junctions;
        readonly IReadOnlyList<RoadSegment> _segments;
        readonly IReadOnlyList<Rect2> _corridors;
        readonly float _junctionRadius;

        RoadNetwork(
            IReadOnlyList<RoadJunction> junctions,
            IReadOnlyList<RoadSegment> segments,
            IReadOnlyList<Rect2> corridors,
            float junctionRadius,
            float corridorLength,
            float unbraidedLength)
        {
            _junctions = junctions;
            _segments = segments;
            _corridors = corridors;
            _junctionRadius = junctionRadius;
            CorridorLength = corridorLength;
            UnbraidedLength = unbraidedLength;
        }

        /// <summary>The places the network was built to reach: portals, then spawns, then attachments.</summary>
        public IReadOnlyList<RoadJunction> Junctions => _junctions;

        /// <summary>The carriageways, arteries first and each in the order it was laid.</summary>
        public IReadOnlyList<RoadSegment> Segments => _segments;

        /// <summary>
        /// The ground the carriageways cover, as axis-aligned rectangles: the reservation every
        /// later stage places around.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Rectangles rather than the swept polygon a polyline and a width really describe, because
        /// every rule in <see cref="ConstraintSet"/> is already stated in terms of rectangles —
        /// <see cref="ConstraintKind.OffReservedPath"/> takes a list of them and asks nothing else
        /// of the caller. A polygon would need an overlap test that exists nowhere in this project
        /// and a second reader for every rule that already has one.
        /// </para>
        /// <para>
        /// <strong>One per stretch of polyline, and a stretch is not always a whole segment.</strong>
        /// The box round a run of road is the run's own bounding box grown by half the carriageway,
        /// which is exact while the run is along an axis and grows without bound as it turns away
        /// from one: <see cref="RoadRouter.Smooth"/> string-pulls, so a segment is routinely tens of
        /// metres long and a diagonal one that length would reserve a square of that side —
        /// measured over two hundred default seeds, a third of the playfield for a single segment.
        /// So a segment is cut into pieces that each stray at most a quarter of a carriageway off
        /// their own box's long axis — see <see cref="PieceSkew"/> — which leaves an axis-aligned
        /// run whole and takes the network's reservation from 1.56 times the ground the carriageways
        /// cover, uncut, to 1.30. The pieces overlap at their joints exactly as the segments do at
        /// theirs.
        /// </para>
        /// </remarks>
        public IReadOnlyList<Rect2> Corridors => _corridors;

        /// <summary>
        /// How much ground a crossing covers, in metres: half the widest carriageway the map can
        /// hold.
        /// </summary>
        /// <remarks>
        /// The disc a junction's height is solved over, and the disc <see cref="RoadKerbs"/> cuts
        /// its runs at so two carriageways' kerbing does not cross where the roads do. One radius
        /// for every junction rather than one worked out per junction from what meets there — see
        /// <see cref="Build"/>.
        /// </remarks>
        public float JunctionRadius => _junctionRadius;

        /// <summary>True when no road was laid.</summary>
        public bool IsEmpty => _segments.Count == 0;

        /// <summary>
        /// Ground the network's centrelines cover, in metres, counting ground two roads share once.
        /// </summary>
        /// <remarks>
        /// Measured over the cells the routes used rather than by adding up polyline lengths,
        /// because that is the only way shared ground can be counted once — two roads that have
        /// merged are two polylines over the same cells. Against <see cref="UnbraidedLength"/> it is
        /// the whole of what braiding buys, and the pair is reported rather than the ratio because
        /// what the ratio means depends on the map: a thousand default seeds come to 0.677 together,
        /// and 0.796 with the decay taken out, but the two overlap map by map.
        /// </remarks>
        public float CorridorLength { get; }

        /// <summary>
        /// The sum of what every road laid here would have covered had it been the only one, in
        /// metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every route is searched twice: once over the grid as the roads before it left it, which
        /// is the road that gets laid, and once over the grid as it stood before any road existed,
        /// which is this. The second search is the price of being able to say what braiding saved
        /// instead of asserting it.
        /// </para>
        /// <para>
        /// <strong>The independent route, not the one laid.</strong> Measuring the laid routes
        /// against each other would count a branch's detour to reach a road it can share as though
        /// the detour were road saved, which is the opposite of the truth and would let the decay
        /// look better the worse it behaved.
        /// </para>
        /// </remarks>
        public float UnbraidedLength { get; }

        /// <summary>
        /// Lays the road network a map's parameters, layout, ground and placements describe.
        /// </summary>
        /// <param name="parameters">The map's generator settings. A <see cref="ArenaParams.RoadDensity"/> of zero returns an empty network.</param>
        /// <param name="layout">The lane bands and grid the roads are laid over.</param>
        /// <param name="terrain">The ground, read for gradient. Never graded.</param>
        /// <param name="committed">Everything already placed, in document order.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A road parameter is outside its supported range.</exception>
        public static RoadNetwork Build(
            ArenaParams parameters,
            ArenaLayout layout,
            TerrainField terrain,
            IReadOnlyList<PlacedObject> committed)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            if (!(parameters.RoadDensity > 0f))
            {
                return new RoadNetwork(NoJunctions, NoSegments, NoCorridors, 0f, 0f, 0f);
            }

            Validate(parameters);

            var structures = new List<Rect2>();
            var doorways = new List<RoadDoorway>();
            Collect(committed, parameters.PathWidth, structures, doorways);

            List<Rect2> gaps = LaneGaps(layout);
            var router = new RoadRouter(parameters, layout, terrain, committed, structures, doorways, gaps);

            // Half the widest carriageway the map can hold, which is how much ground a crossing of
            // any two roads here covers. One radius for every junction rather than one worked out
            // per junction from what meets there: an attachment is a node beside an artery that
            // branches run to, so the widest road at it is one that passes through rather than one
            // that ends there, and a radius counting only the roads that end would leave the trunk
            // crossing its own junction at a different height.
            float junctionRadius = MathF.Max(parameters.ArteryWidth, parameters.PathWidth) * 0.5f;

            var places = new List<RoadPlace>();
            var nodes = new List<int>();
            AddPortals(doorways, router, places, nodes);
            int portalCount = places.Count;
            AddTerminals(layout, router, places, nodes);

            // The trunks are laid and swept before a single junction height is solved, and they can
            // be because no artery ends at one: an artery runs from one edge of the playfield to the
            // other and takes the ground's own height at each end. What that buys is Settle below —
            // a junction standing on a trunk can be given the height the trunk is actually at rather
            // than the height the ground would have had if the trunk were not there.
            var segments = new List<RoadSegment>();
            LayArteries(parameters, layout, gaps, router, segments);
            Sweep(segments, 0, segments.Count, null, terrain, parameters, layout.Grid.CellSize);

            AddAttachments(segments, router, places, nodes);
            List<RoadJunction> junctions = Settle(places, segments, terrain, junctionRadius);
            Attach(places, junctions);

            int branches = segments.Count;
            LayBranches(parameters, portalCount, places, nodes, router, segments);
            Sweep(segments, branches, segments.Count, places, terrain, parameters, layout.Grid.CellSize);

            return new RoadNetwork(
                junctions, segments, Reserve(segments), junctionRadius,
                router.CorridorLength, router.UnbraidedLength);
        }

        /// <summary>
        /// Gives every place the network reaches the one height the ground under it is held to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Solved here and nowhere else, once per junction, before a single branch is swept —
        /// because a height each branch worked out for itself is a crossing two roads arrive at two
        /// heights.
        /// </para>
        /// <para>
        /// <strong>Three answers, in the order of what the crossing has to meet.</strong> A portal
        /// takes the graded height of the structure whose doorway it serves, so a path arrives level
        /// with the door sill rather than a step below it. A junction standing on a trunk takes the
        /// height that trunk is at where it passes, because a crossing has to meet the road going
        /// through it rather than the ground that road replaced. Anything else takes the mean of the
        /// ungraded ground over its own disc, which is the cut and fill a pad makes and for the same
        /// reason: the crossing settles between the high and the low ground it stands on.
        /// </para>
        /// <para>
        /// The middle answer is not a refinement. An artery cut into a bank or filled over a hollow
        /// runs well off the natural ground, and a branch that joined it at the ground's height would
        /// meet the trunk at a step of exactly that much — which is the failure solving junction
        /// heights at all was meant to prevent, arriving by the other door.
        /// </para>
        /// </remarks>
        static List<RoadJunction> Settle(
            List<RoadPlace> places, List<RoadSegment> trunks, TerrainField terrain, float radius)
        {
            var junctions = new List<RoadJunction>(places.Count);

            for (int i = 0; i < places.Count; i++)
            {
                RoadPlace place = places[i];
                float height;

                if (place.Kind == RoadJunctionKind.Portal)
                {
                    height = place.Sill;
                }
                else if (!TryHeightOn(trunks, trunks.Count, place.At, radius, out height))
                {
                    height = Solve(terrain, place.At, radius);
                }

                junctions.Add(new RoadJunction(place.Id, place.At, place.Kind, height));
            }

            return junctions;
        }

        /// <summary>Hands the solved heights back, so a branch swept after them can be pinned.</summary>
        static void Attach(List<RoadPlace> places, List<RoadJunction> junctions)
        {
            for (int i = 0; i < places.Count; i++)
            {
                places[i] = places[i].Solved(junctions[i].Height);
            }
        }

        /// <summary>
        /// The height of the widest carriageway that covers a point, if one does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The widest, because where two roads cover the same ground the one anything arriving has
        /// to meet is the trunk; and among equals the nearest, because two trunks that braid cover
        /// the same ground for a stretch and "whichever was laid first" is not an answer anybody
        /// could predict. Covered means inside the carriageway itself rather than inside its apron:
        /// a point beside a road is a point on open ground, and holding it to the road's height
        /// would drag the verge along with it.
        /// </para>
        /// <para>
        /// <paramref name="reach"/> widens that for a caller whose own ground is wider than a point.
        /// A junction disc is about to be graded over everything within its radius, so the road it
        /// has to agree with is any road that radius reaches rather than only one it stands on the
        /// middle of — and a disc that landed a hair outside a trunk's carriageway would otherwise
        /// override the trunk with the height of the ground the trunk is not on.
        /// </para>
        /// <para>
        /// Walked rather than indexed. A network is a couple of dozen polylines of a hundred points
        /// between them, and this is asked once per junction and once per vertex of a road being
        /// swept — a few thousand times over a map, against a spatial index that would have to be
        /// kept deterministic.
        /// </para>
        /// </remarks>
        static bool TryHeightOn(
            List<RoadSegment> segments, int count, Vec2 at, float reach, out float height)
        {
            height = 0f;
            float widest = 0f;
            float closest = float.MaxValue;

            for (int s = 0; s < count; s++)
            {
                RoadSegment segment = segments[s];
                if (segment.Width < widest || segment.Profile.Count == 0)
                {
                    continue;
                }

                float on = segment.Profile.NearestTo(at, out float nearest);
                if (nearest > segment.Width * 0.5f + reach)
                {
                    continue;
                }

                if (segment.Width > widest || nearest < closest)
                {
                    widest = segment.Width;
                    closest = nearest;
                    height = on;
                }
            }

            return widest > 0f;
        }



        /// <summary>
        /// Grades the ground under every carriageway and every crossing into <paramref name="terrain"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The one thing here that changes something outside itself, and it is a replay rather than
        /// a decision: every height it grades was solved by <see cref="Build"/> before this was
        /// called. That is what lets a field rebuilt from a document come back identical — the same
        /// bargain <see cref="TerrainField.Foundations"/> makes.
        /// </para>
        /// <para>
        /// <strong>Roads first, then the crossings.</strong> A later corridor beats an earlier one
        /// where both are at full weight, so grading the junction discs last is what makes a solved
        /// junction height beat either polyline that reaches it — see <see cref="TerrainField.HeightAt"/>.
        /// </para>
        /// <para>
        /// The apron is the carriageway's own width, so the ground takes a road's width either side
        /// to recover: wide enough that a cutting is a batter rather than a step, and narrow enough
        /// that a road does not re-grade the ground a lane away from it.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is null.</exception>
        public void GradeInto(TerrainField terrain)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                RoadSegment segment = _segments[i];
                terrain.AddCorridor(
                    segment.Profile.Points, segment.Profile.Heights,
                    segment.Width * 0.5f, segment.Width);
            }

            var at = new Vec2[1];
            var height = new float[1];

            for (int i = 0; i < _junctions.Count; i++)
            {
                at[0] = _junctions[i].Position;
                height[0] = _junctions[i].Height;
                terrain.AddCorridor(at, height, _junctionRadius, _junctionRadius * 2f);
            }
        }

        /// <summary>
        /// The mean ungraded ground over a junction's own disc, which is the height the crossing is
        /// held to.
        /// </summary>
        /// <remarks>
        /// Measured over the square that contains the disc rather than over the disc itself, because
        /// <see cref="TerrainField.MeanHeightOn"/> is the sampler this project already has and what
        /// the figure is for is choosing a height rather than measuring an area. The corners it
        /// takes in are a carriageway's width from the crossing, which is ground the crossing is
        /// about to grade anyway.
        /// </remarks>
        static float Solve(TerrainField terrain, Vec2 at, float radius) =>
            terrain.MeanHeightOn(new Rect2(at.X - radius, at.Y - radius, at.X + radius, at.Y + radius));

        /// <summary>
        /// Gives every segment the longitudinal profile the ground under it is graded to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A profile, not a plateau.</strong> A building is held at one height because it is
        /// a stack of level storeys; a road held at its start height is a shelf, and over the length
        /// of a map it leaves the far end floating in the air or buried in a bank. So the ungraded
        /// ground is sampled at every vertex and then clamped only where it climbs faster than a
        /// road may be graded — which is most of the time no clamp at all, because the router would
        /// not have laid a step it could not grade.
        /// </para>
        /// <para>
        /// <strong>Both ends are pinned before anything is swept.</strong> A polyline that ends at a
        /// junction takes that junction's solved height, so two roads meeting there start from the
        /// same number; an artery, whose ends are the edge of the map rather than a junction, takes
        /// the mean ground at its own end. Pinning first and sweeping after is what makes each
        /// polyline an independent profile between two fixed ends rather than a chain of roads whose
        /// heights depend on which was graded first.
        /// </para>
        /// <para>
        /// <strong>Forward, then backward, and the backward sweep is the one that keeps its pin.</strong>
        /// The forward pass propagates the start pin and leaves the far end wherever the clamping
        /// put it; the backward pass restores the far end and propagates that. So a polyline is
        /// turned to put a portal at the far end where there is exactly one, because that is the pin
        /// worth keeping exactly: a path a centimetre below a door sill is a step into a building.
        /// </para>
        /// </remarks>
        static void Sweep(
            List<RoadSegment> segments,
            int from,
            int to,
            List<RoadPlace> places,
            TerrainField terrain,
            ArenaParams parameters,
            float step)
        {
            for (int i = from; i < to; i++)
            {
                RoadSegment segment = segments[i];
                IReadOnlyList<Vec2> points = segment.Points;

                int start = NodeAt(places, points[0]);
                int end = NodeAt(places, points[points.Count - 1]);

                // Turned so a portal is the end the backward sweep pins exactly. With a portal at
                // both ends or at neither the order is the polyline's own, and the far end is the
                // one held.
                bool turn = IsPortal(places, start) && !IsPortal(places, end);

                float startHeight = Pin(places, start, terrain, points[0]);
                float endHeight = Pin(places, end, terrain, points[points.Count - 1]);

                Vec2[] along = Resample(points, step);
                if (turn)
                {
                    Array.Reverse(along);
                }

                float[] heights = Profile(
                    segments, i, segment.Width * 0.5f, terrain, along,
                    turn ? endHeight : startHeight,
                    turn ? startHeight : endHeight,
                    parameters.MaxRoadGradient);

                if (turn)
                {
                    Array.Reverse(along);
                    Array.Reverse(heights);
                }

                segments[i] = segment.WithProfile(new RoadProfile(along, heights));
            }
        }

        /// <summary>
        /// The same polyline with every stretch longer than <paramref name="step"/> cut into equal
        /// pieces no longer than it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every vertex of the original survives and none moves, so the resampled line is the same
        /// line: what is added is collinear, and what it is added for is somewhere to put a height.
        /// See <see cref="RoadProfile"/> for why the plan's own vertices are not enough.
        /// </para>
        /// <para>
        /// Equal pieces rather than a walk that steps a fixed distance and leaves a remainder,
        /// because a remainder is a short last piece against a long one and the clamp that follows
        /// is per piece: the gradient a stretch is allowed depends on how far it runs, and two
        /// pieces of unequal length grade differently for no reason anybody chose.
        /// </para>
        /// </remarks>
        static Vec2[] Resample(IReadOnlyList<Vec2> points, float step)
        {
            var along = new List<Vec2>(points.Count) { points[0] };

            for (int i = 1; i < points.Count; i++)
            {
                Vec2 from = points[i - 1];
                Vec2 to = points[i];
                int pieces = Math.Max(1, (int)MathF.Ceiling(Vec2.Distance(from, to) / step));

                for (int p = 1; p < pieces; p++)
                {
                    along.Add(At(from, to, p / (float)pieces));
                }

                // The vertex itself rather than the interpolation at one, which is a rounding error
                // away from it: the ends of the profile are the ends of the road, and a pin lands on
                // the point it was solved for.
                along.Add(to);
            }

            return along.ToArray();
        }

        /// <summary>The heights of one polyline, sampled from the ground and clamped to a gradient.</summary>
        /// <remarks>
        /// <para>
        /// Sampled from the ungraded ground at every vertex, except where a road is already there:
        /// braiding is what the router's cost decay exists to produce, so two roads sharing a
        /// stretch of ground is the ordinary case rather than the awkward one, and a road that
        /// sampled the natural ground under a trunk it is running along would be graded to a
        /// different height over the same metre of map. Measured over the default map, that came out
        /// as a step of up to a metre and a half down the length of a shared stretch. So a vertex
        /// whose own carriageway overlaps one that has already been swept takes that carriageway's
        /// height, and the two agree by construction rather than by luck.
        /// </para>
        /// <para>
        /// Overlapping rather than standing on, because two roads that merely touch are the same
        /// failure at half the width: the ground is at full weight under both, the later one wins
        /// outright, and the difference between the two appears as a step with no blend across it at
        /// all. Sampling half a carriageway either side is what makes the condition "our carriageways
        /// share ground" rather than "my centreline is on yours".
        /// </para>
        /// <para>
        /// Only roads laid before this one, which is what makes the answer a function of the order
        /// they were laid in rather than of a mutual agreement that might not have one: the trunks
        /// are swept first and a branch follows what it joins.
        /// </para>
        /// </remarks>
        static float[] Profile(
            List<RoadSegment> laid,
            int alreadyLaid,
            float halfWidth,
            TerrainField terrain,
            Vec2[] points,
            float startHeight,
            float endHeight,
            float maxGradient)
        {
            int count = points.Length;
            var heights = new float[count];
            for (int i = 0; i < count; i++)
            {
                heights[i] = TryHeightOn(laid, alreadyLaid, points[i], halfWidth, out float on)
                    ? on
                    : terrain.HeightAt(points[i]);
            }

            heights[0] = startHeight;

            for (int i = 1; i < count; i++)
            {
                float limit = maxGradient * Vec2.Distance(points[i - 1], points[i]);
                heights[i] = Clamp(heights[i], heights[i - 1] - limit, heights[i - 1] + limit);
            }

            // The far end is a pin rather than a result, so it goes back to what it was pinned to
            // before the sweep that propagates it. Without this the backward pass has nothing to do:
            // the forward pass already left every step inside the limit, and clamping is idempotent.
            heights[count - 1] = endHeight;

            for (int i = count - 2; i >= 0; i--)
            {
                float limit = maxGradient * Vec2.Distance(points[i], points[i + 1]);
                heights[i] = Clamp(heights[i], heights[i + 1] - limit, heights[i + 1] + limit);
            }

            return heights;
        }

        static float Clamp(float value, float low, float high) =>
            value < low ? low : value > high ? high : value;

        /// <summary>
        /// The junction a polyline ends at, or -1 where it ends at the edge of the map.
        /// </summary>
        /// <remarks>
        /// By position, because that is the only thing the two have in common: a branch is routed
        /// between two cells and a junction stands at a cell's centre, so the ends of the polyline
        /// are the junctions' own coordinates arrived at by the same arithmetic. The slack is a
        /// millimetre against a grid whose cells are a metre — the same distinction
        /// <see cref="WallRun.CornerSlack"/> makes between a fact about the geometry and a fact
        /// about which way two float sums rounded.
        /// </remarks>
        static int NodeAt(List<RoadPlace> places, Vec2 end)
        {
            if (places == null)
            {
                return -1;
            }

            for (int i = 0; i < places.Count; i++)
            {
                if (Vec2.DistanceSquared(places[i].At, end) <= NodeSlack * NodeSlack)
                {
                    return i;
                }
            }

            return -1;
        }

        static bool IsPortal(List<RoadPlace> places, int node) =>
            node >= 0 && places[node].Kind == RoadJunctionKind.Portal;

        static float Pin(List<RoadPlace> places, int node, TerrainField terrain, Vec2 end) =>
            node >= 0 ? places[node].Height : Solve(terrain, end, EndRadius);



        /// <summary>
        /// Turns the laid carriageways into the rectangles they reserve — see
        /// <see cref="Corridors"/>.
        /// </summary>
        static List<Rect2> Reserve(List<RoadSegment> segments)
        {
            var corridors = new List<Rect2>();

            for (int s = 0; s < segments.Count; s++)
            {
                RoadSegment segment = segments[s];
                float half = segment.Width * 0.5f;
                IReadOnlyList<Vec2> points = segment.Points;

                for (int i = 1; i < points.Count; i++)
                {
                    Vec2 from = points[i - 1];
                    Vec2 to = points[i];
                    int pieces = Pieces(from, to, half);

                    for (int p = 0; p < pieces; p++)
                    {
                        // The two ends are the polyline's own points rather than the interpolation
                        // at nought and one, which are a rounding error away from them: a joint
                        // between two segments has to be the same point read from either side of
                        // it, or the reservation has a hairline gap down the middle of a road.
                        Vec2 start = p == 0 ? from : At(from, to, p / (float)pieces);
                        Vec2 end = p == pieces - 1 ? to : At(from, to, (p + 1) / (float)pieces);
                        corridors.Add(Rect2.FromCorners(start, end).Expanded(half));
                    }
                }
            }

            return corridors;
        }

        /// <summary>
        /// How far a piece may run across the short side of its own box before it is cut in two, as
        /// a share of the carriageway's half width.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A quarter of the carriageway. The short side of the box is exactly what a box grown by
        /// half a carriageway claims and the road does not, so it is the thing worth bounding, and
        /// it is bounded in the road's own width because that is what makes the over-claim a fixed
        /// share of the road rather than of the map.
        /// </para>
        /// <para>
        /// Measured over seeds 1..200 of the default map at a road density of 1, the network's
        /// rectangles come to this many times the ground its carriageways actually cover: 1.56
        /// uncut, 1.37 at half a carriageway, <strong>1.30 here</strong>, 1.26 at an eighth and 1.24
        /// at a sixteenth. The figure flattens below this and the rectangle count does not — the
        /// remainder is the corner a box has and a rounded strip end does not, which no amount of
        /// cutting removes — so this is where the cutting stops.
        /// </para>
        /// </remarks>
        const float PieceSkew = 0.5f;

        /// <summary>
        /// How many rectangles a stretch of centreline is reserved as: one while it runs along an
        /// axis, and one more for every <see cref="PieceSkew"/> of a half carriageway it strays off
        /// the long side of its own box.
        /// </summary>
        static int Pieces(Vec2 from, Vec2 to, float half)
        {
            float skew = MathF.Min(MathF.Abs(to.X - from.X), MathF.Abs(to.Y - from.Y));
            return Math.Max(1, (int)MathF.Ceiling(skew / (half * PieceSkew)));
        }

        static Vec2 At(Vec2 from, Vec2 to, float t) => from + (to - from) * t;

        /// <summary>
        /// The cross-axis bands between one lane band and the next, as world rectangles running the
        /// length of the playfield.
        /// </summary>
        /// <remarks>
        /// A gap is where an artery belongs because it is the one band of a map nothing is ever
        /// built in: <see cref="ArenaLayout"/> divides the cross axis into lanes separated by gaps
        /// and every structure is placed inside a lane, so a gap is structure-free on every seed by
        /// construction rather than by luck.
        /// </remarks>
        static List<Rect2> LaneGaps(ArenaLayout layout)
        {
            var gaps = new List<Rect2>();
            float alongMin = layout.AlongOf(layout.Playfield.Min);
            float alongMax = layout.AlongOf(layout.Playfield.Max);

            for (int i = 1; i < layout.Lanes.Count; i++)
            {
                float low = layout.CrossOf(layout.Lanes[i - 1].Band.Max);
                float high = layout.CrossOf(layout.Lanes[i].Band.Min);
                if (high > low)
                {
                    gaps.Add(Rect2.FromCorners(
                        layout.ToWorld(alongMin, low), layout.ToWorld(alongMax, high)));
                }
            }

            return gaps;
        }

        static void Validate(ArenaParams parameters)
        {
            if (!(parameters.ArteryWidth > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.ArteryWidth, "Artery width must be positive.");
            }

            if (!(parameters.PathWidth > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.PathWidth, "Path width must be positive.");
            }

            if (!(parameters.MaxRoadGradient > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.MaxRoadGradient,
                    "The steepest road gradient must be positive, or no ground may be graded at all.");
            }
        }

        /// <summary>
        /// Walks the placements once, collecting what a road may not cross and what it has to reach.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A structure is an object that declares doorways, and the ground it stands on is the pad
        /// its foundation was graded to. Both are already in its metadata, which is what lets this
        /// run without a catalog. Everything else the map holds is passed over: cover is something a
        /// road is laid through rather than round, which is the model <see cref="WalkableGrid"/>
        /// takes of the same objects and for the same reason.
        /// </para>
        /// <para>
        /// Document order throughout, so two runs over the same document number the portals the same
        /// way. A key that is missing or unreadable is a structure with fewer doorways rather than an
        /// exception, on the same terms as <see cref="ArenaLayoutGenerator.Doorways(WorldDoc)"/> —
        /// and a structure with no recorded pad height gives a sill of zero, which is the height a
        /// map with no relief in it grades everything to anyway.
        /// </para>
        /// </remarks>
        static void Collect(
            IReadOnlyList<PlacedObject> committed,
            float pathWidth,
            List<Rect2> structures,
            List<RoadDoorway> doorways)
        {
            for (int i = 0; i < committed.Count; i++)
            {
                PlacedObject placed = committed[i];
                if (placed == null ||
                    !placed.Metadata.TryGetValue(
                        ArenaLayoutGenerator.DoorwayCountKey, out string countText) ||
                    !int.TryParse(
                        countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    continue;
                }

                Rect2 pad;
                float sill = 0f;
                if (placed.Metadata.TryGetValue(
                        ArenaLayoutGenerator.FoundationHeightKey, out string heightText) &&
                    float.TryParse(
                        heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out float graded))
                {
                    sill = graded;
                }

                Rect2 footing;
                if (placed.Metadata.TryGetValue(
                        ArenaLayoutGenerator.FoundationKey, out string padText) &&
                    RectMetadata.TryParse(padText, out footing) &&
                    footing.Width > 0f && footing.Depth > 0f)
                {
                    pad = footing;
                    structures.Add(pad);
                }
                else
                {
                    // A structure whose pad was never written down still declares its doors, and
                    // where its middle is is still what decides which way they face. Nothing is shut
                    // for it: a rectangle nobody recorded is not one a road can be held off.
                    Vec2 at = placed.Pose.Position.Xz;
                    pad = new Rect2(at.X, at.Y, at.X, at.Y);
                }

                for (int d = 0; d < count; d++)
                {
                    string key = ArenaLayoutGenerator.DoorwayKeyPrefix +
                                 d.ToString("00", CultureInfo.InvariantCulture);
                    if (placed.Metadata.TryGetValue(key, out string rectText) &&
                        RectMetadata.TryParse(rectText, out Rect2 doorway))
                    {
                        doorways.Add(new RoadDoorway(doorway, pad, sill, pathWidth));
                    }
                }
            }
        }

        static void AddPortals(
            List<RoadDoorway> doorways,
            RoadRouter router,
            List<RoadPlace> places,
            List<int> nodes)
        {
            for (int i = 0; i < doorways.Count; i++)
            {
                int cell = router.NearestPassableCell(doorways[i].Portal);
                if (cell < 0 || nodes.Contains(cell))
                {
                    continue;
                }

                places.Add(new RoadPlace(
                    "road/portal_" + places.Count.ToString("00", CultureInfo.InvariantCulture),
                    router.CellCentre(cell),
                    RoadJunctionKind.Portal,
                    doorways[i].Sill));
                nodes.Add(cell);
            }
        }

        static void AddTerminals(
            ArenaLayout layout,
            RoadRouter router,
            List<RoadPlace> places,
            List<int> nodes)
        {
            AddTerminal(layout.SpawnAreaA, "a", router, places, nodes);
            AddTerminal(layout.SpawnAreaB, "b", router, places, nodes);
        }

        static void AddTerminal(
            Rect2 spawn,
            string team,
            RoadRouter router,
            List<RoadPlace> places,
            List<int> nodes)
        {
            int cell = router.NearestPassableCell(spawn.Center);
            if (cell < 0 || nodes.Contains(cell))
            {
                return;
            }

            places.Add(new RoadPlace(
                "road/terminal_" + team, router.CellCentre(cell), RoadJunctionKind.Terminal, 0f));
            nodes.Add(cell);
        }

        /// <remarks>
        /// End to end down the middle of each gap. The route is still a search rather than a drawn
        /// line, because the gap is where an artery wants to be and the ground decides whether it
        /// can be — the lane-gap discount is what holds it there while the ground is willing, and
        /// what it gives up where a slope in the gap is too steep to grade.
        /// </remarks>
        static void LayArteries(
            ArenaParams parameters,
            ArenaLayout layout,
            List<Rect2> gaps,
            RoadRouter router,
            List<RoadSegment> segments)
        {
            for (int i = 0; i < gaps.Count; i++)
            {
                float cross = layout.CrossOf(gaps[i].Center);
                int from = router.NearestPassableCell(layout.ToWorld(router.AlongMin, cross));
                int to = router.NearestPassableCell(layout.ToWorld(router.AlongMax, cross));
                if (from < 0 || to < 0 || from == to)
                {
                    continue;
                }

                List<int> route = router.Route(from, to);
                if (route == null)
                {
                    continue;
                }

                List<Vec2> points = router.Smooth(route, parameters.ArteryWidth);
                router.Lay(route, parameters.ArteryWidth);
                segments.Add(new RoadSegment(
                    "road/artery_" + i.ToString("00", CultureInfo.InvariantCulture),
                    RoadClass.Artery, parameters.ArteryWidth, points));
            }
        }

        /// <remarks>
        /// One attachment for every portal and every spawn, which is the node the tree below hangs
        /// that branch off. Two nodes whose nearest artery point lands in the same cell share one
        /// attachment rather than each getting its own — otherwise the tree would hold two nodes a
        /// metre apart and route a road between them.
        /// </remarks>
        static void AddAttachments(
            List<RoadSegment> segments,
            RoadRouter router,
            List<RoadPlace> places,
            List<int> nodes)
        {
            if (segments.Count == 0)
            {
                return;
            }

            int reachable = nodes.Count;
            int made = 0;

            for (int i = 0; i < reachable; i++)
            {
                int cell = router.NearestPassableCell(
                    NearestOnArteries(segments, places[i].At));
                if (cell < 0 || nodes.Contains(cell))
                {
                    continue;
                }

                made++;
                places.Add(new RoadPlace(
                    "road/attachment_" + made.ToString("00", CultureInfo.InvariantCulture),
                    router.CellCentre(cell),
                    RoadJunctionKind.Attachment,
                    0f));
                nodes.Add(cell);
            }
        }

        static Vec2 NearestOnArteries(List<RoadSegment> segments, Vec2 from)
        {
            Vec2 best = from;
            float bestSquared = float.MaxValue;

            for (int s = 0; s < segments.Count; s++)
            {
                if (segments[s].Class != RoadClass.Artery)
                {
                    continue;
                }

                IReadOnlyList<Vec2> points = segments[s].Points;
                for (int i = 1; i < points.Count; i++)
                {
                    Vec2 on = NearestOnSegment(points[i - 1], points[i], from);
                    float distance = Vec2.DistanceSquared(on, from);
                    if (distance < bestSquared)
                    {
                        bestSquared = distance;
                        best = on;
                    }
                }
            }

            return best;
        }

        static Vec2 NearestOnSegment(Vec2 a, Vec2 b, Vec2 point)
        {
            Vec2 along = b - a;
            float lengthSquared = along.SqrLength;
            if (!(lengthSquared > 0f))
            {
                return a;
            }

            float t = Vec2.Dot(point - a, along) / lengthSquared;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return a + along * t;
        }

        /// <remarks>
        /// The tree first, then the redundant edges density asks for, then every one of them routed
        /// longest-first. Longest-first because a short branch braiding into a long one is a
        /// junction and a long trunk braiding into a short stub is a detour — the order decides
        /// which of the two the decay produces.
        /// </remarks>
        static void LayBranches(
            ArenaParams parameters,
            int portalCount,
            List<RoadPlace> places,
            List<int> nodes,
            RoadRouter router,
            List<RoadSegment> segments)
        {
            var positions = new List<Vec2>(places.Count);
            for (int i = 0; i < places.Count; i++)
            {
                positions.Add(places[i].At);
            }

            List<RoadEdge> edges = MinimumSpanningTree(positions);
            AddRedundantEdges(edges, positions, ExtraEdgeCount(portalCount, parameters.RoadDensity));
            edges.Sort(LongestFirst);

            int laid = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                List<int> route = router.Route(nodes[edges[i].A], nodes[edges[i].B]);
                if (route == null || route.Count < 2)
                {
                    continue;
                }

                List<Vec2> points = router.Smooth(route, parameters.PathWidth);
                router.Lay(route, parameters.PathWidth);
                segments.Add(new RoadSegment(
                    "road/path_" + laid.ToString("00", CultureInfo.InvariantCulture),
                    RoadClass.Path, parameters.PathWidth, points));
                laid++;
            }
        }

        /// <summary>How many loop-closing edges are laid over the tree.</summary>
        /// <remarks>
        /// One per six portals is the shape of it — a map with a dozen doors wants a way round as
        /// well as a way in — and <see cref="ArenaParams.RoadDensity"/> scales that. The floor of one
        /// before scaling is what makes a small map at a density of 1 still come out with a loop in
        /// it rather than with a bare tree.
        /// </remarks>
        /// <remarks>
        /// <para>
        /// <strong>The baseline is a sixth of the portals and is not rounded up to one.</strong> It
        /// used to be, and that made the parameter almost inert on the map it matters most on: a
        /// default arena has six portals, so the baseline was one, and every density from a half to
        /// just under one and a half asked for the same single edge. Turning the knob did nothing —
        /// measured over sixty seeds, densities of 0.5 and 1.0 produced identical networks down to
        /// the metre.
        /// </para>
        /// <para>
        /// Scaling the portal count by the density before the division keeps the whole range live:
        /// six portals at 0.5 ask for none, at 1 for one, at 2 for two. Zero edges is a legitimate
        /// answer — it is the spanning tree with nothing added, which is the one route the network
        /// needs — and the density of zero that turns the stage off entirely is checked long before
        /// this.
        /// </para>
        /// </remarks>
        static int ExtraEdgeCount(int portalCount, float density)
        {
            if (!(density > 0f) || portalCount <= 0)
            {
                return 0;
            }

            int scaled = (int)MathF.Round(portalCount * density / 6f, MidpointRounding.AwayFromZero);
            return scaled < 0 ? 0 : scaled;
        }

        /// <summary>The shortest set of edges that joins every node to every other.</summary>
        /// <remarks>
        /// <para>
        /// Over the portals, the spawns and the attachment points together, which is what leaves the
        /// two trunks joined to each other and every door joined to something. Some of what it buys
        /// is a road along an artery that is already there — two attachments on one trunk are joined
        /// by the trunk, and the tree does not know it. That is not waste: the route between them is
        /// laid on ground the artery has already made cheap, so it costs nothing new on the map, and
        /// leaving those edges in is what makes the whole thing one piece without a special case
        /// saying so.
        /// </para>
        /// <para>
        /// Prim's over the squared distances, which order the same way the distances do and cost no
        /// square root. Ties go to the lowest node index, so the tree is a function of the positions
        /// alone.
        /// </para>
        /// </remarks>
        static List<RoadEdge> MinimumSpanningTree(List<Vec2> positions)
        {
            var edges = new List<RoadEdge>();
            int count = positions.Count;
            if (count < 2)
            {
                return edges;
            }

            var inTree = new bool[count];
            var best = new float[count];
            var bestFrom = new int[count];

            inTree[0] = true;
            for (int i = 1; i < count; i++)
            {
                best[i] = Vec2.DistanceSquared(positions[0], positions[i]);
                bestFrom[i] = 0;
            }

            for (int added = 1; added < count; added++)
            {
                int pick = -1;
                for (int i = 1; i < count; i++)
                {
                    if (!inTree[i] && (pick < 0 || best[i] < best[pick]))
                    {
                        pick = i;
                    }
                }

                if (pick < 0)
                {
                    break;
                }

                inTree[pick] = true;
                edges.Add(new RoadEdge(bestFrom[pick], pick, best[pick]));

                for (int i = 1; i < count; i++)
                {
                    if (inTree[i])
                    {
                        continue;
                    }

                    float distance = Vec2.DistanceSquared(positions[pick], positions[i]);
                    if (distance < best[i])
                    {
                        best[i] = distance;
                        bestFrom[i] = pick;
                    }
                }
            }

            return edges;
        }

        /// <remarks>
        /// The shortest non-tree edges, skipping any whose ends the network already joins in two
        /// hops or fewer. An edge between neighbours is a second road beside a road; an edge that
        /// closes a longer loop is the one that gives a map a way round.
        /// </remarks>
        static void AddRedundantEdges(List<RoadEdge> edges, List<Vec2> positions, int wanted)
        {
            if (wanted <= 0 || positions.Count < 4)
            {
                return;
            }

            var candidates = new List<RoadEdge>();
            for (int a = 0; a < positions.Count; a++)
            {
                for (int b = a + 1; b < positions.Count; b++)
                {
                    if (!Joins(edges, a, b))
                    {
                        candidates.Add(
                            new RoadEdge(a, b, Vec2.DistanceSquared(positions[a], positions[b])));
                    }
                }
            }

            candidates.Sort(ShortestFirst);

            List<List<int>> adjacency = Adjacency(edges, positions.Count);
            int taken = 0;
            for (int i = 0; i < candidates.Count && taken < wanted; i++)
            {
                RoadEdge candidate = candidates[i];
                if (WithinTwoHops(adjacency, candidate.A, candidate.B))
                {
                    continue;
                }

                edges.Add(candidate);
                adjacency[candidate.A].Add(candidate.B);
                adjacency[candidate.B].Add(candidate.A);
                taken++;
            }
        }

        static bool Joins(List<RoadEdge> edges, int a, int b)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if ((edges[i].A == a && edges[i].B == b) || (edges[i].A == b && edges[i].B == a))
                {
                    return true;
                }
            }

            return false;
        }

        static List<List<int>> Adjacency(List<RoadEdge> edges, int count)
        {
            var adjacency = new List<List<int>>(count);
            for (int i = 0; i < count; i++)
            {
                adjacency.Add(new List<int>());
            }

            for (int i = 0; i < edges.Count; i++)
            {
                adjacency[edges[i].A].Add(edges[i].B);
                adjacency[edges[i].B].Add(edges[i].A);
            }

            return adjacency;
        }

        static bool WithinTwoHops(List<List<int>> adjacency, int from, int to)
        {
            List<int> first = adjacency[from];
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i] == to)
                {
                    return true;
                }

                List<int> second = adjacency[first[i]];
                for (int j = 0; j < second.Count; j++)
                {
                    if (second[j] == to)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static int ShortestFirst(RoadEdge a, RoadEdge b)
        {
            int order = a.LengthSquared.CompareTo(b.LengthSquared);
            if (order != 0)
            {
                return order;
            }

            order = a.A.CompareTo(b.A);
            return order != 0 ? order : a.B.CompareTo(b.B);
        }

        static int LongestFirst(RoadEdge a, RoadEdge b)
        {
            int order = b.LengthSquared.CompareTo(a.LengthSquared);
            if (order != 0)
            {
                return order;
            }

            order = a.A.CompareTo(b.A);
            return order != 0 ? order : a.B.CompareTo(b.B);
        }
    }

    /// <summary>
    /// A place the network reaches while it is still being built, before its height is solved.
    /// </summary>
    /// <remarks>
    /// A <see cref="RoadJunction"/> is what a finished network hands out and it carries a height, so
    /// it cannot be made until that height is known — and the height of a junction standing on a
    /// trunk is not known until the trunk has been swept, which is after the junction's position has
    /// to exist for the trunk's branches to be routed to it. This is the half that exists in between:
    /// where the place is and what it is, with the sill a portal brought with it and nothing else.
    /// </remarks>
    readonly struct RoadPlace
    {
        public RoadPlace(string id, Vec2 at, RoadJunctionKind kind, float sill)
            : this(id, at, kind, sill, sill)
        {
        }

        RoadPlace(string id, Vec2 at, RoadJunctionKind kind, float sill, float height)
        {
            Id = id;
            At = at;
            Kind = kind;
            Sill = sill;
            Height = height;
        }

        /// <summary>Readable path, which the junction made from this takes unchanged.</summary>
        public string Id { get; }

        /// <summary>Where the place stands, on the ground plane.</summary>
        public Vec2 At { get; }

        /// <summary>What kind of place this is.</summary>
        public RoadJunctionKind Kind { get; }

        /// <summary>The door sill a portal brought with it. Zero and meaningless for other kinds.</summary>
        public float Sill { get; }

        /// <summary>The solved height, once there is one. The sill until then.</summary>
        public float Height { get; }

        /// <summary>The same place with its height solved.</summary>
        public RoadPlace Solved(float height) => new RoadPlace(Id, At, Kind, Sill, height);
    }

    /// <summary>One doorway read back out of a structure's metadata, and the node it gives.</summary>
    /// <remarks>
    /// The outward normal is the rectangle's short axis — a doorway is a slot through a wall, so the
    /// thin way is the way through — signed away from the middle of the structure it belongs to. The
    /// portal then stands one carriageway outside the threshold, which is where a branch meets the
    /// door rather than where the door is.
    /// </remarks>
    /// <remarks>
    /// The pad rather than a bare centre, because the approach has to be carved past the clearance
    /// ring and the ring is measured off the pad.
    /// </remarks>
    readonly struct RoadDoorway
    {
        public RoadDoorway(Rect2 rect, Rect2 pad, float sill, float pathWidth)
        {
            Sill = sill;

            Vec2 centre = pad.Center;
            bool throughX = rect.Width <= rect.Depth;

            float offset = throughX ? rect.Center.X - centre.X : rect.Center.Y - centre.Y;
            float sign = offset < 0f ? -1f : 1f;
            Vec2 outward = throughX ? new Vec2(sign, 0f) : new Vec2(0f, sign);

            float through = (throughX ? rect.Width : rect.Depth) * 0.5f;
            float across = MathF.Max(throughX ? rect.Depth : rect.Width, pathWidth);
            Portal = rect.Center + outward * (through + pathWidth);

            // The approach is carved out of the pad's own shut ground, because a door opening onto
            // ground no road may use is a building with no way in — the same guarantee the boundary
            // fence gives its gates, arranged rather than hoped for.
            //
            // Clear of the pad and not merely as far as the portal, which is the whole of why the
            // reach is worked out rather than typed: an approach carved only to the portal can leave
            // it in a pocket, a node with a road's worth of ground round it and no way off, which
            // fails as completely as a door with nothing in front of it and is far harder to see.
            // The rectangle is symmetric about the doorway because the half pointing into the
            // building falls on the pad, and the pad is the one thing an approach may not open.
            float beyondPad = (throughX
                ? (sign > 0f ? pad.MaxX - rect.Center.X : rect.Center.X - pad.MinX)
                : (sign > 0f ? pad.MaxZ - rect.Center.Y : rect.Center.Y - pad.MinZ)) + pathWidth;

            float reach = MathF.Max(through + pathWidth, beyondPad);
            Vec2 half = throughX
                ? new Vec2(reach, across * 0.5f)
                : new Vec2(across * 0.5f, reach);
            Approach = Rect2.FromCorners(rect.Center - half, rect.Center + half);
        }

        /// <summary>The graded height of the structure this door is cut into, in metres.</summary>
        /// <remarks>
        /// Read out of the structure's own metadata rather than sampled off the ground at the
        /// threshold, because what a path has to arrive level with is the floor behind the door and
        /// that is the pad's height, not the height of whatever the ground was doing before the pad
        /// was cut into it. Sampling would put the path level with the apron, which is a step.
        /// </remarks>
        public float Sill { get; }

        /// <summary>Where the branch serving this door begins.</summary>
        public Vec2 Portal { get; }

        /// <summary>The ground between the threshold and the portal.</summary>
        public Rect2 Approach { get; }
    }

    /// <summary>One edge of the branch topology, before anything is routed along it.</summary>
    readonly struct RoadEdge
    {
        public RoadEdge(int a, int b, float lengthSquared)
        {
            A = a;
            B = b;
            LengthSquared = lengthSquared;
        }

        /// <summary>Index of the junction at one end.</summary>
        public int A { get; }

        /// <summary>Index of the junction at the other.</summary>
        public int B { get; }

        /// <summary>Squared straight-line distance between the two, which orders as the distance does.</summary>
        public float LengthSquared { get; }
    }

    /// <summary>One entry in the search frontier.</summary>
    /// <remarks>
    /// The estimate and the heuristic are both carried because the pop order is <c>(f, then h, then
    /// cell)</c> and not the heap's own. A heap is not a stable container: two entries of equal
    /// priority come out in whichever order the sift happened to leave them, which is a function of
    /// how many pushes went before and therefore of nothing anybody can reproduce. Preferring the
    /// lower <c>h</c> at equal <c>f</c> also breaks toward the goal, which is the tie-break a plain
    /// A* wants anyway; the cell index is what settles the rest.
    /// </remarks>
    readonly struct Frontier
    {
        public Frontier(int estimate, int heuristic, int cell)
        {
            Estimate = estimate;
            Heuristic = heuristic;
            Cell = cell;
        }

        /// <summary>Cost so far plus the estimate of what is left.</summary>
        public int Estimate { get; }

        /// <summary>The estimate of what is left, on its own.</summary>
        public int Heuristic { get; }

        /// <summary>The grid cell this entry stands for.</summary>
        public int Cell { get; }
    }

    /// <summary>
    /// The cost grid a network is routed over, and the flat A* that routes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <c>int[]</c> over the playfield grid, allocated once and reused by every query a network
    /// makes. That is what lets a route change the ground the next route is planned on: the decay
    /// <see cref="Lay"/> applies is not a separate structure consulted afterwards, it is a rewrite of
    /// the grid itself, so the next A* simply finds the existing road cheap and takes it.
    /// </para>
    /// <para>
    /// <strong>Integer costs throughout.</strong> Ten to cross a cell, ten and fourteen for an
    /// orthogonal and a diagonal step, an octile heuristic in the same units. A float grid would put
    /// the ordering of two nearly equal routes at the mercy of the last bit of a sum, and the sum's
    /// order depends on the order the frontier was popped in — which is exactly the loop this is
    /// trying to keep deterministic.
    /// </para>
    /// </remarks>
    sealed class RoadRouter
    {
        /// <summary>What it costs to cross an ordinary cell.</summary>
        const int BaseCellCost = 10;

        /// <summary>Step multiplier for a move along an axis.</summary>
        const int OrthogonalStep = 10;

        /// <summary>Step multiplier for a diagonal move, which is 10 x sqrt(2) rounded.</summary>
        const int DiagonalStep = 14;

        /// <summary>Most a cell's cost is raised by the slope across it.</summary>
        const int GradientPenalty = 20;

        /// <summary>Most a cell's cost is raised by standing in the open.</summary>
        const int ExposurePenalty = 15;

        /// <summary>What a cell in a lane gap is discounted by.</summary>
        const int LaneGapDiscount = 4;

        /// <summary>
        /// What a change of direction costs, as a multiple of the cell being entered.
        /// </summary>
        /// <remarks>
        /// A multiple rather than a constant so that a road already braided into — whose cells have
        /// been quartered — is not made to run dead straight through expensive ground because
        /// turning onto it costs more than the ground does. What the penalty buys is a route in long
        /// runs joined at corners rather than the shimmering diagonal staircase a tie-broken grid
        /// search produces, which is the difference between something that reads as a road and
        /// something that reads as a path found by a computer.
        /// </remarks>
        const int TurnPenaltyPerCell = 4;

        /// <summary>What a cell decayed by braiding may never fall below.</summary>
        /// <remarks>
        /// One rather than zero. Free ground would let a route wander any distance along an existing
        /// road for nothing, which makes two routes of equal cost differ only in how the tie was
        /// broken — and a road that takes a scenic detour because it was free is not a road.
        /// </remarks>
        const int MinCellCost = 1;

        /// <summary>What a route's cells are divided by once it is laid.</summary>
        const int DecayDivisor = 4;

        /// <summary>What the ground beside a route is divided by.</summary>
        const int VergeDivisor = 2;

        /// <summary>The cost of a cell no road may use.</summary>
        const int Impassable = int.MaxValue;

        /// <summary>How many times a rounded polyline may be re-tested and its bad corners undone.</summary>
        /// <remarks>
        /// Three, which is one more than the two it has ever taken. Each pass gives up on at least
        /// one corner and a corner given up on is never rounded again, so the loop cannot run
        /// forever; the cap is there so a bug in it would come out as an unsmoothed road rather than
        /// as an editor that never returns.
        /// </remarks>
        const int RoundingPasses = 3;

        /// <summary>Marks a point of a polyline that no corner rounding produced.</summary>
        const int NotACorner = -1;

        static readonly int[] NeighbourX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] NeighbourZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        readonly TerrainField _terrain;
        readonly List<Rect2> _structures;
        readonly List<Rect2> _gaps;
        readonly Rect2 _open;
        readonly float _maxGradient;
        readonly float _cellSize;
        readonly float _originX;
        readonly float _originZ;
        readonly int _countX;
        readonly int _countZ;

        readonly float[] _height;
        readonly float[] _shelter;
        readonly int[] _cost;
        readonly int[] _pristine;
        readonly int[] _g;
        readonly int[] _from;
        readonly int[] _seen;
        readonly int[] _closed;
        readonly int[] _decayed;
        readonly int[] _region;
        readonly bool[] _covered;
        readonly List<Frontier> _heap;

        int _generation;
        int _lay;
        int _minCellCost;
        int _pristineMinCost;
        readonly int _mainRegion;
        int _coveredCells;
        int _independentCells;

        /// <summary>Builds the cost grid a map's ground, lanes and structures describe.</summary>
        public RoadRouter(
            ArenaParams parameters,
            ArenaLayout layout,
            TerrainField terrain,
            IReadOnlyList<PlacedObject> committed,
            List<Rect2> structures,
            List<RoadDoorway> doorways,
            List<Rect2> gaps)
        {
            ArenaGrid grid = layout.Grid;
            _terrain = terrain;
            _structures = structures;
            _gaps = gaps;
            _maxGradient = parameters.MaxRoadGradient;
            _cellSize = grid.CellSize;
            _originX = grid.Bounds.MinX;
            _originZ = grid.Bounds.MinZ;
            _countX = grid.CountX;
            _countZ = grid.CountZ;

            // A centreline stays half its own width inside the playfield, so the carriageway's edge
            // stops at the boundary rather than running out under the fence standing on it. Half the
            // widest class, because the margin has to hold whichever class ends up laid there.
            float margin = MathF.Max(parameters.ArteryWidth, parameters.PathWidth) * 0.5f;
            _open = layout.Playfield.Expanded(-margin);
            AlongMin = layout.AlongOf(_open.Min);
            AlongMax = layout.AlongOf(_open.Max);

            int cells = _countX * _countZ;
            _height = new float[cells];
            for (int cell = 0; cell < cells; cell++)
            {
                _height[cell] = terrain.HeightAt(CellCentre(cell));
            }

            _shelter = Shelter(committed);
            _cost = new int[cells];
            _g = new int[cells];
            _from = new int[cells];
            _seen = new int[cells];
            _closed = new int[cells];
            _decayed = new int[cells];
            _region = new int[cells];
            _covered = new bool[cells];
            _heap = new List<Frontier>(cells / 8 + 16);

            for (int cell = 0; cell < cells; cell++)
            {
                _cost[cell] = NaturalCost(cell);
            }

            // The pads, and not a ring round them. A pad is already the footprint plus a metre of
            // doorstep, which is the clearance — so a road runs along the doorstep and never over
            // the building.
            //
            // Anything wider closes the map. Two structures may stand as close as
            // ArenaLayoutGenerator.StructureClearance apart, and a ring reaching more than half of
            // what is left between their pads meets the neighbour's ring in the middle. The ground
            // between two buildings then belongs to neither and opens onto nothing, and a door
            // facing its neighbour becomes a portal with a road's worth of floor in front of it and
            // no way off. The yard was the first answer here and that is exactly what it did.
            for (int i = 0; i < structures.Count; i++)
            {
                Shut(structures[i]);
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                Open(doorways[i].Approach);
            }

            _minCellCost = Impassable;
            for (int cell = 0; cell < cells; cell++)
            {
                if (_cost[cell] != Impassable && _cost[cell] < _minCellCost)
                {
                    _minCellCost = _cost[cell];
                }
            }

            if (_minCellCost == Impassable)
            {
                _minCellCost = BaseCellCost;
            }

            // The grid as it stands before a metre of road has been laid, kept so that every route
            // can also be asked what it would have cost on its own. That answer is the only honest
            // denominator for what braiding saved: measuring against the routes as they were
            // actually laid would count a branch's detour to reach a road it could share as though
            // the detour were road saved, which is the opposite of the truth.
            _pristine = new int[cells];
            Array.Copy(_cost, _pristine, cells);
            _pristineMinCost = _minCellCost;

            _mainRegion = LabelRegions();
        }

        /// <summary>Lowest along-axis coordinate a road may reach.</summary>
        public float AlongMin { get; }

        /// <summary>Highest along-axis coordinate a road may reach.</summary>
        public float AlongMax { get; }

        /// <summary>Distinct ground the laid routes cover, in metres.</summary>
        public float CorridorLength => _coveredCells * _cellSize;

        /// <summary>The sum of what every laid route would have covered on its own, in metres.</summary>
        public float UnbraidedLength => _independentCells * _cellSize;


        /// <summary>Centre of a grid cell.</summary>
        public Vec2 CellCentre(int cell) => new Vec2(
            _originX + (cell % _countX + 0.5f) * _cellSize,
            _originZ + (cell / _countX + 0.5f) * _cellSize);

        /// <summary>
        /// The cell a node stands in, or the nearest one a road can actually get to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Reachable, not merely open.</strong> A door on a hillside opens onto a bank the
        /// map will not let a road be graded to, and the flat yard at the foot of that bank is open
        /// ground with no way off it. A portal put there is a portal no road can reach, and every
        /// route to it fails — which comes out as a network quietly missing a branch rather than as
        /// anything anybody would notice. So a node is only ever placed on the main region: the
        /// largest piece of ground a road can move around in, which is the map itself.
        /// </para>
        /// <para>
        /// Moving a portal to the nearest ground a road can use is also what a surveyor would do.
        /// The road comes as close to the door as the ground allows and the last few metres are
        /// somebody's steps.
        /// </para>
        /// <para>
        /// Rings outward from the cell asked for, taking the true nearest within the first ring that
        /// offers anything and the lowest cell index between two equally near, so the answer is a
        /// function of the grid alone.
        /// </para>
        /// </remarks>
        public int NearestPassableCell(Vec2 point)
        {
            int x = (int)MathF.Floor((point.X - _originX) / _cellSize);
            int z = (int)MathF.Floor((point.Y - _originZ) / _cellSize);
            x = x < 0 ? 0 : x >= _countX ? _countX - 1 : x;
            z = z < 0 ? 0 : z >= _countZ ? _countZ - 1 : z;

            if (Passable(x, z))
            {
                return z * _countX + x;
            }

            int reach = _countX + _countZ;
            for (int ring = 1; ring <= reach; ring++)
            {
                int best = -1;
                float bestDistance = 0f;

                for (int oz = -ring; oz <= ring; oz++)
                {
                    for (int ox = -ring; ox <= ring; ox++)
                    {
                        if (Math.Abs(ox) != ring && Math.Abs(oz) != ring)
                        {
                            continue;
                        }

                        int nx = x + ox;
                        int nz = z + oz;
                        if (!Passable(nx, nz))
                        {
                            continue;
                        }

                        int cell = nz * _countX + nx;
                        float distance = Vec2.DistanceSquared(CellCentre(cell), point);
                        if (best < 0 || distance < bestDistance)
                        {
                            best = cell;
                            bestDistance = distance;
                        }
                    }
                }

                if (best >= 0)
                {
                    return best;
                }
            }

            return -1;
        }

        /// <summary>
        /// The cheapest route between two cells over the grid as it stands, as the cells it runs
        /// through, or null if there is none.
        /// </summary>
        /// <remarks>
        /// The scratch arrays are cleared with a generation stamp rather than a fill, so a query
        /// costs what it visits rather than what the grid holds — which is the difference between a
        /// short branch on a 160,000-cell map costing a hundred cells and costing all of them. Which
        /// costs are read is an argument because the same search answers two questions: what a route
        /// costs now, and what it would have cost before any road existed.
        /// </remarks>
        public List<int> Route(int start, int goal) => Search(_cost, _minCellCost, start, goal);

        List<int> Search(int[] costs, int scale, int start, int goal)
        {
            if (start < 0 || goal < 0 || start == goal || !Passable(start) || !Passable(goal))
            {
                return null;
            }

            _generation++;
            _heap.Clear();

            _g[start] = 0;
            _from[start] = -1;
            _seen[start] = _generation;
            int opening = Heuristic(start, goal, scale);
            Push(new Frontier(opening, opening, start));

            while (_heap.Count > 0)
            {
                int cell = Pop().Cell;
                if (_closed[cell] == _generation)
                {
                    continue;
                }

                _closed[cell] = _generation;
                if (cell == goal)
                {
                    return Reconstruct(start, goal);
                }

                int x = cell % _countX;
                int z = cell / _countX;
                int came = _from[cell];
                int inX = came < 0 ? 0 : x - came % _countX;
                int inZ = came < 0 ? 0 : z - came / _countX;

                for (int d = 0; d < 8; d++)
                {
                    int stepX = NeighbourX[d];
                    int stepZ = NeighbourZ[d];
                    int nx = x + stepX;
                    int nz = z + stepZ;
                    if (nx < 0 || nx >= _countX || nz < 0 || nz >= _countZ)
                    {
                        continue;
                    }

                    int next = nz * _countX + nx;
                    int cost = costs[next];
                    if (cost == Impassable)
                    {
                        continue;
                    }

                    // No cutting a corner between two shut cells: a road that slipped diagonally
                    // between the corners of two buildings is a road through both of them.
                    //
                    // The climb is checked here too, and only here, because a cell's own gradient is
                    // the steepest step to one of its four neighbours — which says nothing about the
                    // fifth way out of it. A diagonal covers half again the ground of an orthogonal
                    // step and can climb both of the rises either side of it, so a route made only of
                    // legal cells could still be a staircase steeper than any of them.
                    bool diagonal = stepX != 0 && stepZ != 0;
                    if (diagonal &&
                        (!Passable(x + stepX, z) || !Passable(x, z + stepZ) || TooSteep(cell, next)))
                    {
                        continue;
                    }

                    int step = cost * (diagonal ? DiagonalStep : OrthogonalStep);
                    if (came >= 0 && (stepX != inX || stepZ != inZ))
                    {
                        step += cost * TurnPenaltyPerCell;
                    }

                    int through = _g[cell] + step;
                    if (_seen[next] == _generation && through >= _g[next])
                    {
                        continue;
                    }

                    _g[next] = through;
                    _from[next] = cell;
                    _seen[next] = _generation;

                    // A step that costs more to turn onto than to run onto makes the cost function
                    // direction-dependent, so a cell already settled can still be improved. Clearing
                    // the mark is what lets it be popped again rather than skipped as stale.
                    _closed[next] = 0;

                    int heuristic = Heuristic(next, goal, scale);
                    Push(new Frontier(through + heuristic, heuristic, next));
                }
            }

            return null;
        }

        /// <summary>
        /// Records a routed line as laid road and quarters the cost of the ground it took.
        /// </summary>
        /// <remarks>
        /// Half a carriageway either side, not just the centreline, because what the next route
        /// should find cheap is the road and not a line down the middle of it — a branch arriving at
        /// forty-five degrees would otherwise cross the carriageway rather than join it.
        /// </remarks>
        public void Lay(List<int> cells, float width)
        {
            _lay++;
            float half = width * 0.5f;
            float halfSquared = half * half;
            int reach = (int)MathF.Ceiling(half / _cellSize);

            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (!_covered[cell])
                {
                    _covered[cell] = true;
                    _coveredCells++;
                }

                int x = cell % _countX;
                int z = cell / _countX;
                Vec2 centre = CellCentre(cell);

                for (int oz = -reach; oz <= reach; oz++)
                {
                    int nz = z + oz;
                    if (nz < 0 || nz >= _countZ)
                    {
                        continue;
                    }

                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        int nx = x + ox;
                        if (nx < 0 || nx >= _countX)
                        {
                            continue;
                        }

                        int near = nz * _countX + nx;
                        if ((_decayed[near] == _lay && near != cell) || _cost[near] == Impassable ||
                            Vec2.DistanceSquared(CellCentre(near), centre) > halfSquared)
                        {
                            continue;
                        }

                        _decayed[near] = _lay;
                        int divisor = near == cell ? DecayDivisor : VergeDivisor;
                        int decayed = _cost[near] / divisor;

                        // Never below the same fraction of what the ground itself costs. Dividing
                        // again for every road that arrives is what the decay says on its face, and
                        // left unbounded it takes a well-used cell to nothing in three routes — at
                        // which point a branch will run the length of the map along existing road
                        // rather than cross one metre of grass, because the grass costs more than
                        // the whole detour. A road is a quarter the cost of the ground it is on; a
                        // road several roads use is not free.
                        int floor = _pristine[near] / divisor;
                        if (decayed < floor)
                        {
                            decayed = floor;
                        }

                        // And never above what the cell already cost. A carriageway that a later
                        // road merely passed beside would otherwise be put back up to a verge's
                        // price, which is a road becoming harder to use the more use it gets.
                        if (decayed > _cost[near])
                        {
                            decayed = _cost[near];
                        }

                        _cost[near] = decayed < MinCellCost ? MinCellCost : decayed;
                        if (_cost[near] < _minCellCost)
                        {
                            _minCellCost = _cost[near];
                        }
                    }
                }
            }

            // What this route would have come to laid on ground no other road had touched. The
            // second search is the price of being able to say what braiding saved rather than
            // asserting it, and it runs over a grid that never changes, so it is the same answer
            // whenever it is asked.
            List<int> alone = Search(
                _pristine, _pristineMinCost, cells[0], cells[cells.Count - 1]);
            _independentCells += alone?.Count ?? cells.Count;
        }

        /// <summary>
        /// Turns a routed line of cells into the polyline a road is drawn as.
        /// </summary>
        /// <remarks>
        /// Simplify, then round, and in that order. Rounding the raw cell centres would spend both
        /// Chaikin iterations smoothing the staircase a grid search leaves behind and come out with
        /// a wavy staircase; string-pulling first throws the staircase away and leaves the corners
        /// that are actually corners for the rounding to work on.
        /// </remarks>
        public List<Vec2> Smooth(List<int> cells, float width)
        {
            var raw = new List<Vec2>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                raw.Add(CellCentre(cells[i]));
            }

            return RoundCorners(Simplify(raw, width), width);
        }

        /// <summary>
        /// True when a carriageway of <paramref name="width"/> may be laid from one point to the
        /// other.
        /// </summary>
        /// <remarks>
        /// Two clauses, and each answers what the other cannot. A structure is tested exactly, as a
        /// rectangle grown by half the carriageway against the segment — the same
        /// <see cref="Occluder"/> arithmetic a sightline is tested with, because "does this line
        /// cross that box" is one question however it is asked, and it is the question the hard
        /// guarantee is about. Everything else a cell can be shut for — the margin round the
        /// playfield, ground too steep to grade — is a fact about cells, and is answered by walking
        /// the cells the centreline crosses, which is the same model the route itself was found in.
        /// </remarks>
        public bool CorridorClear(Vec2 from, Vec2 to, float width)
        {
            float half = width * 0.5f;
            float minX = MathF.Min(from.X, to.X);
            float minZ = MathF.Min(from.Y, to.Y);
            float maxX = MathF.Max(from.X, to.X);
            float maxZ = MathF.Max(from.Y, to.Y);

            for (int i = 0; i < _structures.Count; i++)
            {
                Rect2 kept = _structures[i].Expanded(half);
                var blocker = new Occluder(
                    kept.Center, new Vec2(1f, 0f), kept.Width * 0.5f, kept.Depth * 0.5f);

                if (blocker.MightBlock(minX, minZ, maxX, maxZ) && blocker.Blocks(from, to))
                {
                    return false;
                }
            }

            return NotTooSteep(from, to) && WalkPassable(from, to);
        }

        /// <summary>True if a carriageway laid along this line never climbs faster than it may.</summary>
        /// <remarks>
        /// <para>
        /// A cut corner is a shorter run for the same rise, so a segment straight enough to be worth
        /// keeping can still be steeper than every step it replaced. Held to the limit here rather
        /// than measured afterwards, which is what makes "no routed segment is steeper than the map
        /// allows" a fact about the polyline rather than a hope about it.
        /// </para>
        /// <para>
        /// Along the line rather than end to end, at the resolution the ground is modelled at. End
        /// to end is the average climb, and a road that goes up a bank and down the other side has
        /// an average climb of nothing.
        /// </para>
        /// </remarks>
        bool NotTooSteep(Vec2 from, Vec2 to)
        {
            float run = Vec2.Distance(from, to);
            if (!(run > 0f))
            {
                return true;
            }

            int steps = (int)MathF.Ceiling(run / _cellSize);
            float allowed = _maxGradient * (run / steps);
            float behind = _terrain.HeightAt(from);

            for (int i = 1; i <= steps; i++)
            {
                Vec2 at = from + (to - from) * (i / (float)steps);
                float height = _terrain.HeightAt(at);
                if (MathF.Abs(height - behind) > allowed)
                {
                    return false;
                }

                behind = height;
            }

            return true;
        }

        /// <summary>True if every cell the segment crosses is one a road may use.</summary>
        /// <remarks>
        /// A grid traversal rather than a sample every so many centimetres: a walk visits exactly
        /// the cells the line touches, so a single shut cell clipped at a corner cannot be stepped
        /// over — which is the one thing a sampled test would miss and the reason it would be
        /// missed on one seed in a hundred rather than reliably.
        /// </remarks>
        bool WalkPassable(Vec2 from, Vec2 to)
        {
            int x = (int)MathF.Floor((from.X - _originX) / _cellSize);
            int z = (int)MathF.Floor((from.Y - _originZ) / _cellSize);
            int endX = (int)MathF.Floor((to.X - _originX) / _cellSize);
            int endZ = (int)MathF.Floor((to.Y - _originZ) / _cellSize);

            if (!Passable(x, z) || !Passable(endX, endZ))
            {
                return false;
            }

            float runX = to.X - from.X;
            float runZ = to.Y - from.Y;
            int stepX = runX > 0f ? 1 : runX < 0f ? -1 : 0;
            int stepZ = runZ > 0f ? 1 : runZ < 0f ? -1 : 0;

            float boundaryX = _originX + (x + (stepX > 0 ? 1 : 0)) * _cellSize;
            float boundaryZ = _originZ + (z + (stepZ > 0 ? 1 : 0)) * _cellSize;

            float nextX = stepX != 0 ? (boundaryX - from.X) / runX : float.PositiveInfinity;
            float nextZ = stepZ != 0 ? (boundaryZ - from.Y) / runZ : float.PositiveInfinity;
            float acrossX = stepX != 0 ? _cellSize / MathF.Abs(runX) : float.PositiveInfinity;
            float acrossZ = stepZ != 0 ? _cellSize / MathF.Abs(runZ) : float.PositiveInfinity;

            for (int guard = _countX + _countZ + 2; guard > 0; guard--)
            {
                if (x == endX && z == endZ)
                {
                    return true;
                }

                if (nextX < nextZ)
                {
                    x += stepX;
                    nextX += acrossX;
                }
                else
                {
                    z += stepZ;
                    nextZ += acrossZ;
                }

                if (!Passable(x, z))
                {
                    return false;
                }
            }

            return false;
        }

        /// <remarks>
        /// One pass, dropping every node the last kept one can see past. The clearance is the road's
        /// own width rather than a point-to-point line of sight: a corner cut so fine that the
        /// carriageway clips a wall is a corner that was not there to cut.
        /// </remarks>
        List<Vec2> Simplify(List<Vec2> points, float width)
        {
            if (points.Count <= 2)
            {
                return points;
            }

            var kept = new List<Vec2>(points.Count) { points[0] };
            for (int i = 1; i < points.Count - 1; i++)
            {
                if (!CorridorClear(kept[kept.Count - 1], points[i + 1], width))
                {
                    kept.Add(points[i]);
                }
            }

            kept.Add(points[points.Count - 1]);
            return kept;
        }

        /// <remarks>
        /// <para>
        /// Two Chaikin iterations, which is corner cutting at a quarter twice over. Chaikin because
        /// every point it produces is an add and a multiply by a quarter — exact in binary, and
        /// therefore the same on every machine. An arc fillet would want a sine and a cosine, and
        /// trigonometry is not bit-identical across runtimes, which is the argument
        /// <see cref="YawStep"/> already makes about rotations.
        /// </para>
        /// <para>
        /// Applied corner by corner rather than to the polyline as a whole, so a corner whose
        /// rounding puts the carriageway somewhere it may not go can be put back as it was without
        /// disturbing its neighbours. The chain each corner is tested as reaches all the way to the
        /// vertex either side of it, which covers every segment the finished polyline can hold there
        /// — a neighbour's own rounding only ever cuts those two segments shorter.
        /// </para>
        /// </remarks>
        List<Vec2> RoundCorners(List<Vec2> points, float width)
        {
            if (points.Count <= 2)
            {
                return points;
            }

            var sharp = new bool[points.Count];
            var owner = new List<int>(points.Count * 4);

            for (int pass = 0; pass < RoundingPasses; pass++)
            {
                List<Vec2> rounded = Assemble(points, width, sharp, owner);

                int bad = -1;
                for (int i = 1; i < rounded.Count && bad < 0; i++)
                {
                    if (!CorridorClear(rounded[i - 1], rounded[i], width))
                    {
                        bad = i;
                    }
                }

                if (bad < 0)
                {
                    return rounded;
                }

                // A corner is tested against the vertices either side of it, which is a superset of
                // the segments it ends up between once its neighbours have been rounded too — and a
                // superset that is clear does not prove a piece of itself is. So the assembled line
                // is re-tested as it will be drawn, and a segment that fails puts its own corners
                // back. Reverting only ever shortens a segment towards one the simplify pass already
                // accepted, so this settles rather than chasing itself.
                if (!Revert(sharp, owner, bad - 1) & !Revert(sharp, owner, bad))
                {
                    break;
                }
            }

            return points;
        }

        static bool Revert(bool[] sharp, List<int> owner, int point)
        {
            int corner = owner[point];
            if (corner == NotACorner || sharp[corner])
            {
                return false;
            }

            sharp[corner] = true;
            return true;
        }

        /// <summary>
        /// Builds the polyline, rounding every corner not already given up on, and records which
        /// corner each point came from.
        /// </summary>
        List<Vec2> Assemble(List<Vec2> points, float width, bool[] sharp, List<int> owner)
        {
            var rounded = new List<Vec2>(points.Count * 4);
            var corner = new Vec2[4];

            owner.Clear();
            rounded.Add(points[0]);
            owner.Add(NotACorner);

            for (int i = 1; i < points.Count - 1; i++)
            {
                if (!sharp[i])
                {
                    Chaikin(points[i - 1], points[i], points[i + 1], corner);
                    if (CornerClear(points[i - 1], corner, points[i + 1], width))
                    {
                        for (int c = 0; c < corner.Length; c++)
                        {
                            rounded.Add(corner[c]);
                            owner.Add(i);
                        }

                        continue;
                    }

                    sharp[i] = true;
                }

                rounded.Add(points[i]);
                owner.Add(NotACorner);
            }

            rounded.Add(points[points.Count - 1]);
            owner.Add(NotACorner);
            return rounded;
        }

        static void Chaikin(Vec2 previous, Vec2 corner, Vec2 next, Vec2[] into)
        {
            Vec2 back = Cut(corner, previous);
            Vec2 forward = Cut(corner, next);
            into[0] = Cut(back, previous);
            into[1] = Cut(back, forward);
            into[2] = Cut(forward, back);
            into[3] = Cut(forward, next);
        }

        static Vec2 Cut(Vec2 from, Vec2 towards) => new Vec2(
            from.X + (towards.X - from.X) * 0.25f,
            from.Y + (towards.Y - from.Y) * 0.25f);

        bool CornerClear(Vec2 previous, Vec2[] corner, Vec2 next, float width)
        {
            if (!CorridorClear(previous, corner[0], width))
            {
                return false;
            }

            for (int i = 1; i < corner.Length; i++)
            {
                if (!CorridorClear(corner[i - 1], corner[i], width))
                {
                    return false;
                }
            }

            return CorridorClear(corner[corner.Length - 1], next, width);
        }

        /// <summary>
        /// What one cell costs before any road has been laid: ten, plus the slope, plus how open it
        /// is, less the discount a lane gap carries.
        /// </summary>
        /// <remarks>
        /// The exposure term is what makes a route prefer ground with something beside it, which is
        /// how a street ends up running past the fronts of buildings rather than across the middle
        /// of an empty field. The lane-gap discount is what makes a gap the natural line for a way
        /// through without anything having to draw one there.
        /// </remarks>
        int NaturalCost(int cell)
        {
            Vec2 centre = CellCentre(cell);
            if (!_open.Contains(centre))
            {
                return Impassable;
            }

            float gradient = Gradient(cell);
            if (gradient > _maxGradient)
            {
                return Impassable;
            }

            int cost = BaseCellCost +
                       (int)(GradientPenalty * (gradient / _maxGradient)) +
                       (int)(ExposurePenalty * _shelter[cell]);

            return InLaneGap(centre) ? cost - LaneGapDiscount : cost;
        }

        /// <summary>Steepest slope of the ground at a point, as rise over run.</summary>
        /// <remarks>
        /// <para>
        /// The steepest of the four one-sided differences to the neighbouring cells, not the central
        /// difference across them. A central difference is the slope of a plane fitted through the
        /// point, and it reads a ridge running exactly through a cell as flat ground because the rise
        /// on one side cancels the fall on the other. What the limit is about is the climb a route
        /// actually makes stepping out of this cell, and that is the largest of the four.
        /// </para>
        /// <para>
        /// It is also what makes the property testable as stated: with each step bounded, the rise
        /// over the run of a whole segment is bounded by the same number, where a central difference
        /// leaves a segment steeper than any cell it crosses.
        /// </para>
        /// <para>
        /// Rise over run and never an angle, for the reason
        /// <see cref="ArenaParams.MaxRoadGradient"/> is stated that way.
        /// </para>
        /// </remarks>
        float Gradient(int cell)
        {
            int x = cell % _countX;
            int z = cell / _countX;
            float here = _height[cell];

            float steepest = MathF.Abs(NeighbourHeight(x + 1, z, here) - here);
            steepest = MathF.Max(steepest, MathF.Abs(NeighbourHeight(x - 1, z, here) - here));
            steepest = MathF.Max(steepest, MathF.Abs(NeighbourHeight(x, z + 1, here) - here));
            steepest = MathF.Max(steepest, MathF.Abs(NeighbourHeight(x, z - 1, here) - here));

            return steepest / _cellSize;
        }

        /// <summary>
        /// The height of a neighbouring cell, asked of the ground directly where the neighbour is
        /// off the grid.
        /// </summary>
        float NeighbourHeight(int x, int z, float here)
        {
            if (x >= 0 && x < _countX && z >= 0 && z < _countZ)
            {
                return _height[z * _countX + x];
            }

            // Off the grid there is no cell to have measured, but the ground is still defined there
            // and a cell on the edge is as entitled to a slope as one in the middle.
            return _terrain.HeightAt(new Vec2(
                _originX + (x + 0.5f) * _cellSize, _originZ + (z + 0.5f) * _cellSize));
        }

        bool InLaneGap(Vec2 at)
        {
            for (int i = 0; i < _gaps.Count; i++)
            {
                if (_gaps[i].Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How open every cell is, from nothing beside it at one to something against it at zero.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Distance to the nearest thing on the map rather than the fraction of observers who can
        /// see it, which is what <see cref="ExposureMap"/> measures and cannot be measured here — a
        /// sightline model needs each object's own footprint and height, which is a catalog, and a
        /// router is handed a document. Distance to the nearest cover is the half of the same idea
        /// that the placements alone can answer, and it is the half a road cares about: a street
        /// wants something along it.
        /// </para>
        /// <para>
        /// <strong>Nothing placed after the roads is part of it, and that is what makes a network a
        /// function of a document.</strong> The roads are laid before the kerbs are edged along them
        /// and before cover is scattered — see <see cref="ArenaLayoutGenerator.Generate"/> — so
        /// neither is something a road could have been routed past. Counting one would mean a
        /// network rebuilt from a finished document differed from the network its own cover was
        /// placed around, and the reservation the cover respects would move under it. See
        /// <see cref="IsPlacedAfterTheRoads"/>.
        /// </para>
        /// </remarks>
        float[] Shelter(IReadOnlyList<PlacedObject> committed)
        {
            var nearest = new float[_countX * _countZ];
            for (int i = 0; i < nearest.Length; i++)
            {
                nearest[i] = RoadNetwork.ShelterReach;
            }

            for (int i = 0; i < committed.Count; i++)
            {
                PlacedObject placed = committed[i];
                if (placed == null || IsPlacedAfterTheRoads(placed))
                {
                    continue;
                }

                // A structure shelters from its whole pad; everything else is small enough beside a
                // cell that where its pivot stands is where it stands.
                Vec2 at = placed.Pose.Position.Xz;
                Rect2 area = new Rect2(at.X, at.Y, at.X, at.Y);
                if (placed.Metadata.ContainsKey(ArenaLayoutGenerator.DoorwayCountKey) &&
                    placed.Metadata.TryGetValue(
                        ArenaLayoutGenerator.FoundationKey, out string padText) &&
                    RectMetadata.TryParse(padText, out Rect2 pad) &&
                    pad.Width > 0f && pad.Depth > 0f)
                {
                    area = pad;
                }

                Rect2 reach = area.Expanded(RoadNetwork.ShelterReach);
                int minX = ClampX((int)MathF.Floor((reach.MinX - _originX) / _cellSize));
                int maxX = ClampX((int)MathF.Floor((reach.MaxX - _originX) / _cellSize));
                int minZ = ClampZ((int)MathF.Floor((reach.MinZ - _originZ) / _cellSize));
                int maxZ = ClampZ((int)MathF.Floor((reach.MaxZ - _originZ) / _cellSize));

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int cell = z * _countX + x;
                        float distance = DistanceTo(area, CellCentre(cell));
                        if (distance < nearest[cell])
                        {
                            nearest[cell] = distance;
                        }
                    }
                }
            }

            for (int i = 0; i < nearest.Length; i++)
            {
                nearest[i] /= RoadNetwork.ShelterReach;
            }

            return nearest;
        }

        /// <summary>
        /// True for everything a map puts down after its roads are laid, which is everything the
        /// router may not measure.
        /// </summary>
        /// <remarks>
        /// Three kinds of art, and the last two are there for the first one's reason. Cover is
        /// scattered after the network — see <see cref="ArenaLayoutGenerator.Generate"/> — so a
        /// crate is not something a road could have been routed past; and a kerb and a piece of
        /// street furniture are placed <em>from</em> a network, so counting either would route the
        /// roads round what the last routing put along them. Any of the three would mean a network
        /// rebuilt from a finished document differed from the network its own cover was placed
        /// around, and the reservation the cover respects would move under it.
        /// </remarks>
        static bool IsPlacedAfterTheRoads(PlacedObject placed)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (string.Equals(placed.Tags[i], CoverPlacer.CoverTag, StringComparison.Ordinal) ||
                    string.Equals(placed.Tags[i], RoadKerbs.KerbTag, StringComparison.Ordinal) ||
                    string.Equals(
                        placed.Tags[i], RoadFurniture.FurnitureTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static float DistanceTo(Rect2 rect, Vec2 point)
        {
            float outX = MathF.Max(MathF.Max(rect.MinX - point.X, point.X - rect.MaxX), 0f);
            float outZ = MathF.Max(MathF.Max(rect.MinZ - point.Y, point.Y - rect.MaxZ), 0f);
            return MathF.Sqrt(outX * outX + outZ * outZ);
        }

        /// <summary>Shuts every cell a rectangle touches.</summary>
        void Shut(Rect2 area)
        {
            Span(area, out int minX, out int maxX, out int minZ, out int maxZ);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    _cost[z * _countX + x] = Impassable;
                }
            }
        }

        /// <summary>
        /// Puts every cell a rectangle touches back to what the ground alone would cost it, unless
        /// the cell is on a structure's pad.
        /// </summary>
        /// <remarks>
        /// This is what lets a doorway's approach through the clearance ring round its own building
        /// and through anything else's, while never opening the ground a building stands on. Ground
        /// too steep to grade and ground inside the playfield margin stay shut, because
        /// <see cref="NaturalCost"/> is what is recomputed and those are its own answers.
        /// </remarks>
        void Open(Rect2 area)
        {
            Span(area, out int minX, out int maxX, out int minZ, out int maxZ);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = z * _countX + x;
                    if (!OnAPad(CellCentre(cell)))
                    {
                        _cost[cell] = NaturalCost(cell);
                    }
                }
            }
        }

        bool OnAPad(Vec2 at)
        {
            for (int i = 0; i < _structures.Count; i++)
            {
                if (_structures[i].Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        void Span(Rect2 area, out int minX, out int maxX, out int minZ, out int maxZ)
        {
            minX = ClampX((int)MathF.Floor((area.MinX - _originX) / _cellSize));
            maxX = ClampX((int)MathF.Floor((area.MaxX - _originX) / _cellSize));
            minZ = ClampZ((int)MathF.Floor((area.MinZ - _originZ) / _cellSize));
            maxZ = ClampZ((int)MathF.Floor((area.MaxZ - _originZ) / _cellSize));
        }

        int ClampX(int x) => x < 0 ? 0 : x >= _countX ? _countX - 1 : x;

        int ClampZ(int z) => z < 0 ? 0 : z >= _countZ ? _countZ - 1 : z;

        /// <summary>True if the climb from one cell to another is steeper than a road may be graded.</summary>
        bool TooSteep(int from, int to)
        {
            float run = Vec2.Distance(CellCentre(from), CellCentre(to));
            return run > 0f && MathF.Abs(_height[to] - _height[from]) > _maxGradient * run;
        }

        bool Passable(int cell) =>
            cell >= 0 && cell < _cost.Length && _cost[cell] != Impassable &&
            (_mainRegion < 0 || _region[cell] == _mainRegion);

        bool Passable(int x, int z) =>
            x >= 0 && x < _countX && z >= 0 && z < _countZ && Passable(z * _countX + x);

        /// <summary>
        /// Numbers the pieces of open ground and returns the largest, or -1 if there are none.
        /// </summary>
        /// <remarks>
        /// Four-connected, because the router will not cut a diagonal between two shut cells either
        /// and a region joined only through a corner is not one a road can cross. Scanned in cell
        /// order and filled from a stack, so the numbering and therefore the answer are the same
        /// every run; the largest wins and the lowest label breaks a tie.
        /// </remarks>
        int LabelRegions()
        {
            for (int cell = 0; cell < _region.Length; cell++)
            {
                _region[cell] = -1;
            }

            var frontier = new Stack<int>();
            int label = 0;
            int largest = -1;
            int largestSize = 0;

            for (int seed = 0; seed < _cost.Length; seed++)
            {
                if (_cost[seed] == Impassable || _region[seed] >= 0)
                {
                    continue;
                }

                int size = 0;
                _region[seed] = label;
                frontier.Push(seed);

                while (frontier.Count > 0)
                {
                    int cell = frontier.Pop();
                    size++;
                    int x = cell % _countX;
                    int z = cell / _countX;

                    Spread(x - 1, z, label, frontier);
                    Spread(x + 1, z, label, frontier);
                    Spread(x, z - 1, label, frontier);
                    Spread(x, z + 1, label, frontier);
                }

                if (size > largestSize)
                {
                    largestSize = size;
                    largest = label;
                }

                label++;
            }

            return largest;
        }

        void Spread(int x, int z, int label, Stack<int> frontier)
        {
            if (x < 0 || x >= _countX || z < 0 || z >= _countZ)
            {
                return;
            }

            int cell = z * _countX + x;
            if (_cost[cell] == Impassable || _region[cell] >= 0)
            {
                return;
            }

            _region[cell] = label;
            frontier.Push(cell);
        }

        /// <summary>The octile distance to the goal, priced at the cheapest cell on the grid.</summary>
        /// <remarks>
        /// Admissible by construction: no step can cost less than the cheapest cell times its own
        /// multiplier, and a turn only ever adds. Pricing it at the cheapest cell rather than at the
        /// base cost is what keeps it admissible once braiding has quartered a road — an estimate
        /// that assumed ordinary ground would overshoot the cost of running along one, and A* with
        /// an inadmissible estimate returns whatever it finds first.
        /// </remarks>
        int Heuristic(int cell, int goal, int scale)
        {
            int acrossX = Math.Abs(cell % _countX - goal % _countX);
            int acrossZ = Math.Abs(cell / _countX - goal / _countX);
            int longer = acrossX > acrossZ ? acrossX : acrossZ;
            int shorter = acrossX > acrossZ ? acrossZ : acrossX;
            return (OrthogonalStep * longer + (DiagonalStep - OrthogonalStep) * shorter) * scale;
        }

        List<int> Reconstruct(int start, int goal)
        {
            var cells = new List<int>();
            int at = goal;

            while (at >= 0)
            {
                cells.Add(at);
                if (at == start)
                {
                    break;
                }

                at = _from[at];
            }

            cells.Reverse();
            return cells;
        }

        static bool Before(Frontier a, Frontier b)
        {
            if (a.Estimate != b.Estimate)
            {
                return a.Estimate < b.Estimate;
            }

            return a.Heuristic != b.Heuristic ? a.Heuristic < b.Heuristic : a.Cell < b.Cell;
        }

        void Push(Frontier entry)
        {
            _heap.Add(entry);
            int child = _heap.Count - 1;

            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (!Before(_heap[child], _heap[parent]))
                {
                    break;
                }

                Frontier held = _heap[parent];
                _heap[parent] = _heap[child];
                _heap[child] = held;
                child = parent;
            }
        }

        Frontier Pop()
        {
            Frontier top = _heap[0];
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);

            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= _heap.Count)
                {
                    break;
                }

                int best = left;
                int right = left + 1;
                if (right < _heap.Count && Before(_heap[right], _heap[left]))
                {
                    best = right;
                }

                if (!Before(_heap[best], _heap[parent]))
                {
                    break;
                }

                Frontier held = _heap[parent];
                _heap[parent] = _heap[best];
                _heap[best] = held;
                parent = best;
            }

            return top;
        }
    }
}
