using ArenaForge.Unity;
using UnityEditor;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Identity of the editor layer. Placeholder for the tool window, scene
    /// handles and Undo integration.
    /// </summary>
    public static class ArenaForgeEditorInfo
    {
        /// <summary>
        /// Describes the editor layer and the build target it is running against.
        /// </summary>
        public static string Describe()
        {
            return $"{ArenaForgeRuntimeInfo.Describe()}, target {EditorUserBuildSettings.activeBuildTarget}";
        }
    }
}
