using System;
using System.Collections.Generic;
using System.Linq;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The density rule: how much of a map is built on, how that scales with the size of the map,
    /// and what happens when the one structure a map demands will not fit anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This suite exists because the failure it guards against does not look like a failure. Every
    /// other property in the project holds of a map with one hut on it and nothing else: the hut is
    /// inside the playfield, it is clear of both spawns, the spawns are connected, the floor is
    /// covered. A generator that quietly gave up on the rest of the map would come out green
    /// everywhere and produce an empty field.
    /// </para>
    /// <para>
    /// <strong>What is asserted is the ground, not a count.</strong> The rule used to be one
    /// building and a house per flank, which is a number this file could restate and compare
    /// against. It is now a division of the playfield into layout cells — see
    /// <see cref="ArenaLayoutGenerator.StructureCells"/> — every one of which is offered a
    /// structure, so what a given seed comes out with depends on what the catalog has and where the
    /// draws land. The claims below are therefore the two halves that <em>are</em> claims: the
    /// cells scale with the map, and every structure that stood up did so legally, with nothing
    /// overfilled and nothing crowded.
    /// </para>
    /// <para>
    /// The one count still asserted is the anchor: a map has a building in its middle lane or it
    /// throws saying it could not place one.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Categorised <c>Slow</c>: the four-hundred-metre cases build a town of three dozen buildings
    /// apiece. CI runs them on main and skips them on a pull request — see CONTRIBUTING.md.
    /// </remarks>
    [Category("Slow")]
    public sealed class MapCompositionTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every map.</summary>
        const int Seeds = 120;

        /// <summary>Slack for a measurement that should be exact, in metres.</summary>
        const float Tolerance = 1e-3f;

        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static IReadOnlyList<PlacedObject> Tagged(WorldDoc doc, string tag) =>
            doc.GeneratedObjects.Where(o => o.Tags.Contains(tag)).ToList();

        static PlacedObject InLane(WorldDoc doc, string lane) =>
            Tagged(doc, ArenaLayoutGenerator.StructureTag).SingleOrDefault(
                o => o.Metadata[ArenaLayoutGenerator.LaneKey] == lane);

        // --- the rule itself ----------------------------------------------------------------------

        /// <remarks>
        /// The headline claim, and the one a designer would state first: the default map is built
        /// on in all three of its lanes, on every seed rather than on most. A sixty-metre playfield
        /// divides into exactly one cell per lane, so "every cell filled" and "every lane anchored"
        /// are the same sentence about that map and only about that map.
        /// </remarks>
        [Test]
        public void ASixtyMetreMapAnchorsEveryLane()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                Assert.That(ArenaLayoutGenerator.StructureCells(Params(seed)), Is.EqualTo(3),
                    $"seed {seed}: a sixty-metre map is three cells");
                Assert.That(Tagged(doc, ArenaLayoutGenerator.StructureTag).Count, Is.EqualTo(3),
                    $"seed {seed}: the map left a cell of its own ground empty");
            }
        }

        /// <remarks>
        /// The anchor is a demand where every other cell is an offer, so this is asserted as an
        /// identity rather than as a count: whatever else the map came out with, the thing standing
        /// in the middle lane is a building.
        /// </remarks>
        [Test]
        public void TheBuildingAnchorsTheMiddleLane()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                PlacedObject anchor = InLane(doc, "lane_mid");

                Assert.That(anchor, Is.Not.Null, $"seed {seed}: nothing anchors the middle lane");
                Assert.That(anchor.Tags, Does.Contain(ArenaLayoutGenerator.BuildingTag), $"seed {seed}");
            }
        }

        /// <remarks>
        /// <para>
        /// The density rule stated as the thing it is: a function of the parameters alone. A cell
        /// is about <see cref="ArenaLayoutGenerator.MetresPerStructure"/> of ground, cut out of the
        /// run between the two spawns and across the lane bands, so the count follows the playfield
        /// and not the catalog.
        /// </para>
        /// <para>
        /// The small maps are exact and the large one is a range, and the difference is the lane
        /// width jitter in <c>ArenaLayout</c>: a band narrower than a cell rounds to one row
        /// whatever the jitter did, and a band two and a half cells wide rounds to two or three
        /// depending on it. Asserting an exact figure there would be asserting the jitter.
        /// </para>
        /// </remarks>
        [Test]
        public void CellsAreCountedByTheGroundBetweenTheSpawns()
        {
            var cases = new (Vec2 Size, int Cells, string Why)[]
            {
                (new Vec2(40f, 40f), 3, "a band and a run both shorter than a cell: one per lane"),
                (new Vec2(60f, 60f), 3, "the default map, and the composition it has always had"),
                (new Vec2(100f, 100f), 3, "wider bands, but still one cell each"),
                (new Vec2(150f, 150f), 6, "the run is two cells long now"),
                (new Vec2(200f, 200f), 9, "three cells long, one wide"),
            };

            foreach ((Vec2 size, int cells, string why) in cases)
            {
                var parameters = new ArenaParams { Seed = 1, PlayfieldSize = size };

                Assert.That(ArenaLayoutGenerator.StructureCells(parameters), Is.EqualTo(cells),
                    $"{size.X} x {size.Y}: {why}");
            }

            Assert.That(
                ArenaLayoutGenerator.StructureCells(
                    new ArenaParams { Seed = 1, PlayfieldSize = new Vec2(400f, 400f) }),
                Is.InRange(36, 54),
                "a four-hundred-metre map is six cells long and two or three wide in every lane");
        }

        /// <remarks>
        /// The rule and the generator held against each other, over every shape of map the tool is
        /// used at. A cell is the most a piece of ground may hold and the anchor is the least a map
        /// may hold, and both bounds are checked on the document rather than on the rule.
        /// </remarks>
        [Test]
        public void EveryShapeOfMapFillsWhatItsGroundPaysForAndNoMore()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            var shapes = new (Vec2 Size, int Lanes)[]
            {
                (new Vec2(60f, 60f), 3),
                (new Vec2(80f, 80f), 3),
                (new Vec2(100f, 100f), 3),
                (new Vec2(60f, 60f), 5),
                (new Vec2(40f, 80f), 3),
                (new Vec2(80f, 40f), 3),
                (new Vec2(60f, 60f), 1),
            };

            foreach ((Vec2 size, int lanes) in shapes)
            {
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var parameters = new ArenaParams { Seed = seed, PlayfieldSize = size, LaneCount = lanes };
                    WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                    string where = $"{size.X} x {size.Y}, {lanes} lanes, seed {seed}";

                    Assert.That(Tagged(doc, ArenaLayoutGenerator.StructureTag).Count,
                        Is.InRange(1, ArenaLayoutGenerator.StructureCells(parameters)), where);
                    Assert.That(InLane(doc, "lane_mid"), Is.Not.Null, where + ": no anchor");
                }
            }
        }

        // --- density at scale ---------------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// The claim the whole change was made for. A four-hundred-metre map is a hundred and
        /// eleven times the ground of the default one, and it used to come out with the same three
        /// structures on it — one building and a house per flank, capped at the two flanks, however
        /// much grass there was between them. It now comes out a town.
        /// </para>
        /// <para>
        /// Density is worth nothing on its own, so the spacing is asserted in the same sweep: every
        /// pair of structures keeps <see cref="ArenaLayoutGenerator.StructureClearance"/> between
        /// them, which is two yards' worth, so no two of three dozen buildings share ground or
        /// thread their garden fences through each other.
        /// </para>
        /// </remarks>
        [Test]
        public void AFourHundredMetreMapComesOutATownWithItsSpacingIntact()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 6; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, PlayfieldSize = new Vec2(400f, 400f) };
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                IReadOnlyList<PlacedObject> structures = Tagged(doc, ArenaLayoutGenerator.StructureTag);

                Assert.That(structures.Count, Is.GreaterThanOrEqualTo(24),
                    $"seed {seed}: dozens of structures, not the three a sixty-metre map holds");
                Assert.That(structures.Count,
                    Is.LessThanOrEqualTo(ArenaLayoutGenerator.StructureCells(parameters)),
                    $"seed {seed}: more structures than the ground was divided into");

                var footprints = new List<Rect2>(structures.Count);
                for (int i = 0; i < structures.Count; i++)
                {
                    footprints.Add(PlacedGeometry.WorldFootprint(structures[i], catalog));
                }

                for (int i = 0; i < footprints.Count; i++)
                {
                    for (int j = i + 1; j < footprints.Count; j++)
                    {
                        Assert.That(Rect2.Distance(footprints[i], footprints[j]),
                            Is.GreaterThanOrEqualTo(ArenaLayoutGenerator.StructureClearance - Tolerance),
                            $"seed {seed}: {structures[i].StableId} crowds {structures[j].StableId}");
                    }
                }
            }
        }

        /// <remarks>
        /// The same claim about the two size classes. A cell only ever offers art it can afford, so
        /// a map with three dozen cells of a good size draws both the two-storey building and the
        /// small house out of one list — which is what replaced the hard-coded "one large, two
        /// small". Swept rather than asserted per seed: which class a given cell drew is a draw, and
        /// what is being claimed is that both are on the table.
        /// </remarks>
        [Test]
        public void ALargeMapDrawsBothSizeClassesOutOfOneList()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var buildings = 0;
            var houses = 0;

            for (ulong seed = 1; seed <= 6; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, PlayfieldSize = new Vec2(400f, 400f) }, catalog);

                buildings += Tagged(doc, ArenaLayoutGenerator.BuildingTag).Count;
                houses += Tagged(doc, ArenaLayoutGenerator.HouseTag).Count;
            }

            Assert.That(buildings, Is.GreaterThan(6), "more buildings than the one anchor per map");
            Assert.That(houses, Is.GreaterThan(0), "and houses among them");
        }

        // --- the fallbacks ------------------------------------------------------------------------

        /// <remarks>
        /// The narrow window on the centre of the map is where the anchor belongs, not where it has
        /// to be. A building too long for that window is stood somewhere else in the middle lane
        /// rather than refused — which is the difference between "a map has an anchor" being a rule
        /// and being a hope.
        /// </remarks>
        [Test]
        public void ABuildingTooLongForTheCentreWindowStillAnchorsTheMiddle()
        {
            Catalog catalog = OversizedCatalog(building: 20f, house: 6f);

            for (ulong seed = 1; seed <= 40; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                PlacedObject anchor = InLane(doc, "lane_mid");

                Assert.That(anchor, Is.Not.Null, $"seed {seed}: the building was dropped");
                Assert.That(anchor.Tags, Does.Contain(ArenaLayoutGenerator.BuildingTag),
                    $"seed {seed}: the building left the middle lane it fits in");
            }
        }

        /// <remarks>
        /// <para>
        /// The offer half of the rule, and the half that used to be a demand. A structure wider
        /// than its flank band can hold inside the playfield leaves that ground empty; it is not
        /// pushed into another lane, because another lane is another cell's ground and a cell that
        /// took two structures would be twice the density the rule asked for.
        /// </para>
        /// <para>
        /// Forty by eighty rather than square, so the map is narrow enough for the flanks to refuse
        /// a sixteen-metre house and long enough for the middle lane to hold the building. What is
        /// asserted is that the map still has its anchor and has not quietly grown a house in the
        /// middle lane beside it.
        /// </para>
        /// </remarks>
        [Test]
        public void AStructureThatWillNotFitItsCellLeavesTheGroundEmpty()
        {
            Catalog catalog = OversizedCatalog(building: 4f, house: 16f);

            for (ulong seed = 1; seed <= 40; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, PlayfieldSize = new Vec2(40f, 80f) };
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);

                Assert.That(Tagged(doc, ArenaLayoutGenerator.HouseTag), Is.Empty,
                    $"seed {seed}: a house too wide for any cell on this map stood up anyway");
                Assert.That(InLane(doc, "lane_mid"), Is.Not.Null, $"seed {seed}: no anchor");
            }
        }

        /// <remarks>
        /// The other half of the bargain. When no lane will hold the anchor, the generation fails
        /// and says which tag and how much ground it was asked to fit it on — because the
        /// alternative is a document that looks generated and is an empty field.
        /// </remarks>
        [Test]
        public void AMapTooSmallForItsCatalogSaysSoRatherThanComingOutEmpty()
        {
            Catalog catalog = OversizedCatalog(building: 50f, house: 6f);

            var error = Assert.Throws<InvalidOperationException>(
                () => ArenaLayoutGenerator.Generate(Params(1UL), catalog));

            Assert.That(error.Message, Does.Contain(ArenaLayoutGenerator.BuildingTag));
            Assert.That(error.Message, Does.Contain("60 by 60"), "the message names the playfield");
            Assert.That(error.Message, Does.Contain("3600"), "and the ground it works out at");
        }

        [Test]
        public void StructureCellsRejectsANullParameterSet()
        {
            Assert.Throws<ArgumentNullException>(() => ArenaLayoutGenerator.StructureCells(null));
        }

        /// <summary>
        /// A catalog whose structures are square and as large as the caller says, so a test can put
        /// one somewhere it does not fit and watch what the map does about it.
        /// </summary>
        static Catalog OversizedCatalog(float building, float house) => new Catalog(new[]
        {
            new CatalogEntry(
                "marker/spawn", new[] { "marker", "spawn" }, new Rect2(-1f, -1f, 1f, 1f), 0.5f, 1f, null),
            new CatalogEntry(
                "structure/building/block_01",
                new[] { ArenaLayoutGenerator.StructureTag, ArenaLayoutGenerator.BuildingTag },
                Square(building), 6f, 1f, null),
            new CatalogEntry(
                "structure/house/block_01",
                new[] { ArenaLayoutGenerator.StructureTag, ArenaLayoutGenerator.HouseTag },
                Square(house), 3.5f, 1f, null),
        });

        static Rect2 Square(float side) =>
            new Rect2(-side * 0.5f, -side * 0.5f, side * 0.5f, side * 0.5f);
    }
}
