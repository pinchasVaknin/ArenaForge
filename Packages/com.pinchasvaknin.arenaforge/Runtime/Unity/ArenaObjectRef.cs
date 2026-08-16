using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Marks a realised GameObject with the stable id of the object it came from.
    /// </summary>
    /// <remarks>
    /// This is the thread back from the scene to the document. Without it a crate a user dragged
    /// three metres is just a moved transform; with it the edit can be recorded as an override
    /// against <c>map/lane_mid/cover_03</c> and survive the next regeneration.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ArenaObjectRef : MonoBehaviour
    {
        [SerializeField]
        string _stableId;

        /// <summary>Stable id of the document object this instance realises.</summary>
        public string StableId => _stableId;

        /// <summary>Points this instance at a document object.</summary>
        public void Bind(string stableId) => _stableId = stableId;
    }
}
