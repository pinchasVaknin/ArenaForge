using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Field-by-field comparison of world documents. Separate from a serialised-string comparison
    /// on purpose: matching text would still pass if a field were dropped from both the writer and
    /// the reader, whereas this walks the object graph the rest of the system actually uses.
    /// </summary>
    static class WorldAssert
    {
        public static void AreDeepEqual(WorldDoc expected, WorldDoc actual)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.SchemaVersion, Is.EqualTo(expected.SchemaVersion), "schemaVersion");
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed), "seed");

            Assert.That(actual.Parameters.PlayfieldSize, Is.EqualTo(expected.Parameters.PlayfieldSize));
            Assert.That(actual.Parameters.LaneCount, Is.EqualTo(expected.Parameters.LaneCount));
            Assert.That(actual.Parameters.GridSize, Is.EqualTo(expected.Parameters.GridSize));
            Assert.That(actual.Parameters.StructureDensity, Is.EqualTo(expected.Parameters.StructureDensity));

            Assert.That(actual.GeneratedObjects.Count, Is.EqualTo(expected.GeneratedObjects.Count),
                "generated object count");
            for (int i = 0; i < expected.GeneratedObjects.Count; i++)
            {
                AreDeepEqual(expected.GeneratedObjects[i], actual.GeneratedObjects[i], $"generatedObjects[{i}]");
            }

            Assert.That(actual.Overrides.Count, Is.EqualTo(expected.Overrides.Count), "override count");
            for (int i = 0; i < expected.Overrides.Count; i++)
            {
                AreDeepEqual(expected.Overrides[i], actual.Overrides[i], $"overrides[{i}]");
            }
        }

        public static void AreDeepEqual(PlacedObject expected, PlacedObject actual, string where)
        {
            Assert.That(actual.StableId, Is.EqualTo(expected.StableId), $"{where}.stableId");
            Assert.That(actual.LogicalId, Is.EqualTo(expected.LogicalId), $"{where}.logicalId");
            Assert.That(actual.Pose, Is.EqualTo(expected.Pose), $"{where}.pose");
            Assert.That(actual.Tags, Is.EqualTo(expected.Tags), $"{where}.tags");
            AreDeepEqual(expected.Metadata, actual.Metadata, $"{where}.metadata");
        }

        public static void AreDeepEqual(EditOverride expected, EditOverride actual, string where)
        {
            Assert.That(actual.TargetId, Is.EqualTo(expected.TargetId), $"{where}.targetId");
            Assert.That(actual.Op, Is.EqualTo(expected.Op), $"{where}.op");
            Assert.That(actual.Pose, Is.EqualTo(expected.Pose), $"{where}.pose");
            Assert.That(actual.LogicalId, Is.EqualTo(expected.LogicalId), $"{where}.logicalId");
            Assert.That(actual.Tags, Is.EqualTo(expected.Tags), $"{where}.tags");
            AreDeepEqual(expected.Metadata, actual.Metadata, $"{where}.metadata");
        }

        public static void AreDeepEqual(Catalog expected, Catalog actual)
        {
            Assert.That(actual.SchemaVersion, Is.EqualTo(expected.SchemaVersion), "schemaVersion");
            Assert.That(actual.Entries.Count, Is.EqualTo(expected.Entries.Count), "entry count");

            for (int i = 0; i < expected.Entries.Count; i++)
            {
                CatalogEntry a = expected.Entries[i];
                CatalogEntry b = actual.Entries[i];
                string where = $"entries[{i}]";

                Assert.That(b.LogicalId, Is.EqualTo(a.LogicalId), $"{where}.logicalId");
                Assert.That(b.Tags, Is.EqualTo(a.Tags), $"{where}.tags");
                Assert.That(b.Footprint, Is.EqualTo(a.Footprint), $"{where}.footprint");
                Assert.That(b.Height, Is.EqualTo(a.Height), $"{where}.height");
                Assert.That(b.Weight, Is.EqualTo(a.Weight), $"{where}.weight");
                Assert.That(b.Sockets.Count, Is.EqualTo(a.Sockets.Count), $"{where}.sockets count");

                for (int s = 0; s < a.Sockets.Count; s++)
                {
                    Assert.That(b.Sockets[s].Name, Is.EqualTo(a.Sockets[s].Name), $"{where}.sockets[{s}].name");
                    Assert.That(b.Sockets[s].Tags, Is.EqualTo(a.Sockets[s].Tags), $"{where}.sockets[{s}].tags");
                    Assert.That(b.Sockets[s].LocalPose, Is.EqualTo(a.Sockets[s].LocalPose),
                        $"{where}.sockets[{s}].pose");
                }
            }
        }

        static void AreDeepEqual(
            IReadOnlyDictionary<string, string> expected,
            IReadOnlyDictionary<string, string> actual,
            string where)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"{where} count");
            foreach (KeyValuePair<string, string> pair in expected)
            {
                Assert.That(actual.ContainsKey(pair.Key), $"{where} is missing '{pair.Key}'");
                Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), $"{where}['{pair.Key}']");
            }
        }
    }
}
