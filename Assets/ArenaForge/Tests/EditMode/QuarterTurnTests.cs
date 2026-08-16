using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The quarter-turn table. Placement uses it instead of <see cref="Quat.FromYawDegrees"/>
    /// precisely because a table of literals cannot vary with the runtime's trigonometry, so these
    /// tests check the table agrees with the trigonometry to within float tolerance and is exact
    /// where it matters.
    /// </summary>
    public sealed class QuarterTurnTests
    {
        [Test]
        public void TheIdentityAndHalfTurnAreExact()
        {
            Assert.That(QuarterTurn.Rotation(0), Is.EqualTo(new Quat(0f, 0f, 0f, 1f)));
            Assert.That(QuarterTurn.Rotation(2), Is.EqualTo(new Quat(0f, 1f, 0f, 0f)));
        }

        [Test]
        public void EveryEntryIsAUnitQuaternion()
        {
            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Assert.That(QuarterTurn.Rotation(turns).Length, Is.EqualTo(1f).Within(1e-6f), $"turn {turns}");
            }
        }

        [Test]
        public void EveryEntryMatchesTheTrigonometricRotation()
        {
            // Compared by what the rotation does rather than by its components: the table is
            // written in the w >= 0 form, and 270 degrees comes out of the half-angle formula as
            // the negated — and equivalent — quaternion.
            var probes = new[] { new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 1f), new Vec3(2f, 3f, -4f) };

            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Quat expected = Quat.FromYawDegrees(QuarterTurn.Degrees(turns));
                Quat actual = QuarterTurn.Rotation(turns);

                Assert.That(actual.W, Is.GreaterThanOrEqualTo(0f), $"turn {turns} is not in the w >= 0 form");

                foreach (Vec3 probe in probes)
                {
                    Vec3 want = expected.Rotate(probe);
                    Vec3 got = actual.Rotate(probe);

                    Assert.That(got.X, Is.EqualTo(want.X).Within(1e-5f), $"turn {turns} on {probe} x");
                    Assert.That(got.Y, Is.EqualTo(want.Y).Within(1e-5f), $"turn {turns} on {probe} y");
                    Assert.That(got.Z, Is.EqualTo(want.Z).Within(1e-5f), $"turn {turns} on {probe} z");
                }
            }
        }

        [Test]
        public void AQuarterTurnTakesForwardToTheRight()
        {
            Vec3 rotated = QuarterTurn.Rotation(1).Rotate(new Vec3(0f, 0f, 1f));

            Assert.That(rotated.X, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(rotated.Z, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void RotatingAFootprintAgreesWithRotatingItsCorners()
        {
            var footprint = new Rect2(-6f, -5f, 6f, 5f);

            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Rect2 actual = QuarterTurn.Rotate(footprint, turns);
                Quat rotation = QuarterTurn.Rotation(turns);

                Vec3 a = rotation.Rotate(new Vec3(footprint.MinX, 0f, footprint.MinZ));
                Vec3 b = rotation.Rotate(new Vec3(footprint.MaxX, 0f, footprint.MaxZ));
                Rect2 expected = Rect2.FromCorners(a.Xz, b.Xz);

                Assert.That(actual.MinX, Is.EqualTo(expected.MinX).Within(1e-5f), $"turn {turns} minX");
                Assert.That(actual.MinZ, Is.EqualTo(expected.MinZ).Within(1e-5f), $"turn {turns} minZ");
                Assert.That(actual.MaxX, Is.EqualTo(expected.MaxX).Within(1e-5f), $"turn {turns} maxX");
                Assert.That(actual.MaxZ, Is.EqualTo(expected.MaxZ).Within(1e-5f), $"turn {turns} maxZ");
            }
        }

        [Test]
        public void AQuarterTurnSwapsWidthAndDepthExactly()
        {
            var footprint = new Rect2(-6f, -5f, 6f, 5f);

            Rect2 turned = QuarterTurn.Rotate(footprint, 1);

            Assert.That(turned.Width, Is.EqualTo(footprint.Depth));
            Assert.That(turned.Depth, Is.EqualTo(footprint.Width));
        }

        [Test]
        public void FourQuarterTurnsReturnToTheStart()
        {
            var footprint = new Rect2(-2f, -1f, 3f, 4f);

            Rect2 turned = footprint;
            for (int i = 0; i < 4; i++)
            {
                turned = QuarterTurn.Rotate(turned, 1);
            }

            Assert.That(turned, Is.EqualTo(footprint));
            Assert.That(QuarterTurn.Rotation(4), Is.EqualTo(QuarterTurn.Rotation(0)));
            Assert.That(QuarterTurn.Rotation(-1), Is.EqualTo(QuarterTurn.Rotation(3)));
        }
    }
}
