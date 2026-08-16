using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// The finer yaw table cover props may use: twenty-four steps of fifteen degrees, as exact
    /// constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what <see cref="ArenaParams.FineCoverRotation"/> switches on, and it is a table for
    /// the same reason <see cref="QuarterTurn"/> is: an arbitrary angle needs
    /// <see cref="MathF.Sin"/>, whose last-ulp result is not guaranteed identical across runtimes,
    /// and a map whose props sit one ulp apart on two machines is not byte-identical. Every value
    /// below is a literal, so the same twenty-four rotations come out of every machine.
    /// </para>
    /// <para>
    /// Fifteen degrees is fine enough that a crate no longer reads as axis-aligned, which is the
    /// whole point of the parameter. Steps 0, 6, 12 and 18 are the quarter turns, and they are
    /// bit-identical to <see cref="QuarterTurn.Rotation"/>.
    /// </para>
    /// </remarks>
    public static class YawStep
    {
        // Written in the w >= 0 form, like QuarterTurn: a quaternion and its negation are the same
        // rotation, so every step past 180 degrees is stored as its negative equivalent.
        static readonly Quat[] Rotations =
        {
            new Quat(0f, 0f, 0f, 1f),                                       // 0
            new Quat(0f, 0.13052619222005157f, 0f, 0.99144486137381038f),   // 15
            new Quat(0f, 0.25881904510252074f, 0f, 0.96592582628906831f),   // 30
            new Quat(0f, 0.38268343236508978f, 0f, 0.92387953251128674f),   // 45
            new Quat(0f, 0.5f, 0f, 0.86602540378443871f),                   // 60
            new Quat(0f, 0.60876142900872066f, 0f, 0.79335334029123517f),   // 75
            new Quat(0f, 0.70710678118654752f, 0f, 0.70710678118654752f),   // 90
            new Quat(0f, 0.79335334029123517f, 0f, 0.60876142900872066f),   // 105
            new Quat(0f, 0.86602540378443871f, 0f, 0.5f),                   // 120
            new Quat(0f, 0.92387953251128674f, 0f, 0.38268343236508978f),   // 135
            new Quat(0f, 0.96592582628906831f, 0f, 0.25881904510252074f),   // 150
            new Quat(0f, 0.99144486137381038f, 0f, 0.13052619222005157f),   // 165
            new Quat(0f, 1f, 0f, 0f),                                       // 180
            new Quat(0f, -0.99144486137381038f, 0f, 0.13052619222005157f),  // -165
            new Quat(0f, -0.96592582628906831f, 0f, 0.25881904510252074f),  // -150
            new Quat(0f, -0.92387953251128674f, 0f, 0.38268343236508978f),  // -135
            new Quat(0f, -0.86602540378443871f, 0f, 0.5f),                  // -120
            new Quat(0f, -0.79335334029123517f, 0f, 0.60876142900872066f),  // -105
            new Quat(0f, -0.70710678118654752f, 0f, 0.70710678118654752f),  // -90
            new Quat(0f, -0.60876142900872066f, 0f, 0.79335334029123517f),  // -75
            new Quat(0f, -0.5f, 0f, 0.86602540378443871f),                  // -60
            new Quat(0f, -0.38268343236508978f, 0f, 0.92387953251128674f),  // -45
            new Quat(0f, -0.25881904510252074f, 0f, 0.96592582628906831f),  // -30
            new Quat(0f, -0.13052619222005157f, 0f, 0.99144486137381038f),  // -15
        };

        /// <summary>How many distinct steps make a full turn.</summary>
        public const int Count = 24;

        /// <summary>The rotation for a whole number of steps about the up axis.</summary>
        public static Quat Rotation(int steps) => Rotations[Normalize(steps)];

        /// <summary>
        /// The axis-aligned bounds of a footprint after the same rotation about the origin.
        /// </summary>
        /// <remarks>
        /// Off-axis steps leave a rectangle that is no longer axis aligned, so this is the box
        /// around it rather than the shape itself. Overlap tests against it are conservative —
        /// they reject a few placements that would have fitted — which is the right way round for
        /// a rule whose job is to keep props out of each other.
        /// </remarks>
        public static Rect2 Bounds(Rect2 rect, int steps)
        {
            int normalized = Normalize(steps);
            if (normalized % (Count / QuarterTurn.Count) == 0)
            {
                // Quarter turns stay exact: an axis swap, no corner arithmetic.
                return QuarterTurn.Rotate(rect, normalized / (Count / QuarterTurn.Count));
            }

            Quat rotation = Rotations[normalized];
            Vec3 a = rotation.Rotate(new Vec3(rect.MinX, 0f, rect.MinZ));
            Vec3 b = rotation.Rotate(new Vec3(rect.MaxX, 0f, rect.MinZ));
            Vec3 c = rotation.Rotate(new Vec3(rect.MaxX, 0f, rect.MaxZ));
            Vec3 d = rotation.Rotate(new Vec3(rect.MinX, 0f, rect.MaxZ));

            return new Rect2(
                MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)),
                MathF.Min(MathF.Min(a.Z, b.Z), MathF.Min(c.Z, d.Z)),
                MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)),
                MathF.Max(MathF.Max(a.Z, b.Z), MathF.Max(c.Z, d.Z)));
        }

        /// <summary>Folds any whole number of steps into 0..23.</summary>
        public static int Normalize(int steps) => ((steps % Count) + Count) % Count;
    }
}
