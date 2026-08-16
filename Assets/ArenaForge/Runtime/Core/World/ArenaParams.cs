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

        /// <summary>
        /// How much cover to scatter, as a multiple of the baseline density. 1 is the baseline;
        /// doubling it asks for twice as many props on the same open floor, and 0 asks for none.
        /// </summary>
        /// <remarks>
        /// The count is derived from the floor a lane actually has left after its structures and
        /// spawn clearances are taken out, not from the lane's full area — otherwise the lane with
        /// the building in it would be asked for as much cover as an empty one.
        /// </remarks>
        [JsonProperty("coverDensity", Order = 5)]
        public float CoverDensity { get; set; } = 1f;

        /// <summary>
        /// How many pieces of low cover the generator places for each piece of high cover.
        /// </summary>
        /// <remarks>
        /// Low cover you can shoot over and high cover you cannot, so this is the knob that
        /// decides whether a map plays open or claustrophobic. The default of 2 is the usual
        /// two-to-one starting point.
        /// </remarks>
        [JsonProperty("lowToHighCoverRatio", Order = 6)]
        public float LowToHighCoverRatio { get; set; } = 2f;

        /// <summary>
        /// Whether cover may be rotated to any <see cref="YawStep"/> rather than only to quarter
        /// turns.
        /// </summary>
        /// <remarks>
        /// Off by default: quarter turns keep footprints exactly axis-aligned, and a map of
        /// square-on crates reads as deliberate. Turning it on trades that for variety, and costs
        /// a little placement density because an off-axis footprint is tested against the box
        /// around it. The steps are fifteen degrees rather than a free angle because a free angle
        /// needs trigonometry, and trigonometry is not bit-identical across runtimes — see
        /// <see cref="YawStep"/>.
        /// </remarks>
        [JsonProperty("fineCoverRotation", Order = 7)]
        public bool FineCoverRotation { get; set; }

        /// <summary>Creates an independent copy.</summary>
        public ArenaParams Clone() => new ArenaParams
        {
            Seed = Seed,
            PlayfieldSize = PlayfieldSize,
            LaneCount = LaneCount,
            GridSize = GridSize,
            StructureDensity = StructureDensity,
            CoverDensity = CoverDensity,
            LowToHighCoverRatio = LowToHighCoverRatio,
            FineCoverRotation = FineCoverRotation,
        };
    }
}
