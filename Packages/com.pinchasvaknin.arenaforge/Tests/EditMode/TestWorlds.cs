using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

// UnityEngine is referenced by its full name rather than imported: it also declares a Pose type,
// and an ambiguous reference here would be a confusing way to learn that.

namespace ArenaForge.Tests
{
    /// <summary>
    /// The sample catalog and world used across the suites, built in code so it can be compared
    /// against the committed fixtures under Tests/Fixtures.
    /// </summary>
    static class TestWorlds
    {
        const string FixtureFolder = "Packages/com.pinchasvaknin.arenaforge/Tests/Fixtures";

        /// <remarks>
        /// Through the asset database rather than the file system, because a package installed from
        /// a git URL lives in <c>Library/PackageCache</c> under a hashed folder name and there is no
        /// <c>Packages/…</c> path on disk to open. Unity resolves the logical path either way.
        /// </remarks>
        public static string ReadFixture(string fileName)
        {
            string path = FixtureFolder + "/" + fileName;
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Missing fixture: {path}");
            return asset.text;
        }

        /// <summary>
        /// The sample catalog, deliberately constructed out of logical-id order so the tests can
        /// show that the catalog, not the caller, decides enumeration order.
        /// </summary>
        public static Catalog SampleCatalog() => new Catalog(new[]
        {
            new CatalogEntry(
                "structure/house/small_01",
                new[] { "structure", "structure/house" },
                new Rect2(-4f, -3f, 4f, 3f),
                3.5f,
                1f,
                null),
            new CatalogEntry(
                "cover/low/crate_wood_01",
                new[] { "cover", "cover/low", "wood" },
                new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
                1f,
                3f,
                new[]
                {
                    new CatalogSocket("top", new[] { "prop_surface" }, Pose.At(new Vec3(0f, 1f, 0f))),
                }),
            new CatalogEntry(
                "marker/spawn",
                new[] { "marker", "spawn" },
                new Rect2(-1f, -1f, 1f, 1f),
                0.5f,
                1f,
                null),
            new CatalogEntry(
                "cover/high/barrier_concrete_01",
                new[] { "cover", "cover/high", "concrete" },
                new Rect2(-1f, -0.25f, 1f, 0.25f),
                1.8f,
                1f,
                null),
            new CatalogEntry(
                "structure/building/two_storey_01",
                new[] { "structure", "structure/building" },
                new Rect2(-6f, -5f, 6f, 5f),
                6f,
                1f,
                null),
            new CatalogEntry(
                "cover/low/sandbags_01",
                new[] { "cover", "cover/low", "fabric" },
                new Rect2(-1f, -0.5f, 1f, 0.5f),
                0.9f,
                2f,
                null),
        });

        /// <summary>
        /// <see cref="SampleCatalog"/> plus the clutter a room's floor is scattered with: a metre
        /// cube and a two-by-one oblong.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A catalog of its own rather than two more rows in <see cref="SampleCatalog"/>, for the
        /// reason <see cref="StructuralCatalog"/> is one: the sample is what the committed fixtures
        /// were written from, and adding to it moves every map the other suites measure.
        /// </para>
        /// <para>
        /// The rows carry <see cref="ArenaForge.Core.BuildingGenerator.InteriorCoverTag"/> and
        /// nothing under <c>cover</c>, which is the separation that tag exists for. The sample's
        /// crates, barriers and sandbags are the outdoor arena's furniture; a suite whose rooms
        /// could still be filled out of them would pass whether or not the two queries had been
        /// kept apart. The two pieces are the sizes a room's scatter used to draw, so what changed
        /// is which folder they come from and not how much fits in a room.
        /// </para>
        /// </remarks>
        public static Catalog FurnishableCatalog()
        {
            IReadOnlyList<CatalogEntry> sample = SampleCatalog().Entries;
            var entries = new List<CatalogEntry>(sample.Count + 2);
            for (int i = 0; i < sample.Count; i++)
            {
                entries.Add(sample[i]);
            }

            entries.Add(new CatalogEntry(
                IndoorCrateId,
                new[] { "propbuilding", "propbuilding/decor", BuildingGenerator.InteriorCoverTag },
                new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
                1f,
                3f,
                new[]
                {
                    new CatalogSocket("top", new[] { "prop_surface" }, Pose.At(new Vec3(0f, 1f, 0f))),
                }));
            entries.Add(new CatalogEntry(
                IndoorBoxesId,
                new[] { "propbuilding", "propbuilding/decor", BuildingGenerator.InteriorCoverTag },
                new Rect2(-1f, -0.5f, 1f, 0.5f),
                0.9f,
                2f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Logical id of the cube of interior cover in <see cref="FurnishableCatalog"/>.</summary>
        public const string IndoorCrateId = "propbuilding/decor/interiorcovers/crate_01";

        /// <summary>Logical id of the oblong of interior cover in <see cref="FurnishableCatalog"/>.</summary>
        public const string IndoorBoxesId = "propbuilding/decor/interiorcovers/boxes_01";

        /// <summary>
        /// <see cref="FurnishableCatalog"/> plus the art a building's shell is built from: a
        /// metre-square floor tile, a two-metre wall panel, a doorway frame, a flight of stairs and
        /// a roof rail — and two pieces of interior decor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A separate catalog rather than three more rows in <see cref="SampleCatalog"/>, because
        /// that one is what the committed fixtures were written from and what every other suite
        /// measures against. Adding to it would rewrite <c>catalog.json</c> and move every map the
        /// other suites generate, to test something none of them are about.
        /// </para>
        /// <para>
        /// The floor tile is a metre where the wall module is two, and the mismatch is the point.
        /// A slab is tiled on its own footprint and the walls on theirs — nothing is stretched to
        /// meet the other — so a suite whose art agreed on one size would pass whether or not the
        /// two grids had been kept apart. A metre tile also divides the two-metre module, so the
        /// slab still reaches the walls: what is being tested is that they are separate, not that
        /// they are incompatible.
        /// </para>
        /// <para>
        /// It is the tile size that decides how much floor a stairwell costs, since a slab is laid
        /// in whole tiles and the opening is cut to the ones the flight laps. A metre tile is what
        /// lets a one-by-three-metre flight open exactly one by three metres of floor.
        /// </para>
        /// <para>
        /// The wall is 2.9 m rather than 3 m for the same kind of reason. A storey is its slab
        /// plus what stands on it, so a wall as tall as the floor height would be a wall whose top
        /// is inside the floor above — which the generator refuses, leaving a catalog with wall
        /// art and a building with no walls.
        /// </para>
        /// <para>
        /// The flight of stairs is the exception that proves it: it is exactly the floor height,
        /// because a flight has to <em>reach</em> the storey above rather than stand under it, and
        /// the hole cut in the slab is what it reaches through.
        /// </para>
        /// </remarks>
        public static Catalog StructuralCatalog()
        {
            IReadOnlyList<CatalogEntry> sample = FurnishableCatalog().Entries;
            var entries = new List<CatalogEntry>(sample.Count + 7);
            for (int i = 0; i < sample.Count; i++)
            {
                entries.Add(sample[i]);
            }

            entries.Add(new CatalogEntry(
                "structure/floor/slab_1m",
                new[] { "structure", "structure/floor" },
                new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
                StructuralSlabHeight,
                1f,
                null));
            entries.Add(new CatalogEntry(
                "structure/wall/panel_2m",
                new[] { "structure", "structure/wall" },
                new Rect2(-1f, -0.1f, 1f, 0.1f),
                StructuralWallHeight,
                1f,
                null));
            entries.Add(new CatalogEntry(
                "structure/doorway/frame_2m",
                new[] { "structure", "structure/doorway" },
                new Rect2(-1f, -0.1f, 1f, 0.1f),
                StructuralWallHeight,
                1f,
                null));
            entries.Add(new CatalogEntry(
                StairsId,
                new[] { "structure", "structure/stairs" },
                new Rect2(-1f, -1f, 1f, 1f),
                StructuralStairsHeight,
                1f,
                null));
            entries.Add(new CatalogEntry(
                ParapetId,
                new[] { "structure", "structure/parapet" },
                new Rect2(-1f, -0.1f, 1f, 0.1f),
                StructuralParapetHeight,
                1f,
                null));
            entries.Add(new CatalogEntry(
                "prop/decor/plant_01",
                new[] { "prop", "prop/decor" },
                new Rect2(-0.25f, -0.25f, 0.25f, 0.25f),
                1.2f,
                1f,
                null));
            entries.Add(new CatalogEntry(
                "prop/decor/sofa_01",
                new[] { "prop", "prop/decor" },
                new Rect2(-0.75f, -0.35f, 0.75f, 0.35f),
                0.8f,
                2f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>
        /// <see cref="StructuralCatalog"/> with a long, narrow flight pivoted at the foot of its
        /// run rather than in the middle of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The shape of a real art pack's staircase, and the one the structural catalog's tidy
        /// two-metre square cannot show: a metre wide and three long, so the opening it needs is
        /// three tiles by one rather than a square. On the metre tile both catalogs come out exact,
        /// which is the point of the metre tile; what this one adds is a flight whose two sides are
        /// different, so an opening that mixed up its axes would show.
        /// </para>
        /// <para>
        /// The pivot sits at the foot of the run, as the demo's own stairs do, so this is also what
        /// exercises a footprint that is not centred on its pivot — the measurement that used to
        /// report this flight as five and a half metres long.
        /// </para>
        /// </remarks>
        public static Catalog NarrowStairsCatalog()
        {
            IReadOnlyList<CatalogEntry> structural = StructuralCatalog().Entries;
            var entries = new List<CatalogEntry>(structural.Count);

            for (int i = 0; i < structural.Count; i++)
            {
                CatalogEntry entry = structural[i];
                entries.Add(entry.LogicalId == StairsId
                    ? new CatalogEntry(
                        StairsId,
                        new[] { "structure", "structure/stairs" },
                        new Rect2(-0.5f, 0f, 0.5f, NarrowStairsRun),
                        StructuralStairsHeight,
                        1f,
                        null)
                    : entry);
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>How far the flight in <see cref="NarrowStairsCatalog"/> runs, in metres.</summary>
        public const float NarrowStairsRun = 3f;

        /// <summary>
        /// <see cref="StructuralCatalog"/> with a window the same size as its wall.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Added rather than swapped in, because a window does not replace a catalog's wall art —
        /// it stands in a run of it, so a catalog needs both. It measures exactly what the wall
        /// measures, which is the contract <see cref="BuildingGenerator.WindowTag"/> states and
        /// what the generated starter window is modelled to.
        /// </para>
        /// <para>
        /// Its own catalog rather than a row on the structural one, so that the pair is what proves
        /// the property worth having: a catalog that gains window art gets windows in the building
        /// it already had, and not a different building.
        /// </para>
        /// </remarks>
        public static Catalog WindowCatalog()
        {
            IReadOnlyList<CatalogEntry> structural = StructuralCatalog().Entries;
            var entries = new List<CatalogEntry>(structural.Count + 1);

            for (int i = 0; i < structural.Count; i++)
            {
                entries.Add(structural[i]);
            }

            entries.Add(new CatalogEntry(
                WindowId,
                new[] { "structure", "structure/window" },
                new Rect2(-1f, -0.1f, 1f, 0.1f),
                StructuralWallHeight,
                1f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Logical id of the window in <see cref="WindowCatalog"/>.</summary>
        public const string WindowId = "structure/window/pane_2m";

        /// <summary>
        /// <see cref="StructuralCatalog"/> with a sofa and a table for the middle of a room.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Its own catalog rather than two more rows on the structural one, and it is the pair that
        /// is the point: a catalog with no <see cref="BuildingGenerator.CentrepieceTag"/> art in it
        /// builds the building it always built, so every suite that measures a structural building
        /// goes on measuring the same one. The same bargain <see cref="WindowCatalog"/> makes.
        /// </para>
        /// <para>
        /// Two pieces rather than one, and one of them is not square. A centrepiece is proposed at a
        /// quarter turn like anything else, and a room whose middle is 1.4 m across one way and 3 m
        /// the other takes the sofa one way round and not the other — so a pass that never turned
        /// its art would come out empty in exactly the rooms this is about.
        /// </para>
        /// <para>
        /// They carry every prefix of the folder path, as a synced row does. A query for
        /// <c>propbuilding/decor/centerpieces</c> has to be the query that finds them and one for
        /// <c>cover</c> has to not, which is the whole reason the workspace files them where it
        /// does.
        /// </para>
        /// </remarks>
        public static Catalog FurnishedCatalog()
        {
            IReadOnlyList<CatalogEntry> structural = StructuralCatalog().Entries;
            var entries = new List<CatalogEntry>(structural.Count + 2);

            for (int i = 0; i < structural.Count; i++)
            {
                entries.Add(structural[i]);
            }

            entries.Add(new CatalogEntry(
                SofaId,
                new[] { "propbuilding", "propbuilding/decor", BuildingGenerator.CentrepieceTag },
                new Rect2(-0.75f, -0.35f, 0.75f, 0.35f),
                0.8f,
                2f,
                null));
            entries.Add(new CatalogEntry(
                TableId,
                new[] { "propbuilding", "propbuilding/decor", BuildingGenerator.CentrepieceTag },
                new Rect2(-0.6f, -0.6f, 0.6f, 0.6f),
                0.75f,
                1f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Logical id of the sofa in <see cref="FurnishedCatalog"/>.</summary>
        public const string SofaId = "propbuilding/decor/centerpieces/sofa_01";

        /// <summary>Logical id of the table in <see cref="FurnishedCatalog"/>.</summary>
        public const string TableId = "propbuilding/decor/centerpieces/table_01";

        /// <summary>
        /// <see cref="SampleCatalog"/> with the three kinds of art that go outside a building: a
        /// hedge and a shrub to run along its walls, a barrel and a crate to heap against them, and
        /// a fence panel for the house's yard.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Off the map catalog rather than the structural one, because this is a map stage: what it
        /// dresses is the two structures the arena generator places, and a building's own storeys
        /// have nothing to do with it.
        /// </para>
        /// <para>
        /// Its own catalog, and the pair is the point once more — a project that has never filled
        /// the exterior folders has to generate the map it always generated, so every other suite
        /// here goes on measuring the map <see cref="SampleCatalog"/> produced before this stage
        /// existed.
        /// </para>
        /// <para>
        /// The two continuous pieces are different lengths on purpose. A run is walked by a cursor
        /// that advances by whatever it drew, so a catalog whose pieces all measured the same would
        /// pass whether or not the walk ever looked at the art — and the shorter piece is what fills
        /// the tail of a run the longer one overshoots.
        /// </para>
        /// <para>
        /// The crate is not square, so a cluster whose pieces were never turned would come out
        /// visibly different from one whose pieces were. They carry every prefix of the folder path,
        /// as a synced row does: a query for <c>propbuilding/decor/outdecor/uniquegroup</c> has to
        /// find them and one for <c>propbuilding/decor/decoration</c> has to not.
        /// </para>
        /// </remarks>
        public static Catalog ExteriorCatalog()
        {
            IReadOnlyList<CatalogEntry> sample = SampleCatalog().Entries;
            var entries = new List<CatalogEntry>(sample.Count + 5);

            for (int i = 0; i < sample.Count; i++)
            {
                entries.Add(sample[i]);
            }

            entries.Add(new CatalogEntry(
                HedgeId,
                OutDecorTags(ExteriorPlacer.ContinuousTag),
                new Rect2(-HedgeLength * 0.5f, -HedgeDepth * 0.5f, HedgeLength * 0.5f, HedgeDepth * 0.5f),
                0.8f,
                2f,
                null));
            entries.Add(new CatalogEntry(
                ShrubId,
                OutDecorTags(ExteriorPlacer.ContinuousTag),
                new Rect2(-ShrubLength * 0.5f, -HedgeDepth * 0.5f, ShrubLength * 0.5f, HedgeDepth * 0.5f),
                0.6f,
                1f,
                null));

            entries.Add(new CatalogEntry(
                BarrelId,
                OutDecorTags(ExteriorPlacer.ClusterTag),
                new Rect2(-0.3f, -0.3f, 0.3f, 0.3f),
                0.9f,
                2f,
                null));
            entries.Add(new CatalogEntry(
                CrateId,
                OutDecorTags(ExteriorPlacer.ClusterTag),
                new Rect2(-0.4f, -0.25f, 0.4f, 0.25f),
                0.6f,
                1f,
                null));

            entries.Add(new CatalogEntry(
                FencePanelId,
                new[] { "fence", ExteriorPlacer.FenceTag },
                new Rect2(
                    -FencePanelLength * 0.5f, -FencePanelThickness * 0.5f,
                    FencePanelLength * 0.5f, FencePanelThickness * 0.5f),
                1.2f,
                1f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Every prefix of an <c>OutDecor</c> tag path, the way a synced row carries them.</summary>
        static string[] OutDecorTags(string leaf) => new[]
        {
            "propbuilding", "propbuilding/decor", "propbuilding/decor/outdecor", leaf,
        };

        /// <summary>Logical id of the longer continuous piece in <see cref="ExteriorCatalog"/>.</summary>
        public const string HedgeId = "propbuilding/decor/outdecor/continuearound/hedge_01";

        /// <summary>Logical id of the shorter continuous piece in <see cref="ExteriorCatalog"/>.</summary>
        public const string ShrubId = "propbuilding/decor/outdecor/continuearound/shrub_01";

        /// <summary>Logical id of the square cluster piece in <see cref="ExteriorCatalog"/>.</summary>
        public const string BarrelId = "propbuilding/decor/outdecor/uniquegroup/barrel_01";

        /// <summary>Logical id of the oblong cluster piece in <see cref="ExteriorCatalog"/>.</summary>
        public const string CrateId = "propbuilding/decor/outdecor/uniquegroup/crate_01";

        /// <summary>Logical id of the fence panel in <see cref="ExteriorCatalog"/>.</summary>
        public const string FencePanelId = "fence/woodfence/panel_01";

        /// <summary>How long the hedge piece in <see cref="ExteriorCatalog"/> is, in metres.</summary>
        public const float HedgeLength = 1f;

        /// <summary>How long the shrub piece in <see cref="ExteriorCatalog"/> is, in metres.</summary>
        public const float ShrubLength = 0.6f;

        /// <summary>How deep both continuous pieces in <see cref="ExteriorCatalog"/> are, in metres.</summary>
        public const float HedgeDepth = 0.4f;

        /// <summary>How long one fence panel in <see cref="ExteriorCatalog"/> is, in metres.</summary>
        public const float FencePanelLength = 2f;

        /// <summary>How thick one fence panel in <see cref="ExteriorCatalog"/> is, in metres.</summary>
        public const float FencePanelThickness = 0.1f;

        /// <summary>
        /// <see cref="ExteriorCatalog"/> with a stone fence panel beside the wooden one, which is
        /// what a map needs before it gets a boundary round the outside of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Its own catalog, and the pair is the point once more: <see cref="ExteriorCatalog"/> has
        /// only the wood, so the exterior suites go on measuring a map whose only fence is the one
        /// round the house's yard, and the boundary suite is the one that adds the stone.
        /// </para>
        /// <para>
        /// The stone panel is longer and thicker than the wooden one on purpose, and that ordering
        /// is what the mixing rule turns on. <see cref="PerimeterFence"/> offers a wood panel
        /// alongside the stone only when it is shorter than every stone one, because a shorter piece
        /// is the only thing that closes the tail of a run — so a catalog whose wood was the longer
        /// of the two would tile the boundary in stone alone and prove nothing about the mixture.
        /// </para>
        /// </remarks>
        public static Catalog BoundaryCatalog()
        {
            IReadOnlyList<CatalogEntry> exterior = ExteriorCatalog().Entries;
            var entries = new List<CatalogEntry>(exterior.Count + 1);

            for (int i = 0; i < exterior.Count; i++)
            {
                entries.Add(exterior[i]);
            }

            entries.Add(new CatalogEntry(
                StonePanelId,
                new[] { "fence", PerimeterFence.StoneFenceTag },
                new Rect2(
                    -StonePanelLength * 0.5f, -StonePanelThickness * 0.5f,
                    StonePanelLength * 0.5f, StonePanelThickness * 0.5f),
                2f,
                1f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Logical id of the stone panel in <see cref="BoundaryCatalog"/>.</summary>
        public const string StonePanelId = "fence/stonefence/panel_01";

        /// <summary>How long one stone panel in <see cref="BoundaryCatalog"/> is, in metres.</summary>
        /// <remarks>Longer than <see cref="FencePanelLength"/>, so the wood is what closes a tail.</remarks>
        public const float StonePanelLength = 2.5f;

        /// <summary>How thick one stone panel in <see cref="BoundaryCatalog"/> is, in metres.</summary>
        /// <remarks>
        /// Thicker than <see cref="FencePanelThickness"/>, so it is the stone that decides how far
        /// the boundary's ring stands in from the edge of the playfield — which is the measurement a
        /// segment seated across a thinner piece of art has to survive.
        /// </remarks>
        public const float StonePanelThickness = 0.3f;

        /// <summary>
        /// <see cref="BoundaryCatalog"/> at the scale of the art pack this tool is actually used
        /// with: one thirty-metre stone panel, and a house that nearly spans a lane band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Both numbers are measurements rather than inventions</strong>, and neither is a
        /// size the sample catalog has anywhere in it. The stone wall in the pack is thirty metres
        /// long, so a sixty-metre edge is tiled two panels at a time where the sample's
        /// two-and-a-half-metre panel takes two dozen — and one panel the rules refuse is half an
        /// edge of the level standing open. The pack's houses are eighteen metres square on a map
        /// whose lane bands are eighteen metres wide, so a house reaches within a metre or two of
        /// the edge of the playfield and its dressing reaches the rest of the way.
        /// </para>
        /// <para>
        /// Together they are what the boundary used to fail on and what no catalog built out of
        /// small tidy art can reproduce: a map came out with three quarters of its fence, or half.
        /// The rest of the catalog is <see cref="BoundaryCatalog"/> unchanged, hedges and yard
        /// fencing included, because the thing standing where a panel wanted to go was usually a
        /// piece of dressing.
        /// </para>
        /// </remarks>
        public static Catalog ArtPackScaleBoundaryCatalog()
        {
            IReadOnlyList<CatalogEntry> boundary = BoundaryCatalog().Entries;
            var entries = new List<CatalogEntry>(boundary.Count);

            for (int i = 0; i < boundary.Count; i++)
            {
                CatalogEntry entry = boundary[i];

                if (entry.LogicalId == StonePanelId)
                {
                    entries.Add(new CatalogEntry(
                        entry.LogicalId, Copy(entry.Tags),
                        new Rect2(
                            -LongStonePanelLength * 0.5f, -StonePanelThickness * 0.5f,
                            LongStonePanelLength * 0.5f, StonePanelThickness * 0.5f),
                        entry.Height, entry.Weight, null));
                }
                else if (entry.HasTag(ArenaForge.Core.ArenaLayoutGenerator.HouseTag))
                {
                    entries.Add(new CatalogEntry(
                        entry.LogicalId, Copy(entry.Tags),
                        new Rect2(
                            -BroadHouseSide * 0.5f, -BroadHouseSide * 0.5f,
                            BroadHouseSide * 0.5f, BroadHouseSide * 0.5f),
                        entry.Height, entry.Weight, null));
                }
                else
                {
                    entries.Add(entry);
                }
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>
        /// <see cref="ArtPackScaleBoundaryCatalog"/> with shorter stone panels filed beside the
        /// long one, the way a pack's fence folder holds a family of lengths.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The catalog the length-variant rule is measured against. Every panel here carries
        /// <see cref="PerimeterFence.StoneFenceTag"/>, so this is one folder's art at several
        /// lengths and not the wood-into-stone mixing that stage rejected — the distinction
        /// <c>WallRun.LongestThatFits</c> turns on.
        /// </para>
        /// <para>
        /// Same weight on every row, so the walk's own draws are as likely to reach for a short
        /// panel as a long one and the improvement measured cannot come from weighting.
        /// </para>
        /// </remarks>
        public static Catalog VariantBoundaryCatalog()
        {
            IReadOnlyList<CatalogEntry> boundary = ArtPackScaleBoundaryCatalog().Entries;
            var entries = new List<CatalogEntry>(boundary.Count + StonePanelVariantLengths.Length);

            for (int i = 0; i < boundary.Count; i++)
            {
                entries.Add(boundary[i]);
            }

            foreach (float length in StonePanelVariantLengths)
            {
                entries.Add(new CatalogEntry(
                    StoneVariantId(length),
                    new[] { "fence", PerimeterFence.StoneFenceTag },
                    new Rect2(
                        -length * 0.5f, -StonePanelThickness * 0.5f,
                        length * 0.5f, StonePanelThickness * 0.5f),
                    2f,
                    1f,
                    null));
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>
        /// <see cref="VariantBoundaryCatalog"/> holding only its shortest stone panel, which is what
        /// the boundary was built of before a run could choose a length.
        /// </summary>
        /// <remarks>
        /// The control the variant catalog is compared against. Only the stone is thinned out: the
        /// hedges, the yard fencing and the houses stay, so the two maps differ in the palette one
        /// stage draws from and in nothing else.
        /// </remarks>
        public static Catalog ShortestOnlyBoundaryCatalog()
        {
            float shortest = StonePanelVariantLengths[StonePanelVariantLengths.Length - 1];
            IReadOnlyList<CatalogEntry> variants = VariantBoundaryCatalog().Entries;
            var entries = new List<CatalogEntry>(variants.Count);

            for (int i = 0; i < variants.Count; i++)
            {
                CatalogEntry entry = variants[i];
                bool stone = entry.HasTag(PerimeterFence.StoneFenceTag);

                if (!stone || entry.LogicalId == StoneVariantId(shortest))
                {
                    entries.Add(entry);
                }
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>Logical id of the stone panel of this length in <see cref="VariantBoundaryCatalog"/>.</summary>
        public static string StoneVariantId(float length) =>
            $"fence/stonefence/panel_{length:0}m";

        /// <summary>
        /// <see cref="ArtPackScaleBoundaryCatalog"/> with a door declared in each structure's two
        /// facing walls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every other catalog in this file declares no doorways at all</strong>, which
        /// means every doorway any suite has ever measured is one the map <em>derived</em> from a
        /// footprint — the middle of the two faces across the lane, which is where a door is when
        /// nobody has said. A row that states its own openings takes a completely different path
        /// through <c>ArenaLayoutGenerator</c>, and until this catalog existed nothing generated a
        /// map down it. The regression it was written for is the shape of that hole: the boundary
        /// fence tiled straight past a building's front door, and the whole suite stayed green
        /// because no structure in it had a front door.
        /// </para>
        /// <para>
        /// The doors are modelled the way the art pack's markers measure: half a metre through the
        /// wall and two metres wide, straddling the wall rather than sitting inside or outside it,
        /// and in the two facing walls so that on some maps a door looks down a lane and on others
        /// it looks straight at the edge of the playfield. That second case is the one that used to
        /// break, and it only happens at a size and a seed where the structure lands near an edge —
        /// which is why the suite sweeps sizes rather than trusting the default map.
        /// </para>
        /// <para>
        /// Exactly two, so neither <c>KeepFurthestApart</c> nor <c>TopUpFromFootprint</c> runs and
        /// what the map places against is what this file wrote down.
        /// </para>
        /// </remarks>
        public static Catalog DoorwayDeclaringCatalog()
        {
            IReadOnlyList<CatalogEntry> art = ArtPackScaleBoundaryCatalog().Entries;
            var entries = new List<CatalogEntry>(art.Count);

            for (int i = 0; i < art.Count; i++)
            {
                CatalogEntry entry = art[i];

                if (!entry.HasTag(ArenaForge.Core.ArenaLayoutGenerator.StructureTag))
                {
                    entries.Add(entry);
                    continue;
                }

                float wall = entry.Footprint.MaxX;
                float half = DeclaredDoorWidth * 0.5f;
                float through = DeclaredDoorDepth * 0.5f;

                entries.Add(new CatalogEntry(
                    entry.LogicalId, Copy(entry.Tags), entry.Footprint, entry.Height, entry.Weight,
                    null, entry.BaseOffset,
                    new[]
                    {
                        new Rect2(wall - through, -half, wall + through, half),
                        new Rect2(-wall - through, -half, -wall + through, half),
                    }));
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>How wide a door in <see cref="DoorwayDeclaringCatalog"/> is, in metres.</summary>
        public const float DeclaredDoorWidth = 2f;

        /// <summary>How far a door in <see cref="DoorwayDeclaringCatalog"/> reaches through its wall.</summary>
        public const float DeclaredDoorDepth = 0.5f;

        /// <summary>How long the stone panel in <see cref="ArtPackScaleBoundaryCatalog"/> is, in metres.</summary>
        public const float LongStonePanelLength = 30f;

        /// <summary>How wide the house in <see cref="ArtPackScaleBoundaryCatalog"/> is, in metres.</summary>
        public const float BroadHouseSide = 12f;

        /// <summary>
        /// The shorter stone panels <see cref="VariantBoundaryCatalog"/> offers beside the long
        /// one, in metres, longest first.
        /// </summary>
        /// <remarks>
        /// Ten, five, two and one against a thirty-metre panel, which is the shape a pack's fence
        /// folder actually has: one piece the wall is mostly built of and a few shorter ones for
        /// the ends. They are not divisors of any edge this suite measures, on purpose — a palette
        /// that happened to divide the map would prove the arithmetic and not the choice.
        /// </remarks>
        public static readonly float[] StonePanelVariantLengths = { 10f, 5f, 2f, 1f };

        /// <summary>
        /// <see cref="BoundaryCatalog"/> with a foundation modelled under both fence panels and a
        /// root under the hedge, so every piece of art on it reaches below its own pivot.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A base offset is how a catalog says the art hangs below the pivot, and a catalog whose
        /// every row is zero cannot tell "planted" from "stood": both come out at the height of the
        /// ground and the distinction that decides whether a fence hovers is invisible. So this
        /// catalog gives the fences a metre of footing and the hedge a fifth of one, and the two
        /// have to come out at different heights on the same ground.
        /// </para>
        /// <para>
        /// A metre is deliberately more than a slope over a panel's length could ever be, because
        /// that is what a foundation is for: it fills the wedge the step down a hillside opens
        /// under the panel, and a panel stood on the ground instead would be a fence hanging a
        /// metre in the air with its footings on show.
        /// </para>
        /// </remarks>
        public static Catalog FootedFenceCatalog()
        {
            IReadOnlyList<CatalogEntry> boundary = BoundaryCatalog().Entries;
            var entries = new List<CatalogEntry>(boundary.Count);

            for (int i = 0; i < boundary.Count; i++)
            {
                CatalogEntry entry = boundary[i];
                float below = entry.LogicalId == HedgeId ? HedgeRoot
                    : entry.LogicalId == FencePanelId || entry.LogicalId == StonePanelId
                        ? FenceFooting
                        : 0f;

                entries.Add(below > 0f
                    ? new CatalogEntry(
                        entry.LogicalId, Copy(entry.Tags), entry.Footprint, entry.Height,
                        entry.Weight, null, below)
                    : entry);
            }

            return new Catalog(entries.ToArray());
        }

        /// <summary>How far the fence panels in <see cref="FootedFenceCatalog"/> reach below their pivots.</summary>
        public const float FenceFooting = 1f;

        /// <summary>How far the hedge in <see cref="FootedFenceCatalog"/> reaches below its pivot.</summary>
        public const float HedgeRoot = 0.2f;

        static string[] Copy(IReadOnlyList<string> tags)
        {
            var copy = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                copy[i] = tags[i];
            }

            return copy;
        }

        /// <summary>
        /// <see cref="SampleCatalog"/> with two lengths of kerbing filed under
        /// <see cref="ArenaForge.Core.RoadKerbs.KerbTag"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A catalog of its own rather than two more rows in <see cref="SampleCatalog"/>, for the
        /// reason the furnishable one is: the sample is what every committed digest and every
        /// threshold sweep was measured on, and adding a row that a road-bearing map would place
        /// moves all of them. Which is also the property this catalog is used to state — that a
        /// catalog <em>without</em> these rows generates exactly the map it always did.
        /// </para>
        /// <para>
        /// Two lengths rather than one, because the run's tail is closed with the shortest piece on
        /// offer and a palette of one cannot tell that apart from the only piece there is. Both are
        /// long and thin, which is what kerbing is and what makes the box round a turned one so
        /// much bigger than the piece — the conservatism <see cref="WallRun.Face.Along"/> has to
        /// exempt a run from.
        /// </para>
        /// </remarks>
        public static Catalog KerbCatalog()
        {
            IReadOnlyList<CatalogEntry> sample = SampleCatalog().Entries;
            var entries = new List<CatalogEntry>(sample.Count + 2);

            for (int i = 0; i < sample.Count; i++)
            {
                entries.Add(sample[i]);
            }

            entries.Add(Kerb(KerbId, KerbLength));
            entries.Add(Kerb(ShortKerbId, ShortKerbLength));

            return new Catalog(entries.ToArray());
        }

        static CatalogEntry Kerb(string logicalId, float length) => new CatalogEntry(
            logicalId,
            new[] { "road", ArenaForge.Core.RoadKerbs.KerbTag },
            new Rect2(
                -length * 0.5f, -KerbThickness * 0.5f, length * 0.5f, KerbThickness * 0.5f),
            KerbHeight,
            1f,
            null);

        /// <summary>Logical id of the longer kerb in <see cref="KerbCatalog"/>.</summary>
        public const string KerbId = "road/kerb/kerb_02m";

        /// <summary>Logical id of the shorter one, which is what closes the tail of a run.</summary>
        public const string ShortKerbId = "road/kerb/kerb_01m";

        /// <summary>How long the longer kerb in <see cref="KerbCatalog"/> is, in metres.</summary>
        public const float KerbLength = 2f;

        /// <summary>How long the shorter one is.</summary>
        public const float ShortKerbLength = 1f;

        /// <summary>How thick either kerb is across its own run, in metres.</summary>
        public const float KerbThickness = 0.3f;

        /// <summary>How tall either kerb stands, in metres.</summary>
        public const float KerbHeight = 0.15f;

        /// <summary>
        /// <see cref="KerbCatalog"/> with three pieces of street furniture beside the kerbing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built on the kerb catalog rather than on the sample, because a verge is the ground
        /// <em>behind</em> the edging: a furniture stage tested on a map with no kerbs on it would
        /// never once have to seat a piece past one, which is the whole of what
        /// <see cref="ArenaForge.Core.RoadFurniture.Verge"/> is measured out from.
        /// </para>
        /// <para>
        /// Three pieces because street furniture is not one shape, and the three differ in the two
        /// ways that reach the placement. The lamp is a post — near enough square that no rotation
        /// changes the box round it, and tall enough to stand across the eye line, which is what
        /// makes it an occluder. The bench is long and low: it lies along the run, its box grows by
        /// half again when the road turns off the axes, and it drops out of the standing-eye
        /// visibility test exactly as low cover does. The shelter is both at once — long
        /// <em>and</em> over eye height — which is the piece that has to fit on a verge and the one
        /// that changes what can be seen across a map.
        /// </para>
        /// <para>
        /// The bench is modelled about its own centre, so it carries a
        /// <see cref="CatalogEntry.BaseOffset"/> and is the row that says whether the lift is being
        /// applied: stood without one it sits half a bench inside the ground.
        /// </para>
        /// </remarks>
        public static Catalog FurnitureCatalog()
        {
            IReadOnlyList<CatalogEntry> kerbed = KerbCatalog().Entries;
            var entries = new List<CatalogEntry>(kerbed.Count + 3);

            for (int i = 0; i < kerbed.Count; i++)
            {
                entries.Add(kerbed[i]);
            }

            entries.Add(Furniture(LampId, 0.3f, 0.3f, LampHeight, 3f, 0f));
            entries.Add(Furniture(BenchId, 1.8f, 0.6f, BenchHeight, 2f, BenchBaseOffset));
            entries.Add(Furniture(ShelterId, ShelterLength, 1.4f, ShelterHeight, 1f, 0f));

            return new Catalog(entries.ToArray());
        }

        static CatalogEntry Furniture(
            string logicalId, float length, float depth, float height, float weight, float baseOffset) =>
            new CatalogEntry(
                logicalId,
                new[] { "road", ArenaForge.Core.RoadFurniture.FurnitureTag },
                new Rect2(-length * 0.5f, -depth * 0.5f, length * 0.5f, depth * 0.5f),
                height,
                weight,
                null,
                baseOffset);

        /// <summary>Logical id of the lamp post in <see cref="FurnitureCatalog"/>.</summary>
        public const string LampId = "road/furniture/lamp_post";

        /// <summary>Logical id of the bench, which is long, low and modelled about its centre.</summary>
        public const string BenchId = "road/furniture/bench";

        /// <summary>Logical id of the shelter, which is long and stands over the eye line.</summary>
        public const string ShelterId = "road/furniture/shelter";

        /// <summary>How tall the lamp post stands above its pivot, in metres.</summary>
        public const float LampHeight = 4f;

        /// <summary>How tall the bench stands above its pivot, in metres.</summary>
        public const float BenchHeight = 0.45f;

        /// <summary>How far the bench hangs below its own pivot, in metres.</summary>
        public const float BenchBaseOffset = 0.45f;

        /// <summary>How long the shelter is along its own run, in metres.</summary>
        public const float ShelterLength = 3f;

        /// <summary>How tall the shelter stands, which is over the analysis eye height.</summary>
        public const float ShelterHeight = 2.4f;

        /// <summary>
        /// <see cref="StructuralCatalog"/> with a two-metre floor tile beside the metre one, and a
        /// flight two metres by three standing on them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What a workspace actually ends up holding: the demo art ships a two-metre slab, the
        /// starter art writes a metre tile, and a sync files both of them under
        /// <c>structure/floor</c>. Every other suite here runs on one floor tile, which is the one
        /// case in which the grid a storey lays its slab on and the grid the stairwell was planned
        /// against cannot disagree — so it is the case that proves nothing about what happens when
        /// they do.
        /// </para>
        /// <para>
        /// The flight is two metres by three because that is the starter staircase: a whole number
        /// of metre tiles on both axes and <em>not</em> a whole number of two-metre ones, so a
        /// storey that draws the coarse tile has to give up four metres of floor to open three.
        /// The metre it gives up is the floor at the top of the stairs, and getting that floored
        /// again is what <c>BuildingGenerator.FloorTheMargin</c> is for.
        /// </para>
        /// <para>
        /// Both tiles are the same thickness, so which one a storey draws changes the grid and
        /// nothing else — a suite about where the floor is should not also be a suite about how
        /// thick it is.
        /// </para>
        /// </remarks>
        public static Catalog CoarseSlabCatalog()
        {
            IReadOnlyList<CatalogEntry> structural = StructuralCatalog().Entries;
            var entries = new List<CatalogEntry>(structural.Count + 1);

            for (int i = 0; i < structural.Count; i++)
            {
                CatalogEntry entry = structural[i];
                entries.Add(entry.LogicalId == StairsId
                    ? new CatalogEntry(
                        StairsId,
                        new[] { "structure", "structure/stairs" },
                        new Rect2(
                            -StarterStairsWidth * 0.5f, 0f, StarterStairsWidth * 0.5f, StarterStairsRun),
                        StructuralStairsHeight,
                        1f,
                        null)
                    : entry);
            }

            entries.Add(new CatalogEntry(
                "structure/floor/slab_2m",
                new[] { "structure", "structure/floor" },
                new Rect2(-CoarseTile * 0.5f, -CoarseTile * 0.5f, CoarseTile * 0.5f, CoarseTile * 0.5f),
                StructuralSlabHeight,
                1f,
                null));

            return new Catalog(entries.ToArray());
        }

        /// <summary>Size of the coarse floor tile in <see cref="CoarseSlabCatalog"/>, in metres. Square.</summary>
        public const float CoarseTile = 2f;

        /// <summary>How wide the flight in <see cref="CoarseSlabCatalog"/> is, in metres.</summary>
        /// <remarks>The starter staircase's own width — see <c>ArenaAssetBuilder</c>.</remarks>
        public const float StarterStairsWidth = 2f;

        /// <summary>How far the flight in <see cref="CoarseSlabCatalog"/> runs, in metres.</summary>
        public const float StarterStairsRun = 3f;

        /// <summary>Logical id of the flight of stairs in <see cref="StructuralCatalog"/>.</summary>
        public const string StairsId = "structure/stairs/flight_2m";

        /// <summary>Logical id of the parapet in <see cref="StructuralCatalog"/>.</summary>
        public const string ParapetId = "structure/parapet/rail_2m";

        /// <summary>Length of one wall module in <see cref="StructuralCatalog"/>, in metres.</summary>
        public const float StructuralModule = 2f;

        /// <summary>Thickness of a wall in <see cref="StructuralCatalog"/>, in metres.</summary>
        public const float StructuralThickness = 0.2f;

        /// <summary>Thickness of a floor slab in <see cref="StructuralCatalog"/>, in metres.</summary>
        public const float StructuralSlabHeight = 0.1f;

        /// <summary>Size of a floor tile in <see cref="StructuralCatalog"/>, in metres. Square.</summary>
        /// <remarks>
        /// A metre, where the wall module is two. It is what a stairwell's opening is counted in,
        /// since a slab is laid in whole tiles, so it is the size that decides how much floor a
        /// flight costs: on a two-metre tile a one-metre flight opens two metres of floor, and the
        /// slack is the thing a person sees. The two sizes disagreeing on purpose is also what
        /// keeps the suite honest about the wall grid and the slab grid being separate.
        /// </remarks>
        public const float StructuralTile = 1f;

        /// <summary>Height of a wall in <see cref="StructuralCatalog"/>, in metres.</summary>
        public const float StructuralWallHeight = 2.9f;

        /// <summary>Height of a flight of stairs in <see cref="StructuralCatalog"/>, in metres.</summary>
        /// <remarks>The floor height exactly: a flight reaches the storey above, it does not fit under it.</remarks>
        public const float StructuralStairsHeight = 3f;

        /// <summary>Height of a parapet in <see cref="StructuralCatalog"/>, in metres.</summary>
        public const float StructuralParapetHeight = 1f;

        /// <summary>Logical ids of <see cref="SampleCatalog"/> in the order the catalog holds them.</summary>
        public static readonly string[] SampleCatalogOrder =
        {
            "cover/high/barrier_concrete_01",
            "cover/low/crate_wood_01",
            "cover/low/sandbags_01",
            "marker/spawn",
            "structure/building/two_storey_01",
            "structure/house/small_01",
        };

        /// <summary>A five-object map carrying one override of each kind.</summary>
        public static WorldDoc SampleWorld()
        {
            var doc = new WorldDoc { Parameters = new ArenaParams { Seed = 20260816UL } };

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_a/marker",
                "marker/spawn",
                Pose.At(new Vec3(0f, 0f, -26f)),
                new[] { "marker", "spawn" },
                new Dictionary<string, string> { { "team", "a" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_b/marker",
                "marker/spawn",
                new Pose(new Vec3(0f, 0f, 26f), HalfTurn, 1f),
                new[] { "marker", "spawn" },
                new Dictionary<string, string> { { "team", "b" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/structure_00",
                "structure/building/two_storey_01",
                Pose.At(Vec3.Zero),
                new[] { "structure", "structure/building" },
                new Dictionary<string, string>
                {
                    { "lane", "lane_mid" },
                    { "doorway_00", "-2,4,2,4.5" },
                }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_north/cover_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(-14.5f, 0f, 8f)),
                new[] { "cover", "cover/low" },
                new Dictionary<string, string> { { "lane", "lane_north" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_south/cover_00",
                "cover/low/sandbags_01",
                new Pose(new Vec3(14.5f, 0f, -8f), HalfTurn, 1f),
                new[] { "cover", "cover/low" },
                new Dictionary<string, string> { { "lane", "lane_south" } }));

            doc.Overrides.Add(EditOverride.Move(
                "map/lane_north/cover_00", Pose.At(new Vec3(-12f, 0f, 9.5f))));
            doc.Overrides.Add(EditOverride.Delete("map/lane_south/cover_00"));
            doc.Overrides.Add(EditOverride.Add(
                "user/crate_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(3.5f, 0f, -4f)),
                new[] { "cover", "cover/low", "user" },
                new Dictionary<string, string> { { "note", "placed by hand" } }));
            doc.Overrides.Add(EditOverride.SwapAsset(
                "map/lane_mid/structure_00", "structure/house/small_01"));

            return doc;
        }

        /// <summary>
        /// A small building, generated rather than written out by hand: unlike the sample world,
        /// what is interesting about a building document is the relationship between its floor
        /// seeds and its objects, and a hand-built one would not have it.
        /// </summary>
        public static BuildingDoc SampleBuilding() =>
            BuildingGenerator.Generate(SampleBuildingParams(), FurnishableCatalog());

        /// <summary>The parameters <see cref="SampleBuilding"/> is built from.</summary>
        public static BuildingParams SampleBuildingParams() => new BuildingParams
        {
            Seed = 20260816UL,
            FootprintSize = new Vec2(12f, 10f),
            FloorCount = 3,
            FloorHeight = 3f,
            GridSize = 0.5f,
            WallMargin = 0.5f,
            ContentDensity = 1f,
        };

        /// <summary>
        /// An exact 180 degree yaw. Written out rather than produced by
        /// <see cref="Quat.FromYawDegrees"/> so the fixture's float text does not depend on the
        /// runtime's trigonometry.
        /// </summary>
        public static Quat HalfTurn => new Quat(0f, 1f, 0f, 0f);
    }
}
