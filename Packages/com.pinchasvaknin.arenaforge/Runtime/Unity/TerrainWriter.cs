using ArenaForge.Core;
using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Writes a generated heightfield into a Unity terrain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place the tool puts a shape into the scene rather than a prefab, and it is not an
    /// exception to the rule in ARCHITECTURE.md section 3: no mesh is authored here. A
    /// <see cref="Terrain"/> is a component that renders a grid of numbers, and this hands it the
    /// numbers. The alternative — building a ground mesh — is the thing the rule forbids, and the
    /// other alternative, leaving the field invisible, would put every crate on a map in mid-air
    /// over a flat plane.
    /// </para>
    /// <para>
    /// The terrain's own transform is moved to sit under the playfield, because a Unity terrain
    /// is anchored at its low corner and holds only positive heights: where the ground dips below
    /// zero the object has to come down to meet it. That makes the terrain object derived from the
    /// map in the same way the realised prefabs are, which is the point — nothing about it is
    /// worth hand-placing.
    /// </para>
    /// </remarks>
    public static class TerrainWriter
    {
        /// <summary>
        /// Heightmap resolution written, in samples per side. A power of two plus one, as Unity
        /// requires.
        /// </summary>
        /// <remarks>
        /// 129 over a sixty-metre arena is a sample every half metre, which is finer than the
        /// smallest feature three octaves at any sane feature size can produce. Going up a step
        /// costs four times the hashing for ground that does not change.
        /// </remarks>
        public const int Resolution = 129;

        /// <summary>Shallowest terrain the writer will give a Unity terrain, in metres.</summary>
        /// <remarks>
        /// A terrain of zero height cannot be rendered or collided with, so a flat field is
        /// written as a thin slab held at its own level rather than refused.
        /// </remarks>
        public const float MinimumSpan = 0.5f;

        /// <summary>
        /// Writes <paramref name="field"/> into <paramref name="terrain"/>, sizing and positioning
        /// it to cover the field's bounds.
        /// </summary>
        /// <param name="field">The ground to write.</param>
        /// <param name="terrain">The terrain to write it into. Nothing happens if it is null.</param>
        /// <param name="space">
        /// The transform the field's coordinates are in — a map's own transform. Null puts the
        /// terrain in world space.
        /// </param>
        public static void Apply(TerrainField field, Terrain terrain, Transform space)
        {
            if (field == null || terrain == null)
            {
                return;
            }

            TerrainData data = terrain.terrainData;
            if (data == null)
            {
                Debug.LogWarning(
                    $"ArenaForge: terrain '{terrain.name}' has no terrain data, so the generated " +
                    "ground was not written.", terrain);
                return;
            }

            Rect2 bounds = field.Bounds;
            float[,] heights = Sample(field, bounds, out float lowest, out float highest);
            float span = Mathf.Max(MinimumSpan, highest - lowest);

            // Normalised on the way in, because a terrain stores heights as a fraction of its own
            // size. Doing it here rather than in Core keeps the field in metres, which is what
            // every other reader of it wants.
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    heights[z, x] = (heights[z, x] - lowest) / span;
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RegisterCompleteObjectUndo(data, "ArenaForge: write terrain");
            }
#endif

            data.heightmapResolution = Resolution;
            data.size = new Vector3(bounds.Width, span, bounds.Depth);
            data.SetHeights(0, 0, heights);

            var corner = new Vector3(bounds.MinX, lowest, bounds.MinZ);
            terrain.transform.position = space != null ? space.TransformPoint(corner) : corner;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(data);
            }
#endif
        }

        static float[,] Sample(TerrainField field, Rect2 bounds, out float lowest, out float highest)
        {
            var heights = new float[Resolution, Resolution];
            lowest = float.MaxValue;
            highest = float.MinValue;

            float stepX = bounds.Width / (Resolution - 1);
            float stepZ = bounds.Depth / (Resolution - 1);

            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    // [z, x] rather than [x, z]: Unity indexes a heightmap by row, and a row runs
                    // along the terrain's own Z.
                    float height = field.HeightAt(
                        new Vec2(bounds.MinX + x * stepX, bounds.MinZ + z * stepZ));

                    heights[z, x] = height;
                    lowest = Mathf.Min(lowest, height);
                    highest = Mathf.Max(highest, height);
                }
            }

            return heights;
        }
    }
}
