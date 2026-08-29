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
        static readonly Rect2[] NoDoorways = Array.Empty<Rect2>();

        /// <summary>Readable logical id, for example <c>cover/low/crate_wood_01</c>.</summary>
        [JsonProperty("logicalId", Order = 0)]
        public string LogicalId { get; }

        /// <summary>Tags this entry can be selected by.</summary>
        [JsonProperty("tags", Order = 1)]
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Ground footprint in the entry's local space, relative to its pivot.</summary>
        /// <remarks>
        /// Not necessarily centred on the pivot: a rectangle, so it can say where the art sits as
        /// well as how big it is. Art modelled off to one side of its pivot — a flight of stairs
        /// pivoted at the foot of its run — would otherwise have to declare a centred rectangle
        /// twice the size of the real one, and everything cut to fit it comes out twice as large
        /// too.
        /// </remarks>
        [JsonProperty("footprint", Order = 2)]
        public Rect2 Footprint { get; }

        /// <summary>Height above the pivot, in metres.</summary>
        [JsonProperty("height", Order = 3)]
        public float Height { get; }

        /// <summary>
        /// How far the art hangs <em>below</em> its pivot, in metres. Zero for art modelled with
        /// its pivot on the ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pose puts a pivot somewhere; what a person sees is where the art around it ends up.
        /// For a prefab modelled on its base the two are the same statement, and for one modelled
        /// around its centre — every Unity primitive, and a great deal of furniture — they are half
        /// the piece apart, which is exactly how much of it ends up inside the floor.
        /// </para>
        /// <para>
        /// So a row says how far down its art reaches and the placement adds it back, rather than
        /// the catalog pretending every pivot is on the ground. It is the one fact about a piece of
        /// art that cannot be recovered from a footprint and a height: both of those are measured
        /// outward from the pivot and neither says where the pivot <em>is</em>.
        /// </para>
        /// </remarks>
        [JsonProperty("baseOffset", Order = 4)]
        public float BaseOffset { get; }

        /// <summary>Relative likelihood of being chosen by a weighted pick. Must be positive.</summary>
        [JsonProperty("weight", Order = 5)]
        public float Weight { get; }

        /// <summary>Attachment points, in the order the catalog declares them.</summary>
        [JsonProperty("sockets", Order = 6)]
        public IReadOnlyList<CatalogSocket> Sockets { get; }

        /// <summary>
        /// Where this piece of art can be walked into, in its own space. Empty for art nobody
        /// walks into.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only a structure has any, and this is the one thing about a structure a generator cannot
        /// work out for itself. Where a doorway <em>is</em> is modelled into the prefab: a map
        /// placing a house can see how big it is and cannot see which wall the door is in, so it
        /// used to assume the middle of the two faces across the lane and keep the ground clear
        /// there — which is a clearance in front of a blank wall and a stack of crates against the
        /// actual door.
        /// </para>
        /// <para>
        /// Rectangles rather than points, and in the entry's own space rather than the world's, so
        /// they turn with the art: a row is measured once and the map turns it a quarter at a time.
        /// Each one is the threshold a person crosses — as wide as the opening and deep enough
        /// across the wall to be ground somebody can stand on, which is what makes it something a
        /// walkable grid can be asked about.
        /// </para>
        /// <para>
        /// Two of them is the rule a map enforces, and it is enforced there rather than here — see
        /// <see cref="ArenaLayoutGenerator.DoorwaysPerStructure"/>. What a row states is what the
        /// art has; what a map does about a row that states three is a decision about that map.
        /// </para>
        /// </remarks>
        [JsonProperty("doorways", Order = 7)]
        public IReadOnlyList<Rect2> Doorways { get; }

        /// <summary>
        /// How tall the piece stands above the surface it rests on, in metres — what has to fit
        /// under a ceiling, as against <see cref="Height"/>, which is measured from the pivot.
        /// </summary>
        public float StandingHeight => Height + BaseOffset;

        /// <summary>Creates an entry.</summary>
        /// <param name="logicalId">Readable logical id.</param>
        /// <param name="tags">Tags this entry can be selected by.</param>
        /// <param name="footprint">Ground footprint, centred on the pivot.</param>
        /// <param name="height">Height above the pivot, in metres.</param>
        /// <param name="weight">Relative likelihood of being picked.</param>
        /// <param name="sockets">Attachment points, or null.</param>
        /// <param name="baseOffset">How far the art reaches below its pivot, in metres.</param>
        /// <param name="doorways">Where the art can be walked into, in its own space, or null.</param>
        /// <exception cref="ArgumentException">The id is blank, or a socket name is duplicated.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Height or base offset is negative, weight is not positive, or the footprint is inverted.</exception>
        [JsonConstructor]
        public CatalogEntry(
            string logicalId,
            string[] tags,
            Rect2 footprint,
            float height,
            float weight,
            CatalogSocket[] sockets,
            float baseOffset = 0f,
            Rect2[] doorways = null)
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

            // A negative offset would mean art floating above its own pivot, which is a measurement
            // mistake rather than a way of modelling something: the pivot is where the placement
            // puts the piece, so art above it would hover over whatever it was stood on.
            if (baseOffset < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(baseOffset), baseOffset,
                    $"Base offset of '{logicalId}' must not be negative.");
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
            BaseOffset = baseOffset;
            Weight = weight;
            Sockets = sockets ?? NoSockets;
            Doorways = doorways ?? NoDoorways;

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

        /// <summary>
        /// Suppresses an empty doorway list in serialised output, so a catalog written before
        /// there was such a field round-trips unchanged.
        /// </summary>
        public bool ShouldSerializeDoorways() => Doorways.Count > 0;

        /// <summary>
        /// Suppresses the base offset in serialised output when the art stands on its pivot, which
        /// is what a catalog written before there was such a field means.
        /// </summary>
        public bool ShouldSerializeBaseOffset() => BaseOffset != 0f;

        /// <inheritdoc />
        public override string ToString() => LogicalId;
    }
}
