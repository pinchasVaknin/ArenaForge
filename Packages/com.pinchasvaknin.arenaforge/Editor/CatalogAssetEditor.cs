using System;
using System.Collections.Generic;
using System.IO;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Draws a <see cref="CatalogAsset"/>: the two buttons that fill it from a folder of prefabs
    /// and export what Core is allowed to know about it, and the rows themselves grouped by what
    /// kind of art they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exporting is what keeps prefabs out of Core. The JSON it writes is the whole of what the
    /// generator knows about the art pack, so a Core-only test or tool can run against a real
    /// catalog without a Unity project anywhere in sight.
    /// </para>
    /// <para>
    /// The grouping is a <em>view</em>. A synced art pack is a hundred rows and the default
    /// inspector draws them as one flat list a screen and a half tall, in which finding the four
    /// walls means scrolling past ninety crates. What the list is grouped by is the first segment
    /// of the logical id, because that segment is already how the generator asks for art —
    /// <c>structure</c>, <c>cover</c>, <c>prop</c> are the roots of the tag paths in
    /// ARCHITECTURE.md section 3, so the headings are the tool's own vocabulary rather than a
    /// second one invented for the inspector.
    /// </para>
    /// <para>
    /// Nothing about the asset changes: <see cref="CatalogAsset.Rows"/> is still one flat list in
    /// one order, and every edit goes through <see cref="SerializedProperty"/>, so undo and the
    /// dirty flag work exactly as they do for a list nobody has grouped. Grouping the stored data
    /// instead would put a presentation decision into the file the generator reads, and would make
    /// the row order — which the catalog deliberately sorts away — mean something.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(CatalogAsset))]
    public sealed class CatalogAssetEditor : UnityEditor.Editor
    {
        /// <summary>Heading rows with no logical id are collected under.</summary>
        /// <remarks>
        /// Bracketed rather than blank so a row the user has not named yet is visible as a row
        /// rather than as a heading with no text — which is what an empty group name draws as, and
        /// is indistinguishable from a bug in the inspector.
        /// </remarks>
        internal const string UnnamedCategory = "(no logical id)";

        /// <summary>Backing field of <see cref="CatalogAsset.Rows"/>.</summary>
        const string RowsField = "_rows";

        /// <summary>Field of <c>CatalogAsset.Row</c> holding the logical id.</summary>
        const string LogicalIdField = nameof(CatalogAsset.Row.LogicalId);

        /// <summary>
        /// Which headings are open. Per inspector instance, so it is forgotten when the asset is
        /// deselected — the same lifetime Unity's own foldouts have.
        /// </summary>
        readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>One heading and the rows filed under it, as offsets into the flat list.</summary>
        /// <remarks>
        /// Offsets rather than the rows themselves, because what the inspector draws is the
        /// <see cref="SerializedProperty"/> at that index — the row objects are only consulted for
        /// their ids.
        /// </remarks>
        internal sealed class Category
        {
            /// <summary>Creates a heading.</summary>
            public Category(string name, List<int> rows)
            {
                Name = name;
                Rows = rows;
            }

            /// <summary>First segment of the logical ids filed here.</summary>
            public string Name { get; }

            /// <summary>Which rows of the flat list belong to it, in list order.</summary>
            public List<int> Rows { get; }
        }

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            var asset = (CatalogAsset)target;

            DrawSync(asset);

            EditorGUILayout.Space();
            if (GUILayout.Button("Export catalog JSON..."))
            {
                Export(asset);
            }

            EditorGUILayout.Space();

            // After the two buttons, both of which write to the asset directly: Update() re-reads
            // it, so a sync that has just replaced every row is what the rows below are drawn from.
            serializedObject.Update();
            DrawRows(serializedObject.FindProperty(RowsField));
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Files logical ids under their root category, headings in order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sorted by heading and stable within one, so the same catalog always draws the same way.
        /// A synced catalog arrives in id order and would group in that order anyway; one authored
        /// by hand does not, and a list of headings that jumped about as rows were added would be
        /// worse to read than the flat list this replaces.
        /// </para>
        /// <para>
        /// The root is the first segment because that is the segment the generator queries by. An
        /// id with no separator in it is its own root — a catalog that has not adopted the
        /// convention still groups, it simply gets a heading per row.
        /// </para>
        /// </remarks>
        /// <param name="logicalIds">The rows' ids, in list order. Nulls and blanks are allowed.</param>
        /// <exception cref="ArgumentNullException"><paramref name="logicalIds"/> is null.</exception>
        internal static List<Category> Group(IReadOnlyList<string> logicalIds)
        {
            if (logicalIds == null)
            {
                throw new ArgumentNullException(nameof(logicalIds));
            }

            var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var names = new List<string>();

            for (int i = 0; i < logicalIds.Count; i++)
            {
                string name = CategoryOf(logicalIds[i]);
                if (!byName.TryGetValue(name, out List<int> rows))
                {
                    rows = new List<int>();
                    byName.Add(name, rows);
                    names.Add(name);
                }

                rows.Add(i);
            }

            names.Sort(StringComparer.Ordinal);

            var categories = new List<Category>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                categories.Add(new Category(names[i], byName[names[i]]));
            }

            return categories;
        }

        /// <summary>The heading a logical id belongs under.</summary>
        internal static string CategoryOf(string logicalId)
        {
            if (string.IsNullOrWhiteSpace(logicalId))
            {
                return UnnamedCategory;
            }

            int slash = logicalId.IndexOf('/');
            return slash > 0 ? logicalId.Substring(0, slash) : logicalId;
        }

        /// <summary>Draws the rows under one foldout per category, and the button that adds one.</summary>
        void DrawRows(SerializedProperty rows)
        {
            EditorGUILayout.LabelField(
                $"Rows ({Count(rows.arraySize, "row")})", EditorStyles.boldLabel);

            if (rows.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "This catalog is empty. Point it at a folder of prefabs and press Sync from " +
                    "Folders, or add a row by hand.",
                    MessageType.Info);
            }

            // Collected rather than removed as it is found: deleting an element renumbers every
            // one after it, and the loop below is walking those numbers.
            int doomed = -1;

            List<Category> categories = Group(LogicalIds(rows));
            for (int i = 0; i < categories.Count; i++)
            {
                Category category = categories[i];

                _expanded.TryGetValue(category.Name, out bool open);
                open = EditorGUILayout.Foldout(
                    open, $"{category.Name} ({Count(category.Rows.Count, "item")})", true);
                _expanded[category.Name] = open;

                if (!open)
                {
                    continue;
                }

                EditorGUI.indentLevel++;
                for (int r = 0; r < category.Rows.Count; r++)
                {
                    if (DrawRow(rows.GetArrayElementAtIndex(category.Rows[r])))
                    {
                        doomed = category.Rows[r];
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Add Row"))
            {
                AddRow(rows);
            }

            if (doomed >= 0)
            {
                rows.DeleteArrayElementAtIndex(doomed);
            }
        }

        /// <summary>Draws one row. Returns true if the user asked for it to be removed.</summary>
        /// <remarks>
        /// Labelled with its own logical id rather than with <c>Element 7</c>, which is the whole
        /// reason to draw the list by hand: the id is what a person is looking for, and it is not
        /// visible on a collapsed default element at all.
        /// </remarks>
        static bool DrawRow(SerializedProperty row)
        {
            string id = row.FindPropertyRelative(LogicalIdField).stringValue;
            string label = string.IsNullOrWhiteSpace(id) ? UnnamedCategory : id;

            EditorGUILayout.PropertyField(row, new GUIContent(label), true);
            if (!row.isExpanded)
            {
                return false;
            }

            // Inside the row it belongs to and at the end of it, so the button that discards a row
            // is never next to the heading of a collapsed one.
            bool remove;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            remove = GUILayout.Button("Remove Row", GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();

            return remove;
        }

        /// <summary>
        /// Appends a blank row.
        /// </summary>
        /// <remarks>
        /// Unity fills a new array element by copying the last one, which for a catalog is usually
        /// what you want — the next piece of art is the same size as the last. What it must not
        /// copy is the identity: two rows with one logical id is the error
        /// <see cref="CatalogAsset.ToCatalog"/> refuses, and a second row silently bound to the
        /// first one's prefab is worse than an empty field. An empty list has nothing to copy, so
        /// the row it makes would come out with a zero weight — which the catalog also refuses —
        /// and the defaults are written in.
        /// </remarks>
        static void AddRow(SerializedProperty rows)
        {
            bool first = rows.arraySize == 0;
            rows.arraySize++;

            SerializedProperty added = rows.GetArrayElementAtIndex(rows.arraySize - 1);
            added.FindPropertyRelative(LogicalIdField).stringValue = string.Empty;
            added.FindPropertyRelative(nameof(CatalogAsset.Row.Prefab)).objectReferenceValue = null;
            added.isExpanded = true;

            if (!first)
            {
                return;
            }

            added.FindPropertyRelative(nameof(CatalogAsset.Row.FootprintSize)).vector2Value = Vector2.one;
            added.FindPropertyRelative(nameof(CatalogAsset.Row.Height)).floatValue = 1f;
            added.FindPropertyRelative(nameof(CatalogAsset.Row.Weight)).floatValue = 1f;
        }

        /// <summary>The logical ids of the rows, in list order.</summary>
        static List<string> LogicalIds(SerializedProperty rows)
        {
            var ids = new List<string>(rows.arraySize);
            for (int i = 0; i < rows.arraySize; i++)
            {
                ids.Add(rows.GetArrayElementAtIndex(i).FindPropertyRelative(LogicalIdField).stringValue);
            }

            return ids;
        }

        static string Count(int count, string noun) =>
            count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        /// <remarks>
        /// The folder is drawn as an object field rather than left as the text field the default
        /// inspector gives a string, so it can be dragged in from the project window. The asset
        /// stores the path, because a folder reference is a <c>UnityEditor</c> type and the
        /// catalog is a runtime one.
        /// </remarks>
        static void DrawSync(CatalogAsset asset)
        {
            var current = string.IsNullOrEmpty(asset.SourceFolder)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(asset.SourceFolder);

            EditorGUI.BeginChangeCheck();
            var picked = (DefaultAsset)EditorGUILayout.ObjectField(
                "Source Folder", current, typeof(DefaultAsset), false);

            if (EditorGUI.EndChangeCheck())
            {
                string path = picked != null ? AssetDatabase.GetAssetPath(picked) : string.Empty;
                if (picked == null || AssetDatabase.IsValidFolder(path))
                {
                    Undo.RegisterCompleteObjectUndo(asset, "ArenaForge: set catalog source folder");
                    asset.SourceFolder = path;
                    EditorUtility.SetDirty(asset);
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(asset.SourceFolder)))
            {
                if (GUILayout.Button("Sync from Folders"))
                {
                    Sync(asset);
                }
            }
        }

        static void Sync(CatalogAsset asset)
        {
            try
            {
                CatalogSyncResult result = CatalogSync.Sync(asset, asset.SourceFolder);
                Debug.Log($"ArenaForge: synced '{asset.name}' from {asset.SourceFolder} — {result}.", asset);
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("ArenaForge", error.Message, "OK");
            }
        }

        static void Export(CatalogAsset asset)
        {
            string json;
            try
            {
                json = ArenaJson.SerializeCatalog(asset.ToCatalog());
            }
            catch (InvalidOperationException error)
            {
                EditorUtility.DisplayDialog("ArenaForge", error.Message, "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Export ArenaForge catalog", Application.dataPath, asset.name + ".json", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            File.WriteAllText(path, json);
            AssetDatabase.Refresh();
            Debug.Log($"ArenaForge: exported '{asset.name}' to {path}", asset);
        }
    }
}
