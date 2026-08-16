using System;
using System.IO;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Adds the catalog export button to a <see cref="CatalogAsset"/>'s inspector.
    /// </summary>
    /// <remarks>
    /// Exporting is what keeps prefabs out of Core. The JSON this writes is the whole of what the
    /// generator knows about the art pack, so a Core-only test or tool can run against a real
    /// catalog without a Unity project anywhere in sight.
    /// </remarks>
    [CustomEditor(typeof(CatalogAsset))]
    public sealed class CatalogAssetEditor : UnityEditor.Editor
    {
        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Export catalog JSON..."))
            {
                Export((CatalogAsset)target);
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
