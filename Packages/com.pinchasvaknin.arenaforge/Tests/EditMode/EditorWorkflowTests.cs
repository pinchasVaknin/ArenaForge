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
        TerrainData _ground;

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

            if (_ground != null)
            {
                Object.DestroyImmediate(_ground);
                _ground = null;
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        CatalogAsset.Row Row(
            string logicalId, string[] tags, Vector2 footprint, float height, float baseOffset = 0f)
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
                BaseOffset = baseOffset,
                Weight = 1f,
                Prefab = prefab,
            };
        }

        // --- where a dropped object lands -----------------------------------------------------

        /// <remarks>
        /// <para>
        /// A drop with nothing near it lands on the nearest cell rather than exactly where the mouse
        /// let go. Nudging a crate a few centimetres left and right should not leave it a few
        /// centimetres off the grid every generated object is on — and it is the fallback that
        /// settles the disagreement between the two snaps, since a drop that takes the grid is a
        /// drop the placement rules will accept.
        /// </para>
        /// <para>
        /// Moved well clear of everything else so no edge is in reach: an edge in reach wins, and
        /// that is the case the other snap is for.
        /// </para>
        /// </remarks>
        [Test]
        public void ADropWithNothingNearItLandsOnTheGrid()
        {
            GenerateAndWatch();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaLayout layout = ArenaLayout.Build(_map.BuildParams());
            Vec2 open = layout.Grid.Snap(new Vec2(cover.Pose.Position.X, cover.Pose.Position.Z));

            // A third of a cell off the intersection, which is further than the arithmetic and
            // nearer than the next cell.
            float nudge = layout.Grid.CellSize / 3f;

            MoveInScene(
                cover.StableId,
                CorePose.At(new CoreVec3(open.X + nudge, cover.Pose.Position.Y, open.Y + nudge)));
            Tick();

            CorePose landed = PoseOf(_map, cover.StableId);
            Assert.That(layout.Grid.IsOnGrid(new Vec2(landed.Position.X, landed.Position.Z)), Is.True,
                $"a dropped object came to rest at {landed.Position.X}, {landed.Position.Z}, " +
                "which is not on the grid");
        }

        /// <remarks>
        /// <para>
        /// The other half of a drop. A crate dragged up onto a first-floor slab lands on the slab
        /// rather than at whatever height the mouse happened to let go at, and it is the catalog
        /// that says the slab is a thing to stand on — the tag, not a Unity layer somebody has to
        /// remember to set on every prefab they import.
        /// </para>
        /// <para>
        /// The slab is added to the catalog and stood in the scene <em>after</em> the map is
        /// generated, so it is scenery the drop finds rather than part of what was generated.
        /// </para>
        /// </remarks>
        [Test]
        public void ADroppedObjectComesToRestOnTheFloorUnderIt()
        {
            GenerateAndWatch();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaLayout layout = ArenaLayout.Build(_map.BuildParams());
            Vec2 open = layout.Grid.Snap(new Vec2(cover.Pose.Position.X, cover.Pose.Position.Z));

            CatalogAsset.Row floor = AddRow(
                "structure/floor/slab_01", new[] { BuildingGenerator.FloorTileTag },
                new Vector2(10f, 10f), 0.2f);

            // A unit cube is modelled about its centre, so a slab 0.4 deep standing at 3 has its
            // top face at 3.2.
            Slab(floor, new Vector3(open.X, 3f, open.Y), new Vector3(10f, 0.4f, 10f));

            MoveInScene(cover.StableId, CorePose.At(new CoreVec3(open.X, 3.5f, open.Y)));
            Tick();

            CorePose landed = PoseOf(_map, cover.StableId);
            Assert.That(landed.Position.Y, Is.EqualTo(3.2f).Within(1e-3f),
                "a crate dropped over a slab whose top is at 3.2 came to rest at " +
                landed.Position.Y);
        }

        /// <remarks>
        /// The ground is the other standing surface, and the one that is in every scene. Flat and
        /// lifted off zero, so a crate that came to rest on it can only have got there by being
        /// stood on it rather than by being left where the drag put it.
        /// </remarks>
        [Test]
        public void ADroppedObjectComesToRestOnTheTerrain()
        {
            GenerateAndWatch();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaLayout layout = ArenaLayout.Build(_map.BuildParams());
            Vec2 open = layout.Grid.Snap(new Vec2(cover.Pose.Position.X, cover.Pose.Position.Z));

            FlatTerrainAt(1.5f);

            MoveInScene(cover.StableId, CorePose.At(new CoreVec3(open.X, 4f, open.Y)));
            Tick();

            CorePose landed = PoseOf(_map, cover.StableId);
            Assert.That(landed.Position.Y, Is.EqualTo(1.5f).Within(1e-3f),
                "a crate dropped over ground at 1.5 came to rest at " + landed.Position.Y);
        }

        /// <remarks>
        /// A crate dropped on a table goes through it to the floor, because a table is not
        /// something the catalog says anything stands on. That is the rule working rather than the
        /// rule failing: one tag decides, and everything the tag is not is scenery to fall past.
        /// </remarks>
        [Test]
        public void ADroppedObjectFallsPastWhatIsNotAStandingSurface()
        {
            GenerateAndWatch();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaLayout layout = ArenaLayout.Build(_map.BuildParams());
            Vec2 open = layout.Grid.Snap(new Vec2(cover.Pose.Position.X, cover.Pose.Position.Z));

            CatalogAsset.Row floor = AddRow(
                "structure/floor/slab_01", new[] { BuildingGenerator.FloorTileTag },
                new Vector2(10f, 10f), 0.2f);

            Slab(floor, new Vector3(open.X, 3f, open.Y), new Vector3(10f, 0.4f, 10f));

            // The same shape in the same place two metres higher, tagged as cover — which is the
            // whole of the difference between the two.
            CatalogAsset.Row table = AddRow(
                "cover/high/table_01", new[] { "cover", "cover/high" }, new Vector2(10f, 10f), 0.2f);

            Slab(table, new Vector3(open.X, 5f, open.Y), new Vector3(10f, 0.4f, 10f));

            MoveInScene(cover.StableId, CorePose.At(new CoreVec3(open.X, 6f, open.Y)));
            Tick();

            CorePose landed = PoseOf(_map, cover.StableId);
            Assert.That(landed.Position.Y, Is.EqualTo(3.2f).Within(1e-3f),
                "a crate dropped over a table at 5.2 with a slab at 3.2 under it came to rest at " +
                landed.Position.Y);
        }

        /// <remarks>
        /// <para>
        /// Flush on the ground plane says nothing about how high two pieces stand, and a wall lined
        /// up with the wall beside it but a step above it is not lined up with anything. What has to
        /// match is the <em>undersides</em>, which is the pivot less the art's own
        /// <see cref="CatalogEntry.BaseOffset"/> — so the two pieces here are modelled differently
        /// on purpose: one on its base and one around its centre, half a metre up.
        /// </para>
        /// <para>
        /// The neighbour is stood five metres in the air, well clear of the ground the drop would
        /// otherwise fall to, so a pass that came out level with it cannot have got there by
        /// standing on anything.
        /// </para>
        /// </remarks>
        [Test]
        public void ADroppedObjectLevelsItsUndersideWithWhatItLinedUpAgainst()
        {
            GenerateAndWatch();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaLayout layout = ArenaLayout.Build(_map.BuildParams());
            Vec2 open = layout.Grid.Snap(new Vec2(cover.Pose.Position.X, cover.Pose.Position.Z));

            // Modelled around its centre, so its pivot stands half a metre over its underside.
            const float Offset = 0.5f;
            const float Pivot = 5f;

            CatalogAsset.Row plinth = AddRow(
                "cover/high/plinth_01", new[] { "cover", "cover/high" },
                new Vector2(2f, 2f), 1f, Offset);

            _capture.RecordAdd(
                plinth.LogicalId,
                CorePose.At(new CoreVec3(open.X, Pivot, open.Y)),
                plinth.Tags);

            // Just off flush against the plinth's high-X face, and a long way below it.
            MoveInScene(
                cover.StableId,
                CorePose.At(new CoreVec3(open.X + 1.7f, 0f, open.Y)));

            Tick();

            CorePose landed = PoseOf(_map, cover.StableId);

            // The crate is modelled on its own base, so its pivot is its underside.
            Assert.That(landed.Position.Y, Is.EqualTo(Pivot - Offset).Within(1e-3f),
                $"a crate lined up against a plinth whose underside is at {Pivot - Offset} came to " +
                $"rest at {landed.Position.Y}");
        }

        /// <remarks>
        /// The drop, with the editor's own notification taken out of it: an instance of catalog art
        /// standing at the scene root is exactly what dragging a prefab in from the Project window
        /// leaves behind, and this asks whether capture makes a <c>user/</c> object of it. Split from
        /// the event that delivers it on purpose — when the feature failed in the editor, the whole
        /// question was which of the two halves was broken, and a test that exercised both at once
        /// could not say.
        /// </remarks>
        [Test]
        public void APrefabStandingAtTheSceneRootIsAdoptedAsAUserObject()
        {
            GenerateAndWatch();

            CatalogAsset.Row crate = Crate();

            var dropped = (GameObject)PrefabUtility.InstantiatePrefab(crate.Prefab);
            dropped.transform.position = new Vector3(3.3f, 0f, 4.7f);

            _capture.Adopt(dropped);

            var added = new List<EditOverride>();
            foreach (EditOverride edit in _map.Document.Overrides)
            {
                if (edit.Op == OverrideOp.Add)
                {
                    added.Add(edit);
                }
            }

            Assert.That(added.Count, Is.EqualTo(1),
                "a dropped prefab the catalog knows should become exactly one Add");
            Assert.That(added[0].TargetId, Does.StartWith(ArenaEditCapture.UserIdPrefix));
            Assert.That(added[0].LogicalId, Is.EqualTo(crate.LogicalId));
            Assert.That(dropped == null, Is.True,
                "the dropped instance should be gone, replaced by the realiser's own");
        }

        /// <remarks>
        /// <para>
        /// Not a property of the tool but a fact about the editor, and the reason
        /// <see cref="ArenaDropWatch"/> compares the scene against the last pass instead of
        /// subscribing to <see cref="ObjectChangeEvents"/>. A headless run publishes <em>no change
        /// at all</em> for a prefab instantiation, so a listener cannot be covered by this suite —
        /// which is how the first version of the drop shipped broken and stayed broken while every
        /// test round it was green.
        /// </para>
        /// <para>
        /// <strong>Asserting the absence is the point.</strong> If a later editor starts publishing
        /// here, this fails, and what it will be saying is that the event route has become testable
        /// and is worth reconsidering. A comment could not do that.
        /// </para>
        /// </remarks>
        [Test]
        public void TheEditorPublishesNoChangeForAPrefabInstantiation()
        {
            var kinds = new List<ObjectChangeKind>();
            var seen = new List<int>();

            void OnChanges(ref ObjectChangeEventStream stream)
            {
                for (int i = 0; i < stream.length; i++)
                {
                    kinds.Add(stream.GetEventType(i));

                    if (stream.GetEventType(i) == ObjectChangeKind.CreateGameObjectHierarchy)
                    {
                        stream.GetCreateGameObjectHierarchyEvent(
                            i, out CreateGameObjectHierarchyEventArgs created);

                        seen.Add(created.instanceId);
                    }
                }
            }

            ObjectChangeEvents.changesPublished += OnChanges;
            GameObject dropped = null;

            try
            {
                dropped = (GameObject)PrefabUtility.InstantiatePrefab(_catalog.Rows[3].Prefab);

                // The stream is published on the editor's own loop rather than synchronously, so
                // the test has to let one go round before asking what arrived.
                for (int i = 0; i < 10 && seen.Count == 0; i++)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                    System.Threading.Thread.Sleep(20);
                }
            }
            finally
            {
                ObjectChangeEvents.changesPublished -= OnChanges;
                if (dropped != null)
                {
                    Object.DestroyImmediate(dropped);
                }
            }

            Assert.That(kinds, Is.Empty,
                $"the editor published {string.Join(", ", kinds)} for a prefab instantiation, so " +
                "the event route is testable after all and ArenaDropWatch could use it");

            Assert.That(seen, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// The half that was broken, end to end and without the editor's notification in it: a
        /// prefab appears at the scene root, sits still for one pass, and is taken into the document
        /// as a <c>user/</c> object.
        /// </para>
        /// <para>
        /// <strong>Three passes, and each one is a rule.</strong> The first files what is already
        /// there, so scenery in an opened scene is never swept up. The second sees something new and
        /// notes where it is rather than taking it, because a prefab dragged from the Project window
        /// is carried under the cursor and taking the first thing seen would take it out of
        /// somebody's hand. The third finds it has not moved and adopts it.
        /// </para>
        /// </remarks>
        [Test]
        public void APrefabThatAppearsAndStopsMovingIsAdopted()
        {
            GenerateAndWatch();

            // Begun before the drop, so the crate below is something that appeared afterwards.
            ArenaDropWatch.Restart();
            ArenaDropWatch.Pass();

            var dropped = (GameObject)PrefabUtility.InstantiatePrefab(Crate().Prefab);
            dropped.transform.position = new Vector3(6.4f, 0f, 2.2f);

            ArenaDropWatch.Pass();

            Assert.That(AddedObjects().Count, Is.EqualTo(0),
                "a prefab still under the cursor should be waited on, not taken");
            Assert.That(dropped == null, Is.False);

            ArenaDropWatch.Pass();

            List<EditOverride> added = AddedObjects();
            Assert.That(added.Count, Is.EqualTo(1),
                "a prefab that stopped moving should have been adopted");
            Assert.That(added[0].TargetId, Does.StartWith(ArenaEditCapture.UserIdPrefix));
            Assert.That(added[0].LogicalId, Is.EqualTo(Crate().LogicalId));
        }

        /// <remarks>
        /// <para>
        /// The rule that keeps an opened scene's own furniture out of the document: the first pass
        /// after a domain reload files what it finds and takes none of it, so art already standing
        /// when the watch began stays scenery however long it sits there.
        /// </para>
        /// <para>
        /// <strong>It has to restart the watch to ask this.</strong> The statics survive between
        /// tests, so without it the crate below is new since the previous test's last pass — which
        /// it is, and which the watch is right to adopt. Only a first pass can be asked what a first
        /// pass does.
        /// </para>
        /// </remarks>
        [Test]
        public void APrefabAlreadyStandingWhenTheWatchStartedIsLeftAlone()
        {
            GenerateAndWatch();

            var standing = (GameObject)PrefabUtility.InstantiatePrefab(Crate().Prefab);
            standing.transform.position = new Vector3(9.1f, 0f, 7.3f);

            try
            {
                // Standing before the watch begins, which is what a scene opened from disk looks
                // like. The first pass files it, and it never moves after that.
                ArenaDropWatch.Restart();
                ArenaDropWatch.Pass();
                ArenaDropWatch.Pass();
                ArenaDropWatch.Pass();

                Assert.That(AddedObjects().Count, Is.EqualTo(0),
                    "scenery that was already in the scene should stay scenery");
                Assert.That(standing == null, Is.False);
            }
            finally
            {
                if (standing != null)
                {
                    Object.DestroyImmediate(standing);
                }
            }
        }

        // --- swapping the art under an object -------------------------------------------------

        /// <remarks>
        /// The op has round-tripped since the document model was written and until now nothing
        /// produced one, so these are the first tests of what writing one means rather than of
        /// what resolving one does — <see cref="ResolveTests"/> has the latter.
        /// </remarks>
        [Test]
        public void SwappingAnObjectWritesOneSwapOverride()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            string other = OtherCover(cover);
            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, other);

            Assert.That(doc.Overrides.Count, Is.EqualTo(1));
            Assert.That(doc.Overrides[0].Op, Is.EqualTo(OverrideOp.SwapAsset));
            Assert.That(doc.Overrides[0].TargetId, Is.EqualTo(cover.StableId));
            Assert.That(doc.Overrides[0].LogicalId, Is.EqualTo(other));

            _map.SetDocument(doc);
            ResolvedWorld resolved = _map.Realize();

            foreach (PlacedObject placed in resolved.Objects)
            {
                if (placed.StableId == cover.StableId)
                {
                    Assert.That(placed.LogicalId, Is.EqualTo(other));
                    Assert.That(placed.Pose.Position, Is.EqualTo(cover.Pose.Position),
                        "a swap carries an entry, not a pose");
                    return;
                }
            }

            Assert.Fail($"{cover.StableId} is not in the resolved map");
        }

        /// <remarks>
        /// Two swaps of one object are one decision changed twice, not two edits. An override list
        /// that grew an entry per change would make its own count stop meaning anything.
        /// </remarks>
        [Test]
        public void SwappingTwiceLeavesOneOverrideRatherThanTwo()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, OtherCover(cover));
            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, "structure/house/small_01");

            Assert.That(doc.Overrides.Count, Is.EqualTo(1));
            Assert.That(doc.Overrides[0].LogicalId, Is.EqualTo("structure/house/small_01"));
        }

        /// <remarks>
        /// Choosing the entry the generator picked is undoing the swap, not making another one. A
        /// no-op override left behind would show in the count as an edit the user did not make and
        /// would survive a regeneration as one.
        /// </remarks>
        [Test]
        public void SwappingBackToTheGeneratedEntryRemovesTheOverride()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, OtherCover(cover));
            Assert.That(doc.Overrides.Count, Is.EqualTo(1));

            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, cover.LogicalId);

            Assert.That(doc.Overrides, Is.Empty);
        }

        /// <remarks>
        /// A swap and a move are different edits of one object and both survive: the pose comes from
        /// the move, the art from the swap. Nothing in the resolution says so on its own — the two
        /// ops are applied in list order — so it is worth stating.
        /// </remarks>
        [Test]
        public void ASwapAndAMoveOnOneObjectBothHold()
        {
            _map.Generate();
            WorldDoc doc = _map.Document;
            PlacedObject cover = FirstCover(doc);

            var moved = new CorePose(
                cover.Pose.Position + new CoreVec3(2f, 0f, 0f),
                cover.Pose.Rotation,
                cover.Pose.Scale);

            string other = OtherCover(cover);
            doc.Overrides.Add(EditOverride.Move(cover.StableId, moved));
            ArenaForgeOverlay.ApplySwap(doc, cover.StableId, other);

            Assert.That(doc.Overrides.Count, Is.EqualTo(2));

            _map.SetDocument(doc);
            ResolvedWorld resolved = _map.Realize();

            foreach (PlacedObject placed in resolved.Objects)
            {
                if (placed.StableId == cover.StableId)
                {
                    Assert.That(placed.LogicalId, Is.EqualTo(other));
                    Assert.That(placed.Pose.Position, Is.EqualTo(moved.Position));
                    return;
                }
            }

            Assert.Fail($"{cover.StableId} is not in the resolved map");
        }

        /// <summary>The cover entry in this catalog that is not the one an object stands as.</summary>
        /// <remarks>
        /// Chosen against the object rather than named outright. Which of the two the generator
        /// picks for the first piece of cover is a fact about the seed, and a test that named one
        /// would be asserting the seed as much as the swap — and would turn into a no-op the day
        /// the pick changed, which is what the first version of these tests did.
        /// </remarks>
        static string OtherCover(PlacedObject cover) =>
            cover.LogicalId == "cover/low/crate_wood_01"
                ? "cover/high/barrier_concrete_01"
                : "cover/low/crate_wood_01";

        /// <summary>
        /// Adds a row to the catalog after the map was generated, so nothing was generated off it.
        /// </summary>
        CatalogAsset.Row AddRow(
            string logicalId, string[] tags, Vector2 footprint, float height, float baseOffset = 0f)
        {
            CatalogAsset.Row row = Row(logicalId, tags, footprint, height, baseOffset);
            var rows = new List<CatalogAsset.Row>(_catalog.Rows) { row };
            _catalog.SetRows(rows);
            return row;
        }

        /// <summary>
        /// Stands a row's prefab in the scene as a slab of the given size, outside the document.
        /// </summary>
        /// <remarks>
        /// Parented to the map's own host rather than to the realisation root, so capture never
        /// takes it for an instance it is watching. It is scenery a drop lands on, which is what a
        /// floor slab standing beside the map is.
        /// </remarks>
        Transform Slab(CatalogAsset.Row row, Vector3 at, Vector3 size)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(row.Prefab, _map.transform);
            instance.transform.localPosition = at;
            instance.transform.localScale = size;
            return instance.transform;
        }

        /// <summary>Puts flat ground across the map at a height, and keeps it for teardown.</summary>
        void FlatTerrainAt(float height)
        {
            _ground = new TerrainData { heightmapResolution = 33 };
            _ground.size = new Vector3(128f, 8f, 128f);

            GameObject terrain = Terrain.CreateTerrainGameObject(_ground);
            terrain.transform.SetParent(_map.transform, false);
            terrain.transform.localPosition = new Vector3(-64f, height, -64f);
        }

        static PlacedObject FirstCover(WorldDoc doc)
        {
            foreach (PlacedObject placed in doc.GeneratedObjects)
            {
                if (placed.StableId.Contains("/cover_") && !CoverPlacer.IsSocketProp(placed.StableId))
                {
                    return placed;
                }
            }

            Assert.Fail("the generated map has no cover in it");
            return null;
        }

        // --- what a drag shows before it is a drop ---------------------------------------------

        /// <remarks>
        /// <para>
        /// The property the live drag guides rest on, and the only one worth having: what is drawn
        /// under the cursor is the pose the drop will actually record. A preview that showed the raw
        /// mouse position would be worse than none — it would promise a placement the snap is about
        /// to move, whether the snap comes from an edge in reach or from the grid.
        /// </para>
        /// <para>
        /// Asserted by dropping the same art at the same point and comparing, rather than against
        /// the grid: which of the two snaps applies depends on what the seed happened to place
        /// nearby, and the claim is not about the grid. It is that the two answers are one answer.
        /// </para>
        /// </remarks>
        [Test]
        public void ADragPreviewShowsThePoseTheDropWillRecord()
        {
            GenerateAndWatch();

            CatalogAsset.Row crate = Crate();
            var held = new CoreVec3(3.3f, 0f, 4.7f);

            PlacedObject preview = ArenaDragGuides.Pending(_map, crate.LogicalId, held);

            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.LogicalId, Is.EqualTo(crate.LogicalId));

            var dropped = (GameObject)PrefabUtility.InstantiatePrefab(crate.Prefab);
            dropped.transform.position =
                _map.Realizer.Root.TransformPoint(CoreConvert.ToUnity(held));

            _capture.Adopt(dropped);

            CorePose? recorded = null;
            foreach (EditOverride edit in _map.Document.Overrides)
            {
                if (edit.Op == OverrideOp.Add)
                {
                    recorded = edit.Pose;
                }
            }

            Assert.That(recorded.HasValue, Is.True, "the drop recorded no Add to compare against");

            Assert.That(recorded.Value.Position.X,
                Is.EqualTo(preview.Pose.Position.X).Within(1e-4f),
                "the preview promised a place the drop did not use");
            Assert.That(recorded.Value.Position.Y,
                Is.EqualTo(preview.Pose.Position.Y).Within(1e-4f));
            Assert.That(recorded.Value.Position.Z,
                Is.EqualTo(preview.Pose.Position.Z).Within(1e-4f));
        }

        /// <remarks>
        /// The id has to be one nothing in a document can be called, or the drag would hide a real
        /// object from its own verdict: <c>CoverPlacer.TryJudge</c> leaves the subject out of the
        /// committed set by id, so a preview borrowing a generated id would judge that object
        /// against a map it had been deleted from.
        /// </remarks>
        [Test]
        public void ADragPreviewIsNamedOutsideEveryGeneratedId()
        {
            GenerateAndWatch();

            PlacedObject preview = ArenaDragGuides.Pending(
                _map, "cover/low/crate_wood_01", new CoreVec3(0f, 0f, 0f));

            Assert.That(preview.StableId, Does.StartWith(ArenaEditCapture.UserIdPrefix));

            foreach (PlacedObject placed in _map.Document.GeneratedObjects)
            {
                Assert.That(placed.StableId, Is.Not.EqualTo(preview.StableId));
            }
        }

        /// <remarks>
        /// Art the catalog has never heard of draws nothing rather than drawing a verdict about a
        /// footprint it had to guess. Dragging a prefab from anywhere in the Project window is an
        /// ordinary thing to do, and most of what is there is not this map's art.
        /// </remarks>
        [Test]
        public void ADragOfArtOutsideTheCatalogPreviewsNothing()
        {
            GenerateAndWatch();

            Assert.That(
                ArenaDragGuides.Pending(_map, "cover/low/nothing_like_this", CoreVec3.Zero),
                Is.Null);

            Assert.That(ArenaDragGuides.Pending(_map, null, CoreVec3.Zero), Is.Null);
        }

        /// <remarks>
        /// The two ways this drag starts, which are the same gesture to the person making it: the
        /// tool window's catalog panel carries a logical id outright, and the Project window carries
        /// the prefab, which the catalog asset binds to a row. Anything else is a drag that has
        /// nothing to do with a map.
        /// </remarks>
        [Test]
        public void ADraggedPrefabIsResolvedToItsCatalogRow()
        {
            GameObject prefab = _catalog.Rows[3].Prefab;

            Assert.That(
                ArenaDragGuides.LogicalIdOf(_map, new Object[] { prefab }, null),
                Is.EqualTo("cover/low/crate_wood_01"));

            Assert.That(
                ArenaDragGuides.LogicalIdOf(_map, null, "cover/high/barrier_concrete_01"),
                Is.EqualTo("cover/high/barrier_concrete_01"),
                "the window's own drag carries the id rather than the prefab");

            Assert.That(
                ArenaDragGuides.LogicalIdOf(_map, new Object[] { _catalog }, null), Is.Null,
                "a drag of something that is not this map's art previews nothing");
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

        // --- whole-map operations -------------------------------------------------------------

        [Test]
        public void AWholeMapOperationCollapsesIntoOneNamedUndoStep()
        {
            MapOperationResult result = MapOperations.Run(_map, "generate map", m => m.Generate());

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(Undo.GetCurrentGroupName(), Is.EqualTo("ArenaForge: generate map"),
                "the undo entry is named for what it did, not 'Paste Values'");

            Undo.PerformUndo();

            Assert.That(_map.HasDocument, Is.False, "one undo walks the whole generation back");
            Assert.That(_map.Realizer.RealizedCount, Is.Zero, "and the GameObjects with it");
        }

        [Test]
        public void AFailedOperationLeavesTheDocumentAndTheUndoStackWhereItFoundThem()
        {
            _map.Generate();
            string before = ArenaJson.SerializeWorld(_map.Document);
            int group = Undo.GetCurrentGroup();

            MapOperationResult result = MapOperations.Run(_map, "regenerate map", m =>
            {
                // Half an operation: the document is gone and the scene is empty by the time this
                // throws, which is the state the runner has to be able to walk back out of.
                m.Clear();
                throw new System.InvalidOperationException("no catalog assigned");
            });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.EqualTo("no catalog assigned"));
            Assert.That(ArenaJson.SerializeWorld(_map.Document), Is.EqualTo(before),
                "a half-run operation is reverted, not left in the document");
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(group),
                "and it leaves no undo step behind for the user to step through");
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

        /// <summary>The crate row, which is the art these tests drop.</summary>
        CatalogAsset.Row Crate()
        {
            for (int i = 0; i < _catalog.Rows.Count; i++)
            {
                if (_catalog.Rows[i].LogicalId == "cover/low/crate_wood_01")
                {
                    return _catalog.Rows[i];
                }
            }

            Assert.Fail("the test catalog has no crate to drop");
            return null;
        }

        /// <summary>The Add overrides the document is carrying.</summary>
        List<EditOverride> AddedObjects()
        {
            var added = new List<EditOverride>();
            foreach (EditOverride edit in _map.Document.Overrides)
            {
                if (edit.Op == OverrideOp.Add)
                {
                    added.Add(edit);
                }
            }

            return added;
        }

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
