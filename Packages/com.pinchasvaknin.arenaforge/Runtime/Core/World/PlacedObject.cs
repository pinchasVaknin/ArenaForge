using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// One object in a map: what it is, where it is, and how to refer to it again after a
    /// regeneration.
    /// </summary>
    /// <remarks>
    /// Immutable. Applying a Move or a SwapAsset produces a new instance, so the generated list a
    /// document holds is never disturbed by resolving it.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlacedObject
    {
        static readonly string[] NoTags = Array.Empty<string>();

        readonly SortedDictionary<string, string> _metadata;

        /// <summary>
        /// Readable generation path, for example <c>map/lane_mid/cover_03</c>. Reproducible from
        /// the same seed and parameters; user-created objects live under <c>user/</c>.
        /// </summary>
        [JsonProperty("stableId", Order = 0)]
        public string StableId { get; }

        /// <summary>Which catalog entry this object is an instance of.</summary>
        [JsonProperty("logicalId", Order = 1)]
        public string LogicalId { get; }

        /// <summary>World pose.</summary>
        [JsonProperty("pose", Order = 2)]
        public Pose Pose { get; }

        /// <summary>Tags carried through from the catalog entry, plus any the generator adds.</summary>
        [JsonProperty("tags", Order = 3)]
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// Free-form generator annotations — doorway rectangles, lane membership, placement
        /// statistics.
        /// </summary>
        /// <remarks>
        /// Sorted with an ordinal comparer rather than left in insertion order, because a
        /// <c>Dictionary</c> makes no promise about enumeration order and this collection is
        /// serialised. Ordinal rather than the default comparer so the order cannot depend on
        /// the machine's locale.
        /// </remarks>
        [JsonProperty("metadata", Order = 4)]
        public IReadOnlyDictionary<string, string> Metadata => _metadata;

        /// <summary>Creates a placed object.</summary>
        /// <exception cref="ArgumentException">Either id is blank.</exception>
        [JsonConstructor]
        public PlacedObject(
            string stableId,
            string logicalId,
            Pose pose,
            string[] tags,
            IDictionary<string, string> metadata)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException("Stable id must not be blank.", nameof(stableId));
            }

            if (string.IsNullOrWhiteSpace(logicalId))
            {
                throw new ArgumentException(
                    $"Logical id of '{stableId}' must not be blank.", nameof(logicalId));
            }

            StableId = stableId;
            LogicalId = logicalId;
            Pose = pose;
            Tags = tags ?? NoTags;
            _metadata = CopyMetadata(metadata);
        }

        /// <summary>Copy at a different pose. This is how a Move override is applied.</summary>
        public PlacedObject WithPose(Pose pose) =>
            new PlacedObject(StableId, LogicalId, pose, CopyTags(), _metadata);

        /// <summary>Copy referring to a different catalog entry. This is how a SwapAsset override is applied.</summary>
        public PlacedObject WithLogicalId(string logicalId) =>
            new PlacedObject(StableId, logicalId, Pose, CopyTags(), _metadata);

        /// <summary>Suppresses an empty tag list in serialised output.</summary>
        public bool ShouldSerializeTags() => Tags.Count > 0;

        /// <summary>Suppresses empty metadata in serialised output.</summary>
        public bool ShouldSerializeMetadata() => _metadata.Count > 0;

        /// <inheritdoc />
        public override string ToString() => $"{StableId} ({LogicalId})";

        internal static SortedDictionary<string, string> CopyMetadata(IDictionary<string, string> source)
        {
            var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (KeyValuePair<string, string> pair in source)
                {
                    copy[pair.Key] = pair.Value;
                }
            }

            return copy;
        }

        string[] CopyTags()
        {
            var copy = new string[Tags.Count];
            for (int i = 0; i < Tags.Count; i++)
            {
                copy[i] = Tags[i];
            }

            return copy;
        }
    }
}
