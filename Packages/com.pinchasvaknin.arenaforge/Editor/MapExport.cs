using System;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Bakes a realised map into a prefab a game can load without ArenaForge in the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The end of the road for a map, and the counterpart of <see cref="BuildingExport"/> with the
    /// catalog half taken out. A building is exported so the arena generator can place it, which
    /// is why it needs a catalog row; a map is exported because it is finished, and there is
    /// nothing left to place it into. What both do is the bake, which is
    /// <see cref="PrefabBake"/>.
    /// </para>
    /// <para>
    /// <strong>The prefab is not the map.</strong> The document stays authoritative and the
    /// <see cref="ArenaMap"/> in the scene stays the editable source — this is a copy taken at a
    /// moment, with no seed, no parameters and no overrides in it, and re-exporting over the same
    /// path is how a later change reaches it. That is the same bargain an exported building
    /// makes, and it is the reason the export strips the components rather than leaving them for
    /// a runtime that has no generator to answer them.
    /// </para>
    /// <para>
    /// <strong>The ground goes with it.</strong> An exported map used to be the props and nothing
    /// else, because the terrain is a sibling of the map in the scene rather than a child of the
    /// realisation root the bake walks — so what came out was a field of crates standing in the air
    /// over no ground at all. The terrain is copied into the prefab, and its heightfield is written
    /// beside the prefab as an asset of its own.
    /// </para>
    /// <para>
    /// <strong>A copy of the heightfield, not a reference to the scene's.</strong> A
    /// <c>TerrainData</c> is an asset, and pointing the exported prefab at the one the scene is
    /// still using would mean the next Generate rewrote the ground under every map ever exported
    /// from that scene. The point of an export is that it is finished; a copy is what makes it so.
    /// </para>
    /// </remarks>
    static class MapExport
    {
        /// <summary>
        /// Writes the realised map at <paramref name="prefabPath"/> as a prefab carrying nothing
        /// but the art.
        /// </summary>
        /// <param name="map">A generated and realised map.</param>
        /// <param name="prefabPath">Project-relative path ending in <c>.prefab</c>.</param>
        /// <returns>The saved prefab.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The map has no document, is not realised, or the prefab could not be written.
        /// </exception>
        public static GameObject Export(ArenaMap map, string prefabPath)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                throw new InvalidOperationException("An exported map needs somewhere to be written.");
            }

            WorldDoc doc = map.Document;
            if (doc == null)
            {
                throw new InvalidOperationException(
                    $"'{map.name}' has not been generated, so there is nothing to export.");
            }

            Transform root = map.Realizer.Root;
            if (root.childCount == 0)
            {
                throw new InvalidOperationException(
                    $"'{map.name}' is not realised, so there is nothing to bake. Generate it first.");
            }

            // Parented into the realisation root for the length of the bake and taken out again,
            // rather than baked from a second root: the props are already posed in this space, and
            // moving the ground to them keeps every object exactly where the document put it.
            GameObject ground = Ground(map, prefabPath);

            try
            {
                if (ground != null)
                {
                    ground.transform.SetParent(root, true);
                }

                return PrefabBake.Save(root.gameObject, prefabPath);
            }
            finally
            {
                if (ground != null)
                {
                    UnityEngine.Object.DestroyImmediate(ground);
                }
            }
        }

        /// <summary>
        /// A copy of the map's terrain, standing where the scene's does and carrying a heightfield
        /// of its own, or null when the map has no terrain to export.
        /// </summary>
        /// <remarks>
        /// The heightfield asset is written to a path derived from the prefab's, so re-exporting
        /// over the same prefab overwrites the same ground rather than leaving a numbered trail of
        /// abandoned ones beside it.
        /// </remarks>
        static GameObject Ground(ArenaMap map, string prefabPath)
        {
            Terrain terrain = map.Terrain;
            if (terrain == null || terrain.terrainData == null)
            {
                return null;
            }

            var data = UnityEngine.Object.Instantiate(terrain.terrainData);
            string dataPath = DataPath(prefabPath);

            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var host = UnityEngine.Object.Instantiate(terrain.gameObject);
            host.name = terrain.gameObject.name;

            var copied = host.GetComponent<Terrain>();
            if (copied != null)
            {
                copied.terrainData = data;
            }

            var collider = host.GetComponent<TerrainCollider>();
            if (collider != null)
            {
                collider.terrainData = data;
            }

            host.transform.SetPositionAndRotation(
                terrain.transform.position, terrain.transform.rotation);

            return host;
        }

        /// <summary>Where the exported heightfield goes: beside the prefab, under its own name.</summary>
        internal static string DataPath(string prefabPath)
        {
            const string Extension = ".prefab";

            string stem = prefabPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                ? prefabPath.Substring(0, prefabPath.Length - Extension.Length)
                : prefabPath;

            return stem + " Terrain.asset";
        }
    }
}
