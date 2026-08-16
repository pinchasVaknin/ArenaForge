using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// The project's seeded random number generator: PCG32 (XSH-RR variant) over a 64-bit state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>System.Random</c> is forbidden here because its algorithm is unspecified and has changed
    /// between .NET versions, and <c>UnityEngine.Random</c> because it is global mutable state
    /// shared with everything else in the process. PCG32 is a fixed, published algorithm: the same
    /// seed yields the same sequence on every machine and every run, which is what the override
    /// system and the property-based test suites are built on.
    /// </para>
    /// <para>
    /// This is a mutable struct. Copying it copies the stream position, which is deliberate —
    /// pass it by <c>ref</c> when a callee should consume draws from the caller's stream, and by
    /// value when it should not.
    /// </para>
    /// </remarks>
    public struct Rng
    {
        const ulong Multiplier = 6364136223846793005UL;

        // Any odd constant works as the stream increment; this is the standard PCG default.
        const ulong Increment = 1442695040888963407UL;

        readonly ulong _seed;
        ulong _state;

        /// <summary>Creates a generator from a seed.</summary>
        public Rng(ulong seed)
        {
            _seed = seed;
            unchecked
            {
                // Standard PCG seeding: advance, fold the seed in, advance again.
                _state = Increment;
                _state += seed;
                _state = _state * Multiplier + Increment;
            }
        }

        /// <summary>The seed this stream was created from. <see cref="Fork"/> derives from it.</summary>
        public ulong Seed => _seed;

        /// <summary>Draws the next 32-bit value and advances the stream.</summary>
        public uint NextUInt()
        {
            unchecked
            {
                ulong previous = _state;
                _state = previous * Multiplier + Increment;

                uint xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
                int rotation = (int)(previous >> 59);
                return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
            }
        }

        /// <summary>Draws a value in [0, 1).</summary>
        public float NextFloat()
        {
            // 24 bits is exactly the float mantissa, so every result is representable and the
            // distribution has no gaps or duplicates.
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>Draws an integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        /// <exception cref="ArgumentOutOfRangeException">The range is empty or inverted.</exception>
        public int NextRange(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive), maxExclusive,
                    $"Range must be non-empty; got [{minInclusive}, {maxExclusive}).");
            }

            uint range = (uint)((long)maxExclusive - minInclusive);

            // Rejection sampling. Taking the modulus of a raw draw would over-represent the low
            // end of the range whenever 2^32 is not a multiple of it.
            uint threshold = (uint)((0x100000000UL - range) % range);
            uint draw;
            do
            {
                draw = NextUInt();
            }
            while (draw < threshold);

            return (int)(minInclusive + (long)(draw % range));
        }

        /// <summary>Draws a value in [<paramref name="min"/>, <paramref name="max"/>).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is below <paramref name="min"/>.</exception>
        public float NextRange(float min, float max)
        {
            if (max < min)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(max), max, $"Range must not be inverted; got [{min}, {max}).");
            }

            return min + (max - min) * NextFloat();
        }

        /// <summary>Picks one item uniformly from a list.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="items"/> is empty.</exception>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (items.Count == 0)
            {
                throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            }

            return items[NextRange(0, items.Count)];
        }

        /// <summary>
        /// Picks one item with probability proportional to its weight. List order is part of the
        /// result, so callers must pass a list in a deterministic order — the catalog's sorted
        /// entries, not a set.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentException">The list is empty, or no item has a positive weight.</exception>
        public T WeightedPick<T>(IReadOnlyList<T> items, Func<T, float> weightSelector)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (weightSelector == null)
            {
                throw new ArgumentNullException(nameof(weightSelector));
            }

            if (items.Count == 0)
            {
                throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            }

            float total = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                float weight = weightSelector(items[i]);
                if (weight < 0f)
                {
                    throw new ArgumentException(
                        $"Weight at index {i} is negative ({weight}).", nameof(weightSelector));
                }

                total += weight;
            }

            if (total <= 0f)
            {
                throw new ArgumentException("No item has a positive weight.", nameof(weightSelector));
            }

            float target = NextFloat() * total;
            float running = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                running += weightSelector(items[i]);
                if (target < running)
                {
                    return items[i];
                }
            }

            // Only reachable if rounding leaves target at or past the accumulated total.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (weightSelector(items[i]) > 0f)
                {
                    return items[i];
                }
            }

            throw new ArgumentException("No item has a positive weight.", nameof(weightSelector));
        }

        /// <summary>
        /// Derives an independent child stream for a named subsystem.
        /// </summary>
        /// <remarks>
        /// The child is a function of this stream's <em>seed</em> and the label, not of its current
        /// position, so forking neither consumes a draw nor perturbs the parent, and adding a draw
        /// to an earlier stage cannot reshuffle a later one. The same label always yields the same
        /// child, which makes fork order irrelevant.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="label"/> is null.</exception>
        public Rng Fork(string label) => new Rng(StableHash.Combine(_seed, label));
    }
}
