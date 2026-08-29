using System;
using ArenaForge.Core;
using ArenaForge.Unity;
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

            return PrefabBake.Save(root.gameObject, prefabPath);
        }
    }
}
