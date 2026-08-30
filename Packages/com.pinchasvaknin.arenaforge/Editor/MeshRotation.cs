using System.Collections.Generic;
using System.Text;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Turns the art inside a prefab about its own Y, leaving the prefab's root exactly where it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>For art that faces the wrong way, discovered after it is already in a map.</strong>
    /// A model exported facing X, or -Z, looks like every other piece of art until the generator
    /// stands a row of it along a wall and half of it faces into the wall. The import pipeline
    /// cannot see this — a facing is not a measurement — so the fix belongs here, after the fact,
    /// where somebody has looked at it and knows.
    /// </para>
    /// <para>
    /// <strong>The children turn and the root does not, which is the whole constraint.</strong>
    /// Everything downstream assumes a prefab's root is at rotation zero and scale one:
    /// <see cref="WorldRealizer"/> writes a pose onto that transform, <c>CatalogSync</c> measures
    /// through it, and a placement stage reasons about a footprint it believes is axis-aligned. Turn
    /// the root and every one of those is quietly wrong. Turn the children and the art faces a
    /// different way while the prefab is the same shape of thing it always was.
    /// </para>
    /// <para>
    /// <strong>Position as well as rotation.</strong> Rotating a child in place turns a piece that
    /// is off the pivot without moving it, which shears the art apart rather than turning it — a
    /// door in one wall ends up in the same wall pointing sideways. The children are turned
    /// <em>about the root's origin</em>, which is the one operation that looks like picking the
    /// model up and turning it.
    /// </para>
    /// <para>
    /// <strong>A root that is not already square is refused rather than corrected.</strong>
    /// Squaring it up would move art that is already placed in somebody's map, and this tool is
    /// reached from a right-click on an asset — a place where a silent, wide-reaching change is the
    /// last thing anybody expects. What it does instead is say so.
    /// </para>
    /// <para>
    /// <strong>It reports the catalog rows that need re-measuring rather than re-measuring
    /// them.</strong> A quarter turn swaps a footprint's X and Z, so a row taken off this art is
    /// wrong until the next sync — and syncing here would move rows the person never asked about
    /// and every digest that rests on them. Saying which rows are affected leaves the decision
    /// where it belongs.
    /// </para>
    /// </remarks>
    static class MeshRotation
    {
        /// <summary>The turn the one-press item applies.</summary>
        const float QuarterTurn = 90f;

        /// <summary>How square a root has to be before its children may be turned, in degrees.</summary>
        /// <remarks>
        /// A hundredth of a degree, and the same fraction on scale. A root somebody typed 0 into is
        /// exactly zero; one that arrived through an importer can be a rounding away from it, and
        /// refusing that would be refusing art the tool is for.
        /// </remarks>
        const float Square = 0.01f;

        [MenuItem("Assets/ArenaForge/Rotate Internal Mesh 90° (Y)", false, 40)]
        static void RotateQuarter() => Apply(QuarterTurn);

        [MenuItem("Assets/ArenaForge/Rotate Internal Mesh 90° (Y)", true, 40)]
        static bool CanRotateQuarter() => Selected().Count > 0;

        [MenuItem("Assets/ArenaForge/Rotate Internal Mesh (Custom)…", false, 41)]
        static void RotateCustom() => MeshRotationWindow.Open();

        [MenuItem("Assets/ArenaForge/Rotate Internal Mesh (Custom)…", true, 41)]
        static bool CanRotateCustom() => Selected().Count > 0;

        /// <summary>The prefab assets in the Project window's selection.</summary>
        internal static List<GameObject> Selected()
        {
            Object[] chosen = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
            var prefabs = new List<GameObject>(chosen.Length);

            for (int i = 0; i < chosen.Length; i++)
            {
                if (chosen[i] is GameObject asset && PrefabUtility.IsPartOfPrefabAsset(asset))
                {
                    prefabs.Add(asset);
                }
            }

            return prefabs;
        }

        /// <summary>Turns every selected prefab's art and reports what happened.</summary>
        internal static void Apply(float degrees)
        {
            List<GameObject> prefabs = Selected();
            var turned = new List<string>();
            var refused = new List<string>();

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (Turn(prefabs[i], degrees, out string why))
                {
                    turned.Add(prefabs[i].name);
                }
                else
                {
                    refused.Add($"{prefabs[i].name}: {why}");
                }
            }

            AssetDatabase.SaveAssets();
            Report(degrees, turned, refused);
        }

        /// <summary>
        /// Turns one prefab's direct children about its root's origin, or says why it did not.
        /// </summary>
        internal static bool Turn(GameObject prefab, float degrees, out string why)
        {
            why = null;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                why = "it is not an asset on disk";
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);

            try
            {
                Transform root = contents.transform;

                if (Quaternion.Angle(root.localRotation, Quaternion.identity) > Square)
                {
                    why = $"its root is rotated ({root.localEulerAngles}), and this tool may not " +
                          "square it up for you — the generator poses that transform";

                    return false;
                }

                if (Vector3.Distance(root.localScale, Vector3.one) > Square)
                {
                    why = $"its root is scaled ({root.localScale}), and this tool may not reset it " +
                          "— the catalog measures through that scale";

                    return false;
                }

                if (root.childCount == 0)
                {
                    why = "it has no children, so its art is modelled straight onto the root and " +
                          "there is nothing to turn without turning the root";

                    return false;
                }

                Quaternion turn = Quaternion.Euler(0f, degrees, 0f);

                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);

                    // About the root's origin: the position turns with the rotation, or a piece
                    // that is off the pivot spins where it stands instead of going round.
                    child.localPosition = turn * child.localPosition;
                    child.localRotation = turn * child.localRotation;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void Report(float degrees, List<string> turned, List<string> refused)
        {
            var text = new StringBuilder();
            text.Append($"ArenaForge: turned the art inside {turned.Count} prefab(s) by {degrees:0.##}°.");

            if (turned.Count > 0)
            {
                List<string> stale = RowsFor(turned);
                text.Append(stale.Count == 0
                    ? "  No catalog row is measured off them."
                    : $"  {stale.Count} catalog row(s) are measured off them and want re-syncing, " +
                      $"because a turn swaps a footprint's X and Z: {string.Join(", ", stale)}.");
            }

            for (int i = 0; i < refused.Count; i++)
            {
                text.AppendLine().Append("Refused ").Append(refused[i]);
            }

            if (refused.Count > 0)
            {
                Debug.LogWarning(text.ToString());
                return;
            }

            Debug.Log(text.ToString());
        }

        /// <summary>The workspace catalog's rows bound to any of the named prefabs.</summary>
        static List<string> RowsFor(List<string> turned)
        {
            var rows = new List<string>();

            var catalog = AssetDatabase.LoadAssetAtPath<CatalogAsset>(
                $"{ArenaWorkspace.DefaultRoot}/{ArenaWorkspace.CatalogFolder}/" +
                $"{ArenaWorkspace.CatalogName}.asset");

            if (catalog == null)
            {
                return rows;
            }

            for (int i = 0; i < catalog.Rows.Count; i++)
            {
                CatalogAsset.Row row = catalog.Rows[i];
                if (row.Prefab != null && turned.Contains(row.Prefab.name))
                {
                    rows.Add(row.LogicalId);
                }
            }

            return rows;
        }
    }

    /// <summary>The popup behind <c>Rotate Internal Mesh (Custom)…</c>.</summary>
    /// <remarks>
    /// A window rather than a dialog because Unity has no input dialog, and a field somebody can
    /// type 45 into is the whole of what this needs to add over the one-press item beside it.
    /// </remarks>
    sealed class MeshRotationWindow : EditorWindow
    {
        float _degrees = 90f;

        public static void Open()
        {
            var window = GetWindow<MeshRotationWindow>(true, "Rotate Internal Mesh", true);
            window.minSize = new Vector2(340f, 120f);
            window.maxSize = new Vector2(340f, 120f);
            window.Show();
        }

        void OnSelectionChange() => Repaint();

        void OnGUI()
        {
            List<GameObject> prefabs = MeshRotation.Selected();

            EditorGUILayout.HelpBox(
                prefabs.Count == 0
                    ? "Select the prefabs to turn in the Project window."
                    : $"The art inside {prefabs.Count} prefab(s) will be turned about Y. The root " +
                      "keeps its rotation and scale.",
                prefabs.Count == 0 ? MessageType.Info : MessageType.None);

            _degrees = EditorGUILayout.FloatField("Degrees (Y)", _degrees);

            using (new EditorGUI.DisabledScope(prefabs.Count == 0))
            {
                if (GUILayout.Button("Rotate"))
                {
                    MeshRotation.Apply(_degrees);
                }
            }
        }
    }
}
