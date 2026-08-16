namespace ArenaForge.Core
{
    /// <summary>
    /// The line between a map worth playing and one worth throwing away, one number per metric.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every threshold here is a judgement about arena design rather than a fact about the code,
    /// which is why they are a settable object and not constants: a designer who wants tighter maps
    /// than the defaults should be able to say so without editing Core. The reasoning behind each
    /// default is in REPORT.md.
    /// </para>
    /// <para>
    /// They are also the contract the property suite holds the generator to. A default that had to
    /// be loosened to get a green run would mean the generator regressed, not that the number was
    /// wrong, so they are chosen with headroom over the worst seed of a thousand and then left
    /// alone.
    /// </para>
    /// </remarks>
    public sealed class MapThresholds
    {
        /// <summary>
        /// Least acceptable distance between the two spawns, as a fraction of the playfield's long
        /// axis. Observed across seeds 1..1000 of the default map: 0.88, on every seed.
        /// </summary>
        public float MinSpawnSeparationFraction { get; set; } = 0.7f;

        /// <summary>
        /// Most the two spawns' surroundings may differ in mean exposure before the map favours a
        /// side. Observed across seeds 1..1000 of the default map: 0.00 to 0.22, mean 0.06.
        /// </summary>
        public float MaxExposureAsymmetry { get; set; } = 0.25f;

        /// <summary>
        /// Least fraction of the walkable floor that must be within reach of a piece of cover.
        /// Observed across seeds 1..1000 of the default map: 0.64 to 0.79, mean 0.73.
        /// </summary>
        /// <remarks>
        /// Lower than it looks because the two spawn strips are a quarter of the floor and are
        /// deliberately kept clear of props; four fifths of the uncovered floor is spawn ground.
        /// Off-spawn coverage on the same seeds runs about 0.93. See REPORT.md.
        /// </remarks>
        public float MinCoverCoverage { get; set; } = 0.6f;

        /// <summary>
        /// Longest unobstructed sightline the map may contain, as a fraction of the playfield's
        /// diagonal. Observed across seeds 1..1000 of the default map: 0.81 to 0.91.
        /// </summary>
        /// <remarks>
        /// A fraction rather than a distance so the number means the same thing on a playfield of
        /// any size, and of the diagonal rather than the long axis because the longest line across
        /// a rectangle is its diagonal.
        /// </remarks>
        public float MaxOpenSightlineFraction { get; set; } = 0.95f;

        /// <summary>
        /// Least fraction of the walkable floor that must be reachable from spawn A. Observed
        /// across seeds 1..1000 of the default map: 1.00, on every seed.
        /// </summary>
        public float MinReachableFraction { get; set; } = 0.95f;

        /// <summary>Least fraction of the declared structure doorways that must be reachable from spawn A.</summary>
        public float MinDoorwayReachableFraction { get; set; } = 1f;

        /// <summary>Creates an independent copy.</summary>
        public MapThresholds Clone() => new MapThresholds
        {
            MinSpawnSeparationFraction = MinSpawnSeparationFraction,
            MaxExposureAsymmetry = MaxExposureAsymmetry,
            MinCoverCoverage = MinCoverCoverage,
            MaxOpenSightlineFraction = MaxOpenSightlineFraction,
            MinReachableFraction = MinReachableFraction,
            MinDoorwayReachableFraction = MinDoorwayReachableFraction,
        };
    }
}
