using System;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Puts an imported model under an empty parent and turns it there, so the prefab that comes
    /// out faces its own positive Z.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The generator cannot fix a pivot and will not guess at one.</strong> Everything this
    /// tool places is turned on the assumption an artist modelled the front of a piece down its own
    /// positive Z — see <see cref="ArenaForge.Core.WallFacing"/>, which says so and says nothing
    /// can check it. A model imported facing sideways is stood against a wall sideways, on every
    /// seed, and no amount of work in the placement code can tell that from a piece that really is
    /// meant to face that way. The fix belongs in the art, and until now the fix meant making an
    /// empty by hand, dragging the model into it, zeroing the child, guessing at ninety degrees,
    /// looking, guessing again, and remembering to save.
    /// </para>
    /// <para>
    /// <strong>The parent is the pivot.</strong> It goes at the world origin with no rotation and
    /// no scale of its own, and the model goes under it at the origin too, because what the catalog
    /// measures and what the generator holds a piece by is the prefab's root. A parent left at the
    /// model's own position in the scene would bake that position into the prefab.
    /// </para>
    /// <para>
    /// <strong>Turning the child and not the parent.</strong> The whole point is that the parent's
    /// axes are the ones the tool trusts, so the child is what moves: after a turn the model looks
    /// the way it looks and the parent still says which way is forward. Turning the parent instead
    /// would leave the two agreeing with each other and disagreeing with everything else.
    /// </para>
    /// <para>
    /// <strong>The wrapper carries the collision, and the model carries none.</strong> A model
    /// arrives from a pack with whatever colliders the artist left on it — a mesh collider per
    /// part, thirty of them, or none at all — and neither is a size the generator can use. So the
    /// wrap measures every mesh under the parent, puts one <see cref="BoxCollider"/> on the parent
    /// round the lot of it, and takes the children's colliders off. See
    /// <see cref="EncloseInABox"/>.
    /// </para>
    /// <para>
    /// No catalog row is written. A wrapped model is art on its way into a folder, and what a
    /// folder means is <see cref="CatalogSync"/>'s question — pressing Sync from Folders after
    /// saving is what files it, which is the same path any other imported prefab takes. What the
    /// box buys there is that the row is measured off a size somebody chose rather than off the
    /// art: <see cref="CatalogSync.TryMeasure"/> reads box colliders first and falls back to meshes
    /// only when there are none.
    /// </para>
    /// </remarks>
    static class WrapObject
    {
        /// <summary>What the undo entry for a wrap is called.</summary>
        internal const string UndoName = "ArenaForge: wrap object";

        /// <summary>What the undo entry for one turn of the child is called.</summary>
        internal const string TurnUndoName = "ArenaForge: turn wrapped model";


        /// <summary>Suffix given to the parent, so a wrapped model reads as one in the hierarchy.</summary>
        internal const string WrapperSuffix = "_Wrapped";

        /// <summary>Workspace folder a wrapped model is written into unless another is chosen.</summary>
        /// <remarks>
        /// The decoration folder, because the pieces whose facing matters most are the ones that go
        /// against a wall — a closet, a cabinet, a sideboard. It is a starting point for the browser
        /// and not a restriction; the window writes wherever it is pointed.
        /// </remarks>
        internal const string DefaultFolder = "Props/PropBuilding/Decor/Decoration";

        /// <summary>
        /// Parents <paramref name="model"/> under a new empty at the origin and returns the empty.
        /// </summary>
        /// <remarks>
        /// The model's own local scale is carried across rather than reset with the rest of it. A
        /// position and a rotation are what the wrap exists to take over; a scale is how big the
        /// artist made the thing, and resetting it would silently resize art that imported at a
        /// hundredth of its size.
        /// </remarks>
        /// <param name="model">A model standing in a scene.</param>
        /// <returns>The empty parent, selected and ready to be turned.</returns>
        /// <exception cref="InvalidOperationException">
        /// Nothing was given, or what was given is a project asset rather than a scene object.
        /// </exception>
        public static GameObject Wrap(GameObject model)
        {
            if (model == null)
            {
                throw new InvalidOperationException(
                    "Select the model to wrap in the scene first.");
            }

            if (!model.scene.IsValid())
            {
                throw new InvalidOperationException(
                    $"'{model.name}' is a project asset rather than an object in a scene. Drag it " +
                    "into the scene and wrap the instance.");
            }

            var wrapper = new GameObject(model.name + WrapperSuffix);
            Undo.RegisterCreatedObjectUndo(wrapper, UndoName);
            wrapper.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Vector3 scale = model.transform.localScale;
            Undo.SetTransformParent(model.transform, wrapper.transform, UndoName);
            Undo.RecordObject(model.transform, UndoName);

            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = scale;

            EncloseInABox(wrapper);
            return wrapper;
        }

        /// <summary>
        /// Puts a single box collider on <paramref name="wrapper"/> round every mesh under it, and
        /// takes the colliders off the model.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The children's colliders come off first, and the order is the whole trick.</strong>
        /// <see cref="CatalogSync.TryMeasure"/> measures box colliders where a prefab has any and
        /// the meshes where it has none, so stripping the model before measuring makes it answer
        /// about the art — and the box written from that answer is then exactly what the next sync
        /// of the folder will read back. Measuring first and stripping afterwards would work today
        /// and would be two measurements of one prefab, which is the shape a disagreement grows in.
        /// </para>
        /// <para>
        /// <strong>One box, on the parent.</strong> A pack's model is often a dozen parts with a
        /// mesh collider each, and every one of those is a triangle soup the physics engine tests
        /// against — for a crate the tool will stamp thirty of round a map. What the generator
        /// places is the root and what it reasons about is a rectangle, so the collision that
        /// matches what it believes is one box round the lot. It is also what a
        /// <see cref="CatalogSync"/> row is measured from, so the shape a player walks into and the
        /// shape the placement rules keep clear are the same shape.
        /// </para>
        /// <para>
        /// <strong>Removed rather than disabled.</strong> A disabled collider is still a component
        /// on the prefab, still serialised, and still there for somebody to switch back on without
        /// knowing why it was off. There is nothing to preserve: the box round the meshes is a
        /// better description of the art than the colliders that came with it, and Undo puts them
        /// back if the wrap was a mistake.
        /// </para>
        /// <para>
        /// A wrapper whose model has no mesh at all — an empty, a light, a spawn marker — gets no
        /// box. There is nothing to measure and a one-metre default cube round nothing is a
        /// collider a player walks into in an empty room.
        /// </para>
        /// </remarks>
        /// <param name="wrapper">A wrapper made by <see cref="Wrap"/>.</param>
        /// <returns>The box that was fitted, or null when there was nothing to measure.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="wrapper"/> is null or has no children.</exception>
        public static BoxCollider EncloseInABox(GameObject wrapper)
        {
            if (wrapper == null || wrapper.transform.childCount == 0)
            {
                throw new InvalidOperationException(
                    "Select a wrapped model — an empty parent with the model under it — to fit a " +
                    "collider to.");
            }

            StripColliders(wrapper);

            if (!CatalogSync.TryMeasure(wrapper, out Bounds bounds))
            {
                return null;
            }

            BoxCollider box = Undo.AddComponent<BoxCollider>(wrapper);
            box.center = bounds.center;
            box.size = bounds.size;
            return box;
        }

        /// <summary>Takes every collider off a wrapper and off everything under it.</summary>
        /// <remarks>
        /// <para>
        /// <strong>The wrapper's own box goes too, and it has to.</strong>
        /// <see cref="CatalogSync.TryMeasure"/> prefers box colliders to meshes and does not care
        /// whose they are, so a box left in place while the model under it was measured would be
        /// measured instead of the model — and the box would then be a copy of itself. A wrapper
        /// turned ninety degrees would keep the box it had before the turn, for ever, which is the
        /// one thing refitting after a turn exists to prevent.
        /// </para>
        /// <para>
        /// Inactive children included, for the reason <see cref="CatalogSync.TryMeasure"/> counts
        /// them: a prefab variant's switched-off alternates are still part of the thing, and a
        /// collider that survived because its object happened to be switched off is a collider
        /// nobody will find again.
        /// </para>
        /// </remarks>
        static void StripColliders(GameObject wrapper)
        {
            Collider[] colliders = wrapper.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    Undo.DestroyObjectImmediate(colliders[i]);
                }
            }
        }

        /// <summary>
        /// Turns the model inside <paramref name="wrapper"/> by a whole number of quarter turns
        /// about the parent's up axis.
        /// </summary>
        /// <remarks>
        /// Every child, because a wrap made by hand can hold more than one object and turning half
        /// of a thing is worse than turning none of it. Applied to the local rotation the child
        /// already has, so two presses of the same button are a half turn.
        ///
        /// The collision box is refitted afterwards. A quarter turn swaps a box's two horizontal
        /// sides, and a box left at the size it had before the turn is a collider at right angles
        /// to the art inside it — which is invisible in the scene view and is what a player walks
        /// into.
        /// </remarks>
        /// <param name="wrapper">A wrapper made by <see cref="Wrap"/>.</param>
        /// <param name="quarterTurns">How far to turn, in ninety-degree steps. May be negative.</param>
        /// <exception cref="InvalidOperationException"><paramref name="wrapper"/> is null or has no children.</exception>
        public static void Turn(GameObject wrapper, int quarterTurns)
        {
            if (wrapper == null || wrapper.transform.childCount == 0)
            {
                throw new InvalidOperationException(
                    "Select a wrapped model — an empty parent with the model under it — to turn.");
            }

            Quaternion turn = Quaternion.Euler(0f, quarterTurns * 90f, 0f);

            for (int i = 0; i < wrapper.transform.childCount; i++)
            {
                Transform child = wrapper.transform.GetChild(i);
                Undo.RecordObject(child, TurnUndoName);
                child.localRotation = turn * child.localRotation;
            }

            EncloseInABox(wrapper);
        }

        /// <summary>
        /// Writes <paramref name="wrapper"/> out as a prefab and leaves the scene object connected
        /// to it.
        /// </summary>
        /// <remarks>
        /// Connected rather than saved and forgotten, so a further turn after the save is one
        /// Apply away rather than a second prefab beside the first.
        ///
        /// The box is refitted once more on the way out, because a wrapper is a scene object and
        /// the scene is where people work: a model nudged or scaled by hand between the last turn
        /// and the save would otherwise be written with a collider round where it used to be.
        /// </remarks>
        /// <param name="wrapper">The wrapper to save.</param>
        /// <param name="folder">Project-relative folder to write into.</param>
        /// <param name="name">File name for the prefab, without an extension.</param>
        /// <returns>The prefab asset that was written.</returns>
        /// <exception cref="InvalidOperationException">
        /// The wrapper, folder or name is missing, or Unity could not write the prefab.
        /// </exception>
        public static GameObject SaveAsPrefab(GameObject wrapper, string folder, string name)
        {
            if (wrapper == null)
            {
                throw new InvalidOperationException("There is no wrapped model to save.");
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException("A wrapped model needs somewhere to be written.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("A wrapped model needs a name.");
            }

            EncloseInABox(wrapper);
            ArenaWorkspace.EnsureFolder(folder.TrimEnd('/'));

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder.TrimEnd('/')}/{name}.prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                wrapper, path, InteractionMode.UserAction, out bool ok);

            if (!ok || prefab == null)
            {
                throw new InvalidOperationException($"Unity could not write a prefab to '{path}'.");
            }

            AssetDatabase.SaveAssets();
            return prefab;
        }
    }

    /// <summary>
    /// The window behind <c>Tools/ArenaForge/Wrap Object</c>: wrap the selection, turn it until it
    /// faces the way it should, save it.
    /// </summary>
    /// <remarks>
    /// Immediate-mode, for the reason <see cref="MergeToPrefabWindow"/> is: it acts on the current
    /// selection, and the selection changes while the window is open.
    /// <para>
    /// There is no preview in the window and there is not meant to be one. The scene view is the
    /// preview — a turn is applied to the real object in the real scene, so what the person is
    /// looking at while they press the buttons is the thing that will be saved.
    /// </para>
    /// </remarks>
    sealed class WrapObjectWindow : EditorWindow
    {
        [SerializeField]
        string _folder = WrapObject.DefaultFolder;

        [SerializeField]
        string _name = string.Empty;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        [MenuItem("Tools/ArenaForge/Wrap Object")]
        public static void Open()
        {
            var window = GetWindow<WrapObjectWindow>(true, "Wrap Object", true);
            window.minSize = new Vector2(420f, 210f);
            window.Show();
        }

        /// <summary>
        /// The same window from the hierarchy's context menu, which is where somebody who has just
        /// dragged a model into the scene is already looking.
        /// </summary>
        [MenuItem("GameObject/ArenaForge/Wrap Object", false, 31)]
        static void OpenFromHierarchy() => Open();

        [MenuItem("GameObject/ArenaForge/Wrap Object", true, 31)]
        static bool CanOpenFromHierarchy() => Selection.activeGameObject != null;

        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            GameObject selected = Selection.activeGameObject;
            bool wrapped = IsWrapper(selected);

            EditorGUILayout.HelpBox(
                Explanation(selected, wrapped),
                selected == null ? MessageType.Info : MessageType.None);

            using (new EditorGUI.DisabledScope(selected == null || wrapped))
            {
                if (GUILayout.Button("Wrap Selection"))
                {
                    Wrap(selected);
                    return;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Face the model's front down the parent's +Z", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!wrapped))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Turn −90°"))
                {
                    Turn(selected, -1);
                }

                if (GUILayout.Button("Turn 180°"))
                {
                    Turn(selected, 2);
                }

                if (GUILayout.Button("Turn +90°"))
                {
                    Turn(selected, 1);
                }
            }

            EditorGUILayout.Space();

            if (string.IsNullOrEmpty(_name) && wrapped)
            {
                _name = selected.name;
            }

            _name = EditorGUILayout.TextField("Name", _name);

            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField("Folder", _folder);

                if (GUILayout.Button("Browse…", GUILayout.Width(70f)))
                {
                    Browse();
                }
            }

            using (new EditorGUI.DisabledScope(!wrapped || string.IsNullOrWhiteSpace(_name)))
            {
                if (GUILayout.Button("Save as Prefab"))
                {
                    Save(selected);
                }
            }
        }

        /// <summary>What the box at the top of the window says about the current selection.</summary>
        static string Explanation(GameObject selected, bool wrapped)
        {
            if (selected == null)
            {
                return "Select the model to wrap in the scene.";
            }

            if (wrapped)
            {
                return $"'{selected.name}' is wrapped, and its collider is one box round the whole " +
                       "model. Turn the model until its front points along the parent's blue +Z " +
                       "arrow, then save it.";
            }

            return $"'{selected.name}' will be parented under a new empty at the origin, with one " +
                   "box collider round it and the model's own colliders removed.";
        }

        /// <summary>
        /// True if the selection looks like something <see cref="WrapObject.Wrap"/> made: an empty
        /// with children.
        /// </summary>
        /// <remarks>
        /// By shape rather than by name, so a wrapper somebody renamed still works and a model that
        /// happens to be called <c>…_Wrapped</c> is not turned by mistake. An empty is one with no
        /// renderer of its own; the buttons that need a wrapper are disabled rather than hidden
        /// when the shape is wrong, so what the tool wants is visible before it is satisfied.
        ///
        /// A box collider on the parent is not part of the shape asked for, even though every
        /// wrapper this tool makes has one. A wrapper somebody assembled by hand is still a
        /// wrapper, and refusing to turn it because it has no collider yet would be the tool
        /// insisting on its own output.
        /// </remarks>
        static bool IsWrapper(GameObject candidate) =>
            candidate != null &&
            candidate.scene.IsValid() &&
            candidate.transform.childCount > 0 &&
            candidate.GetComponent<Renderer>() == null;

        void Wrap(GameObject selected)
        {
            try
            {
                GameObject wrapper = WrapObject.Wrap(selected);
                _name = wrapper.name;
                Selection.activeGameObject = wrapper;
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Wrap Object", error.Message, "OK");
            }
        }

        void Turn(GameObject wrapper, int quarterTurns)
        {
            try
            {
                WrapObject.Turn(wrapper, quarterTurns);
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Wrap Object", error.Message, "OK");
            }
        }

        void Save(GameObject wrapper)
        {
            try
            {
                GameObject prefab = WrapObject.SaveAsPrefab(wrapper, Folder(), _name);

                EditorGUIUtility.PingObject(prefab);
                Debug.Log(
                    $"ArenaForge: saved '{wrapper.name}' to {AssetDatabase.GetAssetPath(prefab)}. " +
                    "Press Sync from Folders to file it in the catalog.",
                    prefab);
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Wrap Object", error.Message, "OK");
            }
        }

        /// <summary>
        /// The folder to write into: what the field holds, read under the workspace when it is not
        /// already a project path.
        /// </summary>
        /// <remarks>The same reading <see cref="MergeToPrefabWindow"/> gives its own field.</remarks>
        string Folder() => _folder.StartsWith("Assets", StringComparison.Ordinal)
            ? _folder
            : $"{ArenaWorkspace.DefaultRoot}/{_folder.TrimStart('/')}";

        void Browse()
        {
            string start = AssetDatabase.IsValidFolder(Folder()) ? Folder() : "Assets";
            string chosen = EditorUtility.OpenFolderPanel("Folder for the wrapped prefab", start, string.Empty);

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            string folder = ArenaWorkspace.ProjectFolder(chosen);
            if (folder == null)
            {
                EditorUtility.DisplayDialog(
                    "Wrap Object",
                    $"'{chosen}' is outside this project, so a prefab cannot be written there. " +
                    "Choose a folder under Assets.",
                    "OK");
                return;
            }

            _folder = folder;
            GUI.FocusControl(null);
        }
    }
}
