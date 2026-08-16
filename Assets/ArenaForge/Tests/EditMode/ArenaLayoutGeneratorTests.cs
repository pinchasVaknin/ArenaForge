using System;
using System.Collections.Generic;
using System.Linq;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The layout generator. The determinism suites here are the ones the override system rests on:
    /// if the same parameters can produce two different documents, an edit recorded against one of
    /// them is meaningless.
    /// </summary>
    public sealed class ArenaLayoutGeneratorTests
    {
        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static WorldDoc Generate(ulong seed) =>
            ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.SampleCatalog());

        static IReadOnlyList<PlacedObject> Structures(WorldDoc doc) =>
            doc.GeneratedObjects.Where(o => o.Tags.Contains("structure")).ToList();

        static PlacedObject Single(WorldDoc doc, string tag) =>
            doc.GeneratedObjects.Single(o => o.Tags.Contains(tag));

        static CatalogEntry EntryFor(Catalog catalog, string logicalId) =>
            catalog.Entries.Single(e => e.LogicalId == logicalId);

        /// <summary>
        /// The world-space footprint of a placed object. Recovering the rotation from the pose only
        /// works because placement is restricted to the quarter-turn table.
        /// </summary>
        static Rect2 WorldFootprint(PlacedObject placed, Catalog catalog)
        {
            int turns = -1;
            for (int i = 0; i < QuarterTurn.Count; i++)
            {
                if (QuarterTurn.Rotation(i) == placed.Pose.Rotation)
                {
                    turns = i;
                }
            }

            Assert.That(turns, Is.GreaterThanOrEqualTo(0),
                $"{placed.StableId} is rotated off the quarter-turn table: {placed.Pose.Rotation}");

            return QuarterTurn
                .Rotate(EntryFor(catalog, placed.LogicalId).Footprint, turns)
                .Translated(placed.Pose.Position.Xz);
        }

        [Test]
        public void AMapHasTwoSpawnMarkersAndTwoStructures()
        {
            WorldDoc doc = Generate(20260816UL);

            Assert.That(doc.GeneratedObjects.Count, Is.EqualTo(4));
            Assert.That(doc.GeneratedObjects.Select(o => o.StableId), Is.Unique);
            Assert.That(doc.Overrides, Is.Empty);
            Assert.That(doc.Parameters.Seed, Is.EqualTo(20260816UL));

            Assert.That(
                doc.GeneratedObjects.Where(o => o.Tags.Contains("spawn")).Select(o => o.StableId),
                Is.EqualTo(new[] { "map/spawn_a/marker", "map/spawn_b/marker" }));
            Assert.That(Structures(doc).Count, Is.EqualTo(2));
            Assert.That(Single(doc, "structure/building").StableId, Is.EqualTo("map/lane_mid/structure_00"));
            Assert.That(Single(doc, "structure/house").StableId, Does.StartWith("map/lane_")
                .And.EndsWith("/structure_00"));
        }

        [Test]
        public void TheHouseGoesInAFlankLane()
        {
            for (ulong seed = 1; seed <= 100; seed++)
            {
                WorldDoc doc = Generate(seed);
                string lane = Single(doc, "structure/house").Metadata[ArenaLayoutGenerator.LaneKey];

                Assert.That(lane, Is.Not.EqualTo("lane_mid"), $"seed {seed}");
            }
        }

        [Test]
        public void AGeneratedMapResolvesWithNoOrphans()
        {
            ResolvedWorld resolved = Generate(42UL).Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(4));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void TheSameParametersProduceByteIdenticalDocumentsAcrossAHundredRuns()
        {
            string expected = ArenaJson.SerializeWorld(Generate(20260816UL));

            for (int run = 0; run < 100; run++)
            {
                Assert.That(ArenaJson.SerializeWorld(Generate(20260816UL)), Is.EqualTo(expected),
                    $"run {run} produced a different document");
            }
        }

        [Test]
        public void AGeneratedMapSurvivesASaveAndLoad()
        {
            WorldDoc original = Generate(7UL);

            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(original));

            WorldAssert.AreDeepEqual(original, restored);
        }

        [Test]
        public void DifferentSeedsProduceDifferentStructurePositions()
        {
            var buildings = new List<Vec3>();
            var houses = new List<Vec3>();

            for (ulong seed = 1; seed <= 50; seed++)
            {
                WorldDoc doc = Generate(seed);
                Vec3 building = Single(doc, "structure/building").Pose.Position;
                Vec3 house = Single(doc, "structure/house").Pose.Position;

                if (!buildings.Contains(building))
                {
                    buildings.Add(building);
                }

                if (!houses.Contains(house))
                {
                    houses.Add(house);
                }
            }

            Assert.That(buildings.Count, Is.GreaterThanOrEqualTo(20),
                "50 seeds should not keep putting the building in the same few places");
            Assert.That(houses.Count, Is.GreaterThanOrEqualTo(20),
                "50 seeds should not keep putting the house in the same few places");
        }

        [Test]
        public void StructuresNeverOverlapAndNeverLeaveThePlayfield()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 200; seed++)
            {
                ArenaParams parameters = Params(seed);
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                IReadOnlyList<PlacedObject> structures = Structures(doc);
                for (int i = 0; i < structures.Count; i++)
                {
                    Rect2 footprint = WorldFootprint(structures[i], catalog);

                    Assert.That(layout.Playfield.Contains(footprint), Is.True,
                        $"seed {seed}: {structures[i].StableId} at {footprint} leaves {layout.Playfield}");
                    Assert.That(footprint.Overlaps(layout.SpawnAreaA), Is.False, $"seed {seed}: in spawn A");
                    Assert.That(footprint.Overlaps(layout.SpawnAreaB), Is.False, $"seed {seed}: in spawn B");

                    for (int j = i + 1; j < structures.Count; j++)
                    {
                        Assert.That(footprint.Overlaps(WorldFootprint(structures[j], catalog)), Is.False,
                            $"seed {seed}: {structures[i].StableId} overlaps {structures[j].StableId}");
                    }
                }
            }
        }

        [Test]
        public void StructuresSitOnTheGrid()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 100; seed++)
            {
                ArenaParams parameters = Params(seed);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                foreach (PlacedObject structure in Structures(ArenaLayoutGenerator.Generate(parameters, catalog)))
                {
                    Assert.That(layout.Grid.IsOnGrid(structure.Pose.Position.Xz), Is.True,
                        $"seed {seed}: {structure.StableId} at {structure.Pose.Position} is off the grid");
                    Assert.That(structure.Pose.Position.Y, Is.EqualTo(0f), "structures sit on the ground");
                    Assert.That(structure.Pose.Scale, Is.EqualTo(1f), "art is placed, never rescaled");
                }
            }
        }

        [Test]
        public void SpawnsAreSeparatedByMostOfTheLongAxis()
        {
            var sizes = new[] { new Vec2(60f, 60f), new Vec2(40f, 80f), new Vec2(80f, 40f), new Vec2(50f, 90f) };

            foreach (Vec2 size in sizes)
            {
                for (ulong seed = 1; seed <= 50; seed++)
                {
                    var parameters = new ArenaParams { Seed = seed, PlayfieldSize = size };
                    WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, TestWorlds.SampleCatalog());

                    Vec3 a = doc.GeneratedObjects.Single(o => o.StableId == "map/spawn_a/marker").Pose.Position;
                    Vec3 b = doc.GeneratedObjects.Single(o => o.StableId == "map/spawn_b/marker").Pose.Position;

                    float longAxis = MathF.Max(size.X, size.Y);
                    Assert.That(Vec3.Distance(a, b), Is.GreaterThanOrEqualTo(longAxis * 0.7f),
                        $"{size} seed {seed}: spawns are too close");
                }
            }
        }

        [Test]
        public void SpawnMarkersRecordTheirTeamAndArea()
        {
            WorldDoc doc = Generate(11UL);
            ArenaLayout layout = ArenaLayout.Build(Params(11UL));

            PlacedObject a = doc.GeneratedObjects.Single(o => o.StableId == "map/spawn_a/marker");
            PlacedObject b = doc.GeneratedObjects.Single(o => o.StableId == "map/spawn_b/marker");

            Assert.That(a.Metadata[ArenaLayoutGenerator.TeamKey], Is.EqualTo("a"));
            Assert.That(b.Metadata[ArenaLayoutGenerator.TeamKey], Is.EqualTo("b"));
            Assert.That(RectMetadata.Parse(a.Metadata[ArenaLayoutGenerator.SpawnAreaKey]),
                Is.EqualTo(layout.SpawnAreaA));
            Assert.That(RectMetadata.Parse(b.Metadata[ArenaLayoutGenerator.SpawnAreaKey]),
                Is.EqualTo(layout.SpawnAreaB));
            Assert.That(a.Pose.Position, Is.EqualTo(layout.SpawnAreaA.Center.ToVec3(0f)));
            Assert.That(b.Pose.Rotation, Is.EqualTo(QuarterTurn.Rotation(2)), "spawn B faces spawn A");
        }

        [Test]
        public void EveryStructureDeclaresDoorwaysOnItsOwnFootprint()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 100; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                foreach (PlacedObject structure in Structures(doc))
                {
                    Rect2 footprint = WorldFootprint(structure, catalog);
                    int count = int.Parse(structure.Metadata[ArenaLayoutGenerator.DoorwayCountKey]);
                    Assert.That(count, Is.EqualTo(2), $"seed {seed}: {structure.StableId}");

                    for (int i = 0; i < count; i++)
                    {
                        string key = ArenaLayoutGenerator.DoorwayKeyPrefix + i.ToString("00");
                        Rect2 doorway = RectMetadata.Parse(structure.Metadata[key]);

                        Assert.That(doorway.Overlaps(footprint), Is.True,
                            $"seed {seed}: {structure.StableId} {key} at {doorway} misses {footprint}");
                        Assert.That(footprint.Contains(doorway), Is.False,
                            $"seed {seed}: {structure.StableId} {key} is buried inside the structure");
                        Assert.That(doorway.Width, Is.GreaterThan(0f).And.LessThanOrEqualTo(footprint.Width));
                    }
                }
            }
        }

        [Test]
        public void ALowerStructureDensityChoosesASmallerBuilding()
        {
            Catalog catalog = TwoBuildingCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc sparse = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, StructureDensity = 0.05f }, catalog);

                Assert.That(Single(sparse, "structure/building").LogicalId,
                    Is.EqualTo("structure/building/hut_01"),
                    $"seed {seed}: a density this low leaves no room for the large building");
            }

            var chosen = new List<string>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc dense = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, StructureDensity = 1f }, catalog);
                string logicalId = Single(dense, "structure/building").LogicalId;
                if (!chosen.Contains(logicalId))
                {
                    chosen.Add(logicalId);
                }
            }

            Assert.That(chosen.Count, Is.EqualTo(2), "at full density both buildings should be reachable");
        }

        [Test]
        public void ACatalogMissingAStructureFailsWithAReadableMessage()
        {
            var catalog = new Catalog(new[]
            {
                new CatalogEntry("marker/spawn", new[] { "marker", "spawn" }, new Rect2(-1f, -1f, 1f, 1f), 0.5f, 1f, null),
            });

            var error = Assert.Throws<InvalidOperationException>(
                () => ArenaLayoutGenerator.Generate(Params(1UL), catalog));

            Assert.That(error.Message, Does.Contain(ArenaLayoutGenerator.BuildingTag));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(
                () => ArenaLayoutGenerator.Generate(null, TestWorlds.SampleCatalog()));
            Assert.Throws<ArgumentNullException>(
                () => ArenaLayoutGenerator.Generate(Params(1UL), null));
        }

        static Catalog TwoBuildingCatalog() => new Catalog(new[]
        {
            new CatalogEntry(
                "marker/spawn", new[] { "marker", "spawn" }, new Rect2(-1f, -1f, 1f, 1f), 0.5f, 1f, null),
            new CatalogEntry(
                "structure/building/two_storey_01",
                new[] { "structure", "structure/building" },
                new Rect2(-6f, -5f, 6f, 5f),
                6f,
                1f,
                null),
            new CatalogEntry(
                "structure/building/hut_01",
                new[] { "structure", "structure/building" },
                new Rect2(-2f, -2f, 2f, 2f),
                3f,
                1f,
                null),
            new CatalogEntry(
                "structure/house/small_01",
                new[] { "structure", "structure/house" },
                new Rect2(-4f, -3f, 4f, 3f),
                3.5f,
                1f,
                null),
        });
    }
}
