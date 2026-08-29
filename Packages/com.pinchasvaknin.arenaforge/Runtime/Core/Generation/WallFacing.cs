using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// Which way round a piece of furniture goes: its back to the wall and its front to the room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quarter turn drawn from a stream is the right answer for a crate lying in a lane and the
    /// wrong one for everything that has a front. A sofa is modelled facing its own positive Z, and
    /// a sofa turned at random is a sofa facing the wall three times out of four — which is not a
    /// room somebody furnished, it is a room somebody dropped furniture into. So a piece near a
    /// wall is turned rather than drawn, and the draw is kept for the pieces standing where no wall
    /// is in reach.
    /// </para>
    /// <para>
    /// <strong>Positive Z is the front and negative Z is the back.</strong> That is Unity's own
    /// convention for forward and the one an artist modelling a prefab follows without being told;
    /// nothing here can check it, and a piece modelled facing sideways will be turned sideways. A
    /// catalog states sizes, not intentions.
    /// </para>
    /// <para>
    /// <strong>A corner piece has two backs.</strong> An L-shaped sofa is modelled with its back
    /// along negative Z and its second back along negative X, so a given corner takes it in exactly
    /// one of the four turns — see <see cref="IntoCorner"/>. That same turn is right for an
    /// ordinary piece standing in that corner, which is why the corner pass uses it for everything
    /// it puts down: whichever of the two walls the piece has its back to, it is a wall.
    /// </para>
    /// <para>
    /// The region is the room, as everywhere else in placement: a room's contents are proposed into
    /// the rectangle its walls enclose, so the edges of that rectangle <em>are</em> the walls. See
    /// <see cref="ConstraintKind.AgainstWall"/>, which measures the same thing to decide whether a
    /// piece may stand somewhere rather than which way it faces once it does.
    /// </para>
    /// <para>
    /// <strong>Outdoors the wall is given rather than found.</strong> A piece stood against the
    /// outside of a building was handed the face it is standing on, so there is no nearest edge to
    /// work out and no reach to be inside — see <see cref="AwayFrom"/>. The rule it states is the
    /// same one: back to the wall, front to the ground in front of it.
    /// </para>
    /// </remarks>
    public static class WallFacing
    {
        /// <summary>The answer for a piece no wall is near enough to turn.</summary>
        public const int Free = -1;

        /// <summary>
        /// The quarter turn that puts a piece's back against the nearest edge of a region, or
        /// <see cref="Free"/> when its pivot is further than <paramref name="reach"/> from all four.
        /// </summary>
        /// <param name="region">The room the piece is being proposed into.</param>
        /// <param name="at">Where the piece's pivot goes.</param>
        /// <param name="reach">
        /// How far the pivot may be from a wall and still be turned by it. A caller with a piece in
        /// hand passes <see cref="Radius"/> plus the clear floor it is willing to allow behind it,
        /// since the pivot of a piece standing flush against a wall is half the piece away from it.
        /// </param>
        /// <remarks>
        /// <para>
        /// The nearest edge, not every edge it is near: a piece in a corner is near two of them and
        /// can only have its back to one. The four are compared in a fixed order and a tie goes to
        /// the first of them, so the answer is a function of the region and the point.
        /// </para>
        /// <para>
        /// <strong>The pivot rather than the footprint, and that is not a simplification.</strong> A
        /// footprint is the rectangle the piece occupies <em>after</em> it has been turned, so
        /// asking which wall a footprint is nearest and then turning the piece to face away from it
        /// changes the answer to the question that was just asked — an oblong drawn lying along one
        /// wall stands end-on to another once it has been turned, and the wall it now backs onto is
        /// no longer the one it is nearest. The pivot does not move when a piece is turned, so a
        /// facing decided from it is the facing the piece keeps.
        /// </para>
        /// </remarks>
        public static int Against(Rect2 region, Vec2 at, float reach)
        {
            float nearest = at.Y - region.MinZ;
            int facing = 0;

            Consider(at.X - region.MinX, 1, ref nearest, ref facing);
            Consider(region.MaxZ - at.Y, 2, ref nearest, ref facing);
            Consider(region.MaxX - at.X, 3, ref nearest, ref facing);

            return nearest <= reach ? facing : Free;
        }

        /// <summary>
        /// How far a piece of art reaches from its own pivot on the ground plane, in metres.
        /// </summary>
        /// <remarks>
        /// The furthest of the footprint's four edges, so the answer does not change when the piece
        /// is turned — which is what lets <see cref="Against"/> be asked before the turn is known.
        /// Measured from the pivot rather than as half the footprint, because art modelled off to
        /// one side of its pivot reaches further one way than the other and it is the further way
        /// that decides whether a wall is in reach.
        /// </remarks>
        public static float Radius(Rect2 footprint) => MathF.Max(
            MathF.Max(MathF.Abs(footprint.MinX), MathF.Abs(footprint.MaxX)),
            MathF.Max(MathF.Abs(footprint.MinZ), MathF.Abs(footprint.MaxZ)));

        /// <summary>
        /// The quarter turn that puts a piece's back against a wall it is standing outside of,
        /// given the wall's axis and which way is out of it.
        /// </summary>
        /// <param name="alongX">True when the wall runs along world X, so its normal is world Z.</param>
        /// <param name="outward">Which way is out of the wall on its normal: 1 or -1.</param>
        /// <remarks>
        /// <para>
        /// The same rule as <see cref="Against"/> asked the other way round. Inside a room the wall
        /// has to be found — a piece is somewhere on the floor and which of the four is behind it
        /// is a measurement — whereas a piece stood against the outside of a building is handed the
        /// face it is standing on, so there is nothing to measure and no reach to be within. A
        /// stack of cabinets against the north wall of a house has its back to that wall because
        /// that is the wall it was put against, not because it happened to land near one.
        /// </para>
        /// <para>
        /// Front to <paramref name="outward"/>, since out of the wall is where the room — or, out
        /// here, the map — is. See <see cref="WallRun.Face.Outward"/>, which is the sign this takes.
        /// </para>
        /// </remarks>
        public static int AwayFrom(bool alongX, int outward) => alongX
            ? (outward > 0 ? 0 : 2)
            : (outward > 0 ? 1 : 3);

        /// <summary>
        /// The quarter turn that puts a piece's back — and a corner piece's second back — against
        /// the two walls meeting at the corner of <paramref name="region"/> nearest
        /// <paramref name="at"/>.
        /// </summary>
        /// <remarks>
        /// Which corner is decided by which side of the region's centre the point falls, so every
        /// point in the room has an answer and the four quadrants are exactly the four turns. A
        /// point on a centre line takes the low corner, which is a tie broken rather than a case
        /// handled: a piece there is not in a corner at all, and the rule that offered it the spot
        /// is what refuses it.
        /// </remarks>
        public static int IntoCorner(Rect2 region, Vec2 at)
        {
            Vec2 centre = region.Center;
            bool highX = at.X > centre.X;
            bool highZ = at.Y > centre.Y;

            if (highX)
            {
                return highZ ? 2 : 3;
            }

            return highZ ? 1 : 0;
        }

        /// <summary>Takes one edge as the nearest so far, if it is nearer than the last.</summary>
        static void Consider(float gap, int turn, ref float nearest, ref int facing)
        {
            if (gap < nearest)
            {
                nearest = gap;
                facing = turn;
            }
        }
    }
}
