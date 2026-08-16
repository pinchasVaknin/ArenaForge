using System;
using System.Collections.Generic;
using System.Linq;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Catalog ordering and tag queries. Order matters as much as membership here: a weighted
    /// pick walks the query result, so an unstable order would place different props from the
    /// same seed.
    /// </summary>
    public sealed class CatalogTests
    {
        static string[] Ids(IEnumerable<CatalogEntry> entries) => entries.Select(e => e.LogicalId).ToArray();

        [Test]
        public void EntriesAreSortedByLogicalIdRegardlessOfInputOrder()
        {
            Assert.That(Ids(TestWorlds.SampleCatalog().Entries), Is.EqualTo(TestWorlds.SampleCatalogOrder));
        }

        [Test]
        public void ReorderingTheInputDoesNotChangeCatalogOrder()
        {
            CatalogEntry[] entries = TestWorlds.SampleCatalog().Entries.ToArray();
            Array.Reverse(entries);

            Assert.That(Ids(new Catalog(entries).Entries), Is.EqualTo(TestWorlds.SampleCatalogOrder));
        }

        [Test]
        public void DuplicateLogicalIdsAreRejected()
        {
            CatalogEntry entry = TestWorlds.SampleCatalog().Entries[0];
            Assert.Throws<ArgumentException>(() => new Catalog(new[] { entry, entry }));
        }

        [Test]
        public void RequireAllMatchesEntriesCarryingEveryTag()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            Assert.That(Ids(catalog.Query(TagQuery.All("cover", "cover/low"))), Is.EqualTo(new[]
            {
                "cover/low/crate_wood_01",
                "cover/low/sandbags_01",
            }));
        }

        [Test]
        public void RequireAnyMatchesTheUnion()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            Assert.That(Ids(catalog.Query(TagQuery.All("structure").WithAny("structure/house", "structure/building"))),
                Is.EqualTo(new[]
                {
                    "structure/building/two_storey_01",
                    "structure/house/small_01",
                }));
        }

        [Test]
        public void ExcludeRemovesMatchingEntries()
        {
            Catalog catalog = TestWorlds.SampleCatalog();

            Assert.That(Ids(catalog.Query(TagQuery.All("cover").WithExclude("cover/high"))), Is.EqualTo(new[]
            {
                "cover/low/crate_wood_01",
                "cover/low/sandbags_01",
            }));
        }

        [Test]
        public void AnEmptyQueryMatchesEverythingInCatalogOrder()
        {
            Assert.That(Ids(TestWorlds.SampleCatalog().Query(TagQuery.Empty)),
                Is.EqualTo(TestWorlds.SampleCatalogOrder));
        }

        [Test]
        public void ADefaultQueryMatchesEverything()
        {
            // default(TagQuery) skips the constructor, so its three sets are null. It must still
            // behave like the empty query rather than throwing.
            Assert.That(Ids(TestWorlds.SampleCatalog().Query(default)), Is.EqualTo(TestWorlds.SampleCatalogOrder));
        }

        [Test]
        public void AQueryThatMatchesNothingReturnsAnEmptyList()
        {
            Assert.That(TestWorlds.SampleCatalog().Query(TagQuery.All("vehicle")), Is.Empty);
        }

        [Test]
        public void QueryResultsAreStableAcrossRepeatedCalls()
        {
            Catalog first = TestWorlds.SampleCatalog();
            Catalog second = TestWorlds.SampleCatalog();
            var query = TagQuery.All("cover");

            for (int i = 0; i < 25; i++)
            {
                Assert.That(Ids(second.Query(query)), Is.EqualTo(Ids(first.Query(query))));
            }
        }

        [Test]
        public void WeightedPickOverAQueryIsReproducible()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            IReadOnlyList<CatalogEntry> cover = catalog.Query(TagQuery.All("cover"));

            var first = new Rng(4321UL);
            var second = new Rng(4321UL);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(
                    second.WeightedPick(cover, e => e.Weight).LogicalId,
                    Is.EqualTo(first.WeightedPick(cover, e => e.Weight).LogicalId));
            }
        }

        [Test]
        public void EntriesCarryTheirSockets()
        {
            CatalogEntry crate = TestWorlds.SampleCatalog().Entries
                .Single(e => e.LogicalId == "cover/low/crate_wood_01");

            Assert.That(crate.Sockets.Count, Is.EqualTo(1));
            Assert.That(crate.Sockets[0].Name, Is.EqualTo("top"));
            Assert.That(crate.Sockets[0].HasTag("prop_surface"), Is.True);
            Assert.That(crate.Sockets[0].LocalPose.Position, Is.EqualTo(new Vec3(0f, 1f, 0f)));
        }

        [Test]
        public void ASocketResolvesAgainstItsParentPose()
        {
            CatalogEntry crate = TestWorlds.SampleCatalog().Entries
                .Single(e => e.LogicalId == "cover/low/crate_wood_01");

            var parent = new Pose(new Vec3(4f, 0f, -3f), TestWorlds.HalfTurn, 1f);
            Pose socket = parent.Transform(crate.Sockets[0].LocalPose);

            Assert.That(socket.Position.X, Is.EqualTo(4f).Within(1e-5f));
            Assert.That(socket.Position.Y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(socket.Position.Z, Is.EqualTo(-3f).Within(1e-5f));
        }

        [Test]
        public void AnEntryWithoutAPositiveWeightIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogEntry(
                "cover/low/broken", new[] { "cover" }, new Rect2(-0.5f, -0.5f, 0.5f, 0.5f), 1f, 0f, null));
        }

        [Test]
        public void AnEntryWithABlankIdIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new CatalogEntry(
                "  ", new[] { "cover" }, new Rect2(-0.5f, -0.5f, 0.5f, 0.5f), 1f, 1f, null));
        }

        [Test]
        public void AnEntryWithDuplicateSocketNamesIsRejected()
        {
            var socket = new CatalogSocket("top", null, Pose.Identity);
            Assert.Throws<ArgumentException>(() => new CatalogEntry(
                "cover/low/twin_sockets",
                null,
                Rect2.Zero,
                1f,
                1f,
                new[] { socket, socket }));
        }
    }
}
