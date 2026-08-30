using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// JSON round-tripping. A world document is the only persistent form of a map, so a lossy or
    /// machine-dependent serialiser would take the override system down with it.
    /// </summary>
    public sealed class SerialisationTests
    {
        static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();

        [Test]
        public void AWorldWithAllFourOverrideKindsRoundTrips()
        {
            WorldDoc original = TestWorlds.SampleWorld();

            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(original));

            WorldAssert.AreDeepEqual(original, restored);
        }

        [Test]
        public void ARoundTrippedWorldResolvesIdentically()
        {
            WorldDoc original = TestWorlds.SampleWorld();
            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(original));

            ResolvedWorld before = original.Resolve();
            ResolvedWorld after = restored.Resolve();

            Assert.That(after.Objects.Count, Is.EqualTo(before.Objects.Count));
            for (int i = 0; i < before.Objects.Count; i++)
            {
                WorldAssert.AreDeepEqual(before.Objects[i], after.Objects[i], $"objects[{i}]");
            }
        }

        [Test]
        public void SerialisingTwiceProducesIdenticalText()
        {
            WorldDoc doc = TestWorlds.SampleWorld();

            Assert.That(ArenaJson.SerializeWorld(doc), Is.EqualTo(ArenaJson.SerializeWorld(doc)));
        }

        [Test]
        public void ARoundTripDoesNotChangeTheText()
        {
            string first = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());
            string second = ArenaJson.SerializeWorld(ArenaJson.DeserializeWorld(first));

            Assert.That(second, Is.EqualTo(first));
        }

        /// <remarks>
        /// Non-default values throughout, and every one of them exactly representable, so a
        /// serialiser that dropped the four would fail here rather than pass on the defaults a
        /// fresh <see cref="ArenaParams"/> already carries.
        /// </remarks>
        [Test]
        public void TheRoadParametersRoundTrip()
        {
            var doc = new WorldDoc
            {
                Parameters = new ArenaParams
                {
                    Seed = 20260825UL,
                    RoadDensity = 1.5f,
                    ArteryWidth = 6.25f,
                    PathWidth = 2.75f,
                    MaxRoadGradient = 0.125f,
                },
            };

            ArenaParams restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(doc)).Parameters;

            Assert.That(restored.RoadDensity, Is.EqualTo(1.5f), "roadDensity");
            Assert.That(restored.ArteryWidth, Is.EqualTo(6.25f), "arteryWidth");
            Assert.That(restored.PathWidth, Is.EqualTo(2.75f), "pathWidth");
            Assert.That(restored.MaxRoadGradient, Is.EqualTo(0.125f), "maxRoadGradient");
        }

        /// <remarks>
        /// The whole of why the four cost no schema version. A document written before they
        /// existed carries none of them and comes back meaning exactly what it meant — a density
        /// of zero, which is no roads — so there is nothing an old file could be misread as.
        /// </remarks>
        [Test]
        public void AWorldWrittenBeforeTheRoadParametersLoadsAtTheirDefaults()
        {
            ArenaParams restored =
                ArenaJson.DeserializeWorld(TestWorlds.ReadFixture("world-v1.json")).Parameters;

            Assert.That(restored.RoadDensity, Is.EqualTo(0f), "roadDensity");
            Assert.That(restored.ArteryWidth, Is.EqualTo(4f), "arteryWidth");
            Assert.That(restored.PathWidth, Is.EqualTo(2f), "pathWidth");
            Assert.That(restored.MaxRoadGradient, Is.EqualTo(0.6f), "maxRoadGradient");
        }

        [Test]
        public void OutputIsIndentedWithLineFeedsOnly()
        {
            string json = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

            Assert.That(json, Does.Not.Contain("\r"), "output must not depend on the host's line ending");
            Assert.That(json, Does.Contain("\n  \"parameters\""), "output should be indented two spaces");
            Assert.That(json, Does.Contain("\n    \"seed\""), "the seed lives inside the parameters");
        }

        [Test]
        public void OverrideOperationsAreWrittenAsReadableNames()
        {
            string json = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

            Assert.That(json, Does.Contain("\"op\": \"SwapAsset\""));
            Assert.That(json, Does.Not.Contain("\"op\": 3"));
        }

        [Test]
        public void FloatsSurviveARoundTripBitForBit()
        {
            float[] awkward =
            {
                0.1f, 1f / 3f, -1f / 7f, 1e-8f, 123456.789f, float.Epsilon,
                float.MaxValue, float.MinValue, -0.000123456f, 16777217f,
            };

            var doc = new WorldDoc { Parameters = new ArenaParams { Seed = ulong.MaxValue } };
            for (int i = 0; i < awkward.Length; i++)
            {
                doc.GeneratedObjects.Add(new PlacedObject(
                    $"map/test/float_{i:00}",
                    "cover/low/crate_wood_01",
                    new Pose(
                        new Vec3(awkward[i], -awkward[i], awkward[(i + 1) % awkward.Length]),
                        new Quat(awkward[i], 0f, 0f, 1f),
                        awkward[i]),
                    null,
                    null));
            }

            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(doc));

            Assert.That(restored.Parameters.Seed, Is.EqualTo(ulong.MaxValue));
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                Pose expected = doc.GeneratedObjects[i].Pose;
                Pose actual = restored.GeneratedObjects[i].Pose;

                AssertSameBits(expected.Position.X, actual.Position.X, $"objects[{i}].position.x");
                AssertSameBits(expected.Position.Y, actual.Position.Y, $"objects[{i}].position.y");
                AssertSameBits(expected.Position.Z, actual.Position.Z, $"objects[{i}].position.z");
                AssertSameBits(expected.Rotation.X, actual.Rotation.X, $"objects[{i}].rotation.x");
                AssertSameBits(expected.Scale, actual.Scale, $"objects[{i}].scale");
            }
        }

        [Test]
        public void OutputDoesNotDependOnTheCurrentCulture()
        {
            // A culture with a comma decimal separator would corrupt every float in the file if
            // the serialiser followed the thread's culture instead of the invariant one.
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                string invariant = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string german = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

                Assert.That(german, Is.EqualTo(invariant));
                WorldAssert.AreDeepEqual(TestWorlds.SampleWorld(), ArenaJson.DeserializeWorld(german));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void MetadataIsWrittenInAStableOrderRegardlessOfInsertionOrder()
        {
            string forward = ArenaJson.SerializeWorld(WithMetadata(new[] { "alpha", "beta", "gamma" }));
            string backward = ArenaJson.SerializeWorld(WithMetadata(new[] { "gamma", "beta", "alpha" }));

            Assert.That(backward, Is.EqualTo(forward));
        }

        [Test]
        public void EmptyTagsAndMetadataAreOmitted()
        {
            var doc = new WorldDoc();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/test/bare", "cover/low/crate_wood_01", Pose.Identity, null, null));

            string json = ArenaJson.SerializeWorld(doc);

            Assert.That(json, Does.Not.Contain("\"tags\""));
            Assert.That(json, Does.Not.Contain("\"metadata\""));
        }

        [Test]
        public void AnUnknownSchemaVersionIsRejected()
        {
            string json = ArenaJson.SerializeWorld(TestWorlds.SampleWorld())
                .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99");

            var error = Assert.Throws<UnsupportedSchemaVersionException>(() => ArenaJson.DeserializeWorld(json));

            Assert.That(error.FoundVersion, Is.EqualTo(99));
            Assert.That(error.SupportedVersion, Is.EqualTo(WorldDoc.CurrentSchemaVersion));
            Assert.That(error.Message, Does.Contain("99"));
        }

        // --- two kinds of document ------------------------------------------------------------

        [Test]
        public void AWorldWrittenBeforeBuildingsExistedStillLoads()
        {
            // The whole of the back-compatibility claim, against a file committed at the previous
            // revision rather than against one this build wrote and then edited.
            WorldDoc restored = ArenaJson.DeserializeWorld(TestWorlds.ReadFixture("world-v1.json"));

            WorldAssert.AreDeepEqual(TestWorlds.SampleWorld(), restored);
        }

        /// <remarks>
        /// Reading upgrades, so a file loaded at the old revision and saved again is a whole
        /// document at the new one — not a version 1 header over a body carrying a version 2
        /// field, which is what leaving the number alone would produce.
        /// </remarks>
        [Test]
        public void AWorldLoadedFromTheOldRevisionIsSavedAtTheCurrentOne()
        {
            WorldDoc restored = ArenaJson.DeserializeWorld(TestWorlds.ReadFixture("world-v1.json"));

            string resaved = ArenaJson.SerializeWorld(restored);

            Assert.That(restored.SchemaVersion, Is.EqualTo(WorldDoc.CurrentSchemaVersion));
            Assert.That(
                Normalise(resaved), Is.EqualTo(Normalise(TestWorlds.ReadFixture("world.json"))));
        }

        [Test]
        public void AWorldIsWrittenAtTheCurrentVersionSayingWhatItIs()
        {
            string json = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

            Assert.That(json, Does.Contain("\"schemaVersion\": 2"));
            Assert.That(json, Does.Contain("\"kind\": \"world\""));
        }

        [Test]
        public void ABuildingRoundTrips()
        {
            BuildingDoc original = TestWorlds.SampleBuilding();

            BuildingDoc restored = ArenaJson.DeserializeBuilding(ArenaJson.SerializeBuilding(original));

            Assert.That(
                ArenaJson.SerializeBuilding(restored), Is.EqualTo(ArenaJson.SerializeBuilding(original)));
            Assert.That(restored.Floors.Count, Is.EqualTo(original.Floors.Count));
            for (int i = 0; i < original.Floors.Count; i++)
            {
                Assert.That(restored.Floors[i].Seed, Is.EqualTo(original.Floors[i].Seed), $"floors[{i}]");
            }
        }

        /// <remarks>
        /// The failure the kind field exists to prevent. The two documents have the same shape
        /// from <c>generatedObjects</c> down, so without it this would not throw — it would come
        /// back as a map with default parameters and the building's objects in it.
        /// </remarks>
        [Test]
        public void ABuildingIsNotReadAsAMap()
        {
            string json = ArenaJson.SerializeBuilding(TestWorlds.SampleBuilding());

            var error = Assert.Throws<InvalidOperationException>(() => ArenaJson.DeserializeWorld(json));

            Assert.That(error.Message, Does.Contain("building"));
        }

        [Test]
        public void AMapIsNotReadAsABuilding()
        {
            string json = ArenaJson.SerializeWorld(TestWorlds.SampleWorld());

            Assert.Throws<InvalidOperationException>(() => ArenaJson.DeserializeBuilding(json));
        }

        [Test]
        public void AWorldFromBeforeTheKindFieldIsNotMistakenForABuilding()
        {
            // A v1 file carries no kind at all. It is read as a map, which is what it is, and
            // refused as a building rather than being let through on the missing field.
            Assert.Throws<InvalidOperationException>(
                () => ArenaJson.DeserializeBuilding(TestWorlds.ReadFixture("world-v1.json")));
        }

        [Test]
        public void AMissingSchemaVersionIsRejected()
        {
            var error = Assert.Throws<UnsupportedSchemaVersionException>(
                () => ArenaJson.DeserializeWorld("{ \"parameters\": { \"seed\": 1 } }"));

            Assert.That(error.FoundVersion, Is.Null);
        }

        [Test]
        public void AnOverrideMissingItsPayloadIsRejectedOnLoad()
        {
            string json =
                "{ \"schemaVersion\": 1, \"parameters\": { \"seed\": 1 }, \"overrides\": [ " +
                "{ \"targetId\": \"map/lane_mid/cover_00\", \"op\": \"Move\" } ] }";

            // The deserialiser wraps the failure, so the assertion is on the innermost cause: our
            // own constructor validation, naming the override that is malformed.
            Exception error = Assert.Catch(() => ArenaJson.DeserializeWorld(json));

            Assert.That(error.GetBaseException(), Is.InstanceOf<ArgumentException>());
            Assert.That(error.GetBaseException().Message, Does.Contain("map/lane_mid/cover_00"));
        }

        [Test]
        public void TheCatalogRoundTrips()
        {
            Catalog original = TestWorlds.SampleCatalog();

            Catalog restored = ArenaJson.DeserializeCatalog(ArenaJson.SerializeCatalog(original));

            WorldAssert.AreDeepEqual(original, restored);
        }

        [Test]
        public void ACatalogWithAnUnknownSchemaVersionIsRejected()
        {
            string json = ArenaJson.SerializeCatalog(TestWorlds.SampleCatalog())
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

            Assert.Throws<UnsupportedSchemaVersionException>(() => ArenaJson.DeserializeCatalog(json));
        }

        [Test]
        public void TheCommittedWorldFixtureMatchesWhatTheSerialiserWrites()
        {
            string expected = Normalise(TestWorlds.ReadFixture("world.json"));
            string actual = Normalise(ArenaJson.SerializeWorld(TestWorlds.SampleWorld()));

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void TheCommittedCatalogFixtureMatchesWhatTheSerialiserWrites()
        {
            string expected = Normalise(TestWorlds.ReadFixture("catalog.json"));
            string actual = Normalise(ArenaJson.SerializeCatalog(TestWorlds.SampleCatalog()));

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void TheCommittedFixturesLoad()
        {
            WorldAssert.AreDeepEqual(
                TestWorlds.SampleWorld(), ArenaJson.DeserializeWorld(TestWorlds.ReadFixture("world.json")));
            WorldAssert.AreDeepEqual(
                TestWorlds.SampleCatalog(), ArenaJson.DeserializeCatalog(TestWorlds.ReadFixture("catalog.json")));
        }

        static WorldDoc WithMetadata(IReadOnlyList<string> keysInOrder)
        {
            var metadata = new Dictionary<string, string>();
            for (int i = 0; i < keysInOrder.Count; i++)
            {
                // Value derived from the key, not the index, so insertion order is the only
                // difference between two documents built from different key orders.
                metadata[keysInOrder[i]] = keysInOrder[i] + "-value";
            }

            var doc = new WorldDoc();
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/test/meta", "cover/low/crate_wood_01", Pose.Identity, null, metadata));
            return doc;
        }

        static void AssertSameBits(float expected, float actual, string where)
        {
            Assert.That(
                BitConverter.ToInt32(BitConverter.GetBytes(actual), 0),
                Is.EqualTo(BitConverter.ToInt32(BitConverter.GetBytes(expected), 0)),
                $"{where}: expected {expected:R}, got {actual:R}");
        }
    }
}
