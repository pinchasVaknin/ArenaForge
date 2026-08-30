using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The kerbing laid along the carriageways: that it edges the roads rather than standing in
    /// them, that its ids survive a regeneration, and that a workspace without the art is untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RoadValidationTests"/> asks whether a network is a network and
    /// <see cref="RoadPipelineTests"/> what the road stage costs the maps that never asked for one.
    /// This file asks the two questions a stage that puts objects down raises and a stage that puts
    /// nothing down does not: whether the objects are where they should be, and whether the map
    /// without them is the map it always was.
    /// </para>
    /// <para>
    /// <strong>The ground is flat here on purpose.</strong> Where a kerb sits across its road is a
    /// two-dimensional question and the height it is stood at is settled after the rules have run,
    /// so relief would add a variable to every measurement below without putting one of them at
    /// risk. What relief does to a road is asserted where the profile is worked out, in
    /// <see cref="TerrainTests"/>.
    /// </para>
    /// </remarks>
    public sealed class RoadKerbTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every map.</summary>
        const int Seeds = 120;

        /// <summary>
        /// A road density that lays a real network on the default map, matching
        /// <see cref="RoadPipelineTests"/>.
        /// </summary>
        const float Density = 1f;

        /// <summary>How far into a carriageway a kerb may reach before it is standing in it.</summary>
        /// <remarks>
        /// Five centimetres, which is a hundred times the arithmetic and a sixth of the thinnest
        /// piece in <see cref="TestWorlds.KerbCatalog"/>. A kerb is seated with its inner face
        /// exactly on the edge of the carriageway, so anything measurably inside one is a kerb in
        /// the road rather than a float that rounded the wrong way.
        /// </remarks>
        const float Intrusion = 0.05f;

        /// <summary>
        /// What share of a map's kerbs may clip the inside of a bend of their own carriageway.
        /// </summary>
        /// <remarks>
        /// A run on the concave side of a corner is nearer the far arm of its own polyline than half
        /// a carriageway, so the last piece before a bend reaches into the road it is edging — see
        /// the remarks on <see cref="RoadKerbs"/> for why that is left alone. Over these seeds it is
        /// nine kerbs in a thousand; five per cent is far enough above that to be a guard against
        /// the mitre coming undone rather than a record of where the number happens to sit.
        /// </remarks>
        const float ClippedShareAllowed = 0.05f;

        static ArenaParams Params(ulong seed) => new ArenaParams
        {
            Seed = seed,
            RoadDensity = Density,
        };

        /// <summary>
        /// FNV-1a of the document of seeds 1..200 of the default map at a road density of one,
        /// recorded from the generator with the kerb stage taken out of the pipeline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The counterpart to <c>RoadPipelineTests.RoadlessDigests</c>, and it is here for the same
        /// reason: a workspace that has never filled <c>Props/Road/Kerb</c> is every workspace that
        /// existed before this stage did, and the override system rests on a regeneration putting
        /// every object back where it was. A stage that shifted one crate on a map with no kerb art
        /// in it would invalidate every edit anybody had made against that map.
        /// </para>
        /// <para>
        /// <strong>Recorded with the pipeline's one call to <c>RoadKerbs.Place</c> deleted</strong>,
        /// rather than derived from the code under test. What that baseline cannot cover on its own
        /// is the change to <see cref="WallRun"/> the kerbs needed, because that is upstream of
        /// both — so it is covered by the other baseline instead: the roadless digests in
        /// <see cref="RoadPipelineTests"/> predate this work entirely and hold every tiled run in
        /// the tool to the bytes it produced before <c>WallRun.Face.Along</c> existed.
        /// </para>
        /// <para>
        /// Of the document rather than of the text it serialises to, for the reason given there:
        /// Newtonsoft renders a quarter turn differently under .NET and under the runtime the editor
        /// runs on, and a text digest would fail on one of the two machines and say the generator
        /// had moved.
        /// </para>
        /// </remarks>
        static readonly string[] KerblessDigests =
        {
            "ba9f3d5ec57a2114", "7c2ae57c42f569f1", "66a143c13687fbd7", "2df2ba3ac6270ec9",
            "d0138451306bf7be", "0268404e5ae94cc0", "44d95fd38e5a5b80", "e94e5300aaa8b0ac",
            "2adc3d993e7126c7", "4dda65868937ab70", "daa8d863fdb5c4c0", "1487b4381bedb6bd",
            "6a3ddcf8cd53f3d3", "32a265247d972512", "ee03023db83ff8fb", "b15de77b9b94e661",
            "aedb147b74b76c1c", "ab4bcae421275772", "2c8321cffaf7e467", "b56f989f3c6aae5b",
            "2389b26e4a783fbc", "7a84a395f542d6fe", "74e89c619025bf8c", "b2426f1af588d9c0",
            "b7559d212c85ba4f", "7a61a8e5d987021f", "9f1faf05239edc6c", "4ca689e4a0702c8d",
            "f8c7a0ad34d3f8bb", "73ba5d9e0aa4b723", "350b80474b4a2fa2", "aec1b8fa08873535",
            "eab8225dff6991fb", "2c3848ea02ba2ef2", "db6e5e02ada43965", "9a003c8f4e7c8978",
            "ea004001480895b1", "5054214cd17f49c5", "c9bded82669b6911", "779e6e895559202f",
            "1f81497f44d699bc", "925970fec799ee9e", "7810fe7841fc5c53", "ca2d41845d90e34d",
            "7515cf28de70ebd8", "a74d494c92deeb26", "29fd3cec39bacc20", "b0f5333b558516c2",
            "fac89dfe8116fdd8", "167260ad7c50ace5", "a37bb3558fcf9a25", "e57d727cf416564a",
            "d375824de23b0b1e", "1eecb17317ebf96a", "25500b84c30b913c", "d983d398afa211ff",
            "cf321d8cb7ded63a", "306a78a0b6727ffd", "d98218c188147b94", "164a8d7e7b2199c1",
            "697d8688687d8c93", "55046b1e0aa8b3ed", "f6fad5931209de8f", "2f292568e547ccfb",
            "e40ffdd3c21be59b", "6da6fcfd32b6380e", "ddaaa13be06e80e5", "6289f1ab1a2dba09",
            "0fdb16948e93c20e", "bb816ddfb5979709", "31336ddd98d54df9", "db72f8f060d2f846",
            "bfc5ed92bd45f6cc", "90b51e3031add328", "858e9ec0e9584ea6", "8ad8d42321cf37ce",
            "a71c2dce8a453da6", "b5140bdb5d4b8eb1", "c500f2466a3c3d36", "0b392d51785c72ec",
            "61a1ef000ee3e598", "4f632f8ea50ec341", "696d109bd4e2d1d0", "924d5d54e6e96b01",
            "d5c90dfae10acd1d", "8c2337a88a442415", "5d47f642a2a7180a", "fa1ae0454db26a54",
            "ca395703f432dc16", "19a237429cd0b3e9", "c2b78337838ff6cf", "9090dd353132e4e5",
            "493ae577cfa0bc54", "4ba0f36a7409309f", "0fb691008c7b9418", "39285c02466a4d96",
            "3cb92e865d5fe6b7", "e6fbfd9cd17acc78", "fb7101541e806682", "690fa65410a9853b",
            "afe6fb981cc751c6", "cbadfbb7c93313a1", "0c2da338ca098560", "70cfe64e02686c2a",
            "a41d50dfb87ff4fd", "d4f752cad77d1601", "9661f6e5d33774e2", "df478c929c473a15",
            "d9c611c66381b705", "cb40d9acfc2e2dbc", "eafc3dfef80829cf", "e28a4c12cbea8aed",
            "d07d4fd630d76bd3", "03c2a529402f6141", "6b236166b9ee3fc7", "dd732bf654029392",
            "e81f43e97fe14059", "35dcbe5a6310420a", "3e8cfa828ea45309", "c164a1a15913e576",
            "8a18f52caf616114", "f2ac305a90008573", "7143a72edfc5cc25", "67b7d6824503e28b",
            "0a6f7c81798eeec3", "3b7533fcf25a67d9", "014610c26dc44dcd", "1d32e6865092bf89",
            "2bd0f1cc6731e92f", "a8098a9633805a86", "c8963de09938c511", "3168ee2606030ebd",
            "d239bf04b3c8ef8b", "0260cc6fa3b5409a", "5d8f6041425206b2", "461b3736079822c7",
            "44dd7e452828d252", "fe7d9f2396faab0e", "abd13511c523fdd4", "7f86bc1125e95e0f",
            "509be98c3969b597", "0a1e4fe9035e44f1", "68a27a2e7bdd19aa", "70f8247187573041",
            "7199912c03bd1aeb", "3b28b2a6904be3f1", "b07f0996c4e5087a", "e000cf34c172d0be",
            "796a1ec9be36fd95", "6ed30807dd103086", "e66bc16e7382797e", "4552918f6339005c",
            "f0e326cb93a1eade", "8c9c0fd8715680fd", "84c555af4f79c685", "a50e3652ea0b46df",
            "dda3e33dbaa4a31c", "4ef737bc3b4dbed1", "fe156a639686890c", "d95bc6872350284b",
            "a42830d8f3924960", "e5df13fe9dab1174", "a2bf5d8a550f96f6", "1e32dd026bb6609f",
            "bd292d62ce8441cd", "37c8f2cf0b6c9fc2", "6a0ccf6ed0f985c6", "9dad520a41ee8bd8",
            "46b4d1f52a1fd8de", "98a8925b94786f7e", "d7915cbbecd16901", "79f4e9a1e3585a1c",
            "9bfd7c22b6155ed4", "84c75138e9c4db19", "fc1fcd30e2f2ed11", "227c0489adcd64e7",
            "252ab61ddbb292b6", "1faec2c0d1b5349f", "b4b5ec7bcccf21e5", "5dc256229d92b9ce",
            "4402bcc824d1eab7", "24e94efb1b6ceed1", "2285a6fffc0b1a27", "b265aefcfa98154d",
            "d5792c918907857f", "93adb9d96fb6a156", "14de088347446b36", "fae44c5b0dd6537a",
            "76ebd49ebba9bec9", "2e3ceec11cf4bf95", "15aa8b65dd3c0eb9", "5c296bebefeb27fb",
            "4a2e47ff55ef13cc", "95a7ee455949c944", "c6d1cef050d15b6a", "f5e4198abb9326f0",
            "f50fe5392f822050", "990a35ee7ba68ff5", "80f41a9a1ee1c648", "3c36a05f58c899e7",
        };

        // --- a kerb edges a road rather than standing in one ---------------------------------------

        /// <remarks>
        /// The property the whole stage exists for. Every kerb is measured against every carriageway
        /// on the map — its own and the ones it braids with — and none may be measurably inside one,
        /// except for the bend clipping the remarks above account for.
        /// </remarks>
        [Test]
        public void NoKerbStandsInACarriagewayItDoesNotEdge()
        {
            var offenders = new List<string>();
            var clipped = 0;
            var kerbs = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.KerbCatalog());
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!IsKerb(placed))
                    {
                        continue;
                    }

                    kerbs++;
                    Vec2 at = placed.Pose.Position.Xz;
                    string owner = SegmentOf(placed);

                    for (int s = 0; s < roads.Segments.Count; s++)
                    {
                        RoadSegment segment = roads.Segments[s];
                        if (Distance(segment, at) >= segment.Width * 0.5f - Intrusion)
                        {
                            continue;
                        }

                        if (string.Equals(segment.Id, owner, StringComparison.Ordinal))
                        {
                            clipped++;
                        }
                        else
                        {
                            offenders.Add($"seed {seed}: {placed.StableId} stands in {segment.Id}");
                        }

                        break;
                    }
                }
            }

            Assert.That(kerbs, Is.GreaterThan(0), "no kerb was laid on any seed of the sweep");
            Assert.That(offenders, Is.Empty,
                "a kerb is standing in a carriageway laid by a different road");
            Assert.That(clipped / (float)kerbs, Is.LessThan(ClippedShareAllowed),
                $"{clipped} of {kerbs} kerbs clip the inside of a bend of their own road");
        }

        /// <remarks>
        /// The mechanism behind it: a run stops where it enters a junction disc, so the ground a
        /// crossing covers carries no edging at all. Asserted apart from the property above because
        /// a kerb standing in a crossing would be inside two carriageways at once, and the message
        /// there would name whichever of them was tested first.
        /// </remarks>
        [Test]
        public void NoKerbStandsInAJunction()
        {
            var offenders = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.KerbCatalog());
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!IsKerb(placed))
                    {
                        continue;
                    }

                    Vec2 at = placed.Pose.Position.Xz;
                    for (int j = 0; j < roads.Junctions.Count; j++)
                    {
                        RoadJunction junction = roads.Junctions[j];
                        if (Vec2.Distance(at, junction.Position) < roads.JunctionRadius - Intrusion)
                        {
                            offenders.Add(
                                $"seed {seed}: {placed.StableId} stands in {junction.Id}");
                            break;
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty, "a kerb is standing in a junction");
        }

        /// <remarks>
        /// A kerb reads as a kerb because each piece starts exactly where the last one stopped,
        /// which is what a run buys over a scatter and the whole reason this stage walks
        /// <see cref="WallRun"/> rather than sampling positions. Nine tenths of them have another
        /// piece of the same carriageway flush against them; the tenth that does not is a run a
        /// junction, a braid or a building brought to an end.
        /// </remarks>
        [Test]
        public void KerbsAreLaidEndToEndAlongTheirCarriageway()
        {
            Catalog catalog = TestWorlds.KerbCatalog();
            var flush = 0;
            var kerbs = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                List<PlacedObject> laid = Kerbs(ArenaLayoutGenerator.Generate(Params(seed), catalog));

                for (int i = 0; i < laid.Count; i++)
                {
                    kerbs++;
                    Vec2 at = laid[i].Pose.Position.Xz;
                    float span = PieceLength(laid[i], catalog);

                    for (int j = 0; j < laid.Count; j++)
                    {
                        if (j == i ||
                            !string.Equals(
                                SegmentOf(laid[i]), SegmentOf(laid[j]), StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Two pieces laid end to end sit half of each apart, so the gap between
                        // their pivots is the mean of their two lengths. Nearer than that is a
                        // closing piece doubled up on the run's own tail, which is the one overlap
                        // a run is allowed and is still two pieces touching. Further is a gap.
                        float apart = Vec2.Distance(at, laid[j].Pose.Position.Xz);
                        float flushAt = (span + PieceLength(laid[j], catalog)) * 0.5f;

                        if (apart <= flushAt + Intrusion)
                        {
                            flush++;
                            break;
                        }
                    }
                }
            }

            Assert.That(kerbs, Is.GreaterThan(0), "no kerb was laid on any seed of the sweep");
            Assert.That(flush / (float)kerbs, Is.GreaterThan(0.9f),
                $"only {flush} of {kerbs} kerbs have another piece of their own run flush against " +
                "them, so the runs are not being tiled");
        }

        // --- an id survives a regeneration ---------------------------------------------------------

        /// <remarks>
        /// What every override in the tool rests on. A kerb is named for the carriageway it edges
        /// and its own place along it, and both halves have to come back the same on the next
        /// generation or an edit made against one lands on a different piece of stone.
        /// </remarks>
        [Test]
        public void KerbIdsAreTheSameAcrossTwoGenerationsOfOneSeed()
        {
            Catalog catalog = TestWorlds.KerbCatalog();
            var moved = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                List<PlacedObject> first = Kerbs(ArenaLayoutGenerator.Generate(Params(seed), catalog));
                List<PlacedObject> again = Kerbs(ArenaLayoutGenerator.Generate(Params(seed), catalog));

                if (first.Count != again.Count)
                {
                    moved.Add($"seed {seed}: {first.Count} kerbs became {again.Count}");
                    continue;
                }

                for (int i = 0; i < first.Count; i++)
                {
                    if (!string.Equals(
                            first[i].StableId, again[i].StableId, StringComparison.Ordinal) ||
                        first[i].LogicalId != again[i].LogicalId ||
                        first[i].Pose.Position != again[i].Pose.Position ||
                        first[i].Pose.Rotation != again[i].Pose.Rotation)
                    {
                        moved.Add(
                            $"seed {seed}: {first[i].StableId} at {first[i].Pose.Position} came " +
                            $"back as {again[i].StableId} at {again[i].Pose.Position}");
                        break;
                    }
                }
            }

            Assert.That(moved, Is.Empty, "a kerb did not come back where it was");
        }

        /// <remarks>
        /// The shape of the id, asserted on its own so that changing it has to be a decision. It
        /// names the carriageway because a kerb belongs to a road rather than to a lane, and the
        /// index is padded to three digits because an artery across a large map carries more than a
        /// hundred pieces a side and <c>kerb_98</c> sorting after <c>kerb_100</c> is not an order
        /// anybody reading it expects.
        /// </remarks>
        [Test]
        public void AKerbIsNamedForTheCarriagewayItEdges()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(Params(7UL), TestWorlds.KerbCatalog());
            ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

            List<PlacedObject> laid = Kerbs(doc);
            Assert.That(laid, Is.Not.Empty, "no kerb was laid on this seed");

            var ids = new List<string>();
            for (int i = 0; i < roads.Segments.Count; i++)
            {
                ids.Add(roads.Segments[i].Id);
            }

            for (int i = 0; i < laid.Count; i++)
            {
                string id = laid[i].StableId;
                Assert.That(id, Does.StartWith(RoadKerbs.IdPrefix), id);
                Assert.That(ids, Does.Contain(SegmentOf(laid[i])),
                    $"{id} names a carriageway this network does not have");

                string index = id.Substring(
                    id.IndexOf(RoadKerbs.KerbSegment, StringComparison.Ordinal) +
                    RoadKerbs.KerbSegment.Length);

                Assert.That(index.Length, Is.GreaterThanOrEqualTo(3), id);
                Assert.That(
                    int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                    Is.True, $"{id} does not end in a number");
            }
        }

        // --- a workspace with no kerb art is a workspace this stage never touched -------------------

        /// <remarks>
        /// Two hundred seeds of the default map with roads on it, each reduced to one number over
        /// every id, pose, tag and metadata entry the document holds — see
        /// <see cref="KerblessDigests"/>.
        /// </remarks>
        [Test]
        public void ACatalogWithNoKerbArtGeneratesTheMapItAlwaysDid()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var moved = new List<string>();

            for (int seed = 1; seed <= KerblessDigests.Length; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params((ulong)seed), catalog);
                string digest = Digest(doc).ToString("x16", CultureInfo.InvariantCulture);

                if (!string.Equals(digest, KerblessDigests[seed - 1], StringComparison.Ordinal))
                {
                    moved.Add($"seed {seed}: {KerblessDigests[seed - 1]} became {digest}");
                }
            }

            Assert.That(moved, Is.Empty,
                "a map with roads and no kerb art is no longer the map it was before the kerb " +
                "stage was in the pipeline");
        }

        /// <remarks>
        /// The mechanism the property above rests on: with nothing in the folder the stage returns
        /// before it forks a stream or evaluates a rule, so it writes no statistics at all. A stage
        /// that had run and placed nothing would leave a tally of zero, which reads as a stage that
        /// found no room rather than one that was never asked.
        /// </remarks>
        [Test]
        public void ACatalogWithNoKerbArtTakesNoDraw()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(Params(7UL), TestWorlds.SampleCatalog());

            foreach (KeyValuePair<string, string> entry in doc.Metadata)
            {
                Assert.That(entry.Key, Does.Not.StartWith(RoadKerbs.StatsPrefix),
                    "the kerb stage reported statistics for a catalog it has no art in");
            }

            Assert.That(Kerbs(doc), Is.Empty);
        }

        /// <remarks>
        /// The other half of the same bargain: a map with the art but no roads. A network with no
        /// carriageways has nothing to edge, and the stage leaves before the catalog is queried.
        /// </remarks>
        [Test]
        public void AMapWithNoRoadsGetsNoKerbs()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 7UL }, TestWorlds.KerbCatalog());

            Assert.That(Kerbs(doc), Is.Empty);
        }

        // --- the network is still a function of the document ---------------------------------------

        /// <remarks>
        /// <para>
        /// A network is rebuilt from a finished document every time anything asks where the roads
        /// are, and the router prices sheltered ground below open ground — so an object the router
        /// can see moves the roads. Kerbs are placed <em>from</em> a network, so counting them would
        /// route the next rebuild round the edging the last one laid, and the corridors the cover was
        /// placed against would move under it.
        /// </para>
        /// <para>
        /// Measured by rebuilding the network from a kerbed document and comparing it with the one a
        /// map generated without the art comes out with, which is the same question the realiser
        /// asks every time it puts the ground down.
        /// </para>
        /// </remarks>
        [Test]
        public void TheNetworkIsUnchangedByTheKerbsLaidAlongIt()
        {
            Catalog catalog = TestWorlds.KerbCatalog();
            Catalog plain = TestWorlds.SampleCatalog();
            var moved = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                ArenaLayoutGenerator.Terrain(
                    ArenaLayoutGenerator.Generate(Params(seed), catalog), out RoadNetwork kerbed);

                ArenaLayoutGenerator.Terrain(
                    ArenaLayoutGenerator.Generate(Params(seed), plain), out RoadNetwork bare);

                if (kerbed.Segments.Count != bare.Segments.Count)
                {
                    moved.Add(
                        $"seed {seed}: {bare.Segments.Count} segments became " +
                        $"{kerbed.Segments.Count}");
                    continue;
                }

                for (int i = 0; i < kerbed.Segments.Count; i++)
                {
                    if (!Same(kerbed.Segments[i], bare.Segments[i]))
                    {
                        moved.Add($"seed {seed}: {bare.Segments[i].Id} moved");
                        break;
                    }
                }
            }

            Assert.That(moved, Is.Empty,
                "the roads move when kerbs are laid along them, so a rebuilt network is not the " +
                "network its own cover was placed around");
        }

        static bool Same(RoadSegment a, RoadSegment b)
        {
            if (a.Id != b.Id || a.Class != b.Class || a.Width != b.Width ||
                a.Points.Count != b.Points.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Points.Count; i++)
            {
                if (a.Points[i] != b.Points[i])
                {
                    return false;
                }
            }

            return true;
        }

        static bool IsKerb(PlacedObject placed)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (string.Equals(placed.Tags[i], RoadKerbs.KerbTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static List<PlacedObject> Kerbs(WorldDoc doc)
        {
            var kerbs = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (IsKerb(doc.GeneratedObjects[i]))
                {
                    kerbs.Add(doc.GeneratedObjects[i]);
                }
            }

            return kerbs;
        }

        /// <summary>The carriageway id out of a kerb's own stable id.</summary>
        static string SegmentOf(PlacedObject placed)
        {
            int at = placed.StableId.IndexOf(RoadKerbs.KerbSegment, StringComparison.Ordinal);
            return placed.StableId.Substring(
                RoadKerbs.IdPrefix.Length, at - RoadKerbs.IdPrefix.Length);
        }

        /// <summary>How far a piece of kerbing reaches along its own run.</summary>
        static float PieceLength(PlacedObject placed, Catalog catalog)
        {
            CatalogEntry entry = catalog.Find(placed.LogicalId);
            return MathF.Max(entry.Footprint.Width, entry.Footprint.Depth);
        }

        /// <summary>How far a point is from a carriageway's centreline.</summary>
        static float Distance(RoadSegment segment, Vec2 at)
        {
            float nearest = float.MaxValue;

            for (int i = 1; i < segment.Points.Count; i++)
            {
                Vec2 from = segment.Points[i - 1];
                Vec2 span = segment.Points[i] - from;
                float lengthSquared = span.SqrLength;
                float t = lengthSquared > 0f ? Vec2.Dot(at - from, span) / lengthSquared : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;

                nearest = MathF.Min(nearest, Vec2.Distance(at, from + span * t));
            }

            return nearest;
        }

        /// <summary>
        /// Reduces a document to one number, exactly as <c>RoadPipelineTests.Digest</c> does.
        /// </summary>
        /// <remarks>
        /// Spelled out again rather than shared, because the two are baselines against two different
        /// builds: a change to one of them has to be a change to one of them, and a helper both
        /// depended on would move both sets of recorded numbers at once and so prove nothing about
        /// either.
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
