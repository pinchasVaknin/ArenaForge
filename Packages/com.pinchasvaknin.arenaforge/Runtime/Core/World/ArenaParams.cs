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

        /// <summary>
        /// Peak-to-trough height variation of the ground, in metres. Zero is a flat map.
        /// </summary>
        /// <remarks>
        /// Zero by default, deliberately. The heightfield decides where objects stand, not what
        /// the ground looks like — the tool composes prefabs and does not author a mesh — so a map
        /// with hills in it and nothing rendering them would put its crates in mid-air. Give the
        /// map a Unity terrain to write the field into and then turn this up; see
        /// <see cref="TerrainField"/>.
        /// </remarks>
        [JsonProperty("terrainAmplitude", Order = 8)]
        public float TerrainAmplitude { get; set; }

        /// <summary>How wide the largest ground features are, in metres.</summary>
        /// <remarks>
        /// Roughly the width of one rise or hollow at the coarsest octave. The default is a third
        /// of the default playfield, so a map has a handful of features rather than one dome or a
        /// field of bumps.
        /// </remarks>
        [JsonProperty("terrainFeatureSize", Order = 9)]
        public float TerrainFeatureSize { get; set; } = 20f;

        /// <summary>
        /// How many redundant connections the road network lays over the one route it needs, as a
        /// multiple of the baseline. Zero disables the road stage entirely.
        /// </summary>
        /// <remarks>
        /// Zero by default, for the reason <see cref="TerrainAmplitude"/> is: turning it up
        /// changes the output of every map that already exists, and nothing renders a road yet.
        /// Until there is something to draw one with, every map is exactly the roadless map this
        /// tool generated before these parameters existed — and the three below describe roads
        /// that are not laid until this one is turned up.
        /// </remarks>
        [JsonProperty("roadDensity", Order = 10)]
        public float RoadDensity { get; set; }

        /// <summary>Carriageway width of a trunk road, in metres.</summary>
        /// <remarks>
        /// Metres rather than a count of lanes, because a road here is a strip of ground with a
        /// width and not a thing with markings on it. The default of 4 is two vehicles abreast,
        /// which is what makes an artery read as the route through a map rather than as a wide
        /// path.
        /// </remarks>
        [JsonProperty("arteryWidth", Order = 11)]
        public float ArteryWidth { get; set; } = 4f;

        /// <summary>Carriageway width of a branch road, in metres.</summary>
        /// <remarks>
        /// Half the artery by default. The gap between the two is what tells a player which way is
        /// the way through, so the pair is worth setting together: a path as wide as its artery is
        /// a network with no hierarchy in it.
        /// </remarks>
        [JsonProperty("pathWidth", Order = 12)]
        public float PathWidth { get; set; } = 2f;

        /// <summary>Steepest slope a road may be graded to, as rise over run.</summary>
        /// <remarks>
        /// <para>
        /// Rise over run rather than degrees: a ratio is two heights and a distance divided, and
        /// an angle is a call into trigonometry, which is not bit-identical across runtimes — the
        /// same argument <see cref="YawStep"/> makes.
        /// </para>
        /// <para>
        /// <strong>It is what a road is graded to, and not what the ground under it may be.</strong>
        /// The surface anybody drives on is never steeper than this — the grading holds that, and
        /// the validation suite asserts it on every profile. Ground steeper than the limit is not
        /// shut to the router: it costs <c>RoadNetwork.ClimbDetour</c> cells to cross, so a route
        /// takes any way round a bank it can find and climbs one only when there is no way round.
        /// </para>
        /// <para>
        /// <strong>It was a wall, and a wall could cut a spawn off the map.</strong> A cell over the
        /// limit was impassable outright, which does not slow a route over rough ground, it deletes
        /// the ground — and with it, sometimes, the only way to a spawn. Over forty seeds on a
        /// hundred-metre field at twenty metres of relief, a quarter stranded a spawn on 40 maps of
        /// 40 and six tenths on 3 of 40; priced instead of forbidden, it is 0 of 40 at every limit,
        /// and the road laid per map at a quarter goes from 92.5 m to 371 m.
        /// </para>
        /// <para>
        /// <strong>What the climb costs is earthworks.</strong> Over seeds 1..1000 on the validation
        /// sweep's own ground, 0.137% of the road laid crosses ground steeper than the limit — 396 m
        /// of 289,798 — always one cell at a time, because the smoothing still refuses to straighten
        /// a line along a bank. The grading cuts up to 2.76 m to carry it there.
        /// </para>
        /// <para>
        /// <strong>Six in ten, and the number is a measurement rather than a taste.</strong> It was
        /// a quarter, which is a gentle road and far too gentle a filter. Over sixty seeds at twelve
        /// metres of relief, a quarter left the network spanning half the map; at four tenths that is
        /// 88% of the map and at six tenths 91.7%, which is the same span flat ground gives. Six is
        /// chosen over four because it is the one that stops being the binding constraint rather than
        /// the one that just clears the bar. It is now a preference rather than a wall, so the cost
        /// of setting it low is a road cut deeper into the hill and not a road that is not there.
        /// </para>
        /// </remarks>
        [JsonProperty("maxRoadGradient", Order = 13)]
        public float MaxRoadGradient { get; set; } = 0.6f;

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
            TerrainAmplitude = TerrainAmplitude,
            TerrainFeatureSize = TerrainFeatureSize,
            RoadDensity = RoadDensity,
            ArteryWidth = ArteryWidth,
            PathWidth = PathWidth,
            MaxRoadGradient = MaxRoadGradient,
        };
    }
}
