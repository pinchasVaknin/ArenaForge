using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// Thrown when a document declares a schema version this build does not understand, or
    /// declares none at all.
    /// </summary>
    /// <remarks>
    /// Distinct from a parse failure on purpose: a file written by a newer build is a different
    /// problem from a corrupt one, and the user needs to be told which.
    /// </remarks>
    public sealed class UnsupportedSchemaVersionException : Exception
    {
        /// <summary>The version found in the file, or null if the field was missing.</summary>
        public int? FoundVersion { get; }

        /// <summary>The version this build reads and writes.</summary>
        public int SupportedVersion { get; }

        /// <summary>Creates the exception for a document declaring an unreadable version.</summary>
        public UnsupportedSchemaVersionException(string documentKind, int? foundVersion, int supportedVersion)
            : base(Describe(documentKind, foundVersion, supportedVersion))
        {
            FoundVersion = foundVersion;
            SupportedVersion = supportedVersion;
        }

        static string Describe(string documentKind, int? foundVersion, int supportedVersion)
        {
            return foundVersion.HasValue
                ? $"This ArenaForge {documentKind} declares schema version {foundVersion.Value}; " +
                  $"this build reads version {supportedVersion}."
                : $"This ArenaForge {documentKind} declares no schema version; " +
                  $"this build reads version {supportedVersion}.";
        }
    }
}
