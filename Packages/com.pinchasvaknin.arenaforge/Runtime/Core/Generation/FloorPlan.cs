using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// One leaf of a floor's partition: a room to furnish, or a corridor to leave clear.
    /// </summary>
    /// <remarks>
    /// <see cref="Bounds"/> is the region the partition gave the leaf, not the clear floor inside
    /// it: a wall straddles every edge, so half a thickness of it stands in the room. A caller
    /// placing something in a room pulls in by a whole wall thickness and is clear with room to
    /// spare. Keeping the regions rather than the clear floor is what makes the leaves tile the
    /// storey exactly.
    /// </remarks>
    public sealed class PlanRoom
    {
        static readonly Rect2[] NoPaths = Array.Empty<Rect2>();

        readonly Rect2[] _paths;

        /// <summary>Creates a leaf.</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> is blank.</exception>
        public PlanRoom(string id, Rect2 bounds, bool isCorridor)
            : this(id, bounds, isCorridor, NoPaths)
        {
        }

        /// <summary>Creates a leaf with a walkway reserved across it.</summary>
        /// <exception cref="ArgumentException"><paramref name="id"/> is blank.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="paths"/> is null.</exception>
        public PlanRoom(string id, Rect2 bounds, bool isCorridor, IReadOnlyList<Rect2> paths)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A room needs an id.", nameof(id));
            }

            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            Id = id;
            Bounds = bounds;
            IsCorridor = isCorridor;

            _paths = new Rect2[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                _paths[i] = paths[i];
            }
        }

        /// <summary>Id segment this leaf contributes to a stable id — <c>room_02</c>, <c>corridor_03</c>.</summary>
        public string Id { get; }

        /// <summary>The rectangle its walls' centrelines enclose.</summary>
        public Rect2 Bounds { get; }

        /// <summary>
        /// True if this leaf is circulation rather than a room.
        /// </summary>
        /// <remarks>
        /// A leaf is a corridor when it comes out long and thin — the shape a partition leaves
        /// when it cuts a strip off the side of a region rather than halving it. Nothing else
        /// distinguishes the two: a corridor is a leaf of the same partition, told apart by its
        /// proportions and then left clear, because circulation you cannot walk down is a room.
        /// </remarks>
        public bool IsCorridor { get; }

        /// <summary>
        /// Strips of this room's floor that must stay clear, joining every opening on its walls to
        /// every other and to the stairwell.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rule these express is absolute and it is the one thing a generated interior has to
        /// get right: you can walk from any door of a room to any other door of it. Everything
        /// else a room holds is decoration in the literal sense — a map with too little cover in
        /// it is a poor map, and a map with a crate wedged across the only route between two doors
        /// is a broken one, because the route is what the building is <em>for</em>.
        /// </para>
        /// <para>
        /// Overlapping rectangles rather than a merged region or a grid of cells. A candidate is
        /// tested by asking whether its footprint meets any of them, which is the same question
        /// <see cref="ConstraintKind.NoOverlap"/> already asks of everything committed, so the rule
        /// costs a loop over a handful of rectangles and no new machinery. Merging them into
        /// disjoint pieces would buy a tidier list and the same answer.
        /// </para>
        /// <para>
        /// Empty when the room has fewer than two things to join. One door and no stairwell is not
        /// a passageway, and reserving a strip of floor leading nowhere would take the room's
        /// middle away from the cover that is supposed to stand in it. The clearance every doorway
        /// already keeps in front of itself is what makes that single door usable.
        /// </para>
        /// </remarks>
        public IReadOnlyList<Rect2> Paths => _paths;
    }

    /// <summary>
    /// A straight run of wall, measured in whole modules, with at most one of them a doorway.
    /// </summary>
    /// <remarks>
    /// Modules rather than metres because a wall is art the catalog supplies at a fixed length: a
    /// run is tiled from that piece and never stretched along its own travel to fit. The partition
    /// is laid out in module units for the same reason, so every run is a whole number of them and
    /// a doorway is one module wide. What the generator does fit is the other axis — a piece is
    /// stretched vertically onto the ceiling, which is a fact about the storey's height and touches
    /// nothing here.
    /// </remarks>
    public readonly struct PlanWall
    {
        /// <summary>The module index used by a run with no doorway in it.</summary>
        public const int NoDoorway = -1;

        /// <summary>Creates a run.</summary>
        public PlanWall(Vec2 from, bool alongX, float module, int modules, int doorway)
        {
            From = from;
            AlongX = alongX;
            Module = module;
            Modules = modules;
            Doorway = doorway;
        }

        /// <summary>Low end of the run's centreline.</summary>
        public Vec2 From { get; }

        /// <summary>True if the run travels along world X, false if along world Z.</summary>
        public bool AlongX { get; }

        /// <summary>Length of one module, in metres.</summary>
        public float Module { get; }

        /// <summary>How many modules the run is tiled from.</summary>
        public int Modules { get; }

        /// <summary>Which module is a doorway, or <see cref="NoDoorway"/>.</summary>
        public int Doorway { get; }

        /// <summary>Length of the whole run, in metres.</summary>
        public float Length => Module * Modules;

        /// <summary>High end of the run's centreline.</summary>
        public Vec2 To => AlongX
            ? new Vec2(From.X + Length, From.Y)
            : new Vec2(From.X, From.Y + Length);

        /// <summary>Centre of one module, on the centreline.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a module of this run.</exception>
        public Vec2 ModuleCenter(int index)
        {
            if (index < 0 || index >= Modules)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"This run is {Modules} module(s) long.");
            }

            float along = (index + 0.5f) * Module;
            return AlongX ? new Vec2(From.X + along, From.Y) : new Vec2(From.X, From.Y + along);
        }
    }

    /// <summary>
    /// How one storey is divided: the rooms and corridors it holds, and the walls between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A binary space partition, in module units. The floor starts as one region and is split in
    /// two along its longer axis at a cut line drawn from the stream; each half is split again
    /// until it is too small to be worth dividing. The leaves are the rooms, and every cut is a
    /// wall with one module of it taken out for a doorway — which is what makes the whole floor
    /// connected without a reachability pass to prove it. A BSP tree has one path between any two
    /// leaves, and putting a door in every cut opens all of them.
    /// </para>
    /// <para>
    /// The partition is in modules rather than metres because walls are art: a run is tiled from a
    /// catalog piece at its own length and never stretched along the run, so a cut that did not land
    /// on a module boundary would leave a run that cannot be built. Everything downstream — doorway
    /// width, room size, the floor slab — is a whole number of them. Nothing in this file has a
    /// height in it, so the vertical fitting in <see cref="BuildingGenerator"/> is not a thing the
    /// partition has to know about.
    /// </para>
    /// <para>
    /// One rectangle can be reserved — the stairwell — and the partition works around it: no cut
    /// crosses it, and no run puts its doorway where the doorway would open onto it. That is the
    /// only thing every storey of a building has in common, and it arrives as an argument rather
    /// than as anything one floor tells another. See <see cref="Reserved"/>.
    /// </para>
    /// <para>
    /// This is geometry, not placement. Nothing here consults a catalog, a constraint set or a
    /// placement grid; it takes a rectangle and a module size and returns where the walls go. What
    /// stands inside the rooms is still placed under the rules in <see cref="ConstraintSet"/>,
    /// exactly as a lane's cover is.
    /// </para>
    /// </remarks>
    public sealed class FloorPlan
    {
        /// <summary>Shortest side a leaf may have, in metres.</summary>
        /// <remarks>
        /// <para>
        /// A distance rather than a count of modules, because it is a fact about the person walking
        /// through the room and not about the art the walls are tiled from: two and a half metres
        /// is about the narrowest space that reads as somewhere to be rather than as somewhere to
        /// pass through. A rule stated in modules would mean four metres on one art pack and one
        /// and a half on another, which is the same rule saying two different things.
        /// </para>
        /// <para>
        /// It is checked <em>before</em> a cut is made rather than after: a split that would leave
        /// either half narrower than this is not made at all, and the region stays one larger room.
        /// See <see cref="ModulesPerRoom"/> for how it reaches the partition, and
        /// <see cref="Partition.Subdivide"/> for what refusing a cut does.
        /// </para>
        /// </remarks>
        public const float MinRoomSide = 2.5f;

        /// <summary>A region of fewer module cells than this is a leaf, whatever its shape.</summary>
        /// <remarks>
        /// This, rather than a recursion depth cap, is what sets how finely a floor is divided:
        /// depth would divide a large floor and a small one into the same number of rooms, and it
        /// is the size of a room that a person notices.
        /// </remarks>
        const int MinSplitModules = 10;

        /// <summary>Widest a leaf may be, in modules, and still be circulation rather than a room.</summary>
        /// <remarks>
        /// A leaf this narrow only arises on an art pack whose wall module is itself at least
        /// <see cref="MinRoomSide"/> long: no cut may leave a region shorter than that, so on a
        /// two-metre module the narrowest leaf is two modules and nothing is circulation. That is
        /// the minimum doing its job rather than this constant being dead — what a person called a
        /// corridor when the partition could leave one-module strips was, at two metres across, a
        /// room they could not use.
        /// </remarks>
        const int CorridorModules = 1;

        /// <summary>How many times longer than it is wide a corridor must be.</summary>
        const int CorridorAspect = 2;

        /// <summary>How many ways in the ground floor is given.</summary>
        /// <remarks>
        /// Two, as a rule of level design rather than as a consequence of anything geometric — see
        /// <see cref="Partition.AddEntrances"/>. Nothing in the partition reads it: it is the
        /// number a caller checking a finished plan measures against, stated here so the caller and
        /// the partition cannot come to disagree about it.
        /// </remarks>
        public const int Entrances = 2;

        /// <summary>
        /// How far apart two ways in must be, as a share of the shell's diagonal.
        /// </summary>
        /// <remarks>
        /// Half, which is a rule about the two doors and not about which walls they are in. Two
        /// doors facing each other across the narrow way of a long building clear it comfortably;
        /// two a few modules apart round a corner do not, and neither do two on one wall. Stated as
        /// a share of the diagonal so it means the same thing on a shell of any size: an absolute
        /// distance would forbid every second door in a small building and permit adjacent ones in
        /// a large one.
        /// </remarks>
        const float EntranceSeparation = 0.5f;

        /// <summary>Slack allowed when counting whole modules into a footprint.</summary>
        const float FitTolerance = 1e-3f;

        /// <summary>How wide a reserved walkway is, in metres. See <see cref="PlanRoom.Paths"/>.</summary>
        /// <remarks>
        /// Wide enough to walk down and no wider. A route is reserved out of the same floor the
        /// room's contents are proposed onto, so every centimetre of it is a centimetre of cover
        /// that cannot be placed — and the rule is worth having precisely because it is cheap. A
        /// metre and a fifth clears a player and the shoulder-room to turn a corner without
        /// scraping, which is what the reservation is promising; making it as wide as the doorway
        /// it joins would take a two-metre strip out of rooms that are six metres across.
        /// </remarks>
        public const float PathWidth = 1.2f;

        /// <summary>How close a doorway's centre must be to a room's edge to be one of its doors.</summary>
        /// <remarks>
        /// A doorway sits on a wall's centreline and a room's bounds are the rectangle those
        /// centrelines enclose, so the two coincide exactly and this only has to absorb the float
        /// error in <c>MinX + count * module</c>. It is not a search radius: a tolerance loose
        /// enough to catch a doorway that is genuinely elsewhere would reserve a route to a door
        /// in another room, across a wall.
        /// </remarks>
        const float OnEdgeTolerance = 1e-3f;

        readonly List<PlanRoom> _rooms;
        readonly List<PlanWall> _walls;

        FloorPlan(
            Rect2 bounds,
            float module,
            float thickness,
            Rect2 reserved,
            float doorwayClearance,
            List<PlanRoom> rooms,
            List<PlanWall> walls)
        {
            Bounds = bounds;
            Module = module;
            Thickness = thickness;
            Reserved = reserved;
            DoorwayClearance = doorwayClearance;
            _rooms = rooms;
            _walls = walls;
        }

        /// <summary>
        /// The rectangle the walls' <em>centrelines</em> enclose. A whole number of modules on
        /// both axes, centred in the area the plan was built over.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Centrelines rather than outer faces, so that <em>every</em> run in the plan straddles
        /// its own line — the outside ones as much as the cuts. That is what makes a junction
        /// safe to look at: a run that arrives at another ends half a thickness inside it rather
        /// than flush with its far face, so the two boxes overlap without a single pair of
        /// vertical faces landing on the same plane. Two coplanar faces with the same normal are
        /// what a renderer cannot order, and what flickers as the camera moves.
        /// </para>
        /// <para>
        /// The shell therefore reaches half a thickness past this rectangle on every side, which
        /// <see cref="OuterBounds"/> is. <see cref="Build"/> counts the modules into the area
        /// <em>minus</em> a whole thickness so that outer rectangle still fits inside the
        /// footprint the building declares — the footprint an exported building is placed in an
        /// arena by.
        /// </para>
        /// </remarks>
        public Rect2 Bounds { get; }

        /// <summary>Length of one wall module, in metres. Zero when the plan has no walls.</summary>
        public float Module { get; }

        /// <summary>Thickness of one wall piece, in metres. Zero when the plan has no walls.</summary>
        public float Thickness { get; }

        /// <summary>
        /// A rectangle no cut was allowed to pass through, or an empty one when the floor reserved
        /// nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is how a stairwell survives being partitioned. A shaft has to be at the same place
        /// on every storey or it is not a stairwell, and every storey draws its own partition from
        /// its own seed — so what is shared between them is not the partition but this rectangle,
        /// which the caller picks once for the whole building and each floor's cuts then avoid.
        /// The shaft therefore falls inside a single leaf, whichever way that floor happened to be
        /// divided.
        /// </para>
        /// <para>
        /// What the caller reserves is the <em>opening</em> — the hole cut in the slab — rather
        /// than the flight standing in it. A wall across the part of the shaft the stairs do not
        /// occupy would be a wall standing over a hole.
        /// </para>
        /// <para>
        /// Half a wall thickness is added on top of it, because a cut is a centreline and the piece
        /// standing on it reaches either side.
        /// </para>
        /// </remarks>
        public Rect2 Reserved { get; }

        /// <summary>
        /// How much floor is kept between the reservation and any doorway, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Separate from <see cref="Reserved"/>, and worth the second number rather than one
        /// rectangle grown to cover both. A wall running alongside a stairwell is fine and often
        /// what you want; it is only a <em>doorway</em> in that wall that is a problem, because
        /// walking through it puts you into the shaft. Reserving the clearance against every cut
        /// instead would keep the walls that far off the shaft as well — which pushes every cut of
        /// the region holding it out to the extremes, and a floor of one-module strips is a floor
        /// of corridors.
        /// </para>
        /// <para>
        /// The rule it feeds is absolute: a doorway inside this zone is never emitted. Nothing
        /// this tool can read says which end of a flight is its top, so there is no such thing as
        /// a doorway that opens onto the landing rather than onto the drop — and a door onto the
        /// drop is a hole in the floor with a frame round it. A cut whose every module is inside
        /// the zone is therefore not made at all, and the region it would have divided stays one
        /// room. See <see cref="Build"/>.
        /// </para>
        /// </remarks>
        public float DoorwayClearance { get; }

        /// <summary>The rectangle the outside walls' outer faces enclose.</summary>
        public Rect2 OuterBounds => Bounds.Expanded(Thickness * 0.5f);

        /// <summary>The leaves, in the order the partition produced them.</summary>
        public IReadOnlyList<PlanRoom> Rooms => _rooms;

        /// <summary>
        /// The wall runs: the four sides of the floor first, then one per cut, outermost first.
        /// </summary>
        public IReadOnlyList<PlanWall> Walls => _walls;

        /// <summary>
        /// How many runs at the head of <see cref="Walls"/> are the outside of the building.
        /// </summary>
        /// <remarks>
        /// A named number because two callers now depend on it and neither of them is this file:
        /// the generator only puts windows in runs below this index, and the suite that checks no
        /// cut crosses the stairwell allows the straddle only for these. Reading it off the list
        /// order is what the order is <em>for</em> — the four sides are added before any cut, and a
        /// wall's position in the list is part of its stable id.
        /// </remarks>
        public const int PerimeterRuns = 4;

        /// <summary>
        /// The plan of a floor nothing divides: one room over the whole area, and no walls.
        /// </summary>
        /// <remarks>
        /// What a catalog with no wall art gets, and what an area too small to hold one module
        /// gets. A floor with nothing to build its partitions out of is one open room rather than
        /// a refusal — the building still stands up, it just has no interior.
        /// </remarks>
        public static FloorPlan Single(Rect2 area) => new FloorPlan(
            area,
            0f,
            0f,
            Rect2.Zero,
            0f,
            new List<PlanRoom> { new PlanRoom(RoomId(0, false), area, false) },
            new List<PlanWall>());

        /// <summary>
        /// Partitions <paramref name="area"/> into rooms, corridors and the walls between them.
        /// </summary>
        /// <param name="area">The building's footprint. The shell is fitted inside it, never past it.</param>
        /// <param name="module">Length of one wall piece, in metres.</param>
        /// <param name="thickness">Thickness of one wall piece, in metres.</param>
        /// <param name="withEntrance">Whether to take one module out of an outside wall as a way in.</param>
        /// <param name="reserved">
        /// A rectangle no cut may pass through — a stairwell — or an empty one. See
        /// <see cref="Reserved"/>.
        /// </param>
        /// <param name="doorwayClearance">
        /// How much floor to keep between the reservation and any doorway, in metres. See
        /// <see cref="DoorwayClearance"/>.
        /// </param>
        /// <param name="rng">The stream cut positions and doorways are drawn from.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="module"/> is not positive.</exception>
        public static FloorPlan Build(
            Rect2 area,
            float module,
            float thickness,
            bool withEntrance,
            Rect2 reserved,
            float doorwayClearance,
            ref Rng rng)
        {
            if (!(module > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(module), module, "A wall module must be positive.");
            }

            if (!TryCountModules(area, module, thickness, out int countX, out int countZ))
            {
                return Single(area);
            }

            Rect2 bounds = BoundsOf(area, module, thickness);

            var leaves = new List<Cell>();
            var cuts = new List<Cut>();
            var partition = new Partition(
                bounds, module, thickness * 0.5f, reserved, doorwayClearance, ModulesPerRoom(module),
                leaves, cuts);
            partition.Subdivide(new Cell(0, 0, countX, countZ), ref rng);

            var walls = new List<PlanWall>(4 + cuts.Count);
            partition.AddPerimeter(walls, countX, countZ, withEntrance, ref rng);
            for (int i = 0; i < cuts.Count; i++)
            {
                walls.Add(cuts[i].ToWall(bounds, module));
            }

            var rooms = new List<PlanRoom>(leaves.Count);
            for (int i = 0; i < leaves.Count; i++)
            {
                Cell leaf = leaves[i];
                Rect2 region = leaf.ToRect(bounds, module);
                rooms.Add(new PlanRoom(
                    RoomId(i, leaf.IsCorridor),
                    region,
                    leaf.IsCorridor,
                    Walkways(region, walls, thickness, reserved)));
            }

            return new FloorPlan(bounds, module, thickness, reserved, doorwayClearance, rooms, walls);
        }

        /// <summary>
        /// The rectangle a shell's wall centrelines will enclose inside <paramref name="area"/>,
        /// or the area itself when it cannot hold one whole module.
        /// </summary>
        /// <remarks>
        /// Public because the stairwell has to know it without building a plan. Where a storey's
        /// slab tiles is a fact about that rectangle, and where the hole for the stairs goes has to
        /// be a fact about the <em>building</em> — so the caller works this out once, from the
        /// module a floor is most likely to draw, rather than each floor deciding for itself. See
        /// <see cref="BuildingGenerator.PlanStairwell"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="module"/> is not positive.</exception>
        public static Rect2 BoundsOf(Rect2 area, float module, float thickness)
        {
            if (!(module > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(module), module, "A wall module must be positive.");
            }

            if (!TryCountModules(area, module, thickness, out int countX, out int countZ))
            {
                return area;
            }

            // Centred rather than anchored at the low corner, so a footprint that is not a whole
            // number of modules across leaves the same sliver on both sides instead of all of it
            // against one wall.
            return Rect2.FromCenterSize(area.Center, new Vec2(countX * module, countZ * module));
        }

        /// <summary>
        /// How many whole modules fit across an area, or false when it cannot hold one.
        /// </summary>
        /// <remarks>
        /// Counted into the area less one whole thickness, because every run straddles its
        /// centreline: the outside ones stand half in and half out of the rectangle the modules
        /// tile, so that rectangle has to leave them the other half. See <see cref="Bounds"/> for
        /// why they straddle rather than being tucked inside.
        /// <para>
        /// The tolerance is there because the common case is a footprint that is an exact multiple
        /// of the module, and a float division of two such numbers lands just below the integer as
        /// often as just above it — which would silently drop a whole module off one side of the
        /// building.
        /// </para>
        /// </remarks>
        static bool TryCountModules(
            Rect2 area, float module, float thickness, out int countX, out int countZ)
        {
            countX = (int)MathF.Floor((area.Width - thickness) / module + FitTolerance);
            countZ = (int)MathF.Floor((area.Depth - thickness) / module + FitTolerance);
            return countX >= 1 && countZ >= 1;
        }

        /// <summary>
        /// <see cref="MinRoomSide"/> in whole modules: the shortest side a cut may leave behind.
        /// </summary>
        /// <remarks>
        /// Rounded up, so the rule is never weakened by the module a floor happened to draw, and
        /// held at one however long the module is — a region a single module across cannot be cut
        /// at all, and a minimum of zero would let a cut leave a region of no width.
        /// </remarks>
        public static int ModulesPerRoom(float module) =>
            Math.Max(1, (int)MathF.Ceiling(MinRoomSide / module - FitTolerance));

        static string RoomId(int index, bool corridor) =>
            (corridor ? "corridor_" : "room_") + index.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>
        /// The strips of one room's floor that have to stay clear: a route from each of its
        /// openings to the next, and on to the stairwell when the shaft is in this room.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A chain rather than a star through the middle of the room, and rather than a route
        /// between every pair. A chain over the doors joins all of them — that is what a connected
        /// graph is — and it does it with one fewer segment than there are doors, where every pair
        /// would reserve most of the floor and a star would reserve the exact middle, which is
        /// where a room's cover is supposed to stand.
        /// </para>
        /// <para>
        /// <strong>Each route leaves its door square on.</strong> The polyline steps from the
        /// doorway perpendicular to its own wall before it turns, so the strip in front of a door
        /// is the floor you actually walk on rather than a strip running along the wall beside it.
        /// Routing straight between two doorway centres would reserve a diagonal's worth of
        /// L-bend hugging two walls, taking the edges — where the decor goes — instead of the
        /// middle it crosses anyway.
        /// </para>
        /// <para>
        /// The stairwell is joined last and joined at its centre. What that reserves is the
        /// landing you step onto the flight from; the part of the strip lying inside the shaft
        /// itself costs nothing, because a shaft already refuses everything proposed into it.
        /// </para>
        /// <para>
        /// No draw is taken from any stream. Where a room's walkway runs is a fact about where its
        /// doors ended up, so two runs of one seed reserve the same floor — and adding this stage
        /// did not move a single existing placement by shifting a stream.
        /// </para>
        /// </remarks>
        static List<Rect2> Walkways(Rect2 room, List<PlanWall> walls, float thickness, Rect2 reserved)
        {
            var nodes = new List<Node>();

            for (int i = 0; i < walls.Count; i++)
            {
                PlanWall wall = walls[i];
                if (wall.Doorway == PlanWall.NoDoorway)
                {
                    continue;
                }

                Vec2 at = wall.ModuleCenter(wall.Doorway);
                if (IsOnEdge(room, at, wall.AlongX))
                {
                    nodes.Add(new Node(at, !wall.AlongX, true));
                }
            }

            // Last, so it is never the end a route sets off from and never has to say which way it
            // faces — a shaft is a hole in the floor rather than a gap in a wall, so it has no
            // direction of its own to leave by.
            if (reserved.Width > 0f && reserved.Depth > 0f && room.Overlaps(reserved))
            {
                nodes.Add(new Node(reserved.Center, false, false));
            }

            var paths = new List<Rect2>();
            if (nodes.Count < 2)
            {
                return paths;
            }

            float half = PathWidth * 0.5f;
            float step = thickness * 0.5f + half;

            for (int i = 0; i + 1 < nodes.Count; i++)
            {
                Node from = nodes[i];
                Node to = nodes[i + 1];

                Vec2 out_ = Inward(room, from, step);
                Vec2 in_ = Inward(room, to, step);
                Vec2 elbow = from.AlongX
                    ? new Vec2(in_.X, out_.Y)
                    : new Vec2(out_.X, in_.Y);

                Add(paths, room, from.At, out_, half);
                Add(paths, room, out_, elbow, half);
                Add(paths, room, elbow, in_, half);
                Add(paths, room, in_, to.At, half);
            }

            return paths;
        }

        /// <summary>
        /// Adds the strip covering one leg of a route, clipped to the room it belongs to.
        /// </summary>
        /// <remarks>
        /// Clipped because a room's reservation is floor in that room. A leg that starts at a
        /// doorway reaches half a walkway's width into the wall and out the far side, where it
        /// would be describing the neighbour's floor — and the neighbour reserves its own side of
        /// that same doorway anyway, from its own list. A leg of no length is dropped rather than
        /// reserved as a square of floor nothing crosses.
        /// </remarks>
        static void Add(List<Rect2> paths, Rect2 room, Vec2 from, Vec2 to, float half)
        {
            if (from.X == to.X && from.Y == to.Y)
            {
                return;
            }

            Rect2 strip = Rect2.FromCorners(from, to).Expanded(half);
            var clipped = new Rect2(
                MathF.Max(strip.MinX, room.MinX),
                MathF.Max(strip.MinZ, room.MinZ),
                MathF.Min(strip.MaxX, room.MaxX),
                MathF.Min(strip.MaxZ, room.MaxZ));

            if (clipped.Width > 0f && clipped.Depth > 0f)
            {
                paths.Add(clipped);
            }
        }

        /// <summary>The point a route reaches after stepping off a node into the room.</summary>
        /// <remarks>
        /// Towards the room's centre, which is the inside for a doorway on any of the four walls.
        /// A node with no facing — the stairwell — does not step at all: it is already in the
        /// middle of the floor rather than on the edge of it.
        /// </remarks>
        static Vec2 Inward(Rect2 room, Node node, float step)
        {
            if (!node.Faces)
            {
                return node.At;
            }

            Vec2 centre = room.Center;
            return node.AlongX
                ? new Vec2(node.At.X + MathF.Sign(centre.X - node.At.X) * step, node.At.Y)
                : new Vec2(node.At.X, node.At.Y + MathF.Sign(centre.Y - node.At.Y) * step);
        }

        /// <summary>True if a doorway on a run of the given direction opens into this room.</summary>
        /// <remarks>
        /// The doorway's centre has to sit on the edge the run is parallel to <em>and</em> within
        /// the span of the other one. The first alone would claim every doorway on a wall that
        /// runs past the room, including the ones opening into the rooms beyond it; the second
        /// alone would claim the doorways of a wall crossing the room, which is a wall this room
        /// is not on either side of.
        /// </remarks>
        static bool IsOnEdge(Rect2 room, Vec2 at, bool alongX)
        {
            if (alongX)
            {
                return (MathF.Abs(at.Y - room.MinZ) <= OnEdgeTolerance ||
                        MathF.Abs(at.Y - room.MaxZ) <= OnEdgeTolerance) &&
                       at.X >= room.MinX - OnEdgeTolerance && at.X <= room.MaxX + OnEdgeTolerance;
            }

            return (MathF.Abs(at.X - room.MinX) <= OnEdgeTolerance ||
                    MathF.Abs(at.X - room.MaxX) <= OnEdgeTolerance) &&
                   at.Y >= room.MinZ - OnEdgeTolerance && at.Y <= room.MaxZ + OnEdgeTolerance;
        }

        /// <summary>One end of a leg of a route: where it is, and which way it faces.</summary>
        readonly struct Node
        {
            public Node(Vec2 at, bool alongX, bool faces)
            {
                At = at;
                AlongX = alongX;
                Faces = faces;
            }

            /// <summary>The doorway's centre, or the stairwell's.</summary>
            public Vec2 At { get; }

            /// <summary>True if a route leaves this node along world X — a doorway in a wall running along Z.</summary>
            public bool AlongX { get; }

            /// <summary>
            /// True if a route has to step off this node before it may turn.
            /// </summary>
            /// <remarks>
            /// A doorway does: it is a gap in a wall, and the floor in front of it is only reached
            /// by walking out of it square on. A stairwell does not — it is a hole in the middle of
            /// the floor, reached from whichever side the route arrives on.
            /// </remarks>
            public bool Faces { get; }
        }

        /// <summary>
        /// Which module of a run of <paramref name="modules"/> is the doorway.
        /// </summary>
        /// <remarks>
        /// Away from both ends when the run is long enough to have a middle. A doorway in the last
        /// module of a run opens into the corner where two walls meet, which is a door you have to
        /// walk sideways through.
        /// </remarks>
        static int PickDoorway(int modules, ref Rng rng) => modules > 2
            ? rng.NextRange(1, modules - 1)
            : rng.NextRange(0, modules);

        /// <summary>
        /// The binary space partition itself: the region tree, the cuts it leaves, and the one
        /// rectangle those cuts have to work around.
        /// </summary>
        /// <remarks>
        /// A type rather than a static method with six parameters, because the four things the
        /// recursion carries — where the module grid sits, how thick a wall is, what is reserved,
        /// and the two lists it fills — are the same at every level and only the region changes.
        /// </remarks>
        readonly struct Partition
        {
            readonly Rect2 _bounds;
            readonly float _module;
            readonly float _halfThickness;
            readonly Rect2 _reserved;
            readonly float _clearance;
            readonly int _minRoom;
            readonly float _separation;
            readonly List<Cell> _leaves;
            readonly List<Cut> _cuts;

            public Partition(
                Rect2 bounds,
                float module,
                float halfThickness,
                Rect2 reserved,
                float clearance,
                int minRoom,
                List<Cell> leaves,
                List<Cut> cuts)
            {
                _bounds = bounds;
                _module = module;
                _halfThickness = halfThickness;
                _reserved = reserved;
                _clearance = clearance;
                _minRoom = minRoom;
                _separation = EntranceSeparation *
                    MathF.Sqrt(bounds.Width * bounds.Width + bounds.Depth * bounds.Depth);
                _leaves = leaves;
                _cuts = cuts;
            }

            bool HasReservation => _reserved.Width > 0f && _reserved.Depth > 0f;

            /// <summary>
            /// The four outside walls, and the two modules of them the ways in come out of.
            /// </summary>
            /// <remarks>
            /// <para>
            /// On the edges of <see cref="Bounds"/> rather than tucked inside them, so each run
            /// straddles its own line exactly as a cut does. Two runs still overlap where they meet
            /// at a corner — a ring built from straight pieces has to, unless a module happens to
            /// be as long as a wall is thick — but each one's end face now lands in the middle of
            /// the other rather than on its outer face, so the overlap has no coplanar vertical
            /// faces in it to flicker.
            /// </para>
            /// <para>
            /// The sides are ordered so that the wall facing a given one is a single exclusive-or
            /// away: the two runs along X first, then the two along Z. See <see cref="Opposite"/>.
            /// </para>
            /// </remarks>
            public void AddPerimeter(
                List<PlanWall> walls, int countX, int countZ, bool withEntrance, ref Rng rng)
            {
                var sides = new[]
                {
                    new PlanWall(new Vec2(_bounds.MinX, _bounds.MinZ), true, _module, countX, PlanWall.NoDoorway),
                    new PlanWall(new Vec2(_bounds.MinX, _bounds.MaxZ), true, _module, countX, PlanWall.NoDoorway),
                    new PlanWall(new Vec2(_bounds.MinX, _bounds.MinZ), false, _module, countZ, PlanWall.NoDoorway),
                    new PlanWall(new Vec2(_bounds.MaxX, _bounds.MinZ), false, _module, countZ, PlanWall.NoDoorway),
                };

                if (withEntrance)
                {
                    AddEntrances(sides, ref rng);
                }

                walls.AddRange(sides);
            }

            /// <summary>
            /// Takes <see cref="FloorPlan.Entrances"/> modules out of the outside walls, as far
            /// apart as the shell allows.
            /// </summary>
            /// <remarks>
            /// <para>
            /// Two ways in rather than one, and the second is not a spare. A building with a single
            /// door is a cul-de-sac: the only way out of it is the way you came, so the room behind
            /// the door is where a fight ends rather than ground either side can move through. Two
            /// doors far apart make the building a route, which is what a structure standing across
            /// a lane is for.
            /// </para>
            /// <para>
            /// <strong>The opposite wall first, and the far end of a neighbouring wall after
            /// that.</strong> Two doors in adjacent modules are one wide door, and two on
            /// neighbouring walls near their shared corner are a door with a corner in it: both
            /// leave the rest of the building with its back to the map. So the second way in is
            /// asked of the far wall before the two beside it, and wherever it lands it has to be
            /// <see cref="EntranceSeparation"/> of the shell's diagonal from the first.
            /// </para>
            /// <para>
            /// A side with no module clear of the stairwell is walked past, exactly as the single
            /// entrance always was: a door onto the drop is worse than a door somewhere else. A
            /// shell whose every other wall is spoken for keeps the one door it managed — there is
            /// no fifth wall to try, and one way in is still a building.
            /// </para>
            /// </remarks>
            void AddEntrances(PlanWall[] sides, ref Rng rng)
            {
                if (!TryOpen(sides, rng.NextRange(0, sides.Length), -1, Vec2.Zero, ref rng, out int first))
                {
                    return;
                }

                TryOpen(
                    sides,
                    Opposite(first),
                    first,
                    sides[first].ModuleCenter(sides[first].Doorway),
                    ref rng,
                    out _);
            }

            /// <summary>
            /// Opens a doorway in the first side from <paramref name="from"/> round that can carry
            /// one, and reports which side that was.
            /// </summary>
            /// <remarks>
            /// The walk is what makes the preference an order rather than a rule with exceptions:
            /// starting at the opposite wall and going round reaches the two walls beside it next,
            /// and the one already open — which is skipped — last.
            /// </remarks>
            bool TryOpen(
                PlanWall[] sides, int from, int taken, Vec2 apartFrom, ref Rng rng, out int side)
            {
                for (int i = 0; i < sides.Length; i++)
                {
                    side = (from + i) % sides.Length;
                    if (side == taken)
                    {
                        continue;
                    }

                    PlanWall run = sides[side];
                    if (TryPickEntrance(run, taken >= 0, apartFrom, ref rng, out int doorway))
                    {
                        sides[side] = new PlanWall(run.From, run.AlongX, _module, run.Modules, doorway);
                        return true;
                    }
                }

                side = -1;
                return false;
            }

            /// <summary>
            /// Which module of an outside wall is a way in, or false when none of them is.
            /// Consumes a draw only when it succeeds.
            /// </summary>
            /// <remarks>
            /// An explicit list of the modules that qualify, rather than the excluded-span
            /// arithmetic <see cref="TryPickDoorwayClearOf"/> uses on a cut. Two filters apply here
            /// and they do not fall in any tidy relation to each other — the modules the shaft
            /// rules out are a span in the middle of the run, and the ones too near the other door
            /// are a span at one end — so what is left need not be contiguous at all. One uniform
            /// pick over the list is the same draw discipline by a plainer route: how many draws a
            /// plan takes still does not depend on how many candidates were rejected.
            /// </remarks>
            bool TryPickEntrance(PlanWall run, bool apart, Vec2 from, ref Rng rng, out int doorway)
            {
                bool blocked = BlockedDoorways(
                    run.From, run.AlongX, run.Modules, out int firstBad, out int lastBad);

                // Away from both ends first and over the whole run second, which is what
                // PickDoorway does for a cut and for the same reason: a door in the last module of
                // a run opens into the corner where two walls meet.
                var legal = new List<int>(run.Modules);
                for (int pass = 0; pass < 2; pass++)
                {
                    bool middle = pass == 0 && run.Modules > 2;
                    int min = middle ? 1 : 0;
                    int max = middle ? run.Modules - 2 : run.Modules - 1;

                    for (int module = min; module <= max; module++)
                    {
                        if (blocked && module >= firstBad && module <= lastBad)
                        {
                            continue;
                        }

                        if (apart && !FarEnough(run.ModuleCenter(module), from))
                        {
                            continue;
                        }

                        legal.Add(module);
                    }

                    if (legal.Count > 0)
                    {
                        doorway = legal[rng.NextRange(0, legal.Count)];
                        return true;
                    }
                }

                doorway = PlanWall.NoDoorway;
                return false;
            }

            /// <summary>True if two ways in are far enough apart to be two ways in.</summary>
            bool FarEnough(Vec2 door, Vec2 other)
            {
                float x = door.X - other.X;
                float z = door.Y - other.Y;
                return x * x + z * z >= _separation * _separation;
            }

            /// <summary>The wall facing this one, in the order <see cref="AddPerimeter"/> builds them.</summary>
            static int Opposite(int side) => side ^ 1;

            /// <summary>
            /// Splits a region in two and recurses, or records it as a leaf.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The cut is recorded before either child is visited, so the wall list reads outermost
            /// first and the draw order is a plain depth-first walk. Both matter: a wall's position
            /// in the list is part of its stable id, and the order draws are taken in is what makes
            /// the same seed produce the same floor.
            /// </para>
            /// <para>
            /// A cut is only made if the run it leaves can hold a doorway clear of the shaft. Where
            /// it cannot, the other axis is tried and then the region is left whole — one larger
            /// room rather than two rooms joined by a door onto a drop. That is the one place the
            /// partition gives up a split for a reason that is not geometry, and it is the reason
            /// the strict rule is affordable: refusing the door and refusing the wall are the same
            /// decision, so nothing is ever sealed off.
            /// </para>
            /// </remarks>
            public void Subdivide(Cell region, ref Rng rng)
            {
                bool canSplitX = region.Width >= 2 * _minRoom;
                bool canSplitZ = region.Depth >= 2 * _minRoom;

                if (region.Area < MinSplitModules || (!canSplitX && !canSplitZ))
                {
                    _leaves.Add(region);
                    return;
                }

                bool alongX;
                if (canSplitX && canSplitZ)
                {
                    // The longer axis, so rooms tend towards square. A square region draws, rather
                    // than always picking X, so a square floor is not divided the same way every
                    // time.
                    alongX = region.Width != region.Depth
                        ? region.Width > region.Depth
                        : rng.NextRange(0, 2) == 0;
                }
                else
                {
                    alongX = canSplitX;
                }

                if (!TrySplit(region, alongX, ref rng, out int cut, out int doorway))
                {
                    // Every line this way through the region would put a wall through what is
                    // reserved, or leave a run whose every doorway would open into the shaft. The
                    // other axis is tried before the region is given up on: a floor with a shaft
                    // across the middle of it is still worth dividing the long way.
                    alongX = !alongX;
                    bool canSplit = alongX ? canSplitX : canSplitZ;

                    if (!canSplit || !TrySplit(region, alongX, ref rng, out cut, out doorway))
                    {
                        _leaves.Add(region);
                        return;
                    }
                }

                _cuts.Add(new Cut(region, alongX, cut, doorway));

                if (alongX)
                {
                    Subdivide(new Cell(region.X0, region.Z0, region.X0 + cut, region.Z1), ref rng);
                    Subdivide(new Cell(region.X0 + cut, region.Z0, region.X1, region.Z1), ref rng);
                    return;
                }

                Subdivide(new Cell(region.X0, region.Z0, region.X1, region.Z0 + cut), ref rng);
                Subdivide(new Cell(region.X0, region.Z0 + cut, region.X1, region.Z1), ref rng);
            }

            /// <summary>
            /// Draws where to split <paramref name="region"/> and which module of the resulting run
            /// is its doorway, or reports that no line this way can carry both. Consumes a draw
            /// only for each part that succeeds.
            /// </summary>
            /// <remarks>
            /// The two are decided together because a cut with nowhere legal to put its door is
            /// not a cut this partition may make — see <see cref="Subdivide"/>.
            /// </remarks>
            bool TrySplit(Cell region, bool alongX, ref Rng rng, out int cut, out int doorway)
            {
                doorway = PlanWall.NoDoorway;

                if (!TryCut(region, alongX, ref rng, out cut))
                {
                    return false;
                }

                PlanWall run = new Cut(region, alongX, cut, PlanWall.NoDoorway).ToWall(_bounds, _module);
                return TryPickDoorwayClearOf(run.From, run.AlongX, run.Modules, ref rng, out doorway);
            }

            /// <summary>
            /// Draws where to split <paramref name="region"/>, or reports that every line this way
            /// is blocked. Consumes a draw only when it succeeds.
            /// </summary>
            bool TryCut(Cell region, bool alongX, ref Rng rng, out int cut)
            {
                int length = alongX ? region.Width : region.Depth;
                bool blocked = Blocked(region, alongX, out int firstBad, out int lastBad);

                // The unblocked path is exactly the draw this made before anything was reserved,
                // which is what keeps a building with no stairwell in it laid out as it was.
                return TryPickExcluding(
                    _minRoom, length - _minRoom, firstBad, lastBad, blocked, ref rng, out cut);
            }

            /// <summary>
            /// Which module of a run is the doorway, or false when every one of them would open
            /// into the shaft. Consumes a draw only when it succeeds.
            /// </summary>
            /// <remarks>
            /// There is no fallback, and that is the point. A doorway onto the drop is a hole in
            /// the floor with a frame round it, and it looked survivable only while the alternative
            /// was a sealed room — which it is not: the caller answers a refusal by not making the
            /// cut, so the two rooms that would have been sealed off from each other are one room
            /// instead. Connectivity is still structural.
            /// </remarks>
            bool TryPickDoorwayClearOf(Vec2 from, bool alongX, int modules, ref Rng rng, out int doorway)
            {
                if (!BlockedDoorways(from, alongX, modules, out int firstBad, out int lastBad))
                {
                    doorway = PickDoorway(modules, ref rng);
                    return true;
                }

                // Away from the ends first, as an unblocked run does, and over the whole run only
                // when the middle has nothing left: a door in the last module of a run opens into
                // a corner, which is worse than the middle and far better than nothing.
                bool middle = modules > 2;
                return TryPickExcluding(
                           middle ? 1 : 0, middle ? modules - 2 : modules - 1,
                           firstBad, lastBad, true, ref rng, out doorway) ||
                       TryPickExcluding(0, modules - 1, firstBad, lastBad, true, ref rng, out doorway);
            }

            /// <summary>
            /// Draws an integer in [<paramref name="min"/>, <paramref name="max"/>], skipping
            /// [<paramref name="firstBad"/>, <paramref name="lastBad"/>]. Consumes a draw only when
            /// it succeeds.
            /// </summary>
            /// <remarks>
            /// One uniform pick over what is left rather than a draw-and-retry, so the number of
            /// draws a floor takes does not depend on how the rejections fell — which is what a
            /// seed has to mean the same thing on every run.
            /// </remarks>
            static bool TryPickExcluding(
                int min, int max, int firstBad, int lastBad, bool restricted, ref Rng rng, out int value)
            {
                if (max < min)
                {
                    value = 0;
                    return false;
                }

                if (!restricted || lastBad < min || firstBad > max)
                {
                    value = rng.NextRange(min, max + 1);
                    return true;
                }

                int belowCount = Math.Max(0, Math.Min(max, firstBad - 1) - min + 1);
                int aboveStart = Math.Max(min, lastBad + 1);
                int aboveCount = Math.Max(0, max - aboveStart + 1);

                if (belowCount + aboveCount == 0)
                {
                    value = 0;
                    return false;
                }

                int pick = rng.NextRange(0, belowCount + aboveCount);
                value = pick < belowCount ? min + pick : aboveStart + (pick - belowCount);
                return true;
            }

            /// <summary>
            /// The span of cut offsets that would stand a wall inside the reserved rectangle, if
            /// any reach this region at all.
            /// </summary>
            /// <remarks>
            /// A cut lands on a module boundary and the piece standing on it straddles that line,
            /// so an offset is blocked when the reserved rectangle grown by half a thickness holds
            /// the line. The blocked offsets are contiguous because the reservation is a rectangle,
            /// which is what lets the draw above stay a single uniform pick.
            /// </remarks>
            bool Blocked(Cell region, bool alongX, out int firstBad, out int lastBad)
            {
                firstBad = 0;
                lastBad = -1;

                if (!HasReservation || !region.ToRect(_bounds, _module).Overlaps(_reserved))
                {
                    return false;
                }

                float origin = alongX
                    ? _bounds.MinX + region.X0 * _module
                    : _bounds.MinZ + region.Z0 * _module;
                float low = (alongX ? _reserved.MinX : _reserved.MinZ) - _halfThickness;
                float high = (alongX ? _reserved.MaxX : _reserved.MaxZ) + _halfThickness;

                firstBad = (int)MathF.Floor((low - origin) / _module) + 1;
                lastBad = (int)MathF.Ceiling((high - origin) / _module) - 1;
                return lastBad >= firstBad;
            }

            /// <summary>
            /// The span of modules of a run whose doorway would open onto the reservation, if the
            /// run passes close enough to it at all.
            /// </summary>
            /// <remarks>
            /// The clearance is applied here rather than to <see cref="Blocked"/> for the reason
            /// <see cref="FloorPlan.DoorwayClearance"/> gives: a wall alongside a stairwell is
            /// fine, and only a door in it is not.
            /// </remarks>
            bool BlockedDoorways(Vec2 from, bool alongX, int modules, out int firstBad, out int lastBad)
            {
                firstBad = 0;
                lastBad = -1;

                if (!HasReservation)
                {
                    return false;
                }

                // Measured as the module's own rectangle against the zone, rather than as a point
                // against a zone grown to compensate, so that "this doorway is inside the
                // clearance" means the same thing here as it does to anyone looking at the two
                // rectangles afterwards.
                Rect2 zone = _reserved.Expanded(_clearance);
                float across = alongX ? from.Y : from.X;
                if (across + _halfThickness <= (alongX ? zone.MinZ : zone.MinX) ||
                    across - _halfThickness >= (alongX ? zone.MaxZ : zone.MaxX))
                {
                    return false;
                }

                float origin = alongX ? from.X : from.Y;
                float low = alongX ? zone.MinX : zone.MinZ;
                float high = alongX ? zone.MaxX : zone.MaxZ;

                firstBad = Math.Max(0, (int)MathF.Floor((low - origin) / _module));
                lastBad = Math.Min(modules - 1, (int)MathF.Ceiling((high - origin) / _module) - 1);
                return lastBad >= firstBad;
            }
        }

        /// <summary>A region of the partition, in module cells.</summary>
        readonly struct Cell
        {
            public Cell(int x0, int z0, int x1, int z1)
            {
                X0 = x0;
                Z0 = z0;
                X1 = x1;
                Z1 = z1;
            }

            public int X0 { get; }

            public int Z0 { get; }

            public int X1 { get; }

            public int Z1 { get; }

            public int Width => X1 - X0;

            public int Depth => Z1 - Z0;

            public int Area => Width * Depth;

            // Narrow as well as long. Being twice as long as it is wide makes a ten-by-five room
            // a corridor, which is a room; being one module wide makes it something you can only
            // walk down, which is what the word is for.
            public bool IsCorridor =>
                Math.Min(Width, Depth) <= CorridorModules &&
                Math.Max(Width, Depth) >= CorridorAspect * Math.Min(Width, Depth);

            public Rect2 ToRect(Rect2 bounds, float module) => new Rect2(
                bounds.MinX + X0 * module,
                bounds.MinZ + Z0 * module,
                bounds.MinX + X1 * module,
                bounds.MinZ + Z1 * module);
        }

        /// <summary>One split of the partition, and the wall it puts there.</summary>
        readonly struct Cut
        {
            readonly Cell _region;
            readonly bool _alongX;
            readonly int _offset;
            readonly int _doorway;

            public Cut(Cell region, bool alongX, int offset, int doorway)
            {
                _region = region;
                _alongX = alongX;
                _offset = offset;
                _doorway = doorway;
            }

            /// <summary>
            /// The wall the cut leaves: across the region, at the cut line, spanning the whole of
            /// the axis it did not divide.
            /// </summary>
            public PlanWall ToWall(Rect2 bounds, float module) => _alongX
                ? new PlanWall(
                    new Vec2(bounds.MinX + (_region.X0 + _offset) * module, bounds.MinZ + _region.Z0 * module),
                    false,
                    module,
                    _region.Depth,
                    _doorway)
                : new PlanWall(
                    new Vec2(bounds.MinX + _region.X0 * module, bounds.MinZ + (_region.Z0 + _offset) * module),
                    true,
                    module,
                    _region.Width,
                    _doorway);
        }
    }
}
