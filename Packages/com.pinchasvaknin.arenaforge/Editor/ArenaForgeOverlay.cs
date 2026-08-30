using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ArenaForge.Editor
{
    /// <summary>
    /// The controls you use while arranging, in the scene view with the thing you are arranging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately not a second copy of <see cref="ArenaForgeWindow"/>. The window is the
    /// instrument panel — parameters, validation metrics, the heatmap, the override list, orphaned
    /// overrides, the catalog — and you read it. The overlay is the pair of hands: the seed, a
    /// re-roll, the whole-document buttons, saving, loading and export, how many edits are live,
    /// and what the thing you have just clicked on is. Anything you would look at for more than a
    /// second belongs in the window, because two panels showing the same thing is two panels to
    /// keep in step.
    /// </para>
    /// <para>
    /// A button is not a panel, which is why Generate, the seed and now Save and Load are on both
    /// surfaces while no measurement is on either twice. The split is between reading and acting,
    /// not between the two windows: an action you reach for while arranging belongs where your
    /// eyes already are, and both surfaces run it through <see cref="MapOperations"/> so there is
    /// one implementation behind the two buttons.
    /// </para>
    /// <para>
    /// It is not a remote control for the window either: it finds its own target, runs its
    /// operations through <see cref="MapOperations"/>, and works exactly the same with the window
    /// closed. The two surfaces share the map, not each other.
    /// </para>
    /// <para>
    /// Two modes, because there are two kinds of thing to arrange. Map mode is the arena. Item mode
    /// is one building on its own, and it carries more than map mode does — the building's whole
    /// shaping parameters as well as the hands — because a building has no window panel:
    /// everything the window shows is a measurement of an arena, and none of it means anything
    /// about a stack of storeys. What is not here is on the <see cref="ArenaBuilding"/> component's
    /// own inspector, which is where a value you set once and forget belongs.
    /// </para>
    /// <para>
    /// The one parameter map mode does carry is how big the arena is, and it is here because item
    /// mode carries the footprint of a building: a tool where you can resize the building you are
    /// looking at but have to go to another window to resize the map around it is a tool with a
    /// seam in it. That is one field, not the window's parameter panel — the other eight are still
    /// only there, and see <see cref="BuildMapParams"/>.
    /// </para>
    /// </remarks>
    [Overlay(typeof(SceneView), OverlayId, "ArenaForge", true)]
    public sealed class ArenaForgeOverlay : Overlay
    {
        const string OverlayId = "arenaforge-scene-controls";

        // Logical package paths, for the same reason the window uses them: an installed package
        // lives under a hashed folder in Library/PackageCache, and "Packages/<name>/…" resolves to
        // it either way. Internal so a test can assert they still point at the markup.
        internal const string UxmlPath =
            "Packages/com.pinchasvaknin.arenaforge/Editor/UI/ArenaForgeOverlay.uxml";

        internal const string UssPath =
            "Packages/com.pinchasvaknin.arenaforge/Editor/UI/ArenaForgeOverlay.uss";

        /// <summary>Where the guides toggle is remembered, so it survives a domain reload.</summary>
        const string GuidesPrefKey = "ArenaForge.Overlay.Guides";

        /// <summary>Where the roads toggle is remembered, so it survives a domain reload.</summary>
        /// <remarks>
        /// Off by default, where the guides are on. The guides describe every map there is, so
        /// showing them costs a reader nothing; a network is a feature most maps do not have —
        /// <see cref="ArenaParams.RoadDensity"/> is zero unless it is asked for — and a toggle that
        /// is on by default and draws nothing on the default map is a control that looks broken.
        /// </remarks>
        const string RoadsPrefKey = "ArenaForge.Overlay.Roads";

        /// <summary>Editor pref the placement verdict toggle is remembered under.</summary>
        const string PlacementPrefKey = "ArenaForge.Overlay.Placement";

        /// <summary>Where the mode is remembered, so it survives a domain reload.</summary>
        const string ModePrefKey = "ArenaForge.Overlay.ItemMode";

        /// <summary>Class marking the selected mode button.</summary>
        const string SelectedModeClass = "af-overlay-mode-on";

        /// <summary>How often the labels are re-read from the bound component, in seconds.</summary>
        const double RefreshInterval = 0.2;

        /// <summary>Default folder an exported building is offered to.</summary>
        const string ExportFolder = "Assets";

        /// <summary>What the overlay is arranging.</summary>
        enum Mode
        {
            /// <summary>An arena.</summary>
            Map,

            /// <summary>One building on its own.</summary>
            Item,
        }

        bool _guides = EditorPrefs.GetBool(GuidesPrefKey, true);
        bool _roads = EditorPrefs.GetBool(RoadsPrefKey, false);
        bool _placement = EditorPrefs.GetBool(PlacementPrefKey, true);
        Mode _mode = EditorPrefs.GetBool(ModePrefKey, false) ? Mode.Item : Mode.Map;
        bool _boundMap;
        bool _boundBuilding;
        int _floorRows = -1;
        double _nextRefresh;

        ArenaMap _map;
        ArenaBuilding _building;

        VisualElement _mapSection;
        VisualElement _itemSection;
        Button _modeMap;
        Button _modeItem;

        ObjectField _mapField;
        UnsignedLongField _seedField;
        Vector2Field _mapSizeField;
        VisualElement _controls;
        Label _overrideCount;
        Label _selection;
        VisualElement _swapRow;
        DropdownField _swapField;
        string _swapBoundTo;

        ObjectField _buildingField;
        UnsignedLongField _buildingSeedField;
        VisualElement _buildingControls;
        VisualElement _floors;
        Vector2Field _footprintField;
        IntegerField _floorCountField;
        FloatField _floorHeightField;
        FloatField _contentDensityField;
        Label _buildingSummary;

        Label _status;

        /// <inheritdoc />
        public override void OnCreated()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
        }

        /// <inheritdoc />
        public override void OnWillBeDestroyed()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
        }

        /// <inheritdoc />
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                root.Add(new Label($"ArenaForge: {UxmlPath} is missing."));
                return root;
            }

            tree.CloneTree(root);

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (style != null)
            {
                root.styleSheets.Add(style);
            }

            _mapSection = root.Q<VisualElement>("map-mode");
            _itemSection = root.Q<VisualElement>("item-mode");
            _controls = root.Q<VisualElement>("controls");
            _buildingControls = root.Q<VisualElement>("building-controls");
            _overrideCount = root.Q<Label>("override-count");
            _selection = root.Q<Label>("selection");
            _buildingSummary = root.Q<Label>("building-summary");
            _floors = root.Q<VisualElement>("floors");
            _status = root.Q<Label>("status");

            BuildModeButtons(root);
            BuildMapField(root);
            BuildSeedRow(root);
            BuildMapParams(root);
            BuildButtons(root);
            BuildGuidesToggle(root);
            BuildSwapRow(root);

            BuildBuildingField(root);
            BuildBuildingSeedRow(root);
            BuildBuildingParams(root);
            BuildBuildingButtons(root);

            Bind(FindMap());
            Bind(FindBuilding());
            ApplyMode();
            return root;
        }

        // --- construction -------------------------------------------------------------------

        void BuildModeButtons(VisualElement root)
        {
            _modeMap = root.Q<Button>("mode-map");
            _modeItem = root.Q<Button>("mode-item");

            _modeMap.clicked += () => SetMode(Mode.Map);
            _modeItem.clicked += () => SetMode(Mode.Item);
        }

        void BuildMapField(VisualElement root)
        {
            _mapField = new ObjectField("Map")
            {
                objectType = typeof(ArenaMap),
                allowSceneObjects = true,
            };
            _mapField.RegisterValueChangedCallback(e => Bind(e.newValue as ArenaMap));
            root.Q<VisualElement>("map-slot").Add(_mapField);
        }

        void BuildSeedRow(VisualElement root)
        {
            _seedField = new UnsignedLongField("Seed") { style = { flexGrow = 1f } };
            _seedField.RegisterValueChangedCallback(
                e => MapOperations.Edit(_map, "change seed", m => m.Seed = e.newValue));

            root.Q<VisualElement>("seed-slot").Add(SeedRow(_seedField, RerollSeed));
        }

        /// <summary>
        /// The one shaping parameter map mode carries: how big the arena is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It is here for the reason the seed is on both surfaces — an action you reach for while
        /// looking at the thing it changes belongs where your eyes already are — and it is the
        /// same value the window shows, written through the same component. What the split in
        /// ARCHITECTURE.md section 6 forbids is a <em>panel</em> in both places: the window's
        /// parameter panel is nine fields, a foldout and the arithmetic that goes with them, and
        /// none of the other eight has moved.
        /// </para>
        /// <para>
        /// This one did because item mode already had its footprint field, and a tool where the
        /// size of a building is a control and the size of the arena is somewhere else is a tool
        /// that has to be explained. Like the seed, it is re-read from the component on every tick
        /// rather than assumed, because the window can write it too.
        /// </para>
        /// </remarks>
        void BuildMapParams(VisualElement root)
        {
            _mapSizeField = new Vector2Field("Map size (m)");
            _mapSizeField.RegisterValueChangedCallback(e => MapOperations.Edit(
                _map, "resize map",
                m => m.PlayfieldSize = new Vector2(
                    Mathf.Max(1f, e.newValue.x), Mathf.Max(1f, e.newValue.y))));

            root.Q<VisualElement>("map-params").Add(_mapSizeField);
        }

        void BuildButtons(VisualElement root)
        {
            root.Q<Button>("generate").clicked += () => Run("generate map", _map, m => m.Generate());
            root.Q<Button>("regenerate").clicked += () => Run("regenerate map", _map, m => m.Regenerate());
            root.Q<Button>("clear").clicked += () => Run("clear map", _map, m =>
            {
                m.Clear();
                return Nothing;
            });

            root.Q<Button>("save").clicked += Save;
            root.Q<Button>("load").clicked += Load;
            root.Q<Button>("map-export").clicked += ExportMap;
        }

        void BuildGuidesToggle(VisualElement root)
        {
            var guides = root.Q<Toggle>("guides");
            guides.SetValueWithoutNotify(_guides);
            guides.RegisterValueChangedCallback(e =>
            {
                _guides = e.newValue;
                EditorPrefs.SetBool(GuidesPrefKey, _guides);
                SceneView.RepaintAll();
            });

            var roads = root.Q<Toggle>("roads");
            roads.SetValueWithoutNotify(_roads);
            roads.RegisterValueChangedCallback(e =>
            {
                _roads = e.newValue;
                EditorPrefs.SetBool(RoadsPrefKey, _roads);
                SceneView.RepaintAll();
            });

            var placement = root.Q<Toggle>("placement");
            placement.SetValueWithoutNotify(_placement);
            placement.RegisterValueChangedCallback(e =>
            {
                _placement = e.newValue;
                EditorPrefs.SetBool(PlacementPrefKey, _placement);
                SceneView.RepaintAll();
            });
        }

        /// <summary>
        /// The one control that writes a <see cref="OverrideOp.SwapAsset"/> override: pick another
        /// catalog entry for the selected object and stand that instead.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On the overlay rather than in the window, on the split in section 6 of
        /// <c>ARCHITECTURE.md</c>: this acts on the thing you have just clicked on, and the half of
        /// the screen your eyes are already in is the half it belongs in. It is a row rather than a
        /// panel, so it does not put the same panel on both surfaces.
        /// </para>
        /// <para>
        /// The op has round-tripped since the document model was written and nothing produced one.
        /// What made it worth a control is length variants in a catalog: once a fence folder holds
        /// the same wall at four lengths, "use the two-metre one here" is a thing somebody wants to
        /// say by hand, and this is the override that says it.
        /// </para>
        /// </remarks>
        void BuildSwapRow(VisualElement root)
        {
            _swapField = new DropdownField { style = { flexGrow = 1f } };

            var button = new Button(Swap) { text = "Swap" };

            _swapRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _swapRow.Add(_swapField);
            _swapRow.Add(button);

            root.Q<VisualElement>("swap-slot").Add(_swapRow);
        }

        /// <remarks>
        /// Hidden outright when there is nothing to choose between, which is most of the time: a
        /// selection that is not a realised object, or a catalog with one entry that fits. A
        /// dropdown holding exactly the thing already standing there is a control that does
        /// nothing, and the overlay is short of room.
        /// </remarks>
        void RefreshSwap()
        {
            if (_swapRow == null)
            {
                return;
            }

            List<string> choices = SwapChoices(out string current);
            bool worth = choices.Count > 1;

            _swapRow.style.display = worth ? DisplayStyle.Flex : DisplayStyle.None;
            if (!worth)
            {
                return;
            }

            _swapField.choices = choices;

            // Only when the row starts describing a different object. This runs on every editor
            // update, and writing the value each time put the dropdown back to what the object
            // already is between the user choosing something and reaching the button — so Swap
            // swapped a thing for itself and appeared to do nothing at all.
            ArenaObjectRef reference = SelectedRef();
            string bound = reference != null ? reference.StableId : null;

            if (!string.Equals(bound, _swapBoundTo, StringComparison.Ordinal))
            {
                _swapBoundTo = bound;
                _swapField.SetValueWithoutNotify(current);
            }
        }

        /// <summary>
        /// Entries the selected object could be swapped for: everything in the catalog carrying all
        /// of the tags it carries.
        /// </summary>
        /// <remarks>
        /// All of its tags rather than its most specific one, because the tags are what the
        /// generator selected it by in the first place. A crate tagged <c>cover</c> and
        /// <c>cover/low</c> offers the other low cover and not a fence panel; a stone fence panel
        /// offers the other stone fence panels, which is the length variants. Nothing here decides
        /// what a sensible swap is — the catalog own tagging does, and that is the same answer
        /// <c>Catalog.Query</c> gives the placer.
        /// </remarks>
        List<string> SwapChoices(out string current)
        {
            current = null;
            var choices = new List<string>();

            ArenaObjectRef reference = SelectedRef();
            CatalogAsset asset = _map != null && _map.Realizer != null ? _map.Realizer.Catalog : null;
            if (reference == null || asset == null)
            {
                return choices;
            }

            WorldDoc doc = _map.Document;
            PlacedObject generated = doc != null ? Generated(doc, reference.StableId) : null;
            if (generated == null || generated.Tags.Count == 0)
            {
                return choices;
            }

            current = SwappedTo(doc, reference.StableId) ?? generated.LogicalId;

            var tags = new string[generated.Tags.Count];
            for (int i = 0; i < generated.Tags.Count; i++)
            {
                tags[i] = generated.Tags[i];
            }

            IReadOnlyList<CatalogEntry> matches = asset.ToCatalog().Query(TagQuery.All(tags));
            for (int i = 0; i < matches.Count; i++)
            {
                choices.Add(matches[i].LogicalId);
            }

            // The row it is standing as, even when the catalog no longer offers it: a dropdown that
            // silently showed something else would read as a swap nobody made.
            if (!choices.Contains(current))
            {
                choices.Insert(0, current);
            }

            return choices;
        }

        /// <remarks>
        /// Choosing the entry the generator picked removes the override rather than writing one
        /// that says nothing. An override list that grows an entry per undone decision is a list
        /// whose count stops meaning "edits you have made".
        /// </remarks>
        void Swap()
        {
            ArenaObjectRef reference = SelectedRef();
            if (reference == null || _swapField == null)
            {
                return;
            }

            string chosen = _swapField.value;
            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            Run($"swap {reference.StableId}", _map, map =>
            {
                WorldDoc doc = map.Document;
                ApplySwap(doc, reference.StableId, chosen);
                map.SetDocument(doc);
                return map.Realize();
            });

            _swapBoundTo = null;
        }

        /// <summary>
        /// Puts one swap on a document: upserts the override, or removes it when the choice is what
        /// the generator picked in the first place.
        /// </summary>
        /// <remarks>
        /// Internal and separate from the control that calls it, on the same grounds as the rest of
        /// the override bookkeeping in this assembly: which override an action upserts and which one
        /// it takes away are rules with something to get wrong, and they are worth testing without a
        /// scene, a selection and a dropdown in the way.
        /// </remarks>
        internal static void ApplySwap(WorldDoc doc, string stableId, string chosen)
        {
            PlacedObject generated = Generated(doc, stableId);

            for (int i = doc.Overrides.Count - 1; i >= 0; i--)
            {
                EditOverride edit = doc.Overrides[i];
                if (edit.Op == OverrideOp.SwapAsset &&
                    string.Equals(edit.TargetId, stableId, StringComparison.Ordinal))
                {
                    doc.Overrides.RemoveAt(i);
                }
            }

            if (generated != null &&
                !string.Equals(chosen, generated.LogicalId, StringComparison.Ordinal))
            {
                doc.Overrides.Add(EditOverride.SwapAsset(stableId, chosen));
            }
        }

        ArenaObjectRef SelectedRef()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null || _map == null)
            {
                return null;
            }

            ArenaObjectRef reference = selected.GetComponentInParent<ArenaObjectRef>(true);
            return reference != null && reference.GetComponentInParent<ArenaMap>(true) == _map
                ? reference
                : null;
        }

        static PlacedObject Generated(WorldDoc doc, string stableId)
        {
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (string.Equals(doc.GeneratedObjects[i].StableId, stableId, StringComparison.Ordinal))
                {
                    return doc.GeneratedObjects[i];
                }
            }

            return null;
        }

        static string SwappedTo(WorldDoc doc, string stableId)
        {
            for (int i = 0; i < doc.Overrides.Count; i++)
            {
                EditOverride edit = doc.Overrides[i];
                if (edit.Op == OverrideOp.SwapAsset &&
                    string.Equals(edit.TargetId, stableId, StringComparison.Ordinal))
                {
                    return edit.LogicalId;
                }
            }

            return null;
        }

        void BuildBuildingField(VisualElement root)
        {
            _buildingField = new ObjectField("Building")
            {
                objectType = typeof(ArenaBuilding),
                allowSceneObjects = true,
            };
            _buildingField.RegisterValueChangedCallback(e => Bind(e.newValue as ArenaBuilding));
            root.Q<VisualElement>("building-slot").Add(_buildingField);
        }

        void BuildBuildingSeedRow(VisualElement root)
        {
            _buildingSeedField = new UnsignedLongField("Seed") { style = { flexGrow = 1f } };
            _buildingSeedField.RegisterValueChangedCallback(
                e => MapOperations.Edit(_building, "change building seed", b => b.Seed = e.newValue));

            root.Q<VisualElement>("building-seed-slot").Add(
                SeedRow(_buildingSeedField, RerollBuildingSeed));
        }

        /// <summary>
        /// The four parameters that change what a building is. The rest — grid size, wall margin —
        /// are on the component, because they are set once for a project and never touched again
        /// while you are looking at the thing they shape.
        /// </summary>
        void BuildBuildingParams(VisualElement root)
        {
            VisualElement slot = root.Q<VisualElement>("building-params");

            _footprintField = new Vector2Field("Footprint (m)");
            _footprintField.RegisterValueChangedCallback(e => MapOperations.Edit(
                _building, "change footprint",
                b => b.FootprintSize = new Vector2(
                    Mathf.Max(0.1f, e.newValue.x), Mathf.Max(0.1f, e.newValue.y))));
            slot.Add(_footprintField);

            _floorCountField = new IntegerField("Floors");
            _floorCountField.RegisterValueChangedCallback(e => MapOperations.Edit(
                _building, "change floor count", b => b.FloorCount = Mathf.Max(1, e.newValue)));
            slot.Add(_floorCountField);

            _floorHeightField = new FloatField("Floor height (m)");
            _floorHeightField.RegisterValueChangedCallback(e => MapOperations.Edit(
                _building, "change floor height", b => b.FloorHeight = Mathf.Max(0.1f, e.newValue)));
            slot.Add(_floorHeightField);

            _contentDensityField = new FloatField("Content density");
            _contentDensityField.RegisterValueChangedCallback(e => MapOperations.Edit(
                _building, "change content density", b => b.ContentDensity = Mathf.Max(0f, e.newValue)));
            slot.Add(_contentDensityField);
        }

        void BuildBuildingButtons(VisualElement root)
        {
            root.Q<Button>("building-generate").clicked +=
                () => Run("generate building", _building, b => b.Generate());
            root.Q<Button>("building-regenerate").clicked +=
                () => Run("regenerate building", _building, b => b.Regenerate());
            root.Q<Button>("building-clear").clicked += () => Run("clear building", _building, b =>
            {
                b.Clear();
                return Nothing;
            });

            root.Q<Button>("export").clicked += ExportBuilding;
        }

        static VisualElement SeedRow(VisualElement field, Action reroll)
        {
            var button = new Button(reroll) { text = "↻", tooltip = "Pick a new seed" };
            button.style.width = 26f;

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            row.Add(field);
            row.Add(button);
            return row;
        }

        static ResolvedWorld Nothing =>
            new ResolvedWorld(Array.Empty<PlacedObject>(), Array.Empty<EditOverride>());

        // --- the bound components ---------------------------------------------------------------

        /// <summary>
        /// The map to work on: the one the selection is part of, or the first one in the open
        /// scenes.
        /// </summary>
        /// <remarks>
        /// <c>GetComponentInParent</c> rather than a component lookup on the selection itself, so
        /// clicking the crate you are about to drag keeps the map it belongs to bound.
        /// </remarks>
        static ArenaMap FindMap()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                ArenaMap fromSelection = selected.GetComponentInParent<ArenaMap>(true);
                if (fromSelection != null)
                {
                    return fromSelection;
                }
            }

            return Object.FindFirstObjectByType<ArenaMap>(FindObjectsInactive.Include);
        }

        /// <summary>The building to work on, found the same way.</summary>
        static ArenaBuilding FindBuilding()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                ArenaBuilding fromSelection = selected.GetComponentInParent<ArenaBuilding>(true);
                if (fromSelection != null)
                {
                    return fromSelection;
                }
            }

            return Object.FindFirstObjectByType<ArenaBuilding>(FindObjectsInactive.Include);
        }

        void Bind(ArenaMap map)
        {
            _map = map;
            _boundMap = map != null;

            if (_mapField != null && !ReferenceEquals(_mapField.value, map))
            {
                _mapField.SetValueWithoutNotify(map);
            }

            Status(null);
            Refresh();
            SceneView.RepaintAll();
        }

        void Bind(ArenaBuilding building)
        {
            _building = building;
            _boundBuilding = building != null;

            if (_buildingField != null && !ReferenceEquals(_buildingField.value, building))
            {
                _buildingField.SetValueWithoutNotify(building);
            }

            // Forces the floor rows to be rebuilt for whatever is bound now, rather than left
            // showing the previous building's storeys until its floor count happens to differ.
            _floorRows = -1;

            Status(null);
            Refresh();
            SceneView.RepaintAll();
        }

        void SetMode(Mode mode)
        {
            if (_mode == mode)
            {
                return;
            }

            _mode = mode;
            EditorPrefs.SetBool(ModePrefKey, mode == Mode.Item);
            Status(null);
            ApplyMode();
            Refresh();
            SceneView.RepaintAll();
        }

        void ApplyMode()
        {
            if (_mapSection == null)
            {
                return;
            }

            bool map = _mode == Mode.Map;

            _mapSection.style.display = map ? DisplayStyle.Flex : DisplayStyle.None;
            _itemSection.style.display = map ? DisplayStyle.None : DisplayStyle.Flex;

            _modeMap.EnableInClassList(SelectedModeClass, map);
            _modeItem.EnableInClassList(SelectedModeClass, !map);
        }

        // --- actions --------------------------------------------------------------------------

        void RerollSeed()
        {
            ulong seed = MapOperations.RandomSeed();

            MapOperations.Edit(_map, "randomise seed", m => m.Seed = seed);
            _seedField.SetValueWithoutNotify(seed);
        }

        void RerollBuildingSeed()
        {
            ulong seed = MapOperations.RandomSeed();

            MapOperations.Edit(_building, "randomise building seed", b => b.Seed = seed);
            _buildingSeedField.SetValueWithoutNotify(seed);
        }

        /// <summary>
        /// Re-rolls one floor: a whole-document operation like any other, so it is one named undo
        /// step and it reverts cleanly if the catalog cannot fill the floor.
        /// </summary>
        void RerollFloor(int index)
        {
            ulong seed = MapOperations.RandomSeed();
            Run(
                $"re-roll floor {(index + 1).ToString(CultureInfo.InvariantCulture)}",
                _building,
                b => b.RerollFloor(index, seed));
        }

        void Run<T>(string action, T target, Func<T, ResolvedWorld> operation)
            where T : UnityEngine.Object
        {
            MapOperationResult result = MapOperations.Run(target, action, operation);
            if (!result.Succeeded)
            {
                Status(result.Error);
                return;
            }

            Status(OrphanNote(result.Resolved));
            Refresh();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Writes the document to a file the user picks.
        /// </summary>
        /// <remarks>
        /// The outcome goes to the console rather than to the status line, as every other outcome
        /// on this overlay does. The status line is where a refusal appears, and a line that
        /// sometimes says "saved" and sometimes says "there is no map to save" in the same colour
        /// is a line you stop reading.
        /// </remarks>
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

            if (path == null)
            {
                return;
            }

            Status(null);
            Debug.Log($"ArenaForge: saved '{_map.name}' to {path}.");
        }

        /// <summary>
        /// Reads a document back into the bound map, parameters and all.
        /// </summary>
        /// <remarks>
        /// Through <see cref="Run"/> like every other whole-document operation, so a load is one
        /// named undo step and a file that turns out to be a building leaves the map it was aimed
        /// at exactly as it was.
        /// </remarks>
        void Load()
        {
            if (_map == null)
            {
                Status("There is no map bound to load into.");
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

            Run("load map", _map, map =>
            {
                // The parameters come along with the document. Leaving the component showing a
                // different set would mean the next Regenerate quietly built a different map from
                // the one on screen.
                map.ApplyParams(doc.Parameters);
                map.SetDocument(doc);
                return map.Realize();
            });
        }

        void ExportMap()
        {
            if (_map == null)
            {
                Status("There is no map bound to export.");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Export ArenaForge map", _map.name, "prefab",
                "Bake this map into a prefab with no ArenaForge components left in it.", ExportFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                MapExport.Export(_map, path);
            }
            catch (Exception error) when (error is InvalidOperationException ||
                                          error is ArgumentException)
            {
                Status(error.Message);
                return;
            }

            Status(null);
            Debug.Log(
                $"ArenaForge: exported '{_map.name}' to {path}. It carries nothing of ArenaForge — " +
                "the map in the scene stays the editable source, and re-exporting is how a change " +
                "reaches the prefab.");
        }

        void ExportBuilding()
        {
            if (_building == null)
            {
                Status("There is no building bound to export.");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Export ArenaForge building", _building.name, "prefab",
                "Bake this building into a prefab the arena generator can place.", ExportFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string logicalId = BuildingExport.LogicalIdFor(path);

            try
            {
                BuildingExport.Export(_building, logicalId, path);
            }
            catch (Exception error) when (error is InvalidOperationException ||
                                          error is ArgumentException)
            {
                Status(error.Message);
                return;
            }

            Status(null);
            Debug.Log(
                $"ArenaForge: exported '{_building.name}' to {path} as '{logicalId}'. " +
                "It is now in the catalog, so the arena generator can place it.");
            Refresh();
        }

        // Orphans are the window's panel, with its keep and discard buttons — but an edit quietly
        // losing its target is the one thing this tool refuses to be silent about, so the overlay
        // says it happened and where to go.
        static string OrphanNote(ResolvedWorld resolved)
        {
            int orphans = resolved != null ? resolved.OrphanedOverrides.Count : 0;
            if (orphans == 0)
            {
                return null;
            }

            return $"{orphans} edit(s) no longer have a target. Nothing was discarded — the Arena " +
                   "Forge window has the keep and discard actions.";
        }

        // --- the edit loop --------------------------------------------------------------------

        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRefresh)
            {
                return;
            }

            _nextRefresh = now + RefreshInterval;

            if (_controls == null)
            {
                return;
            }

            if (_mode == Mode.Map)
            {
                TickMap();
                return;
            }

            TickBuilding();
        }

        void TickMap()
        {
            if (_map == null)
            {
                // Also the path a deleted map takes: _boundMap is still set, so the controls are
                // put back into their unbound state rather than staying live over nothing.
                ArenaMap found = FindMap();
                if (found != null || _boundMap)
                {
                    Bind(found);
                }

                return;
            }

            // The seed and the map size are the values the tool window also shows and also writes,
            // so they are re-read rather than assumed. Everything else here is derived from the
            // document.
            if (_seedField.value != _map.Seed)
            {
                _seedField.SetValueWithoutNotify(_map.Seed);
            }

            if (_mapSizeField.value != _map.PlayfieldSize)
            {
                _mapSizeField.SetValueWithoutNotify(_map.PlayfieldSize);
            }

            RefreshLabels();
        }

        void TickBuilding()
        {
            if (_building == null)
            {
                ArenaBuilding found = FindBuilding();
                if (found != null || _boundBuilding)
                {
                    Bind(found);
                }

                return;
            }

            if (_buildingSeedField.value != _building.Seed)
            {
                _buildingSeedField.SetValueWithoutNotify(_building.Seed);
            }

            RefreshFloors();
            RefreshLabels();
        }

        void OnUndoRedo()
        {
            // An undo can put back a document with a different number of storeys in it, so the
            // floor rows are rebuilt rather than only relabelled.
            _floorRows = -1;
            Refresh();
            SceneView.RepaintAll();
        }

        void OnSelectionChanged()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                var map = selected.GetComponentInParent<ArenaMap>(true);
                if (map != null && map != _map)
                {
                    Bind(map);
                    return;
                }

                var building = selected.GetComponentInParent<ArenaBuilding>(true);
                if (building != null && building != _building)
                {
                    Bind(building);
                    return;
                }
            }

            RefreshLabels();
        }

        void OnSceneGui(SceneView view)
        {
            // Both layers are the arena's, so both are map-mode things. A building has no lanes, no
            // spawns and no roads, and drawing the bound map's while you arrange a building would be
            // a picture of something else.
            if (!displayed || _mode != Mode.Map || _map == null ||
                Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (_guides)
            {
                ArenaLayoutGuides.Draw(_map);
            }

            // Drawn after the guides so a carriageway reads as lying on the lane band it crosses.
            // Nothing is computed here: the network is the one the map kept when it was last
            // realised, and a map that has none draws none — see ArenaMap.Roads.
            if (_roads)
            {
                ArenaRoadGuides.Draw(_map);
            }

            // Last of the three, because it is the only one that is about a particular object: a
            // verdict has to read over the bands and the carriageway it is standing on.
            if (_placement)
            {
                ArenaPlacementGuides.Draw(_map);
            }
        }

        // --- panel ----------------------------------------------------------------------------

        void Refresh()
        {
            if (_controls == null)
            {
                return;
            }

            _controls.SetEnabled(_map != null);
            _buildingControls.SetEnabled(_building != null);

            if (_map != null)
            {
                _seedField.SetValueWithoutNotify(_map.Seed);
                _mapSizeField.SetValueWithoutNotify(_map.PlayfieldSize);
            }

            if (_building != null)
            {
                _buildingSeedField.SetValueWithoutNotify(_building.Seed);
                _footprintField.SetValueWithoutNotify(_building.FootprintSize);
                _floorCountField.SetValueWithoutNotify(_building.FloorCount);
                _floorHeightField.SetValueWithoutNotify(_building.FloorHeight);
                _contentDensityField.SetValueWithoutNotify(_building.ContentDensity);
            }

            RefreshFloors();
            RefreshLabels();
        }

        /// <summary>
        /// One re-roll button per storey the document actually has.
        /// </summary>
        /// <remarks>
        /// Driven by the document rather than by the floor count parameter, because between
        /// raising that parameter and pressing Regenerate the two disagree — and a button offering
        /// to re-roll a floor that has not been built yet would run an operation with nothing to
        /// index into.
        /// </remarks>
        void RefreshFloors()
        {
            if (_floors == null)
            {
                return;
            }

            int count = _building != null ? _building.GeneratedFloorCount : 0;
            if (count == _floorRows)
            {
                return;
            }

            _floorRows = count;
            _floors.Clear();

            if (count == 0)
            {
                var empty = new Label("Not generated yet.");
                empty.AddToClassList("af-overlay-line");
                _floors.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("af-overlay-floor");
                row.Add(new Label($"Floor {(i + 1).ToString(CultureInfo.InvariantCulture)}"));

                var reroll = new Button(() => RerollFloor(index))
                {
                    text = "↻",
                    tooltip = "Re-roll this floor. Every other floor stays exactly as it is.",
                };
                reroll.style.width = 26f;
                row.Add(reroll);

                _floors.Add(row);
            }
        }

        void RefreshLabels()
        {
            if (_overrideCount == null)
            {
                return;
            }

            Set(_overrideCount, _map == null ? "No map in the open scenes." : OverrideText());
            Set(_selection, SelectionText());
            Set(_buildingSummary, BuildingText());
            RefreshSwap();
        }

        static void Set(Label label, string text)
        {
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }
        }

        string OverrideText()
        {
            WorldDoc doc = _map.Document;
            if (doc == null)
            {
                return "Not generated yet.";
            }

            int count = doc.Overrides.Count;
            return count == 1 ? "1 active override" : $"{count} active overrides";
        }

        /// <summary>
        /// What the selected realised object is, and whether an edit is holding it in place.
        /// </summary>
        /// <remarks>
        /// The one thing about a map that is otherwise invisible while you are dragging things
        /// around: a GameObject in the hierarchy is named for its stable id, but nothing in the
        /// scene says whether the pose you are looking at is the generator's or your own.
        /// </remarks>
        string SelectionText()
        {
            GameObject selected = Selection.activeGameObject;
            ArenaObjectRef reference = selected != null
                ? selected.GetComponentInParent<ArenaObjectRef>(true)
                : null;

            if (reference == null)
            {
                return "Nothing realised is selected.";
            }

            WorldDoc doc = _map != null ? _map.Document : null;
            if (doc != null)
            {
                for (int i = 0; i < doc.Overrides.Count; i++)
                {
                    EditOverride edit = doc.Overrides[i];
                    if (string.Equals(edit.TargetId, reference.StableId, StringComparison.Ordinal))
                    {
                        return $"{reference.StableId} — {edit.Op} override";
                    }
                }
            }

            return $"{reference.StableId} — no override";
        }

        string BuildingText()
        {
            if (_building == null)
            {
                return "No building in the open scenes.";
            }

            BuildingDoc doc = _building.Document;
            if (doc == null)
            {
                return "Not generated yet.";
            }

            int overrides = doc.Overrides.Count;
            string edits = overrides == 1 ? "1 active override" : $"{overrides} active overrides";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} object(s) over {1} floor(s), {2:0.#} m tall. {3}",
                doc.GeneratedObjects.Count, doc.Floors.Count, doc.Height, edits);
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
    }
}
