using System;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Writes a realised hierarchy out as a prefab with nothing left in it that points back at a
    /// document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both exports do this, and only this, before they go their separate ways — a building then
    /// writes a catalog row and a map does not. It lived inside <see cref="BuildingExport"/> while
    /// a building was the only thing that could be baked; <see cref="MapExport"/> is the second
    /// caller, which is when it moved, per rule 3 in CLAUDE.md.
    /// </para>
    /// <para>
    /// <strong>What comes out is a leaf.</strong> The components stripped below are the ones that
    /// name objects in a document, or that would go looking for one: after the bake the prefab is
    /// art, the scene object it came from stays the editable source, and re-exporting over the
    /// same path is how a change gets into it. A prefab that kept its
    /// <see cref="ArenaObjectRef"/> components would claim to be governed by a document that no
    /// longer governs it.
    /// </para>
    /// </remarks>
    static class PrefabBake
    {
        /// <summary>
        /// The components taken off on the way out, dependents before what they require.
        /// </summary>
        /// <remarks>
        /// The order matters: Unity refuses to destroy a component that another component's
        /// <c>RequireComponent</c> depends on, and both generator components require the realiser.
        /// The two generator components are on the list at all because a catalog may bind a prefab
        /// that has one — a building someone dropped into their art folder without exporting it —
        /// and an exported map is supposed to be free of the generator, not free of most of it.
        /// </remarks>
        static readonly Type[] Detached =
        {
            typeof(ArenaObjectRef),
            typeof(ArenaMap),
            typeof(ArenaBuilding),
            typeof(WorldRealizer),
        };

        /// <summary>
        /// Saves <paramref name="source"/> to <paramref name="prefabPath"/> and strips the
        /// components that only mean something to a document.
        /// </summary>
        /// <remarks>
        /// Stripped from the saved asset rather than from a copy of the scene objects, because
        /// copying the hierarchy first would flatten the catalog prefabs inside it into plain
        /// meshes — and an exported prefab whose crates are no longer crate prefabs would break
        /// exactly the art-pack swap the catalog exists to allow.
        /// </remarks>
        /// <param name="source">The realised hierarchy to bake.</param>
        /// <param name="prefabPath">Project-relative path ending in <c>.prefab</c>.</param>
        /// <returns>The saved prefab asset.</returns>
        /// <exception cref="InvalidOperationException">Unity could not write the prefab.</exception>
        public static GameObject Save(GameObject source, string prefabPath)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(source, prefabPath, out bool ok);
            if (!ok || saved == null)
            {
                throw new InvalidOperationException($"Unity could not write a prefab to '{prefabPath}'.");
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (Strip(contents) > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        /// <summary>Removes every detached component, returning how many came off.</summary>
        static int Strip(GameObject root)
        {
            int removed = 0;

            for (int i = 0; i < Detached.Length; i++)
            {
                Component[] found = root.GetComponentsInChildren(Detached[i], true);
                for (int j = 0; j < found.Length; j++)
                {
                    Object.DestroyImmediate(found[j], true);
                    removed++;
                }
            }

            return removed;
        }
    }
}
