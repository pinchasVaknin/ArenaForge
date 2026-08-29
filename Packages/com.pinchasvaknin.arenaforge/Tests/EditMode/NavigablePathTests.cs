using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The walkways a floor plan reserves between the doors of a room, and the rule that keeps
    /// them clear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one interior property that is not a matter of degree. A room with too little
    /// cover in it is a poor room and a map full of them is a poor map, and both are still maps;
    /// a room whose only route between two doors is blocked by a crate is a room a player walks
    /// into and cannot leave, which is not a map at all. Everything below is written as a sweep
    /// rather than a case, because the failure it is looking for is one seed in a hundred and a
    /// single hand-picked plan would not find it.
    /// </para>
    /// <para>
    /// What is asserted is the reservation and its enforcement, not a pathfind over the finished
    /// floor. The two are the same claim only because nothing is allowed to stand on a reserved
    /// strip — so the strips are checked to be connected and to reach every door, and the placed
    /// objects are checked to be off them, and between those two the route survives.
    /// </para>
    /// </remarks>
    public sealed class NavigablePathTests
    {
        /// <summary>Seeds swept by the placement properties.</summary>
        const int Seeds = 60;

        /// <summary>Seeds swept by the plan-only properties, which are far cheaper.</summary>
        const int PlanSeeds = 400;

        const float Module = 2f;
        const float Thickness = 0.2f;

        /// <summary>Slack for comparing two rectangles that are meant to touch exactly.</summary>
        const float Tolerance = 1e-3f;

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        // --- the reservation itself ---------------------------------------------------------------

        /// <remarks>
        /// The claim the whole feature rests on. A room's strips have to form one connected region
        /// — two separate puddles of reserved floor would each be clear and would not join
        /// anything to anything — and every door of that room has to touch it.
        /// </remarks>
        [Test]
        public void EveryDoorOfARoomReachesOneConnectedWalkway()
        {
            var failures = new List<string>();

            for (ulong seed = 1; seed <= PlanSeeds; seed++)
            {
                FloorPlan plan = Plan(seed, out Rect2 shaft);

                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    PlanRoom room = plan.Rooms[r];
                    List<Rect2> doors = DoorsOf(plan, room);

                    if (room.Paths.Count == 0)
                    {
                        continue;
                    }

                    if (!IsOneRegion(room.Paths))
                    {
                        failures.Add($"seed {seed}: {room.Id} reserved floor in disconnected pieces");
                    }

                    for (int i = 0; i < doors.Count; i++)
                    {
                        if (!Touches(room.Paths, doors[i]))
                        {
                            failures.Add($"seed {seed}: {room.Id} has a door reaching no walkway");
                            break;
                        }
                    }

                    if (HasShaft(room, shaft) && !Touches(room.Paths, shaft))
                    {
                        failures.Add($"seed {seed}: {room.Id} holds the shaft and reserved no route to it");
                    }
                }
            }

            Assert.That(failures, Is.Empty);
        }

        /// <remarks>
        /// The other half of the same claim, and the one that would go unnoticed: a room that
        /// reserved nothing at all would pass every assertion above, because there is no
        /// disconnected piece and no door failing to touch a strip that does not exist.
        /// </remarks>
        [Test]
        public void ARoomWithTwoWaysOutAlwaysReservesAWalkway()
        {
            var failures = new List<string>();
            var reserved = 0;

            for (ulong seed = 1; seed <= PlanSeeds; seed++)
            {
                FloorPlan plan = Plan(seed, out Rect2 shaft);

                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    PlanRoom room = plan.Rooms[r];
                    int ways = DoorsOf(plan, room).Count + (HasShaft(room, shaft) ? 1 : 0);

                    if (ways >= 2 && room.Paths.Count == 0)
                    {
                        failures.Add($"seed {seed}: {room.Id} has {ways} ways out and reserved nothing");
                    }

                    if (room.Paths.Count > 0)
                    {
                        reserved++;
                    }
                }
            }

            Assert.That(failures, Is.Empty);
            Assert.That(reserved, Is.GreaterThan(0), "no room on any floor ever reserved a walkway");
        }

        /// <remarks>
        /// A single door is not a passageway, and reserving a strip leading from it to nowhere
        /// would take the middle of the room away from the cover that is supposed to stand there.
        /// The clearance every doorway already keeps in front of itself is what makes that one
        /// door usable.
        /// </remarks>
        [Test]
        public void ARoomWithOneWayOutReservesNothing()
        {
            var failures = new List<string>();

            for (ulong seed = 1; seed <= PlanSeeds; seed++)
            {
                FloorPlan plan = Plan(seed, out Rect2 shaft);

                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    PlanRoom room = plan.Rooms[r];
                    int ways = DoorsOf(plan, room).Count + (HasShaft(room, shaft) ? 1 : 0);

                    if (ways < 2 && room.Paths.Count > 0)
                    {
                        failures.Add($"seed {seed}: {room.Id} has {ways} way(s) out and reserved floor");
                    }
                }
            }

            Assert.That(failures, Is.Empty);
        }

        /// <remarks>
        /// A room's reservation is floor in that room. A strip reaching past its walls would be
        /// describing the neighbour's floor, which the neighbour reserves for itself from its own
        /// doors — and a rule fed the wrong room's geometry rejects placements for no reason
        /// anybody looking at the map could see.
        /// </remarks>
        [Test]
        public void NoWalkwayReachesOutsideTheRoomItBelongsTo()
        {
            var failures = new List<string>();

            for (ulong seed = 1; seed <= PlanSeeds; seed++)
            {
                FloorPlan plan = Plan(seed, out _);

                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    PlanRoom room = plan.Rooms[r];
                    for (int i = 0; i < room.Paths.Count; i++)
                    {
                        if (!room.Bounds.Expanded(Tolerance).Contains(room.Paths[i]))
                        {
                            failures.Add($"seed {seed}: {room.Id} reserved floor outside itself");
                            break;
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty);
        }

        /// <remarks>
        /// A strip narrower than a person is a reservation that promises a route and does not
        /// deliver one. Measured on the short side of each rectangle, which is the width of the
        /// leg it covers — the long side is how far that leg runs.
        /// </remarks>
        [Test]
        public void AWalkwayIsAsWideAsItSaysItIs()
        {
            var failures = new List<string>();

            for (ulong seed = 1; seed <= PlanSeeds; seed++)
            {
                FloorPlan plan = Plan(seed, out _);

                for (int r = 0; r < plan.Rooms.Count; r++)
                {
                    PlanRoom room = plan.Rooms[r];
                    for (int i = 0; i < room.Paths.Count; i++)
                    {
                        Rect2 strip = room.Paths[i];
                        float across = MathF.Min(strip.Width, strip.Depth);

                        // Clipped to the room, so a strip may come out narrower than the walkway
                        // where it runs alongside a wall — never wider, and never nothing.
                        if (across <= 0f || across > FloorPlan.PathWidth + Tolerance)
                        {
                            failures.Add(
                                $"seed {seed}: {room.Id} reserved a strip {across} metres across");
                            break;
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty);
        }

        /// <remarks>
        /// Where a walkway runs is a fact about where the doors ended up, so it takes no draw from
        /// any stream. If it took one, adding this stage would have shifted every placement made
        /// after it and the same seed would no longer build the same floor.
        /// </remarks>
        [Test]
        public void TheSameSeedReservesTheSameFloor()
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                FloorPlan first = Plan(seed, out _);
                FloorPlan second = Plan(seed, out _);

                Assert.That(second.Rooms.Count, Is.EqualTo(first.Rooms.Count));

                for (int r = 0; r < first.Rooms.Count; r++)
                {
                    Assert.That(
                        second.Rooms[r].Paths, Is.EqualTo(first.Rooms[r].Paths),
                        $"seed {seed}: {first.Rooms[r].Id} reserved different floor on the second run");
                }
            }
        }

        // --- and nothing is allowed to stand on it ------------------------------------------------

        /// <remarks>
        /// The rule the reservation exists for, swept over whole buildings rather than plans:
        /// cover, decor and everything else a room is filled with, against the strips of the room
        /// its own metadata names.
        /// </remarks>
        [Test]
        public void NothingAFloorHoldsStandsOnAReservedWalkway()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var blocked = new List<string>();
            var checkedAgainst = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    var paths = new Dictionary<string, IReadOnlyList<Rect2>>(StringComparer.Ordinal);
                    for (int r = 0; r < plan.Rooms.Count; r++)
                    {
                        paths[plan.Rooms[r].Id] = plan.Rooms[r].Paths;
                    }

                    string prefix = BuildingDoc.FloorIdPrefix(floor) + "/";
                    for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                    {
                        PlacedObject placed = doc.GeneratedObjects[i];
                        if (!placed.StableId.StartsWith(prefix, StringComparison.Ordinal) ||
                            !placed.Metadata.TryGetValue(BuildingGenerator.RoomKey, out string room) ||
                            !paths.TryGetValue(room, out IReadOnlyList<Rect2> strips))
                        {
                            continue;
                        }

                        checkedAgainst++;
                        Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);

                        for (int p = 0; p < strips.Count; p++)
                        {
                            if (footprint.Overlaps(strips[p]))
                            {
                                blocked.Add($"seed {seed}: {placed.StableId} stands in the way");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
            Assert.That(checkedAgainst, Is.GreaterThan(0), "no room contents were ever tested");
        }

        /// <remarks>
        /// The rule has to be one the constraint set actually evaluates, and a rule that never
        /// rejects anything is indistinguishable in the statistics from one that was never asked.
        /// Over sixty seeds of a building whose rooms are filled to a density, some candidate
        /// lands on a walkway; if none ever did, the rule would be costing an evaluation and
        /// buying nothing, and the reservation would be wide enough to be pointless.
        /// </remarks>
        [Test]
        public void TheWalkwayRuleIsOneThatActuallyTurnsCandidatesAway()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            string key = BuildingGenerator.StatsPrefix + "rejected_" +
                         PlacementStats.MetadataName(ConstraintKind.OffReservedPath);
            var rejections = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                if (doc.Metadata.TryGetValue(key, out string tally) &&
                    int.TryParse(tally, out int count))
                {
                    rejections += count;
                }
            }

            Assert.That(rejections, Is.GreaterThan(0),
                "no candidate was ever turned away by the walkway rule");
        }

        /// <remarks>
        /// A room still has to be worth walking into. The reservation takes floor away from the
        /// content pass, and a width chosen badly would take all of it — leaving a building of
        /// empty rooms joined by immaculate corridors, which passes every property above.
        /// </remarks>
        [Test]
        public void RoomsAreStillFilledWithTheWalkwaysReserved()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var contents = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    string id = doc.GeneratedObjects[i].StableId;
                    if (id.Contains("/cover_") || id.Contains("/decor_"))
                    {
                        contents++;
                    }
                }
            }

            Assert.That(contents, Is.GreaterThan(Seeds),
                "the reservation left the rooms with nothing in them");
        }

        // --- helpers --------------------------------------------------------------------------------

        /// <summary>
        /// A plan drawn over a footprint that varies with the seed, with a shaft in it on every
        /// other run.
        /// </summary>
        /// <remarks>
        /// Varying the footprint rather than sweeping one shape, because how a room comes out is
        /// mostly a fact about how many modules the partition had to divide — a fixed rectangle
        /// sweeps a thousand seeds through the same handful of room shapes.
        /// </remarks>
        static FloorPlan Plan(ulong seed, out Rect2 shaft)
        {
            Rect2 area = Rect2.FromCenterSize(
                Vec2.Zero, new Vec2(12f + seed % 5 * 4f, 12f + seed / 5 % 5 * 4f));

            Rect2 bounds = FloorPlan.BoundsOf(area, Module, Thickness);
            shaft = seed % 2 == 0
                ? new Rect2(bounds.MinX + 2f, bounds.MinZ + 2f, bounds.MinX + 4f, bounds.MinZ + 5f)
                : Rect2.Zero;

            var rng = new Rng(seed);
            return FloorPlan.Build(
                area, Module, Thickness, true, shaft, BuildingGenerator.DoorwayClearance, ref rng);
        }

        /// <summary>The doorways that open into one room, as the rectangles a person walks through.</summary>
        static List<Rect2> DoorsOf(FloorPlan plan, PlanRoom room)
        {
            var doors = new List<Rect2>();

            for (int i = 0; i < plan.Walls.Count; i++)
            {
                PlanWall wall = plan.Walls[i];
                if (wall.Doorway == PlanWall.NoDoorway)
                {
                    continue;
                }

                Vec2 at = wall.ModuleCenter(wall.Doorway);
                if (!OnEdgeOf(room.Bounds, at, wall.AlongX))
                {
                    continue;
                }

                doors.Add(Rect2.FromCenterSize(
                    at,
                    wall.AlongX
                        ? new Vec2(wall.Module, plan.Thickness)
                        : new Vec2(plan.Thickness, wall.Module)));
            }

            return doors;
        }

        static bool OnEdgeOf(Rect2 room, Vec2 at, bool alongX) => alongX
            ? (MathF.Abs(at.Y - room.MinZ) <= Tolerance || MathF.Abs(at.Y - room.MaxZ) <= Tolerance) &&
              at.X >= room.MinX - Tolerance && at.X <= room.MaxX + Tolerance
            : (MathF.Abs(at.X - room.MinX) <= Tolerance || MathF.Abs(at.X - room.MaxX) <= Tolerance) &&
              at.Y >= room.MinZ - Tolerance && at.Y <= room.MaxZ + Tolerance;

        /// <summary>True if the stairwell shaft is a real rectangle and it lands in this room.</summary>
        /// <remarks>
        /// The size is checked first because <see cref="Rect2.Zero"/> is what a floor with no
        /// stairwell reserves, and a rectangle of no size still overlaps every room that holds the
        /// origin — which would count a shaft that does not exist as a second way out of the room
        /// in the middle of the building.
        /// </remarks>
        static bool HasShaft(PlanRoom room, Rect2 shaft) =>
            shaft.Width > 0f && shaft.Depth > 0f && room.Bounds.Overlaps(shaft);

        static bool Touches(IReadOnlyList<Rect2> strips, Rect2 what)
        {
            for (int i = 0; i < strips.Count; i++)
            {
                if (strips[i].Expanded(Tolerance).Overlaps(what))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if every strip is reachable from the first by stepping between overlaps.</summary>
        static bool IsOneRegion(IReadOnlyList<Rect2> strips)
        {
            var seen = new bool[strips.Count];
            var pending = new List<int> { 0 };
            seen[0] = true;
            int reached = 1;

            while (pending.Count > 0)
            {
                int at = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);

                for (int i = 0; i < strips.Count; i++)
                {
                    if (!seen[i] && strips[at].Expanded(Tolerance).Overlaps(strips[i]))
                    {
                        seen[i] = true;
                        reached++;
                        pending.Add(i);
                    }
                }
            }

            return reached == strips.Count;
        }
    }
}
