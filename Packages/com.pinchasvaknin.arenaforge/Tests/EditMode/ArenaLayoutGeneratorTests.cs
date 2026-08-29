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

        static IReadOnlyList<PlacedObject> Tagged(WorldDoc doc, string tag) =>
            doc.GeneratedObjects.Where(o => o.Tags.Contains(tag)).ToList();

        static Rect2 WorldFootprint(PlacedObject placed, Catalog catalog) =>
            PlacedGeometry.WorldFootprint(placed, catalog);

        [Test]
        public void AMapHasTwoSpawnMarkersAndTheStructuresTheCompositionRuleAsksFor()
        {
            WorldDoc doc = Generate(20260816UL);

            Assert.That(doc.GeneratedObjects.Select(o => o.StableId), Is.Unique);
            Assert.That(doc.Overrides, Is.Empty);
            Assert.That(doc.Parameters.Seed, Is.EqualTo(20260816UL));

            Assert.That(
                doc.GeneratedObjects.Where(o => o.Tags.Contains("spawn")).Select(o => o.StableId),
                Is.EqualTo(new[] { "map/spawn_a/marker", "map/spawn_b/marker" }));
            Assert.That(Structures(doc).Count, Is.EqualTo(3));
            Assert.That(Single(doc, "structure/building").StableId, Is.EqualTo("map/lane_mid/structure_00"));

            foreach (PlacedObject house in Tagged(doc, "structure/house"))
            {
                Assert.That(house.StableId, Does.StartWith("map/lane_").And.EndsWith("/structure_00"));
            }
        }

        /// <remarks>
        /// A flank each, not merely a flank. The composition rule asks for one house per flank, and
        /// two of them in the same lane would be two anchors on one route and none on the other —
        /// which is the thing the shuffle in <c>FlankLanes</c> exists to rule out, and the thing a
        /// weighted draw per house would have got wrong about one map in two.
        /// </remarks>
        /// <remarks>
        /// A property of the default map rather than of every map, and the layout cells are why: a
        /// sixty-metre playfield divides into one cell per lane — see
        /// <see cref="ArenaLayoutGenerator.StructureCells"/> — so a lane there holds one structure
        /// or none, and the one in the middle lane is the map's anchor. A four-hundred-metre map
        /// puts a dozen cells in every lane and this says nothing about it.
        /// </remarks>
        [Test]
        public void EveryLaneOfTheDefaultMapAnchorsOneStructureAtMost()
        {
            for (ulong seed = 1; seed <= 100; seed++)
            {
                WorldDoc doc = Generate(seed);
                var lanes = new List<string>();

                foreach (PlacedObject structure in Tagged(doc, ArenaLayoutGenerator.StructureTag))
                {
                    string lane = structure.Metadata[ArenaLayoutGenerator.LaneKey];
                    Assert.That(lanes, Does.Not.Contain(lane),
                        $"seed {seed}: two structures in {lane}");
                    lanes.Add(lane);
                }

                Assert.That(lanes, Does.Contain("lane_mid"), $"seed {seed}: the middle lane is bare");
            }
        }

        [Test]
        public void AGeneratedMapResolvesWithNoOrphans()
        {
            WorldDoc doc = Generate(42UL);

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(doc.GeneratedObjects.Count));
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

                if (!buildings.Contains(building))
                {
                    buildings.Add(building);
                }

                foreach (PlacedObject placed in Tagged(doc, "structure/house"))
                {
                    if (!houses.Contains(placed.Pose.Position))
                    {
                        houses.Add(placed.Pose.Position);
                    }
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
                    Assert.That(PlacedGeometry.YawSteps(structure) % (YawStep.Count / QuarterTurn.Count),
                        Is.EqualTo(0), $"seed {seed}: {structure.StableId} is off the quarter turns");
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

        /// <remarks>
        /// The rectangles a row declares are the ones a map keeps clear, and they arrive turned the
        /// way the structure was turned. A door modelled into the north wall of a house is in the
        /// house's east wall once the map has laid it across a lane, and a clearance kept where the
        /// row said it was is a clearance in front of the wrong wall — which is exactly the failure
        /// the derived pair used to be: a gap in the cover in front of a blank facade, and crates
        /// stacked against the actual door.
        /// </remarks>
        [Test]
        public void AStructureThatDeclaresItsDoorwaysGetsThoseAndNotTheDerivedPair()
        {
            var declared = new[] { new Rect2(-1f, -3.5f, 1f, -2.5f), new Rect2(-1f, 2.5f, 1f, 3.5f) };
            Catalog catalog = WithDoorways(TestWorlds.SampleCatalog(), HouseId, declared);

            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                foreach (PlacedObject house in Tagged(doc, "structure/house"))
                {
                    List<Rect2> doorways = DoorwaysOf(house);
                    Assert.That(doorways.Count, Is.EqualTo(ArenaLayoutGenerator.DoorwaysPerStructure),
                        $"seed {seed}: {house.StableId}");

                    int turns = QuarterTurnsOf(house);
                    var expected = new List<Rect2>();
                    for (int i = 0; i < declared.Length; i++)
                    {
                        expected.Add(QuarterTurn.Rotate(declared[i], turns)
                            .Translated(house.Pose.Position.Xz));
                    }

                    for (int i = 0; i < doorways.Count; i++)
                    {
                        Assert.That(Matches(doorways[i], expected), Is.True,
                            $"seed {seed}: {doorways[i]} is not one of the two the row declared");
                    }
                }
            }
        }

        /// <remarks>
        /// Exactly two, whatever the art says, and the two that are furthest apart. Three doors in
        /// a row along one wall is a wall that is not there; two at opposite ends are what make a
        /// structure something a player crosses rather than something they go round. The middle one
        /// is the one that goes.
        /// </remarks>
        [Test]
        public void AStructureThatDeclaresThreeDoorwaysKeepsTheTwoFurthestApart()
        {
            var near = new Rect2(-1f, -3.5f, 1f, -2.5f);
            var middle = new Rect2(-1f, -0.5f, 1f, 0.5f);
            var far = new Rect2(-1f, 2.5f, 1f, 3.5f);

            Catalog catalog = WithDoorways(
                TestWorlds.SampleCatalog(), HouseId, new[] { near, middle, far });

            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                foreach (PlacedObject house in Tagged(doc, "structure/house"))
                {
                    List<Rect2> doorways = DoorwaysOf(house);
                    Assert.That(doorways.Count, Is.EqualTo(ArenaLayoutGenerator.DoorwaysPerStructure),
                        $"seed {seed}: {house.StableId}");

                    Rect2 dropped = QuarterTurn.Rotate(middle, QuarterTurnsOf(house))
                        .Translated(house.Pose.Position.Xz);

                    Assert.That(Matches(dropped, doorways), Is.False,
                        $"seed {seed}: the door between the other two was kept");
                }
            }
        }

        /// <remarks>
        /// A row that declares one door still gets two, and the second is on the face further from
        /// it. A structure with one way in is a dead end wherever the one way in happens to be, and
        /// a second door beside the first is a wider dead end.
        /// </remarks>
        [Test]
        public void AStructureThatDeclaresOneDoorwayIsToppedUpFromTheFarFace()
        {
            var declared = new Rect2(-1f, -3.5f, 1f, -2.5f);
            Catalog catalog = WithDoorways(TestWorlds.SampleCatalog(), HouseId, new[] { declared });

            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                foreach (PlacedObject house in Tagged(doc, "structure/house"))
                {
                    List<Rect2> doorways = DoorwaysOf(house);
                    Rect2 footprint = WorldFootprint(house, catalog);

                    Assert.That(doorways.Count, Is.EqualTo(ArenaLayoutGenerator.DoorwaysPerStructure),
                        $"seed {seed}: {house.StableId}");

                    Rect2 placed = QuarterTurn.Rotate(declared, QuarterTurnsOf(house))
                        .Translated(house.Pose.Position.Xz);

                    Assert.That(Matches(placed, doorways), Is.True,
                        $"seed {seed}: the declared door was dropped");

                    float apart = Apart(doorways[0], doorways[1]);
                    float span = MathF.Max(footprint.Width, footprint.Depth);
                    Assert.That(apart, Is.GreaterThan(0.5f * span),
                        $"seed {seed}: the doors are {apart:0.##} m apart on a {span:0.##} m structure");
                }
            }
        }

        const string HouseId = "structure/house/small_01";

        /// <summary>The same catalog with one entry declaring the doorways given.</summary>
        static Catalog WithDoorways(Catalog catalog, string logicalId, Rect2[] doorways)
        {
            IReadOnlyList<CatalogEntry> entries = catalog.Entries;
            var rebuilt = new CatalogEntry[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                CatalogEntry entry = entries[i];
                rebuilt[i] = entry.LogicalId == logicalId
                    ? new CatalogEntry(
                        entry.LogicalId, entry.Tags.ToArray(), entry.Footprint, entry.Height,
                        entry.Weight, null, entry.BaseOffset, doorways)
                    : entry;
            }

            return new Catalog(rebuilt);
        }

        static List<Rect2> DoorwaysOf(PlacedObject structure)
        {
            var doorways = new List<Rect2>();
            int count = int.Parse(structure.Metadata[ArenaLayoutGenerator.DoorwayCountKey]);

            for (int i = 0; i < count; i++)
            {
                doorways.Add(RectMetadata.Parse(
                    structure.Metadata[ArenaLayoutGenerator.DoorwayKeyPrefix + i.ToString("00")]));
            }

            return doorways;
        }

        /// <summary>How many quarter turns a placed object was turned by.</summary>
        static int QuarterTurnsOf(PlacedObject placed)
        {
            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Quat rotation = QuarterTurn.Rotation(turns);
                if (MathF.Abs(rotation.Y - placed.Pose.Rotation.Y) < 1e-4f &&
                    MathF.Abs(rotation.W - placed.Pose.Rotation.W) < 1e-4f)
                {
                    return turns;
                }
            }

            Assert.Fail($"{placed.StableId} is not at a quarter turn");
            return 0;
        }

        static bool Matches(Rect2 rect, IReadOnlyList<Rect2> among)
        {
            for (int i = 0; i < among.Count; i++)
            {
                if (MathF.Abs(rect.MinX - among[i].MinX) < 1e-3f &&
                    MathF.Abs(rect.MinZ - among[i].MinZ) < 1e-3f &&
                    MathF.Abs(rect.MaxX - among[i].MaxX) < 1e-3f &&
                    MathF.Abs(rect.MaxZ - among[i].MaxZ) < 1e-3f)
                {
                    return true;
                }
            }

            return false;
        }

        static float Apart(Rect2 a, Rect2 b)
        {
            float x = a.Center.X - b.Center.X;
            float z = a.Center.Y - b.Center.Y;
            return MathF.Sqrt(x * x + z * z);
        }

        /// <remarks>
        /// Read off the anchor rather than off "the building on the map", because a flank cell now
        /// draws from the buildings as well as the houses — see
        /// <c>ArenaLayoutGenerator.Fillable</c> — and a map with a hut on each flank has three
        /// buildings on it. The anchor is the one slot whose art the density is being asked about.
        ///
        /// The dense half asks for a density of two rather than one. The budget a cell offers is
        /// measured against a structure <em>and its yard</em>, and the twelve-by-ten building plus
        /// two and a half metres all round is more ground than a quarter of a sixty-metre map's
        /// cell — so at a density of one this catalog only ever affords the hut, which is the
        /// composition that map is meant to have and not a test of anything.
        /// </remarks>
        [Test]
        public void ALowerStructureDensityChoosesASmallerBuilding()
        {
            Catalog catalog = TwoBuildingCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc sparse = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, StructureDensity = 0.05f }, catalog);

                Assert.That(Anchor(sparse).LogicalId,
                    Is.EqualTo("structure/building/hut_01"),
                    $"seed {seed}: a density this low leaves no room for the large building");
            }

            var chosen = new List<string>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc dense = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, StructureDensity = 2f }, catalog);
                string logicalId = Anchor(dense).LogicalId;
                if (!chosen.Contains(logicalId))
                {
                    chosen.Add(logicalId);
                }
            }

            Assert.That(chosen.Count, Is.EqualTo(2), "at a high density both buildings should be reachable");
        }

        /// <summary>The map's anchor: the structure standing in the middle lane.</summary>
        static PlacedObject Anchor(WorldDoc doc) =>
            doc.GeneratedObjects.Single(
                o => o.Tags.Contains(ArenaLayoutGenerator.StructureTag) &&
                     o.Metadata[ArenaLayoutGenerator.LaneKey] == "lane_mid");

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
