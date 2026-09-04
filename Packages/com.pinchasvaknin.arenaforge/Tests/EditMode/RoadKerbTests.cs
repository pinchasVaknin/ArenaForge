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
    /// <remarks>
    /// Categorised <c>Slow</c>: every case lays a kerbed network over a generated map.
    /// <c>-testCategory "!Slow"</c> leaves them out of the short run used while iterating — see
    /// CONTRIBUTING.md.
    /// </remarks>
    [Category("Slow")]
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
            "040a09c572a2659a", "9d1678a581bed158", "a50a0c0059cc601b", "9bc392c663537cd6",
            "0bdfa468fc28954d", "3e4c5bf33acce1bc", "1ad6074ac9cccdc5", "8e1ac0908d414d40",
            "2980bc80cdf70e17", "e679cb752e87610c", "da948efa3d0eab1e", "1f275631e8be9972",
            "a7d4dd8c30e2ebf9", "90f3b05898860b63", "e75c053ecb42ac4e", "772ab3a1ec8eefe3",
            "56cb8ea88ae7da0b", "fac171d4e58eae5c", "8c2eccfbec51a1e1", "784c5cccbf4ba233",
            "22cc3c4fd06dbd68", "e8c3306da8739d96", "6e2def7fe5f65e71", "0f71c982929a8a69",
            "70a1c4d25e61a259", "b346d52e3690c6c3", "58feb514a6016b56", "3e523172d36dcd88",
            "23419ab928de9de8", "7b96e31052e223df", "def225a94c3056ed", "d6709ab3cee849da",
            "59d65e89a5866f3d", "a88ff57158609c59", "f9750357a3e447af", "e0b34edfc4dc75fd",
            "5f69ffded3021305", "5e461bcadc080018", "7f7445c3610a4f7a", "38897cfd5ef0f21e",
            "e8b3b55548bb1586", "11d83e7a4800c19e", "2cdf8f4ebade867f", "495069a0ded46d59",
            "ef811abb84f2ded6", "9270e3b8d0b855bf", "208b8dbb90db782e", "2329551db88130de",
            "3a5a756a7534e02b", "a076d9908f5fcbf6", "4d02be116ca69e23", "f3947d8e24895c9d",
            "fcf7231c7133afe0", "e68111774dd37f9c", "92397211e489dac8", "bde9331a39357161",
            "757d969abee294d4", "af70877a5880f764", "bfdc49dbd9d53239", "b60f0f49fcaf55ad",
            "eaf3eda0bb6db0fd", "4e589ead614d904f", "0d2e1ced5f4731e8", "c6e77408265430d3",
            "f519ca1e70f2ceba", "4bd806a9e5b1e3e1", "13c3c8194599d931", "f18dbea2b515b881",
            "c626971abcf3f0d1", "70ba321e786e2e2a", "1f3c3bea2b9a4170", "8a0c4e97dbd2999d",
            "ef37c12ef38766a8", "5fea4b61e7996aa6", "54d706b6cf26dc0f", "d751ba131a1497b7",
            "f0fd36312579cba5", "f2aef1b1aedb6c8c", "2986500a9eebc2c0", "d96b166f69c2b93f",
            "0686de701ac3dc4d", "75c5aa4e5e4a46fc", "2fd987dfc109a945", "1938f55c08cff5fa",
            "df3e960febac09f9", "913a16b171cbbef4", "8c719cb07a6dba7e", "8facdd216232335c",
            "ef58bbc03a3de792", "5c053e04ce58dd0e", "312b5a93de4bdb60", "7af3ca5d3ffef8ad",
            "f896efadede7dff2", "acaee2e2e555c645", "8f5c265e0d0464bd", "949676452b0789cb",
            "540cfd8f84b21b6c", "57b77892acff5c19", "d806cf4a23cfb711", "912f6b4bdadcf0e9",
            "0671b738dab1099d", "4d9df6f6aea749ec", "3b2b73d027c05244", "d2d3f0a561eb38f4",
            "2f5bb93585d6e649", "0f0f309bc6c94422", "bc7d23e9718ef681", "93f2b5d18debf55f",
            "b1efcd2254ebfee9", "c38cea2da11667e7", "eec9e23a0033b7f1", "9ae22520d76c6f12",
            "044b996a5a7b3eae", "a738eb85b8575fff", "d9eccca2b01005b0", "163ee1580b1642dd",
            "9517b53c01bbda84", "b5f656083ff2dd43", "be7e0aa85c55c5e8", "3856e3bab496e84e",
            "189f4a1c29786df8", "048d230833098462", "bc60f6d6e7ddae15", "455348bbe778e425",
            "1f735930c2d67e4a", "150bce1719b0ffe7", "b866060798b33ad8", "6090f163fad16a1e",
            "f42445250932a76b", "905234b0b8cf28bd", "68dfc5de92ddbd28", "f2d22165326acde9",
            "55aa96ca4ff2ed67", "2c5a204cce1de04a", "b4092d792573e63a", "8651e009100d9c02",
            "fa30fcc56efac805", "c00ee4f0ee15d5aa", "6cba5b18ebfcc9d0", "d557e6bda3accdb4",
            "5119e272ca755bb2", "7bcf11f412ed5fca", "f7b00384336c7d28", "93d5a00e11e497ae",
            "6d62f13f7f43f635", "faced87c26866b64", "98b04f3dfbb13495", "a89a436df790dc58",
            "3a81c59ef6b44a8b", "219662759cc1608a", "4eb34b1b44a8554b", "01a4a276df6268ed",
            "b6d616bc3fc220c2", "fd9c5ced58ff34a2", "3e5e12b472505836", "aa8ebb198364cae1",
            "f116dcf385981abc", "2ba8f1b10249b40e", "0f74e7d0403640ea", "26814d64f788ab8e",
            "143b4d1bc5b44b72", "1c7f7c239f04ad56", "703d0761bd3c71e2", "eb959898a6382314",
            "7fd28f2d3b480249", "9633a7181af4d4ce", "4785292990d9fdef", "6d4135a2da7dfc23",
            "5bd322d83463864f", "3e14e9a2fac61c05", "e868193e2463590f", "9739d4501a2fd0ea",
            "679105c1f816d5f0", "1021faaa4d64837e", "a43c6379f2a7871e", "a5ebf02310cab73e",
            "660b9ddc578aae92", "4055964d1103178a", "cbb67b38297aea05", "4a0569624f275bb4",
            "bc394f11d7a139a5", "4bd5b468badbf219", "454c0abed0c1ce6d", "3b44b5d1f8c59914",
            "4d48eb8541f72ef9", "4f5acdc129d3d41e", "2974b801978ae615", "4624ddd835caaee1",
            "39c02d4e1a809448", "641eba9a61a46719", "36748879c4e95ca6", "958bf038753c6343",
            "0aef38a71662c744", "0920c8acf79ce4b8", "c19d33caa15435ad", "ef73e428936932cf",
            "6227ed6dc6e17756", "1a2af42b32b5e30d", "09d635f85d89a7ab", "5697a8f241e4d87e",
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
