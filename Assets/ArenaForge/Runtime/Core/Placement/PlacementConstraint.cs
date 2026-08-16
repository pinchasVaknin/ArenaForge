using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// The complete set of rules a candidate placement can be tested against.
    /// </summary>
    /// <remarks>
    /// A closed enum rather than a constraint interface with an implementation per rule. There
    /// are eight rules, <see cref="ConstraintSet"/> evaluates all eight, and nothing outside Core
    /// adds a ninth. An extension point here would be a guess about a caller that does not exist;
    /// when one turns up, the switch that evaluates these is the obvious place to change.
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
        /// Reads as the factory call that produced it — <c>NoOverlap(0.75)</c>. This is the text a
        /// rejection is reported with, so it goes in placement statistics and test failures.
        /// </summary>
        public override string ToString()
        {
            string value = Value.ToString("0.###", CultureInfo.InvariantCulture);

            switch (Kind)
            {
                case ConstraintKind.InsidePlayfield:
                    return "InsidePlayfield";
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
