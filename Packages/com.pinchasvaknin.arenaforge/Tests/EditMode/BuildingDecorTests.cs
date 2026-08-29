using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Interior decor: the small stuff a room keeps in its corners, as distinct from cover, which
    /// is the thing in the middle of it you fight from.
    /// </summary>
    /// <remarks>
    /// What the suite is really checking is that decor and cover are not the same pass with a
    /// different tag on it. A pot plant that behaved like a crate would still pass every property
    /// the shell suite asserts — inside its room, out of the walls, clear of the doorways — and
    /// would look exactly wrong, because a room whose plant is marooned in the middle of the floor
    /// reads as a bug in a way no amount of correct clearance fixes. The corner is not a preference
    /// the pass expresses where it can, either: it is the only floor this art is allowed on, so the
    /// properties below are written as "every piece" rather than as a proportion.
    /// </remarks>
    public sealed class BuildingDecorTests
    {
        /// <summary>Seeds swept by the placement properties.</summary>
        const int Seeds = 60;

        /// <summary>Corners a rectangular room has, which is how much decor it can hold.</summary>
        const int Corners = 4;

        const string DecorSegment = "/decor_";
        const string CoverSegment = "/cover_";

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed) =>
            BuildingGenerator.Generate(Params(seed), TestWorlds.StructuralCatalog());

        // --- decor is placed at all --------------------------------------------------------------

        [Test]
        public void ACatalogWithDecorArtFurnishesTheRooms()
        {
            BuildingDoc doc = Generate(20260816UL);

            var decor = 0;
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!placed.StableId.Contains(DecorSegment))
                {
                    continue;
                }

                decor++;
                Assert.That(placed.LogicalId, Does.StartWith("prop/decor/"));
                Assert.That(placed.Tags, Does.Contain(BuildingGenerator.DecorTag));
                Assert.That(placed.Metadata.ContainsKey(BuildingGenerator.RoomKey), Is.True,
                    "a piece of decor stands in a room and says which");
            }

            Assert.That(decor, Is.GreaterThan(0), "nothing was ever furnished");
        }

        /// <remarks>
        /// Decor is art the catalog either has or does not, exactly as the shell is. A catalog of
        /// crates and walls builds the same building it did before decor existed.
        /// </remarks>
        [Test]
        public void ACatalogWithNoDecorArtBuildsTheSameBuildingAsBefore()
        {
            BuildingDoc doc = BuildingGenerator.Generate(Params(4UL), TestWorlds.FurnishableCatalog());

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                Assert.That(doc.GeneratedObjects[i].StableId, Does.Not.Contain(DecorSegment));
            }
        }

        [Test]
        public void NothingIsFurnishedIntoACorridor()
        {
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (placed.StableId.Contains(DecorSegment))
                    {
                        Assert.That(placed.Metadata[BuildingGenerator.RoomKey],
                            Does.StartWith("room_"), placed.StableId);
                    }
                }
            }
        }

        // --- decor is not cover ------------------------------------------------------------------

        /// <remarks>
        /// The property the rule exists for, in its strong form: every piece of decor comes within
        /// reach of two perpendicular walls of the floor it was proposed onto — a corner, and not
        /// merely a wall. Cover ends up wherever the sampler found room, so if the two passes had
        /// quietly become one this is what would fail.
        /// </remarks>
        [Test]
        public void EveryPieceOfDecorStandsInACorner()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var adrift = new List<string>();
            var placed = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor);

                    List<PlacedObject> decor = Decor(doc, floor);
                    for (int i = 0; i < decor.Count; i++)
                    {
                        Rect2 area = rooms[decor[i].Metadata[BuildingGenerator.RoomKey]];
                        Rect2 piece = PlacedGeometry.WorldFootprint(decor[i], catalog);
                        placed++;

                        if (!NearEdgeX(area, piece) || !NearEdgeZ(area, piece))
                        {
                            adrift.Add(
                                $"seed {seed}: {decor[i].StableId} at {piece} is " +
                                $"{Gap(area, piece):0.##} m from the nearest wall of {area} and " +
                                "reaches no second one");
                        }
                    }
                }
            }

            Assert.That(adrift, Is.Empty);
            Assert.That(placed, Is.GreaterThan(Seeds), "the sweep furnished almost nothing");
        }

        /// <remarks>
        /// The other half of it: cover is <em>not</em> held to the edge of the room, so if the two
        /// passes had quietly become one this would fail. Measured over a sweep rather than one
        /// seed because a single room can honestly come out with all its cover round the edge.
        /// </remarks>
        [Test]
        public void CoverIsNotHeldAgainstTheWallsTheWayDecorIs()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            int inTheOpen = 0;
            int cover = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    var rooms = new Dictionary<string, Rect2>(StringComparer.Ordinal);
                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        rooms[plan.Rooms[i].Id] = plan.Rooms[i].Bounds.Expanded(-plan.Thickness);
                    }

                    List<PlacedObject> placed = OnFloor(doc, floor, CoverSegment);
                    for (int i = 0; i < placed.Count; i++)
                    {
                        cover++;
                        Rect2 area = rooms[placed[i].Metadata[BuildingGenerator.RoomKey]];
                        if (Gap(area, PlacedGeometry.WorldFootprint(placed[i], catalog)) >
                            BuildingGenerator.DecorReach + 1e-4f)
                        {
                            inTheOpen++;
                        }
                    }
                }
            }

            Assert.That(cover, Is.GreaterThan(0), "no cover was placed at all");
            Assert.That(inTheOpen, Is.GreaterThan(0),
                "every piece of cover ended up against a wall, so cover and decor are one pass");
        }

        /// <remarks>
        /// <para>
        /// A room has four corners and puts at most one piece in each, which is the half of the
        /// rule <see cref="ConstraintKind.InCorner"/> cannot state. That rule asks whether a piece
        /// has two walls within reach of it and says nothing about <em>which</em> two, so four
        /// plants heaped into one corner satisfy it exactly as well as four plants in four corners
        /// do — and the overlap margin decor is placed under is small enough to let them.
        /// </para>
        /// <para>
        /// Which corner a piece is in is read off the walls it reaches rather than off anything the
        /// document says, so this measures where the furniture ended up and not what the generator
        /// meant by it.
        /// </para>
        /// </remarks>
        [Test]
        public void NoTwoPiecesOfDecorShareACorner()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var heaped = new List<string>();
            var corners = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor);
                    var taken = new HashSet<string>(StringComparer.Ordinal);

                    List<PlacedObject> decor = Decor(doc, floor);
                    for (int i = 0; i < decor.Count; i++)
                    {
                        string room = decor[i].Metadata[BuildingGenerator.RoomKey];
                        Rect2 area = rooms[room];
                        Rect2 piece = PlacedGeometry.WorldFootprint(decor[i], catalog);

                        string corner = room + ":" + CornerOf(area, piece);
                        corners++;

                        if (!taken.Add(corner))
                        {
                            heaped.Add(
                                $"seed {seed}: {decor[i].StableId} is a second piece in {corner}");
                        }
                    }
                }
            }

            Assert.That(heaped, Is.Empty);
            Assert.That(corners, Is.GreaterThan(Seeds), "no room ever put anything in a corner");
        }

        /// <remarks>
        /// The corners are the scarce thing, so a room never stands more decor than it has of them
        /// however high the density is wound.
        /// </remarks>
        [Test]
        public void NoRoomStandsMoreDecorThanItHasCorners()
        {
            BuildingParams parameters = Params(9UL);
            parameters.ContentDensity = 6f;

            BuildingDoc doc = BuildingGenerator.Generate(parameters, TestWorlds.StructuralCatalog());
            var perRoom = new Dictionary<string, int>(StringComparer.Ordinal);
            var crowded = new List<string>();

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!placed.StableId.Contains(DecorSegment))
                {
                    continue;
                }

                // The storey is part of the key: every floor has a room_00 of its own.
                string key = placed.Metadata[BuildingGenerator.FloorKey] + "/" +
                             placed.Metadata[BuildingGenerator.RoomKey];
                perRoom.TryGetValue(key, out int count);
                perRoom[key] = count + 1;

                if (count + 1 > Corners)
                {
                    crowded.Add($"{key} holds {count + 1} pieces of decor");
                }
            }

            Assert.That(crowded, Is.Empty);
            Assert.That(perRoom, Is.Not.Empty, "nothing was furnished at six times the density");
        }

        /// <remarks>
        /// A corner the plan has reserved a walkway across is not a corner decor may use. The rule
        /// is <see cref="ConstraintKind.OffReservedPath"/>, and what makes it worth asserting over
        /// the decor alone rather than only over everything a floor holds is that decor has nowhere
        /// else to go: cover turned away from a strip is proposed again somewhere else, where a
        /// corner turned down is a corner left empty.
        /// </remarks>
        [Test]
        public void NoPieceOfDecorStandsOnAReservedWalkway()
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

                    var paths = new Dictionary<string, IReadOnlyList<Rect2>>(StringComparer.Ordinal);
                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        paths[plan.Rooms[i].Id] = plan.Rooms[i].Paths;
                    }

                    List<PlacedObject> decor = Decor(doc, floor);
                    for (int i = 0; i < decor.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(decor[i], catalog);
                        IReadOnlyList<Rect2> strips =
                            paths[decor[i].Metadata[BuildingGenerator.RoomKey]];

                        for (int p = 0; p < strips.Count; p++)
                        {
                            if (piece.Overlaps(strips[p]))
                            {
                                blocked.Add($"seed {seed}: {decor[i].StableId} stands on a walkway");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
        }

        // --- decor obeys everything content obeys -------------------------------------------------

        /// <remarks>
        /// Against everything on the floor except the floor itself — a slab is what decor
        /// <em>stands on</em>, and a rule that kept furniture off it would be a rule that kept
        /// furniture out of the building.
        /// </remarks>
        [Test]
        public void NoPieceOfDecorOverlapsAnythingElseStandingOnItsFloor()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var collisions = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> onFloor = Standing(doc, floor);
                    List<PlacedObject> decor = Decor(doc, floor);

                    for (int i = 0; i < decor.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(decor[i], catalog);
                        for (int j = 0; j < onFloor.Count; j++)
                        {
                            if (ReferenceEquals(decor[i], onFloor[j]))
                            {
                                continue;
                            }

                            if (piece.Overlaps(PlacedGeometry.WorldFootprint(onFloor[j], catalog)))
                            {
                                collisions.Add(
                                    $"seed {seed}: {decor[i].StableId} overlaps {onFloor[j].StableId}");
                            }
                        }
                    }
                }
            }

            Assert.That(collisions, Is.Empty);
        }

        /// <remarks>
        /// Decor is meant to sit alongside a doorway, not in it. The rule is the same
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> the cover obeys, which is why a piece of
        /// furniture against the wall beside a door is fine and one across the opening is not.
        /// </remarks>
        [Test]
        public void NoPieceOfDecorStandsInADoorway()
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
                    List<PlacedObject> decor = Decor(doc, floor);

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

                        for (int i = 0; i < decor.Count; i++)
                        {
                            if (PlacedGeometry.WorldFootprint(decor[i], catalog).Overlaps(gap))
                            {
                                blocked.Add($"seed {seed}: {decor[i].StableId} blocks a doorway");
                            }
                        }
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
        }

        // --- decor stands on the floor rather than in it -------------------------------------------

        /// <remarks>
        /// <para>
        /// A pose puts a pivot somewhere, and half the art pack does not have its pivot on its base
        /// — every Unity primitive is modelled around its centre, and so is a great deal of
        /// furniture. Placed by the pivot alone, a sofa is sunk half its own depth into the floor
        /// slab, which is what this catalog's decor is built to reproduce.
        /// </para>
        /// <para>
        /// What is asserted is both halves of standing on something: the underside of the art lands
        /// exactly on the walking surface — the slab's top face, not the storey's own datum — and
        /// not a millimetre above it, because furniture hovering over the floor reads as wrong in
        /// the same way as furniture sunk into it.
        /// </para>
        /// </remarks>
        [Test]
        public void NoPieceOfDecorIsSunkIntoTheFloorItStandsOn()
        {
            Catalog catalog = CentrePivoted(TestWorlds.StructuralCatalog());
            var clipping = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.Contains(DecorSegment))
                    {
                        continue;
                    }

                    CatalogEntry entry = catalog.Find(placed.LogicalId);
                    Assert.That(entry.BaseOffset, Is.GreaterThan(0f),
                        $"{placed.LogicalId} is not the centre-pivoted art this test is about");

                    float floor = int.Parse(placed.Metadata[BuildingGenerator.FloorKey]) - 1;
                    float surface = floor * doc.Parameters.FloorHeight + TestWorlds.StructuralSlabHeight;
                    float underside = placed.Pose.Position.Y - entry.BaseOffset;

                    if (MathF.Abs(underside - surface) > 1e-4f)
                    {
                        clipping.Add(
                            $"seed {seed}: {placed.StableId} has its underside at {underside}, " +
                            $"{surface - underside:0.###} m below the floor at {surface}");
                    }
                }
            }

            Assert.That(clipping, Is.Empty);
        }

        /// <remarks>
        /// The same fact from the catalog's side: art that hangs below its pivot needs that much
        /// more headroom than its height alone says, or a piece stood on the floor reaches into the
        /// slab above. A building whose storeys are 3 m and whose ceiling is 2.9 m has no room for
        /// a 2 m sofa modelled around its centre, however small its declared height is.
        /// </remarks>
        [Test]
        public void ArtIsMeasuredForHeadroomFromItsBaseRatherThanItsPivot()
        {
            var sunken = new CatalogEntry(
                "prop/decor/tall_01",
                new[] { "prop", "prop/decor" },
                new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
                1.5f,
                1f,
                null,
                1.5f);

            Assert.That(sunken.StandingHeight, Is.EqualTo(3f).Within(1e-6f));

            BuildingDoc doc = BuildingGenerator.Generate(
                Params(6UL), With(TestWorlds.StructuralCatalog(), sunken));

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                Assert.That(doc.GeneratedObjects[i].LogicalId, Is.Not.EqualTo(sunken.LogicalId),
                    "a piece three metres tall was put on a floor with 2.9 m of headroom");
            }
        }

        /// <remarks>
        /// Its own fork of the floor's seed, so decor arriving in this generator did not move a
        /// single crate in any building anyone had already made — and so a change to how decor is
        /// placed will not either.
        /// </remarks>
        [Test]
        public void DecorAndCoverDrawFromSeparateStreams()
        {
            Catalog withDecor = TestWorlds.StructuralCatalog();
            Catalog withoutDecor = WithoutDecor(withDecor);

            BuildingDoc furnished = BuildingGenerator.Generate(Params(13UL), withDecor);
            BuildingDoc bare = BuildingGenerator.Generate(Params(13UL), withoutDecor);

            Assert.That(Signature(furnished, CoverSegment), Is.EqualTo(Signature(bare, CoverSegment)));
            Assert.That(Signature(furnished, DecorSegment), Is.Not.Empty);
            Assert.That(Signature(bare, DecorSegment), Is.Empty);
        }

        [Test]
        public void TheSameSeedFurnishesTheSameRooms()
        {
            Assert.That(
                ArenaJson.SerializeBuilding(Generate(77UL)),
                Is.EqualTo(ArenaJson.SerializeBuilding(Generate(77UL))));
        }

        // --- helpers ------------------------------------------------------------------------------

        /// <summary>How far a rectangle stops short of the nearest edge of the area holding it.</summary>
        static float Gap(Rect2 area, Rect2 piece) => MathF.Min(
            MathF.Min(piece.MinX - area.MinX, area.MaxX - piece.MaxX),
            MathF.Min(piece.MinZ - area.MinZ, area.MaxZ - piece.MaxZ));

        /// <summary>
        /// Which of the four corners of an area a rectangle stands in, named for the two edges it
        /// is nearer.
        /// </summary>
        /// <exception cref="AssertionException">It reaches fewer than two of them.</exception>
        static string CornerOf(Rect2 area, Rect2 piece)
        {
            Assert.That(NearEdgeX(area, piece) && NearEdgeZ(area, piece), Is.True,
                $"{piece} is not in any corner of {area}");

            string x = piece.MinX - area.MinX <= area.MaxX - piece.MaxX ? "minX" : "maxX";
            string z = piece.MinZ - area.MinZ <= area.MaxZ - piece.MaxZ ? "minZ" : "maxZ";
            return x + "/" + z;
        }

        /// <summary>One floor's rooms as the areas their contents are proposed onto, by room id.</summary>
        static Dictionary<string, Rect2> Rooms(BuildingDoc doc, Catalog catalog, int floor)
        {
            FloorPlan plan = BuildingGenerator.PlanFloor(
                doc.Parameters, catalog, doc.Floors[floor], floor);

            var rooms = new Dictionary<string, Rect2>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                rooms[plan.Rooms[i].Id] = plan.Rooms[i].Bounds.Expanded(-plan.Thickness);
            }

            return rooms;
        }

        static bool NearEdgeX(Rect2 area, Rect2 piece) =>
            MathF.Min(piece.MinX - area.MinX, area.MaxX - piece.MaxX) <=
            BuildingGenerator.DecorReach + 1e-4f;

        static bool NearEdgeZ(Rect2 area, Rect2 piece) =>
            MathF.Min(piece.MinZ - area.MinZ, area.MaxZ - piece.MaxZ) <=
            BuildingGenerator.DecorReach + 1e-4f;

        static List<PlacedObject> Decor(BuildingDoc doc, int floor) => OnFloor(doc, floor, DecorSegment);

        /// <summary>Everything on a floor that stands on the slab rather than being the slab.</summary>
        static List<PlacedObject> Standing(BuildingDoc doc, int floor)
        {
            List<PlacedObject> onFloor = OnFloor(doc, floor, string.Empty);
            onFloor.RemoveAll(p => p.StableId.Contains("/slab/"));
            return onFloor;
        }

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

        /// <summary>Every object whose id carries a segment, as one comparable string.</summary>
        static string Signature(BuildingDoc doc, string segment)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.Contains(segment))
                {
                    text.Append(placed.StableId).Append('@').Append(placed.Pose).Append(';');
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// The structural catalog with every piece of decor remodelled around its own centre.
        /// </summary>
        /// <remarks>
        /// A separate catalog rather than a base offset on the shared one, for the reason
        /// <see cref="TestWorlds.StructuralCatalog"/> is separate from the sample: the offset moves
        /// nothing on the ground plane, but it is a change to the art every other suite measures
        /// against, and the suites that check what a building looks like should not have to know
        /// about the one that checks how it is stood up.
        /// </remarks>
        static Catalog CentrePivoted(Catalog catalog)
        {
            var remodelled = new List<CatalogEntry>(catalog.Entries.Count);
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                CatalogEntry entry = catalog.Entries[i];
                remodelled.Add(entry.HasTag(BuildingGenerator.DecorTag)
                    ? new CatalogEntry(
                        entry.LogicalId,
                        new List<string>(entry.Tags).ToArray(),
                        entry.Footprint,
                        entry.Height * 0.5f,
                        entry.Weight,
                        null,
                        entry.Height * 0.5f)
                    : entry);
            }

            return new Catalog(remodelled.ToArray());
        }

        /// <summary>A catalog with one more entry in it.</summary>
        static Catalog With(Catalog catalog, CatalogEntry extra)
        {
            var entries = new List<CatalogEntry>(catalog.Entries) { extra };
            return new Catalog(entries.ToArray());
        }

        /// <summary>The structural catalog with its decor rows taken out.</summary>
        static Catalog WithoutDecor(Catalog catalog)
        {
            var kept = new List<CatalogEntry>(catalog.Entries.Count);
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                CatalogEntry entry = catalog.Entries[i];
                if (!entry.LogicalId.StartsWith("prop/decor/", StringComparison.Ordinal))
                {
                    kept.Add(entry);
                }
            }

            return new Catalog(kept.ToArray());
        }
    }
}
