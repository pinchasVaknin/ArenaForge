using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// One storey of a building: the seed its contents are drawn from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seed is stored rather than derived at generation time, and that is the whole point of
    /// the type. Forking a floor's stream from the building seed on every run would make
    /// re-rolling one floor impossible: the only thing a re-roll could change is the building
    /// seed, and every floor would move. With the seed in the document, re-rolling floor 2 writes
    /// one number and floors 1 and 3 serialise to the bytes they already had.
    /// </para>
    /// <para>
    /// Its <em>initial</em> value is still a fork of the building seed, so a building nobody has
    /// re-rolled is fully determined by its seed and parameters, exactly as a map is.
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BuildingFloor
    {
        /// <summary>Creates a floor.</summary>
        [JsonConstructor]
        public BuildingFloor(ulong seed)
        {
            Seed = seed;
        }

        /// <summary>Seed this floor's contents are drawn from.</summary>
        [JsonProperty("seed", Order = 0)]
        public ulong Seed { get; }

        /// <summary>
        /// The id segment a floor at <paramref name="index"/> contributes to a stable id.
        /// </summary>
        /// <remarks>
        /// One-based, unlike every other index in the project, because a floor number is read by
        /// a person: the ground floor is floor 1, and <c>building/floor_00/cover_00</c> would be
        /// the only id in the tool that had to be mentally incremented before it meant anything.
        /// </remarks>
        public static string IdOf(int index) =>
            "floor_" + (index + 1).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A building: parameters, one seed per storey, what the generator produced, and the manual
    /// edits layered on top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate type from <see cref="WorldDoc"/> rather than a generalisation of it. The two
    /// share exactly one thing — applying an override list to a list of placed objects, which is
    /// <see cref="OverrideResolution"/> — and disagree about everything else: a map has
    /// parameters and a building has parameters and a floor list, a map's ids start at
    /// <c>map/</c> and a building's at <c>building/</c>, and only one of them is analysed. See
    /// ARCHITECTURE.md section 2 for the decision and what the alternative would have cost.
    /// </para>
    /// <para>
    /// It is a document, not a saved object, on the same terms as a map: the editable source is
    /// this JSON, and the GameObjects are rebuilt from it. An <em>exported</em> building is not —
    /// see <c>BuildingExport</c>. That prefab is baked art with no floors left in it.
    /// </para>
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BuildingDoc
    {
        /// <summary>Schema revision written into building JSON.</summary>
        /// <remarks>
        /// Its own number line. A building document has never had another shape, so this starts
        /// at 1 while <see cref="WorldDoc.CurrentSchemaVersion"/> is at 2; the <c>kind</c> field
        /// is what keeps the two from being read as each other.
        /// </remarks>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Value of the <c>kind</c> field a building document carries.</summary>
        public const string BuildingKind = "building";

        /// <summary>Stable id prefix every object in a building lives under.</summary>
        public const string IdPrefix = "building";

        /// <summary>Schema revision this document was written with.</summary>
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>What kind of document this is. Always <see cref="BuildingKind"/>.</summary>
        [JsonProperty("kind", Order = 1)]
        public string Kind => BuildingKind;

        /// <summary>Generator settings the generated objects were produced with.</summary>
        [JsonProperty("parameters", Order = 2)]
        public BuildingParams Parameters { get; set; } = new BuildingParams();

        /// <summary>
        /// One entry per storey, from the ground up. Its length is the building's real floor
        /// count; <see cref="BuildingParams.FloorCount"/> is what the next generation will use.
        /// </summary>
        [JsonProperty("floors", Order = 3)]
        public List<BuildingFloor> Floors { get; } = new List<BuildingFloor>();

        /// <summary>What the generator produced, in generation order.</summary>
        [JsonProperty("generatedObjects", Order = 4)]
        public List<PlacedObject> GeneratedObjects { get; } = new List<PlacedObject>();

        /// <summary>Manual edits, applied in list order.</summary>
        [JsonProperty("overrides", Order = 5)]
        public List<EditOverride> Overrides { get; } = new List<EditOverride>();

        /// <summary>Generator annotations about the building as a whole.</summary>
        [JsonProperty("metadata", Order = 6)]
        public IDictionary<string, string> Metadata { get; } =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Suppresses empty metadata in serialised output.</summary>
        public bool ShouldSerializeMetadata() => Metadata.Count > 0;

        /// <summary>
        /// How far the roof and its parapet stand above the top storey's ceiling plane, in metres.
        /// Zero when the catalog had nothing to cap the building with.
        /// </summary>
        /// <remarks>
        /// Read back out of the metadata the generator wrote rather than recomputed, because it is
        /// a fact about the art that was drawn: how thick the slab came out and how tall the
        /// parapet is are not in the parameters, and a document written before there was a roof
        /// says nothing about one.
        /// </remarks>
        public float RoofHeight =>
            Metadata.TryGetValue(BuildingGenerator.RoofHeightKey, out string text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float height)
                ? height
                : 0f;

        /// <summary>Height of the whole building: its storeys, and whatever caps them.</summary>
        /// <remarks>
        /// The roof counts because this is the height an exported building is bound into a catalog
        /// with, and a row that under-reports by a parapet is wrong in the same way a footprint
        /// that under-reported would be.
        /// </remarks>
        public float Height => Floors.Count * Parameters.FloorHeight + RoofHeight;

        /// <summary>The declared ground footprint, centred on the building's origin.</summary>
        public Rect2 Footprint => Rect2.FromCenterSize(Vec2.Zero, Parameters.FootprintSize);

        /// <summary>Stable id prefix everything on a floor lives under, for example <c>building/floor_02</c>.</summary>
        public static string FloorIdPrefix(int index) => IdPrefix + "/" + BuildingFloor.IdOf(index);

        /// <summary>
        /// Applies the overrides to the generated objects, exactly as a map does.
        /// </summary>
        /// <exception cref="InvalidOperationException">Two generated objects share a stable id.</exception>
        public ResolvedWorld Resolve() => OverrideResolution.Apply(GeneratedObjects, Overrides);
    }
}
