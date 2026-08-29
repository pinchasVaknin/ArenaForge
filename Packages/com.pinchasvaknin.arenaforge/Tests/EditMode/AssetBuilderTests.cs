using System;
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
    /// The starter art builder: four prefabs with real meshes, exactly fitted colliders and
    /// sizes the generator can lay out without slack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is worth asserting is the measurement, not the modelling. A staircase that looks right
    /// and measures 1 × 5.5 m opens a hole twice the size it needs; a fence a metre thick is a wall
    /// on the edge of a roof; a window that is not exactly a wall module is a gap in the middle of
    /// a run. So the suite below syncs the generated prefabs into a catalog and checks the rows
    /// that come out, which is exactly what the generator will read.
    /// </para>
    /// <para>
    /// The sizes are checked against the numbers the tool was asked for rather than against
    /// constants it exports, so a change to the modelling that quietly changed a size would fail
    /// here rather than surface as a gap in a roof three suites away.
    /// </para>
    /// </remarks>
    public sealed class AssetBuilderTests
    {
        const string Root = "Assets/ArenaForgeAssetTests";

        /// <summary>The starter pieces, and the folder each one belongs in, in the same order.</summary>
        static readonly string[] Pieces =
        {
            ArenaAssetBuilder.StairsName,
            ArenaAssetBuilder.FenceName,
            ArenaAssetBuilder.FloorName,
            ArenaAssetBuilder.WindowName,
        };

        /// <inheritdoc cref="Pieces"/>
        static readonly string[] Folders =
        {
            ArenaAssetBuilder.StairsFolder,
            ArenaAssetBuilder.FenceFolder,
            ArenaAssetBuilder.FloorFolder,
            ArenaAssetBuilder.WindowFolder,
        };

        CatalogAsset _catalog;

        [SetUp]
        public void CreateWorkspace()
        {
            Delete();
            AssetDatabase.Refresh();
            AssetDatabase.CreateFolder("Assets", "ArenaForgeAssetTests");
            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
        }

        [TearDown]
        public void RemoveWorkspace()
        {
            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
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

        // --- what it writes --------------------------------------------------------------------

        [Test]
        public void ItWritesTheStarterPrefabsWhereTheSyncWillFindThem()
        {
            IReadOnlyList<GameObject> written = ArenaAssetBuilder.Generate(Root);

            Assert.That(written.Count, Is.EqualTo(Pieces.Length));
            for (int i = 0; i < Pieces.Length; i++)
            {
                Assert.That(Prefab(Folders[i], Pieces[i]), Is.Not.Null, Pieces[i]);
            }
        }

        /// <remarks>
        /// The folders are the workspace's own, not a set of the builder's beside them.
        /// Nothing about the catalog would catch the difference — <see cref="CatalogSync"/>
        /// restarts the tag path at a name it recognises, so <c>Stairs</c> and <c>PropBuilding/Stairs</c>
        /// both spell <c>structure/stairs</c> — so what a workspace off the map costs is two
        /// places to put a staircase and a person having to guess which.
        /// </remarks>
        [Test]
        public void TheStarterArtLandsInFoldersTheWorkspaceScaffolds()
        {
            for (int i = 0; i < Folders.Length; i++)
            {
                Assert.That(ArenaWorkspace.Folders, Contains.Item(Folders[i]), Folders[i]);
            }
        }

        /// <remarks>
        /// A prefab with a collider and no mesh is invisible and still measures correctly, so it
        /// passes every size property this suite has and looks like a broken generator in the
        /// scene. It is the failure a mesh saved after its prefab produces.
        /// </remarks>
        [Test]
        public void EveryStarterPrefabHasAMeshThatSurvivedBeingSaved()
        {
            ArenaAssetBuilder.Generate(Root);

            for (int i = 0; i < Pieces.Length; i++)
            {
                GameObject prefab = Prefab(Folders[i], Pieces[i]);
                var filter = prefab.GetComponent<MeshFilter>();

                Assert.That(filter, Is.Not.Null, $"{Pieces[i]} has no mesh filter");
                Assert.That(filter.sharedMesh, Is.Not.Null, $"{Pieces[i]} lost its mesh");
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0), Pieces[i]);
                Assert.That(AssetDatabase.Contains(filter.sharedMesh), Is.True,
                    $"{Pieces[i]} points at a mesh that was never saved");
                Assert.That(prefab.GetComponent<MeshRenderer>().sharedMaterial, Is.Not.Null,
                    $"{Pieces[i]} would draw magenta");
            }
        }

        // --- what it measures as ------------------------------------------------------------------

        /// <remarks>
        /// The property the whole tool exists for. These are the numbers the building generator
        /// reads, and every one of them being exact is what lets a 2 × 3 m flight cut a 2 × 3 m
        /// hole and a metre of fence close a roof of metre tiles with no corner gap.
        /// </remarks>
        [Test]
        public void TheGeneratedArtMeasuresExactlyTheSizeItWasAskedFor()
        {
            ArenaAssetBuilder.Generate(Root);
            CatalogSync.Sync(_catalog, Root);
            Catalog catalog = _catalog.ToCatalog();

            CatalogEntry stairs = Entry(catalog, BuildingGenerator.StairsTag);
            Assert.That(stairs.Footprint.Width, Is.EqualTo(2f).Within(1e-4f),
                "stairs are two metres wide, which is the width of a corridor");
            Assert.That(stairs.Footprint.Depth, Is.EqualTo(3f).Within(1e-4f), "stairs run three metres");
            Assert.That(stairs.Height, Is.EqualTo(3f).Within(1e-4f), "stairs climb a whole storey");

            CatalogEntry fence = Entry(catalog, BuildingGenerator.ParapetTag);
            Assert.That(MathF.Max(fence.Footprint.Width, fence.Footprint.Depth),
                Is.EqualTo(1f).Within(1e-4f), "a fence piece is a metre long");
            Assert.That(MathF.Min(fence.Footprint.Width, fence.Footprint.Depth),
                Is.EqualTo(0.1f).Within(1e-4f), "and a tenth of a metre thick");
            Assert.That(fence.Height, Is.EqualTo(1f).Within(1e-4f));

            CatalogEntry floor = Entry(catalog, BuildingGenerator.FloorTileTag);
            Assert.That(floor.Footprint.Width, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(floor.Footprint.Depth, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(floor.Height, Is.EqualTo(0.2f).Within(1e-4f));

            // A window is measured against the wall it is swapped into rather than against the
            // floor tile: anything else is a gap in the middle of a run.
            CatalogEntry window = Entry(catalog, BuildingGenerator.WindowTag);
            Assert.That(MathF.Max(window.Footprint.Width, window.Footprint.Depth),
                Is.EqualTo(2f).Within(1e-4f), "a window is one wall module long");
            Assert.That(MathF.Min(window.Footprint.Width, window.Footprint.Depth),
                Is.EqualTo(0.2f).Within(1e-4f), "and as thick as the wall");
            Assert.That(window.Height, Is.EqualTo(2.9f).Within(1e-4f), "and as tall");
        }

        /// <remarks>
        /// <para>
        /// The point of the piece rather than a detail of it. A window you cannot see or shoot
        /// through is a wall with a picture of a window on it, and the generator swaps these into
        /// exterior runs precisely so that a building has sightlines out of it — so what is
        /// asserted is that the opening is a hole in the colliders as well as in the mesh.
        /// </para>
        /// <para>
        /// In the upper half, because that is where a window goes and where it is worth having: the
        /// wall under the sill is the cover that makes the building worth standing inside.
        /// </para>
        /// </remarks>
        [Test]
        public void TheWindowIsAHoleInTheUpperHalfOfAWall()
        {
            ArenaAssetBuilder.Generate(Root);

            BoxCollider[] parts = Prefab(ArenaAssetBuilder.WindowFolder, ArenaAssetBuilder.WindowName)
                .GetComponentsInChildren<BoxCollider>(true);

            Assert.That(parts.Length, Is.GreaterThan(1), "a window of one box is a wall");

            // A point in the middle of the opening, and one directly below it in the wall. Clear
            // of the jambs at 0.6 m and of the bar down the middle, both of which are solid.
            var through = new Vector3(0.3f, 2f, 0f);
            var solid = new Vector3(0.3f, 0.7f, 0f);

            var blocked = false;
            var held = false;
            var top = 0f;
            var bottom = float.MaxValue;

            for (int i = 0; i < parts.Length; i++)
            {
                var box = new Bounds(parts[i].center, parts[i].size);

                blocked |= box.Contains(through);
                held |= box.Contains(solid);
                top = MathF.Max(top, box.max.y);
                bottom = MathF.Min(bottom, box.min.y);
            }

            Assert.That(blocked, Is.False, "the opening is filled in");
            Assert.That(held, Is.True, "there is no wall under the sill");
            Assert.That(bottom, Is.EqualTo(0f).Within(1e-4f), "the piece floats off its base");
            Assert.That(top, Is.EqualTo(2.9f).Within(1e-4f), "the piece is not a wall's height");

            // The opening is above the middle of the wall, not a doorway with a lintel.
            Assert.That(through.y, Is.GreaterThan(2.9f * 0.5f));
        }

        /// <remarks>
        /// Every piece is modelled on its base and centred on its footprint, so none of them needs
        /// the offsets that exist for art that is not. A generated piece that did would be the tool
        /// making work for itself.
        /// </remarks>
        [Test]
        public void TheGeneratedArtStandsOnItsOwnPivot()
        {
            ArenaAssetBuilder.Generate(Root);
            CatalogSync.Sync(_catalog, Root);
            Catalog catalog = _catalog.ToCatalog();

            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                CatalogEntry entry = catalog.Entries[i];

                Assert.That(entry.BaseOffset, Is.EqualTo(0f).Within(1e-4f),
                    $"{entry.LogicalId} hangs below its pivot");
                Assert.That(entry.Footprint.Center.X, Is.EqualTo(0f).Within(1e-4f),
                    $"{entry.LogicalId} is off to one side of its pivot");
                Assert.That(entry.Footprint.Center.Y, Is.EqualTo(0f).Within(1e-4f),
                    $"{entry.LogicalId} is off to one side of its pivot");
            }
        }

        /// <remarks>
        /// <para>
        /// What the user had to do by hand, and must not have to again: widen the flight to fit the
        /// corridor grid and then type a footprint offset into the catalog to put the measurement
        /// back where the art is. A sync reads the box its colliders make, so a piece modelled
        /// centred on its own pivot comes back with no offset and nothing to correct.
        /// </para>
        /// <para>
        /// A whole number of floor tiles on both axes is the other half of it: the opening a
        /// stairwell cuts is whole tiles, so a flight measuring two by three on a metre tile costs
        /// exactly the floor it stands on and a flight measuring anything else does not.
        /// </para>
        /// </remarks>
        [Test]
        public void TheStaircaseFitsTheFloorTileGridWithNoOffsetToTypeIn()
        {
            ArenaAssetBuilder.Generate(Root);
            CatalogSync.Sync(_catalog, Root);
            Catalog catalog = _catalog.ToCatalog();

            CatalogEntry stairs = Entry(catalog, BuildingGenerator.StairsTag);
            CatalogEntry floor = Entry(catalog, BuildingGenerator.FloorTileTag);

            Assert.That(stairs.Footprint.Center.X, Is.EqualTo(0f).Within(1e-4f),
                "the sync had to be told where the stairs are");
            Assert.That(stairs.Footprint.Center.Y, Is.EqualTo(0f).Within(1e-4f),
                "the sync had to be told where the stairs are");

            Assert.That(stairs.Footprint.Width / floor.Footprint.Width, Is.EqualTo(2f).Within(1e-4f),
                "a flight two floor tiles wide");
            Assert.That(stairs.Footprint.Depth / floor.Footprint.Depth, Is.EqualTo(3f).Within(1e-4f),
                "and three long");
        }

        /// <remarks>
        /// A staircase is steps, and a single collider over the lot is a ramp you cannot walk up.
        /// Counted rather than shaped, because what the mesh looks like is taste and what the
        /// colliders do is not.
        /// </remarks>
        [Test]
        public void TheStaircaseIsBuiltFromStepsRatherThanOneBlock()
        {
            ArenaAssetBuilder.Generate(Root);

            BoxCollider[] steps = Prefab(ArenaAssetBuilder.StairsFolder, ArenaAssetBuilder.StairsName)
                .GetComponentsInChildren<BoxCollider>(true);

            Assert.That(steps.Length, Is.GreaterThan(4), "a staircase of four boxes is a ramp");

            // Each one is a step up from the last, and the top of the last is the whole storey.
            // Every one of them is the full width of the flight, so the colliders make the box the
            // catalog measures rather than a stack of narrower ones inside it.
            float top = 0f;
            for (int i = 0; i < steps.Length; i++)
            {
                top = MathF.Max(top, steps[i].center.y + steps[i].size.y * 0.5f);
                Assert.That(steps[i].size.x, Is.EqualTo(2f).Within(1e-4f), $"step {i} is not the full width");
            }

            Assert.That(top, Is.EqualTo(3f).Within(1e-4f));
        }

        /// <remarks>
        /// <para>
        /// What a solid flight costs, and it is not a modelling opinion. A building stacks its
        /// flights one above another in the same shaft, so the underside of the flight above is the
        /// ceiling of the flight below — and a step that reaches the floor makes that ceiling flat
        /// at the height of the storey. You then climb the bottom of the run under three metres of
        /// headroom and the top of it under none, having crawled the last few steps.
        /// </para>
        /// <para>
        /// So the property is measured the way a person meets it: stand the flight over a copy of
        /// itself one storey up and check what is above every step of the lower one. Two metres is
        /// a door, and a staircase you have to duck to climb is the thing this exists to stop.
        /// </para>
        /// </remarks>
        [Test]
        public void AFlightIsOpenUnderneathSoOneCanStandOverAnother()
        {
            ArenaAssetBuilder.Generate(Root);

            BoxCollider[] steps = Prefab(ArenaAssetBuilder.StairsFolder, ArenaAssetBuilder.StairsName)
                .GetComponentsInChildren<BoxCollider>(true);

            // The storey the flight climbs, which is where the next one up stands.
            const float storey = 3f;
            var tight = new List<string>();

            for (int i = 0; i < steps.Length; i++)
            {
                var step = new Bounds(steps[i].center, steps[i].size);
                float headroom = float.MaxValue;

                for (int j = 0; j < steps.Length; j++)
                {
                    var above = new Bounds(steps[j].center, steps[j].size);

                    // Only what is actually over this step: pieces that merely touch it end on
                    // are the next step along rather than something to walk into.
                    if (above.max.z <= step.min.z + 1e-4f || above.min.z >= step.max.z - 1e-4f)
                    {
                        continue;
                    }

                    headroom = MathF.Min(headroom, above.min.y + storey - step.max.y);
                }

                if (headroom < 2f)
                {
                    tight.Add($"step {i} has {headroom:0.##} m over it, not a doorway's worth");
                }
            }

            Assert.That(tight, Is.Empty);

            // And it is still standing on the floor rather than hovering a step above it.
            float underside = float.MaxValue;
            for (int i = 0; i < steps.Length; i++)
            {
                underside = MathF.Min(underside, steps[i].center.y - steps[i].size.y * 0.5f);
            }

            Assert.That(underside, Is.EqualTo(0f).Within(1e-4f), "the flight floats off its own base");
        }

        // --- running it twice ---------------------------------------------------------------------

        /// <remarks>
        /// Running it twice is what a person does when they cannot remember whether they ran it,
        /// and the second run must not leave two copies or a prefab pointing at an orphaned mesh.
        /// </remarks>
        [Test]
        public void GeneratingTwiceLeavesTheSameAssets()
        {
            ArenaAssetBuilder.Generate(Root);
            ArenaAssetBuilder.Generate(Root);

            Assert.That(
                AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Length,
                Is.EqualTo(Pieces.Length));

            var filter = Prefab(ArenaAssetBuilder.StairsFolder, ArenaAssetBuilder.StairsName)
                .GetComponent<MeshFilter>();
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(AssetDatabase.Contains(filter.sharedMesh), Is.True);
        }

        /// <remarks>
        /// The point of writing into a workspace rather than anywhere: the folders it uses are the
        /// ones <see cref="CatalogSync"/> reads tags out of, so a sync straight afterwards binds
        /// the art to the tags the building generator queries for and nothing has to be typed in.
        /// </remarks>
        [Test]
        public void TheGeneratedArtSyncsStraightIntoTheTagsTheGeneratorQueries()
        {
            ArenaAssetBuilder.Generate(Root);
            CatalogSyncResult result = CatalogSync.Sync(_catalog, Root);

            Assert.That(result.Added, Is.EqualTo(Pieces.Length));
            Assert.That(result.Unmeasured, Is.EqualTo(0), "generated art always has a collider");

            Catalog catalog = _catalog.ToCatalog();
            Assert.That(Entry(catalog, BuildingGenerator.StairsTag), Is.Not.Null);
            Assert.That(Entry(catalog, BuildingGenerator.ParapetTag), Is.Not.Null);
            Assert.That(Entry(catalog, BuildingGenerator.FloorTileTag), Is.Not.Null);
            Assert.That(Entry(catalog, BuildingGenerator.WindowTag), Is.Not.Null);
        }

        // --- helpers ------------------------------------------------------------------------------

        static GameObject Prefab(string folder, string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{folder}/{name}.prefab");

        static CatalogEntry Entry(Catalog catalog, string tag)
        {
            IReadOnlyList<CatalogEntry> found = catalog.Query(TagQuery.All(tag));
            Assert.That(found.Count, Is.EqualTo(1), $"expected exactly one '{tag}' entry");
            return found[0];
        }
    }
}
