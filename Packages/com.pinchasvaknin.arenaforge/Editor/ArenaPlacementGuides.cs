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
        // Above the lane bands and the spawns so a verdict reads over them, below the playfield
        // outline, which is the one line that should stay on top.
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
            if (map == null || !map.HasDocument)
            {
                return;
            }

            WorldDoc doc;
            ArenaLayout layout;
            try
            {
                doc = map.Document;
                layout = ArenaLayout.Build(doc.Parameters);
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is InvalidOperationException ||
                                          error is UnsupportedSchemaVersionException)
            {
                return;
            }

            CatalogAsset asset = map.Realizer != null ? map.Realizer.Catalog : null;
            if (asset == null)
            {
                return;
            }

            Selected(map, doc, Subjects);
            if (Subjects.Count == 0)
            {
                return;
            }

            Catalog catalog = asset.ToCatalog();
            IReadOnlyList<Rect2> corridors = map.Roads != null ? map.Roads.Corridors : null;

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;

            Handles.matrix = map.transform.localToWorldMatrix;
            Handles.zTest = CompareFunction.LessEqual;

            for (int i = 0; i < Subjects.Count; i++)
            {
                DrawVerdict(layout, doc, catalog, corridors, Subjects[i]);
            }

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;

            Subjects.Clear();
        }

        static void DrawVerdict(
            ArenaLayout layout,
            WorldDoc doc,
            Catalog catalog,
            IReadOnlyList<Rect2> corridors,
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

                    DrawCell(grid.CellBounds(x, z), fill, line);
                }
            }

            // The rule, not a colour: "blocked" is what the red already says, and the whole point of
            // keeping the refusal is being able to say which one.
            DrawChip(
                footprint.Center,
                result.IsOk ? "ok" : result.Failed.ToString(),
                line);
        }

        // Cells are laid from the grid's own low corner, so which cell a coordinate falls in is a
        // floor of the offset rather than a round of it: rounding puts the boundary between two
        // cells half a cell out and paints a column the footprint does not touch.
        static int Index(ArenaGrid grid, float coordinate, float origin) =>
            Mathf.FloorToInt((coordinate - origin) / grid.CellSize);

        // The selection is GameObjects; the verdict is about document objects. ArenaObjectRef is
        // what bridges the two, and it is on the realised instance rather than looked up by name
        // because a name is not an id and a user may rename anything in a scene.
        static void Selected(ArenaMap map, WorldDoc doc, List<PlacedObject> into)
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

                PlacedObject placed = Find(doc, reference.StableId);
                if (placed != null && !IsStructure(placed))
                {
                    into.Add(placed);
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

        static PlacedObject Find(WorldDoc doc, string stableId)
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

        static void DrawCell(Rect2 cell, Color fill, Color line)
        {
            Quad[0] = new Vector3(cell.MinX, CellHeight, cell.MinZ);
            Quad[1] = new Vector3(cell.MaxX, CellHeight, cell.MinZ);
            Quad[2] = new Vector3(cell.MaxX, CellHeight, cell.MaxZ);
            Quad[3] = new Vector3(cell.MinX, CellHeight, cell.MaxZ);

            // Reset to white before the fill for the reason ArenaLayoutGuides does: both calls
            // multiply by Handles.color, so the previous cell's line colour would tint this fill.
            Handles.color = Color.white;
            Handles.DrawSolidRectangleWithOutline(Quad, fill, Color.clear);

            Handles.color = line;
            Handles.DrawAAPolyLine(2f, Quad[0], Quad[1], Quad[2], Quad[3], Quad[0]);
        }

        static void DrawChip(Vec2 center, string text, Color color)
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
            Handles.Label(new Vector3(center.X, CellHeight, center.Y), text, _chip);
        }
    }
}
