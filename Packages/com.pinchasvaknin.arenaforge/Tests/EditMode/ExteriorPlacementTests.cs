using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The dressing outside a structure: the run that hugs its walls, the heaps that stand against
    /// them, and the fence round the house's yard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three passes and three shapes, and the suite is written to fail if any two of them quietly
    /// became one. A hedge that behaved like a heap would pass every clearance property here and
    /// look wrong, because a line of planting with three-metre holes in it is not a line; a heap
    /// that behaved like a hedge would be a row of barrels evenly spaced round a building. So the
    /// properties are about <em>shape</em> — flush, adjacent, tight, distinct — rather than about
    /// how many objects came out.
    /// </para>
    /// <para>
    /// The fence is the exception, and it is the one property here that is a promise to a player
    /// rather than a matter of taste: a fence that closes is a house nobody can get into. It is
    /// asserted twice over — once against the ring's own geometry, and once by walking a spawn to
    /// the front door across a grid with every piece of dressing on the map blocked out.
    /// </para>
    /// </remarks>
    public sealed class ExteriorPlacementTests
    {
        /// <summary>How finely the walks round a yard ring sample it, in metres.</summary>
        const float GapStep = 0.1f;

        /// <summary>
        /// Least share of a map's yard corners the fences have to close. See
        /// <see cref="AYardFenceClosesTheCornersNothingAsksItToLeaveOpen"/>.
        /// </summary>
        const float ClosedYardCorners = 0.75f;

        /// <summary>Seeds swept by the placement properties.</summary>
        const int Seeds = 60;

        /// <summary>Slack for a measurement that should be exact, in metres.</summary>
        const float Tolerance = 1e-3f;

        /// <summary>
        /// How far the largest piece of exterior art reaches along one axis, in metres.
        /// </summary>
        /// <remarks>
        /// Read off <see cref="TestWorlds.ExteriorCatalog"/> rather than written down twice. It is
        /// what turns the placer's constants — which bound where a <em>pivot</em> may go — into a
        /// bound on where the art ends up.
        /// </remarks>
        const float LargestPiece = 0.8f;

        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static WorldDoc Generate(ulong seed) =>
            ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.ExteriorCatalog());

        // --- the art gate ------------------------------------------------------------------------

        /// <remarks>
        /// The bargain every optional stage in this tool makes. A catalog with nothing in the
        /// exterior folders has to produce the map it produced before the stage existed, which is
        /// why the pass returns before it draws rather than drawing and placing nothing.
        /// </remarks>
        [Test]
        public void ACatalogWithNoExteriorArtDressesNothing()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.SampleCatalog());

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    string id = doc.GeneratedObjects[i].StableId;
                    Assert.That(id, Does.Not.Contain(ExteriorPlacer.AroundSegment));
                    Assert.That(id, Does.Not.Contain(ExteriorPlacer.GroupSegment));
                    Assert.That(id, Does.Not.Contain(ExteriorPlacer.FenceSegment));
                }

                foreach (KeyValuePair<string, string> pair in doc.Metadata)
                {
                    Assert.That(pair.Key, Does.Not.StartWith(ExteriorPlacer.StatsPrefix),
                        "a stage that placed nothing should not report a tally");
                }
            }
        }

        [Test]
        public void ACatalogWithExteriorArtDressesEveryStructure()
        {
            WorldDoc doc = Generate(20260816UL);
            var dressing = new Dressing(doc, TestWorlds.ExteriorCatalog());

            Assert.That(dressing.StructureIds.Count, Is.EqualTo(3),
                "a map anchors a building in the middle and a house on each flank");

            var fenced = 0;
            for (int i = 0; i < dressing.StructureIds.Count; i++)
            {
                string id = dressing.StructureIds[i];
                Assert.That(dressing.Around(id).Count, Is.GreaterThan(0), $"{id} has nothing along its walls");
                fenced += dressing.Fence(id).Count;
            }

            Assert.That(dressing.GroupIds.Count, Is.GreaterThan(0), "nothing was heaped against anything");
            Assert.That(fenced, Is.GreaterThan(0), "the house was not fenced");
            Assert.That(doc.Metadata.ContainsKey(ExteriorPlacer.StatsPrefix + "accepted"), Is.True);
        }

        [Test]
        public void TheSameSeedProducesTheSameDressing()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                Assert.That(
                    ArenaJson.SerializeWorld(Generate(seed)),
                    Is.EqualTo(ArenaJson.SerializeWorld(Generate(seed))),
                    $"seed {seed} did not reproduce");
            }
        }

        // --- the continuous run ------------------------------------------------------------------

        /// <remarks>
        /// Flush, not merely near. The run is seated from the wall's own coordinate rather than
        /// proposed at a sampled position and tested for nearness, so the right assertion is an
        /// equality: a piece whose footprint has any daylight behind it is a piece that was placed
        /// by some other rule than the one this pass has.
        /// </remarks>
        [Test]
        public void EveryPieceOfTheRunIsFlushAgainstItsStructure()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    Rect2 wall = dressing.Footprint(id);
                    IReadOnlyList<Rect2> run = dressing.Around(id);

                    for (int i = 0; i < run.Count; i++)
                    {
                        Assert.That(Rect2.Distance(run[i], wall), Is.LessThanOrEqualTo(Tolerance),
                            $"seed {seed}: a piece of {id}'s run stands off its wall");
                        Assert.That(wall.Expanded(TestWorlds.HedgeDepth + Tolerance).Contains(run[i]), Is.True,
                            $"seed {seed}: a piece of {id}'s run is not beside its footprint");
                    }
                }
            }
        }

        /// <remarks>
        /// <para>
        /// The property the pass exists for. A cursor lays each piece where the last one stopped,
        /// so a run is adjacent unless a rule broke it — and the rules that break one are the
        /// clearance in front of a doorway, the heap the cluster pass stood there first, and the
        /// edge of the playfield. Nine pieces in ten having a neighbour is what "semi-continuous"
        /// comes out as on this art; a pass that had drifted into sampling positions would be far
        /// below it, because two independently sampled positions are never flush.
        /// </para>
        /// <para>
        /// A share over the sweep rather than per structure, because one structure's share is not a
        /// meaningful number: a small house whose two doorway faces are entirely taken by the
        /// approach clearance has a handful of pieces left in short pieces of wall, and that is the
        /// correct answer for that house rather than a shortfall.
        /// </para>
        /// </remarks>
        [Test]
        public void MostOfTheRunIsLaidAdjacent()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var pieces = 0;
            var withNeighbour = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    IReadOnlyList<Rect2> run = dressing.Around(dressing.StructureIds[s]);
                    for (int i = 0; i < run.Count; i++)
                    {
                        pieces++;
                        if (HasNeighbour(run, i))
                        {
                            withNeighbour++;
                        }
                    }
                }
            }

            Assert.That(pieces, Is.GreaterThan(0), "nothing was ever laid");
            Assert.That((float)withNeighbour / pieces, Is.GreaterThanOrEqualTo(0.85f),
                $"only {withNeighbour} of {pieces} pieces have another flush against them");
        }

        /// <remarks>
        /// The weakest thing that is still a run rather than a scattering, asserted of every
        /// structure on every seed: somewhere round it, two pieces are touching. Measured over a
        /// thousand structures, the worst case is exactly this and the median is a run of nine.
        /// </remarks>
        [Test]
        public void EveryRunHoldsAtLeastOnePairOfTouchingPieces()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    IReadOnlyList<Rect2> run = dressing.Around(id);
                    Assert.That(run.Count, Is.GreaterThan(1), $"seed {seed}: {id} has no run at all");

                    var touching = false;
                    for (int i = 0; i < run.Count && !touching; i++)
                    {
                        touching = HasNeighbour(run, i);
                    }

                    Assert.That(touching, Is.True, $"seed {seed}: nothing in {id}'s run touches anything else");
                }
            }
        }

        /// <remarks>
        /// It goes round, not along. Two faces rather than four is the floor because a small house
        /// spends both of its short faces on the doorway approach — a 2.4 m door on a 6 m wall,
        /// plus a metre and a half of clearance each side, leaves nothing a bush would fit in — and
        /// a run that stopped at the first corner would still pass every other property here.
        /// </remarks>
        [Test]
        public void TheRunReachesMoreThanOneFace()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    Assert.That(FacesTouched(dressing.Footprint(id), dressing.Around(id)),
                        Is.GreaterThanOrEqualTo(2), $"seed {seed}: {id}'s run stays on one wall");
                }
            }
        }

        // --- the clusters ------------------------------------------------------------------------

        /// <remarks>
        /// Tight is the whole of what a cluster is. The bound is the placer's own constants read as
        /// art rather than as pivots: the pieces of one heap are drawn within
        /// <see cref="ExteriorPlacer.ClusterSpan"/> of each other along the wall and
        /// <see cref="ExteriorPlacer.ClusterDepth"/> of it out from the wall, and each piece then
        /// reaches half its own size past the pivot on either side.
        /// </remarks>
        [Test]
        public void EveryClusterIsTight()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            float alongLimit = ExteriorPlacer.ClusterSpan + LargestPiece + Tolerance;
            float acrossLimit = ExteriorPlacer.ClusterDepth + LargestPiece + Tolerance;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int g = 0; g < dressing.GroupIds.Count; g++)
                {
                    string id = dressing.GroupIds[g];
                    IReadOnlyList<Rect2> items = dressing.Items(id);

                    Assert.That(items.Count, Is.InRange(1, ExteriorPlacer.ClusterSize),
                        $"seed {seed}: {id} is not a small group");

                    Rect2 box = Union(items);
                    Assert.That(MathF.Min(box.Width, box.Depth), Is.LessThanOrEqualTo(acrossLimit),
                        $"seed {seed}: {id} is spread out from the wall");
                    Assert.That(MathF.Max(box.Width, box.Depth), Is.LessThanOrEqualTo(alongLimit),
                        $"seed {seed}: {id} is spread along the wall");
                }
            }
        }

        [Test]
        public void EveryClusterStandsAgainstItsStructure()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int g = 0; g < dressing.GroupIds.Count; g++)
                {
                    string id = dressing.GroupIds[g];
                    Rect2 wall = dressing.Footprint(dressing.OwnerOfGroup(id));
                    IReadOnlyList<Rect2> items = dressing.Items(id);

                    for (int i = 0; i < items.Count; i++)
                    {
                        Assert.That(Rect2.Distance(items[i], wall),
                            Is.LessThanOrEqualTo(ExteriorPlacer.ClusterDepth + Tolerance),
                            $"seed {seed}: a piece of {id} is out in the lane");
                    }
                }
            }
        }


        /// <remarks>
        /// <para>
        /// The way in stays the way in. A gap is opened in front of every doorway a structure
        /// declares, as a rectangle decided before the run is walked, so the break is a property of
        /// the pass rather than a rule that happened to fire — the same treatment the yard fence
        /// gives its gates, and for the same reason: a doorway a hedge has grown across is a
        /// building nobody can get into.
        /// </para>
        /// <para>
        /// Measured against the doorway widened by the clearance rather than against the doorway
        /// itself. A bush whose leaves stop a hair short of the frame is a door you have to squeeze
        /// through sideways, and the clearance is the width at which a doorway reads as an
        /// entrance.
        /// </para>
        /// </remarks>
        [Test]
        public void NoPieceOfTheRunStandsInADoorwaysApproach()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var blocked = new List<string>();
            var approaches = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                var dressing = new Dressing(doc, catalog);
                List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    Rect2 wall = dressing.Footprint(id);
                    IReadOnlyList<Rect2> run = dressing.Around(id);

                    for (int d = 0; d < doorways.Count; d++)
                    {
                        if (!wall.Overlaps(doorways[d]))
                        {
                            continue;
                        }

                        approaches++;
                        Rect2 approach = doorways[d].Expanded(ExteriorPlacer.GateClearance);

                        for (int i = 0; i < run.Count; i++)
                        {
                            if (run[i].Overlaps(approach))
                            {
                                blocked.Add($"seed {seed}: {id}\'s run stands in a doorway");
                            }
                        }
                    }
                }
            }

            Assert.That(approaches, Is.GreaterThan(0), "no structure declared a doorway");
            Assert.That(blocked, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// Flush, the same zero clearance the run is seated at. A heap standing a hand\'s width off
        /// the wall leaves a strip of open ground behind it, and the strip is exactly what a bush
        /// fits in — so the run tiles through the back of the heap and the building comes out with
        /// two lines of dressing against a wall the art says has one.
        /// </para>
        /// <para>
        /// Of the heap rather than of each piece: what has to touch the wall is the heap, and the
        /// pieces stacked in front of the one that does are the heap having depth.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryHeapStandsFlushAgainstItsWall()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int g = 0; g < dressing.GroupIds.Count; g++)
                {
                    string id = dressing.GroupIds[g];
                    Rect2 wall = dressing.Footprint(dressing.OwnerOfGroup(id));

                    Assert.That(Rect2.Distance(Union(dressing.Items(id)), wall),
                        Is.LessThanOrEqualTo(Tolerance),
                        $"seed {seed}: {id} stands off the wall it is heaped against");
                }
            }
        }

        /// <remarks>
        /// <para>
        /// What a workspace files in <c>UniqueGroup</c> is furniture — cabinets, lockers, stacked
        /// crates — and furniture has a front. A quarter turn out of the stream faced three of every
        /// four pieces into the wall they were put against, which is the one thing about a heap that
        /// no property here could see: every rule the placer applies is a statement about a
        /// footprint, and a cabinet's footprint is the same rectangle whichever way it is facing.
        /// </para>
        /// <para>
        /// Indoors the same rule has to find the wall first, because a piece is scattered onto a
        /// floor and which of the four is behind it is a measurement — see
        /// <see cref="FurnitureFacingTests"/>. Out here the face is handed in, so the turn is a
        /// function of the wall and there is no reach to be inside of and no draw to override.
        /// </para>
        /// <para>
        /// Which wall a piece stands against is recovered from the geometry rather than read back
        /// off the placer: a heap lies wholly outside its structure on exactly one axis, which is
        /// the same claim <see cref="Reserved"/> is built on.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryPieceOfAHeapHasItsBackToTheWallItStandsAgainst()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var facing = new List<string>();
            var pieces = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                var dressing = new Dressing(doc, catalog);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    int cut = placed.StableId.IndexOf(
                        ExteriorPlacer.ItemSegment, StringComparison.Ordinal);
                    if (cut < 0)
                    {
                        continue;
                    }

                    string group = placed.StableId.Substring(0, cut);
                    Rect2 wall = dressing.Footprint(dressing.OwnerOfGroup(group));
                    Rect2 piece = PlacedGeometry.WorldFootprint(placed, catalog);
                    pieces++;

                    int expected = FacingOutOf(piece, wall);
                    Assert.That(expected, Is.Not.EqualTo(-1),
                        $"seed {seed}: {placed.StableId} is not outside its structure on one axis");

                    if (PlacedGeometry.YawSteps(placed) != expected * (YawStep.Count / QuarterTurn.Count))
                    {
                        facing.Add(
                            $"seed {seed}: {placed.StableId} is turned " +
                            $"{PlacedGeometry.YawSteps(placed) / (YawStep.Count / QuarterTurn.Count)} " +
                            $"against a wall that calls for {expected}");
                    }
                }
            }

            Assert.That(facing, Is.Empty);
            Assert.That(pieces, Is.GreaterThan(Seeds), "nothing was ever heaped against anything");
        }

        /// <summary>
        /// The quarter turn the wall a piece stands outside of calls for, or -1 when the piece is
        /// not clear of the structure on exactly one axis.
        /// </summary>
        /// <remarks>
        /// The turn is spelled out here rather than taken from <see cref="WallFacing.AwayFrom"/>,
        /// which is the code under test. A piece standing outside the structure's <em>high</em> Z
        /// wall has the wall behind it on negative Z and the map in front of it on positive Z,
        /// which is a quarter turn of nothing; the three others are read off the same way.
        /// </remarks>
        static int FacingOutOf(Rect2 piece, Rect2 wall)
        {
            if (piece.MaxZ <= wall.MinZ + Tolerance)
            {
                return 2;
            }

            if (piece.MinZ >= wall.MaxZ - Tolerance)
            {
                return 0;
            }

            if (piece.MaxX <= wall.MinX + Tolerance)
            {
                return 3;
            }

            return piece.MinX >= wall.MaxX - Tolerance ? 1 : -1;
        }

        /// <remarks>
        /// <para>
        /// The other half of the same fix, and the one that needed more than a clearance. A heap is
        /// three separate pieces, so the ground beside a barrel and between two of them overlaps
        /// nothing — and a run seated flush tiles straight into it, threading planting through
        /// somebody\'s stack of barrels and out the other side.
        /// </para>
        /// <para>
        /// So a heap reserves the stretch of wall it stands on, from the wall out to its own far
        /// edge, and the run treats it as a gap decided before the walk: it stops before the heap
        /// and picks up after it. The heap is the feature and the run is the filler — see the class
        /// remarks on <see cref="ExteriorPlacer"/> — so the wall belongs to the heap for as long as
        /// the heap is standing on it.
        /// </para>
        /// </remarks>
        [Test]
        public void TheRunNeverPassesBehindOrThroughAHeap()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var threaded = new List<string>();
            var heaps = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int g = 0; g < dressing.GroupIds.Count; g++)
                {
                    string id = dressing.GroupIds[g];
                    string owner = dressing.OwnerOfGroup(id);
                    Rect2 taken = Reserved(Union(dressing.Items(id)), dressing.Footprint(owner));
                    IReadOnlyList<Rect2> run = dressing.Around(owner);
                    heaps++;

                    for (int i = 0; i < run.Count; i++)
                    {
                        if (run[i].Overlaps(taken))
                        {
                            threaded.Add($"seed {seed}: {owner}\'s run stands on {id}\'s wall");
                        }
                    }
                }
            }

            Assert.That(heaps, Is.GreaterThan(0), "nothing was ever heaped against anything");
            Assert.That(threaded, Is.Empty);
        }

        /// <summary>
        /// The stretch of wall a heap takes: the ground it covers, carried back to the wall it
        /// stands against.
        /// </summary>
        /// <remarks>
        /// Derived here from the art and the footprint rather than read back from the placer, so
        /// the property is a statement about where things ended up. A heap lies wholly outside its
        /// structure on exactly one axis, which is what says which of the four walls it is against.
        /// </remarks>
        static Rect2 Reserved(Rect2 heap, Rect2 wall)
        {
            if (heap.MaxZ <= wall.MinZ + Tolerance)
            {
                return new Rect2(heap.MinX, heap.MinZ, heap.MaxX, wall.MinZ);
            }

            if (heap.MinZ >= wall.MaxZ - Tolerance)
            {
                return new Rect2(heap.MinX, wall.MaxZ, heap.MaxX, heap.MaxZ);
            }

            if (heap.MaxX <= wall.MinX + Tolerance)
            {
                return new Rect2(heap.MinX, heap.MinZ, wall.MinX, heap.MaxZ);
            }

            Assert.That(heap.MinX, Is.GreaterThanOrEqualTo(wall.MaxX - Tolerance),
                "a heap stands against one of its structure\'s four walls");
            return new Rect2(wall.MaxX, heap.MinZ, heap.MaxX, heap.MaxZ);
        }

        /// <remarks>
        /// Distinct, which <see cref="ConstraintKind.NoOverlap"/> alone does not give: three heaps
        /// standing shoulder to shoulder along one wall overlap nothing and are one heap. The pass
        /// gives each a face of its own, so what has to be true is that no two of a structure's
        /// heaps share any ground at all — not even the boxes around them.
        /// </remarks>
        [Test]
        public void NoTwoClustersOnAStructureRunIntoEachOther()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int a = 0; a < dressing.GroupIds.Count; a++)
                {
                    for (int b = a + 1; b < dressing.GroupIds.Count; b++)
                    {
                        string first = dressing.GroupIds[a];
                        string second = dressing.GroupIds[b];
                        if (dressing.OwnerOfGroup(first) != dressing.OwnerOfGroup(second))
                        {
                            continue;
                        }

                        Assert.That(Union(dressing.Items(first)).Overlaps(Union(dressing.Items(second))),
                            Is.False, $"seed {seed}: {first} and {second} are one heap");
                    }
                }
            }
        }

        /// <remarks>
        /// A structure has four faces and each holds at most one heap, so this is a count of faces
        /// rather than a budget — the same argument the interior decor pass makes about a room's
        /// corners. There is no floor to go with the ceiling: a wall whose every spot is inside a
        /// doorway's approach holds no heap at all, and that is the right answer for that wall.
        /// </remarks>
        [Test]
        public void AStructureHoldsAtMostOneClusterPerFace()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    Assert.That(dressing.GroupsOn(id).Count, Is.LessThanOrEqualTo(4),
                        $"seed {seed}: {id} carries more heaps than it has walls");
                }
            }
        }

        /// <remarks>
        /// Groups of about three, which is what the folder is for. Not every one of them: a heap
        /// whose anchor landed where a doorway's clearance reaches gets what fits, and one barrel
        /// against a wall is a reasonable thing for a level to contain. Nine in ten holding more
        /// than one piece is what says the pass is placing groups rather than singles.
        /// </remarks>
        [Test]
        public void MostClustersAreGroupsRatherThanSinglePieces()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();
            var groups = 0;
            var plural = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int g = 0; g < dressing.GroupIds.Count; g++)
                {
                    groups++;
                    if (dressing.Items(dressing.GroupIds[g]).Count > 1)
                    {
                        plural++;
                    }
                }
            }

            Assert.That(groups, Is.GreaterThan(0), "no heap was ever stood up");
            Assert.That((float)plural / groups, Is.GreaterThanOrEqualTo(0.75f),
                $"only {plural} of {groups} heaps hold more than one piece");
        }

        // --- the fence ---------------------------------------------------------------------------

        [Test]
        public void OnlyAHouseGetsAFence()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);
                var fenced = 0;

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    if (dressing.Fence(id).Count == 0)
                    {
                        continue;
                    }

                    fenced++;
                    Assert.That(dressing.IsHouse(id), Is.True, $"seed {seed}: {id} is not a house and is fenced");
                }

                Assert.That(fenced, Is.EqualTo(dressing.HouseIds.Count),
                    $"seed {seed}: a house went unfenced");
            }
        }

        [Test]
        public void EveryFenceSegmentStraddlesTheYardRing()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    Rect2 ring = dressing.Footprint(id).Expanded(ExteriorPlacer.YardMargin);
                    IReadOnlyList<Rect2> fence = dressing.Fence(id);

                    // Half a panel's thickness either side of the ring, because the run straddles
                    // it — plus the corner slack, because the run that owns a corner reaches that
                    // far past the end of its own side to take it.
                    float half = TestWorlds.FencePanelThickness * 0.5f + WallRun.CornerSlack + Tolerance;

                    for (int i = 0; i < fence.Count; i++)
                    {
                        Assert.That(ring.Expanded(half).Contains(fence[i]), Is.True,
                            $"seed {seed}: a segment of {id}'s fence is off the ring");
                        Assert.That(ring.Expanded(-half).Contains(fence[i]), Is.False,
                            $"seed {seed}: a segment of {id}'s fence is inside the yard");
                    }
                }
            }
        }

        /// <remarks>
        /// <para>
        /// The golden rule, measured on the ring itself: walk the yard's perimeter and find the
        /// longest stretch of it no segment stands on. A fence that had closed would report a gap
        /// of nothing, and a fence with only the corner notches between its runs would report a
        /// gap far narrower than a person.
        /// </para>
        /// <para>
        /// The opening a gate is asked to be is <see cref="ExteriorPlacer.GateWidth"/>, and the
        /// narrowest one measured across five hundred seeds is more than twice that — a doorway gap
        /// runs together with the slack at the end of the run it interrupts.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryFencePerimeterHasAnOpeningWideEnoughToWalkThrough()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    IReadOnlyList<Rect2> fence = dressing.Fence(id);
                    if (fence.Count == 0)
                    {
                        continue;
                    }

                    Rect2 ring = dressing.Footprint(id).Expanded(ExteriorPlacer.YardMargin);
                    Assert.That(WidestGapIn(ring, fence),
                        Is.GreaterThanOrEqualTo(ExteriorPlacer.GateWidth),
                        $"seed {seed}: {id}'s fence closes its yard");
                }
            }
        }

        /// <remarks>
        /// <para>
        /// The corners, which are the part of a ring four straight runs are worst at. Each of the
        /// four is reached by two runs and may be had by only one of them — a run standing in ground
        /// the other can occupy is a rejection, and a rejection at a corner is a hole — so the runs
        /// are slid round like a pinwheel and every corner is claimed exactly once. Before that they
        /// were all shortened at both ends instead, which kept them apart by leaving a notch of open
        /// ground at every corner of every yard on every map: not a wide hole, and four of them at
        /// the four places a person's eye goes.
        /// </para>
        /// <para>
        /// A share rather than all of them, because a corner may also be open for a reason the yard
        /// asked for. A gate is drawn on the ring wherever the stream puts it and can land on a
        /// corner; a doorway's gate can reach one; a cluster of clutter standing on the ring refuses
        /// the panel that would have closed it. None of those is this test's business and none can
        /// be read back out of the document, so what is asserted is the share — which was exactly
        /// zero before, and is measured below.
        /// </para>
        /// <para>
        /// Measured, not chosen: across seeds 1..200 of the default map with
        /// <see cref="TestWorlds.ExteriorCatalog"/>, 1371 of 1600 yard corners are closed — 0.857,
        /// where the old geometry closed none of them at all. Three quarters is that with headroom.
        /// A run of failures here means the runs stopped meeting, not that the number wants
        /// lowering.
        /// </para>
        /// </remarks>
        [Test]
        public void AYardFenceClosesTheCornersNothingAsksItToLeaveOpen()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            var corners = 0;
            var closed = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    IReadOnlyList<Rect2> fence = dressing.Fence(id);
                    if (fence.Count == 0)
                    {
                        continue;
                    }

                    Rect2 ring = dressing.Footprint(id).Expanded(ExteriorPlacer.YardMargin);
                    IReadOnlyList<Vec2> ends = CornersOf(ring);

                    for (int c = 0; c < ends.Count; c++)
                    {
                        corners++;
                        if (Stands(fence, ends[c]))
                        {
                            closed++;
                        }
                    }
                }
            }

            Assert.That(corners, Is.GreaterThan(0), "no yard was fenced at all");
            Assert.That((float)closed / corners, Is.GreaterThanOrEqualTo(ClosedYardCorners),
                $"only {closed} of {corners} yard corners are closed");
        }

        /// <summary>The four corners of a rectangle, anticlockwise from its low one.</summary>
        static IReadOnlyList<Vec2> CornersOf(Rect2 rect) => new[]
        {
            new Vec2(rect.MinX, rect.MinZ),
            new Vec2(rect.MaxX, rect.MinZ),
            new Vec2(rect.MaxX, rect.MaxZ),
            new Vec2(rect.MinX, rect.MaxZ),
        };

        /// <summary>True if any of these footprints stands on a point.</summary>
        static bool Stands(IReadOnlyList<Rect2> footprints, Vec2 at)
        {
            for (int i = 0; i < footprints.Count; i++)
            {
                if (footprints[i].Expanded(WallRun.CornerSlack).Contains(at))
                {
                    return true;
                }
            }

            return false;
        }

        /// <remarks>
        /// <para>
        /// The same promise from the other end, and the one that matters: a player standing in a
        /// spawn can walk to the house's front door. Every structure and every piece of dressing on
        /// the map is blocked out of the walkable grid — which is not how the map analysis models
        /// the world, where only a structure stops a player, and is deliberately harsher here
        /// because what is being tested is whether the fence <em>could</em> shut somebody out.
        /// </para>
        /// <para>
        /// On half-metre cells rather than the default metre, because a cell is blocked by any
        /// footprint that touches it: a coarse grid would round a fence out to something far
        /// thicker than the art and could report a gate as closed that a person walks straight
        /// through.
        /// </para>
        /// </remarks>
        [Test]
        public void ASpawnCanAlwaysWalkToTheHousesDoor()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                ArenaParams parameters = Params(seed);
                parameters.GridSize = 0.5f;

                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
                var dressing = new Dressing(doc, catalog);

                WalkableGrid walkable = WalkableGrid.Build(layout, dressing.Everything);
                bool[] reachable = walkable.ReachableFrom(layout.SpawnAreaA);

                // Every house, not the first one. Each gets its own fence, so each is its own
                // chance for the gaps to have been arranged wrong.
                IReadOnlyList<Rect2> houses = dressing.HouseFootprints();
                List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);

                for (int h = 0; h < houses.Count; h++)
                {
                    Rect2 house = houses[h];
                    var doors = 0;
                    var reached = false;

                    for (int i = 0; i < doorways.Count && !reached; i++)
                    {
                        if (!house.Overlaps(doorways[i]))
                        {
                            continue;
                        }

                        doors++;
                        reached = walkable.AnyReached(doorways[i].Expanded(1f), reachable);
                    }

                    Assert.That(doors, Is.GreaterThan(0), $"seed {seed}: house {h} declared no doorway");
                    Assert.That(reached, Is.True, $"seed {seed}: house {h} is walled off from spawn A");
                }
            }
        }

        // --- declared doorways ---------------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// <strong>Nothing standing outside a structure may crowd a door the art declared.</strong>
        /// Every other doorway property in this project is measured on a map whose structures
        /// declare none, so what they test is the pair the generator <em>derives</em> from a
        /// footprint — the middle of the two faces across the lane. A row that states its own
        /// openings puts the door somewhere else entirely, and the whole exterior has to keep off
        /// that one instead. Nothing generated a map down that path until
        /// <see cref="TestWorlds.DoorwayDeclaringCatalog"/> existed.
        /// </para>
        /// <para>
        /// Every stage that puts something on the ground outside is swept together — hedges,
        /// clusters, yard fencing, the boundary round the map and the cover scattered last —
        /// because "the door is blocked" is a fact about the ground in front of it and not about
        /// which pass put the thing there. The three that place under
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> have always held. The boundary is the one
        /// that does not, and may not: a hole in a hedge is a feature and a hole in the edge of the
        /// world is a way out of the level. What keeps it off a door instead is
        /// <c>ArenaLayoutGenerator.DoorsCanBeReached</c>, which refuses to stand a structure where a
        /// declared door would open onto the fence — so the fence never has to choose.
        /// </para>
        /// <para>
        /// Swept over map sizes rather than over the default map alone. A door only faces the edge
        /// of the playfield when its structure lands near one, which is a fact about how the lane
        /// bands divide a particular size, and the default map is the size where it happens least.
        /// </para>
        /// </remarks>
        [Test]
        public void NothingOutsideCrowdsADeclaredDoorway()
        {
            Catalog catalog = TestWorlds.DoorwayDeclaringCatalog();

            var sizes = new[]
            {
                new Vec2(60f, 60f), new Vec2(80f, 80f), new Vec2(100f, 100f),
                new Vec2(120f, 90f), new Vec2(75f, 45f),
            };

            var measured = 0;

            foreach (Vec2 size in sizes)
            {
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    var parameters = new ArenaParams { Seed = seed, PlayfieldSize = size };
                    WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                    List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);

                    Assert.That(doorways, Is.Not.Empty, $"{size.X} x {size.Y}, seed {seed}");
                    measured += doorways.Count;

                    for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                    {
                        PlacedObject placed = doc.GeneratedObjects[i];
                        if (!IsOutdoorProp(placed))
                        {
                            continue;
                        }

                        Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);

                        for (int d = 0; d < doorways.Count; d++)
                        {
                            Assert.That(
                                footprint.Overlaps(
                                    doorways[d].Expanded(CoverPlacer.DoorwayClearance - Tolerance)),
                                Is.False,
                                $"{size.X} x {size.Y}, seed {seed}: {placed.StableId} stands in " +
                                $"the way of the doorway at {doorways[d]}");
                        }
                    }
                }
            }

            Assert.That(measured, Is.GreaterThan(0), "the sweep measured no doorways at all");
        }

        /// <summary>
        /// True for anything the generator stands on the ground outside a structure: the dressing,
        /// the boundary and the cover, and not the structures or the spawn markers themselves.
        /// </summary>
        /// <remarks>
        /// A structure is excluded because its own doorway is in its own wall — a wall overlapping
        /// the door in it is the door, not an obstruction. Socket props are excluded for the reason
        /// every other two-dimensional test excludes them: a prop on a socket stands on top of the
        /// thing under it and shares that thing's ground on purpose.
        /// </remarks>
        static bool IsOutdoorProp(PlacedObject placed) =>
            !PlacedGeometry.IsStructure(placed) &&
            !PlacedGeometry.IsSocketProp(placed) &&
            !placed.StableId.EndsWith("/marker", StringComparison.Ordinal);

        /// <remarks>
        /// <para>
        /// The other half of the claim, and the half the one above cannot make. A yard ring stands
        /// <see cref="ExteriorPlacer.YardMargin"/> out from the wall, which is further than the
        /// clearance a doorway keeps — so a fence that closed the ring in front of a declared door
        /// would leave the door's own approach clear and still shut the house in. What is asserted
        /// here is the hole rather than the absence of an obstruction: a straight walk from the door
        /// out through the ring meets no fence.
        /// </para>
        /// <para>
        /// This is what a <c>DoorwayMarker</c> is <em>for</em>, and it is the reason
        /// <c>ExteriorPlacer.GatesFor</c> asks each doorway whether the house it belongs to overlaps
        /// it: a marker straddles the wall it is cut into, so the half of it inside the house is what
        /// says whose door it is, and the gate is projected out from there.
        /// </para>
        /// </remarks>
        [Test]
        public void AYardFenceOpensAtEveryDeclaredDoorway()
        {
            Catalog catalog = TestWorlds.DoorwayDeclaringCatalog();

            var opened = 0;

            // Wide enough that a cell can afford the house, which is what a yard fence goes round:
            // on a sixty-metre map this catalog's twelve-metre structures plus their yards are more
            // than a cell's size budget, so every cell falls back to the smaller building and no
            // house is placed at all. See ArenaLayoutGenerator.SelectStructure.
            foreach (Vec2 size in new[] { new Vec2(80f, 80f), new Vec2(100f, 100f) })
            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(
                    new ArenaParams { Seed = seed, PlayfieldSize = size }, catalog);
                var dressing = new Dressing(doc, catalog);
                List<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);

                for (int s = 0; s < dressing.StructureIds.Count; s++)
                {
                    string id = dressing.StructureIds[s];
                    IReadOnlyList<Rect2> fence = dressing.Fence(id);
                    if (fence.Count == 0)
                    {
                        continue;
                    }

                    Rect2 house = dressing.Footprint(id);

                    for (int d = 0; d < doorways.Count; d++)
                    {
                        // A doorway straddles the wall it is cut into, so the half inside the house
                        // is what says whose door it is — the same question GatesFor asks.
                        if (!house.Overlaps(doorways[d]))
                        {
                            continue;
                        }

                        Rect2 wayOut = WayOutOf(house, doorways[d]);
                        opened++;

                        for (int f = 0; f < fence.Count; f++)
                        {
                            Assert.That(fence[f].Overlaps(wayOut), Is.False,
                                $"{size.X} x {size.Y}, seed {seed}: {id}'s fence closes the way " +
                                $"out of the doorway at {doorways[d]}");
                        }
                    }
                }
            }

            Assert.That(opened, Is.GreaterThan(0), "the sweep found no fenced house with a door");
        }

        /// <summary>
        /// The strip a person walks along to get from a doorway out through the yard ring.
        /// </summary>
        /// <remarks>
        /// The doorway's own width, pushed out from the house along whichever axis the door faces —
        /// which is read off where the door sits relative to the middle of the house, because a
        /// declared doorway carries no facing of its own and does not need one: it is in a wall, and
        /// which wall is a fact about where it is.
        /// </remarks>
        static Rect2 WayOutOf(Rect2 house, Rect2 doorway)
        {
            float reach = ExteriorPlacer.YardMargin * 2f;
            Vec2 away = doorway.Center - house.Center;

            if (MathF.Abs(away.X) >= MathF.Abs(away.Y))
            {
                return away.X >= 0f
                    ? new Rect2(doorway.MinX, doorway.MinZ, house.MaxX + reach, doorway.MaxZ)
                    : new Rect2(house.MinX - reach, doorway.MinZ, doorway.MaxX, doorway.MaxZ);
            }

            return away.Y >= 0f
                ? new Rect2(doorway.MinX, doorway.MinZ, doorway.MaxX, house.MaxZ + reach)
                : new Rect2(doorway.MinX, house.MinZ - reach, doorway.MaxX, doorway.MaxZ);
        }

        // --- the dressing against everything else ------------------------------------------------

        /// <remarks>
        /// <para>
        /// One exception, and it is the one <see cref="WallRun"/> closes a run with: a piece seated
        /// backwards over a stretch the walk left bare doubles up on whatever ended at that
        /// stretch. That is deliberate — a run tiled from indivisible pieces cannot reach a line it
        /// does not divide into any other way, and a doubled fence post is a far smaller thing to
        /// be wrong than an open corner.
        /// </para>
        /// <para>
        /// So the property is narrowed rather than dropped: two pieces may share ground only when
        /// they belong to the same run. Anything else overlapping anything else — two clusters, a
        /// hedge and a house, two fences round different yards — is still the failure it always was.
        /// </para>
        /// </remarks>
        [Test]
        public void NothingOutsideOverlapsAnythingElseOutside()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var dressing = new Dressing(Generate(seed), catalog);
                IReadOnlyList<Rect2> all = dressing.Everything;

                for (int i = 0; i < all.Count; i++)
                {
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        if (!all[i].Overlaps(all[j]))
                        {
                            continue;
                        }

                        Assert.That(dressing.AreNeighboursInARun(i, j), Is.True,
                            $"seed {seed}: {dressing.IdAt(i)} and {dressing.IdAt(j)} stand in the " +
                            "same place and are not one run closing itself");
                    }
                }
            }
        }

        /// <remarks>
        /// The reason the exterior stage hands its placements to the cover stage. Cover is scattered
        /// after the dressing and has to place around it, so what is asserted is the margin cover
        /// keeps from anything else — not merely that the two do not intersect.
        /// </remarks>
        [Test]
        public void CoverKeepsItsMarginFromTheDressing()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                var dressing = new Dressing(doc, catalog);
                List<PlacedObject> cover = PlacedGeometry.GroundCover(doc);

                for (int c = 0; c < cover.Count; c++)
                {
                    Rect2 prop = PlacedGeometry.WorldFootprint(cover[c], catalog);
                    for (int i = 0; i < dressing.Dressed.Count; i++)
                    {
                        Assert.That(Rect2.Distance(prop, dressing.Dressed[i]),
                            Is.GreaterThanOrEqualTo(CoverPlacer.PropMargin - Tolerance),
                            $"seed {seed}: {cover[c].StableId} crowds the dressing");
                    }
                }
            }
        }

        // --- helpers -----------------------------------------------------------------------------

        /// <summary>True if another piece of the run is flush against this one.</summary>
        static bool HasNeighbour(IReadOnlyList<Rect2> run, int index)
        {
            for (int i = 0; i < run.Count; i++)
            {
                if (i != index && Rect2.Distance(run[i], run[index]) <= Tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How many of a footprint's four faces the run touches.</summary>
        static int FacesTouched(Rect2 wall, IReadOnlyList<Rect2> run)
        {
            var touched = new bool[4];
            for (int i = 0; i < run.Count; i++)
            {
                Rect2 piece = run[i];
                if (piece.MaxZ <= wall.MinZ + Tolerance)
                {
                    touched[0] = true;
                }
                else if (piece.MinX >= wall.MaxX - Tolerance)
                {
                    touched[1] = true;
                }
                else if (piece.MinZ >= wall.MaxZ - Tolerance)
                {
                    touched[2] = true;
                }
                else
                {
                    touched[3] = true;
                }
            }

            var count = 0;
            for (int i = 0; i < touched.Length; i++)
            {
                if (touched[i])
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The smallest rectangle holding all of them.</summary>
        static Rect2 Union(IReadOnlyList<Rect2> boxes)
        {
            Rect2 box = boxes[0];
            for (int i = 1; i < boxes.Count; i++)
            {
                box = new Rect2(
                    MathF.Min(box.MinX, boxes[i].MinX), MathF.Min(box.MinZ, boxes[i].MinZ),
                    MathF.Max(box.MaxX, boxes[i].MaxX), MathF.Max(box.MaxZ, boxes[i].MaxZ));
            }

            return box;
        }

        /// <summary>
        /// The longest run of a ring's perimeter that no segment stands on, in metres.
        /// </summary>
        /// <remarks>
        /// Walked as a loop rather than as four sides, so a gap straddling a corner is measured as
        /// the one gap it is. Each sample is tested against the segments grown by half a step,
        /// which can only make a gap read narrower than it is — the measurement errs against the
        /// property it is used to assert.
        /// </remarks>
        static float WidestGapIn(Rect2 ring, IReadOnlyList<Rect2> segments)
        {
            bool[] open = OpenAlong(ring, segments);

            var longest = 0;
            var run = 0;

            // Twice round, so a gap that wraps past the start of the walk is counted whole.
            for (int lap = 0; lap < 2 * open.Length; lap++)
            {
                if (!open[lap % open.Length])
                {
                    run = 0;
                    continue;
                }

                run++;
                if (run > longest)
                {
                    longest = run;
                }
            }

            return MathF.Min(longest, open.Length) * GapStep;
        }

        /// <summary>Which steps round a ring no segment stands on.</summary>
        static bool[] OpenAlong(Rect2 ring, IReadOnlyList<Rect2> segments)
        {
            float perimeter = 2f * (ring.Width + ring.Depth);
            var open = new bool[(int)MathF.Ceiling(perimeter / GapStep)];

            for (int i = 0; i < open.Length; i++)
            {
                Vec2 point = OnPerimeter(ring, (i + 0.5f) * GapStep);
                open[i] = true;

                for (int s = 0; s < segments.Count && open[i]; s++)
                {
                    open[i] = !segments[s].Expanded(GapStep * 0.5f).Contains(point);
                }
            }

            return open;
        }

        /// <summary>A point a given distance clockwise round a rectangle from its lower corner.</summary>
        static Vec2 OnPerimeter(Rect2 ring, float distance)
        {
            if (distance < ring.Width)
            {
                return new Vec2(ring.MinX + distance, ring.MinZ);
            }

            distance -= ring.Width;
            if (distance < ring.Depth)
            {
                return new Vec2(ring.MaxX, ring.MinZ + distance);
            }

            distance -= ring.Depth;
            if (distance < ring.Width)
            {
                return new Vec2(ring.MaxX - distance, ring.MaxZ);
            }

            return new Vec2(ring.MinX, ring.MaxZ - (distance - ring.Width));
        }

        /// <summary>
        /// One map's structures and the dressing round them, read back out of the document the way
        /// a validator would: by stable id and by catalog lookup, with nothing passed through from
        /// the generator.
        /// </summary>
        /// <remarks>
        /// Lists beside the dictionaries, so the suite walks the objects in the order the document
        /// holds them rather than in whatever order a hash table enumerates. Iteration order does
        /// not change the verdict of a property that has to hold of everything, but it does change
        /// which failure gets reported first, and a test that names a different object each run is
        /// a test nobody can bisect.
        /// </remarks>
        sealed class Dressing
        {
            readonly Dictionary<string, Rect2> _footprints = new Dictionary<string, Rect2>(StringComparer.Ordinal);
            readonly Dictionary<string, List<Rect2>> _pieces = new Dictionary<string, List<Rect2>>(StringComparer.Ordinal);
            readonly List<string> _structures = new List<string>();
            readonly List<string> _houses = new List<string>();
            readonly List<string> _groups = new List<string>();
            readonly List<Rect2> _dressed = new List<Rect2>();
            readonly List<Rect2> _everything = new List<Rect2>();

            // Parallel to _everything: which run each entry belongs to and where in it, so that the
            // one overlap a run is allowed — its closing piece doubling up on the piece before —
            // can be told from every overlap that is still a failure. Null and -1 for a structure,
            // which belongs to no run.
            readonly List<string> _runs = new List<string>();
            readonly List<int> _positions = new List<int>();
            readonly List<string> _ids = new List<string>();

            public Dressing(WorldDoc doc, Catalog catalog)
            {
                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    string id = placed.StableId;

                    if (PlacedGeometry.IsStructure(placed))
                    {
                        Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);
                        _footprints[id] = footprint;
                        _structures.Add(id);
                        Record(footprint, id, null, -1);

                        if (placed.LogicalId.StartsWith(
                                ArenaLayoutGenerator.HouseTag, StringComparison.Ordinal))
                        {
                            _houses.Add(id);
                        }

                        continue;
                    }

                    string key = KeyOf(id);
                    if (key == null)
                    {
                        continue;
                    }

                    Rect2 piece = PlacedGeometry.WorldFootprint(placed, catalog);
                    if (!_pieces.TryGetValue(key, out List<Rect2> pieces))
                    {
                        pieces = new List<Rect2>();
                        _pieces[key] = pieces;

                        if (key.Contains(ExteriorPlacer.GroupSegment))
                        {
                            _groups.Add(key.Substring(0, key.Length - ExteriorPlacer.ItemSegment.Length));
                        }
                    }

                    pieces.Add(piece);
                    _dressed.Add(piece);
                    Record(piece, id, key, PositionIn(id, key));
                }
            }

            void Record(Rect2 footprint, string id, string run, int position)
            {
                _everything.Add(footprint);
                _ids.Add(id);
                _runs.Add(run);
                _positions.Add(position);
            }

            /// <summary>Where in its run a piece stands, read off the tail of its stable id.</summary>
            static int PositionIn(string stableId, string run) =>
                int.TryParse(
                    stableId.Substring(run.Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int position)
                    ? position
                    : -1;

            /// <summary>The structures, in the order the document holds them.</summary>
            public IReadOnlyList<string> StructureIds => _structures;

            /// <summary>Every cluster on the map, by the stable id its pieces nest under.</summary>
            public IReadOnlyList<string> GroupIds => _groups;

            /// <summary>Every piece of dressing, without the structures.</summary>
            public IReadOnlyList<Rect2> Dressed => _dressed;

            /// <summary>Everything standing on the ground, structures included.</summary>
            public IReadOnlyList<Rect2> Everything => _everything;

            /// <summary>The stable id of one entry of <see cref="Everything"/>.</summary>
            public string IdAt(int index) => _ids[index];

            /// <summary>True if two entries of <see cref="Everything"/> are pieces of one run.</summary>
            /// <remarks>
            /// <para>
            /// One run rather than two consecutive pieces of one, which is what this asked until
            /// the run learned to close every stretch it left bare rather than only its tail — see
            /// <c>WallRun.CloseGaps</c>. A closing piece is stood up after the walk has finished, so
            /// it is numbered after everything the walk laid and doubles up on whichever piece
            /// happened to end at the stretch it filled. Adjacent numbering was the exact shape of
            /// the one overlap a run could have when a run could only close its tail, and it is not
            /// the shape of this one.
            /// </para>
            /// <para>
            /// What the narrowing still rules out is everything the property was written for: two
            /// clusters in one heap, a hedge through a house, two yards' fences meeting. A run is
            /// allowed to double up on itself and nothing is allowed to stand in anything else.
            /// </para>
            /// </remarks>
            public bool AreNeighboursInARun(int a, int b) =>
                _runs[a] != null &&
                string.Equals(_runs[a], _runs[b], StringComparison.Ordinal) &&
                _positions[a] >= 0 && _positions[b] >= 0;

            public Rect2 Footprint(string structureId) => _footprints[structureId];

            public bool IsHouse(string structureId) => _houses.Contains(structureId);

            /// <summary>The stable ids of the houses on the map. A yard fence goes round each.</summary>
            /// <remarks>
            /// It can be empty, and that is not a broken map. A cell draws its art from everything
            /// tagged <see cref="ArenaLayoutGenerator.StructureTag"/> that fits its budget, so a map
            /// whose every cell came up with a building has no house on it and no yard to fence.
            /// The suites that need houses to look at sweep seeds and assert that some turned up
            /// rather than assuming this one did.
            /// </remarks>
            public IReadOnlyList<string> HouseIds => _houses;

            /// <summary>Every house on the map, in document order. A yard fence goes round each.</summary>
            public IReadOnlyList<Rect2> HouseFootprints()
            {
                var footprints = new List<Rect2>(_houses.Count);
                for (int i = 0; i < _houses.Count; i++)
                {
                    footprints.Add(_footprints[_houses[i]]);
                }

                return footprints;
            }

            public IReadOnlyList<Rect2> Around(string structureId) =>
                Pieces(structureId + ExteriorPlacer.AroundSegment);

            public IReadOnlyList<Rect2> Fence(string structureId) =>
                Pieces(structureId + ExteriorPlacer.FenceSegment);

            public IReadOnlyList<Rect2> Items(string groupId) => Pieces(groupId + ExteriorPlacer.ItemSegment);

            /// <summary>The structure a cluster's stable id nests under.</summary>
            public string OwnerOfGroup(string groupId) =>
                groupId.Substring(0, groupId.IndexOf(ExteriorPlacer.GroupSegment, StringComparison.Ordinal));

            /// <summary>The clusters standing against one structure, in generation order.</summary>
            public List<string> GroupsOn(string structureId)
            {
                var mine = new List<string>();
                for (int i = 0; i < _groups.Count; i++)
                {
                    if (OwnerOfGroup(_groups[i]) == structureId)
                    {
                        mine.Add(_groups[i]);
                    }
                }

                return mine;
            }

            /// <summary>
            /// What a stable id says a piece belongs to: the run, the fence, or one cluster.
            /// </summary>
            /// <remarks>
            /// Off the id rather than off a tag or a metadata key, because the nesting is what the
            /// belonging <em>is</em> — the same claim <c>PlacedGeometry.IsSocketProp</c> reads.
            /// </remarks>
            static string KeyOf(string stableId)
            {
                int item = stableId.IndexOf(ExteriorPlacer.ItemSegment, StringComparison.Ordinal);
                if (item >= 0)
                {
                    return stableId.Substring(0, item + ExteriorPlacer.ItemSegment.Length);
                }

                int around = stableId.IndexOf(ExteriorPlacer.AroundSegment, StringComparison.Ordinal);
                if (around >= 0)
                {
                    return stableId.Substring(0, around + ExteriorPlacer.AroundSegment.Length);
                }

                int fence = stableId.IndexOf(ExteriorPlacer.FenceSegment, StringComparison.Ordinal);
                return fence >= 0
                    ? stableId.Substring(0, fence + ExteriorPlacer.FenceSegment.Length)
                    : null;
            }

            IReadOnlyList<Rect2> Pieces(string key) =>
                _pieces.TryGetValue(key, out List<Rect2> pieces) ? pieces : Array.Empty<Rect2>();
        }
    }
}
