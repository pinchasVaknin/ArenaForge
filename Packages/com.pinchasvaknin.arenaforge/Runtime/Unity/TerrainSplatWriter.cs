using ArenaForge.Core;
using UnityEngine;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Paints a generated road network into a Unity terrain's splat map, so the carriageways read
    /// as surface rather than as the absence of anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion to <see cref="TerrainWriter"/> and it makes the same bargain: no geometry is
    /// authored here. A <see cref="Terrain"/> already blends a set of layers across the ground by
    /// weight, and this hands it the weights. Building a ribbon of triangles down each polyline is
    /// the thing ARCHITECTURE.md section 3 forbids, and the argument that lets the heightfield
    /// through does not reach it — a terrain is a component whose whole job is to render a grid of
    /// numbers, and a road mesh is a model.
    /// </para>
    /// <para>
    /// <strong>A <c>TerrainData</c> is a binary asset, and painting into one trades away part of
    /// what a map is.</strong> The promise everywhere else in this tool is that a map is a seed, a
    /// handful of parameters and a few kilobytes of readable JSON — diffable, mergeable, and
    /// legible without opening Unity. The surface is not that: it is a few megabytes of alphamap
    /// inside an asset nobody can read. What keeps the bargain honest is that the terrain stays
    /// <em>derived</em>. Nothing here is authoritative over anything: the whole dirty rect is
    /// rebuilt from the document's own network on every write, so a carve made by hand is replaced
    /// rather than compounded, and a lost <c>TerrainData</c> costs a regenerate rather than a map.
    /// The document remains the only thing worth putting in version control.
    /// </para>
    /// <para>
    /// <strong>One buffer, one upload.</strong> The whole network is rasterised into a single
    /// window of weights and written with one
    /// <see cref="TerrainData.SetAlphamaps(int, int, float[,,])"/>. Painting a path at a time is the
    /// obvious shape and it is quadratic in disguise: every call re-uploads the region it was given
    /// and Unity re-derives the terrain's basemap from it, so a network of thirty branches pays for
    /// thirty full passes over ground that only changes once.
    /// </para>
    /// <para>
    /// <strong>Every texel that is touched is renormalised.</strong> Writing the road channel and
    /// leaving the rest is the mistake that looks like it works: Unity normalises an alphamap for
    /// you, so a texel whose grass was already at one and whose road is now also at one comes back
    /// as half of each — a road with the field showing through it at fifty per cent, which is the
    /// muddy-road look and no amount of blending fixes it. So the other layers of a touched texel
    /// are scaled to exactly what the road leaves them, and the weights sum to one before they are
    /// handed over.
    /// </para>
    /// </remarks>
    public static class TerrainSplatWriter
    {
        /// <summary>
        /// How small a texel has to be against the narrowest carriageway before a road stops
        /// aliasing: a quarter of its width.
        /// </summary>
        /// <remarks>
        /// Below four texels across, a path is a row of squares that a diagonal turns into a dashed
        /// line, and no amount of feathering recovers it — the information is not in the buffer.
        /// Four is where an edge has a texel of transition and two of solid road on either side of
        /// the centreline, which is the coarsest a strip can be and still read as a strip. It is a
        /// warning rather than a refusal because the map is still improved by a coarse splat, and
        /// because the fix — raising the alphamap resolution — is a decision about a project's
        /// memory budget rather than one this writer can make.
        /// </remarks>
        public const float TexelsPerNarrowestRoad = 4f;

        /// <summary>
        /// How far the painted edge of a carriageway is feathered, as a multiple of the texel size.
        /// </summary>
        /// <remarks>
        /// One texel, which is the widest feather that carries no information the buffer does not
        /// have. Anything narrower is a hard edge that stairsteps on a diagonal; anything wider is a
        /// road whose edge is blurred by a distance the resolution did not require.
        /// </remarks>
        const float FeatherTexels = 1f;

        /// <summary>
        /// Paints the carriageways of <paramref name="roads"/> into <paramref name="terrain"/>.
        /// </summary>
        /// <remarks>
        /// The field is what says where the terrain is and how big it is, exactly as it does for
        /// <see cref="TerrainWriter.Apply"/> — the two have to agree about the ground or the paint
        /// lands somewhere the heights did not.
        /// </remarks>
        /// <param name="roads">The network to paint. An empty one paints nothing.</param>
        /// <param name="field">The ground the network was laid over, for its bounds.</param>
        /// <param name="terrain">The terrain to paint into. Nothing happens if it is null.</param>
        /// <param name="arteryLayer">Index of the terrain layer a trunk is painted in.</param>
        /// <param name="pathLayer">Index of the terrain layer a branch is painted in.</param>
        /// <summary>
        /// Takes the road surface back off a terrain, leaving every texel wholly its first layer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The counterpart of <see cref="Apply"/>, and what <c>ArenaMap.Clear</c> needs: flattening
        /// the heights puts the ground back and leaves the roads painted on it, so a cleared map
        /// still had a network drawn across a field with nothing on it.
        /// </para>
        /// <para>
        /// <strong>Everything, not just what a network covered.</strong> Clearing is the one moment
        /// there is no network to ask — the document has gone — so the region a road used to occupy
        /// is not knowable. Painting the whole map back to its first layer is the only answer that
        /// cannot leave a stripe behind, and it is what a terrain looks like before this tool
        /// touches it.
        /// </para>
        /// <para>
        /// It writes nothing when the terrain has one layer or none: with a single channel every
        /// texel is already wholly that layer, and a write would dirty an asset to no effect.
        /// </para>
        /// </remarks>
        public static void Clear(Terrain terrain)
        {
            if (terrain == null)
            {
                return;
            }

            TerrainData data = terrain.terrainData;
            if (data == null || data.alphamapLayers <= 1)
            {
                return;
            }

            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            int layers = data.alphamapLayers;
            var weights = new float[height, width, layers];

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    weights[z, x, 0] = 1f;
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RegisterCompleteObjectUndo(data, "ArenaForge: clear roads");
            }
#endif

            data.SetAlphamaps(0, 0, weights);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(data);
            }
#endif
        }

        public static void Apply(
            RoadNetwork roads, TerrainField field, Terrain terrain, int arteryLayer, int pathLayer)
        {
            if (roads == null || field == null || terrain == null || roads.IsEmpty)
            {
                return;
            }

            TerrainData data = terrain.terrainData;
            if (data == null)
            {
                Debug.LogWarning(
                    $"ArenaForge: terrain '{terrain.name}' has no terrain data, so the roads were " +
                    "not painted.", terrain);
                return;
            }

            int layers = data.alphamapLayers;
            if (layers <= 0)
            {
                Debug.LogWarning(
                    $"ArenaForge: terrain '{terrain.name}' has no terrain layers assigned, so " +
                    "there is no channel to paint the roads into. Add layers to the terrain's " +
                    "material and set the road layer indices on the map.", terrain);
                return;
            }

            if (arteryLayer < 0 || arteryLayer >= layers || pathLayer < 0 || pathLayer >= layers)
            {
                Debug.LogWarning(
                    $"ArenaForge: terrain '{terrain.name}' has {layers} terrain layer(s), so the " +
                    $"road layer indices {arteryLayer} and {pathLayer} are not both among them. " +
                    "The roads were not painted.", terrain);
                return;
            }

            Rect2 bounds = field.Bounds;
            int resolution = data.alphamapResolution;
            if (resolution <= 0 || !(bounds.Width > 0f) || !(bounds.Depth > 0f))
            {
                return;
            }

            float texel = Mathf.Max(bounds.Width, bounds.Depth) / resolution;
            WarnIfTooCoarse(terrain, roads, texel, resolution);

            if (!Window(roads, bounds, resolution, texel, out int fromX, out int fromZ,
                    out int countX, out int countZ))
            {
                return;
            }

            // One read, one rasterise, one write. The weights that come back are what the terrain
            // already holds under the roads — grass, dirt, whatever the project painted — and the
            // rasteriser scales them rather than replacing them, so ground the network only clips
            // the edge of keeps most of what it had.
            float[,,] weights = data.GetAlphamaps(fromX, fromZ, countX, countZ);
            if (!Rasterise(
                    roads, bounds, resolution, texel, fromX, fromZ, weights, arteryLayer, pathLayer))
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RegisterCompleteObjectUndo(data, "ArenaForge: paint roads");
            }
#endif

            data.SetAlphamaps(fromX, fromZ, weights);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(data);
            }
#endif
        }

        /// <summary>
        /// The narrowest carriageway anywhere in the network, in metres, or zero if it has none.
        /// </summary>
        /// <remarks>
        /// The narrowest rather than the average, because aliasing is a property of the thinnest
        /// thing being drawn: a map whose arteries are four metres and whose paths are two is
        /// legible at the trunk and dashed at the branch, and the branch is what the reader
        /// notices.
        /// </remarks>
        public static float NarrowestRoad(RoadNetwork roads)
        {
            if (roads == null)
            {
                return 0f;
            }

            float narrowest = float.MaxValue;
            for (int i = 0; i < roads.Segments.Count; i++)
            {
                narrowest = Mathf.Min(narrowest, roads.Segments[i].Width);
            }

            return narrowest == float.MaxValue ? 0f : narrowest;
        }

        /// <summary>
        /// True when an alphamap texel of <paramref name="texel"/> metres is too coarse to draw
        /// the narrowest road in <paramref name="roads"/> as anything but a dashed line.
        /// </summary>
        /// <remarks>
        /// Public because it is the question and not the message: a caller sizing a terrain can ask
        /// it before writing anything, and the suite that holds the warning to its threshold asks
        /// it rather than parsing a string.
        /// </remarks>
        public static bool IsTooCoarse(RoadNetwork roads, float texel)
        {
            float narrowest = NarrowestRoad(roads);
            return narrowest > 0f && texel > narrowest / TexelsPerNarrowestRoad;
        }

        static void WarnIfTooCoarse(Terrain terrain, RoadNetwork roads, float texel, int resolution)
        {
            if (!IsTooCoarse(roads, texel))
            {
                return;
            }

            float narrowest = NarrowestRoad(roads);
            int wanted = Mathf.NextPowerOfTwo(
                Mathf.CeilToInt(resolution * texel / (narrowest / TexelsPerNarrowestRoad)));

            Debug.LogWarning(
                $"ArenaForge: terrain '{terrain.name}' has an alphamap texel of " +
                $"{texel:0.###} m and the narrowest road on this map is {narrowest:0.###} m wide, " +
                $"so the road is under {TexelsPerNarrowestRoad:0} texels across and will paint as " +
                $"a dashed line rather than a strip. Raise the terrain's alphamap resolution from " +
                $"{resolution} to at least {wanted}.", terrain);
        }

        /// <summary>
        /// The block of alphamap the network reaches, in texels, clamped to the map. False when it
        /// reaches none of it.
        /// </summary>
        /// <remarks>
        /// Off <see cref="RoadNetwork.Corridors"/>, which is the ground the carriageways cover and
        /// is already computed. It is the conservative answer — the boxes overlap and a diagonal
        /// one is wider than the road in it — and conservative is the right way round for a window:
        /// too large costs a read and a write of ground that comes back unchanged, too small leaves
        /// paint outside it.
        /// </remarks>
        static bool Window(
            RoadNetwork roads,
            Rect2 bounds,
            int resolution,
            float texel,
            out int fromX,
            out int fromZ,
            out int countX,
            out int countZ)
        {
            fromX = 0;
            fromZ = 0;
            countX = 0;
            countZ = 0;

            var reach = new Rect2(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = 0; i < roads.Corridors.Count; i++)
            {
                Rect2 corridor = roads.Corridors[i];
                reach = new Rect2(
                    Mathf.Min(reach.MinX, corridor.MinX),
                    Mathf.Min(reach.MinZ, corridor.MinZ),
                    Mathf.Max(reach.MaxX, corridor.MaxX),
                    Mathf.Max(reach.MaxZ, corridor.MaxZ));
            }

            if (reach.MinX > reach.MaxX)
            {
                return false;
            }

            // A texel of slack all round, because the edge of a carriageway is feathered over one
            // and a feather that ran off the window would be a hard edge instead.
            float margin = texel * (FeatherTexels + 1f);

            int lowX = TexelIndex(reach.MinX - margin, bounds.MinX, bounds.Width, resolution);
            int highX = TexelIndex(reach.MaxX + margin, bounds.MinX, bounds.Width, resolution);
            int lowZ = TexelIndex(reach.MinZ - margin, bounds.MinZ, bounds.Depth, resolution);
            int highZ = TexelIndex(reach.MaxZ + margin, bounds.MinZ, bounds.Depth, resolution);

            fromX = Mathf.Clamp(lowX, 0, resolution - 1);
            fromZ = Mathf.Clamp(lowZ, 0, resolution - 1);
            countX = Mathf.Clamp(highX, 0, resolution - 1) - fromX + 1;
            countZ = Mathf.Clamp(highZ, 0, resolution - 1) - fromZ + 1;

            return countX > 0 && countZ > 0;
        }

        /// <summary>
        /// Writes the network's coverage into a window of weights and renormalises every texel it
        /// touched. False when it touched none of them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Segment by segment over each one's own texels rather than texel by texel over the whole
        /// window, because a road covers a thin fraction of the box round it: the window of a
        /// sixty-metre map is most of the map, and asking every texel in it for its distance to
        /// every polyline is the same answer arrived at a hundred times more slowly.
        /// </para>
        /// <para>
        /// <strong>The wider road wins a crossing.</strong> A texel that two classes both cover
        /// takes the one with more of it — a branch running into a trunk is painted as trunk where
        /// the two meet, which is what a junction looks like. Only one road channel is written per
        /// texel; the other is one of the layers scaled down to make room, so a crossing does not
        /// come out as half of each.
        /// </para>
        /// </remarks>
        static bool Rasterise(
            RoadNetwork roads,
            Rect2 bounds,
            int resolution,
            float texel,
            int fromX,
            int fromZ,
            float[,,] weights,
            int arteryLayer,
            int pathLayer)
        {
            int countZ = weights.GetLength(0);
            int countX = weights.GetLength(1);
            int layers = weights.GetLength(2);

            var artery = new float[countZ, countX];
            var path = new float[countZ, countX];
            float feather = texel * FeatherTexels;
            bool any = false;

            for (int i = 0; i < roads.Segments.Count; i++)
            {
                RoadSegment segment = roads.Segments[i];
                float[,] coverage = segment.Class == RoadClass.Artery ? artery : path;
                float half = segment.Width * 0.5f;
                float reach = half + feather * 0.5f;

                for (int step = 1; step < segment.Points.Count; step++)
                {
                    any |= Stroke(
                        segment.Points[step - 1], segment.Points[step], half, feather, reach,
                        bounds, resolution, fromX, fromZ, countX, countZ, coverage);
                }
            }

            if (!any)
            {
                return false;
            }

            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    float trunk = artery[z, x];
                    float branch = path[z, x];
                    float road = Mathf.Max(trunk, branch);
                    if (!(road > 0f))
                    {
                        continue;
                    }

                    Blend(weights, z, x, layers, trunk >= branch ? arteryLayer : pathLayer, road);
                }
            }

            return true;
        }

        /// <summary>
        /// Lays one straight run of centreline into a coverage buffer, and returns whether it
        /// reached the window at all.
        /// </summary>
        static bool Stroke(
            Vec2 from,
            Vec2 to,
            float half,
            float feather,
            float reach,
            Rect2 bounds,
            int resolution,
            int fromX,
            int fromZ,
            int countX,
            int countZ,
            float[,] coverage)
        {
            int lowX = TexelIndex(
                Mathf.Min(from.X, to.X) - reach, bounds.MinX, bounds.Width, resolution) - fromX;
            int highX = TexelIndex(
                Mathf.Max(from.X, to.X) + reach, bounds.MinX, bounds.Width, resolution) - fromX;
            int lowZ = TexelIndex(
                Mathf.Min(from.Y, to.Y) - reach, bounds.MinZ, bounds.Depth, resolution) - fromZ;
            int highZ = TexelIndex(
                Mathf.Max(from.Y, to.Y) + reach, bounds.MinZ, bounds.Depth, resolution) - fromZ;

            lowX = Mathf.Max(lowX, 0);
            lowZ = Mathf.Max(lowZ, 0);
            highX = Mathf.Min(highX, countX - 1);
            highZ = Mathf.Min(highZ, countZ - 1);

            if (lowX > highX || lowZ > highZ)
            {
                return false;
            }

            Vec2 span = to - from;
            float lengthSquared = span.SqrLength;

            for (int z = lowZ; z <= highZ; z++)
            {
                float worldZ = TexelCentre(fromZ + z, bounds.MinZ, bounds.Depth, resolution);

                for (int x = lowX; x <= highX; x++)
                {
                    float worldX = TexelCentre(fromX + x, bounds.MinX, bounds.Width, resolution);

                    var at = new Vec2(worldX, worldZ);
                    float t = lengthSquared > 0f
                        ? Mathf.Clamp01(Vec2.Dot(at - from, span) / lengthSquared)
                        : 0f;

                    float distance = Vec2.Distance(at, from + span * t);

                    // One at the centre of the carriageway, nought a feather outside its edge, and
                    // a half exactly on the edge. Nothing here subtracts coverage, so two strokes
                    // over the same texel leave the deeper of the two.
                    float weight = Mathf.Clamp01((half - distance) / feather + 0.5f);
                    if (weight > coverage[z, x])
                    {
                        coverage[z, x] = weight;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Puts <paramref name="road"/> of a texel into one layer and scales the rest into what is
        /// left, so the weights sum to one.
        /// </summary>
        /// <remarks>
        /// The scaling is what a terrain would otherwise do for itself, and doing it here is the
        /// whole point: Unity divides every channel by the total, so writing a road of one over a
        /// grass of one gives half a road. Scaling the others by what the road leaves them gives a
        /// road of one over nothing, and a road of a quarter over three quarters of whatever the
        /// ground was — which is the blend the feather asked for rather than a blend of everything
        /// with everything.
        ///
        /// A texel whose other layers are already at nothing has nothing to scale, so the road takes
        /// all of it. That is the only case where the painted weight is not the coverage, and the
        /// alternative is a texel that sums to less than one, which a terrain would renormalise back
        /// to exactly this.
        /// </remarks>
        static void Blend(float[,,] weights, int z, int x, int layers, int layer, float road)
        {
            float others = 0f;
            for (int i = 0; i < layers; i++)
            {
                if (i != layer)
                {
                    others += weights[z, x, i];
                }
            }

            if (others > 0f)
            {
                float scale = (1f - road) / others;
                for (int i = 0; i < layers; i++)
                {
                    if (i != layer)
                    {
                        weights[z, x, i] *= scale;
                    }
                }
            }
            else
            {
                road = 1f;
            }

            weights[z, x, layer] = road;
        }

        /// <summary>Which texel a world coordinate falls in, unclamped.</summary>
        static int TexelIndex(float at, float origin, float size, int resolution) =>
            Mathf.FloorToInt((at - origin) / size * resolution);

        /// <summary>Where the centre of a texel sits in world coordinates.</summary>
        static float TexelCentre(int index, float origin, float size, int resolution) =>
            origin + (index + 0.5f) * size / resolution;
    }
}
