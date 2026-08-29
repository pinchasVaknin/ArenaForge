using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Bakes a generated building into a prefab and binds it into a catalog, so the arena
    /// generator can place it like any other structure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the join between the two generators, and it is deliberately the only one. The
    /// arena generator is not changed by any of this: it asks the catalog for something tagged
    /// <c>structure/building</c> and gets a row back, and the row this writes is the same shape as
    /// a row somebody typed into the inspector. If placing an exported building had needed a
    /// change in <c>ArenaLayoutGenerator</c>, that would have been a hole in the catalog boundary
    /// worth reporting rather than working around.
    /// </para>
    /// <para>
    /// <strong>An exported building is a leaf.</strong> The prefab is baked art: it has no
    /// document, no floors and no seeds, and no amount of clicking will re-roll its second storey.
    /// The <see cref="ArenaBuilding"/> it came from stays the editable source, and re-exporting
    /// over the same path is how a change gets into the prefab. The <see cref="ArenaObjectRef"/>
    /// components are stripped on the way out for exactly this reason — they name objects in a
    /// document that no longer governs the thing carrying them.
    /// </para>
    /// </remarks>
    static class BuildingExport
    {
        /// <summary>Logical ids exported buildings are given, so a catalog reads by origin.</summary>
        public const string LogicalIdPrefix = "structure/building/";

        /// <summary>Tags an exported row carries, which is what the arena generator selects it by.</summary>
        public static readonly string[] Tags =
        {
            ArenaLayoutGenerator.StructureTag,
            ArenaLayoutGenerator.BuildingTag,
        };

        /// <summary>
        /// Writes <paramref name="building"/> to a prefab at <paramref name="prefabPath"/> and adds
        /// or replaces the catalog row bound to it.
        /// </summary>
        /// <remarks>
        /// The row goes into the catalog the building was generated from, which is the same
        /// catalog the map generator draws from. There is nowhere else for it to go and no second
        /// catalog to keep in step.
        /// </remarks>
        /// <param name="building">A generated and realised building.</param>
        /// <param name="logicalId">Logical id for the new row, under <see cref="LogicalIdPrefix"/>.</param>
        /// <param name="prefabPath">Project-relative path ending in <c>.prefab</c>.</param>
        /// <returns>The saved prefab.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="building"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The building has no document, is not realised, has no catalog, or the prefab could not
        /// be written.
        /// </exception>
        public static GameObject Export(ArenaBuilding building, string logicalId, string prefabPath)
        {
            if (building == null)
            {
                throw new ArgumentNullException(nameof(building));
            }

            if (string.IsNullOrWhiteSpace(logicalId))
            {
                throw new InvalidOperationException("An exported building needs a logical id.");
            }

            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                throw new InvalidOperationException("An exported building needs somewhere to be written.");
            }

            BuildingDoc doc = building.Document;
            if (doc == null)
            {
                throw new InvalidOperationException(
                    $"'{building.name}' has not been generated, so there is nothing to export.");
            }

            CatalogAsset catalog = building.Realizer.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    $"'{building.name}' has no catalog assigned on its World Realizer, so an exported " +
                    "building would have nowhere to be bound.");
            }

            Transform root = building.Realizer.Root;
            if (root.childCount == 0)
            {
                throw new InvalidOperationException(
                    $"'{building.name}' is not realised, so there is nothing to bake. Generate it first.");
            }

            GameObject prefab = PrefabBake.Save(root.gameObject, prefabPath);
            prefab = BakeDoorways(prefabPath, Doorways(catalog, doc));
            BindRow(catalog, doc, prefab, logicalId);

            return prefab;
        }

        /// <summary>A logical id for a building exported to <paramref name="prefabPath"/>.</summary>
        public static string LogicalIdFor(string prefabPath) =>
            LogicalIdPrefix + System.IO.Path.GetFileNameWithoutExtension(prefabPath);

        /// <summary>
        /// The ways into a generated building: what its metadata declares, or failing that what
        /// laying its ground floor out again says.
        /// </summary>
        static List<CatalogAsset.DoorwayRow> Doorways(CatalogAsset catalog, BuildingDoc doc)
        {
            List<CatalogAsset.DoorwayRow> doorways = DoorwaysOf(doc);
            return doorways.Count > 0 ? doorways : DoorwaysFromPlan(catalog, doc);
        }

        /// <summary>
        /// Puts a <c>DoorwayMarker</c> child in the saved prefab for each doorway, and returns the
        /// prefab as it stands afterwards.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>An exported building carries its own doorways.</strong> They used to reach the
        /// catalog and stop there: the row said where the doors were and the prefab said nothing,
        /// so the two agreed only for as long as that particular row survived. Delete the row and
        /// press <em>Sync from Folders</em> — which is the ordinary way to get a building the
        /// catalog has lost back into it — and the building came back with no doorways at all,
        /// because the scan asks the asset and the asset had never been told. The map then went
        /// back to guessing at the middle of two faces while the real doors were somewhere else.
        /// </para>
        /// <para>
        /// What it makes is an ordinary marker: a named child with a trigger box on it, exactly
        /// what <see cref="DoorwaySetup"/> makes for a house somebody modelled and exactly what
        /// <see cref="CatalogSync.DoorwaysIn"/> reads. There is no second kind of marker and no
        /// second reader — a generated building is now a prefab that declares its doors the way
        /// every other prefab does, which is what makes it survive being re-scanned from a folder
        /// by a catalog that has never heard of it.
        /// </para>
        /// <para>
        /// The rectangles are in the building's own space and the prefab's root is its origin, so
        /// a marker goes at the centre of one with a box the size of it: what
        /// <see cref="CatalogSync.DoorwaysIn"/> measures back out is the rectangle that went in.
        /// The height is <see cref="DoorwaySetup.MarkerHeight"/>, standing on the ground floor —
        /// only X and Z ever reach a row, so it is a handle size and shared with the tool that
        /// makes them by hand rather than chosen again here.
        /// </para>
        /// <para>
        /// Written into the saved asset rather than onto the scene objects first, for the reason
        /// <see cref="PrefabBake"/> strips components the same way: the building in the scene is
        /// the editable source and it is not the tool's to add children to. A re-export over the
        /// same path replaces the whole prefab, so markers cannot pile up across exports.
        /// </para>
        /// </remarks>
        static GameObject BakeDoorways(string prefabPath, List<CatalogAsset.DoorwayRow> doorways)
        {
            if (doorways.Count == 0)
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                for (int i = 0; i < doorways.Count; i++)
                {
                    Marker(contents.transform, doorways[i], i);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        /// <summary>
        /// One marker under <paramref name="root"/>, numbered the way
        /// <see cref="DoorwaySetup.AddMarker"/> numbers the ones a person adds.
        /// </summary>
        static void Marker(Transform root, CatalogAsset.DoorwayRow doorway, int index)
        {
            var marker = new GameObject(
                $"{CatalogSync.DoorwayMarkerName}_{index.ToString("00", CultureInfo.InvariantCulture)}");

            marker.transform.SetParent(root, false);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            marker.transform.localPosition = new Vector3(
                doorway.Center.x, DoorwaySetup.MarkerHeight * 0.5f, doorway.Center.y);

            BoxCollider box = marker.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(doorway.Size.x, DoorwaySetup.MarkerHeight, doorway.Size.y);
        }

        /// <summary>
        /// Adds the catalog row the arena generator will find, replacing any row already using the
        /// logical id.
        /// </summary>
        /// <remarks>
        /// The footprint is the building's declared one and the height is its floors: both are
        /// facts about the document rather than measurements of the prefab, and both are what the
        /// contents were generated against — every object was held inside that footprint by a
        /// constraint, and no object was offered to a floor it would have stood through. Measuring
        /// the meshes instead would report the extent of whatever the seed happened to place,
        /// which shrinks when a floor comes up sparse.
        /// <para>
        /// <strong>The doorways are read back off the prefab, not written straight into the
        /// row.</strong> <see cref="BakeDoorways"/> has just put a marker in the asset for each
        /// one, so what states them here is <see cref="CatalogSync.DoorwaysIn"/> — the same reader
        /// that answers for a house somebody modelled and marked up by hand. A row that agreed
        /// with the document but not with the prefab would be a row nothing could check, and the
        /// next <em>Sync from Folders</em> is exactly the thing that checks it.
        /// </para>
        /// </remarks>
        static void BindRow(CatalogAsset catalog, BuildingDoc doc, GameObject prefab, string logicalId)
        {
            var row = new CatalogAsset.Row
            {
                LogicalId = logicalId,
                Tags = (string[])Tags.Clone(),
                FootprintSize = new Vector2(doc.Footprint.Width, doc.Footprint.Depth),
                Height = doc.Height,
                Weight = 1f,
                Prefab = prefab,
                Doorways = CatalogSync.DoorwaysIn(prefab),
            };

            CatalogBinding.AddOrReplace(catalog, row, "ArenaForge: export building");
        }

        /// <summary>
        /// The ways into a generated building, read back out of the metadata its ground floor
        /// wrote.
        /// </summary>
        /// <remarks>
        /// The first answer and the only one a current document needs. The declared metadata is the
        /// contract — it is what a person reads in the inspector and what a test asserts on — so it
        /// is read rather than worked out again here. A key that is missing or unreadable is a
        /// doorway this does not report rather than an exception; what it produces is then topped up
        /// by <see cref="DoorwaysFromPlan"/>, which asks the generator itself rather than being a
        /// second implementation of the rule.
        /// </remarks>
        static List<CatalogAsset.DoorwayRow> DoorwaysOf(BuildingDoc doc)
        {
            var doorways = new List<CatalogAsset.DoorwayRow>();

            if (!doc.Metadata.TryGetValue(BuildingGenerator.DoorwayCountKey, out string countText) ||
                !int.TryParse(
                    countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                return doorways;
            }

            for (int i = 0; i < count; i++)
            {
                string key = BuildingGenerator.DoorwayKeyPrefix +
                    i.ToString("00", CultureInfo.InvariantCulture);

                if (!doc.Metadata.TryGetValue(key, out string text) ||
                    !RectMetadata.TryParse(text, out Rect2 rect))
                {
                    continue;
                }

                doorways.Add(Row(rect));
            }

            return doorways;
        }

        /// <summary>
        /// The ground floor's doorways, laid out again from the document's own parameters and floor
        /// seed, for a document whose metadata does not declare any.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Floor zero's doorways reach the catalog whatever the metadata says.</strong> A
        /// generation writes them down — see <see cref="BuildingGenerator.ExteriorDoorways"/> — and
        /// a document generated before it did says nothing, which the reader above turns into a row
        /// with no openings and no complaint. That is a building the map places by guessing at the
        /// middle of two faces while the real doors are somewhere else, and the person who exported
        /// it has no way to tell: the prefab is right, the row looks right, and only the maps built
        /// from it are wrong. A single-storey building is where it showed up, because a single
        /// storey is the quickest thing to make and the likeliest to still be sitting in a scene
        /// from before the openings were recorded.
        /// </para>
        /// <para>
        /// Not a second implementation of the rule. <see cref="BuildingGenerator.PlanFloor"/>
        /// rebuilds the ground floor's plan from the seed that storey already holds — the same plan
        /// the shell in the prefab was tiled from, not one that resembles it — and
        /// <see cref="BuildingGenerator.ExteriorDoorways"/> reads the openings out of it, which is
        /// the same call the generation made.
        /// </para>
        /// <para>
        /// A catalog that can no longer lay the floor out — the wall art deleted since, say — is a
        /// building with no declared doorways rather than a failed export. That is the bargain the
        /// metadata route already makes for an unreadable key, and it is the right way round for a
        /// fallback: the export was working before this ran, and the worst case is the behaviour
        /// there has always been.
        /// </para>
        /// </remarks>
        static List<CatalogAsset.DoorwayRow> DoorwaysFromPlan(CatalogAsset catalog, BuildingDoc doc)
        {
            var doorways = new List<CatalogAsset.DoorwayRow>();
            if (doc.Floors.Count == 0)
            {
                return doorways;
            }

            List<Rect2> rects;
            try
            {
                rects = BuildingGenerator.ExteriorDoorways(
                    BuildingGenerator.PlanFloor(doc.Parameters, catalog.ToCatalog(), doc.Floors[0], 0));
            }
            catch (InvalidOperationException)
            {
                return doorways;
            }

            for (int i = 0; i < rects.Count; i++)
            {
                doorways.Add(Row(rects[i]));
            }

            return doorways;
        }

        /// <summary>One doorway rectangle as the catalog states it: a centre and a size.</summary>
        static CatalogAsset.DoorwayRow Row(Rect2 rect) => new CatalogAsset.DoorwayRow
        {
            Center = new Vector2(rect.Center.X, rect.Center.Y),
            Size = new Vector2(rect.Width, rect.Depth),
        };
    }
}
