using System.Collections.Generic;
using System.Linq;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Applying manual edits on top of generated output. This is the behaviour the whole design
    /// exists for, including the part where an edit outlives the object it pointed at.
    /// </summary>
    public sealed class ResolveTests
    {
        static WorldDoc TwoObjects()
        {
            var doc = new WorldDoc { Parameters = new ArenaParams { Seed = 1UL } };
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/cover_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(1f, 0f, 2f)),
                new[] { "cover", "cover/low" },
                null));
            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/cover_01",
                "cover/low/sandbags_01",
                Pose.At(new Vec3(3f, 0f, 4f)),
                new[] { "cover", "cover/low" },
                null));
            return doc;
        }

        static PlacedObject Find(ResolvedWorld world, string stableId) =>
            world.Objects.SingleOrDefault(o => o.StableId == stableId);

        [Test]
        public void WithNoOverridesResolveReturnsTheGeneratedObjects()
        {
            ResolvedWorld resolved = TwoObjects().Resolve();

            Assert.That(resolved.Objects.Select(o => o.StableId), Is.EqualTo(new[]
            {
                "map/lane_mid/cover_00",
                "map/lane_mid/cover_01",
            }));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void MoveRelocatesTheObject()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(9f, 0f, 9f))));

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(Find(resolved, "map/lane_mid/cover_00").Pose.Position, Is.EqualTo(new Vec3(9f, 0f, 9f)));
            Assert.That(resolved.Objects.Count, Is.EqualTo(2));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void MoveKeepsEverythingElseAboutTheObject()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(9f, 0f, 9f))));

            PlacedObject moved = Find(doc.Resolve(), "map/lane_mid/cover_00");

            Assert.That(moved.LogicalId, Is.EqualTo("cover/low/crate_wood_01"));
            Assert.That(moved.Tags, Is.EqualTo(new[] { "cover", "cover/low" }));
        }

        [Test]
        public void DeleteRemovesTheObject()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Delete("map/lane_mid/cover_00"));

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Select(o => o.StableId), Is.EqualTo(new[] { "map/lane_mid/cover_01" }));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void AddIntroducesANewObject()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Add(
                "user/crate_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(-5f, 0f, -5f)),
                new[] { "cover", "user" },
                new Dictionary<string, string> { { "note", "by hand" } }));

            ResolvedWorld resolved = doc.Resolve();
            PlacedObject added = Find(resolved, "user/crate_00");

            Assert.That(resolved.Objects.Count, Is.EqualTo(3));
            Assert.That(added.LogicalId, Is.EqualTo("cover/low/crate_wood_01"));
            Assert.That(added.Pose.Position, Is.EqualTo(new Vec3(-5f, 0f, -5f)));
            Assert.That(added.Tags, Is.EqualTo(new[] { "cover", "user" }));
            Assert.That(added.Metadata["note"], Is.EqualTo("by hand"));
        }

        [Test]
        public void SwapAssetChangesTheEntryButNotThePose()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.SwapAsset(
                "map/lane_mid/cover_00", "cover/high/barrier_concrete_01"));

            PlacedObject swapped = Find(doc.Resolve(), "map/lane_mid/cover_00");

            Assert.That(swapped.LogicalId, Is.EqualTo("cover/high/barrier_concrete_01"));
            Assert.That(swapped.Pose.Position, Is.EqualTo(new Vec3(1f, 0f, 2f)));
        }

        [TestCase(OverrideOp.Move)]
        [TestCase(OverrideOp.Delete)]
        [TestCase(OverrideOp.SwapAsset)]
        public void AnOverrideTargetingAMissingIdIsOrphanedRatherThanThrowing(OverrideOp op)
        {
            WorldDoc doc = TwoObjects();
            EditOverride edit = op switch
            {
                OverrideOp.Move => EditOverride.Move("map/lane_mid/cover_99", Pose.At(Vec3.Zero)),
                OverrideOp.Delete => EditOverride.Delete("map/lane_mid/cover_99"),
                _ => EditOverride.SwapAsset("map/lane_mid/cover_99", "cover/low/sandbags_01"),
            };
            doc.Overrides.Add(edit);

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(2), "the map is untouched");
            Assert.That(resolved.OrphanedOverrides, Is.EqualTo(new[] { edit }));
        }

        [Test]
        public void OrphanedOverridesSurviveAlongsideAppliedOnes()
        {
            WorldDoc doc = TwoObjects();
            EditOverride applied = EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(7f, 0f, 7f)));
            EditOverride orphan = EditOverride.Move("map/lane_west/cover_04", Pose.At(Vec3.Zero));
            doc.Overrides.Add(applied);
            doc.Overrides.Add(orphan);

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(Find(resolved, "map/lane_mid/cover_00").Pose.Position, Is.EqualTo(new Vec3(7f, 0f, 7f)));
            Assert.That(resolved.OrphanedOverrides, Is.EqualTo(new[] { orphan }));
        }

        [Test]
        public void RegeneratingUnderANewSeedOrphansEditsWhoseTargetIsGone()
        {
            // The scenario the design exists to handle: the user moves a crate, then changes a
            // parameter and the generator stops producing that crate.
            WorldDoc before = TwoObjects();
            before.Overrides.Add(EditOverride.Move("map/lane_mid/cover_01", Pose.At(new Vec3(6f, 0f, 6f))));
            Assert.That(before.Resolve().OrphanedOverrides, Is.Empty);

            var after = new WorldDoc { Parameters = new ArenaParams { Seed = 2UL } };
            after.GeneratedObjects.Add(before.GeneratedObjects[0]);
            after.Overrides.AddRange(before.Overrides);

            ResolvedWorld resolved = after.Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(1));
            Assert.That(resolved.OrphanedOverrides.Count, Is.EqualTo(1));
            Assert.That(resolved.OrphanedOverrides[0].TargetId, Is.EqualTo("map/lane_mid/cover_01"));
        }

        [Test]
        public void OverridesApplyInListOrder()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Delete("map/lane_mid/cover_00"));
            EditOverride afterDelete = EditOverride.Move("map/lane_mid/cover_00", Pose.At(Vec3.Zero));
            doc.Overrides.Add(afterDelete);

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Select(o => o.StableId), Is.EqualTo(new[] { "map/lane_mid/cover_01" }));
            Assert.That(resolved.OrphanedOverrides, Is.EqualTo(new[] { afterDelete }));
        }

        [Test]
        public void TheLastMoveWins()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(1f, 0f, 1f))));
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(2f, 0f, 2f))));

            Assert.That(Find(doc.Resolve(), "map/lane_mid/cover_00").Pose.Position,
                Is.EqualTo(new Vec3(2f, 0f, 2f)));
        }

        [Test]
        public void AnAddCollidingWithAnExistingIdIsOrphaned()
        {
            WorldDoc doc = TwoObjects();
            EditOverride collision = EditOverride.Add(
                "map/lane_mid/cover_00", "cover/low/sandbags_01", Pose.At(Vec3.Zero));
            doc.Overrides.Add(collision);

            ResolvedWorld resolved = doc.Resolve();

            Assert.That(resolved.Objects.Count, Is.EqualTo(2));
            Assert.That(Find(resolved, "map/lane_mid/cover_00").LogicalId,
                Is.EqualTo("cover/low/crate_wood_01"), "the generated object is not overwritten");
            Assert.That(resolved.OrphanedOverrides, Is.EqualTo(new[] { collision }));
        }

        [Test]
        public void AnAddedObjectCanBeMovedByALaterOverride()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Add(
                "user/crate_00", "cover/low/crate_wood_01", Pose.At(Vec3.Zero)));
            doc.Overrides.Add(EditOverride.Move("user/crate_00", Pose.At(new Vec3(0f, 0f, 8f))));

            Assert.That(Find(doc.Resolve(), "user/crate_00").Pose.Position, Is.EqualTo(new Vec3(0f, 0f, 8f)));
        }

        [Test]
        public void ResolveDoesNotMutateTheDocument()
        {
            WorldDoc doc = TwoObjects();
            doc.Overrides.Add(EditOverride.Move("map/lane_mid/cover_00", Pose.At(new Vec3(9f, 0f, 9f))));
            doc.Overrides.Add(EditOverride.Delete("map/lane_mid/cover_01"));
            doc.Overrides.Add(EditOverride.Add("user/crate_00", "cover/low/crate_wood_01", Pose.At(Vec3.Zero)));

            doc.Resolve();

            Assert.That(doc.GeneratedObjects.Count, Is.EqualTo(2));
            Assert.That(doc.GeneratedObjects[0].Pose.Position, Is.EqualTo(new Vec3(1f, 0f, 2f)));
            Assert.That(doc.GeneratedObjects[1].StableId, Is.EqualTo("map/lane_mid/cover_01"));
        }

        [Test]
        public void ResolveIsRepeatable()
        {
            WorldDoc doc = TestWorlds.SampleWorld();

            ResolvedWorld first = doc.Resolve();
            ResolvedWorld second = doc.Resolve();

            Assert.That(second.Objects.Select(o => o.StableId), Is.EqualTo(first.Objects.Select(o => o.StableId)));
            Assert.That(second.Objects.Select(o => o.Pose), Is.EqualTo(first.Objects.Select(o => o.Pose)));
        }

        [Test]
        public void TheSampleWorldResolvesToTheExpectedMap()
        {
            ResolvedWorld resolved = TestWorlds.SampleWorld().Resolve();

            Assert.That(resolved.Objects.Select(o => o.StableId), Is.EqualTo(new[]
            {
                "map/spawn_a/marker",
                "map/spawn_b/marker",
                "map/lane_mid/structure_00",
                "map/lane_north/cover_00",
                "user/crate_00",
            }));
            Assert.That(Find(resolved, "map/lane_mid/structure_00").LogicalId,
                Is.EqualTo("structure/house/small_01"));
            Assert.That(Find(resolved, "map/lane_north/cover_00").Pose.Position,
                Is.EqualTo(new Vec3(-12f, 0f, 9.5f)));
            Assert.That(resolved.OrphanedOverrides, Is.Empty);
        }

        [Test]
        public void DuplicateGeneratedIdsAreRejected()
        {
            var doc = new WorldDoc();
            var duplicate = new PlacedObject(
                "map/lane_mid/cover_00", "cover/low/crate_wood_01", Pose.Identity, null, null);
            doc.GeneratedObjects.Add(duplicate);
            doc.GeneratedObjects.Add(duplicate);

            Assert.Throws<System.InvalidOperationException>(() => doc.Resolve());
        }
    }
}
