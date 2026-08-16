using UnityEngine;
using CoreQuat = ArenaForge.Core.Quat;
using CoreVec2 = ArenaForge.Core.Vec2;
using CoreVec3 = ArenaForge.Core.Vec3;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Converts Core's engine-free math types into Unity's.
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
    }
}
