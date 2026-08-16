using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEngine;

namespace ArenaForge.Samples
{
    /// <summary>
    /// Generates a map from the inspector's seed and realises it into the scene on Start.
    /// </summary>
    /// <remarks>
    /// The sample driver, not the tool: it exists so the demo scene shows something when you press
    /// play. The editor window built in a later milestone replaces it for real use.
    /// </remarks>
    [RequireComponent(typeof(WorldRealizer))]
    [AddComponentMenu("ArenaForge/Arena Demo Map")]
    public sealed class ArenaDemoMap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Same seed, same map. Change it and everything moves.")]
        ulong _seed = 20260816;

        [SerializeField]
        Vector2 _playfieldSize = new Vector2(60f, 60f);

        [SerializeField]
        [Min(1)]
        int _laneCount = 3;

        [SerializeField]
        [Min(0.05f)]
        float _gridSize = 1f;

        [SerializeField]
        [Min(0.01f)]
        float _structureDensity = 1f;

        [SerializeField]
        bool _generateOnStart = true;

        /// <summary>Seed the map is generated from.</summary>
        public ulong Seed
        {
            get => _seed;
            set => _seed = value;
        }

        /// <summary>Generates a map and realises it, replacing whatever was there.</summary>
        [ContextMenu("Generate")]
        public void Generate()
        {
            var realizer = GetComponent<WorldRealizer>();
            if (realizer.Catalog == null)
            {
                Debug.LogWarning($"ArenaForge: '{name}' has no catalog to generate against.", this);
                return;
            }

            WorldDoc doc = ArenaLayoutGenerator.Generate(BuildParams(), realizer.Catalog.ToCatalog());
            realizer.Realize(doc);
        }

        /// <summary>Removes the realised map.</summary>
        [ContextMenu("Clear")]
        public void Clear() => GetComponent<WorldRealizer>().Derealize();

        /// <summary>The parameters the inspector fields describe.</summary>
        public ArenaParams BuildParams() => new ArenaParams
        {
            Seed = _seed,
            PlayfieldSize = new Vec2(_playfieldSize.x, _playfieldSize.y),
            LaneCount = _laneCount,
            GridSize = _gridSize,
            StructureDensity = _structureDensity,
        };

        void Start()
        {
            if (_generateOnStart)
            {
                Generate();
            }
        }
    }
}
