using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// The four yaw rotations the generator is allowed to place props at, as exact constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Quat.FromYawDegrees"/> would give the same rotations, but it routes through
    /// <see cref="MathF.Sin"/> and <see cref="MathF.Cos"/>, whose last-ulp results are not
    /// guaranteed identical across runtimes. A table of literals is: the same four quaternions
    /// come out of every machine, so a serialised map is byte-identical everywhere.
    /// </para>
    /// <para>
    /// <see cref="Rotate(Rect2, int)"/> is likewise an exact axis swap rather than a rotation of
    /// four corners through the quaternion, which would leave a footprint's bounds a rounding
    /// error away from the grid.
    /// </para>
    /// </remarks>
    public static class QuarterTurn
    {
        // The nearest float to sqrt(2)/2, written out so it is a compile-time constant. Entries are
        // written in the w >= 0 form: 270 degrees is the same rotation as -90, and a quaternion and
        // its negation are the same rotation, so the shortest-arc representation is the one stored.
        const float Sin45 = 0.70710678118654752f;

        static readonly Quat[] Rotations =
        {
            new Quat(0f, 0f, 0f, 1f),        // 0 degrees
            new Quat(0f, Sin45, 0f, Sin45),  // 90
            new Quat(0f, 1f, 0f, 0f),        // 180
            new Quat(0f, -Sin45, 0f, Sin45), // 270
        };

        /// <summary>How many distinct quarter turns there are.</summary>
        public const int Count = 4;

        /// <summary>The rotation for a whole number of quarter turns about the up axis.</summary>
        public static Quat Rotation(int quarterTurns) => Rotations[Normalize(quarterTurns)];

        /// <summary>Degrees of yaw for a whole number of quarter turns.</summary>
        public static float Degrees(int quarterTurns) => Normalize(quarterTurns) * 90f;

        /// <summary>
        /// The footprint of a rectangle after the same rotation about the origin.
        /// </summary>
        /// <remarks>
        /// A quarter turn maps (x, z) to (z, -x), so the result is an exact swap and negation of
        /// the input bounds — no trigonometry, no rounding.
        /// </remarks>
        public static Rect2 Rotate(Rect2 rect, int quarterTurns)
        {
            switch (Normalize(quarterTurns))
            {
                case 1:
                    return new Rect2(rect.MinZ, -rect.MaxX, rect.MaxZ, -rect.MinX);
                case 2:
                    return new Rect2(-rect.MaxX, -rect.MaxZ, -rect.MinX, -rect.MinZ);
                case 3:
                    return new Rect2(-rect.MaxZ, rect.MinX, -rect.MinZ, rect.MaxX);
                default:
                    return rect;
            }
        }

        /// <summary>Folds any whole number of quarter turns into 0..3.</summary>
        public static int Normalize(int quarterTurns) => ((quarterTurns % Count) + Count) % Count;
    }
}
