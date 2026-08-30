using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// Pulls a dragged footprint flush with the ones already standing near it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a person arranging a map by hand spends the time on: a wall beside a wall, a barrier
    /// continuing the line of the one before it, a crate against the corner of a building. All of
    /// that is arithmetic on two rectangles, and doing it by eye means either a seam or a minute
    /// with the inspector.
    /// </para>
    /// <para>
    /// <strong>Each neighbour decides which axis the run goes along, and the two axes are then
    /// snapped differently.</strong> The run is along whichever axis the two footprints share least
    /// of: there the offset that wins butts them together, and across it the offset that wins lines
    /// their edges up. Letting both kinds of offer compete on both axes is the obvious shape and it
    /// is wrong — a wall dropped a little sideways of the one before it is nearer to sitting
    /// alongside its neighbour than to continuing it, so the nearest edge alone builds a staircase
    /// out of what was meant to be a straight run.
    /// </para>
    /// <para>
    /// <strong>A neighbour has to be beside the moving box on both axes to pull at all.</strong> A
    /// crate ten metres up the lane is not something to line up with. Without that gate every
    /// object on the map competes for every drag and the nearest edge wins by accident.
    /// </para>
    /// <para>
    /// No draw is taken and ties break by list order, so the same drag against the same neighbours
    /// gives the same answer. This is not in the generation path — nothing here can move a seed's
    /// output — but a snap that jittered between two equally near edges would be unusable for a
    /// different reason.
    /// </para>
    /// </remarks>
    public static class EdgeSnap
    {
        /// <summary>
        /// Finds the translation that puts <paramref name="moving"/> flush with the nearest edges
        /// within <paramref name="reach"/>, and returns whether there was one.
        /// </summary>
        /// <param name="moving">The footprint being dragged, in world space.</param>
        /// <param name="neighbours">Footprints already standing, in world space.</param>
        /// <param name="reach">How far an edge may pull, in metres.</param>
        /// <param name="offset">The translation to apply. Zero when this returns false.</param>
        /// <exception cref="ArgumentNullException"><paramref name="neighbours"/> is null.</exception>
        public static bool TryFlush(
            Rect2 moving, IReadOnlyList<Rect2> neighbours, float reach, out Vec2 offset)
        {
            if (neighbours == null)
            {
                throw new ArgumentNullException(nameof(neighbours));
            }

            offset = Vec2.Zero;

            if (!(reach > 0f))
            {
                return false;
            }

            float bestX = 0f;
            float bestZ = 0f;
            float nearestX = reach;
            float nearestZ = reach;

            for (int i = 0; i < neighbours.Count; i++)
            {
                Rect2 other = neighbours[i];

                float overlapX = Overlap(moving.MinX, moving.MaxX, other.MinX, other.MaxX);
                float overlapZ = Overlap(moving.MinZ, moving.MaxZ, other.MinZ, other.MaxZ);

                // Not beside this one at all on one axis or the other, so there is no edge here to
                // line up with: a crate across the lane is near on X and nowhere near on Z.
                if (overlapX < -reach || overlapZ < -reach)
                {
                    continue;
                }

                // The run goes along the axis the two share least of. Butt them together on that
                // one and line their edges up on the other — see the remarks on the type.
                if (overlapX <= overlapZ)
                {
                    Consider(other.MinX - moving.MaxX, ref nearestX, ref bestX);
                    Consider(other.MaxX - moving.MinX, ref nearestX, ref bestX);
                    Consider(other.MinZ - moving.MinZ, ref nearestZ, ref bestZ);
                    Consider(other.MaxZ - moving.MaxZ, ref nearestZ, ref bestZ);
                }
                else
                {
                    Consider(other.MinZ - moving.MaxZ, ref nearestZ, ref bestZ);
                    Consider(other.MaxZ - moving.MinZ, ref nearestZ, ref bestZ);
                    Consider(other.MinX - moving.MinX, ref nearestX, ref bestX);
                    Consider(other.MaxX - moving.MaxX, ref nearestX, ref bestX);
                }
            }

            offset = new Vec2(bestX, bestZ);
            return bestX != 0f || bestZ != 0f;
        }

        /// <summary>
        /// Keeps the nearest candidate seen so far. Strictly nearer, so a tie is broken by the
        /// neighbour that came first in the list rather than by the last one to be looked at.
        /// </summary>
        static void Consider(float candidate, ref float nearest, ref float best)
        {
            float distance = MathF.Abs(candidate);
            if (distance < nearest)
            {
                nearest = distance;
                best = candidate;
            }
        }

        /// <summary>
        /// How much of two spans lie over one another. Negative when they are apart, and then it is
        /// the size of the gap.
        /// </summary>
        /// <remarks>
        /// Signed rather than clamped at nothing, because the sign is the whole of what tells a run
        /// from a row: two walls end to end have a gap on the axis they run along and a full overlap
        /// across it, and a clamped figure would report nothing for the first and could not order
        /// the two.
        /// </remarks>
        static float Overlap(float minA, float maxA, float minB, float maxB) =>
            MathF.Min(maxA, maxB) - MathF.Max(minA, minB);
    }
}
