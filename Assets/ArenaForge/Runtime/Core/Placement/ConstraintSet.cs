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
    /// </remarks>
    public sealed class ConstraintSet
    {
        /// <summary>How far a coordinate may sit from a grid line and still count as on it.</summary>
        const float GridTolerance = 1e-4f;

        readonly ArenaLayout _layout;
        readonly List<PlacementConstraint> _constraints;
        readonly List<Placement> _committed = new List<Placement>();
        readonly List<Rect2> _doorways = new List<Rect2>();

        /// <summary>Creates a set of rules to place against.</summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public ConstraintSet(ArenaLayout layout, IReadOnlyList<PlacementConstraint> constraints)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (constraints == null)
            {
                throw new ArgumentNullException(nameof(constraints));
            }

            _layout = layout;
            _constraints = new List<PlacementConstraint>(constraints.Count);
            for (int i = 0; i < constraints.Count; i++)
            {
                _constraints.Add(constraints[i]);
            }
        }

        /// <summary>The rules, in evaluation order.</summary>
        public IReadOnlyList<PlacementConstraint> Constraints => _constraints;

        /// <summary>What has been committed so far, in commit order.</summary>
        public IReadOnlyList<Placement> Committed => _committed;

        /// <summary>The doorway rectangles candidates must keep clear of.</summary>
        public IReadOnlyList<Rect2> Doorways => _doorways;

        /// <summary>Records a doorway a committed structure declares.</summary>
        public void AddDoorway(Rect2 doorway) => _doorways.Add(doorway);

        /// <summary>
        /// Accepts a placement into the map. Every later candidate is evaluated against it.
        /// </summary>
        public void Commit(Placement placement) => _committed.Add(placement);

        /// <summary>
        /// Tests a candidate, returning the first rule that rejects it or
        /// <see cref="ConstraintResult.Ok"/> if none do.
        /// </summary>
        /// <exception cref="InvalidOperationException">A <c>WithinLane</c> rule names a lane this layout does not have.</exception>
        public ConstraintResult Evaluate(Placement candidate)
        {
            for (int i = 0; i < _constraints.Count; i++)
            {
                PlacementConstraint constraint = _constraints[i];
                if (!Satisfies(candidate, constraint))
                {
                    return ConstraintResult.RejectedBy(constraint);
                }
            }

            return ConstraintResult.Ok;
        }

        bool Satisfies(Placement candidate, PlacementConstraint constraint)
        {
            switch (constraint.Kind)
            {
                case ConstraintKind.InsidePlayfield:
                    return _layout.Playfield.Contains(candidate.Footprint);

                case ConstraintKind.WithinLane:
                    return LaneBand(constraint.Target).Contains(candidate.Footprint);

                case ConstraintKind.OnGrid:
                    return IsOnGrid(candidate.Pose.Position, constraint.Value);

                case ConstraintKind.NoOverlap:
                    return NothingOverlaps(candidate.Footprint.Expanded(constraint.Value));

                case ConstraintKind.MinDistanceFrom:
                    return IsClearOfTagged(candidate, constraint.Target, constraint.Value);

                case ConstraintKind.MaxDistanceFrom:
                    return IsNearTagged(candidate, constraint.Target, constraint.Value);

                case ConstraintKind.NotBlockingDoorway:
                    return LeavesDoorwaysClear(candidate.Footprint, constraint.Value);

                case ConstraintKind.ClearOfSpawn:
                    return !candidate.Footprint.Overlaps(_layout.SpawnAreaA.Expanded(constraint.Value)) &&
                           !candidate.Footprint.Overlaps(_layout.SpawnAreaB.Expanded(constraint.Value));

                default:
                    throw new InvalidOperationException($"Unhandled constraint kind {constraint.Kind}.");
            }
        }

        Rect2 LaneBand(string laneId)
        {
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

            return IsOnGrid(position.X, _layout.Playfield.MinX, cellSize) &&
                   IsOnGrid(position.Z, _layout.Playfield.MinZ, cellSize);
        }

        static bool IsOnGrid(float value, float origin, float cellSize)
        {
            float steps = MathF.Round((value - origin) / cellSize, MidpointRounding.AwayFromZero);
            return MathF.Abs(value - (origin + steps * cellSize)) < GridTolerance;
        }

        bool NothingOverlaps(Rect2 grown)
        {
            for (int i = 0; i < _committed.Count; i++)
            {
                if (grown.Overlaps(_committed[i].Footprint))
                {
                    return false;
                }
            }

            return true;
        }

        bool IsClearOfTagged(Placement candidate, string tag, float distance)
        {
            for (int i = 0; i < _committed.Count; i++)
            {
                if (_committed[i].HasTag(tag) &&
                    Rect2.Distance(candidate.Footprint, _committed[i].Footprint) < distance)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsNearTagged(Placement candidate, string tag, float distance)
        {
            bool anyTagged = false;
            for (int i = 0; i < _committed.Count; i++)
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
    }
}
