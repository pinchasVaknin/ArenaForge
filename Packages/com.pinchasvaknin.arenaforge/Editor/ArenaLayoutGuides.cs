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
        // How far above the ground the three layers are drawn, so they do not z-fight each other
        // or the terrain. The playfield is on top and drawn last because everything else reaches its
        // edges — the lanes run its full length and the spawns its full width — so drawn underneath
        // it is drawn invisibly.
        const float LaneHeight = 0.05f;
        const float SpawnHeight = 0.06f;
        const float PlayfieldHeight = 0.07f;

        /// <summary>Most patches an area is cut into along one axis.</summary>
        /// <remarks>
        /// A ceiling on the work rather than a shape: a guide is redrawn per scene view and per
        /// camera in it, and a four-hundred-metre playfield would otherwise be several thousand
        /// quads a frame. Thirty-two a side is finer than any relief this samples at a feature size
        /// worth drawing.
        /// </remarks>
        const int MostPatches = 32;

        static readonly Color PlayfieldLine = new Color(1f, 0.82f, 0.25f, 0.95f);
        static readonly Color LaneFill = new Color(0.25f, 0.65f, 1f, 0.07f);
        static readonly Color LaneLine = new Color(0.25f, 0.65f, 1f, 0.85f);
        static readonly Color SpawnFill = new Color(0.25f, 0.85f, 0.45f, 0.10f);
        static readonly Color SpawnLine = new Color(0.20f, 0.80f, 0.40f, 0.9f);

        static readonly Vector3[] Quad = new Vector3[4];
        static readonly List<Vector3> Edge = new List<Vector3>();

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

            // The ground the last realise produced, which is the graded one — the roads are cut
            // into it — so a guide lies on the surface the map actually has rather than on the one
            // it would have had without them. Null until a map has been realised in this domain,
            // and then everything below draws flat, which is what it did before this.
            TerrainField ground = map.Ground;
            float step = PatchSize(map);

            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                ArenaLane lane = layout.Lanes[i];
                DrawArea(lane.Band, ground, LaneHeight, step, LaneFill, LaneLine, 2f);
                DrawChip(lane.Band.Center, ground, LaneHeight, lane.Id, LaneLine);
            }

            DrawArea(layout.SpawnAreaA, ground, SpawnHeight, step, SpawnFill, SpawnLine, 2f);
            DrawArea(layout.SpawnAreaB, ground, SpawnHeight, step, SpawnFill, SpawnLine, 2f);
            DrawChip(layout.SpawnAreaA.Center, ground, SpawnHeight, "spawn A", SpawnLine);
            DrawChip(layout.SpawnAreaB.Center, ground, SpawnHeight, "spawn B", SpawnLine);

            // A boundary rather than a fill: the playfield encloses everything above, and tinting
            // the whole map would tint the art it is meant to be read against.
            DrawArea(layout.Playfield, ground, PlayfieldHeight, step, Color.clear, PlayfieldLine, 3f);

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
        //
        // Cut into patches so it lies along the ground instead of cutting into a hill at one end
        // and hanging over a hollow at the other. On a map with no relief there is one patch and
        // the four corners of it are the four corners of the area, which is what this drew before.
        static void DrawArea(
            Rect2 area,
            TerrainField ground,
            float height,
            float step,
            Color fill,
            Color line,
            float width)
        {
            int alongX = Patches(area.MaxX - area.MinX, ground, step);
            int alongZ = Patches(area.MaxZ - area.MinZ, ground, step);

            if (fill.a > 0f)
            {
                Handles.color = Color.white;

                for (int iz = 0; iz < alongZ; iz++)
                {
                    float z0 = Mathf.Lerp(area.MinZ, area.MaxZ, iz / (float)alongZ);
                    float z1 = Mathf.Lerp(area.MinZ, area.MaxZ, (iz + 1) / (float)alongZ);

                    for (int ix = 0; ix < alongX; ix++)
                    {
                        float x0 = Mathf.Lerp(area.MinX, area.MaxX, ix / (float)alongX);
                        float x1 = Mathf.Lerp(area.MinX, area.MaxX, (ix + 1) / (float)alongX);

                        Quad[0] = Corner(ground, x0, z0, height);
                        Quad[1] = Corner(ground, x1, z0, height);
                        Quad[2] = Corner(ground, x1, z1, height);
                        Quad[3] = Corner(ground, x0, z1, height);

                        Handles.DrawSolidRectangleWithOutline(Quad, fill, Color.clear);
                    }
                }
            }

            Perimeter(area, ground, height, alongX, alongZ);

            Handles.color = line;
            Handles.DrawAAPolyLine(width, Edge.ToArray());
        }

        /// <summary>The four edges of an area, walked at the same spacing the patches use.</summary>
        static void Perimeter(Rect2 area, TerrainField ground, float height, int alongX, int alongZ)
        {
            Edge.Clear();

            for (int i = 0; i <= alongX; i++)
            {
                Edge.Add(Corner(ground, Mathf.Lerp(area.MinX, area.MaxX, i / (float)alongX), area.MinZ, height));
            }

            for (int i = 1; i <= alongZ; i++)
            {
                Edge.Add(Corner(ground, area.MaxX, Mathf.Lerp(area.MinZ, area.MaxZ, i / (float)alongZ), height));
            }

            for (int i = alongX - 1; i >= 0; i--)
            {
                Edge.Add(Corner(ground, Mathf.Lerp(area.MinX, area.MaxX, i / (float)alongX), area.MaxZ, height));
            }

            for (int i = alongZ - 1; i >= 0; i--)
            {
                Edge.Add(Corner(ground, area.MinX, Mathf.Lerp(area.MinZ, area.MaxZ, i / (float)alongZ), height));
            }
        }

        /// <summary>How far apart to sample the ground: a quarter of one rise or hollow.</summary>
        /// <remarks>
        /// Derived from <see cref="ArenaMap.TerrainFeatureSize"/> rather than picked, because what a
        /// guide has to follow is the shape of the ground and that parameter is how wide one bump in
        /// it is. Four samples across a bump is enough for a tinted band to read as lying on it.
        /// </remarks>
        static float PatchSize(ArenaMap map) => MathF.Max(1f, map.TerrainFeatureSize * 0.25f);

        static int Patches(float extent, TerrainField ground, float step)
        {
            if (ground == null || !(step > 0f))
            {
                return 1;
            }

            var count = (int)MathF.Ceiling(extent / step);

            return count < 1 ? 1 : count > MostPatches ? MostPatches : count;
        }

        static Vector3 Corner(TerrainField ground, float x, float z, float height) => new Vector3(
            x, ground == null ? height : ground.HeightAt(new Vec2(x, z)) + height, z);

        // Labels sit on the editor's own help-box background, which is what makes them readable
        // over both a pale skybox and a dark one: the chip covers whatever is behind it, and both
        // it and the text follow the editor skin.
        static void DrawChip(Vec2 center, TerrainField ground, float height, string text, Color color)
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
            Handles.Label(Corner(ground, center.X, center.Y, height), text, _chip);
        }
    }
}
