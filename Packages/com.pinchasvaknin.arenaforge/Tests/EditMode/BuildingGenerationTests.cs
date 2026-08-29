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
    /// The building generator: determined by its document, re-rollable one floor at a time, and
    /// stacked without anything standing through a ceiling.
    /// </summary>
    /// <remarks>
    /// The per-floor re-roll is the reason this generator exists rather than being a second
    /// preset of the arena one, so the tests that matter most here are the ones asserting on
    /// serialised bytes. An assertion on object counts would pass just as happily against a
    /// generator that reshuffled every floor and happened to place the same number of crates.
    /// </remarks>
    public sealed class BuildingGenerationTests
    {
        /// <summary>Seeds swept by the overlap property. Enough for a rare collision to surface.</summary>
        const int OverlapSeeds = 200;

        /// <summary>Seeds swept by the determinism property.</summary>
        const int DeterminismSeeds = 100;

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed) =>
            BuildingGenerator.Generate(Params(seed), TestWorlds.FurnishableCatalog());

        // --- determinism ----------------------------------------------------------------------

        [Test]
        public void TheSameSeedAndParametersProduceTheSameBytes()
        {
            var mismatched = new List<ulong>();

            for (ulong seed = 1; seed <= DeterminismSeeds; seed++)
            {
                string first = ArenaJson.SerializeBuilding(Generate(seed));
                string second = ArenaJson.SerializeBuilding(Generate(seed));

                if (!string.Equals(first, second, StringComparison.Ordinal))
                {
                    mismatched.Add(seed);
                }
            }

            Assert.That(mismatched, Is.Empty, "seeds whose second generation differed from the first");
        }

        [Test]
        public void ABuildingIsFullyDeterminedByItsSeedBeforeAnythingIsRerolled()
        {
            // The floor seeds are stored, but their initial values are a fork of the building
            // seed — so a freshly generated building carries no randomness the parameters do not
            // already imply, exactly as a map does.
            BuildingDoc first = Generate(4242UL);
            List<BuildingFloor> derived = BuildingGenerator.DeriveFloors(Params(4242UL), null);

            Assert.That(first.Floors.Count, Is.EqualTo(derived.Count));
            for (int i = 0; i < derived.Count; i++)
            {
                Assert.That(first.Floors[i].Seed, Is.EqualTo(derived[i].Seed), $"floors[{i}]");
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentBuildings()
        {
            Assert.That(
                ArenaJson.SerializeBuilding(Generate(1UL)),
                Is.Not.EqualTo(ArenaJson.SerializeBuilding(Generate(2UL))));
        }

        // --- the per-floor re-roll ---------------------------------------------------------------

        /// <remarks>
        /// The property the whole feature is for, asserted on the serialised form of each floor's
        /// slice of the document. A count-based assertion would pass against a generator that
        /// moved every crate on floors 1 and 3 and put the same number back.
        /// </remarks>
        [Test]
        public void RerollingOneFloorLeavesEveryOtherFloorByteIdentical()
        {
            var changedTheWrongFloor = new List<string>();

            for (ulong seed = 1; seed <= 25; seed++)
            {
                BuildingDoc before = Generate(seed);

                for (int floor = 0; floor < before.Floors.Count; floor++)
                {
                    BuildingDoc after = BuildingGenerator.Generate(
                        Params(seed),
                        TestWorlds.FurnishableCatalog(),
                        BuildingGenerator.WithRerolledFloor(before.Floors, floor, 0xDEADBEEFUL + seed));

                    for (int other = 0; other < before.Floors.Count; other++)
                    {
                        string was = FloorBytes(before, other);
                        string now = FloorBytes(after, other);
                        bool same = string.Equals(was, now, StringComparison.Ordinal);

                        if (other == floor && same)
                        {
                            changedTheWrongFloor.Add(
                                $"seed {seed}: re-rolling floor {floor + 1} did not change it");
                        }
                        else if (other != floor && !same)
                        {
                            changedTheWrongFloor.Add(
                                $"seed {seed}: re-rolling floor {floor + 1} also moved floor {other + 1}");
                        }
                    }

                    Assert.That(after.Floors[floor].Seed, Is.EqualTo(0xDEADBEEFUL + seed),
                        "the re-rolled floor stores the seed it was given");
                }
            }

            Assert.That(changedTheWrongFloor, Is.Empty);
        }

        [Test]
        public void RerollingAFloorTwiceWithTheSameSeedIsIdempotent()
        {
            BuildingDoc once = Reroll(Generate(7UL), 1, 99UL);
            BuildingDoc twice = Reroll(once, 1, 99UL);

            Assert.That(
                ArenaJson.SerializeBuilding(twice), Is.EqualTo(ArenaJson.SerializeBuilding(once)));
        }

        [Test]
        public void AddingAStoreyLeavesTheStoreysBelowItByteIdentical()
        {
            BuildingDoc before = Generate(11UL);

            BuildingParams taller = Params(11UL);
            taller.FloorCount = before.Floors.Count + 1;
            BuildingDoc after = BuildingGenerator.Generate(
                taller, TestWorlds.FurnishableCatalog(), before.Floors);

            Assert.That(after.Floors.Count, Is.EqualTo(before.Floors.Count + 1));
            for (int floor = 0; floor < before.Floors.Count; floor++)
            {
                Assert.That(FloorBytes(after, floor), Is.EqualTo(FloorBytes(before, floor)),
                    $"floor {floor + 1} moved when a storey was added on top of it");
            }
        }

        [Test]
        public void RerollingAFloorThatIsNotThereIsRefused()
        {
            BuildingDoc doc = Generate(3UL);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => BuildingGenerator.WithRerolledFloor(doc.Floors, doc.Floors.Count, 1UL));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BuildingGenerator.WithRerolledFloor(doc.Floors, -1, 1UL));
        }

        // --- the shape of a building ------------------------------------------------------------

        [Test]
        public void EveryFloorsContentsStayInsideTheFootprint()
        {
            Catalog catalog = TestWorlds.FurnishableCatalog();
            var escaped = new List<string>();

            for (ulong seed = 1; seed <= OverlapSeeds; seed++)
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

        /// <remarks>
        /// What "floors stack without intersecting" means: nothing on a storey reaches the ground
        /// of the storey above it. The generator gets this by never offering a floor an entry
        /// taller than the floor height, which is a selection rule rather than a constraint
        /// because the constraint set is two-dimensional.
        /// </remarks>
        [Test]
        public void NothingOnAFloorStandsThroughTheOneAboveIt()
        {
            Catalog catalog = TestWorlds.FurnishableCatalog();
            var through = new List<string>();

            for (ulong seed = 1; seed <= OverlapSeeds; seed++)
            {
                BuildingParams parameters = Params(seed);
                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

                IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;
                for (int i = 0; i < objects.Count; i++)
                {
                    CatalogEntry entry = catalog.Find(objects[i].LogicalId);
                    float baseline = objects[i].Pose.Position.Y;
                    float top = baseline + entry.Height;
                    float ceiling = baseline + parameters.FloorHeight;

                    if (top > ceiling + 1e-4f)
                    {
                        through.Add(
                            $"seed {seed}: {objects[i].StableId} reaches {top.ToString("0.##", CultureInfo.InvariantCulture)} m " +
                            $"through a ceiling at {ceiling.ToString("0.##", CultureInfo.InvariantCulture)} m");
                    }
                }
            }

            Assert.That(through, Is.Empty);
        }

        [Test]
        public void EveryFloorSitsAtItsOwnElevation()
        {
            BuildingDoc doc = Generate(5UL);
            var seen = new Dictionary<string, float>(StringComparer.Ordinal);

            IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                string prefix = FloorOf(objects[i]);
                float y = objects[i].Pose.Position.Y;

                if (seen.TryGetValue(prefix, out float expected))
                {
                    Assert.That(y, Is.EqualTo(expected).Within(1e-4f),
                        $"{objects[i].StableId} is not level with the rest of its floor");
                    continue;
                }

                seen[prefix] = y;
            }

            Assert.That(seen.Count, Is.EqualTo(doc.Floors.Count), "every floor holds something");

            for (int floor = 0; floor < doc.Floors.Count; floor++)
            {
                string prefix = BuildingDoc.FloorIdPrefix(floor);
                Assert.That(seen.ContainsKey(prefix), Is.True, $"nothing was placed on {prefix}");
                Assert.That(seen[prefix], Is.EqualTo(floor * doc.Parameters.FloorHeight).Within(1e-4f));
            }
        }

        /// <remarks>
        /// The path is the whole point of a stable id: <c>building/floor_02/room_01/cover_03</c>
        /// says which storey, which room of it and which object without anything having to be
        /// looked up, and it survives a regeneration that did not disturb that branch.
        /// </remarks>
        [Test]
        public void StableIdsNestUnderTheirFloorAndTheirRoom()
        {
            BuildingDoc doc = Generate(9UL);

            Assert.That(doc.GeneratedObjects, Is.Not.Empty);
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                string id = doc.GeneratedObjects[i].StableId;
                Assert.That(id, Does.StartWith("building/floor_"));
                Assert.That(id.Split('/').Length, Is.EqualTo(4), id);
            }

            Assert.That(doc.GeneratedObjects[0].StableId,
                Is.EqualTo("building/floor_01/room_00/cover_00"));
            Assert.That(BuildingDoc.FloorIdPrefix(0), Is.EqualTo("building/floor_01"),
                "the ground floor is floor 1, because a floor number is read by a person");
        }

        // --- placement ----------------------------------------------------------------------

        [Test]
        public void NoTwoObjectsOnAFloorOverlap()
        {
            Catalog catalog = TestWorlds.FurnishableCatalog();
            var collisions = new string[OverlapSeeds + 1];

            Parallel.For(1, OverlapSeeds + 1, seed =>
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params((ulong)seed), catalog);
                collisions[seed] = FindOverlap(doc, catalog, seed);
            });

            var broken = new StringBuilder();
            int count = 0;
            for (int seed = 1; seed <= OverlapSeeds; seed++)
            {
                if (collisions[seed] == null)
                {
                    continue;
                }

                count++;
                if (count <= 8)
                {
                    broken.AppendLine().Append("  ").Append(collisions[seed]);
                }
            }

            Assert.That(count, Is.Zero, $"{count} of {OverlapSeeds} seeds overlapped:{broken}");
        }

        /// <remarks>
        /// Grouped by floor, because two objects on different storeys are allowed to share a
        /// footprint — one is standing above the other. Objects in different rooms of one floor
        /// are not: a wall between them is not a licence to overlap through it.
        /// </remarks>
        static string FindOverlap(BuildingDoc doc, Catalog catalog, int seed)
        {
            var byFloor = new Dictionary<string, List<PlacedObject>>(StringComparer.Ordinal);
            IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;

            for (int i = 0; i < objects.Count; i++)
            {
                string floor = FloorOf(objects[i]);
                if (!byFloor.TryGetValue(floor, out List<PlacedObject> onFloor))
                {
                    onFloor = new List<PlacedObject>();
                    byFloor[floor] = onFloor;
                }

                onFloor.Add(objects[i]);
            }

            foreach (KeyValuePair<string, List<PlacedObject>> floor in byFloor)
            {
                List<PlacedObject> onFloor = floor.Value;
                for (int i = 0; i < onFloor.Count; i++)
                {
                    Rect2 a = PlacedGeometry.WorldFootprint(onFloor[i], catalog);
                    for (int j = i + 1; j < onFloor.Count; j++)
                    {
                        Rect2 b = PlacedGeometry.WorldFootprint(onFloor[j], catalog);
                        if (a.Overlaps(b))
                        {
                            return $"seed {seed}: {onFloor[i].StableId} overlaps {onFloor[j].StableId}";
                        }
                    }
                }
            }

            return null;
        }

        // --- refusals -------------------------------------------------------------------------

        [Test]
        public void ACatalogWithNothingShortEnoughIsRefusedRatherThanBuiltEmpty()
        {
            BuildingParams parameters = Params(1UL);
            parameters.FloorHeight = 0.5f;

            var error = Assert.Throws<InvalidOperationException>(
                () => BuildingGenerator.Generate(parameters, TestWorlds.FurnishableCatalog()));

            Assert.That(error.Message, Does.Contain("0.5"));
        }

        [Test]
        public void ABuildingNeedsAtLeastOneFloor()
        {
            BuildingParams parameters = Params(1UL);
            parameters.FloorCount = 0;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => BuildingGenerator.Generate(parameters, TestWorlds.FurnishableCatalog()));
        }

        [Test]
        public void AWallMarginThatSwallowsTheFootprintIsRefused()
        {
            BuildingParams parameters = Params(1UL);
            parameters.WallMargin = 6f;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => BuildingGenerator.Generate(parameters, TestWorlds.FurnishableCatalog()));
        }

        [Test]
        public void AZeroContentDensityBuildsAnEmptyShell()
        {
            BuildingParams parameters = Params(1UL);
            parameters.ContentDensity = 0f;

            BuildingDoc doc = BuildingGenerator.Generate(parameters, TestWorlds.FurnishableCatalog());

            Assert.That(doc.GeneratedObjects, Is.Empty);
            Assert.That(doc.Floors.Count, Is.EqualTo(parameters.FloorCount),
                "the floors are still there — they just have nothing on them");
        }

        // --- the rules a floor does not have ----------------------------------------------------

        /// <remarks>
        /// A floor is a bounded region like a lane is, but it is not part of an arena: it has no
        /// neighbouring bands and no spawns at its ends. Rather than being quietly satisfied,
        /// those two rules refuse — a rule that can never reject anything would sit in the
        /// placement statistics reporting zero rejections, which reads as a rule that passed.
        /// </remarks>
        [Test]
        public void TheTwoArenaOnlyRulesRefuseOverABoundedRegion()
        {
            var region = new Rect2(-5f, -5f, 5f, 5f);
            CatalogEntry entry = TestWorlds.SampleCatalog().Find("cover/low/crate_wood_01");
            Placement candidate = Placement.AtQuarterTurn(entry, Vec2.Zero, 0);

            var lane = new ConstraintSet(region, new[] { PlacementConstraint.WithinLane("lane_mid") });
            var spawn = new ConstraintSet(region, new[] { PlacementConstraint.ClearOfSpawn(3f) });

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => lane.Evaluate(candidate)).Message,
                Does.Contain("bounded region"));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => spawn.Evaluate(candidate)).Message,
                Does.Contain("bounded region"));
        }

        [Test]
        public void TheSixOtherRulesWorkOverABoundedRegionUnchanged()
        {
            var region = new Rect2(-5f, -5f, 5f, 5f);
            CatalogEntry entry = TestWorlds.SampleCatalog().Find("cover/low/crate_wood_01");

            var constraints = new ConstraintSet(region, new[]
            {
                PlacementConstraint.OnGrid(1f),
                PlacementConstraint.InsidePlayfield(),
                PlacementConstraint.NoOverlap(0.5f),
            });

            Placement inside = Placement.AtQuarterTurn(entry, Vec2.Zero, 0);
            Assert.That(constraints.Evaluate(inside).IsOk, Is.True);

            Placement outside = Placement.AtQuarterTurn(entry, new Vec2(6f, 0f), 0);
            Assert.That(constraints.Evaluate(outside).Failed.Kind,
                Is.EqualTo(ConstraintKind.InsidePlayfield));

            constraints.Commit(inside);
            Assert.That(constraints.Evaluate(inside).Failed.Kind, Is.EqualTo(ConstraintKind.NoOverlap));
        }

        // --- helpers ------------------------------------------------------------------------

        /// <summary>The storey an object stands on, from the first two segments of its id.</summary>
        static string FloorOf(PlacedObject placed)
        {
            string[] parts = placed.StableId.Split('/');
            return parts[0] + "/" + parts[1];
        }

        static BuildingDoc Reroll(BuildingDoc doc, int floor, ulong seed) =>
            BuildingGenerator.Generate(
                doc.Parameters,
                TestWorlds.FurnishableCatalog(),
                BuildingGenerator.WithRerolledFloor(doc.Floors, floor, seed));

        /// <summary>
        /// One floor's slice of a document, serialised. Its stored seed and every object standing
        /// on it, and nothing else — so a comparison says whether that floor changed without
        /// being disturbed by what happened on the others.
        /// </summary>
        /// <remarks>
        /// The parameters are deliberately left at their defaults rather than copied from
        /// <paramref name="doc"/>. Adding a storey changes the floor count, and a slice carrying
        /// it would differ for every floor of a taller building whether or not anything on those
        /// floors moved.
        /// </remarks>
        static string FloorBytes(BuildingDoc doc, int floor)
        {
            var slice = new BuildingDoc();
            slice.Floors.Add(doc.Floors[floor]);

            string prefix = BuildingDoc.FloorIdPrefix(floor) + "/";
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].StableId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    slice.GeneratedObjects.Add(doc.GeneratedObjects[i]);
                }
            }

            return ArenaJson.SerializeBuilding(slice);
        }
    }
}
