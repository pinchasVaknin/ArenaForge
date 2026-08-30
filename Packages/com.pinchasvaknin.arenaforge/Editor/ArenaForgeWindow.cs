using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CorePose = ArenaForge.Core.Pose;
using Object = UnityEngine.Object;

namespace ArenaForge.Editor
{
    /// <summary>
    /// The tool window: generate a map, judge it, edit it by hand, and regenerate without losing the
    /// edits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window owns three things the rest of the tool deliberately does not. It holds the
    /// analysis settings, because how a map is measured is not part of what a map is — see
    /// <see cref="AnalysisParams"/>. It runs <see cref="ArenaEditCapture"/>, so scene edits become
    /// document overrides only while a tool the user opened is watching. And it groups every
    /// mutation into one named Undo step, so a regeneration that touched two hundred GameObjects is
    /// one Ctrl+Z rather than two hundred.
    /// </para>
    /// <para>
    /// Analysis is run on a short delay after the document changes rather than on every edit, so
    /// dragging a crate does not put a thousand segment tests behind the mouse.
    /// </para>
    /// </remarks>
    public sealed class ArenaForgeWindow : EditorWindow
    {
        // Logical package paths rather than a location on disk: an installed package lives in
        // Library/PackageCache under a hashed folder name, and Unity resolves "Packages/<name>/…"
        // to it either way. Internal so a test can assert they still resolve — a path that stops
        // matching where the file lives leaves a window with no markup in it and nothing else fails.
        internal const string UxmlPath =
            "Packages/com.pinchasvaknin.arenaforge/Editor/UI/ArenaForgeWindow.uxml";

        internal const string UssPath =
            "Packages/com.pinchasvaknin.arenaforge/Editor/UI/ArenaForgeWindow.uss";

        /// <summary>Drag-and-drop payload key the catalog rows and the scene view agree on.</summary>
        const string DragKey = "ArenaForge.LogicalId";

        /// <summary>How often the scene is diffed against the document, in seconds.</summary>
        const double TickInterval = 0.1;

        /// <summary>How long the document must sit still before it is re-analysed, in seconds.</summary>
        const double AnalysisDelay = 0.4;

        [SerializeField]
        ArenaMap _map;

        [SerializeField]
        float _eyeHeight = 1.6f;

        [SerializeField]
        bool _showHeatmapInScene;

        readonly List<EditOverride> _orphans = new List<EditOverride>();
        readonly HashSet<string> _acknowledgedOrphans = new HashSet<string>(StringComparer.Ordinal);

        ArenaEditCapture _capture;
        MapReport _report;
        Texture2D _heatmapTexture;
        Texture2D _rampTexture;

        double _nextTick;
        double _analyseAt;
        bool _analysisDue;

        ObjectField _targetField;
        UnsignedLongField _seedField;
        Vector2Field _playfieldField;
        VisualElement _emptyState;
        VisualElement _body;
        VisualElement _metricRows;
        VisualElement _overrideRows;
        VisualElement _orphanRows;
        VisualElement _catalogRows;
        Foldout _overridesFoldout;
        Foldout _orphansFoldout;
        Foldout _catalogFoldout;
        Image _heatmapImage;
        Label _status;
        Label _verdict;
        Label _caveat;
        Label _placementSummary;
        Label _exposureSummary;
        Label _overrideEmpty;

        /// <summary>Opens the window, or focuses it if it is already open.</summary>
        [MenuItem("Window/ArenaForge/Arena Forge")]
        public static void Open() =>
            GetWindow<ArenaForgeWindow>("ArenaForge").minSize = new Vector2(320f, 460f);

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGui;
            Selection.selectionChanged -= OnSelectionChanged;

            DestroyTexture(ref _heatmapTexture);
            DestroyTexture(ref _rampTexture);
        }

        void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                rootVisualElement.Add(new Label($"ArenaForge: {UxmlPath} is missing."));
                return;
            }

            tree.CloneTree(rootVisualElement);

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (style != null)
            {
                rootVisualElement.styleSheets.Add(style);
            }

            BuildTargetField();
            BuildParameterFields();
            BuildButtons();
            CacheElements();

            if (_map == null)
            {
                _map = Object.FindFirstObjectByType<ArenaMap>(FindObjectsInactive.Include);
            }

            Bind(_map);
        }

        // --- construction -------------------------------------------------------------------

        void BuildTargetField()
        {
            _targetField = new ObjectField("Map")
            {
                objectType = typeof(ArenaMap),
                allowSceneObjects = true,
            };
            _targetField.RegisterValueChangedCallback(e => Bind(e.newValue as ArenaMap));
            rootVisualElement.Q<VisualElement>("target-slot").Add(_targetField);
        }

        void BuildParameterFields()
        {
            // Built in code rather than declared in the UXML because these three have editor-only
            // element types; the rest of the window is markup.
            _seedField = new UnsignedLongField("Seed") { style = { flexGrow = 1f } };
            _seedField.RegisterValueChangedCallback(e => Edit("change seed", m => m.Seed = e.newValue));

            var randomise = new Button(RandomiseSeed) { text = "↻", tooltip = "Pick a new seed" };
            randomise.style.width = 26f;

            var seedRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            seedRow.Add(_seedField);
            seedRow.Add(randomise);
            rootVisualElement.Q<VisualElement>("seed-slot").Add(seedRow);

            _playfieldField = new Vector2Field("Playfield (m)");
            _playfieldField.RegisterValueChangedCallback(
                e => Edit("resize playfield", m => m.PlayfieldSize = e.newValue));
            rootVisualElement.Q<VisualElement>("playfield-slot").Add(_playfieldField);

            Bind<IntegerField, int>("lane-count", (m, v) => m.LaneCount = Mathf.Max(1, v), "change lane count");
            Bind<FloatField, float>("grid-size", (m, v) => m.GridSize = Mathf.Max(0.05f, v), "change grid size");
            Bind<FloatField, float>(
                "structure-density", (m, v) => m.StructureDensity = Mathf.Max(0.01f, v),
                "change structure density");
            Bind<FloatField, float>(
                "cover-density", (m, v) => m.CoverDensity = Mathf.Max(0f, v), "change cover density");
            Bind<FloatField, float>(
                "low-high-ratio", (m, v) => m.LowToHighCoverRatio = Mathf.Max(0f, v),
                "change cover ratio");
            Bind<Toggle, bool>(
                "fine-rotation", (m, v) => m.FineCoverRotation = v, "change cover rotation");
            Bind<FloatField, float>(
                "terrain-amplitude", (m, v) => m.TerrainAmplitude = Mathf.Max(0f, v),
                "change terrain relief");
            Bind<FloatField, float>(
                "terrain-feature", (m, v) => m.TerrainFeatureSize = Mathf.Max(0.5f, v),
                "change terrain feature size");

            var eyeHeight = rootVisualElement.Q<FloatField>("eye-height");
            eyeHeight.value = _eyeHeight;
            eyeHeight.RegisterValueChangedCallback(e =>
            {
                _eyeHeight = Mathf.Max(0.1f, e.newValue);
                ScheduleAnalysis();
            });
        }

        void Bind<TField, TValue>(string name, Action<ArenaMap, TValue> apply, string action)
            where TField : BaseField<TValue>
        {
            TField field = rootVisualElement.Q<TField>(name);
            field.RegisterValueChangedCallback(e => Edit(action, m => apply(m, e.newValue)));
        }

        void BuildButtons()
        {
            rootVisualElement.Q<Button>("create-map").clicked += CreateMapInScene;
            rootVisualElement.Q<Button>("generate").clicked += () => Rebuild("generate map", m => m.Generate());
            rootVisualElement.Q<Button>("regenerate").clicked +=
                () => Rebuild("regenerate map", m => m.Regenerate());
            rootVisualElement.Q<Button>("clear").clicked += () => Rebuild("clear map", m =>
            {
                m.Clear();
                return new ResolvedWorld(Array.Empty<PlacedObject>(), Array.Empty<EditOverride>());
            });
            rootVisualElement.Q<Button>("save").clicked += Save;
            rootVisualElement.Q<Button>("load").clicked += Load;
        }

        void CacheElements()
        {
            _emptyState = rootVisualElement.Q<VisualElement>("empty-state");
            _body = rootVisualElement.Q<VisualElement>("body");
            _status = rootVisualElement.Q<Label>("status");
            _verdict = rootVisualElement.Q<Label>("report-verdict");
            _caveat = rootVisualElement.Q<Label>("report-caveat");
            _metricRows = rootVisualElement.Q<VisualElement>("metric-rows");
            _placementSummary = rootVisualElement.Q<Label>("placement-summary");
            _exposureSummary = rootVisualElement.Q<Label>("exposure-summary");
            _heatmapImage = rootVisualElement.Q<Image>("heatmap");
            _overrideRows = rootVisualElement.Q<VisualElement>("override-rows");
            _overrideEmpty = rootVisualElement.Q<Label>("override-empty");
            _orphanRows = rootVisualElement.Q<VisualElement>("orphan-rows");
            _catalogRows = rootVisualElement.Q<VisualElement>("catalog-rows");
            _overridesFoldout = rootVisualElement.Q<Foldout>("overrides-foldout");
            _orphansFoldout = rootVisualElement.Q<Foldout>("orphans-foldout");
            _catalogFoldout = rootVisualElement.Q<Foldout>("catalog-foldout");

            var showInScene = rootVisualElement.Q<Toggle>("show-in-scene");
            showInScene.value = _showHeatmapInScene;
            showInScene.RegisterValueChangedCallback(e =>
            {
                _showHeatmapInScene = e.newValue;
                SceneView.RepaintAll();
            });

            _rampTexture = BuildRampTexture();
            rootVisualElement.Q<VisualElement>("ramp").style.backgroundImage =
                Background.FromTexture2D(_rampTexture);
        }

        // --- target -------------------------------------------------------------------------

        void Bind(ArenaMap map)
        {
            _map = map;
            _capture = map != null ? new ArenaEditCapture(map) : null;
            _capture?.Rebuild();
            _orphans.Clear();
            _acknowledgedOrphans.Clear();
            _report = null;

            if (_targetField != null && !ReferenceEquals(_targetField.value, map))
            {
                _targetField.SetValueWithoutNotify(map);
            }

            RefreshAll();
            ScheduleAnalysis();
        }

        void OnSelectionChanged()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }

            var map = selected.GetComponent<ArenaMap>();
            if (map != null && map != _map)
            {
                Bind(map);
            }
        }

        void CreateMapInScene()
        {
            var host = new GameObject("ArenaForge Map");
            Undo.RegisterCreatedObjectUndo(host, "ArenaForge: create map");
            ArenaMap map = Undo.AddComponent<ArenaMap>(host);
            Undo.SetCurrentGroupName("ArenaForge: create map");

            Selection.activeGameObject = host;
            Bind(map);
            Status("Assign a catalog on the World Realizer, then press Generate.");
        }

        // --- actions ------------------------------------------------------------------------

        void Edit(string action, Action<ArenaMap> apply) => MapOperations.Edit(_map, action, apply);

        void RandomiseSeed()
        {
            ulong seed = MapOperations.RandomSeed();

            Edit("randomise seed", m => m.Seed = seed);
            _seedField.SetValueWithoutNotify(seed);
        }

        /// <summary>
        /// Runs one whole-map operation and re-reads everything in the window that depends on the
        /// result. The undo grouping is <see cref="MapOperations.Run"/>'s.
        /// </summary>
        void Rebuild(string action, Func<ArenaMap, ResolvedWorld> operation)
        {
            MapOperationResult result = MapOperations.Run(_map, action, operation);
            if (!result.Succeeded)
            {
                Status(result.Error);
                return;
            }

            Status(null);
            TakeOrphans(result.Resolved);
            _capture.Rebuild();
            RefreshAll();
            ScheduleAnalysis();
        }

        void Save()
        {
            string path;
            try
            {
                path = MapOperations.SaveWorld(_map);
            }
            catch (Exception error) when (error is InvalidOperationException || error is IOException)
            {
                Status(error.Message);
                return;
            }

            if (path != null)
            {
                Status($"Saved to {path}");
            }
        }

        void Load()
        {
            if (_map == null)
            {
                return;
            }

            WorldDoc doc;
            try
            {
                doc = MapOperations.OpenWorld();
            }
            catch (Exception error) when (error is UnsupportedSchemaVersionException ||
                                          error is InvalidOperationException ||
                                          error is ArgumentException ||
                                          error is IOException)
            {
                Status(error.Message);
                return;
            }

            if (doc == null)
            {
                return;
            }

            Rebuild("load map", map =>
            {
                map.ApplyParams(doc.Parameters);
                map.SetDocument(doc);
                return map.Realize();
            });
        }

        // --- the edit loop ------------------------------------------------------------------

        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextTick)
            {
                return;
            }

            _nextTick = now + TickInterval;

            if (_capture != null && _map != null && _capture.Tick())
            {
                // The document may have moved under the window rather than because of a scene edit
                // — the scene-view overlay regenerating is the ordinary case — so the orphan list
                // is re-read too, not just the override list.
                TakeOrphans(_map.Document.Resolve());
                RefreshOverrides();
                ScheduleAnalysis();
            }

            // The seed is the one parameter the overlay also writes, so the field follows it rather
            // than assuming this window is the only thing that can have changed it.
            if (_map != null && _seedField != null && _seedField.value != _map.Seed)
            {
                _seedField.SetValueWithoutNotify(_map.Seed);
            }

            if (_analysisDue && now >= _analyseAt)
            {
                _analysisDue = false;
                Analyse();
            }
        }

        void OnUndoRedo()
        {
            // The document is authoritative and the scene follows it, so an undo that put a
            // different document back has to be re-read before the next diff, or the capture would
            // measure the new document against the old scene and record the undo as an edit.
            _capture?.Rebuild();
            ReadParametersFromMap();
            RefreshAll();
            ScheduleAnalysis();
            Repaint();
        }

        void ScheduleAnalysis()
        {
            _analysisDue = true;
            _analyseAt = EditorApplication.timeSinceStartup + AnalysisDelay;
        }

        void Analyse()
        {
            WorldDoc doc = _map != null ? _map.Document : null;
            CatalogAsset catalogAsset = _map != null ? _map.Realizer.Catalog : null;
            if (doc == null || catalogAsset == null)
            {
                _report = null;
                DestroyTexture(ref _heatmapTexture);
                RefreshReport();
                return;
            }

            try
            {
                _report = MapAnalyzer.Analyze(
                    doc, catalogAsset.ToCatalog(), new AnalysisParams { EyeHeight = _eyeHeight });
            }
            catch (Exception error) when (error is InvalidOperationException ||
                                          error is ArgumentException)
            {
                _report = null;
                Status(error.Message);
                RefreshReport();
                return;
            }

            DestroyTexture(ref _heatmapTexture);
            _heatmapTexture = ExposureHeatmap.ToTexture(_report.Exposure);
            RefreshReport();

            if (_showHeatmapInScene)
            {
                SceneView.RepaintAll();
            }
        }

        // --- scene view ---------------------------------------------------------------------

        void OnSceneGui(SceneView view)
        {
            if (_map == null)
            {
                return;
            }

            if (_showHeatmapInScene && _report != null && Event.current.type == EventType.Repaint)
            {
                ExposureHeatmap.DrawInScene(_report.Exposure, _map.Realizer.Root);
            }

            HandleCatalogDrop();
        }

        void HandleCatalogDrop()
        {
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            {
                return;
            }

            if (!(DragAndDrop.GetGenericData(DragKey) is string logicalId))
            {
                return;
            }

            if (!_map.HasDocument)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                current.Use();
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                if (TryGroundPoint(current.mousePosition, out Vec3 local))
                {
                    _capture.RecordAdd(logicalId, CorePose.At(local), TagsFor(logicalId));
                    TakeOrphans(_map.Document.Resolve());
                    RefreshAll();
                    ScheduleAnalysis();
                }
            }

            current.Use();
        }

        /// <summary>
        /// Where a scene-view mouse position lands on the map's ground plane, in the realisation
        /// root's local space — the space document poses are expressed in.
        /// </summary>
        bool TryGroundPoint(Vector2 mousePosition, out Vec3 local)
        {
            Transform root = _map.Realizer.Root;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var ground = new Plane(root.up, root.position);

            if (!ground.Raycast(ray, out float distance))
            {
                local = default;
                return false;
            }

            Vector3 point = root.InverseTransformPoint(ray.GetPoint(distance));
            local = CoreConvert.ToCore(point);
            return true;
        }

        IReadOnlyList<string> TagsFor(string logicalId)
        {
            CatalogAsset asset = _map.Realizer.Catalog;
            if (asset == null)
            {
                return null;
            }

            for (int i = 0; i < asset.Rows.Count; i++)
            {
                if (string.Equals(asset.Rows[i].LogicalId, logicalId, StringComparison.Ordinal))
                {
                    return asset.Rows[i].Tags;
                }
            }

            return null;
        }

        // --- panels -------------------------------------------------------------------------

        void RefreshAll()
        {
            if (_emptyState == null)
            {
                return;
            }

            bool bound = _map != null;
            _emptyState.style.display = bound ? DisplayStyle.None : DisplayStyle.Flex;
            _body.style.display = bound ? DisplayStyle.Flex : DisplayStyle.None;

            if (bound)
            {
                ReadParametersFromMap();
                RefreshOverrides();
                RefreshCatalog();
                RefreshReport();
            }
        }

        void ReadParametersFromMap()
        {
            if (_map == null || _seedField == null)
            {
                return;
            }

            _seedField.SetValueWithoutNotify(_map.Seed);
            _playfieldField.SetValueWithoutNotify(_map.PlayfieldSize);
            Set<IntegerField, int>("lane-count", _map.LaneCount);
            Set<FloatField, float>("grid-size", _map.GridSize);
            Set<FloatField, float>("structure-density", _map.StructureDensity);
            Set<FloatField, float>("cover-density", _map.CoverDensity);
            Set<FloatField, float>("low-high-ratio", _map.LowToHighCoverRatio);
            Set<Toggle, bool>("fine-rotation", _map.FineCoverRotation);
            Set<FloatField, float>("terrain-amplitude", _map.TerrainAmplitude);
            Set<FloatField, float>("terrain-feature", _map.TerrainFeatureSize);
        }

        void Set<TField, TValue>(string name, TValue value)
            where TField : BaseField<TValue> =>
            rootVisualElement.Q<TField>(name).SetValueWithoutNotify(value);

        void RefreshReport()
        {
            if (_metricRows == null)
            {
                return;
            }

            _metricRows.Clear();

            if (_report == null)
            {
                _verdict.text = _map != null && _map.HasDocument ? "measuring..." : "no map yet";
                _verdict.RemoveFromClassList("af-pass");
                _verdict.RemoveFromClassList("af-fail");
                _placementSummary.text = string.Empty;
                _exposureSummary.text = string.Empty;
                _caveat.text = string.Empty;
                _heatmapImage.image = null;
                return;
            }

            _verdict.text = _report.IsPlayable
                ? "Playable — every metric within threshold"
                : $"Not playable — {_report.Failures.Count} metric(s) out of threshold";
            _verdict.EnableInClassList("af-pass", _report.IsPlayable);
            _verdict.EnableInClassList("af-fail", !_report.IsPlayable);
            _caveat.text = Caveats();

            for (int i = 0; i < _report.Readings.Count; i++)
            {
                _metricRows.Add(MetricRow(_report.Readings[i]));
            }

            PlacementStats stats = _report.PlacementStats;
            _placementSummary.text = string.Format(
                CultureInfo.InvariantCulture,
                "Placement: {0} attempted, {1} accepted, {2} rejected.",
                stats.Attempted, stats.Accepted, stats.Rejected);

            ExposureMap exposure = _report.Exposure;
            _exposureSummary.text = string.Format(
                CultureInfo.InvariantCulture,
                "min {0:0.##}, mean {1:0.##}, max {2:0.##} over {3} observers and {4} walkable cells.",
                exposure.Min, exposure.Mean, exposure.Max, exposure.ObserverCount, exposure.Walkable.Count);

            _heatmapImage.image = _heatmapTexture;
        }

        /// <summary>
        /// What the verdict above does not cover, in one line, or nothing when it covers everything.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Relief is the one that matters.</strong> Every metric here is measured on the
        /// flat: occluders are rectangles on the XZ plane and exposure is sampled at one eye height
        /// on the ground, so a sightline that runs through a hill is reported clear and a slope is
        /// not something connectivity knows about. A map with no relief is measured exactly; a map
        /// with relief is measured as though it had none, and saying "playable" without saying that
        /// is the report claiming more than it checked.
        /// </para>
        /// <para>
        /// The road warning is the other half, and it names both parameters because neither is wrong
        /// on its own. Ground steeper than <c>MaxRoadGradient</c> is closed to the router, so a
        /// limit set low against a lively amplitude leaves it a corner of the map to work in — and
        /// the network it lays there is a tangle rather than a road across the arena. The share is
        /// measured by the router itself, so this reports rather than guesses.
        /// </para>
        /// </remarks>
        string Caveats()
        {
            if (_map == null)
            {
                return string.Empty;
            }

            var lines = new List<string>(2);

            if (_map.TerrainAmplitude > 0f)
            {
                lines.Add(
                    "Measured on flat ground: relief is not in the visibility model, so a sightline " +
                    "through a hill reads as clear.");
            }

            RoadNetwork roads = _map.Roads;
            if (roads != null && roads.PassableShare < RoadReachWarning)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Roads run level over {0:0.#}% of the map: Max Road Gradient ({1:0.##}) is " +
                    "under the slope Terrain Amplitude ({2:0.#} m) raises, so the rest is cut in.",
                    roads.PassableShare * 100f, _map.MaxRoadGradient, _map.TerrainAmplitude));
            }

            return string.Join("  ", lines);
        }

        /// <summary>
        /// How little of the map a road may cross before the report says the two parameters are
        /// fighting.
        /// </summary>
        /// <remarks>
        /// Three quarters, from the measurement that prompted it: at twelve metres of relief and a
        /// gradient limit of a quarter, the network spanned half the map and scribbled in a corner.
        /// A map with buildings and spawns on it never has all of its ground open to a road anyway,
        /// so the threshold is set where the loss stops being the map's own furniture and starts
        /// being the ground itself. The consequence it warns about has since changed: a road across
        /// that ground is now laid and cut in rather than refused, so what a low reading means is
        /// earthworks rather than a network that never arrives.
        /// </remarks>
        const float RoadReachWarning = 0.75f;

        static VisualElement MetricRow(MetricReading reading)
        {
            var row = new VisualElement();
            row.AddToClassList("af-metric");

            var name = new Label(reading.Metric);
            name.AddToClassList("af-metric-name");

            var value = new Label(reading.Value.ToString("0.###", CultureInfo.InvariantCulture));
            value.AddToClassList("af-metric-value");

            var threshold = new Label(string.Format(
                CultureInfo.InvariantCulture, "{0} {1:0.###}", reading.BoundText, reading.Threshold));
            threshold.AddToClassList("af-metric-threshold");

            var verdict = new Label(reading.Meets ? "pass" : "FAIL");
            verdict.AddToClassList("af-metric-verdict");
            verdict.AddToClassList(reading.Meets ? "af-pass" : "af-fail");

            row.Add(name);
            row.Add(value);
            row.Add(threshold);
            row.Add(verdict);
            return row;
        }

        void RefreshOverrides()
        {
            if (_overrideRows == null || _map == null)
            {
                return;
            }

            WorldDoc doc = _map.Document;
            IReadOnlyList<EditOverride> edits = doc != null
                ? (IReadOnlyList<EditOverride>)doc.Overrides
                : Array.Empty<EditOverride>();

            _overrideRows.Clear();
            _overridesFoldout.text = $"Overrides ({edits.Count})";
            _overrideEmpty.style.display = edits.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < edits.Count; i++)
            {
                EditOverride edit = edits[i];
                _overrideRows.Add(Row($"{edit.Op}  {edit.TargetId}", "Revert", () =>
                {
                    _capture.Revert(edit);
                    TakeOrphans(_map.Document.Resolve());
                    RefreshAll();
                    ScheduleAnalysis();
                }));
            }

            RefreshOrphans();
        }

        void RefreshOrphans()
        {
            _orphanRows.Clear();

            int shown = 0;
            for (int i = 0; i < _orphans.Count; i++)
            {
                EditOverride orphan = _orphans[i];
                if (_acknowledgedOrphans.Contains(orphan.TargetId))
                {
                    continue;
                }

                shown++;
                var row = new VisualElement();
                row.AddToClassList("af-row");

                var label = new Label($"{orphan.Op}  {orphan.TargetId}");
                label.AddToClassList("af-row-label");
                row.Add(label);

                row.Add(new Button(() =>
                {
                    _acknowledgedOrphans.Add(orphan.TargetId);
                    RefreshOrphans();
                })
                { text = "Keep", tooltip = "Leave the edit in place; it may find its target again." });

                row.Add(new Button(() =>
                {
                    _capture.Revert(orphan);
                    _orphans.Remove(orphan);
                    RefreshAll();
                })
                { text = "Discard", tooltip = "Delete the edit." });

                _orphanRows.Add(row);
            }

            _orphansFoldout.text = $"Orphaned overrides ({shown})";
            _orphansFoldout.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void TakeOrphans(ResolvedWorld resolved)
        {
            _orphans.Clear();
            if (resolved == null)
            {
                return;
            }

            for (int i = 0; i < resolved.OrphanedOverrides.Count; i++)
            {
                _orphans.Add(resolved.OrphanedOverrides[i]);
            }
        }

        void RefreshCatalog()
        {
            _catalogRows.Clear();

            CatalogAsset asset = _map != null ? _map.Realizer.Catalog : null;
            if (asset == null)
            {
                _catalogFoldout.text = "Catalog (none assigned)";
                return;
            }

            _catalogFoldout.text = $"Catalog ({asset.Rows.Count})";

            for (int i = 0; i < asset.Rows.Count; i++)
            {
                CatalogAsset.Row entry = asset.Rows[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.LogicalId))
                {
                    continue;
                }

                _catalogRows.Add(CatalogRow(entry.LogicalId));
            }
        }

        static VisualElement CatalogRow(string logicalId)
        {
            var row = new VisualElement();
            row.AddToClassList("af-catalog-row");

            var label = new Label(logicalId);
            label.AddToClassList("af-catalog-id");
            row.Add(label);

            var hint = new Label("drag to scene");
            hint.AddToClassList("af-catalog-hint");
            row.Add(hint);

            row.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<Object>();
                DragAndDrop.SetGenericData(DragKey, logicalId);
                DragAndDrop.StartDrag(logicalId);
                e.StopPropagation();
            });

            return row;
        }

        static VisualElement Row(string text, string buttonText, Action action)
        {
            var row = new VisualElement();
            row.AddToClassList("af-row");

            var label = new Label(text);
            label.AddToClassList("af-row-label");
            row.Add(label);
            row.Add(new Button(action) { text = buttonText });

            return row;
        }

        void Status(string message)
        {
            if (_status == null)
            {
                return;
            }

            _status.text = message ?? string.Empty;
            _status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static Texture2D BuildRampTexture()
        {
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "ArenaForge Ramp",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color[width];
            for (int i = 0; i < width; i++)
            {
                pixels[i] = ExposureHeatmap.Sample(i / (width - 1f));
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }
    }
}
