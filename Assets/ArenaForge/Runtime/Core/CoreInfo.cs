using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// Version stamp written at the head of every serialised ArenaForge document.
    /// </summary>
    public sealed class SchemaStamp
    {
        /// <summary>Schema revision the document was written with.</summary>
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }
    }

    /// <summary>
    /// Identity of the engine-free Core layer. Placeholder for the real data model.
    /// </summary>
    public static class CoreInfo
    {
        /// <summary>Current document schema revision.</summary>
        public const int SchemaVersion = 1;

        /// <summary>
        /// Serialises the current schema stamp to JSON.
        /// </summary>
        /// <remarks>
        /// Round-tripping through Newtonsoft here is deliberate. Core sets
        /// <c>overrideReferences</c>, so its only precompiled dependency is
        /// Newtonsoft.Json.dll; without a call site the skeleton would compile
        /// even if that reference failed to resolve.
        /// </remarks>
        public static string SchemaStampJson()
        {
            return JsonConvert.SerializeObject(new SchemaStamp { SchemaVersion = SchemaVersion });
        }
    }
}
