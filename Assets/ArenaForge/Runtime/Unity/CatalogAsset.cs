using System;
using System.Collections.Generic;
using ArenaForge.Core;
using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Binds the logical asset ids Core places to the prefabs Unity instantiates.
    /// </summary>
    /// <remarks>
    /// This asset is the only place the two halves of an art entry meet. Core gets the engine-free
    /// half — id, tags, footprint, height, weight — through <see cref="ToCatalog"/> or the JSON the
    /// inspector's export button writes, and never learns that a prefab exists. Swapping the art
    /// pack is then a matter of repointing prefab fields here; no world document changes, because
    /// documents refer to <c>cover/low/crate_wood_01</c> and not to a GUID.
    /// </remarks>
    [CreateAssetMenu(menuName = "ArenaForge/Catalog", fileName = "ArenaCatalog")]
    public sealed class CatalogAsset : ScriptableObject
    {
        /// <summary>One piece of art: what Core knows about it, and what to instantiate for it.</summary>
        [Serializable]
        public sealed class Row
        {
            /// <summary>Readable logical id, for example <c>cover/low/crate_wood_01</c>.</summary>
            public string LogicalId;

            /// <summary>Tags the generator selects this entry by.</summary>
            public string[] Tags;

            /// <summary>Ground footprint in metres, centred on the prefab's pivot.</summary>
            public Vector2 FootprintSize = Vector2.one;

            /// <summary>Height above the pivot in metres.</summary>
            public float Height = 1f;

            /// <summary>Relative likelihood of being chosen by a weighted pick. Must be positive.</summary>
            public float Weight = 1f;

            /// <summary>What to instantiate. The pivot sits at ground level, centred on the footprint.</summary>
            public GameObject Prefab;
        }

        [SerializeField]
        List<Row> _rows = new List<Row>();

        /// <summary>The rows this asset declares, in inspector order.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>
        /// Replaces every row. For tooling that builds a catalog in code — the inspector is the
        /// normal way to author one.
        /// </summary>
        public void SetRows(IReadOnlyList<Row> rows)
        {
            _rows = rows != null ? new List<Row>(rows) : new List<Row>();
        }

        /// <summary>
        /// Builds the Core catalog these rows describe. The result is sorted by logical id, so the
        /// order rows happen to sit in the inspector cannot reach a generated map.
        /// </summary>
        /// <exception cref="InvalidOperationException">A row is incomplete or duplicated.</exception>
        public Catalog ToCatalog()
        {
            var entries = new CatalogEntry[_rows.Count];
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.LogicalId))
                {
                    throw new InvalidOperationException(
                        $"Catalog '{name}' row {i} has no logical id.");
                }

                if (!(row.Weight > 0f))
                {
                    throw new InvalidOperationException(
                        $"Catalog '{name}' row '{row.LogicalId}' has a weight of {row.Weight}; it must be positive.");
                }

                entries[i] = new CatalogEntry(
                    row.LogicalId,
                    row.Tags ?? Array.Empty<string>(),
                    Rect2.FromCenterSize(Vec2.Zero, new Vec2(row.FootprintSize.x, row.FootprintSize.y)),
                    row.Height,
                    row.Weight,
                    null);
            }

            try
            {
                return new Catalog(entries);
            }
            catch (ArgumentException error)
            {
                throw new InvalidOperationException($"Catalog '{name}' is not valid: {error.Message}", error);
            }
        }

        /// <summary>
        /// The prefab bound to a logical id, or null if this catalog does not carry one.
        /// </summary>
        public GameObject PrefabFor(string logicalId)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null && string.Equals(_rows[i].LogicalId, logicalId, StringComparison.Ordinal))
                {
                    return _rows[i].Prefab;
                }
            }

            return null;
        }
    }
}
