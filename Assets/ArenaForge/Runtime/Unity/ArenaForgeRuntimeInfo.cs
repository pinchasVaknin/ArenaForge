using ArenaForge.Core;
using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Identity of the Unity adapter layer. Placeholder for the prefab catalog
    /// binding and the world realiser.
    /// </summary>
    public static class ArenaForgeRuntimeInfo
    {
        /// <summary>
        /// Describes the adapter and the Core schema it is bound to.
        /// </summary>
        public static string Describe()
        {
            return $"ArenaForge adapter, Core schema {CoreInfo.SchemaVersion}, Unity {Application.unityVersion}";
        }
    }
}
