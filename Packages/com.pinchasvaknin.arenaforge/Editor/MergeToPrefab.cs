using System;
using System.Collections.Generic;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Turns a group of objects standing in a scene into one prefab, and files it in the catalog so
    /// the generator can place it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thing a person actually builds is rarely one prefab. A desk with a monitor on it and a
    /// chair pulled up to it is a workstation, and a generator that places its three pieces
    /// separately will put the chair through the desk and the monitor on the floor — because
    /// nothing in the catalog says they belong together. Arranging them by hand in a scene is the
    /// natural way to say it, and this is the button that turns the arrangement into a row.
    /// </para>
    /// <para>
    /// <strong>What it does is the workflow, not a new kind of art.</strong> The prefab that comes
    /// out is an ordinary prefab in an ordinary workspace folder, and the row that comes out is the
    /// row <see cref="CatalogSync"/> would have written for it — same tags off the same folder,
    /// same measurement off the same colliders. Pressing Sync from Folders afterwards finds it and
    /// changes nothing. That is the point: this saves the four steps, it does not add a fifth kind
    /// of thing to the project.
    /// </para>
    /// <para>
    /// <strong>The pivot goes at the average of the selection.</strong> Not the centre of the box
    /// round it, which drifts towards whichever piece is largest, and not the first object's pivot,
    /// which puts the group's origin inside the desk. The average of what the person placed is the
    /// middle of what the person was looking at — and the catalog measures the footprint off the
    /// art afterwards anyway, so the pivot decides where the group is <em>held</em> rather than how
    /// big it is said to be. See <see cref="CatalogAsset.Row.FootprintOffset"/>.
    /// </para>
    /// <para>
    /// <strong>The pieces keep their collision and the root gets a trigger.</strong> A table and
    /// four chairs is mostly air, so one solid box round the group would wall off the gaps a player
    /// walks through. What the merge adds is a single trigger box on the root round the whole of the
    /// art — a footprint for the catalog and the clearance rules to read, which stops nothing. See
    /// <see cref="EncloseInATrigger"/>.
    /// </para>
    /// <para>
    /// <strong>And a mesh that nothing blocks for gets a box of its own.</strong> Half a bought art
    /// pack ships with no colliders at all, and a group made of it came out of the merge as scenery
    /// a player walks straight through — the root's trigger is a measurement and stops nothing by
    /// design. Every mesh with no solid collider over it is given one at its own bounds. See
    /// <see cref="FitMissingHitboxes"/>.
    /// </para>
    /// </remarks>
    static class MergeToPrefab
    {
        /// <summary>Workspace folder a merged group is written into unless another is chosen.</summary>
        /// <remarks>
        /// The centrepiece folder, because a group somebody arranged by hand is nearly always the
        /// thing a room is arranged around — see
        /// <see cref="ArenaForge.Core.BuildingGenerator.CentrepieceTag"/>. The interior cover folder
        /// is the other likely home and is offered beside it. Neither is a restriction: the window
        /// browses the project, and these two are shortcuts to the answer that is usually right.
        /// </remarks>
        public const string DefaultFolder = "Props/PropBuilding/Decor/Centerpieces";

        /// <summary>The other folder offered as a shortcut.</summary>
        public const string CoverFolder = "Props/PropBuilding/Decor/InteriorCovers";

        /// <summary>
        /// Merges <paramref name="selection"/> under one pivot, writes it as a prefab and binds a
        /// catalog row to it.
        /// </summary>
        /// <param name="selection">Objects standing in a scene. Nested selections are collapsed.</param>
        /// <param name="folder">Project-relative folder to write the prefab into.</param>
        /// <param name="name">File name for the prefab, without an extension.</param>
        /// <param name="catalog">The catalog to bind the new row into.</param>
        /// <returns>The prefab asset that was written.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Nothing was selected, something selected is an asset rather than a scene object, the
        /// name or folder is blank, or Unity could not write the prefab.
        /// </exception>
        public static GameObject Merge(
            IReadOnlyList<GameObject> selection, string folder, string name, CatalogAsset catalog)
        {
            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("A merged prefab needs a name.");
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException("A merged prefab needs somewhere to be written.");
            }

            List<GameObject> roots = Roots(selection);
            if (roots.Count == 0)
            {
                throw new InvalidOperationException(
                    "Select the objects to merge in the scene first — a prefab asset in the " +
                    "project cannot be merged, only an instance of one in a scene.");
            }

            // Before anything is created. A folder that spells no tags is a refusal, and a refusal
            // that arrives after the prefab has been written leaves an orphan in the project and
            // the person's scene rearranged for nothing.
            RequireTags(catalog, $"{folder.TrimEnd('/')}/{name}.prefab");

            ArenaWorkspace.EnsureFolder(folder);

            var parent = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(parent, UndoName);
            parent.transform.position = Pivot(roots);

            // Reparented one at a time and through Undo, which keeps each object's world position
            // as it does in the hierarchy window. The order is the selection's, so the children of
            // the prefab read the way the person picked them.
            for (int i = 0; i < roots.Count; i++)
            {
                Undo.SetTransformParent(roots[i].transform, parent.transform, UndoName);
            }

            FitMissingHitboxes(parent);
            EncloseInATrigger(parent);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/{name}.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                parent, path, InteractionMode.UserAction, out bool ok);

            if (!ok || prefab == null)
            {
                throw new InvalidOperationException($"Unity could not write a prefab to '{path}'.");
            }

            Bind(catalog, prefab, path);
            return prefab;
        }

        /// <summary>
        /// Gives a solid box to every mesh in the group that nothing is already blocking for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A mesh with no collider anywhere over it is scenery a player walks through.</strong>
        /// Half a bought art pack ships that way — the pieces render and stop nothing — and until now
        /// a merge preserved that faithfully, because preserving the pieces' collision is what it was
        /// asked to do and there was none to preserve. The root's trigger does not cover for it: a
        /// trigger is a measurement and blocks nothing by design, so a group of collider-less art came
        /// out of the merge exactly as walk-through as it went in. Each such mesh gets a box at its own
        /// bounds, on its own object, which keeps the shape of the group the shape of its parts.
        /// </para>
        /// <para>
        /// <strong>A collider above a mesh already answers for it.</strong> The walk goes up to the
        /// merged root rather than looking at the object alone, because
        /// <see cref="WrapObject.EncloseInABox"/> makes precisely that arrangement: a wrapped model is
        /// a solid box on the wrapper with the art's own colliders stripped out from under it, so a
        /// pass that only asked each mesh about itself would put a box back on every part the wrap
        /// deliberately cleared — a dozen colliders inside a box that already covers them, on a prop
        /// this tool stamps thirty of round a map. What is asked is whether the mesh is blocked, not
        /// whether it holds the component itself.
        /// </para>
        /// <para>
        /// <strong>Triggers do not count as an answer.</strong> A trigger is there to be measured and
        /// stops nothing, so a mesh whose only cover is one is uncovered. That is also what keeps this
        /// pass independent of <see cref="EncloseInATrigger"/> — the root's own box cannot be mistaken
        /// for collision on the way up, whichever order the two run in.
        /// </para>
        /// <para>
        /// Sized off <see cref="MeshFilter.sharedMesh"/>, which is where a box that can be sized at all
        /// gets its numbers, and in the mesh's own space — which is the space a
        /// <see cref="BoxCollider"/>'s centre and size are already in, so a part rotated or scaled
        /// inside the group needs no arithmetic to fit.
        /// </para>
        /// </remarks>
        /// <param name="parent">The new parent the selection has been reparented under.</param>
        static void FitMissingHitboxes(GameObject parent)
        {
            MeshFilter[] filters = parent.GetComponentsInChildren<MeshFilter>(true);

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 ||
                    IsBlocked(parent.transform, filters[i].transform))
                {
                    continue;
                }

                BoxCollider box = Undo.AddComponent<BoxCollider>(filters[i].gameObject);
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
        }

        /// <summary>
        /// True if a solid collider stands on <paramref name="mesh"/> or on anything between it and
        /// the merged root.
        /// </summary>
        /// <remarks>
        /// The root itself is not asked, because the only collider it carries is the trigger this
        /// tool puts there — see <see cref="EncloseInATrigger"/>.
        /// </remarks>
        static bool IsBlocked(Transform root, Transform mesh)
        {
            for (Transform at = mesh; at != null && at != root; at = at.parent)
            {
                Collider[] colliders = at.GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null && !colliders[i].isTrigger)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Puts one trigger box on the merged parent round every mesh under it, and leaves the
        /// pieces' own colliders exactly as they are.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A group is mostly air, so one solid box round it is wrong.</strong> A table with
        /// four chairs pulled up to it fills perhaps a third of the rectangle it stands in, and a
        /// single solid collider round the lot walls off the other two thirds — a player cannot
        /// walk between the chairs, cannot step into the gap at the end of the table, and cannot
        /// see why not. The pieces already carry collision somebody chose: a chair blocks where the
        /// chair is. So the merge takes nothing off them.
        /// </para>
        /// <para>
        /// <strong>The root box is still needed, and it is needed for measuring.</strong>
        /// <see cref="CatalogSync.TryMeasure"/> reads box colliders where a prefab has any, and the
        /// clearance the generator keeps round a placed piece is that rectangle — so a group with a
        /// mesh collider per part and no box on it anywhere is a group whose footprint nothing
        /// states. One box on the root states it once, for the whole arrangement.
        /// </para>
        /// <para>
        /// <strong>Which is why it is a trigger.</strong> A box that exists to be measured must not
        /// also be a wall; <c>isTrigger</c> is how a collider answers questions about its size
        /// without stopping anything that walks into it. The physical shape of the group stays what
        /// the pieces say it is, and the shape the catalog reasons about is one rectangle round
        /// them. This is the opposite bargain from <see cref="WrapObject.EncloseInABox"/>, and for
        /// the opposite input: a wrap is a raw import whose colliders are whatever the pack shipped,
        /// and a merge is an arrangement of pieces that are already finished.
        /// </para>
        /// <para>
        /// Measured off the art through <see cref="CatalogSync.TryMeasureArt"/> rather than off
        /// <see cref="CatalogSync.TryMeasure"/>, because the children keep their colliders and a
        /// measurement that read those would make the root box a copy of them rather than of the
        /// group. Where a piece's collider matches its art the two are the same box; where somebody
        /// drew one larger than the art, the row records the larger of the two — and reads back the
        /// same number on the next sync either way, which is the property the tool rests on.
        /// </para>
        /// <para>
        /// A group that renders nothing — markers, empties, a light — gets no box, for the reason
        /// <see cref="WrapObject.EncloseInABox"/> gives: a one-metre default cube round nothing is a
        /// trigger firing in an empty room.
        /// </para>
        /// </remarks>
        /// <param name="parent">The new parent the selection has been reparented under.</param>
        /// <returns>The box that was fitted, or null when the group renders nothing.</returns>
        static BoxCollider EncloseInATrigger(GameObject parent)
        {
            if (!CatalogSync.TryMeasureArt(parent, out Bounds bounds))
            {
                return null;
            }

            BoxCollider box = Undo.AddComponent<BoxCollider>(parent);
            box.center = bounds.center;
            box.size = bounds.size;
            box.isTrigger = true;

            return box;
        }

        /// <summary>
        /// Writes the catalog row for a merged prefab: the tags its folder gives it, and its own
        /// measurements.
        /// </summary>
        /// <remarks>
        /// Read through <see cref="CatalogSync"/> rather than assembled here, so a merged prefab
        /// and a synced one are described the same way — down to the folder table that turns
        /// <c>Decor/Centerpieces</c> into the tag the generator queries. A folder outside the
        /// workspace spells no tags at all, and that is reported rather than written: a row nothing
        /// can query is a row that looks right in the inspector and never appears on a map.
        /// </remarks>
        static void Bind(CatalogAsset catalog, GameObject prefab, string path)
        {
            string[] tags = RequireTags(catalog, path, out string tagPath);

            var row = new CatalogAsset.Row
            {
                LogicalId = tagPath + "/" + Slug(System.IO.Path.GetFileNameWithoutExtension(path)),
                Tags = tags,
                Weight = 1f,
                Prefab = prefab,
                Doorways = CatalogSync.DoorwaysIn(prefab),
            };

            if (CatalogSync.TryMeasure(prefab, out Bounds bounds))
            {
                CatalogSync.Apply(row, bounds);
            }

            CatalogBinding.AddOrReplace(catalog, row, UndoName);
        }

        /// <summary>
        /// The tags a prefab written to <paramref name="path"/> will carry, or a refusal naming what
        /// is wrong.
        /// </summary>
        /// <remarks>
        /// Asked twice — once before the merge and once when the row is written — because the answer
        /// is a fact about the folder, and the folder is chosen before anything is created. The
        /// second call is what supplies the tag path, and it cannot fail once the first has passed.
        /// </remarks>
        static string[] RequireTags(CatalogAsset catalog, string path) =>
            RequireTags(catalog, path, out _);

        static string[] RequireTags(CatalogAsset catalog, string path, out string tagPath)
        {
            string root = WorkspaceRootFor(catalog);
            string[] tags = CatalogSync.TagsFor(root, path, out tagPath);

            if (tags.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{path}' is not in a folder under '{root}', so nothing says what the merged " +
                    "prefab is. Write it into one of the workspace's art folders, or point the " +
                    "catalog's source folder at the workspace it belongs to.");
            }

            return tags;
        }

        /// <summary>
        /// The workspace the catalog belongs to: the folder it was last synced from, or the folder
        /// two above the catalog asset itself.
        /// </summary>
        /// <remarks>
        /// The synced folder first because it is the person's own answer to the same question, and
        /// the catalog's own path as the fallback because a workspace keeps its catalog in
        /// <c>&lt;root&gt;/Catalog</c> — so the folder above that folder is the root. A catalog
        /// somewhere else entirely gets the tags its own neighbourhood spells, which is wrong in a
        /// way the person can see rather than wrong silently.
        /// </remarks>
        static string WorkspaceRootFor(CatalogAsset catalog)
        {
            if (!string.IsNullOrWhiteSpace(catalog.SourceFolder))
            {
                return catalog.SourceFolder.TrimEnd('/');
            }

            string path = AssetDatabase.GetAssetPath(catalog);
            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string above = System.IO.Path.GetDirectoryName(folder ?? string.Empty)?.Replace('\\', '/');

            return string.IsNullOrEmpty(above) ? ArenaWorkspace.DefaultRoot : above;
        }

        /// <summary>
        /// The selected objects that are in a scene and not inside another selected object.
        /// </summary>
        /// <remarks>
        /// Both filters are there because both cases arrive from a real selection. Rubber-banding
        /// in the hierarchy picks a parent and its children together, and reparenting a child that
        /// is already going along with its parent would pull it out of the group it belongs to. A
        /// prefab asset picked in the project window is not in a scene at all and cannot be
        /// reparented anywhere.
        /// </remarks>
        static List<GameObject> Roots(IReadOnlyList<GameObject> selection)
        {
            var roots = new List<GameObject>(selection.Count);

            for (int i = 0; i < selection.Count; i++)
            {
                GameObject candidate = selection[i];
                if (candidate == null || !candidate.scene.IsValid())
                {
                    continue;
                }

                if (!IsInside(candidate.transform, selection))
                {
                    roots.Add(candidate);
                }
            }

            return roots;
        }

        static bool IsInside(Transform candidate, IReadOnlyList<GameObject> selection)
        {
            for (Transform at = candidate.parent; at != null; at = at.parent)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    if (selection[i] != null && selection[i].transform == at)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The average of the selection's own pivots.</summary>
        static Vector3 Pivot(IReadOnlyList<GameObject> roots)
        {
            var total = Vector3.zero;
            for (int i = 0; i < roots.Count; i++)
            {
                total += roots[i].transform.position;
            }

            return total / roots.Count;
        }

        /// <summary>Lower case, with anything that is not a letter, digit or dot as an underscore.</summary>
        /// <remarks>
        /// The same shape a logical id takes everywhere else — see <c>CatalogSync.Slug</c>, which
        /// is what turns a file name into the last segment of an id. A merged prefab called
        /// <c>Desk Setup</c> is <c>…/desk_setup</c>, exactly as it would be if it had been dropped
        /// into the folder and synced.
        /// </remarks>
        static string Slug(string name)
        {
            var slug = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = char.ToLowerInvariant(name[i]);
                slug.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
            }

            return slug.ToString();
        }

        /// <summary>What the undo entry for a whole merge is called.</summary>
        internal const string UndoName = "ArenaForge: merge to prefab";
    }

    /// <summary>
    /// The window behind <c>Tools/ArenaForge/Merge to Prefab</c>: what to call the group, where to
    /// put it, and which catalog to file it in.
    /// </summary>
    /// <remarks>
    /// Immediate-mode rather than the UI Toolkit markup the main tool window uses. This is four
    /// fields and a button that acts on the current selection, and the selection changes while the
    /// window is open — an immediate-mode window redraws from the selection every frame and needs
    /// nothing to keep the two in step.
    /// </remarks>
    sealed class MergeToPrefabWindow : EditorWindow
    {
        static readonly string[] FolderChoices =
        {
            MergeToPrefab.DefaultFolder,
            MergeToPrefab.CoverFolder,
        };

        [SerializeField]
        string _folder = MergeToPrefab.DefaultFolder;

        [SerializeField]
        string _name = string.Empty;

        [SerializeField]
        CatalogAsset _catalog;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        [MenuItem("Tools/ArenaForge/Merge to Prefab")]
        public static void Open()
        {
            var window = GetWindow<MergeToPrefabWindow>(true, "Merge to Prefab", true);
            window.minSize = new Vector2(420f, 190f);
            window.Show();
        }

        /// <summary>
        /// The same window from the hierarchy's own context menu, which is where a person who has
        /// just arranged three objects is already looking.
        /// </summary>
        [MenuItem("GameObject/ArenaForge/Merge to Prefab", false, 30)]
        static void OpenFromHierarchy() => Open();

        [MenuItem("GameObject/ArenaForge/Merge to Prefab", true, 30)]
        static bool CanOpenFromHierarchy() => Selection.gameObjects.Length > 0;

        void OnEnable()
        {
            if (_catalog == null)
            {
                _catalog = AssetDatabase.LoadAssetAtPath<CatalogAsset>(
                    $"{ArenaWorkspace.DefaultRoot}/{ArenaWorkspace.CatalogFolder}/" +
                    $"{ArenaWorkspace.CatalogName}.asset");
            }
        }

        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            GameObject[] selection = Selection.gameObjects;

            EditorGUILayout.HelpBox(
                selection.Length == 0
                    ? "Select the objects to merge in the scene."
                    : $"{selection.Length} object(s) will be parented under one pivot and saved as " +
                      "a prefab.",
                selection.Length == 0 ? MessageType.Info : MessageType.None);

            if (string.IsNullOrEmpty(_name) && selection.Length > 0)
            {
                _name = selection[0].name;
            }

            _name = EditorGUILayout.TextField("Name", _name);
            _catalog = (CatalogAsset)EditorGUILayout.ObjectField(
                "Catalog", _catalog, typeof(CatalogAsset), false);

            // A path starting at Assets is taken as it stands; anything else is read under the
            // workspace, so the two shortcuts below stay short.
            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField("Folder", _folder);

                if (GUILayout.Button("Browse…", GUILayout.Width(70f)))
                {
                    Browse();
                }
            }

            // The two folders a merged group nearly always goes in, as shortcuts beside the browser
            // rather than as the only two answers. They fill the field, so what they picked is
            // visible and can be edited afterwards.
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(" ", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                for (int i = 0; i < FolderChoices.Length; i++)
                {
                    if (GUILayout.Button(Leaf(FolderChoices[i])))
                    {
                        _folder = FolderChoices[i];
                        GUI.FocusControl(null);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(
                selection.Length == 0 || _catalog == null || string.IsNullOrWhiteSpace(_name)))
            {
                if (GUILayout.Button("Merge to Prefab"))
                {
                    Run(selection);
                }
            }
        }

        /// <summary>
        /// Asks for a folder with the editor's own browser, starting where the field points.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The panel rather than a dropdown of the workspace's folders, which is what this was and
        /// what made it a list of two: a project keeps art wherever it keeps art, and a tool that
        /// only offers the folders it created cannot write into any of the rest. Every folder in
        /// the project is now reachable, and the folder table still decides what a row is tagged —
        /// so a folder that spells no tags is refused when the merge runs, by name, rather than
        /// being unreachable in the first place.
        /// </para>
        /// <para>
        /// A folder outside this project's <c>Assets</c> is refused here rather than written to.
        /// Unity cannot make an asset there at all, and the panel will happily return one.
        /// </para>
        /// </remarks>
        void Browse()
        {
            string start = AssetDatabase.IsValidFolder(_folder) ? _folder : "Assets";
            string chosen = EditorUtility.OpenFolderPanel("Folder for the merged prefab", start, string.Empty);

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            string folder = ArenaWorkspace.ProjectFolder(chosen);
            if (folder == null)
            {
                EditorUtility.DisplayDialog(
                    "Merge to Prefab",
                    $"'{chosen}' is outside this project, so a prefab cannot be written there. " +
                    "Choose a folder under Assets.",
                    "OK");
                return;
            }

            _folder = folder;
            GUI.FocusControl(null);
        }

        /// <summary>The last segment of a folder path, which is what a shortcut button reads as.</summary>
        static string Leaf(string folder)
        {
            int split = folder.LastIndexOf('/');
            return split < 0 ? folder : folder.Substring(split + 1);
        }

        void Run(GameObject[] selection)
        {
            string root = string.IsNullOrWhiteSpace(_catalog.SourceFolder)
                ? ArenaWorkspace.DefaultRoot
                : _catalog.SourceFolder.TrimEnd('/');

            string folder = _folder.StartsWith("Assets", StringComparison.Ordinal)
                ? _folder
                : $"{root}/{_folder.TrimStart('/')}";

            try
            {
                GameObject prefab = MergeToPrefab.Merge(selection, folder, _name, _catalog);

                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log(
                    $"ArenaForge: merged {selection.Length} object(s) into " +
                    $"{AssetDatabase.GetAssetPath(prefab)} and added it to {_catalog.name}.",
                    prefab);

                _name = string.Empty;
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Merge to Prefab", error.Message, "OK");
            }
        }
    }
}
