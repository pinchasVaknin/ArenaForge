using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// The set of art the generator may draw from, held in a fixed order.
    /// </summary>
    /// <remarks>
    /// Entries are sorted by logical id at construction and every query preserves that order. The
    /// order is part of the determinism contract: a weighted pick walks the query result, so a
    /// catalog that enumerated differently between runs would place different props from the same
    /// seed. Sorting at construction means the order cannot depend on the order rows happened to
    /// appear in the JSON file, or on a dictionary's internal layout.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Catalog
    {
        /// <summary>Schema revision written into catalog JSON.</summary>
        public const int CurrentSchemaVersion = 1;

        readonly List<CatalogEntry> _entries;

        /// <summary>Creates a catalog, sorting the entries by logical id.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        /// <exception cref="ArgumentException">An entry is null, or a logical id appears twice.</exception>
        [JsonConstructor]
        public Catalog(CatalogEntry[] entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            _entries = new List<CatalogEntry>(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    throw new ArgumentException($"Catalog entry at index {i} is null.", nameof(entries));
                }

                _entries.Add(entries[i]);
            }

            _entries.Sort((a, b) => string.CompareOrdinal(a.LogicalId, b.LogicalId));

            for (int i = 1; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i - 1].LogicalId, _entries[i].LogicalId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Catalog declares '{_entries[i].LogicalId}' twice.", nameof(entries));
                }
            }
        }

        /// <summary>Schema revision of this catalog.</summary>
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion => CurrentSchemaVersion;

        /// <summary>All entries, sorted by logical id.</summary>
        [JsonProperty("entries", Order = 1)]
        public IReadOnlyList<CatalogEntry> Entries => _entries;

        /// <summary>
        /// Entries matching the query, in catalog order. Returns an empty list rather than null
        /// when nothing matches.
        /// </summary>
        public IReadOnlyList<CatalogEntry> Query(TagQuery query)
        {
            var matches = new List<CatalogEntry>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (query.Matches(_entries[i]))
                {
                    matches.Add(_entries[i]);
                }
            }

            return matches;
        }
    }
}
