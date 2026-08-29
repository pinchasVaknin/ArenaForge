using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// A candidate for placement: which catalog entry to put down and where.
    /// </summary>
    /// <remarks>
    /// The world-space footprint and the entry's tags travel with the candidate rather than being
    /// looked up from the catalog on every test. Both are needed by nearly every constraint, and
    /// deriving the footprint once — at the factory below, which knows whether the rotation came
    /// from the exact quarter-turn table or the finer one — keeps the rotation arithmetic in a
    /// single place.
    /// </remarks>
    public readonly struct Placement
    {
        static readonly string[] NoTags = Array.Empty<string>();

        /// <summary>Creates a candidate from its parts.</summary>
        /// <exception cref="ArgumentException"><paramref name="logicalId"/> is blank.</exception>
        public Placement(string logicalId, Pose pose, Rect2 footprint, IReadOnlyList<string> tags)
        {
            if (string.IsNullOrWhiteSpace(logicalId))
            {
                throw new ArgumentException("A placement needs a logical id.", nameof(logicalId));
            }

            LogicalId = logicalId;
            Pose = pose;
            Footprint = footprint;
            Tags = tags ?? NoTags;
        }

        /// <summary>The catalog entry this candidate would place.</summary>
        public string LogicalId { get; }

        /// <summary>Where it would go.</summary>
        public Pose Pose { get; }

        /// <summary>The ground footprint <see cref="Pose"/> gives it, in world space.</summary>
        public Rect2 Footprint { get; }

        /// <summary>Tags carried through from the catalog entry.</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>A candidate at a whole number of quarter turns. The footprint stays exact.</summary>
        /// <remarks>
        /// The pose sits at <see cref="CatalogEntry.BaseOffset"/> rather than at zero, so the
        /// underside of the art lands on the surface it is being stood on rather than inside it.
        /// Every caller then adds the height of that surface — a storey's elevation, the ground —
        /// on the way out, which is why the lift belongs here: it is a fact about the art, and the
        /// callers are each about a different surface.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
        public static Placement AtQuarterTurn(CatalogEntry entry, Vec2 position, int quarterTurns)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return new Placement(
                entry.LogicalId,
                new Pose(position.ToVec3(entry.BaseOffset), QuarterTurn.Rotation(quarterTurns), 1f),
                QuarterTurn.Rotate(entry.Footprint, quarterTurns).Translated(position),
                entry.Tags);
        }

        /// <summary>A candidate at a whole number of <see cref="YawStep"/>s.</summary>
        /// <remarks>Stood on its own base, as <see cref="AtQuarterTurn"/> is.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
        public static Placement AtYawStep(CatalogEntry entry, Vec2 position, int steps)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return new Placement(
                entry.LogicalId,
                new Pose(position.ToVec3(entry.BaseOffset), YawStep.Rotation(steps), 1f),
                YawStep.Bounds(entry.Footprint, steps).Translated(position),
                entry.Tags);
        }

        /// <summary>True if this candidate carries the tag.</summary>
        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override string ToString() => $"{LogicalId} at {Pose.Position}";
    }
}
