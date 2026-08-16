using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The lane and spawn skeleton. Everything the generator places is positioned against this, so
    /// a layout that leaves gaps, overlaps or drifts off the grid corrupts every later stage.
    /// </summary>
    public sealed class ArenaLayoutTests
    {
        static ArenaParams Params(ulong seed) => new ArenaParams { Seed = seed };

        [Test]
        public void TheGridTilesThePlayfieldFromItsLowerCorner()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));

            Assert.That(layout.Grid.CountX, Is.EqualTo(60));
            Assert.That(layout.Grid.CountZ, Is.EqualTo(60));
            Assert.That(layout.Grid.CellBounds(0, 0).Min, Is.EqualTo(layout.Playfield.Min));
            Assert.That(layout.Grid.CellBounds(59, 59).Max, Is.EqualTo(layout.Playfield.Max));
            Assert.That(layout.Grid.CellCenter(0, 0), Is.EqualTo(new Vec2(-29.5f, -29.5f)));
        }

        [Test]
        public void SnappingLandsOnGridIntersections()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));

            Assert.That(layout.SnapAlong(3.4f), Is.EqualTo(3f));
            Assert.That(layout.SnapAlong(3.6f), Is.EqualTo(4f));
            Assert.That(layout.SnapAlong(-3.5f), Is.EqualTo(-3f),
                "a coordinate halfway between two lines always resolves the same way");
            Assert.That(layout.Grid.IsOnGrid(new Vec2(layout.SnapCross(7.2f), layout.SnapAlong(-11.9f))), Is.True);
        }

        [Test]
        public void SnappingHonoursAGridThatIsNotOneMetre()
        {
            ArenaLayout layout = ArenaLayout.Build(new ArenaParams { Seed = 1UL, GridSize = 2.5f });

            Assert.That(layout.Grid.CellSize, Is.EqualTo(2.5f));
            Assert.That(layout.SnapAlong(1f), Is.EqualTo(0f));
            Assert.That(layout.SnapAlong(1.5f), Is.EqualTo(2.5f));
        }

        [Test]
        public void ThreeLanesAreNamedNorthMidSouth()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(1UL));

            Assert.That(layout.Lanes.Count, Is.EqualTo(3));
            Assert.That(layout.Lanes[0].Id, Is.EqualTo("lane_north"));
            Assert.That(layout.Lanes[1].Id, Is.EqualTo("lane_mid"));
            Assert.That(layout.Lanes[2].Id, Is.EqualTo("lane_south"));
            Assert.That(layout.MiddleLaneIndex, Is.EqualTo(1));
        }

        [TestCase(1, new[] { "lane_mid" })]
        [TestCase(2, new[] { "lane_north", "lane_south" })]
        [TestCase(4, new[] { "lane_north_02", "lane_north_01", "lane_south_01", "lane_south_02" })]
        [TestCase(5, new[] { "lane_north_02", "lane_north_01", "lane_mid", "lane_south_01", "lane_south_02" })]
        public void LaneIdsAreReadableAtEveryLaneCount(int laneCount, string[] expected)
        {
            ArenaLayout layout = ArenaLayout.Build(new ArenaParams { Seed = 7UL, LaneCount = laneCount });

            var actual = new string[layout.Lanes.Count];
            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                actual[i] = layout.Lanes[i].Id;
            }

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void LaneBandsAreSeparatedGridAlignedAndInsideThePlayfield()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                for (int laneCount = 1; laneCount <= 5; laneCount++)
                {
                    ArenaLayout layout = ArenaLayout.Build(
                        new ArenaParams { Seed = seed, LaneCount = laneCount });

                    for (int i = 0; i < layout.Lanes.Count; i++)
                    {
                        Rect2 band = layout.Lanes[i].Band;
                        Assert.That(layout.Playfield.Contains(band), Is.True,
                            $"seed {seed}, {laneCount} lanes: {layout.Lanes[i].Id} leaves the playfield");
                        Assert.That(layout.Grid.IsOnGrid(band.Min) && layout.Grid.IsOnGrid(band.Max), Is.True,
                            $"seed {seed}, {laneCount} lanes: {layout.Lanes[i].Id} is off the grid");
                        Assert.That(Cross(layout, band.Max) - Cross(layout, band.Min),
                            Is.GreaterThanOrEqualTo(layout.Grid.CellSize),
                            $"seed {seed}, {laneCount} lanes: {layout.Lanes[i].Id} is thinner than a cell");

                        if (i > 0)
                        {
                            float gap = Cross(layout, band.Min) - Cross(layout, layout.Lanes[i - 1].Band.Max);
                            Assert.That(gap, Is.GreaterThan(0f),
                                $"seed {seed}, {laneCount} lanes: no gap before {layout.Lanes[i].Id}");
                        }
                    }
                }
            }
        }

        [Test]
        public void LaneWidthsVaryWithTheSeed()
        {
            var widths = new List<float>();
            for (ulong seed = 1; seed <= 40; seed++)
            {
                float width = ArenaLayout.Build(Params(seed)).Lanes[0].Band.Width;
                if (!widths.Contains(width))
                {
                    widths.Add(width);
                }
            }

            Assert.That(widths.Count, Is.GreaterThan(1), "every seed produced the same lane widths");
        }

        [Test]
        public void SpawnAreasSitAtOppositeEndsAndSpanEveryLane()
        {
            ArenaLayout layout = ArenaLayout.Build(Params(3UL));

            Assert.That(layout.SpawnAreaA.MinZ, Is.EqualTo(layout.Playfield.MinZ));
            Assert.That(layout.SpawnAreaB.MaxZ, Is.EqualTo(layout.Playfield.MaxZ));
            Assert.That(layout.SpawnAreaA.Overlaps(layout.SpawnAreaB), Is.False);

            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                Rect2 band = layout.Lanes[i].Band;
                Assert.That(layout.SpawnAreaA.MinX, Is.LessThanOrEqualTo(band.MinX));
                Assert.That(layout.SpawnAreaA.MaxX, Is.GreaterThanOrEqualTo(band.MaxX));
                Assert.That(layout.SpawnAreaB.MinX, Is.LessThanOrEqualTo(band.MinX));
                Assert.That(layout.SpawnAreaB.MaxX, Is.GreaterThanOrEqualTo(band.MaxX));
            }
        }

        [Test]
        public void LanesRunDownTheLongerAxis()
        {
            ArenaLayout tall = ArenaLayout.Build(
                new ArenaParams { Seed = 5UL, PlayfieldSize = new Vec2(40f, 80f) });
            ArenaLayout wide = ArenaLayout.Build(
                new ArenaParams { Seed = 5UL, PlayfieldSize = new Vec2(80f, 40f) });

            Assert.That(tall.LanesRunAlongZ, Is.True);
            Assert.That(tall.LongExtent, Is.EqualTo(80f));
            Assert.That(tall.Lanes[0].Band.Depth, Is.EqualTo(80f), "a lane spans the long axis");

            Assert.That(wide.LanesRunAlongZ, Is.False);
            Assert.That(wide.LongExtent, Is.EqualTo(80f));
            Assert.That(wide.Lanes[0].Band.Width, Is.EqualTo(80f));
            Assert.That(wide.SpawnAreaA.MinX, Is.EqualTo(wide.Playfield.MinX));
        }

        [Test]
        public void APlayfieldTooSmallForItsLanesFailsWithAReadableMessage()
        {
            var error = Assert.Throws<InvalidOperationException>(() => ArenaLayout.Build(
                new ArenaParams { Seed = 1UL, PlayfieldSize = new Vec2(6f, 60f), LaneCount = 5 }));

            Assert.That(error.Message, Does.Contain("5 lanes"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ALaneCountBelowOneIsRejected(int laneCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayout.Build(
                new ArenaParams { Seed = 1UL, LaneCount = laneCount }));
        }

        [Test]
        public void ANonPositiveGridOrPlayfieldIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayout.Build(
                new ArenaParams { Seed = 1UL, GridSize = 0f }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayout.Build(
                new ArenaParams { Seed = 1UL, PlayfieldSize = new Vec2(0f, 60f) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ArenaLayout.Build(
                new ArenaParams { Seed = 1UL, StructureDensity = 0f }));
        }

        [Test]
        public void TheLayoutIsAFunctionOfTheParametersAlone()
        {
            ArenaLayout first = ArenaLayout.Build(Params(99UL));
            ArenaLayout second = ArenaLayout.Build(Params(99UL));

            for (int i = 0; i < first.Lanes.Count; i++)
            {
                Assert.That(second.Lanes[i].Id, Is.EqualTo(first.Lanes[i].Id));
                Assert.That(second.Lanes[i].Band, Is.EqualTo(first.Lanes[i].Band));
            }

            Assert.That(second.SpawnAreaA, Is.EqualTo(first.SpawnAreaA));
            Assert.That(second.SpawnAreaB, Is.EqualTo(first.SpawnAreaB));
        }

        static float Cross(ArenaLayout layout, Vec2 corner) => layout.CrossOf(corner);
    }
}
