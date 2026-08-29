using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// A map: a seed, the parameters it was generated with, what the generator produced, and the
    /// manual edits layered on top.
    /// </summary>
    /// <remarks>
    /// This is the whole persistence story. A map is not a saved scene — it is this document,
    /// a few kilobytes of readable JSON, from which the scene is rebuilt on demand. See
    /// ARCHITECTURE.md section 2 for why.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WorldDoc
    {
        /// <summary>Schema revision written into world JSON.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// Oldest schema revision this build still reads.
        /// </summary>
        /// <remarks>
        /// Version 1 is version 2 without the <see cref="Kind"/> field, which is why it can be
        /// read unchanged: a file that does not say what it is, in a build where a map was the
        /// only thing a document could be, is a map.
        /// </remarks>
        public const int MinReadableSchemaVersion = 1;

        /// <summary>Value of the <c>kind</c> field a world document carries.</summary>
        public const string WorldKind = "world";

        /// <summary>Schema revision this document was written with.</summary>
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// What kind of document this is. Always <see cref="WorldKind"/>.
        /// </summary>
        /// <remarks>
        /// There are two kinds of document now, and they have the same shape from
        /// <c>generatedObjects</c> down. Without this field a building loaded through
        /// <see cref="ArenaJson.DeserializeWorld"/> would come back as a map with default
        /// parameters rather than as an error — the quiet failure the schema version check exists
        /// to prevent, one level up.
        /// </remarks>
        [JsonProperty("kind", Order = 1)]
        public string Kind => WorldKind;

        /// <summary>
        /// Generator settings the generated objects were produced with, seed included.
        /// </summary>
        [JsonProperty("parameters", Order = 2)]
        public ArenaParams Parameters { get; set; } = new ArenaParams();

        /// <summary>What the generator produced, in generation order.</summary>
        [JsonProperty("generatedObjects", Order = 3)]
        public List<PlacedObject> GeneratedObjects { get; } = new List<PlacedObject>();

        /// <summary>Manual edits, applied in list order.</summary>
        [JsonProperty("overrides", Order = 4)]
        public List<EditOverride> Overrides { get; } = new List<EditOverride>();

        /// <summary>
        /// Generator annotations about the document as a whole, as opposed to about one object —
        /// currently the cover placement statistics.
        /// </summary>
        /// <remarks>
        /// Sorted with an ordinal comparer for the same reason <see cref="PlacedObject.Metadata"/>
        /// is: this is serialised, and a <c>Dictionary</c> makes no promise about the order it
        /// enumerates in.
        /// </remarks>
        [JsonProperty("metadata", Order = 5)]
        public IDictionary<string, string> Metadata { get; } =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Suppresses empty metadata in serialised output.</summary>
        public bool ShouldSerializeMetadata() => Metadata.Count > 0;

        /// <summary>
        /// Applies the overrides to the generated objects and returns the result, together with
        /// any override that could not be applied.
        /// </summary>
        /// <remarks>
        /// Overrides are applied in list order, so an edit is evaluated against the state the
        /// preceding edits left behind: a Move targeting something an earlier Delete removed is an
        /// orphan, not an error. The document itself is not mutated.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Two generated objects share a stable id.</exception>
        public ResolvedWorld Resolve() => OverrideResolution.Apply(GeneratedObjects, Overrides);
    }
}
