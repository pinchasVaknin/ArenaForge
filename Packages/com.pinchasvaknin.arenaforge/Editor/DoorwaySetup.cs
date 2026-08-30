using System;
using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Puts <c>DoorwayMarker</c> children on an imported house and carries what they measure into
    /// its catalog row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Where a house is walked into is the one thing about it nobody can measure.</strong>
    /// How big it is, is visible in its meshes; which wall the door is in is not, so a marker in
    /// the prefab is how the person who made the model says so — see
    /// <see cref="CatalogSync.DoorwayMarkerName"/>, which is the contract this writes to and does
    /// not extend. A generated building writes its own out of the plan it came from and needs none
    /// of this; an imported one has no plan, and until now declaring its doors meant making a child,
    /// naming it exactly right, adding a collider, remembering the trigger box, and then finding the
    /// row by hand.
    /// </para>
    /// <para>
    /// <strong>What it makes is an ordinary marker.</strong> A named child with a trigger box on it,
    /// which is exactly what a person would have made, so a prefab set up here and a prefab set up
    /// by hand are the same prefab and the sync reads both. The tool is the four steps, not a fifth
    /// kind of marker — the same bargain <see cref="MergeToPrefab"/> makes about rows.
    /// </para>
    /// <para>
    /// <strong>Placed, then moved.</strong> A new marker lands in the middle of the model's ground
    /// floor, which is nowhere in particular and is meant to be: what settles where a door is, is
    /// the person dragging the box onto the wall with Unity's own handles, and a tool that guessed
    /// at a wall would be wrong silently rather than obviously. Only X and Z reach the catalog — a
    /// threshold is ground somebody stands on — so the height the box is dragged to is the person's
    /// own business.
    /// </para>
    /// </remarks>
    static class DoorwaySetup
    {
        /// <summary>What the undo entry for adding a marker is called.</summary>
        internal const string UndoName = "ArenaForge: add doorway marker";

        /// <summary>What the undo entry for writing the doorways into a row is called.</summary>
        internal const string SyncUndoName = "ArenaForge: update doorways";

        /// <summary>How wide across the wall a new marker starts, in metres.</summary>
        /// <remarks>
        /// A double door, because a marker is easier to narrow than to find. It is a starting size
        /// and nothing reads it afterwards: what the catalog takes is the box the person left,
        /// through <see cref="CatalogSync.DoorwaysIn"/>.
        /// </remarks>
        internal const float MarkerWidth = 2f;

        /// <summary>How tall a new marker starts, in metres.</summary>
        /// <remarks>
        /// Tall enough to be worth clicking on in a scene view looking at a house. The catalog
        /// reads a threshold's X and Z and never its height, so this is a handle size.
        /// </remarks>
        internal const float MarkerHeight = 2f;

        /// <summary>
        /// Adds a doorway marker to <paramref name="model"/> and returns it.
        /// </summary>
        /// <remarks>
        /// Numbered off the markers already there, so a house with a front door and a back door
        /// gets <c>DoorwayMarker_00</c> and <c>DoorwayMarker_01</c> and the sync reads two. The
        /// prefix is <see cref="CatalogSync.DoorwayMarkerName"/> itself rather than a copy of the
        /// string, because that is the name the sync matches on.
        /// </remarks>
        /// <param name="model">A model standing in a scene.</param>
        /// <returns>The new marker, selected and ready to be dragged onto a wall.</returns>
        /// <exception cref="InvalidOperationException">
        /// Nothing was given, or what was given is a project asset rather than a scene object.
        /// </exception>
        public static GameObject AddMarker(GameObject model)
        {
            if (model == null)
            {
                throw new InvalidOperationException("Select the house to mark up in the scene first.");
            }

            if (!model.scene.IsValid())
            {
                throw new InvalidOperationException(
                    $"'{model.name}' is a project asset rather than an object in a scene. Drag it " +
                    "into the scene, mark it up there, and press Save and Update Catalog.");
            }

            var marker = new GameObject(
                $"{CatalogSync.DoorwayMarkerName}_{MarkersIn(model).Count:00}");
            Undo.RegisterCreatedObjectUndo(marker, UndoName);
            Undo.SetTransformParent(marker.transform, model.transform, UndoName);

            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            marker.transform.localPosition = Start(model);

            BoxCollider box = Undo.AddComponent<BoxCollider>(marker);
            box.isTrigger = true;
            box.size = new Vector3(MarkerWidth, MarkerHeight, ArenaLayoutGenerator.DoorwayDepth);

            return marker;
        }

        /// <summary>
        /// Writes the markers on <paramref name="model"/> into the prefab it came from and into the
        /// catalog row bound to that prefab. Returns how many doorways the row now declares.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both halves, because either alone is a trap. Applying to the prefab without touching the
        /// row leaves a house that declares its doors to nobody — the catalog is what a generator
        /// reads, not the prefab — and writing the row without applying leaves a row describing
        /// markers that vanish the moment the scene is closed.
        /// </para>
        /// <para>
        /// The doorways are read back out of the saved prefab rather than off the scene objects, so
        /// what the row states is what the asset holds. A marker somebody added and did not apply
        /// is then absent from both, which is the honest answer.
        /// </para>
        /// <para>
        /// The row is found by the prefab it points at rather than by name. A logical id is spelled
        /// from a folder and a file name and either can be edited; the prefab reference is what the
        /// binding actually <em>is</em>, and a row pointing at this house is this house's row
        /// whatever it is called.
        /// </para>
        /// </remarks>
        /// <param name="model">A prefab instance in a scene, or a prefab asset in the project.</param>
        /// <param name="catalog">The catalog holding the row for it.</param>
        /// <returns>How many doorways the row declares afterwards.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Nothing was given, what was given is not part of a prefab, or no row in the catalog is
        /// bound to that prefab.
        /// </exception>
        public static int Sync(GameObject model, CatalogAsset catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (model == null)
            {
                throw new InvalidOperationException("There is no house selected to save.");
            }

            GameObject prefab = Apply(model);
            string path = AssetDatabase.GetAssetPath(prefab);

            CatalogAsset.Row row = RowFor(catalog, path);
            if (row == null)
            {
                throw new InvalidOperationException(
                    $"No row in '{catalog.name}' is bound to '{path}', so there is nowhere to write " +
                    "its doorways. Press Sync from Folders first, so the prefab has a row.");
            }

            row.Doorways = CatalogSync.DoorwaysIn(prefab);
            CatalogBinding.AddOrReplace(catalog, row, SyncUndoName);

            return row.Doorways.Count;
        }

        /// <summary>
        /// The prefab asset behind <paramref name="model"/>, with the scene's edits applied to it.
        /// </summary>
        /// <remarks>
        /// A prefab asset picked in the project window is already the asset and has nothing to
        /// apply, which is the case a person hits when they mark a house up by opening it in prefab
        /// mode rather than by dragging an instance into a scene.
        /// </remarks>
        static GameObject Apply(GameObject model)
        {
            if (!model.scene.IsValid())
            {
                if (!PrefabUtility.IsPartOfPrefabAsset(model))
                {
                    throw new InvalidOperationException(
                        $"'{model.name}' is not a prefab, so there is nothing to save it into.");
                }

                return model;
            }

            GameObject instance = PrefabUtility.GetNearestPrefabInstanceRoot(model);
            if (instance == null)
            {
                throw new InvalidOperationException(
                    $"'{model.name}' is not an instance of a prefab, so its doorways have nowhere " +
                    "to be saved. Make it a prefab first — Wrap Object writes one — then press " +
                    "Sync from Folders so the catalog has a row for it.");
            }

            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.UserAction);
            AssetDatabase.SaveAssets();

            return PrefabUtility.GetCorrespondingObjectFromSource(instance);
        }

        /// <summary>The row bound to the prefab at a path, or null when the catalog has none.</summary>
        static CatalogAsset.Row RowFor(CatalogAsset catalog, string prefabPath)
        {
            IReadOnlyList<CatalogAsset.Row> rows = catalog.Rows;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null &&
                    rows[i].Prefab != null &&
                    string.Equals(
                        AssetDatabase.GetAssetPath(rows[i].Prefab), prefabPath, StringComparison.Ordinal))
                {
                    return rows[i];
                }
            }

            return null;
        }

        /// <summary>The markers already on a model, in hierarchy order.</summary>
        /// <remarks>
        /// By the same name match the sync uses, so the count a new marker is numbered from is the
        /// count the sync will read. Matching on the prefix rather than on the whole name is what
        /// lets a person rename one <c>DoorwayMarker_Front</c> without it stopping counting.
        /// </remarks>
        internal static List<Transform> MarkersIn(GameObject model)
        {
            var markers = new List<Transform>();
            if (model == null)
            {
                return markers;
            }

            Transform[] children = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name.StartsWith(
                        CatalogSync.DoorwayMarkerName, StringComparison.OrdinalIgnoreCase))
                {
                    markers.Add(children[i]);
                }
            }

            return markers;
        }

        /// <summary>
        /// Where a new marker starts, in the model's own space: the middle of its ground floor.
        /// </summary>
        /// <remarks>
        /// The middle across X and Z, and standing on the model's own floor rather than half way up
        /// it. A threshold is ground somebody walks over — only X and Z reach the catalog at all —
        /// and a box hanging in the air inside a house is harder to find and harder to drag onto a
        /// wall than one sitting on the floor of it. A model with nothing to measure starts at its
        /// own origin, which is where an empty transform's marker would have gone anyway.
        /// </remarks>
        /// <remarks>
        /// <strong>Divided by the model's own scale, because this is a local position and the
        /// measurement is not.</strong> <see cref="CatalogSync.TryMeasure"/> answers in the space
        /// the art stands in, root scale and all — which is what a catalog row wants and the exact
        /// opposite of what a <c>localPosition</c> under that same root wants. On a house scaled
        /// three times up, the undivided figure put the marker a metre under its own floor. The
        /// marker's own box is scaled by the root too, so its half height needs no dividing: it is
        /// already in the same local units the position is written in.
        /// </remarks>
        static Vector3 Start(GameObject model)
        {
            if (!CatalogSync.TryMeasure(model, out Bounds bounds))
            {
                return new Vector3(0f, MarkerHeight * 0.5f, 0f);
            }

            Vector3 scale = model.transform.localScale;

            return new Vector3(
                Over(bounds.center.x, scale.x),
                Over(bounds.min.y, scale.y) + MarkerHeight * 0.5f,
                Over(bounds.center.z, scale.z));
        }

        /// <summary>A measurement back in local units, or nothing where the scale is flat.</summary>
        static float Over(float measured, float scale) =>
            Mathf.Approximately(scale, 0f) ? 0f : measured / scale;
    }

    /// <summary>
    /// The window behind <c>Tools/ArenaForge/Doorway Setup</c>: add a marker, drag it onto the
    /// wall, write it into the catalog.
    /// </summary>
    /// <remarks>
    /// Immediate-mode, for the reason <see cref="MergeToPrefabWindow"/> is: it acts on the current
    /// selection, and the selection changes while the window is open — including to the marker the
    /// last press just made, which is what the person wants to be holding.
    /// </remarks>
    sealed class DoorwaySetupWindow : EditorWindow
    {
        [SerializeField]
        CatalogAsset _catalog;

        [SerializeField]
        GameObject _house;

        /// <summary>Opens the window, or brings the open one to the front.</summary>
        [MenuItem("Tools/ArenaForge/Doorway Setup")]
        public static void Open()
        {
            var window = GetWindow<DoorwaySetupWindow>(true, "Doorway Setup", true);
            window.minSize = new Vector2(440f, 210f);
            window.Show();
        }

        [MenuItem("GameObject/ArenaForge/Doorway Setup", false, 32)]
        static void OpenFromHierarchy() => Open();

        [MenuItem("GameObject/ArenaForge/Doorway Setup", true, 32)]
        static bool CanOpenFromHierarchy() => Selection.activeGameObject != null;

        void OnEnable()
        {
            if (_catalog == null)
            {
                _catalog = AssetDatabase.LoadAssetAtPath<CatalogAsset>(
                    $"{ArenaWorkspace.DefaultRoot}/{ArenaWorkspace.CatalogFolder}/" +
                    $"{ArenaWorkspace.CatalogName}.asset");
            }
        }

        /// <summary>
        /// Keeps the bound house pointing at the house rather than following the selection into the
        /// marker the last press made.
        /// </summary>
        /// <remarks>
        /// Selecting a marker is the normal state of this window — it is how the marker is dragged
        /// onto a wall — so a window that took the selection as its house would lose the house the
        /// moment the tool did its job. A selection that is not part of the bound house replaces
        /// it, which is how a person moves on to the next model.
        /// </remarks>
        void OnSelectionChange()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected != null && selected.scene.IsValid() && !IsPartOf(_house, selected))
            {
                _house = selected;
            }

            Repaint();
        }

        static bool IsPartOf(GameObject root, GameObject candidate)
        {
            if (root == null)
            {
                return false;
            }

            for (Transform at = candidate.transform; at != null; at = at.parent)
            {
                if (at.gameObject == root)
                {
                    return true;
                }
            }

            return false;
        }

        void OnGUI()
        {
            if (_house == null && Selection.activeGameObject != null &&
                Selection.activeGameObject.scene.IsValid())
            {
                _house = Selection.activeGameObject;
            }

            _house = (GameObject)EditorGUILayout.ObjectField("House", _house, typeof(GameObject), true);
            _catalog = (CatalogAsset)EditorGUILayout.ObjectField(
                "Catalog", _catalog, typeof(CatalogAsset), false);

            int markers = DoorwaySetup.MarkersIn(_house).Count;

            EditorGUILayout.HelpBox(
                _house == null
                    ? "Select the house in the scene, or drag it into the House field."
                    : $"'{_house.name}' declares {markers} doorway(s). Add one, drag the box onto " +
                      "the wall it belongs in, then save.",
                _house == null ? MessageType.Info : MessageType.None);

            using (new EditorGUI.DisabledScope(_house == null))
            {
                if (GUILayout.Button("Add Doorway"))
                {
                    Add();
                    return;
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_house == null || _catalog == null))
            {
                if (GUILayout.Button("Save and Update Catalog"))
                {
                    Save();
                }
            }
        }

        void Add()
        {
            try
            {
                // The new marker is selected, because the next thing to happen is somebody dragging
                // it onto a wall with the move tool.
                Selection.activeGameObject = DoorwaySetup.AddMarker(_house);
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Doorway Setup", error.Message, "OK");
            }
        }

        void Save()
        {
            try
            {
                int doorways = DoorwaySetup.Sync(_house, _catalog);

                Debug.Log(
                    $"ArenaForge: '{_house.name}' now declares {doorways} doorway(s) in " +
                    $"{_catalog.name}. Maps keep the ground in front of them clear.",
                    _catalog);
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("Doorway Setup", error.Message, "OK");
            }
        }
    }
}
