using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The catalog sync: tags read out of folder names, footprint and height read off box colliders
    /// where there are any and off the art itself where there are not.
    /// </summary>
    /// <remarks>
    /// What is worth asserting is that the tags it produces are the tags the generator queries
    /// for. A sync that invented a plausible-looking tag nobody looks up would fill a catalog with
    /// rows no map could ever use, and would look right in the inspector while doing it — so the
    /// tests below name <see cref="BuildingGenerator.WallTag"/> and the rest rather than repeating
    /// the strings.
    /// </remarks>
    public sealed class CatalogSyncTests
    {
        const string Root = "Assets/ArenaForgeSyncTests";

        CatalogAsset _catalog;

        [SetUp]
        public void CreateFolder()
        {
            Delete();
            AssetDatabase.Refresh();
            AssetDatabase.CreateFolder("Assets", "ArenaForgeSyncTests");
            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
        }

        [TearDown]
        public void RemoveFolder()
        {
            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
                _catalog = null;
            }

            Delete();
        }

        static void Delete()
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.DeleteAsset(Root);
            }
        }

        // --- tags come from folders ------------------------------------------------------------

        [Test]
        public void AFolderNamesTheTagsThePrefabsInItGet()
        {
            WritePrefab("Walls", "Panel_2m", new Vector3(2f, 3f, 0.2f));
            WritePrefab("Floors", "Slab_2m", new Vector3(2f, 0.1f, 2f));
            WritePrefab("Covers/Low", "Crate_Wood_01", new Vector3(1f, 1f, 1f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("structure/wall/panel_2m"),
                Is.EqualTo(new[] { ArenaLayoutGenerator.StructureTag, BuildingGenerator.WallTag }));
            Assert.That(TagsOf("structure/floor/slab_2m"),
                Is.EqualTo(new[] { ArenaLayoutGenerator.StructureTag, BuildingGenerator.FloorTileTag }));
            Assert.That(TagsOf("cover/low/crate_wood_01"),
                Is.EqualTo(new[] { CoverPlacer.CoverTag, CoverPlacer.LowCoverTag }));
        }

        /// <remarks>
        /// The point of the restart rule. A project files its art under a vendor folder, an art
        /// pack name or just <c>Props</c>, and none of those should end up as the first segment of
        /// every tag underneath — a crate in <c>Props/Covers/Low</c> is cover, not
        /// <c>prop/cover/low</c>, which nothing queries for.
        /// </remarks>
        [Test]
        public void AnOrganisingFolderAboveAKnownOneDoesNotReachTheTags()
        {
            WritePrefab("KitBash v2/Props/Covers/High", "Barrier", new Vector3(2f, 1.8f, 0.5f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("cover/high/barrier"),
                Is.EqualTo(new[] { CoverPlacer.CoverTag, CoverPlacer.HighCoverTag }));
        }

        /// <remarks>
        /// The layout the semantic folders need. A tag path has to survive four levels intact,
        /// because every level is a different answer to the only question the placement stages ask
        /// — <c>Decoration</c> goes in a room's corners, <c>Covers</c> in its middle,
        /// <c>OutDecor/ContinueAround</c> round the outside without a gap and
        /// <c>OutDecor/UniqueGroup</c> in clusters. A table that flattened any of them into
        /// <c>prop/decor</c> would hand all four to whichever stage asked first.
        /// </remarks>
        [Test]
        public void TheDecorTreeKeepsItsFullDepth()
        {
            WritePrefab("Props/PropBuilding/Decor/Decoration", "Vase", new Vector3(0.3f, 0.5f, 0.3f));
            WritePrefab("Props/PropBuilding/Decor/Centerpieces", "Table", new Vector3(1.6f, 0.8f, 0.9f));
            WritePrefab("Props/PropBuilding/Decor/OutDecor/ContinueAround", "Kerb", new Vector3(1f, 0.2f, 0.4f));
            WritePrefab("Props/PropBuilding/Decor/OutDecor/UniqueGroup", "Bins", new Vector3(1.2f, 1.1f, 0.8f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("propbuilding/decor/decoration/vase"), Is.EqualTo(
                new[] { "propbuilding", "propbuilding/decor", "propbuilding/decor/decoration" }));
            Assert.That(TagsOf("propbuilding/decor/centerpieces/table"), Is.EqualTo(
                new[] { "propbuilding", "propbuilding/decor", "propbuilding/decor/centerpieces" }));
            Assert.That(TagsOf("propbuilding/decor/outdecor/continuearound/kerb"), Is.EqualTo(
                new[]
                {
                    "propbuilding", "propbuilding/decor", "propbuilding/decor/outdecor",
                    "propbuilding/decor/outdecor/continuearound",
                }));
            Assert.That(TagsOf("propbuilding/decor/outdecor/uniquegroup/bins"), Is.EqualTo(
                new[]
                {
                    "propbuilding", "propbuilding/decor", "propbuilding/decor/outdecor",
                    "propbuilding/decor/outdecor/uniquegroup",
                }));
        }

        /// <remarks>
        /// <c>Covers</c> appears twice in the layout and means two different things, so a table
        /// keyed on the folder name alone cannot read both. Inside the decor tree it is furniture
        /// for the middle of a room; at the top of <c>Props</c> it is what
        /// <see cref="CoverPlacer"/> builds the firefight out of.
        /// </remarks>
        [Test]
        public void ACoversFolderInsideTheDecorTreeIsNotTacticalCover()
        {
            WritePrefab("Props/PropBuilding/Decor/Centerpieces", "Table", new Vector3(1.6f, 0.8f, 0.9f));
            WritePrefab("Props/Covers/Low", "Crate", new Vector3(1f, 1f, 1f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("propbuilding/decor/centerpieces/table"),
                Does.Not.Contain(CoverPlacer.CoverTag));
            Assert.That(TagsOf("cover/low/crate"),
                Is.EqualTo(new[] { CoverPlacer.CoverTag, CoverPlacer.LowCoverTag }));
        }

        /// <remarks>
        /// The shell folders moved under <c>PropBuilding</c> and their tags did not move with
        /// them: a wall is <c>structure/wall</c> wherever it is filed, because the name restarts
        /// the tag path. That is what lets the tree be reorganised without the generator's queries
        /// being rewritten alongside it.
        /// </remarks>
        [Test]
        public void TheShellFoldersSpellTheSameTagsUnderPropBuildingAsWithoutIt()
        {
            WritePrefab("Props/PropBuilding/Walls", "Panel", new Vector3(2f, 3f, 0.2f));
            WritePrefab("Props/PropBuilding/Stairs", "Flight", new Vector3(2f, 3f, 3f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("structure/wall/panel"),
                Is.EqualTo(new[] { ArenaLayoutGenerator.StructureTag, BuildingGenerator.WallTag }));
            Assert.That(TagsOf("structure/stairs/flight"),
                Is.EqualTo(new[] { ArenaLayoutGenerator.StructureTag, BuildingGenerator.StairsTag }));
        }

        /// <remarks>
        /// <para>
        /// A project that has not run the relocation still has its decor in <c>Props/Decor</c>, or
        /// in a <c>Decor</c> folder at the top of an art pack, and both still mean what they
        /// meant. The interior decor pass asks for this spelling and the deeper one for exactly
        /// this reason — see <see cref="BuildingGenerator.RoomDecorTag"/>.
        /// </para>
        /// <para>
        /// Both positions, because <c>Decor</c> is the one name in the table that qualifies the
        /// kind above it <em>and</em> is a kind on its own. A rule that only handled the qualifying
        /// case would move every art pack's top-level decor folder onto a tag nothing queries, and
        /// the catalog would look untouched while it happened.
        /// </para>
        /// </remarks>
        [Test]
        public void TheOlderDecorFolderStillSpellsTheTagItAlwaysDid()
        {
            WritePrefab("Props/Decor", "Sideboard", new Vector3(1.6f, 0.9f, 0.5f));
            WritePrefab("Decor", "Lamp", new Vector3(0.3f, 1.4f, 0.3f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("prop/decor/sideboard"),
                Is.EqualTo(new[] { "prop", BuildingGenerator.DecorTag }));
            Assert.That(TagsOf("prop/decor/lamp"),
                Is.EqualTo(new[] { "prop", BuildingGenerator.DecorTag }),
                "a decor folder with nothing above it stopped naming the tag it always named");
        }

        [Test]
        public void AFolderNobodyHasHeardOfBecomesItsOwnTag()
        {
            WritePrefab("Covers/Low/Wooden", "Pallet", new Vector3(1f, 0.4f, 1f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(TagsOf("cover/low/wooden/pallet"),
                Is.EqualTo(new[] { "cover", "cover/low", "cover/low/wooden" }));
        }

        [Test]
        public void APrefabInNoFolderIsSkippedRatherThanGivenAnEmptyRow()
        {
            WritePrefab(null, "Loose", new Vector3(1f, 1f, 1f));
            WritePrefab("Walls", "Panel", new Vector3(2f, 3f, 0.2f));

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Untagged, Is.EqualTo(1));
            Assert.That(_catalog.Rows.Count, Is.EqualTo(1));
            Assert.That(_catalog.Rows[0].LogicalId, Is.EqualTo("structure/wall/panel"));
        }

        // --- sizes come from box colliders ------------------------------------------------------

        [Test]
        public void ABoxColliderFillsInTheFootprintAndTheHeight()
        {
            WritePrefab("Covers/Low", "Sandbags", new Vector3(2f, 0.9f, 1f));

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("cover/low/sandbags");

            Assert.That(row.FootprintSize.x, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(row.FootprintSize.y, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(row.Height, Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(row.BaseOffset, Is.EqualTo(0f).Within(1e-4f),
                "art modelled on its base does not hang below its pivot");
        }

        /// <remarks>
        /// <para>
        /// The vertical half of the same measurement, and the half that was being thrown away. A
        /// prefab modelled around its own centre — which is every Unity primitive and a great deal
        /// of furniture — reaches as far below its pivot as above it, and a row that recorded only
        /// the height above stood it half sunk into the floor.
        /// </para>
        /// <para>
        /// Unlike the footprint, this is a number a row can state exactly rather than having to err
        /// large: a footprint has to be centred on the pivot because that is all a row can say
        /// about it, but how far down the art reaches is one number and it is the one the generator
        /// lifts the piece by.
        /// </para>
        /// </remarks>
        [Test]
        public void ArtModelledAroundItsPivotIsMeasuredAsHangingBelowIt()
        {
            WritePrefab("Decor", "Capsule", new Vector3(1f, 2f, 1f), Vector3.zero);

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("prop/decor/capsule");

            Assert.That(row.Height, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(row.BaseOffset, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(_catalog.ToCatalog().Find("prop/decor/capsule").StandingHeight,
                Is.EqualTo(2f).Within(1e-4f),
                "the piece is two metres tall however its pivot is placed");
        }

        /// <remarks>
        /// <para>
        /// The case that was making art hover, and the reason the offset is signed. A pot whose
        /// modeller left the pivot below the art reaches <em>up</em> from its pivot rather than
        /// down, so the lift that stands it on a floor is a negative one — and the figure was being
        /// clamped at zero, which stood the pivot on the floor and left the art in the air by
        /// exactly the amount that had been thrown away.
        /// </para>
        /// <para>
        /// A metre here, which is absurd for a plant and is the point: nothing downstream reports
        /// the number, so the only way the fault shows is as a piece of art visibly off the ground,
        /// and how far off depends entirely on how far the modeller was out. What is asserted is
        /// the sign and the arithmetic, not a tolerance.
        /// </para>
        /// </remarks>
        [Test]
        public void ArtModelledAboveItsPivotIsMeasuredAsReachingUpFromIt()
        {
            WritePrefab("Decor", "Potted", new Vector3(1f, 1f, 1f), new Vector3(0f, 1.5f, 0f));

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("prop/decor/potted");

            Assert.That(row.Height, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(row.BaseOffset, Is.EqualTo(-1f).Within(1e-4f),
                "art whose lowest point is above its pivot has to be lowered, not lifted");

            CatalogEntry entry = _catalog.ToCatalog().Find("prop/decor/potted");
            Assert.That(entry.BaseOffset, Is.EqualTo(-1f).Within(1e-4f),
                "the sign has to survive the trip into the catalog Core reads");

            Assert.That(entry.StandingHeight, Is.EqualTo(1f).Within(1e-4f),
                "the piece is a metre tall however its pivot is placed");
        }

        /// <remarks>
        /// The half of the same fix that a person actually sees: the piece placed on level ground
        /// stands <em>on</em> it. Asserted through <see cref="Placement.AtQuarterTurn"/> rather than
        /// on the row alone, because the row is only half the arithmetic and it is the other half
        /// that used to leave the plant floating.
        /// </remarks>
        [Test]
        public void ArtModelledAboveItsPivotIsStillPlacedOnTheGround()
        {
            WritePrefab("Decor", "Standing", new Vector3(1f, 1f, 1f), new Vector3(0f, 1.5f, 0f));

            CatalogSync.Sync(_catalog, Root);
            CatalogEntry entry = _catalog.ToCatalog().Find("prop/decor/standing");

            Placement placed = Placement.AtQuarterTurn(entry, Vec2.Zero, 0);

            Assert.That(placed.Pose.Position.Y + 1f, Is.EqualTo(0f).Within(1e-4f),
                "the underside of the art should land on the surface, not a metre over it");
        }

        /// <remarks>
        /// <para>
        /// Art modelled off to one side is measured at its own size, with the offset from the pivot
        /// recorded beside it. A row used to be able to say only how big something was, so this
        /// came back as the box that held the collider <em>and</em> was centred on the pivot —
        /// four metres across for a one-metre crate a metre and a half out.
        /// </para>
        /// <para>
        /// Erring large was the safe direction for a rule whose job is to keep props out of each
        /// other, but it is the wrong number for anything cut to fit: a flight of stairs pivoted at
        /// the foot of its run measured nearly twice its length, and the stairwell opening cut from
        /// it was four times the floor the stairs cover. See
        /// <see cref="BuildingVerticalTests.TheOpeningIsTheFewestWholeTilesTheFlightCanNeed"/>.
        /// </para>
        /// </remarks>
        [Test]
        public void ArtModelledOffToOneSideIsMeasuredAtItsOwnSizeAndOffset()
        {
            WritePrefab("Props", "Offset", new Vector3(1f, 1f, 1f), new Vector3(1.5f, 0.5f, 0f));

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("prop/offset");

            Assert.That(row.FootprintSize.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(row.FootprintOffset.x, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(row.FootprintOffset.y, Is.EqualTo(0f).Within(1e-4f));

            // And the footprint Core is handed is that rectangle, off to one side of the pivot.
            Rect2 footprint = _catalog.ToCatalog().Find("prop/offset").Footprint;
            Assert.That(footprint.MinX, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(footprint.MaxX, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void APrefabWithNothingToMeasureIsReportedAndLeftAtItsDefaults()
        {
            WritePrefab("Props", "Bare", null);

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Unmeasured, Is.EqualTo(1));
            Assert.That(result.FromArt, Is.Zero, "there was no art to measure either");
            Assert.That(Row("prop/bare").Prefab, Is.Not.Null);
        }

        /// <remarks>
        /// <para>
        /// A bought art pack ships no box colliders, and often no colliders at all. A row this used
        /// to leave alone kept the defaults on <see cref="CatalogAsset.Row"/> — a one-metre cube —
        /// so every prop in the pack came out the same size and every fence panel came out square.
        /// A square footprint has no long axis, so the run that lays fence panels end to end could
        /// not tell which way round to turn them and stepped a metre at a time along art that is
        /// metres long: the boundary of the map came out as a row of panels turned across the line
        /// they were meant to run along.
        /// </para>
        /// <para>
        /// So the art itself is the fallback. The mesh is 3 m along X and 0.4 along Z, which is what
        /// the row has to say for the run to lay it lengthwise.
        /// </para>
        /// </remarks>
        [Test]
        public void APrefabWithNoColliderIsMeasuredOffItsOwnArt()
        {
            WriteMeshPrefab("fence/WoodFence", "Panel", new Vector3(3f, 1.2f, 0.4f));

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("fence/woodfence/panel");

            Assert.That(result.Unmeasured, Is.Zero, "the art was there to be measured");
            Assert.That(result.FromArt, Is.EqualTo(1), "the sync did not say where the size came from");

            Assert.That(row.FootprintSize.x, Is.EqualTo(3f).Within(1e-3f));
            Assert.That(row.FootprintSize.y, Is.EqualTo(0.4f).Within(1e-3f));
            Assert.That(row.Height, Is.EqualTo(1.2f).Within(1e-3f));
        }

        /// <remarks>
        /// The order matters and only one way round. A collider is a size somebody chose and a mesh
        /// is whatever the art happens to be, so a prefab that has both is measured by the box —
        /// which is also what keeps every catalog anybody has already synced exactly as it was.
        /// </remarks>
        [Test]
        public void ABoxColliderIsPreferredToTheArtAroundIt()
        {
            GameObject prefab = WriteMeshPrefab("Props", "Crate", new Vector3(3f, 1.2f, 0.4f));

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            BoxCollider box = instance.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 1f);
            box.center = new Vector3(0f, 0.5f, 0f);
            PrefabUtility.SaveAsPrefabAsset(instance, AssetDatabase.GetAssetPath(prefab));
            Object.DestroyImmediate(instance);

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.FromArt, Is.Zero, "the collider was ignored in favour of the mesh");
            Assert.That(Row("prop/crate").FootprintSize.x, Is.EqualTo(1f).Within(1e-3f));
        }

        // --- syncing over a catalog somebody has edited -----------------------------------------

        [Test]
        public void ASecondSyncKeepsTheWeightAndSocketsSomebodyTypedIn()
        {
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            CatalogSync.Sync(_catalog, Root);

            var rows = new List<CatalogAsset.Row>(_catalog.Rows);
            rows[0].Weight = 7f;
            rows[0].Sockets.Add(new CatalogAsset.SocketRow
            {
                Name = "top",
                Tags = new[] { CoverPlacer.PropSurfaceTag },
                LocalPosition = new Vector3(0f, 1f, 0f),
            });
            _catalog.SetRows(rows);

            CatalogSyncResult again = CatalogSync.Sync(_catalog, Root);

            Assert.That(again.Added, Is.EqualTo(0));
            Assert.That(again.Updated, Is.EqualTo(1));
            Assert.That(Row("cover/low/crate").Weight, Is.EqualTo(7f));
            Assert.That(Row("cover/low/crate").Sockets.Count, Is.EqualTo(1));
        }

        /// <remarks>
        /// A catalog holds rows a folder knows nothing about — an exported building, something
        /// bound by hand, art filed outside the workspace — and a sync that deleted them for being
        /// absent from the folder it happened to scan would make that folder the authority over a
        /// file the user also edits. The prefab here sits in the root, which the scan skips for not
        /// being in a folder, so the row is bound to art the project has and the scan did not
        /// produce.
        /// </remarks>
        [Test]
        public void ASyncLeavesRowsItDidNotProduceAlone()
        {
            _catalog.SetRows(new[]
            {
                new CatalogAsset.Row
                {
                    LogicalId = "structure/building/by_hand",
                    Weight = 1f,
                    Prefab = WritePrefab(string.Empty, "ByHand", new Vector3(6f, 5f, 6f)),
                },
            });

            WritePrefab("Walls", "Panel", new Vector3(2f, 3f, 0.2f));
            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Kept, Is.EqualTo(1));
            Assert.That(result.Pruned, Is.EqualTo(0));
            Assert.That(Row("structure/building/by_hand"), Is.Not.Null);
            Assert.That(_catalog.Rows.Count, Is.EqualTo(2));
        }

        /// <remarks>
        /// <para>
        /// The other side of the same question, and the reason it is a different one. A row whose
        /// prefab has been deleted is still tagged, so every query still returns it and every
        /// weighted pick can still land on it — and what the generator does with the one it picked
        /// is reserve the ground, write the placement and realise nothing. That is a hole in the map
        /// at a spot something was deliberately chosen for, recorded in the document as a perfectly
        /// ordinary object.
        /// </para>
        /// <para>
        /// Deleted through the asset database rather than by clearing the field, because that is how
        /// it happens: somebody removes a prefab and the reference in the catalog resolves to null
        /// from then on.
        /// </para>
        /// </remarks>
        [Test]
        public void ASyncPrunesARowWhosePrefabTheProjectNoLongerHas()
        {
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            WritePrefab("Walls", "Panel", new Vector3(2f, 3f, 0.2f));
            CatalogSync.Sync(_catalog, Root);

            Assert.That(_catalog.Rows.Count, Is.EqualTo(2));

            AssetDatabase.DeleteAsset(Root + "/Covers/Low/Crate.prefab");
            AssetDatabase.Refresh();

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Pruned, Is.EqualTo(1));
            Assert.That(Ids(new List<CatalogAsset.Row>(_catalog.Rows)),
                Is.EqualTo(new[] { "structure/wall/panel" }));
            Assert.That(
                _catalog.ToCatalog().Query(TagQuery.All(CoverPlacer.CoverTag)).Count, Is.EqualTo(0),
                "a pruned row must not still answer a query");
        }

        /// <remarks>
        /// A row that never named a prefab is the same hole arrived at by another route, so it goes
        /// the same way. There is nothing a catalog can do with a row it cannot instantiate, and
        /// leaving it in place to be picked is worse than not having it.
        /// </remarks>
        [Test]
        public void ASyncPrunesARowThatNamesNoPrefabAtAll()
        {
            _catalog.SetRows(new[]
            {
                new CatalogAsset.Row { LogicalId = "structure/building/never_bound", Weight = 1f },
            });

            WritePrefab("Walls", "Panel", new Vector3(2f, 3f, 0.2f));
            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Pruned, Is.EqualTo(1));
            Assert.That(result.Kept, Is.EqualTo(0));
            Assert.That(_catalog.Rows.Count, Is.EqualTo(1));
        }

        /// <remarks>
        /// <para>
        /// The way a person replaces a piece of art: delete the prefab, make a new one, call it
        /// what the old one was called. The new prefab is a new asset with a new guid, so the row
        /// that named the old one is left holding nothing — and the sweep used to run after the
        /// scan had already been merged in, which put a live row and a dead one side by side under
        /// the same name. Both were tagged, so every query returned two crates where the folder
        /// held one, and half the picks that landed on it realised nothing.
        /// </para>
        /// <para>
        /// Deleted through the asset database rather than by clearing the field, because that is
        /// how it happens.
        /// </para>
        /// </remarks>
        [Test]
        public void APrefabDeletedAndRemadeUnderTheSameNameLeavesOneRow()
        {
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            CatalogSync.Sync(_catalog, Root);

            AssetDatabase.DeleteAsset(Root + "/Covers/Low/Crate.prefab");
            AssetDatabase.Refresh();
            GameObject remade = WritePrefab("Covers/Low", "Crate", new Vector3(2f, 2f, 2f));

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Pruned, Is.EqualTo(1));
            Assert.That(Ids(new List<CatalogAsset.Row>(_catalog.Rows)),
                Is.EqualTo(new[] { "cover/low/crate" }));
            Assert.That(Row("cover/low/crate").Prefab, Is.EqualTo(remade),
                "the surviving row must be the one bound to the prefab that is actually there");
            Assert.That(Row("cover/low/crate").FootprintSize.x, Is.EqualTo(2f).Within(1e-4f),
                "and it must be measured off it");
        }

        /// <remarks>
        /// <para>
        /// A logical id is spelled from a folder and a file name, so renaming the file spells a
        /// different one — and matching a scan to the catalog by id alone therefore wrote a second
        /// row for a prefab that already had one. Nothing pruned it either: the reference follows
        /// the asset through a rename, so both rows were live, both were tagged, and the piece was
        /// twice as likely to be picked as the person asked for.
        /// </para>
        /// <para>
        /// The id it keeps is its own. A saved world document names the ids it placed, so a sync
        /// that renamed a row to follow a file would quietly break every map already built from
        /// it — see <c>CatalogSync.Merge</c>.
        /// </para>
        /// </remarks>
        [Test]
        public void ARenamedPrefabUpdatesItsRowRatherThanGettingASecondOne()
        {
            GameObject prefab = WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            CatalogSync.Sync(_catalog, Root);

            AssetDatabase.RenameAsset(Root + "/Covers/Low/Crate.prefab", "Barrel");
            AssetDatabase.Refresh();

            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Added, Is.EqualTo(0));
            Assert.That(result.Updated, Is.EqualTo(1));
            Assert.That(_catalog.Rows.Count, Is.EqualTo(1));
            Assert.That(Row("cover/low/crate").Prefab, Is.EqualTo(prefab));
        }

        /// <remarks>
        /// The other half of the same rule, and the half that has to keep working: a row is the
        /// prefab's, so moving the file updates the row it already had — and what the folder says
        /// is what the tags are for, so those follow the move even though the id does not.
        /// </remarks>
        [Test]
        public void APrefabMovedToAnotherFolderKeepsItsRowAndTakesTheNewFoldersTags()
        {
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            CatalogSync.Sync(_catalog, Root);

            FolderUnderRoot("Covers/High");
            AssetDatabase.MoveAsset(
                Root + "/Covers/Low/Crate.prefab", Root + "/Covers/High/Crate.prefab");
            AssetDatabase.Refresh();

            CatalogSync.Sync(_catalog, Root);

            Assert.That(_catalog.Rows.Count, Is.EqualTo(1));
            Assert.That(TagsOf("cover/low/crate"),
                Is.EqualTo(new[] { CoverPlacer.CoverTag, CoverPlacer.HighCoverTag }));
        }

        [Test]
        public void TheSameFolderAlwaysProducesTheSameRowsInTheSameOrder()
        {
            WritePrefab("Walls", "Panel", new Vector3(2f, 3f, 0.2f));
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));
            WritePrefab("Floors", "Slab", new Vector3(2f, 0.1f, 2f));

            List<CatalogAsset.Row> first = CatalogSync.Scan(Root);
            List<CatalogAsset.Row> second = CatalogSync.Scan(Root);

            Assert.That(Ids(second), Is.EqualTo(Ids(first)));
            Assert.That(
                Ids(first),
                Is.Ordered.Using((System.Collections.IComparer)System.StringComparer.Ordinal));
        }

        [Test]
        public void ASyncedCatalogBuildsACoreCatalog()
        {
            WritePrefab("Walls", "Panel_2m", new Vector3(2f, 2.9f, 0.2f));
            WritePrefab("Covers/Low", "Crate", new Vector3(1f, 1f, 1f));

            CatalogSync.Sync(_catalog, Root);
            Catalog catalog = _catalog.ToCatalog();

            Assert.That(catalog.Query(TagQuery.All(BuildingGenerator.WallTag)).Count, Is.EqualTo(1));
            Assert.That(catalog.Query(TagQuery.All(CoverPlacer.CoverTag)).Count, Is.EqualTo(1));
        }

        [Test]
        public void ASyncOfSomethingThatIsNotAFolderIsRefused()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => CatalogSync.Sync(_catalog, "Assets/NoSuchFolder"));
        }

        // --- helpers ---------------------------------------------------------------------------

        // --- doorway markers ---------------------------------------------------------------------

        /// <remarks>
        /// The one thing about a structure a scan cannot work out for itself. A box collider on a
        /// child called <c>DoorwayMarker</c> says "the way in is here", and the row comes out
        /// carrying that rectangle in the prefab's own space — which is what a map turns and keeps
        /// clear when it places the house.
        /// </remarks>
        [Test]
        public void ADoorwayMarkerInAPrefabBecomesADeclaredDoorwayOnItsRow()
        {
            GameObject house = WritePrefab("Houses", "Cottage", new Vector3(8f, 3f, 6f));
            MarkDoorway(house, "DoorwayMarker", new Vector3(0f, 1f, -3f), new Vector3(2f, 2f, 1f));

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("structure/house/cottage");

            Assert.That(row.Doorways, Has.Count.EqualTo(1));
            Assert.That(row.Doorways[0].Center.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(row.Doorways[0].Center.y, Is.EqualTo(-3f).Within(1e-3f));
            Assert.That(row.Doorways[0].Size.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(row.Doorways[0].Size.y, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <remarks>
        /// A marker is an annotation and not art. A prefab that grew by half a metre because
        /// somebody said where its door was would be a measurement reporting on itself — and the
        /// footprint is what the map holds the house inside, so the house would stop fitting the
        /// lane it fitted yesterday.
        /// </remarks>
        [Test]
        public void ADoorwayMarkerIsNotMeasuredIntoTheFootprint()
        {
            GameObject plain = WritePrefab("Houses", "Plain", new Vector3(8f, 3f, 6f));
            GameObject marked = WritePrefab("Houses", "Marked", new Vector3(8f, 3f, 6f));
            MarkDoorway(marked, "DoorwayMarker_Front", new Vector3(0f, 1f, -4f), new Vector3(2f, 2f, 2f));

            CatalogSync.Sync(_catalog, Root);

            Assert.That(Row("structure/house/marked").FootprintSize,
                Is.EqualTo(Row("structure/house/plain").FootprintSize));
            Assert.That(Row("structure/house/marked").Height,
                Is.EqualTo(Row("structure/house/plain").Height).Within(1e-3f));
        }

        /// <remarks>
        /// The quickest way of marking a spot is an empty transform, and it has to work. What it
        /// cannot say is how wide the opening is, so what it declares is a threshold a metre square
        /// — that a person walks through here, rather than how much room they have doing it.
        /// </remarks>
        [Test]
        public void AnEmptyDoorwayMarkerDeclaresAThresholdOfItsOwn()
        {
            GameObject house = WritePrefab("Houses", "Empty", new Vector3(8f, 3f, 6f));
            MarkDoorway(house, "doorwaymarker", new Vector3(2f, 0f, -3f), null);

            CatalogSync.Sync(_catalog, Root);
            CatalogAsset.Row row = Row("structure/house/empty");

            Assert.That(row.Doorways, Has.Count.EqualTo(1));
            Assert.That(row.Doorways[0].Center.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(row.Doorways[0].Size.x,
                Is.EqualTo(ArenaLayoutGenerator.DoorwayDepth).Within(1e-3f));
        }

        /// <remarks>
        /// The same rule the measurements follow. A baked building has no marker in it — nobody
        /// modelled it — and the doorways it was exported with are exactly right, so a later sync of
        /// the folder it was written into must not sweep them away.
        /// </remarks>
        [Test]
        public void ASyncKeepsTheDoorwaysOnARowWhosePrefabDeclaresNone()
        {
            WritePrefab("Houses", "Baked", new Vector3(8f, 3f, 6f));
            CatalogSync.Sync(_catalog, Root);

            Row("structure/house/baked").Doorways.Add(
                new CatalogAsset.DoorwayRow { Center = new Vector2(0f, -3f), Size = Vector2.one });

            CatalogSync.Sync(_catalog, Root);

            Assert.That(Row("structure/house/baked").Doorways, Has.Count.EqualTo(1));
        }

        /// <summary>Adds a doorway marker to a prefab on disk, with a box collider or without one.</summary>
        static void MarkDoorway(GameObject prefab, string name, Vector3 at, Vector3? size)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);

            try
            {
                var marker = new GameObject(name);
                marker.transform.SetParent(contents.transform, false);
                marker.transform.localPosition = at;

                if (size.HasValue)
                {
                    marker.AddComponent<BoxCollider>().size = size.Value;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Writes a prefab with a mesh and no collider into a folder under the root, sized to
        /// <paramref name="size"/> and standing on its own base.
        /// </summary>
        /// <remarks>
        /// A cube primitive with its collider taken off, scaled to the size asked for and lifted so
        /// its underside is on the pivot — which is how art in this tool's catalog is modelled, and
        /// what makes the base offset come out at zero.
        /// </remarks>
        static GameObject WriteMeshPrefab(string folder, string name, Vector3 size)
        {
            string path = FolderUnderRoot(folder);

            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = name;
            Object.DestroyImmediate(source.GetComponent<BoxCollider>());
            source.transform.localScale = size;
            source.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            // A parent at the origin, so the mesh sits off the prefab root's pivot the way a piece
            // of bought art does rather than being centred on it.
            var root = new GameObject(name);
            source.transform.SetParent(root.transform, false);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Creates a folder chain under the root and returns its path.</summary>
        static string FolderUnderRoot(string folder)
        {
            string path = Root;
            if (string.IsNullOrEmpty(folder))
            {
                return path;
            }

            string[] parts = folder.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, parts[i]);
                }

                path = next;
            }

            return path;
        }

        /// <summary>Writes a prefab with one box collider into a folder under the root.</summary>
        static GameObject WritePrefab(string folder, string name, Vector3? size, Vector3? centre = null)
        {
            string path = FolderUnderRoot(folder);

            var source = new GameObject(name);
            if (size.HasValue)
            {
                var box = source.AddComponent<BoxCollider>();
                box.size = size.Value;

                // Sitting on the ground by default, which is where the pivot of a piece of art in
                // this tool's catalog is.
                box.center = centre ?? new Vector3(0f, size.Value.y * 0.5f, 0f);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path + "/" + name + ".prefab");
            Object.DestroyImmediate(source);
            return prefab;
        }

        CatalogAsset.Row Row(string logicalId)
        {
            for (int i = 0; i < _catalog.Rows.Count; i++)
            {
                if (_catalog.Rows[i].LogicalId == logicalId)
                {
                    return _catalog.Rows[i];
                }
            }

            Assert.Fail($"The catalog has no row '{logicalId}'.");
            return null;
        }

        string[] TagsOf(string logicalId) => Row(logicalId).Tags;

        static string[] Ids(List<CatalogAsset.Row> rows)
        {
            var ids = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                ids[i] = rows[i].LogicalId;
            }

            return ids;
        }
    }
}
