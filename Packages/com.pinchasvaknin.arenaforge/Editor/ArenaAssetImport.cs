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
    /// <strong>The correction goes on the prefab's children.</strong> Art modelled straight onto
    /// its root has nowhere to carry one and is reported unchanged. The root itself could carry it
    /// now that <c>CatalogSync</c> counts a root's own scale and <c>WorldRealizer</c> composes it,
    /// which it did not when this was written; putting it there is a change to make on purpose
    /// rather than a line to quietly delete, and it is in <c>FUTURE.md</c>.
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
        /// <strong>On the children, never on the root.</strong> The reason it was written that way
        /// has since gone: a scale on the root cancelled out of the measurement <c>CatalogSync</c>
        /// took, so a correction put there would have left the row saying one size and the map
        /// standing at another. The measurement counts it now, and <c>WorldRealizer</c> composes it
        /// rather than overwriting it — see
        /// <c>ArenaAssetImportTests.TheMeasurementCountsTheRootsOwnScale</c>. What keeps the
        /// correction on the children today is only that nothing has moved it.
        /// </para>
        /// <para>
        /// Positions as well as scales, because a uniform scale about the root's origin moves what is
        /// offset from it. Scaling the pieces without moving them apart would resize each one and
        /// leave the gaps between them, which is a different shape rather than a bigger one.
        /// </para>
        /// <para>
        /// <strong>Art modelled straight onto the root is refused.</strong> There is no child to
        /// carry the correction, so the size is reported as it was measured and left alone. Saying
        /// so is the point: a size silently declined is worse than one visibly declined.
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

    /// <summary>
    /// The window behind <c>Tools/ArenaForge/Import Art…</c>: turns the prefabs selected in the
    /// Project window into fitted, size-corrected variants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ArenaAssetImport"/> had no way in but a script call, which meant the one piece of
    /// the pipeline aimed squarely at somebody else's art pack was the one piece nobody could reach.
    /// The window adds nothing to what it does; it is the folder, the module and the tolerance,
    /// which are the three things <see cref="ArenaAssetImport.Import"/> cannot guess.
    /// </para>
    /// <para>
    /// <strong>The selection is the input, as it is for Merge to Prefab and Wrap Object.</strong>
    /// Importing art begins with picking art out of the Project window, so the window reads what is
    /// selected rather than keeping a list of its own for somebody to fill in twice.
    /// </para>
    /// </remarks>
    sealed class ArenaAssetImportWindow : EditorWindow
    {
        /// <summary>Where the folder field starts, which is not where art should end up.</summary>
        /// <remarks>
        /// The workspace's prop root, so the browser opens where the answer is — and not one of the
        /// folders under it, because which one is the whole question. A folder below <c>Props</c>
        /// does not say what a piece of art is, it says where the generator may put it, and only
        /// the person importing knows that. Written straight into <c>Props</c> the art syncs into
        /// no tag the placement stages query, which is why the field is a starting point rather
        /// than a default worth accepting.
        /// </remarks>
        static readonly string DefaultFolder = $"{ArenaWorkspace.DefaultRoot}/Props";

        string _folder = DefaultFolder;
        float _module = ArenaAssetImport.DefaultModule;
        float _tolerance = ArenaAssetImport.DefaultTolerance;
        string _result;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        [MenuItem("Tools/ArenaForge/Import Art…")]
        public static void Open()
        {
            var window = GetWindow<ArenaAssetImportWindow>(true, "Import Art", true);
            window.minSize = new Vector2(420f, 210f);
            window.Show();
        }

        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            List<GameObject> sources = Sources();

            EditorGUILayout.HelpBox(
                sources.Count == 0
                    ? "Select the prefabs to import in the Project window."
                    : $"{sources.Count} prefab(s) will be imported as variants, fitted with a " +
                      "collider and corrected to size.",
                sources.Count == 0 ? MessageType.Info : MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField("Folder", _folder);

                if (GUILayout.Button("Browse…", GUILayout.Width(70f)))
                {
                    Browse();
                }
            }

            _module = EditorGUILayout.FloatField("Module", _module);
            _tolerance = EditorGUILayout.FloatField("Tolerance", _tolerance);

            EditorGUILayout.HelpBox(
                $"A piece whose longest horizontal axis is within {_tolerance:0.###} m of a " +
                $"multiple of {_module:0.###} m is scaled onto it. Zero tolerance corrects nothing." +
                "  The folder decides where the generator may place this art, so pick the " +
                "prop folder that says so rather than the root.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(
                       sources.Count == 0 || string.IsNullOrEmpty(_folder)))
            {
                if (GUILayout.Button("Import"))
                {
                    Run(sources);
                }
            }

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.HelpBox(_result, MessageType.Info);
            }
        }

        /// <summary>The prefab assets among the selection, in selection order.</summary>
        static List<GameObject> Sources()
        {
            Object[] chosen = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
            var sources = new List<GameObject>(chosen.Length);

            for (int i = 0; i < chosen.Length; i++)
            {
                if (chosen[i] is GameObject asset && PrefabUtility.IsPartOfPrefabAsset(asset))
                {
                    sources.Add(asset);
                }
            }

            return sources;
        }

        void Run(List<GameObject> sources)
        {
            List<ImportedAsset> imported =
                ArenaAssetImport.Import(sources, _folder, _module, _tolerance);

            var corrected = 0;
            var collidered = 0;

            for (int i = 0; i < imported.Count; i++)
            {
                if (imported[i].Rescaled)
                {
                    corrected++;
                }

                if (imported[i].ColliderAdded)
                {
                    collidered++;
                }
            }

            // What was declined is as much of the answer as what was done: a piece too far off a
            // module to correct is a piece somebody chose that size, and the report is where that
            // shows.
            _result = imported.Count == 0
                ? $"Nothing was imported into {_folder}."
                : $"{imported.Count} of {sources.Count} imported into {_folder}. " +
                  $"{corrected} corrected to size, {imported.Count - corrected} left as measured, " +
                  $"{collidered} given a collider.";

            if (imported.Count > 0)
            {
                Selection.objects = Variants(imported);
            }
        }

        static Object[] Variants(List<ImportedAsset> imported)
        {
            var objects = new Object[imported.Count];
            for (int i = 0; i < imported.Count; i++)
            {
                objects[i] = imported[i].Variant;
            }

            return objects;
        }

        void Browse()
        {
            string start = AssetDatabase.IsValidFolder(_folder) ? _folder : "Assets";
            string chosen = EditorUtility.OpenFolderPanel("Folder for the imported art", start, string.Empty);

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            string folder = ArenaWorkspace.ProjectFolder(chosen);
            if (folder == null)
            {
                EditorUtility.DisplayDialog(
                    "Import Art",
                    $"'{chosen}' is outside this project, so art cannot be written there. " +
                    "Choose a folder under Assets.",
                    "OK");

                return;
            }

            _folder = folder;
            GUI.FocusControl(null);
        }
    }
}
