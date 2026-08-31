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
        public ImportedAsset(
            GameObject prefab,
            float measured,
            float corrected,
            bool collider,
            float recentred = 0f,
            bool replaced = false)
        {
            Prefab = prefab;
            Measured = measured;
            Corrected = corrected;
            ColliderAdded = collider;
            Recentred = recentred;
            Replaced = replaced;
        }

        /// <summary>The prefab that was written.</summary>
        public GameObject Prefab { get; }

        /// <summary>How long the source art measured along its longest horizontal axis, in metres.</summary>
        public float Measured { get; }

        /// <summary>What that axis measures once imported. Equal to <see cref="Measured"/> when nothing was corrected.</summary>
        public float Corrected { get; }

        /// <summary>Whether the art gained a box collider the source did not have.</summary>
        public bool ColliderAdded { get; }

        /// <summary>How far the art was moved on the ground plane to sit over the root, in metres.</summary>
        /// <remarks>
        /// Zero for art already modelled over its own pivot, which is most of it. What it measures
        /// when it is not zero is the radius the piece used to swing through when somebody turned
        /// it — see <see cref="ArenaAssetImport.Centre"/>.
        /// </remarks>
        public float Recentred { get; }

        /// <summary>Whether this was written over a prefab that was already there.</summary>
        public bool Replaced { get; }

        /// <summary>Whether the size was changed.</summary>
        public bool Rescaled => Corrected != Measured;

        /// <summary>Whether the art was moved over the root.</summary>
        public bool Centred => Recentred > 0f;

        /// <inheritdoc />
        public override string ToString() => Rescaled
            ? $"{Prefab.name}: {Measured.ToString("0.###", CultureInfo.InvariantCulture)} m -> " +
              $"{Corrected.ToString("0.###", CultureInfo.InvariantCulture)} m"
            : $"{Prefab.name}: {Measured.ToString("0.###", CultureInfo.InvariantCulture)} m";
    }

    /// <summary>
    /// Turns art somebody else modelled into art this tool can place: an empty root with the source
    /// nested under it, a box collider fitted where there was none, and its size corrected to the
    /// module it was plainly meant to be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A wrapper rather than a copy.</strong> The source prefab is left exactly as it was
    /// and the workspace holds a prefab that nests it, so an art pack can be updated in place and the
    /// change follows through. Copying the prefab would double every mesh reference in the project
    /// and make the two versions a synchronisation problem for as long as both exist.
    /// </para>
    /// <para>
    /// <strong>An empty root, and that is the point of the wrapper.</strong> This wrote a prefab
    /// variant until somebody tried to turn one. A variant's root <em>is</em> the source's root, so
    /// art modelled straight onto a prefab root came out of here flat — mesh and collider on the
    /// imported root, nothing underneath — and <see cref="MeshRotation"/> has to refuse that, because
    /// the only thing it could turn is the root the generator poses through. A root with nothing on
    /// it is at rotation zero and scale one because there is nothing to put it anywhere else, and it
    /// always has exactly one child to turn. See <see cref="ArtName"/>.
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
    /// <strong>The correction goes on the <c>Art</c> child.</strong> One scale on one transform,
    /// where it used to be a scale and a shifted position on each of the source's own children — and
    /// it works whatever shape the source is, including art modelled straight onto its root, which
    /// used to be reported unchanged for want of anywhere to put a scale. The imported prefab's own
    /// root stays at one, which is what the catalog measures through and what the realiser poses.
    /// </para>
    /// <para>
    /// <strong>The tolerance is small by default and the user's to raise.</strong> Nothing in the
    /// number can tell a mis-measured 4.70 that was meant to be five from a 2.30 that was meant to be
    /// 2.30, and rounding the second is a 30 cm distortion that makes runs worse rather than better.
    /// Five centimetres is larger than modelling slop and smaller than any deliberate size, so the
    /// default corrects the first kind and declines the second. Everything declined is reported.
    /// </para>
    /// <para>
    /// <strong>The art is centred over the root, and that is what makes a turn a turn.</strong> A
    /// pose is a position and a yaw about the root, so art modelled off its own pivot does not spin
    /// where it stands when it is turned — it swings round the pivot at whatever radius the modeller
    /// left. That is not a hypothetical: the stone boundary panel in this project's own workspace
    /// measured its art sixteen metres off its pivot, so a quarter turn of it moved the wall the
    /// best part of a lane. See <see cref="Centre"/>, which also says why it is X and Z only.
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

        /// <summary>What the nested source is called inside an imported prefab.</summary>
        /// <remarks>
        /// One name for every import, so a person opening two of them finds the same thing in the
        /// same place, and so a tool that walks the children has something to say in a message.
        /// </remarks>
        public const string ArtName = "Art";

        /// <summary>
        /// Writes a wrapped copy of each source prefab into <paramref name="folder"/>, fitting a
        /// collider and correcting the size where it is within tolerance of a module.
        /// </summary>
        /// <param name="sources">Prefabs to import. Nulls are skipped.</param>
        /// <param name="folder">Project folder the variants are written to. Created if missing.</param>
        /// <param name="module">Size the longest horizontal axis is snapped to a multiple of.</param>
        /// <param name="tolerance">How far that axis may be moved, in metres. Zero corrects nothing.</param>
        /// <param name="overwrite">
        /// Whether a prefab already standing at the name being written is replaced rather than
        /// written beside. See <see cref="TargetPath"/>.
        /// </param>
        public static List<ImportedAsset> Import(
            IReadOnlyList<GameObject> sources,
            string folder,
            float module = DefaultModule,
            float tolerance = DefaultTolerance,
            bool overwrite = false)
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

                ImportedAsset? one = ImportOne(source, folder, module, tolerance, overwrite);
                if (one.HasValue)
                {
                    imported.Add(one.Value);
                }
            }

            AssetDatabase.SaveAssets();
            return imported;
        }

        static ImportedAsset? ImportOne(
            GameObject source, string folder, float module, float tolerance, bool overwrite)
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

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (model == null)
            {
                return null;
            }

            var root = new GameObject(source.name);

            try
            {
                model.name = ArtName;
                model.transform.SetParent(root.transform, false);

                // Held at one while the collider is fitted, so what is measured is the art in its
                // own space — which is the space a box on that same transform is written in.
                // CatalogSync answers in the space the art stands in, its own scale included, so
                // measuring at the final size and then hanging the box on the transform carrying
                // that size would count the scale twice. The scale goes on afterwards and takes the
                // collider with it, which is also what keeps the two together under a rotation.
                Vector3 own = model.transform.localScale;
                model.transform.localScale = Vector3.one;

                bool addedCollider = false;
                if (CatalogSync.HasNoBoxCollider(source) &&
                    CatalogSync.TryMeasureArt(model, out Bounds fitted))
                {
                    var box = model.AddComponent<BoxCollider>();
                    box.center = fitted.center;
                    box.size = fitted.size;
                    addedCollider = true;
                }

                bool rescaled = wanted && scale > 0f;
                model.transform.localScale = rescaled ? own * scale : own;

                // Last, and after the scale, because what has to end up over the root is the art at
                // the size it will be standing at rather than the size it arrived as.
                float recentred = Centre(root, model.transform);

                string path = TargetPath(source, folder, overwrite, out bool replaced);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);

                return prefab == null
                    ? (ImportedAsset?)null
                    : new ImportedAsset(
                        prefab, measured, rescaled ? target : measured, addedCollider,
                        recentred, replaced);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Slides the art sideways under its root until its measured centre is over the root's
        /// origin, and returns how far it had to go.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>What this is for is the rotation, not the tidiness.</strong> A pose puts a pivot
        /// somewhere and turns the root about it; art modelled off that pivot therefore travels an
        /// arc of its own offset every time somebody turns it, which reads as a piece flying off
        /// across the map rather than turning. Nothing downstream can fix that — the generator, the
        /// realiser and the rotate handle are all correct about a pivot the modeller put in the
        /// wrong place — so it is fixed here, once, where the art comes in.
        /// </para>
        /// <para>
        /// <strong>X and Z, and deliberately not Y.</strong> How far a piece reaches below its own
        /// pivot is not slop to be corrected: it is the one fact
        /// <see cref="ArenaForge.Core.CatalogEntry.BaseOffset"/> records and every placement stage
        /// adds back, and <see cref="ArenaForge.Core.PerimeterFence"/> plants a boundary panel on
        /// its pivot exactly so that the deep foundation modelled under it goes under the ground.
        /// Centring the height would bury half of every wall and leave the catalog describing art
        /// that no longer matches the convention the rest of the tool places by. The yaw is about Y,
        /// so the ground plane is the whole of what the orbit is made of anyway.
        /// </para>
        /// <para>
        /// Measured off the finished root rather than off the source, so it accounts for the
        /// source's own root offset, the correction scale and anything the source has turned inside
        /// itself — one measurement of the thing that is about to be written.
        /// </para>
        /// </remarks>
        static float Centre(GameObject root, Transform art)
        {
            if (!CatalogSync.TryMeasureArt(root, out Bounds whole))
            {
                return 0f;
            }

            var offset = new Vector3(whole.center.x, 0f, whole.center.z);
            if (offset == Vector3.zero)
            {
                return 0f;
            }

            art.localPosition -= offset;
            return offset.magnitude;
        }

        /// <summary>
        /// Where an import writes, and whether it is replacing a prefab that was already there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Overwriting is what makes a re-import a re-import.</strong> Writing beside is the
        /// safe default and the wrong one for the thing people actually do with this: fix the art,
        /// import it again, and find <c>Barrel 1</c> beside <c>Barrel</c> with every catalog row,
        /// every saved map and every scene still pointing at the old one. Saving over the path keeps
        /// the asset's own GUID, so the row and the map follow the fix instead of being orphaned by
        /// it.
        /// </para>
        /// <para>
        /// <strong>Never over the art being imported.</strong> A source filed in the folder it is
        /// being imported into is somebody importing in place, and replacing it there would write a
        /// wrapper round a model over the model the wrapper nests — art that refers to itself. That
        /// one falls back to a fresh name whatever the toggle says.
        /// </para>
        /// </remarks>
        static string TargetPath(
            GameObject source, string folder, bool overwrite, out bool replaced)
        {
            replaced = false;
            string path = $"{folder}/{source.name}.prefab";

            if (!overwrite ||
                AssetDatabase.LoadAssetAtPath<GameObject>(path) == null ||
                string.Equals(
                    AssetDatabase.GetAssetPath(source), path, System.StringComparison.Ordinal))
            {
                return AssetDatabase.GenerateUniqueAssetPath(path);
            }

            replaced = true;
            return path;
        }

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
        bool _overwrite;
        string _result;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        [MenuItem("Tools/ArenaForge/Import Art…")]
        public static void Open()
        {
            var window = GetWindow<ArenaAssetImportWindow>(true, "Import Art", true);
            window.minSize = new Vector2(420f, 250f);
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
            _overwrite = EditorGUILayout.Toggle(
                new GUIContent(
                    "Overwrite existing",
                    "Write over a prefab of the same name in this folder instead of writing a " +
                    "second one beside it. The asset keeps its GUID, so catalog rows, saved maps " +
                    "and scenes follow the re-import."),
                _overwrite);

            EditorGUILayout.HelpBox(
                $"A piece whose longest horizontal axis is within {_tolerance:0.###} m of a " +
                $"multiple of {_module:0.###} m is scaled onto it. Zero tolerance corrects nothing." +
                "  The art is centred over the root on X and Z, so turning it spins it in place." +
                "  The folder decides where the generator may place this art, so pick the " +
                "prop folder that says so rather than the root.",
                MessageType.None);

            if (_overwrite)
            {
                EditorGUILayout.HelpBox(
                    "Re-importing replaces the prefab of the same name in this folder. Its rows " +
                    "will want re-syncing afterwards, because centring and any size correction " +
                    "move what the catalog measured.",
                    MessageType.Warning);
            }

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
                ArenaAssetImport.Import(sources, _folder, _module, _tolerance, _overwrite);

            var corrected = 0;
            var collidered = 0;
            var centred = 0;
            var replaced = 0;

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

                if (imported[i].Centred)
                {
                    centred++;
                }

                if (imported[i].Replaced)
                {
                    replaced++;
                }
            }

            // What was declined is as much of the answer as what was done: a piece too far off a
            // module to correct is a piece somebody chose that size, and the report is where that
            // shows.
            _result = imported.Count == 0
                ? $"Nothing was imported into {_folder}."
                : $"{imported.Count} of {sources.Count} imported into {_folder}. " +
                  $"{corrected} corrected to size, {imported.Count - corrected} left as measured, " +
                  $"{collidered} given a collider, {centred} centred over their root" +
                  (replaced > 0 ? $", {replaced} written over the prefab already there." : ".");

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
                objects[i] = imported[i].Prefab;
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
