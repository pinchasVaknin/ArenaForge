using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The third dimension of a building: one stairwell running the whole way up through holes cut
    /// in the slabs, and a roof with a parapet round it at the top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property that matters is alignment. Every storey draws its own partition from its own
    /// stored seed and re-rolling one has to leave the others alone — so a shaft that any floor had
    /// a say in would be in a different place on each of them. What the suite below asserts is that
    /// the shaft is a fact about the building, that no storey's walls cross it, and that every slab
    /// over it is open.
    /// </para>
    /// <para>
    /// A hole that is not there is the failure worth spending seeds on: a flight of stairs into a
    /// ceiling looks completely correct in a screenshot and is useless to walk up.
    /// </para>
    /// </remarks>
    public sealed class BuildingVerticalTests
    {
        /// <summary>Seeds swept by the shape properties.</summary>
        const int Seeds = 120;

        const string StairsSegment = "/stairs/";

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed) =>
            BuildingGenerator.Generate(Params(seed), TestWorlds.StructuralCatalog());

        // --- the stairwell ----------------------------------------------------------------------

        /// <remarks>
        /// The whole point of the feature, and the one thing a count would not notice: three
        /// flights at three different places would tally exactly the same as three flights in a
        /// shaft.
        /// </remarks>
        [Test]
        public void EveryStoreysFlightStandsAtTheSamePlaceAsEveryOthers()
        {
            var wandering = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed);
                List<PlacedObject> flights = Flights(doc);

                Assert.That(flights.Count, Is.EqualTo(doc.Floors.Count),
                    $"seed {seed}: one flight per storey");

                for (int i = 1; i < flights.Count; i++)
                {
                    Vec2 first = flights[0].Pose.Position.Xz;
                    Vec2 here = flights[i].Pose.Position.Xz;

                    if (here != first || flights[i].Pose.Rotation != flights[0].Pose.Rotation)
                    {
                        wandering.Add($"seed {seed}: {flights[i].StableId} is at {here}, not {first}");
                    }
                }
            }

            Assert.That(wandering, Is.Empty);
        }

        [Test]
        public void EachFlightStandsOnItsOwnStoreyAndReachesTheNext()
        {
            BuildingDoc doc = Generate(20260816UL);
            float pitch = doc.Parameters.FloorHeight;

            List<PlacedObject> flights = Flights(doc);
            for (int i = 0; i < flights.Count; i++)
            {
                float foot = flights[i].Pose.Position.Y;

                Assert.That(foot, Is.EqualTo(i * pitch + TestWorlds.StructuralSlabHeight).Within(1e-4f),
                    $"{flights[i].StableId} does not stand on its own floor");
                Assert.That(foot + TestWorlds.StructuralStairsHeight,
                    Is.GreaterThanOrEqualTo((i + 1) * pitch + TestWorlds.StructuralSlabHeight - 1e-4f),
                    $"{flights[i].StableId} stops short of the storey above");
            }
        }

        /// <remarks>
        /// <para>
        /// The reason the shaft is handed to <see cref="FloorPlan.Build"/> rather than checked
        /// afterwards. A cut through the shaft would put a wall across the opening on that storey
        /// and nowhere else, which is a seed-dependent bug of the worst kind.
        /// </para>
        /// <para>
        /// The outside walls are the deliberate exception, and the bound on them is the property
        /// rather than an excuse: a flight may be anchored on the slab joint at the edge of the
        /// floor, which on an art pack whose wall is as long as its floor tile is wide lands on
        /// the outside wall's own centreline — so that wall straddles the edge of the opening by
        /// half its thickness and no more. Refusing that instead leaves a flight nowhere to stand
        /// but the middle of the floor, which is what turns a plan into corridors; letting it go
        /// unbounded would be a wall standing across the stairs.
        /// </para>
        /// </remarks>
        [Test]
        public void NoWallCrossesTheShaftAndOnlyAnOutsideOneEvenTouchesIt()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var crossed = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                Assert.That(stairs.Exists, Is.True, $"seed {seed}: no stairwell was planned");

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.Contains("/wall_"))
                    {
                        continue;
                    }

                    Rect2 wall = PlacedGeometry.WorldFootprint(placed, catalog);
                    if (!wall.Overlaps(stairs.Shaft))
                    {
                        continue;
                    }

                    // A cut may not reach the opening at all — that is what reserving it is for.
                    // Only the four outside runs are allowed to straddle its edge, and only by the
                    // half thickness a run always stands either side of its own centreline.
                    float allowed = IsOutsideWall(placed)
                        ? TestWorlds.StructuralThickness * 0.5f
                        : 0f;

                    float into = Overlap(wall, stairs.Shaft);
                    if (into > allowed + 1e-4f)
                    {
                        crossed.Add(
                            $"seed {seed}: {placed.StableId} reaches {into:0.###} m into the " +
                            $"stairwell, where {allowed:0.###} m is allowed");
                    }
                }
            }

            Assert.That(crossed, Is.Empty);
        }

        /// <remarks>
        /// The cut in the slab is whole tiles because a slab is laid in whole tiles, so the honest
        /// version of "the hole matches the stairs" is that it is the <em>fewest</em> tiles the
        /// flight can be made to need. Anchoring the flight on a joint is what buys that: at any
        /// other position a footprint laps one more tile on each axis than it covers, which is how
        /// a one-by-five-and-a-half metre flight ended up in a four-by-eight metre hole.
        /// </remarks>
        [Test]
        public void TheOpeningIsTheFewestWholeTilesTheFlightCanNeed()
        {
            const float tile = TestWorlds.StructuralTile;

            // Both a flight that fills a tile exactly and one that does not — and the second is
            // pivoted at the foot of its run, which is the shape that used to be measured at nearly
            // twice its length and opened four times the floor it covers.
            var catalogs = new[] { TestWorlds.StructuralCatalog(), TestWorlds.NarrowStairsCatalog() };
            var wasteful = new List<string>();

            for (int c = 0; c < catalogs.Length; c++)
            {
                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    BuildingGenerator.Stairwell stairs =
                        BuildingGenerator.PlanStairwell(Params(seed), catalogs[c]);

                    Assert.That(stairs.Shaft.Contains(stairs.Flight), Is.True,
                        $"catalog {c} seed {seed}: the opening does not hold the flight in it");

                    int wide = (int)MathF.Round(stairs.Shaft.Width / tile);
                    int deep = (int)MathF.Round(stairs.Shaft.Depth / tile);

                    int leastWide = (int)MathF.Ceiling(stairs.Flight.Width / tile - 1e-4f);
                    int leastDeep = (int)MathF.Ceiling(stairs.Flight.Depth / tile - 1e-4f);

                    if (wide != leastWide || deep != leastDeep)
                    {
                        wasteful.Add(
                            $"catalog {c} seed {seed}: a {stairs.Flight.Size} flight opened " +
                            $"{wide}x{deep} tiles where {leastWide}x{leastDeep} would do");
                    }
                }
            }

            Assert.That(wasteful, Is.Empty);
        }

        /// <remarks>
        /// The measurement behind it. A flight one metre by three, pivoted at the foot of its run,
        /// is exactly the shape that a footprint centred on its pivot reports as one by five and a
        /// half — and a five-and-a-half-metre flight asks for three floor tiles where three metres
        /// asks for two. Asserting the flight's own rectangle rather than the opening keeps the two
        /// halves of the fix apart: this is what the catalog now says, and the test above is what
        /// the generator does with it.
        /// </remarks>
        [Test]
        public void AFlightPivotedAtItsFootIsNotMeasuredAsTwiceItsLength()
        {
            BuildingGenerator.Stairwell stairs =
                BuildingGenerator.PlanStairwell(Params(1UL), TestWorlds.NarrowStairsCatalog());

            Assert.That(
                MathF.Max(stairs.Flight.Width, stairs.Flight.Depth),
                Is.EqualTo(TestWorlds.NarrowStairsRun).Within(1e-4f),
                "the flight is as long as its art, not as long as the box that would centre it");
            Assert.That(MathF.Min(stairs.Flight.Width, stairs.Flight.Depth), Is.EqualTo(1f).Within(1e-4f));
        }

        /// <remarks>
        /// The other half of the same fix. A hole the size of the stairs is no use if the stairs
        /// are in the middle of it — what you want is to step off the floor straight onto the
        /// flight, which means its footprint starts where the floor stops.
        /// </remarks>
        [Test]
        public void TheFlightStandsFlushInTheCornerOfItsOpening()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var adrift = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(Params(seed), catalog);

                if (MathF.Abs(stairs.Flight.MinX - stairs.Shaft.MinX) > 1e-4f ||
                    MathF.Abs(stairs.Flight.MinZ - stairs.Shaft.MinZ) > 1e-4f)
                {
                    adrift.Add(
                        $"seed {seed}: the flight at {stairs.Flight} floats in {stairs.Shaft}");
                }
            }

            Assert.That(adrift, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The death-trap property, and it is absolute. A doorway into the shaft is a hole in the
        /// floor with a frame round it: a step through it on any storey above the ground is a fall
        /// down the stairwell, and nothing this tool can read says which end of a flight is its top,
        /// so there is no such thing here as a door that opens onto the landing instead.
        /// </para>
        /// <para>
        /// It used to be asserted the weak way — that a doorway only opened onto the stairs when
        /// its whole run did — because the alternative looked like sealing a room off. It is not:
        /// a run with nowhere legal to put a door is a cut the partition simply does not make, so
        /// the two rooms it would have divided stay one room. That is why this can be a flat claim
        /// with no exception in it, and <see cref="BuildingStructureTests"/> still walks the
        /// doorways to show every floor comes out in one piece.
        /// </para>
        /// </remarks>
        [Test]
        public void NoDoorwayAnywhereOpensIntoTheStairwell()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var deadly = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);
                Rect2 zone = stairs.Shaft.Expanded(BuildingGenerator.DoorwayClearance);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    for (int w = 0; w < plan.Walls.Count; w++)
                    {
                        PlanWall wall = plan.Walls[w];
                        if (wall.Doorway == PlanWall.NoDoorway)
                        {
                            continue;
                        }

                        if (Opening(wall, plan, wall.Doorway).Overlaps(zone))
                        {
                            deadly.Add(
                                $"seed {seed} floor {floor + 1}: wall {w} opens into the stairwell");
                        }
                    }
                }
            }

            Assert.That(deadly, Is.Empty);
        }

        /// <remarks>
        /// The price of the rule above, and the reason it does not cost connectivity: a cut whose
        /// every module would open into the shaft is not made at all. Asserted from the other end —
        /// every cut the partition <em>did</em> make has a doorway — because a partition that
        /// quietly emitted a wall with no way through it would pass the death-trap test and seal a
        /// room off, which is the failure the whole partition exists to make impossible.
        /// </remarks>
        [Test]
        public void ACutTheShaftWouldSealIsNotMadeAtAll()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var walledUp = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    for (int w = 4; w < plan.Walls.Count; w++)
                    {
                        if (plan.Walls[w].Doorway == PlanWall.NoDoorway)
                        {
                            walledUp.Add($"seed {seed} floor {floor + 1}: cut {w} was walled up");
                        }
                    }
                }
            }

            Assert.That(walledUp, Is.Empty);
        }

        /// <remarks>
        /// The entrance is the one opening that has to work — it is how you get into the building
        /// at all — and it is on an outside wall, where there is always somewhere else to put it.
        /// </remarks>
        [Test]
        public void TheWayInIsNeverTheWayOntoTheStairs()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var blocked = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                FloorPlan ground = BuildingGenerator.PlanFloor(
                    doc.Parameters, catalog, doc.Floors[0], 0);

                for (int w = 0; w < 4; w++)
                {
                    PlanWall wall = ground.Walls[w];
                    if (wall.Doorway == PlanWall.NoDoorway)
                    {
                        continue;
                    }

                    if (Opening(wall, ground, wall.Doorway)
                        .Overlaps(stairs.Shaft.Expanded(BuildingGenerator.DoorwayClearance)))
                    {
                        blocked.Add($"seed {seed}: the entrance opens onto the stairs");
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// Against <see cref="BuildingGenerator.Stairwell.Landing"/> rather than against the
        /// opening, and the difference between the two is the whole of what this asserts now. A
        /// crate off the opening by a centimetre is a crate standing on the bottom step; the way up
        /// is approached, turned onto and squeezed past, and a room that scattered its contents
        /// flush against all four sides of the shaft was a room the storey above was reached
        /// through sideways.
        /// </para>
        /// <para>
        /// All three of a room's passes, because they are three separate stages placing into one
        /// room and only the committed list keeps them out of each other — a centrepiece is
        /// proposed into the middle of a room and the middle of a small room is where the stairs
        /// are.
        /// </para>
        /// </remarks>
        [Test]
        public void ContentKeepsClearOfTheStairs()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var underfoot = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                Assert.That(stairs.Exists, Is.True, $"seed {seed}: this catalog has stairs in it");

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.Contains("/cover_") &&
                        !placed.StableId.Contains("/decor_") &&
                        !placed.StableId.Contains("/centrepiece_"))
                    {
                        continue;
                    }

                    if (PlacedGeometry.WorldFootprint(placed, catalog).Overlaps(stairs.Landing))
                    {
                        underfoot.Add($"seed {seed}: {placed.StableId} crowds the stairs");
                    }
                }
            }

            Assert.That(underfoot, Is.Empty);
        }

        /// <remarks>
        /// The clearance stated as the measurement it is, so a change to
        /// <see cref="BuildingGenerator.StairClearance"/> that did not reach the rectangle the
        /// rooms are furnished against would be caught here rather than by eye.
        /// </remarks>
        [Test]
        public void TheLandingIsTheOpeningPlusItsClearanceOnEverySide()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingGenerator.Stairwell stairs =
                BuildingGenerator.PlanStairwell(Params(1UL), catalog);

            Assert.That(stairs.Exists, Is.True);
            Assert.That(BuildingGenerator.StairClearance,
                Is.EqualTo(2f * BuildingGenerator.DoorwayClearance),
                "the way upstairs gets twice the approach a door gets");
            Assert.That(stairs.Landing.MinX,
                Is.EqualTo(stairs.Shaft.MinX - BuildingGenerator.StairClearance).Within(1e-4f));
            Assert.That(stairs.Landing.MaxZ,
                Is.EqualTo(stairs.Shaft.MaxZ + BuildingGenerator.StairClearance).Within(1e-4f));
        }

        /// <remarks>
        /// The stairwell is drawn from the building seed, so it is exactly as determined as the
        /// rest of a building nobody has touched — and, unlike everything else about a storey, it
        /// does not move when a storey is re-rolled.
        /// </remarks>
        [Test]
        public void RerollingAFloorLeavesTheStairwellWhereItWas()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc before = Generate(3UL);

            BuildingDoc after = BuildingGenerator.Generate(
                Params(3UL), catalog, BuildingGenerator.WithRerolledFloor(before.Floors, 1, 555UL));

            List<PlacedObject> was = Flights(before);
            List<PlacedObject> now = Flights(after);

            Assert.That(now.Count, Is.EqualTo(was.Count));
            for (int i = 0; i < was.Count; i++)
            {
                Assert.That(now[i].Pose, Is.EqualTo(was[i].Pose), now[i].StableId);
            }
        }

        [Test]
        public void ACatalogWithNoStairsBuildsAStackOfSeparateStoreys()
        {
            BuildingDoc doc = BuildingGenerator.Generate(Params(5UL), NoStairs());

            Assert.That(
                BuildingGenerator.PlanStairwell(doc.Parameters, NoStairs()).Exists, Is.False);
            Assert.That(Flights(doc), Is.Empty);
        }

        // --- the hole in the ceiling --------------------------------------------------------------

        /// <remarks>
        /// A flight of stairs into a solid ceiling is a sculpture. Every slab above a storey — the
        /// roof included — has the tiles over the flight left out; the ground floor's does not,
        /// because there is nothing under it to come up from.
        /// </remarks>
        [Test]
        public void EverySlabAboveTheGroundFloorIsOpenOverTheFlight()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var sealedOver = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.Contains("/slab/") || IsGroundSlab(placed))
                    {
                        continue;
                    }

                    if (PlacedGeometry.WorldFootprint(placed, catalog).Overlaps(stairs.Flight))
                    {
                        sealedOver.Add($"seed {seed}: {placed.StableId} is laid over the stairwell");
                    }
                }
            }

            Assert.That(sealedOver, Is.Empty);
        }

        /// <remarks>
        /// The other half of it, and the more useful direction: a hole cut wider than it needs to
        /// be is a floor with a chunk missing. The ground floor's slab is the untouched grid, so
        /// what the storey above has to be is that grid minus exactly the tiles the flight laps —
        /// no fewer, which would seal it, and no more, which would open the floor up.
        /// </remarks>
        [Test]
        public void TheHoleIsExactlyTheTilesTheFlightLapsOver()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = Generate(17UL);
            BuildingGenerator.Stairwell stairs =
                BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

            List<Rect2> ground = SlabTiles(doc, catalog, BuildingDoc.FloorIdPrefix(0));
            List<Rect2> above = SlabTiles(doc, catalog, BuildingDoc.FloorIdPrefix(1));

            Assert.That(ground, Is.Not.Empty, "the ground floor has no slab");

            int cut = 0;
            for (int i = 0; i < ground.Count; i++)
            {
                bool overFlight = ground[i].Overlaps(stairs.Flight);
                if (overFlight)
                {
                    cut++;
                }

                Assert.That(above.Contains(ground[i]), Is.EqualTo(!overFlight),
                    $"the tile at {ground[i]} is on the wrong side of the hole");
            }

            Assert.That(cut, Is.GreaterThan(0), "no tile was ever taken out for the stairs");
            Assert.That(above.Count, Is.EqualTo(ground.Count - cut));
        }

        /// <remarks>
        /// <para>
        /// The same property on the catalog that can break it. Every other suite here runs on one
        /// floor tile, and one floor tile is the case in which the grid a storey lays its slab on
        /// and the grid the stairwell was planned against cannot disagree. Give a workspace the
        /// demo's two-metre slab <em>and</em> the starter metre tile — which is what a sync
        /// actually produces — and a storey can draw either, so a hole cut to whole tiles of the
        /// coarse grid is up to a metre wider than the stairs on each axis.
        /// </para>
        /// <para>
        /// Stated as areas rather than as a tile count, because the two passes lay different
        /// pieces: what has to be true is that the floor covers the whole slab except the ground
        /// the flight stands on, whatever it is tiled from. Tiles never overlap, so the areas add.
        /// </para>
        /// </remarks>
        [Test]
        public void TheOpeningIsCutToTheFlightAndNothingMore()
        {
            Catalog catalog = TestWorlds.CoarseSlabCatalog();
            var wrong = new List<string>();
            int coarse = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                for (int floor = 1; floor < doc.Floors.Count; floor++)
                {
                    List<Rect2> tiles = SlabTiles(doc, catalog, BuildingDoc.FloorIdPrefix(floor));
                    if (tiles.Count == 0)
                    {
                        continue;
                    }

                    float laid = 0f;
                    for (int i = 0; i < tiles.Count; i++)
                    {
                        laid += tiles[i].Area;
                        if (tiles[i].Overlaps(stairs.Flight))
                        {
                            wrong.Add($"seed {seed} floor {floor}: the tile at {tiles[i]} " +
                                "is laid over the flight");
                        }
                    }

                    if (Sizes(tiles) > 1)
                    {
                        coarse++;
                    }

                    Rect2 slab = Extent(tiles);
                    float want = slab.Area - stairs.Flight.Area;

                    if (MathF.Abs(laid - want) > 1e-3f)
                    {
                        wrong.Add(
                            $"seed {seed} floor {floor}: {laid} m2 of floor over a {slab.Size} slab " +
                            $"with a {stairs.Flight.Size} flight in it, where {want} m2 is the slab " +
                            "less the stairs");
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
            Assert.That(coarse, Is.GreaterThan(0),
                "no storey ever had to floor a margin, so this proves nothing about coarse tiles");
        }

        /// <remarks>
        /// The failure as a person meets it, and the reason the property above is worth having. A
        /// hole a metre longer than the flight puts that metre at the top of the run: you climb the
        /// stairs and step off the last tread into the storey below. What has to be there is the
        /// floor immediately beyond each end of the run — the landing you step onto, and the square
        /// you step off to start climbing.
        /// </remarks>
        [Test]
        public void TheFloorAtEachEndOfTheFlightIsNeverCutAway()
        {
            Catalog catalog = TestWorlds.CoarseSlabCatalog();
            var drops = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(doc.Parameters, catalog);

                if (!stairs.Exists)
                {
                    continue;
                }

                Rect2 flight = stairs.Flight;
                Vec2 centre = flight.Center;

                // Half a metre past each end of the run, which is inside the tile you stand on.
                Vec2[] ends = flight.Depth >= flight.Width
                    ? new[]
                    {
                        new Vec2(centre.X, flight.MinZ - 0.5f), new Vec2(centre.X, flight.MaxZ + 0.5f),
                    }
                    : new[]
                    {
                        new Vec2(flight.MinX - 0.5f, centre.Y), new Vec2(flight.MaxX + 0.5f, centre.Y),
                    };

                for (int floor = 1; floor < doc.Floors.Count; floor++)
                {
                    List<Rect2> tiles = SlabTiles(doc, catalog, BuildingDoc.FloorIdPrefix(floor));
                    if (tiles.Count == 0)
                    {
                        continue;
                    }

                    Rect2 slab = Extent(tiles);
                    for (int e = 0; e < ends.Length; e++)
                    {
                        // An end past the edge of the slab is a flight against an outside wall,
                        // which is a landing the building never had rather than one that was cut.
                        if (!slab.Contains(ends[e]) || Covers(tiles, ends[e]))
                        {
                            continue;
                        }

                        drops.Add(
                            $"seed {seed} floor {floor}: nothing to stand on at {ends[e]}, " +
                            $"at the end of a flight at {flight}");
                    }
                }
            }

            Assert.That(drops, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The failure this catches is the one you see from outside the building rather than from
        /// inside it. An opening cut flush with the edge of the slab takes the floor out from under
        /// the outside wall standing on that edge — and a wall stands on <em>top</em> of its
        /// storey's slab, so what is left is a wall hanging a slab's thickness clear of the storey
        /// below, with daylight running along the outside of the building at that floor line. The
        /// roof's parapet does the same thing a storey higher.
        /// </para>
        /// <para>
        /// Asserted as the piece and the floor under it, not as a rule about where the stairs may
        /// go: what has to be true is that something is holding every piece of the shell up, and a
        /// rule about the shaft is only one way to arrange it. The probe is a quarter of a
        /// thickness inside each piece's own centre, which is inboard of the wall centrelines the
        /// slab is laid to and inside the parapet's own footprint.
        /// </para>
        /// <para>
        /// Run on the coarse catalog as well, because that is where the two grids disagree: a
        /// storey drawing the two-metre slab gives up whole two-metre tiles for a three-metre
        /// flight, and what is holding the wall up over the part it could not reach is the margin
        /// laid from the finer tile.
        /// </para>
        /// </remarks>
        [Test]
        public void NothingInTheShellIsLeftStandingOverTheStairwellOpening()
        {
            var catalogs = new[] { TestWorlds.StructuralCatalog(), TestWorlds.CoarseSlabCatalog() };
            var floating = new List<string>();

            for (int c = 0; c < catalogs.Length; c++)
            {
                Catalog catalog = catalogs[c];

                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                    // The ground floor's slab is never cut, so its walls were never at risk.
                    for (int floor = 1; floor < doc.Floors.Count; floor++)
                    {
                        string prefix = BuildingDoc.FloorIdPrefix(floor);
                        Unsupported(
                            doc, catalog, OutsideWalls(doc, prefix), SlabTiles(doc, catalog, prefix),
                            $"catalog {c} seed {seed} floor {floor}", floating);
                    }

                    Unsupported(
                        doc, catalog, Parapet(doc),
                        SlabTiles(doc, catalog, BuildingGenerator.RoofIdPrefix),
                        $"catalog {c} seed {seed} roof", floating);
                }
            }

            Assert.That(floating, Is.Empty);
        }

        // --- the roof -----------------------------------------------------------------------------

        [Test]
        public void TheTopOfTheBuildingIsCappedWithASlab()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = Generate(11UL);

            List<PlacedObject> roof = RoofSlab(doc);
            Assert.That(roof, Is.Not.Empty, "the top storey is open to the sky");

            float want = doc.Floors.Count * doc.Parameters.FloorHeight;
            for (int i = 0; i < roof.Count; i++)
            {
                Assert.That(roof[i].Pose.Position.Y, Is.EqualTo(want).Within(1e-4f), roof[i].StableId);
                Assert.That(roof[i].Metadata[BuildingGenerator.FloorKey],
                    Is.EqualTo(BuildingGenerator.RoofFloor),
                    "a roof object belongs to no storey");
            }

            // Laid on the same grid as every other slab, so the roof is the top storey's ceiling
            // rather than a second building sitting on it.
            Assert.That(roof.Count, Is.GreaterThan(0));
            for (int i = 0; i < roof.Count; i++)
            {
                Rect2 a = PlacedGeometry.WorldFootprint(roof[i], catalog);
                for (int j = i + 1; j < roof.Count; j++)
                {
                    Assert.That(a.Overlaps(PlacedGeometry.WorldFootprint(roof[j], catalog)), Is.False,
                        $"{roof[i].StableId} laps {roof[j].StableId}");
                }
            }
        }

        [Test]
        public void TheRoofIsFencedOnAllFourSides()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = Generate(11UL);

            var sides = new HashSet<string>(StringComparer.Ordinal);
            List<PlacedObject> parapet = Parapet(doc);

            Assert.That(parapet, Is.Not.Empty, "the roof has no edge to it");

            float want = doc.Floors.Count * doc.Parameters.FloorHeight + TestWorlds.StructuralSlabHeight;
            for (int i = 0; i < parapet.Count; i++)
            {
                string[] parts = parapet[i].StableId.Split('/');
                sides.Add(parts[2]);

                Assert.That(parapet[i].Pose.Position.Y, Is.EqualTo(want).Within(1e-4f),
                    $"{parapet[i].StableId} does not stand on the roof");
            }

            Assert.That(sides.Count, Is.EqualTo(4), "the roof is fenced on fewer than four sides");
        }

        /// <remarks>
        /// <para>
        /// A fence with a hole at every corner is not a fence, and that is what the roof had: the
        /// runs along Z used to stop half a piece short of the runs along X so that no two pieces
        /// would lap, and the four gaps that bought were the most visible thing on the building.
        /// </para>
        /// <para>
        /// So the ring is now built the way the walls below it are — every run spanning the whole
        /// of its own side and straddling its own centreline — and this asserts the thing that
        /// buys: the pieces form one closed loop. Walked rather than counted, because four runs of
        /// the right length that do not quite meet would tally exactly the same as four that do.
        /// </para>
        /// </remarks>
        [Test]
        public void TheParapetFormsOneClosedLoopWithNoGapInIt()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var broken = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                List<PlacedObject> parapet = Parapet(doc);

                Assert.That(parapet, Is.Not.Empty, $"seed {seed}: the roof has no edge to it");

                var pieces = new List<Rect2>(parapet.Count);
                for (int i = 0; i < parapet.Count; i++)
                {
                    pieces.Add(PlacedGeometry.WorldFootprint(parapet[i], catalog));
                }

                // A loop is a connected graph in which every node has exactly two neighbours. A gap
                // anywhere breaks one of the two: a piece at the end of a run drops to one
                // neighbour, and a whole side coming adrift leaves two islands.
                var neighbours = new int[pieces.Count];
                var group = new int[pieces.Count];
                for (int i = 0; i < group.Length; i++)
                {
                    group[i] = i;
                }

                for (int i = 0; i < pieces.Count; i++)
                {
                    for (int j = i + 1; j < pieces.Count; j++)
                    {
                        if (Rect2.Distance(pieces[i], pieces[j]) > 1e-4f)
                        {
                            continue;
                        }

                        neighbours[i]++;
                        neighbours[j]++;
                        Join(group, i, j);
                    }
                }

                for (int i = 0; i < pieces.Count; i++)
                {
                    if (neighbours[i] != 2)
                    {
                        broken.Add(
                            $"seed {seed}: {parapet[i].StableId} has {neighbours[i]} neighbour(s), " +
                            "so the loop is open there");
                    }
                }

                var islands = new HashSet<int>();
                for (int i = 0; i < pieces.Count; i++)
                {
                    islands.Add(Find(group, i));
                }

                if (islands.Count != 1)
                {
                    broken.Add($"seed {seed}: the parapet falls into {islands.Count} pieces");
                }
            }

            Assert.That(broken, Is.Empty);
        }

        /// <remarks>
        /// The other side of the bargain the loop is closed with. Two pieces of a run laid over one
        /// another would share a top face, which on a roof is the face a person is standing on and
        /// looking at — so the only laps allowed are the four corners, where a run ends buried in
        /// the middle of the run it meets, exactly as a wall junction does.
        /// </remarks>
        [Test]
        public void TheParapetOnlyLapsItselfAtTheFourCorners()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var lapped = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                List<PlacedObject> parapet = Parapet(doc);
                int corners = 0;

                for (int i = 0; i < parapet.Count; i++)
                {
                    Rect2 a = PlacedGeometry.WorldFootprint(parapet[i], catalog);
                    for (int j = i + 1; j < parapet.Count; j++)
                    {
                        if (!a.Overlaps(PlacedGeometry.WorldFootprint(parapet[j], catalog)))
                        {
                            continue;
                        }

                        corners++;
                        if (SideOf(parapet[i]) == SideOf(parapet[j]))
                        {
                            lapped.Add(
                                $"seed {seed}: {parapet[i].StableId} laps {parapet[j].StableId} " +
                                "along their own run");
                        }
                    }
                }

                if (corners != 4)
                {
                    lapped.Add($"seed {seed}: the parapet meets itself at {corners} places, not 4");
                }
            }

            Assert.That(lapped, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The corner gaps came back because a run was being shortened along its own travel as well
        /// as inset across it, so what is asserted now is the two separately: every piece stands on
        /// the roof, and the ring reaches the roof's outer edge on all four sides with nothing left
        /// over.
        /// </para>
        /// <para>
        /// Measured against the roof slab rather than the walls under it, which is the other half of
        /// the fix. The slab is what a person stands on and falls off, and where its edge is comes
        /// from the tiles rather than from the wall centrelines — a parapet lined up with the walls
        /// sat half a wall thickness out over the drop.
        /// </para>
        /// </remarks>
        [Test]
        public void TheParapetIsFlushWithEveryEdgeOfTheRoof()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var adrift = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                Rect2 roof = RoofExtent(doc, catalog);

                List<PlacedObject> parapet = Parapet(doc);
                Assert.That(parapet, Is.Not.Empty, $"seed {seed}: the roof has no edge to it");

                float minX = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxZ = float.MinValue;

                for (int i = 0; i < parapet.Count; i++)
                {
                    Rect2 piece = PlacedGeometry.WorldFootprint(parapet[i], catalog);

                    if (!roof.Contains(piece))
                    {
                        adrift.Add($"seed {seed}: {parapet[i].StableId} hangs off the roof at {piece}");
                    }

                    minX = MathF.Min(minX, piece.MinX);
                    minZ = MathF.Min(minZ, piece.MinZ);
                    maxX = MathF.Max(maxX, piece.MaxX);
                    maxZ = MathF.Max(maxZ, piece.MaxZ);
                }

                // Flush on all four sides: the ring's outer faces are the roof's own edges.
                if (MathF.Abs(minX - roof.MinX) > 1e-4f || MathF.Abs(maxX - roof.MaxX) > 1e-4f ||
                    MathF.Abs(minZ - roof.MinZ) > 1e-4f || MathF.Abs(maxZ - roof.MaxZ) > 1e-4f)
                {
                    adrift.Add(
                        $"seed {seed}: the parapet encloses [{minX}, {minZ}]..[{maxX}, {maxZ}] " +
                        $"where the roof is {roof}");
                }
            }

            Assert.That(adrift, Is.Empty);
        }

        /// <remarks>
        /// The corner property stated the way a person sees it: stand at a corner of the roof and
        /// there is parapet between you and the drop, on both of the sides that meet there. A ring
        /// that is closed everywhere except its corners passes every count and every span check
        /// there is, which is how this survived two fixes.
        /// </remarks>
        [Test]
        public void EveryCornerOfTheRoofIsClosedByBothOfItsRuns()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var open = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                Rect2 roof = RoofExtent(doc, catalog);

                var pieces = new List<Rect2>();
                List<PlacedObject> parapet = Parapet(doc);
                for (int i = 0; i < parapet.Count; i++)
                {
                    pieces.Add(PlacedGeometry.WorldFootprint(parapet[i], catalog));
                }

                // A point just inside each corner of the roof, on both of the sides meeting there.
                float step = TestWorlds.StructuralThickness * 0.25f;
                var corners = new[]
                {
                    new Vec2(roof.MinX + step, roof.MinZ + step),
                    new Vec2(roof.MaxX - step, roof.MinZ + step),
                    new Vec2(roof.MinX + step, roof.MaxZ - step),
                    new Vec2(roof.MaxX - step, roof.MaxZ - step),
                };

                for (int c = 0; c < corners.Length; c++)
                {
                    bool held = false;
                    for (int i = 0; i < pieces.Count && !held; i++)
                    {
                        held = pieces[i].Contains(corners[c]);
                    }

                    if (!held)
                    {
                        open.Add($"seed {seed}: the roof corner at {corners[c]} is open");
                    }
                }
            }

            Assert.That(open, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The one hole in a roof that is fenced all the way round otherwise, and what is under it
        /// is a flight dropping most of a storey — so a person walking the roof can step off the
        /// side of the stairs from either flank.
        /// </para>
        /// <para>
        /// The ends are the point of the test rather than an omission from it. One end of a flight
        /// is the way off it and the other is the drop, and nothing this tool can read says which,
        /// so a fence across both is a fence between the roof and the only way down from it. What
        /// is asserted is therefore both halves: the flanks are held, and the ends are clear.
        /// </para>
        /// </remarks>
        [Test]
        public void TheStairwellOpeningInTheRoofIsFencedAlongItsFlanksAndOpenAtItsEnds()
        {
            Catalog catalog = TestWorlds.NarrowStairsCatalog();
            var wrong = new List<string>();
            int fenced = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingParams parameters = Params(seed);
                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);
                BuildingGenerator.Stairwell stairs =
                    BuildingGenerator.PlanStairwell(parameters, catalog);

                List<PlacedObject> rail = Rail(doc);
                if (rail.Count == 0)
                {
                    continue;
                }

                fenced++;

                Rect2 flight = stairs.Flight;
                Vec2 centre = flight.Center;
                float top = doc.Floors.Count * parameters.FloorHeight + TestWorlds.StructuralSlabHeight;

                // Just past the middle of each end of the run: where you step off the top tread,
                // and where you would step onto the bottom one.
                Vec2[] ends = flight.Width > flight.Depth
                    ? new[]
                    {
                        new Vec2(flight.MinX - 0.25f, centre.Y), new Vec2(flight.MaxX + 0.25f, centre.Y),
                    }
                    : new[]
                    {
                        new Vec2(centre.X, flight.MinZ - 0.25f), new Vec2(centre.X, flight.MaxZ + 0.25f),
                    };

                for (int i = 0; i < rail.Count; i++)
                {
                    Rect2 piece = PlacedGeometry.WorldFootprint(rail[i], catalog);

                    // Flush against the edge of the opening is where it belongs, so what is
                    // measured is how far it reaches in rather than whether the two touch.
                    if (Overlap(piece, flight) > 1e-3f)
                    {
                        wrong.Add($"seed {seed}: {rail[i].StableId} stands in the opening at {piece}");
                    }

                    if (MathF.Abs(rail[i].Pose.Position.Y - top) > 1e-4f)
                    {
                        wrong.Add($"seed {seed}: {rail[i].StableId} does not stand on the roof");
                    }

                    for (int e = 0; e < ends.Length; e++)
                    {
                        if (piece.Contains(ends[e]))
                        {
                            wrong.Add(
                                $"seed {seed}: {rail[i].StableId} closes the end of the run at {ends[e]}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
            Assert.That(fenced, Is.GreaterThan(0), "no seed ever fenced the opening, so this proves nothing");
        }

        /// <remarks>
        /// The refinement rather than the feature. The rail stands outboard of the opening, so an
        /// opening near the edge of the roof would put it inside the ring that is already there —
        /// two parapets over one piece of ground, with the coplanar top faces that always follow.
        /// Nothing is lost by leaving that run out: what a person would fall over there is the edge
        /// of the roof, and the edge of the roof is fenced.
        /// </remarks>
        [Test]
        public void NoStairwellRailStandsInTheParapetRoundTheRoof()
        {
            var catalogs = new[] { TestWorlds.NarrowStairsCatalog(), TestWorlds.CoarseSlabCatalog() };
            var doubled = new List<string>();

            for (int c = 0; c < catalogs.Length; c++)
            {
                Catalog catalog = catalogs[c];

                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);
                    List<PlacedObject> rail = Rail(doc);
                    List<PlacedObject> parapet = Parapet(doc);

                    for (int i = 0; i < rail.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(rail[i], catalog);

                        for (int j = 0; j < parapet.Count; j++)
                        {
                            if (Overlap(piece, PlacedGeometry.WorldFootprint(parapet[j], catalog))
                                <= 1e-3f)
                            {
                                continue;
                            }

                            doubled.Add(
                                $"catalog {c} seed {seed}: {rail[i].StableId} laps " +
                                $"{parapet[j].StableId}");
                        }
                    }
                }
            }

            Assert.That(doubled, Is.Empty);
        }

        /// <remarks>
        /// A parapet round a roof that was never laid would be a fence in mid-air. The shell is art
        /// the catalog either has or does not, which is the bargain the walls and doorways make.
        /// </remarks>
        [Test]
        public void ACatalogWithNoFloorArtGetsNoRoofAndNoParapet()
        {
            BuildingDoc doc = BuildingGenerator.Generate(Params(5UL), TestWorlds.FurnishableCatalog());

            Assert.That(RoofSlab(doc), Is.Empty);
            Assert.That(Parapet(doc), Is.Empty);
            Assert.That(doc.Metadata.ContainsKey(BuildingGenerator.RoofHeightKey), Is.False);
            Assert.That(doc.RoofHeight, Is.EqualTo(0f));
        }

        /// <remarks>
        /// The height an exported building is bound into a catalog with. A row that under-reports
        /// by a parapet is wrong in the same way a footprint that under-reported would be.
        /// </remarks>
        [Test]
        public void ABuildingsHeightCountsWhatIsOnTopOfIt()
        {
            BuildingDoc doc = Generate(11UL);

            float storeys = doc.Floors.Count * doc.Parameters.FloorHeight;
            float roof = TestWorlds.StructuralSlabHeight + TestWorlds.StructuralParapetHeight;

            Assert.That(doc.RoofHeight, Is.EqualTo(roof).Within(1e-4f));
            Assert.That(doc.Height, Is.EqualTo(storeys + roof).Within(1e-4f));
        }

        [Test]
        public void ARoofAndItsStairsSurviveASaveAndLoad()
        {
            BuildingDoc doc = Generate(11UL);

            BuildingDoc reloaded = ArenaJson.DeserializeBuilding(ArenaJson.SerializeBuilding(doc));

            Assert.That(reloaded.Height, Is.EqualTo(doc.Height).Within(1e-6f));
            Assert.That(Flights(reloaded).Count, Is.EqualTo(Flights(doc).Count));
            Assert.That(RoofSlab(reloaded).Count, Is.EqualTo(RoofSlab(doc).Count));
        }

        // --- a one-storey building ---------------------------------------------------------------

        /// <remarks>
        /// A bungalow is the edge of both features at once: one flight, one hole, and it is in the
        /// roof. Getting the roof access from the general case rather than special-casing it is
        /// what makes the parapet mean something on a building of any height.
        /// </remarks>
        [Test]
        public void ASingleStoreyBuildingStillHasAWayOntoItsRoof()
        {
            BuildingParams parameters = Params(8UL);
            parameters.FloorCount = 1;

            Catalog catalog = TestWorlds.StructuralCatalog();
            BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);
            BuildingGenerator.Stairwell stairs = BuildingGenerator.PlanStairwell(parameters, catalog);

            Assert.That(Flights(doc).Count, Is.EqualTo(1));
            Assert.That(RoofSlab(doc), Is.Not.Empty);
            Assert.That(Parapet(doc), Is.Not.Empty);

            List<PlacedObject> roof = RoofSlab(doc);
            for (int i = 0; i < roof.Count; i++)
            {
                Assert.That(PlacedGeometry.WorldFootprint(roof[i], catalog).Overlaps(stairs.Flight),
                    Is.False, $"{roof[i].StableId} roofs over the only way up");
            }
        }

        // --- helpers ------------------------------------------------------------------------------

        static Catalog NoStairs() => TestWorlds.FurnishableCatalog();

        /// <summary>The outside wall and doorway pieces of one storey.</summary>
        static List<PlacedObject> OutsideWalls(BuildingDoc doc, string floorPrefix)
        {
            var found = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.StartsWith(floorPrefix + "/wall_", StringComparison.Ordinal) &&
                    IsOutsideWall(placed))
                {
                    found.Add(placed);
                }
            }

            return found;
        }

        /// <summary>
        /// Records every piece of <paramref name="shell"/> with no floor under it, ignoring the
        /// ones standing past the edge of the slab.
        /// </summary>
        /// <remarks>
        /// A piece out beyond the last tile is a slab the art pack could not reach to the wall
        /// centreline, which is floor the building never had rather than floor the stairs took —
        /// the same distinction <see cref="TheFloorAtEachEndOfTheFlightIsNeverCutAway"/> draws.
        /// </remarks>
        static void Unsupported(
            BuildingDoc doc,
            Catalog catalog,
            List<PlacedObject> shell,
            List<Rect2> tiles,
            string where,
            List<string> floating)
        {
            if (tiles.Count == 0 || shell.Count == 0)
            {
                return;
            }

            Rect2 slab = Extent(tiles);
            for (int i = 0; i < shell.Count; i++)
            {
                Vec2 at = JustInside(PlacedGeometry.WorldFootprint(shell[i], catalog), slab);
                if (!slab.Contains(at) || Covers(tiles, at))
                {
                    continue;
                }

                floating.Add($"{where}: {shell[i].StableId} stands over nothing at {at}");
            }
        }

        /// <summary>
        /// A point a quarter of a piece's own thickness inboard of its centre, which is the ground
        /// the piece needs under it whether or not it also overhangs the slab.
        /// </summary>
        static Vec2 JustInside(Rect2 piece, Rect2 slab)
        {
            Vec2 centre = piece.Center;
            float step = MathF.Min(piece.Width, piece.Depth) * 0.25f;

            return piece.Width >= piece.Depth
                ? new Vec2(centre.X, centre.Y + (centre.Y < slab.Center.Y ? step : -step))
                : new Vec2(centre.X + (centre.X < slab.Center.X ? step : -step), centre.Y);
        }

        /// <summary>The ground the roof slab covers, read off the tiles it was laid from.</summary>
        /// <remarks>
        /// Measured from the objects rather than recomputed from the plan, so a test of where the
        /// parapet sits cannot agree with the generator by repeating its arithmetic.
        /// </remarks>
        static Rect2 RoofExtent(BuildingDoc doc, Catalog catalog)
        {
            List<PlacedObject> tiles = RoofSlab(doc);
            Assert.That(tiles, Is.Not.Empty, "the roof was never laid");

            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < tiles.Count; i++)
            {
                Rect2 tile = PlacedGeometry.WorldFootprint(tiles[i], catalog);
                minX = MathF.Min(minX, tile.MinX);
                minZ = MathF.Min(minZ, tile.MinZ);
                maxX = MathF.Max(maxX, tile.MaxX);
                maxZ = MathF.Max(maxZ, tile.MaxZ);
            }

            return new Rect2(minX, minZ, maxX, maxZ);
        }

        /// <summary>How far one rectangle reaches into another, along whichever axis is shallower.</summary>
        static float Overlap(Rect2 a, Rect2 b) => MathF.Min(
            MathF.Min(a.MaxX, b.MaxX) - MathF.Max(a.MinX, b.MinX),
            MathF.Min(a.MaxZ, b.MaxZ) - MathF.Max(a.MinZ, b.MinZ));

        /// <summary>Which of the four runs a piece of parapet belongs to, from its stable id.</summary>
        static string SideOf(PlacedObject placed) => placed.StableId.Split('/')[2];

        /// <summary>
        /// True if a wall piece belongs to one of the four outside runs rather than to a cut.
        /// </summary>
        /// <remarks>
        /// Read off the stable id, because a run's index in the plan is what its id is built from:
        /// <see cref="FloorPlan.Walls"/> puts the four sides of the storey first and every cut
        /// after them, so <c>wall_00</c> to <c>wall_03</c> are the outside of the building.
        /// </remarks>
        static bool IsOutsideWall(PlacedObject placed)
        {
            string[] parts = placed.StableId.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("wall_", StringComparison.Ordinal))
                {
                    return int.Parse(parts[i].Substring("wall_".Length)) < FloorPlan.PerimeterRuns;
                }
            }

            return false;
        }

        static int Find(int[] group, int i)
        {
            while (group[i] != i)
            {
                i = group[i];
            }

            return i;
        }

        static void Join(int[] group, int a, int b) => group[Find(group, a)] = Find(group, b);

        /// <summary>The rectangle one module of a run occupies, doorway or not.</summary>
        static Rect2 Opening(PlanWall wall, FloorPlan plan, int module) => Rect2.FromCenterSize(
            wall.ModuleCenter(module),
            wall.AlongX
                ? new Vec2(wall.Module, plan.Thickness)
                : new Vec2(plan.Thickness, wall.Module));

        static bool IsGroundSlab(PlacedObject placed) =>
            placed.StableId.StartsWith(BuildingDoc.FloorIdPrefix(0) + "/", StringComparison.Ordinal);

        /// <summary>The smallest rectangle holding all of <paramref name="tiles"/>.</summary>
        static Rect2 Extent(List<Rect2> tiles)
        {
            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < tiles.Count; i++)
            {
                minX = MathF.Min(minX, tiles[i].MinX);
                minZ = MathF.Min(minZ, tiles[i].MinZ);
                maxX = MathF.Max(maxX, tiles[i].MaxX);
                maxZ = MathF.Max(maxZ, tiles[i].MaxZ);
            }

            return new Rect2(minX, minZ, maxX, maxZ);
        }

        /// <summary>True if any of <paramref name="tiles"/> is floor under <paramref name="at"/>.</summary>
        static bool Covers(List<Rect2> tiles, Vec2 at)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i].Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How many different sizes a slab was tiled from.</summary>
        static int Sizes(List<Rect2> tiles)
        {
            var seen = new List<Vec2>(2);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!seen.Contains(tiles[i].Size))
                {
                    seen.Add(tiles[i].Size);
                }
            }

            return seen.Count;
        }

        /// <summary>The world rectangles one storey's slab is tiled from.</summary>
        static List<Rect2> SlabTiles(BuildingDoc doc, Catalog catalog, string floorPrefix)
        {
            var tiles = new List<Rect2>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.StartsWith(floorPrefix + "/slab/", StringComparison.Ordinal))
                {
                    tiles.Add(PlacedGeometry.WorldFootprint(placed, catalog));
                }
            }

            return tiles;
        }

        static List<PlacedObject> Flights(BuildingDoc doc) => Matching(doc, StairsSegment);

        static List<PlacedObject> RoofSlab(BuildingDoc doc) =>
            Matching(doc, BuildingGenerator.RoofIdPrefix + "/slab/");

        static List<PlacedObject> Parapet(BuildingDoc doc) =>
            Matching(doc, $"{BuildingGenerator.RoofIdPrefix}/{BuildingGenerator.ParapetSegment}_");

        static List<PlacedObject> Rail(BuildingDoc doc) => Matching(
            doc, $"{BuildingGenerator.RoofIdPrefix}/{BuildingGenerator.StairwellRailSegment}_");

        static List<PlacedObject> Matching(BuildingDoc doc, string segment)
        {
            var found = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].StableId.Contains(segment))
                {
                    found.Add(doc.GeneratedObjects[i]);
                }
            }

            return found;
        }
    }
}
