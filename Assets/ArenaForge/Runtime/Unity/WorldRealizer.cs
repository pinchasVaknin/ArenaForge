using System.Collections.Generic;
using ArenaForge.Core;
using UnityEngine;
using CorePose = ArenaForge.Core.Pose;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Turns a world document into GameObjects, and takes them away again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The realiser instantiates prefabs and nothing else — no meshes are authored, combined or
    /// optimised. What comes out of <see cref="Realize"/> is exactly the prefabs the catalog binds,
    /// posed where the document says, so a map stays as editable as the art it was built from.
    /// </para>
    /// <para>
    /// Poses are applied in the root's local space, so moving or rotating this GameObject moves the
    /// whole map without invalidating a single document position.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("ArenaForge/World Realizer")]
    public sealed class WorldRealizer : MonoBehaviour
    {
        const string RootName = "Generated";

        [SerializeField]
        [Tooltip("Binds the logical ids in a world document to prefabs.")]
        CatalogAsset _catalog;

        [SerializeField]
        [HideInInspector]
        Transform _root;

        /// <summary>The catalog logical ids are resolved against.</summary>
        public CatalogAsset Catalog
        {
            get => _catalog;
            set => _catalog = value;
        }

        /// <summary>
        /// The GameObject realised instances are parented to. Created on first use.
        /// </summary>
        public Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = transform.Find(RootName);
                }

                if (_root == null)
                {
                    var root = new GameObject(RootName);
                    root.transform.SetParent(transform, false);
                    _root = root.transform;
                }

                return _root;
            }
        }

        /// <summary>How many instances are currently realised.</summary>
        public int RealizedCount => Root.childCount;

        /// <summary>
        /// Resolves the document and instantiates one GameObject per resolved object.
        /// </summary>
        /// <remarks>
        /// Realising over an already-realised map clears it first, so calling this twice leaves the
        /// same scene as calling it once.
        /// </remarks>
        /// <returns>The resolution, including any override that could not be applied.</returns>
        public ResolvedWorld Realize(WorldDoc doc)
        {
            if (doc == null)
            {
                throw new System.ArgumentNullException(nameof(doc));
            }

            Derealize();

            ResolvedWorld resolved = doc.Resolve();
            if (_catalog == null)
            {
                Debug.LogWarning($"ArenaForge: '{name}' has no catalog, so nothing was realised.", this);
                return resolved;
            }

            for (int i = 0; i < resolved.Objects.Count; i++)
            {
                RealizeObject(resolved.Objects[i]);
            }

            return resolved;
        }

        /// <summary>
        /// Destroys every realised instance, leaving the root empty. Safe to call when nothing is
        /// realised.
        /// </summary>
        public void Derealize()
        {
            Transform root = Root;
            var doomed = new List<GameObject>(root.childCount);
            for (int i = 0; i < root.childCount; i++)
            {
                doomed.Add(root.GetChild(i).gameObject);
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                if (Application.isPlaying)
                {
                    Destroy(doomed[i]);
                }
                else
                {
                    DestroyImmediate(doomed[i]);
                }
            }
        }

        void RealizeObject(PlacedObject placed)
        {
            GameObject prefab = _catalog.PrefabFor(placed.LogicalId);
            if (prefab == null)
            {
                Debug.LogWarning(
                    $"ArenaForge: catalog '{_catalog.name}' binds no prefab to '{placed.LogicalId}', " +
                    $"so '{placed.StableId}' was skipped.", this);
                return;
            }

            GameObject instance = InstantiatePrefab(prefab, Root);
            if (instance == null)
            {
                return;
            }

            instance.name = placed.StableId;

            CorePose pose = placed.Pose;
            Transform t = instance.transform;
            t.localPosition = CoreConvert.ToUnity(pose.Position);
            t.localRotation = CoreConvert.ToUnity(pose.Rotation);
            t.localScale = Vector3.one * pose.Scale;

            instance.AddComponent<ArenaObjectRef>().Bind(placed.StableId);
        }

        // In the editor the prefab link has to survive: an instance that is a plain copy cannot be
        // updated when the art pack changes, and the hierarchy stops showing what came from where.
        // In play mode there is no asset to link to, so the plain instantiate is correct.
        static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            }
#endif
            return Object.Instantiate(prefab, parent);
        }
    }
}
