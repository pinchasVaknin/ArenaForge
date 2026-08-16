using System;
using System.Collections.Generic;
using System.Linq;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Cover placement, mostly as property tests over two hundred seeds.
    /// </summary>
    /// <remarks>
    /// A single seed proves nothing here. Placement is a search, and a search that happens to
    /// produce a legal map for one seed can still put a crate through a wall on the next one, so
    /// the suites below assert the properties that have to hold for every map the generator can
    /// produce rather than the output of one it did.
    /// </remarks>
    public sealed class CoverPlacementTests
    {
        const int Seeds = 200;

        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static WorldDoc Generate(ulong seed, Catalog catalog) =>
            ArenaLayoutGenerator.Generate(Params(seed), catalog);

        static int TargetOf(WorldDoc doc) => int.Parse(doc.Metadata[CoverPlacer.TargetKey]);

        [Test]
        public void NoTwoFootprintsOverlap()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                List<PlacedObject> occupants = PlacedGeometry.GroundOccupants(Generate(seed, catalog));

                for (int i = 0; i < occupants.Count; i++)
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(occupants[i], catalog);
                    for (int j = i + 1; j < occupants.Count; j++)
                    {
                        Rect2 other = PlacedGeometry.WorldFootprint(occupants[j], catalog);

                        Assert.That(footprint.Overlaps(other), Is.False,
                            $"seed {seed}: {occupants[i].StableId} at {footprint} overlaps " +
                            $"{occupants[j].StableId} at {other}");
                    }
                }
            }
        }

        [Test]
        public void NoPropBlocksADoorway()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed, catalog);
                List<Rect2> doorways = PlacedGeometry.Doorways(doc);
                Assert.That(doorways, Is.Not.Empty, $"seed {seed}: no doorways to test against");

                foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(prop, catalog);
                    for (int i = 0; i < doorways.Count; i++)
                    {
                        Rect2 blocked = doorways[i].Expanded(CoverPlacer.DoorwayClearance);

                        Assert.That(footprint.Overlaps(blocked), Is.False,
                            $"seed {seed}: {prop.StableId} at {footprint} blocks the doorway at {doorways[i]}");
                    }
                }
            }
        }

        [Test]
        public void TheCoverCountLandsOnTheDensityTarget()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            foreach (float density in new[] { 0.5f, 1f, 2f })
            {
                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    WorldDoc doc = ArenaLayoutGenerator.Generate(
                        new ArenaParams { Seed = seed, CoverDensity = density }, catalog);

                    int target = TargetOf(doc);
                    int placed = PlacedGeometry.GroundCover(doc).Count;

                    Assert.That(Math.Abs(placed - target), Is.LessThanOrEqualTo(target * 0.15f),
                        $"density {density} seed {seed}: placed {placed} against a target of {target}");
                }
            }
        }

        /// <summary>
        /// The target the suite above measures against is the generator's own figure, so this one
        /// checks the figure itself is sane: a sixty-metre arena should come out with dozens of
        /// props, not four and not four hundred.
        /// </summary>
        [Test]
        public void ADefaultMapIsPopulatedWithoutBeingCrowded()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 50; seed++)
            {
                int placed = PlacedGeometry.GroundCover(Generate(seed, catalog)).Count;

                Assert.That(placed, Is.InRange(25, 55), $"seed {seed} placed {placed} props");
            }
        }

        [Test]
        public void TheLowToHighRatioMatchesTheParameter()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            foreach (float ratio in new[] { 2f, 4f })
            {
                int low = 0;
                int high = 0;

                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    WorldDoc doc = ArenaLayoutGenerator.Generate(
                        new ArenaParams { Seed = seed, LowToHighCoverRatio = ratio }, catalog);

                    foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
                    {
                        if (prop.Tags.Contains(CoverPlacer.LowCoverTag))
                        {
                            low++;
                        }
                        else if (prop.Tags.Contains(CoverPlacer.HighCoverTag))
                        {
                            high++;
                        }
                    }
                }

                Assert.That(high, Is.GreaterThan(0), $"ratio {ratio}: no high cover at all");

                // A tenth of the ratio, which over this many props is far outside the sampling
                // noise: the two-stage pick makes the ratio a property of the parameter, not of
                // how many low-cover entries the catalog happens to carry.
                Assert.That((float)low / high, Is.EqualTo(ratio).Within(ratio * 0.1f),
                    $"ratio {ratio}: {low} low against {high} high");
            }
        }

        [Test]
        public void EverySeedProducesAByteIdenticalDocumentTwice()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                Assert.That(
                    ArenaJson.SerializeWorld(Generate(seed, catalog)),
                    Is.EqualTo(ArenaJson.SerializeWorld(Generate(seed, catalog))),
                    $"seed {seed} generated two different documents");
            }
        }

        [Test]
        public void CoverSitsOnTheGridInsideItsOwnLane()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                ArenaParams parameters = Params(seed);
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
                {
                    string laneId = prop.Metadata[ArenaLayoutGenerator.LaneKey];
                    ArenaLane lane = layout.Lanes.Single(l => l.Id == laneId);
                    Rect2 footprint = PlacedGeometry.WorldFootprint(prop, catalog);

                    Assert.That(prop.StableId, Does.StartWith($"map/{laneId}/cover_"), $"seed {seed}");
                    Assert.That(layout.Grid.IsOnGrid(prop.Pose.Position.Xz), Is.True,
                        $"seed {seed}: {prop.StableId} at {prop.Pose.Position} is off the grid");
                    Assert.That(prop.Pose.Position.Y, Is.EqualTo(0f), "ground cover sits on the ground");
                    Assert.That(prop.Pose.Scale, Is.EqualTo(1f), "art is placed, never rescaled");
                    Assert.That(lane.Band.Contains(footprint), Is.True,
                        $"seed {seed}: {prop.StableId} at {footprint} leaves {laneId} at {lane.Band}");
                    Assert.That(layout.Playfield.Contains(footprint), Is.True,
                        $"seed {seed}: {prop.StableId} leaves the playfield");
                }
            }
        }

        [Test]
        public void CoverKeepsClearOfSpawnsAndStructures()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                ArenaParams parameters = Params(seed);
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                var structures = new List<Rect2>();
                foreach (PlacedObject placed in doc.GeneratedObjects)
                {
                    if (PlacedGeometry.IsStructure(placed))
                    {
                        structures.Add(PlacedGeometry.WorldFootprint(placed, catalog));
                    }
                }

                foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(prop, catalog);

                    Assert.That(footprint.Overlaps(layout.SpawnAreaA.Expanded(CoverPlacer.SpawnClearance)),
                        Is.False, $"seed {seed}: {prop.StableId} crowds spawn A");
                    Assert.That(footprint.Overlaps(layout.SpawnAreaB.Expanded(CoverPlacer.SpawnClearance)),
                        Is.False, $"seed {seed}: {prop.StableId} crowds spawn B");

                    for (int i = 0; i < structures.Count; i++)
                    {
                        Assert.That(Rect2.Distance(footprint, structures[i]),
                            Is.GreaterThanOrEqualTo(CoverPlacer.StructureClearance),
                            $"seed {seed}: {prop.StableId} leaves no room to walk past a structure");
                    }
                }
            }
        }

        [Test]
        public void PropsAreSpacedFarEnoughApartToWalkBetween()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 50; seed++)
            {
                List<PlacedObject> cover = PlacedGeometry.GroundCover(Generate(seed, catalog));

                for (int i = 0; i < cover.Count; i++)
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(cover[i], catalog);
                    for (int j = i + 1; j < cover.Count; j++)
                    {
                        Rect2 other = PlacedGeometry.WorldFootprint(cover[j], catalog);
                        if (cover[i].Metadata[ArenaLayoutGenerator.LaneKey] !=
                            cover[j].Metadata[ArenaLayoutGenerator.LaneKey])
                        {
                            continue;
                        }

                        Assert.That(Rect2.Distance(footprint, other),
                            Is.GreaterThanOrEqualTo(CoverPlacer.PropMargin),
                            $"seed {seed}: {cover[i].StableId} and {cover[j].StableId} are jammed together");
                    }
                }
            }
        }

        [Test]
        public void EveryLaneGetsCover()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 50; seed++)
            {
                ArenaParams parameters = Params(seed);
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                var lanes = new List<string>();
                foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
                {
                    string lane = prop.Metadata[ArenaLayoutGenerator.LaneKey];
                    if (!lanes.Contains(lane))
                    {
                        lanes.Add(lane);
                    }
                }

                Assert.That(lanes.Count, Is.EqualTo(layout.Lanes.Count),
                    $"seed {seed}: only {lanes.Count} of {layout.Lanes.Count} lanes got any cover");
            }
        }

        [Test]
        public void CoverIdsAreUniqueAndNumberedFromZeroWithinALane()
        {
            WorldDoc doc = Generate(20260816UL, TestWorlds.SampleCatalog());

            Assert.That(doc.GeneratedObjects.Select(o => o.StableId), Is.Unique);

            var perLane = new Dictionary<string, List<string>>();
            foreach (PlacedObject prop in PlacedGeometry.GroundCover(doc))
            {
                string lane = prop.Metadata[ArenaLayoutGenerator.LaneKey];
                if (!perLane.ContainsKey(lane))
                {
                    perLane[lane] = new List<string>();
                }

                perLane[lane].Add(prop.StableId);
            }

            foreach (KeyValuePair<string, List<string>> lane in perLane)
            {
                for (int i = 0; i < lane.Value.Count; i++)
                {
                    Assert.That(lane.Value[i], Is.EqualTo($"map/{lane.Key}/cover_{i:00}"));
                }
            }
        }

        [Test]
        public void APropOnASocketNestsUnderTheObjectItSitsOn()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            int found = 0;

            for (ulong seed = 1; seed <= 50; seed++)
            {
                WorldDoc doc = Generate(seed, catalog);

                foreach (PlacedObject prop in doc.GeneratedObjects)
                {
                    if (!PlacedGeometry.IsSocketProp(prop))
                    {
                        continue;
                    }

                    found++;
                    int split = prop.StableId.IndexOf("/socket_", StringComparison.Ordinal);
                    string parentId = prop.StableId.Substring(0, split);
                    PlacedObject parent = doc.GeneratedObjects.Single(o => o.StableId == parentId);
                    CatalogEntry parentEntry = catalog.Find(parent.LogicalId);

                    Assert.That(prop.StableId, Does.EndWith("/prop_00"));
                    Assert.That(parentEntry.Sockets.Count, Is.GreaterThan(0),
                        $"{parentId} has no sockets to hang {prop.StableId} on");
                    Assert.That(parentEntry.Sockets[0].HasTag(CoverPlacer.PropSurfaceTag), Is.True);
                    Assert.That(prop.Pose, Is.EqualTo(parent.Pose.Transform(parentEntry.Sockets[0].LocalPose)),
                        $"{prop.StableId} is not on its parent's socket");
                    Assert.That(prop.Pose.Position.Y, Is.GreaterThan(0f), "a socket prop is off the ground");
                }
            }

            Assert.That(found, Is.GreaterThan(0), "the sample catalog has a prop surface; nothing used it");
        }

        [Test]
        public void ThePlacementStatisticsAreRecorded()
        {
            WorldDoc doc = Generate(20260816UL, TestWorlds.SampleCatalog());

            int attempted = int.Parse(doc.Metadata[CoverPlacer.StatsPrefix + "attempted"]);
            int accepted = int.Parse(doc.Metadata[CoverPlacer.StatsPrefix + "accepted"]);
            int rejected = int.Parse(doc.Metadata[CoverPlacer.StatsPrefix + "rejected"]);

            Assert.That(accepted, Is.EqualTo(PlacedGeometry.GroundCover(doc).Count));
            Assert.That(attempted - accepted, Is.EqualTo(rejected));
            Assert.That(TargetOf(doc), Is.GreaterThan(0));
        }

        [Test]
        public void RejectionsAreTalliedByTheConstraintThatCausedThem()
        {
            // A density far past what the lanes can hold, so the overlap rule has to start
            // refusing candidates and the tally has somewhere to show it.
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 3UL, CoverDensity = 12f }, TestWorlds.SampleCatalog());

            string key = CoverPlacer.StatsPrefix + "rejected_" +
                         PlacementStats.MetadataName(ConstraintKind.NoOverlap);

            Assert.That(doc.Metadata.ContainsKey(key), Is.True,
                "a saturated map should record which rule turned candidates away");
            Assert.That(int.Parse(doc.Metadata[key]), Is.GreaterThan(0));
            Assert.That(PlacedGeometry.GroundCover(doc).Count, Is.LessThan(TargetOf(doc)),
                "a saturated map cannot reach its target, and that is what the statistics are for");
        }

        [Test]
        public void FineRotationPlacesPropsOffTheQuarterTurns()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            int offAxis = 0;

            for (ulong seed = 1; seed <= 50; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, FineCoverRotation = true }, catalog);

                List<PlacedObject> occupants = PlacedGeometry.GroundOccupants(doc);
                for (int i = 0; i < occupants.Count; i++)
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(occupants[i], catalog);
                    if (PlacedGeometry.IsCover(occupants[i]) &&
                        PlacedGeometry.YawSteps(occupants[i]) % (YawStep.Count / QuarterTurn.Count) != 0)
                    {
                        offAxis++;
                    }

                    for (int j = i + 1; j < occupants.Count; j++)
                    {
                        Assert.That(
                            footprint.Overlaps(PlacedGeometry.WorldFootprint(occupants[j], catalog)),
                            Is.False,
                            $"seed {seed}: {occupants[i].StableId} overlaps {occupants[j].StableId}");
                    }
                }
            }

            Assert.That(offAxis, Is.GreaterThan(0), "fine rotation placed nothing off the quarter turns");
        }

        [Test]
        public void QuarterTurnRotationIsTheDefault()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 50; seed++)
            {
                foreach (PlacedObject prop in PlacedGeometry.GroundCover(Generate(seed, catalog)))
                {
                    Assert.That(PlacedGeometry.YawSteps(prop) % (YawStep.Count / QuarterTurn.Count),
                        Is.EqualTo(0), $"seed {seed}: {prop.StableId} is off the quarter turns");
                }
            }
        }

        [Test]
        public void ADensityOfZeroPlacesNoCover()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 9UL, CoverDensity = 0f }, TestWorlds.SampleCatalog());

            Assert.That(PlacedGeometry.GroundCover(doc), Is.Empty);
            Assert.That(TargetOf(doc), Is.EqualTo(0));
        }

        [Test]
        public void RaisingTheDensityPlacesMoreCover()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            int sparse = 0;
            int dense = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                sparse += PlacedGeometry.GroundCover(ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, CoverDensity = 0.5f }, catalog)).Count;
                dense += PlacedGeometry.GroundCover(ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, CoverDensity = 2f }, catalog)).Count;
            }

            Assert.That(dense, Is.GreaterThan(sparse * 3), $"{sparse} at half density, {dense} at double");
        }

        [Test]
        public void ACatalogWithoutCoverStillProducesAMap()
        {
            var catalog = new Catalog(new[]
            {
                new CatalogEntry("marker/spawn", new[] { "marker", "spawn" }, new Rect2(-1f, -1f, 1f, 1f), 0.5f, 1f, null),
                new CatalogEntry(
                    "structure/building/two_storey_01",
                    new[] { "structure", "structure/building" }, new Rect2(-6f, -5f, 6f, 5f), 6f, 1f, null),
                new CatalogEntry(
                    "structure/house/small_01",
                    new[] { "structure", "structure/house" }, new Rect2(-4f, -3f, 4f, 3f), 3.5f, 1f, null),
            });

            WorldDoc doc = Generate(4UL, catalog);

            Assert.That(doc.GeneratedObjects.Count, Is.EqualTo(4));
            Assert.That(TargetOf(doc), Is.EqualTo(0));
        }

        [Test]
        public void OnlyHighCoverInTheCatalogStillFillsTheMap()
        {
            var catalog = new Catalog(new[]
            {
                new CatalogEntry("marker/spawn", new[] { "marker", "spawn" }, new Rect2(-1f, -1f, 1f, 1f), 0.5f, 1f, null),
                new CatalogEntry(
                    "structure/building/two_storey_01",
                    new[] { "structure", "structure/building" }, new Rect2(-6f, -5f, 6f, 5f), 6f, 1f, null),
                new CatalogEntry(
                    "structure/house/small_01",
                    new[] { "structure", "structure/house" }, new Rect2(-4f, -3f, 4f, 3f), 3.5f, 1f, null),
                new CatalogEntry(
                    "cover/high/barrier_concrete_01",
                    new[] { "cover", "cover/high" }, new Rect2(-1f, -0.25f, 1f, 0.25f), 1.8f, 1f, null),
            });

            WorldDoc doc = Generate(4UL, catalog);

            Assert.That(PlacedGeometry.GroundCover(doc).Count, Is.GreaterThan(10));
            Assert.That(PlacedGeometry.GroundCover(doc)
                .All(o => o.LogicalId == "cover/high/barrier_concrete_01"), Is.True);
        }

        [Test]
        public void ANegativeCoverParameterIsRejected()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 1UL, CoverDensity = -1f }, catalog));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 1UL, LowToHighCoverRatio = -1f }, catalog));
        }

        [Test]
        public void AGeneratedMapWithCoverResolvesWithNoOrphans()
        {
            WorldDoc doc = Generate(20260816UL, TestWorlds.SampleCatalog());

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(doc.GeneratedObjects.Count));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void AMapWithCoverSurvivesASaveAndLoad()
        {
            WorldDoc original = Generate(31UL, TestWorlds.SampleCatalog());

            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(original));

            WorldAssert.AreDeepEqual(original, restored);
        }
    }
}
