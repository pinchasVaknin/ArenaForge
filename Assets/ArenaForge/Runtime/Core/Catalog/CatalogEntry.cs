using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// A named local attachment point on a catalog entry — the top face of a crate, a window
    /// ledge — where a smaller prop may be placed.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CatalogSocket
    {
        static readonly string[] NoTags = Array.Empty<string>();

        /// <summary>Socket name, unique within its entry. Forms part of a child object's stable id.</summary>
        [JsonProperty("name", Order = 0)]
        public string Name { get; }

        /// <summary>Tags describing what may attach here, for example <c>prop_surface</c>.</summary>
        [JsonProperty("tags", Order = 1)]
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Where the socket sits in the entry's local space.</summary>
        [JsonProperty("pose", Order = 2)]
        public Pose LocalPose { get; }

        /// <summary>Creates a socket.</summary>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
        [JsonConstructor]
        public CatalogSocket(string name, string[] tags, Pose pose)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Socket name must not be blank.", nameof(name));
            }

            Name = name;
            Tags = tags ?? NoTags;
            LocalPose = pose;
        }

        /// <summary>True if this socket carries the tag.</summary>
        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Suppresses an empty tag list in serialised output.</summary>
        public bool ShouldSerializeTags() => Tags.Count > 0;
    }

    /// <summary>
    /// One piece of art as Core sees it: a logical id, tags to select it by, and the geometry
    /// facts placement needs. No prefab, no GUID, no file path — see ARCHITECTURE.md section 3.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CatalogEntry
    {
        static readonly string[] NoTags = Array.Empty<string>();
        static readonly CatalogSocket[] NoSockets = Array.Empty<CatalogSocket>();

        /// <summary>Readable logical id, for example <c>cover/low/crate_wood_01</c>.</summary>
        [JsonProperty("logicalId", Order = 0)]
        public string LogicalId { get; }

        /// <summary>Tags this entry can be selected by.</summary>
        [JsonProperty("tags", Order = 1)]
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Ground footprint in the entry's local space, centred on its pivot.</summary>
        [JsonProperty("footprint", Order = 2)]
        public Rect2 Footprint { get; }

        /// <summary>Height above the pivot, in metres.</summary>
        [JsonProperty("height", Order = 3)]
        public float Height { get; }

        /// <summary>Relative likelihood of being chosen by a weighted pick. Must be positive.</summary>
        [JsonProperty("weight", Order = 4)]
        public float Weight { get; }

        /// <summary>Attachment points, in the order the catalog declares them.</summary>
        [JsonProperty("sockets", Order = 5)]
        public IReadOnlyList<CatalogSocket> Sockets { get; }

        /// <summary>Creates an entry.</summary>
        /// <exception cref="ArgumentException">The id is blank, or a socket name is duplicated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Height is negative, weight is not positive, or the footprint is inverted.</exception>
        [JsonConstructor]
        public CatalogEntry(
            string logicalId,
            string[] tags,
            Rect2 footprint,
            float height,
            float weight,
            CatalogSocket[] sockets)
        {
            if (string.IsNullOrWhiteSpace(logicalId))
            {
                throw new ArgumentException("Catalog entry id must not be blank.", nameof(logicalId));
            }

            if (footprint.Width < 0f || footprint.Depth < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(footprint), footprint, $"Footprint of '{logicalId}' is inverted.");
            }

            if (height < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(height), height, $"Height of '{logicalId}' must not be negative.");
            }

            // A zero weight is rejected rather than silently excluded from selection: in practice
            // it means the field was omitted from the catalog JSON, and failing loudly beats an
            // entry that quietly never appears in a generated map.
            if (!(weight > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weight), weight, $"Weight of '{logicalId}' must be positive.");
            }

            LogicalId = logicalId;
            Tags = tags ?? NoTags;
            Footprint = footprint;
            Height = height;
            Weight = weight;
            Sockets = sockets ?? NoSockets;

            for (int i = 0; i < Sockets.Count; i++)
            {
                for (int j = i + 1; j < Sockets.Count; j++)
                {
                    if (string.Equals(Sockets[i].Name, Sockets[j].Name, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"Entry '{logicalId}' declares socket '{Sockets[i].Name}' twice.", nameof(sockets));
                    }
                }
            }
        }

        /// <summary>True if this entry carries the tag.</summary>
        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Suppresses an empty tag list in serialised output.</summary>
        public bool ShouldSerializeTags() => Tags.Count > 0;

        /// <summary>Suppresses an empty socket list in serialised output.</summary>
        public bool ShouldSerializeSockets() => Sockets.Count > 0;

        /// <inheritdoc />
        public override string ToString() => LogicalId;
    }
}
