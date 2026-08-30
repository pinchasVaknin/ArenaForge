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
    /// The road feature held to its properties across a thousand seeds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MapValidationTests"/> for the roads, and the same argument: a procedural stage is
    /// not a function with a right answer to check, it is a family of a billion networks, and the
    /// only honest claim about it is a claim about properties that hold across the family. Every
    /// road property in the project is stated here, so there is one place to read what a network is
    /// promised to be and one sweep to pay for saying it.
    /// </para>
    /// <para>
    /// <strong>Two sweeps, because the properties do not all describe the same ground.</strong>
    /// Grading and portal heights are claims about relief — on a flat map the gradient limit never
    /// once rejects anything, and a rule that never rejects is indistinguishable in the output from
    /// one that was never asked — so those are measured over <see cref="Amplitude"/> metres of it.
    /// Playability is a claim against <see cref="MapThresholds"/>, whose every default was measured
    /// on the default map, so it is measured there too: holding a threshold to ground it was never
    /// derived on would be reading a harder map's numbers as a regression. The properties that hold
    /// on both are asserted on the relief sweep, which is the harder of the two.
    /// </para>
    /// <para>
    /// <strong>The relief sweep is laid over the fully furnished catalog</strong> — kerbing and
    /// street furniture as well as cover — because <see cref="CorridorsAreNotStoodIn"/> is only a
    /// claim about the stages that place after the roads if those stages have art to place. It
    /// costs about a quarter again in generation and buys the property over three stages instead of
    /// one. Nothing it adds reaches the network: a kerb and a bench are both
    /// <c>RoadNetwork.IsPlacedAfterTheRoads</c>, so the router cannot see either.
    /// </para>
    /// <para>
    /// A failure here is a generator bug, not a test bug. The two numbers this file carries —
    /// <see cref="MaxCorridorShare"/> and <see cref="DoorwayReach"/> — were set from the measured
    /// spread of these same sweeps with headroom on top, on the same terms
    /// <see cref="MapThresholds"/> was, and loosening either to get a green run would throw away
    /// the only thing this suite is for.
    /// </para>
    /// </remarks>
    public sealed class RoadValidationTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every map.</summary>
        const int Seeds = 1000;

        /// <summary>Seeds swept by determinism, which generates and serialises every map twice.</summary>
        const int RepeatedSeeds = 200;

        /// <summary>
        /// Seeds the braiding total is taken over.
        /// </summary>
        /// <remarks>
        /// Two hundred, because braiding is a claim about a total rather than about a map and the
        /// total converges long before the sweep ends: the running ratio over seeds 1..1000 sits
        /// between 0.672 and 0.677 at every hundred-seed mark, so the first two hundred say what all
        /// thousand say.
        /// </remarks>
        const int BraidedSeeds = 200;

        /// <summary>
        /// Peak-to-trough ground the relief sweep is laid over, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Ten, and it was four because of a bug rather than because of the ground.</strong>
        /// The cap used to read: four is the largest value at which the whole sweep still comes out
        /// with both spawns on the same piece of ground, because at six a handful of seeds wall a
        /// spawn off behind a bank no road may be graded up. That was true, and the reason no road
        /// could be graded up it was <see cref="ArenaParams.MaxRoadGradient"/> at a quarter — which
        /// does not slow a route over rough ground, it shuts the ground to the router outright. The
        /// sweep was calibrated around the defect.
        /// </para>
        /// <para>
        /// With the limit at six tenths the ground is open again, and the sweep is laid over relief
        /// that actually exercises what it is here to measure. At four the limit now refuses almost
        /// nothing — the test below says so in its own words — so the grading properties would be
        /// asserted over ground that never needs grading.
        /// </para>
        /// <para>
        /// Ten rather than twelve because it is the value that keeps the braiding total clear of its
        /// threshold with room to spare: over seeds 1..200, braiding runs 0.725 at four metres,
        /// 0.694 at eight, 0.674 at ten and 0.638 at twelve. That fall is worth understanding rather
        /// than exploiting — a confined router shares corridors because it has nowhere else to go,
        /// so the metric measures how boxed in the routing is as much as it measures the cost decay
        /// it was written for.
        /// </para>
        /// </remarks>
        const float Amplitude = 10f;

        /// <summary>
        /// A road density that lays a real network. One is the baseline redundancy the parameter
        /// multiplies, and on a sixty-metre map the arteries are most of the road whatever it is.
        /// </summary>
        const float Density = 1f;

        /// <summary>
        /// What the finished network may cover, against the same routes laid one at a time on
        /// untouched ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Seventy per cent, measured across the sweep rather than seed by seed. Seeds 1..200 come
        /// to 0.677 with the cost decay in place and about 0.80 with it removed, which is what this
        /// number is set to catch: the two distributions overlap map by map — a map with little to
        /// braid saves little however the decay is tuned — and separate cleanly in the total.
        /// </para>
        /// <para>
        /// It is the one property nothing else here would catch. A network whose branches never
        /// merged would still join every doorway, still reach both spawns, still be one piece and
        /// still keep off every wall; it would simply be a spider, with a road of its own from each
        /// door running parallel to and a metre from its neighbour's.
        /// </para>
        /// </remarks>
        const float MaxCorridorShare = 0.70f;

        /// <summary>
        /// How far a doorway's threshold may be from the nearest reserved corridor, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One carriageway of the road that serves a door, which is
        /// <see cref="ArenaParams.PathWidth"/>. A branch stands its portal one path width outside
        /// the threshold and is reserved half a carriageway either side of that, so the reservation
        /// arrives half a path width from the door and the rest is headroom: over the default map's
        /// six thousand doorways not one is further than 1.0 m, against this bound of 2.0.
        /// </para>
        /// <para>
        /// <strong>Stated on the default map, and that is the whole of what it claims.</strong> On
        /// four metres of relief the same measure runs to 4.19 m, because a portal is snapped to the
        /// nearest cell a road may actually use and ground too steep to grade pushes that cell
        /// outwards — 226 of 6000 doorways end up further than this bound, none of them for want of
        /// a branch. That is the ground refusing a road, not the stage failing to lay one, and it is
        /// recorded here rather than rounded into the number.
        /// </para>
        /// </remarks>
        const float DoorwayReach = 2f;

        /// <summary>Slack for a gradient compared against a limit it was built to meet exactly.</summary>
        const float GradientTolerance = 1e-3f;

        /// <summary>
        /// Most of the road laid over the whole sweep that may cross ground steeper than the limit.
        /// </summary>
        /// <remarks>
        /// Half of one per cent, against 0.137% measured over seeds 1..1000 — 396 m of 289,798.
        /// The headroom is three and a half times because what this guards against is not a drift in the
        /// figure but a change of kind: a router that started running <em>along</em> a hillside
        /// rather than stepping over one moves this by an order of magnitude, and a router that
        /// went back to refusing the ground moves it to zero, which the last check in the same property
        /// catches from the other side.
        /// </remarks>
        const float OverLimitShare = 0.005f;

        /// <summary>Most of any one map's road that may cross ground steeper than the limit.</summary>
        /// <remarks>
        /// Five per cent, against a worst seed of 2.08%. A per-map bound as well as a total,
        /// because a thousand-seed average hides one map made entirely of switchbacks.
        /// </remarks>
        const float OverLimitSeedShare = 0.05f;

        /// <summary>How far a path may arrive from the sill of the door it serves, in metres.</summary>
        /// <remarks>
        /// A centimetre. The two numbers are meant to be the same float — the profile's end is
        /// pinned to the structure's own recorded height and swept from there — so this is slack for
        /// a comparison rather than a tolerance anything is allowed to use up. A path that arrived a
        /// centimetre low would be a step into a building.
        /// </remarks>
        const float SillSlack = 0.01f;

        /// <summary>How many of the worst seeds a failure message names for inspection by hand.</summary>
        const int WorstSeedsToReport = 8;

        /// <summary>Relief ground, fully furnished — the sweep most properties are measured over.</summary>
        RoadMap[] _relief;

        /// <summary>The default map with roads on it, which is the map the thresholds describe.</summary>
        RoadMap[] _default;

        /// <summary>The analysis of that same default map, seed for seed.</summary>
        MapReport[] _reports;

        static ArenaParams Params(ulong seed, float amplitude) => new ArenaParams
        {
            Seed = seed,
            RoadDensity = Density,
            TerrainAmplitude = amplitude,
        };

        [OneTimeSetUp]
        public void LayEveryRoad()
        {
            Catalog furnished = TestWorlds.FurnitureCatalog();
            Catalog sample = TestWorlds.SampleCatalog();

            _relief = new RoadMap[Seeds + 1];
            _default = new RoadMap[Seeds + 1];
            _reports = new MapReport[Seeds + 1];

            // The catalog is immutable once constructed and every stage keeps its state in locals,
            // so the seeds are genuinely independent — the argument MapValidationTests makes.
            Parallel.For(1, Seeds + 1, seed =>
            {
                _relief[seed] = new RoadMap(
                    Params((ulong)seed, Amplitude), furnished, paintCarriageways: true);

                _default[seed] = new RoadMap(
                    Params((ulong)seed, 0f), sample, paintCarriageways: false);

                _reports[seed] = MapAnalyzer.Analyze(_default[seed].Document, sample);
            });
        }

        // --- everything the sweeps can be asked at once ------------------------------------------

        /// <summary>
        /// Measures both sweeps once and asserts every property that has to hold of all of them.
        /// </summary>
        /// <remarks>
        /// One test rather than ten, for the reason <see cref="MapValidationTests"/> gives: the
        /// sweep is what costs, and running it ten times over to assert ten things about the same
        /// maps would buy nothing but more minutes. Every property is still checked for every seed
        /// and all of them are reported together, so one run says everything that is wrong rather
        /// than only the first thing. (Collected by hand rather than with <c>Assert.Multiple</c>,
        /// which the NUnit version Unity ships does not have.)
        /// </remarks>
        [Test]
        public void EverySeedLaysARoadNetworkWorthDriving()
        {
            var broken = new List<string>();

            NoCarriagewayCrossesAStructure(broken);
            EveryDoorwayHasARoadToIt(broken);
            TheNetworkIsOnePieceReachingBothSpawns(broken);
            NoGradedProfileIsSteeperThanTheMapAllows(broken);
            EveryPortalArrivesLevelWithItsDoor(broken);
            CorridorsAreNotStoodIn(broken);
            EverySeedWithRoadsIsStillWorthPlaying(broken);
            TheBranchesMerged(broken);

            // The two properties that came with the sweep rather than out of the list above, and
            // that would have lost their thousand maps if they had been left where they were.
            EveryMetreOfCarriagewayIsReserved(broken);
            RoadOverGroundTooSteepIsARareSingleStep(broken);

            if (broken.Count > 0)
            {
                Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, broken));
            }
        }

        // --- 1. the same seed is the same map ------------------------------------------------------

        /// <summary>
        /// Two generations of one seed serialise to the same bytes, with the roads turned on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The claim the override system rests on, stated at the only level that settles it. A
        /// network is never serialised — it is rebuilt on demand from the finished document — so
        /// what has to be identical is not the network but everything laid along it: every kerb,
        /// every lamp post and every crate that was placed around a corridor. A router that moved a
        /// carriageway by an ulp would move the run of kerbing that edges it, and this is where that
        /// shows.
        /// </para>
        /// <para>
        /// Its own test rather than a clause of the sweep above, because it is the one property that
        /// cannot read a cached map: it has to generate all two hundred a second time. Compared as
        /// text rather than as a digest of the values, because <em>byte-identical</em> is the actual
        /// promise the file format makes, and the two runs being compared are the same process on
        /// the same runtime, where the shortest-round-trip rendering of a float cannot differ. (A
        /// digest recorded on one runtime and checked on another is a different problem, and
        /// <c>RoadPipelineTests</c> is where it is handled.)
        /// </para>
        /// </remarks>
        [Test]
        public void TwoGenerationsOfOneSeedSerialiseToTheSameBytes()
        {
            Catalog catalog = TestWorlds.FurnitureCatalog();
            var differ = new string[RepeatedSeeds + 1];

            Parallel.For(1, RepeatedSeeds + 1, seed =>
            {
                ArenaParams parameters = Params((ulong)seed, Amplitude);

                string first = ArenaJson.SerializeWorld(
                    ArenaLayoutGenerator.Generate(parameters, catalog));
                string second = ArenaJson.SerializeWorld(
                    ArenaLayoutGenerator.Generate(parameters, catalog));

                if (!string.Equals(first, second, StringComparison.Ordinal))
                {
                    differ[seed] = $"seed {seed}: {Divergence(first, second)}";
                }
            });

            var moved = new List<string>();
            for (int seed = 1; seed <= RepeatedSeeds; seed++)
            {
                if (differ[seed] != null)
                {
                    moved.Add(differ[seed]);
                }
            }

            Assert.That(moved, Is.Empty,
                "the same seed and parameters generated two different maps");
        }

        /// <summary>Where two documents first stop agreeing, with enough either side to read it.</summary>
        static string Divergence(string first, string second)
        {
            int at = 0;
            while (at < first.Length && at < second.Length && first[at] == second[at])
            {
                at++;
            }

            return $"diverges at character {at}: " +
                   $"'{Window(first, at)}' became '{Window(second, at)}'";
        }

        static string Window(string text, int at)
        {
            int from = Math.Max(0, at - 30);
            return text.Substring(from, Math.Min(70, text.Length - from)).Replace('\n', ' ');
        }

        // --- 2. and it is a network of roads, not of walls ------------------------------------------

        /// <remarks>
        /// <para>
        /// The hard guarantee. A road drawn through a building is not a road that plays badly, it is
        /// geometry through geometry, and it is the one thing the smoothing could introduce after
        /// the routing had avoided it — a corner cut across the inside of a corner of a wall reaches
        /// ground the routed line never touched. Tested against the footprints the catalog gives
        /// rather than the pads in the metadata, which are what the router was told to avoid: a test
        /// that measured the router's own rectangles could not tell a footprint from a mistake about
        /// one.
        /// </para>
        /// <para>
        /// <strong>Of the smoothed centreline, which is the line the guarantee is about.</strong>
        /// Neither of the other two readings of "the road" is clear of a footprint and both are
        /// meant not to be. The reserved corridors are boxes drawn round diagonal runs and clip a
        /// structure on 414 of 3000 across this sweep, by up to half a metre — that conservatism is
        /// what buys cover a rectangle it can be judged against, and
        /// <see cref="CorridorsAreNotStoodIn"/> is the property it exists for. The swept carriageway
        /// reaches a footprint on 392 seeds in a thousand, again by up to exactly half a metre,
        /// because the router enforces its clearance on the cells it shuts rather than on the strip
        /// it lays: a pad is shut with a metre of apron round the art, cells are a metre, and the
        /// nearest cell centre a route may use sits half a cell off the pad. What is guaranteed is
        /// that no vehicle drives through a wall, and that is a claim about the line.
        /// </para>
        /// </remarks>
        void NoCarriagewayCrossesAStructure(List<string> broken)
        {
            var offences = new List<Offence>();
            var tested = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];

                for (int s = 0; s < map.Network.Segments.Count; s++)
                {
                    RoadSegment segment = map.Network.Segments[s];
                    IReadOnlyList<Vec2> points = segment.Points;

                    for (int i = 1; i < points.Count; i++)
                    {
                        for (int f = 0; f < map.Footprints.Count; f++)
                        {
                            tested++;
                            if (Box(map.Footprints[f]).Blocks(points[i - 1], points[i]))
                            {
                                offences.Add(new Offence(
                                    seed, 1f,
                                    $"{segment.Id} runs through {map.Footprints[f]}"));
                            }
                        }
                    }
                }
            }

            Report(broken, "a carriageway runs through a structure", offences);
            RequireWork(broken, tested, 0, "no road was ever tested against a structure");
        }

        // --- 3. and it goes where a network has to go -----------------------------------------------

        /// <remarks>
        /// <para>
        /// Every door on the map has a road to it. A doorway served by nothing is the failure this
        /// stage is most likely to have and least likely to show: the network still comes out, still
        /// reaches the spawns, and one building on one seed is simply not served.
        /// </para>
        /// <para>
        /// <strong>Of the doorways the structures declared, not of the portals the network chose to
        /// make.</strong> Those are different claims and only the first is the one anybody wants: a
        /// network that quietly dropped a portal would join every portal it had and leave a door
        /// with nothing in front of it. Measured against the reserved corridors rather than the
        /// centreline, because a road four metres wide reaches a threshold its centre stops two
        /// metres short of. See <see cref="DoorwayReach"/> for the bound and for what the same
        /// measure does on ground a road cannot climb.
        /// </para>
        /// </remarks>
        void EveryDoorwayHasARoadToIt(List<string> broken)
        {
            var offences = new List<Offence>();
            var doorways = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _default[seed];
                IReadOnlyList<Rect2> corridors = map.Network.Corridors;

                for (int d = 0; d < map.Doorways.Count; d++)
                {
                    doorways++;
                    Rect2 doorway = map.Doorways[d];

                    float nearest = float.MaxValue;
                    for (int i = 0; i < corridors.Count; i++)
                    {
                        nearest = MathF.Min(nearest, Rect2.Distance(doorway, corridors[i]));
                    }

                    if (nearest > DoorwayReach)
                    {
                        offences.Add(new Offence(
                            seed, nearest,
                            $"the doorway at {doorway} is {nearest:0.###} m from the nearest " +
                            $"corridor, which is over {DoorwayReach:0.###} m"));
                    }
                }
            }

            Report(broken, "a doorway has no road to it", offences);
            RequireWork(broken, doorways, Seeds, "the sweep declared almost no doorways to reach");
        }

        /// <remarks>
        /// <para>
        /// One piece, and it reaches both ends of the map. Two of the three claims a network makes
        /// are here: that you can get from any of it to any other part of it, and that what it
        /// connects includes the two places players start.
        /// </para>
        /// <para>
        /// Flood filled over the carriageways rather than walked over the junctions, because where
        /// two roads meet is where their ground overlaps and not an entry in a table. A network that
        /// was connected in a table and in two pieces on the ground would pass the other test and
        /// fail this one, which is the right way round.
        /// </para>
        /// </remarks>
        void TheNetworkIsOnePieceReachingBothSpawns(List<string> broken)
        {
            var offences = new List<Offence>();

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];
                bool[] reached = map.Carriageways.Reachable();

                if (!map.Carriageways.AllReached(reached))
                {
                    offences.Add(new Offence(seed, 1f, "the roads are laid in more than one piece"));
                    continue;
                }

                if (!map.Carriageways.AnyReached(map.Layout.SpawnAreaA, reached))
                {
                    offences.Add(new Offence(seed, 1f, "no road reaches spawn A"));
                }

                if (!map.Carriageways.AnyReached(map.Layout.SpawnAreaB, reached))
                {
                    offences.Add(new Offence(seed, 1f, "no road reaches spawn B"));
                }
            }

            Report(broken, "the network is not one piece reaching both spawns", offences);
        }

        // --- 4. and the ground under it is a road a vehicle could take ------------------------------

        /// <remarks>
        /// <para>
        /// The whole point of grading. A road laid across ground it cannot climb is a road nothing
        /// can drive up, and the router's own gradient rule does not settle it: that rule is about
        /// the ground as it was, and what a player walks on is the ground as it ends up. Measured
        /// over the profile rather than over the plan, because the profile is what the corridor is
        /// graded to — see <see cref="RoadProfile"/>.
        /// </para>
        /// <para>
        /// Every segment of it, and there are three times as many as the plan has: the profile is
        /// resampled at the placement grid's cell, so this is the claim at the resolution the ground
        /// is decided at rather than at the resolution the road is drawn at.
        /// </para>
        /// </remarks>
        void NoGradedProfileIsSteeperThanTheMapAllows(List<string> broken)
        {
            var offences = new List<Offence>();
            var tested = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];
                float limit = map.Parameters.MaxRoadGradient;

                for (int s = 0; s < map.Network.Segments.Count; s++)
                {
                    RoadSegment segment = map.Network.Segments[s];
                    RoadProfile profile = segment.Profile;

                    for (int i = 1; i < profile.Count; i++)
                    {
                        float run = Vec2.Distance(profile.Points[i - 1], profile.Points[i]);
                        if (!(run > 0f))
                        {
                            continue;
                        }

                        tested++;
                        float rise = MathF.Abs(profile.Heights[i] - profile.Heights[i - 1]);
                        float gradient = rise / run;

                        if (gradient > limit + GradientTolerance)
                        {
                            offences.Add(new Offence(
                                seed, gradient,
                                $"{segment.Id} is graded at {gradient:0.####} over {run:0.###} m " +
                                $"at point {i}, against a limit of {limit:0.###}"));
                            break;
                        }
                    }
                }
            }

            Report(broken, "a graded profile is steeper than the map allows", offences);
            RequireWork(broken, tested, Seeds * 100, "the sweep graded almost no road to measure");
        }

        /// <remarks>
        /// <para>
        /// <strong>This was a hard property and is now a bounded one.</strong> The limit stopped
        /// being a wall: ground steeper than <see cref="ArenaParams.MaxRoadGradient"/> costs
        /// <c>RoadNetwork.ClimbDetour</c> cells to cross rather than being shut to the router, so a
        /// route climbs a bank when there is no way round one and a spawn behind a ridge is no
        /// longer cut off from the map. What the road is still <em>held</em> to is the graded
        /// profile above — the surface anybody drives on is never steeper than the limit. This is
        /// about the ground underneath it, which the grading is there to cut.
        /// </para>
        /// <para>
        /// <strong>A crossing is one step.</strong> The smoothing still tests every line it
        /// straightens or rounds, so it will not lay a run <em>along</em> a bank — an over-limit
        /// stretch is never longer than the one cell the route steps over. That is the claim worth
        /// keeping from the old property, and it is the one that says the smoothing is still
        /// testing what it emits rather than a superset of it.
        /// </para>
        /// <para>
        /// <strong>And there is very little of it.</strong> Over seeds 1..1000, 396 m of 289,798 —
        /// 0.137% — on 210 seeds, no one of them over 2.08% of its own road. See
        /// <see cref="OverLimitShare"/> and <see cref="OverLimitSeedShare"/> for what those became.
        /// </para>
        /// <para>
        /// <strong>Over the segment, at the resolution the map has.</strong> Sampled finer than the
        /// grid the same roads measure half again as steep, and that is a fact about three octaves
        /// of value noise rather than about the road: a limit applied per cell is a claim about
        /// cells, and a map whose ground is a function has no finer truth to be held to.
        /// </para>
        /// </remarks>
        void RoadOverGroundTooSteepIsARareSingleStep(List<string> broken)
        {
            var offences = new List<Offence>();
            var vetoed = 0;
            var laid = 0f;
            var over = 0f;
            var worstShare = 0f;
            var worstSeed = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];
                float limit = map.Parameters.MaxRoadGradient;
                float step = map.Layout.Grid.CellSize;

                if (map.TooSteepCells > 0)
                {
                    vetoed++;
                }

                var seedLaid = 0f;
                var seedOver = 0f;

                for (int s = 0; s < map.Network.Segments.Count; s++)
                {
                    RoadSegment segment = map.Network.Segments[s];
                    IReadOnlyList<Vec2> points = segment.Points;

                    for (int i = 1; i < points.Count; i++)
                    {
                        float run = Vec2.Distance(points[i - 1], points[i]);
                        if (!(run > 0f))
                        {
                            continue;
                        }

                        seedLaid += run;

                        float rise = MathF.Abs(
                            map.Terrain.HeightAt(points[i]) - map.Terrain.HeightAt(points[i - 1]));
                        float gradient = rise / run;

                        if (gradient <= limit + GradientTolerance)
                        {
                            continue;
                        }

                        seedOver += run;

                        if (run > step + GradientTolerance)
                        {
                            offences.Add(new Offence(
                                seed, run,
                                $"{segment.Id} runs {run:0.###} m along ground at " +
                                $"{gradient:0.####}, which is further than the one cell a route " +
                                $"may step over it"));
                        }
                    }
                }

                laid += seedLaid;
                over += seedOver;

                if (seedLaid > 0f && seedOver / seedLaid > worstShare)
                {
                    worstShare = seedOver / seedLaid;
                    worstSeed = seed;
                }
            }

            Report(broken, "a road runs along ground steeper than the map allows", offences);

            float share = laid > 0f ? over / laid : 0f;
            if (share > OverLimitShare)
            {
                broken.Add(
                    $"{share:0.000%} of the road laid crosses ground steeper than the limit " +
                    $"({over:0.#} m of {laid:0.#} m), against {OverLimitShare:0.0%} allowed");
            }

            if (worstShare > OverLimitSeedShare)
            {
                broken.Add(
                    $"seed {worstSeed} lays {worstShare:0.00%} of its road over ground steeper " +
                    $"than the limit, against {OverLimitSeedShare:0.0%} allowed");
            }

            // The relief sweep exists so the gradient limit is doing work on every map in it. A
            // sweep laid over ground gentle enough to charge nothing would pass both gradient
            // properties without ever evaluating them.
            if (vetoed != Seeds)
            {
                broken.Add(
                    $"the gradient limit charges nothing on {Seeds - vetoed} of {Seeds} seeds, so " +
                    "the relief sweep is not laid over ground steep enough to test grading");
            }
        }

        /// <remarks>
        /// <para>
        /// A path arrives level with the door it was built to reach. It is the one place the ground
        /// under a road is not free to follow the ground: a building stands on a pad cut to its own
        /// height, and a road that met that pad a step below it would put a kerb across every
        /// doorway on the map.
        /// </para>
        /// <para>
        /// Asserted of the junction and of the end of every profile that reaches it, because the two
        /// are different claims. The first is that the height was solved from the structure's own
        /// recorded pad rather than sampled off the ground beside it; the second is that the sweep
        /// that clamps the gradient did not then move the pin it was given.
        /// </para>
        /// </remarks>
        void EveryPortalArrivesLevelWithItsDoor(List<string> broken)
        {
            var offences = new List<Offence>();
            var portals = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];

                for (int j = 0; j < map.Network.Junctions.Count; j++)
                {
                    RoadJunction junction = map.Network.Junctions[j];
                    if (junction.Kind != RoadJunctionKind.Portal)
                    {
                        continue;
                    }

                    portals++;
                    float sill = map.SillNearest(junction.Position);
                    float step = MathF.Abs(junction.Height - sill);

                    if (step > SillSlack)
                    {
                        offences.Add(new Offence(
                            seed, step,
                            $"{junction.Id} is held at {junction.Height} against a door sill of " +
                            $"{sill}, a step of {step:0.####} m"));
                        continue;
                    }

                    for (int s = 0; s < map.Network.Segments.Count; s++)
                    {
                        RoadProfile profile = map.Network.Segments[s].Profile;
                        if (profile.Count == 0)
                        {
                            continue;
                        }

                        int last = profile.Count - 1;

                        if (Ends(profile.Points[0], junction.Position) &&
                            MathF.Abs(profile.Heights[0] - sill) > SillSlack)
                        {
                            offences.Add(new Offence(
                                seed, MathF.Abs(profile.Heights[0] - sill),
                                $"{map.Network.Segments[s].Id} starts at {profile.Heights[0]} " +
                                $"against a sill of {sill}"));
                        }

                        if (Ends(profile.Points[last], junction.Position) &&
                            MathF.Abs(profile.Heights[last] - sill) > SillSlack)
                        {
                            offences.Add(new Offence(
                                seed, MathF.Abs(profile.Heights[last] - sill),
                                $"{map.Network.Segments[s].Id} ends at {profile.Heights[last]} " +
                                $"against a sill of {sill}"));
                        }
                    }
                }
            }

            Report(broken, "a portal is not level with the door it serves", offences);
            RequireWork(broken, portals, Seeds, "the sweep produced almost no portals");
        }

        /// <summary>True if a profile's end is the same place as a junction.</summary>
        static bool Ends(Vec2 end, Vec2 junction) => Vec2.DistanceSquared(end, junction) <= 1e-6f;

        // --- 5. and the ground it reserves is the ground it covers, and nobody stands on it ---------

        /// <remarks>
        /// <para>
        /// A reservation that is not the road is worse than no reservation: everything downstream
        /// places against the rectangles rather than against the carriageway, so a rectangle that
        /// misses a stretch of road is a stretch of road with crates on it. Walked along every
        /// centreline at a quarter of a metre, and each sample is asked to be inside some corridor.
        /// </para>
        /// <para>
        /// The centreline rather than the whole strip, because a rectangle grown by half the
        /// carriageway covers the strip either side of any centre it contains. What this is looking
        /// for is a gap between two pieces of the same segment — the arithmetic that cuts a diagonal
        /// run into pieces getting one of its joints wrong — which shows up on the centreline first.
        /// </para>
        /// </remarks>
        void EveryMetreOfCarriagewayIsReserved(List<string> broken)
        {
            var offences = new List<Offence>();
            var sampled = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];

                for (int s = 0; s < map.Network.Segments.Count; s++)
                {
                    RoadSegment segment = map.Network.Segments[s];
                    IReadOnlyList<Vec2> points = segment.Points;

                    for (int i = 1; i < points.Count; i++)
                    {
                        int steps = (int)MathF.Ceiling(
                            Vec2.Distance(points[i - 1], points[i]) / 0.25f);

                        for (int step = 0; step <= steps; step++)
                        {
                            Vec2 at = points[i - 1] +
                                      (points[i] - points[i - 1]) * (step / (float)MathF.Max(1, steps));
                            sampled++;

                            if (!Reserved(map.Network.Corridors, at))
                            {
                                offences.Add(new Offence(
                                    seed, 1f,
                                    $"{segment.Id} runs over unreserved ground at {at}"));
                                break;
                            }
                        }
                    }
                }
            }

            Report(broken, "a carriageway runs over ground no corridor reserves", offences);
            RequireWork(broken, sampled, 0, "no carriageway was ever sampled");
        }

        /// <remarks>
        /// <para>
        /// The reason the road stage moved in front of the cover stage. A crate in the carriageway
        /// is the failure the whole reordering exists to rule out, and it is one the old order
        /// produced on a large share of seeds because the road was computed after the crates were
        /// already down. Asserted over the cover the placer scattered <em>and</em> the street
        /// furniture the verge stage stood, which are the two stages judged against the reservation.
        /// </para>
        /// <para>
        /// <strong>What the corridor is a reservation against is what comes after it.</strong> Three
        /// kinds of thing on a finished map overlap a corridor, all three of them deliberately. The
        /// two spawn markers are inside one on every seed by construction — the network is built to
        /// reach the spawns, and <see cref="RoadJunctionKind.Terminal"/> is the centre of one.
        /// Structures are clipped by the corner of a rectangle round a diagonal run on 414 of 3000
        /// across this sweep, by at most half a metre, which
        /// <see cref="NoCarriagewayCrossesAStructure"/> holds the road itself clear of. And kerbing
        /// is flush against the carriageway it edges by definition — 80% of it overlaps, by up to
        /// 2.2 m for the box round an oblique piece — because a kerb is the road rather than
        /// something standing on it. All three are placed before or by the roads; none of them was
        /// ever offered the reservation to keep off.
        /// </para>
        /// </remarks>
        void CorridorsAreNotStoodIn(List<string> broken)
        {
            var offences = new List<Offence>();
            var tested = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                RoadMap map = _relief[seed];
                IReadOnlyList<Rect2> corridors = map.Network.Corridors;

                for (int p = 0; p < map.PlacedAfterTheRoads.Count; p++)
                {
                    Rect2 footprint = map.PlacedAfterTheRoads[p].Footprint;
                    tested++;

                    for (int i = 0; i < corridors.Count; i++)
                    {
                        if (!footprint.Overlaps(corridors[i]))
                        {
                            continue;
                        }

                        offences.Add(new Offence(
                            seed, 1f,
                            $"{map.PlacedAfterTheRoads[p].Id} at {footprint} stands in " +
                            $"{corridors[i]}"));
                        break;
                    }
                }
            }

            Report(broken, "something placed after the roads stands in a corridor", offences);
            RequireWork(
                broken, tested, Seeds * 10,
                "the sweep placed almost nothing to test against the reservation");
        }

        // --- 6. and the branches merged -------------------------------------------------------------

        /// <remarks>
        /// See <see cref="MaxCorridorShare"/>. The totals rather than a bound per seed, because how
        /// much braiding can save is a fact about how much a given map had to braid, and the map
        /// with one door on each side of it has almost nothing.
        /// </remarks>
        void TheBranchesMerged(List<string> broken)
        {
            double corridor = 0d;
            double unbraided = 0d;

            for (int seed = 1; seed <= BraidedSeeds; seed++)
            {
                corridor += _relief[seed].Network.CorridorLength;
                unbraided += _relief[seed].Network.UnbraidedLength;
            }

            if (!(unbraided > 0d))
            {
                broken.Add($"the first {BraidedSeeds} seeds laid no road at all");
                return;
            }

            double share = corridor / unbraided;
            if (share >= MaxCorridorShare)
            {
                broken.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "the branches are not merging: over {0} seeds the network covers {1:0} m " +
                        "where the same routes laid separately cover {2:0} m, a share of {3:0.####} " +
                        "against a limit of {4:0.###}",
                        BraidedSeeds, corridor, unbraided, share, MaxCorridorShare));
            }
        }

        // --- 7. and a map with a road across it is still a map --------------------------------------

        /// <remarks>
        /// <para>
        /// <see cref="MapValidationTests"/> over again with the roads turned on, against the same
        /// <see cref="MapThresholds"/> and on the same ground those thresholds were measured over.
        /// It is the claim the whole feature stands on: a map with a road network across it is not a
        /// worse map, it is a map with a road network across it.
        /// </para>
        /// <para>
        /// <strong>Cover coverage is the metric that pays for the road, and it is worth knowing by
        /// how much.</strong> A carriageway is ground no prop may stand in and ground the metric
        /// still counts as floor that wants cover within reach, and on the default map the network
        /// covers about a fifth of the playfield. Over these thousand seeds the roadless map runs
        /// 0.620 to 0.776 with a mean of 0.712 and this one runs 0.602 to 0.771 with a mean of
        /// 0.700, so the worst seed of a thousand clears the threshold by 0.002 where the roadless
        /// map clears it by 0.020. That margin is thin and it is not an accident of tuning: with no
        /// roadside preference at all the same sweep runs down to 0.567 and ten seeds fail, and it
        /// is the cover standing along the roads that pays the difference back.
        /// </para>
        /// <para>
        /// <strong>Which is also why this one property is measured on the default map rather than on
        /// the relief sweep.</strong> At <see cref="Amplitude"/> metres of ground the same measure
        /// runs down to 0.573 and two seeds in a thousand — 869 and 921 — fall under the threshold,
        /// because relief costs walkable floor and the cover budget is counted off what is left.
        /// That is a fact about steep ground rather than about roads, and holding a threshold
        /// derived on the default map to a map it never described would report it as a regression.
        /// </para>
        /// </remarks>
        void EverySeedWithRoadsIsStillWorthPlaying(List<string> broken)
        {
            var thresholds = new MapThresholds();

            Check(
                broken, "spawn B is reachable from spawn A",
                r => r.Connectivity.SpawnsConnected ? 1f : 0f, MetricBound.AtLeast, 1f);

            Check(
                broken, "every declared doorway is reachable from spawn A",
                r => r.Connectivity.DoorwayReachableFraction, MetricBound.AtLeast,
                thresholds.MinDoorwayReachableFraction);

            Check(
                broken, "the walkable floor is reachable from spawn A",
                r => r.Connectivity.ReachableFraction, MetricBound.AtLeast,
                thresholds.MinReachableFraction);

            Check(
                broken, "the two spawns are overlooked about equally",
                r => r.ExposureAsymmetry, MetricBound.AtMost, thresholds.MaxExposureAsymmetry);

            Check(
                broken, "the walkable floor is within reach of cover",
                r => r.CoverCoverage, MetricBound.AtLeast, thresholds.MinCoverCoverage);

            // Sweeps up the two metrics the five above do not name — spawn separation and the
            // longest open sightline — and is what the editor's panel shows.
            var message = new StringBuilder();
            var count = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                if (_reports[seed].IsPlayable)
                {
                    continue;
                }

                count++;
                if (count <= WorstSeedsToReport)
                {
                    message.AppendLine().Append("  seed ").Append(seed).Append(": ")
                        .Append(_reports[seed].ToString());
                }
            }

            if (count > 0)
            {
                broken.Add(
                    $"{count} of {Seeds} seeds with roads produced an unplayable map:{message}");
            }
        }

        /// <summary>
        /// Checks a metric of every report against a threshold, describing the worst offenders when
        /// it does not hold.
        /// </summary>
        void Check(
            List<string> broken,
            string property,
            Func<MapReport, float> measure,
            MetricBound bound,
            float threshold)
        {
            var offenders = new List<int>();
            for (int seed = 1; seed <= Seeds; seed++)
            {
                float value = measure(_reports[seed]);
                bool ok = bound == MetricBound.AtLeast ? value >= threshold : value <= threshold;
                if (!ok)
                {
                    offenders.Add(seed);
                }
            }

            if (offenders.Count == 0)
            {
                return;
            }

            // Worst first, so the seeds worth loading into the editor are the ones printed.
            offenders.Sort((a, b) => bound == MetricBound.AtLeast
                ? measure(_reports[a]).CompareTo(measure(_reports[b]))
                : measure(_reports[b]).CompareTo(measure(_reports[a])));

            var message = new StringBuilder();
            message.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0} on {1} of {2} seeds with roads (needs {3} {4:0.###}). Worst seeds:",
                property, offenders.Count, Seeds,
                bound == MetricBound.AtLeast ? "at least" : "at most", threshold);

            for (int i = 0; i < offenders.Count && i < WorstSeedsToReport; i++)
            {
                message.AppendLine().AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  seed {0}: {1:0.####}", offenders[i], measure(_reports[offenders[i]]));
            }

            message.AppendLine().AppendLine().Append(_reports[offenders[0]].Describe());

            broken.Add(message.ToString());
        }

        // --- reporting ------------------------------------------------------------------------------

        /// <summary>One map failing one property, and how badly.</summary>
        /// <remarks>
        /// The magnitude is what sorts a failure message, so the seeds printed are the ones worth
        /// loading into the editor. A property with nothing to measure — a road through a wall is
        /// through it or it is not — carries one, which leaves the offences in seed order.
        /// </remarks>
        readonly struct Offence
        {
            public Offence(int seed, float magnitude, string what)
            {
                Seed = seed;
                Magnitude = magnitude;
                What = what;
            }

            public int Seed { get; }

            public float Magnitude { get; }

            public string What { get; }
        }

        /// <summary>Adds a description of a broken property, worst seeds first, or nothing.</summary>
        static void Report(List<string> broken, string property, List<Offence> offences)
        {
            if (offences.Count == 0)
            {
                return;
            }

            offences.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));

            var seeds = new HashSet<int>();
            for (int i = 0; i < offences.Count; i++)
            {
                seeds.Add(offences[i].Seed);
            }

            var message = new StringBuilder();
            message.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0}: {1} times over {2} of {3} seeds. Worst:",
                property, offences.Count, seeds.Count, Seeds);

            for (int i = 0; i < offences.Count && i < WorstSeedsToReport; i++)
            {
                message.AppendLine().AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  seed {0}: {1}", offences[i].Seed, offences[i].What);
            }

            broken.Add(message.ToString());
        }

        /// <summary>Guards a property against passing because it never measured anything.</summary>
        static void RequireWork(List<string> broken, int measured, int least, string complaint)
        {
            if (measured <= least)
            {
                broken.Add($"{complaint} ({measured} measured)");
            }
        }

        /// <summary>True if any corridor rectangle contains the point.</summary>
        static bool Reserved(IReadOnlyList<Rect2> corridors, Vec2 at)
        {
            for (int i = 0; i < corridors.Count; i++)
            {
                if (corridors[i].Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>An axis-aligned rectangle as something a segment can be tested against.</summary>
        static Occluder Box(Rect2 rect) =>
            new Occluder(rect.Center, new Vec2(1f, 0f), rect.Width * 0.5f, rect.Depth * 0.5f);

        // --- the sweep ------------------------------------------------------------------------------

        /// <summary>A placed object reduced to what a road property asks about it.</summary>
        readonly struct Standing
        {
            public Standing(string id, Rect2 footprint)
            {
                Id = id;
                Footprint = footprint;
            }

            public string Id { get; }

            public Rect2 Footprint { get; }
        }

        /// <summary>One map of a sweep, with everything the properties are measured against.</summary>
        sealed class RoadMap
        {
            readonly List<Rect2> _pads = new List<Rect2>();
            readonly List<float> _sills = new List<float>();

            public RoadMap(ArenaParams parameters, Catalog catalog, bool paintCarriageways)
            {
                Parameters = parameters;
                Document = ArenaLayoutGenerator.Generate(parameters, catalog);

                Layout = ArenaLayout.Build(parameters);
                Terrain = ArenaLayoutGenerator.TerrainBeforeRoads(Document);
                Network = RoadNetwork.Build(
                    parameters, Layout, Terrain, Document.GeneratedObjects);

                Footprints = new List<Rect2>();
                List<PlacedObject> structures = PlacedGeometry.Structures(Document);
                for (int i = 0; i < structures.Count; i++)
                {
                    Footprints.Add(PlacedGeometry.WorldFootprint(structures[i], catalog));
                }

                Doorways = PlacedGeometry.Doorways(Document);

                PlacedAfterTheRoads = new List<Standing>();
                for (int i = 0; i < Document.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = Document.GeneratedObjects[i];

                    // Socket props ride whatever they are attached to, so where they stand is a
                    // claim about their parent rather than about the ground.
                    if (PlacedGeometry.IsSocketProp(placed) || !IsJudgedAgainstTheReservation(placed))
                    {
                        continue;
                    }

                    PlacedAfterTheRoads.Add(new Standing(
                        placed.StableId, PlacedGeometry.WorldFootprint(placed, catalog)));
                }

                for (int i = 0; i < Document.GeneratedObjects.Count; i++)
                {
                    IReadOnlyDictionary<string, string> metadata =
                        Document.GeneratedObjects[i].Metadata;

                    if (!metadata.ContainsKey(ArenaLayoutGenerator.DoorwayCountKey) ||
                        !metadata.TryGetValue(ArenaLayoutGenerator.FoundationKey, out string pad) ||
                        !metadata.TryGetValue(
                            ArenaLayoutGenerator.FoundationHeightKey, out string height))
                    {
                        continue;
                    }

                    _pads.Add(RectMetadata.Parse(pad));
                    _sills.Add(float.Parse(height, CultureInfo.InvariantCulture));
                }

                Carriageways = paintCarriageways ? new PaintedRoads(Layout, Network) : null;
                TooSteepCells = paintCarriageways ? CountTooSteep() : 0;
            }

            public ArenaParams Parameters { get; }

            public WorldDoc Document { get; }

            public ArenaLayout Layout { get; }

            public TerrainField Terrain { get; }

            public RoadNetwork Network { get; }

            /// <summary>The world footprint of every structure on the map.</summary>
            public List<Rect2> Footprints { get; }

            /// <summary>Every doorway rectangle the structures declared.</summary>
            public List<Rect2> Doorways { get; }

            /// <summary>
            /// Everything the map put down after the roads were laid and judged against the
            /// reservation, which is the cover and the street furniture.
            /// </summary>
            /// <remarks>
            /// Kerbing is left out, and it is the one exclusion that is about the art rather than
            /// about the order: a kerb is placed from a network and seated flush against the
            /// carriageway it edges, so it is inside the reservation by construction. See
            /// <see cref="CorridorsAreNotStoodIn"/>.
            /// </remarks>
            public List<Standing> PlacedAfterTheRoads { get; }

            /// <summary>The ground the carriageways cover, or null when it was not painted.</summary>
            public PaintedRoads Carriageways { get; }

            /// <summary>How many cells of this map are too steep for a road to be graded over.</summary>
            public int TooSteepCells { get; }

            /// <summary>
            /// The recorded pad height of the structure whose pad is nearest a point.
            /// </summary>
            /// <remarks>
            /// Which structure a portal belongs to, read off the document rather than off the
            /// network. A portal stands about a metre outside its own structure's pad and two
            /// structures are kept two yards apart, so the nearest pad is the one whose doorway the
            /// portal serves.
            /// </remarks>
            public float SillNearest(Vec2 at)
            {
                var point = new Rect2(at.X, at.Y, at.X, at.Y);
                float nearest = float.MaxValue;
                float sill = 0f;

                for (int i = 0; i < _pads.Count; i++)
                {
                    float distance = Rect2.Distance(_pads[i], point);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        sill = _sills[i];
                    }
                }

                return sill;
            }

            static bool IsJudgedAgainstTheReservation(PlacedObject placed)
            {
                for (int i = 0; i < placed.Tags.Count; i++)
                {
                    if (string.Equals(placed.Tags[i], CoverPlacer.CoverTag, StringComparison.Ordinal) ||
                        string.Equals(
                            placed.Tags[i], RoadFurniture.FurnitureTag, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// The same measure the router refuses a cell on: the steepest step from a cell to one
            /// of its four neighbours.
            /// </summary>
            int CountTooSteep()
            {
                ArenaGrid grid = Layout.Grid;
                float step = grid.CellSize;
                int count = 0;

                for (int z = 0; z < grid.CountZ; z++)
                {
                    for (int x = 0; x < grid.CountX; x++)
                    {
                        Vec2 at = grid.CellCenter(x, z);
                        float here = Terrain.HeightAt(at);

                        float steepest = MathF.Abs(Terrain.HeightAt(new Vec2(at.X + step, at.Y)) - here);
                        steepest = MathF.Max(
                            steepest, MathF.Abs(Terrain.HeightAt(new Vec2(at.X - step, at.Y)) - here));
                        steepest = MathF.Max(
                            steepest, MathF.Abs(Terrain.HeightAt(new Vec2(at.X, at.Y + step)) - here));
                        steepest = MathF.Max(
                            steepest, MathF.Abs(Terrain.HeightAt(new Vec2(at.X, at.Y - step)) - here));

                        if (steepest / step > Parameters.MaxRoadGradient)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// The ground the network's carriageways actually cover, painted onto the playfield grid.
        /// </summary>
        /// <remarks>
        /// The network reports polylines and a width; what a player walks on is the strip that makes
        /// out of them. Painting it is how the two questions a network has to answer — is this
        /// joined to that, and does any of it reach here — get asked about the road rather than
        /// about a line down the middle of it.
        /// </remarks>
        sealed class PaintedRoads
        {
            readonly ArenaGrid _grid;
            readonly bool[] _covered;

            public PaintedRoads(ArenaLayout layout, RoadNetwork network)
            {
                _grid = layout.Grid;
                _covered = new bool[_grid.CellCount];

                for (int s = 0; s < network.Segments.Count; s++)
                {
                    RoadSegment segment = network.Segments[s];
                    float half = segment.Width * 0.5f;

                    for (int i = 1; i < segment.Points.Count; i++)
                    {
                        Paint(segment.Points[i - 1], segment.Points[i], half);
                    }
                }
            }

            /// <summary>Flood fills the carriageways from the first cell of them.</summary>
            /// <remarks>
            /// Four-connected, on the same grounds <see cref="WalkableGrid.ReachableFrom"/> is: two
            /// strips of road that share only a corner are two roads meeting at a point, which is
            /// not somewhere anybody walks from one to the other.
            /// </remarks>
            public bool[] Reachable()
            {
                var reached = new bool[_covered.Length];
                var frontier = new Stack<int>();

                for (int cell = 0; cell < _covered.Length; cell++)
                {
                    if (_covered[cell])
                    {
                        reached[cell] = true;
                        frontier.Push(cell);
                        break;
                    }
                }

                while (frontier.Count > 0)
                {
                    int cell = frontier.Pop();
                    int x = cell % _grid.CountX;
                    int z = cell / _grid.CountX;

                    Spread(x - 1, z, reached, frontier);
                    Spread(x + 1, z, reached, frontier);
                    Spread(x, z - 1, reached, frontier);
                    Spread(x, z + 1, reached, frontier);
                }

                return reached;
            }

            /// <summary>True if the fill took in every metre of road there is.</summary>
            public bool AllReached(bool[] reached)
            {
                var any = false;
                for (int cell = 0; cell < _covered.Length; cell++)
                {
                    if (!_covered[cell])
                    {
                        continue;
                    }

                    any = true;
                    if (!reached[cell])
                    {
                        return false;
                    }
                }

                return any;
            }

            /// <summary>True if any road the fill took in lies inside the area.</summary>
            public bool AnyReached(Rect2 area, bool[] reached)
            {
                for (int cell = 0; cell < reached.Length; cell++)
                {
                    if (reached[cell] &&
                        area.Contains(_grid.CellCenter(cell % _grid.CountX, cell / _grid.CountX)))
                    {
                        return true;
                    }
                }

                return false;
            }

            void Paint(Vec2 from, Vec2 to, float half)
            {
                // Stepped along at a quarter of a cell so no cell the strip covers is skipped
                // between two stamps, which on a diagonal a whole-cell step would do.
                float length = Vec2.Distance(from, to);
                int steps = (int)MathF.Ceiling(length / (_grid.CellSize * 0.25f)) + 1;

                for (int i = 0; i <= steps; i++)
                {
                    Stamp(from + (to - from) * (i / (float)steps), half);
                }
            }

            void Stamp(Vec2 at, float half)
            {
                int reach = (int)MathF.Ceiling(half / _grid.CellSize);
                int centreX = (int)MathF.Floor((at.X - _grid.Bounds.MinX) / _grid.CellSize);
                int centreZ = (int)MathF.Floor((at.Y - _grid.Bounds.MinZ) / _grid.CellSize);

                for (int oz = -reach; oz <= reach; oz++)
                {
                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        int x = centreX + ox;
                        int z = centreZ + oz;
                        if (x < 0 || x >= _grid.CountX || z < 0 || z >= _grid.CountZ ||
                            Vec2.Distance(_grid.CellCenter(x, z), at) > half)
                        {
                            continue;
                        }

                        _covered[z * _grid.CountX + x] = true;
                    }
                }
            }

            void Spread(int x, int z, bool[] reached, Stack<int> frontier)
            {
                if (x < 0 || x >= _grid.CountX || z < 0 || z >= _grid.CountZ)
                {
                    return;
                }

                int cell = z * _grid.CountX + x;
                if (!_covered[cell] || reached[cell])
                {
                    return;
                }

                reached[cell] = true;
                frontier.Push(cell);
            }
        }
    }
}
