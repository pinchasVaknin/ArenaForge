namespace ArenaForge.Core
{
    /// <summary>
    /// How a map is measured: the eye the visibility test looks through, how many places it looks
    /// from, and the two radii the metrics are summarised over.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="ArenaParams"/> and not serialised into a world document.
    /// These describe the measurement, not the map: changing the observer count must not change
    /// what the generator produces, and a document that recorded them would invite exactly that
    /// confusion.
    /// </remarks>
    public sealed class AnalysisParams
    {
        /// <summary>Height a standing player's eyes are at, in metres.</summary>
        /// <remarks>
        /// This is the threshold that separates cover you can shoot over from cover you cannot.
        /// An occluder counts only if it spans this height — see
        /// <see cref="Occluder.TryCreate"/> — so raising it turns a map of high barriers into an
        /// open one.
        /// </remarks>
        public float EyeHeight { get; set; } = 1.6f;

        /// <summary>
        /// How many walkable cells the exposure of every other cell is measured against.
        /// </summary>
        /// <remarks>
        /// A sample rather than every cell. Full pairwise visibility on a sixty-metre arena is
        /// about thirteen million segment tests per map, which is affordable once and not
        /// affordable a thousand times; three hundred observers is roughly a tenth of the floor and
        /// settles the per-cell fraction to within a couple of percent, which is finer than any
        /// threshold here cares about.
        /// </remarks>
        public int ObserverSamples { get; set; } = 300;

        /// <summary>
        /// Radius around a spawn centre that the exposure comparison between the two spawns is
        /// taken over, in metres.
        /// </summary>
        /// <remarks>
        /// Ten metres is about the first few seconds out of a spawn — the stretch where being seen
        /// before you can react is the difference between a fair start and a spawn trap.
        /// </remarks>
        public float SpawnAnalysisRadius { get; set; } = 10f;

        /// <summary>
        /// How far a cell may be from a piece of cover and still count as covered, in metres.
        /// </summary>
        /// <remarks>
        /// Six metres is about a second and a half of sprinting: near enough that a player caught
        /// in the open there has somewhere to reach.
        /// </remarks>
        public float CoverRadius { get; set; } = 6f;

        /// <summary>Creates an independent copy.</summary>
        public AnalysisParams Clone() => new AnalysisParams
        {
            EyeHeight = EyeHeight,
            ObserverSamples = ObserverSamples,
            SpawnAnalysisRadius = SpawnAnalysisRadius,
            CoverRadius = CoverRadius,
        };
    }
}
