using System;
using System.Collections.Generic;
using ArenaForge.Unity;
using UnityEditor;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Puts a row into a catalog, replacing the one already using its logical id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One line of the catalog is the unit of work every tool that produces art shares: an exported
    /// building writes one, and so does a group of scene objects merged into a prefab. Both want
    /// the same three things — replace rather than duplicate, put the change on the undo stack
    /// before making it, and write the asset out — and both got them wrong in different ways while
    /// each had its own copy.
    /// </para>
    /// <para>
    /// <strong>Replace, keeping the row's place in the list.</strong> The inspector order is a
    /// person's order, and re-exporting a building over itself should not move it to the bottom of
    /// their catalog. The order does not reach a generated map — <see cref="CatalogAsset.ToCatalog"/>
    /// sorts by logical id — so this is about the inspector and nothing else.
    /// </para>
    /// </remarks>
    static class CatalogBinding
    {
        /// <summary>
        /// Adds <paramref name="row"/> to <paramref name="catalog"/>, or replaces the row already
        /// using its logical id.
        /// </summary>
        /// <param name="catalog">The catalog to write into.</param>
        /// <param name="row">The row to add or replace with.</param>
        /// <param name="undoName">What the undo entry is called.</param>
        /// <returns>True if a row was replaced, false if one was added.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> or <paramref name="row"/> is null.</exception>
        public static bool AddOrReplace(CatalogAsset catalog, CatalogAsset.Row row, string undoName)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            var rows = new List<CatalogAsset.Row>(catalog.Rows);
            int existing = rows.FindIndex(
                r => r != null && string.Equals(r.LogicalId, row.LogicalId, StringComparison.Ordinal));

            Undo.RegisterCompleteObjectUndo(catalog, undoName);

            if (existing >= 0)
            {
                rows[existing] = row;
            }
            else
            {
                rows.Add(row);
            }

            catalog.SetRows(rows);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return existing >= 0;
        }
    }
}
