using System.Collections.Generic;
using ArenaForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The Wrap Object tool: an imported model gets a parent whose axes the generator can trust.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claims worth asserting are the ones a person cannot see by looking at the scene view.
    /// That the model turned is obvious; that the <em>parent</em> did not is the whole point of the
    /// tool, and a wrap that quietly turned the pair together would look identical and be useless.
    /// </para>
    /// <para>
    /// The objects are made in the open scene, as <see cref="MergeToPrefabTests"/> makes its own:
    /// the tool reparents scene objects, and refusing a project asset is a case tested rather than
    /// avoided.
    /// </para>
    /// </remarks>
    public sealed class WrapObjectTests
    {
        const string Folder = "Assets/ArenaForgeWrapTests";

        readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void CreateFolder()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "ArenaForgeWrapTests");
        }

        [TearDown]
        public void Clean()
        {
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
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>A cube standing somewhere other than the origin and turned off axis.</summary>
        GameObject Model(string name)
        {
            GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = name;
            model.transform.SetPositionAndRotation(
                new Vector3(3f, 1f, -2f), Quaternion.Euler(0f, 37f, 0f));
            model.transform.localScale = new Vector3(2f, 2f, 2f);

            _made.Add(model);
            return model;
        }

        // --- the wrap -------------------------------------------------------------------------

        [Test]
        public void TheWrapperStandsAtTheOriginWithTheModelUnderIt()
        {
            GameObject model = Model("Closet");

            GameObject wrapper = WrapObject.Wrap(model);

            Assert.That(wrapper.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(wrapper.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(model.transform.parent, Is.EqualTo(wrapper.transform));
            Assert.That(model.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(model.transform.localRotation, Is.EqualTo(Quaternion.identity));
        }

        /// <remarks>
        /// A position and a rotation are what the wrap takes over; how big the artist made the
        /// thing is not. A model that imported at a hundredth of its size and came out of a wrap at
        /// full size would be a tool that broke the art it exists to fix.
        /// </remarks>
        [Test]
        public void TheModelKeepsItsOwnScale()
        {
            GameObject model = Model("Closet");

            WrapObject.Wrap(model);

            Assert.That(model.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }

        /// <remarks>
        /// The claim the whole tool rests on. What the generator turns is the prefab root, so the
        /// root's forward has to stay world forward however far the model inside it is turned —
        /// otherwise the wrap has moved the problem rather than fixed it.
        /// </remarks>
        [Test]
        public void TurningMovesTheModelAndLeavesTheWrapperFacingForward()
        {
            GameObject model = Model("Closet");
            GameObject wrapper = WrapObject.Wrap(model);

            WrapObject.Turn(wrapper, 1);

            Assert.That(wrapper.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(model.transform.forward.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(model.transform.forward.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TurnsAccumulate()
        {
            GameObject wrapper = WrapObject.Wrap(Model("Closet"));
            Transform model = wrapper.transform.GetChild(0);

            WrapObject.Turn(wrapper, 1);
            WrapObject.Turn(wrapper, 1);

            Assert.That(model.forward.z, Is.EqualTo(-1f).Within(1e-4f), "two quarters make a half");
        }

        [Test]
        public void TurningBackwardsUndoesTurningForwards()
        {
            GameObject wrapper = WrapObject.Wrap(Model("Closet"));
            Transform model = wrapper.transform.GetChild(0);

            WrapObject.Turn(wrapper, 1);
            WrapObject.Turn(wrapper, -1);

            Assert.That(model.forward.z, Is.EqualTo(1f).Within(1e-4f));
        }

        // --- the collision box -------------------------------------------------------------------

        /// <remarks>
        /// The size the tool exists to produce, and the one a person cannot check by looking: the
        /// box is round the art rather than round the pivot, so a model built off to one side of
        /// its own origin gets a box off to that side too. A box centred on the pivot would be
        /// twice the size of the model in every direction it is offset — which is the mistake the
        /// catalog measurement itself used to make.
        /// </remarks>
        [Test]
        public void TheWrapperGetsOneBoxRoundTheWholeModel()
        {
            GameObject model = Model("Closet");
            model.transform.localScale = Vector3.one;

            GameObject wrapper = WrapObject.Wrap(model);

            var box = wrapper.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, "the wrapper carries the collision");
            Assert.That(box.size, Is.EqualTo(Vector3.one).Using(Approximately));
            Assert.That(box.center, Is.EqualTo(Vector3.zero).Using(Approximately));
        }

        [Test]
        public void TheBoxHoldsEveryPartOfAModelMadeOfSeveral()
        {
            GameObject model = Model("Closet");
            model.transform.localScale = Vector3.one;

            GameObject wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.transform.SetParent(model.transform, false);
            wing.transform.localPosition = new Vector3(2f, 0f, 0f);

            GameObject wrapper = WrapObject.Wrap(model);

            var box = wrapper.GetComponent<BoxCollider>();
            Assert.That(box.size, Is.EqualTo(new Vector3(3f, 1f, 1f)).Using(Approximately));
            Assert.That(box.center, Is.EqualTo(new Vector3(1f, 0f, 0f)).Using(Approximately));
        }

        /// <remarks>
        /// The other half of the bargain, and the one that is about the frame rate rather than the
        /// size: a pack's model brings a collider per part, and a map stamps thirty of it. One box
        /// on the parent is the whole of the collision, so everything underneath has to go.
        /// </remarks>
        [Test]
        public void TheModelsOwnCollidersAreTakenOff()
        {
            GameObject model = Model("Closet");
            GameObject wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.transform.SetParent(model.transform, false);

            Assert.That(model.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(2),
                "two primitives arrive with a box collider each");

            GameObject wrapper = WrapObject.Wrap(model);

            Assert.That(wrapper.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1),
                "the wrapper's own box, and nothing under it");
            Assert.That(model.GetComponent<Collider>(), Is.Null);
        }

        /// <remarks>
        /// A quarter turn swaps a box's two horizontal sides. A collider left at the size it had
        /// before the turn stands at right angles to the art inside it, which looks perfect in the
        /// scene view and is what a player walks into.
        /// </remarks>
        [Test]
        public void TurningRefitsTheBoxToTheModelsNewFacing()
        {
            GameObject model = Model("Closet");
            model.transform.localScale = new Vector3(4f, 1f, 1f);

            GameObject wrapper = WrapObject.Wrap(model);
            Assert.That(wrapper.GetComponent<BoxCollider>().size,
                Is.EqualTo(new Vector3(4f, 1f, 1f)).Using(Approximately));

            WrapObject.Turn(wrapper, 1);

            Assert.That(wrapper.GetComponent<BoxCollider>().size,
                Is.EqualTo(new Vector3(1f, 1f, 4f)).Using(Approximately),
                "a quarter turn swaps the box's two horizontal sides");
        }

        /// <remarks>
        /// Refitting has to measure the art rather than the box already standing round it. The
        /// catalog's measurement prefers box colliders to meshes and does not care whose they are,
        /// so a refit that left the old box in place would measure a copy of itself and never
        /// change again — which is a turn that quietly does nothing to the collision.
        /// </remarks>
        [Test]
        public void RefittingTwiceGivesTheSameBoxRatherThanAGrowingOne()
        {
            GameObject wrapper = WrapObject.Wrap(Model("Closet"));

            WrapObject.Turn(wrapper, 1);
            Vector3 once = wrapper.GetComponent<BoxCollider>().size;
            WrapObject.EncloseInABox(wrapper);

            Assert.That(wrapper.GetComponent<BoxCollider>().size,
                Is.EqualTo(once).Using(Approximately));
            Assert.That(wrapper.GetComponents<BoxCollider>().Length, Is.EqualTo(1),
                "one box, not one per refit");
        }

        /// <remarks>
        /// A wrapper round something with no mesh in it — an empty, a light, a marker — has nothing
        /// to measure, and a default one-metre cube round nothing is a collider a player walks into
        /// in an empty room.
        /// </remarks>
        [Test]
        public void AModelWithNoArtInItGetsNoBox()
        {
            var model = new GameObject("Marker");
            model.transform.position = new Vector3(3f, 1f, -2f);
            _made.Add(model);

            GameObject wrapper = WrapObject.Wrap(model);

            Assert.That(WrapObject.EncloseInABox(wrapper), Is.Null);
            Assert.That(wrapper.GetComponent<BoxCollider>(), Is.Null);
        }

        [Test]
        public void FittingABoxToNothingIsRefusedWithAMessage()
        {
            Assert.That(
                Assert.Throws<System.InvalidOperationException>(
                    () => WrapObject.EncloseInABox(null)).Message,
                Does.Contain("Select"));
        }

        /// <remarks>
        /// The reason the box is fitted at all: a catalog row is measured off box colliders where a
        /// prefab has any, so the rectangle the generator places against and the shape a player
        /// walks into are one measurement rather than two.
        /// </remarks>
        [Test]
        public void TheSavedPrefabMeasuresAsItsOwnBox()
        {
            GameObject model = Model("Closet");
            model.transform.localScale = new Vector3(4f, 1f, 2f);
            GameObject wrapper = WrapObject.Wrap(model);

            GameObject prefab = WrapObject.SaveAsPrefab(wrapper, Folder, "closet");

            Assert.That(CatalogSync.TryMeasure(prefab, out Bounds bounds), Is.True);
            Assert.That(bounds.size, Is.EqualTo(new Vector3(4f, 1f, 2f)).Using(Approximately));
        }

        /// <summary>Vector comparison at the precision a measurement of a mesh is good to.</summary>
        static readonly System.Collections.IComparer Approximately =
            new VectorTolerance(1e-4f);

        sealed class VectorTolerance : System.Collections.IComparer
        {
            readonly float _tolerance;

            public VectorTolerance(float tolerance) => _tolerance = tolerance;

            public int Compare(object x, object y)
            {
                var a = (Vector3)x;
                var b = (Vector3)y;
                return Mathf.Abs(a.x - b.x) <= _tolerance &&
                       Mathf.Abs(a.y - b.y) <= _tolerance &&
                       Mathf.Abs(a.z - b.z) <= _tolerance
                    ? 0
                    : 1;
            }
        }

        // --- what it refuses -------------------------------------------------------------------

        /// <remarks>
        /// The tool acts on a selection, and a selection is empty far more often than it is not.
        /// What that has to produce is a sentence saying what to select, which is what the window
        /// puts in a dialog — a null reference in the console is the tool telling the person
        /// nothing and the console telling them about the tool.
        /// </remarks>
        [Test]
        public void WrappingNothingIsRefusedWithAMessage()
        {
            Assert.That(
                Assert.Throws<System.InvalidOperationException>(() => WrapObject.Wrap(null)).Message,
                Does.Contain("Select"));
        }

        [Test]
        public void WrappingAProjectAssetIsRefused()
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, Folder + "/cube.prefab");
            Object.DestroyImmediate(source);

            Assert.Throws<System.InvalidOperationException>(() => WrapObject.Wrap(asset));
        }

        [Test]
        public void TurningNothingIsRefusedWithAMessage()
        {
            Assert.Throws<System.InvalidOperationException>(() => WrapObject.Turn(null, 1));

            var empty = new GameObject("Empty");
            _made.Add(empty);

            Assert.Throws<System.InvalidOperationException>(() => WrapObject.Turn(empty, 1));
        }

        [Test]
        public void SavingNothingIsRefusedWithAMessage()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => WrapObject.SaveAsPrefab(null, Folder, "closet"));

            GameObject wrapper = WrapObject.Wrap(Model("Closet"));

            Assert.Throws<System.InvalidOperationException>(
                () => WrapObject.SaveAsPrefab(wrapper, Folder, "   "));
            Assert.Throws<System.InvalidOperationException>(
                () => WrapObject.SaveAsPrefab(wrapper, string.Empty, "closet"));
        }

        // --- the prefab ------------------------------------------------------------------------

        /// <remarks>
        /// What is saved is what was on screen: the turn a person pressed the buttons for is in the
        /// asset, and the root is still the identity the catalog will measure the piece about.
        /// </remarks>
        [Test]
        public void TheSavedPrefabHoldsTheTurnAndAnUnturnedRoot()
        {
            GameObject wrapper = WrapObject.Wrap(Model("Closet"));
            WrapObject.Turn(wrapper, 1);

            GameObject prefab = WrapObject.SaveAsPrefab(wrapper, Folder, "closet");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(Folder + "/closet.prefab"));
            Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(prefab.transform.childCount, Is.EqualTo(1));
            Assert.That(prefab.transform.GetChild(0).forward.x, Is.EqualTo(1f).Within(1e-4f));
        }

        /// <remarks>
        /// A folder that does not exist yet is made rather than refused — a person browsing to a
        /// folder they have just made in the project window is the normal case, and one typing a
        /// path is entitled to the same treatment the workspace setup gives.
        /// </remarks>
        [Test]
        public void SavingIntoAFolderThatDoesNotExistYetCreatesIt()
        {
            GameObject wrapper = WrapObject.Wrap(Model("Closet"));

            GameObject prefab = WrapObject.SaveAsPrefab(wrapper, Folder + "/Deeper/Still", "closet");

            Assert.That(
                AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo(Folder + "/Deeper/Still/closet.prefab"));
        }

        [Test]
        public void SavingTwiceUnderOneNameWritesTwoPrefabsRatherThanOverwritingOne()
        {
            GameObject first = WrapObject.Wrap(Model("Closet"));
            GameObject second = WrapObject.Wrap(Model("Closet"));

            WrapObject.SaveAsPrefab(first, Folder, "closet");
            GameObject other = WrapObject.SaveAsPrefab(second, Folder, "closet");

            Assert.That(
                AssetDatabase.GetAssetPath(other), Is.Not.EqualTo(Folder + "/closet.prefab"));
        }
    }
}
