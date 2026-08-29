using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The ground: an organic heightfield over the playfield, and the flat pads graded into it
    /// under everything that has to stand square.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two properties carry the feature. The field has to be a function — the same point read
    /// twice, or read by a different part of the tool, gives the same height, or the realiser puts
    /// a map on ground the generator did not place it on. And a structure has to stand on ground
    /// that is dead flat under the whole of it, because a building is a stack of level storeys and
    /// there is no sense in which it could follow a slope.
    /// </para>
    /// <para>
    /// The corridors a road grades are the second feature the field carries, and the section at the
    /// end holds them to the same two: the ground is still a function of the document, and it is
    /// still untouched everywhere no road reaches. What the road's own profile has to be —
    /// gradeable, and level with the doors it serves — is asserted where the profile is worked out,
    /// in <see cref="RoadValidationTests"/>.
    /// </para>
    /// </remarks>
    public sealed class TerrainTests
    {
        /// <summary>Seeds swept by the shape properties.</summary>
        const int Seeds = 60;

        /// <summary>A ground with enough relief in it that being wrong about it would show.</summary>
        const float Amplitude = 6f;

        /// <summary>Slack for a height in metres that two readings ought to agree on exactly.</summary>
        const float Millimetre = 1e-3f;

        static ArenaParams Params(ulong seed) => new ArenaParams
        {
            Seed = seed,
            TerrainAmplitude = Amplitude,
        };

        static WorldDoc Generate(ulong seed) =>
            ArenaLayoutGenerator.Generate(Params(seed), TestWorlds.SampleCatalog());

        /// <summary>The same map with a road network laid across it.</summary>
        static ArenaParams RoadParams(ulong seed) => new ArenaParams
        {
            Seed = seed,
            TerrainAmplitude = Amplitude,
            RoadDensity = 1f,
        };

        static WorldDoc GenerateWithRoads(ulong seed) =>
            ArenaLayoutGenerator.Generate(RoadParams(seed), TestWorlds.SampleCatalog());

        // --- the field is a function ------------------------------------------------------------

        [Test]
        public void TheSameParametersGiveTheSameGround()
        {
            TerrainField first = TerrainField.Build(Params(20260816UL));
            TerrainField second = TerrainField.Build(Params(20260816UL));

            foreach (Vec2 point in Probes(first.Bounds))
            {
                Assert.That(second.HeightAt(point), Is.EqualTo(first.HeightAt(point)).Within(0f),
                    $"the ground at {point} is not reproducible");
            }
        }

        [Test]
        public void ADifferentSeedGivesADifferentGround()
        {
            TerrainField first = TerrainField.Build(Params(1UL));
            TerrainField second = TerrainField.Build(Params(2UL));

            var different = 0;
            foreach (Vec2 point in Probes(first.Bounds))
            {
                if (MathF.Abs(first.HeightAt(point) - second.HeightAt(point)) > 1e-3f)
                {
                    different++;
                }
            }

            Assert.That(different, Is.GreaterThan(0), "two seeds produced the same ground");
        }

        /// <remarks>
        /// Zero is the default, and it is what keeps every map generated before there was a
        /// heightfield exactly where it was: a flat field puts everything at y = 0, so a document
        /// generated with the terrain off is the document that generator would have produced.
        /// </remarks>
        [Test]
        public void AZeroAmplitudeIsFlat()
        {
            TerrainField terrain = TerrainField.Build(new ArenaParams { Seed = 7UL });

            foreach (Vec2 point in Probes(terrain.Bounds))
            {
                Assert.That(terrain.HeightAt(point), Is.EqualTo(0f), $"the ground at {point} is not flat");
            }
        }

        [Test]
        public void TheGroundStaysInsideTheAmplitudeItWasAskedFor()
        {
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                TerrainField terrain = TerrainField.Build(Params(seed));

                foreach (Vec2 point in Probes(terrain.Bounds))
                {
                    Assert.That(MathF.Abs(terrain.HeightAt(point)), Is.LessThanOrEqualTo(Amplitude * 0.5f + 1e-4f),
                        $"seed {seed}: the ground reaches past its amplitude at {point}");
                }
            }
        }

        /// <remarks>
        /// What "organic" has to mean to be worth having: neighbouring points are close in height —
        /// so the ground is walkable rather than a field of spikes — and distant ones are not, so
        /// there is something there to take cover behind.
        /// </remarks>
        [Test]
        public void TheGroundRollsRatherThanJumping()
        {
            TerrainField terrain = TerrainField.Build(Params(20260816UL));

            float steepest = 0f;
            float lowest = float.MaxValue;
            float highest = float.MinValue;

            for (float z = -28f; z <= 28f; z += 1f)
            {
                for (float x = -28f; x <= 28f; x += 1f)
                {
                    float here = terrain.HeightAt(new Vec2(x, z));
                    float next = terrain.HeightAt(new Vec2(x + 1f, z));

                    steepest = MathF.Max(steepest, MathF.Abs(next - here));
                    lowest = MathF.Min(lowest, here);
                    highest = MathF.Max(highest, here);
                }
            }

            float spread = highest - lowest;

            Assert.That(steepest, Is.LessThan(1f), "the ground climbs more than a metre in a metre");
            Assert.That(spread, Is.GreaterThan(Amplitude * 0.25f), "the ground is nearly level");
        }

        // --- foundations --------------------------------------------------------------------

        [Test]
        public void AFoundationIsDeadFlatOverTheWholePad()
        {
            TerrainField terrain = TerrainField.Build(Params(3UL));
            var footprint = new Rect2(-6f, -5f, 6f, 5f);

            float height = BuildingGenerator.LevelFoundation(terrain, footprint);
            Rect2 pad = BuildingGenerator.FoundationPad(footprint);

            for (float z = pad.MinZ; z <= pad.MaxZ; z += 0.5f)
            {
                for (float x = pad.MinX; x <= pad.MaxX; x += 0.5f)
                {
                    Assert.That(terrain.HeightAt(new Vec2(x, z)), Is.EqualTo(height).Within(1e-4f),
                        $"the pad slopes at {x}, {z}");
                }
            }
        }

        [Test]
        public void AFoundationLeavesTheGroundBeyondItsApronAlone()
        {
            TerrainField terrain = TerrainField.Build(Params(3UL));
            var footprint = new Rect2(-6f, -5f, 6f, 5f);

            var before = new List<float>();
            var far = new List<Vec2>();
            for (float z = -28f; z <= 28f; z += 2f)
            {
                for (float x = -28f; x <= 28f; x += 2f)
                {
                    var point = new Vec2(x, z);
                    float reach = BuildingGenerator.FoundationApron + 1e-3f;
                    if (Rect2.Distance(BuildingGenerator.FoundationPad(footprint),
                            new Rect2(x, z, x, z)) > reach)
                    {
                        far.Add(point);
                        before.Add(terrain.HeightAt(point));
                    }
                }
            }

            BuildingGenerator.LevelFoundation(terrain, footprint);

            Assert.That(far, Is.Not.Empty);
            for (int i = 0; i < far.Count; i++)
            {
                Assert.That(terrain.HeightAt(far[i]), Is.EqualTo(before[i]).Within(1e-4f),
                    $"the ground moved at {far[i]}, well outside the pad");
            }
        }

        /// <remarks>
        /// The pad is cut and fill rather than a plinth or a pit, so a building neither perches
        /// above the ground around it nor sinks into it: the level it settles at is inside the
        /// range the ground under it had.
        /// </remarks>
        [Test]
        public void AFoundationSettlesBetweenTheHighAndLowGroundUnderIt()
        {
            var footprint = new Rect2(-6f, -5f, 6f, 5f);

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                TerrainField raw = TerrainField.Build(Params(seed));
                Rect2 pad = BuildingGenerator.FoundationPad(footprint);

                float lowest = float.MaxValue;
                float highest = float.MinValue;
                for (float z = pad.MinZ; z <= pad.MaxZ; z += 0.5f)
                {
                    for (float x = pad.MinX; x <= pad.MaxX; x += 0.5f)
                    {
                        float height = raw.HeightAt(new Vec2(x, z));
                        lowest = MathF.Min(lowest, height);
                        highest = MathF.Max(highest, height);
                    }
                }

                float settled = BuildingGenerator.LevelFoundation(
                    TerrainField.Build(Params(seed)), footprint);

                Assert.That(settled, Is.InRange(lowest, highest), $"seed {seed}");
            }
        }

        // --- what a generated map stands on ---------------------------------------------------

        /// <remarks>
        /// The constraint the whole feature is for. A generated building is a stack of level
        /// storeys, so the ground under every part of its footprint has to be one height — if it
        /// is not, the building is on a slant and its walls stop meeting the floor.
        /// </remarks>
        [Test]
        public void NoStructureStandsOnGroundThatSlopesUnderIt()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var slanted = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!PlacedGeometry.IsStructure(placed))
                    {
                        continue;
                    }

                    Rect2 world = PlacedGeometry.WorldFootprint(placed, catalog);
                    float stands = placed.Pose.Position.Y;

                    for (float z = world.MinZ; z <= world.MaxZ; z += 0.5f)
                    {
                        for (float x = world.MinX; x <= world.MaxX; x += 0.5f)
                        {
                            if (MathF.Abs(terrain.HeightAt(new Vec2(x, z)) - stands) > 1e-3f)
                            {
                                slanted.Add($"seed {seed}: {placed.StableId} at {x}, {z}");
                            }
                        }
                    }
                }
            }

            Assert.That(slanted, Is.Empty);
        }

        [Test]
        public void EveryObjectOnAMapStandsOnTheGround()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var floating = new List<string>();

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (PlacedGeometry.IsSocketProp(placed))
                    {
                        // A socket prop stands on its parent, not on the ground.
                        continue;
                    }

                    float ground = terrain.HeightAt(placed.Pose.Position.Xz);
                    if (MathF.Abs(placed.Pose.Position.Y - ground) > 1e-3f)
                    {
                        floating.Add(
                            $"seed {seed}: {placed.StableId} is at {placed.Pose.Position.Y} over " +
                            $"ground at {ground}");
                    }
                }
            }

            Assert.That(floating, Is.Empty);
        }


        // --- fences are planted in the ground rather than stood on it ---------------------------

        /// <remarks>
        /// <para>
        /// The one place in the tool where a pivot goes <em>on</em> the ground rather than a base
        /// offset above it. Everything else is stood on the surface, so its underside lands on the
        /// ground and its pivot sits however far the art hangs below it — which is right for a
        /// crate and puts a fence panel with a metre of footing modelled under it a metre in the
        /// air, dangling its own foundation.
        /// </para>
        /// <para>
        /// Asserted against a catalog whose fences and hedge <em>both</em> reach below their
        /// pivots, because a catalog of zero base offsets cannot tell the two rules apart: on that
        /// art both come out at the height of the ground and the test passes whichever rule is in
        /// force. See <see cref="TestWorlds.FootedFenceCatalog"/>.
        /// </para>
        /// <para>
        /// A yard's fence and not the map's boundary. Both are planted rather than stood, and only
        /// one of them follows the ground: the boundary takes one height for the whole ring — see
        /// <see cref="TheWorldBoundaryStandsLevelOverTheHighestGroundItCrosses"/> — because a
        /// stepped top on the edge of the level is a series of ledges. A fence round somebody's
        /// garden is exactly the thing that ought to step.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryYardFenceSegmentIsPlantedAtTheHeightOfTheGroundUnderIt()
        {
            Catalog catalog = TestWorlds.FootedFenceCatalog();
            var wrong = new List<string>();
            var segments = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!IsYardFence(placed))
                    {
                        continue;
                    }

                    segments++;
                    float ground = terrain.HeightAt(placed.Pose.Position.Xz);
                    if (MathF.Abs(placed.Pose.Position.Y - ground) > 1e-3f)
                    {
                        wrong.Add(
                            $"seed {seed}: {placed.StableId} is at {placed.Pose.Position.Y} on " +
                            $"ground at {ground}");
                    }
                }
            }

            Assert.That(segments, Is.GreaterThan(0), "no fence was ever stood up");
            Assert.That(wrong, Is.Empty);
        }

        /// <remarks>
        /// The other half of the same rule, and what stops it being read as "the base offset is
        /// ignored". A hedge on the same ground out of the same catalog is stood on the surface,
        /// so its pivot comes out its own root above the ground — the piece of dressing beside the
        /// fence proves the fence was treated differently rather than that nothing was.
        /// </remarks>
        [Test]
        public void NothingButAFenceIsPlantedInTheGround()
        {
            Catalog catalog = TestWorlds.FootedFenceCatalog();
            var wrong = new List<string>();
            var hedges = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (placed.LogicalId != TestWorlds.HedgeId)
                    {
                        continue;
                    }

                    hedges++;
                    float stood = terrain.HeightAt(placed.Pose.Position.Xz) + TestWorlds.HedgeRoot;
                    if (MathF.Abs(placed.Pose.Position.Y - stood) > 1e-3f)
                    {
                        wrong.Add(
                            $"seed {seed}: {placed.StableId} is at {placed.Pose.Position.Y} rather " +
                            $"than {stood}");
                    }
                }
            }

            Assert.That(hedges, Is.GreaterThan(0), "no hedge was ever laid");
            Assert.That(wrong, Is.Empty);
        }

        /// <remarks>
        /// <para>
        /// A fence follows the ground by stepping, not by leaning. Every segment is upright — a
        /// quarter turn about Y and nothing else — so the posts stay plumb, the joins stay square
        /// and a foundation modelled straight down covers the wedge each step opens. A run pitched
        /// to the ground's normal instead would satisfy every height assertion above and be visibly
        /// wrong.
        /// </para>
        /// <para>
        /// The step itself is asserted too: over ground this lumpy a yard fence has to change
        /// height somewhere, or the seating is not reading the ground at all.
        /// </para>
        /// </remarks>
        [Test]
        public void AYardFenceStepsDownASlopeWithoutLeaningOver()
        {
            Catalog catalog = TestWorlds.FootedFenceCatalog();
            var leaning = new List<string>();
            var stepped = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                var heights = new List<float>();

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!IsFence(placed))
                    {
                        continue;
                    }

                    if (!IsUpright(placed.Pose.Rotation))
                    {
                        leaning.Add($"seed {seed}: {placed.StableId} is rotated {placed.Pose.Rotation}");
                    }

                    if (IsYardFence(placed))
                    {
                        heights.Add(placed.Pose.Position.Y);
                    }
                }

                for (int i = 1; i < heights.Count; i++)
                {
                    if (MathF.Abs(heights[i] - heights[i - 1]) > 1e-3f)
                    {
                        stepped++;
                    }
                }
            }

            Assert.That(leaning, Is.Empty);
            Assert.That(stepped, Is.GreaterThan(0), "no yard fence ever changed height");
        }

        /// <remarks>
        /// <para>
        /// The edge of the world is one wall rather than a hundred walls of different heights, and
        /// that is the whole of what this asserts: one height for every segment of the ring, and
        /// that height at or above the highest ground anywhere on the path the ring runs. Stepping
        /// it panel by panel keeps the art on the ground and gives a top edge that goes up and down
        /// — every step a ledge, and every ledge somewhere to be got over. Standing it level and
        /// letting the deep foundations bury themselves in the low ground is the other way round,
        /// and it is the one an edge of a level wants.
        /// </para>
        /// <para>
        /// At the highest and not merely at some constant: a ring level at the average height is a
        /// ring the ground rises through somewhere, which is a ramp over the edge of the map.
        /// </para>
        /// </remarks>
        [Test]
        public void TheWorldBoundaryStandsLevelOverTheHighestGroundItCrosses()
        {
            Catalog catalog = TestWorlds.FootedFenceCatalog();
            var uneven = new List<string>();
            var buried = new List<string>();
            var segments = 0;

            for (ulong seed = 1; seed <= 20; seed++)
            {
                WorldDoc doc = ArenaLayoutGenerator.Generate(Params(seed), catalog);
                TerrainField terrain = ArenaLayoutGenerator.Terrain(doc);
                float level = float.NaN;

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (!placed.StableId.StartsWith(PerimeterFence.IdPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    segments++;
                    if (float.IsNaN(level))
                    {
                        level = placed.Pose.Position.Y;
                    }
                    else if (MathF.Abs(placed.Pose.Position.Y - level) > 1e-3f)
                    {
                        uneven.Add(
                            $"seed {seed}: {placed.StableId} is at {placed.Pose.Position.Y} on a " +
                            $"ring at {level}");
                    }

                    float ground = terrain.HeightAt(placed.Pose.Position.Xz);
                    if (ground > placed.Pose.Position.Y + 1e-3f)
                    {
                        buried.Add(
                            $"seed {seed}: {placed.StableId} sits at {placed.Pose.Position.Y} under " +
                            $"ground at {ground}");
                    }
                }
            }

            Assert.That(segments, Is.GreaterThan(0), "no boundary was ever stood up");
            Assert.That(uneven, Is.Empty);
            Assert.That(buried, Is.Empty);
        }

        /// <summary>True if the object is a segment of a fence, round the map or round a yard.</summary>
        static bool IsFence(PlacedObject placed) =>
            placed.StableId.StartsWith(PerimeterFence.IdPrefix, StringComparison.Ordinal) ||
            IsYardFence(placed);

        /// <summary>True if the object is a segment of the fence round a house's yard.</summary>
        /// <remarks>
        /// The boundary is ruled out by name rather than left to the segment to distinguish: the
        /// map's own run is filed under <c>map/boundary/fence_</c>, which contains the yard's id
        /// segment, so a test for the segment alone answers yes for every panel on the map.
        /// </remarks>
        static bool IsYardFence(PlacedObject placed) =>
            placed.StableId.Contains(ExteriorPlacer.FenceSegment) &&
            !placed.StableId.StartsWith(PerimeterFence.IdPrefix, StringComparison.Ordinal);

        /// <summary>
        /// True if the rotation is a turn about Y alone, so the piece stands plumb.
        /// </summary>
        /// <remarks>
        /// Read off the quaternion rather than off the quarter-turn table, because what is being
        /// asserted is that nothing tilted a piece — and a pitch or a roll shows up as an X or a Z
        /// term whether or not the yaw is still one the table would produce.
        /// </remarks>
        static bool IsUpright(Quat rotation) =>
            MathF.Abs(rotation.X) < 1e-6f && MathF.Abs(rotation.Z) < 1e-6f;

        /// <remarks>
        /// The ground is not stored in the document — it is a seed, two numbers and a list of pads
        /// — so the realiser rebuilds it. If the rebuild disagreed with the field the generator
        /// placed against, every object on a saved map would be at the wrong height by whatever
        /// the two fields differ by.
        /// </remarks>
        [Test]
        public void TheGroundRebuiltFromADocumentIsTheGroundItWasGeneratedOn()
        {
            WorldDoc doc = Generate(20260816UL);

            TerrainField first = ArenaLayoutGenerator.Terrain(doc);
            TerrainField second = ArenaLayoutGenerator.Terrain(doc);

            Assert.That(
                first.Foundations.Count,
                Is.EqualTo(2 + PlacedGeometry.Structures(doc).Count),
                "both spawns and every structure grade a pad");

            foreach (Vec2 point in Probes(first.Bounds))
            {
                Assert.That(second.HeightAt(point), Is.EqualTo(first.HeightAt(point)).Within(0f));
            }
        }

        [Test]
        public void ARoundTrippedDocumentStandsOnTheSameGround()
        {
            WorldDoc doc = Generate(4242UL);
            WorldDoc restored = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(doc));

            TerrainField before = ArenaLayoutGenerator.Terrain(doc);
            TerrainField after = ArenaLayoutGenerator.Terrain(restored);

            foreach (Vec2 point in Probes(before.Bounds))
            {
                Assert.That(after.HeightAt(point), Is.EqualTo(before.HeightAt(point)).Within(1e-4f));
            }
        }

        [Test]
        public void ATerrainRefusesParametersItCannotBuild()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TerrainField.Build(new ArenaParams { TerrainAmplitude = -1f }));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TerrainField.Build(new ArenaParams { TerrainFeatureSize = 0f }));
        }

        // --- the ground under a road -------------------------------------------------------------

        /// <remarks>
        /// The same claim <see cref="TheGroundRebuiltFromADocumentIsTheGroundItWasGeneratedOn"/>
        /// makes, of a map that has roads on it — and it is a stronger claim than it looks. A
        /// corridor's height is not in the document either: the network is rebuilt from the
        /// parameters and the placements, its profile is swept again, and the corridors are graded
        /// again. Two rebuilds that disagreed anywhere would put every prop beside a road at the
        /// wrong height on a saved map.
        /// </remarks>
        [Test]
        public void TheGroundUnderARoadIsRebuiltFromADocumentUnchanged()
        {
            WorldDoc doc = GenerateWithRoads(20260816UL);

            TerrainField first = ArenaLayoutGenerator.Terrain(doc);
            TerrainField second = ArenaLayoutGenerator.Terrain(doc);

            Assert.That(first.Corridors.Count, Is.GreaterThan(0), "no road was graded at all");
            Assert.That(second.Corridors.Count, Is.EqualTo(first.Corridors.Count));

            for (int i = 0; i < first.Corridors.Count; i++)
            {
                Corridor a = first.Corridors[i];
                Corridor b = second.Corridors[i];

                Assert.That(b.HalfWidth, Is.EqualTo(a.HalfWidth), $"corridor {i}");
                Assert.That(b.Apron, Is.EqualTo(a.Apron), $"corridor {i}");
                Assert.That(b.Points.Count, Is.EqualTo(a.Points.Count), $"corridor {i}");

                for (int p = 0; p < a.Points.Count; p++)
                {
                    Assert.That(b.Points[p], Is.EqualTo(a.Points[p]), $"corridor {i} point {p}");
                    Assert.That(b.Heights[p], Is.EqualTo(a.Heights[p]).Within(0f),
                        $"corridor {i} height {p}");
                }
            }

            foreach (Vec2 point in Probes(first.Bounds))
            {
                Assert.That(second.HeightAt(point), Is.EqualTo(first.HeightAt(point)).Within(0f),
                    $"the ground at {point} is not reproducible");
            }
        }

        /// <remarks>
        /// <para>
        /// A road changes the ground under itself and its verges and nothing else. Everywhere no
        /// corridor reaches — outside the carriageway and outside the apron either side of it — the
        /// height is the height the map had before a metre of road was laid, to the bit.
        /// </para>
        /// <para>
        /// Which is worth asserting rather than assuming, because the cheap way to write the blend
        /// is a weight that is small rather than zero at the far edge of the apron. That would leave
        /// every road on the map pulling the whole playfield a few millimetres towards it, which no
        /// eye would catch and which would move every prop on the map.
        /// </para>
        /// </remarks>
        [Test]
        public void TheGroundIsUntouchedWhereNoCorridorReaches()
        {
            var moved = new List<string>();
            var tested = 0;

            for (ulong seed = 1; seed <= 12; seed++)
            {
                WorldDoc doc = GenerateWithRoads(seed);
                TerrainField before = ArenaLayoutGenerator.TerrainBeforeRoads(doc);
                TerrainField after = ArenaLayoutGenerator.Terrain(doc);

                foreach (Vec2 point in Lattice(after.Bounds, 60))
                {
                    if (Reached(after, point))
                    {
                        continue;
                    }

                    tested++;
                    if (after.HeightAt(point) != before.HeightAt(point))
                    {
                        moved.Add($"seed {seed}: the ground at {point} moved from " +
                                  $"{before.HeightAt(point)} to {after.HeightAt(point)} with no " +
                                  "corridor within reach of it");
                    }
                }
            }

            Assert.That(moved, Is.Empty);
            Assert.That(tested, Is.GreaterThan(10000), "almost every probe was beside a road");
        }

        /// <remarks>
        /// <para>
        /// The grading actually happened, and it happened in the order it says it did. Under a
        /// carriageway the ground is the profile that carriageway was swept to — unless something
        /// the documented precedence puts above it is there, which is a pad, a junction disc or
        /// another road crossing, and those are exactly the exceptions taken out below.
        /// </para>
        /// <para>
        /// Written the long way round on purpose. A test that only asked whether the ground had
        /// moved would pass on a grading that put the road at any height at all; this one says which
        /// height, and by restating the precedence it fails if the order the features are applied in
        /// ever changes without the rule changing with it.
        /// </para>
        /// </remarks>
        [Test]
        public void TheGroundUnderACarriagewayIsTheProfileItWasGradedTo()
        {
            var wrong = new List<string>();
            var tested = 0;

            for (ulong seed = 1; seed <= 12; seed++)
            {
                WorldDoc doc = GenerateWithRoads(seed);
                TerrainField field = ArenaLayoutGenerator.Terrain(doc);

                for (int i = 0; i < field.Corridors.Count; i++)
                {
                    Corridor corridor = field.Corridors[i];

                    for (int p = 0; p < corridor.Points.Count; p++)
                    {
                        Vec2 at = corridor.Points[p];
                        if (OnAPad(field, at) || UnderALaterCorridor(field, i, at))
                        {
                            continue;
                        }

                        tested++;
                        float height = field.HeightAt(at);
                        if (MathF.Abs(height - corridor.Heights[p]) > Millimetre)
                        {
                            wrong.Add($"seed {seed}: corridor {i} stands at {corridor.Heights[p]} " +
                                      $"at {at} and the ground there is {height}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
            Assert.That(tested, Is.GreaterThan(1000), "almost no carriageway was measured");
        }

        /// <summary>True if any corridor can change the height at this point.</summary>
        static bool Reached(TerrainField field, Vec2 point)
        {
            for (int i = 0; i < field.Corridors.Count; i++)
            {
                if (field.Corridors[i].Reach.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if a pad holds this point, which beats every corridor.</summary>
        static bool OnAPad(TerrainField field, Vec2 point)
        {
            var at = new Rect2(point.X, point.Y, point.X, point.Y);
            for (int i = 0; i < field.Foundations.Count; i++)
            {
                if (Rect2.Distance(field.Foundations[i].Pad, at) <= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if a corridor graded after this one holds this point at its own height.</summary>
        /// <remarks>
        /// A millimetre wider than the carriageway, because the blend either side of one is a
        /// smoothstep and a smoothstep saturates: a point a hair outside the carriageway comes back
        /// with a weight that rounds to exactly one, and the field treats that as being held at the
        /// road's height rather than blended towards it. The two answers differ by nothing anybody
        /// could measure — the weight is one either way — but the edge between them is a float
        /// comparison, and a test that restates a rule has to restate the version the code applies.
        /// </remarks>
        static bool UnderALaterCorridor(TerrainField field, int corridor, Vec2 point)
        {
            for (int i = corridor + 1; i < field.Corridors.Count; i++)
            {
                if (Nearest(field.Corridors[i], point) <= field.Corridors[i].HalfWidth + Millimetre)
                {
                    return true;
                }
            }

            return false;
        }

        static float Nearest(Corridor corridor, Vec2 point)
        {
            IReadOnlyList<Vec2> points = corridor.Points;
            if (points.Count == 1)
            {
                return Vec2.Distance(points[0], point);
            }

            float nearest = float.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                Vec2 from = points[i - 1];
                Vec2 along = points[i] - from;
                float lengthSquared = along.SqrLength;
                float t = lengthSquared > 0f ? Vec2.Dot(point - from, along) / lengthSquared : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                nearest = MathF.Min(nearest, Vec2.Distance(point, from + along * t));
            }

            return nearest;
        }

        static IEnumerable<Vec2> Lattice(Rect2 bounds, int steps)
        {
            for (int z = 0; z <= steps; z++)
            {
                for (int x = 0; x <= steps; x++)
                {
                    yield return new Vec2(
                        bounds.MinX + bounds.Width * x / steps,
                        bounds.MinZ + bounds.Depth * z / steps);
                }
            }
        }

        static IEnumerable<Vec2> Probes(Rect2 bounds)
        {
            for (int z = 0; z <= 12; z++)
            {
                for (int x = 0; x <= 12; x++)
                {
                    yield return new Vec2(
                        bounds.MinX + bounds.Width * x / 12f,
                        bounds.MinZ + bounds.Depth * z / 12f);
                }
            }
        }
    }
}
