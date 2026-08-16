using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>What a manual edit does to the generated object it targets.</summary>
    public enum OverrideOp
    {
        /// <summary>Relocate the target.</summary>
        Move,

        /// <summary>Remove the target from the resolved map.</summary>
        Delete,

        /// <summary>Introduce an object the generator did not produce.</summary>
        Add,

        /// <summary>Keep the target's pose but swap which catalog entry it instantiates.</summary>
        SwapAsset,
    }

    /// <summary>
    /// One manual edit, stored as a diff against the generated output rather than baked into it.
    /// </summary>
    /// <remarks>
    /// A single concrete type with the payload each operation needs, rather than a class per
    /// operation. Four fixed operations do not justify a hierarchy, and a flat shape keeps the
    /// serialised form readable and free of type discriminators.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class EditOverride
    {
        static readonly string[] NoTags = Array.Empty<string>();

        readonly SortedDictionary<string, string> _metadata;

        /// <summary>
        /// Stable id this edit applies to. For <see cref="OverrideOp.Add"/> it is the id the new
        /// object will take, which by convention lives under <c>user/</c>.
        /// </summary>
        [JsonProperty("targetId", Order = 0)]
        public string TargetId { get; }

        /// <summary>Which operation this is.</summary>
        [JsonProperty("op", Order = 1)]
        public OverrideOp Op { get; }

        /// <summary>New pose. Set for <see cref="OverrideOp.Move"/> and <see cref="OverrideOp.Add"/>.</summary>
        [JsonProperty("pose", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public Pose? Pose { get; }

        /// <summary>Catalog entry. Set for <see cref="OverrideOp.Add"/> and <see cref="OverrideOp.SwapAsset"/>.</summary>
        [JsonProperty("logicalId", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string LogicalId { get; }

        /// <summary>Tags for the new object. Used by <see cref="OverrideOp.Add"/>.</summary>
        [JsonProperty("tags", Order = 4)]
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Metadata for the new object. Used by <see cref="OverrideOp.Add"/>.</summary>
        [JsonProperty("metadata", Order = 5)]
        public IReadOnlyDictionary<string, string> Metadata => _metadata;

        /// <summary>
        /// Creates an override and validates that the payload matches the operation. Prefer the
        /// named factory methods; this constructor exists for the deserialiser, and it is where a
        /// hand-edited or truncated document fails with a message naming the offending id.
        /// </summary>
        /// <exception cref="ArgumentException">The id is blank, or the payload does not match the operation.</exception>
        [JsonConstructor]
        public EditOverride(
            string targetId,
            OverrideOp op,
            Pose? pose,
            string logicalId,
            string[] tags,
            IDictionary<string, string> metadata)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new ArgumentException("Override target id must not be blank.", nameof(targetId));
            }

            switch (op)
            {
                case OverrideOp.Move:
                    Require(pose.HasValue, targetId, op, "a pose");
                    break;
                case OverrideOp.Delete:
                    break;
                case OverrideOp.Add:
                    Require(pose.HasValue, targetId, op, "a pose");
                    Require(!string.IsNullOrWhiteSpace(logicalId), targetId, op, "a logical id");
                    break;
                case OverrideOp.SwapAsset:
                    Require(!string.IsNullOrWhiteSpace(logicalId), targetId, op, "a logical id");
                    break;
                default:
                    throw new ArgumentException($"Unknown override operation '{op}'.", nameof(op));
            }

            TargetId = targetId;
            Op = op;
            Pose = pose;
            LogicalId = logicalId;
            Tags = tags ?? NoTags;
            _metadata = PlacedObject.CopyMetadata(metadata);
        }

        /// <summary>Relocates an existing object.</summary>
        public static EditOverride Move(string targetId, Pose pose) =>
            new EditOverride(targetId, OverrideOp.Move, pose, null, null, null);

        /// <summary>Removes an existing object.</summary>
        public static EditOverride Delete(string targetId) =>
            new EditOverride(targetId, OverrideOp.Delete, null, null, null, null);

        /// <summary>Adds an object the generator did not produce.</summary>
        public static EditOverride Add(
            string stableId,
            string logicalId,
            Pose pose,
            string[] tags = null,
            IDictionary<string, string> metadata = null) =>
            new EditOverride(stableId, OverrideOp.Add, pose, logicalId, tags, metadata);

        /// <summary>Swaps which catalog entry an existing object instantiates.</summary>
        public static EditOverride SwapAsset(string targetId, string logicalId) =>
            new EditOverride(targetId, OverrideOp.SwapAsset, null, logicalId, null, null);

        /// <summary>Suppresses an empty tag list in serialised output.</summary>
        public bool ShouldSerializeTags() => Tags.Count > 0;

        /// <summary>Suppresses empty metadata in serialised output.</summary>
        public bool ShouldSerializeMetadata() => _metadata.Count > 0;

        /// <summary>Builds the object an <see cref="OverrideOp.Add"/> introduces.</summary>
        /// <exception cref="InvalidOperationException">This override is not an Add.</exception>
        internal PlacedObject ToPlacedObject()
        {
            if (Op != OverrideOp.Add)
            {
                throw new InvalidOperationException(
                    $"Override '{TargetId}' is a {Op}, not an Add.");
            }

            var tags = new string[Tags.Count];
            for (int i = 0; i < Tags.Count; i++)
            {
                tags[i] = Tags[i];
            }

            return new PlacedObject(TargetId, LogicalId, Pose.Value, tags, _metadata);
        }

        /// <inheritdoc />
        public override string ToString() => $"{Op} {TargetId}";

        static void Require(bool condition, string targetId, OverrideOp op, string what)
        {
            if (!condition)
            {
                throw new ArgumentException($"A {op} override on '{targetId}' needs {what}.");
            }
        }
    }
}
