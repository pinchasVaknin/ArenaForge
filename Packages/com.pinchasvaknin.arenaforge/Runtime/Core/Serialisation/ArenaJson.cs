using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace ArenaForge.Core
{
    /// <summary>
    /// Reads and writes ArenaForge documents as JSON.
    /// </summary>
    /// <remarks>
    /// Every setting here exists to make the output byte-identical for identical input, on any
    /// machine: the invariant culture so a comma never becomes a decimal separator, an explicit
    /// line ending so the file does not change shape between Windows and Linux, no date parsing
    /// so a metadata value that looks like a timestamp survives a round trip unchanged, and
    /// explicit property ordering on every serialised type so member order never depends on
    /// reflection order. Floats go through Newtonsoft's round-trip formatting, which is verified
    /// bit-for-bit by the serialisation tests rather than assumed.
    /// </remarks>
    /// <remarks>
    /// The serialised types are all marked <c>MemberSerialization.OptIn</c>. Without it Newtonsoft
    /// writes every public property, so a computed convenience like <c>Vec3.Normalized</c> would
    /// end up in the file — and, being another <c>Vec3</c> with its own <c>Normalized</c>, would
    /// recurse forever. Opting in means adding a derived property to a math type can never change
    /// the document format.
    /// </remarks>
    public static class ArenaJson
    {
        /// <summary>Serialises a world document.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static string SerializeWorld(WorldDoc doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            return Write(doc);
        }

        /// <summary>Deserialises a world document.</summary>
        /// <remarks>
        /// Schema version 1 — the revision before building documents existed, and so before the
        /// <c>kind</c> field — is still read. A version 1 file carries no kind, and in a build
        /// where a map was the only document there was, that is unambiguous.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="UnsupportedSchemaVersionException">The document declares an unreadable schema version.</exception>
        /// <exception cref="InvalidOperationException">The document is of another kind.</exception>
        public static WorldDoc DeserializeWorld(string json)
        {
            JObject root = Parse(json);

            // The kind first, then the version. A file of the wrong kind is refused for being the
            // wrong kind, rather than for whatever its own version line happens to say — "this is
            // a building, not a world document" is the useful message, and a complaint about
            // version numbers between two documents that were never the same format is not.
            RequireKind(root, WorldDoc.WorldKind, "world document");
            RequireSchemaVersion(
                root, "world document", WorldDoc.MinReadableSchemaVersion, WorldDoc.CurrentSchemaVersion);

            var doc = root.ToObject<WorldDoc>(JsonSerializer.Create(CreateSettings()));

            // Upgraded on the way in rather than left declaring the revision it was written at.
            // The document in memory is this build's shape whatever it was read from, and a
            // version 1 file re-saved with a version 2 field in it would be neither.
            doc.SchemaVersion = WorldDoc.CurrentSchemaVersion;
            return doc;
        }

        /// <summary>Serialises a building document.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static string SerializeBuilding(BuildingDoc doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            return Write(doc);
        }

        /// <summary>Deserialises a building document.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="UnsupportedSchemaVersionException">The document declares an unreadable schema version.</exception>
        /// <exception cref="InvalidOperationException">The document is of another kind.</exception>
        public static BuildingDoc DeserializeBuilding(string json)
        {
            JObject root = Parse(json);

            // No back-compatible default here, unlike a world: every building document ever
            // written carries a kind, so one that does not is a map being read as a building.
            RequireKind(root, BuildingDoc.BuildingKind, "building document", allowMissing: false);
            RequireSchemaVersion(
                root, "building document", BuildingDoc.CurrentSchemaVersion, BuildingDoc.CurrentSchemaVersion);

            return root.ToObject<BuildingDoc>(JsonSerializer.Create(CreateSettings()));
        }

        /// <summary>Serialises a catalog.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        public static string SerializeCatalog(Catalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            return Write(catalog);
        }

        /// <summary>Deserialises a catalog.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="UnsupportedSchemaVersionException">The catalog declares an unreadable schema version.</exception>
        public static Catalog DeserializeCatalog(string json)
        {
            JObject root = Parse(json);
            RequireSchemaVersion(
                root, "catalog", Catalog.CurrentSchemaVersion, Catalog.CurrentSchemaVersion);
            return root.ToObject<Catalog>(JsonSerializer.Create(CreateSettings()));
        }

        static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture,
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Include,
            Converters = { new StringEnumConverter() },
        };

        static string Write(object value)
        {
            // StringWriter rather than JsonConvert.SerializeObject so the indent newline can be
            // pinned to "\n"; JsonTextWriter takes it from the underlying writer, which would
            // otherwise mean CRLF on Windows and LF everywhere else.
            using (var buffer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" })
            {
                using (var writer = new JsonTextWriter(buffer)
                {
                    Formatting = Formatting.Indented,
                    Indentation = 2,
                    IndentChar = ' ',
                })
                {
                    JsonSerializer.Create(CreateSettings()).Serialize(writer, value);
                }

                return buffer.ToString();
            }
        }

        static JObject Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            using (var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
            })
            {
                return JObject.Load(reader);
            }
        }

        // The version is read before the document is materialised so a file from a future build
        // fails with a message about versions rather than an obscure binding error.
        static void RequireSchemaVersion(JObject root, string documentKind, int minSupported, int supported)
        {
            JToken token = root["schemaVersion"];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new UnsupportedSchemaVersionException(documentKind, null, supported);
            }

            int found = token.Value<int>();
            if (found < minSupported || found > supported)
            {
                throw new UnsupportedSchemaVersionException(documentKind, found, supported);
            }
        }

        /// <summary>
        /// Rejects a document of the wrong kind before it is materialised into the wrong type.
        /// </summary>
        /// <remarks>
        /// Worth its own check because the two kinds have the same shape from
        /// <c>generatedObjects</c> down: a building read as a world does not fail, it comes back
        /// as an empty map with default parameters. That is the quiet failure this whole preamble
        /// exists to prevent.
        /// </remarks>
        static void RequireKind(JObject root, string expected, string documentKind, bool allowMissing = true)
        {
            JToken token = root["kind"];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (allowMissing)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"This file does not say what it is, so it is not a {documentKind}.");
            }

            string found = token.Value<string>();
            if (!string.Equals(found, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"This is a '{found}' document, not a {documentKind}.");
            }
        }
    }
}
