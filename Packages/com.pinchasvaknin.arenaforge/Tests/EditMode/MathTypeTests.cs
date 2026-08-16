using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The engine-free math types, concentrating on the conventions that later stages depend on:
    /// what counts as an overlap, and how a rotation composes with a pose.
    /// </summary>
    public sealed class MathTypeTests
    {
        [Test]
        public void RectanglesThatOnlyTouchDoNotOverlap()
        {
            var left = new Rect2(0f, 0f, 1f, 1f);
            var right = new Rect2(1f, 0f, 2f, 1f);

            Assert.That(left.Overlaps(right), Is.False, "props may sit flush against one another");
            Assert.That(left.Overlaps(right.Translated(new Vec2(-0.001f, 0f))), Is.True);
        }

        [Test]
        public void OverlapIsSymmetric()
        {
            var a = new Rect2(0f, 0f, 2f, 2f);
            var b = new Rect2(1f, 1f, 3f, 3f);

            Assert.That(a.Overlaps(b), Is.EqualTo(b.Overlaps(a)));
        }

        [Test]
        public void ExpandingARectangleGrowsItOnEverySide()
        {
            Rect2 expanded = new Rect2(0f, 0f, 1f, 1f).Expanded(0.5f);

            Assert.That(expanded, Is.EqualTo(new Rect2(-0.5f, -0.5f, 1.5f, 1.5f)));
            Assert.That(expanded.Size, Is.EqualTo(new Vec2(2f, 2f)));
        }

        [Test]
        public void ContainsIncludesTheBoundary()
        {
            var playfield = new Rect2(-30f, -30f, 30f, 30f);

            Assert.That(playfield.Contains(new Vec2(30f, -30f)), Is.True);
            Assert.That(playfield.Contains(new Vec2(30.001f, 0f)), Is.False);
            Assert.That(playfield.Contains(new Rect2(-29f, -29f, 29f, 29f)), Is.True);
            Assert.That(playfield.Contains(new Rect2(-31f, 0f, 0f, 1f)), Is.False);
        }

        [Test]
        public void FromCenterSizeIsCentredOnItsPoint()
        {
            Rect2 rect = Rect2.FromCenterSize(new Vec2(4f, -2f), new Vec2(2f, 6f));

            Assert.That(rect, Is.EqualTo(new Rect2(3f, -5f, 5f, 1f)));
            Assert.That(rect.Center, Is.EqualTo(new Vec2(4f, -2f)));
            Assert.That(rect.Area, Is.EqualTo(12f));
        }

        [Test]
        public void AQuarterTurnRotatesForwardOntoRight()
        {
            Vec3 rotated = Quat.FromYawDegrees(90f).Rotate(new Vec3(0f, 0f, 1f));

            Assert.That(rotated.X, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(rotated.Y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(rotated.Z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void ComposingTwoQuarterTurnsGivesAHalfTurn()
        {
            Quat quarter = Quat.FromYawDegrees(90f);
            Vec3 rotated = (quarter * quarter).Rotate(new Vec3(0f, 0f, 1f));

            Assert.That(rotated.Z, Is.EqualTo(-1f).Within(1e-5f));
        }

        [Test]
        public void YawSurvivesARoundTripThroughDegrees()
        {
            Assert.That(Quat.FromYawDegrees(135f).YawDegrees, Is.EqualTo(135f).Within(1e-3f));
            Assert.That(Quat.Identity.YawDegrees, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void APoseTransformsALocalPointIntoWorldSpace()
        {
            var pose = new Pose(new Vec3(10f, 0f, 5f), Quat.FromYawDegrees(90f), 2f);

            Vec3 world = pose.TransformPoint(new Vec3(0f, 0f, 1f));

            Assert.That(world.X, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(world.Z, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void ComposingPosesMultipliesScaleAndRotation()
        {
            var parent = new Pose(new Vec3(1f, 0f, 0f), Quat.FromYawDegrees(90f), 2f);
            var child = new Pose(new Vec3(0f, 1f, 0f), Quat.FromYawDegrees(90f), 3f);

            Pose composed = parent.Transform(child);

            Assert.That(composed.Scale, Is.EqualTo(6f));
            Assert.That(composed.Position.Y, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(composed.Rotation.YawDegrees, Is.EqualTo(180f).Within(1e-3f));
        }

        [Test]
        public void VectorsUseValueEquality()
        {
            Assert.That(new Vec3(1f, 2f, 3f), Is.EqualTo(new Vec3(1f, 2f, 3f)));
            Assert.That(new Vec3(1f, 2f, 3f) == new Vec3(1f, 2f, 3f), Is.True);
            Assert.That(new Vec2(1f, 2f), Is.Not.EqualTo(new Vec2(2f, 1f)));
            Assert.That(new Vec3(1f, 2f, 3f).Xz, Is.EqualTo(new Vec2(1f, 3f)));
            Assert.That(new Vec2(1f, 3f).ToVec3(2f), Is.EqualTo(new Vec3(1f, 2f, 3f)));
        }
    }
}
