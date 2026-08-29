using System.Text.RegularExpressions;
using ArenaForge.Core;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The road surface written into a terrain's splat map: that the weights it hands over are
    /// weights, and that a resolution too coarse to draw a road says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one suite in the project that builds a real <see cref="TerrainData"/>, because the two
    /// things worth asserting are both about what Unity is given rather than about what the tool
    /// computed. An alphamap is normalised by the terrain on the way in, so a writer that leaves a
    /// texel summing to anything but one has handed over a blend it did not choose — the muddy-road
    /// look — and the only way to see that is to read the buffer back out of the asset.
    /// </para>
    /// <para>
    /// Two layers and a ground already painted solid with the first of them, which is what a
    /// project's terrain actually looks like when somebody comes to put roads on it. A buffer that
    /// started at nothing would let a writer that never touched the other channels pass.
    /// </para>
    /// </remarks>
    public sealed class TerrainSplatTests
    {
        /// <summary>Seed whose default map lays a network with both classes of road on it.</summary>
        const ulong Seed = 7UL;

        /// <summary>How far a texel's weights may sum from one.</summary>
        /// <remarks>
        /// A ten-thousandth, which is far larger than the sum of two or three floats can drift and
        /// far smaller than any blend anybody could see. Unity stores an alphamap as bytes, so a
        /// weight read back is quantised to a 255th — hence a tolerance rather than an equality.
        /// </remarks>
        const float SumTolerance = 6e-3f;

        Terrain _terrain;
        TerrainData _data;
        TerrainLayer[] _layers;

        [TearDown]
        public void TearDown()
        {
            if (_terrain != null)
            {
                Object.DestroyImmediate(_terrain.gameObject);
            }

            if (_data != null)
            {
                Object.DestroyImmediate(_data);
            }

            if (_layers != null)
            {
                for (int i = 0; i < _layers.Length; i++)
                {
                    Object.DestroyImmediate(_layers[i]);
                }
            }

            _terrain = null;
            _data = null;
            _layers = null;
        }

        // --- the weights are weights ---------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// The property the writer exists to keep. Unity divides an alphamap texel by its own total
        /// when it reads one, so writing the road channel and leaving the ground channel at one
        /// gives a road at fifty per cent with the field showing through it — and it does not look
        /// like a bug, it looks like a muddy road. Every texel of the whole window is checked, both
        /// the ones the roads reached and the ones they did not.
        /// </para>
        /// <para>
        /// That some texels actually came out as road is asserted alongside it, because a writer
        /// that painted nothing at all would satisfy the sum trivially.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryTouchedTexelSumsToOne()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);
            Build(parameters, resolution: 256);

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);

            int resolution = _data.alphamapResolution;
            float[,,] weights = _data.GetAlphamaps(0, 0, resolution, resolution);

            var painted = 0;
            var worst = 0f;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float sum = 0f;
                    for (int layer = 0; layer < weights.GetLength(2); layer++)
                    {
                        sum += weights[z, x, layer];
                    }

                    worst = Mathf.Max(worst, Mathf.Abs(sum - 1f));

                    if (weights[z, x, 1] > 0.5f || weights[z, x, 2] > 0.5f)
                    {
                        painted++;
                    }
                }
            }

            Assert.That(worst, Is.LessThan(SumTolerance),
                "a texel's weights do not sum to one, so the terrain will renormalise them and " +
                "every other layer will bleed through the road");

            Assert.That(painted, Is.GreaterThan(0), "no texel came out as road at all");
        }

        /// <remarks>
        /// The other half of it: a texel the roads reached is road rather than a blend of road and
        /// whatever was under it. The middle of a carriageway is the strongest claim the rasteriser
        /// makes, so it is the one measured — the feathered edge is meant to be a blend and asserting
        /// it were not would be asserting the aliasing back in.
        /// </remarks>
        [Test]
        public void TheMiddleOfACarriagewayIsRoadAndNotABlend()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);
            Build(parameters, resolution: 256);

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);

            Rect2 bounds = field.Bounds;
            int resolution = _data.alphamapResolution;

            RoadSegment artery = roads.Segments[0];
            Assert.That(artery.Class, Is.EqualTo(RoadClass.Artery), "the first segment is a trunk");

            // Halfway along the trunk's first straight run, which is as far from a junction and a
            // bend as anywhere on the network gets.
            Vec2 middle = (artery.Points[0] + artery.Points[1]) * 0.5f;

            int x = Mathf.FloorToInt((middle.X - bounds.MinX) / bounds.Width * resolution);
            int z = Mathf.FloorToInt((middle.Y - bounds.MinZ) / bounds.Depth * resolution);

            float[,,] weights = _data.GetAlphamaps(x, z, 1, 1);

            Assert.That(weights[0, 0, 1], Is.GreaterThan(0.99f),
                $"the middle of {artery.Id} is not painted as artery");

            Assert.That(weights[0, 0, 0], Is.LessThan(0.01f),
                "the ground layer still shows through the middle of a carriageway");
        }

        /// <remarks>
        /// Ground the network never reaches keeps exactly what the project painted there. The writer
        /// reads back the window it is about to change and scales what it finds, so this is what
        /// says it is editing a splat map rather than replacing one.
        /// </remarks>
        [Test]
        public void GroundNoRoadReachesIsLeftAlone()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);
            Build(parameters, resolution: 256);

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);

            Rect2 bounds = field.Bounds;
            int resolution = _data.alphamapResolution;
            float[,,] weights = _data.GetAlphamaps(0, 0, resolution, resolution);

            var untouched = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var at = new Vec2(
                        bounds.MinX + (x + 0.5f) * bounds.Width / resolution,
                        bounds.MinZ + (z + 0.5f) * bounds.Depth / resolution);

                    if (Nearest(roads, at) < 8f)
                    {
                        continue;
                    }

                    untouched++;
                    Assert.That(weights[z, x, 0], Is.GreaterThan(0.99f),
                        $"the ground at {at} is eight metres from any road and was repainted");
                }
            }

            Assert.That(untouched, Is.GreaterThan(0), "every texel of the map is within a road");
        }

        // --- a resolution too coarse to draw a road says so ----------------------------------------

        /// <remarks>
        /// Under four texels across, a carriageway is a row of squares that a diagonal turns into a
        /// dashed line, and no amount of feathering recovers it — the information is not in the
        /// buffer. The warning names both numbers because the fix is arithmetic the reader has to do:
        /// which resolution is enough depends on how wide their narrowest road is and how big their
        /// map is, and neither is on the terrain inspector.
        /// </remarks>
        [Test]
        public void ACoarseAlphamapWarnsAndNamesBothNumbers()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);

            // Sixteen texels over a sixty-metre map is a texel of nearly four metres, against a
            // two-metre path. Well the wrong side of a quarter.
            Build(parameters, resolution: 16);

            float texel = Mathf.Max(field.Bounds.Width, field.Bounds.Depth) / 16;
            float narrowest = TerrainSplatWriter.NarrowestRoad(roads);

            Assert.That(TerrainSplatWriter.IsTooCoarse(roads, texel), Is.True,
                $"a texel of {texel} m against a {narrowest} m road is not being called coarse");

            LogAssert.Expect(LogType.Warning, new Regex(
                Regex.Escape(texel.ToString("0.###")) + @".*" +
                Regex.Escape(narrowest.ToString("0.###"))));

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);
        }

        /// <remarks>
        /// And an alphamap fine enough says nothing, which is the half that keeps the warning worth
        /// reading. <see cref="LogAssert.NoUnexpectedReceived"/> fails on any log the test did not
        /// ask for, so this covers every other message the writer could emit as well.
        /// </remarks>
        [Test]
        public void AFineAlphamapWarnsAboutNothing()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);
            Build(parameters, resolution: 512);

            float texel = Mathf.Max(field.Bounds.Width, field.Bounds.Depth) / 512;
            Assert.That(TerrainSplatWriter.IsTooCoarse(roads, texel), Is.False,
                $"a texel of {texel} m is being called too coarse");

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);

            LogAssert.NoUnexpectedReceived();
        }

        /// <remarks>
        /// The threshold itself, asserted either side of it so that moving
        /// <see cref="TerrainSplatWriter.TexelsPerNarrowestRoad"/> has to be a decision rather than
        /// a side effect. The narrowest road decides it and not the average, because aliasing is a
        /// property of the thinnest thing being drawn.
        /// </remarks>
        [Test]
        public void TheCoarsenessThresholdIsAQuarterOfTheNarrowestRoad()
        {
            Ground(out RoadNetwork roads, out _);

            float narrowest = TerrainSplatWriter.NarrowestRoad(roads);
            float quarter = narrowest / TerrainSplatWriter.TexelsPerNarrowestRoad;

            Assert.That(narrowest, Is.GreaterThan(0f), "this network has no road in it");
            Assert.That(TerrainSplatWriter.IsTooCoarse(roads, quarter * 0.99f), Is.False);
            Assert.That(TerrainSplatWriter.IsTooCoarse(roads, quarter * 1.01f), Is.True);
        }

        /// <remarks>
        /// A terrain with no layers on it has no channel to paint into, and saying so beats writing
        /// nothing and leaving the reader to wonder why their roads are invisible. Same for a layer
        /// index that is not among them, which is what an inspector field left at its default is.
        /// </remarks>
        [Test]
        public void ALayerIndexThatIsNotOnTheTerrainWarns()
        {
            TerrainField field = Ground(out RoadNetwork roads, out ArenaParams parameters);
            Build(parameters, resolution: 512);

            LogAssert.Expect(LogType.Warning, new Regex("terrain layer"));
            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 9);
        }

        /// <remarks>
        /// And a map with no roads on it paints nothing and says nothing, which is the same bargain
        /// every other stage makes with a feature that was never turned on.
        /// </remarks>
        [Test]
        public void AMapWithNoRoadsPaintsNothing()
        {
            var parameters = new ArenaParams { Seed = Seed };
            WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, TestWorlds.SampleCatalog());
            TerrainField field = ArenaLayoutGenerator.Terrain(doc, out RoadNetwork roads);

            Build(parameters, resolution: 64);
            float[,,] before = _data.GetAlphamaps(0, 0, 64, 64);

            TerrainSplatWriter.Apply(roads, field, _terrain, arteryLayer: 1, pathLayer: 2);

            float[,,] after = _data.GetAlphamaps(0, 0, 64, 64);
            for (int z = 0; z < 64; z++)
            {
                for (int x = 0; x < 64; x++)
                {
                    for (int layer = 0; layer < before.GetLength(2); layer++)
                    {
                        Assert.That(after[z, x, layer], Is.EqualTo(before[z, x, layer]),
                            $"the splat map changed at {x},{z} on a map with no roads");
                    }
                }
            }

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The default map at a road density that lays a network of both classes.</summary>
        static TerrainField Ground(out RoadNetwork roads, out ArenaParams parameters)
        {
            parameters = new ArenaParams { Seed = Seed, RoadDensity = 1f };
            WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, TestWorlds.SampleCatalog());
            return ArenaLayoutGenerator.Terrain(doc, out roads);
        }

        /// <summary>
        /// A terrain the size of the playfield with three layers on it, painted solid with the
        /// first.
        /// </summary>
        /// <remarks>
        /// Three rather than two, so that a road class painted into layer one has both a ground
        /// layer under it and another road layer beside it — which is what makes "the other road
        /// channel is scaled down like any other layer" a thing this suite can see rather than a
        /// thing the code says.
        /// </remarks>
        void Build(ArenaParams parameters, int resolution)
        {
            _layers = new[] { new TerrainLayer(), new TerrainLayer(), new TerrainLayer() };

            _data = new TerrainData
            {
                heightmapResolution = TerrainWriter.Resolution,
                size = new Vector3(parameters.PlayfieldSize.X, 1f, parameters.PlayfieldSize.Y),
                terrainLayers = _layers,
                alphamapResolution = resolution,
            };

            var weights = new float[resolution, resolution, _layers.Length];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    weights[z, x, 0] = 1f;
                }
            }

            _data.SetAlphamaps(0, 0, weights);

            var host = new GameObject("ArenaForge Test Terrain");
            _terrain = host.AddComponent<Terrain>();
            _terrain.terrainData = _data;
        }

        /// <summary>How far a point is from the nearest carriageway centreline.</summary>
        static float Nearest(RoadNetwork roads, Vec2 at)
        {
            var nearest = float.MaxValue;

            for (int s = 0; s < roads.Segments.Count; s++)
            {
                RoadSegment segment = roads.Segments[s];
                for (int i = 1; i < segment.Points.Count; i++)
                {
                    Vec2 from = segment.Points[i - 1];
                    Vec2 span = segment.Points[i] - from;
                    float lengthSquared = span.SqrLength;
                    float t = lengthSquared > 0f ? Vec2.Dot(at - from, span) / lengthSquared : 0f;
                    t = Mathf.Clamp01(t);

                    nearest = Mathf.Min(nearest, Vec2.Distance(at, from + span * t));
                }
            }

            return nearest;
        }
    }
}
