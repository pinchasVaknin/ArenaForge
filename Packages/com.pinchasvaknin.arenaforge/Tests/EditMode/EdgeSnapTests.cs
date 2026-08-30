using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The edge snap: which neighbour pulls a dragged footprint, how far, and when none does.
    /// </summary>
    /// <remarks>
    /// Arithmetic on two rectangles, tested as arithmetic. What it is measured against in the
    /// editor — the resolved map minus the object being dragged — is
    /// <see cref="ArenaForge.Editor.ArenaEditCapture"/>'s, and the interesting failures there are
    /// about which rectangles go in rather than what is done with them.
    /// </remarks>
    public sealed class EdgeSnapTests
    {
        const float Reach = 0.5f;

        static Rect2 Box(float minX, float minZ, float maxX, float maxZ) =>
            new Rect2(minX, minZ, maxX, maxZ);

        static List<Rect2> Near(params Rect2[] boxes) => new List<Rect2>(boxes);

        [Test]
        public void AFootprintDroppedShortOfItsNeighbourIsPulledFlush()
        {
            // A two-metre wall ending at x = 2, and another dropped with a 0.2 m seam after it.
            Rect2 wall = Box(0f, 0f, 2f, 0.3f);
            Rect2 dropped = Box(2.2f, 0f, 4.2f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(wall), Reach, out Vec2 offset), Is.True);
            Assert.That(offset.X, Is.EqualTo(-0.2f).Within(1e-5f), "closes the seam");
            Assert.That(offset.Y, Is.EqualTo(0f).Within(1e-5f), "already in line across the run");
        }

        [Test]
        public void AFootprintDroppedPastItsNeighbourIsPushedBack()
        {
            Rect2 wall = Box(0f, 0f, 2f, 0.3f);
            Rect2 dropped = Box(1.85f, 0f, 3.85f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(wall), Reach, out Vec2 offset), Is.True);
            Assert.That(offset.X, Is.EqualTo(0.15f).Within(1e-5f));
        }

        /// <remarks>
        /// The property the two independent one-dimensional snaps exist for. Along the run the
        /// boxes are adjacent, so the offset that wins butts them together; across it they overlap,
        /// so the offset that wins lines their edges up. One two-dimensional snap would have to
        /// choose between those answers.
        /// </remarks>
        [Test]
        public void AWallOffsetOnBothAxesComesBackAsAContinuationOfTheRun()
        {
            Rect2 wall = Box(0f, 0f, 2f, 0.3f);
            Rect2 dropped = Box(2.2f, 0.2f, 4.2f, 0.5f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(wall), Reach, out Vec2 offset), Is.True);
            Assert.That(offset.X, Is.EqualTo(-0.2f).Within(1e-5f), "butted along the run");
            Assert.That(offset.Y, Is.EqualTo(-0.2f).Within(1e-5f), "lined up across it");
        }

        [Test]
        public void NothingWithinReachPullsNothing()
        {
            Rect2 wall = Box(0f, 0f, 2f, 0.3f);
            Rect2 dropped = Box(3f, 0f, 5f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(wall), Reach, out Vec2 offset), Is.False);
            Assert.That(offset, Is.EqualTo(Vec2.Zero));
        }

        /// <remarks>
        /// The gate that keeps every object on the map out of every drag. A crate directly ahead
        /// across the lane is near on X and nowhere near on Z, and lining a wall up with it would be
        /// lining it up with something the user cannot see a relationship to.
        /// </remarks>
        [Test]
        public void ANeighbourFarAwayOnTheOtherAxisDoesNotPull()
        {
            Rect2 crate = Box(2.2f, 20f, 3.2f, 21f);
            Rect2 dropped = Box(0f, 0f, 2f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(crate), Reach, out Vec2 _), Is.False);
        }

        [Test]
        public void TheNearestEdgeWins()
        {
            Rect2 far = Box(0f, 0f, 2f, 0.3f);
            Rect2 near = Box(2.45f, 0f, 4.45f, 0.3f);
            Rect2 dropped = Box(2.2f, 0f, 2.4f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(far, near), Reach, out Vec2 offset), Is.True);

            // Its right edge is 0.05 from the near wall and its left edge 0.2 from the far one.
            Assert.That(offset.X, Is.EqualTo(0.05f).Within(1e-5f));
        }

        /// <remarks>
        /// Two neighbours the same distance away is a real arrangement — a gap exactly one piece
        /// wide — and which way it goes has to be a fact about the list rather than about the order
        /// the loop happened to run in.
        /// </remarks>
        [Test]
        public void AnExactTieBreaksTowardsTheFirstNeighbourInTheList()
        {
            Rect2 left = Box(0f, 0f, 2f, 0.3f);
            Rect2 right = Box(3f, 0f, 5f, 0.3f);
            Rect2 dropped = Box(2.1f, 0f, 2.9f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(left, right), Reach, out Vec2 first), Is.True);
            Assert.That(EdgeSnap.TryFlush(dropped, Near(right, left), Reach, out Vec2 second), Is.True);

            Assert.That(first.X, Is.EqualTo(-0.1f).Within(1e-5f), "pulled back onto the left wall");
            Assert.That(second.X, Is.EqualTo(0.1f).Within(1e-5f), "pulled on to the right one");
        }

        [Test]
        public void AReachOfZeroSnapsNothing()
        {
            Rect2 wall = Box(0f, 0f, 2f, 0.3f);
            Rect2 dropped = Box(2.01f, 0f, 4.01f, 0.3f);

            Assert.That(EdgeSnap.TryFlush(dropped, Near(wall), 0f, out Vec2 _), Is.False);
        }

        [Test]
        public void AnEmptyMapSnapsNothing()
        {
            Assert.That(
                EdgeSnap.TryFlush(Box(0f, 0f, 1f, 1f), Near(), Reach, out Vec2 _), Is.False);
        }
    }
}
