using UnityEngine;
using CorePose = ArenaForge.Core.Pose;
using CoreQuat = ArenaForge.Core.Quat;
using CoreVec3 = ArenaForge.Core.Vec3;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Converts between Core's engine-free math types and Unity's.
    /// </summary>
    /// <remarks>
    /// This is the whole of the type boundary described in ARCHITECTURE.md section 1. Core uses the
    /// same left-handed, Y-up convention Unity does, so every conversion is component-for-component
    /// with no axis flipping to get wrong.
    /// </remarks>
    public static class CoreConvert
    {
        /// <summary>Converts a Core vector to a Unity one.</summary>
        public static Vector3 ToUnity(CoreVec3 value) => new Vector3(value.X, value.Y, value.Z);

        /// <summary>Converts a Core rotation to a Unity one.</summary>
        public static Quaternion ToUnity(CoreQuat value) =>
            new Quaternion(value.X, value.Y, value.Z, value.W);

        /// <summary>Converts a Unity vector to a Core one.</summary>
        public static CoreVec3 ToCore(Vector3 value) => new CoreVec3(value.x, value.y, value.z);

        /// <summary>Converts a Unity rotation to a Core one.</summary>
        public static CoreQuat ToCore(Quaternion value) =>
            new CoreQuat(value.x, value.y, value.z, value.w);

        /// <summary>
        /// Reads a transform's local placement as a Core pose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Local rather than world, because <see cref="WorldRealizer"/> poses instances in the
        /// realisation root's space — so a map that has been dragged around the scene still reads
        /// back the document coordinates it was built from.
        /// </para>
        /// <para>
        /// The uniform scale is the X component and the vertical scale is what Y is over it, which
        /// is the inverse of what the realiser writes. A pose can say one stretch along its own Y
        /// and nothing else, so a user who scales an instance unevenly in Z has typed something the
        /// document cannot hold; X is taken as the uniform factor because that is the axis the
        /// realiser wrote it to.
        /// </para>
        /// </remarks>
        public static CorePose ToCorePose(Transform transform)
        {
            Vector3 scale = transform.localScale;
            float uniform = scale.x;

            return new CorePose(
                ToCore(transform.localPosition),
                ToCore(transform.localRotation),
                uniform,
                uniform != 0f ? scale.y / uniform : 1f);
        }
    }
}
