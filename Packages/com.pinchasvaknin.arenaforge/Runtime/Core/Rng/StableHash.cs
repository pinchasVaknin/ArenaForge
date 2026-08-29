using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// FNV-1a 64-bit hashing — the only hash ArenaForge is allowed to persist or derive
    /// generation decisions from.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode</c> is randomised per process on modern .NET, so two runs of the
    /// same seed would silently disagree. FNV-1a is fixed by its constants, so a label hashes
    /// to the same value on every machine, every run, forever.
    /// </remarks>
    public static class StableHash
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        /// <summary>Hashes a string.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static ulong Hash64(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return HashString(OffsetBasis, text);
        }

        /// <summary>
        /// Mixes a string into a numeric seed. This is how <see cref="Rng.Fork"/> derives a child
        /// stream: the same seed and label always give the same child, and two different labels
        /// give unrelated children.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static ulong Combine(ulong seed, string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return HashString(Fold(OffsetBasis, seed), text);
        }

        /// <summary>
        /// Mixes a number into a numeric seed, the way <see cref="Combine(ulong, string)"/> mixes
        /// a label in.
        /// </summary>
        /// <remarks>
        /// For the places a label would be a lattice coordinate — the terrain heightfield hashes
        /// a grid corner per sample, and building the string for it would allocate a few hundred
        /// thousand times over one playfield.
        /// </remarks>
        public static ulong Combine(ulong seed, long value)
        {
            unchecked
            {
                ulong hash = OffsetBasis;
                hash = Fold(hash, seed);
                return Fold(hash, (ulong)value);
            }
        }

        static ulong Fold(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= Prime;
                }
            }

            return hash;
        }

        // Folds UTF-16 code units low byte first. Hashing code units rather than a UTF-8
        // encoding keeps this allocation-free and just as stable.
        static ulong HashString(ulong hash, string text)
        {
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)c;
                    hash *= Prime;
                    hash ^= (byte)(c >> 8);
                    hash *= Prime;
                }
            }

            return hash;
        }
    }
}
