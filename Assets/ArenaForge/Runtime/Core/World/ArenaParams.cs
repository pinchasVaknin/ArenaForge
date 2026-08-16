using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// The generator settings half of a world document. Together with the seed these are the
    /// authoritative description of a map; everything the generator emits is derived from them.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ArenaParams
    {
        /// <summary>Playfield extent in metres, X by Z.</summary>
        [JsonProperty("playfieldSize", Order = 0)]
        public Vec2 PlayfieldSize { get; set; } = new Vec2(60f, 60f);

        /// <summary>Number of lane bands running between the two spawns.</summary>
        [JsonProperty("laneCount", Order = 1)]
        public int LaneCount { get; set; } = 3;

        /// <summary>Placement grid resolution in metres. Structures snap to it.</summary>
        [JsonProperty("gridSize", Order = 2)]
        public float GridSize { get; set; } = 1f;

        /// <summary>Multiplier on how much of the playfield structures take up. 1 is the baseline.</summary>
        [JsonProperty("structureDensity", Order = 3)]
        public float StructureDensity { get; set; } = 1f;

        /// <summary>Creates an independent copy.</summary>
        public ArenaParams Clone() => new ArenaParams
        {
            PlayfieldSize = PlayfieldSize,
            LaneCount = LaneCount,
            GridSize = GridSize,
            StructureDensity = StructureDensity,
        };
    }
}
