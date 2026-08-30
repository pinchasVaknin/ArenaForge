using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// What the road stage costs the maps that never asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RoadValidationTests"/> asks whether a network is a network and whether the maps
    /// that asked for one are still worth playing. This file asks the question that only arises
    /// because the stage was wedged into a pipeline that already existed: that a map at a
    /// <see cref="ArenaParams.RoadDensity"/> of zero — which is every map anybody has already
    /// generated — is untouched, object for object.
    /// </para>
    /// </remarks>
    public sealed class RoadPipelineTests
    {
        /// <summary>Peak-to-trough ground the density-zero properties are checked over.</summary>
        /// <remarks>
        /// Four, matching <c>RoadValidationTests</c>, so what is being tested is the parameter and
        /// not an absence of ground for a road to be laid over.
        /// </remarks>
        const float Amplitude = 4f;

        /// <summary>
        /// FNV-1a of the document of seeds 1..200 of the default map, recorded from the generator
        /// as it stood before the road stage entered the pipeline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole of what makes the reordering safe to land. A road density of zero is the
        /// default, so every map anybody has already generated is one of these, and the override
        /// system rests on a regeneration putting every object back where it was — a stage that
        /// shifted one crate would invalidate every edit made against it.
        /// </para>
        /// <para>
        /// Recorded rather than derived, and that is the point: the numbers below came out of a
        /// build with no road stage in <see cref="ArenaLayoutGenerator.Generate"/> at all, so
        /// nothing in the code under test can agree with them by construction. If a deliberate
        /// change to the generator moves the default map this fails and the list has to be
        /// re-recorded, which is the notification rather than the nuisance.
        /// </para>
        /// <para>
        /// <strong>Of the document rather than of the text it serialises to</strong>, which is the
        /// stronger of the two and the only one that is a fact about the generator. Every float goes
        /// in as the bits it is, so nothing here depends on how a runtime renders one — and they do
        /// differ: Newtonsoft writes a quarter turn as <c>0.70710677</c> under .NET and
        /// <c>0.707106769</c> under the runtime the editor runs on, which is the same number twice.
        /// A digest over the serialised text would fail on one of the two machines and say the
        /// generator had moved.
        /// </para>
        /// </remarks>
        static readonly string[] RoadlessDigests =
        {
            "3ffe0b5e16e2d675", "dd57fe8193f6d968", "6badcf249d024496", "eb9da7127da8b649",
            "ded31af12eb702c0", "d9ade4da615be309", "db6aab859d486235", "670302a6ef903db8",
            "959f81e8fc6023fe", "3f0d86fa872f2494", "f38715fced5db3d5", "201bcbea0de634d4",
            "48038093449e01ea", "e4e1604ff82b583a", "c36201f8394e8745", "d3796f3bc182ee36",
            "94a799569b5fbe91", "54e555cab3558166", "cd632d03af49ef8b", "252bb5ce3bcb7304",
            "e2cc751613e6febf", "2dcafc3567a5fa9a", "44f9c1183a3759b8", "97979c2e7dad5198",
            "61438f334715b2ef", "70f549632a1c7b8d", "bbab96e901323ff4", "179f55c6efa2890c",
            "55f0a2fa959596db", "67bf49d876496ecf", "45e51a29aabccb7c", "3fcdfc7be092d17b",
            "d79d6074c32ed8f7", "8749ddc22d9c095f", "c8109d3e10d8ac56", "881edbd50cdeae9f",
            "56f7eebb0d03ff99", "069e4c4108b6d072", "96f960f89b86f169", "c1cfb8effc83a89a",
            "6ba0e4f847380cb6", "12f256abc92cf643", "e22e1902fae449a9", "ea6f17fd7773b590",
            "87374a3897b86008", "01d1251bcba64e39", "7d3364c06fabb6c4", "ae828cd811245b87",
            "aa41038f7b6c9b98", "8b188f3d8fcb5620", "095e272a1dae6567", "decc6ca58fe182ad",
            "be06ad80dbf6b590", "4bc506564a8077bd", "d1ac6c5a4a700fd3", "a7c58fee0eecbf9b",
            "acb5a4855b2db8f1", "4ad13e303407aba7", "449a8dd8e90048b3", "1ccb94c9130c5b85",
            "6548e405cff00aaa", "13d172ea418585e0", "9dee3284162240c3", "51293c35f3b8a9d3",
            "f74a9499ba6f8c4d", "e86af1e661366a0e", "d23c1d6450d079b6", "d78c4a36f5e7b41f",
            "e4ae972aa64955a5", "b1d4e72812e52258", "fe52ae22520ef4a1", "133a2ab3f0dd164e",
            "f3809d7385c43d93", "1d1519d00933df3d", "4853810634c31710", "8f5ef544cab18eda",
            "631ecabed4b53c85", "df1203fe54708119", "8564106da289a0ac", "7da5a58d3790ef05",
            "6c9652052e7d7d65", "ecc47467574d1eb4", "d4d856e87a39db40", "ee89d5e611cab9b1",
            "0c24821832320557", "27fbc4ce7d5fe2f6", "a39ccbc26364cba6", "69b1553672506a24",
            "4370a805f7664702", "2f4ea9f8e8263781", "51f984e3c1ff72c5", "0291a3002310e7d6",
            "4bc7b8870effd9ef", "ebbbb9250a983150", "d44a3cd87cbeb88a", "16f05f8c82ba8cbc",
            "66de54cc610713bd", "339bc5f56bad3f36", "dc9a5ea2ea9a98ea", "6db80fe53d8eb2f5",
            "e9ddd672f924e883", "634cc4608431b0ad", "1726776b0b0d433a", "396097645480af51",
            "b8abb166d4d73df5", "1131017d657cf934", "735ee277ac1619ac", "66c34bf84baa38b2",
            "8078c21b5f0e5b29", "7ef0e3bbd0acd926", "3a4f526a57e03636", "e788802b56282750",
            "ab569030c185c080", "a1aa17c1ab6c592c", "7476ee9c2c6142ca", "8420024106434635",
            "f9a9f0a50b022655", "e213dd0ea6a2c7ad", "7de155bca9f68d0e", "b10573d4ae0d7c58",
            "b947d8dbbc2a3f42", "f6310890aedad5bc", "5fdaf0e94e2e14f9", "e90a1251dae69486",
            "12978f7e75467761", "2fefa476f9dec19b", "2c180fbc400c386c", "c0287cb95572f023",
            "32d024489905c709", "237b350fd898d003", "eb6ca2f573d57094", "d1d24645e9546e59",
            "0afc60584cf244c5", "c22af264ff808c1d", "4bd94b88e934b9b8", "aadc6ed67f8adc1e",
            "3845c44fcf1c80af", "3f099daf52d0545a", "8c00d34d6548a2f9", "59e100644dc9531a",
            "b634d8bfdfe26933", "f16de6e680890296", "d7addc71da60c214", "4eb760f2652a29a6",
            "0641fc567f19a4d5", "e105083e615b625b", "a61ef8ce3274faa8", "aa1f11528115fd09",
            "d50422d6f3a8ba55", "449dd8a6a6e73891", "ca69a512309e0825", "f09e28058c970635",
            "b04c0c44a41abf8e", "65c3f87829770c7c", "17fb8bd392594b86", "be6dc06f18b8c19e",
            "6f3ebc380d33000e", "dce4e10518df01e4", "4fc28c4beb137671", "38f6f6fb8403d2c9",
            "f32fa10639f00b80", "87263fa78cae281f", "86d3d19eb0e49913", "e1b65c7d0ab08e3b",
            "acbcd316d45c0f05", "3bed8f83562ca521", "0e9693a09e972913", "3100a472ecf12fd1",
            "c6ccc257d1537247", "71b1c2166dabf292", "cc8ce964554fc459", "0fefa12ca2082e92",
            "39b3ac696d8f4b42", "b74702247724097e", "427b784adb9ae4df", "696914d376b68683",
            "f275aeb0e74fecaa", "4f14bfc3ab55d652", "d08bac2adca97b90", "f30ed8e0bce00eab",
            "6aa0e4a285b571cc", "b1528b4af887b948", "919f4f55ab3b5c58", "8899a8f507786167",
            "19e6e21d727aee2d", "529b94a153308fbd", "3af3c8c3f815838f", "bf08480a776afc3e",
            "9e6470631d2e1d24", "00a5e838672af373", "a20d6a7dbb112ca2", "a48551c5cfa1e7f2",
            "8a4f1069242b2375", "20fb325d1850f70b", "de0b0f2f5e072eaf", "49e0a8db66f86dbc",
            "7a80e8674d200d63", "ee209a4f6ae2c849", "96bc038989d40188", "0c5b290440a253e9",
        };

        // --- the maps that never asked for a road are the maps they were ---------------------------

        /// <remarks>
        /// Two hundred seeds, each reduced to one number over every id, pose, tag and metadata entry
        /// the document holds — see <see cref="RoadlessDigests"/> and <see cref="Digest"/>.
        /// </remarks>
        [Test]
        public void ARoadDensityOfZeroLeavesEveryMapExactlyAsItWas()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var moved = new List<string>();

            for (int seed = 1; seed <= RoadlessDigests.Length; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = (ulong)seed }, catalog);

                string digest = Digest(doc).ToString("x16", CultureInfo.InvariantCulture);

                if (!string.Equals(digest, RoadlessDigests[seed - 1], StringComparison.Ordinal))
                {
                    moved.Add($"seed {seed}: {RoadlessDigests[seed - 1]} became {digest}");
                }
            }

            Assert.That(moved, Is.Empty,
                "the default map is no longer the map it was before the road stage was in the pipeline");
        }

        /// <remarks>
        /// The mechanism the property above rests on, asserted on its own so that a failure there
        /// says which of the two things went wrong. A density of zero lays no corridor — see
        /// <see cref="ARoadDensityOfZeroLaysNoRoad"/> — so the rule that keeps cover off one has
        /// nothing to reject a candidate with, and a constraint kind that rejected nothing is left
        /// out of the placement statistics entirely.
        /// </remarks>
        [Test]
        public void ARoadDensityOfZeroRejectsNothingForAReservedPath()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            string key = CoverPlacer.StatsPrefix + "rejected_" +
                         PlacementStats.MetadataName(ConstraintKind.OffReservedPath);

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed }, catalog);

                Assert.That(doc.Metadata.ContainsKey(key), Is.False,
                    $"seed {seed}: a reserved path rejected a candidate on a map with no road on it");
            }
        }

        /// <remarks>
        /// The mechanism both properties above rest on, asserted on its own so a failure there says
        /// which of the things went wrong. Checked on ground a road could have been laid over — see
        /// <see cref="Amplitude"/> — so what is being tested is the parameter and not an absence of
        /// anywhere to put a road.
        /// </remarks>
        [Test]
        public void ARoadDensityOfZeroLaysNoRoad()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var parameters = new ArenaParams
                {
                    Seed = seed,
                    RoadDensity = 0f,
                    TerrainAmplitude = Amplitude,
                };

                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                RoadNetwork network = RoadNetwork.Build(
                    parameters,
                    ArenaLayout.Build(parameters),
                    ArenaLayoutGenerator.TerrainBeforeRoads(doc),
                    doc.GeneratedObjects);

                Assert.That(network.IsEmpty, Is.True, $"seed {seed}: a road was laid at density zero");
                Assert.That(network.Segments, Is.Empty, $"seed {seed}");
                Assert.That(network.Junctions, Is.Empty, $"seed {seed}");
                Assert.That(network.Corridors, Is.Empty, $"seed {seed}");
                Assert.That(network.CorridorLength, Is.Zero, $"seed {seed}");
                Assert.That(network.UnbraidedLength, Is.Zero, $"seed {seed}");
            }
        }

        /// <summary>
        /// Reduces a document to one number over everything a regeneration has to reproduce: every
        /// object in order, its two ids, its pose, its tags and its metadata, and then the world's
        /// own metadata.
        /// </summary>
        /// <remarks>
        /// Floats go in as their bits rather than as text, so this is a fact about the values and
        /// not about the runtime's formatting. The metadata of both a placed object and a document
        /// is a <c>SortedDictionary</c> with an ordinal comparer, so walking it is already an order
        /// nothing can shuffle.
        /// </remarks>
        static ulong Digest(WorldDoc doc)
        {
            ulong hash = StableHash.Hash64("arenaforge/world");

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                hash = StableHash.Combine(hash, placed.StableId);
                hash = StableHash.Combine(hash, placed.LogicalId);
                hash = Mix(hash, placed.Pose.Position.X);
                hash = Mix(hash, placed.Pose.Position.Y);
                hash = Mix(hash, placed.Pose.Position.Z);
                hash = Mix(hash, placed.Pose.Rotation.X);
                hash = Mix(hash, placed.Pose.Rotation.Y);
                hash = Mix(hash, placed.Pose.Rotation.Z);
                hash = Mix(hash, placed.Pose.Rotation.W);
                hash = Mix(hash, placed.Pose.Scale);

                for (int t = 0; t < placed.Tags.Count; t++)
                {
                    hash = StableHash.Combine(hash, placed.Tags[t]);
                }

                foreach (KeyValuePair<string, string> entry in placed.Metadata)
                {
                    hash = StableHash.Combine(hash, entry.Key);
                    hash = StableHash.Combine(hash, entry.Value);
                }
            }

            foreach (KeyValuePair<string, string> entry in doc.Metadata)
            {
                hash = StableHash.Combine(hash, entry.Key);
                hash = StableHash.Combine(hash, entry.Value);
            }

            return hash;
        }

        static ulong Mix(ulong hash, float value) =>
            StableHash.Combine(hash, BitConverter.DoubleToInt64Bits(value));
    }
}
