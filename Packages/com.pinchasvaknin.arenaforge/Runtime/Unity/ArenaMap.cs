using System;
using ArenaForge.Core;
using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// A map in a scene: the parameters it is generated from and the document those parameters and
    /// the user's edits have produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document is held as its serialised JSON rather than as an object graph. Unity can
    /// serialise a string into a scene, snapshot it for <c>Undo.RegisterCompleteObjectUndo</c> and
    /// restore it again, none of which it can do for a <see cref="WorldDoc"/>; and it means the map
    /// in the scene and the map in a saved <c>.json</c> file are the same bytes, so there is one
    /// persistence path rather than two. Parsing is cached, and only redone when the string the
    /// cache was built from stops matching the field — which is exactly what an undo does to it.
    /// </para>
    /// <para>
    /// The realised GameObjects are not state. They are rebuilt from the document by
    /// <see cref="WorldRealizer"/>, which is why this component survives a scene reload with nothing
    /// but a few kilobytes of text.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldRealizer))]
    [AddComponentMenu("ArenaForge/Arena Map")]
    public sealed class ArenaMap : MonoBehaviour
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
        [Min(0f)]
        [Tooltip("How much cover to scatter, as a multiple of the baseline density.")]
        float _coverDensity = 1f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Pieces of low cover per piece of high cover.")]
        float _lowToHighCoverRatio = 2f;

        [SerializeField]
        [Tooltip("Rotate cover to fifteen-degree steps instead of quarter turns.")]
        bool _fineCoverRotation;

        // Hidden because a sixty-kilobyte string in an inspector is noise, not information. The
        // window shows what is in it; Save world... writes exactly these bytes.
        [SerializeField]
        [HideInInspector]
        string _worldJson;

        [NonSerialized]
        WorldDoc _document;

        [NonSerialized]
        string _documentJson;

        /// <summary>Seed the map is generated from.</summary>
        public ulong Seed
        {
            get => _seed;
            set => _seed = value;
        }

        /// <summary>Playfield extent in metres, X by Z.</summary>
        public Vector2 PlayfieldSize
        {
            get => _playfieldSize;
            set => _playfieldSize = value;
        }

        /// <summary>Number of lane bands running between the two spawns.</summary>
        public int LaneCount
        {
            get => _laneCount;
            set => _laneCount = value;
        }

        /// <summary>Placement grid resolution in metres.</summary>
        public float GridSize
        {
            get => _gridSize;
            set => _gridSize = value;
        }

        /// <summary>How much of a lane a structure may cover, as a multiple of the baseline share.</summary>
        public float StructureDensity
        {
            get => _structureDensity;
            set => _structureDensity = value;
        }

        /// <summary>How much cover to scatter, as a multiple of the baseline density.</summary>
        public float CoverDensity
        {
            get => _coverDensity;
            set => _coverDensity = value;
        }

        /// <summary>Pieces of low cover the generator places per piece of high cover.</summary>
        public float LowToHighCoverRatio
        {
            get => _lowToHighCoverRatio;
            set => _lowToHighCoverRatio = value;
        }

        /// <summary>Whether cover may be rotated to fifteen-degree steps rather than quarter turns.</summary>
        public bool FineCoverRotation
        {
            get => _fineCoverRotation;
            set => _fineCoverRotation = value;
        }

        /// <summary>The realiser that turns this map's document into GameObjects.</summary>
        public WorldRealizer Realizer => GetComponent<WorldRealizer>();

        /// <summary>True if this map has been generated or loaded.</summary>
        public bool HasDocument => !string.IsNullOrEmpty(_worldJson);

        /// <summary>
        /// The document this map holds, or null if it has none yet.
        /// </summary>
        /// <remarks>
        /// The returned document is the live one, so an edit to its override list takes effect
        /// immediately — but it is not persisted until <see cref="SetDocument"/> writes it back.
        /// </remarks>
        /// <exception cref="UnsupportedSchemaVersionException">The stored document is of a schema this build does not read.</exception>
        public WorldDoc Document
        {
            get
            {
                if (string.IsNullOrEmpty(_worldJson))
                {
                    _document = null;
                    _documentJson = null;
                    return null;
                }

                if (_document == null || !string.Equals(_documentJson, _worldJson, StringComparison.Ordinal))
                {
                    _document = ArenaJson.DeserializeWorld(_worldJson);
                    _documentJson = _worldJson;
                }

                return _document;
            }
        }

        /// <summary>
        /// Stores a document, or clears the map when given null. Call it again after mutating the
        /// document <see cref="Document"/> returned, to write the change back into the scene.
        /// </summary>
        public void SetDocument(WorldDoc doc)
        {
            _worldJson = doc != null ? ArenaJson.SerializeWorld(doc) : string.Empty;
            _document = doc;
            _documentJson = _worldJson;
        }

        /// <summary>The parameters the fields on this component describe.</summary>
        public ArenaParams BuildParams() => new ArenaParams
        {
            Seed = _seed,
            PlayfieldSize = new Vec2(_playfieldSize.x, _playfieldSize.y),
            LaneCount = _laneCount,
            GridSize = _gridSize,
            StructureDensity = _structureDensity,
            CoverDensity = _coverDensity,
            LowToHighCoverRatio = _lowToHighCoverRatio,
            FineCoverRotation = _fineCoverRotation,
        };

        /// <summary>
        /// Copies a parameter set onto this component's fields.
        /// </summary>
        /// <remarks>
        /// Loading a saved map goes through here. A document carries the parameters it was generated
        /// from, and leaving the component showing a different set would mean the next Regenerate
        /// silently built a different map from the one on screen.
        /// </remarks>
        public void ApplyParams(ArenaParams parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            _seed = parameters.Seed;
            _playfieldSize = new Vector2(parameters.PlayfieldSize.X, parameters.PlayfieldSize.Y);
            _laneCount = parameters.LaneCount;
            _gridSize = parameters.GridSize;
            _structureDensity = parameters.StructureDensity;
            _coverDensity = parameters.CoverDensity;
            _lowToHighCoverRatio = parameters.LowToHighCoverRatio;
            _fineCoverRotation = parameters.FineCoverRotation;
        }

        /// <summary>
        /// Generates a map from the current parameters, discarding any edits, and realises it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The realiser has no catalog.</exception>
        [ContextMenu("Generate")]
        public ResolvedWorld Generate()
        {
            SetDocument(ArenaLayoutGenerator.Generate(BuildParams(), RequireCatalog()));
            return Realize();
        }

        /// <summary>
        /// Generates a map from the current parameters and re-applies the existing edits on top,
        /// then realises the result.
        /// </summary>
        /// <remarks>
        /// Every override is carried over, including ones the new generation no longer has a target
        /// for. Those come back in <see cref="ResolvedWorld.OrphanedOverrides"/> for the caller to
        /// show; discarding one is the user's decision, never this method's.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The realiser has no catalog.</exception>
        [ContextMenu("Regenerate")]
        public ResolvedWorld Regenerate()
        {
            WorldDoc previous = Document;
            WorldDoc doc = ArenaLayoutGenerator.Generate(BuildParams(), RequireCatalog());

            if (previous != null)
            {
                for (int i = 0; i < previous.Overrides.Count; i++)
                {
                    doc.Overrides.Add(previous.Overrides[i]);
                }
            }

            SetDocument(doc);
            return Realize();
        }

        /// <summary>Realises the document this map holds, replacing whatever is in the scene.</summary>
        public ResolvedWorld Realize()
        {
            WorldDoc doc = Document;
            if (doc == null)
            {
                Realizer.Derealize();
                return new ResolvedWorld(Array.Empty<PlacedObject>(), Array.Empty<EditOverride>());
            }

            return Realizer.Realize(doc);
        }

        /// <summary>Drops the document and takes the realised map out of the scene.</summary>
        [ContextMenu("Clear")]
        public void Clear()
        {
            Realizer.Derealize();
            SetDocument(null);
        }

        Catalog RequireCatalog()
        {
            CatalogAsset asset = Realizer.Catalog;
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"'{name}' has no catalog assigned on its World Realizer, so there is nothing to " +
                    "select art from.");
            }

            return asset.ToCatalog();
        }
    }
}
