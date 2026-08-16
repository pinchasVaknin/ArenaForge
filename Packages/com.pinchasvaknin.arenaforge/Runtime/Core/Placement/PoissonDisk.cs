using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// Deterministic Poisson-disk sampling over the open cells of a <see cref="PlacementGrid"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bridson's algorithm: start from one sample, and repeatedly try to grow a new one into the
    /// annulus around a sample that is still active, dropping a sample from the active list once
    /// it has no room left. What comes out is a set of points no closer than the radius and with
    /// no large empty patch — which is what scattered cover wants, where uniform random draws
    /// would leave clumps and holes.
    /// </para>
    /// <para>
    /// Two departures from the textbook version, both for determinism. Annulus candidates are
    /// drawn as a point in the enclosing square and rejected if they miss the ring, rather than
    /// from an angle and a distance, because an angle needs <see cref="MathF.Sin"/> and its last
    /// ulp is not guaranteed identical across runtimes. And neighbours are checked by walking the
    /// samples rather than through a background lookup grid: a lane holds tens of samples, not
    /// thousands, so the quadratic walk is the cheaper of the two and has nothing that can
    /// enumerate out of order.
    /// </para>
    /// <para>
    /// A third departure is not about determinism but about the shape of a lane. The textbook
    /// algorithm stops when its frontier dies, which in a strip a few metres wide — or in a lane a
    /// building has cut in two — happens long before the space is full, because the ring around
    /// every live sample falls outside the domain. This version then reseeds from the open cells
    /// it has not reached and carries on, so a narrow lane is filled as thoroughly as a wide one
    /// and the sample count stays a function of the area rather than of where the first sample
    /// happened to land.
    /// </para>
    /// </remarks>
    public static class PoissonDisk
    {
        /// <summary>Candidates tried around one active sample before it is retired.</summary>
        const int CandidatesPerSample = 30;

        /// <summary>
        /// Samples open cells of <paramref name="grid"/> inside <paramref name="bounds"/>, no two
        /// closer than <paramref name="radius"/> metres, in the order they were generated.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The radius is not positive.</exception>
        public static List<Vec2> Sample(
            PlacementGrid grid, Rect2 bounds, float radius, int maxSamples, ref Rng rng)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!(radius > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            }

            var samples = new List<Vec2>();
            if (maxSamples <= 0)
            {
                return samples;
            }

            var open = new List<Vec2>();
            grid.CollectOpenCentres(bounds, open);
            if (open.Count == 0)
            {
                return samples;
            }

            float squaredRadius = radius * radius;
            float reach = radius * 2f;

            // Walked in a shuffled order so the reseeds are spread over the domain rather than
            // sweeping it corner to corner, which would leave the last region of every lane
            // looking combed.
            Shuffle(open, ref rng);

            var active = new List<int>();
            int cursor = 0;

            while (samples.Count < maxSamples)
            {
                while (cursor < open.Count && !IsClear(samples, open[cursor], squaredRadius))
                {
                    cursor++;
                }

                if (cursor >= open.Count)
                {
                    break;
                }

                samples.Add(open[cursor]);
                active.Add(samples.Count - 1);
                cursor++;

                while (active.Count > 0 && samples.Count < maxSamples)
                {
                    int slot = rng.NextRange(0, active.Count);
                    Vec2 origin = samples[active[slot]];
                    bool grew = false;

                    for (int attempt = 0; attempt < CandidatesPerSample && !grew; attempt++)
                    {
                        float dx = rng.NextRange(-reach, reach);
                        float dz = rng.NextRange(-reach, reach);
                        float distance = dx * dx + dz * dz;
                        if (distance < squaredRadius || distance > reach * reach)
                        {
                            continue;
                        }

                        var candidate = new Vec2(origin.X + dx, origin.Y + dz);
                        if (!bounds.Contains(candidate) || !grid.IsFree(candidate) ||
                            !IsClear(samples, candidate, squaredRadius))
                        {
                            continue;
                        }

                        samples.Add(candidate);
                        active.Add(samples.Count - 1);
                        grew = true;
                    }

                    if (!grew)
                    {
                        active.RemoveAt(slot);
                    }
                }
            }

            return samples;
        }

        static void Shuffle(List<Vec2> items, ref Rng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                Vec2 swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        static bool IsClear(List<Vec2> samples, Vec2 candidate, float squaredRadius)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (Vec2.DistanceSquared(samples[i], candidate) < squaredRadius)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
