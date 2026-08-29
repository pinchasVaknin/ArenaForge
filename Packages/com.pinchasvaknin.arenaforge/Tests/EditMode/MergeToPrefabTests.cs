using System.Collections.Generic;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The Merge to Prefab tool: a group of objects arranged in a scene becomes one prefab and one
    /// catalog row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is worth asserting is that the row it writes is the row a sync would have written. The
    /// tool exists to save four steps, not to introduce a fifth kind of thing — so if the tags, the
    /// logical id or the measurement came out differently here from the way
    /// <see cref="CatalogSync"/> spells them, a merged prefab would work until somebody pressed
    /// Sync from Folders and then quietly change under them.
    /// </para>
    /// <para>
    /// The objects are made in the open scene rather than in one loaded for the purpose: the tool
    /// reparents scene objects, and a prefab asset cannot be reparented — which is a case the tests
    /// below cover rather than avoid.
    /// </para>
    /// </remarks>
    public sealed class MergeToPrefabTests
    {
        const string Root = "Assets/ArenaForgeMergeTests";
        const string Folder = Root + "/Props/PropBuilding/Decor/Centerpieces";

        CatalogAsset _catalog;
        readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void CreateWorkspace()
        {
            Delete();
            AssetDatabase.Refresh();
            AssetDatabase.CreateFolder("Assets", "ArenaForgeMergeTests");

            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
            _catalog.SourceFolder = Root;
        }

        [TearDown]
        public void RemoveWorkspace()
        {
            // The topmost ancestor, because a merge parents what it was given under a new object
            // that no test made and every test leaves standing in the open scene.
            for (int i = 0; i < _made.Count; i++)
            {
                if (_made[i] == null)
                {
                    continue;
                }

                Transform at = _made[i].transform;
                while (at.parent != null)
                {
                    at = at.parent;
                }

                Object.DestroyImmediate(at.gameObject);
            }

            _made.Clear();

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

        // --- what comes out of a merge -----------------------------------------------------------

        /// <remarks>
        /// The whole of the workflow in one assertion: three objects go in, one prefab comes out
        /// with all three inside it, and the catalog has a row bound to that prefab.
        /// </remarks>
        [Test]
        public void MergingAGroupWritesOnePrefabWithAllOfItInside()
        {
            List<GameObject> group = Group();

            GameObject prefab = MergeToPrefab.Merge(group, Folder, "Desk Setup", _catalog);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(Folder + "/Desk Setup.prefab"));
            Assert.That(prefab.transform.childCount, Is.EqualTo(group.Count));
            Assert.That(_catalog.Rows, Has.Count.EqualTo(1));
            Assert.That(_catalog.Rows[0].Prefab, Is.EqualTo(prefab));
        }

        /// <remarks>
        /// The pivot is the average of what the person placed, which is the middle of what they were
        /// looking at. The centre of the box round the group would drift towards whichever piece is
        /// largest, and the first object's own pivot would put the group's origin inside the desk.
        /// </remarks>
        [Test]
        public void TheNewParentStandsAtTheAverageOfTheSelectionsPivots()
        {
            var group = new List<GameObject>
            {
                At("A", new Vector3(0f, 0f, 0f)),
                At("B", new Vector3(4f, 0f, 0f)),
                At("C", new Vector3(2f, 0f, 6f)),
            };

            MergeToPrefab.Merge(group, Folder, "Group", _catalog);

            Assert.That(group[0].transform.parent, Is.Not.Null);
            Vector3 pivot = group[0].transform.parent.position;

            Assert.That(pivot.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(pivot.z, Is.EqualTo(2f).Within(1e-3f));
        }

        /// <remarks>
        /// Reparenting keeps world positions, which is what makes the merge a merge rather than a
        /// rearrangement. A group that jumped when it was merged would be a tool that destroyed the
        /// arrangement it was asked to preserve.
        /// </remarks>
        [Test]
        public void NothingInTheGroupMovesWhenItIsMerged()
        {
            List<GameObject> group = Group();
            var before = new List<Vector3>();
            for (int i = 0; i < group.Count; i++)
            {
                before.Add(group[i].transform.position);
            }

            MergeToPrefab.Merge(group, Folder, "Group", _catalog);

            for (int i = 0; i < group.Count; i++)
            {
                Assert.That(Vector3.Distance(group[i].transform.position, before[i]),
                    Is.LessThan(1e-3f), group[i].name);
            }
        }

        /// <remarks>
        /// The row is the one a sync of the same folder would have written: the tags the folder
        /// chain spells, the logical id that tag path plus the file's own slug, and the size read
        /// off the art. That is the property the tool rests on — pressing Sync from Folders
        /// afterwards has to find the prefab and change nothing.
        /// </remarks>
        [Test]
        public void TheRowIsTheOneASyncOfTheSameFolderWouldHaveWritten()
        {
            MergeToPrefab.Merge(Group(), Folder, "Desk Setup", _catalog);
            CatalogAsset.Row merged = _catalog.Rows[0];

            Assert.That(merged.LogicalId,
                Is.EqualTo("propbuilding/decor/centerpieces/desk_setup"));
            Assert.That(merged.Tags, Does.Contain("propbuilding/decor/centerpieces"));
            Assert.That(merged.Weight, Is.EqualTo(1f));

            List<CatalogAsset.Row> scanned = CatalogSync.Scan(Root);
            CatalogAsset.Row same = scanned.Find(r => r.LogicalId == merged.LogicalId);

            Assert.That(same, Is.Not.Null, "a sync of the same folder does not find the merged prefab");
            Assert.That(same.Tags, Is.EqualTo(merged.Tags));
            Assert.That(same.FootprintSize.x, Is.EqualTo(merged.FootprintSize.x).Within(1e-3f));
            Assert.That(same.FootprintSize.y, Is.EqualTo(merged.FootprintSize.y).Within(1e-3f));
            Assert.That(same.Height, Is.EqualTo(merged.Height).Within(1e-3f));
        }

        /// <remarks>
        /// The footprint is measured off the whole group rather than off any one piece of it, so a
        /// desk with a chair pulled up to it is as wide as the two together — which is the ground
        /// the generator has to keep clear for it.
        /// </remarks>
        [Test]
        public void TheRowIsMeasuredOverTheWholeGroup()
        {
            var group = new List<GameObject>
            {
                Box("Desk", new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 1f)),
                Box("Chair", new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 1f)),
            };

            MergeToPrefab.Merge(group, Folder, "Workstation", _catalog);

            // From the desk's low X face at -3 to the chair's high X face at 2.5, centred on the
            // pivot the merge put at the average of the two.
            Assert.That(_catalog.Rows[0].FootprintSize.x, Is.EqualTo(5.5f).Within(1e-3f));
        }

        // --- collision ----------------------------------------------------------------------------

        /// <remarks>
        /// The box on the root is what the catalog and the clearance rules measure, and it has to
        /// stop nothing: a table and four chairs is mostly air, and a solid box round the group
        /// would wall off every gap between the pieces.
        /// </remarks>
        [Test]
        public void TheMergedRootCarriesOneTriggerBoxRoundTheWholeGroup()
        {
            var group = new List<GameObject>
            {
                Piece("Desk", new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 1f)),
                Piece("Chair", new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 1f)),
            };

            GameObject prefab = MergeToPrefab.Merge(group, Folder, "Workstation", _catalog);

            BoxCollider[] onTheRoot = prefab.GetComponents<BoxCollider>();
            Assert.That(onTheRoot, Has.Length.EqualTo(1), "the root should carry exactly one box");
            Assert.That(onTheRoot[0].isTrigger, Is.True, "the root box is a wall, not a measurement");

            // From the desk's low X face at -3 to the chair's high X face at 2.5, in the space of
            // the pivot the merge put at the average of the two.
            Assert.That(onTheRoot[0].size.x, Is.EqualTo(5.5f).Within(1e-3f));
            Assert.That(onTheRoot[0].center.x, Is.EqualTo(-0.25f).Within(1e-3f));
        }

        /// <remarks>
        /// The pieces are already finished art carrying collision somebody chose, and a chair has to
        /// go on blocking where the chair is. Stripping them is what <see cref="WrapObject"/> does to
        /// a raw import, and it is the wrong bargain for an arrangement of finished pieces.
        /// </remarks>
        [Test]
        public void ThePiecesKeepTheirOwnSolidColliders()
        {
            var group = new List<GameObject>
            {
                Piece("Desk", new Vector3(-2f, 0f, 0f), new Vector3(2f, 1f, 1f)),
                Piece("Chair", new Vector3(2f, 0f, 0f), new Vector3(1f, 1f, 1f)),
            };

            GameObject prefab = MergeToPrefab.Merge(group, Folder, "Workstation", _catalog);

            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                GameObject child = prefab.transform.GetChild(i).gameObject;
                var box = child.GetComponent<BoxCollider>();

                Assert.That(box, Is.Not.Null, $"'{child.name}' lost the collider it came with");
                Assert.That(box.isTrigger, Is.False, $"'{child.name}' stopped blocking anything");
            }
        }

        /// <remarks>
        /// The root box is measured off the art rather than off the pieces' colliders, because the
        /// pieces keep theirs and a box read off those would be a copy of them rather than a
        /// measurement of the group. A collider drawn larger than the art it covers is the case that
        /// tells the two apart.
        /// </remarks>
        [Test]
        public void TheRootBoxIsMeasuredOffTheArtAndNotOffThePiecesColliders()
        {
            GameObject piece = Piece("Crate", Vector3.zero, Vector3.one);
            piece.GetComponent<BoxCollider>().size = new Vector3(4f, 1f, 1f);

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { piece }, Folder, "Crate Group", _catalog);

            Assert.That(prefab.GetComponent<BoxCollider>().size.x, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <remarks>
        /// Half a bought art pack ships with no colliders at all. A group made of it used to come out
        /// of the merge as scenery a player walks straight through — the pieces had nothing to
        /// preserve, and the root's trigger stops nothing by design.
        /// </remarks>
        [Test]
        public void AMeshThatArrivedWithNoColliderIsGivenOne()
        {
            GameObject art = Art("Statue", Vector3.zero, new Vector3(2f, 3f, 1f));

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { art }, Folder, "Statue Group", _catalog);

            var box = prefab.transform.GetChild(0).GetComponent<BoxCollider>();

            Assert.That(box, Is.Not.Null, "the mesh is still walk-through");
            Assert.That(box.isTrigger, Is.False, "a hitbox that stops nothing is not a hitbox");

            // The cube's own mesh, in the cube's own space: a unit box at its origin. The scale on
            // the transform is what makes it two by three by one in the group.
            Assert.That(box.size.x, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(box.size.y, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(box.size.z, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(box.center, Is.EqualTo(Vector3.zero));
        }

        /// <remarks>
        /// Sized to the mesh it is on and not to the group, so a part standing off to one side gets a
        /// box where the part is. A box round the whole group on every part would be the invisible
        /// wall the root trigger exists to avoid, repeated once per piece.
        /// </remarks>
        [Test]
        public void EachGeneratedHitboxFitsItsOwnMeshAndNotTheGroup()
        {
            var group = new List<GameObject>
            {
                Art("Left", new Vector3(-4f, 0f, 0f), Vector3.one),
                Art("Right", new Vector3(4f, 0f, 0f), Vector3.one),
            };

            GameObject prefab = MergeToPrefab.Merge(group, Folder, "Pair", _catalog);

            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                Transform child = prefab.transform.GetChild(i);
                var box = child.GetComponent<BoxCollider>();

                Assert.That(box, Is.Not.Null, child.name);
                Assert.That(box.size.x, Is.EqualTo(1f).Within(1e-3f), child.name);
            }
        }

        /// <remarks>
        /// The question is whether the mesh is blocked, not whether it holds the component itself.
        /// <see cref="WrapObject"/> makes exactly the arrangement that tells the two apart: one solid
        /// box on a wrapper with the art's own colliders stripped from under it. A pass that asked
        /// each mesh about itself would put a box back on every part the wrap deliberately cleared.
        /// </remarks>
        [Test]
        public void AMeshAlreadyBlockedByAColliderAboveItIsLeftAlone()
        {
            GameObject wrapper = At("Wrapped", Vector3.zero);
            wrapper.AddComponent<BoxCollider>().size = new Vector3(3f, 3f, 3f);

            GameObject part = Art("Part", Vector3.zero, Vector3.one);
            part.transform.SetParent(wrapper.transform, true);

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { wrapper }, Folder, "Wrapped Group", _catalog);

            Collider[] inside = prefab.GetComponentsInChildren<Collider>(true);

            Assert.That(inside, Has.Length.EqualTo(2),
                "the wrapper's box, the root's trigger, and nothing else");
            Assert.That(prefab.transform.GetChild(0).GetChild(0).GetComponent<Collider>(), Is.Null,
                "the wrapped part was given a box the wrap had cleared");
        }

        /// <remarks>
        /// A trigger is there to be measured and stops nothing, so a mesh whose only cover is one is
        /// uncovered. This is also what keeps the pass independent of the root's own trigger box.
        /// </remarks>
        [Test]
        public void AMeshCoveredOnlyByATriggerCountsAsUncovered()
        {
            GameObject art = Art("Ghost", Vector3.zero, Vector3.one);
            art.AddComponent<SphereCollider>().isTrigger = true;

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { art }, Folder, "Ghost Group", _catalog);

            var box = prefab.transform.GetChild(0).GetComponent<BoxCollider>();

            Assert.That(box, Is.Not.Null, "a trigger was mistaken for a hitbox");
            Assert.That(box.isTrigger, Is.False);
        }

        /// <remarks>
        /// A piece that came with collision keeps exactly what it came with. Adding a second box
        /// beside a mesh collider somebody fitted would be the tool overruling the art.
        /// </remarks>
        [Test]
        public void APieceThatAlreadyBlocksIsNotGivenASecondCollider()
        {
            GameObject piece = Piece("Crate", Vector3.zero, Vector3.one);

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { piece }, Folder, "Crate Group", _catalog);

            Assert.That(prefab.transform.GetChild(0).GetComponents<Collider>(),
                Has.Length.EqualTo(1));
        }

        /// <remarks>
        /// A group of markers and empties renders nothing, and a one-metre default cube round nothing
        /// is a trigger firing in an empty room — the same answer <see cref="WrapObject"/> gives.
        /// </remarks>
        [Test]
        public void AGroupThatRendersNothingGetsNoBox()
        {
            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { At("A", Vector3.zero), At("B", Vector3.one) },
                Folder, "Markers", _catalog);

            Assert.That(prefab.GetComponent<BoxCollider>(), Is.Null);
        }

        /// <remarks>
        /// Rubber-banding in the hierarchy picks a parent and its children together. Reparenting a
        /// child that is already going along with its parent would pull it out of the group it
        /// belongs to, so only the roots of the selection are moved.
        /// </remarks>
        [Test]
        public void AChildPickedAlongWithItsParentStaysWhereItIs()
        {
            GameObject parent = At("Parent", Vector3.zero);
            GameObject child = At("Child", new Vector3(1f, 0f, 0f));
            child.transform.SetParent(parent.transform, true);

            GameObject prefab = MergeToPrefab.Merge(
                new List<GameObject> { parent, child }, Folder, "Nested", _catalog);

            Assert.That(prefab.transform.childCount, Is.EqualTo(1), "the child was reparented too");
            Assert.That(child.transform.parent, Is.EqualTo(parent.transform));
        }

        /// <remarks>
        /// A prefab picked in the project window is not in a scene and cannot be reparented. Saying
        /// so is the whole of the handling: the alternative is a tool that silently merges nothing
        /// and reports success.
        /// </remarks>
        [Test]
        public void MergingSomethingThatIsNotInASceneSaysSo()
        {
            AssetDatabase.CreateFolder(Root, "Art");
            var source = new GameObject("Asset");
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, Root + "/Art/Asset.prefab");
            Object.DestroyImmediate(source);

            Assert.That(
                () => MergeToPrefab.Merge(new List<GameObject> { asset }, Folder, "Nope", _catalog),
                Throws.InvalidOperationException);
        }

        /// <remarks>
        /// A folder outside the workspace spells no tags, and a row with no tags is a row no query
        /// can return — it looks right in the inspector and never appears on a map. Refusing is what
        /// tells the person to write it somewhere the folder means something.
        /// </remarks>
        [Test]
        public void MergingIntoAFolderOutsideTheWorkspaceSaysSo()
        {
            Assert.That(
                () => MergeToPrefab.Merge(Group(), Root, "Loose", _catalog),
                Throws.InvalidOperationException);
        }

        // --- helpers -------------------------------------------------------------------------------

        List<GameObject> Group() => new List<GameObject>
        {
            Box("Desk", new Vector3(0f, 0f, 0f), new Vector3(2f, 1f, 1f)),
            Box("Monitor", new Vector3(0f, 1f, 0f), new Vector3(1f, 0.5f, 0.2f)),
            Box("Chair", new Vector3(0f, 0f, 1f), new Vector3(1f, 1f, 1f)),
        };

        GameObject At(string name, Vector3 position)
        {
            var made = new GameObject(name);
            made.transform.position = position;
            _made.Add(made);

            return made;
        }

        /// <summary>
        /// A cube of the given size: art to measure and a solid collider of its own, which is what
        /// a piece somebody arranges in a scene actually is.
        /// </summary>
        GameObject Piece(string name, Vector3 position, Vector3 size)
        {
            GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cube);
            made.name = name;
            made.transform.position = position;
            made.transform.localScale = size;
            _made.Add(made);

            return made;
        }

        /// <summary>
        /// A cube with its collider taken off: art that renders and stops nothing, which is how half
        /// a bought pack arrives.
        /// </summary>
        GameObject Art(string name, Vector3 position, Vector3 size)
        {
            GameObject made = Piece(name, position, size);
            Object.DestroyImmediate(made.GetComponent<Collider>());

            return made;
        }

        /// <summary>A scene object with a box collider on it, so the sync has something to measure.</summary>
        GameObject Box(string name, Vector3 position, Vector3 size)
        {
            GameObject made = At(name, position);
            made.AddComponent<BoxCollider>().size = size;

            return made;
        }
    }
}
