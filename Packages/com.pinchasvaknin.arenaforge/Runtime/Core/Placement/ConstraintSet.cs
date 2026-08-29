using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// The rules a stage places under, together with what it has already placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rules and committed objects live in one type because every rule that can reject a candidate
    /// is a statement about the map so far: overlap is against committed footprints, clearance is
    /// against committed structures, doorways are declared by structures already down. Splitting
    /// them would mean passing both halves everywhere they are used together, which is everywhere.
    /// </para>
    /// <para>
    /// Constraints are evaluated in the order they were given and evaluation stops at the first
    /// rejection, so cheap rules should come first. That order is part of what the set means: the
    /// reported reason for a rejection is the first rule that caught it, not the only one that
    /// would have.
    /// </para>
    /// <para>
    /// A set is built over either a whole arena or a plain bounded region — a building's floor is
    /// the second. Eleven of the thirteen rules do not care which: they are statements about a
    /// rectangle, a grid and what has already been committed. The two that do care,
    /// <see cref="ConstraintKind.WithinLane"/> and <see cref="ConstraintKind.ClearOfSpawn"/>,
    /// name arena features a floor has no equivalent of, and over a bounded region they throw
    /// rather than being quietly satisfied. A floor is like a lane in shape, not in what it is
    /// part of, and pretending a storey has a spawn to keep clear of would put a rule in the
    /// statistics that never rejected anything.
    /// </para>
    /// </remarks>
    public sealed class ConstraintSet
    {
        /// <summary>How far a coordinate may sit from a grid line and still count as on it.</summary>
        const float GridTolerance = 1e-4f;

        /// <summary>The arena this set is part of, or null when it is over a bounded region.</summary>
        readonly ArenaLayout _layout;

        /// <summary>
        /// What <see cref="ConstraintKind.InsidePlayfield"/> tests against and what
        /// <see cref="ConstraintKind.OnGrid"/> measures from: the playfield, or the region.
        /// </summary>
        readonly Rect2 _bounds;

        readonly List<PlacementConstraint> _constraints;
        readonly List<Placement> _committed = new List<Placement>();
        readonly List<Rect2> _doorways = new List<Rect2>();
        readonly List<Rect2> _paths = new List<Rect2>();

        /// <summary>Creates a set of rules to place into an arena against.</summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public ConstraintSet(ArenaLayout layout, IReadOnlyList<PlacementConstraint> constraints)
            : this(layout, layout != null ? layout.Playfield : default, constraints)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }
        }

        /// <summary>
        /// Creates a set of rules to place into a plain bounded region against — a building's
        /// floor. <see cref="ConstraintKind.WithinLane"/> and
        /// <see cref="ConstraintKind.ClearOfSpawn"/> are not available over one.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="constraints"/> is null.</exception>
        public ConstraintSet(Rect2 region, IReadOnlyList<PlacementConstraint> constraints)
            : this(null, region, constraints)
        {
        }

        ConstraintSet(ArenaLayout layout, Rect2 bounds, IReadOnlyList<PlacementConstraint> constraints)
        {
            if (constraints == null)
            {
                throw new ArgumentNullException(nameof(constraints));
            }

            _layout = layout;
            _bounds = bounds;
            _constraints = new List<PlacementConstraint>(constraints.Count);
            for (int i = 0; i < constraints.Count; i++)
            {
                _constraints.Add(constraints[i]);
            }
        }

        /// <summary>The area <see cref="ConstraintKind.InsidePlayfield"/> holds candidates inside.</summary>
        public Rect2 Bounds => _bounds;

        /// <summary>The rules, in evaluation order.</summary>
        public IReadOnlyList<PlacementConstraint> Constraints => _constraints;

        /// <summary>What has been committed so far, in commit order.</summary>
        public IReadOnlyList<Placement> Committed => _committed;

        /// <summary>The doorway rectangles candidates must keep clear of.</summary>
        public IReadOnlyList<Rect2> Doorways => _doorways;

        /// <summary>The walkways candidates must stay off entirely.</summary>
        public IReadOnlyList<Rect2> ReservedPaths => _paths;

        /// <summary>Records a doorway a committed structure declares.</summary>
        public void AddDoorway(Rect2 doorway) => _doorways.Add(doorway);

        /// <summary>
        /// Records a strip of floor that has to stay walkable.
        /// </summary>
        /// <remarks>
        /// Kept apart from the doorways rather than added to them, because the two rules mean
        /// different things about the same floor: a doorway is a gap to be approached and a
        /// walkway is a route to be crossed, and only the first wants a clearance measured around
        /// it. Sharing the list would apply that clearance to both.
        /// </remarks>
        public void AddReservedPath(Rect2 path) => _paths.Add(path);

        /// <summary>
        /// Accepts a placement into the map. Every later candidate is evaluated against it.
        /// </summary>
        public void Commit(Placement placement) => _committed.Add(placement);

        /// <summary>
        /// Tests a candidate, returning the first rule that rejects it or
        /// <see cref="ConstraintResult.Ok"/> if none do.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A <c>WithinLane</c> rule names a lane this layout does not have, or a <c>WithinLane</c>
        /// or <c>ClearOfSpawn</c> rule was given to a set built over a bounded region.
        /// </exception>
        public ConstraintResult Evaluate(Placement candidate) =>
            Evaluate(candidate, _committed.Count);

        /// <summary>
        /// Tests a candidate as <see cref="Evaluate(Placement)"/> does, but as though only the first
        /// <paramref name="against"/> placements had been committed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One caller, and it is <see cref="WallRun"/> closing the end of a run. A run is tiled from
        /// indivisible pieces, so the last piece leaves a remainder — and the only way to reach the
        /// end of the line with art that is never stretched is to seat one more piece flush against
        /// it, overlapping the piece before. That piece still has to answer every rule about
        /// <em>everything else</em>: it may not leave the playfield, stand in a gate, or run through
        /// a building. What it may do is double up on its own run, and this is how it says so.
        /// </para>
        /// <para>
        /// A count rather than a set of exemptions, because <see cref="Committed"/> is in commit
        /// order and a run's own pieces are exactly its tail. The caller reads the count before it
        /// lays a piece and hands the same number back.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="against"/> is negative or past the end of <see cref="Committed"/>.
        /// </exception>
        public ConstraintResult Evaluate(Placement candidate, int against)
        {
            if (against < 0 || against > _committed.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(against), against,
                    $"This set holds {_committed.Count} committed placement(s).");
            }

            for (int i = 0; i < _constraints.Count; i++)
            {
                PlacementConstraint constraint = _constraints[i];
                if (!Satisfies(candidate, constraint, against))
                {
                    return ConstraintResult.RejectedBy(constraint);
                }
            }

            return ConstraintResult.Ok;
        }

        bool Satisfies(Placement candidate, PlacementConstraint constraint, int against)
        {
            switch (constraint.Kind)
            {
                case ConstraintKind.InsidePlayfield:
                    return _bounds.Contains(candidate.Footprint);

                case ConstraintKind.WithinLane:
                    return LaneBand(constraint.Target).Contains(candidate.Footprint);

                case ConstraintKind.OnGrid:
                    return IsOnGrid(candidate.Pose.Position, constraint.Value);

                case ConstraintKind.NoOverlap:
                    return NothingOverlaps(candidate.Footprint.Expanded(constraint.Value), against);

                case ConstraintKind.MinDistanceFrom:
                    return IsClearOfTagged(candidate, constraint.Target, constraint.Value, against);

                case ConstraintKind.MaxDistanceFrom:
                    return IsNearTagged(candidate, constraint.Target, constraint.Value, against);

                case ConstraintKind.NotBlockingDoorway:
                    return LeavesDoorwaysClear(candidate.Footprint, constraint.Value);

                case ConstraintKind.ClearOfSpawn:
                    RequireArena(ConstraintKind.ClearOfSpawn);
                    return !candidate.Footprint.Overlaps(_layout.SpawnAreaA.Expanded(constraint.Value)) &&
                           !candidate.Footprint.Overlaps(_layout.SpawnAreaB.Expanded(constraint.Value));

                case ConstraintKind.AgainstWall:
                    return ReachesEdgeAlongX(candidate.Footprint, constraint.Value) ||
                           ReachesEdgeAlongZ(candidate.Footprint, constraint.Value);

                case ConstraintKind.InCorner:
                    return ReachesEdgeAlongX(candidate.Footprint, constraint.Value) &&
                           ReachesEdgeAlongZ(candidate.Footprint, constraint.Value);

                case ConstraintKind.InCentre:
                    return !ReachesEdgeAlongX(candidate.Footprint, constraint.Value) &&
                           !ReachesEdgeAlongZ(candidate.Footprint, constraint.Value);

                case ConstraintKind.NearDoorway:
                    return IsNearADoorway(candidate.Footprint, constraint.Value);

                case ConstraintKind.OffReservedPath:
                    return LeavesPathsClear(candidate.Footprint);

                default:
                    throw new InvalidOperationException($"Unhandled constraint kind {constraint.Kind}.");
            }
        }

        /// <summary>
        /// Refuses a rule that only means something inside an arena.
        /// </summary>
        /// <remarks>
        /// Explicitly rather than by treating a missing arena as "nothing to keep clear of". A
        /// rule that cannot reject anything is worse than an absent one: it would sit in the
        /// evaluation order and in the placement statistics reporting zero rejections, which
        /// reads as a rule that was satisfied rather than one that was never asked.
        /// </remarks>
        void RequireArena(ConstraintKind kind)
        {
            if (_layout == null)
            {
                throw new InvalidOperationException(
                    $"A {kind} rule was given to a constraint set built over a bounded region " +
                    "rather than an arena. That region has no lanes and no spawns, so the rule " +
                    "has nothing to test against.");
            }
        }

        Rect2 LaneBand(string laneId)
        {
            RequireArena(ConstraintKind.WithinLane);

            for (int i = 0; i < _layout.Lanes.Count; i++)
            {
                if (string.Equals(_layout.Lanes[i].Id, laneId, StringComparison.Ordinal))
                {
                    return _layout.Lanes[i].Band;
                }
            }

            throw new InvalidOperationException(
                $"A WithinLane constraint names '{laneId}', which is not a lane of this layout.");
        }

        bool IsOnGrid(Vec3 position, float cellSize)
        {
            if (!(cellSize > 0f))
            {
                throw new InvalidOperationException(
                    $"An OnGrid constraint has a cell size of {cellSize}; it must be positive.");
            }

            return IsOnGrid(position.X, _bounds.MinX, cellSize) &&
                   IsOnGrid(position.Z, _bounds.MinZ, cellSize);
        }

        static bool IsOnGrid(float value, float origin, float cellSize)
        {
            float steps = MathF.Round((value - origin) / cellSize, MidpointRounding.AwayFromZero);
            return MathF.Abs(value - (origin + steps * cellSize)) < GridTolerance;
        }

        bool NothingOverlaps(Rect2 grown, int against)
        {
            for (int i = 0; i < against; i++)
            {
                if (grown.Overlaps(_committed[i].Footprint))
                {
                    return false;
                }
            }

            return true;
        }

        bool IsClearOfTagged(Placement candidate, string tag, float distance, int against)
        {
            for (int i = 0; i < against; i++)
            {
                if (_committed[i].HasTag(tag) &&
                    Rect2.Distance(candidate.Footprint, _committed[i].Footprint) < distance)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsNearTagged(Placement candidate, string tag, float distance, int against)
        {
            bool anyTagged = false;
            for (int i = 0; i < against; i++)
            {
                if (!_committed[i].HasTag(tag))
                {
                    continue;
                }

                anyTagged = true;
                if (Rect2.Distance(candidate.Footprint, _committed[i].Footprint) <= distance)
                {
                    return true;
                }
            }

            // Nothing carrying the tag is on the map yet, so there is nothing to be far from.
            return !anyTagged;
        }

        bool LeavesDoorwaysClear(Rect2 footprint, float clearance)
        {
            for (int i = 0; i < _doorways.Count; i++)
            {
                if (footprint.Overlaps(_doorways[i].Expanded(clearance)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True if the footprint comes within <paramref name="reach"/> of either edge of the
        /// region along world X.
        /// </summary>
        /// <remarks>
        /// The two axes are kept apart because <see cref="ConstraintKind.InCorner"/> is the two of
        /// them together: a footprint against the low and the high X edge at once — which a room
        /// only a shade wider than the thing standing in it produces — is against a wall, not in a
        /// corner. <see cref="ConstraintKind.InCentre"/> is neither of them, over the same
        /// distance, so all three rules agree about what reaching an edge means.
        /// </remarks>
        bool ReachesEdgeAlongX(Rect2 footprint, float reach) =>
            footprint.MinX - _bounds.MinX <= reach || _bounds.MaxX - footprint.MaxX <= reach;

        bool ReachesEdgeAlongZ(Rect2 footprint, float reach) =>
            footprint.MinZ - _bounds.MinZ <= reach || _bounds.MaxZ - footprint.MaxZ <= reach;

        /// <summary>
        /// True if the footprint touches none of the reserved walkways.
        /// </summary>
        /// <remarks>
        /// No clearance and no tolerance: the strips overlap each other at every bend and are
        /// already as wide as the route needs to be, so touching one at all is standing in it. That
        /// holds of a carriageway as much as of a room's walkway — see
        /// <see cref="PlacementConstraint.OffReservedPath"/>.
        /// </remarks>
        bool LeavesPathsClear(Rect2 footprint)
        {
            for (int i = 0; i < _paths.Count; i++)
            {
                if (footprint.Overlaps(_paths[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool IsNearADoorway(Rect2 footprint, float distance)
        {
            // No doorway to measure from, so nothing to be far from — the same bargain
            // MaxDistanceFrom makes when nothing committed carries its tag.
            if (_doorways.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < _doorways.Count; i++)
            {
                if (Rect2.Distance(footprint, _doorways[i]) <= distance)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
