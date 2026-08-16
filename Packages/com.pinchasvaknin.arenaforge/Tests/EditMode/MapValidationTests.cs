using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The generator held to its thresholds across a thousand seeds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the suite the analysis layer exists for. A procedural generator is not a function
    /// with a right answer to check; it is a family of a billion maps, and the only honest claim
    /// anyone can make about it is a claim about properties that hold across the family. A thousand
    /// seeds is enough for a one-in-two-hundred defect to show up reliably, and cheap enough to run
    /// every session.
    /// </para>
    /// <para>
    /// A failure here is a generator bug, not a test bug. The thresholds in
    /// <see cref="MapThresholds"/> were set from the measured spread of these same thousand seeds
    /// with headroom on top and are part of the deliverable — loosening one to get a green run
    /// would throw away the only thing this suite is for. REPORT.md records where each came from.
    /// </para>
    /// </remarks>
    public sealed class MapValidationTests
    {
        const int Seeds = 1000;

        /// <summary>How many of the worst seeds a failure message names for inspection by hand.</summary>
        const int WorstSeedsToReport = 8;

        /// <summary>
        /// Measures seeds 1..1000 once and asserts every property that has to hold for all of them.
        /// </summary>
        /// <remarks>
        /// One test rather than six, because the sweep — not the generation — is what costs, and
        /// running it six times over to assert six things about the same maps would buy nothing but
        /// five more minutes. Every property is still checked for every seed, and all of them are
        /// reported together, so one run says everything that is wrong rather than only the first
        /// thing. (Collected by hand rather than with <c>Assert.Multiple</c>, which the NUnit
        /// version Unity ships does not have.)
        /// </remarks>
        [Test]
        public void EverySeedProducesAConnectedCoveredAndEvenHandedMap()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var reports = new MapReport[Seeds + 1];

            // The catalog is immutable once constructed, and the generator and the analyser keep
            // all their state in locals, so the seeds are genuinely independent.
            Parallel.For(1, Seeds + 1, seed =>
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = (ulong)seed }, catalog);
                reports[seed] = MapAnalyzer.Analyze(doc, catalog);
            });

            var thresholds = new MapThresholds();
            var broken = new List<string>();

            Check(
                broken, reports, "spawn B is reachable from spawn A",
                r => r.Connectivity.SpawnsConnected ? 1f : 0f, MetricBound.AtLeast, 1f);

            Check(
                broken, reports, "every declared doorway is reachable from spawn A",
                r => r.Connectivity.DoorwayReachableFraction, MetricBound.AtLeast,
                thresholds.MinDoorwayReachableFraction);

            Check(
                broken, reports, "the walkable floor is reachable from spawn A",
                r => r.Connectivity.ReachableFraction, MetricBound.AtLeast,
                thresholds.MinReachableFraction);

            Check(
                broken, reports, "the two spawns are overlooked about equally",
                r => r.ExposureAsymmetry, MetricBound.AtMost, thresholds.MaxExposureAsymmetry);

            Check(
                broken, reports, "the walkable floor is within reach of cover",
                r => r.CoverCoverage, MetricBound.AtLeast, thresholds.MinCoverCoverage);

            // Sweeps up the two metrics the five above do not name — spawn separation and the
            // longest open sightline — and is what the editor's panel will show.
            CheckEveryMapIsPlayable(broken, reports);

            if (broken.Count > 0)
            {
                Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, broken));
            }
        }

        /// <summary>
        /// Checks a property of every report, describing the worst offenders when it does not hold.
        /// </summary>
        static void Check(
            List<string> broken,
            MapReport[] reports,
            string property,
            Func<MapReport, float> measure,
            MetricBound bound,
            float threshold)
        {
            var offenders = new List<int>();
            for (int seed = 1; seed < reports.Length; seed++)
            {
                float value = measure(reports[seed]);
                bool ok = bound == MetricBound.AtLeast ? value >= threshold : value <= threshold;
                if (!ok)
                {
                    offenders.Add(seed);
                }
            }

            if (offenders.Count == 0)
            {
                return;
            }

            // Worst first, so the seeds worth loading into the editor are the ones printed.
            offenders.Sort((a, b) => bound == MetricBound.AtLeast
                ? measure(reports[a]).CompareTo(measure(reports[b]))
                : measure(reports[b]).CompareTo(measure(reports[a])));

            var message = new StringBuilder();
            message.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0} on {1} of {2} seeds (needs {3} {4:0.###}). Worst seeds:",
                property, offenders.Count, reports.Length - 1,
                bound == MetricBound.AtLeast ? "at least" : "at most", threshold);

            for (int i = 0; i < offenders.Count && i < WorstSeedsToReport; i++)
            {
                message.AppendLine().AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  seed {0}: {1:0.####}", offenders[i], measure(reports[offenders[i]]));
            }

            message.AppendLine().AppendLine().Append(reports[offenders[0]].Describe());

            broken.Add(message.ToString());
        }

        static void CheckEveryMapIsPlayable(List<string> broken, MapReport[] reports)
        {
            var message = new StringBuilder();
            int count = 0;

            for (int seed = 1; seed < reports.Length; seed++)
            {
                if (reports[seed].IsPlayable)
                {
                    continue;
                }

                count++;
                if (count <= WorstSeedsToReport)
                {
                    message.AppendLine().Append("  seed ").Append(seed).Append(": ")
                        .Append(reports[seed].ToString());
                }
            }

            if (count > 0)
            {
                broken.Add(
                    $"{count} of {reports.Length - 1} seeds produced an unplayable map:{message}");
            }
        }
    }
}
