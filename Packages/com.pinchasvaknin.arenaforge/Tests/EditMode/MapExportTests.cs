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
    /// Baking a finished map into a prefab a game can load with ArenaForge not in the project.
    /// </summary>
    /// <remarks>
    /// The claim being tested is "decoupled", and it is the kind of claim that fails quietly:
    /// a prefab still carrying an <see cref="ArenaObjectRef"/> looks identical in the scene view
    /// and breaks the first time it is opened in a project without the package. So the assertions
    /// are about what is <em>not</em> in the prefab as much as what is.
    /// </remarks>
    public sealed class MapExportTests
    {
        const string TempFolder = "Assets/ArenaForgeMapExportTestTemp";
        const string ExportPath = TempFolder + "/exported_map.prefab";

        CatalogAsset _catalog;
        ArenaMap _map;
        TerrainData _ground;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeMapExportTestTemp");
            }

            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
            _catalog.SetRows(new List<CatalogAsset.Row>
            {
                Row("marker/spawn", new[] { "marker", "spawn" }, new Vector2(2f, 2f), 0.5f),
                Row("structure/building/two_storey_01", new[] { "structure", "structure/building" },
                    new Vector2(12f, 10f), 6f),
                Row("structure/house/small_01", new[] { "structure", "structure/house" },
                    new Vector2(8f, 6f), 3.5f),
                Row("cover/low/crate_wood_01", new[] { "cover", "cover/low" }, new Vector2(1f, 1f), 1f),
                Row("cover/high/barrier_concrete_01", new[] { "cover", "cover/high" },
                    new Vector2(2f, 0.5f), 1.8f),
            });

            var host = new GameObject("ArenaForge Test Map");
            host.AddComponent<WorldRealizer>().Catalog = _catalog;
            _map = host.AddComponent<ArenaMap>();
            _map.Seed = 20260816UL;
        }

        [TearDown]
        public void TearDown()
        {
            if (_map != null)
            {
                UnityEngine.Object.DestroyImmediate(_map.gameObject);
            }

            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
            }

            if (_ground != null)
            {
                UnityEngine.Object.DestroyImmediate(_ground);
                _ground = null;
            }

            AssetDatabase.DeleteAsset(MapExport.DataPath(ExportPath));
            AssetDatabase.DeleteAsset(TempFolder);
        }

        CatalogAsset.Row Row(string logicalId, string[] tags, Vector2 footprint, float height)
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            string path = $"{TempFolder}/{logicalId.Replace('/', '_')}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            UnityEngine.Object.DestroyImmediate(source);

            return new CatalogAsset.Row
            {
                LogicalId = logicalId,
                Tags = tags,
                FootprintSize = footprint,
                Height = height,
                Weight = 1f,
                Prefab = prefab,
            };
        }

        /// <remarks>
        /// <para>
        /// An exported map used to be the props and nothing else: the terrain is a sibling of the
        /// map rather than a child of the realisation root the bake walks, so what came out was a
        /// field of crates over no ground at all.
        /// </para>
        /// <para>
        /// <strong>And the heightfield is the export's own.</strong> Pointing the prefab at the
        /// <c>TerrainData</c> the scene is still using would mean the next Generate rewrote the
        /// ground under every map ever exported from that scene, which is the opposite of what
        /// exporting is for. Both halves are asserted, because a terrain in the prefab that shared
        /// the scene's heightfield would look exactly like a working export until the day it did
        /// not.
        /// </para>
        /// </remarks>
        [Test]
        public void TheExportCarriesTheGroundAndAHeightfieldOfItsOwn()
        {
            TerrainData scene = Ground(2f);

            _map.Generate();

            GameObject prefab = MapExport.Export(_map, ExportPath);

            var exported = prefab.GetComponentInChildren<Terrain>(true);
            Assert.That(exported, Is.Not.Null, "the exported map has no ground in it");
            Assert.That(exported.terrainData, Is.Not.Null);

            Assert.That(exported.terrainData, Is.Not.SameAs(scene),
                "the export shares the scene's heightfield, so regenerating would rewrite it");

            Assert.That(AssetDatabase.Contains(exported.terrainData), Is.True,
                "the exported heightfield is not an asset on disk, so the prefab points at nothing");

            var collider = prefab.GetComponentInChildren<TerrainCollider>(true);
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.terrainData, Is.SameAs(exported.terrainData),
                "the collider and the renderer disagree about the ground");
        }

        /// <remarks>
        /// The map is the editable source and the export is a copy taken at a moment, so exporting
        /// must not reach back into the scene: the terrain the map is still pointing at has to be
        /// the one it had before.
        /// </remarks>
        [Test]
        public void ExportingLeavesTheScenesOwnTerrainAlone()
        {
            TerrainData scene = Ground(2f);

            _map.Generate();
            MapExport.Export(_map, ExportPath);

            Assert.That(_map.Terrain, Is.Not.Null);
            Assert.That(_map.Terrain.terrainData, Is.SameAs(scene));
        }

        /// <summary>Gives the map a terrain to export, and keeps it for teardown.</summary>
        TerrainData Ground(float height)
        {
            _ground = new TerrainData { heightmapResolution = 33 };
            _ground.size = new Vector3(128f, 8f, 128f);

            GameObject terrain = Terrain.CreateTerrainGameObject(_ground);
            terrain.transform.SetParent(_map.transform, false);
            terrain.transform.localPosition = new Vector3(-64f, height, -64f);

            _map.Terrain = terrain.GetComponent<Terrain>();
            return _ground;
        }

        // --- the bake -------------------------------------------------------------------------

        [Test]
        public void TheExportedPrefabHoldsExactlyTheResolvedObjects()
        {
            ResolvedWorld resolved = _map.Generate();

            GameObject prefab = MapExport.Export(_map, ExportPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.transform.childCount, Is.EqualTo(resolved.Objects.Count));
        }

        [Test]
        public void TheExportedPrefabCarriesNothingOfTheGenerator()
        {
            _map.Generate();

            GameObject prefab = MapExport.Export(_map, ExportPath);

            Assert.That(prefab.GetComponentsInChildren<ArenaObjectRef>(true), Is.Empty,
                "the prefab still names objects in a document that no longer governs it");
            Assert.That(prefab.GetComponentsInChildren<ArenaMap>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<ArenaBuilding>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<WorldRealizer>(true), Is.Empty);
        }

        /// <remarks>
        /// A hand edit is part of the map, so it has to be part of the bake. This is the one that
        /// would go unnoticed: exporting the generated list instead of the resolved one produces a
        /// prefab that looks right until you count the crates.
        /// </remarks>
        [Test]
        public void TheExportedPrefabIsTheMapAfterItsEditsRatherThanBefore()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            string doomed = doc.GeneratedObjects[doc.GeneratedObjects.Count - 1].StableId;
            doc.Overrides.Add(EditOverride.Delete(doomed));
            _map.SetDocument(doc);
            ResolvedWorld resolved = _map.Realize();

            GameObject prefab = MapExport.Export(_map, ExportPath);

            Assert.That(prefab.transform.childCount, Is.EqualTo(resolved.Objects.Count));
            Assert.That(prefab.transform.childCount, Is.EqualTo(doc.GeneratedObjects.Count - 1));
            Assert.That(Named(prefab, doomed), Is.Null, "the deleted object came back in the prefab");
        }

        /// <remarks>
        /// The realised instances are prefab instances rather than copies, and they have to stay
        /// that way through the bake: an exported map whose crates are no longer crate prefabs
        /// cannot follow an art-pack swap, which is the whole reason the catalog binds logical ids
        /// instead of storing meshes.
        /// </remarks>
        [Test]
        public void TheArtInsideTheExportedPrefabIsStillTheCatalogsPrefabs()
        {
            _map.Generate();

            GameObject prefab = MapExport.Export(_map, ExportPath);
            Transform crate = FindByLogicalId(prefab, "cover/low/crate_wood_01");

            Assert.That(crate, Is.Not.Null, "the map has no crate in it to check");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(crate.gameObject), Is.Not.Null,
                "the instance was flattened into a plain copy");
        }

        [Test]
        public void PosesSurviveTheBake()
        {
            ResolvedWorld resolved = _map.Generate();
            PlacedObject placed = resolved.Objects[0];

            GameObject prefab = MapExport.Export(_map, ExportPath);
            Transform baked = Named(prefab, placed.StableId);

            Assert.That(baked, Is.Not.Null);
            Assert.That(
                Vector3.Distance(baked.localPosition, CoreConvert.ToUnity(placed.Pose.Position)),
                Is.LessThan(1e-4f));
        }

        [Test]
        public void ExportingTwiceOverwritesRatherThanLeavingTwoAssets()
        {
            _map.Generate();

            MapExport.Export(_map, ExportPath);
            _map.Seed = 987654321UL;
            ResolvedWorld second = _map.Generate();
            GameObject prefab = MapExport.Export(_map, ExportPath);

            Assert.That(prefab.transform.childCount, Is.EqualTo(second.Objects.Count),
                "the second export is the map as it is now");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(ExportPath), Is.Not.Null);
        }

        // --- refusals -------------------------------------------------------------------------

        [Test]
        public void ExportingAMapThatHasNotBeenGeneratedIsRefused()
        {
            var error = Assert.Throws<InvalidOperationException>(() => MapExport.Export(_map, ExportPath));

            Assert.That(error.Message, Does.Contain("generated"));
        }

        [Test]
        public void ExportingAMapThatIsNotRealisedIsRefused()
        {
            _map.Generate();
            _map.Realizer.Derealize();

            var error = Assert.Throws<InvalidOperationException>(() => MapExport.Export(_map, ExportPath));

            Assert.That(error.Message, Does.Contain("realised"));
        }

        [Test]
        public void ExportingNothingIsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => MapExport.Export(null, ExportPath));

            _map.Generate();
            Assert.Throws<InvalidOperationException>(() => MapExport.Export(_map, "  "));
        }

        // --- helpers --------------------------------------------------------------------------

        static Transform Named(GameObject prefab, string stableId)
        {
            Transform root = prefab.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name == stableId)
                {
                    return root.GetChild(i);
                }
            }

            return null;
        }

        /// <summary>
        /// A baked child instantiated from the catalog row bound to <paramref name="logicalId"/>,
        /// found through the realised instance's name — which is its stable id, and the stable id
        /// is what the document ties to the logical id.
        /// </summary>
        Transform FindByLogicalId(GameObject prefab, string logicalId)
        {
            IReadOnlyList<PlacedObject> objects = _map.Document.Resolve().Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].LogicalId != logicalId)
                {
                    continue;
                }

                Transform found = Named(prefab, objects[i].StableId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
