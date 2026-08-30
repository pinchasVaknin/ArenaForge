using ArenaForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The post-import turn for art that faces the wrong way: the children go round and the root
    /// stays exactly where the rest of the tool needs it.
    /// </summary>
    /// <remarks>
    /// The root is the whole point of these. <c>WorldRealizer</c> writes a pose onto it,
    /// <c>CatalogSync</c> measures through it and the placement stages reason about a footprint they
    /// believe is axis-aligned, so a tool that turned the root would break all three quietly. Every
    /// test here asserts what the root is as well as what the art did.
    /// </remarks>
    public sealed class MeshRotationTests
    {
        const string TempFolder = "Assets/ArenaForgeMeshRotationTestTemp";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeMeshRotationTestTemp");
            }
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(TempFolder);

        /// <remarks>
        /// A child a metre out along X, which is the case that tells a turn from a spin: rotating it
        /// where it stands leaves it at the same place pointing elsewhere, and turning it about the
        /// root's origin carries it round to -Z. Only the second one looks like picking the model up
        /// and turning it.
        /// </remarks>
        [Test]
        public void TheArtTurnsAboutTheRootAndTheRootStaysSquare()
        {
            GameObject prefab = Prefab("Facing", new Vector3(1f, 0f, 0f), Quaternion.identity);

            Assert.That(MeshRotation.Turn(prefab, 90f, out string why), Is.True, why);

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(Path("Facing"));
            Transform child = saved.transform.GetChild(0);

            Assert.That(child.localPosition.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(child.localPosition.z, Is.EqualTo(-1f).Within(1e-3f),
                "a child a metre out along X should come round to -Z, not stay where it was");

            Assert.That(Quaternion.Angle(child.localRotation, Quaternion.Euler(0f, 90f, 0f)),
                Is.LessThan(0.01f));

            Assert.That(Quaternion.Angle(saved.transform.localRotation, Quaternion.identity),
                Is.LessThan(0.01f), "the root was turned, which the generator poses through");

            Assert.That(saved.transform.localScale, Is.EqualTo(Vector3.one));
        }

        /// <remarks>
        /// Two quarter turns are a half turn, which is the cheapest check that the operation is the
        /// composition it looks like rather than something that only happens to work once.
        /// </remarks>
        [Test]
        public void TurningTwiceIsTurningOnceByTwiceAsMuch()
        {
            GameObject prefab = Prefab("Twice", new Vector3(1f, 0f, 0f), Quaternion.identity);

            Assert.That(MeshRotation.Turn(prefab, 90f, out _), Is.True);
            Assert.That(MeshRotation.Turn(prefab, 90f, out _), Is.True);

            Transform child = AssetDatabase.LoadAssetAtPath<GameObject>(Path("Twice"))
                .transform.GetChild(0);

            Assert.That(child.localPosition.x, Is.EqualTo(-1f).Within(1e-3f));
            Assert.That(child.localPosition.z, Is.EqualTo(0f).Within(1e-3f));
        }

        /// <remarks>
        /// Squaring the root up would move art already standing in somebody's map, and this is
        /// reached from a right-click on an asset. Refusing says so where a silent correction would
        /// not.
        /// </remarks>
        [Test]
        public void APrefabWhoseRootIsRotatedIsRefusedRatherThanSquaredUp()
        {
            GameObject prefab = Prefab(
                "Crooked", new Vector3(1f, 0f, 0f), Quaternion.identity, Quaternion.Euler(0f, 30f, 0f));

            Assert.That(MeshRotation.Turn(prefab, 90f, out string why), Is.False);
            Assert.That(why, Does.Contain("root is rotated"));

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(Path("Crooked"));

            Assert.That(saved.transform.GetChild(0).localPosition.x, Is.EqualTo(1f).Within(1e-3f),
                "a refused prefab should come back untouched");

            Assert.That(Quaternion.Angle(saved.transform.localRotation, Quaternion.Euler(0f, 30f, 0f)),
                Is.LessThan(0.01f), "a refusal should not have squared the root up either");
        }

        /// <remarks>
        /// Art modelled straight onto the root has nothing to turn that is not the root, so the only
        /// honest answer is to say so — the same answer <c>ArenaAssetImport</c> gives when it has no
        /// child to put a size correction on.
        /// </remarks>
        [Test]
        public void APrefabWithNoChildrenIsRefused()
        {
            var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, Path("Bare"));
            Object.DestroyImmediate(source);

            Assert.That(MeshRotation.Turn(prefab, 90f, out string why), Is.False);
            Assert.That(why, Does.Contain("no children"));
        }

        static string Path(string name) => $"{TempFolder}/{name}.prefab";

        /// <summary>A prefab with one child mesh, at a pose, under a root at a rotation.</summary>
        static GameObject Prefab(
            string name, Vector3 childAt, Quaternion childTurn, Quaternion? rootTurn = null)
        {
            var root = new GameObject(name);
            root.transform.localRotation = rootTurn ?? Quaternion.identity;

            GameObject art = GameObject.CreatePrimitive(PrimitiveType.Cube);
            art.name = "Art";
            art.transform.SetParent(root.transform, false);
            art.transform.localPosition = childAt;
            art.transform.localRotation = childTurn;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Path(name));
            Object.DestroyImmediate(root);

            return prefab;
        }
    }
}
