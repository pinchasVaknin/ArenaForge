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

        [SerializeField]
        [Min(0f)]
        [Tooltip("Peak-to-trough height variation of the ground, in metres. Zero is a flat map.")]
        float _terrainAmplitude;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("How wide the largest rises and hollows are, in metres.")]
        float _terrainFeatureSize = 20f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("How many redundant connections the road network lays over the one route it "
            + "needs. Zero lays no roads at all.")]
        float _roadDensity;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Carriageway width of a trunk road, in metres.")]
        float _arteryWidth = 4f;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Carriageway width of a branch road, in metres.")]
        float _pathWidth = 2f;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Steepest slope a road may be graded to, as rise over run. Ground steeper than "
            + "this is dear to cross rather than shut, so lowering it far makes a hilly map's roads "
            + "cut deeper rather than go missing.")]
        float _maxRoadGradient = 0.6f;

        [SerializeField]
        [Tooltip("Optional. The terrain the generated ground is written into.")]
        Terrain _terrain;

        [SerializeField]
        [Tooltip("Terrain layer the arteries are painted in. Negative paints no road surface.")]
        int _arteryLayer = -1;

        [SerializeField]
        [Tooltip("Terrain layer the paths are painted in. Negative paints no road surface.")]
        int _pathLayer = -1;

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

        /// <summary>Peak-to-trough height variation of the ground, in metres.</summary>
        public float TerrainAmplitude
        {
            get => _terrainAmplitude;
            set => _terrainAmplitude = value;
        }

        /// <summary>How wide the largest ground features are, in metres.</summary>
        public float TerrainFeatureSize
        {
            get => _terrainFeatureSize;
            set => _terrainFeatureSize = value;
        }

        /// <summary>
        /// How many redundant connections the road network lays over the one route it needs, as a
        /// multiple of the baseline. Zero disables the road stage entirely.
        /// </summary>
        /// <remarks>
        /// Zero by default, for the reason <see cref="TerrainAmplitude"/> is: turning it up
        /// changes the output of every map that already exists. The three below describe roads
        /// that are not laid until this one is turned up.
        /// </remarks>
        public float RoadDensity
        {
            get => _roadDensity;
            set => _roadDensity = value;
        }

        /// <summary>Carriageway width of a trunk road, in metres.</summary>
        public float ArteryWidth
        {
            get => _arteryWidth;
            set => _arteryWidth = value;
        }

        /// <summary>Carriageway width of a branch road, in metres.</summary>
        /// <remarks>
        /// Half the artery by default. The gap between the two is what tells a player which way is
        /// the way through, so the pair is worth setting together.
        /// </remarks>
        public float PathWidth
        {
            get => _pathWidth;
            set => _pathWidth = value;
        }

        /// <summary>Steepest slope a road may be graded to, as rise over run.</summary>
        public float MaxRoadGradient
        {
            get => _maxRoadGradient;
            set => _maxRoadGradient = value;
        }

        /// <summary>
        /// The terrain the generated ground is written into, or null to leave the scene's ground
        /// alone.
        /// </summary>
        /// <remarks>
        /// Optional, and a map with an amplitude but no terrain is a map whose objects follow a
        /// ground nothing draws. That is why <see cref="ArenaParams.TerrainAmplitude"/> is zero by
        /// default: verticality is something you turn on once there is a terrain to put it on.
        /// </remarks>
        public Terrain Terrain
        {
            get => _terrain;
            set => _terrain = value;
        }

        /// <summary>
        /// Index of the terrain layer the arteries are painted in, or negative to paint nothing.
        /// </summary>
        /// <remarks>
        /// Off by default, and for the reason the amplitude is zero by default: which layer of
        /// somebody's terrain material is road is a fact about their art that this tool cannot
        /// guess, and painting into a layer picked at random would replace ground the project
        /// meant to be there. A project that has not set these gets the map it always got.
        /// </remarks>
        public int ArteryLayer
        {
            get => _arteryLayer;
            set => _arteryLayer = value;
        }

        /// <summary>
        /// Index of the terrain layer the paths are painted in, or negative to paint nothing.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="ArteryLayer"/> rather than one road layer for both, because
        /// the two classes are two kinds of road — a trunk is tarmac and a branch is a track — and
        /// a project that wants one surface for both simply gives them the same index.
        /// </remarks>
        public int PathLayer
        {
            get => _pathLayer;
            set => _pathLayer = value;
        }

        /// <summary>The realiser that turns this map's document into GameObjects.</summary>
        public WorldRealizer Realizer => GetComponent<WorldRealizer>();

        /// <summary>
        /// The road network of the document as it was last realised, or null if this map has not
        /// been realised since the assembly was loaded.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Kept so that a scene-view display of the roads has something to draw without rebuilding
        /// one. A network is a routing sweep over the whole playfield — the expensive half of
        /// rebuilding the ground — and anything that recomputed it to draw it would be paying that
        /// per repaint per camera.
        /// </para>
        /// <para>
        /// <strong>Not serialised, and null is a legitimate answer.</strong> It is derived from the
        /// document exactly as the terrain is, so storing it would be storing the same map twice and
        /// inviting the two to disagree. A caller that finds it null draws nothing rather than
        /// building one: the network survives until the next domain reload, and the next Generate,
        /// Regenerate or Load puts it back.
        /// </para>
        /// </remarks>
        public RoadNetwork Roads { get; private set; }

        /// <summary>
        /// The ground the last realise put this map on, or null if it has not been realised in this
        /// domain.
        /// </summary>
        /// <remarks>
        /// Kept for the same reason <see cref="Roads"/> is, and out of the same call: it comes back
        /// from <c>ArenaLayoutGenerator.Terrain</c> beside the network, and rebuilding it per repaint
        /// would be re-finding the roads several times a frame. What wants it is the scene view —
        /// guides drawn at a fixed height are buried in a hill and hang in the air over a hollow, so
        /// anything drawn flat on the ground has to be able to ask how high the ground is.
        /// Not serialised, on the same grounds: it is derived from the document, and a stored copy
        /// is the same map twice.
        /// </remarks>
        public TerrainField Ground { get; private set; }

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
            TerrainAmplitude = _terrainAmplitude,
            TerrainFeatureSize = _terrainFeatureSize,
            RoadDensity = _roadDensity,
            ArteryWidth = _arteryWidth,
            PathWidth = _pathWidth,
            MaxRoadGradient = _maxRoadGradient,
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
            _terrainAmplitude = parameters.TerrainAmplitude;
            _terrainFeatureSize = parameters.TerrainFeatureSize;
            _roadDensity = parameters.RoadDensity;
            _arteryWidth = parameters.ArteryWidth;
            _pathWidth = parameters.PathWidth;
            _maxRoadGradient = parameters.MaxRoadGradient;
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
        /// <remarks>
        /// The ground goes down before the objects that stand on it, and it is rebuilt from the
        /// document rather than remembered: a map loaded from a file has never had its terrain
        /// written, and a map whose seed has just changed has the wrong one.
        /// </remarks>
        public ResolvedWorld Realize()
        {
            WorldDoc doc = Document;
            if (doc == null)
            {
                Roads = null;
                Ground = null;
                Realizer.Derealize();
                return new ResolvedWorld(Array.Empty<PlacedObject>(), Array.Empty<EditOverride>());
            }

            // The ground and the network come out of one call, because rebuilding the network is
            // the expensive half of rebuilding the ground and a second call would do it twice. A
            // map with no terrain still gets its network kept — nothing is written anywhere, but
            // what the roads are is a fact about the document rather than about the ground being
            // drawn, and the scene-view display is entitled to it either way.
            TerrainField ground = ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);
            Roads = roads;
            Ground = ground;

            if (_terrain != null)
            {
                TerrainWriter.Apply(ground, _terrain, transform);

                // After the heights, because the splat is written over the window the network
                // reaches and the terrain has to be the right size and in the right place before
                // that window means anything. Both are off by default: see ArteryLayer.
                if (_arteryLayer >= 0 && _pathLayer >= 0)
                {
                    // Unpainted before it is painted. A network is drawn over whatever the last one
                    // left, so without this a second Generate lays its roads on top of the first
                    // map's and the terrain keeps every network it has ever been given.
                    TerrainSplatWriter.Clear(_terrain, _arteryLayer, _pathLayer);
                    TerrainSplatWriter.Apply(roads, ground, _terrain, _arteryLayer, _pathLayer);
                }
            }

            return Realizer.Realize(doc);
        }

        /// <summary>
        /// Flattens the terrain to the height of the world origin, or to the bottom of the terrain if the origin is below it.
        /// </summary>
        private void FlattenTerrain()
        {
            if (_terrain != null)
            {
                TerrainData tData = _terrain.terrainData;
                int res = tData.heightmapResolution;

                float worldZeroHeight = (0f - _terrain.transform.position.y) / tData.size.y;

                worldZeroHeight = Mathf.Clamp01(worldZeroHeight);

                float[,] flatHeights = new float[res, res];

                for (int i = 0; i < res; i++)
                {
                    for (int j = 0; j < res; j++)
                    {
                        flatHeights[i, j] = worldZeroHeight;
                    }
                }

                tData.SetHeights(0, 0, flatHeights);
            }
        }

        /// <summary>Drops the document and takes the realised map out of the scene.</summary>
        [ContextMenu("Clear")]
        public void Clear()
        {
            Roads = null;
            Ground = null;
            Realizer.Derealize();
            FlattenTerrain();

            // The heights go back and the paint has to go with them, or a cleared map keeps a road
            // network drawn across a field with nothing on it.
            TerrainSplatWriter.Clear(_terrain, _arteryLayer, _pathLayer);
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
