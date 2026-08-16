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
        /// <summary>
        /// Seed every generation draw derives from.
        /// </summary>
        /// <remarks>
        /// The seed lives here rather than beside the parameters because it is not a separate
        /// input: the generator takes exactly one argument describing what to build, and a seed
        /// that travelled separately could disagree with the one the document was written from.
        /// </remarks>
        [JsonProperty("seed", Order = 0)]
        public ulong Seed { get; set; }

        /// <summary>Playfield extent in metres, X by Z.</summary>
        [JsonProperty("playfieldSize", Order = 1)]
        public Vec2 PlayfieldSize { get; set; } = new Vec2(60f, 60f);

        /// <summary>Number of lane bands running between the two spawns.</summary>
        [JsonProperty("laneCount", Order = 2)]
        public int LaneCount { get; set; } = 3;

        /// <summary>Placement grid resolution in metres. Structures snap to it.</summary>
        [JsonProperty("gridSize", Order = 3)]
        public float GridSize { get; set; } = 1f;

        /// <summary>
        /// How much of a lane a structure may cover, as a multiple of the baseline share. 1 is the
        /// baseline; halving it restricts the generator to structures half the size.
        /// </summary>
        [JsonProperty("structureDensity", Order = 4)]
        public float StructureDensity { get; set; } = 1f;

        /// <summary>Creates an independent copy.</summary>
        public ArenaParams Clone() => new ArenaParams
        {
            Seed = Seed,
            PlayfieldSize = PlayfieldSize,
            LaneCount = LaneCount,
            GridSize = GridSize,
            StructureDensity = StructureDensity,
        };
    }
}
