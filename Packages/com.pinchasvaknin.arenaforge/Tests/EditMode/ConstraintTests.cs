using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The eleven placement rules, one at a time, plus the pieces the placer is built out of: the
    /// open-cell grid, the sampler and the yaw table.
    /// </summary>
    /// <remarks>
    /// The suites in <see cref="CoverPlacementTests"/> check that a generated map obeys the rules.
    /// These check the rules themselves, which is what makes a failure there readable: a rule that
    /// silently accepts everything would leave every property test passing.
    /// </remarks>
    public sealed class ConstraintTests
    {
        static readonly CatalogEntry Crate = new CatalogEntry(
            "cover/low/crate_wood_01",
            new[] { "cover", "cover/low" },
            new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
            1f,
            1f,
            null);

        static readonly CatalogEntry Building = new CatalogEntry(
            "structure/building/two_storey_01",
            new[] { "structure", "structure/building" },
            new Rect2(-6f, -5f, 6f, 5f),
            6f,
            1f,
            null);

        static ArenaLayout Layout() => ArenaLayout.Build(new ArenaParams { Seed = 20260816UL });

        static ConstraintSet SetOf(ArenaLayout layout, params PlacementConstraint[] constraints) =>
            new ConstraintSet(layout, constraints);

        static Placement Crate_At(float x, float z) =>
            Placement.AtQuarterTurn(Crate, new Vec2(x, z), 0);

        [Test]
        public void InsidePlayfieldRejectsAFootprintOverTheEdge()
        {
            ArenaLayout layout = Layout();
            ConstraintSet set = SetOf(layout, PlacementConstraint.InsidePlayfield());

            Assert.That(set.Evaluate(Crate_At(0f, 0f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(layout.Playfield.MaxX, 0f)).IsOk, Is.False,
                "half of this crate hangs over the edge");
            Assert.That(set.Evaluate(Crate_At(layout.Playfield.MaxX - 0.5f, 0f)).IsOk, Is.True,
                "flush with the edge is inside it");
        }

        [Test]
        public void WithinLaneRejectsAFootprintThatCrossesIntoTheGap()
        {
            ArenaLayout layout = Layout();
            ArenaLane lane = layout.Lanes[0];
            ConstraintSet set = SetOf(layout, PlacementConstraint.WithinLane(lane.Id));

            Vec2 centre = lane.Band.Center;

            Assert.That(set.Evaluate(Crate_At(centre.X, centre.Y)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(lane.Band.MaxX, centre.Y)).IsOk, Is.False);
        }

        [Test]
        public void WithinLaneNamingAnUnknownLaneIsAProgrammingError()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.WithinLane("lane_atlantis"));

            var error = Assert.Throws<InvalidOperationException>(() => set.Evaluate(Crate_At(0f, 0f)));

            Assert.That(error.Message, Does.Contain("lane_atlantis"));
        }

        [Test]
        public void OnGridRejectsAPoseBetweenTheGridLines()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.OnGrid(1f));

            Assert.That(set.Evaluate(Crate_At(3f, -4f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(3.5f, -4f)).IsOk, Is.False);
            Assert.That(set.Evaluate(Crate_At(3f, -4.25f)).IsOk, Is.False);
        }

        [Test]
        public void OnGridMeasuresTheCellSizeItWasGivenNotTheLayoutGrid()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.OnGrid(2f));

            Assert.That(set.Evaluate(Crate_At(4f, -6f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(3f, -6f)).IsOk, Is.False, "on the metre grid, off the two-metre one");
        }

        [Test]
        public void NoOverlapKeepsTheMarginBetweenFootprints()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.NoOverlap(0.75f));
            set.Commit(Crate_At(0f, 0f));

            Assert.That(set.Evaluate(Crate_At(1f, 0f)).IsOk, Is.False, "flush, inside the margin");
            Assert.That(set.Evaluate(Crate_At(2f, 0f)).IsOk, Is.True, "a metre of floor between them");
        }

        [Test]
        public void MinDistanceFromMeasuresFootprintEdgesNotCentres()
        {
            ConstraintSet set = SetOf(
                Layout(), PlacementConstraint.MinDistanceFrom("structure", 1.5f));
            set.Commit(Placement.AtQuarterTurn(Building, Vec2.Zero, 0));

            // The building reaches to x = 6, so its clearance ring ends at 7.5 and the crate's own
            // half metre pushes the first legal pivot out to 8.
            Assert.That(set.Evaluate(Crate_At(7f, 0f)).IsOk, Is.False);
            Assert.That(set.Evaluate(Crate_At(8f, 0f)).IsOk, Is.True);
        }

        [Test]
        public void MinDistanceFromIgnoresObjectsWithoutTheTag()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.MinDistanceFrom("structure", 10f));
            set.Commit(Crate_At(0f, 0f));

            Assert.That(set.Evaluate(Crate_At(1f, 0f)).IsOk, Is.True, "a crate is not a structure");
        }

        [Test]
        public void MaxDistanceFromRequiresSomethingTaggedNearby()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.MaxDistanceFrom("structure", 3f));
            set.Commit(Placement.AtQuarterTurn(Building, Vec2.Zero, 0));

            Assert.That(set.Evaluate(Crate_At(8f, 0f)).IsOk, Is.True, "one and a half metres from the wall");
            Assert.That(set.Evaluate(Crate_At(12f, 0f)).IsOk, Is.False, "too far from anything tagged");
        }

        [Test]
        public void MaxDistanceFromWithNothingToMeasureAgainstAcceptsEverything()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.MaxDistanceFrom("structure", 1f));

            Assert.That(set.Evaluate(Crate_At(20f, 20f)).IsOk, Is.True);
        }

        [Test]
        public void NotBlockingDoorwayKeepsTheClearanceInFrontOfIt()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.NotBlockingDoorway(1.5f));
            set.AddDoorway(new Rect2(-1.25f, 4.5f, 1.25f, 5.5f));

            Assert.That(set.Evaluate(Crate_At(0f, 7f)).IsOk, Is.False);
            Assert.That(set.Evaluate(Crate_At(0f, 8f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(4f, 5f)).IsOk, Is.True, "beside the door, not in front of it");
        }

        [Test]
        public void ClearOfSpawnKeepsPropsOutOfTheSpawnAndItsApron()
        {
            ArenaLayout layout = Layout();
            ConstraintSet set = SetOf(layout, PlacementConstraint.ClearOfSpawn(3f));

            float justOutside = layout.SpawnAreaA.MaxZ + 3f + 0.5f;

            Assert.That(set.Evaluate(Crate_At(0f, layout.SpawnAreaA.Center.Y)).IsOk, Is.False);
            Assert.That(set.Evaluate(Crate_At(0f, layout.SpawnAreaA.MaxZ + 1f)).IsOk, Is.False);
            Assert.That(set.Evaluate(Crate_At(0f, justOutside)).IsOk, Is.True);
        }

        // --- the four rules a room's contents are placed under -------------------------------------

        /// <remarks>
        /// Measured against the region rather than against a committed wall, because a building's
        /// walls are art rather than placed objects: the region a room's decor is proposed into is
        /// bounded by exactly the walls that enclose it.
        /// </remarks>
        [Test]
        public void AgainstWallRejectsAFootprintOutInTheMiddleOfTheFloor()
        {
            var room = new Rect2(-5f, -5f, 5f, 5f);
            var set = new ConstraintSet(room, new[] { PlacementConstraint.AgainstWall(0.25f) });

            Assert.That(set.Evaluate(Crate_At(-4.5f, 0f)).IsOk, Is.True, "flush with the west wall");
            Assert.That(set.Evaluate(Crate_At(0f, 4.5f)).IsOk, Is.True, "flush with the north wall");
            Assert.That(set.Evaluate(Crate_At(-4.25f, 0f)).IsOk, Is.True, "within the reach of it");
            Assert.That(set.Evaluate(Crate_At(0f, 0f)).IsOk, Is.False, "the middle of the room");
            Assert.That(set.Evaluate(Crate_At(-3f, 0f)).IsOk, Is.False, "two metres out from the wall");
        }

        /// <remarks>
        /// Strictly stronger than <see cref="ConstraintKind.AgainstWall"/>, and the two axes are
        /// kept apart on purpose: a footprint touching both the low and the high X edge — which a
        /// cupboard in a room barely wider than it is produces — is against a wall, not in a
        /// corner.
        /// </remarks>
        [Test]
        public void InCornerWantsTwoWallsAtOnceRatherThanOneTwice()
        {
            var room = new Rect2(-5f, -5f, 5f, 5f);
            var set = new ConstraintSet(room, new[] { PlacementConstraint.InCorner(0.25f) });

            Assert.That(set.Evaluate(Crate_At(-4.5f, -4.5f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(4.5f, -4.5f)).IsOk, Is.True);
            Assert.That(set.Evaluate(Crate_At(-4.5f, 0f)).IsOk, Is.False, "against one wall only");

            var slot = new ConstraintSet(
                new Rect2(-0.5f, -5f, 0.5f, 5f), new[] { PlacementConstraint.InCorner(0.25f) });

            Assert.That(slot.Evaluate(Crate_At(0f, 0f)).IsOk, Is.False,
                "touching both sides of a narrow room is not a corner");
        }

        /// <remarks>
        /// <para>
        /// The exact negation of <see cref="ConstraintKind.AgainstWall"/>, over the same predicate
        /// and the same distance. Reaching one wall is enough to fail it, which is what makes the
        /// middle of a room the floor that is neither against a wall nor in a corner rather than a
        /// third idea of where the middle is.
        /// </para>
        /// <para>
        /// The narrow room is the case the two axes being kept apart pays for a second time. A
        /// crate touching both sides of a slot is against a wall by the first rule and cannot be in
        /// the middle by this one, however exactly it is centred between them — a metre-wide
        /// corridor has no middle to stand a table in.
        /// </para>
        /// </remarks>
        [Test]
        public void InCentreWantsNoWallWithinReachAtAll()
        {
            var room = new Rect2(-5f, -5f, 5f, 5f);
            var set = new ConstraintSet(room, new[] { PlacementConstraint.InCentre(0.25f) });

            Assert.That(set.Evaluate(Crate_At(0f, 0f)).IsOk, Is.True, "the middle of the room");
            Assert.That(set.Evaluate(Crate_At(-3f, 0f)).IsOk, Is.True, "two metres out from the wall");
            Assert.That(set.Evaluate(Crate_At(-4.25f, 0f)).IsOk, Is.False, "within reach of one wall");
            Assert.That(set.Evaluate(Crate_At(-4.5f, 0f)).IsOk, Is.False, "flush with the west wall");
            Assert.That(set.Evaluate(Crate_At(-4.5f, -4.5f)).IsOk, Is.False, "in a corner");

            var slot = new ConstraintSet(
                new Rect2(-0.5f, -5f, 0.5f, 5f), new[] { PlacementConstraint.InCentre(0.25f) });

            Assert.That(slot.Evaluate(Crate_At(0f, 0f)).IsOk, Is.False,
                "a room no wider than the crate has no middle to stand it in");
        }

        [Test]
        public void NearDoorwayIsTheOtherDirectionOfNotBlockingOne()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.NearDoorway(2f));
            set.AddDoorway(new Rect2(-1.25f, 4.5f, 1.25f, 5.5f));

            Assert.That(set.Evaluate(Crate_At(0f, 7f)).IsOk, Is.True, "a metre from the opening");
            Assert.That(set.Evaluate(Crate_At(0f, 12f)).IsOk, Is.False, "the far side of the room");
        }

        [Test]
        public void NearDoorwayWithNoDoorwayToMeasureAgainstAcceptsEverything()
        {
            ConstraintSet set = SetOf(Layout(), PlacementConstraint.NearDoorway(1f));

            Assert.That(set.Evaluate(Crate_At(20f, 20f)).IsOk, Is.True);
        }

        [Test]
        public void TheFirstFailingConstraintIsTheOneReported()
        {
            ArenaLayout layout = Layout();
            ConstraintSet set = SetOf(
                layout,
                PlacementConstraint.OnGrid(1f),
                PlacementConstraint.ClearOfSpawn(3f));

            // Off the grid and in a spawn: both rules would reject it, and the answer is the first.
            ConstraintResult result = set.Evaluate(Crate_At(0.5f, layout.SpawnAreaA.Center.Y));

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Failed.Kind, Is.EqualTo(ConstraintKind.OnGrid));
            Assert.That(result.ToString(), Is.EqualTo("OnGrid(1)"));
        }

        [Test]
        public void AnEmptyConstraintSetAcceptsAnything()
        {
            ConstraintSet set = SetOf(Layout());

            ConstraintResult result = set.Evaluate(Crate_At(1000f, -1000f));

            Assert.That(result.IsOk, Is.True);
            Assert.That(result.ToString(), Is.EqualTo("ok"));
        }

        [Test]
        public void ConstraintsReadAsTheCallThatMadeThem()
        {
            Assert.That(PlacementConstraint.InsidePlayfield().ToString(), Is.EqualTo("InsidePlayfield"));
            Assert.That(PlacementConstraint.WithinLane("lane_mid").ToString(), Is.EqualTo("WithinLane(lane_mid)"));
            Assert.That(PlacementConstraint.NoOverlap(0.75f).ToString(), Is.EqualTo("NoOverlap(0.75)"));
            Assert.That(PlacementConstraint.MinDistanceFrom("structure", 1.5f).ToString(),
                Is.EqualTo("MinDistanceFrom(structure, 1.5)"));
            Assert.That(PlacementConstraint.AgainstWall(0.25f).ToString(), Is.EqualTo("AgainstWall(0.25)"));
            Assert.That(PlacementConstraint.InCorner(0.25f).ToString(), Is.EqualTo("InCorner(0.25)"));
            Assert.That(PlacementConstraint.InCentre(1.2f).ToString(), Is.EqualTo("InCentre(1.2)"));
            Assert.That(PlacementConstraint.NearDoorway(2f).ToString(), Is.EqualTo("NearDoorway(2)"));
        }

        [Test]
        public void CommittedPlacementsAreKeptInCommitOrder()
        {
            ConstraintSet set = SetOf(Layout());
            set.Commit(Crate_At(0f, 0f));
            set.Commit(Crate_At(4f, 0f));

            Assert.That(set.Committed.Count, Is.EqualTo(2));
            Assert.That(set.Committed[1].Pose.Position.X, Is.EqualTo(4f));
        }

        [Test]
        public void NullArgumentsToAConstraintSetAreRejected()
        {
            Assert.Throws<ArgumentNullException>(
                () => new ConstraintSet(null, new PlacementConstraint[0]));
            Assert.Throws<ArgumentNullException>(() => new ConstraintSet(Layout(), null));
        }

        [Test]
        public void StatisticsTallyEveryOutcomeAndWriteThemselvesOut()
        {
            var stats = new PlacementStats();
            stats.Record(ConstraintResult.Ok);
            stats.Record(ConstraintResult.RejectedBy(PlacementConstraint.NoOverlap(0.75f)));
            stats.Record(ConstraintResult.RejectedBy(PlacementConstraint.NoOverlap(0.75f)));
            stats.Record(ConstraintResult.RejectedBy(PlacementConstraint.ClearOfSpawn(3f)));

            Assert.That(stats.Attempted, Is.EqualTo(4));
            Assert.That(stats.Accepted, Is.EqualTo(1));
            Assert.That(stats.Rejected, Is.EqualTo(3));
            Assert.That(stats.RejectedBy(ConstraintKind.NoOverlap), Is.EqualTo(2));
            Assert.That(stats.RejectedBy(ConstraintKind.OnGrid), Is.EqualTo(0));

            var metadata = new Dictionary<string, string>();
            stats.WriteTo(metadata, "cover_");

            Assert.That(metadata["cover_attempted"], Is.EqualTo("4"));
            Assert.That(metadata["cover_accepted"], Is.EqualTo("1"));
            Assert.That(metadata["cover_rejected"], Is.EqualTo("3"));
            Assert.That(metadata["cover_rejected_no_overlap"], Is.EqualTo("2"));
            Assert.That(metadata["cover_rejected_clear_of_spawn"], Is.EqualTo("1"));
            Assert.That(metadata.ContainsKey("cover_rejected_on_grid"), Is.False,
                "a rule that rejected nothing should not clutter the document");
        }

        [Test]
        public void EveryConstraintKindHasAMetadataName()
        {
            var names = new List<string>();
            foreach (ConstraintKind kind in Enum.GetValues(typeof(ConstraintKind)))
            {
                string name = PlacementStats.MetadataName(kind);
                Assert.That(name, Is.Not.Null.And.Not.Empty);
                Assert.That(names, Does.Not.Contain(name));
                names.Add(name);
            }
        }

        [Test]
        public void ClaimingAnAreaTakesTheCellsItCoversAndNoMore()
        {
            var bounds = new Rect2(0f, 0f, 10f, 10f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));

            Assert.That(grid.FreeCount, Is.EqualTo(100));
            Assert.That(grid.FreeArea, Is.EqualTo(100f));

            grid.Claim(new Rect2(2f, 2f, 5f, 4f));

            Assert.That(grid.FreeCount, Is.EqualTo(100 - 6), "three cells by two");
            Assert.That(grid.IsFree(2, 2), Is.False);
            Assert.That(grid.IsFree(4, 3), Is.False);
            Assert.That(grid.IsFree(5, 3), Is.True, "flush with the claim's edge, not inside it");
            Assert.That(grid.IsFree(1, 2), Is.True);
        }

        [Test]
        public void ClaimingTheSameCellTwiceCountsItOnce()
        {
            var grid = new PlacementGrid(new ArenaGrid(new Rect2(0f, 0f, 10f, 10f), 1f));

            grid.Claim(new Rect2(1f, 1f, 4f, 4f));
            grid.Claim(new Rect2(2f, 2f, 5f, 5f));

            Assert.That(grid.FreeCount, Is.EqualTo(100 - (9 + 9 - 4)));
        }

        [Test]
        public void ClaimsOutsideTheGridChangeNothing()
        {
            var grid = new PlacementGrid(new ArenaGrid(new Rect2(0f, 0f, 10f, 10f), 1f));

            grid.Claim(new Rect2(-30f, -30f, -20f, -20f));
            grid.Claim(new Rect2(40f, 40f, 50f, 50f));

            Assert.That(grid.FreeCount, Is.EqualTo(100));
        }

        [Test]
        public void OpenCentresComeBackInAFixedOrder()
        {
            var grid = new PlacementGrid(new ArenaGrid(new Rect2(0f, 0f, 4f, 4f), 1f));
            grid.Claim(new Rect2(0f, 0f, 2f, 1f));

            var first = new List<Vec2>();
            var second = new List<Vec2>();
            grid.CollectOpenCentres(new Rect2(0f, 0f, 4f, 4f), first);
            grid.CollectOpenCentres(new Rect2(0f, 0f, 4f, 4f), second);

            Assert.That(first.Count, Is.EqualTo(14));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first[0], Is.EqualTo(new Vec2(2.5f, 0.5f)), "the first open cell of the bottom row");
        }

        [Test]
        public void SamplesAreNeverCloserThanTheRadius()
        {
            var bounds = new Rect2(-20f, -20f, 20f, 20f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));

            for (ulong seed = 1; seed <= 25; seed++)
            {
                var rng = new Rng(seed);
                List<Vec2> samples = PoissonDisk.Sample(grid, bounds, 3f, 1000, ref rng);

                Assert.That(samples.Count, Is.GreaterThan(20), $"seed {seed} barely sampled anything");
                for (int i = 0; i < samples.Count; i++)
                {
                    for (int j = i + 1; j < samples.Count; j++)
                    {
                        Assert.That(Vec2.Distance(samples[i], samples[j]), Is.GreaterThanOrEqualTo(3f),
                            $"seed {seed}: two samples fell inside the radius");
                    }
                }
            }
        }

        [Test]
        public void SamplesStayOutOfClaimedCells()
        {
            var bounds = new Rect2(-20f, -20f, 20f, 20f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));
            var blocked = new Rect2(-6f, -6f, 6f, 6f);
            grid.Claim(blocked);

            var rng = new Rng(7UL);
            List<Vec2> samples = PoissonDisk.Sample(grid, bounds, 2.5f, 1000, ref rng);

            foreach (Vec2 sample in samples)
            {
                Assert.That(blocked.Contains(sample), Is.False, $"{sample} landed in a claimed area");
            }
        }

        [Test]
        public void TheSameSeedSamplesTheSamePointsInTheSameOrder()
        {
            var bounds = new Rect2(-15f, -15f, 15f, 15f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));

            var first = new Rng(99UL);
            var second = new Rng(99UL);

            Assert.That(
                PoissonDisk.Sample(grid, bounds, 2f, 500, ref first),
                Is.EqualTo(PoissonDisk.Sample(grid, bounds, 2f, 500, ref second)));
        }

        /// <summary>
        /// The reason the sampler reseeds rather than stopping when its frontier dies. A lane cut
        /// in two by a building has to fill on both sides of it.
        /// </summary>
        [Test]
        public void ADomainSplitInTwoIsFilledOnBothSides()
        {
            var bounds = new Rect2(-20f, -5f, 20f, 5f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));
            grid.Claim(new Rect2(-3f, -5f, 3f, 5f));

            var rng = new Rng(5UL);
            List<Vec2> samples = PoissonDisk.Sample(grid, bounds, 2.5f, 500, ref rng);

            int left = 0;
            int right = 0;
            foreach (Vec2 sample in samples)
            {
                if (sample.X < -3f)
                {
                    left++;
                }
                else if (sample.X > 3f)
                {
                    right++;
                }
            }

            Assert.That(left, Is.GreaterThan(4), "the half the first sample did not land in was skipped");
            Assert.That(right, Is.GreaterThan(4));
        }

        [Test]
        public void ARadiusThatIsNotPositiveIsRejected()
        {
            var bounds = new Rect2(0f, 0f, 10f, 10f);
            var grid = new PlacementGrid(new ArenaGrid(bounds, 1f));
            var rng = new Rng(1UL);

            Assert.Throws<ArgumentOutOfRangeException>(() => PoissonDisk.Sample(grid, bounds, 0f, 10, ref rng));
        }

        [Test]
        public void TheYawTableAgreesWithTheQuarterTurnTable()
        {
            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                int steps = turns * (YawStep.Count / QuarterTurn.Count);

                Assert.That(YawStep.Rotation(steps), Is.EqualTo(QuarterTurn.Rotation(turns)),
                    $"{steps} steps should be {turns} quarter turns");
                Assert.That(YawStep.Bounds(new Rect2(-2f, -0.5f, 2f, 0.5f), steps),
                    Is.EqualTo(QuarterTurn.Rotate(new Rect2(-2f, -0.5f, 2f, 0.5f), turns)));
            }
        }

        [Test]
        public void EveryYawStepIsAUnitRotationAboutTheUpAxis()
        {
            for (int steps = 0; steps < YawStep.Count; steps++)
            {
                Quat rotation = YawStep.Rotation(steps);

                Assert.That(rotation.X, Is.EqualTo(0f), $"step {steps} is not a yaw");
                Assert.That(rotation.Z, Is.EqualTo(0f), $"step {steps} is not a yaw");
                Assert.That(rotation.Length, Is.EqualTo(1f).Within(1e-6f), $"step {steps} is not a unit quaternion");
                Assert.That(rotation.W, Is.GreaterThanOrEqualTo(0f), $"step {steps} is not the shortest arc");
            }
        }

        [Test]
        public void AnOffAxisStepBoundsAFootprintWithTheBoxAroundIt()
        {
            var footprint = new Rect2(-2f, -0.5f, 2f, 0.5f);

            Rect2 bounds = YawStep.Bounds(footprint, 3);

            Assert.That(bounds.Width, Is.GreaterThan(footprint.Width * 0.5f).And.LessThan(footprint.Width));
            Assert.That(bounds.Depth, Is.GreaterThan(footprint.Depth));
            Assert.That(bounds.Width, Is.EqualTo(bounds.Depth).Within(1e-5f), "at forty-five degrees it is square");
        }

        [Test]
        public void StepsWrapAroundTheTableInBothDirections()
        {
            Assert.That(YawStep.Normalize(YawStep.Count + 5), Is.EqualTo(5));
            Assert.That(YawStep.Normalize(-1), Is.EqualTo(YawStep.Count - 1));
            Assert.That(YawStep.Rotation(-YawStep.Count), Is.EqualTo(YawStep.Rotation(0)));
        }

        [Test]
        public void TheDistanceBetweenRectanglesIsMeasuredEdgeToEdge()
        {
            var a = new Rect2(0f, 0f, 2f, 2f);

            Assert.That(Rect2.Distance(a, new Rect2(5f, 0f, 6f, 2f)), Is.EqualTo(3f));
            Assert.That(Rect2.Distance(a, new Rect2(1f, 1f, 3f, 3f)), Is.EqualTo(0f), "overlapping");
            Assert.That(Rect2.Distance(a, new Rect2(2f, 2f, 3f, 3f)), Is.EqualTo(0f), "corner to corner");
            Assert.That(Rect2.Distance(a, new Rect2(5f, 6f, 6f, 7f)), Is.EqualTo(5f), "three across, four up");
        }

        [Test]
        public void APlacementCarriesTheFootprintItsPoseGivesIt()
        {
            Placement upright = Placement.AtQuarterTurn(Building, new Vec2(3f, -4f), 0);
            Placement turned = Placement.AtQuarterTurn(Building, new Vec2(3f, -4f), 1);

            Assert.That(upright.Footprint, Is.EqualTo(new Rect2(-3f, -9f, 9f, 1f)));
            Assert.That(turned.Footprint, Is.EqualTo(new Rect2(-2f, -10f, 8f, 2f)), "width and depth swap");
            Assert.That(turned.LogicalId, Is.EqualTo(Building.LogicalId));
            Assert.That(turned.HasTag("structure"), Is.True);
            Assert.That(turned.HasTag("cover"), Is.False);
        }

        [Test]
        public void APlacementWithoutALogicalIdIsRejected()
        {
            Assert.Throws<ArgumentException>(
                () => new Placement(" ", Pose.Identity, Rect2.Zero, null));
            Assert.Throws<ArgumentNullException>(
                () => Placement.AtQuarterTurn(null, Vec2.Zero, 0));
        }

        // --- judging an object where it already stands --------------------------------------------

        /// <remarks>
        /// <para>
        /// The property the scene-view verdict rests on, and the reason
        /// <see cref="CoverPlacer.Rules"/> exists as one list rather than two: every piece of cover
        /// the generator put down was accepted by those rules when it went down, so asking the same
        /// rules about it afterwards has to accept it again. A drift between the list the placer
        /// uses and the list the editor asks under shows up here and nowhere else — every other
        /// suite would stay green.
        /// </para>
        /// <para>
        /// Sweeping seeds rather than checking one map, because what could go wrong is a rule that
        /// only bites on a particular arrangement: a prop by a doorway, a prop near a spawn apron,
        /// a prop against the one structure that overhangs its band.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryPieceOfCoverIsAcceptedWhereTheGeneratorPutIt()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var refused = new List<string>();

            for (ulong seed = 1; seed <= 40; seed++)
            {
                var parameters = new ArenaParams { Seed = seed };
                WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
                ArenaLayout layout = ArenaLayout.Build(parameters);

                foreach (PlacedObject placed in doc.GeneratedObjects)
                {
                    // Ground cover only. A prop on a socket sits inside its parent's footprint by
                    // design and is placed by a pass that never asks a rule, so there is no ground
                    // verdict to be had about one — CoverPlacer.IsSocketProp is what says so.
                    if (!placed.StableId.Contains("/cover_") ||
                        CoverPlacer.IsSocketProp(placed.StableId))
                    {
                        continue;
                    }

                    Assert.That(
                        CoverPlacer.TryJudge(
                            layout, doc, catalog, null, placed,
                            out ConstraintResult result, out Rect2 _),
                        Is.True,
                        $"seed {seed}: {placed.StableId} has no catalog row");

                    if (!result.IsOk)
                    {
                        refused.Add($"seed {seed}: {placed.StableId} refused by {result.Failed}");
                    }
                }
            }

            Assert.That(refused, Is.Empty,
                "cover the generator placed under these rules is not accepted by them");
        }

        /// <remarks>
        /// The other half: a verdict that accepted everything would pass the property above without
        /// saying anything. Each case names the rule it expects, because "refused" is not the useful
        /// answer — which rule refused is.
        /// </remarks>
        [Test]
        public void APropMovedSomewhereItMayNotStandIsRefusedByTheRuleThatSaysSo()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var parameters = new ArenaParams { Seed = 20260816UL };
            WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
            ArenaLayout layout = ArenaLayout.Build(parameters);

            PlacedObject cover = null;
            PlacedObject structure = null;
            foreach (PlacedObject placed in doc.GeneratedObjects)
            {
                if (cover == null && placed.StableId.Contains("/cover_"))
                {
                    cover = placed;
                }

                if (structure == null && placed.StableId.Contains("/structure_"))
                {
                    structure = placed;
                }
            }

            Assert.That(cover, Is.Not.Null, "the map has no cover to move");
            Assert.That(structure, Is.Not.Null, "the map has no structure to move it onto");

            // Onto the middle of a structure: the nearest rule that bites is the clearance a prop
            // has to keep from one, which fires before the overlap does. Snapped first, because a
            // structure is not placed on the prop grid and OnGrid is evaluated before either of
            // them — the verdict would be right and about the wrong thing.
            Vec2 centre = layout.Grid.Snap(
                new Vec2(structure.Pose.Position.X, structure.Pose.Position.Z));
            PlacedObject onStructure = cover.WithPose(new Pose(
                new Vec3(centre.X, cover.Pose.Position.Y, centre.Y),
                cover.Pose.Rotation,
                cover.Pose.Scale));

            Assert.That(
                CoverPlacer.TryJudge(layout, doc, catalog, null, onStructure, out ConstraintResult onIt, out Rect2 _),
                Is.True);
            Assert.That(onIt.IsOk, Is.False, "a crate in the middle of a building was accepted");
            Assert.That(onIt.Failed.Kind, Is.EqualTo(ConstraintKind.MinDistanceFrom));

            // Off the edge of the world.
            PlacedObject outside = cover.WithPose(new Pose(
                new Vec3(layout.Playfield.MaxX + 20f, cover.Pose.Position.Y, cover.Pose.Position.Z),
                cover.Pose.Rotation,
                cover.Pose.Scale));

            Assert.That(
                CoverPlacer.TryJudge(layout, doc, catalog, null, outside, out ConstraintResult out_, out Rect2 _),
                Is.True);
            Assert.That(out_.IsOk, Is.False, "a crate outside the playfield was accepted");
            Assert.That(out_.Failed.Kind, Is.EqualTo(ConstraintKind.InsidePlayfield));

            // Half a cell off the grid.
            PlacedObject offGrid = cover.WithPose(new Pose(
                new Vec3(
                    cover.Pose.Position.X + layout.Grid.CellSize * 0.5f,
                    cover.Pose.Position.Y,
                    cover.Pose.Position.Z),
                cover.Pose.Rotation,
                cover.Pose.Scale));

            Assert.That(
                CoverPlacer.TryJudge(layout, doc, catalog, null, offGrid, out ConstraintResult off, out Rect2 _),
                Is.True);
            Assert.That(off.IsOk, Is.False, "a crate half a cell off the grid was accepted");
            Assert.That(off.Failed.Kind, Is.EqualTo(ConstraintKind.OnGrid));
        }

        /// <remarks>
        /// A row that has gone leaves nothing to measure, and a verdict of "fine" would be a lie
        /// drawn in green. The caller is told there is nothing to say instead.
        /// </remarks>
        [Test]
        public void AnObjectTheCatalogCannotResolveGetsNoVerdict()
        {
            Catalog catalog = TestWorlds.SampleCatalog();
            var parameters = new ArenaParams { Seed = 20260816UL };
            WorldDoc doc = ArenaLayoutGenerator.Generate(parameters, catalog);
            ArenaLayout layout = ArenaLayout.Build(parameters);

            var orphan = new PlacedObject(
                "map/lane_mid/cover_99", "cover/low/deleted_01", Pose.Identity, null, null);

            Assert.That(
                CoverPlacer.TryJudge(
                    layout, doc, catalog, null, orphan, out ConstraintResult _, out Rect2 _),
                Is.False);
        }
    }
}
