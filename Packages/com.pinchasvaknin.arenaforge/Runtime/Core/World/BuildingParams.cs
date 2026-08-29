using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// The generator settings half of a building document. Together with the per-floor seeds in
    /// <see cref="BuildingDoc.Floors"/> these are the authoritative description of a building.
    /// </summary>
    /// <remarks>
    /// The seed lives here, as it does in <see cref="ArenaParams"/>, and for the same reason: the
    /// generator takes one argument describing what to build. Unlike a map, though, the seed is
    /// not the only stored randomness — it is the value the floor seeds are first derived from,
    /// after which each floor's seed is its own. See <see cref="BuildingDoc.Floors"/>.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BuildingParams
    {
        /// <summary>Seed the initial floor seeds are derived from.</summary>
        [JsonProperty("seed", Order = 0)]
        public ulong Seed { get; set; }

        /// <summary>Ground footprint in metres, X by Z, centred on the building's origin.</summary>
        [JsonProperty("footprintSize", Order = 1)]
        public Vec2 FootprintSize { get; set; } = new Vec2(12f, 10f);

        /// <summary>Number of storeys.</summary>
        [JsonProperty("floorCount", Order = 2)]
        public int FloorCount { get; set; } = 3;

        /// <summary>Vertical distance between one floor's slab and the next, in metres.</summary>
        /// <remarks>
        /// <para>
        /// The storey pitch, not the headroom: the slab is laid at this height and everything
        /// else on the floor stands on top of it, so what a wall or a crate has to fit under is
        /// this less the slab's own thickness. A catalog entry taller than that is not offered to
        /// a floor, because it would stand through the floor above — see
        /// <see cref="BuildingGenerator"/>.
        /// </para>
        /// <para>
        /// <strong>3.1 rather than a round 3, and the tenth of a metre is the slab.</strong> Every
        /// piece of wall art this package ships — the demo pack's panel and doorway, and the window
        /// <c>ArenaAssetBuilder</c> writes to match them — is 2.9 m tall, and the floor tile beside
        /// them is 0.2 m thick. A 3 m storey leaves 2.8 m of headroom, so all three are refused, and
        /// a storey with no wall art is not a storey with slightly wrong walls: it is one undivided
        /// room with no envelope at all, its contents standing under a slab held up by nothing. The
        /// default has to be a pitch the art in the box fits under, or the first building anybody
        /// generates is a stack of floating floors.
        /// </para>
        /// </remarks>
        [JsonProperty("floorHeight", Order = 3)]
        public float FloorHeight { get; set; } = 3.1f;

        /// <summary>Placement grid resolution in metres. Contents snap to it.</summary>
        /// <remarks>
        /// Finer than a map's default metre, because a floor is a room-sized space and a metre
        /// grid across twelve metres leaves very few distinct positions to place into.
        /// </remarks>
        [JsonProperty("gridSize", Order = 4)]
        public float GridSize { get; set; } = 0.5f;

        /// <summary>
        /// Clear floor kept between the contents and the outside walls, in metres.
        /// </summary>
        /// <remarks>
        /// The building's own walls are art rather than placed objects, so this is what keeps the
        /// contents from ending up flush against them and, in a building that is later exported,
        /// what keeps the prefab's footprint inside the footprint it declares.
        /// </remarks>
        [JsonProperty("wallMargin", Order = 5)]
        public float WallMargin { get; set; } = 0.5f;

        /// <summary>
        /// How much to put on a floor, as a multiple of the baseline density. 1 is the baseline;
        /// 0 asks for an empty shell.
        /// </summary>
        [JsonProperty("contentDensity", Order = 6)]
        public float ContentDensity { get; set; } = 1f;

        /// <summary>Creates an independent copy.</summary>
        public BuildingParams Clone() => new BuildingParams
        {
            Seed = Seed,
            FootprintSize = FootprintSize,
            FloorCount = FloorCount,
            FloorHeight = FloorHeight,
            GridSize = GridSize,
            WallMargin = WallMargin,
            ContentDensity = ContentDensity,
        };
    }
}
