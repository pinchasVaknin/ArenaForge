using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The shell: a floor divided into rooms and corridors, walls between them with a doorway in
    /// each, and the contents distributed inside the rooms rather than over the whole storey.
    /// </summary>
    /// <remarks>
    /// The properties worth asserting here are the ones a person would notice and a count would
    /// not: that every room can be reached from every other, that nothing stands in a doorway or
    /// inside a wall, and that no part of the shell reaches past the footprint the building
    /// declares — which is the footprint an exported building is placed in an arena by.
    /// </remarks>
    public sealed class BuildingStructureTests
    {
        /// <summary>Seeds swept by the shape properties.</summary>
        const int Seeds = 120;

        /// <summary>Slack for a measurement that should be exact, in metres.</summary>
        const float Tolerance = 1e-3f;

        const string SlabSegment = "/slab/";
        const string WallSegment = "/wall_";
        const string CoverSegment = "/cover_";
        const string DecorSegment = "/decor_";
        const string StairsSegment = "/stairs/";
        const string WindowSegment = "/window_";

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed) =>
            BuildingGenerator.Generate(Params(seed), TestWorlds.StructuralCatalog());

        // --- the shell is made of catalog art -------------------------------------------------

        [Test]
        public void AFloorIsBuiltFromTheArtTheCatalogTagsAsStructure()
        {
            BuildingDoc doc = Generate(20260816UL);

            var slabs = 0;
            var walls = 0;
            var doorways = 0;
            var stairs = 0;
            var parapets = 0;

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                switch (placed.LogicalId)
                {
                    case "structure/floor/slab_1m":
                        slabs++;
                        Assert.That(placed.StableId, Does.Contain(SlabSegment));
                        break;
                    case "structure/wall/panel_2m":
                        walls++;
                        Assert.That(placed.StableId, Does.Contain("/module_"));
                        break;
                    case "structure/doorway/frame_2m":
                        doorways++;
                        Assert.That(placed.StableId, Does.Contain("/doorway_"));
                        break;
                    case TestWorlds.StairsId:
                        stairs++;
                        Assert.That(placed.StableId, Does.Contain(StairsSegment));
                        break;
                    case TestWorlds.ParapetId:
                        parapets++;
                        Assert.That(placed.StableId, Does.Contain("/parapet_"));
                        break;
                }
            }

            Assert.That(slabs, Is.GreaterThan(0), "the floors were never laid");
            Assert.That(walls, Is.GreaterThan(0), "the building has no walls");
            Assert.That(doorways, Is.GreaterThan(0), "the building has no way through itself");
            Assert.That(stairs, Is.EqualTo(doc.Floors.Count), "one flight per storey, the top one included");
            Assert.That(parapets, Is.GreaterThan(0), "the roof has no edge to it");
            Assert.That(doc.Metadata[BuildingGenerator.StructureCountKey],
                Is.EqualTo((slabs + walls + doorways + stairs + parapets).ToString()));
        }

        /// <remarks>
        /// The catalog boundary, from the other side. The generator asks for three tags and gets
        /// whatever the catalog binds to them; a catalog with none of them still produces a
        /// building, because a shell is art rather than a feature.
        /// </remarks>
        [Test]
        public void ACatalogWithNoStructuralArtStillBuildsTheContents()
        {
            BuildingDoc doc = BuildingGenerator.Generate(Params(20260816UL), TestWorlds.FurnishableCatalog());

            Assert.That(doc.GeneratedObjects, Is.Not.Empty);
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                Assert.That(doc.GeneratedObjects[i].StableId, Does.Contain(CoverSegment),
                    "nothing but contents can be built without structural art");
            }

            Assert.That(doc.Metadata[BuildingGenerator.StructureCountKey], Is.EqualTo("0"));
            Assert.That(doc.Metadata[BuildingGenerator.RoomCountKey],
                Is.EqualTo(doc.Floors.Count.ToString()),
                "with nothing to divide it with, a floor is one room");
        }

        /// <remarks>
        /// <para>
        /// <c>Props/Covers</c> is the outdoor arena's and a room's floor may not draw from it. The
        /// scatter read that folder once, which sounds like economy — cover is cover, and a crate
        /// is a crate wherever it stands — and put dumpsters, concrete barriers and sandbags in
        /// somebody's living room, because what a workspace files there is the tactical furniture
        /// of an open arena.
        /// </para>
        /// <para>
        /// Asserted against a catalog holding both, which is the only way it means anything: the
        /// structural catalog carries the sample's outdoor cover as well as its own interior
        /// clutter, so a floor could still be furnished from the wrong folder and this is what says
        /// it was not. Both directions, because a query that returned neither would satisfy one
        /// half of it.
        /// </para>
        /// </remarks>
        [Test]
        public void AFloorIsFurnishedFromTheInteriorFolderAndNeverFromTheArenasCover()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();

            Assert.That(catalog.Query(TagQuery.All(CoverPlacer.CoverTag)).Count, Is.GreaterThan(0),
                "the catalog has outdoor cover in it for the floor to get wrong");

            var scattered = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.Contains(CoverSegment))
                    {
                        continue;
                    }

                    scattered++;
                    Assert.That(placed.Tags, Does.Contain(BuildingGenerator.InteriorCoverTag),
                        placed.StableId);
                    Assert.That(placed.Tags, Does.Not.Contain(CoverPlacer.CoverTag), placed.StableId);
                }
            }

            Assert.That(scattered, Is.GreaterThan(0), "no floor was ever furnished");
        }

        /// <remarks>
        /// The catalog boundary, as every other piece of art in a building states it — except that
        /// this is the one a building cannot do without. A shell is optional and its contents are
        /// not: a building with empty rooms on every storey is not a building, and a catalog that
        /// has not filed anything in <c>InteriorCovers</c> is far more likely to be a workspace
        /// nobody has filled than a deliberate choice. So it is refused, and the message names the
        /// folder.
        /// </remarks>
        [Test]
        public void ACatalogWithNoInteriorCoverIsRefusedRatherThanBuiltWithEmptyRooms()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => BuildingGenerator.Generate(Params(1UL), TestWorlds.SampleCatalog()));

            Assert.That(error.Message, Does.Contain(BuildingGenerator.InteriorCoverTag));
            Assert.That(error.Message, Does.Contain("InteriorCovers"));
        }

        [Test]
        public void AZeroContentDensityLeavesTheShellStanding()
        {
            BuildingParams parameters = Params(1UL);
            parameters.ContentDensity = 0f;

            BuildingDoc doc = BuildingGenerator.Generate(parameters, TestWorlds.StructuralCatalog());

            Assert.That(doc.GeneratedObjects, Is.Not.Empty, "the walls and floors are still there");
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                Assert.That(doc.GeneratedObjects[i].StableId, Does.Not.Contain(CoverSegment));
                Assert.That(doc.GeneratedObjects[i].StableId, Does.Not.Contain(DecorSegment));
            }
        }

        // --- the plan ------------------------------------------------------------------------

        /// <remarks>
        /// The property the whole partition is for. A BSP tree has exactly one path between any
        /// two leaves, so putting a doorway in every cut opens all of them — this walks the rooms
        /// through the doorways that were actually emitted and asserts they come out as one piece,
        /// rather than trusting the argument.
        /// </remarks>
        [Test]
        public void EveryRoomOnAFloorCanBeReachedFromEveryOther()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var stranded = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    int islands = CountIslands(plan);
                    if (islands != 1)
                    {
                        stranded.Add($"seed {seed} floor {floor + 1}: {islands} disconnected group(s)");
                    }
                }
            }

            Assert.That(stranded, Is.Empty);
        }

        [Test]
        public void EveryWallBetweenTwoRoomsHasExactlyOneDoorway()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var sealedOff = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    // The first four runs are the outside of the building; everything after them
                    // is a cut, and a cut with no way through it would wall a room off.
                    for (int wall = 4; wall < plan.Walls.Count; wall++)
                    {
                        if (plan.Walls[wall].Doorway == PlanWall.NoDoorway)
                        {
                            sealedOff.Add($"seed {seed} floor {floor + 1}: wall {wall} has no doorway");
                        }
                    }
                }
            }

            Assert.That(sealedOff, Is.Empty);
        }

        /// <remarks>
        /// There are no stairs, so a doorway in the outside wall of an upper storey would open
        /// onto a drop. The ground floor gets both ways in and the others get none.
        /// </remarks>
        [Test]
        public void OnlyTheGroundFloorHasAWayInFromOutside()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    Assert.That(Entrances(plan).Count, Is.EqualTo(floor == 0 ? FloorPlan.Entrances : 0),
                        $"seed {seed}, floor {floor + 1}");
                }
            }
        }

        /// <remarks>
        /// The rule the second door is there for. One way into a building makes it a dead end, and
        /// two doors a few modules apart round a corner make it a dead end with a bend in it — so
        /// what is asserted is the distance rather than the count, which the test above has.
        /// </remarks>
        [Test]
        public void TheTwoWaysInAreFarApartAndNeverOnTheSameWall()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                FloorPlan plan = BuildingGenerator.PlanFloor(doc.Parameters, catalog, doc.Floors[0], 0);
                List<int> entrances = Entrances(plan);

                Assert.That(entrances.Count, Is.EqualTo(FloorPlan.Entrances), $"seed {seed}");
                Assert.That(entrances[0], Is.Not.EqualTo(entrances[1]), $"seed {seed}: one wall, two doors");

                Vec2 a = DoorwayCentre(plan.Walls[entrances[0]]);
                Vec2 b = DoorwayCentre(plan.Walls[entrances[1]]);
                float apart = MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                float diagonal = MathF.Sqrt(
                    plan.Bounds.Width * plan.Bounds.Width + plan.Bounds.Depth * plan.Bounds.Depth);

                Assert.That(apart, Is.GreaterThanOrEqualTo(0.5f * diagonal - Tolerance),
                    $"seed {seed}: the two ways in are {apart:0.##} m apart on a {diagonal:0.##} m diagonal");
            }
        }

        /// <remarks>
        /// The partitioning rule, and the one property of a floor plan a person notices before any
        /// other: a leaf narrower than <see cref="FloorPlan.MinRoomSide"/> is a hallway with a room
        /// id, and what makes it one is a cut that should never have been made. The floors still
        /// have to be divided — a rule that produced one room per storey would pass the minimum and
        /// be no plan at all.
        /// </remarks>
        [Test]
        public void NoLeafOfAPartitionIsNarrowerThanARoomMayBe()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var narrow = new List<string>();
            int rooms = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                rooms += int.Parse(doc.Metadata[BuildingGenerator.RoomCountKey]);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        Rect2 bounds = plan.Rooms[i].Bounds;
                        if (MathF.Min(bounds.Width, bounds.Depth) < FloorPlan.MinRoomSide - Tolerance)
                        {
                            narrow.Add(
                                $"seed {seed}: {plan.Rooms[i].Id} is {bounds.Width:0.##} by " +
                                $"{bounds.Depth:0.##} m");
                        }
                    }
                }
            }

            Assert.That(narrow, Is.Empty);
            Assert.That(rooms, Is.GreaterThan(2 * Seeds), "a three-storey building has rooms on each floor");
        }

        /// <remarks>
        /// <para>
        /// What a building tells the map it is going into. The document declares the ways in from
        /// outside, and every one of them is a doorway in the ground floor's own shell: not the
        /// doors between its rooms, of which there is one per cut and a dozen per building, and not
        /// anything on a storey above, where a door in an outside wall would open onto a drop.
        /// </para>
        /// <para>
        /// The interior doors are counted here so the assertion cannot pass by there being nothing
        /// to confuse them with. A building whose floors were never divided would declare two
        /// doorways and prove nothing.
        /// </para>
        /// </remarks>
        [Test]
        public void ABuildingDeclaresTheWaysInFromOutsideAndOnlyThose()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var wrong = new List<string>();
            int interior = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                FloorPlan plan = BuildingGenerator.PlanFloor(doc.Parameters, catalog, doc.Floors[0], 0);

                for (int i = FloorPlan.PerimeterRuns; i < plan.Walls.Count; i++)
                {
                    if (plan.Walls[i].Doorway != PlanWall.NoDoorway)
                    {
                        interior++;
                    }
                }

                List<Rect2> declared = DeclaredDoorways(doc);
                Assert.That(declared.Count, Is.EqualTo(FloorPlan.Entrances), $"seed {seed}");

                for (int i = 0; i < declared.Count; i++)
                {
                    Vec2 at = declared[i].Center;
                    bool onX = Near(at.X, plan.Bounds.MinX) || Near(at.X, plan.Bounds.MaxX);
                    bool onZ = Near(at.Y, plan.Bounds.MinZ) || Near(at.Y, plan.Bounds.MaxZ);

                    if (!onX && !onZ)
                    {
                        wrong.Add(
                            $"seed {seed}: a declared doorway at {at} is not on the shell " +
                            $"{plan.Bounds}");
                    }

                    if (!doc.Footprint.Overlaps(declared[i]))
                    {
                        wrong.Add($"seed {seed}: a declared doorway at {at} misses the building");
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
            Assert.That(interior, Is.GreaterThan(Seeds),
                "the ground floors were never divided, so there were no interior doors to leave out");
        }

        /// <summary>The doorway rectangles a building's document declares, in metadata order.</summary>
        static List<Rect2> DeclaredDoorways(BuildingDoc doc)
        {
            var doorways = new List<Rect2>();
            int count = int.Parse(doc.Metadata[BuildingGenerator.DoorwayCountKey]);

            for (int i = 0; i < count; i++)
            {
                doorways.Add(RectMetadata.Parse(
                    doc.Metadata[BuildingGenerator.DoorwayKeyPrefix + i.ToString("00")]));
            }

            return doorways;
        }

        static bool Near(float a, float b) => MathF.Abs(a - b) <= Tolerance;

        /// <remarks>
        /// <para>
        /// The failure mode a minimum room size invites. A partition that refuses a cut has to
        /// refuse it by giving up rather than by trying again, or a region no legal cut can divide
        /// is a region it looks for a legal cut in for ever — and the smaller the footprint, the
        /// more of the region tree is in that state.
        /// </para>
        /// <para>
        /// Every footprint from one that cannot hold a single wall module up to one that can hold
        /// two rooms, in half-metre steps, so the sweep crosses the threshold rather than sitting
        /// on one side of it. Each has to come back with at least one room and no room narrower
        /// than the minimum. The test hanging is the failure it is written for; the assertions are
        /// what it does once it has not.
        /// </para>
        /// </remarks>
        [Test]
        public void AFootprintTooSmallToSplitComesBackAsOneRoomRatherThanLooping()
        {
            for (float side = 0.5f; side <= 14f; side += 0.5f)
            {
                for (ulong seed = 1; seed <= 8; seed++)
                {
                    var rng = new Rng(seed);
                    FloorPlan plan = FloorPlan.Build(
                        Rect2.FromCenterSize(Vec2.Zero, new Vec2(side, side)),
                        TestWorlds.StructuralModule,
                        TestWorlds.StructuralThickness,
                        true,
                        Rect2.Zero,
                        BuildingGenerator.DoorwayClearance,
                        ref rng);

                    Assert.That(plan.Rooms, Is.Not.Empty, $"{side} m, seed {seed}");

                    // One room over the whole of it is the answer for a footprint no legal cut can
                    // divide, whatever size that footprint is: the minimum is a rule about cuts.
                    if (plan.Rooms.Count == 1)
                    {
                        continue;
                    }

                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        Rect2 bounds = plan.Rooms[i].Bounds;
                        Assert.That(MathF.Min(bounds.Width, bounds.Depth),
                            Is.GreaterThanOrEqualTo(FloorPlan.MinRoomSide - Tolerance),
                            $"{side} m, seed {seed}: {plan.Rooms[i].Id}");
                    }
                }
            }
        }

        /// <summary>Which of the four outside walls of a plan have a way in.</summary>
        static List<int> Entrances(FloorPlan plan)
        {
            var entrances = new List<int>();
            for (int wall = 0; wall < FloorPlan.PerimeterRuns; wall++)
            {
                if (plan.Walls[wall].Doorway != PlanWall.NoDoorway)
                {
                    entrances.Add(wall);
                }
            }

            return entrances;
        }

        static Vec2 DoorwayCentre(PlanWall wall) => wall.ModuleCenter(wall.Doorway);

        [Test]
        public void TheLeavesOfAPartitionTileTheFloorWithoutOverlapping()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = Generate(4242UL);

            for (int floor = 0; floor < doc.Floors.Count; floor++)
            {
                FloorPlan plan = BuildingGenerator.PlanFloor(
                    doc.Parameters, catalog, doc.Floors[floor], floor);

                float covered = 0f;
                for (int i = 0; i < plan.Rooms.Count; i++)
                {
                    covered += plan.Rooms[i].Bounds.Area;
                    Assert.That(plan.Bounds.Contains(plan.Rooms[i].Bounds), Is.True,
                        $"{plan.Rooms[i].Id} is not inside the floor");

                    for (int j = i + 1; j < plan.Rooms.Count; j++)
                    {
                        Assert.That(plan.Rooms[i].Bounds.Overlaps(plan.Rooms[j].Bounds), Is.False,
                            $"{plan.Rooms[i].Id} overlaps {plan.Rooms[j].Id}");
                    }
                }

                Assert.That(covered, Is.EqualTo(plan.Bounds.Area).Within(1e-3f),
                    $"floor {floor + 1}: the leaves leave a gap");
            }
        }

        // --- what the shell does to the contents ------------------------------------------------

        [Test]
        public void NoPartOfTheShellReachesPastTheDeclaredFootprint()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var escaped = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                Rect2 footprint = doc.Footprint;

                IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;
                for (int i = 0; i < objects.Count; i++)
                {
                    Rect2 world = PlacedGeometry.WorldFootprint(objects[i], catalog);
                    if (!footprint.Contains(world))
                    {
                        escaped.Add($"seed {seed}: {objects[i].StableId} at {world} is outside {footprint}");
                    }
                }
            }

            Assert.That(escaped, Is.Empty);
        }

        [Test]
        public void ContentStandsInsideTheRoomItsIdNames()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var loose = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    var bounds = new Dictionary<string, Rect2>(StringComparer.Ordinal);
                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        bounds[plan.Rooms[i].Id] = plan.Rooms[i].Bounds;
                        Assert.That(plan.Rooms[i].IsCorridor, Is.EqualTo(plan.Rooms[i].Id.StartsWith("corridor_")),
                            "a leaf's id says what it is");
                    }

                    List<PlacedObject> content = Content(doc, floor);
                    for (int i = 0; i < content.Count; i++)
                    {
                        string room = RoomOf(content[i]);
                        Assert.That(bounds.ContainsKey(room), Is.True,
                            $"seed {seed}: {content[i].StableId} names a room the plan does not have");
                        Assert.That(room, Does.StartWith("room_"), "nothing is furnished into a corridor");

                        Rect2 world = PlacedGeometry.WorldFootprint(content[i], catalog);
                        if (!bounds[room].Contains(world))
                        {
                            loose.Add($"seed {seed}: {content[i].StableId} is outside {room}");
                        }
                    }
                }
            }

            Assert.That(loose, Is.Empty);
        }

        [Test]
        public void ContentNeverStandsInsideAWall()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var buried = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> content = Content(doc, floor);
                    List<PlacedObject> walls = Walls(doc, floor);

                    for (int i = 0; i < content.Count; i++)
                    {
                        Rect2 world = PlacedGeometry.WorldFootprint(content[i], catalog);
                        for (int j = 0; j < walls.Count; j++)
                        {
                            if (world.Overlaps(PlacedGeometry.WorldFootprint(walls[j], catalog)))
                            {
                                buried.Add($"seed {seed}: {content[i].StableId} is inside {walls[j].StableId}");
                            }
                        }
                    }
                }
            }

            Assert.That(buried, Is.Empty);
        }

        /// <remarks>
        /// The rule is <see cref="ConstraintKind.NotBlockingDoorway"/>, the same one that keeps a
        /// map's cover out of a building's entrance. A doorway a crate stands in is an opening
        /// that no longer connects the two rooms it was cut for, which would quietly undo the
        /// reachability property above.
        /// </remarks>
        [Test]
        public void NothingIsPlacedInADoorway()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var blocked = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);
                    List<PlacedObject> content = Content(doc, floor);

                    for (int w = 0; w < plan.Walls.Count; w++)
                    {
                        PlanWall wall = plan.Walls[w];
                        if (wall.Doorway == PlanWall.NoDoorway)
                        {
                            continue;
                        }

                        Rect2 gap = Rect2.FromCenterSize(
                            wall.ModuleCenter(wall.Doorway),
                            wall.AlongX
                                ? new Vec2(wall.Module, TestWorlds.StructuralThickness)
                                : new Vec2(TestWorlds.StructuralThickness, wall.Module));

                        for (int i = 0; i < content.Count; i++)
                        {
                            if (PlacedGeometry.WorldFootprint(content[i], catalog).Overlaps(gap))
                            {
                                blocked.Add($"seed {seed}: {content[i].StableId} stands in a doorway");
                            }
                        }
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
        }

        // --- nothing is laid over anything else -------------------------------------------------

        /// <remarks>
        /// A storey is a stack: the slab is laid at the floor's own height and the walls and the
        /// crates stand on top of it. Standing all of it at one height, as the generator first did,
        /// buries the bottom of every object in the floor and puts its underside in the same plane
        /// as the slab's — which is a pair of coplanar surfaces under every object in the building.
        /// </remarks>
        [Test]
        public void EverythingOnAFloorStandsOnTopOfItsSlab()
        {
            BuildingDoc doc = Generate(19UL);

            IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                PlacedObject placed = objects[i];
                if (IsRoof(placed))
                {
                    continue;
                }

                int floor = FloorOf(placed);
                float slab = floor * doc.Parameters.FloorHeight;

                float want = placed.StableId.Contains(SlabSegment)
                    ? slab
                    : slab + TestWorlds.StructuralSlabHeight;

                Assert.That(placed.Pose.Position.Y, Is.EqualTo(want).Within(1e-4f), placed.StableId);
            }
        }

        /// <remarks>
        /// <para>
        /// The other half of the same property, read the other way up: the top of a wall is the
        /// underside of the floor above it and not a hundredth of a metre higher. A wall that
        /// reaches into the slab above has its top face in the same plane as that slab's, over the
        /// whole length of the wall — which is the single largest patch of coplanar surface a
        /// generated building can have, and the flicker that reads as the ceiling crawling.
        /// </para>
        /// <para>
        /// The stairs are the exception and are excused by name rather than by a looser bound: a
        /// flight is the one piece meant to reach the storey above, which is what the hole in the
        /// slab is cut for.
        /// </para>
        /// </remarks>
        [Test]
        public void NoPartOfTheShellStandsThroughTheStoreyAboveIt()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingParams parameters = Params(7UL);
            BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

            IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                PlacedObject placed = objects[i];
                if (placed.StableId.Contains(SlabSegment) ||
                    placed.StableId.Contains(StairsSegment) ||
                    IsRoof(placed))
                {
                    continue;
                }

                CatalogEntry entry = catalog.Find(placed.LogicalId);
                float top = placed.Pose.Position.Y + entry.Height;
                float slabAbove = (FloorOf(placed) + 1) * parameters.FloorHeight;

                Assert.That(top, Is.LessThanOrEqualTo(slabAbove + 1e-4f),
                    $"{placed.StableId} reaches into the floor above it");
            }
        }

        /// <remarks>
        /// Two prefabs at one pose are two identical meshes at one depth: every face of both is
        /// coplanar with a face of the other, which is the worst case of the flicker this suite is
        /// about, and it is also simply a wasted draw call. The partition cannot produce one — a
        /// cut is drawn strictly inside its region, so no two cuts land on the same line — but
        /// "we reasoned it cannot happen" is what stops being true when someone adds a second kind
        /// of cut, so it is walked rather than argued.
        /// </remarks>
        [Test]
        public void NoTwoPiecesOfTheShellStandAtTheSamePlace()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var doubled = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                var seen = new Dictionary<string, string>(StringComparer.Ordinal);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    string place = placed.Pose.ToString();

                    if (seen.TryGetValue(place, out string first))
                    {
                        doubled.Add($"seed {seed}: {placed.StableId} stands where {first} already does");
                        continue;
                    }

                    seen[place] = placed.StableId;
                }
            }

            Assert.That(doubled, Is.Empty);
        }

        /// <remarks>
        /// The slab is tiled on the floor tile's own footprint rather than on the wall's module,
        /// so an art pack whose floor is not exactly as wide as its wall is long leaves a strip of
        /// bare ground rather than a lap of double floor. Two slabs over the same ground share a
        /// top face, which is the flicker that reads as the floor breaking up underfoot.
        /// </remarks>
        [Test]
        public void NoTwoFloorTilesOverlap()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var lapped = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> tiles = OnFloor(doc, floor, SlabSegment);
                    Assert.That(tiles, Is.Not.Empty, $"seed {seed} floor {floor + 1} has no floor");

                    for (int i = 0; i < tiles.Count; i++)
                    {
                        Rect2 a = PlacedGeometry.WorldFootprint(tiles[i], catalog);
                        for (int j = i + 1; j < tiles.Count; j++)
                        {
                            if (a.Overlaps(PlacedGeometry.WorldFootprint(tiles[j], catalog)))
                            {
                                lapped.Add($"seed {seed}: {tiles[i].StableId} laps {tiles[j].StableId}");
                            }
                        }
                    }
                }
            }

            Assert.That(lapped, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The property the whole junction rule exists for. A ring of straight wall pieces has to
        /// overlap somewhere — the corner cannot be closed otherwise unless a module happens to be
        /// as long as a wall is thick — so what is asserted is not that walls never overlap but
        /// that where they do, no face of one lands in the plane of a face of the other. Two
        /// coplanar faces with the same normal are what a renderer has no way to order, and what
        /// flickers as the camera moves.
        /// </para>
        /// <para>
        /// Two walls that merely touch — the modules along one run — are not overlapping, so they
        /// never reach the check; two walls that cross now do so with each one's end buried in the
        /// middle of the other.
        /// </para>
        /// </remarks>
        [Test]
        public void WhereTwoWallsOverlapNeitherHasAFaceInTheOthersPlane()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var flickering = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> walls = Walls(doc, floor);
                    for (int i = 0; i < walls.Count; i++)
                    {
                        Rect2 a = PlacedGeometry.WorldFootprint(walls[i], catalog);
                        for (int j = i + 1; j < walls.Count; j++)
                        {
                            Rect2 b = PlacedGeometry.WorldFootprint(walls[j], catalog);
                            if (a.Overlaps(b) && SharesAFace(a, b))
                            {
                                flickering.Add(
                                    $"seed {seed}: {walls[i].StableId} at {a} is flush with " +
                                    $"{walls[j].StableId} at {b}");
                            }
                        }
                    }
                }
            }

            Assert.That(flickering, Is.Empty);
        }

        // --- windows ------------------------------------------------------------------------

        /// <remarks>
        /// Rule one, and the whole of why a window is not simply another piece of wall art. A
        /// window in a cut is a window between two rooms, which is a serving hatch. The generator
        /// decides this from the run's index, because <see cref="FloorPlan.Walls"/> puts the four
        /// sides first by construction — so what is asserted here is the geometry that index is
        /// supposed to mean: every window reaches the outside face of the building.
        /// </remarks>
        [Test]
        public void EveryWindowIsInAnOutsideWallAndNoneIsInACut()
        {
            Catalog catalog = TestWorlds.WindowCatalog();
            var inside = new List<string>();
            var windows = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingParams parameters = Params(seed);
                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        parameters, catalog, doc.Floors[floor], floor);

                    List<PlacedObject> placed = OnFloor(doc, floor, WindowSegment);
                    windows += placed.Count;

                    for (int i = 0; i < placed.Count; i++)
                    {
                        if (RunOf(placed[i]) >= FloorPlan.PerimeterRuns)
                        {
                            inside.Add(
                                $"seed {seed}: {placed[i].StableId} is in a cut, not an outside wall");
                        }

                        Rect2 piece = PlacedGeometry.WorldFootprint(placed[i], catalog);
                        if (!OnTheEdgeOf(piece, plan.OuterBounds))
                        {
                            inside.Add(
                                $"seed {seed}: {placed[i].StableId} at {piece} is inside the " +
                                $"building rather than on {plan.OuterBounds}");
                        }
                    }
                }
            }

            Assert.That(inside, Is.Empty);
            Assert.That(windows, Is.GreaterThan(0), "no seed ever placed a window");
        }

        /// <remarks>
        /// Rule two as a person states it: a window may not stand next to another window. A module
        /// is two metres, so a rule that glazes every module that will take one gives a wall of
        /// glass, and one that glazes every second module gives a building striped like a caravan.
        /// Measured between the pieces themselves, which is the only version of "next to" that
        /// survives a run turning a corner into the run it meets.
        /// </remarks>
        [Test]
        public void NoWindowStandsNextToAnotherWindowOrOverTheDoorway()
        {
            Catalog catalog = TestWorlds.WindowCatalog();
            var touching = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> windows = OnFloor(doc, floor, WindowSegment);
                    List<PlacedObject> doorways = OnFloor(doc, floor, "/doorway_");

                    for (int i = 0; i < windows.Count; i++)
                    {
                        Rect2 a = PlacedGeometry.WorldFootprint(windows[i], catalog);

                        for (int j = i + 1; j < windows.Count; j++)
                        {
                            Rect2 b = PlacedGeometry.WorldFootprint(windows[j], catalog);
                            if (Rect2.Distance(a, b) <= 1e-3f)
                            {
                                touching.Add(
                                    $"seed {seed}: {windows[i].StableId} is next to " +
                                    $"{windows[j].StableId}");
                            }
                        }

                        for (int j = 0; j < doorways.Count; j++)
                        {
                            if (a.Overlaps(PlacedGeometry.WorldFootprint(doorways[j], catalog)))
                            {
                                touching.Add(
                                    $"seed {seed}: {windows[i].StableId} stands in " +
                                    $"{doorways[j].StableId}");
                            }
                        }
                    }
                }
            }

            Assert.That(touching, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The rule the spacing comes out of, rather than the spacing itself: a room gets one
        /// window on each side of the building it reaches, in the middle of its own stretch of that
        /// side. That is what makes the result read as a building rather than as an evenly spaced
        /// pattern — where a window lands is a fact about the room behind it, and a facade of one
        /// large room and two small ones is glazed accordingly.
        /// </para>
        /// <para>
        /// Measured against the room rectangle rather than by repeating the generator's own count
        /// of modules, so this cannot agree with a mistake by making it twice. Half a module of
        /// slack is exactly what a stretch of an even number of modules has to have: its middle
        /// falls on a joint between two of them, and a window has to be in one or the other.
        /// </para>
        /// </remarks>
        [Test]
        public void EachRoomGetsOneWindowPerFacadeCentredOnIt()
        {
            Catalog catalog = TestWorlds.WindowCatalog();
            var wrong = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingParams parameters = Params(seed);
                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        parameters, catalog, doc.Floors[floor], floor);

                    var taken = new HashSet<string>(StringComparer.Ordinal);
                    List<PlacedObject> windows = OnFloor(doc, floor, WindowSegment);

                    for (int i = 0; i < windows.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(windows[i], catalog);
                        int run = RunOf(windows[i]);
                        bool alongX = plan.Walls[run].AlongX;

                        PlanRoom room = RoomBehind(plan, piece);
                        if (room == null)
                        {
                            wrong.Add($"seed {seed}: {windows[i].StableId} backs onto no room");
                            continue;
                        }

                        if (!taken.Add($"{run}/{room.Id}"))
                        {
                            wrong.Add(
                                $"seed {seed}: {room.Id} has a second window on run {run} " +
                                $"({windows[i].StableId})");
                        }

                        float middle = alongX ? room.Bounds.Center.X : room.Bounds.Center.Y;
                        float at = alongX ? piece.Center.X : piece.Center.Y;

                        if (MathF.Abs(at - middle) > plan.Module * 0.5f + 1e-3f)
                        {
                            wrong.Add(
                                $"seed {seed}: {windows[i].StableId} is at {at} where the middle " +
                                $"of {room.Id}'s facade is {middle}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The property that makes windows an addition rather than a regeneration, and the reason
        /// the art is drawn from a fork rather than from the shell's own stream. A workspace syncs
        /// a window prefab into its catalog and presses Regenerate: what has to come back is the
        /// building it had with windows in it, not a differently partitioned one.
        /// </para>
        /// <para>
        /// Asserted object by object and pose by pose, because "much the same" is exactly what a
        /// perturbed stream produces — the same counts, the same tags, and every room somewhere
        /// else.
        /// </para>
        /// </remarks>
        [Test]
        public void AddingWindowArtSwapsModulesAndMovesNothingElse()
        {
            BuildingDoc without = BuildingGenerator.Generate(
                Params(20260816UL), TestWorlds.StructuralCatalog());
            BuildingDoc with = BuildingGenerator.Generate(
                Params(20260816UL), TestWorlds.WindowCatalog());

            Assert.That(with.GeneratedObjects.Count, Is.EqualTo(without.GeneratedObjects.Count));

            var swapped = 0;
            for (int i = 0; i < without.GeneratedObjects.Count; i++)
            {
                PlacedObject was = without.GeneratedObjects[i];
                PlacedObject now = with.GeneratedObjects[i];

                Assert.That(now.Pose, Is.EqualTo(was.Pose), $"{was.StableId} moved");

                if (now.StableId == was.StableId)
                {
                    Assert.That(now.LogicalId, Is.EqualTo(was.LogicalId), was.StableId);
                    continue;
                }

                // The one difference allowed: a module of an outside run is now a window.
                Assert.That(now.StableId,
                    Is.EqualTo(was.StableId.Replace("/module_", "/window_")), was.StableId);
                Assert.That(now.LogicalId, Is.EqualTo(TestWorlds.WindowId));
                swapped++;
            }

            Assert.That(swapped, Is.GreaterThan(0), "no module was ever swapped for a window");
        }

        /// <remarks>
        /// The bargain the rest of the shell makes, made once more: a catalog is art it either has
        /// or does not. A building from one with no window art is walls all round rather than a
        /// building with gaps where the windows would have gone.
        /// </remarks>
        [Test]
        public void ACatalogWithNoWindowArtBuildsWallsAllRound()
        {
            BuildingDoc doc = Generate(11UL);

            for (int floor = 0; floor < doc.Floors.Count; floor++)
            {
                Assert.That(OnFloor(doc, floor, WindowSegment), Is.Empty, $"floor {floor}");
            }
        }

        // --- the plan and the seed --------------------------------------------------------------

        /// <remarks>
        /// The shell is drawn from the same stored per-floor seed the contents are, so the whole
        /// of ARCHITECTURE.md section 7's bargain still holds with a plan in front of it: one
        /// floor's walls move and the others do not.
        /// </remarks>
        [Test]
        public void RerollingAFloorRebuildsItsWallsAndNoOthers()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc before = Generate(3UL);

            BuildingDoc after = BuildingGenerator.Generate(
                Params(3UL), catalog, BuildingGenerator.WithRerolledFloor(before.Floors, 1, 555UL));

            Assert.That(WallSignature(after, 0), Is.EqualTo(WallSignature(before, 0)));
            Assert.That(WallSignature(after, 2), Is.EqualTo(WallSignature(before, 2)));
            Assert.That(WallSignature(after, 1), Is.Not.EqualTo(WallSignature(before, 1)),
                "the re-rolled floor is laid out afresh");
        }

        [Test]
        public void ThePlanReadBackIsThePlanTheFloorWasBuiltOn()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = Generate(11UL);

            for (int floor = 0; floor < doc.Floors.Count; floor++)
            {
                FloorPlan first = BuildingGenerator.PlanFloor(doc.Parameters, catalog, doc.Floors[floor], floor);
                FloorPlan second = BuildingGenerator.PlanFloor(doc.Parameters, catalog, doc.Floors[floor], floor);

                Assert.That(second.Rooms.Count, Is.EqualTo(first.Rooms.Count));
                Assert.That(second.Walls.Count, Is.EqualTo(first.Walls.Count));

                // Every doorway the plan describes is an object the generator emitted, which is
                // what makes reading the plan back a description of that floor rather than of a
                // second one that happens to resemble it.
                int emitted = 0;
                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    if (doc.GeneratedObjects[i].StableId.StartsWith(
                            BuildingDoc.FloorIdPrefix(floor) + "/", StringComparison.Ordinal) &&
                        doc.GeneratedObjects[i].StableId.Contains("/doorway_"))
                    {
                        emitted++;
                    }
                }

                Assert.That(emitted, Is.EqualTo(Doorways(first)), $"floor {floor + 1}");
            }
        }

        [Test]
        public void APlanRefusesAModuleItCannotTile()
        {
            var rng = new Rng(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => FloorPlan.Build(
                    new Rect2(-5f, -5f, 5f, 5f), 0f, 0.2f, false, Rect2.Zero, 0.75f, ref rng));
        }

        [Test]
        public void AFloorTooSmallForOneModuleIsOneUndividedRoom()
        {
            var rng = new Rng(1UL);
            var area = new Rect2(-0.5f, -0.5f, 0.5f, 0.5f);

            FloorPlan plan = FloorPlan.Build(area, 2f, 0.2f, true, Rect2.Zero, 0.75f, ref rng);

            Assert.That(plan.Walls, Is.Empty);
            Assert.That(plan.Rooms.Count, Is.EqualTo(1));
            Assert.That(plan.Rooms[0].Bounds, Is.EqualTo(area));
        }

        /// <remarks>
        /// The partition with nothing reserved draws exactly as it always did, which is what keeps
        /// a building with no stairwell in it — a catalog with no stairs art — laid out the way it
        /// was before there was such a thing as a stairwell.
        /// </remarks>
        [Test]
        public void ReservingNothingPartitionsAFloorTheWayAnUnreservedOneIs()
        {
            var area = new Rect2(-6f, -5f, 6f, 5f);

            var first = new Rng(31UL);
            FloorPlan plain = FloorPlan.Build(area, 2f, 0.2f, true, Rect2.Zero, 0.75f, ref first);

            var second = new Rng(31UL);
            FloorPlan empty = FloorPlan.Build(
                area, 2f, 0.2f, true, new Rect2(3f, 3f, 3f, 3f), 0.75f, ref second);

            Assert.That(empty.Rooms.Count, Is.EqualTo(plain.Rooms.Count));
            for (int i = 0; i < plain.Walls.Count; i++)
            {
                Assert.That(empty.Walls[i].From, Is.EqualTo(plain.Walls[i].From), $"wall {i}");
                Assert.That(empty.Walls[i].Doorway, Is.EqualTo(plain.Walls[i].Doorway), $"wall {i}");
            }
        }

        // --- helpers ------------------------------------------------------------------------

        /// <summary>How many groups the rooms fall into once the doorways are walked.</summary>
        /// <remarks>
        /// A doorway sits in the middle of its module across a shared edge, so the point at its
        /// centre lies on the boundary of exactly the two leaves it joins — which
        /// <see cref="Rect2.Contains(Vec2)"/> reports for both, boundary included. Union those
        /// and count what is left.
        /// </remarks>
        static int CountIslands(FloorPlan plan)
        {
            var group = new int[plan.Rooms.Count];
            for (int i = 0; i < group.Length; i++)
            {
                group[i] = i;
            }

            for (int w = 0; w < plan.Walls.Count; w++)
            {
                PlanWall wall = plan.Walls[w];
                if (wall.Doorway == PlanWall.NoDoorway)
                {
                    continue;
                }

                Vec2 centre = wall.ModuleCenter(wall.Doorway);
                int first = -1;
                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    if (!plan.Rooms[r].Bounds.Contains(centre))
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = r;
                        continue;
                    }

                    Union(group, first, r);
                }
            }

            var roots = new HashSet<int>();
            for (int i = 0; i < group.Length; i++)
            {
                roots.Add(Root(group, i));
            }

            return roots.Count;
        }

        static int Root(int[] group, int i)
        {
            while (group[i] != i)
            {
                i = group[i];
            }

            return i;
        }

        static void Union(int[] group, int a, int b) => group[Root(group, a)] = Root(group, b);

        static int Doorways(FloorPlan plan)
        {
            int count = 0;
            for (int i = 0; i < plan.Walls.Count; i++)
            {
                if (plan.Walls[i].Doorway != PlanWall.NoDoorway)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>True if the two rectangles have an edge on a common line.</summary>
        static bool SharesAFace(Rect2 a, Rect2 b) =>
            Flush(a.MinX, b.MinX) || Flush(a.MinX, b.MaxX) ||
            Flush(a.MaxX, b.MinX) || Flush(a.MaxX, b.MaxX) ||
            Flush(a.MinZ, b.MinZ) || Flush(a.MinZ, b.MaxZ) ||
            Flush(a.MaxZ, b.MinZ) || Flush(a.MaxZ, b.MaxZ);

        // A tenth of a millimetre. Two faces further apart than that are separated by more than
        // the depth buffer's precision at any distance a player stands from a wall.
        static bool Flush(float a, float b) => MathF.Abs(a - b) < 1e-4f;

        /// <summary>Which storey an object's stable id says it is on, counting from zero.</summary>
        static int FloorOf(PlacedObject placed)
        {
            string[] parts = placed.StableId.Split('/');
            return int.Parse(parts[1].Substring("floor_".Length)) - 1;
        }

        /// <summary>True if this object is part of the roof rather than of any storey.</summary>
        static bool IsRoof(PlacedObject placed) =>
            placed.StableId.StartsWith(BuildingGenerator.RoofIdPrefix + "/", StringComparison.Ordinal);

        /// <summary>
        /// Everything a room was furnished with: the cover scattered over its floor and the decor
        /// pushed back against its walls. Both are content, and every property below is about both.
        /// </summary>
        static List<PlacedObject> Content(BuildingDoc doc, int floor)
        {
            List<PlacedObject> content = OnFloor(doc, floor, CoverSegment);
            content.AddRange(OnFloor(doc, floor, DecorSegment));
            return content;
        }

        static List<PlacedObject> Walls(BuildingDoc doc, int floor) =>
            OnFloor(doc, floor, WallSegment);

        static List<PlacedObject> OnFloor(BuildingDoc doc, int floor, string segment)
        {
            string prefix = BuildingDoc.FloorIdPrefix(floor) + "/";
            var found = new List<PlacedObject>();

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.StartsWith(prefix, StringComparison.Ordinal) &&
                    placed.StableId.Contains(segment))
                {
                    found.Add(placed);
                }
            }

            return found;
        }

        /// <summary>Which wall run a piece of the shell belongs to, from its stable id.</summary>
        static int RunOf(PlacedObject placed)
        {
            string[] parts = placed.StableId.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("wall_", StringComparison.Ordinal))
                {
                    return int.Parse(parts[i].Substring("wall_".Length));
                }
            }

            return -1;
        }

        /// <summary>True if a piece of the shell reaches one of the building's outside faces.</summary>
        static bool OnTheEdgeOf(Rect2 piece, Rect2 outer) =>
            Flush(piece.MinX, outer.MinX) || Flush(piece.MaxX, outer.MaxX) ||
            Flush(piece.MinZ, outer.MinZ) || Flush(piece.MaxZ, outer.MaxZ);

        /// <summary>
        /// The leaf a piece of an outside run backs onto, probed from inside the piece rather than
        /// from its centreline — which is the boundary the rooms are measured to.
        /// </summary>
        static PlanRoom RoomBehind(FloorPlan plan, Rect2 piece)
        {
            float step = plan.Module * 0.25f;
            Vec2 centre = piece.Center;
            Vec2 middle = plan.Bounds.Center;

            Vec2 at = piece.Width >= piece.Depth
                ? new Vec2(centre.X, centre.Y + (centre.Y < middle.Y ? step : -step))
                : new Vec2(centre.X + (centre.X < middle.X ? step : -step), centre.Y);

            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                if (plan.Rooms[i].Bounds.Contains(at))
                {
                    return plan.Rooms[i];
                }
            }

            return null;
        }

        /// <summary>The room segment of a content object's stable id.</summary>
        static string RoomOf(PlacedObject placed)
        {
            string[] parts = placed.StableId.Split('/');
            return parts[parts.Length - 2];
        }

        /// <summary>One floor's walls and doorways, as one comparable string.</summary>
        static string WallSignature(BuildingDoc doc, int floor)
        {
            var text = new System.Text.StringBuilder();
            List<PlacedObject> walls = Walls(doc, floor);
            for (int i = 0; i < walls.Count; i++)
            {
                text.Append(walls[i].StableId).Append('@').Append(walls[i].Pose).Append(';');
            }

            return text.ToString();
        }
    }
}
