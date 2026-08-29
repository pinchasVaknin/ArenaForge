using System;
using System.Collections.Generic;
using System.Text;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Centrepieces: the sofa or the table a room is arranged around, drawn from the art a
    /// workspace files in <c>Props/PropBuilding/Decor/Centerpieces</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third thing a room is filled with, and the one whose whole claim is about where it is
    /// rather than what it is. A table placed by the cover pass would satisfy every property the
    /// shell and decor suites assert — inside its room, off the walkways, clear of the doorways —
    /// and would end up wherever the sampler found space, which for a piece of furniture that is
    /// meant to be the focus of the room is the one place it must not be.
    /// </para>
    /// <para>
    /// So what is swept below is the pair of claims the pass exists to make: every centrepiece
    /// keeps the full clearance off every wall of its room, and it is nearer the middle of that
    /// room than the cover scattered round it. The first is the rule; the second is the rule
    /// actually being worth having, because a room whose middle is a strip of reserved walkway can
    /// satisfy the first with a piece tucked into what is left and still not read as furnished.
    /// </para>
    /// </remarks>
    public sealed class BuildingCentrepieceTests
    {
        /// <summary>Seeds swept by the placement properties.</summary>
        const int Seeds = 60;

        const string CentreSegment = "/centrepiece_";
        const string CoverSegment = "/cover_";
        const string DecorSegment = "/decor_";

        /// <summary>Slack for a distance the generator computed and this recomputes.</summary>
        const float Tolerance = 1e-4f;

        static BuildingParams Params(ulong seed)
        {
            BuildingParams parameters = TestWorlds.SampleBuildingParams();
            parameters.Seed = seed;
            return parameters;
        }

        static BuildingDoc Generate(ulong seed) =>
            BuildingGenerator.Generate(Params(seed), TestWorlds.FurnishedCatalog());

        // --- centrepieces are placed at all -------------------------------------------------------

        [Test]
        public void ACatalogWithCentrepieceArtStandsThemInTheRooms()
        {
            BuildingDoc doc = Generate(20260816UL);
            var centrepieces = 0;

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!placed.StableId.Contains(CentreSegment))
                {
                    continue;
                }

                centrepieces++;
                Assert.That(placed.Tags, Does.Contain(BuildingGenerator.CentrepieceTag));
                Assert.That(placed.Metadata.ContainsKey(BuildingGenerator.RoomKey), Is.True,
                    "a centrepiece stands in a room and says which");
            }

            Assert.That(centrepieces, Is.GreaterThan(0), "no room was ever given a centrepiece");
        }

        /// <remarks>
        /// A centrepiece is art the catalog either has or does not, exactly as decor and windows
        /// are. A catalog of crates, walls and pot plants builds the same building it built before
        /// there was such a thing as a centrepiece — the stage draws nothing and commits nothing
        /// when the query comes back empty, so it cannot have moved a crate in any building anyone
        /// had already made.
        /// </remarks>
        [Test]
        public void ACatalogWithNoCentrepieceArtStandsNothingInTheMiddle()
        {
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(
                    Params(seed), TestWorlds.StructuralCatalog());

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    Assert.That(doc.GeneratedObjects[i].StableId, Does.Not.Contain(CentreSegment));
                }
            }
        }

        /// <remarks>
        /// <para>
        /// The claim the folder rename was made for, asserted rather than argued. Tactical cover
        /// and a room's centrepiece are two piles of art in two folders, and they used to be two
        /// folders with the same name on them — <c>Props/Covers</c> and
        /// <c>Props/PropBuilding/Decor/Covers</c> — kept apart by the catalog sync's branch rule
        /// alone. A dumpster standing in the middle of a room as the thing that room is arranged
        /// around is the failure that arrangement invited, and the query being an exact match on a
        /// tag nothing else carries is what rules it out.
        /// </para>
        /// <para>
        /// Swept over a catalog that has both, because the property is only worth anything when
        /// there is cover art on offer for the pass to have picked by mistake.
        /// </para>
        /// </remarks>
        [Test]
        public void NoCentrepieceIsEverDrawnFromTheMapsOwnCoverArt()
        {
            var cover = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (placed.StableId.Contains(CoverSegment))
                    {
                        cover++;
                    }

                    if (!placed.StableId.Contains(CentreSegment))
                    {
                        continue;
                    }

                    Assert.That(placed.Tags, Does.Not.Contain(CoverPlacer.CoverTag),
                        $"seed {seed}: {placed.LogicalId} is map cover standing as a centrepiece");
                    Assert.That(placed.LogicalId,
                        Does.StartWith(BuildingGenerator.CentrepieceTag + "/"),
                        $"seed {seed}: {placed.StableId} was not drawn from the centrepiece folder");
                }
            }

            Assert.That(cover, Is.GreaterThan(0),
                "no cover was placed at all, so nothing was ever on offer to be picked by mistake");
        }

        [Test]
        public void NothingIsStoodInTheMiddleOfACorridor()
        {
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed);

                for (int i = 0; i < doc.GeneratedObjects.Count; i++)
                {
                    PlacedObject placed = doc.GeneratedObjects[i];
                    if (placed.StableId.Contains(CentreSegment))
                    {
                        Assert.That(placed.Metadata[BuildingGenerator.RoomKey],
                            Does.StartWith("room_"), placed.StableId);
                    }
                }
            }
        }

        // --- and they are in the middle -----------------------------------------------------------

        /// <remarks>
        /// The rule itself. Every centrepiece keeps <see cref="BuildingGenerator.CentrepieceClearance"/>
        /// of floor between itself and all four walls of the room it was proposed into — not the
        /// nearest one, all of them, which is what makes it the negation of standing against a wall
        /// rather than a looser version of it.
        /// </remarks>
        [Test]
        public void EveryCentrepieceKeepsItsClearanceOffEveryWall()
        {
            Catalog catalog = TestWorlds.FurnishedCatalog();
            var beached = new List<string>();
            var placed = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor);

                    List<PlacedObject> centrepieces = OnFloor(doc, floor, CentreSegment);
                    for (int i = 0; i < centrepieces.Count; i++)
                    {
                        Rect2 area = rooms[centrepieces[i].Metadata[BuildingGenerator.RoomKey]];
                        Rect2 piece = PlacedGeometry.WorldFootprint(centrepieces[i], catalog);
                        placed++;

                        if (Gap(area, piece) < BuildingGenerator.CentrepieceClearance - Tolerance)
                        {
                            beached.Add(
                                $"seed {seed}: {centrepieces[i].StableId} at {piece} is " +
                                $"{Gap(area, piece):0.##} m from a wall of {area}");
                        }
                    }
                }
            }

            Assert.That(beached, Is.Empty);
            Assert.That(placed, Is.GreaterThan(Seeds), "the sweep placed almost nothing");
        }

        /// <remarks>
        /// <para>
        /// The comparative property, and the one that would catch the pass quietly becoming the
        /// cover pass with another tag on it. A centrepiece is nearer the middle of its room than
        /// the average piece of cover in the same room is, measured as a fraction of how far the
        /// middle is from the wall so that rooms of different sizes can be counted together.
        /// </para>
        /// <para>
        /// Over the sweep rather than room by room: a single room can honestly come out with its
        /// one crate dead centre and its table pushed off the middle by a walkway, and asserting
        /// per room would be asserting that never happens rather than that the two passes differ.
        /// </para>
        /// </remarks>
        [Test]
        public void ACentrepieceSitsNearerTheMiddleThanTheCoverAroundIt()
        {
            Catalog catalog = TestWorlds.FurnishedCatalog();
            float centre = 0f;
            float cover = 0f;
            var centreCount = 0;
            var coverCount = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    Dictionary<string, Rect2> rooms = Rooms(doc, catalog, floor);

                    List<PlacedObject> centrepieces = OnFloor(doc, floor, CentreSegment);
                    for (int i = 0; i < centrepieces.Count; i++)
                    {
                        centre += OffCentre(centrepieces[i], rooms, catalog);
                        centreCount++;
                    }

                    List<PlacedObject> crates = OnFloor(doc, floor, CoverSegment);
                    for (int i = 0; i < crates.Count; i++)
                    {
                        cover += OffCentre(crates[i], rooms, catalog);
                        coverCount++;
                    }
                }
            }

            Assert.That(centreCount, Is.GreaterThan(0), "no centrepiece was placed at all");
            Assert.That(coverCount, Is.GreaterThan(0), "no cover was placed at all");
            Assert.That(centre / centreCount, Is.LessThan(cover / coverCount),
                "centrepieces are no nearer the middle of a room than the cover is");
        }

        /// <remarks>
        /// The rule has to be one the constraint set actually evaluates. A rule that never rejected
        /// anything would be indistinguishable in the statistics from one that was never asked, and
        /// the pass offers it the cells nearest the middle of the room first — so if none of them
        /// were ever refused, the clearance would be one no piece of art could fail and the middle
        /// of a room would mean nothing.
        /// </remarks>
        [Test]
        public void TheCentreRuleIsOneThatActuallyTurnsCandidatesAway()
        {
            string key = BuildingGenerator.StatsPrefix + "rejected_" +
                         PlacementStats.MetadataName(ConstraintKind.InCentre);
            var rejections = 0;

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = Generate(seed);

                if (doc.Metadata.TryGetValue(key, out string tally) &&
                    int.TryParse(tally, out int count))
                {
                    rejections += count;
                }
            }

            Assert.That(rejections, Is.GreaterThan(0),
                "no candidate was ever turned away for standing too near a wall");
        }

        // --- and they obey everything else the floor obeys ----------------------------------------

        /// <remarks>
        /// The golden rule, against the pass most likely to break it: a centrepiece wants exactly
        /// the floor a walkway is reserved on, because the chain of strips joining a room's doors
        /// runs through the middle of it. A table across the only route between two doors is a room
        /// a player walks into and cannot cross.
        /// </remarks>
        [Test]
        public void NoCentrepieceStandsOnAReservedWalkway()
        {
            Catalog catalog = TestWorlds.FurnishedCatalog();
            var blocked = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    FloorPlan plan = BuildingGenerator.PlanFloor(
                        doc.Parameters, catalog, doc.Floors[floor], floor);

                    var paths = new Dictionary<string, IReadOnlyList<Rect2>>(StringComparer.Ordinal);
                    for (int i = 0; i < plan.Rooms.Count; i++)
                    {
                        paths[plan.Rooms[i].Id] = plan.Rooms[i].Paths;
                    }

                    List<PlacedObject> centrepieces = OnFloor(doc, floor, CentreSegment);
                    for (int i = 0; i < centrepieces.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(centrepieces[i], catalog);
                        IReadOnlyList<Rect2> strips =
                            paths[centrepieces[i].Metadata[BuildingGenerator.RoomKey]];

                        for (int p = 0; p < strips.Count; p++)
                        {
                            if (piece.Overlaps(strips[p]))
                            {
                                blocked.Add(
                                    $"seed {seed}: {centrepieces[i].StableId} stands in the way");
                                break;
                            }
                        }
                    }
                }
            }

            Assert.That(blocked, Is.Empty);
        }

        [Test]
        public void NoCentrepieceOverlapsAnythingElseStandingOnItsFloor()
        {
            Catalog catalog = TestWorlds.FurnishedCatalog();
            var collisions = new List<string>();

            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                BuildingDoc doc = BuildingGenerator.Generate(Params(seed), catalog);

                for (int floor = 0; floor < doc.Floors.Count; floor++)
                {
                    List<PlacedObject> onFloor = Standing(doc, floor);
                    List<PlacedObject> centrepieces = OnFloor(doc, floor, CentreSegment);

                    for (int i = 0; i < centrepieces.Count; i++)
                    {
                        Rect2 piece = PlacedGeometry.WorldFootprint(centrepieces[i], catalog);
                        for (int j = 0; j < onFloor.Count; j++)
                        {
                            if (ReferenceEquals(centrepieces[i], onFloor[j]))
                            {
                                continue;
                            }

                            if (piece.Overlaps(PlacedGeometry.WorldFootprint(onFloor[j], catalog)))
                            {
                                collisions.Add(
                                    $"seed {seed}: {centrepieces[i].StableId} overlaps " +
                                    onFloor[j].StableId);
                            }
                        }
                    }
                }
            }

            Assert.That(collisions, Is.Empty);
        }

        // --- and they are their own stage ---------------------------------------------------------

        /// <remarks>
        /// <para>
        /// A centrepiece is not cover and is not decor, and the tags say so rather than the folder
        /// alone. <see cref="CoverPlacer"/> queries <c>cover</c> to scatter a lane, and a sofa that
        /// answered that query would be strewn over the open ground of the map and shot over from
        /// the next lane along.
        /// </para>
        /// <para>
        /// The reverse too: a crate that answered the centrepiece query would be placed by the
        /// centre pass, which is the one thing the deep folder layout exists to stop.
        /// </para>
        /// </remarks>
        [Test]
        public void ACentrepieceIsNeitherCoverNorDecor()
        {
            BuildingDoc doc = Generate(11UL);
            var centrepieces = 0;

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];

                if (placed.StableId.Contains(CentreSegment))
                {
                    centrepieces++;
                    Assert.That(placed.Tags, Does.Not.Contain(CoverPlacer.CoverTag), placed.StableId);
                    Assert.That(placed.Tags, Does.Not.Contain(BuildingGenerator.DecorTag), placed.StableId);
                }

                if (placed.StableId.Contains(CoverSegment) || placed.StableId.Contains(DecorSegment))
                {
                    Assert.That(placed.Tags, Does.Not.Contain(BuildingGenerator.CentrepieceTag),
                        placed.StableId);
                }
            }

            Assert.That(centrepieces, Is.GreaterThan(0), "nothing was stood in the middle at all");
        }

        /// <remarks>
        /// Its own fork of the floor's seed, as decor has. The decor pass runs after this one and
        /// draws from a stream of its own, so a catalog that gains or loses its pot plants stands
        /// its tables in exactly the same places — which is the property that lets someone add art
        /// to a workspace without re-rolling the buildings they have already arranged.
        /// </remarks>
        [Test]
        public void CentrepiecesAndDecorDrawFromSeparateStreams()
        {
            Catalog withDecor = TestWorlds.FurnishedCatalog();
            Catalog withoutDecor = WithoutDecor(withDecor);

            BuildingDoc furnished = BuildingGenerator.Generate(Params(13UL), withDecor);
            BuildingDoc bare = BuildingGenerator.Generate(Params(13UL), withoutDecor);

            Assert.That(Signature(furnished, CentreSegment), Is.Not.Empty);
            Assert.That(Signature(furnished, CentreSegment), Is.EqualTo(Signature(bare, CentreSegment)));
            Assert.That(Signature(bare, DecorSegment), Is.Empty);
        }

        [Test]
        public void TheSameSeedStandsTheSameCentrepieces()
        {
            Assert.That(
                ArenaJson.SerializeBuilding(Generate(77UL)),
                Is.EqualTo(ArenaJson.SerializeBuilding(Generate(77UL))));
        }

        // --- helpers ------------------------------------------------------------------------------

        /// <summary>How far a rectangle stops short of the nearest edge of the area holding it.</summary>
        static float Gap(Rect2 area, Rect2 piece) => MathF.Min(
            MathF.Min(piece.MinX - area.MinX, area.MaxX - piece.MaxX),
            MathF.Min(piece.MinZ - area.MinZ, area.MaxZ - piece.MaxZ));

        /// <summary>
        /// How far a placed object is from the middle of its room, as a fraction of how far the
        /// middle is from the corner.
        /// </summary>
        /// <remarks>
        /// Scaled, so that a piece in the middle of a small room and one in the middle of a large
        /// one both read as nought and the two can be averaged together. Unscaled, a sweep over
        /// rooms of every size would mostly be measuring which rooms are big.
        /// </remarks>
        static float OffCentre(
            PlacedObject placed, Dictionary<string, Rect2> rooms, Catalog catalog)
        {
            Rect2 area = rooms[placed.Metadata[BuildingGenerator.RoomKey]];
            Vec2 at = PlacedGeometry.WorldFootprint(placed, catalog).Center;
            Vec2 middle = area.Center;

            float x = MathF.Abs(at.X - middle.X) / (area.Width * 0.5f);
            float z = MathF.Abs(at.Y - middle.Y) / (area.Depth * 0.5f);
            return MathF.Sqrt(x * x + z * z);
        }

        /// <summary>One floor's rooms as the areas their contents are proposed onto, by room id.</summary>
        static Dictionary<string, Rect2> Rooms(BuildingDoc doc, Catalog catalog, int floor)
        {
            FloorPlan plan = BuildingGenerator.PlanFloor(
                doc.Parameters, catalog, doc.Floors[floor], floor);

            var rooms = new Dictionary<string, Rect2>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                rooms[plan.Rooms[i].Id] = plan.Rooms[i].Bounds.Expanded(-plan.Thickness);
            }

            return rooms;
        }

        /// <summary>Everything on a floor that stands on the slab rather than being the slab.</summary>
        static List<PlacedObject> Standing(BuildingDoc doc, int floor)
        {
            List<PlacedObject> onFloor = OnFloor(doc, floor, string.Empty);
            onFloor.RemoveAll(p => p.StableId.Contains("/slab/"));
            return onFloor;
        }

        static List<PlacedObject> OnFloor(BuildingDoc doc, int floor, string segment)
        {
            string prefix = BuildingDoc.FloorIdPrefix(floor) + "/";
            var found = new List<PlacedObject>();

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.StartsWith(prefix, StringComparison.Ordinal) &&
                    placed.StableId.Contains(segment))
                {
                    found.Add(placed);
                }
            }

            return found;
        }

        /// <summary>Every object whose id carries a segment, as one comparable string.</summary>
        static string Signature(BuildingDoc doc, string segment)
        {
            var text = new StringBuilder();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (placed.StableId.Contains(segment))
                {
                    text.Append(placed.StableId).Append('@').Append(placed.Pose).Append(';');
                }
            }

            return text.ToString();
        }

        /// <summary>The furnished catalog with its corner decor taken out.</summary>
        static Catalog WithoutDecor(Catalog catalog)
        {
            var kept = new List<CatalogEntry>(catalog.Entries.Count);
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                CatalogEntry entry = catalog.Entries[i];
                if (!entry.HasTag(BuildingGenerator.DecorTag))
                {
                    kept.Add(entry);
                }
            }

            return new Catalog(kept.ToArray());
        }
    }
}
