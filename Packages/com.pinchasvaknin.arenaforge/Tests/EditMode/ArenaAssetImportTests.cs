using System.Collections.Generic;
using ArenaForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Importing somebody else's art: the size correction, and the variant it is written onto.
    /// </summary>
    /// <remarks>
    /// The arithmetic is most of it, because the arithmetic is where this can quietly do harm. A
    /// correction that fired too readily would resize art that was already the size it meant to be,
    /// and the map that came out would be wrong in a way no other suite here would notice.
    /// </remarks>
    public sealed class ArenaAssetImportTests
    {
        const string TempFolder = "Assets/ArenaForgeImportTestTemp";

        /// <summary>Where the overwrite cases import to, which is not where their sources live.</summary>
        /// <remarks>
        /// A prefab asset is named after its own file, so a source written as <c>Crate_source</c> is
        /// called <c>Crate_source</c> — and importing it back into its own folder therefore aims at
        /// the source's own path. That is the collision
        /// <see cref="OverwriteNeverWritesOverTheArtBeingImported"/> is about, so the cases that are
        /// about replacing a previous <em>import</em> have to write somewhere else to be about that
        /// at all. The first run of these tests found this by failing.
        /// </remarks>
        const string OutFolder = TempFolder + "/Imported";

        /// <remarks>
        /// The folder goes and everything in it with it. Destroying the assets one by one is refused
        /// by Unity outright — "destroying assets is not permitted to avoid data loss" — which is a
        /// scene object's cleanup applied to something that lives on disk.
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        // --- the correction ------------------------------------------------------------------

        /// <remarks>
        /// The case the whole thing exists for: art measured a little under the metre it was plainly
        /// modelled as.
        /// </remarks>
        [Test]
        public void ASizeJustUnderAModuleIsCorrectedToIt()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(0.97f, 1f, 0.05f, out float scale, out float target),
                Is.True);

            Assert.That(target, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(0.97f * scale, Is.EqualTo(1f).Within(1e-5f));
        }

        /// <remarks>
        /// The case that must not be corrected, and the reason the tolerance is small by default.
        /// Nothing in the number tells a mis-measured 4.70 from a deliberate 2.30; a tolerance wide
        /// enough to catch the first rounds the second by 30 cm and makes every run worse. Measured:
        /// snapping 2.3 to 2 took the lapping on a default map from 2.14 m to 2.54 m.
        /// </remarks>
        [Test]
        public void ASizeFurtherOffThanTheToleranceIsLeftAlone()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(2.3f, 1f, 0.05f, out float _, out float target),
                Is.False);

            Assert.That(target, Is.EqualTo(2.3f), "an untouched size reports itself");
        }

        /// <remarks>
        /// The tolerance is 0.35 rather than the 0.3 this correction actually needs, because 0.3 is
        /// the boundary: 4.7f is 4.6999998 and the gap to five comes out a hair over the 0.3f it is
        /// being compared with, so the case would be asserting single precision rather than the
        /// rule. Which side of an exact boundary a tolerance falls is not a promise worth making.
        /// </remarks>
        [Test]
        public void AToleranceWideEnoughForItCorrectsIt()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(4.7f, 1f, 0.35f, out float scale, out float target),
                Is.True);

            Assert.That(target, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(4.7f * scale, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void ASizeAlreadyOnTheModuleIsNotTouched()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(2f, 1f, 0.05f, out float _, out float _),
                Is.False,
                "there is nothing to correct, and a scale of one is still a change to the asset");
        }

        /// <remarks>
        /// A door handle has a nearest metre of zero, and a scale of zero is not a correction — it is
        /// art that disappears. Anything under half a module is art that was not modelled to this
        /// grid at all.
        /// </remarks>
        [Test]
        public void ArtSmallerThanHalfAModuleIsLeftAlone()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(0.1f, 1f, 0.5f, out float scale, out float _),
                Is.False);

            Assert.That(scale, Is.EqualTo(1f));
        }

        [TestCase(0f, 1f, 0.05f, TestName = "nothing measured")]
        [TestCase(1f, 0f, 0.05f, TestName = "no module to snap to")]
        [TestCase(1f, 1f, -1f, TestName = "a negative tolerance")]
        public void NonsenseArgumentsCorrectNothing(float measured, float module, float tolerance)
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(measured, module, tolerance, out float scale, out float _),
                Is.False);

            Assert.That(scale, Is.EqualTo(1f));
        }

        [Test]
        public void AToleranceOfZeroCorrectsNothingAtAll()
        {
            Assert.That(
                ArenaAssetImport.TryCorrection(0.97f, 1f, 0f, out float _, out float _), Is.False);
        }

        // --- what the measurement counts -------------------------------------------------------

        /// <remarks>
        /// <para>
        /// The convention the whole import has to be built around, pinned here because nothing else
        /// states it. <c>CatalogSync.Extent</c> gathers a prefab's corners in its <em>own root
        /// space</em>, which is the one space the root's own scale is invisible in — the root's
        /// <c>worldToLocalMatrix</c> carries its inverse and every child's <c>localToWorldMatrix</c>
        /// carries it, so the two cancel. It is applied when the box is closed.
        /// </para>
        /// <para>
        /// <strong>This test used to assert the opposite, and the reason it gave was wrong.</strong>
        /// It said the root scale was not counted "though a realised instance carries it" — and a
        /// realised instance did not carry it: <c>WorldRealizer</c> assigned <c>localScale</c>
        /// outright, so the prefab's own root scale was discarded at realisation. The row and the
        /// map therefore agreed, and both disagreed with the art. Counting it in the measurement
        /// while the realiser still overwrote it would have broken the one agreement there was,
        /// which is why the fix is in both places at once.
        /// </para>
        /// </remarks>
        [Test]
        public void TheMeasurementCountsTheRootsOwnScale()
        {
            GameObject source = SourcePrefab("Rooted", 1f);

            Assert.That(CatalogSync.TryMeasureArt(source, out Bounds before), Is.True);
            Assert.That(before.size.x, Is.EqualTo(1f).Within(1e-4f));

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.localScale = Vector3.one * 2f;
            GameObject scaled = PrefabUtility.SaveAsPrefabAsset(
                instance, $"{TempFolder}/Rooted_scaled.prefab");
            Object.DestroyImmediate(instance);

            Assert.That(CatalogSync.TryMeasureArt(scaled, out Bounds after), Is.True);
            Assert.That(after.size.x, Is.EqualTo(2f).Within(1e-4f),
                "a prefab scaled to twice its size measures twice as wide");
        }

        /// <remarks>
        /// The other half, asserted where it can be seen: the art stands in the map at the size the
        /// row says it is. Without it the two halves of the fix could drift apart again and only one
        /// of them would fail.
        /// </remarks>
        [Test]
        public void ARealisedInstanceStandsAtTheSizeItsRowWasMeasuredAt()
        {
            GameObject source = SourcePrefab("Standing", 1f);

            GameObject authored = (GameObject)PrefabUtility.InstantiatePrefab(source);
            authored.transform.localScale = Vector3.one * 2f;
            GameObject scaled = PrefabUtility.SaveAsPrefabAsset(
                authored, $"{TempFolder}/Standing_scaled.prefab");
            Object.DestroyImmediate(authored);

            Assert.That(CatalogSync.TryMeasureArt(scaled, out Bounds measured), Is.True);

            GameObject realised = (GameObject)PrefabUtility.InstantiatePrefab(scaled);
            try
            {
                // What WorldRealizer does to a pose of scale one, which is every object but a
                // stretched wall run.
                Vector3 own = realised.transform.localScale;
                realised.transform.localScale = new Vector3(own.x * 1f, own.y * 1f, own.z * 1f);

                Assert.That(CatalogSync.TryMeasureArt(realised, out Bounds stood), Is.True);
                Assert.That(stood.size.x, Is.EqualTo(measured.size.x).Within(1e-4f),
                    $"the row says {measured.size.x} across and the instance stands {stood.size.x}");
            }
            finally
            {
                Object.DestroyImmediate(realised);
            }
        }

        // --- the variant ---------------------------------------------------------------------

        /// <remarks>
        /// The whole import in one pass: an empty root with the source nested under it, standing at
        /// the size the correction decided and carrying a collider the source did not have. That the
        /// source is <em>nested</em> rather than copied is checked, because a copy would look
        /// identical here and would be a second asset to keep in step for as long as both existed.
        /// </remarks>
        [Test]
        public void ImportingUndersizedArtWritesACorrectedPrefabWithACollider()
        {
            GameObject source = SourcePrefab("Undersized", 0.97f);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported.Count, Is.EqualTo(1));

            ImportedAsset result = imported[0];
            Assert.That(result.Measured, Is.EqualTo(0.97f).Within(1e-4f));
            Assert.That(result.Corrected, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(result.Rescaled, Is.True);
            Assert.That(result.ColliderAdded, Is.True);

            Assert.That(result.Prefab.transform.childCount, Is.EqualTo(1),
                "an imported prefab is an empty root with the art under it");

            Transform art = result.Prefab.transform.GetChild(0);
            Assert.That(art.name, Is.EqualTo(ArenaAssetImport.ArtName));

            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(art.gameObject), Is.EqualTo(source),
                "the art should be the source nested, not a copy of it");

            var renderer = result.Prefab.GetComponentInChildren<MeshRenderer>();
            Assert.That(renderer.bounds.size.x, Is.EqualTo(1f).Within(1e-3f),
                "the corrected art should measure the module it was snapped to");

            var box = result.Prefab.GetComponentInChildren<BoxCollider>();
            Assert.That(box, Is.Not.Null);
        }

        /// <remarks>
        /// <para>
        /// The reason the wrapper exists. Art modelled straight onto a prefab's root used to come
        /// out of a variant import flat — mesh and collider on the imported root, nothing beneath —
        /// which <see cref="MeshRotation"/> has to refuse, because the only thing there to turn is
        /// the root the generator poses through.
        /// </para>
        /// <para>
        /// Both halves are asserted here rather than only the shape: that the root is square and
        /// carries nothing, and that the turn the shape exists for actually goes through.
        /// </para>
        /// </remarks>
        [Test]
        public void ArtModelledOntoItsRootImportsWithARootThatCanBeTurned()
        {
            GameObject source = FlatSourcePrefab("Flat", 1f);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported.Count, Is.EqualTo(1));

            GameObject prefab = imported[0].Prefab;

            Assert.That(prefab.GetComponent<MeshRenderer>(), Is.Null,
                "the imported root should carry nothing but its transform");

            Assert.That(prefab.transform.childCount, Is.EqualTo(1));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(
                Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity),
                Is.LessThan(0.01f));

            Assert.That(MeshRotation.Turn(prefab, 90f, out string why), Is.True,
                $"flat art should be turnable once imported, and was refused: {why}");
        }

        /// <remarks>
        /// The correction lands on the one child rather than on the source's own children, so it
        /// works on art with nothing under its root — which used to be reported unchanged for want
        /// of anywhere to put a scale.
        /// </remarks>
        [Test]
        public void ArtModelledOntoItsRootIsStillCorrectedToTheModule()
        {
            GameObject source = FlatSourcePrefab("FlatUndersized", 0.97f);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported[0].Rescaled, Is.True);
            Assert.That(imported[0].Corrected, Is.EqualTo(1f).Within(1e-4f));

            var renderer = imported[0].Prefab.GetComponentInChildren<MeshRenderer>();
            Assert.That(renderer.bounds.size.x, Is.EqualTo(1f).Within(1e-3f));
        }

        /// <remarks>
        /// The other half of the bargain: art the correction declines is written through untouched,
        /// so importing is never a reason for a size to move on its own.
        /// </remarks>
        [Test]
        public void ImportingArtOutsideTheToleranceLeavesItsSizeAlone()
        {
            GameObject source = SourcePrefab("Deliberate", 2.3f);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported.Count, Is.EqualTo(1));
            Assert.That(imported[0].Rescaled, Is.False);

            var renderer = imported[0].Prefab.GetComponentInChildren<MeshRenderer>();
            Assert.That(renderer.bounds.size.x, Is.EqualTo(2.3f).Within(1e-3f));
        }

        /// <remarks>
        /// A source that already carries a box collider keeps its own: the collider is a size
        /// somebody chose, and replacing it with the mesh bounds would overrule that silently.
        /// </remarks>
        [Test]
        public void ASourceThatAlreadyHasAColliderGainsNoSecondOne()
        {
            GameObject source = SourcePrefab("Collided", 1f, withCollider: true);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported[0].ColliderAdded, Is.False);
            Assert.That(imported[0].Prefab.GetComponentsInChildren<BoxCollider>().Length,
                Is.EqualTo(1));
        }

        // --- the art is centred over the root --------------------------------------------------

        /// <remarks>
        /// <para>
        /// What the centring is for. A pose puts a pivot somewhere and turns the root about it, so
        /// art the modeller left three metres off that pivot travels a three-metre arc every time
        /// somebody turns it — which reads as a piece flying off across the map rather than turning
        /// in place. The workspace's own stone boundary panel measured sixteen metres out.
        /// </para>
        /// <para>
        /// Asserted as a turn rather than as a coordinate, because the coordinate is the mechanism
        /// and the turn is the property: after a quarter turn about the root the art occupies the
        /// same ground it did before, which is what "spins in place" means.
        /// </para>
        /// </remarks>
        [Test]
        public void ArtModelledOffItsPivotIsCentredSoATurnSpinsItInPlace()
        {
            GameObject source = OffsetSourcePrefab("Offset", 1f, new Vector3(3f, 0f, 0f));

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported.Count, Is.EqualTo(1));
            Assert.That(imported[0].Centred, Is.True);
            Assert.That(imported[0].Recentred, Is.EqualTo(3f).Within(1e-3f));

            GameObject prefab = imported[0].Prefab;
            Assert.That(CatalogSync.TryMeasureArt(prefab, out Bounds before), Is.True);

            Assert.That(before.center.x, Is.EqualTo(0f).Within(1e-3f),
                "the art should sit over the root it is turned about");
            Assert.That(before.center.z, Is.EqualTo(0f).Within(1e-3f));

            Assert.That(MeshRotation.Turn(prefab, 90f, out string why), Is.True, why);
            Assert.That(CatalogSync.TryMeasureArt(prefab, out Bounds after), Is.True);

            Assert.That(after.center.x, Is.EqualTo(before.center.x).Within(1e-3f),
                "a quarter turn moved centred art off its pivot");
            Assert.That(after.center.z, Is.EqualTo(before.center.z).Within(1e-3f));
        }

        /// <remarks>
        /// The other half of the same decision, and the half that would break the boundary fence if
        /// it went the other way. How far a piece reaches below its own pivot is what
        /// <c>CatalogEntry.BaseOffset</c> records and every placement stage adds back, and
        /// <c>PerimeterFence</c> plants a panel on its pivot precisely so the foundation modelled
        /// under it goes underground. Centring the height would bury half of every wall.
        /// </remarks>
        [Test]
        public void CentringLeavesTheHeightOfTheArtWhereTheModellerPutIt()
        {
            GameObject source = OffsetSourcePrefab("Raised", 1f, new Vector3(2f, 4f, 0f));

            Assert.That(CatalogSync.TryMeasureArt(source, out Bounds art), Is.True);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(
                CatalogSync.TryMeasureArt(imported[0].Prefab, out Bounds wrapped), Is.True);

            Assert.That(wrapped.center.y, Is.EqualTo(art.center.y).Within(1e-3f),
                "the height above the pivot is a fact about the art, not slop to be corrected");
        }

        /// <remarks>
        /// Art already over its own pivot is not moved, and says so — so the report can tell the
        /// pieces that needed this from the pieces that did not.
        /// </remarks>
        [Test]
        public void ArtAlreadyOverItsPivotIsNotMoved()
        {
            GameObject source = SourcePrefab("Centred", 1f);

            List<ImportedAsset> imported =
                ArenaAssetImport.Import(new[] { source }, TempFolder, 1f, 0.05f);

            Assert.That(imported[0].Centred, Is.False);
            Assert.That(imported[0].Recentred, Is.Zero);
        }

        // --- re-importing over what is already there ---------------------------------------------

        /// <remarks>
        /// The default, and the reason the toggle had to exist. Writing beside is safe and is not
        /// what anybody wants from a re-import: it leaves the fixed art in a second file while every
        /// catalog row, saved map and scene goes on pointing at the old one.
        /// </remarks>
        [Test]
        public void ImportingTwiceWritesASecondPrefabByDefault()
        {
            GameObject source = SourcePrefab("Twice", 1f);

            GameObject first =
                ArenaAssetImport.Import(new[] { source }, OutFolder, 1f, 0.05f)[0].Prefab;
            ImportedAsset second =
                ArenaAssetImport.Import(new[] { source }, OutFolder, 1f, 0.05f)[0];

            Assert.That(second.Replaced, Is.False);
            Assert.That(
                AssetDatabase.GetAssetPath(second.Prefab),
                Is.Not.EqualTo(AssetDatabase.GetAssetPath(first)));
        }

        /// <remarks>
        /// <para>
        /// What the toggle buys, and the assertion that matters is the GUID rather than the path.
        /// A prefab written over keeps its own asset identity, so a catalog row bound to it, a map
        /// that placed it and a scene holding an instance of it all follow the re-import — which is
        /// the whole point of updating in place rather than writing a second file.
        /// </para>
        /// </remarks>
        [Test]
        public void ImportingWithOverwriteReplacesThePrefabAndKeepsItsGuid()
        {
            GameObject source = SourcePrefab("Replaced", 1f);

            GameObject first =
                ArenaAssetImport.Import(new[] { source }, OutFolder, 1f, 0.05f)[0].Prefab;

            string path = AssetDatabase.GetAssetPath(first);
            string guid = AssetDatabase.AssetPathToGUID(path);

            ImportedAsset again = ArenaAssetImport.Import(
                new[] { source }, OutFolder, 1f, 0.05f, true)[0];

            Assert.That(again.Replaced, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(again.Prefab), Is.EqualTo(path));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid),
                "a replaced prefab has to keep the identity every row and map refers to it by");
        }

        /// <remarks>
        /// The one case the toggle may not honour. A source filed in the folder it is being imported
        /// into is somebody importing in place, and writing over it would replace the model with a
        /// wrapper that nests the model it just destroyed.
        /// </remarks>
        [Test]
        public void OverwriteNeverWritesOverTheArtBeingImported()
        {
            // Filed under its own name in the folder it is about to be imported into, which is the
            // one arrangement where the wrapper's path and the source's path are the same path.
            GameObject source = SourcePrefabNamed("InPlace", 1f);
            string sourcePath = AssetDatabase.GetAssetPath(source);

            Assert.That(sourcePath, Is.EqualTo($"{TempFolder}/InPlace.prefab"),
                "the fixture is not set up for the collision this guards");

            ImportedAsset imported = ArenaAssetImport.Import(
                new[] { source }, TempFolder, 1f, 0.05f, true)[0];

            Assert.That(imported.Replaced, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(imported.Prefab), Is.Not.EqualTo(sourcePath));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), Is.Not.Null,
                "the art being imported should still be on disk afterwards");
        }

        /// <summary>
        /// The same art as <see cref="SourcePrefab"/>, filed under the name an import would write
        /// rather than under a name of its own.
        /// </summary>
        GameObject SourcePrefabNamed(string name, float size)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeImportTestTemp");
            }

            var root = new GameObject(name);
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = Vector3.one * size;
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                root, $"{TempFolder}/{name}.prefab");

            Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>
        /// A prefab whose art measures <paramref name="size"/> across and sits
        /// <paramref name="offset"/> from its own pivot, which is how a great deal of bought art
        /// arrives.
        /// </summary>
        GameObject OffsetSourcePrefab(string name, float size, Vector3 offset)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeImportTestTemp");
            }

            var root = new GameObject(name);
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = Vector3.one * size;
            cube.transform.localPosition = offset;
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                root, $"{TempFolder}/{name}_source.prefab");

            Object.DestroyImmediate(root);

            return prefab;
        }

        /// <summary>
        /// A prefab whose art measures <paramref name="size"/> across, modelled the way imported art
        /// usually is: a plain root with the mesh on a child under it.
        /// </summary>
        /// <remarks>
        /// The shape matters to what is being tested. CatalogSync measures a prefab in its own root
        /// space, so a mesh on a child is measured through that child's transform and a mesh on the
        /// root is measured through the root's own scale — see
        /// <see cref="TheMeasurementCountsTheRootsOwnScale"/>.
        /// </remarks>
        /// <summary>
        /// A prefab whose art is modelled straight onto its root, which is the shape that used to
        /// come out of an import flat and could not then be turned.
        /// </summary>
        GameObject FlatSourcePrefab(string name, float size)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeImportTestTemp");
            }

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.localScale = Vector3.one * size;
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                cube, $"{TempFolder}/{name}_source.prefab");

            Object.DestroyImmediate(cube);

            return prefab;
        }

        GameObject SourcePrefab(string name, float size, bool withCollider = false)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "ArenaForgeImportTestTemp");
            }

            var root = new GameObject(name);
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = Vector3.one * size;

            if (!withCollider)
            {
                Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                root, $"{TempFolder}/{name}_source.prefab");

            Object.DestroyImmediate(root);

            return prefab;
        }
    }
}
