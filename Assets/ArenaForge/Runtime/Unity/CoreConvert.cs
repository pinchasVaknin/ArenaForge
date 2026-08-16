using UnityEngine;
using CorePose = ArenaForge.Core.Pose;
using CoreQuat = ArenaForge.Core.Quat;
using CoreVec2 = ArenaForge.Core.Vec2;
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

        /// <summary>Converts a Core ground-plane coordinate to a Unity vector at the given height.</summary>
        public static Vector3 ToUnity(CoreVec2 value, float height) => new Vector3(value.X, height, value.Y);

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
        /// Local rather than world, because <see cref="WorldRealizer"/> poses instances in the
        /// realisation root's space — so a map that has been dragged around the scene still reads
        /// back the document coordinates it was built from. The scale is the X component: document
        /// poses carry one uniform scale, and a non-uniform scale a user typed into the inspector
        /// has no representation to be captured into.
        /// </remarks>
        public static CorePose ToCorePose(Transform transform) => new CorePose(
            ToCore(transform.localPosition),
            ToCore(transform.localRotation),
            transform.localScale.x);
    }
}
