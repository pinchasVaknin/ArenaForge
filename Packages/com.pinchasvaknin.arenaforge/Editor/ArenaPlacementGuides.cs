using System;
using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Colours the placement grid under the selected object green or red, and names the rule that
    /// refused it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing the constraint model could always say and nothing ever showed. A candidate is
    /// judged by <see cref="ConstraintSet"/>, which answers with the *first rule* that rejected it
    /// rather than with a boolean — see <c>ARCHITECTURE.md</c> section 5 — so "you cannot put it
    /// there" can always be "you cannot put it there because it overlaps a structure". Drawing that
    /// costs a query and a quad per cell.
    /// </para>
    /// <para>
    /// <strong>Nothing is decided here.</strong> The verdict is
    /// <see cref="CoverPlacer.TryJudge"/>'s, under <see cref="CoverPlacer.Rules"/> — the rules the
    /// generator itself places cover by. A second opinion in the editor assembly would be a second
    /// idea of where a prop may stand, and the one on screen would be the one that drifted.
    /// </para>
    /// <para>
    /// <strong>Only the cells the object stands on.</strong> Not a map-wide legality field: that is
    /// one query per cell over a few thousand cells on every repaint, and the question a person
    /// dragging a crate is asking is about where the crate is, not where it could have gone.
    /// </para>
    /// <para>
    /// Structures are skipped. A building is placed by another stage under other rules, and asked
    /// under these it would be refused for standing near its neighbour — a red box round a building
    /// that is exactly where it belongs.
    /// </para>
    /// </remarks>
    static class ArenaPlacementGuides
    {
        // Clearance over whatever it is drawn on, so the quad does not z-fight the ground it is
        // describing. Above the lane bands and the spawns, which are drawn flat.
        const float CellHeight = 0.065f;

        static readonly Color AllowedFill = new Color(0.25f, 0.85f, 0.45f, 0.18f);
        static readonly Color AllowedLine = new Color(0.25f, 0.90f, 0.45f, 0.85f);
        static readonly Color RefusedFill = new Color(0.95f, 0.30f, 0.28f, 0.22f);
        static readonly Color RefusedLine = new Color(1f, 0.35f, 0.32f, 0.9f);

        static readonly Vector3[] Quad = new Vector3[4];
        static readonly List<PlacedObject> Subjects = new List<PlacedObject>();

        static GUIStyle _chip;

        /// <summary>
        /// Draws the verdict for whichever of the map's realised objects are selected. Draws
        /// nothing when the selection holds none of them, which is most of the time.
        /// </summary>
        public static void Draw(ArenaMap map)
        {
            if (!TryOpen(map, out WorldDoc doc, out ArenaLayout layout, out CatalogAsset asset))
            {
                return;
            }

            Selected(map, doc.Resolve(), Subjects);
            if (Subjects.Count == 0)
            {
                return;
            }

            // After the selection and not before it, because this builds a whole catalog and most
            // repaints have nothing of this map's selected to build it for.
            Catalog catalog = asset.ToCatalog();
            HandleState previous = Begin(map);

            for (int i = 0; i < Subjects.Count; i++)
            {
                DrawVerdict(
                    layout, doc, catalog, Corridors(map), map.Ground, Subjects[i]);
            }

            previous.Restore();
            Subjects.Clear();
        }

        /// <summary>
        /// Draws the verdict for one object that is not in the document — a prefab still under the
        /// cursor.
        /// </summary>
        /// <remarks>
        /// The same query and the same colours as a selected object gets, because it is the same
        /// question: a drag is a placement somebody has not committed to yet, and the point of
        /// answering it early is that the answer is the one they will get. Separate from
        /// <see cref="Draw"/> only because the subject comes from a drag rather than from the
        /// selection — see <see cref="ArenaDragGuides"/>.
        /// </remarks>
        public static void DrawPending(ArenaMap map, PlacedObject subject)
        {
            // Structures are skipped here for the reason they are skipped in the selection: a
            // building is placed by another stage under other rules, and asked under these it would
            // be refused for standing near its neighbour.
            if (subject == null || IsStructure(subject) ||
                !TryOpen(map, out WorldDoc doc, out ArenaLayout layout, out CatalogAsset asset))
            {
                return;
            }

            HandleState previous = Begin(map);
            DrawVerdict(layout, doc, asset.ToCatalog(), Corridors(map), map.Ground, subject);
            previous.Restore();
        }

        /// <summary>
        /// The three things every verdict is measured against, or false where the map cannot say.
        /// </summary>
        /// <remarks>
        /// The catalog asset rather than the catalog it converts to, because converting one builds
        /// every row and each caller knows a different point at which it is worth doing.
        /// </remarks>
        static bool TryOpen(
            ArenaMap map, out WorldDoc doc, out ArenaLayout layout, out CatalogAsset asset)
        {
            doc = null;
            layout = null;
            asset = map != null && map.Realizer != null ? map.Realizer.Catalog : null;

            if (map == null || !map.HasDocument || asset == null)
            {
                return false;
            }

            try
            {
                doc = map.Document;
                layout = ArenaLayout.Build(doc.Parameters);
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is InvalidOperationException ||
                                          error is UnsupportedSchemaVersionException)
            {
                return false;
            }

            return true;
        }

        static IReadOnlyList<Rect2> Corridors(ArenaMap map) =>
            map.Roads != null ? map.Roads.Corridors : null;

        /// <summary>Puts the handles into the map's own space, handing back what was there.</summary>
        /// <remarks>
        /// Returned rather than stashed in a static, because two callers draw through this in one
        /// repaint and a shared slot would have the second one restore the first one's state.
        /// </remarks>
        static HandleState Begin(ArenaMap map)
        {
            var previous = new HandleState(Handles.matrix, Handles.color, Handles.zTest);

            Handles.matrix = map.transform.localToWorldMatrix;
            Handles.zTest = CompareFunction.LessEqual;

            return previous;
        }

        /// <summary>What the handles were set to before a draw, so it can be put back.</summary>
        readonly struct HandleState
        {
            readonly Matrix4x4 _matrix;
            readonly Color _color;
            readonly CompareFunction _zTest;

            public HandleState(Matrix4x4 matrix, Color color, CompareFunction zTest)
            {
                _matrix = matrix;
                _color = color;
                _zTest = zTest;
            }

            public void Restore()
            {
                Handles.zTest = _zTest;
                Handles.color = _color;
                Handles.matrix = _matrix;
            }
        }

        static void DrawVerdict(
            ArenaLayout layout,
            WorldDoc doc,
            Catalog catalog,
            IReadOnlyList<Rect2> corridors,
            TerrainField ground,
            PlacedObject subject)
        {
            if (!CoverPlacer.TryJudge(
                    layout, doc, catalog, corridors, subject,
                    out ConstraintResult result, out Rect2 footprint))
            {
                return;
            }

            Color fill = result.IsOk ? AllowedFill : RefusedFill;
            Color line = result.IsOk ? AllowedLine : RefusedLine;

            ArenaGrid grid = layout.Grid;
            int minX = Index(grid, footprint.MinX, grid.Bounds.MinX);
            int maxX = Index(grid, footprint.MaxX, grid.Bounds.MinX);
            int minZ = Index(grid, footprint.MinZ, grid.Bounds.MinZ);
            int maxZ = Index(grid, footprint.MaxZ, grid.Bounds.MinZ);

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    if (x < 0 || z < 0 || x >= grid.CountX || z >= grid.CountZ)
                    {
                        continue;
                    }

                    DrawCell(grid.CellBounds(x, z), ground, fill, line);
                }
            }

            // The rule, not a colour: "blocked" is what the red already says, and the whole point of
            // keeping the refusal is being able to say which one.
            DrawChip(
                footprint.Center,
                Lift(ground, footprint.Center),
                result.IsOk ? "ok" : result.Failed.ToString(),
                line);
        }

        // Cells are laid from the grid's own low corner, so which cell a coordinate falls in is a
        // floor of the offset rather than a round of it: rounding puts the boundary between two
        // cells half a cell out and paints a column the footprint does not touch.
        static int Index(ArenaGrid grid, float coordinate, float origin) =>
            Mathf.FloorToInt((coordinate - origin) / grid.CellSize);

        /// <summary>
        /// The realised objects in the selection, at the pose they are standing at right now.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The selection is GameObjects; the verdict is about document objects. ArenaObjectRef is
        /// what bridges the two, and it is on the realised instance rather than looked up by name
        /// because a name is not an id and a user may rename anything in a scene.
        /// </para>
        /// <para>
        /// <strong>The pose comes off the transform, not out of the document.</strong> A drag does
        /// not reach the document until the object comes to rest — that is what makes one gesture
        /// one override — so a verdict read from the document is a verdict about where the object
        /// used to be. It stayed behind while the crate moved, which is worse than no verdict at
        /// all.
        /// </para>
        /// <para>
        /// Found in the <em>resolved</em> map rather than the generated list, so an object somebody
        /// added by hand is judged like any other. Under the generated list alone it was invisible:
        /// selecting it drew nothing.
        /// </para>
        /// </remarks>
        static void Selected(ArenaMap map, ResolvedWorld world, List<PlacedObject> into)
        {
            into.Clear();
            GameObject[] selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                return;
            }

            for (int i = 0; i < selection.Length; i++)
            {
                var reference = selection[i].GetComponentInParent<ArenaObjectRef>();
                if (reference == null || string.IsNullOrEmpty(reference.StableId) ||
                    reference.GetComponentInParent<ArenaMap>() != map)
                {
                    continue;
                }

                PlacedObject placed = Find(world, reference.StableId);
                if (placed != null && !IsStructure(placed))
                {
                    into.Add(placed.WithPose(CoreConvert.ToCorePose(reference.transform)));
                }
            }
        }

        static bool IsStructure(PlacedObject placed)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (string.Equals(
                        placed.Tags[i], ArenaLayoutGenerator.StructureTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static PlacedObject Find(ResolvedWorld world, string stableId)
        {
            for (int i = 0; i < world.Objects.Count; i++)
            {
                if (string.Equals(world.Objects[i].StableId, stableId, StringComparison.Ordinal))
                {
                    return world.Objects[i];
                }
            }

            return null;
        }

        // A corner at a time rather than one height for the cell, so a quad on a slope lies along
        // it instead of cutting into the hill at one end and lifting off it at the other.
        static void DrawCell(Rect2 cell, TerrainField ground, Color fill, Color line)
        {
            Quad[0] = Corner(ground, cell.MinX, cell.MinZ);
            Quad[1] = Corner(ground, cell.MaxX, cell.MinZ);
            Quad[2] = Corner(ground, cell.MaxX, cell.MaxZ);
            Quad[3] = Corner(ground, cell.MinX, cell.MaxZ);

            // Reset to white before the fill for the reason ArenaLayoutGuides does: both calls
            // multiply by Handles.color, so the previous cell's line colour would tint this fill.
            Handles.color = Color.white;
            Handles.DrawSolidRectangleWithOutline(Quad, fill, Color.clear);

            Handles.color = line;
            Handles.DrawAAPolyLine(2f, Quad[0], Quad[1], Quad[2], Quad[3], Quad[0]);
        }

        static Vector3 Corner(TerrainField ground, float x, float z) =>
            new Vector3(x, Lift(ground, new Vec2(x, z)), z);

        /// <summary>
        /// How high to draw at a point: just clear of the ground, or of nothing when the map has not
        /// been realised in this domain.
        /// </summary>
        /// <remarks>
        /// The field is the one the last realise produced — <c>ArenaMap.Ground</c> — for the reason
        /// the road network is cached rather than rebuilt: a repaint runs per scene view and per
        /// camera in it, and re-deriving a heightfield to draw on would do it several times a frame.
        /// A map with no cached ground draws flat, which is what it did before this and is right for
        /// a map with no relief in it.
        /// </remarks>
        static float Lift(TerrainField ground, Vec2 at) =>
            ground == null ? CellHeight : ground.HeightAt(at) + CellHeight;

        static void DrawChip(Vec2 center, float height, string text, Color color)
        {
            if (_chip == null)
            {
                _chip = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(5, 5, 1, 1),
                };
            }

            _chip.normal.textColor = color;
            Handles.Label(new Vector3(center.X, height, center.Y), text, _chip);
        }
    }
}
