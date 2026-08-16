using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ArenaForge.Core
{
    /// <summary>Which side of its threshold a metric has to stay on.</summary>
    public enum MetricBound
    {
        /// <summary>The value must be at least the threshold.</summary>
        AtLeast,

        /// <summary>The value must be at most the threshold.</summary>
        AtMost,
    }

    /// <summary>
    /// One metric that did not meet its threshold, and by how much.
    /// </summary>
    /// <remarks>
    /// The margin is carried rather than left for the reader to subtract because this is what a
    /// failing property test prints. "CoverCoverage 0.79, needs at least 0.80, short by 0.01" says
    /// the generator drifted; the same line reading 0.31 says it broke.
    /// </remarks>
    public readonly struct MetricFailure
    {
        /// <summary>Creates a failure record.</summary>
        public MetricFailure(string metric, float value, float threshold, MetricBound bound)
        {
            Metric = metric;
            Value = value;
            Threshold = threshold;
            Bound = bound;
        }

        /// <summary>Name of the metric that failed.</summary>
        public string Metric { get; }

        /// <summary>What it measured.</summary>
        public float Value { get; }

        /// <summary>What it had to reach or stay under.</summary>
        public float Threshold { get; }

        /// <summary>Which side of the threshold the value had to be on.</summary>
        public MetricBound Bound { get; }

        /// <summary>How far the wrong side of the threshold the value fell.</summary>
        public float Margin => Bound == MetricBound.AtLeast ? Threshold - Value : Value - Threshold;

        /// <inheritdoc />
        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1:0.###}, needs {2} {3:0.###} (out by {4:0.###})",
            Metric, Value, Bound == MetricBound.AtLeast ? "at least" : "at most", Threshold, Margin);
    }

    /// <summary>
    /// What a flood fill from spawn A can reach.
    /// </summary>
    /// <remarks>
    /// A procedural map can produce a building that seals a lane or a pocket of floor with no way
    /// in, and neither shows up in any of the other metrics: a walled-off corner is beautifully
    /// sheltered and perfectly covered. This is the metric that catches a map nobody can play.
    /// </remarks>
    public readonly struct ConnectivityReport
    {
        /// <summary>Creates a connectivity result.</summary>
        public ConnectivityReport(
            bool spawnsConnected, int doorwayCount, int reachableDoorwayCount, float reachableFraction)
        {
            SpawnsConnected = spawnsConnected;
            DoorwayCount = doorwayCount;
            ReachableDoorwayCount = reachableDoorwayCount;
            ReachableFraction = reachableFraction;
        }

        /// <summary>True if a player can walk from spawn A to spawn B.</summary>
        public bool SpawnsConnected { get; }

        /// <summary>How many doorways the map's structures declare.</summary>
        public int DoorwayCount { get; }

        /// <summary>How many of them can be walked to from spawn A.</summary>
        public int ReachableDoorwayCount { get; }

        /// <summary>
        /// Share of the declared doorways that can be reached. One when a map has no doorways at
        /// all — nothing is unreachable in that case.
        /// </summary>
        public float DoorwayReachableFraction =>
            DoorwayCount > 0 ? (float)ReachableDoorwayCount / DoorwayCount : 1f;

        /// <summary>Share of the walkable floor reachable from spawn A.</summary>
        public float ReachableFraction { get; }
    }

    /// <summary>
    /// Everything measurable about a generated map, each figure beside the threshold it has to
    /// meet.
    /// </summary>
    /// <remarks>
    /// This is the object the editor's validation panel renders and the property suite asserts on.
    /// It is deliberately a plain result: it measures, it does not repair. A map that fails is
    /// still returned in full, because which metric failed and by how much is the only useful
    /// thing to say about a bad seed.
    /// </remarks>
    public sealed class MapReport
    {
        readonly List<MetricFailure> _failures = new List<MetricFailure>();

        /// <summary>Creates a report and evaluates every metric against its threshold.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MapReport(
            ExposureMap exposure,
            MapThresholds thresholds,
            float spawnSeparation,
            float minSpawnSeparation,
            float exposureAsymmetry,
            float coverCoverage,
            float maxOpenSightline,
            float maxAllowedOpenSightline,
            ConnectivityReport connectivity,
            PlacementStats placementStats)
        {
            Exposure = exposure ?? throw new ArgumentNullException(nameof(exposure));
            Thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
            PlacementStats = placementStats ?? throw new ArgumentNullException(nameof(placementStats));

            SpawnSeparation = spawnSeparation;
            MinSpawnSeparation = minSpawnSeparation;
            ExposureAsymmetry = exposureAsymmetry;
            CoverCoverage = coverCoverage;
            MaxOpenSightline = maxOpenSightline;
            MaxAllowedOpenSightline = maxAllowedOpenSightline;
            Connectivity = connectivity;

            Check("SpawnSeparation", spawnSeparation, minSpawnSeparation, MetricBound.AtLeast);
            Check("ExposureAsymmetry", exposureAsymmetry, thresholds.MaxExposureAsymmetry, MetricBound.AtMost);
            Check("CoverCoverage", coverCoverage, thresholds.MinCoverCoverage, MetricBound.AtLeast);
            Check("MaxOpenSightline", maxOpenSightline, maxAllowedOpenSightline, MetricBound.AtMost);
            Check(
                "SpawnsConnected", connectivity.SpawnsConnected ? 1f : 0f, 1f, MetricBound.AtLeast);
            Check(
                "DoorwaysReachable", connectivity.DoorwayReachableFraction,
                thresholds.MinDoorwayReachableFraction, MetricBound.AtLeast);
            Check(
                "ReachableFraction", connectivity.ReachableFraction,
                thresholds.MinReachableFraction, MetricBound.AtLeast);
        }

        /// <summary>Exposure of every walkable cell, and the heatmap that comes from it.</summary>
        public ExposureMap Exposure { get; }

        /// <summary>The thresholds this report was judged against.</summary>
        public MapThresholds Thresholds { get; }

        /// <summary>Distance between the two spawn centres, in metres.</summary>
        public float SpawnSeparation { get; }

        /// <summary>
        /// The separation the threshold works out to for this playfield, in metres — the fraction
        /// in <see cref="MapThresholds.MinSpawnSeparationFraction"/> times the long axis.
        /// </summary>
        public float MinSpawnSeparation { get; }

        /// <summary>
        /// How differently the two spawns are overlooked: the gap between the mean exposure around
        /// spawn A and the mean exposure around spawn B.
        /// </summary>
        public float ExposureAsymmetry { get; }

        /// <summary>Share of the walkable floor within reach of a piece of cover.</summary>
        public float CoverCoverage { get; }

        /// <summary>Longest unobstructed sightline found between two walkable cells, in metres.</summary>
        public float MaxOpenSightline { get; }

        /// <summary>
        /// The sightline the threshold works out to for this playfield, in metres — the fraction in
        /// <see cref="MapThresholds.MaxOpenSightlineFraction"/> times the playfield's diagonal.
        /// </summary>
        public float MaxAllowedOpenSightline { get; }

        /// <summary>What a player leaving spawn A can reach.</summary>
        public ConnectivityReport Connectivity { get; }

        /// <summary>What the cover placer tried and what stopped it, read back from the document.</summary>
        public PlacementStats PlacementStats { get; }

        /// <summary>Every metric that missed its threshold, in the order they are checked.</summary>
        public IReadOnlyList<MetricFailure> Failures => _failures;

        /// <summary>True if every metric met its threshold.</summary>
        public bool IsPlayable => _failures.Count == 0;

        /// <summary>
        /// A multi-line summary of every metric, its threshold and its verdict — the form this
        /// takes in a test failure message or a console dump.
        /// </summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0} ({1} failing)", IsPlayable ? "playable" : "NOT playable", _failures.Count)
                .AppendLine();

            Line(text, "SpawnSeparation", SpawnSeparation, MetricBound.AtLeast, MinSpawnSeparation);
            Line(text, "ExposureAsymmetry", ExposureAsymmetry, MetricBound.AtMost, Thresholds.MaxExposureAsymmetry);
            Line(text, "CoverCoverage", CoverCoverage, MetricBound.AtLeast, Thresholds.MinCoverCoverage);
            Line(text, "MaxOpenSightline", MaxOpenSightline, MetricBound.AtMost, MaxAllowedOpenSightline);
            Line(text, "SpawnsConnected", Connectivity.SpawnsConnected ? 1f : 0f, MetricBound.AtLeast, 1f);
            Line(
                text, "DoorwaysReachable", Connectivity.DoorwayReachableFraction, MetricBound.AtLeast,
                Thresholds.MinDoorwayReachableFraction);
            Line(
                text, "ReachableFraction", Connectivity.ReachableFraction, MetricBound.AtLeast,
                Thresholds.MinReachableFraction);

            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "  exposure          min {0:0.###} mean {1:0.###} max {2:0.###} over {3} observers, {4} cells",
                Exposure.Min, Exposure.Mean, Exposure.Max, Exposure.ObserverCount, Exposure.Walkable.Count)
                .AppendLine();
            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "  placement         {0} attempted, {1} accepted, {2} rejected",
                PlacementStats.Attempted, PlacementStats.Accepted, PlacementStats.Rejected)
                .AppendLine();

            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString() => IsPlayable
            ? "playable"
            : $"not playable: {string.Join("; ", _failures)}";

        static bool Meets(float value, float threshold, MetricBound bound) =>
            bound == MetricBound.AtLeast ? value >= threshold : value <= threshold;

        static void Line(StringBuilder text, string name, float value, MetricBound bound, float threshold)
        {
            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "  {0,-17} {1,8:0.###}   {2} {3:0.###}   {4}",
                name, value, bound == MetricBound.AtLeast ? "at least" : "at most", threshold,
                Meets(value, threshold, bound) ? "pass" : "FAIL")
                .AppendLine();
        }

        void Check(string metric, float value, float threshold, MetricBound bound)
        {
            if (!Meets(value, threshold, bound))
            {
                _failures.Add(new MetricFailure(metric, value, threshold, bound));
            }
        }
    }
}
