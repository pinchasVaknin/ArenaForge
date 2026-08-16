using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The occlusion model: which objects block sight, and the segment-versus-rectangle test that
    /// decides whether a given sightline crosses one.
    /// </summary>
    /// <remarks>
    /// This is the bottom of the analysis layer. Every metric that says anything about visibility
    /// is a count of <see cref="Occluder.Blocks"/> answers, so a mistake here is a mistake in every
    /// number the validator reports, and it would not look like a mistake — it would look like a
    /// map that plays more open or more sheltered than it does.
    /// </remarks>
    public sealed class OcclusionTests
    {
        static CatalogEntry Entry(string id, string tag, Rect2 footprint, float height) =>
            new CatalogEntry(id, new[] { tag }, footprint, height, 1f, null);

        static PlacedObject Placed(CatalogEntry entry, Vec3 position, Quat rotation) =>
            new PlacedObject("map/test/object", entry.LogicalId, new Pose(position, rotation, 1f), null, null);

        static Occluder Barrier(Vec2 center, int yawSteps, float halfWidth, float halfDepth)
        {
            CatalogEntry entry = Entry(
                "cover/high/test", "cover/high",
                new Rect2(-halfWidth, -halfDepth, halfWidth, halfDepth), 1.8f);

            Assert.That(
                Occluder.TryCreate(
                    Placed(entry, center.ToVec3(0f), YawStep.Rotation(yawSteps)), entry, 1.6f, out Occluder result),
                Is.True);

            return result;
        }

        [Test]
        public void ASegmentThroughARectangleIsBlockedAndOneBesideItIsNot()
        {
            Occluder wall = Barrier(Vec2.Zero, 0, 2f, 0.25f);

            Assert.That(wall.Blocks(new Vec2(0f, -3f), new Vec2(0f, 3f)), Is.True, "straight through");
            Assert.That(wall.Blocks(new Vec2(-3f, -3f), new Vec2(3f, 3f)), Is.True, "diagonally through");
            Assert.That(wall.Blocks(new Vec2(-3f, 2f), new Vec2(3f, 2f)), Is.False, "passing in front");
            Assert.That(wall.Blocks(new Vec2(3f, -3f), new Vec2(3f, 3f)), Is.False, "passing beside");
        }

        [Test]
        public void ASegmentEndingShortOfARectangleIsNotBlocked()
        {
            Occluder wall = Barrier(Vec2.Zero, 0, 2f, 0.25f);

            Assert.That(wall.Blocks(new Vec2(0f, -3f), new Vec2(0f, -1f)), Is.False,
                "a segment that stops before the wall is a sightline, not a collision");
            Assert.That(wall.Blocks(new Vec2(0f, -3f), new Vec2(0f, 0f)), Is.True,
                "a segment ending inside the wall is blocked by it");
        }

        /// <summary>
        /// The reason the model carries an oriented rectangle rather than the box around it.
        /// </summary>
        [Test]
        public void ARotatedRectangleIsTestedAsItselfAndNotAsTheBoxAroundIt()
        {
            Occluder diagonal = Barrier(Vec2.Zero, 3, 2f, 0.25f);

            // Well inside the bounds, well clear of the rectangle itself: the corner of the box a
            // barrier turned forty-five degrees does not occupy.
            var from = new Vec2(-1.5f, -1.5f);
            var to = new Vec2(-1f, -1f);

            Assert.That(diagonal.MightBlock(-1.5f, -1.5f, -1f, -1f), Is.True,
                "this segment does share the occluder's bounds, or the test proves nothing");
            Assert.That(diagonal.Blocks(from, to), Is.False,
                "the box around a rotated barrier is nearly four times its area; only the barrier blocks");
            Assert.That(diagonal.Blocks(new Vec2(-1f, -1f), new Vec2(1f, 1f)), Is.True,
                "across the barrier's short axis, though, it blocks");
        }

        [Test]
        public void TheBoundsRejectionNeverTurnsDownASegmentThatWouldHaveBeenBlocked()
        {
            Occluder diagonal = Barrier(new Vec2(3f, -2f), 5, 2f, 0.5f);
            var stream = new Rng(20260816UL);
            int blocked = 0;

            for (int i = 0; i < 20000; i++)
            {
                var from = new Vec2(stream.NextRange(-10f, 10f), stream.NextRange(-10f, 10f));
                var to = new Vec2(stream.NextRange(-10f, 10f), stream.NextRange(-10f, 10f));

                bool hit = diagonal.Blocks(from, to);
                if (hit)
                {
                    blocked++;
                    Assert.That(
                        diagonal.MightBlock(
                            MathF.Min(from.X, to.X), MathF.Min(from.Y, to.Y),
                            MathF.Max(from.X, to.X), MathF.Max(from.Y, to.Y)),
                        Is.True,
                        $"the cheap rejection discarded a segment {from}..{to} that the real test blocks");
                }
            }

            Assert.That(blocked, Is.GreaterThan(0), "no segment was blocked, so nothing was tested");
        }

        [Test]
        public void OnlyWhatStandsAcrossTheEyeLineOccludes()
        {
            var footprint = new Rect2(-1f, -1f, 1f, 1f);
            CatalogEntry low = Entry("cover/low/crate", "cover/low", footprint, 1f);
            CatalogEntry high = Entry("cover/high/barrier", "cover/high", footprint, 1.8f);

            Assert.That(
                Occluder.TryCreate(Placed(low, Vec3.Zero, Quat.Identity), low, 1.6f, out _), Is.False,
                "a metre-tall crate does not block a sightline taken at 1.6 m");
            Assert.That(
                Occluder.TryCreate(Placed(high, Vec3.Zero, Quat.Identity), high, 1.6f, out _), Is.True,
                "a barrier taller than eye height does");
        }

        [Test]
        public void EyeHeightIsWhatDecidesIt()
        {
            CatalogEntry barrier = Entry(
                "cover/high/barrier", "cover/high", new Rect2(-1f, -1f, 1f, 1f), 1.8f);
            PlacedObject placed = Placed(barrier, Vec3.Zero, Quat.Identity);

            Assert.That(Occluder.TryCreate(placed, barrier, 1.6f, out _), Is.True);
            Assert.That(Occluder.TryCreate(placed, barrier, 2f, out _), Is.False,
                "raise the eye above the barrier and it stops being an occluder");
        }

        /// <summary>
        /// The height rule is about the span an object occupies, not about its own height, which is
        /// the only reading that gets a crate on a socket right.
        /// </summary>
        [Test]
        public void APropRaisedOnASocketOccludesEvenThoughItIsShort()
        {
            CatalogEntry crate = Entry(
                "cover/low/crate", "cover/low", new Rect2(-0.5f, -0.5f, 0.5f, 0.5f), 1f);

            Assert.That(
                Occluder.TryCreate(Placed(crate, Vec3.Zero, Quat.Identity), crate, 1.6f, out _),
                Is.False, "on the ground it spans 0 m to 1 m, which is below the eye");
            Assert.That(
                Occluder.TryCreate(Placed(crate, new Vec3(0f, 1f, 0f), Quat.Identity), crate, 1.6f, out _),
                Is.True, "on a socket a metre up it spans 1 m to 2 m, which the eye line runs through");
            Assert.That(
                Occluder.TryCreate(Placed(crate, new Vec3(0f, 3f, 0f), Quat.Identity), crate, 1.6f, out _),
                Is.False, "three metres up it is over your head");
        }

        [Test]
        public void AnOccludersBoundsCoverEveryCornerOfIt()
        {
            for (int steps = 0; steps < YawStep.Count; steps++)
            {
                Occluder turned = Barrier(new Vec2(2f, -1f), steps, 2f, 0.5f);
                Rect2 bounds = turned.Bounds;

                Vec2 axis = turned.AxisX;
                var across = new Vec2(-axis.Y, axis.X);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vec2 point = turned.Center +
                                 axis * (turned.HalfWidth * ((corner & 1) == 0 ? 1f : -1f)) +
                                 across * (turned.HalfDepth * ((corner & 2) == 0 ? 1f : -1f));

                    Assert.That(bounds.Expanded(1e-4f).Contains(point), Is.True,
                        $"step {steps}: corner {point} lies outside the bounds {bounds}");
                }
            }
        }

        [Test]
        public void TheGeneratorsHighCoverAndStructuresAreTheOccluders()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            WorldDoc doc = ArenaLayoutGenerator.Generate(new ArenaParams { Seed = 7UL }, catalog);

            var occluding = new List<string>();
            foreach (PlacedObject placed in doc.GeneratedObjects)
            {
                CatalogEntry entry = catalog.Find(placed.LogicalId);
                if (Occluder.TryCreate(placed, entry, 1.6f, out _))
                {
                    occluding.Add(placed.LogicalId);
                }
            }

            Assert.That(occluding, Does.Contain("structure/building/two_storey_01"));
            Assert.That(occluding, Does.Contain("structure/house/small_01"));
            Assert.That(occluding, Does.Contain("cover/high/barrier_concrete_01"));
            Assert.That(occluding, Does.Not.Contain("cover/low/sandbags_01"),
                "sandbags are 0.9 m; nobody is hidden behind them standing up");
            Assert.That(occluding, Does.Not.Contain("marker/spawn"),
                "a spawn marker is not geometry");
        }
    }
}
