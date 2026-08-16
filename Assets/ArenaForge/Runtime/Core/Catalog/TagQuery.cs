using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// How the generator asks the catalog for art: three tag sets, evaluated in a fixed order.
    /// </summary>
    /// <remarks>
    /// Deliberately not a query language. The generator wants "a piece of low cover", not a
    /// specific crate, and three sets express that. Anything richer is speculation until a
    /// second caller needs it.
    /// </remarks>
    public readonly struct TagQuery
    {
        static readonly string[] NoTags = Array.Empty<string>();

        readonly string[] _requireAll;
        readonly string[] _requireAny;
        readonly string[] _exclude;

        /// <summary>Creates a query from its three sets. Null is treated as empty.</summary>
        public TagQuery(string[] requireAll, string[] requireAny, string[] exclude)
        {
            _requireAll = requireAll;
            _requireAny = requireAny;
            _exclude = exclude;
        }

        /// <summary>The query that matches every entry.</summary>
        public static TagQuery Empty => new TagQuery(null, null, null);

        /// <summary>A query requiring every one of these tags.</summary>
        public static TagQuery All(params string[] tags) => new TagQuery(tags, null, null);

        /// <summary>Copy that also requires at least one of these tags.</summary>
        public TagQuery WithAny(params string[] tags) => new TagQuery(_requireAll, tags, _exclude);

        /// <summary>Copy that also rejects entries carrying any of these tags.</summary>
        public TagQuery WithExclude(params string[] tags) => new TagQuery(_requireAll, _requireAny, tags);

        /// <summary>Tags an entry must all carry.</summary>
        public IReadOnlyList<string> RequireAll => _requireAll ?? NoTags;

        /// <summary>Tags an entry must carry at least one of. Empty means no constraint.</summary>
        public IReadOnlyList<string> RequireAny => _requireAny ?? NoTags;

        /// <summary>Tags that disqualify an entry.</summary>
        public IReadOnlyList<string> Exclude => _exclude ?? NoTags;

        /// <summary>True if the entry satisfies all three sets.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
        public bool Matches(CatalogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (_requireAll != null)
            {
                for (int i = 0; i < _requireAll.Length; i++)
                {
                    if (!entry.HasTag(_requireAll[i]))
                    {
                        return false;
                    }
                }
            }

            if (_requireAny != null && _requireAny.Length > 0)
            {
                bool any = false;
                for (int i = 0; i < _requireAny.Length && !any; i++)
                {
                    any = entry.HasTag(_requireAny[i]);
                }

                if (!any)
                {
                    return false;
                }
            }

            if (_exclude != null)
            {
                for (int i = 0; i < _exclude.Length; i++)
                {
                    if (entry.HasTag(_exclude[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
