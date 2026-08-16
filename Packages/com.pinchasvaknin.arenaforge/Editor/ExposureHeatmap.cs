using ArenaForge.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Renders an <see cref="ExposureMap"/> — as a texture for the tool window, and as coloured
    /// quads in the scene view.
    /// </summary>
    /// <remarks>
    /// The ramp Core documents on <see cref="ExposureMap.ToGrayscale"/> is realised here, because a
    /// colour is an engine concept and Core does not have one. Values are the raw 0..1 exposure
    /// fractions rather than the map's own range stretched to fit, so two maps rendered side by side
    /// are comparable: a sheltered map looks cold instead of looking like an ordinary map with the
    /// contrast turned up.
    /// </remarks>
    static class ExposureHeatmap
    {
        /// <summary>Height above the ground the scene-view overlay is drawn at, in metres.</summary>
        const float OverlayHeight = 0.03f;

        /// <summary>Opacity of the scene-view overlay.</summary>
        const float OverlayAlpha = 0.5f;

        /// <summary>
        /// How finely exposure is banded before neighbouring cells are merged into one quad.
        /// </summary>
        /// <remarks>
        /// A 60×60 map is 3600 cells, and 3600 <c>Handles</c> calls per repaint makes the scene view
        /// crawl. Banding to 32 steps and merging equal runs along X cuts that by roughly five
        /// times, and 32 steps is finer than the eye reads off a ramp anyway.
        /// </remarks>
        const int OverlayBands = 32;

        static readonly Color[] Ramp =
        {
            new Color(0.05f, 0.10f, 0.45f), // dead ground, seen from nowhere
            new Color(0.00f, 0.70f, 0.80f), // sheltered, worth holding
            new Color(0.20f, 0.75f, 0.25f), // ordinary lane floor
            new Color(0.95f, 0.70f, 0.10f), // crossed under observation
            new Color(0.85f, 0.12f, 0.12f), // open ground, seen from everywhere
        };

        static readonly Vector3[] Quad = new Vector3[4];

        /// <summary>
        /// The colour an exposure fraction reads as: deep blue sheltered through to red exposed.
        /// </summary>
        public static Color Sample(float exposure)
        {
            if (float.IsNaN(exposure))
            {
                return Color.clear;
            }

            float scaled = Mathf.Clamp01(exposure) * (Ramp.Length - 1);
            int stop = Mathf.Min((int)scaled, Ramp.Length - 2);
            return Color.Lerp(Ramp[stop], Ramp[stop + 1], scaled - stop);
        }

        /// <summary>
        /// Builds a texture of the exposure, one pixel per grid cell, with the cells a structure
        /// stands on left transparent.
        /// </summary>
        /// <remarks>
        /// Laid out as the map is seen from above: X across, Z up. Point filtering and no mipmaps,
        /// so a cell stays a readable square rather than being smeared into its neighbours.
        /// </remarks>
        public static Texture2D ToTexture(ExposureMap exposure)
        {
            float[,] values = exposure.ToGrayscale();
            int width = values.GetLength(0);
            int height = values.GetLength(1);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ArenaForge Exposure",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color[width * height];
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[z * width + x] = Sample(values[x, z]);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Draws the exposure over the scene's ground as coloured quads, in the space of
        /// <paramref name="root"/>.
        /// </summary>
        /// <remarks>
        /// Depth-tested, so the overlay disappears behind a building instead of painting itself over
        /// the roof.
        /// </remarks>
        public static void DrawInScene(ExposureMap exposure, Transform root)
        {
            WalkableGrid walkable = exposure.Walkable;
            ArenaGrid grid = walkable.Grid;

            Matrix4x4 previousMatrix = Handles.matrix;
            CompareFunction previousZTest = Handles.zTest;
            Handles.matrix = root != null ? root.localToWorldMatrix : Matrix4x4.identity;
            Handles.zTest = CompareFunction.LessEqual;

            for (int z = 0; z < grid.CountZ; z++)
            {
                int runStart = -1;
                int runBand = 0;

                for (int x = 0; x <= grid.CountX; x++)
                {
                    int band = x < grid.CountX ? BandAt(walkable, exposure, x, z) : -1;

                    if (band == runBand && runStart >= 0)
                    {
                        continue;
                    }

                    if (runStart >= 0)
                    {
                        DrawRun(grid, runStart, x - 1, z, runBand);
                    }

                    runStart = band >= 0 ? x : -1;
                    runBand = band;
                }
            }

            Handles.zTest = previousZTest;
            Handles.matrix = previousMatrix;
        }

        static int BandAt(WalkableGrid walkable, ExposureMap exposure, int x, int z)
        {
            int index = walkable.IndexAt(x, z);
            return index < 0
                ? -1
                : Mathf.Clamp((int)(exposure.At(index) * OverlayBands), 0, OverlayBands - 1);
        }

        static void DrawRun(ArenaGrid grid, int fromX, int toX, int z, int band)
        {
            Rect2 from = grid.CellBounds(fromX, z);
            Rect2 to = grid.CellBounds(toX, z);

            Quad[0] = new Vector3(from.MinX, OverlayHeight, from.MinZ);
            Quad[1] = new Vector3(to.MaxX, OverlayHeight, from.MinZ);
            Quad[2] = new Vector3(to.MaxX, OverlayHeight, to.MaxZ);
            Quad[3] = new Vector3(from.MinX, OverlayHeight, to.MaxZ);

            Color color = Sample((band + 0.5f) / OverlayBands);
            color.a = OverlayAlpha;
            Handles.DrawSolidRectangleWithOutline(Quad, color, Color.clear);
        }
    }
}
