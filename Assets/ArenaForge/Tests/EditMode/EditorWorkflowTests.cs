using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CorePose = ArenaForge.Core.Pose;
using CoreVec3 = ArenaForge.Core.Vec3;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The loop the tool exists for: generate, edit by hand, change a parameter, regenerate, and
    /// find the hand edits still there.
    /// </summary>
    /// <remarks>
    /// These run the real components — a <see cref="ArenaMap"/> with a real
    /// <see cref="WorldRealizer"/> and real prefab assets — because the parts that can break are the
    /// joins: the document surviving a round trip through Unity's serialisation, an override
    /// finding its target again after the ids were regenerated, and an edit against an id the new
    /// generation no longer produces coming back as an orphan rather than vanishing.
    /// </remarks>
    public sealed class EditorWorkflowTests
    {
        const string TempFolder = "Assets/ArenaForgeEditorTestTemp";
        const string BuildingId = "map/lane_mid/structure_00";

        CatalogAsset _catalog;
        ArenaMap _map;
        ArenaEditCapture _capture;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeEditorTestTemp");
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
                Object.DestroyImmediate(_map.gameObject);
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

        // --- the core loop ------------------------------------------------------------------

        [Test]
        public void AHandEditSurvivesARegenerationOnADifferentSeed()
        {
            GenerateAndWatch();
            CorePose generated = PoseOf(_map, BuildingId);
            string before = GeneratedSignature();
            var moved = CorePose.At(generated.Position + new CoreVec3(3f, 0f, -2f));

            MoveInScene(BuildingId, moved);
            Tick();

            _map.Seed = 987654321UL;
            ResolvedWorld resolved = _map.Regenerate();

            Assert.That(resolved.OrphanedOverrides, Is.Empty);
            Assert.That(Vec3.Distance(PoseOf(_map, BuildingId).Position, moved.Position),
                Is.LessThan(1e-4f), "the edited object stays where it was put");
            Assert.That(GeneratedSignature(), Is.Not.EqualTo(before),
                "everything else moved, so the seed really did change the map");
        }

        [Test]
        public void MovingARealisedObjectRecordsExactlyOneMoveOverride()
        {
            GenerateAndWatch();
            var first = CorePose.At(new CoreVec3(4f, 0f, 4f));
            var second = CorePose.At(new CoreVec3(-4f, 0f, 6f));

            MoveInScene(BuildingId, first);
            Tick();
            MoveInScene(BuildingId, second);
            Tick();

            Assert.That(_map.Document.Overrides.Count, Is.EqualTo(1), "a second nudge upserts the first");
            Assert.That(_map.Document.Overrides[0].Op, Is.EqualTo(OverrideOp.Move));
            Assert.That(Vec3.Distance(PoseOf(_map, BuildingId).Position, second.Position),
                Is.LessThan(1e-4f));
        }

        [Test]
        public void DeletingARealisedObjectRecordsADeleteOverride()
        {
            GenerateAndWatch();
            int before = _map.Realizer.RealizedCount;

            Object.DestroyImmediate(FindInstance(BuildingId).gameObject);
            Tick();

            Assert.That(_map.Document.Overrides.Count, Is.EqualTo(1));
            Assert.That(_map.Document.Overrides[0].Op, Is.EqualTo(OverrideOp.Delete));
            Assert.That(_map.Document.Overrides[0].TargetId, Is.EqualTo(BuildingId));

            _map.Realize();
            Assert.That(_map.Realizer.RealizedCount, Is.EqualTo(before - 1));
        }

        [Test]
        public void AnEditWhoseTargetIsGoneComesBackAsAnOrphan()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_999", CorePose.Identity));
            _map.SetDocument(doc);

            ResolvedWorld resolved = _map.Regenerate();

            Assert.That(resolved.OrphanedOverrides.Count, Is.EqualTo(1));
            Assert.That(resolved.OrphanedOverrides[0].TargetId, Is.EqualTo("map/lane_mid/cover_999"));
            Assert.That(_map.Document.Overrides.Count, Is.EqualTo(1),
                "an orphan is surfaced, never dropped");
        }

        [Test]
        public void GenerateDiscardsEditsAndRegenerateKeepsThem()
        {
            GenerateAndWatch();
            MoveInScene(BuildingId, CorePose.At(new CoreVec3(2f, 0f, 2f)));
            Tick();

            _map.Regenerate();
            Assert.That(_map.Document.Overrides.Count, Is.EqualTo(1));

            _map.Generate();
            Assert.That(_map.Document.Overrides, Is.Empty, "Generate starts from the seed alone");
        }

        // --- document storage ---------------------------------------------------------------

        [Test]
        public void TheDocumentSurvivesTheRoundTripThroughTheComponent()
        {
            _map.Generate();
            string before = ArenaJson.SerializeWorld(_map.Document);

            // What a scene reload does: the object graph is gone, only the serialised string is left.
            _map.SetDocument(ArenaJson.DeserializeWorld(before));

            Assert.That(ArenaJson.SerializeWorld(_map.Document), Is.EqualTo(before));
        }

        [Test]
        public void ClearDropsTheDocumentAndTheRealisedObjects()
        {
            _map.Generate();
            Assert.That(_map.Realizer.RealizedCount, Is.GreaterThan(0));

            _map.Clear();

            Assert.That(_map.HasDocument, Is.False);
            Assert.That(_map.Document, Is.Null);
            Assert.That(_map.Realizer.RealizedCount, Is.Zero);
        }

        [Test]
        public void LoadingAMapBringsItsParametersWithIt()
        {
            _map.Generate();
            WorldDoc saved = _map.Document;

            _map.Seed = 42UL;
            _map.CoverDensity = 0.25f;
            _map.ApplyParams(saved.Parameters);

            Assert.That(_map.Seed, Is.EqualTo(20260816UL));
            Assert.That(_map.CoverDensity, Is.EqualTo(saved.Parameters.CoverDensity));
        }

        // --- override bookkeeping -----------------------------------------------------------

        [Test]
        public void MovingAUserAddedObjectUpdatesItsAddRatherThanStackingAMove()
        {
            var doc = new WorldDoc();
            var placed = CorePose.At(new CoreVec3(1f, 0f, 1f));
            doc.Overrides.Add(EditOverride.Add("user/crate_wood_01_00", "cover/low/crate_wood_01", placed));

            ArenaEditCapture.RecordMove(doc, "user/crate_wood_01_00", CorePose.At(new CoreVec3(5f, 0f, 5f)));

            Assert.That(doc.Overrides.Count, Is.EqualTo(1));
            Assert.That(doc.Overrides[0].Op, Is.EqualTo(OverrideOp.Add));
            Assert.That(doc.Overrides[0].Pose.Value.Position, Is.EqualTo(new CoreVec3(5f, 0f, 5f)));
            Assert.That(doc.Overrides[0].LogicalId, Is.EqualTo("cover/low/crate_wood_01"));
        }

        [Test]
        public void DeletingAUserAddedObjectRemovesItsAddInsteadOfLeavingAPairOfEdits()
        {
            var doc = new WorldDoc();
            doc.Overrides.Add(EditOverride.Add(
                "user/crate_wood_01_00", "cover/low/crate_wood_01", CorePose.Identity));

            ArenaEditCapture.RecordDelete(doc, "user/crate_wood_01_00");

            Assert.That(doc.Overrides, Is.Empty);
        }

        [Test]
        public void DeletingAMovedObjectDropsTheMoveItNoLongerNeeds()
        {
            var doc = new WorldDoc();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/cover_00", "cover/low/crate_wood_01", CorePose.Identity, null, null));
            ArenaEditCapture.RecordMove(doc, "map/lane_mid/cover_00", CorePose.At(new CoreVec3(2f, 0f, 0f)));

            ArenaEditCapture.RecordDelete(doc, "map/lane_mid/cover_00");

            Assert.That(doc.Overrides.Count, Is.EqualTo(1));
            Assert.That(doc.Overrides[0].Op, Is.EqualTo(OverrideOp.Delete));
        }

        [Test]
        public void AUserIdIsReadableAndCannotCollideWithAGeneratedOne()
        {
            var doc = new WorldDoc();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/cover_00", "cover/low/crate_wood_01", CorePose.Identity, null, null));

            string first = ArenaEditCapture.NextUserId(doc, "cover/low/crate_wood_01");
            doc.Overrides.Add(EditOverride.Add(first, "cover/low/crate_wood_01", CorePose.Identity));
            string second = ArenaEditCapture.NextUserId(doc, "cover/low/crate_wood_01");

            Assert.That(first, Is.EqualTo("user/crate_wood_01_00"));
            Assert.That(second, Is.EqualTo("user/crate_wood_01_01"));
            Assert.That(first, Does.StartWith(ArenaEditCapture.UserIdPrefix));
        }

        [Test]
        public void AnAddedObjectResolvesIntoTheMapUnderTheUserNamespace()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            int before = doc.Resolve().Objects.Count;

            string id = ArenaEditCapture.NextUserId(doc, "cover/low/crate_wood_01");
            doc.Overrides.Add(EditOverride.Add(
                id, "cover/low/crate_wood_01", CorePose.At(new CoreVec3(0f, 0f, 0f)),
                new[] { "cover", "cover/low" }));
            _map.SetDocument(doc);
            _map.Realize();

            Assert.That(_map.Document.Resolve().Objects.Count, Is.EqualTo(before + 1));
            Assert.That(FindInstance(id), Is.Not.Null);
        }

        // --- the heatmap ramp ---------------------------------------------------------------

        [Test]
        public void TheRampRunsColdToHotAndLeavesUnwalkableCellsClear()
        {
            Color sheltered = ExposureHeatmap.Sample(0f);
            Color exposed = ExposureHeatmap.Sample(1f);

            Assert.That(sheltered.b, Is.GreaterThan(sheltered.r), "the cold end is blue");
            Assert.That(exposed.r, Is.GreaterThan(exposed.b), "the hot end is red");
            Assert.That(ExposureHeatmap.Sample(float.NaN).a, Is.Zero, "a blocked cell is not painted");
            Assert.That(ExposureHeatmap.Sample(2f), Is.EqualTo(exposed), "values are clamped, not wrapped");
        }

        // --- helpers ------------------------------------------------------------------------

        /// <summary>Generates a map and starts watching it, as opening the window on it would.</summary>
        void GenerateAndWatch()
        {
            _map.Generate();
            _capture = new ArenaEditCapture(_map);
            _capture.Rebuild();
        }

        /// <summary>
        /// Two capture passes: the first sees the object has moved, the second sees it come to rest
        /// and records the edit. That two-step is deliberate — it is what keeps one drag to one
        /// override and one undo step instead of ten a second of both.
        /// </summary>
        void Tick()
        {
            _capture.Tick();
            _capture.Tick();
        }

        /// <summary>What the generator produced, as one comparable string.</summary>
        string GeneratedSignature()
        {
            var text = new System.Text.StringBuilder();
            List<PlacedObject> objects = _map.Document.GeneratedObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                text.Append(objects[i].StableId).Append('@').Append(objects[i].Pose).Append(';');
            }

            return text.ToString();
        }

        void MoveInScene(string stableId, CorePose pose)
        {
            Transform instance = FindInstance(stableId);
            Assert.That(instance, Is.Not.Null, $"'{stableId}' is not realised");

            instance.localPosition = CoreConvert.ToUnity(pose.Position);
            instance.localRotation = CoreConvert.ToUnity(pose.Rotation);
            instance.localScale = Vector3.one * pose.Scale;
        }

        Transform FindInstance(string stableId)
        {
            var refs = _map.Realizer.Root.GetComponentsInChildren<ArenaObjectRef>(true);
            for (int i = 0; i < refs.Length; i++)
            {
                if (refs[i].StableId == stableId)
                {
                    return refs[i].transform;
                }
            }

            return null;
        }

        static CorePose PoseOf(ArenaMap map, string stableId)
        {
            IReadOnlyList<PlacedObject> objects = map.Document.Resolve().Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].StableId == stableId)
                {
                    return objects[i].Pose;
                }
            }

            Assert.Fail($"'{stableId}' is not in the resolved map");
            return CorePose.Identity;
        }
    }
}
