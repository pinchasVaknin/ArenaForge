using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// A tally of what a placement stage tried and what stopped it.
    /// </summary>
    /// <remarks>
    /// Recorded into the world document because a sparse map is otherwise a mystery: a lane that
    /// came out with half the cover it asked for looks the same as one that was never asked for
    /// much. The per-constraint counts say which rule ate the candidates, which is the difference
    /// between "the doorway clearance is too generous" and "the catalog has nothing small enough".
    /// </remarks>
    public sealed class PlacementStats
    {
        readonly int[] _rejections = new int[Enum.GetValues(typeof(ConstraintKind)).Length];

        /// <summary>Candidates evaluated.</summary>
        public int Attempted { get; private set; }

        /// <summary>Candidates that satisfied every constraint and were committed.</summary>
        public int Accepted { get; private set; }

        /// <summary>Candidates rejected by some constraint.</summary>
        public int Rejected => Attempted - Accepted;

        /// <summary>Counts one evaluated candidate.</summary>
        public void Record(ConstraintResult result)
        {
            Attempted++;
            if (result.IsOk)
            {
                Accepted++;
            }
            else
            {
                _rejections[(int)result.Failed.Kind]++;
            }
        }

        /// <summary>How many candidates this constraint kind was the first to reject.</summary>
        public int RejectedBy(ConstraintKind kind) => _rejections[(int)kind];

        /// <summary>
        /// Writes the tally into a metadata dictionary, one key per figure, each prefixed with
        /// <paramref name="prefix"/>. Constraint kinds that rejected nothing are left out.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public void WriteTo(IDictionary<string, string> metadata, string prefix)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            metadata[prefix + "attempted"] = Attempted.ToString(CultureInfo.InvariantCulture);
            metadata[prefix + "accepted"] = Accepted.ToString(CultureInfo.InvariantCulture);
            metadata[prefix + "rejected"] = Rejected.ToString(CultureInfo.InvariantCulture);

            for (int i = 0; i < _rejections.Length; i++)
            {
                if (_rejections[i] > 0)
                {
                    metadata[prefix + "rejected_" + MetadataName((ConstraintKind)i)] =
                        _rejections[i].ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>
        /// Reads a tally back out of a metadata dictionary written by <see cref="WriteTo"/>.
        /// </summary>
        /// <remarks>
        /// The analysis layer measures a document, not a generator run, so the statistics have to
        /// come back from where the generator left them. Missing or unreadable keys read as zero:
        /// a document written by a catalog with no cover in it has no tally to recover, and that is
        /// a fact about the map rather than a corrupt file.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static PlacementStats ReadFrom(IDictionary<string, string> metadata, string prefix)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            var stats = new PlacementStats
            {
                Attempted = ReadCount(metadata, prefix + "attempted"),
                Accepted = ReadCount(metadata, prefix + "accepted"),
            };

            for (int i = 0; i < stats._rejections.Length; i++)
            {
                stats._rejections[i] =
                    ReadCount(metadata, prefix + "rejected_" + MetadataName((ConstraintKind)i));
            }

            return stats;
        }

        static int ReadCount(IDictionary<string, string> metadata, string key) =>
            metadata.TryGetValue(key, out string text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;

        /// <summary>
        /// The metadata key fragment naming a constraint kind, in the lower-case style the rest of
        /// the document's keys use.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than derived from the enum name, so renaming a member cannot quietly
        /// change the shape of every document ever written.
        /// </remarks>
        public static string MetadataName(ConstraintKind kind)
        {
            switch (kind)
            {
                case ConstraintKind.InsidePlayfield:
                    return "inside_playfield";
                case ConstraintKind.WithinLane:
                    return "within_lane";
                case ConstraintKind.OnGrid:
                    return "on_grid";
                case ConstraintKind.NoOverlap:
                    return "no_overlap";
                case ConstraintKind.MinDistanceFrom:
                    return "min_distance_from";
                case ConstraintKind.MaxDistanceFrom:
                    return "max_distance_from";
                case ConstraintKind.NotBlockingDoorway:
                    return "not_blocking_doorway";
                case ConstraintKind.ClearOfSpawn:
                    return "clear_of_spawn";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unnamed constraint kind.");
            }
        }
    }
}
