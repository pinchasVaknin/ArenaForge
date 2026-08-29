using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// How a storey's walls meet its ceiling: which art is offered a floor at all, and how far each
    /// piece is stretched once it gets there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The headroom is the one measurement in this tool where a parameter the user typed has to
    /// agree with a size the catalog declared, and both ways of disagreeing are quiet. Too tall by
    /// a rounding error and no wall art is offered at all, so every storey comes back as one
    /// undivided room — which looks exactly like a catalog with no walls in it. Too short by
    /// anything and there is a strip of daylight along the top of every wall in the building.
    /// </para>
    /// <para>
    /// So the two halves are tested together: what the tolerance lets through, and that everything
    /// it lets through is then fitted onto the slab above it exactly.
    /// </para>
    /// </remarks>
    public sealed class BuildingHeadroomTests
    {
        /// <summary>Seeds swept by the properties that hold for every building.</summary>
        const int Seeds = 40;

        const string WallSegment = "/wall_";

        /// <summary>The floor height and slab thickness from the bug this suite was written for.</summary>
        /// <remarks>
        /// A 3.1 m storey on a 0.2 m slab, with the 2.9 m wall an art pack would ship for it. The
        /// numbers matter: see <see cref="TheHeadroomArithmeticThisSuiteExistsForReallyDoesMiss"/>.
        /// </remarks>
        const float AwkwardFloorHeight = 3.1f;

        const float AwkwardSlab = 0.2f;

        const float AwkwardWall = 2.9f;

        /// <summary>How thick a floor tile the package's own starter art writes, in metres.</summary>
        /// <remarks>
        /// <c>ArenaAssetBuilder.FloorSize</c>. Twice the demo pack's slab, which is why a storey
        /// pitch the demo pack is happy with is not one a workspace is — see
        /// <see cref="TheDefaultStoreyPitchClearsTheArtThePackageShips"/>.
        /// </remarks>
        const float ShippedSlab = 0.2f;

        /// <summary>How tall the wall, doorway and window art the package ships stands, in metres.</summary>
        const float ShippedWall = 2.9f;

        // --- what is offered a floor ------------------------------------------------------------

        /// <remarks>
        /// The evidence, asserted rather than described, because the whole suite rests on it and it
        /// is invisible in the source: a 2.9 m wall does not fit under "3.1 less 0.2" in single
        /// precision, by about a ten-millionth of a metre. Written as the comparison the generator
        /// used to make, so that a future runtime on which the arithmetic comes out even shows up
        /// here as a failure to explain rather than as a suite quietly testing nothing.
        /// </remarks>
        [Test]
        public void TheHeadroomArithmeticThisSuiteExistsForReallyDoesMiss()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;

            Assert.That(AwkwardWall, Is.GreaterThan(headroom),
                "the float arithmetic behind the bug no longer misses, so this suite is not testing it");
            Assert.That(AwkwardWall - headroom, Is.LessThan(1e-6f),
                "and it misses by a rounding error rather than by a real amount");
        }

        /// <remarks>
        /// The bug itself. A user types a floor height, the slab comes off it, and the wall the art
        /// pack ships for that storey is refused for a difference no one can see or type — leaving a
        /// building with no interior walls and nothing to say why.
        /// </remarks>
        [Test]
        public void AWallMissingByARoundingErrorIsStillOfferedToTheFloor()
        {
            BuildingDoc doc = Generate(1UL, AwkwardFloorHeight, ShellCatalog(AwkwardSlab, AwkwardWall));

            Assert.That(WallPieces(doc), Is.Not.Empty,
                "a storey whose wall art misses the headroom by a rounding error has no walls");
        }

        /// <remarks>
        /// Stated on the plan as well as on the objects, because the two failures are different
        /// sizes. No wall art means no module to lay a partition out in, so the storey is not
        /// merely unwalled — it is one room, and every room count, corridor count and content
        /// distribution in the document is the single-room answer.
        /// </remarks>
        [Test]
        public void AFloorWhoseWallsMissedByARoundingErrorIsStillPartitioned()
        {
            Catalog catalog = ShellCatalog(AwkwardSlab, AwkwardWall);
            BuildingParams parameters = Params(1UL, AwkwardFloorHeight);
            BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

            FloorPlan plan = BuildingGenerator.PlanFloor(parameters, catalog, doc.Floors[0], 0);

            Assert.That(plan.Walls, Is.Not.Empty, "the storey has no shell to divide it");
            Assert.That(plan.Rooms.Count, Is.GreaterThan(1), "the storey was never partitioned");
        }

        /// <remarks>
        /// The other side of the tolerance, and the reason it is a fixed small number rather than
        /// something proportional: art that is simply too big for the storey has to keep being
        /// refused, or the generator would squash a two-storey wall into one storey and call it a
        /// fit.
        /// </remarks>
        [Test]
        public void ArtTallerThanTheToleranceIsStillRefused()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;
            float tooTall = headroom + BuildingGenerator.HeadroomTolerance + 0.01f;

            BuildingDoc doc = Generate(1UL, AwkwardFloorHeight, ShellCatalog(AwkwardSlab, tooTall));

            Assert.That(WallPieces(doc), Is.Empty,
                "a wall well over the headroom was fitted rather than refused");
        }

        [Test]
        public void AWallExactlyAtTheToleranceIsAdmitted()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;

            BuildingDoc doc = Generate(
                1UL, AwkwardFloorHeight,
                ShellCatalog(AwkwardSlab, headroom + BuildingGenerator.HeadroomTolerance));

            Assert.That(WallPieces(doc), Is.Not.Empty);
        }

        /// <remarks>
        /// <para>
        /// The bug the whole tolerance was a partial answer to, met head on: the package ships wall,
        /// doorway and window art 2.9 m tall and a starter floor tile 0.2 m thick, so the storey
        /// pitch it also ships has to clear 3.1 m. It used to be a round 3, and a round 3 refuses
        /// every one of those three pieces — which does not produce a building with the wrong walls,
        /// it produces one with no envelope at all: bare slabs, floating contents, and nothing in
        /// the document to say why.
        /// </para>
        /// <para>
        /// Written against <c>new BuildingParams()</c> rather than against
        /// <see cref="TestWorlds.SampleBuildingParams"/>, because the default is the thing on trial.
        /// </para>
        /// </remarks>
        [Test]
        public void TheDefaultStoreyPitchClearsTheArtThePackageShips()
        {
            Catalog catalog = ShellCatalog(ShippedSlab, ShippedWall);

            for (ulong seed = 1; seed <= 8; seed++)
            {
                var parameters = new BuildingParams { Seed = seed };

                Assert.That(parameters.FloorHeight - ShippedSlab,
                    Is.GreaterThanOrEqualTo(ShippedWall - BuildingGenerator.HeadroomTolerance),
                    "the default storey pitch does not leave room for the wall art in the box");

                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

                Assert.That(WallPieces(doc), Is.Not.Empty,
                    $"seed {seed}: a building at the default floor height has no walls");
                Assert.That(
                    doc.Metadata[BuildingGenerator.RoomCountKey],
                    Is.Not.EqualTo(parameters.FloorCount.ToString(CultureInfo.InvariantCulture)),
                    $"seed {seed}: every storey came back as one undivided room");
            }
        }

        /// <remarks>
        /// The other half of the same claim, and the one a person would notice: a run that has art
        /// for every module puts something in every module. A hole in an outside wall is a hole in
        /// the envelope whatever the reason for it, so the count is checked against the plan rather
        /// than against itself.
        /// </remarks>
        [Test]
        public void EveryModuleOfEveryRunIsFilledAtTheDefaultPitch()
        {
            Catalog catalog = ShellCatalog(ShippedSlab, ShippedWall);

            for (ulong seed = 1; seed <= 8; seed++)
            {
                var parameters = new BuildingParams { Seed = seed };
                BuildingDoc doc = BuildingGenerator.Generate(parameters, catalog);

                for (int floor = 0; floor < parameters.FloorCount; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(parameters, catalog, doc.Floors[floor], floor);
                    var modules = 0;
                    for (int i = 0; i < plan.Walls.Count; i++)
                    {
                        modules += plan.Walls[i].Modules;
                    }

                    Assert.That(modules, Is.GreaterThan(0),
                        $"seed {seed}, floor {floor}: the storey was never given a shell to fill");
                    Assert.That(PiecesOnFloor(doc, floor), Is.EqualTo(modules),
                        $"seed {seed}, floor {floor}: the shell is short of the plan's modules");
                }
            }
        }

        // --- the scaling math -------------------------------------------------------------------

        /// <remarks>
        /// The property the whole feature is for, swept over seeds and over three art packs that
        /// disagree with the storey in different directions: one that fits it exactly, one whose
        /// wall is half a metre short, and one the tolerance had to let through. Every wall in every
        /// one of them stands on its own floor and reaches the underside of the slab above.
        /// </remarks>
        [Test]
        public void EveryWallStandsOnItsFloorAndReachesTheSlabAboveIt()
        {
            var packs = new[]
            {
                ShellCatalog(AwkwardSlab, AwkwardFloorHeight - AwkwardSlab),
                ShellCatalog(AwkwardSlab, AwkwardFloorHeight - AwkwardSlab - 0.5f),
                ShellCatalog(AwkwardSlab, AwkwardWall),
            };

            float headroom = AwkwardFloorHeight - AwkwardSlab;
            var wrong = new List<string>();

            for (int p = 0; p < packs.Length; p++)
            {
                for (ulong seed = 1; seed <= Seeds; seed++)
                {
                    BuildingDoc doc = Generate(seed, AwkwardFloorHeight, packs[p]);
                    List<PlacedObject> walls = WallPieces(doc);

                    Assert.That(walls, Is.Not.Empty, $"pack {p} seed {seed}: no walls at all");

                    for (int i = 0; i < walls.Count; i++)
                    {
                        CatalogEntry entry = packs[p].Find(walls[i].LogicalId);
                        float stretch = walls[i].Pose.VerticalScale;
                        float floor = Elevation(walls[i]);

                        float bottom = walls[i].Pose.Position.Y - stretch * entry.BaseOffset;
                        float top = walls[i].Pose.Position.Y + stretch * entry.Height;

                        if (MathF.Abs(bottom - floor) > 1e-4f)
                        {
                            wrong.Add(
                                $"pack {p} seed {seed}: {walls[i].StableId} stands at {bottom}, " +
                                $"where its floor is {floor}");
                        }

                        if (MathF.Abs(top - (floor + headroom)) > 1e-4f)
                        {
                            wrong.Add(
                                $"pack {p} seed {seed}: {walls[i].StableId} reaches {top}, " +
                                $"where the ceiling is {floor + headroom}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        /// <remarks>
        /// The two directions named separately, because a factor that was clamped at one would pass
        /// the property above on every catalog whose wall is too tall and fail silently on every
        /// catalog whose wall is too short — which is the common case, since art packs round down.
        /// </remarks>
        [Test]
        public void AShortWallIsStretchedAndAnOversizeOneIsSquashed()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;

            float stretched = FirstWallStretch(ShellCatalog(AwkwardSlab, headroom - 0.5f));
            float squashed = FirstWallStretch(
                ShellCatalog(AwkwardSlab, headroom + BuildingGenerator.HeadroomTolerance));

            Assert.That(stretched, Is.EqualTo(headroom / (headroom - 0.5f)).Within(1e-5f));
            Assert.That(stretched, Is.GreaterThan(1f));

            Assert.That(squashed, Is.LessThan(1f));
            Assert.That(squashed, Is.GreaterThan(0.9f), "the tolerance is not a licence to crush art");
        }

        /// <remarks>
        /// Art whose pivot is not on its base is what makes the lift and the stretch one decision
        /// rather than two. Scale a piece modelled around its own centre and it reaches that much
        /// further below its pivot too, so a pivot left at the unscaled base offset sinks a squashed
        /// wall into the floor — by half the difference, over the whole building.
        /// </remarks>
        [Test]
        public void AWallModelledAroundItsCentreStillStandsOnItsFloor()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;
            float standing = headroom - 0.4f;

            Catalog catalog = ShellCatalog(AwkwardSlab, standing, standing * 0.5f);
            BuildingDoc doc = Generate(2UL, AwkwardFloorHeight, catalog);

            List<PlacedObject> walls = WallPieces(doc);
            Assert.That(walls, Is.Not.Empty);

            for (int i = 0; i < walls.Count; i++)
            {
                CatalogEntry entry = catalog.Find(walls[i].LogicalId);
                Assert.That(entry.BaseOffset, Is.GreaterThan(0f), "the fixture lost its base offset");

                float stretch = walls[i].Pose.VerticalScale;
                float bottom = walls[i].Pose.Position.Y - stretch * entry.BaseOffset;

                Assert.That(bottom, Is.EqualTo(Elevation(walls[i])).Within(1e-4f),
                    $"{walls[i].StableId} is not standing on its own floor");
            }
        }

        /// <remarks>
        /// Doorways go with the walls because the two stand in one run. A frame left at its own
        /// height in a wall fitted to another height is a step in the top of the wall, which is more
        /// obvious than the gap the fitting was for.
        /// </remarks>
        [Test]
        public void ADoorwayIsFittedToTheSameCeilingAsTheWallAroundIt()
        {
            float headroom = AwkwardFloorHeight - AwkwardSlab;
            Catalog catalog = ShellCatalog(AwkwardSlab, headroom - 0.5f);

            BuildingDoc doc = Generate(3UL, AwkwardFloorHeight, catalog);
            List<PlacedObject> doorways = Matching(doc, "/doorway_");

            Assert.That(doorways, Is.Not.Empty, "the building has no doorways to check");

            for (int i = 0; i < doorways.Count; i++)
            {
                CatalogEntry entry = catalog.Find(doorways[i].LogicalId);
                float top = doorways[i].Pose.Position.Y +
                            doorways[i].Pose.VerticalScale * entry.Height;

                Assert.That(top, Is.EqualTo(Elevation(doorways[i]) + headroom).Within(1e-4f),
                    doorways[i].StableId);
            }
        }

        /// <remarks>
        /// The bound on the whole feature. Rescaling art is a defect everywhere else in this tool —
        /// ARCHITECTURE.md section 3 — and the wall run is the exception because its height comes
        /// from a parameter rather than from the catalog. A crate is not fitted to anything, and a
        /// crate stretched to the ceiling would be a very obvious bug.
        /// </remarks>
        [Test]
        public void NothingButTheWallRunsIsEverRescaled()
        {
            Catalog catalog = ShellCatalog(AwkwardSlab, AwkwardFloorHeight - AwkwardSlab - 0.5f);
            var rescaled = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed, AwkwardFloorHeight, catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (placed.StableId.Contains(WallSegment))
                    {
                        continue;
                    }

                    if (placed.Pose.VerticalScale != 1f || placed.Pose.Scale != 1f)
                    {
                        rescaled.Add(
                            $"seed {seed}: {placed.StableId} is scaled " +
                            $"{placed.Pose.Scale} x {placed.Pose.VerticalScale}");
                    }
                }
            }

            Assert.That(rescaled, Is.Empty);
        }

        /// <remarks>
        /// A catalog whose wall fits its storey exactly must come out untouched, not stretched by a
        /// factor that happens to round to one: every building anyone has already generated is one
        /// of those, and a document that changed under them would orphan every override in it.
        /// </remarks>
        [Test]
        public void AWallThatAlreadyFitsIsLeftExactlyAlone()
        {
            BuildingDoc doc = BuildingGenerator.Generate(
                TestWorlds.SampleBuildingParams(), TestWorlds.StructuralCatalog());

            List<PlacedObject> walls = WallPieces(doc);
            Assert.That(walls, Is.Not.Empty);

            for (int i = 0; i < walls.Count; i++)
            {
                Assert.That(walls[i].Pose.VerticalScale, Is.EqualTo(1f), walls[i].StableId);
            }
        }

        // --- the stretch as a document fact -----------------------------------------------------

        [Test]
        public void TheStretchSurvivesASaveAndLoad()
        {
            Catalog catalog = ShellCatalog(AwkwardSlab, AwkwardFloorHeight - AwkwardSlab - 0.5f);
            BuildingDoc doc = Generate(7UL, AwkwardFloorHeight, catalog);

            BuildingDoc reloaded = ArenaJson.DeserializeBuilding(ArenaJson.SerializeBuilding(doc));

            List<PlacedObject> before = WallPieces(doc);
            List<PlacedObject> after = WallPieces(reloaded);

            Assert.That(after.Count, Is.EqualTo(before.Count));
            for (int i = 0; i < before.Count; i++)
            {
                Assert.That(after[i].Pose, Is.EqualTo(before[i].Pose), before[i].StableId);
            }

            Assert.That(before[0].Pose.VerticalScale, Is.Not.EqualTo(1f),
                "the fixture stopped stretching anything, so the round trip proves nothing");
        }

        /// <remarks>
        /// The field is suppressed when it is one, which is what keeps every map document — and
        /// every building whose art already fitted — byte-identical to what this build's
        /// predecessor wrote. A new field on every pose in every file would have been a schema
        /// change dressed up as a bug fix.
        /// </remarks>
        [Test]
        public void AnUnstretchedPoseWritesNoVerticalScaleAtAll()
        {
            string map = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

            Assert.That(map, Does.Not.Contain("verticalScale"));
        }

        [Test]
        public void APoseWithNoVerticalScaleInItReadsBackUnstretched()
        {
            var doc = new WorldDoc();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/test/plain", "cover/low/crate_wood_01", Pose.Identity, null, null));

            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(doc));

            Assert.That(restored.GeneratedObjects[0].Pose.VerticalScale, Is.EqualTo(1f));
        }

        // --- helpers ------------------------------------------------------------------------------

        static BuildingParams Params(ulong seed, float floorHeight)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            parameters.FloorHeight = floorHeight;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed, float floorHeight, Catalog catalog) =>
            BuildingGenerator.Generate(Params(seed, floorHeight), catalog);

        /// <summary>
        /// <see cref="TestWorlds.StructuralCatalog"/> with its slab and its wall art resized.
        /// </summary>
        /// <remarks>
        /// The doorway is resized with the wall, because an art pack that ships a 2.9 m wall ships
        /// a 2.9 m door frame — and the two disagreeing is a different bug from the one this suite
        /// is about.
        /// </remarks>
        /// <param name="slab">How thick the floor tile is, in metres.</param>
        /// <param name="wallStanding">How tall a wall stands from the surface it rests on.</param>
        /// <param name="wallBaseOffset">How far the wall art reaches below its own pivot.</param>
        static Catalog ShellCatalog(float slab, float wallStanding, float wallBaseOffset = 0f)
        {
            IReadOnlyList<CatalogEntry> structural = TestWorlds.StructuralCatalog().Entries;
            var entries = new List<CatalogEntry>(structural.Count);

            for (int i = 0; i < structural.Count; i++)
            {
                CatalogEntry entry = structural[i];

                if (entry.HasTag(BuildingGenerator.FloorTileTag))
                {
                    entries.Add(new CatalogEntry(
                        entry.LogicalId, Tags(entry), entry.Footprint, slab, entry.Weight, null));
                    continue;
                }

                if (entry.HasTag(BuildingGenerator.WallTag) || entry.HasTag(BuildingGenerator.DoorwayTag))
                {
                    entries.Add(new CatalogEntry(
                        entry.LogicalId,
                        Tags(entry),
                        entry.Footprint,
                        wallStanding - wallBaseOffset,
                        entry.Weight,
                        null,
                        wallBaseOffset));
                    continue;
                }

                entries.Add(entry);
            }

            return new Catalog(entries.ToArray());
        }

        static string[] Tags(CatalogEntry entry)
        {
            var tags = new string[entry.Tags.Count];
            for (int i = 0; i < entry.Tags.Count; i++)
            {
                tags[i] = entry.Tags[i];
            }

            return tags;
        }

        /// <summary>The stretch on the first wall of a building made from this art.</summary>
        static float FirstWallStretch(Catalog catalog)
        {
            List<PlacedObject> walls = WallPieces(Generate(1UL, AwkwardFloorHeight, catalog));
            Assert.That(walls, Is.Not.Empty, "this catalog built no walls");
            return walls[0].Pose.VerticalScale;
        }

        /// <summary>Where the storey an object stands on has its walking surface.</summary>
        /// <remarks>
        /// Read from the object's own metadata rather than recomputed from the floor index, so the
        /// test is not agreeing with the generator by repeating its arithmetic.
        /// </remarks>
        static float Elevation(PlacedObject placed) => float.Parse(
            placed.Metadata[BuildingGenerator.ElevationKey],
            NumberStyles.Float,
            CultureInfo.InvariantCulture);

        static List<PlacedObject> WallPieces(BuildingDoc doc) => Matching(doc, WallSegment);

        /// <summary>How many pieces stand in the wall runs of one storey, doorways and windows and all.</summary>
        static int PiecesOnFloor(BuildingDoc doc, int floor)
        {
            string prefix = BuildingDoc.FloorIdPrefix(floor) + WallSegment;

            var pieces = 0;
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].StableId.StartsWith(prefix, StringComparison.Ordinal))
                {
                    pieces++;
                }
            }

            return pieces;
        }

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
