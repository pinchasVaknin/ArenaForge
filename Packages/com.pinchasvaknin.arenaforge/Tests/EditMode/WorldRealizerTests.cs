using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using CorePose = ArenaForge.Core.Pose;
using CoreVec2 = ArenaForge.Core.Vec2;
using CoreVec3 = ArenaForge.Core.Vec3;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The Unity adapter's half of the round trip: a document in, GameObjects out, and back to
    /// nothing again.
    /// </summary>
    /// <remarks>
    /// The prefabs are written into a temporary folder under Assets rather than faked with loose
    /// GameObjects, because the behaviour worth testing — that an editor-time instance keeps its
    /// prefab link — only exists for a real prefab asset.
    /// </remarks>
    public sealed class WorldRealizerTests
    {
        const string TempFolder = "Assets/ArenaForgeTestTemp";

        CatalogAsset _catalog;
        WorldRealizer _realizer;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeTestTemp");
            }

            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
            var rows = new List<CatalogAsset.Row>
            {
                Row("marker/spawn", new[] { "marker", "spawn" }, new Vector2(2f, 2f), 0.5f),
                Row("structure/building/two_storey_01", new[] { "structure", "structure/building" },
                    new Vector2(12f, 10f), 6f),
                Row("structure/house/small_01", new[] { "structure", "structure/house" },
                    new Vector2(8f, 6f), 3.5f),
            };

            _catalog.SetRows(rows);

            var host = new GameObject("ArenaForge Test Map");
            _realizer = host.AddComponent<WorldRealizer>();
            _realizer.Catalog = _catalog;
        }

        [TearDown]
        public void TearDown()
        {
            if (_realizer != null)
            {
                Object.DestroyImmediate(_realizer.gameObject);
            }

            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        CatalogAsset.Row Row(string logicalId, string[] tags, Vector2 footprint, float height)
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            string path = $"{TempFolder}/{logicalId.Replace('/', '_')}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);

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

        static WorldDoc TwoMarkers()
        {
            var doc = new WorldDoc { Parameters = new ArenaParams { Seed = 1UL } };
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_a/marker", "marker/spawn", CorePose.At(new CoreVec3(0f, 0f, -26f)), null, null));
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_b/marker",
                "marker/spawn",
                new CorePose(new CoreVec3(0f, 0f, 26f), QuarterTurn.Rotation(2), 1f),
                null,
                null));
            return doc;
        }

        [Test]
        public void RealizeInstantiatesOneObjectPerResolvedObject()
        {
            _realizer.Realize(TwoMarkers());

            Assert.That(_realizer.RealizedCount, Is.EqualTo(2));
            Assert.That(_realizer.Root.GetChild(0).name, Is.EqualTo("map/spawn_a/marker"));
            Assert.That(_realizer.Root.GetChild(1).GetComponent<ArenaObjectRef>().StableId,
                Is.EqualTo("map/spawn_b/marker"));
        }

        [Test]
        public void RealisedInstancesCarryTheDocumentPose()
        {
            _realizer.Realize(TwoMarkers());

            Transform b = _realizer.Root.GetChild(1);

            Assert.That(b.localPosition, Is.EqualTo(new Vector3(0f, 0f, 26f)));
            Assert.That(Quaternion.Angle(b.localRotation, Quaternion.Euler(0f, 180f, 0f)),
                Is.LessThan(0.01f));
            Assert.That(b.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void RealisedInstancesKeepTheirPrefabLink()
        {
            _realizer.Realize(TwoMarkers());

            GameObject instance = _realizer.Root.GetChild(0).gameObject;

            Assert.That(PrefabUtility.IsPartOfPrefabInstance(instance), Is.True,
                "an editor-time instance must stay linked to its prefab");
        }

        [Test]
        public void DerealizeLeavesNoChildrenUnderTheRoot()
        {
            _realizer.Realize(TwoMarkers());

            _realizer.Derealize();

            Assert.That(_realizer.RealizedCount, Is.Zero);
            Assert.That(_realizer.Root.childCount, Is.Zero);
        }

        [Test]
        public void RealizeAndDerealizeAreIdempotent()
        {
            WorldDoc doc = TwoMarkers();

            _realizer.Realize(doc);
            _realizer.Realize(doc);
            _realizer.Realize(doc);

            Assert.That(_realizer.RealizedCount, Is.EqualTo(2), "realising again must not stack instances");

            _realizer.Derealize();
            _realizer.Derealize();

            Assert.That(_realizer.RealizedCount, Is.Zero);
        }

        [Test]
        public void OverridesAreAppliedBeforeAnythingIsInstantiated()
        {
            WorldDoc doc = TwoMarkers();
            doc.Overrides.Add(EditOverride.Delete("map/spawn_a/marker"));
            doc.Overrides.Add(EditOverride.Move("map/spawn_b/marker", CorePose.At(new CoreVec3(1f, 0f, 2f))));
            doc.Overrides.Add(EditOverride.Add(
                "user/marker_00", "marker/spawn", CorePose.At(new CoreVec3(3f, 0f, 4f))));

            _realizer.Realize(doc);

            Assert.That(_realizer.RealizedCount, Is.EqualTo(2));
            Assert.That(_realizer.Root.GetChild(0).name, Is.EqualTo("map/spawn_b/marker"));
            Assert.That(_realizer.Root.GetChild(0).localPosition, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(_realizer.Root.GetChild(1).name, Is.EqualTo("user/marker_00"));
        }

        [Test]
        public void AnOrphanedOverrideComesBackFromRealize()
        {
            WorldDoc doc = TwoMarkers();
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_09", CorePose.Identity));

            ResolvedWorld resolved = _realizer.Realize(doc);

            Assert.That(resolved.OrphanedOverrides.Count, Is.EqualTo(1));
            Assert.That(resolved.OrphanedOverrides[0].TargetId, Is.EqualTo("map/lane_mid/cover_09"));
            Assert.That(_realizer.RealizedCount, Is.EqualTo(2));
        }

        [Test]
        public void AnObjectWithNoPrefabIsSkippedWithAWarning()
        {
            WorldDoc doc = TwoMarkers();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/mystery", "cover/low/unbound", CorePose.Identity, null, null));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("cover/low/unbound"));
            _realizer.Realize(doc);

            Assert.That(_realizer.RealizedCount, Is.EqualTo(2));
        }

        [Test]
        public void AGeneratedMapRealisesIntoTheScene()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 20260816UL }, _catalog.ToCatalog());

            _realizer.Realize(doc);

            Assert.That(
                _realizer.RealizedCount,
                Is.EqualTo(2 + PlacedGeometry.Structures(doc).Count),
                "both spawn markers and every structure the composition rule asks for");
            Assert.That(_realizer.Root.GetChild(0).GetComponent<ArenaObjectRef>().StableId,
                Is.EqualTo("map/spawn_a/marker"));
        }

        [Test]
        public void TheCatalogAssetExportsTheEntriesCoreNeeds()
        {
            Catalog catalog = _catalog.ToCatalog();

            Assert.That(catalog.Entries.Count, Is.EqualTo(3));
            Assert.That(catalog.Entries[0].LogicalId, Is.EqualTo("marker/spawn"), "entries come out sorted");

            CatalogEntry building = catalog.Entries[1];
            Assert.That(building.LogicalId, Is.EqualTo("structure/building/two_storey_01"));
            Assert.That(building.Footprint, Is.EqualTo(new Rect2(-6f, -5f, 6f, 5f)));
            Assert.That(building.Height, Is.EqualTo(6f));

            // The exported JSON is the only form Core-only tooling ever sees, so it has to be
            // readable back with no Unity types involved.
            Catalog roundTripped = ArenaJson.DeserializeCatalog(ArenaJson.SerializeCatalog(catalog));
            Assert.That(roundTripped.Entries.Count, Is.EqualTo(3));
        }

        [Test]
        public void ACatalogRowWithNoLogicalIdIsRejectedByName()
        {
            var broken = ScriptableObject.CreateInstance<CatalogAsset>();
            try
            {
                broken.SetRows(new[] { new CatalogAsset.Row { LogicalId = " " } });

                var error = Assert.Throws<System.InvalidOperationException>(() => broken.ToCatalog());
                Assert.That(error.Message, Does.Contain("row 0"));
            }
            finally
            {
                Object.DestroyImmediate(broken);
            }
        }

        [Test]
        public void RealizingWithNoCatalogWarnsInsteadOfThrowing()
        {
            _realizer.Catalog = null;

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no catalog"));
            ResolvedWorld resolved = _realizer.Realize(TwoMarkers());

            Assert.That(resolved.Objects.Count, Is.EqualTo(2));
            Assert.That(_realizer.RealizedCount, Is.Zero);
        }
    }
}
