using System;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Draws the skeleton a map is generated onto — playfield, lane bands, spawn areas — into the
    /// scene view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The geometry is <see cref="ArenaLayout.Build"/>'s, taken from the component's current
    /// parameters rather than from the document. That is the point of it: the lanes on screen are
    /// the lanes the <em>next</em> generation will use, so changing the lane count or the playfield
    /// size shows you what you are about to get without generating it first. Nothing is recomputed
    /// here — this is a renderer for a Core result, and there is no arena arithmetic in the editor
    /// assembly.
    /// </para>
    /// <para>
    /// Drawn in the map's own space rather than the realisation root's. They are the same space —
    /// <see cref="WorldRealizer"/> parents its root to this transform at identity — and asking for
    /// the root would create it, which is not something a repaint should do to a map that has never
    /// been generated.
    /// </para>
    /// </remarks>
    static class ArenaLayoutGuides
    {
        // Heights the three layers are drawn at, so they do not z-fight each other. The playfield is
        // on top and drawn last because everything else reaches its edges — the lanes run its full
        // length and the spawns its full width — so drawn underneath it is drawn invisibly.
        const float LaneHeight = 0.05f;
        const float SpawnHeight = 0.06f;
        const float PlayfieldHeight = 0.07f;

        static readonly Color PlayfieldLine = new Color(1f, 0.82f, 0.25f, 0.95f);
        static readonly Color LaneFill = new Color(0.25f, 0.65f, 1f, 0.07f);
        static readonly Color LaneLine = new Color(0.25f, 0.65f, 1f, 0.85f);
        static readonly Color SpawnFill = new Color(0.25f, 0.85f, 0.45f, 0.10f);
        static readonly Color SpawnLine = new Color(0.20f, 0.80f, 0.40f, 0.9f);

        static readonly Vector3[] Quad = new Vector3[4];

        static GUIStyle _chip;

        /// <summary>
        /// Draws the guides for a map's current parameters. Does nothing if those parameters do not
        /// describe a layout — the message for that comes out of Generate, not out of a repaint.
        /// </summary>
        public static void Draw(ArenaMap map)
        {
            if (map == null)
            {
                return;
            }

            ArenaLayout layout;
            try
            {
                layout = ArenaLayout.Build(map.BuildParams());
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is InvalidOperationException)
            {
                return;
            }

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;

            Handles.matrix = map.transform.localToWorldMatrix;
            Handles.zTest = CompareFunction.LessEqual;

            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                ArenaLane lane = layout.Lanes[i];
                DrawArea(lane.Band, LaneHeight, LaneFill, LaneLine, 2f);
                DrawChip(lane.Band.Center, LaneHeight, lane.Id, LaneLine);
            }

            DrawArea(layout.SpawnAreaA, SpawnHeight, SpawnFill, SpawnLine, 2f);
            DrawArea(layout.SpawnAreaB, SpawnHeight, SpawnFill, SpawnLine, 2f);
            DrawChip(layout.SpawnAreaA.Center, SpawnHeight, "spawn A", SpawnLine);
            DrawChip(layout.SpawnAreaB.Center, SpawnHeight, "spawn B", SpawnLine);

            // A boundary rather than a fill: the playfield encloses everything above, and tinting
            // the whole map would tint the art it is meant to be read against.
            DrawArea(layout.Playfield, PlayfieldHeight, Color.clear, PlayfieldLine, 3f);

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }

        // The outline is a separate anti-aliased polyline rather than the one
        // DrawSolidRectangleWithOutline draws, because that one is a single hairline pixel and a
        // lane band read as a smudge next to the scene grid at any distance.
        //
        // Both calls multiply the colour they are given by Handles.color, which is why it is reset
        // to white before the fill: leaving the previous area's colour in place tinted every band
        // after the first.
        static void DrawArea(Rect2 area, float height, Color fill, Color line, float width)
        {
            Corners(area, height);

            Handles.color = Color.white;
            Handles.DrawSolidRectangleWithOutline(Quad, fill, Color.clear);

            Handles.color = line;
            Handles.DrawAAPolyLine(width, Quad[0], Quad[1], Quad[2], Quad[3], Quad[0]);
        }

        static void Corners(Rect2 area, float height)
        {
            Quad[0] = new Vector3(area.MinX, height, area.MinZ);
            Quad[1] = new Vector3(area.MaxX, height, area.MinZ);
            Quad[2] = new Vector3(area.MaxX, height, area.MaxZ);
            Quad[3] = new Vector3(area.MinX, height, area.MaxZ);
        }

        // Labels sit on the editor's own help-box background, which is what makes them readable
        // over both a pale skybox and a dark one: the chip covers whatever is behind it, and both
        // it and the text follow the editor skin.
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
