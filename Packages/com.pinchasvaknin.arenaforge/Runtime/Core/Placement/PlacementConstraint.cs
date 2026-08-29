using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// The complete set of rules a candidate placement can be tested against.
    /// </summary>
    /// <remarks>
    /// A closed enum rather than a constraint interface with an implementation per rule. There
    /// are thirteen rules, <see cref="ConstraintSet"/> evaluates all thirteen, and nothing outside
    /// Core adds a fourteenth. An extension point here would be a guess about a caller that does not
    /// exist; when one turns up, the switch that evaluates these is the obvious place to change —
    /// which is exactly what happened when interior decor arrived and wanted to be told where a
    /// wall is.
    /// </remarks>
    public enum ConstraintKind
    {
        /// <summary>The footprint must lie inside the playfield.</summary>
        InsidePlayfield,

        /// <summary>The footprint must lie inside a named lane's band.</summary>
        WithinLane,

        /// <summary>The pose must sit on a grid of the given cell size.</summary>
        OnGrid,

        /// <summary>The footprint, grown by a margin, must not overlap anything already committed.</summary>
        NoOverlap,

        /// <summary>The footprint must be at least this far from everything carrying a tag.</summary>
        MinDistanceFrom,

        /// <summary>The footprint must be within this distance of something carrying a tag.</summary>
        MaxDistanceFrom,

        /// <summary>The footprint must stay out of every declared doorway, plus a clearance.</summary>
        NotBlockingDoorway,

        /// <summary>The footprint must stay out of both spawn areas, plus a radius.</summary>
        ClearOfSpawn,

        /// <summary>The footprint must reach within this distance of one of the region's edges.</summary>
        AgainstWall,

        /// <summary>The footprint must reach within this distance of two perpendicular edges.</summary>
        InCorner,

        /// <summary>The footprint must keep this much floor between itself and every edge.</summary>
        InCentre,

        /// <summary>The footprint must be within this distance of a declared doorway.</summary>
        NearDoorway,

        /// <summary>The footprint must stay off every reserved walkway.</summary>
        OffReservedPath,
    }

    /// <summary>
    /// One rule, with the argument it was configured with.
    /// </summary>
    /// <remarks>
    /// Both arguments are held on every constraint rather than in a per-kind payload type: a rule
    /// takes at most a name and a distance, and eight tiny types carrying one field each would be
    /// harder to read than one struct whose unused field is documented as unused.
    /// </remarks>
    public readonly struct PlacementConstraint
    {
        PlacementConstraint(ConstraintKind kind, string target, float value)
        {
            Kind = kind;
            Target = target;
            Value = value;
        }

        /// <summary>Which rule this is.</summary>
        public ConstraintKind Kind { get; }

        /// <summary>
        /// The lane id for <see cref="ConstraintKind.WithinLane"/> and the tag for the two
        /// distance rules. Null for every other kind.
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// The distance the rule is configured with, in metres — a cell size, a margin, a
        /// clearance or a radius depending on the kind. Zero where the kind takes no distance.
        /// </summary>
        public float Value { get; }

        /// <summary>The footprint must lie inside the playfield.</summary>
        public static PlacementConstraint InsidePlayfield() =>
            new PlacementConstraint(ConstraintKind.InsidePlayfield, null, 0f);

        /// <summary>The footprint must lie inside the band of the lane with this id.</summary>
        public static PlacementConstraint WithinLane(string laneId) =>
            new PlacementConstraint(ConstraintKind.WithinLane, laneId, 0f);

        /// <summary>The pose must sit on a grid of <paramref name="cellSize"/> metre cells.</summary>
        public static PlacementConstraint OnGrid(float cellSize) =>
            new PlacementConstraint(ConstraintKind.OnGrid, null, cellSize);

        /// <summary>
        /// The footprint, grown by <paramref name="margin"/> on every side, must not overlap any
        /// committed footprint. The margin is the walking space kept between two props.
        /// </summary>
        public static PlacementConstraint NoOverlap(float margin) =>
            new PlacementConstraint(ConstraintKind.NoOverlap, null, margin);

        /// <summary>
        /// The footprint must be at least <paramref name="distance"/> metres from the footprint of
        /// everything committed that carries <paramref name="tag"/>.
        /// </summary>
        public static PlacementConstraint MinDistanceFrom(string tag, float distance) =>
            new PlacementConstraint(ConstraintKind.MinDistanceFrom, tag, distance);

        /// <summary>
        /// The footprint must be within <paramref name="distance"/> metres of the footprint of
        /// something committed carrying <paramref name="tag"/>. Satisfied when nothing committed
        /// carries the tag: a rule with no anchor to measure from cannot reject anything.
        /// </summary>
        public static PlacementConstraint MaxDistanceFrom(string tag, float distance) =>
            new PlacementConstraint(ConstraintKind.MaxDistanceFrom, tag, distance);

        /// <summary>
        /// The footprint must not touch any doorway rectangle grown by
        /// <paramref name="clearance"/>, so a structure can still be entered.
        /// </summary>
        public static PlacementConstraint NotBlockingDoorway(float clearance) =>
            new PlacementConstraint(ConstraintKind.NotBlockingDoorway, null, clearance);

        /// <summary>
        /// The footprint must not touch either spawn area grown by <paramref name="radius"/>, so
        /// a team can leave its spawn.
        /// </summary>
        public static PlacementConstraint ClearOfSpawn(float radius) =>
            new PlacementConstraint(ConstraintKind.ClearOfSpawn, null, radius);

        /// <summary>
        /// The footprint must come within <paramref name="reach"/> metres of one of the region's
        /// four edges — the rule that puts a sofa along a wall instead of in the middle of the
        /// floor.
        /// </summary>
        /// <remarks>
        /// Against the region rather than against a committed wall, because a building's walls are
        /// art rather than placed objects: the region a room's contents are proposed into is
        /// bounded by exactly the walls that enclose it, so its edges <em>are</em> the walls. A
        /// reach of zero means flush.
        /// </remarks>
        public static PlacementConstraint AgainstWall(float reach) =>
            new PlacementConstraint(ConstraintKind.AgainstWall, null, reach);

        /// <summary>
        /// The footprint must come within <paramref name="reach"/> metres of two perpendicular
        /// edges of the region at once. Strictly stronger than <see cref="AgainstWall"/>.
        /// </summary>
        public static PlacementConstraint InCorner(float reach) =>
            new PlacementConstraint(ConstraintKind.InCorner, null, reach);

        /// <summary>
        /// The footprint must keep <paramref name="clearance"/> metres of floor between itself and
        /// every edge of the region — the rule that puts a table in the middle of a room rather
        /// than back against its wall.
        /// </summary>
        /// <remarks>
        /// The exact negation of <see cref="AgainstWall"/> over the same two predicates, which is
        /// why it is one rule rather than a pair of distance tests: a piece the region's edges
        /// cannot reach is a piece standing in the middle of it, whatever the region's shape.
        /// A clearance rather than a radius from the centre, because what makes a room's middle
        /// its middle is the floor left round the thing standing there — a hall's centrepiece is
        /// no further from its walls than a cupboard's would be if the rule were measured the
        /// other way about.
        /// </remarks>
        public static PlacementConstraint InCentre(float clearance) =>
            new PlacementConstraint(ConstraintKind.InCentre, null, clearance);

        /// <summary>
        /// The footprint must be within <paramref name="distance"/> metres of some declared
        /// doorway. Satisfied when no doorway has been declared, for the reason
        /// <see cref="MaxDistanceFrom"/> is: a rule with nothing to measure from cannot reject.
        /// </summary>
        public static PlacementConstraint NearDoorway(float distance) =>
            new PlacementConstraint(ConstraintKind.NearDoorway, null, distance);

        /// <summary>
        /// The footprint must not touch any walkway reserved on the region — the rule that keeps
        /// the route between a room's doors walkable, and the road across a map open.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It takes no distance, where <see cref="NotBlockingDoorway"/> takes a clearance, because
        /// a walkway is not a thing to keep away from: it is a strip of floor whose width already
        /// says how much room a person needs. A margin on top would be that width stated twice,
        /// and stating it here would let two callers disagree about how wide the same strip is.
        /// </para>
        /// <para>
        /// <strong>Two callers now, and the second wanted no distance either.</strong> A room's
        /// walkway is reserved by <c>FloorPlan</c>; a carriageway is reserved by
        /// <see cref="RoadNetwork.Corridors"/> and handed to the same rule by
        /// <see cref="CoverPlacer"/>. An outdoor verge looked like the thing the unused
        /// <see cref="Value"/> field was waiting for and measured as the opposite: cover snaps to
        /// the placement grid, so a verge of even half a metre moves a prop a whole cell further
        /// from the road, and the road's own ground stops being covered from beside it. Over seeds
        /// 1..1000 of the default map at a road density of 1 that took cover coverage from 0.700 to
        /// 0.668 and put 47 seeds under the threshold; a metre of verge took it to 0.658 and 102.
        /// The rectangles a network reserves are where the road is, and that is the whole of the
        /// width worth stating.
        /// </para>
        /// <para>
        /// Separate from <see cref="NotBlockingDoorway"/> rather than expressed by declaring each
        /// strip as another doorway, because the clearance that rule applies would grow every
        /// strip by three quarters of a metre on all four sides — turning a walkway a person can
        /// use into a reservation that swallows a small room whole.
        /// </para>
        /// </remarks>
        public static PlacementConstraint OffReservedPath() =>
            new PlacementConstraint(ConstraintKind.OffReservedPath, null, 0f);

        /// <summary>
        /// Reads as the factory call that produced it — <c>NoOverlap(0.75)</c>. This is the text a
        /// rejection is reported with, so it goes in placement statistics and test failures.
        /// </summary>
        public override string ToString()
        {
            string value = Value.ToString("0.###", CultureInfo.InvariantCulture);

            switch (Kind)
            {
                case ConstraintKind.InsidePlayfield:
                case ConstraintKind.OffReservedPath:
                    return Kind.ToString();
                case ConstraintKind.WithinLane:
                    return $"WithinLane({Target})";
                case ConstraintKind.MinDistanceFrom:
                case ConstraintKind.MaxDistanceFrom:
                    return $"{Kind}({Target}, {value})";
                default:
                    return $"{Kind}({value})";
            }
        }
    }

    /// <summary>
    /// What a <see cref="ConstraintSet"/> made of a candidate: accepted, or rejected by one named
    /// rule.
    /// </summary>
    /// <remarks>
    /// The rejecting constraint is kept rather than reduced to a boolean because it is the only
    /// useful thing to say about a failed placement. It drives the per-constraint tallies in
    /// <see cref="PlacementStats"/>, and it is what a test failure or a validator report quotes.
    /// </remarks>
    public readonly struct ConstraintResult
    {
        ConstraintResult(bool accepted, PlacementConstraint failed)
        {
            IsOk = accepted;
            Failed = failed;
        }

        /// <summary>True when every constraint was satisfied.</summary>
        public bool IsOk { get; }

        /// <summary>The first constraint that rejected the candidate. Meaningless when <see cref="IsOk"/>.</summary>
        public PlacementConstraint Failed { get; }

        /// <summary>The accepting result.</summary>
        public static ConstraintResult Ok => new ConstraintResult(true, default);

        /// <summary>A rejection by one constraint.</summary>
        public static ConstraintResult RejectedBy(PlacementConstraint constraint) =>
            new ConstraintResult(false, constraint);

        /// <inheritdoc />
        public override string ToString() => IsOk ? "ok" : Failed.ToString();
    }
}
