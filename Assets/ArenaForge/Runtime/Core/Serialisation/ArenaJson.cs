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
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="UnsupportedSchemaVersionException">The document declares an unreadable schema version.</exception>
        public static WorldDoc DeserializeWorld(string json)
        {
            JObject root = Parse(json);
            RequireSchemaVersion(root, "world document", WorldDoc.CurrentSchemaVersion);
            return root.ToObject<WorldDoc>(JsonSerializer.Create(CreateSettings()));
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
            RequireSchemaVersion(root, "catalog", Catalog.CurrentSchemaVersion);
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
        static void RequireSchemaVersion(JObject root, string documentKind, int supported)
        {
            JToken token = root["schemaVersion"];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new UnsupportedSchemaVersionException(documentKind, null, supported);
            }

            int found = token.Value<int>();
            if (found != supported)
            {
                throw new UnsupportedSchemaVersionException(documentKind, found, supported);
            }
        }
    }
}
