using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Determinism of the seeded generator. Everything downstream — reproducible stable ids,
    /// byte-identical documents, replayable failing seeds — rests on these.
    /// </summary>
    public sealed class RngTests
    {
        static uint[] Draw(Rng rng, int count)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = rng.NextUInt();
            }

            return values;
        }

        [Test]
        public void SameSeedProducesSameSequence()
        {
            Assert.That(Draw(new Rng(12345UL), 64), Is.EqualTo(Draw(new Rng(12345UL), 64)));
        }

        [Test]
        public void DifferentSeedsProduceDifferentSequences()
        {
            Assert.That(Draw(new Rng(12345UL), 64), Is.Not.EqualTo(Draw(new Rng(12346UL), 64)));
        }

        [Test]
        public void SeedZeroStillProducesAVaryingSequence()
        {
            uint[] draws = Draw(new Rng(0UL), 32);
            Assert.That(draws, Is.Unique);
        }

        [Test]
        public void ForkWithDifferentLabelsProducesDifferentStreams()
        {
            var parent = new Rng(99UL);
            uint[] lanes = Draw(parent.Fork("lanes"), 32);
            uint[] structures = Draw(parent.Fork("structures"), 32);

            Assert.That(lanes, Is.Not.EqualTo(structures));
        }

        [Test]
        public void ForkWithTheSameLabelIsRepeatable()
        {
            var parent = new Rng(99UL);
            Assert.That(Draw(parent.Fork("lanes"), 32), Is.EqualTo(Draw(parent.Fork("lanes"), 32)));
        }

        [Test]
        public void ForkDoesNotPerturbTheParentStream()
        {
            var untouched = new Rng(2024UL);
            uint[] expected = Draw(untouched, 32);

            var forked = new Rng(2024UL);
            forked.Fork("lanes");
            forked.Fork("structures");
            uint[] actual = Draw(forked, 32);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ForkDependsOnTheSeedNotTheStreamPosition()
        {
            // Adding a draw to an early stage must not reshuffle a later stage's stream.
            var early = new Rng(7UL);
            uint[] before = Draw(early.Fork("cover"), 16);

            early.NextUInt();
            early.NextUInt();
            uint[] after = Draw(early.Fork("cover"), 16);

            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void ForkOfAForkIsIndependentOfItsParentLabel()
        {
            var root = new Rng(5UL);
            Rng north = root.Fork("lane_north");
            Rng south = root.Fork("lane_south");

            Assert.That(Draw(north.Fork("cover"), 16), Is.Not.EqualTo(Draw(south.Fork("cover"), 16)));
        }

        [Test]
        public void NextFloatStaysInTheUnitInterval()
        {
            var rng = new Rng(31337UL);
            for (int i = 0; i < 100000; i++)
            {
                float value = rng.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void NextFloatHasAPlausibleMean()
        {
            var rng = new Rng(8888UL);
            double total = 0d;
            const int samples = 200000;
            for (int i = 0; i < samples; i++)
            {
                total += rng.NextFloat();
            }

            Assert.That(total / samples, Is.EqualTo(0.5d).Within(0.005d));
        }

        [Test]
        public void NextRangeIntStaysInBoundsAndCoversThem()
        {
            var rng = new Rng(4242UL);
            var seen = new HashSet<int>();
            for (int i = 0; i < 10000; i++)
            {
                int value = rng.NextRange(-3, 4);
                Assert.That(value, Is.InRange(-3, 3));
                seen.Add(value);
            }

            Assert.That(seen.Count, Is.EqualTo(7), "every value in the range should appear");
        }

        [Test]
        public void NextRangeIntRejectsAnEmptyRange()
        {
            var rng = new Rng(1UL);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextRange(5, 5));
        }

        [Test]
        public void NextRangeFloatStaysInBounds()
        {
            var rng = new Rng(55UL);
            for (int i = 0; i < 10000; i++)
            {
                float value = rng.NextRange(-2.5f, 7.5f);
                Assert.That(value, Is.GreaterThanOrEqualTo(-2.5f).And.LessThan(7.5f));
            }
        }

        [Test]
        public void PickReturnsEveryItemEventually()
        {
            var items = new[] { "a", "b", "c", "d" };
            var rng = new Rng(606UL);
            var seen = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
            {
                seen.Add(rng.Pick(items));
            }

            Assert.That(seen, Is.EquivalentTo(items));
        }

        [Test]
        public void PickRejectsAnEmptyList()
        {
            var rng = new Rng(1UL);
            Assert.Throws<ArgumentException>(() => rng.Pick(Array.Empty<string>()));
        }

        [Test]
        public void WeightedPickFollowsTheWeights()
        {
            var items = new[] { new Weighted("common", 3f), new Weighted("rare", 1f) };
            var rng = new Rng(777UL);

            int common = 0;
            const int samples = 40000;
            for (int i = 0; i < samples; i++)
            {
                if (rng.WeightedPick(items, w => w.Weight).Name == "common")
                {
                    common++;
                }
            }

            Assert.That(common / (double)samples, Is.EqualTo(0.75d).Within(0.02d));
        }

        [Test]
        public void WeightedPickSkipsZeroWeightedItems()
        {
            var items = new[] { new Weighted("never", 0f), new Weighted("always", 1f) };
            var rng = new Rng(9UL);

            for (int i = 0; i < 1000; i++)
            {
                Assert.That(rng.WeightedPick(items, w => w.Weight).Name, Is.EqualTo("always"));
            }
        }

        [Test]
        public void WeightedPickRejectsAllZeroWeights()
        {
            var items = new[] { new Weighted("a", 0f), new Weighted("b", 0f) };
            var rng = new Rng(9UL);

            Assert.Throws<ArgumentException>(() => rng.WeightedPick(items, w => w.Weight));
        }

        [Test]
        public void StableHashIsFixedAcrossRuns()
        {
            // Pinned values. If these ever change, every previously generated map's ids change
            // with them, so a deliberate change here is a schema break.
            Assert.That(StableHash.Hash64(string.Empty), Is.EqualTo(14695981039346656037UL));
            Assert.That(StableHash.Hash64("lanes"), Is.EqualTo(StableHash.Hash64("lanes")));
            Assert.That(StableHash.Hash64("lanes"), Is.Not.EqualTo(StableHash.Hash64("lane")));
            Assert.That(StableHash.Combine(1UL, "a"), Is.Not.EqualTo(StableHash.Combine(2UL, "a")));
        }

        sealed class Weighted
        {
            public Weighted(string name, float weight)
            {
                Name = name;
                Weight = weight;
            }

            public string Name { get; }

            public float Weight { get; }
        }
    }
}
