using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The analysis layer piece by piece: the walkable set, the flood fill, the exposure sweep and
    /// the report that reads them.
    /// </summary>
    /// <remarks>
    /// The thousand-seed suite next door says the generator produces playable maps. It can only say
    /// that if the measurements mean what they claim to, which is what these check — on maps built
    /// by hand where the right answer is known by inspection rather than by running the code.
    /// </remarks>
    public sealed class MapAnalysisTests
    {
        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static WorldDoc Generate(ulong seed, Catalog catalog) =>
            ArenaLayoutGenerator.Generate(Params(seed), catalog);

        // --- the walkable set -------------------------------------------------------------

        [Test]
        public void StructuresComeOutOfTheWalkableFloorAndCoverDoesNot()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));
            var building = new Rect2(-6f, -5f, 6f, 5f);

            WalkableGrid empty = WalkableGrid.Build(layout, Array.Empty<Rect2>());
            WalkableGrid withBuilding = WalkableGrid.Build(layout, new[] { building });

            Assert.That(empty.Count, Is.EqualTo(layout.Grid.CellCount));
            Assert.That(empty.Count - withBuilding.Count, Is.EqualTo(120),
                "a 12 x 10 m building on a 1 m grid removes 120 cells");
            Assert.That(withBuilding.IndexAt(Vec2.Zero), Is.EqualTo(-1), "the middle of the building");
            Assert.That(withBuilding.IndexAt(new Vec2(-6.5f, 0f)), Is.GreaterThanOrEqualTo(0),
                "the floor just outside its wall");
        }

        [Test]
        public void TheFloodFillStopsAtAWallAndFindsItsWayRoundAGap()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));
            Rect2 playfield = layout.Playfield;

            // A wall clean across the map, then the same wall with a doorway cut in it.
            var sealed_ = new Rect2(playfield.MinX, -1f, playfield.MaxX, 1f);
            WalkableGrid split = WalkableGrid.Build(layout, new[] { sealed_ });
            bool[] reachedAcrossWall = split.ReachableFrom(layout.SpawnAreaA);

            Assert.That(split.AnyReached(layout.SpawnAreaB, reachedAcrossWall), Is.False,
                "a wall across the whole map cuts spawn B off");
            Assert.That(Fraction(reachedAcrossWall), Is.EqualTo(0.5f).Within(0.02f),
                "and leaves about half the floor reachable");

            WalkableGrid gapped = WalkableGrid.Build(layout, new[]
            {
                new Rect2(playfield.MinX, -1f, -2f, 1f),
                new Rect2(2f, -1f, playfield.MaxX, 1f),
            });
            bool[] reachedThroughGap = gapped.ReachableFrom(layout.SpawnAreaA);

            Assert.That(gapped.AnyReached(layout.SpawnAreaB, reachedThroughGap), Is.True,
                "leave a four-metre gap and the far half is reachable again");
            Assert.That(Fraction(reachedThroughGap), Is.EqualTo(1f));
        }

        /// <summary>
        /// Four-connected, not eight. Two rooms touching at one corner are two rooms.
        /// </summary>
        [Test]
        public void ADiagonalPinchIsNotARoute()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));
            Rect2 playfield = layout.Playfield;

            WalkableGrid pinched = WalkableGrid.Build(layout, new[]
            {
                new Rect2(playfield.MinX, -1f, 0f, 0f),
                new Rect2(0f, 0f, playfield.MaxX, 1f),
            });

            Assert.That(
                pinched.AnyReached(layout.SpawnAreaB, pinched.ReachableFrom(layout.SpawnAreaA)),
                Is.False,
                "the two halves meet at a single grid corner, which is not somewhere you can walk");
        }

        // --- the exposure sweep -----------------------------------------------------------

        [Test]
        public void AnEmptyPlayfieldIsCompletelyExposed()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 5UL, CoverDensity = 0f }, catalog);

            // Nothing left but the spawn markers, which are half a metre tall.
            RemoveEverythingTagged(doc, ArenaLayoutGenerator.StructureTag);

            MapReport report = MapAnalyzer.Analyze(doc, catalog);

            Assert.That(report.Exposure.Min, Is.EqualTo(1f), "with nothing to hide behind, every cell is seen");
            Assert.That(report.Exposure.Max, Is.EqualTo(1f));
            Assert.That(report.Exposure.Mean, Is.EqualTo(1f));
            Assert.That(report.MaxOpenSightline, Is.GreaterThan(50f), "and the map is one long sightline");
        }

        [Test]
        public void ABuildingCastsAShadowNothingCanSeeInto()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(11UL, catalog);

            MapReport report = MapAnalyzer.Analyze(doc, catalog);

            Assert.That(report.Exposure.Min, Is.LessThan(0.1f),
                "a map with a two-storey building in it has ground nobody can see");
            Assert.That(report.Exposure.Max, Is.LessThan(1f),
                "and no cell that the whole map can see");
            Assert.That(report.Exposure.Mean,
                Is.GreaterThan(report.Exposure.Min).And.LessThan(report.Exposure.Max));
        }

        [Test]
        public void RaisingTheEyeAboveTheCoverOpensTheMapUp()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(13UL, catalog);

            float atStandingHeight = MapAnalyzer
                .Analyze(doc, catalog, new AnalysisParams { EyeHeight = 1.6f }).Exposure.Mean;
            float aboveTheBarriers = MapAnalyzer
                .Analyze(doc, catalog, new AnalysisParams { EyeHeight = 2.5f }).Exposure.Mean;

            Assert.That(aboveTheBarriers, Is.GreaterThan(atStandingHeight),
                "look over the concrete barriers and more of the map is visible");
        }

        [Test]
        public void AnalysingTheSameDocumentTwiceGivesTheSameNumbers()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(17UL, catalog);

            MapReport first = MapAnalyzer.Analyze(doc, catalog);
            MapReport second = MapAnalyzer.Analyze(doc, catalog);

            Assert.That(first.Exposure.Values, Is.EqualTo(second.Exposure.Values).AsCollection,
                "the observer sample comes from the seeded stream, so it is the same sample twice");
            Assert.That(first.Exposure.ObserverCount, Is.EqualTo(300));
        }

        [Test]
        public void TwoRunsOfTheSameSeedAreMeasuredIdentically()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                MapReport first = MapAnalyzer.Analyze(Generate(seed, catalog), catalog);
                MapReport second = MapAnalyzer.Analyze(Generate(seed, catalog), catalog);

                Assert.That(first.Exposure.Values, Is.EqualTo(second.Exposure.Values).AsCollection,
                    $"seed {seed} measured differently twice");
                Assert.That(second.Describe(), Is.EqualTo(first.Describe()),
                    $"seed {seed} produced two different reports");
            }
        }

        [Test]
        public void TheObserverCountIsAParameterAndMoreOfThemSettlesTheAnswer()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(23UL, catalog);

            MapReport few = MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { ObserverSamples = 60 });
            MapReport many = MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { ObserverSamples = 1200 });

            Assert.That(few.Exposure.ObserverCount, Is.EqualTo(60));
            Assert.That(many.Exposure.ObserverCount, Is.EqualTo(1200));
            Assert.That(few.Exposure.Mean, Is.EqualTo(many.Exposure.Mean).Within(0.05f),
                "a tenth of the observers should already be reading the same map");
        }

        [Test]
        public void AskingForMoreObserversThanThereIsFloorUsesEveryCell()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(3UL, catalog);

            MapReport report = MapAnalyzer.Analyze(
                doc, catalog, new AnalysisParams { ObserverSamples = 100000 });

            Assert.That(report.Exposure.ObserverCount, Is.EqualTo(report.Exposure.Walkable.Count));
        }

        // --- the metrics ------------------------------------------------------------------

        [Test]
        public void TheMetricsMeasureTheResolvedMapAndNotTheGeneratedOne()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(29UL, catalog);

            MapReport before = MapAnalyzer.Analyze(doc, catalog);
            int walkableBefore = before.Exposure.Walkable.Count;

            RemoveEverythingTagged(doc, ArenaLayoutGenerator.StructureTag);
            MapReport after = MapAnalyzer.Analyze(doc, catalog);

            Assert.That(after.Exposure.Walkable.Count, Is.GreaterThan(walkableBefore),
                "deleting the buildings by hand gives the floor they stood on back");
            Assert.That(after.Exposure.Mean, Is.GreaterThan(before.Exposure.Mean),
                "and opens the map up");
            Assert.That(before.Connectivity.DoorwayCount, Is.GreaterThan(0));
            Assert.That(after.Connectivity.DoorwayCount, Is.EqualTo(0),
                "a building deleted by hand takes its doorways with it");
        }

        [Test]
        public void MeasuringAMapDoesNotChangeIt()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(31UL, catalog);
            string before = ArenaJson.SerializeWorld(doc);

            MapAnalyzer.Analyze(doc, catalog);

            Assert.That(ArenaJson.SerializeWorld(doc), Is.EqualTo(before));
        }

        [Test]
        public void TheReportCarriesThePlacementStatisticsBackOutOfTheDocument()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(37UL, catalog);

            PlacementStats stats = MapAnalyzer.Analyze(doc, catalog).PlacementStats;

            Assert.That(stats.Attempted.ToString(),
                Is.EqualTo(doc.Metadata[CoverPlacer.StatsPrefix + "attempted"]));
            Assert.That(stats.Accepted.ToString(),
                Is.EqualTo(doc.Metadata[CoverPlacer.StatsPrefix + "accepted"]));
            Assert.That(stats.Rejected, Is.EqualTo(stats.Attempted - stats.Accepted));
        }

        [Test]
        public void ASaturatedMapCarriesItsRejectionTallyIntoTheReport()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 3UL, CoverDensity = 12f }, catalog);

            PlacementStats stats = MapAnalyzer.Analyze(doc, catalog).PlacementStats;

            Assert.That(stats.RejectedBy(ConstraintKind.NoOverlap), Is.GreaterThan(0),
                "a map this crowded rejects candidates for overlap, and the report should say so");
        }

        [Test]
        public void CoverCoverageCountsTheFloorWithinReachOfAProp()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(41UL, catalog);

            float near = MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { CoverRadius = 2f }).CoverCoverage;
            float far = MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { CoverRadius = 12f }).CoverCoverage;

            Assert.That(near, Is.LessThan(far), "widen the radius and more of the floor counts as covered");
            Assert.That(near, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(far, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void AMapWithNoCoverAtAllHasNoCoverage()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 43UL, CoverDensity = 0f }, catalog);

            MapReport report = MapAnalyzer.Analyze(doc, catalog);

            Assert.That(report.CoverCoverage, Is.EqualTo(0f));
            Assert.That(report.IsPlayable, Is.False, "and that is not a map worth playing");
            Assert.That(Failed(report, "CoverCoverage"), Is.True);
        }

        // --- the report -------------------------------------------------------------------

        [Test]
        public void ImpossibleThresholdsFailWithAReadableMargin()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(47UL, catalog);

            MapReport report = MapAnalyzer.Analyze(doc, catalog, null, new MapThresholds
            {
                MinCoverCoverage = 1.5f,
                MaxOpenSightlineFraction = 0.01f,
            });

            Assert.That(report.IsPlayable, Is.False);
            Assert.That(Failed(report, "CoverCoverage"), Is.True);
            Assert.That(Failed(report, "MaxOpenSightline"), Is.True);

            MetricReading coverage = FailureFor(report, "CoverCoverage");
            Assert.That(coverage.Bound, Is.EqualTo(MetricBound.AtLeast));
            Assert.That(coverage.Margin, Is.EqualTo(1.5f - report.CoverCoverage).Within(1e-5f));
            Assert.That(coverage.ToString(), Does.Contain("CoverCoverage").And.Contain("at least"));

            Assert.That(report.Describe(), Does.Contain("NOT playable").And.Contain("FAIL"));
        }

        [Test]
        public void ADefaultMapPassesEveryThreshold()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            MapReport report = MapAnalyzer.Analyze(Generate(20260816UL, catalog), catalog);

            Assert.That(report.IsPlayable, Is.True, report.Describe());
            Assert.That(report.Failures, Is.Empty);
            Assert.That(report.Describe(), Does.Contain("playable").And.Contain("pass"));
        }

        [Test]
        public void SpawnSeparationIsMeasuredAgainstThePlayfieldAndNotAFixedDistance()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            MapReport small = MapAnalyzer.Analyze(
                ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = 2UL, PlayfieldSize = new Vec2(40f, 40f) }, catalog), catalog);
            MapReport large = MapAnalyzer.Analyze(Generate(2UL, catalog), catalog);

            Assert.That(small.MinSpawnSeparation, Is.EqualTo(0.7f * 40f).Within(1e-4f));
            Assert.That(large.MinSpawnSeparation, Is.EqualTo(0.7f * 60f).Within(1e-4f));
            Assert.That(small.SpawnSeparation, Is.GreaterThanOrEqualTo(small.MinSpawnSeparation));
            Assert.That(large.SpawnSeparation, Is.GreaterThanOrEqualTo(large.MinSpawnSeparation));
        }

        // --- the heatmap ------------------------------------------------------------------

        [Test]
        public void TheGrayscaleIsTheGridWithTheBlockedCellsLeftOut()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(53UL, catalog);
            ExposureMap exposure = MapAnalyzer.Analyze(doc, catalog).Exposure;

            float[,] image = exposure.ToGrayscale();
            ArenaGrid grid = exposure.Walkable.Grid;

            Assert.That(image.GetLength(0), Is.EqualTo(grid.CountX));
            Assert.That(image.GetLength(1), Is.EqualTo(grid.CountZ));

            int blocked = 0;
            for (int x = 0; x < grid.CountX; x++)
            {
                for (int z = 0; z < grid.CountZ; z++)
                {
                    int index = exposure.Walkable.IndexAt(x, z);
                    if (index < 0)
                    {
                        blocked++;
                        Assert.That(float.IsNaN(image[x, z]), Is.True,
                            $"cell {x},{z} is under a structure and should have no exposure at all");
                    }
                    else
                    {
                        Assert.That(image[x, z], Is.EqualTo(exposure.At(index)));
                        Assert.That(image[x, z], Is.InRange(0f, 1f));
                    }
                }
            }

            Assert.That(blocked, Is.GreaterThan(0), "the map has structures; some cells must be blocked");
        }

        // --- guards -----------------------------------------------------------------------

        [Test]
        public void NonsenseAnalysisParametersAreRejected()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = Generate(1UL, catalog);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { EyeHeight = 0f }));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { ObserverSamples = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MapAnalyzer.Analyze(doc, catalog, new AnalysisParams { CoverRadius = -1f }));
            Assert.Throws<ArgumentNullException>(() => MapAnalyzer.Analyze(null, catalog));
            Assert.Throws<ArgumentNullException>(() => MapAnalyzer.Analyze(doc, null));
        }

        static float Fraction(bool[] reached)
        {
            int count = 0;
            for (int i = 0; i < reached.Length; i++)
            {
                if (reached[i])
                {
                    count++;
                }
            }

            return (float)count / reached.Length;
        }

        static bool Failed(MapReport report, string metric)
        {
            for (int i = 0; i < report.Failures.Count; i++)
            {
                if (report.Failures[i].Metric == metric)
                {
                    return true;
                }
            }

            return false;
        }

        static MetricReading FailureFor(MapReport report, string metric)
        {
            for (int i = 0; i < report.Failures.Count; i++)
            {
                if (report.Failures[i].Metric == metric)
                {
                    return report.Failures[i];
                }
            }

            Assert.Fail($"{metric} did not fail; the report says {report}");
            return default;
        }

        /// <summary>Records a Delete override for every generated object carrying the tag.</summary>
        static void RemoveEverythingTagged(WorldDoc doc, string tag)
        {
            var removed = new List<string>();
            foreach (PlacedObject placed in doc.GeneratedObjects)
            {
                for (int i = 0; i < placed.Tags.Count; i++)
                {
                    if (placed.Tags[i] == tag)
                    {
                        removed.Add(placed.StableId);
                        break;
                    }
                }
            }

            Assert.That(removed, Is.Not.Empty, $"nothing in this map is tagged '{tag}'");
            foreach (string id in removed)
            {
                doc.Overrides.Add(EditOverride.Delete(id));
            }
        }
    }
}
