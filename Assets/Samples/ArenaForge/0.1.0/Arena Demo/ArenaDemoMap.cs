using ArenaForge.Unity;
using UnityEngine;

namespace ArenaForge.Samples
{
    /// <summary>
    /// Puts the scene's map on screen when play begins.
    /// </summary>
    /// <remarks>
    /// The sample driver, not the tool. It defers to the <see cref="ArenaMap"/> beside it for both
    /// the parameters and the document, so a map authored in the editor window — hand edits and all
    /// — is what you walk around in play mode, rather than a second map generated from a second
    /// seed that happened to be typed into this component.
    /// </remarks>
    [RequireComponent(typeof(ArenaMap))]
    [AddComponentMenu("ArenaForge/Arena Demo Map")]
    public sealed class ArenaDemoMap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Generate a map on play if the scene does not already hold one.")]
        bool _generateOnStart = true;

        void Start()
        {
            ArenaMap map = GetComponent<ArenaMap>();

            if (map.HasDocument)
            {
                map.Realize();
                return;
            }

            if (_generateOnStart)
            {
                map.Generate();
            }
        }
    }
}
