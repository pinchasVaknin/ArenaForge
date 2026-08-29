using System;
using System.Collections.Generic;
using System.Globalization;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The fence round the outside of the map: that it is there, that it is on all four edges, and
    /// that it very nearly closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The properties here are about a <em>line</em> rather than about a set of objects, which is
    /// why so few of them count anything. A boundary made of the right number of panels in the wrong
    /// places is not a boundary, and one that covers three of the four edges is a level a player
    /// walks out of on the fourth. So each edge is measured on its own, and the run along it is
    /// merged into intervals and compared with the length of the edge.
    /// </para>
    /// <para>
    /// <strong>The coverage figure is not one, and what is missing from it is exactly four
    /// corners.</strong> The four runs go round like a pinwheel — each takes the corner at one of
    /// its ends and stops a thickest-panel short of the other, so every corner is claimed once and
    /// no two runs ask for the same ground. A corner claimed by the run that turns into it is a
    /// corner this file's per-edge measurement credits to that other edge, so the figure comes out
    /// short by four thicknesses over the whole perimeter and by nothing else. That is derived
    /// below rather than measured, which is the difference between a threshold and a property.
    /// </para>
    /// <para>
    /// The <em>ends</em> of the runs are closed outright. A run tiled from indivisible pieces stops
    /// short of the line by a remainder, so <see cref="WallRun"/> seats one last piece backwards
    /// from the end — overlapping the piece before it, which is the one overlap
    /// <see cref="NothingOnTheMapOverlapsTheBoundary"/> allows.
    /// </para>
    /// </remarks>
    public sealed class WorldBoundaryTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every map.</summary>
        const int Seeds = 40;

        /// <summary>Slack for a measurement that should be exact, in metres.</summary>
        const float Tolerance = 1e-3f;

        /// <summary>
        /// Least share of a playfield's perimeter the boundary may stand on: all of it, less the
        /// four corners each run hands to the run that turns there.
        /// </summary>
        /// <remarks>
        /// Derived rather than measured, and the derivation is the property. Each of the four runs
        /// gives up a thickest-panel of its own line so that the run meeting it there can have the
        /// corner — see <c>PerimeterFence.Edges</c> — and the given-up length is credited to the
        /// other edge by <see cref="BoundaryRun"/>, which sorts a segment onto the edge it lies
        /// along. So four thicknesses of the perimeter are covered and not counted, and nothing else
        /// is missing at all. A failure here is a run that stopped short, not a number that needs
        /// raising.
        /// </remarks>
        static float MinPerimeterCoverage(Catalog catalog, Rect2 field) =>
            1f - WallRun.FaceCount * ThickestPanel(catalog) / (2f * (field.Width + field.Depth));

        /// <summary>How thick the thickest panel the boundary could be tiled from is, in metres.</summary>
        /// <remarks>
        /// The stone folder alone, because that is the whole of what the stage draws from — the
        /// wood next door fences yards and never the map. Read off the catalog rather than typed in,
        /// which is what makes the derived figure follow the art pack rather than the test.
        /// </remarks>
        static float ThickestPanel(Catalog catalog) =>
            WallRun.ThickestSegment(catalog.Query(TagQuery.All(PerimeterFence.StoneFenceTag)));

        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        static WorldDoc Generate(ulong seed) =>
            ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.BoundaryCatalog());

        // --- the art gate --------------------------------------------------------------------------

        /// <remarks>
        /// The bargain every optional stage in this tool makes. A catalog with nothing in either
        /// fence folder produces the map it produced before the stage existed, which is why the pass
        /// returns before it draws rather than drawing and placing nothing.
        /// </remarks>
        [Test]
        public void ACatalogWithNoFenceArtLeavesTheMapOpen()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.SampleCatalog());

                Assert.That(Segments(doc), Is.Empty, $"seed {seed}");

                foreach (KeyValuePair<string, string> pair in doc.Metadata)
                {
                    Assert.That(pair.Key, Does.Not.StartWith(PerimeterFence.StatsPrefix),
                        "a stage that placed nothing should not report a tally");
                }
            }
        }

        [Test]
        public void ACatalogWithFenceArtRecordsWhatTheBoundaryCost()
        {
            WorldDoc doc = Generate(20260816UL);

            Assert.That(Segments(doc).Count, Is.GreaterThan(0));
            Assert.That(doc.Metadata.ContainsKey(PerimeterFence.StatsPrefix + "accepted"), Is.True);
        }

        [Test]
        public void TheSameSeedProducesTheSameBoundary()
        {
            for (ulong seed = 1; seed <= 10; seed++)
            {
                Assert.That(
                    ArenaJson.SerializeWorld(Generate(seed)),
                    Is.EqualTo(ArenaJson.SerializeWorld(Generate(seed))),
                    $"seed {seed} did not reproduce");
            }
        }

        // --- where the boundary is -----------------------------------------------------------------

        /// <remarks>
        /// The task the stage exists for, stated the plainest way there is: every side of the
        /// bounding box carries fence. Three sides and a gap is a map with a way out of it.
        /// </remarks>
        [Test]
        public void TheBoundaryRunsAlongAllFourEdgesOfThePlayfield()
        {
            Catalog catalog = TestWorlds.BoundaryCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
                var run = new BoundaryRun(doc, catalog, layout);

                for (int edge = 0; edge < BoundaryRun.EdgeCount; edge++)
                {
                    Assert.That(run.SegmentsOn(edge), Is.GreaterThan(0),
                        $"seed {seed}: nothing stands on the {BoundaryRun.NameOf(edge)} edge");
                }
            }
        }

        /// <remarks>
        /// Both halves of "flush and inside", and they pull against each other: a segment straddling
        /// the playfield's own edge would be half outside it, and one held clear of the edge by a
        /// rule would leave a strip of ground beyond the boundary that a player could stand on.
        /// </remarks>
        [Test]
        public void EverySegmentStandsInsideThePlayfieldAndAgainstItsEdge()
        {
            Catalog catalog = TestWorlds.BoundaryCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
                Rect2 field = layout.Playfield;

                foreach (PlacedObject placed in Segments(doc))
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);

                    Assert.That(field.Contains(footprint), Is.True,
                        $"seed {seed}: {placed.StableId} at {footprint} leaves {field}");
                    Assert.That(
                        Rect2.Distance(footprint, Edge(field, BoundaryRun.EdgeOf(footprint, field))),
                        Is.LessThanOrEqualTo(Tolerance),
                        $"seed {seed}: {placed.StableId} stands off the edge it belongs to");
                }
            }
        }

        [Test]
        public void EverySegmentCarriesTheFenceTag()
        {
            WorldDoc doc = Generate(11UL);

            foreach (PlacedObject placed in Segments(doc))
            {
                Assert.That(placed.Tags, Does.Contain("fence"), placed.StableId);
                Assert.That(placed.Pose.Scale, Is.EqualTo(1f), "art is placed, never rescaled");
            }
        }

        /// <remarks>
        /// Merged intervals per edge rather than a count of panels, because what a player meets is
        /// the line and not the objects. A hundred panels heaped on one corner would pass any count.
        /// </remarks>
        [Test]
        public void TheBoundaryCoversTheWholePerimeterBarTheCornersItHandsOver()
        {
            Catalog catalog = TestWorlds.BoundaryCatalog();
            float worst = 1f;
            ulong worstSeed = 0;
            var field = Rect2.Zero;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
                field = layout.Playfield;
                float covered = new BoundaryRun(doc, catalog, layout).Coverage;

                if (covered < worst)
                {
                    worst = covered;
                    worstSeed = seed;
                }
            }

            Assert.That(worst,
                Is.GreaterThanOrEqualTo(MinPerimeterCoverage(catalog, field) - Tolerance),
                $"seed {worstSeed} left {(1f - worst) * 100f:0.###}% of the perimeter open");
        }

        /// <remarks>
        /// <para>
        /// The same claim where it used to fail, and the two things that broke it are both in this
        /// case. The panel is thirty metres, so a sixty-metre edge is two of them and nothing about
        /// the arithmetic is forgiving; and the map is generated at half a dozen sizes, none of
        /// which the panel divides. What used to come out of that was three quarters of a boundary
        /// on the default map and half of one at eighty metres — a whole edge missing, because a
        /// lamp post the dressing had already stood on the edge refused a panel and a refused panel
        /// there is thirty metres of open level.
        /// </para>
        /// <para>
        /// The coverage figure is derived exactly as it is above, off the art in the catalog, so
        /// this is the same property measured on harder ground rather than a second threshold.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBoundaryClosesOnEveryMapSizeAndOnArtThatDoesNotDivideOne()
        {
            Catalog catalog = TestWorlds.ArtPackScaleBoundaryCatalog();

            var sizes = new[]
            {
                new Vec2(60f, 60f), new Vec2(80f, 80f), new Vec2(100f, 100f),
                new Vec2(120f, 90f), new Vec2(75f, 45f), new Vec2(200f, 200f),
            };

            foreach (Vec2 size in sizes)
            {
                for (ulong seed = 1; seed <= 24; seed++)
                {
                    var parameters = new ArenaParams { Seed = seed, PlayfieldSize = size };
                    WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                    ArenaLayout layout = ArenaLayout.Build(parameters);
                    float covered = new BoundaryRun(doc, catalog, layout).Coverage;

                    Assert.That(covered,
                        Is.GreaterThanOrEqualTo(
                            MinPerimeterCoverage(catalog, layout.Playfield) - Tolerance),
                        $"{size.X} x {size.Y}, seed {seed}: " +
                        $"{(1f - covered) * 100f:0.###}% of the perimeter is open");
                }
            }
        }

        /// <remarks>
        /// The other half of the same claim, and the half a per-edge measurement cannot make: a
        /// corner is where two runs meet, and the whole of the pinwheel in <c>PerimeterFence.Edges</c>
        /// is about exactly one of them reaching it. A gap at the corner of a map is the gap a player
        /// finds, because a corner is where somebody pressed into the edge of the level ends up.
        /// </remarks>
        [Test]
        public void TheBoundaryClosesEveryCornerOfThePlayfield()
        {
            Catalog catalog = TestWorlds.BoundaryCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                Rect2 field = ArenaLayout.Build(doc.Parameters).Playfield;

                var corners = new[]
                {
                    new Vec2(field.MinX, field.MinZ),
                    new Vec2(field.MaxX, field.MinZ),
                    new Vec2(field.MaxX, field.MaxZ),
                    new Vec2(field.MinX, field.MaxZ),
                };

                for (int i = 0; i < corners.Length; i++)
                {
                    Assert.That(Closes(doc, catalog, corners[i]), Is.True,
                        $"seed {seed}: no segment stands on the corner at {corners[i]}");
                }
            }
        }

        /// <summary>True if any segment of the boundary stands on a point.</summary>
        static bool Closes(WorldDoc doc, Catalog catalog, Vec2 corner)
        {
            foreach (PlacedObject placed in Segments(doc))
            {
                if (PlacedGeometry.WorldFootprint(placed, catalog).Contains(corner))
                {
                    return true;
                }
            }

            return false;
        }

        /// <remarks>
        /// <para>
        /// Nothing may stand in the boundary's way and nothing may stand in it, which is one rule
        /// read from both ends. The stage places last of the anchored three, so a segment overlapping
        /// a hedge would mean it never asked; a piece of cover overlapping a segment would mean the
        /// cover stage was never told the boundary was there.
        /// </para>
        /// <para>
        /// The boundary <em>does</em> overlap itself, once at the end of each of its four runs, and
        /// that is what closing the line costs. Panels are indivisible and nothing in this tool
        /// stretches art, so a run tiled from one end always stops short by a remainder — and the
        /// only way to reach the far end with the art the catalog describes is to seat one last
        /// panel backwards from it, over the panel before. So the claim here is the narrow one: two
        /// segments may share ground only when they are consecutive in the run.
        /// </para>
        /// </remarks>
        [Test]
        public void NothingOnTheMapOverlapsTheBoundary()
        {
            Catalog catalog = TestWorlds.BoundaryCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);
                var boundary = new List<Segment>();
                var everything = new List<Rect2>();

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];

                    // A prop on a socket stands on top of the thing under it, so it shares that
                    // thing's ground on purpose and is not part of a two-dimensional overlap test.
                    if (PlacedGeometry.IsSocketProp(placed))
                    {
                        continue;
                    }

                    Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);
                    if (IsBoundary(placed))
                    {
                        boundary.Add(new Segment(placed, footprint));
                    }
                    else
                    {
                        everything.Add(footprint);
                    }
                }

                for (int i = 0; i < boundary.Count; i++)
                {
                    for (int j = i + 1; j < boundary.Count; j++)
                    {
                        if (!boundary[i].Footprint.Overlaps(boundary[j].Footprint))
                        {
                            continue;
                        }

                        Assert.That(Math.Abs(boundary[i].Index - boundary[j].Index), Is.EqualTo(1),
                            $"seed {seed}: segments {boundary[i].Index} and {boundary[j].Index} " +
                            "share ground and are not one run closing itself");
                    }

                    for (int j = 0; j < everything.Count; j++)
                    {
                        Assert.That(boundary[i].Footprint.Overlaps(everything[j]), Is.False,
                            $"seed {seed}: {boundary[i].Footprint} runs through {everything[j]}");
                    }
                }
            }
        }

        // --- what the boundary is made of ----------------------------------------------------------

        /// <remarks>
        /// <para>
        /// The strict separation between the two fence folders, from the side that can see both.
        /// The boundary catalog holds a wooden panel as well as a stone one — it is what a yard is
        /// fenced with — and the wooden one is shorter, so it would close the tail of a run the
        /// stone cannot finish. It is still never used here, and that is the property: the edge of
        /// the level is a stone wall, and a garden panel spliced into it reads as a hole somebody
        /// patched.
        /// </para>
        /// <para>
        /// The wood has to appear somewhere on the map, or the assertion is passing because the
        /// catalog never offered any.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBoundaryIsBuiltOfStoneAndNeverOfTheWoodBesideIt()
        {
            var stone = 0;
            var yard = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = Generate(seed);

                foreach (PlacedObject placed in Segments(doc))
                {
                    Assert.That(placed.LogicalId, Is.EqualTo(TestWorlds.StonePanelId),
                        $"seed {seed}: {placed.StableId} is not stone");
                    stone++;
                }

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    if (doc.GeneratedObjects[i].LogicalId == TestWorlds.FencePanelId)
                    {
                        yard++;
                    }
                }
            }

            Assert.That(stone, Is.GreaterThan(0), "the boundary is meant to be made of stone");
            Assert.That(yard, Is.GreaterThan(0), "and the wood beside it is meant to fence the yards");
        }

        /// <remarks>
        /// The other side of the same rule, and the price of it. The boundary draws from one folder
        /// and only that folder, so a workspace that has filled <c>WoodFence</c> and not
        /// <c>StoneFence</c> gets no boundary at all rather than a boundary made of garden panels —
        /// which is the answer that tells the person what to file, where the quiet substitution
        /// left them with a map whose edge did not look like one.
        /// </remarks>
        [Test]
        public void ACatalogWithOnlyWoodFencingGetsNoBoundaryAtAll()
        {
            Catalog catalog = TestWorlds.ExteriorCatalog();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                Assert.That(Segments(doc), Is.Empty, $"seed {seed}");
            }
        }

        // --- reading the boundary back out of a document ---------------------------------------------

        static IReadOnlyList<PlacedObject> Segments(WorldDoc doc)
        {
            var segments = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (IsBoundary(doc.GeneratedObjects[i]))
                {
                    segments.Add(doc.GeneratedObjects[i]);
                }
            }

            return segments;
        }

        static bool IsBoundary(PlacedObject placed) =>
            placed.StableId.StartsWith(PerimeterFence.IdPrefix, StringComparison.Ordinal);

        /// <summary>One segment of the boundary, with its place in the run it belongs to.</summary>
        /// <remarks>
        /// The number off the end of the stable id rather than the position in the document, so what
        /// is compared is the run's own ordering — which is what "consecutive" means when the
        /// question is whether a run closed itself.
        /// </remarks>
        readonly struct Segment
        {
            public Segment(PlacedObject placed, Rect2 footprint)
            {
                Footprint = footprint;
                Index = int.Parse(
                    placed.StableId.Substring(PerimeterFence.IdPrefix.Length),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
            }

            public Rect2 Footprint { get; }

            public int Index { get; }
        }

        /// <summary>One edge of the playfield, as a rectangle of no width.</summary>
        static Rect2 Edge(Rect2 field, int edge)
        {
            switch (edge)
            {
                case 0: return new Rect2(field.MinX, field.MinZ, field.MaxX, field.MinZ);
                case 1: return new Rect2(field.MaxX, field.MinZ, field.MaxX, field.MaxZ);
                case 2: return new Rect2(field.MinX, field.MaxZ, field.MaxX, field.MaxZ);
                default: return new Rect2(field.MinX, field.MinZ, field.MinX, field.MaxZ);
            }
        }

        /// <summary>
        /// One map's boundary, sorted onto the four edges of the playfield and merged into the
        /// intervals it actually occupies.
        /// </summary>
        /// <remarks>
        /// Read back out of the document the way a validator would — by stable id and by catalog
        /// lookup, with nothing passed through from the generator — so what is measured is what a
        /// saved map holds rather than what the stage believed it placed.
        /// </remarks>
        sealed class BoundaryRun
        {
            /// <summary>Edges a rectangle has.</summary>
            public const int EdgeCount = 4;

            readonly int[] _counts = new int[EdgeCount];
            readonly float _coverage;

            public BoundaryRun(WorldDoc doc, Catalog catalog, ArenaLayout layout)
            {
                Rect2 field = layout.Playfield;
                var spans = new List<List<Vec2>>(EdgeCount);
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    spans.Add(new List<Vec2>());
                }

                foreach (PlacedObject placed in Segments(doc))
                {
                    Rect2 footprint = PlacedGeometry.WorldFootprint(placed, catalog);
                    int edge = EdgeOf(footprint, field);
                    _counts[edge]++;

                    // A span along the edge, held as a pair rather than as a rectangle: what is
                    // being merged is an interval on a line.
                    spans[edge].Add(edge == 0 || edge == 2
                        ? new Vec2(footprint.MinX, footprint.MaxX)
                        : new Vec2(footprint.MinZ, footprint.MaxZ));
                }

                float covered = 0f;
                for (int edge = 0; edge < EdgeCount; edge++)
                {
                    covered += Merged(spans[edge]);
                }

                _coverage = covered / (2f * (field.Width + field.Depth));
            }

            /// <summary>Share of the playfield's perimeter the boundary actually stands on.</summary>
            public float Coverage => _coverage;

            /// <summary>How many segments stand on one edge.</summary>
            public int SegmentsOn(int edge) => _counts[edge];

            /// <summary>Which edge a footprint belongs to: the nearest one it lies along.</summary>
            public static int EdgeOf(Rect2 footprint, Rect2 field)
            {
                Vec2 centre = footprint.Center;

                if (footprint.Width >= footprint.Depth)
                {
                    return centre.Y - field.MinZ <= field.MaxZ - centre.Y ? 0 : 2;
                }

                return field.MaxX - centre.X <= centre.X - field.MinX ? 1 : 3;
            }

            /// <summary>Readable name of an edge, for a failure message.</summary>
            public static string NameOf(int edge)
            {
                switch (edge)
                {
                    case 0: return "low Z";
                    case 1: return "high X";
                    case 2: return "high Z";
                    default: return "low X";
                }
            }

            /// <summary>Total length the spans cover, counting overlapping ones once.</summary>
            static float Merged(List<Vec2> spans)
            {
                spans.Sort((a, b) => a.X.CompareTo(b.X));

                float covered = 0f;
                float cursor = float.NegativeInfinity;

                for (int i = 0; i < spans.Count; i++)
                {
                    float from = MathF.Max(spans[i].X, cursor);
                    if (spans[i].Y > from)
                    {
                        covered += spans[i].Y - from;
                    }

                    cursor = MathF.Max(cursor, spans[i].Y);
                }

                return covered;
            }
        }
    }
}
