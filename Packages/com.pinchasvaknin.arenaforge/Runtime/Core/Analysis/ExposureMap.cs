using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// How exposed every walkable cell of a map is: the fraction of the sampled observer positions
    /// that can see it.
    /// </summary>
    /// <remarks>
    /// Zero means no observer had a line to the cell — a corner behind a building. One means every
    /// one of them did, which on a sixty-metre arena is a cell standing in the open with nothing
    /// between it and the rest of the map. The interesting maps are the ones with both.
    /// </remarks>
    public sealed class ExposureMap
    {
        readonly float[] _values;

        /// <summary>Creates a map from one exposure value per walkable cell.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The value count does not match the walkable cell count.</exception>
        public ExposureMap(WalkableGrid walkable, float[] values, int observerCount)
        {
            if (walkable == null)
            {
                throw new ArgumentNullException(nameof(walkable));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Length != walkable.Count)
            {
                throw new ArgumentException(
                    $"Expected {walkable.Count} exposure values, one per walkable cell; got {values.Length}.",
                    nameof(values));
            }

            Walkable = walkable;
            _values = values;
            ObserverCount = observerCount;

            float min = 1f;
            float max = 0f;
            double total = 0d;
            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }

                total += value;
            }

            if (values.Length == 0)
            {
                min = 0f;
            }

            Min = min;
            Max = max;
            Mean = values.Length > 0 ? (float)(total / values.Length) : 0f;
        }

        /// <summary>The cells these values describe.</summary>
        public WalkableGrid Walkable { get; }

        /// <summary>How many observer positions each cell's fraction was taken over.</summary>
        public int ObserverCount { get; }

        /// <summary>Exposure of every walkable cell, in walkable-index order.</summary>
        public IReadOnlyList<float> Values => _values;

        /// <summary>Exposure of the most sheltered walkable cell.</summary>
        public float Min { get; }

        /// <summary>Mean exposure across the walkable floor.</summary>
        public float Mean { get; }

        /// <summary>Exposure of the most exposed walkable cell.</summary>
        public float Max { get; }

        /// <summary>Exposure of one walkable cell.</summary>
        public float At(int walkableIndex) => _values[walkableIndex];

        /// <summary>
        /// Mean exposure of the walkable cells within <paramref name="radius"/> of a point, or
        /// zero if there are none.
        /// </summary>
        public float MeanWithin(Vec2 center, float radius)
        {
            float radiusSquared = radius * radius;
            double total = 0d;
            int count = 0;

            for (int i = 0; i < _values.Length; i++)
            {
                if (Vec2.DistanceSquared(Walkable.CentreOf(i), center) <= radiusSquared)
                {
                    total += _values[i];
                    count++;
                }
            }

            return count > 0 ? (float)(total / count) : 0f;
        }

        /// <summary>
        /// The exposure laid back out over the playfield grid as <c>[x, z]</c>, with
        /// <see cref="float.NaN"/> in every cell that is not walkable.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Values are the raw exposure fractions, 0 to 1, not rescaled to the map's own range. Two
        /// maps rendered from this are therefore comparable: a sheltered map looks cold rather than
        /// looking like a normal map with the contrast turned up. NaN marks the cells a structure
        /// stands on, so a renderer can leave them clear instead of painting them as sheltered
        /// floor.
        /// </para>
        /// <para>
        /// The colour ramp this is meant to be read through runs cold to hot — sheltered to
        /// exposed — through five stops:
        /// </para>
        /// <list type="table">
        ///   <item><term>0.00</term><description>deep blue — dead ground, seen from nowhere</description></item>
        ///   <item><term>0.25</term><description>cyan — sheltered, worth holding</description></item>
        ///   <item><term>0.50</term><description>green — ordinary lane floor</description></item>
        ///   <item><term>0.75</term><description>amber — crossed under observation</description></item>
        ///   <item><term>1.00</term><description>red — open ground, seen from everywhere</description></item>
        /// </list>
        /// <para>
        /// Interpolate linearly between stops. Core stops here on purpose: turning these numbers
        /// into pixels is the editor's job, and a colour type in Core would be the first engine
        /// concept to cross the boundary.
        /// </para>
        /// </remarks>
        public float[,] ToGrayscale()
        {
            ArenaGrid grid = Walkable.Grid;
            var image = new float[grid.CountX, grid.CountZ];

            for (int x = 0; x < grid.CountX; x++)
            {
                for (int z = 0; z < grid.CountZ; z++)
                {
                    int index = Walkable.IndexAt(x, z);
                    image[x, z] = index >= 0 ? _values[index] : float.NaN;
                }
            }

            return image;
        }
    }
}
