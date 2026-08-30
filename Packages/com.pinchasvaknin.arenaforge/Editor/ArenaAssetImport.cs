using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// What one prefab's import did.
    /// </summary>
    public readonly struct ImportedAsset
    {
        /// <summary>Creates a result.</summary>
        public ImportedAsset(GameObject variant, float measured, float corrected, bool collider)
        {
            Variant = variant;
            Measured = measured;
            Corrected = corrected;
            ColliderAdded = collider;
        }

        /// <summary>The variant that was written.</summary>
        public GameObject Variant { get; }

        /// <summary>How long the source art measured along its longest horizontal axis, in metres.</summary>
        public float Measured { get; }

        /// <summary>What that axis measures on the variant. Equal to <see cref="Measured"/> when nothing was corrected.</summary>
        public float Corrected { get; }

        /// <summary>Whether the variant gained a box collider the source did not have.</summary>
        public bool ColliderAdded { get; }

        /// <summary>Whether the size was changed.</summary>
        public bool Rescaled => Corrected != Measured;

        /// <inheritdoc />
        public override string ToString() => Rescaled
            ? $"{Variant.name}: {Measured.ToString("0.###", CultureInfo.InvariantCulture)} m -> " +
              $"{Corrected.ToString("0.###", CultureInfo.InvariantCulture)} m"
            : $"{Variant.name}: {Measured.ToString("0.###", CultureInfo.InvariantCulture)} m";
    }

    /// <summary>
    /// Turns art somebody else modelled into art this tool can place: a prefab variant of it, with a
    /// box collider fitted where there was none, and its size corrected to the module it was plainly
    /// meant to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A variant rather than a copy.</strong> The source prefab is left exactly as it was and
    /// the workspace holds a variant of it, so an art pack can be updated in place and the overrides
    /// this adds follow. Copying the prefab would double every mesh reference in the project and make
    /// the two versions a synchronisation problem for as long as both exist.
    /// </para>
    /// <para>
    /// <strong>Nothing here authors geometry.</strong> That is <see cref="ArenaAssetBuilder"/>'s job
    /// and it is the only place in the package that does it. This composes: an existing prefab, a
    /// collider fitted to the art already there, and a uniform scale on what the prefab contains.
    /// </para>
    /// <para>
    /// <strong>The size correction is for measurement error and nothing else.</strong> A fence panel
    /// that measures 0.97 m was meant to be a metre, and every run tiled from it carries three
    /// centimetres of slack per panel that a person can see. Snapping it to the metre fixes the art
    /// once, at import, where the fault is — rather than in the generator, where scaling a piece to
    /// fill a gap was measured and does not work: it fires on almost nothing, because a leftover is
    /// only near a piece's own length by coincidence.
    /// </para>
    /// <para>
    /// <strong>The correction goes on the prefab's children.</strong> A scale on the root cancels
    /// out of the measurement <c>CatalogSync</c> takes and does not cancel out of what a realised
    /// instance stands at, so putting it there would make the catalog disagree with the map. Art
    /// modelled straight onto its root has nowhere to carry a correction and is reported unchanged.
    /// </para>
    /// <para>
    /// <strong>The tolerance is small by default and the user's to raise.</strong> Nothing in the
    /// number can tell a mis-measured 4.70 that was meant to be five from a 2.30 that was meant to be
    /// 2.30, and rounding the second is a 30 cm distortion that makes runs worse rather than better.
    /// Five centimetres is larger than modelling slop and smaller than any deliberate size, so the
    /// default corrects the first kind and declines the second. Everything declined is reported.
    /// </para>
    /// </remarks>
    public static class ArenaAssetImport
    {
        /// <summary>Module a corrected size is snapped to by default, in metres.</summary>
        /// <remarks>
        /// The metre, because that is the grid the rest of the tool is laid out on — the placement
        /// cell, the starter floor tile, the starter fence. Art meant to sit in a run of this tool's
        /// making was meant to be a whole number of them.
        /// </remarks>
        public const float DefaultModule = 1f;

        /// <summary>How far a size may be corrected, in metres.</summary>
        /// <remarks>
        /// Five centimetres: larger than the slop a modeller leaves and smaller than any size
        /// somebody chose on purpose. Raising it starts correcting art that is the size it meant to
        /// be, which is why it is a parameter and not a constant.
        /// </remarks>
        public const float DefaultTolerance = 0.05f;

        /// <summary>
        /// Writes a variant of each source prefab into <paramref name="folder"/>, fitting a collider
        /// and correcting the size where it is within tolerance of a module.
        /// </summary>
        /// <param name="sources">Prefabs to import. Nulls are skipped.</param>
        /// <param name="folder">Project folder the variants are written to. Created if missing.</param>
        /// <param name="module">Size the longest horizontal axis is snapped to a multiple of.</param>
        /// <param name="tolerance">How far that axis may be moved, in metres. Zero corrects nothing.</param>
        public static List<ImportedAsset> Import(
            IReadOnlyList<GameObject> sources,
            string folder,
            float module = DefaultModule,
            float tolerance = DefaultTolerance)
        {
            var imported = new List<ImportedAsset>();
            if (sources == null || string.IsNullOrEmpty(folder))
            {
                return imported;
            }

            EnsureFolder(folder);

            for (int i = 0; i < sources.Count; i++)
            {
                GameObject source = sources[i];
                if (source == null)
                {
                    continue;
                }

                ImportedAsset? one = ImportOne(source, folder, module, tolerance);
                if (one.HasValue)
                {
                    imported.Add(one.Value);
                }
            }

            AssetDatabase.SaveAssets();
            return imported;
        }

        static ImportedAsset? ImportOne(
            GameObject source, string folder, float module, float tolerance)
        {
            // The art as modelled, not as somebody's collider describes it. A collider is a size a
            // person chose and may already be the rounded one; the mesh is what a run will be
            // measured against once this is done. See CatalogSync.TryMeasureArt.
            if (!CatalogSync.TryMeasureArt(source, out Bounds art))
            {
                return null;
            }

            float measured = Mathf.Max(art.size.x, art.size.z);
            bool wanted = TryCorrection(measured, module, tolerance, out float scale, out float target);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
            {
                return null;
            }

            try
            {
                bool rescaled = wanted && TryRescale(instance.transform, scale);

                // Fitted after the correction and measured off the instance, so the collider is the
                // box round the art as it now stands. Fitting it first and scaling afterwards would
                // leave a collider on the root that the child scaling never touched.
                bool addedCollider = false;
                if (CatalogSync.HasNoBoxCollider(source) &&
                    CatalogSync.TryMeasureArt(instance, out Bounds fitted))
                {
                    var box = instance.AddComponent<BoxCollider>();
                    box.center = fitted.center;
                    box.size = fitted.size;
                    addedCollider = true;
                }

                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.name}.prefab");
                GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, path);

                return variant == null
                    ? (ImportedAsset?)null
                    : new ImportedAsset(
                        variant, measured, rescaled ? target : measured, addedCollider);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Applies a uniform scale to a prefab's contents, and reports whether there was anywhere to
        /// put it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>On the children, never on the root.</strong> <c>CatalogSync</c> measures a prefab
        /// in its own root space, so a scale on the root itself cancels out of the measurement — see
        /// <c>ArenaAssetImportTests.TheMeasurementIgnoresTheRootsOwnScale</c> — while a realised
        /// instance plainly carries it. Correcting the root would leave the row saying one size and
        /// the map standing at another, which is the one kind of wrong this whole pipeline exists to
        /// prevent.
        /// </para>
        /// <para>
        /// Positions as well as scales, because a uniform scale about the root's origin moves what is
        /// offset from it. Scaling the pieces without moving them apart would resize each one and
        /// leave the gaps between them, which is a different shape rather than a bigger one.
        /// </para>
        /// <para>
        /// <strong>Art modelled straight onto the root is refused.</strong> There is no child to
        /// carry the correction and the root cannot, so the size is reported as it was measured and
        /// left alone. Saying so is the point: the alternative is a silent disagreement between the
        /// catalog and the scene.
        /// </para>
        /// </remarks>
        static bool TryRescale(Transform root, float scale)
        {
            if (root.childCount == 0)
            {
                return false;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                child.localPosition *= scale;
                child.localScale *= scale;
            }

            return true;
        }

        /// <summary>
        /// The uniform scale that snaps a measured size onto the nearest multiple of a module, and
        /// whether it is worth applying.
        /// </summary>
        /// <remarks>
        /// <para>
        /// False rather than a scale of one when there is nothing to do, so a caller can report the
        /// art it declined to touch. The three ways to get there are worth telling apart in a log:
        /// the size is already a multiple, the nearest multiple is further away than the tolerance,
        /// or the art is smaller than half a module and the nearest multiple is nothing at all.
        /// </para>
        /// <para>
        /// The last is not an edge case. A door handle measured at 0.1 m has a nearest metre of zero,
        /// and a scale of zero is not a correction — it is art that disappears. Anything under half a
        /// module is left alone, which is the same answer as "this was not modelled to your grid".
        /// </para>
        /// </remarks>
        internal static bool TryCorrection(
            float measured, float module, float tolerance, out float scale, out float target)
        {
            scale = 1f;
            target = measured;

            if (!(measured > 0f) || !(module > 0f) || tolerance < 0f)
            {
                return false;
            }

            float nearest = Mathf.Round(measured / module) * module;
            if (!(nearest > 0f))
            {
                return false;
            }

            if (Mathf.Abs(nearest - measured) > tolerance || nearest == measured)
            {
                return false;
            }

            target = nearest;
            scale = nearest / measured;
            return true;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string path = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(path, parts[i]);
                }

                path = next;
            }
        }
    }
}
