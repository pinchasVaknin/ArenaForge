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

        // --- and a fence is not something a road is laid through ------------------------------------

        /// <summary>How many seeds the fence properties are measured over.</summary>
        /// <remarks>
        /// Sixty, because the failure was not rare and did not need finding: measured against the
        /// generator as it stood, fifty-nine of these sixty had a carriageway centreline running
        /// through the middle of a fence panel. What the count has to be large enough for is the
        /// opposite claim — that none of them does — and sixty maps carry some two thousand panels
        /// between them.
        /// </remarks>
        const int FenceSeeds = 60;

        /// <summary>
        /// Every fence panel on a map, as the world rectangle it stands on.
        /// </summary>
        /// <remarks>
        /// Read out of the catalog by tag rather than out of the metadata the fencing stages write,
        /// deliberately: the metadata is the mechanism under test, and a test that measured through
        /// it would pass just as happily if every stage had stopped writing it and the router had
        /// stopped shutting anything.
        /// </remarks>
        static List<Rect2> Fences(WorldDoc doc, Catalog catalog)
        {
            var fences = new List<Rect2>();

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!HasFenceTag(placed))
                {
                    continue;
                }

                CatalogEntry entry = catalog.Find(placed.LogicalId);
                if (entry != null)
                {
                    fences.Add(placed.Pose.Bounds(entry.Footprint));
                }
            }

            return fences;
        }

        static bool HasFenceTag(PlacedObject placed)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (placed.Tags[i] == PerimeterFence.StoneFenceTag ||
                    placed.Tags[i] == ExteriorPlacer.FenceTag)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where a network's centrelines go, sampled a quarter of a metre apart.
        /// </summary>
        /// <remarks>
        /// The centreline rather than the reservation, because the reservation is a box round a
        /// stretch of road and is deliberately larger than the road — see
        /// <see cref="RoadNetwork.Corridors"/> — so a panel merely near a bend would count as one
        /// the road ran through. A quarter of a metre is finer than the thinnest fence art in the
        /// suite, so nothing can pass between two samples.
        /// </remarks>
        static IEnumerable<Vec2> Centrelines(RoadNetwork network)
        {
            for (int s = 0; s < network.Segments.Count; s++)
            {
                IReadOnlyList<Vec2> points = network.Segments[s].Points;

                for (int i = 1; i < points.Count; i++)
                {
                    Vec2 from = points[i - 1];
                    Vec2 to = points[i];
                    var steps = (int)MathF.Max(1f, Vec2.Distance(from, to) / 0.25f);

                    for (int step = 0; step <= steps; step++)
                    {
                        float along = step / (float)steps;
                        yield return new Vec2(
                            from.X + ((to.X - from.X) * along),
                            from.Y + ((to.Y - from.Y) * along));
                    }
                }
            }
        }

        /// <remarks>
        /// <para>
        /// The property the whole fence-as-obstacle change exists for. A road was routed over ground
        /// and pads alone, so a fence — the one piece of dressing a player cannot walk through — was
        /// invisible to it, and a path was drawn straight over the wall round a yard or the ring
        /// round a spawn on nearly every seed.
        /// </para>
        /// <para>
        /// Asserted on the centreline being <em>inside</em> a panel rather than on any clearance
        /// from one, because that is the failure as somebody sees it in the scene and it needs no
        /// number chosen to say so. A road that runs along a fence is a street; a road that runs
        /// through one is a hole.
        /// </para>
        /// </remarks>
        [Test]
        public void NoRoadIsLaidThroughAFence()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var through = new List<string>();
            var panels = 0;

            for (ulong seed = 1; seed <= FenceSeeds; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);

                List<Rect2> fences = Fences(doc, catalog);
                panels += fences.Count;

                RoadNetwork network = RoadNetwork.Build(
                    parameters,
                    ArenaLayout.Build(parameters),
                    ArenaLayoutGenerator.TerrainBeforeRoads(doc),
                    doc.GeneratedObjects);

                foreach (Vec2 at in Centrelines(network))
                {
                    for (int f = 0; f < fences.Count; f++)
                    {
                        if (fences[f].Contains(at))
                        {
                            through.Add($"seed {seed}: a carriageway runs through {fences[f]}");
                            f = fences.Count;
                        }
                    }
                }
            }

            Assert.That(panels, Is.GreaterThan(FenceSeeds * 10),
                "the fixture fenced almost nothing, so the property is not being tested");

            Assert.That(through, Is.Empty, "a road was laid through a fence");
        }

        /// <remarks>
        /// The other half of the bargain, and the half a blunter fix would fail. Shutting the ground
        /// under a fence can only ever take routes away, so a change that shut too much would answer
        /// the property above by laying almost nothing at all — a map with no roads on it crosses no
        /// fences. Measured against the same seeds generated without the fencing art in the catalog,
        /// which is as close to the same map without fences as there is.
        /// </remarks>
        [Test]
        public void FencingAMapStillLeavesItARoadNetwork()
        {
            Catalog fenced = TestWorlds.ExteriorCatalog();
            Catalog bare = TestWorlds.SampleCatalog();
            var thin = new List<string>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };

                float withFences = Laid(parameters, fenced);
                float without = Laid(parameters, bare);

                Assert.That(withFences, Is.GreaterThan(0f), $"seed {seed}: no road was laid at all");

                // Two thirds, which is a long way below what the sweep actually comes out at and
                // far enough above nothing to fail a change that routed round a fence by giving up
                // on the route.
                if (withFences < without * (2f / 3f))
                {
                    thin.Add(
                        $"seed {seed}: {withFences:0.0} m of road with fences against " +
                        $"{without:0.0} m without them");
                }
            }

            Assert.That(thin, Is.Empty, "fencing the map cost it most of its road network");
        }

        static float Laid(ArenaParams parameters, Catalog catalog)
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);

            return RoadNetwork.Build(
                parameters,
                ArenaLayout.Build(parameters),
                ArenaLayoutGenerator.TerrainBeforeRoads(doc),
                doc.GeneratedObjects).CorridorLength;
        }

        // --- nor through a fence somebody put there themselves ------------------------------------

        /// <summary>The network a document describes, over the ground its own pads left.</summary>
        static RoadNetwork Network(ArenaParams parameters, WorldDoc doc) =>
            RoadNetwork.Build(
                parameters,
                ArenaLayout.Build(parameters),
                ArenaLayoutGenerator.TerrainBeforeRoads(doc),
                doc.GeneratedObjects,
                ArenaLayoutGenerator.RecordedBarriers(doc));

        /// <summary>The middle of the longest carriageway on a map, which is where a fence goes.</summary>
        /// <remarks>
        /// The longest rather than the first, so the panel lands on a road the map actually depends
        /// on rather than on a stub a branch happened to leave. Deterministic on ties by list order,
        /// as everything else here is.
        /// </remarks>
        static Vec2 MiddleOfTheLongestRoad(RoadNetwork network)
        {
            var best = -1f;
            Vec2 middle = Vec2.Zero;

            for (int s = 0; s < network.Segments.Count; s++)
            {
                IReadOnlyList<Vec2> points = network.Segments[s].Points;
                var length = 0f;
                for (int i = 1; i < points.Count; i++)
                {
                    length += Vec2.Distance(points[i - 1], points[i]);
                }

                if (length > best)
                {
                    best = length;
                    middle = points[points.Count / 2];
                }
            }

            return middle;
        }

        /// <summary>A fence panel dropped in by hand, as the editor records one.</summary>
        static EditOverride HandPlaced(string id, CatalogEntry entry, Vec2 at) =>
            EditOverride.Add(
                PlacedObject.UserIdPrefix + id,
                entry.LogicalId,
                Pose.At(new Vec3(at.X, 0f, at.Y)),
                new[] { "fence", ExteriorPlacer.FenceTag });

        /// <summary>The ground a placement of this art at this point would cover.</summary>
        static Rect2 FootprintAt(CatalogEntry entry, Vec2 at) =>
            Pose.At(new Vec3(at.X, 0f, at.Y)).Bounds(entry.Footprint);

        /// <summary>True if any of a network's centrelines runs inside a rectangle.</summary>
        static bool RunsThrough(RoadNetwork network, Rect2 area)
        {
            foreach (Vec2 at in Centrelines(network))
            {
                if (area.Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <remarks>
        /// <para>
        /// What the barrier work had to reach: a fence somebody stands by hand is a fence, and the
        /// next network has to go round it. The generated ones say where they are in their own
        /// metadata; a hand-placed one cannot, because it is an override rather than something a
        /// stage produced — so the edits are handed to the generator and read before the roads are
        /// laid.
        /// </para>
        /// <para>
        /// The panel is dropped in the middle of the longest carriageway, so what is measured is a
        /// road that has to move rather than a fence that was never in the way. The count of seeds
        /// where it <em>was</em> in the way is asserted for that reason: without it, a change that
        /// stopped laying roads at all would pass.
        /// </para>
        /// </remarks>
        [Test]
        public void AHandPlacedFenceTurnsTheNextGenerationsRoads()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            CatalogEntry panel = catalog.Find(TestWorlds.FencePanelId);
            var wasInTheWay = 0;
            var through = new List<string>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };
                WorldDoc first = ArenaLayoutGenerator.Generate(parameters, catalog);

                RoadNetwork before = Network(parameters, first);
                if (before.IsEmpty)
                {
                    continue;
                }

                Vec2 at = MiddleOfTheLongestRoad(before);
                Rect2 ground = FootprintAt(panel, at);

                if (RunsThrough(before, ground))
                {
                    wasInTheWay++;
                }

                WorldDoc second = ArenaLayoutGenerator.Generate(
                    parameters, catalog, new[] { HandPlaced("fence_00", panel, at) });

                Assert.That(
                    ArenaLayoutGenerator.RecordedBarriers(second).Count, Is.EqualTo(1),
                    $"seed {seed}: the hand-placed fence was not recorded");

                RoadNetwork after = Network(parameters, second);
                Assert.That(after.IsEmpty, Is.False, $"seed {seed}: one fence emptied the network");

                if (RunsThrough(after, ground))
                {
                    through.Add($"seed {seed}: a carriageway still runs through {ground}");
                }
            }

            Assert.That(wasInTheWay, Is.GreaterThan(15),
                "the fence was hardly ever in a road's way, so the property is not being tested");

            Assert.That(through, Is.Empty, "a road was laid through a hand-placed fence");
        }

        /// <remarks>
        /// <para>
        /// The other half, and the one that was wrong in a way nothing showed. A generated fence
        /// records the ground it stands on when the stage stands it, and a person who then drags
        /// that panel across the map leaves the record behind: the road was held off ground the
        /// fence had left and laid straight through the ground it had gone to. The edits are read
        /// before the roads now, so the record is restated where they moved it.
        /// </para>
        /// <para>
        /// The panel is moved onto the longest carriageway for the same reason the hand-placed one
        /// is dropped there, and the restated record is asserted to be the ground the fence went
        /// <em>to</em> rather than merely different from where it was: a fix that shut both
        /// rectangles would pass a test that only asked whether the record had changed, and would
        /// quietly cost the map the ground the fence had left.
        /// </para>
        /// </remarks>
        [Test]
        public void AGeneratedFenceSomebodyMovedTurnsTheRoadsFromWhereItNowStands()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            CatalogEntry panel = catalog.Find(TestWorlds.FencePanelId);
            var moved = 0;
            var through = new List<string>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };
                WorldDoc first = ArenaLayoutGenerator.Generate(parameters, catalog);

                RoadNetwork before = Network(parameters, first);
                PlacedObject fence = FirstFence(first);
                if (before.IsEmpty || fence == null)
                {
                    continue;
                }

                Rect2 was = Barrier(fence);
                Vec2 to = MiddleOfTheLongestRoad(before);
                Rect2 now = FootprintAt(panel, to);

                WorldDoc second = ArenaLayoutGenerator.Generate(
                    parameters,
                    catalog,
                    new[]
                    {
                        EditOverride.Move(fence.StableId, Pose.At(new Vec3(to.X, 0f, to.Y))),
                    });

                Rect2 restated = Barrier(FindGenerated(second, fence.StableId));
                Assert.That(restated, Is.Not.EqualTo(was), $"seed {seed}: the record did not move");
                Assert.That(restated, Is.EqualTo(now),
                    $"seed {seed}: the record moved somewhere the fence did not");

                moved++;

                RoadNetwork after = Network(parameters, second);

                if (RunsThrough(after, now))
                {
                    through.Add($"seed {seed}: a carriageway runs through {now}, where the fence is");
                }
            }

            Assert.That(moved, Is.GreaterThan(15), "no fence was moved, so nothing was tested");
            Assert.That(through, Is.Empty, "a road was laid through a fence somebody had moved");
        }

        /// <remarks>
        /// A fence somebody deletes is not something to route round. The same reading order buys
        /// this one for nothing: a Delete takes the object out of the resolved world, so its record
        /// goes with it.
        /// </remarks>
        [Test]
        public void AGeneratedFenceSomebodyDeletedStopsTurningTheRoads()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var parameters = new ArenaParams { Seed = 3UL, RoadDensity = 1f };

            WorldDoc first = ArenaLayoutGenerator.Generate(parameters, catalog);
            PlacedObject fence = FirstFence(first);
            Assert.That(fence, Is.Not.Null, "the fixture generated no fence at all");

            WorldDoc second = ArenaLayoutGenerator.Generate(
                parameters, catalog, new[] { EditOverride.Delete(fence.StableId) });

            PlacedObject after = FindGenerated(second, fence.StableId);
            Assert.That(after, Is.Not.Null, "the object itself is still generated; only the edit removes it");
            Assert.That(
                after.Metadata.ContainsKey(ArenaLayoutGenerator.BarrierKey), Is.False,
                "a fence that will be deleted is not ground a road has to go round");
        }

        /// <remarks>
        /// <para>
        /// The second half of what a hard obstacle means. A road that goes round a hand-placed wall
        /// and a crate that stands inside it are the same fault twice, and only the first was fixed:
        /// the cover stage scatters against the generated world, which a user object is not part of.
        /// </para>
        /// <para>
        /// The barrier is put where the cover stage will want to stand something — the middle of a
        /// lane band — and the unedited map is measured first, so the seeds counted are the ones
        /// where cover really would have stood there.
        /// </para>
        /// </remarks>
        [Test]
        public void CoverKeepsOffAHandPlacedBarrier()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            CatalogEntry panel = catalog.Find(TestWorlds.FencePanelId);
            var wouldHaveStoodThere = 0;
            var clipping = new List<string>();

            for (ulong seed = 1; seed <= 30; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };
                WorldDoc plain = ArenaLayoutGenerator.Generate(parameters, catalog);

                // Wherever this seed's first crate went, which is by construction a place the cover
                // stage was willing to put one.
                PlacedObject stood = FirstCover(plain);
                if (stood == null)
                {
                    continue;
                }

                Vec2 at = stood.Pose.Position.Xz;
                Rect2 ground = FootprintAt(panel, at);
                wouldHaveStoodThere++;

                WorldDoc fenced = ArenaLayoutGenerator.Generate(
                    parameters, catalog, new[] { HandPlaced("fence_00", panel, at) });

                for (int i = 0; i < fenced.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = fenced.GeneratedObjects[i];
                    if (!placed.StableId.Contains("/cover_"))
                    {
                        continue;
                    }

                    CatalogEntry entry = catalog.Find(placed.LogicalId);
                    if (entry != null && placed.Pose.Bounds(entry.Footprint).Overlaps(ground))
                    {
                        clipping.Add($"seed {seed}: {placed.StableId} stands in {ground}");
                        break;
                    }
                }
            }

            Assert.That(wouldHaveStoodThere, Is.GreaterThan(25),
                "the fixture scattered almost no cover, so the property is not being tested");

            Assert.That(clipping, Is.Empty, "cover was scattered into a hand-placed barrier");
        }

        /// <remarks>
        /// <para>
        /// The property the snapshot exists for, and the one that would break first. A network is
        /// not stored — it is rebuilt from the document every time the map is realised — so a
        /// rebuild that could not see the hand-placed obstacles would lay a different network from
        /// the one the map was built around, and re-grade the ground under every object already
        /// standing on it.
        /// </para>
        /// <para>
        /// Compared point for point rather than by length, because two networks of the same total
        /// length can be different networks.
        /// </para>
        /// </remarks>
        [Test]
        public void AMapWithHandPlacedBarriersReplaysTheSameRoads()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            CatalogEntry panel = catalog.Find(TestWorlds.FencePanelId);

            for (ulong seed = 1; seed <= 20; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };
                WorldDoc first = ArenaLayoutGenerator.Generate(parameters, catalog);

                RoadNetwork before = Network(parameters, first);
                if (before.IsEmpty)
                {
                    continue;
                }

                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    parameters,
                    catalog,
                    new[] { HandPlaced("fence_00", panel, MiddleOfTheLongestRoad(before)) });

                RoadNetwork laid = Network(parameters, doc);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork replayed);

                Assert.That(replayed.Segments.Count, Is.EqualTo(laid.Segments.Count),
                    $"seed {seed}: the replay laid a different number of roads");

                for (int i = 0; i < laid.Segments.Count; i++)
                {
                    IReadOnlyList<Vec2> want = laid.Segments[i].Points;
                    IReadOnlyList<Vec2> got = replayed.Segments[i].Points;

                    Assert.That(got.Count, Is.EqualTo(want.Count), $"seed {seed} road {i}");

                    for (int at = 0; at < want.Count; at++)
                    {
                        Assert.That(got[at].X, Is.EqualTo(want[at].X).Within(1e-6f),
                            $"seed {seed} road {i} point {at}");
                        Assert.That(got[at].Y, Is.EqualTo(want[at].Y).Within(1e-6f),
                            $"seed {seed} road {i} point {at}");
                    }
                }
            }
        }

        /// <remarks>
        /// A map nobody has edited records nothing and reads back nothing, which is what keeps every
        /// recorded digest in this suite where it was: the document of a map with no hand-placed
        /// obstacle on it is unchanged, key for key.
        /// </remarks>
        [Test]
        public void AMapWithNothingStandingRecordsNothing()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= 10; seed++)
            {
                var parameters = new ArenaParams { Seed = seed, RoadDensity = 1f };

                WorldDoc unedited = ArenaLayoutGenerator.Generate(parameters, catalog);
                WorldDoc empty = ArenaLayoutGenerator.Generate(
                    parameters, catalog, new List<EditOverride>());

                foreach (WorldDoc doc in new[] { unedited, empty })
                {
                    Assert.That(
                        doc.Metadata.ContainsKey(ArenaLayoutGenerator.StandingBarrierCountKey),
                        Is.False,
                        $"seed {seed}: a map with nothing standing wrote a barrier key");

                    Assert.That(ArenaLayoutGenerator.RecordedBarriers(doc), Is.Empty);
                }
            }
        }

        /// <remarks>
        /// Cover is deliberately not a barrier. A road is laid <em>past</em> a crate rather than
        /// round it — the reading the generated map already takes of its own cover — so a crate
        /// dropped in by hand records nothing and turns nothing.
        /// </remarks>
        [Test]
        public void HandPlacedCoverIsNotABarrier()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var parameters = new ArenaParams { Seed = 7UL, RoadDensity = 1f };
            CatalogEntry crate = catalog.Find(TestWorlds.CrateId);

            WorldDoc doc = ArenaLayoutGenerator.Generate(
                parameters,
                catalog,
                new[]
                {
                    EditOverride.Add(
                        PlacedObject.UserIdPrefix + "crate_00",
                        crate.LogicalId,
                        Pose.At(new Vec3(2f, 0f, 3f)),
                        new[] { "propbuilding/decor/outdecor/uniquegroup" }),
                });

            Assert.That(ArenaLayoutGenerator.RecordedBarriers(doc), Is.Empty);
            Assert.That(doc.Overrides.Count, Is.EqualTo(1),
                "the edit should be carried onto the document exactly once");
        }

        /// <summary>The first fence panel a document generated, or null if it generated none.</summary>
        static PlacedObject FirstFence(WorldDoc doc)
        {
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].Metadata.ContainsKey(ArenaLayoutGenerator.BarrierKey))
                {
                    return doc.GeneratedObjects[i];
                }
            }

            return null;
        }

        /// <summary>The first scattered crate a document holds, or null if it scattered none.</summary>
        static PlacedObject FirstCover(WorldDoc doc)
        {
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.Contains("/cover_") && !CoverPlacer.IsSocketProp(placed.StableId))
                {
                    return placed;
                }
            }

            return null;
        }

        static PlacedObject FindGenerated(WorldDoc doc, string stableId)
        {
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].StableId == stableId)
                {
                    return doc.GeneratedObjects[i];
                }
            }

            return null;
        }

        /// <summary>The ground an object records itself as standing on.</summary>
        static Rect2 Barrier(PlacedObject placed)
        {
            Assert.That(placed, Is.Not.Null);
            Assert.That(
                placed.Metadata.TryGetValue(ArenaLayoutGenerator.BarrierKey, out string text),
                Is.True,
                $"{placed.StableId} records no barrier");

            return RectMetadata.Parse(text);
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
