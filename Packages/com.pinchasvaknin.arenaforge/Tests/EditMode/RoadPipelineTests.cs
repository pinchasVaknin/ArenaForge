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
            "80f598d402e49697", "3f93b2b6ad698feb", "1a4faa6f34491125", "d4d2ed6eda6a25b4",
            "fe0d2350b2c286f3", "e688e8641bd57e06", "cc1fbbc3ca4759bf", "5ae4d219e8375184",
            "98f2866cd1e579ec", "3dacc78115ee631d", "5fe46352fef184c8", "09ef1def8cd54201",
            "929077006ecf7821", "be4847d1c8b8c520", "3f31a5e8d715a5ac", "5e11b824b419be15",
            "f4595343c6633e40", "bed756a5934e485a", "0a01ec802be918c8", "a3ec19f200e6d396",
            "e9a4b49ca0963aa8", "3dddb7b0426c2301", "7cf38abacfa1e384", "0e996516bb563ed1",
            "94e5a52c90c1e11c", "4d32552d6ce79073", "4cb356c8c5e6f945", "ab25d4345a8ab30d",
            "2c0916290b2ae392", "2509dcb7f6c7cdd0", "9c05824a14bbfd85", "f9954536176356fb",
            "10b42320070c81c5", "45a7c525e939a5c8", "a4d2e74663f9cd06", "b0e4de2943e98491",
            "b531ff14c1f1f2b6", "22a47e87128650eb", "6f2c2b6baf8eabc6", "b7d7be15485c7f58",
            "8c273de8beaf1ecb", "f1e7efd16ea9fffd", "1cbe56f2923a4695", "ce99c740298f25a3",
            "fc094da0f576622b", "968baee7adf22da9", "ba5cfb6e499fad81", "6665c657d879cf42",
            "310586847f6ae452", "ee0666684140102b", "5e24b3bc465cb262", "589a3f01124750be",
            "fbc5c491c072c1bc", "17de946505abee37", "910b3b24b4840052", "4eb9a33c876435c5",
            "b8ad8a2ecff4ebc7", "7c5223eba3baec9c", "9c17867bbdd634f0", "86124077f338c7b9",
            "2e8bf216fd07ac1c", "8f00f1ce6b2021ed", "07bbcbeae9d49689", "2c3e467e54dc54e1",
            "799132cf7fb2ef03", "c4cfd1bc104f18dc", "0051d3f71466d1c2", "d8c6a912bb3ac261",
            "6b5096fa60a1b4c5", "55bf17c290dced4b", "52d9d1c899eac440", "71d95a4611f71e8b",
            "f7573dd1c2b99103", "e0cc301d502caa63", "3d538dc205661e36", "78194f356fd71839",
            "65c8776b28500d09", "224a8f36b89b2984", "ad32fc35bd02ff0b", "d7313a09e4437174",
            "17270f5dbc6c3c34", "7293aea7b0766df5", "e1c06af9be237295", "335851bace39be9d",
            "d93cd510c6571f91", "83647f6cfa984b18", "1655d5774f715fd8", "4b42da883c55a2eb",
            "0a930b41ecfe348a", "435555fb0564181b", "bcbc8b73cecbc070", "486ef56d51bb9037",
            "fb34e167c4465d3a", "1d9ae7fc89e83bd6", "ad0a418efc43a65e", "98673406a8f9fbc8",
            "bddc602c168d1baa", "551124ab0b7461f9", "7f624617f885c246", "6ff71bb88d71b2bd",
            "ce3c4b255f0556ed", "e480b748ccfe5357", "6140e2ceb79d3fd5", "28f16faf99c4e389",
            "87834ba3e857423c", "48b6ab15cab04df0", "ac22127f90d8ccb1", "583b5784fadeb4e1",
            "b6c26ec0ae8cb110", "2fbc7d34ddbadf72", "c1c2a907bb1d4a97", "0e6ccbb88706347d",
            "97360b1c7832507c", "5fdcb00a9b2389c3", "079fda518fa61340", "98fc31fb72504d90",
            "68e91f3ed5fc6897", "2c32f84eca037b1f", "ac55992ea1193036", "625c2ac98f6f2e84",
            "00c5f7db1ec415be", "a318fc6e50c598fd", "5e931abdeb722814", "4054101b54e18b9c",
            "24b0e84d4510b102", "a1c56e6623985ced", "5c9144bf51d0ad18", "24981881dd99a65c",
            "91552145b97c3e6d", "e060f04193b4cdc0", "7fa01f6d0fd9fabe", "b7fe6b09ae0cd255",
            "3e4bd6f3e531f539", "4bcafe0ea2288861", "c91806157f531c48", "64237bb8f9d32a93",
            "9598235f8de7fbf0", "47175dd6e2dacd62", "f211f38aad9232e7", "af34f61574104336",
            "4d6b814152c69260", "76329bc8e780e82b", "491a697a7b23da74", "c582833e89545b77",
            "7c92c412f844d18a", "b899a18bb798ef3e", "33f06622fd9e3760", "b0cabc7118129f8a",
            "bdc983a186ea701f", "623969e65abf651c", "7d987ebc4c5fbfe9", "9131f004bcced4a9",
            "43b51a1db3ba5207", "48102bb1f6d3fc30", "a7a0261a1ef5d74c", "076ef5b002804e0c",
            "e72b3f7858f3d8df", "555c19be08b13a1f", "b2166ff39183699a", "f7f7cb95b95ca401",
            "56521c76c5ec1211", "cf189d2627d7fd1a", "4452640d8b92f489", "4dd57df4f81b2af1",
            "7dcc0ddfa5d82b71", "5311a55f32bbc9c6", "557d608841f3d9ae", "de9e6f249ec67e79",
            "218cbacfb9a648e8", "fcb8f9a91a078879", "2dd69356ec346879", "34429ddc8c9668d5",
            "cf70f0a728d4faa1", "de33cb5eb8f861e6", "6da0ac2bfde0a698", "4119b9bb0b871421",
            "9998f1e6d7c3fbda", "4697c454270207b3", "ade8bbd1fc857f6c", "5ea14a02ecefedb9",
            "32c171f1ff1b90e0", "113d278662ac793b", "864d8535763b0dfa", "0da601c7e38583fd",
            "8d5cfa6c9b4e2169", "7cf82a45ec24e188", "f017e3028205a8e6", "ef5a3cb8b0286ce3",
            "cc965cd3ec6a60c9", "307fb1a31c8ad8db", "88e902b019711ac8", "abe6f6945a927ca5",
            "cb0e11bff0b735f2", "53841c0febc49523", "cbc616bfb49043c3", "7f3074daea1e7a80",
            "32f1715c9a496a51", "2a19f151a36ba0ea", "61517c45636e8160", "fa9244cf2fb44543",
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
