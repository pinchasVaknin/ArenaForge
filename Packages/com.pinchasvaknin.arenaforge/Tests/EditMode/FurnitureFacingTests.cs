using System;
using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Which way round the furniture in a room ends up: its back to the wall and its front to the
    /// floor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The properties here are about a rotation, which is the one thing about a placement that no
    /// constraint looks at. Every rule in <see cref="ConstraintSet"/> is a statement about a
    /// footprint, and a footprint is the same rectangle whether the sofa in it is facing the room
    /// or facing the plaster — so a suite that only measured positions would go on passing through
    /// the whole of this.
    /// </para>
    /// <para>
    /// A quarter turn is compared rather than an angle. The generator places on the exact
    /// quarter-turn table for the reason <see cref="QuarterTurn"/> gives, so the question "which
    /// way is this facing" has four answers and comparing them is exact.
    /// </para>
    /// </remarks>
    public sealed class FurnitureFacingTests
    {
        /// <summary>Seeds swept by the properties that have to hold of every room.</summary>
        const int Seeds = 60;

        const string DecorSegment = "/decor_";
        const string CoverSegment = "/cover_";

        static readonly Rect2 Room = new Rect2(-5f, -4f, 5f, 4f);

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        // --- the rule itself -----------------------------------------------------------------------

        /// <remarks>
        /// The four walls and the turn each one calls for, written out rather than derived, because
        /// the mapping is the whole of what the type says and a test that computed it the same way
        /// the code does would assert nothing. A piece against the low Z wall faces positive Z,
        /// which is a quarter turn of zero, and each wall round from there is one more.
        /// </remarks>
        [Test]
        public void APieceAgainstAWallIsTurnedToFaceAwayFromIt()
        {
            Vec2 centre = Room.Center;

            Assert.That(WallFacing.Against(Room, new Vec2(centre.X, Room.MinZ + 0.4f), 0.5f),
                Is.EqualTo(0), "low Z");
            Assert.That(WallFacing.Against(Room, new Vec2(Room.MinX + 0.4f, centre.Y), 0.5f),
                Is.EqualTo(1), "low X");
            Assert.That(WallFacing.Against(Room, new Vec2(centre.X, Room.MaxZ - 0.4f), 0.5f),
                Is.EqualTo(2), "high Z");
            Assert.That(WallFacing.Against(Room, new Vec2(Room.MaxX - 0.4f, centre.Y), 0.5f),
                Is.EqualTo(3), "high X");
        }

        [Test]
        public void APieceInTheOpenIsLeftForItsStreamToTurn()
        {
            Assert.That(WallFacing.Against(Room, Room.Center, 0.5f), Is.EqualTo(WallFacing.Free));
        }

        /// <remarks>
        /// The outdoor half of the same rule, where the wall is handed in rather than found. The
        /// four faces of a rectangle are the four <see cref="WallRun.Faces"/> gives, in the order
        /// it gives them, and each one calls for the turn that leaves the piece's front pointing
        /// the way that face points out.
        /// </remarks>
        [Test]
        public void APieceStoodAgainstAGivenWallIsTurnedByThatWallAndNothingElse()
        {
            List<WallRun.Face> faces = WallRun.Faces(Room);

            Assert.That(WallFacing.AwayFrom(faces[0].AlongX, faces[0].Outward), Is.EqualTo(2), "low Z");
            Assert.That(WallFacing.AwayFrom(faces[1].AlongX, faces[1].Outward), Is.EqualTo(1), "high X");
            Assert.That(WallFacing.AwayFrom(faces[2].AlongX, faces[2].Outward), Is.EqualTo(0), "high Z");
            Assert.That(WallFacing.AwayFrom(faces[3].AlongX, faces[3].Outward), Is.EqualTo(3), "low X");
        }

        /// <remarks>
        /// The geometry the table above only names. A piece stood outside a face has its back on
        /// the face's own line and its front pointing out of the rectangle — measured on the axes
        /// themselves, so it is the claim being asserted rather than the four constants.
        /// </remarks>
        [Test]
        public void TheTurnAGivenWallCallsForPutsTheBackOnTheWall()
        {
            List<WallRun.Face> faces = WallRun.Faces(Room);

            for (int i = 0; i < faces.Count; i++)
            {
                WallRun.Face face = faces[i];
                Vec2 front = Turned(new Vec2(0f, 1f), WallFacing.AwayFrom(face.AlongX, face.Outward));
                Vec2 out_ = face.AlongX ? new Vec2(0f, face.Outward) : new Vec2(face.Outward, 0f);

                Assert.That(front.X, Is.EqualTo(out_.X).Within(1e-4f), $"face {face}");
                Assert.That(front.Y, Is.EqualTo(out_.Y).Within(1e-4f), $"face {face}");
            }
        }

        /// <remarks>
        /// The reach is measured to the pivot, so a piece's own size is what a caller adds to it —
        /// and a piece is as wide from its pivot whichever way it is turned, which is the property
        /// that lets the facing be decided before the turn is. An oblong measures the same radius
        /// as the square that contains it.
        /// </remarks>
        [Test]
        public void APiecesReachFromItsPivotDoesNotChangeWhenItIsTurned()
        {
            var oblong = new Rect2(-0.5f, -1.5f, 0.5f, 1.5f);
            float radius = WallFacing.Radius(oblong);

            Assert.That(radius, Is.EqualTo(1.5f).Within(1e-4f));

            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Assert.That(WallFacing.Radius(QuarterTurn.Rotate(oblong, turns)),
                    Is.EqualTo(radius).Within(1e-4f), $"turned {turns}");
            }
        }

        /// <remarks>
        /// Art modelled off to one side of its pivot reaches further one way than the other, and it
        /// is the further way that decides whether a wall is near enough to turn the piece. Half the
        /// footprint would understate it by however far the art sits off its own origin.
        /// </remarks>
        [Test]
        public void ThePivotReachIsMeasuredFromThePivotAndNotFromTheMiddleOfTheArt()
        {
            Assert.That(WallFacing.Radius(new Rect2(0f, -0.5f, 3f, 0.5f)),
                Is.EqualTo(3f).Within(1e-4f));
        }

        /// <remarks>
        /// The corner rule, and the reason it is a rule of its own. A corner piece is modelled with
        /// two backs — negative Z and negative X — so the corner it stands in decides the turn
        /// outright: there is exactly one of the four in which both of those faces have a wall
        /// behind them.
        /// </remarks>
        [Test]
        public void ACornerPieceIsTurnedSoBothOfItsBacksHaveAWall()
        {
            Assert.That(WallFacing.IntoCorner(Room, new Vec2(Room.MinX, Room.MinZ)), Is.EqualTo(0));
            Assert.That(WallFacing.IntoCorner(Room, new Vec2(Room.MinX, Room.MaxZ)), Is.EqualTo(1));
            Assert.That(WallFacing.IntoCorner(Room, new Vec2(Room.MaxX, Room.MaxZ)), Is.EqualTo(2));
            Assert.That(WallFacing.IntoCorner(Room, new Vec2(Room.MaxX, Room.MinZ)), Is.EqualTo(3));
        }

        /// <remarks>
        /// The claim the two methods above only make separately: the turn a corner calls for really
        /// does put a back against each of the two walls that meet there. Measured on the axes
        /// themselves — where the piece's own negative Z and negative X end up in the world — so it
        /// is the geometry being asserted rather than the table.
        /// </remarks>
        [Test]
        public void TheCornerTurnPutsBothBacksAgainstTheWallsThatMeetThere()
        {
            var corners = new[]
            {
                new Vec2(Room.MinX, Room.MinZ),
                new Vec2(Room.MinX, Room.MaxZ),
                new Vec2(Room.MaxX, Room.MaxZ),
                new Vec2(Room.MaxX, Room.MinZ),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                int turns = WallFacing.IntoCorner(Room, corners[i]);

                Vec2 back = Turned(new Vec2(0f, -1f), turns);
                Vec2 side = Turned(new Vec2(-1f, 0f), turns);
                Vec2 wall = new Vec2(corners[i].X - Room.Center.X, corners[i].Y - Room.Center.Y);

                // One of the two faces the corner's X wall and the other faces its Z wall, so the
                // two together point exactly at the corner.
                Assert.That(back.X + side.X, Is.EqualTo(MathF.Sign(wall.X)).Within(1e-4f),
                    $"corner {corners[i]}");
                Assert.That(back.Y + side.Y, Is.EqualTo(MathF.Sign(wall.Y)).Within(1e-4f),
                    $"corner {corners[i]}");
            }
        }

        // --- what a generated building comes out like -----------------------------------------------

        /// <remarks>
        /// The corner pass, end to end. Every piece it stands up is in a corner — which
        /// <see cref="BuildingDecorTests.EveryPieceOfDecorStandsInACorner"/> asserts — and this is
        /// the half of it that a footprint cannot show: the piece is turned into that corner rather
        /// than turned at random inside it. A pot plant facing the wall is the one thing about a
        /// furnished room that a person notices immediately and no rule about rectangles can see.
        /// </remarks>
        [Test]
        public void EveryPieceOfDecorIsTurnedIntoTheCornerItStandsIn()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var facing = new List<string>();
            var placed = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor, 0f);

                    foreach (PlacedObject decor in OnFloor(doc, floor, DecorSegment))
                    {
                        Rect2 area = rooms[decor.Metadata[BuildingGenerator.RoomKey]];
                        Rect2 piece = PlacedGeometry.WorldFootprint(decor, catalog);
                        placed++;

                        int expected = WallFacing.IntoCorner(area, piece.Center);
                        if (QuarterTurnsOf(decor) != expected)
                        {
                            facing.Add(
                                $"seed {seed}: {decor.StableId} is turned {QuarterTurnsOf(decor)} " +
                                $"in a corner that calls for {expected}");
                        }
                    }
                }
            }

            Assert.That(facing, Is.Empty);
            Assert.That(placed, Is.GreaterThan(Seeds), "the sweep furnished almost nothing");
        }

        /// <remarks>
        /// <para>
        /// The scatter pass, which is the harder half: a room's cover is offered the whole floor, so
        /// what lands against a wall does so because the sampler put it there rather than because a
        /// rule asked for it. Whatever does land there has its back to that wall.
        /// </para>
        /// <para>
        /// Pieces further out than the reach are counted and not checked. They keep the turn their
        /// stream drew, which is the point of the reach — a crate in the middle of a floor has no
        /// wall to have its back to, and pretending it does would turn every room into a ring of
        /// furniture facing inwards.
        /// </para>
        /// </remarks>
        [Test]
        public void CoverThatLandsAgainstAWallHasItsBackToIt()
        {
            Catalog catalog = TestWorlds.StructuralCatalog();
            var facing = new List<string>();
            var against = 0;
            var free = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(
                        doc, catalog, floor, doc.Parameters.WallMargin);

                    foreach (PlacedObject cover in OnFloor(doc, floor, CoverSegment))
                    {
                        Rect2 area = rooms[cover.Metadata[BuildingGenerator.RoomKey]];
                        Rect2 piece = PlacedGeometry.WorldFootprint(cover, catalog);

                        float reach = WallFacing.Radius(catalog.Find(cover.LogicalId).Footprint) +
                            BuildingGenerator.WallFacingReach;

                        int expected = WallFacing.Against(area, cover.Pose.Position.Xz, reach);
                        if (expected == WallFacing.Free)
                        {
                            free++;
                            continue;
                        }

                        against++;
                        if (QuarterTurnsOf(cover) != expected)
                        {
                            facing.Add(
                                $"seed {seed}: {cover.StableId} is turned {QuarterTurnsOf(cover)} " +
                                $"against a wall that calls for {expected}");
                        }
                        else if (Behind(area, piece, expected) > reach)
                        {
                            facing.Add(
                                $"seed {seed}: {cover.StableId} backs onto a wall " +
                                $"{Behind(area, piece, expected):0.##} m away");
                        }
                    }
                }
            }

            Assert.That(facing, Is.Empty);
            Assert.That(against, Is.GreaterThan(Seeds), "nothing ever landed against a wall");
            Assert.That(free, Is.GreaterThan(0), "everything landed against a wall, so the reach is not a reach");
        }

        /// <remarks>
        /// <para>
        /// The corner pass again, with the offer widened until it reaches across the room. How far
        /// from a corner a piece may be offered is sized off the largest piece of decor the catalog
        /// holds, so one big piece widens the offer for every piece — and art modelled well off its
        /// own pivot, which is what an imported model without a fixed pivot is, widens it to the
        /// whole floor. A pass that took its turn from the corner it was <em>walking</em> then stood
        /// furniture in the far corner with its back to open floor and its front in the plaster,
        /// which is what a room full of randomly spun closets actually was.
        /// </para>
        /// <para>
        /// The property is about the walls rather than about the turn: whichever way a piece ended
        /// up facing, each of its two backs has to face the nearer of the two walls on that axis. It
        /// says nothing about which corner the pass meant to fill, which is exactly the thing that
        /// turned out not to be a fact about the piece.
        /// </para>
        /// </remarks>
        [Test]
        public void DecorIsTurnedByTheCornerItLandsInAndNotTheOneItWasOffered()
        {
            Catalog catalog = WithOffPivotDecor(TestWorlds.StructuralCatalog());
            var facing = new List<string>();
            var placed = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor, 0f);

                    foreach (PlacedObject decor in OnFloor(doc, floor, DecorSegment))
                    {
                        Rect2 area = rooms[decor.Metadata[BuildingGenerator.RoomKey]];
                        Vec2 at = decor.Pose.Position.Xz;
                        int turns = QuarterTurnsOf(decor);
                        placed++;

                        // Where the piece's back and a corner piece's second back point once it has
                        // been turned. One of the two is on X and the other on Z, so together they
                        // name the corner the turn claims.
                        Vec2 back = Turned(new Vec2(0f, -1f), turns);
                        Vec2 side = Turned(new Vec2(-1f, 0f), turns);

                        if (!Nearer(back.X + side.X, at.X - area.MinX, area.MaxX - at.X) ||
                            !Nearer(back.Y + side.Y, at.Y - area.MinZ, area.MaxZ - at.Y))
                        {
                            facing.Add(
                                $"seed {seed}: {decor.StableId} is turned {turns} at {at} in {area}, " +
                                "which puts a back against the further of two walls");
                        }
                    }
                }
            }

            Assert.That(facing, Is.Empty);
            Assert.That(placed, Is.GreaterThan(Seeds), "the sweep furnished almost nothing");
        }

        /// <summary>
        /// True if a back pointing <paramref name="towards"/> on one axis faces the nearer of that
        /// axis's two walls.
        /// </summary>
        /// <remarks>
        /// Negative is towards the low wall and positive towards the high one; the two gaps are
        /// measured from the pivot, which is the point the turn is decided from and the one that
        /// does not move when the piece is turned. Equal gaps pass either way — a piece exactly
        /// mid-way between two walls has no nearer one, and the tie is broken rather than wrong.
        /// </remarks>
        static bool Nearer(float towards, float toLow, float toHigh) =>
            towards < 0f ? toLow <= toHigh : toHigh <= toLow;

        const string OffPivotDecorId = "prop/decor/sideboard_offpivot";

        /// <summary>
        /// The same catalog with one piece of decor modelled a long way off its own pivot.
        /// </summary>
        /// <remarks>
        /// What an imported model looks like before somebody fixes its pivot, and it is in the
        /// catalog rather than a number typed into a constant because the widening it causes is a
        /// consequence of the art: the pass measures how far the largest piece of decor reaches
        /// from its own origin, and this reaches three metres.
        /// </remarks>
        static Catalog WithOffPivotDecor(Catalog catalog)
        {
            IReadOnlyList<CatalogEntry> entries = catalog.Entries;
            var rebuilt = new List<CatalogEntry>(entries.Count + 1);

            for (int i = 0; i < entries.Count; i++)
            {
                rebuilt.Add(entries[i]);
            }

            rebuilt.Add(new CatalogEntry(
                OffPivotDecorId,
                new[] { "prop", "prop/decor" },
                new Rect2(-0.4f, 2.6f, 0.4f, 3f),
                0.9f,
                1f,
                null));

            return new Catalog(rebuilt.ToArray());
        }

        /// <remarks>
        /// <para>
        /// The other half of what marks a piece as a corner piece. Its turn is settled by the corner
        /// — asserted above, for the pass that puts everything there — and <em>where</em> it may be
        /// put is settled by its folder: nothing tagged
        /// <see cref="BuildingGenerator.CornerPieceTag"/> answers the scatter's query, so an
        /// L-shaped sofa cannot be drawn into the middle of a floor with its short back over open
        /// ground.
        /// </para>
        /// <para>
        /// The catalog here has one corner piece and it is enormously heavier than the rest of the
        /// decor, so a sweep this long would put it in nearly every corner in the building if the
        /// two queries had quietly become one.
        /// </para>
        /// </remarks>
        [Test]
        public void ACornerPieceIsOnlyEverStoodInACorner()
        {
            Catalog catalog = WithCornerPiece(TestWorlds.StructuralCatalog());
            var scattered = new List<string>();
            var stood = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    foreach (PlacedObject placed in OnFloor(doc, floor, string.Empty))
                    {
                        if (placed.LogicalId != CornerPieceId)
                        {
                            continue;
                        }

                        stood++;
                        if (!placed.StableId.Contains(DecorSegment))
                        {
                            scattered.Add($"seed {seed}: {placed.StableId} is not corner decor");
                        }
                    }
                }
            }

            Assert.That(scattered, Is.Empty);
            Assert.That(stood, Is.GreaterThan(0), "the corner piece was never picked at all");
        }

        // --- helpers ---------------------------------------------------------------------------------

        const string CornerPieceId = "propbuilding/decor/corners/sofa_l_01";

        /// <summary>The same catalog with one heavily weighted corner piece in it.</summary>
        static Catalog WithCornerPiece(Catalog catalog)
        {
            IReadOnlyList<CatalogEntry> entries = catalog.Entries;
            var rebuilt = new List<CatalogEntry>(entries.Count + 1);

            for (int i = 0; i < entries.Count; i++)
            {
                rebuilt.Add(entries[i]);
            }

            rebuilt.Add(new CatalogEntry(
                CornerPieceId,
                new[] { "propbuilding", "propbuilding/decor", BuildingGenerator.CornerPieceTag },
                new Rect2(-0.7f, -0.7f, 0.7f, 0.7f),
                0.8f,
                20f,
                null));

            return new Catalog(rebuilt.ToArray());
        }

        /// <summary>How far a piece stands from the wall a given turn puts its back to.</summary>
        static float Behind(Rect2 region, Rect2 piece, int facing)
        {
            switch (facing)
            {
                case 0:
                    return piece.MinZ - region.MinZ;
                case 1:
                    return piece.MinX - region.MinX;
                case 2:
                    return region.MaxZ - piece.MaxZ;
                default:
                    return region.MaxX - piece.MaxX;
            }
        }

        /// <summary>A direction on the ground plane after a whole number of quarter turns.</summary>
        /// <remarks>
        /// Through <see cref="QuarterTurn.Rotate(Rect2, int)"/> rather than through a rotation of
        /// the vector, so the turn being measured is exactly the one the generator applies to a
        /// footprint.
        /// </remarks>
        static Vec2 Turned(Vec2 direction, int quarterTurns)
        {
            Rect2 rotated = QuarterTurn.Rotate(
                Rect2.FromCorners(Vec2.Zero, direction), quarterTurns);

            return new Vec2(
                MathF.Abs(rotated.MinX) > MathF.Abs(rotated.MaxX) ? rotated.MinX : rotated.MaxX,
                MathF.Abs(rotated.MinZ) > MathF.Abs(rotated.MaxZ) ? rotated.MinZ : rotated.MaxZ);
        }

        /// <summary>How many quarter turns a placed object was turned by.</summary>
        static int QuarterTurnsOf(PlacedObject placed)
        {
            for (int turns = 0; turns < QuarterTurn.Count; turns++)
            {
                Quat rotation = QuarterTurn.Rotation(turns);
                if (MathF.Abs(rotation.Y - placed.Pose.Rotation.Y) < 1e-4f &&
                    MathF.Abs(rotation.W - placed.Pose.Rotation.W) < 1e-4f)
                {
                    return turns;
                }
            }

            Assert.Fail($"{placed.StableId} is not at a quarter turn");
            return 0;
        }

        /// <summary>One floor's rooms as the regions their contents were proposed onto.</summary>
        /// <remarks>
        /// The inset is the pass's own: the corner pass proposes onto the room less a wall
        /// thickness, and the scatter pass onto the same less the wall margin on top. The turn a
        /// piece was given was measured against that exact rectangle, so anything else here would
        /// be measuring a different room.
        /// </remarks>
        static Dictionary<string, Rect2> Rooms(
            BuildingDoc doc, Catalog catalog, int floor, float margin)
        {
            FloorPlan plan = BuildingGenerator.PlanFloor(
                doc.Parameters, catalog, doc.Floors[floor], floor);

            var rooms = new Dictionary<string, Rect2>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                rooms[plan.Rooms[i].Id] = plan.Rooms[i].Bounds.Expanded(-(plan.Thickness + margin));
            }

            return rooms;
        }

        static List<PlacedObject> OnFloor(BuildingDoc doc, int floor, string segment)
        {
            string prefix = BuildingDoc.FloorIdPrefix(floor) + "/";
            var found = new List<PlacedObject>();

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.StartsWith(prefix, StringComparison.Ordinal) &&
                    placed.StableId.Contains(segment) &&
                    placed.Metadata.ContainsKey(BuildingGenerator.RoomKey))
                {
                    found.Add(placed);
                }
            }

            return found;
        }
    }
}
