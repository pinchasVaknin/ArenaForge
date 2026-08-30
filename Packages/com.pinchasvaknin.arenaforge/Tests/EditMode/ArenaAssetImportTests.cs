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
        /// The whole import in one pass: a variant of the source rather than a copy of it, standing
        /// at the size the correction decided, carrying a collider the source did not have. The
        /// variant is checked to be a variant, because a copy would look identical here and would be
        /// a second asset to keep in step for as long as both existed.
        /// </remarks>
        [Test]
        public void ImportingUndersizedArtWritesACorrectedVariantWithACollider()
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

            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(result.Variant), Is.EqualTo(source),
                "the variant should be a variant of the source, not a copy of it");

            var renderer = result.Variant.GetComponentInChildren<MeshRenderer>();
            Assert.That(renderer.bounds.size.x, Is.EqualTo(1f).Within(1e-3f),
                "the corrected art should measure the module it was snapped to");

            var box = result.Variant.GetComponentInChildren<BoxCollider>();
            Assert.That(box, Is.Not.Null);
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

            var renderer = imported[0].Variant.GetComponentInChildren<MeshRenderer>();
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
            Assert.That(imported[0].Variant.GetComponentsInChildren<BoxCollider>().Length,
                Is.EqualTo(1));
        }

        /// <summary>
        /// A prefab whose art measures <paramref name="size"/> across, modelled the way imported art
        /// usually is: a plain root with the mesh on a child under it.
        /// </summary>
        /// <remarks>
        /// The shape matters to what is being tested. CatalogSync measures a prefab in its own root
        /// space, so a mesh on a child is measured through that child's transform and a mesh on the
        /// root is measured through nothing at all — see
        /// <see cref="TheMeasurementIgnoresTheRootsOwnScale"/>.
        /// </remarks>
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
