using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The street furniture stood along the verges: that it is spaced by distance along the road
    /// rather than by position along the polyline, that it stands nowhere a player needs to walk,
    /// that the map it furnishes is still a map worth playing, and that a workspace without the art
    /// is untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RoadKerbTests"/> asks the same questions of the edging and this file asks them of
    /// what stands behind it, with two more that only a spaced run raises: whether the spacing is
    /// the spacing it claims, and whether the places a road has something to mark actually get a
    /// piece.
    /// </para>
    /// <para>
    /// <strong>The ground is flat here on purpose</strong>, as it is for the kerbs. Where a piece
    /// sits along and across its road is a two-dimensional question and the height it is stood at
    /// is settled after the rules have run, so relief would add a variable to every measurement
    /// below without putting one of them at risk — with the single exception of
    /// <see cref="EveryPieceStandsOnTheGroundUnderItAtItsOwnBaseOffset"/>, which is the one
    /// measurement about height and turns the relief up to ask it.
    /// </para>
    /// </remarks>
    public sealed class RoadFurnitureTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every map.</summary>
        const int Seeds = 120;

        /// <summary>Seeds swept by the properties cheap enough to ask of a thousand maps.</summary>
        const int PropertySeeds = 1000;

        /// <summary>
        /// Seeds swept by the properties that have to analyse every map they generate.
        /// </summary>
        /// <remarks>
        /// Fewer than a thousand, and the reason is the clock rather than the statistics: measuring
        /// a map is far more expensive than generating one — <see cref="MapValidationTests"/> is the
        /// slowest test in this project at a thousand roadless seeds — and a furnished map at a road
        /// density of one carries more of everything the visibility sweep walks. Three hundred is
        /// what fits inside the runner's per-test timeout with room to spare on a machine that is
        /// doing something else too.
        /// </remarks>
        const int AnalysisSeeds = 300;

        /// <summary>
        /// A road density that lays a real network on the default map, matching
        /// <see cref="RoadPipelineTests"/> and <see cref="RoadKerbTests"/>.
        /// </summary>
        const float Density = 1f;

        /// <summary>How far a measurement may be out before it is a placement rather than a float.</summary>
        const float Slack = 0.01f;

        static ArenaParams Params(ulong seed) => new ArenaParams
        {
            Seed = seed,
            RoadDensity = Density,
        };

        /// <summary>
        /// FNV-1a of the document of seeds 1..200 of the default map at a road density of one with
        /// kerb art but no furniture art, recorded from the generator with the furniture stage taken
        /// out of the pipeline.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The counterpart to <c>RoadKerbTests.KerblessDigests</c> and it is here for the same
        /// reason: a workspace that has never filled <c>Props/Road/Furniture</c> is every workspace
        /// that existed before this stage did, and the override system rests on a regeneration
        /// putting every object back where it was.
        /// </para>
        /// <para>
        /// <strong>Recorded with the pipeline's one call to <c>RoadFurniture.Place</c> deleted</strong>,
        /// from a separate build of Core rather than from the code under test — re-recording from
        /// the changed code would prove nothing.
        /// </para>
        /// <para>
        /// <strong>And recorded <em>with</em> the cover-budget fix, which is the one thing here that
        /// is meant to have moved a kerbed map.</strong> Taking the road stages' own art out of the
        /// floor the cover target is counted off changes every map that has kerbing on it, on
        /// purpose — see <c>CoverPlacer.IsRoadside</c> — so a baseline that predated it would fail
        /// for the change it was made to allow and say nothing about the furniture. What this set
        /// isolates is the furniture stage alone, which is what it is for.
        /// </para>
        /// <para>
        /// The other line this work changed upstream, <c>RoadNetwork.IsPlacedAfterTheRoads</c>, is
        /// covered by the two baselines that predate the work entirely: the kerbless digests in
        /// <see cref="RoadKerbTests"/> and the roadless ones in <see cref="RoadPipelineTests"/>,
        /// both of which are maps with no furniture and no kerbing on them — so nothing in either
        /// carries a tag either change was taught, and both still hold.
        /// </para>
        /// <para>
        /// Of the document rather than of the text it serialises to, for the reason given there:
        /// Newtonsoft renders a quarter turn differently under .NET and under the runtime the editor
        /// runs on, and a text digest would fail on one of the two machines and say the generator
        /// had moved.
        /// </para>
        /// <para>
        /// <strong>Recorded on the runtime the editor runs on, and that turns out to matter for a
        /// map with kerbing on it.</strong> The same source generating the same seed gives one
        /// document under .NET 8 and a different one under Mono once there is a kerb run in it — see
        /// <c>FUTURE.md</c>, which has the measurement and what causes it. That is a fault of its
        /// own and it is older than this suite; what it means here is only that the baseline has to
        /// come from the editor, and the offline harness will disagree with this one test.
        /// The unwired build was measured on <em>both</em> runtimes before this array was chosen, so
        /// the difference is known to be the runtime rather than the stage: with the pipeline's call
        /// to <c>RoadFurniture.Place</c> deleted the editor gives exactly what it gives with the
        /// call in place, seed for seed.
        /// </para>
        /// </remarks>
        static readonly string[] UnfurnishedDigests =
        {
            "e8dd4ae3ca7166a8", "1af8e3df41cbe8e4", "bfc687956898c9b9", "ad264998ad8f36fb",
            "a48620e712637f42", "a107afc93c99d0f7", "6b1c146b91a1109a", "cab3e5a9d8c5323a",
            "973a15fb24780f4d", "4ab60ac0b6d41418", "fba0e107b4da1c5a", "422cdaf380cf3e5b",
            "29212663533c2c90", "8f97ab373709389d", "cf9395cc403e3393", "1bb4661c2678654c",
            "523715d05b4e115b", "367da449ac80a98d", "1a0a17626b0619fb", "fd0e58a0aeea1b07",
            "361720faea791be7", "c0f8aaa2e8689c36", "d8d61bc535de07ed", "c4fd8e83a6604dc0",
            "35702ca3e23efe71", "aec0bc1fcb8edd0e", "ddd63f535325abe7", "62271874b7838aa3",
            "f00469a07292b2a5", "8483635e23fcf26e", "4833a42428f24819", "458d215ee12d81f5",
            "9e0a97d008ac185c", "40137f6eaaff7e56", "ec4058c2152a4355", "4c61c8871a213e8e",
            "47cf0406af945159", "2da508842d77219d", "4a59fb45b15eabae", "925276622a9d1eb9",
            "7581e878e0fbb3a7", "2088b55638754379", "8b19f26a3438b15d", "2c2c6a5afd45c0ed",
            "cc5d67b8944fd373", "5adc73ea481aad4c", "4659ef1ce2dacf56", "5f4eb30919e65c5c",
            "ff07cc4733be7b04", "936641af37e974cf", "7f0becc5cf88dedc", "1badd24d6577fd43",
            "223456f8390b0131", "de977b089d72d156", "3254ea5f82c3f8d4", "a2fcaef3ec7a980d",
            "e3eed8dee063d5bb", "8bd4015d6549bd33", "7b695e94401e8f1e", "c904e451c0808d30",
            "3412ed0ea75275d4", "014fd119315637ce", "fb2a17ae06e1c575", "27028831e742ca03",
            "84ce1d7e49efcc67", "f41f215854346231", "0e89de4a4573a3f0", "55696b577b67d248",
            "112f2ca86bbcb78b", "d75780cda0d41a9f", "8ff294050483e003", "303ec0aa201a95df",
            "576aeb8ba8122dca", "b916b2dae15af7c9", "a8cdbedf3eb6e209", "2cf55f3b94425445",
            "314e19cfdc8d16ad", "c24cea6bf3bfc3a7", "c361c2609c9c0eb8", "a8f941fc407d9132",
            "38247dd95ef791c8", "c9b49f0e71f02635", "ef01222acda2348b", "14b063acecdc4a24",
            "865ed6826fcb07c4", "f83b5974424e18eb", "56bb6e11153f5385", "fca1a2c800144a3d",
            "1ee1ea999162f277", "5593715ec41dd26d", "ea41b6b1aa1f6035", "3d473d66350789e7",
            "3d0c8c0664bc516f", "864e6ee307de4e0a", "05c2c6fe535918c7", "7abdc384d392afd1",
            "41bc5c19c44bd855", "bfc8da142d055337", "7de2c2df326a3b0a", "1abd5352ba51f817",
            "205a8fb8d56c2cec", "85d4373aa23fc97d", "da1f75a4e25cdc95", "c698eb1acb4fcec9",
            "77c78d43ccd2cf25", "d5d69f8a2730d992", "1f207d69d361e98d", "6d49b1b7f2eeb08c",
            "40b4b4d142232cc7", "17c0f4f870e0214c", "5cd36a4c6c9acf84", "7fd9d17653b55e01",
            "ea71a0a6a5862dd5", "f95a71f8f53ccf69", "ff3afa9e849fa2c1", "086661b486ce8f1f",
            "8b20e39cd7183b09", "f0498f026b578819", "82a5d0a237137568", "fbbe0d9341b6641c",
            "b2cf967b3f0f934d", "5f0f4a7370bcfb5d", "44e65ce8b7dd8261", "4ca2374129004430",
            "018d9c08c925e456", "3dc3215281e5720b", "c72972078e9b340a", "979f2eb4afe2a282",
            "829246f6e8659c70", "9dc17da4a5a07b23", "e573436fa68224c2", "aee5e60673722de7",
            "295604e01bdc9901", "8af7a58f755710fc", "c647482b76327594", "5d949e183d6e8836",
            "a8d2ed70a658682b", "831757859fb78214", "25cb9fac27be4899", "59da587c4d0ba23a",
            "974af2e2dd985e76", "70b71f4f4bd97963", "9114654fa0e2e6ef", "4d86dd9fc7625d72",
            "ab302b4e737aadd6", "b2a0fc489e46b84f", "42af4777ef8e4c7c", "ed39b41bcad35326",
            "7137ecabf7cd5c5e", "28608c2611d2e477", "cfc2705974b852c9", "b7c3edf086fc3eda",
            "7d9010fcde5dfcec", "9dba66120fe66b62", "c4292d68cfa2cf06", "8dd5ee57ddb60221",
            "e9e06853fb6092f8", "18465460c6b95d8e", "b07e90f367b293b5", "e3883b53f8306f3f",
            "0c7cc434a85032fa", "20c574a9e788b25d", "9a752ebf2e5e6f9d", "96cbc2dd5f6be87a",
            "0ad524f24d5fa3c2", "44d117bb60dc2fa6", "6329f6c5792bd8a6", "8a04de8dace5e8d2",
            "d13cff33c2643847", "bb4aa6d49d3ff0f6", "ed5e422bb5be80d4", "263928d2ffcef6e8",
            "15954d2c31ceb2cf", "f1e75df8ec4a079a", "836b73fa7b312839", "aba049d087681d28",
            "f7c91dccc47f25c5", "ec407c0329e2425f", "c3ac413b8852e126", "5dc5402cfca39b9b",
            "7852b6bffef3f9c8", "0d5681f239f44711", "b18ec4edf764897c", "034a72c27f15e768",
            "af5960869df89550", "cb05cc537d1c43d5", "af2daa583e3539ec", "353c5edfb04db78b",
            "14831da52e69248f", "4e976e346dbdb518", "0cf8e0119bdf954b", "4a2711a6b109aae0",
            "12d4dcf4069b39a9", "320d4462dc4f98f4", "642c6b291639ce9e", "9956a2fba0e07960",
            "667b21bb15ad8f28", "e143939a76404f90", "8177d31f43e1a38b", "61efae5f2d13991a",
        };

        // --- nothing stands where a player has to be able to walk -------------------------------

        /// <remarks>
        /// <para>
        /// The whole of what <see cref="RoadFurniture.Rules"/> is for, over a thousand seeds. A
        /// bench in a doorway is the same bug as a crate in one, a bench in a carriageway is the
        /// same bug as a crate in one, and a bench in a spawn is worse than either.
        /// </para>
        /// <para>
        /// Measured against <see cref="RoadNetwork.Corridors"/> rather than against the carriageways
        /// themselves, because the corridors are what the rule tests and they are deliberately the
        /// larger claim — see <see cref="RoadFurniture.Verge"/> for what holding the furniture to
        /// them costs. A piece clear of the reservation is clear of the road inside it.
        /// </para>
        /// <para>
        /// Generation only, with no analysis in it, which is what makes a thousand seeds affordable
        /// here where <see cref="EverySeedProducesAFurnishedMapWorthPlaying"/> settles for three
        /// hundred.
        /// </para>
        /// </remarks>
        [Test]
        public void NoFurnitureStandsInACorridorADoorwayOrASpawn()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var offenders = new System.Collections.Concurrent.ConcurrentBag<string>();
            var counts = new int[PropertySeeds + 1];

            Parallel.For(1, PropertySeeds + 1, seed =>
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params((ulong)seed), catalog);
                ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);
                List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);

                List<PlacedObject> furniture = Furniture(doc);
                counts[seed] = furniture.Count;

                for (int i = 0; i < furniture.Count; i++)
                {
                    PlacedObject placed = furniture[i];
                    Rect2 box = PlacedGeometry.WorldFootprint(placed, catalog);

                    for (int c = 0; c < roads.Corridors.Count; c++)
                    {
                        if (box.Overlaps(roads.Corridors[c]))
                        {
                            offenders.Add($"seed {seed}: {placed.StableId} stands in a carriageway");
                            break;
                        }
                    }

                    for (int d = 0; d < doorways.Count; d++)
                    {
                        if (box.Overlaps(doorways[d].Expanded(CoverPlacer.DoorwayClearance)))
                        {
                            offenders.Add($"seed {seed}: {placed.StableId} stands in a doorway");
                            break;
                        }
                    }

                    if (box.Overlaps(layout.SpawnAreaA.Expanded(CoverPlacer.SpawnClearance)) ||
                        box.Overlaps(layout.SpawnAreaB.Expanded(CoverPlacer.SpawnClearance)))
                    {
                        offenders.Add($"seed {seed}: {placed.StableId} stands in a spawn");
                    }

                    if (!layout.Playfield.Contains(box))
                    {
                        offenders.Add($"seed {seed}: {placed.StableId} stands off the map");
                    }
                }
            });

            var total = 0;
            var empty = 0;
            for (int seed = 1; seed <= PropertySeeds; seed++)
            {
                total += counts[seed];
                if (counts[seed] == 0)
                {
                    empty++;
                }
            }

            Assert.That(total, Is.GreaterThan(0), "no furniture was stood up on any seed of the sweep");
            Assert.That(empty, Is.Zero, $"{empty} of {PropertySeeds} seeds furnished no road at all");
            Assert.That(
                offenders, Is.Empty,
                "street furniture is standing where the rules said it may not");
        }

        // --- the spacing is a distance along the road -------------------------------------------

        /// <remarks>
        /// <para>
        /// <strong>The property the arc-length table buys.</strong> A carriageway's centreline is
        /// smoothed, so its vertices crowd round the bends; a run stepped through that list in
        /// parameter space comes out bunched on the curves and stretched on the straights whatever
        /// spacing it claims. Measured in metres along the polyline the gaps have to be the gaps the
        /// jitter produces, and nothing may be closer to its neighbour than the shortest of them.
        /// </para>
        /// <para>
        /// <strong>Measured only where the measurement is exact, which is what "where it should be"
        /// means.</strong> Recovering how far along a road a piece stands means projecting its pivot
        /// back onto the polyline, and a pivot seated four metres out on the concave side of a bend
        /// is nearer the far arm of its own polyline than the arm it was placed on — the same fold
        /// <see cref="RoadKerbs"/> documents about the last piece before a corner. A piece that has
        /// folded is detectable rather than guessed at: it comes back nearer the polyline than the
        /// seat it was placed at, and the pairs where either has are left out. Over seeds 1..120
        /// that leaves about a third of the gaps, and every one of them is exact.
        /// </para>
        /// <para>
        /// The floor is a hard bound and the band is a share, because they are two different claims.
        /// Nothing may stand closer than the jitter's floor — that is arithmetic, and a run that
        /// broke it would be putting two pieces on one spot. Falling <em>inside</em> the band is
        /// what a stretch of open verge does; a gap longer than the band is a piece the rules
        /// refused or a junction the run was cut at, and those are meant to be there.
        /// </para>
        /// </remarks>
        [Test]
        public void FurnitureIsSpacedByDistanceAlongTheRoadRatherThanByPolylinePosition()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();

            float floor = RoadFurniture.Spacing * (1f - RoadFurniture.SpacingJitter);
            float ceiling = RoadFurniture.Spacing * (1f + RoadFurniture.SpacingJitter);

            var tooClose = new List<string>();
            var measured = 0;
            var inBand = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                for (int s = 0; s < roads.Segments.Count; s++)
                {
                    RoadSegment segment = roads.Segments[s];
                    List<Seat> seats = Seats(doc, catalog, segment);
                    seats.Sort((a, b) => a.Along.CompareTo(b.Along));

                    for (int i = 1; i < seats.Count; i++)
                    {
                        if (!seats[i].IsExact || !seats[i - 1].IsExact ||
                            seats[i].Piece != seats[i - 1].Piece)
                        {
                            continue;
                        }

                        float gap = seats[i].Along - seats[i - 1].Along;
                        measured++;

                        if (gap < floor - Slack)
                        {
                            tooClose.Add(
                                $"seed {seed}: {seats[i].Id} stands " +
                                $"{gap.ToString("0.00", CultureInfo.InvariantCulture)} m from " +
                                $"{seats[i - 1].Id} along {segment.Id}");
                        }

                        if (gap >= floor - Slack && gap <= ceiling + Slack)
                        {
                            inBand++;
                        }
                    }
                }
            }

            Assert.That(measured, Is.GreaterThan(200),
                "too few gaps could be measured exactly for the sweep to say anything");
            Assert.That(tooClose, Is.Empty,
                $"street furniture is closer together along its road than the jitter's floor of " +
                $"{floor.ToString("0.00", CultureInfo.InvariantCulture)} m");
            Assert.That(inBand / (float)measured, Is.GreaterThan(0.75f),
                $"only {inBand} of {measured} exactly measured gaps fall inside " +
                $"[{floor.ToString("0.0", CultureInfo.InvariantCulture)}, " +
                $"{ceiling.ToString("0.0", CultureInfo.InvariantCulture)}] m");
        }

        /// <remarks>
        /// <para>
        /// The other half of the same claim: the pieces are not all at one spacing. A run laid at
        /// exactly <see cref="RoadFurniture.Spacing"/> would satisfy the floor and the band above
        /// and would read as a generated map from across the arena, which is what the jitter exists
        /// to prevent.
        /// </para>
        /// <para>
        /// <strong>Stated as the spread of the gaps rather than as no two of them matching.</strong>
        /// The jitter draws from a continuous range, so on any sample big enough to be worth
        /// measuring some pairs of gaps land within a centimetre of each other whatever the
        /// generator does — counting near-matches measures how many gaps there are, not whether they
        /// vary. Counting the two tails does not work either, and the reason is the measurement
        /// rather than the run: a gap is only measurable where both of its pieces sit on one
        /// straight stretch of the polyline, and a long gap is likelier to straddle two — so the
        /// long tail is under-sampled by the very filter that makes the measurement exact.
        /// </para>
        /// <para>
        /// What survives both is the spread between the tenth and ninetieth percentile of the gaps
        /// that fall inside the band. A run laid at a fixed spacing puts that at zero however it is
        /// sampled.
        /// </para>
        /// </remarks>
        [Test]
        public void TheSpacingIsJitteredRatherThanFixed()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();

            float floor = RoadFurniture.Spacing * (1f - RoadFurniture.SpacingJitter);
            float ceiling = RoadFurniture.Spacing * (1f + RoadFurniture.SpacingJitter);
            var band = new List<float>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                for (int s = 0; s < roads.Segments.Count; s++)
                {
                    List<Seat> seats = Seats(doc, catalog, roads.Segments[s]);
                    seats.Sort((a, b) => a.Along.CompareTo(b.Along));

                    for (int i = 1; i < seats.Count; i++)
                    {
                        if (!seats[i].IsExact || !seats[i - 1].IsExact ||
                            seats[i].Piece != seats[i - 1].Piece)
                        {
                            continue;
                        }

                        // Only gaps inside the band say anything about the jitter; a longer one is
                        // a piece the rules refused or a junction the run was cut at.
                        float gap = seats[i].Along - seats[i - 1].Along;
                        if (gap <= ceiling + Slack)
                        {
                            band.Add(gap);
                        }
                    }
                }
            }

            Assert.That(band.Count, Is.GreaterThan(150), "too few gaps to say anything about spread");

            band.Sort();
            float spread = band[band.Count * 9 / 10] - band[band.Count / 10];

            Assert.That(spread, Is.GreaterThan((ceiling - floor) / 3f),
                $"the middle four fifths of the gaps span only " +
                $"{spread.ToString("0.00", CultureInfo.InvariantCulture)} m of a " +
                $"{(ceiling - floor).ToString("0.00", CultureInfo.InvariantCulture)} m jitter, " +
                "which is a run laid at a fixed spacing rather than a jittered one");
        }

        // --- the places a road has something to mark get a piece --------------------------------

        /// <remarks>
        /// <para>
        /// The events go down before the fill, and the junction corners are the events there are
        /// most of. A run that only filled at its spacing would mark a corner when the rhythm
        /// happened to land on one; over these seeds two corners in three carry a piece within a
        /// gap's reach of them, and the third is a corner where a building, a braid or a doorway
        /// refused everything offered.
        /// </para>
        /// <para>
        /// A share rather than a per-corner guarantee, because a guarantee is not what this stage
        /// makes: an event is a position offered, and <see cref="ConstraintSet"/> is still what
        /// decides. Two in three is far enough above what the fill alone would manage — a corner
        /// falls inside one gap in <see cref="RoadFurniture.Spacing"/> metres of run — to be a
        /// statement about the events rather than about the spacing.
        /// </para>
        /// </remarks>
        [Test]
        public void TheCornersOfAJunctionAreMarked()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var corners = 0;
            var marked = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                // How far from a junction's centre the first piece on an approach to it can stand:
                // out of the disc, past a portal's own setback, and up to a gap along from there.
                float reach = roads.JunctionRadius + CoverPlacer.DoorwayClearance +
                              RoadFurniture.Spacing * (1f + RoadFurniture.SpacingJitter);

                for (int s = 0; s < roads.Segments.Count; s++)
                {
                    RoadSegment segment = roads.Segments[s];
                    List<Seat> seats = Seats(doc, catalog, segment);
                    if (seats.Count == 0)
                    {
                        continue;
                    }

                    for (int j = 0; j < roads.Junctions.Count; j++)
                    {
                        RoadJunction junction = roads.Junctions[j];
                        if (junction.Kind == RoadJunctionKind.Terminal ||
                            !Touches(segment, junction.Position, roads.JunctionRadius))
                        {
                            continue;
                        }

                        corners++;
                        for (int i = 0; i < seats.Count; i++)
                        {
                            if (Vec2.Distance(seats[i].At, junction.Position) <= reach)
                            {
                                marked++;
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(corners, Is.GreaterThan(500), "too few junction corners to say anything");
            Assert.That(marked / (float)corners, Is.GreaterThan(0.6f),
                $"only {marked} of {corners} junction corners on a furnished road carry a piece");
        }

        /// <remarks>
        /// <para>
        /// The other collector: a bend tighter than <see cref="RoadFurniture.ApexRadius"/> gets a
        /// piece at its deepest point. Over these seeds nine apexes in ten carry one within a gap's
        /// reach, and the tenth is a bend where the rules refused everything offered.
        /// </para>
        /// <para>
        /// <strong>The apexes are worked out here rather than asked of the stage.</strong> A test
        /// that called the same code to find the bends it then checked were marked would pass
        /// whatever that code said — so the depth sweep is written out again, and the two agreeing
        /// is the measurement. It is also what makes this a test of the threshold: a curvature limit
        /// that never fired would leave this with nothing to count, and the sweep would say so.
        /// </para>
        /// </remarks>
        [Test]
        public void TheApexOfABendIsMarked()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var apexes = 0;
            var marked = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                float reach = RoadFurniture.Spacing * (1f + RoadFurniture.SpacingJitter);

                for (int s = 0; s < roads.Segments.Count; s++)
                {
                    RoadSegment segment = roads.Segments[s];
                    List<Seat> seats = Seats(doc, catalog, segment);
                    if (seats.Count == 0)
                    {
                        continue;
                    }

                    List<Vec2> bends = Apexes(segment);
                    for (int b = 0; b < bends.Count; b++)
                    {
                        apexes++;
                        for (int i = 0; i < seats.Count; i++)
                        {
                            if (Vec2.Distance(seats[i].At, bends[b]) <= reach)
                            {
                                marked++;
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(apexes, Is.GreaterThan(500),
                "no bend on any road of the sweep was tight enough to count as an apex, so the " +
                "curvature threshold is never firing and the collector is dead");
            Assert.That(marked / (float)apexes, Is.GreaterThan(0.75f),
                $"only {marked} of {apexes} bends carry a piece within a gap of their apex");
        }

        // --- turned by the road, through the yaw table -------------------------------------------

        /// <remarks>
        /// <para>
        /// <strong>Every rotation is one of the twenty-four, exactly.</strong> Not near one — equal
        /// to the literal in <see cref="YawStep"/>, bit for bit. That is the property the table
        /// exists for: an arbitrary angle needs <see cref="MathF.Sin"/>, whose last-ulp result is
        /// not guaranteed identical across runtimes, and a map whose furniture sits one ulp apart on
        /// two machines is not the same map. <see cref="PlacedGeometry.YawSteps"/> fails the test
        /// if a rotation is off the table.
        /// </para>
        /// <para>
        /// <strong>And the step is the one nearest the road's own tangent.</strong> Measured only
        /// where the projection back onto the polyline is exact, for the reason
        /// <see cref="FurnitureIsSpacedByDistanceAlongTheRoadRatherThanByPolylinePosition"/> gives:
        /// a folded projection reports the tangent of the wrong arm of a bend, so the answer it
        /// would be checked against is the wrong answer rather than the placement being wrong.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryPieceIsTurnedToTheStepOfTheYawTableNearestItsRoad()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var offenders = new List<string>();
            var measured = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                for (int s = 0; s < roads.Segments.Count; s++)
                {
                    List<Seat> seats = Seats(doc, catalog, roads.Segments[s]);
                    for (int i = 0; i < seats.Count; i++)
                    {
                        Seat seat = seats[i];

                        // Reads the step off the table and fails outright if there is none, which is
                        // the first half of the property.
                        int steps = PlacedGeometry.YawSteps(seat.Placed);
                        if (!seat.IsExact)
                        {
                            continue;
                        }

                        measured++;

                        // The run's own yaw plus the quarter turn that lays the art's long axis
                        // along it, which is exactly what WallRun.Face.Steps composes.
                        Rect2 footprint = catalog.Find(seat.Placed.LogicalId).Footprint;
                        int quarter = footprint.Width >= footprint.Depth ? 0 : YawStep.Count / 4;
                        int wanted = YawStep.Normalize(YawStep.Nearest(seat.Tangent) + quarter);

                        if (steps != wanted)
                        {
                            offenders.Add(
                                $"seed {seed}: {seat.Id} is at step {steps} where its road wants " +
                                $"{wanted}");
                        }
                    }
                }
            }

            Assert.That(measured, Is.GreaterThan(500), "too few pieces could be measured exactly");
            Assert.That(offenders, Is.Empty, "street furniture is not turned by the road it lines");
        }

        // --- stood on the ground, at the art's own base offset ------------------------------------

        /// <remarks>
        /// <para>
        /// The height comes from <see cref="TerrainField.HeightAt"/> and the lift from the entry's
        /// own <see cref="CatalogEntry.BaseOffset"/> through <see cref="Placement.AtYawStep"/> — not
        /// from a raycast, which would answer with whatever colliders a scene happened to have and
        /// would put the result outside the document. Asserted by reading the same field back and
        /// finding the same number.
        /// </para>
        /// <para>
        /// <strong>On relief, which is the only test here that turns it up.</strong> On flat ground
        /// every height is the same height and a stage that ignored the field entirely would pass;
        /// the bench in <see cref="TestWorlds.FurnitureCatalog"/> carries a base offset for the same
        /// reason, since art modelled about its own centre is what catches a lift that was never
        /// applied.
        /// </para>
        /// <para>
        /// The field is rebuilt through <see cref="ArenaLayoutGenerator.Terrain(WorldDoc)"/>, which
        /// replays the foundations and then the roads — so what a piece is measured against is the
        /// ground with the carriageway already graded into it, which is the ground it was stood on.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryPieceStandsOnTheGroundUnderItAtItsOwnBaseOffset()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var offenders = new List<string>();
            var measured = 0;

            for (ulong seed = 1; seed <= 40; seed++)
            {
                ArenaParams parameters = Params(seed);
                parameters.TerrainAmplitude = 4f;

                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);

                List<PlacedObject> furniture = Furniture(doc);
                for (int i = 0; i < furniture.Count; i++)
                {
                    PlacedObject placed = furniture[i];
                    CatalogEntry entry = catalog.Find(placed.LogicalId);
                    float wanted = terrain.HeightAt(placed.Pose.Position.Xz) + entry.BaseOffset;

                    measured++;
                    if (MathF.Abs(placed.Pose.Position.Y - wanted) > Slack)
                    {
                        offenders.Add(
                            $"seed {seed}: {placed.StableId} stands at " +
                            $"{placed.Pose.Position.Y.ToString("0.###", CultureInfo.InvariantCulture)} " +
                            $"where the ground plus its base offset is " +
                            $"{wanted.ToString("0.###", CultureInfo.InvariantCulture)}");
                    }
                }
            }

            Assert.That(measured, Is.GreaterThan(0), "no furniture was stood up on any seed");
            Assert.That(offenders, Is.Empty,
                "street furniture is not standing on the ground under it");
        }

        // --- the ids are a generation path an override can hold on to -----------------------------

        [Test]
        public void APieceIsNamedForTheRoadItStandsBeside()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var offenders = new List<string>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

                var roads_ = new List<string>();
                for (int i = 0; i < roads.Segments.Count; i++)
                {
                    roads_.Add(roads.Segments[i].Id);
                }

                List<PlacedObject> furniture = Furniture(doc);
                for (int i = 0; i < furniture.Count; i++)
                {
                    string id = furniture[i].StableId;
                    Assert.That(id, Does.StartWith(RoadFurniture.IdPrefix));

                    int at = id.LastIndexOf(RoadFurniture.FurnitureSegment, StringComparison.Ordinal);
                    Assert.That(at, Is.GreaterThan(0), $"'{id}' is not a furniture path");

                    string owner = id.Substring(
                        RoadFurniture.IdPrefix.Length, at - RoadFurniture.IdPrefix.Length);
                    if (!roads_.Contains(owner))
                    {
                        offenders.Add($"seed {seed}: '{id}' names '{owner}', which is not a road");
                    }

                    string index = id.Substring(at + RoadFurniture.FurnitureSegment.Length);
                    Assert.That(
                        int.TryParse(
                            index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _),
                        $"'{id}' does not end in a number");
                }
            }

            Assert.That(offenders, Is.Empty, "a piece of furniture is filed under something that is not a road");
        }

        /// <remarks>
        /// The property the override system rests on: the same seed regenerates the same furniture,
        /// down to the ids, the poses and the tags. Deep-compared rather than counted, because a
        /// stage that placed the same number of pieces in different places would pass a count.
        /// </remarks>
        [Test]
        public void TheFurnitureIsIdenticalAcrossTwoGenerationsOfOneSeed()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                List<PlacedObject> first =
                    Furniture(ArenaLayoutGenerator.Generate(Params(seed), catalog));
                List<PlacedObject> second =
                    Furniture(ArenaLayoutGenerator.Generate(Params(seed), catalog));

                Assert.That(second.Count, Is.EqualTo(first.Count),
                    $"seed {seed} placed {first.Count} pieces and then {second.Count}");

                for (int i = 0; i < first.Count; i++)
                {
                    WorldAssert.AreDeepEqual(first[i], second[i], $"seed {seed} furniture {i}");
                }
            }
        }

        // --- a workspace without the art is the workspace it always was ---------------------------

        /// <remarks>
        /// Two hundred seeds of the default map with roads and kerbs on it, each reduced to one
        /// number over every id, pose, tag and metadata entry the document holds — see
        /// <see cref="UnfurnishedDigests"/>.
        /// </remarks>
        [Test]
        public void ACatalogWithNoFurnitureArtGeneratesTheMapItAlwaysDid()
        {
            Catalog catalog = TestWorlds.KerbCatalog();
            var moved = new List<string>();

            for (int seed = 1; seed <= UnfurnishedDigests.Length; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params((ulong)seed), catalog);
                string digest = Digest(doc).ToString("x16", CultureInfo.InvariantCulture);

                if (!string.Equals(digest, UnfurnishedDigests[seed - 1], StringComparison.Ordinal))
                {
                    moved.Add($"seed {seed}: {UnfurnishedDigests[seed - 1]} became {digest}");
                }
            }

            Assert.That(moved, Is.Empty,
                "a map with roads, kerbs and no furniture art is no longer the map it was before " +
                "the furniture stage was in the pipeline");
        }

        /// <remarks>
        /// The mechanism the property above rests on: with nothing in the folder the stage returns
        /// before it forks a stream or evaluates a rule, so it writes no statistics at all. A stage
        /// that had run and placed nothing would leave a tally of zero, which reads as a stage that
        /// found no room rather than one that was never asked.
        /// </remarks>
        [Test]
        public void ACatalogWithNoFurnitureArtTakesNoDraw()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(Params(7UL), TestWorlds.KerbCatalog());

            foreach (KeyValuePair<string, string> entry in doc.Metadata)
            {
                Assert.That(entry.Key, Does.Not.StartWith(RoadFurniture.StatsPrefix),
                    "the furniture stage reported statistics for a catalog it has no art in");
            }

            Assert.That(Furniture(doc), Is.Empty);
        }

        /// <remarks>
        /// The other half of the same bargain: a map with the art but no roads. A network with no
        /// carriageways has no verge, and the stage leaves before the catalog is queried.
        /// </remarks>
        [Test]
        public void AMapWithNoRoadsGetsNoFurniture()
        {
            WorldDoc doc = ArenaLayoutGenerator.Generate(
                new ArenaParams { Seed = 7UL }, TestWorlds.FurnitureCatalog());

            Assert.That(Furniture(doc), Is.Empty);

            foreach (KeyValuePair<string, string> entry in doc.Metadata)
            {
                Assert.That(entry.Key, Does.Not.StartWith(RoadFurniture.StatsPrefix));
            }
        }

        // --- the network is still a function of the document ---------------------------------------

        /// <remarks>
        /// A network is rebuilt from a finished document every time anything asks where the roads
        /// are, and the router prices sheltered ground below open ground — so an object the router
        /// can see moves the roads. Furniture is placed <em>from</em> a network, so counting it
        /// would route the next rebuild round the verges the last one furnished, and the corridors
        /// the cover was placed against would move under it. The same bargain the kerbs and the
        /// cover already make, asserted the same way.
        /// </remarks>
        [Test]
        public void TheNetworkIsUnchangedByTheFurnitureAlongIt()
        {
            Catalog furnished = TestWorlds.FurnitureCatalog();
            Catalog plain = TestWorlds.KerbCatalog();
            var moved = new List<string>();

            for (ulong seed = 1; seed <= 40; seed++)
            {
                ArenaLayoutGenerator.Terrain(
                    ArenaLayoutGenerator.Generate(Params(seed), furnished), out RoadNetwork with);
                ArenaLayoutGenerator.Terrain(
                    ArenaLayoutGenerator.Generate(Params(seed), plain), out RoadNetwork without);

                if (with.Segments.Count != without.Segments.Count)
                {
                    moved.Add(
                        $"seed {seed}: {without.Segments.Count} carriageways became " +
                        $"{with.Segments.Count}");
                    continue;
                }

                for (int i = 0; i < with.Segments.Count; i++)
                {
                    if (!Same(with.Segments[i], without.Segments[i]))
                    {
                        moved.Add($"seed {seed}: {without.Segments[i].Id} moved");
                    }
                }
            }

            Assert.That(moved, Is.Empty,
                "the roads are routed differently on a map that has furniture standing along them");
        }

        // --- the analysis sees it -------------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// <strong>A bus shelter on a verge is cover, not decoration.</strong>
        /// <see cref="MapAnalyzer"/> builds an <see cref="Occluder"/> from every object whose art
        /// stands across the eye line, whatever put it there, and the furniture is not excluded from
        /// that — so a map with shelters along its roads is measurably more sheltered than the same
        /// map without them.
        /// </para>
        /// <para>
        /// Measured through <see cref="ExposureMap.Mean"/> rather than by reaching into the
        /// analyser's occluder list, because the question is whether the metric moves — and
        /// measured by taking the furniture back out of a finished document rather than by
        /// generating a second map without the art, so that the only thing that changes is what the
        /// analyser is looking at. Two catalogs would give two different maps, with the cover in
        /// different places, and the exposure would move for reasons that have nothing to do with
        /// this question.
        /// </para>
        /// <para>
        /// The bench does not appear in this: it is 0.9 m to the top of its back and the eye line is
        /// at 1.6, so it drops out of the standing-eye test exactly as low cover does. That is the
        /// height model working, not a piece being missed — see <see cref="Occluder.TryCreate"/>.
        /// </para>
        /// </remarks>
        [Test]
        public void MapAnalyzerCountsFurnitureThatStandsAcrossTheEyeLine()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();

            var sheltered = 0;
            var moreExposed = new List<string>();
            var measured = 0;

            for (ulong seed = 1; seed <= 24; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);

                float with = MapAnalyzer.Analyze(doc, catalog).Exposure.Mean;
                float without = MapAnalyzer.Analyze(
                    Without(doc, RoadFurniture.FurnitureTag), catalog).Exposure.Mean;

                measured++;

                // Taking occluders off a map can only open it up, so this direction is a fact about
                // the model rather than a tendency, and a seed that went the other way would mean
                // the furniture was making the map more exposed by standing on it.
                if (without < with - Slack)
                {
                    moreExposed.Add($"seed {seed}: taking the furniture out lowered mean exposure");
                }

                if (without > with + Slack)
                {
                    sheltered++;
                }
            }

            Assert.That(moreExposed, Is.Empty);
            Assert.That(sheltered / (float)measured, Is.GreaterThan(0.7f),
                $"taking the street furniture out of a document raised its mean exposure on only " +
                $"{sheltered} of {measured} seeds, so the analysis is not seeing the furniture as " +
                "occluders");
        }

        // --- the cover budget ---------------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// <strong>A road does not make a map want less cover, and neither does what a road stage
        /// stood on it.</strong> The floor the cover target is counted off already leaves the
        /// carriageways in — a player moves through a road, so a lane with one down it wants exactly
        /// the cover it wanted without one — and a kerb along its edge and a lamp post on its verge
        /// are the same claim about the same ground. See <c>CoverPlacer.IsRoadside</c>.
        /// </para>
        /// <para>
        /// Asserted as an equality rather than as a bound, because it is one: three catalogs that
        /// differ only in their road art, on one seed, produce three maps whose structures,
        /// boundary, dressing and carriageways are identical, so the floor the target is counted off
        /// is identical too. Before the fix the same three seeds asked for measurably different
        /// amounts of cover, and the one with the most road art asked for the least.
        /// </para>
        /// </remarks>
        [Test]
        public void TheCoverTargetIsNotShrunkByWhatTheRoadStagesStandOnTheGround()
        {
            Catalog bare = TestWorlds.SampleCatalog();
            Catalog kerbed = TestWorlds.KerbCatalog();
            Catalog furnished = TestWorlds.FurnitureCatalog();
            var moved = new List<string>();

            for (ulong seed = 1; seed <= 60; seed++)
            {
                int want = Target(ArenaLayoutGenerator.Generate(Params(seed), bare));
                int withKerbs = Target(ArenaLayoutGenerator.Generate(Params(seed), kerbed));
                int withBoth = Target(ArenaLayoutGenerator.Generate(Params(seed), furnished));

                if (withKerbs != want || withBoth != want)
                {
                    moved.Add(
                        $"seed {seed}: a bare map asks for {want}, a kerbed one for {withKerbs}, " +
                        $"a furnished one for {withBoth}");
                }
            }

            Assert.That(moved, Is.Empty,
                "the cover target shrinks when the road stages stand something on the floor");
        }

        /// <remarks>
        /// <para>
        /// <strong>The other half of the budget: what fulfils the target rather than what sets
        /// it.</strong> A bench on a verge is cover — in a sixty-metre arena a player pinned in the
        /// open beside a road reaches it, and it stands in place of a crate the placer would
        /// otherwise have put there — so it counts towards the floor being within reach of cover. A
        /// kerb is a line of stone a few centimetres proud of the road and nobody gets behind one,
        /// so it does not.
        /// </para>
        /// <para>
        /// <strong>Measured by taking the art back out of a finished document rather than by
        /// generating two maps.</strong> Two catalogs give two different maps — the kerbing takes
        /// ground the cover placer would have used, so cover lands elsewhere and some of it does not
        /// land at all — and that is a fact about placement, not about what counts. Analysing one
        /// document twice, with the pieces and then without them, changes only what the analyser is
        /// looking at, which is exactly the question.
        /// </para>
        /// </remarks>
        [Test]
        public void FurnitureCountsTowardsTheCoverTargetAndKerbingDoesNot()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var kerbsMoved = new List<string>();
            var raised = 0;
            var measured = 0;

            for (ulong seed = 1; seed <= 24; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                float whole = MapAnalyzer.Analyze(doc, catalog).CoverCoverage;

                float withoutKerbs = MapAnalyzer.Analyze(
                    Without(doc, RoadKerbs.KerbTag), catalog).CoverCoverage;
                float withoutFurniture = MapAnalyzer.Analyze(
                    Without(doc, RoadFurniture.FurnitureTag), catalog).CoverCoverage;

                if (MathF.Abs(withoutKerbs - whole) > Slack)
                {
                    kerbsMoved.Add(
                        $"seed {seed}: taking the kerbing out took cover coverage from " +
                        $"{whole.ToString("0.000", CultureInfo.InvariantCulture)} to " +
                        $"{withoutKerbs.ToString("0.000", CultureInfo.InvariantCulture)}");
                }

                measured++;
                if (withoutFurniture < whole - Slack)
                {
                    raised++;
                }
            }

            Assert.That(kerbsMoved, Is.Empty,
                "kerbing is being counted towards the floor being within reach of cover, and " +
                "nobody takes cover behind a kerb");
            Assert.That(raised, Is.GreaterThan(measured - 3),
                $"taking the street furniture out lowered cover coverage on only {raised} of " +
                $"{measured} seeds, so the analysis is not counting it towards the target it " +
                "helps fulfil");
        }

        /// <summary>The same document with everything carrying a tag taken out of it.</summary>
        static WorldDoc Without(WorldDoc doc, string tag)
        {
            var stripped = new WorldDoc { Parameters = doc.Parameters.Clone() };

            foreach (KeyValuePair<string, string> entry in doc.Metadata)
            {
                stripped.Metadata[entry.Key] = entry.Value;
            }

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                var carries = false;
                for (int t = 0; t < placed.Tags.Count; t++)
                {
                    carries |= string.Equals(placed.Tags[t], tag, StringComparison.Ordinal);
                }

                if (!carries)
                {
                    stripped.GeneratedObjects.Add(placed);
                }
            }

            return stripped;
        }

        /// <remarks>
        /// <para>
        /// <strong>Everything the thresholds say, of a furnished map, with the thresholds
        /// unchanged.</strong> Cover coverage is the metric at risk here and it is the one this asks
        /// about most directly: the furniture stands on ground the cover placer would otherwise have
        /// used, and the two tests above are the two halves of why that does not cost the map its
        /// coverage.
        /// </para>
        /// <para>
        /// Not a separate set of numbers for furnished maps. A threshold that had to be loosened to
        /// get a green run would mean the generator regressed rather than that the number was wrong,
        /// which is what <see cref="MapThresholds"/> says about itself.
        /// </para>
        /// </remarks>
        [Test]
        public void EverySeedProducesAFurnishedMapWorthPlaying()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var reports = new MapReport[AnalysisSeeds + 1];

            Parallel.For(1, AnalysisSeeds + 1, seed =>
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params((ulong)seed), catalog);
                reports[seed] = MapAnalyzer.Analyze(doc, catalog);
            });

            var broken = new List<string>();
            for (int seed = 1; seed <= AnalysisSeeds; seed++)
            {
                if (!reports[seed].IsPlayable)
                {
                    broken.Add($"seed {seed}: {reports[seed]}");
                }
            }

            if (broken.Count > 0)
            {
                Assert.Fail(
                    $"{broken.Count} of {AnalysisSeeds} furnished seeds produced an unplayable map:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, broken.GetRange(0, Math.Min(8, broken.Count))) +
                    Environment.NewLine + Environment.NewLine +
                    reports[int.Parse(
                        broken[0].Substring(5, broken[0].IndexOf(':') - 5),
                        CultureInfo.InvariantCulture)].Describe());
            }
        }

        // --- helpers ----------------------------------------------------------------------------

        /// <summary>How much cover a document's parameters asked its lanes for.</summary>
        static int Target(WorldDoc doc) => int.Parse(
            doc.Metadata[CoverPlacer.TargetKey], NumberStyles.Integer, CultureInfo.InvariantCulture);

        static List<PlacedObject> Furniture(WorldDoc doc)
        {
            var furniture = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (IsFurniture(doc.GeneratedObjects[i]))
                {
                    furniture.Add(doc.GeneratedObjects[i]);
                }
            }

            return furniture;
        }

        static bool IsFurniture(PlacedObject placed)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (string.Equals(
                        placed.Tags[i], RoadFurniture.FurnitureTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static string SegmentOf(PlacedObject placed)
        {
            int at = placed.StableId.LastIndexOf(
                RoadFurniture.FurnitureSegment, StringComparison.Ordinal);
            return at < 0
                ? string.Empty
                : placed.StableId.Substring(
                    RoadFurniture.IdPrefix.Length, at - RoadFurniture.IdPrefix.Length);
        }

        /// <summary>
        /// The points of a polyline where it bends tightly enough to be worth marking, worked out
        /// independently of the stage that marks them.
        /// </summary>
        /// <remarks>
        /// The depth the road runs off the straight line between the points
        /// <see cref="RoadFurniture.ApexWindow"/> either side of a place on it, swept a metre at a
        /// time, kept where it is a local maximum over the depth an arc of
        /// <see cref="RoadFurniture.ApexRadius"/> reaches off its own chord. Written out here rather
        /// than called, so that the test and the stage agreeing is a measurement rather than a
        /// tautology.
        /// </remarks>
        static List<Vec2> Apexes(RoadSegment segment)
        {
            var apexes = new List<Vec2>();
            IReadOnlyList<Vec2> points = segment.Points;

            var arc = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
            {
                arc[i] = arc[i - 1] + Vec2.Distance(points[i - 1], points[i]);
            }

            float length = arc[points.Count - 1];
            float threshold =
                RoadFurniture.ApexWindow * RoadFurniture.ApexWindow / (2f * RoadFurniture.ApexRadius);

            int samples = (int)MathF.Floor(length) + 1;
            if (samples < 3)
            {
                return apexes;
            }

            float previous = Depth(points, arc, length, 0f);
            float current = Depth(points, arc, length, 1f);

            for (int i = 1; i < samples - 1; i++)
            {
                float next = Depth(points, arc, length, i + 1);

                if (current >= threshold && current >= previous && current > next)
                {
                    apexes.Add(At(points, arc, i));
                }

                previous = current;
                current = next;
            }

            return apexes;
        }

        /// <summary>How far the polyline runs off its own chord over the window, at a distance along it.</summary>
        static float Depth(IReadOnlyList<Vec2> points, float[] arc, float length, float at)
        {
            float back = MathF.Max(0f, at - RoadFurniture.ApexWindow);
            float ahead = MathF.Min(length, at + RoadFurniture.ApexWindow);

            Vec2 from = At(points, arc, back);
            Vec2 chord = At(points, arc, ahead) - from;
            float span = chord.Length;
            if (!(span > 0f))
            {
                return 0f;
            }

            Vec2 offset = At(points, arc, at) - from;
            return MathF.Abs(offset.X * chord.Y - offset.Y * chord.X) / span;
        }

        /// <summary>Where a polyline is, a distance along it.</summary>
        static Vec2 At(IReadOnlyList<Vec2> points, float[] arc, float distance)
        {
            var piece = 0;
            for (int i = 1; i < points.Count - 1 && arc[i] <= distance; i++)
            {
                piece = i;
            }

            float span = arc[piece + 1] - arc[piece];
            return span > 0f
                ? points[piece] + (points[piece + 1] - points[piece]) * ((distance - arc[piece]) / span)
                : points[piece];
        }

        /// <summary>True if any point of the polyline falls inside the disc.</summary>
        static bool Touches(RoadSegment segment, Vec2 centre, float radius)
        {
            for (int i = 0; i < segment.Points.Count; i++)
            {
                if (Vec2.Distance(segment.Points[i], centre) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        static bool Same(RoadSegment a, RoadSegment b)
        {
            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                a.Class != b.Class ||
                a.Width != b.Width ||
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

        /// <summary>
        /// Recovers where along a carriageway each piece of its furniture was seated, and whether
        /// that recovery is exact.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A document stores a pose, not a distance along a road, so how far along its polyline a
        /// piece stands has to be recovered by projecting the pivot back onto it — the nearest point
        /// on the polyline, which is how anything measuring against a road here does it.
        /// </para>
        /// <para>
        /// <strong>The recovery is exact only where exactly one arm of the polyline can have put
        /// the piece there.</strong> A pivot is seated a known distance out from the centreline:
        /// half the carriageway, plus the thickest kerb the catalog holds, plus
        /// <see cref="RoadFurniture.Verge"/>, plus half the piece's own thickness across the run. So
        /// the arm it was placed on is an arm whose perpendicular foot is inside its own span and
        /// exactly that far away — and the recovery is trustworthy when one arm answers to that and
        /// nothing else does.
        /// </para>
        /// <para>
        /// Two arms answer to it at a corner: a piece seated on the outside of a bend stands the
        /// same distance from both of the arms that meet there, and which one the nearest-point
        /// search picks is decided by the last bit of a float. Those come back as inexact, and so do
        /// the pieces on the concave side of a bend, where the far arm comes <em>nearer</em> than
        /// the seat and the nearest point lands on the wrong arm outright — the same fold
        /// <see cref="RoadKerbs"/> documents about the last piece before a corner. Both are facts
        /// about measuring a curve from the outside rather than about the placement.
        /// </para>
        /// </remarks>
        static List<Seat> Seats(WorldDoc doc, Catalog catalog, RoadSegment segment)
        {
            var seats = new List<Seat>();
            float kerb = WallRun.ThickestSegment(
                WallRun.Tileable(catalog.Query(TagQuery.All(RoadKerbs.KerbTag))));

            List<PlacedObject> furniture = Furniture(doc);
            for (int i = 0; i < furniture.Count; i++)
            {
                PlacedObject placed = furniture[i];
                if (!string.Equals(SegmentOf(placed), segment.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                // Where the pivot was seated across the run, which the arm it was placed on has to
                // come back with.
                Rect2 footprint = catalog.Find(placed.LogicalId).Footprint;
                float across = MathF.Min(footprint.Width, footprint.Depth) * 0.5f;
                float seat = segment.Width * 0.5f + kerb + RoadFurniture.Verge + across;

                Vec2 at = placed.Pose.Position.Xz;
                float arc = 0f;
                float nearest = float.MaxValue;
                float along = 0f;
                int piece = 0;
                Vec2 tangent = new Vec2(1f, 0f);
                var carriers = 0;

                for (int p = 1; p < segment.Points.Count; p++)
                {
                    Vec2 from = segment.Points[p - 1];
                    Vec2 span = segment.Points[p] - from;
                    float length = span.Length;

                    if (length > 0f)
                    {
                        float raw = Vec2.Dot(at - from, span) / (length * length);
                        float t = raw < 0f ? 0f : raw > 1f ? 1f : raw;
                        float distance = Vec2.Distance(at, from + span * t);

                        if (distance < nearest)
                        {
                            nearest = distance;
                            if (carriers == 0)
                            {
                                // Only a fallback, for sorting the inexact ones into some order.
                                along = arc + t * length;
                                piece = p - 1;
                                tangent = span / length;
                            }
                        }

                        // An arm that could have seated this piece: the foot is inside its own span
                        // and stands at exactly the distance the stage seats a piece at. The
                        // measurement is read off that arm rather than off the nearest one, because
                        // on the concave side of a bend the nearest is the arm the piece was *not*
                        // placed on.
                        if (raw >= 0f && raw <= 1f && MathF.Abs(distance - seat) <= Slack)
                        {
                            if (carriers == 0)
                            {
                                along = arc + t * length;
                                piece = p - 1;
                                tangent = span / length;
                            }

                            carriers++;
                        }
                    }

                    arc += length;
                }

                seats.Add(new Seat(placed, at, along, piece, tangent, carriers == 1));
            }

            return seats;
        }

        /// <summary>Where one piece of furniture was seated along its carriageway.</summary>
        readonly struct Seat
        {
            public Seat(
                PlacedObject placed, Vec2 at, float along, int piece, Vec2 tangent, bool isExact)
            {
                Placed = placed;
                At = at;
                Along = along;
                Piece = piece;
                Tangent = tangent;
                IsExact = isExact;
            }

            /// <summary>The object itself.</summary>
            public PlacedObject Placed { get; }

            /// <summary>Its stable id, for a message.</summary>
            public string Id => Placed.StableId;

            /// <summary>Where its pivot is.</summary>
            public Vec2 At { get; }

            /// <summary>How far along the polyline that is, in metres.</summary>
            public float Along { get; }

            /// <summary>Which straight piece of the polyline the projection landed on.</summary>
            public int Piece { get; }

            /// <summary>Which way that piece runs.</summary>
            public Vec2 Tangent { get; }

            /// <summary>False when the projection folded onto the far arm of a bend.</summary>
            public bool IsExact { get; }
        }

        /// <summary>
        /// Reduces a document to one number, exactly as <c>RoadKerbTests.Digest</c> does.
        /// </summary>
        /// <remarks>
        /// Spelled out again rather than shared, for the reason given there: the three digest sets
        /// in this project are baselines against three different builds, and a helper all of them
        /// depended on would move every recorded number at once and so prove nothing about any of
        /// them.
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
