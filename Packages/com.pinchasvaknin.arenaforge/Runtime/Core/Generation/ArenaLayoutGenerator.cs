using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Turns parameters and a catalog into a small competitive arena: lane bands, two opposing
    /// spawns, the structures that anchor the middle and the flanks, the fence round the outside,
    /// and the cover between them all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape follows how these maps are actually built. Two spawns at either end, a handful of
    /// roughly parallel routes between them, a strong point of interest in the contested middle
    /// lane, and something softer on each flank. Cover is scattered last, by
    /// <see cref="CoverPlacer"/>, because it has to place around everything else.
    /// </para>
    /// <para>
    /// <strong>How many structures there are is decided by the ground, not by a number in this
    /// file.</strong> The playfield between the two spawns is divided into layout cells of about
    /// <see cref="MetresPerStructure"/> each — see <see cref="StructureCells"/> — and every cell is
    /// offered a structure it can afford. A forty-metre map has room for one or two, the default
    /// sixty-metre map comes out with the three it always had, and a four-hundred-metre map is a
    /// town. It used to be one building and a house per flank, capped at the number of flanks,
    /// which made every map above about eighty metres the same three buildings on more and more
    /// empty grass.
    /// </para>
    /// <para>
    /// <strong>One of those cells is a demand and the rest are offers.</strong> The map's anchor —
    /// a building, in the middle lane, near the centre — is tried in its own cell, then in the
    /// whole middle lane, then in every other lane, and the generation fails with a message naming
    /// what could not be placed rather than returning a field with nothing on it. Every other cell
    /// takes a structure if one will stand there and stays empty if none will, because "as many as
    /// the ground will hold" is not a number anything can be held to in advance. Which piece of art
    /// a cell gets is a weighted draw over everything tagged <see cref="StructureTag"/> that fits
    /// the cell's own size budget, so a big cell may come up with a building or a house and a small
    /// one only ever with a house.
    /// </para>
    /// <para>
    /// <see cref="ExteriorPlacer"/> and <see cref="PerimeterFence"/> run between the structures and
    /// the cover, and the order is the same argument. What they put down is anchored — a hedge
    /// belongs against a particular wall, a fence round a particular yard, the boundary along the
    /// edge of the playfield — where a piece of cover belongs wherever there is room for it, so the
    /// anchored stages go first and the free one places around what it finds. They hand their
    /// placements to the cover stage for exactly that reason.
    /// </para>
    /// <para>
    /// <strong><see cref="RoadNetwork"/> is the one stage that puts nothing down.</strong> It runs
    /// near the end, and it has to: the network is laid to reach the doorways the structures
    /// declared, so it cannot run before they are placed, and what it produces is a reservation
    /// cover has to respect, so it cannot run after the cover is scattered. What it hands on is
    /// <see cref="RoadNetwork.Corridors"/> — the ground the carriageways cover — and cover both
    /// keeps off it and prefers to stand beside it. It also grades the ground under itself, through
    /// <see cref="RoadNetwork.GradeInto"/>, which is why it goes down before the cover rather than
    /// beside it: a prop reads a height as it is stood up. At a
    /// <see cref="ArenaParams.RoadDensity"/> of zero no road is laid, nothing is reserved, nothing
    /// is graded, and the document is byte for byte the document this generator produced before the
    /// stage was in the pipeline at all.
    /// </para>
    /// <para>
    /// <strong><see cref="RoadKerbs"/> is what the network does put down</strong>, and it is the
    /// last anchored stage rather than a part of the road stage because a kerb is art and a network
    /// is a measurement. It follows the grading — a kerb stands on the road's own surface — and
    /// hands its pieces to the cover stage with the rest of the anchored art. A catalog with nothing
    /// tagged <see cref="RoadKerbs.KerbTag"/> takes no draw there, so a workspace that has never
    /// filled the folder generates the map it always did whatever its road density is.
    /// </para>
    /// <para>
    /// <strong><see cref="RoadFurniture"/> is the last of them</strong>, and it goes after the kerbs
    /// rather than beside them because it is seated <em>beyond</em> them: a bench judged against a
    /// kerb it was meant to stand behind is a bench refused. It makes the same bargain with an empty
    /// folder. What it stands on the ground does not shrink the floor the cover target is counted
    /// off, and neither does the kerbing — see <c>CoverPlacer.IsRoadside</c>, which is where the two
    /// road stages' art is told apart from the dressing round a building.
    /// </para>
    /// </remarks>
    public static class ArenaLayoutGenerator
    {
        /// <summary>Metadata key naming the lane an object belongs to.</summary>
        public const string LaneKey = "lane";

        /// <summary>Metadata key naming the side a spawn marker belongs to — <c>a</c> or <c>b</c>.</summary>
        public const string TeamKey = "team";

        /// <summary>Metadata key holding a spawn marker's area as a rectangle.</summary>
        public const string SpawnAreaKey = "spawn_area";

        /// <summary>Metadata key holding the radius of a spawn's graded pad, in metres.</summary>
        /// <remarks>
        /// Its presence is what tells <see cref="TerrainBeforeRoads"/> that this object's pad is a
        /// disc rather than the rectangle <see cref="FoundationKey"/> names. Replaying the ground a
        /// document was generated on has to put back the same shape, not an equivalent one.
        /// </remarks>
        public const string FoundationRadiusKey = "foundation_radius";

        /// <summary>
        /// How much of a spawn area's short side its graded pad takes, as a radius.
        /// </summary>
        /// <remarks>
        /// A half, so the disc is inscribed in the band: the widest circle that is still entirely
        /// inside the ground the layout calls a spawn. Larger would grade flat ground into the lane
        /// in front of it, which is the ground the map is supposed to be interesting on.
        /// </remarks>
        public const float SpawnPadShare = 0.5f;

        /// <summary>Metadata key holding how many doorway rectangles a structure declares.</summary>
        public const string DoorwayCountKey = "doorway_count";

        /// <summary>Prefix of the metadata keys holding a structure's doorway rectangles.</summary>
        public const string DoorwayKeyPrefix = "doorway_";

        /// <summary>Metadata key holding the rectangle of ground levelled under an object.</summary>
        public const string FoundationKey = "foundation";

        /// <summary>Metadata key holding the height that rectangle was levelled to.</summary>
        public const string FoundationHeightKey = "foundation_height";

        /// <summary>Tag a catalog entry must carry to be used as a spawn marker.</summary>
        public const string SpawnMarkerTag = "spawn";

        /// <summary>Tag every structure carries, whichever slot it fills.</summary>
        public const string StructureTag = "structure";

        /// <summary>Tag a catalog entry must carry to be used as the middle-lane building.</summary>
        public const string BuildingTag = "structure/building";

        /// <summary>Tag a catalog entry must carry to be used as the flank house.</summary>
        public const string HouseTag = "structure/house";

        /// <summary>Ground one layout cell is given, in square metres.</summary>
        /// <remarks>
        /// <para>
        /// A fifty-metre square, which is the size of the arena this tool was built for, and that
        /// is the whole of the argument. The default sixty-metre map is one structure to a lane —
        /// three of them, which is the map every screenshot and every threshold in this project was
        /// measured on — so the density that map has is the density every larger map is tiled at.
        /// A hundred-metre map comes out with about three, a two-hundred-metre map with about six,
        /// and a four-hundred-metre map with about three dozen.
        /// </para>
        /// <para>
        /// It is the ground <em>a cell</em> is given rather than the ground a structure takes, and
        /// the difference is what leaves the map playable. A cell that draws a small house leaves
        /// most of itself as yard, route and cover; the size budget in
        /// <see cref="SelectStructure"/> is what keeps a cell from being filled corner to corner by
        /// one enormous building.
        /// </para>
        /// </remarks>
        public const float MetresPerStructure = 2500f;

        /// <summary>Clear space a structure keeps from a spawn area, in metres.</summary>
        public const float SpawnClearance = 2f;

        /// <summary>Clear space two structures keep from each other, in metres.</summary>
        /// <remarks>
        /// Two yards' worth, because a yard is what goes in it. Every house on the map is fenced
        /// <see cref="ExteriorPlacer.YardMargin"/> out from its own walls, so two structures closer
        /// together than twice that have their fences threaded through each other — which was
        /// invisible while a map held three structures on sixty metres and is the normal case on a
        /// map that holds three dozen. Stating it as twice the yard rather than as a number of its
        /// own is what keeps the two from drifting apart.
        /// </remarks>
        public const float StructureClearance = 2f * ExteriorPlacer.YardMargin;

        /// <summary>Share of its cell a structure may cover at <c>StructureDensity</c> of 1.</summary>
        /// <remarks>
        /// A quarter, which used to be a quarter of a whole lane and is now a quarter of one layout
        /// cell. On the default map the two are nearly the same number, because a lane there holds
        /// exactly one cell; on a large map the cell is what a structure is actually competing for
        /// and the lane is a strip a hundred metres wide.
        /// </remarks>
        const float BaselineLaneShare = 0.25f;

        /// <summary>How far from the centre of the long axis the middle structure may sit.</summary>
        const float BuildingCentreSpan = 0.15f;

        /// <summary>Structures a map may hold, whatever the catalog declares. See <see cref="Doorways(ArenaLayout, CatalogEntry, Vec2, int, Rect2)"/>.</summary>
        /// <remarks>
        /// Exactly two, and it is a rule of level design rather than a measurement. One way into a
        /// building makes the building a dead end; three make it a crossroads with no walls worth
        /// holding. Two, far apart, make it a route — so a structure that declares more openings
        /// than that has the two furthest apart chosen, and one that declares fewer is topped up
        /// from its own footprint.
        /// </remarks>
        public const int DoorwaysPerStructure = 2;

        const float DoorwayWidth = 2.5f;

        /// <summary>How deep a doorway rectangle is across the wall it is in, in metres.</summary>
        /// <remarks>
        /// Public because a building declares its own doorways in its own space and they have to
        /// be the same shape as the ones derived here: the rectangle is the threshold a person
        /// crosses rather than the gap in the wall, and it is a metre deep so that the walkable
        /// grid the validator builds lands a cell inside it. A rectangle as deep as a wall is thick
        /// is a fifth of a metre of ground, which that grid steps over — and a doorway no cell
        /// falls inside is reported as one nobody can reach.
        /// </remarks>
        public const float DoorwayDepth = 1f;

        const float DoorwayShareOfFace = 0.4f;

        /// <summary>Random positions tried before a structure falls back to a deterministic sweep.</summary>
        const int PlacementAttempts = 64;

        /// <summary>
        /// Generates a map. The same parameters always produce the same document, down to the
        /// bytes it serialises to.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">
        /// The catalog cannot supply a required piece, or a structure the composition rule demands
        /// will not fit in any lane of the map.
        /// </exception>
        public static WorldDoc Generate(ArenaParams parameters, Catalog catalog)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            ArenaLayout layout = ArenaLayout.Build(parameters);
            TerrainField terrain = TerrainField.Build(parameters);
            var doc = new WorldDoc { Parameters = parameters.Clone() };

            EmitSpawnMarkers(doc, layout, terrain, catalog);
            List<MapStructure> structures = EmitStructures(doc, layout, terrain, catalog, parameters);

            // Everything anchored, in the order it goes down: the boundary round the map, then the
            // dressing round each structure, which places round whatever the boundary left. Cover
            // is scattered through what is left of the floor.
            //
            // The boundary goes first because it is the only one of the three that may not have a
            // hole in it. It is tiled from the longest art on the map — a stone pack whose panels
            // are thirty metres each tiles a sixty-metre edge two panels at a time — so a single
            // lamp post standing where a panel wanted to go used to cost half a side of the level,
            // and the lamp post had got there first only because the dressing ran first. The edge
            // of the world outranks a hedge, and putting it down first is how that is said.
            List<Placement> boundary = PerimeterFence.Place(doc, layout, terrain, catalog, structures);

            // Then the rings round the spawns, before the dressing rather than after it for the
            // same reason the boundary goes before both: a spawn's fence is a fact about the map
            // that a hedge may not take the ground out from under. It is much less art than the
            // boundary — two squares of a dozen metres — so it costs the dressing almost nothing.
            List<Placement> spawns = SpawnEnclosure.Place(
                doc, layout, terrain, catalog, structures, boundary);

            var up = new List<Placement>(boundary.Count + spawns.Count);
            up.AddRange(boundary);
            up.AddRange(spawns);

            List<Placement> anchored = ExteriorPlacer.Place(
                doc, layout, terrain, catalog, structures, up);
            anchored.AddRange(up);

            // The roads go down between the two, and the order is the whole of what makes them
            // roads. A network is laid to reach the doorways the structures declared, so it cannot
            // run before they are placed; and the ground it reserves is ground cover may not stand
            // in, so it cannot run after the cover is scattered. Computing it afterwards — which is
            // where the stage sat while nothing consumed it — leaves crates standing in the
            // carriageway on a large fraction of seeds.
            //
            // Nothing is added to the document here. A network is a pure function of the parameters,
            // the ground and the placements, in the way ArenaLayout and TerrainField are, and this
            // is the one stage of the pipeline whose output is a reservation rather than an object.
            RoadNetwork roads = RoadNetwork.Build(parameters, layout, terrain, doc.GeneratedObjects);

            // And then the ground under it, before anything else is stood on that ground. The
            // network was routed over the field as the structures left it, so it has to be graded
            // in after it is laid rather than before; and cover reads a height for every prop it
            // stands up, so it has to be graded before the cover stage rather than after. That
            // leaves exactly here.
            roads.GradeInto(terrain);

            // And then the one thing the network does put down. A kerb is anchored art like a hedge
            // or a boundary panel — it belongs against a particular carriageway — so it goes down
            // with the other anchored stages and the cover places around it. It runs after the
            // grading rather than before, because a kerb is stood on the road's own surface and a
            // prop reads its height as it is stood up. A catalog with nothing tagged road/kerb in it
            // takes no draw here and adds no object.
            anchored.AddRange(
                RoadKerbs.Place(doc, layout, terrain, catalog, roads, structures, anchored));

            // And then what stands on the verge behind the edging. Street furniture is anchored art
            // for the same reason a kerb is — it belongs against a particular carriageway — and it
            // goes down after the kerbs rather than before because it is seated beyond them: a
            // bench judged against a kerb it was meant to stand behind is a bench refused. A
            // catalog with nothing tagged road/furniture in it takes no draw here and adds no
            // object, exactly as an empty kerb folder does.
            anchored.AddRange(
                RoadFurniture.Place(doc, layout, terrain, catalog, roads, structures, anchored));

            CoverPlacer.Place(doc, layout, terrain, catalog, structures, anchored, roads);

            return doc;
        }

        /// <summary>
        /// Rebuilds the ground a document was generated on: pads, roads and all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A heightfield is a function of the parameters and the features graded into it, so the
        /// document does not carry a grid of floats — it carries a rectangle and a height on each
        /// object that has one, and this replays them in document order. That is what lets the
        /// realiser put the same ground under a saved map that the generator placed it on.
        /// </para>
        /// <para>
        /// <strong>The roads are replayed too, and the order is the pipeline's order.</strong> A
        /// network is not in the document either; it is rebuilt from the parameters, the ground and
        /// the placements, exactly as <see cref="Generate"/> built it — which means over the ground
        /// as the pads left it, before a metre of road was graded into it. Rebuilding it over the
        /// finished ground instead would route it along the roads it laid last time, which is a
        /// different network every time it is asked for.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static TerrainField Terrain(WorldDoc doc) => Terrain(doc, out _);

        /// <summary>
        /// Rebuilds the ground a document was generated on, and hands back the network that was
        /// graded into it.
        /// </summary>
        /// <remarks>
        /// The network comes out of the same call rather than out of a second one because rebuilding
        /// it is the expensive half of rebuilding the ground, and because a network built over the
        /// finished ground would be a different network — see the remarks on
        /// <see cref="Terrain(WorldDoc)"/>. A caller that wants to paint the carriageways into a
        /// splat map needs exactly the network the heights came from.
        /// </remarks>
        /// <param name="doc">The document to rebuild the ground of.</param>
        /// <param name="roads">The network laid over that ground, empty if the map has no roads.</param>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static TerrainField Terrain(WorldDoc doc, out RoadNetwork roads)
        {
            TerrainField terrain = TerrainBeforeRoads(doc);

            roads = RoadNetwork.Build(
                doc.Parameters,
                ArenaLayout.Build(doc.Parameters),
                terrain,
                doc.GeneratedObjects);

            roads.GradeInto(terrain);
            return terrain;
        }

        /// <summary>
        /// Rebuilds the ground the roads were laid over: the pads and nothing else.
        /// </summary>
        /// <remarks>
        /// What <see cref="RoadNetwork.Build"/> has to be handed, and what anything measuring the
        /// ground a route was chosen against has to measure. The finished ground is flat along every
        /// carriageway by construction, so a gradient asked of it says only that the grading worked;
        /// asked of this it says the router laid a road a vehicle could take.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static TerrainField TerrainBeforeRoads(WorldDoc doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            TerrainField terrain = TerrainField.Build(doc.Parameters);

            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                IReadOnlyDictionary<string, string> metadata = doc.GeneratedObjects[i].Metadata;
                if (!metadata.TryGetValue(FoundationKey, out string pad) ||
                    !metadata.TryGetValue(FoundationHeightKey, out string height))
                {
                    continue;
                }

                Rect2 area = RectMetadata.Parse(pad);
                float level = float.Parse(height, NumberStyles.Float, CultureInfo.InvariantCulture);

                if (metadata.TryGetValue(FoundationRadiusKey, out string radius))
                {
                    terrain.AddFoundation(
                        area.Center,
                        float.Parse(radius, NumberStyles.Float, CultureInfo.InvariantCulture),
                        BuildingGenerator.FoundationApron,
                        level);

                    continue;
                }

                terrain.AddFoundation(area, BuildingGenerator.FoundationApron, level);
            }

            return terrain;
        }

        /// <remarks>
        /// <para>
        /// The spawn pads are graded before anything else is placed, so the two ends of the map
        /// are flat whatever the ground does in between. That is worth having on its own — a spawn
        /// on a slope gives one team a look down it — and it also means a marker's height is
        /// settled before a structure's apron could come near it.
        /// </para>
        /// <para>
        /// <strong>A disc round the marker, not the whole band.</strong> A spawn area runs the full
        /// width of the map, and holding all of it flat levelled a strip clean across the arena at
        /// both ends — the two ends of the map were flat, and so was a fifth of everything between
        /// them. The pad is now the circle inscribed in the band, centred on the marker, so the
        /// spawn itself is a level base and the lane in front of it keeps the relief the amplitude
        /// asked for. See <see cref="SpawnPadShare"/>.
        /// </para>
        /// </remarks>
        static void EmitSpawnMarkers(
            WorldDoc doc, ArenaLayout layout, TerrainField terrain, Catalog catalog)
        {
            CatalogEntry marker = RequireEntry(catalog, SpawnMarkerTag);

            // Each spawn faces its opposite number: the low-end marker looks up the long axis, the
            // high-end one looks back down it.
            int facingUp = layout.LanesRunAlongZ ? 0 : 1;

            doc.GeneratedObjects.Add(SpawnMarker(marker, "a", layout.SpawnAreaA, facingUp, terrain));
            doc.GeneratedObjects.Add(SpawnMarker(marker, "b", layout.SpawnAreaB, facingUp + 2, terrain));
        }

        static PlacedObject SpawnMarker(
            CatalogEntry entry, string team, Rect2 area, int quarterTurns, TerrainField terrain)
        {
            float radius = SpawnPadRadius(area);
            Rect2 pad = SpawnPad(area);

            // Measured over the disc's own square rather than over the whole band: what the pad is
            // held at should be the cut and fill of the ground it actually levels, and the band
            // reaches ground fifty metres away that the pad never touches.
            float height = terrain.MeanHeightOn(pad);
            terrain.AddFoundation(area.Center, radius, BuildingGenerator.FoundationApron, height);

            return new PlacedObject(
                $"map/spawn_{team}/marker",
                entry.LogicalId,
                new Pose(area.Center.ToVec3(height), QuarterTurn.Rotation(quarterTurns), 1f),
                ToArray(entry.Tags),
                new Dictionary<string, string>
                {
                    { TeamKey, team },
                    { SpawnAreaKey, RectMetadata.Format(area) },
                    { FoundationKey, RectMetadata.Format(pad) },
                    { FoundationHeightKey, height.ToString("R", CultureInfo.InvariantCulture) },
                    { FoundationRadiusKey, radius.ToString("R", CultureInfo.InvariantCulture) },
                });
        }

        /// <summary>How far out of a spawn's marker the ground is held dead flat, in metres.</summary>
        public static float SpawnPadRadius(Rect2 area) =>
            MathF.Min(area.Width, area.Depth) * SpawnPadShare;

        /// <summary>The square around a spawn's graded disc.</summary>
        public static Rect2 SpawnPad(Rect2 area)
        {
            float radius = SpawnPadRadius(area);
            Vec2 centre = area.Center;

            return new Rect2(
                centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);
        }

        /// <summary>
        /// The structures placed, with their world footprints, for the stages that place around
        /// them.
        /// </summary>
        /// <remarks>
        /// The counts are a demand rather than an attempt — see <see cref="RequiredHouses"/> — so
        /// this returns a map carrying everything the composition rule asked for, or throws saying
        /// what would not fit. A map that quietly came out with one structure on it would satisfy
        /// every property the suites assert and still be an empty field.
        /// </remarks>
        static List<MapStructure> EmitStructures(
            WorldDoc doc, ArenaLayout layout, TerrainField terrain, Catalog catalog, ArenaParams parameters)
        {
            Rng stream = new Rng(parameters.Seed).Fork("structures");

            float betweenSpawnsMin = layout.AlongOf(layout.SpawnAreaA.Max) + SpawnClearance;
            float betweenSpawnsMax = layout.AlongOf(layout.SpawnAreaB.Min) - SpawnClearance;

            List<LaneSpan> cells = Cells(layout, betweenSpawnsMin, betweenSpawnsMax);
            var committed = new List<MapStructure>(cells.Count);
            Rect2 buildable = Buildable(layout, catalog);

            // The anchor first, out of the cell nearest the middle of the map, and demanded: a map
            // that came back with nothing standing on it would satisfy every property the suites
            // assert and would still be a field.
            int anchor = AnchorCell(layout, cells);
            ArenaLane middle = layout.Lanes[layout.MiddleLaneIndex];
            float centre = layout.AlongOf(layout.Playfield.Center);
            float reach = layout.LongExtent * BuildingCentreSpan;

            CatalogEntry building = SelectStructure(
                catalog, TagQuery.All(BuildingTag), BuildingTag, cells[anchor], parameters, ref stream);
            doc.GeneratedObjects.Add(RequireStructure(
                layout, buildable, terrain, building, BuildingTag,
                BuildingSpans(
                    layout, middle, cells[anchor], centre - reach, centre + reach,
                    betweenSpawnsMin, betweenSpawnsMax),
                committed, ref stream));

            // Then every other cell, in an order the stream shuffles. Shuffled rather than walked
            // lane by lane because the cell that fails is the one whose neighbour filled up first,
            // and walking in order would spend every failure along the same edge of the map.
            List<LaneSpan> rest = Remaining(cells, anchor, ref stream);
            for (int i = 0; i < rest.Count; i++)
            {
                CatalogEntry entry = SelectStructure(
                    catalog, Fillable, StructureTag, rest[i], parameters, ref stream);
                if (TryPlaceStructure(layout, buildable, terrain, rest[i], entry, committed,
                        ref stream, out PlacedObject placed))
                {
                    doc.GeneratedObjects.Add(placed);
                }
            }

            return committed;
        }

        /// <summary>
        /// The ground a structure may stand on: the playfield, less the strip the boundary fence
        /// stands on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A structure standing on the edge of the map used to be the boundary along its
        /// own wall</strong>, and that reading held while boundary art was a couple of metres long.
        /// It does not hold at the length real stone comes in. A thirty-metre panel needs thirty
        /// clear metres: a ten-metre building standing flush against a seventy-five-metre edge
        /// leaves seventeen metres on one side of itself and seventeen on the other, neither of
        /// which will take a panel, so ten metres of wall costs forty-five metres of open level.
        /// It is not a gap anybody would design and it is not one the run can close.
        /// </para>
        /// <para>
        /// So the strip is reserved instead, and it costs the thickness of one panel of the
        /// playfield's width — a third of a metre on the sample art, half a metre on the pack.
        /// A structure held that far in has the fence tiling behind it, flush against its wall, and
        /// the edge of the map closes all the way round.
        /// </para>
        /// <para>
        /// Measured off the catalog rather than typed in, and zero when the catalog has no stone in
        /// it: a workspace that has never filled <c>Props/fence/StoneFence</c> gets no boundary, so
        /// there is no strip to keep clear and its structures stand exactly where they always did.
        /// </para>
        /// </remarks>
        static Rect2 Buildable(ArenaLayout layout, Catalog catalog)
        {
            float reserved = WallRun.ThickestSegment(
                WallRun.Tileable(catalog.Query(TagQuery.All(PerimeterFence.StoneFenceTag))));

            return reserved > 0f ? layout.Playfield.Expanded(-reserved) : layout.Playfield;
        }

        /// <summary>
        /// The layout cells a map's ground divides into, lane by lane and low end first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ground that counts is the run between the two spawn areas, across the lane bands —
        /// the gaps between the bands are the routes past the structures, and a spawn is not ground
        /// anything may stand on. Each band is cut into as many cells of about
        /// <see cref="MetresPerStructure"/> as it holds, on both axes, so a wide band on a large map
        /// carries rows of structures across it rather than one row down the middle of a hundred
        /// metres of grass.
        /// </para>
        /// <para>
        /// Rounded rather than floored, and never below one. Flooring would drop the remainder of
        /// every band — a band one and a half cells wide would carry one row and waste the other
        /// half — and the floor of one is what keeps a lane on a small map from being skipped
        /// altogether. The default sixty-metre map comes out at one cell per lane either way, which
        /// is the composition that map has always had.
        /// </para>
        /// </remarks>
        static List<LaneSpan> Cells(ArenaLayout layout, float alongMin, float alongMax)
        {
            float side = MathF.Sqrt(MetresPerStructure);
            float run = alongMax - alongMin;
            int along = Slices(run, side);

            var cells = new List<LaneSpan>(layout.Lanes.Count * along);

            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                ArenaLane lane = layout.Lanes[i];
                float bandMin = layout.CrossOf(lane.Band.Min);
                float bandMax = layout.CrossOf(lane.Band.Max);
                int across = Slices(bandMax - bandMin, side);

                for (int a = 0; a < along; a++)
                {
                    for (int c = 0; c < across; c++)
                    {
                        cells.Add(new LaneSpan(
                            lane,
                            alongMin + run * (a / (float)along),
                            alongMin + run * ((a + 1) / (float)along),
                            bandMin + (bandMax - bandMin) * (c / (float)across),
                            bandMin + (bandMax - bandMin) * ((c + 1) / (float)across)));
                    }
                }
            }

            return cells;
        }

        /// <summary>How many cells of about <paramref name="side"/> metres a length divides into.</summary>
        static int Slices(float length, float side) =>
            Math.Max(1, (int)MathF.Round(length / side, MidpointRounding.AwayFromZero));

        /// <summary>
        /// How many layout cells a map of these parameters has, which is the most structures it can
        /// hold.
        /// </summary>
        /// <remarks>
        /// The ceiling rather than the count: a cell no structure will stand in stays empty, and how
        /// many of those there are depends on the catalog and the seed. It is public because it is
        /// the half of the density rule that <em>is</em> a function of the parameters alone, and a
        /// caller asking whether a map will come out a courtyard or a town is asking about this.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        public static int StructureCells(ArenaParams parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            ArenaLayout layout = ArenaLayout.Build(parameters);
            return Cells(
                layout,
                layout.AlongOf(layout.SpawnAreaA.Max) + SpawnClearance,
                layout.AlongOf(layout.SpawnAreaB.Min) - SpawnClearance).Count;
        }

        /// <summary>
        /// Which cell the map's anchor is tried in: the middle lane's, nearest the centre of the
        /// map.
        /// </summary>
        /// <remarks>
        /// Nearest along the long axis alone, because that is the axis the anchor is meant to sit in
        /// the middle of — naming the middle lane has already settled the other one. Ties break to
        /// the lower index, so a lane holding an even number of cells picks the same one of the two
        /// middle cells every time.
        /// </remarks>
        static int AnchorCell(ArenaLayout layout, List<LaneSpan> cells)
        {
            string middle = layout.Lanes[layout.MiddleLaneIndex].Id;
            float centre = layout.AlongOf(layout.Playfield.Center);

            int best = 0;
            float nearest = float.MaxValue;

            for (int i = 0; i < cells.Count; i++)
            {
                if (!string.Equals(cells[i].Lane.Id, middle, StringComparison.Ordinal))
                {
                    continue;
                }

                float away = MathF.Abs((cells[i].AlongMin + cells[i].AlongMax) * 0.5f - centre);
                if (away < nearest)
                {
                    nearest = away;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Every cell but the anchor, in the order the stream wants them tried.</summary>
        /// <remarks>Fisher-Yates from the caller's stream, in place and walking downward.</remarks>
        static List<LaneSpan> Remaining(List<LaneSpan> cells, int anchor, ref Rng rng)
        {
            var rest = new List<LaneSpan>(cells.Count - 1);
            for (int i = 0; i < cells.Count; i++)
            {
                if (i != anchor)
                {
                    rest.Add(cells[i]);
                }
            }

            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                LaneSpan swap = rest[i];
                rest[i] = rest[j];
                rest[j] = swap;
            }

            return rest;
        }

        /// <summary>
        /// Where the building is tried: its own narrow window on the centre of the map first, then
        /// the whole run between the spawns, then every other lane.
        /// </summary>
        /// <remarks>
        /// The first span is what a map wants — a strong point in the contested middle — and the
        /// two after it are what "at least one building" means on a map with no room for what it
        /// wants. Widening the window before giving the lane up is the right order round: a
        /// building a few metres off centre still anchors the middle lane, and one in a flank does
        /// not.
        /// </remarks>
        static List<LaneSpan> BuildingSpans(
            ArenaLayout layout,
            ArenaLane middle,
            LaneSpan anchor,
            float centreMin,
            float centreMax,
            float betweenSpawnsMin,
            float betweenSpawnsMax)
        {
            var spans = new List<LaneSpan>(layout.Lanes.Count + 1)
            {
                // The anchor cell, narrowed to the window on the centre of the map. On the default
                // map the cell is the whole middle band and this is the window alone, which is what
                // it has always been.
                anchor.Between(
                    MathF.Max(anchor.AlongMin, centreMin), MathF.Min(anchor.AlongMax, centreMax)),
                Whole(layout, middle, betweenSpawnsMin, betweenSpawnsMax),
            };

            AddRemainingLanes(spans, layout, betweenSpawnsMin, betweenSpawnsMax);
            return spans;
        }

        /// <summary>A whole lane band, over the run between the spawns.</summary>
        static LaneSpan Whole(
            ArenaLayout layout, ArenaLane lane, float betweenSpawnsMin, float betweenSpawnsMax) =>
            new LaneSpan(
                lane, betweenSpawnsMin, betweenSpawnsMax,
                layout.CrossOf(lane.Band.Min), layout.CrossOf(lane.Band.Max));

        /// <summary>
        /// Adds every lane the list does not already name, over the whole run between the spawns.
        /// </summary>
        /// <remarks>
        /// This is the fallback that turns a count into a guarantee. A composition rule that gave
        /// up as soon as one lane was full would be a suggestion, and the lane a structure was
        /// assigned is the narrowest of the places it could legally stand.
        /// </remarks>
        static void AddRemainingLanes(
            List<LaneSpan> spans, ArenaLayout layout, float alongMin, float alongMax)
        {
            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                ArenaLane lane = layout.Lanes[i];
                bool named = false;

                for (int j = 0; j < spans.Count && !named; j++)
                {
                    named = string.Equals(spans[j].Lane.Id, lane.Id, StringComparison.Ordinal);
                }

                if (!named)
                {
                    spans.Add(Whole(layout, lane, alongMin, alongMax));
                }
            }
        }

        /// <summary>The art a cell that is not the anchor may be filled from.</summary>
        /// <remarks>
        /// Buildings and houses, and not everything tagged <see cref="StructureTag"/>. That tag is
        /// carried by every piece of structural art in a workspace — wall panels, floor tiles, door
        /// frames, a flight of stairs — because they are the pieces a <em>building</em> is built
        /// out of, and a map that asked for "a structure" by that tag alone would stand a two-metre
        /// wall panel on a lane and call it a house.
        /// </remarks>
        static TagQuery Fillable =>
            TagQuery.All(StructureTag).WithAny(BuildingTag, HouseTag);

        /// <summary>
        /// Chooses which piece of art fills a structure slot: a weighted pick over the entries that
        /// fit the cell's size budget, or the smallest entry when none of them do.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what <see cref="ArenaParams.StructureDensity"/> controls. A density of 1 lets a
        /// structure and its yard cover a quarter of its cell; halving it restricts the generator to
        /// entries half that size, so a sparser map is a map of smaller buildings rather than a map
        /// with the same building scaled down.
        /// </para>
        /// <para>
        /// <strong>The yard counts against the budget, not the walls alone.</strong> A structure
        /// takes the ground it stands on and the <see cref="ExteriorPlacer.YardMargin"/> round it
        /// that gets fenced, and measuring only the footprint let a two-storey building into a cell
        /// that could hold its walls and nothing else — which on the default sixty-metre map is a
        /// third of a lane given to one building, three times over. The floor that was left came out
        /// measurably worse covered: the 1000-seed sweep in <c>MapValidationTests</c> put the worst
        /// seed at 0.56 against a threshold of 0.6. Measured with the yard, a sixty-metre flank cell
        /// affords the small house and not the building, which is the composition that map has
        /// always had, and a cell on a large map affords either.
        /// </para>
        /// <para>
        /// <strong>That is also what decides large against small.</strong> Every cell but the anchor
        /// draws from <see cref="Fillable"/> rather than asking for a building or a house by name,
        /// so the two size classes come out of one list and the budget is what separates them. A
        /// cell with room for neither takes the smallest structure the catalog has, because a cell
        /// is ground that is meant to be built on.
        /// </para>
        /// </remarks>
        static CatalogEntry SelectStructure(
            Catalog catalog,
            TagQuery query,
            string named,
            LaneSpan cell,
            ArenaParams parameters,
            ref Rng rng)
        {
            IReadOnlyList<CatalogEntry> matches = RequireEntries(catalog, query, named);
            float budget = parameters.StructureDensity * BaselineLaneShare * cell.Area;

            var affordable = new List<CatalogEntry>(matches.Count);
            CatalogEntry smallest = matches[0];
            for (int i = 0; i < matches.Count; i++)
            {
                if (Grounds(matches[i]) <= budget)
                {
                    affordable.Add(matches[i]);
                }

                if (Grounds(matches[i]) < Grounds(smallest))
                {
                    smallest = matches[i];
                }
            }

            if (affordable.Count == 0)
            {
                // Still one weighted pick, over a one-entry list, so the stream advances the same
                // way whether or not the budget bit.
                affordable.Add(smallest);
            }

            return rng.WeightedPick(affordable, e => e.Weight);
        }

        /// <summary>How much ground a structure takes: its own footprint and the yard round it.</summary>
        static float Grounds(CatalogEntry entry) =>
            entry.Footprint.Expanded(ExteriorPlacer.YardMargin).Area;

        /// <summary>
        /// Places one structure at the first of <paramref name="spans"/> that will hold it.
        /// </summary>
        /// <exception cref="InvalidOperationException">None of them will.</exception>
        static PlacedObject RequireStructure(
            ArenaLayout layout,
            Rect2 buildable,
            TerrainField terrain,
            CatalogEntry entry,
            string tag,
            IReadOnlyList<LaneSpan> spans,
            List<MapStructure> committed,
            ref Rng rng)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                if (TryPlaceStructure(layout, buildable, terrain, spans[i], entry, committed, ref rng,
                        out PlacedObject placed))
                {
                    return placed;
                }
            }

            throw new InvalidOperationException(
                $"This map must hold a structure tagged '{tag}' to anchor it, and a " +
                $"{Metres(layout.Playfield.Width)} by {Metres(layout.Playfield.Depth)} metre " +
                $"playfield is {Metres(layout.Playfield.Area)} square metres of ground. " +
                $"'{entry.LogicalId}' does not fit in any of the {spans.Count} lane spans tried. " +
                "Either the playfield is too small for this catalog or the lane count is too high.");
        }

        /// <summary>
        /// Tries to place a structure somewhere in one lane span, and reports whether it did.
        /// </summary>
        static bool TryPlaceStructure(
            ArenaLayout layout,
            Rect2 buildable,
            TerrainField terrain,
            LaneSpan span,
            CatalogEntry entry,
            List<MapStructure> committed,
            ref Rng rng,
            out PlacedObject placed)
        {
            ArenaLane lane = span.Lane;

            // The middle of the cell across the lane, which on a map with one cell to a band is the
            // middle of the band — where a structure has always sat.
            float cross = layout.SnapCross((span.CrossMin + span.CrossMax) * 0.5f);

            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                int quarterTurns = rng.NextRange(0, QuarterTurn.Count);
                Rect2 local = QuarterTurn.Rotate(entry.Footprint, quarterTurns);

                float low = span.AlongMin - layout.AlongOf(local.Min);
                float high = span.AlongMax - layout.AlongOf(local.Max);
                if (high < low)
                {
                    // This orientation is longer than the span allows. Draw anyway so the stream
                    // advances identically whichever orientation came up, then reject.
                    rng.NextFloat();
                    continue;
                }

                float along = layout.SnapAlong(rng.NextRange(low, high));
                if (TryCommit(layout, buildable, terrain, lane, entry, along, cross, quarterTurns,
                        committed, out placed))
                {
                    return true;
                }
            }

            // Every draw was rejected. Sweep the span instead, so a tight map produces a structure
            // in the one place it fits rather than failing because the draws missed it.
            for (int quarterTurns = 0; quarterTurns < QuarterTurn.Count; quarterTurns++)
            {
                Rect2 local = QuarterTurn.Rotate(entry.Footprint, quarterTurns);
                float low = layout.SnapAlong(span.AlongMin - layout.AlongOf(local.Min));
                float high = span.AlongMax - layout.AlongOf(local.Max);
                int steps = (int)MathF.Floor((high - low) / layout.Grid.CellSize);

                // Stepped from the start rather than accumulated, so a fractional cell size cannot
                // drift the sweep off the grid.
                for (int step = 0; step <= steps; step++)
                {
                    float along = low + step * layout.Grid.CellSize;
                    if (TryCommit(layout, buildable, terrain, lane, entry, along, cross, quarterTurns,
                            committed, out placed))
                    {
                        return true;
                    }
                }
            }

            placed = null;
            return false;
        }

        static bool TryCommit(
            ArenaLayout layout,
            Rect2 buildable,
            TerrainField terrain,
            ArenaLane lane,
            CatalogEntry entry,
            float along,
            float cross,
            int quarterTurns,
            List<MapStructure> committed,
            out PlacedObject placed)
        {
            placed = null;

            Vec2 position = layout.ToWorld(along, cross);
            Placement candidate = Placement.AtQuarterTurn(entry, position, quarterTurns);
            Rect2 world = candidate.Footprint;

            if (!buildable.Contains(world))
            {
                return false;
            }

            Rect2 clearOfSpawns = world.Expanded(SpawnClearance);
            if (clearOfSpawns.Overlaps(layout.SpawnAreaA) || clearOfSpawns.Overlaps(layout.SpawnAreaB))
            {
                return false;
            }

            for (int i = 0; i < committed.Count; i++)
            {
                if (world.Expanded(StructureClearance).Overlaps(committed[i].Footprint))
                {
                    return false;
                }
            }

            // Where this structure would be walked into, worked out before the placement is
            // accepted rather than written after it, because whether the doors have anywhere to
            // open is part of whether the structure may stand here at all. See DoorsCanBeReached.
            List<Rect2> doorways = Doorways(layout, entry, position, quarterTurns, world);
            if (!DoorsCanBeReached(buildable, doorways))
            {
                return false;
            }

            int slot = 0;
            for (int i = 0; i < committed.Count; i++)
            {
                if (string.Equals(committed[i].LaneId, lane.Id, StringComparison.Ordinal))
                {
                    slot++;
                }
            }

            // The foundation is graded only once the placement is accepted: a candidate that no
            // rule wanted must leave no trace in the ground, or the terrain would depend on how
            // many positions the draws happened to try.
            float foundation = BuildingGenerator.LevelFoundation(terrain, world);

            var metadata = new Dictionary<string, string>
            {
                { LaneKey, lane.Id },
                { FoundationKey, RectMetadata.Format(BuildingGenerator.FoundationPad(world)) },
                { FoundationHeightKey, foundation.ToString("R", CultureInfo.InvariantCulture) },
            };
            WriteDoorways(metadata, doorways);

            placed = new PlacedObject(
                $"map/{lane.Id}/structure_{slot.ToString("00", CultureInfo.InvariantCulture)}",
                candidate.LogicalId,
                candidate.Pose.WithPosition(candidate.Pose.Position + new Vec3(0f, foundation, 0f)),
                ToArray(candidate.Tags),
                metadata);

            committed.Add(new MapStructure(placed, candidate));
            return true;
        }

        /// <summary>
        /// True when every doorway has the ground in front of it to be walked through.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A door needs somewhere to open onto, and the edge of the map is not it.</strong>
        /// <paramref name="buildable"/> is the playfield less the strip the boundary fence stands on
        /// — see <see cref="Buildable"/> — so requiring a doorway's approach to fit inside it is one
        /// rule saying two things: the approach does not run off the map, and it does not run into
        /// the fence round it.
        /// </para>
        /// <para>
        /// The second half is why this exists. The boundary is the one stage in the tool with no
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> rule, and that is deliberate: a hole in a
        /// hedge is a feature and a hole in the edge of the world is a way out of the level, so the
        /// fence may not be asked to step aside for a door. Something has to give, and it is the
        /// building: a structure whose front door faces the boundary a metre away is a structure in
        /// the wrong place, not a fence in the wrong place, because the door is unusable at that
        /// range whether or not anything is standing in front of it. Four quarter turns are tried at
        /// every position, so the usual outcome is a building that turns its doors along the lane
        /// rather than a cell left empty.
        /// </para>
        /// <para>
        /// The clearance is <see cref="CoverPlacer.DoorwayClearance"/>, which is the same ground
        /// cover and the exterior dressing already keep in front of a doorway. A door that three
        /// stages hold different amounts of floor in front of would be three opinions about one
        /// measurement.
        /// </para>
        /// </remarks>
        static bool DoorsCanBeReached(Rect2 buildable, List<Rect2> doorways)
        {
            for (int i = 0; i < doorways.Count; i++)
            {
                if (!buildable.Contains(doorways[i].Expanded(CoverPlacer.DoorwayClearance)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Records where a structure can be entered, as rectangles in world space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>What the art says, where the art says anything.</strong> A doorway used to be
        /// derived from the footprint alone, on the argument that the catalog describes art and a
        /// doorway is a fact about where a structure sits — which is true of a box with no door
        /// modelled into it and false of everything else. A prefab with a way in has that way in at
        /// a particular place, and a rectangle guessed off the middle of the face is a rectangle
        /// the cover stage keeps clear of a blank wall while it stacks crates against the door. So
        /// a row that declares its openings — through a marker in the prefab, or because it was
        /// exported from a generated building — is believed, and the rectangles are carried through
        /// the same quarter turn and translation the footprint is.
        /// </para>
        /// <para>
        /// <strong>Two of them, and only ever two.</strong> See
        /// <see cref="DoorwaysPerStructure"/>. Where a row declares more, the pair furthest apart is
        /// kept: doors at opposite ends of a building are what make it a route, and a third one
        /// beside either of them only shortens the way round. Where a row declares one, the face
        /// further from it supplies the second. Where a row declares none, both come off the
        /// footprint as they always did — on the two faces across the lane, so the structure can be
        /// run through along the route it blocks.
        /// </para>
        /// <para>
        /// Cover placement and the exterior dressing read these back and keep them clear, and the
        /// validator walks to them.
        /// </para>
        /// </remarks>
        static List<Rect2> Doorways(
            ArenaLayout layout,
            CatalogEntry entry,
            Vec2 position,
            int quarterTurns,
            Rect2 world)
        {
            List<Rect2> doorways = Declared(entry, position, quarterTurns);

            if (doorways.Count > DoorwaysPerStructure)
            {
                KeepFurthestApart(doorways);
            }
            else if (doorways.Count < DoorwaysPerStructure)
            {
                TopUpFromFootprint(doorways, layout, world);
            }

            return doorways;
        }

        /// <summary>Writes a structure's doorways into its metadata, in the order they were made.</summary>
        /// <remarks>
        /// Separate from working them out, because they are worked out before the placement is
        /// accepted — <see cref="DoorsCanBeReached"/> is one of the rules — and written only once it
        /// is. A candidate no rule wanted leaves no trace, here as in the terrain.
        /// </remarks>
        static void WriteDoorways(IDictionary<string, string> metadata, List<Rect2> doorways)
        {
            metadata[DoorwayCountKey] = doorways.Count.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < doorways.Count; i++)
            {
                metadata[DoorwayKeyPrefix + i.ToString("00", CultureInfo.InvariantCulture)] =
                    RectMetadata.Format(doorways[i]);
            }
        }

        /// <summary>
        /// The doorways a catalog entry declares, placed where this structure stands.
        /// </summary>
        /// <remarks>
        /// The same transform <see cref="Placement.AtQuarterTurn"/> puts the footprint through,
        /// because they are the same statement: both are rectangles in the entry's own space, and a
        /// door that did not turn with the wall it is in would be a door in a different wall.
        /// </remarks>
        static List<Rect2> Declared(CatalogEntry entry, Vec2 position, int quarterTurns)
        {
            var doorways = new List<Rect2>(entry.Doorways.Count);
            for (int i = 0; i < entry.Doorways.Count; i++)
            {
                doorways.Add(QuarterTurn.Rotate(entry.Doorways[i], quarterTurns).Translated(position));
            }

            return doorways;
        }

        /// <summary>
        /// Reduces a list of declared doorways to the two furthest apart, keeping document order.
        /// </summary>
        /// <remarks>
        /// Every pair, which is a handful of comparisons on a list that is a handful long. The
        /// order the two survivors are written in is the order they were declared in, so a row that
        /// is re-measured but not re-authored produces the same document.
        /// </remarks>
        static void KeepFurthestApart(List<Rect2> doorways)
        {
            int bestA = 0;
            int bestB = 1;
            float furthest = -1f;

            for (int i = 0; i < doorways.Count; i++)
            {
                for (int j = i + 1; j < doorways.Count; j++)
                {
                    float apart = SquaredDistance(doorways[i].Center, doorways[j].Center);
                    if (apart > furthest)
                    {
                        furthest = apart;
                        bestA = i;
                        bestB = j;
                    }
                }
            }

            Rect2 a = doorways[bestA];
            Rect2 b = doorways[bestB];
            doorways.Clear();
            doorways.Add(a);
            doorways.Add(b);
        }

        /// <summary>
        /// Fills a short list of doorways out of the structure's footprint, furthest face first.
        /// </summary>
        /// <remarks>
        /// The two faces across the lane, as the derived pair has always been. A row that declared
        /// one door takes whichever of them is further from it, so the pair still spans the
        /// structure rather than doubling up on one end of it.
        /// </remarks>
        static void TopUpFromFootprint(List<Rect2> doorways, ArenaLayout layout, Rect2 world)
        {
            float face = layout.LanesRunAlongZ ? world.Width : world.Depth;
            float width = MathF.Min(DoorwayWidth, face * DoorwayShareOfFace);
            float cross = layout.CrossOf(world.Center);

            Rect2 low = Doorway(layout, layout.AlongOf(world.Min), cross, width);
            Rect2 high = Doorway(layout, layout.AlongOf(world.Max), cross, width);

            if (doorways.Count == 0)
            {
                doorways.Add(low);
                doorways.Add(high);
                return;
            }

            Vec2 declared = doorways[0].Center;
            doorways.Add(
                SquaredDistance(declared, low.Center) >= SquaredDistance(declared, high.Center)
                    ? low
                    : high);
        }

        static float SquaredDistance(Vec2 from, Vec2 to)
        {
            float x = to.X - from.X;
            float z = to.Y - from.Y;
            return x * x + z * z;
        }

        static Rect2 Doorway(ArenaLayout layout, float along, float cross, float width) => Rect2.FromCorners(
            layout.ToWorld(along - DoorwayDepth * 0.5f, cross - width * 0.5f),
            layout.ToWorld(along + DoorwayDepth * 0.5f, cross + width * 0.5f));

        /// <summary>
        /// Reads back every doorway rectangle the document's objects declare, in document order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Beside <see cref="WriteDoorways"/>, which is what writes them. Both later stages need
        /// these and both would otherwise have parsed the same keys their own way, which is two
        /// readers of one format and one of them eventually reading it wrong.
        /// </para>
        /// <para>
        /// Through the document rather than passed along from the structure stage on purpose: the
        /// declared metadata is the contract, and it is what the validator and the editor overlay
        /// read too. A key that is missing or unreadable is a structure with fewer doorways rather
        /// than an exception — a hand-edited document is still a document.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
        public static List<Rect2> Doorways(WorldDoc doc)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            var doorways = new List<Rect2>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!placed.Metadata.TryGetValue(DoorwayCountKey, out string countText) ||
                    !int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    continue;
                }

                for (int d = 0; d < count; d++)
                {
                    string key = DoorwayKeyPrefix + d.ToString("00", CultureInfo.InvariantCulture);
                    if (placed.Metadata.TryGetValue(key, out string rect))
                    {
                        doorways.Add(RectMetadata.Parse(rect));
                    }
                }
            }

            return doorways;
        }

        static CatalogEntry RequireEntry(Catalog catalog, string tag) =>
            RequireEntries(catalog, TagQuery.All(tag), tag)[0];

        /// <summary>
        /// The entries a query matches, or an exception naming what the catalog is missing.
        /// </summary>
        /// <remarks>
        /// <paramref name="named"/> is the tag the message blames, which is the query's own subject
        /// rather than every tag it mentions: a workspace with no <c>structure/building</c> in it
        /// needs to be told that, not handed the three-tag query that went looking.
        /// </remarks>
        static IReadOnlyList<CatalogEntry> RequireEntries(
            Catalog catalog, TagQuery query, string named)
        {
            IReadOnlyList<CatalogEntry> matches = catalog.Query(query);
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The catalog has no entry tagged '{named}', so this map cannot be generated.");
            }

            return matches;
        }

        static string[] ToArray(IReadOnlyList<string> values)
        {
            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }

        /// <summary>A measurement as it reads in an error message.</summary>
        static string Metres(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// One place a structure may be tried: a lane, and how far along the map it may sit in it.
        /// </summary>
        /// <remarks>
        /// A structure is offered a list of these rather than one span, because the composition
        /// rule is a demand and a demand needs somewhere to fall back to. The first entry is where
        /// the structure belongs; the rest are where it stands instead when that will not hold it.
        /// </remarks>
        readonly struct LaneSpan
        {
            public LaneSpan(ArenaLane lane, float alongMin, float alongMax, float crossMin, float crossMax)
            {
                Lane = lane;
                AlongMin = alongMin;
                AlongMax = alongMax;
                CrossMin = crossMin;
                CrossMax = crossMax;
            }

            /// <summary>The lane the structure would stand in.</summary>
            public ArenaLane Lane { get; }

            /// <summary>The low end of the run along the map the structure may sit in.</summary>
            public float AlongMin { get; }

            /// <summary>The high end of it.</summary>
            public float AlongMax { get; }

            /// <summary>The low edge of the strip across the lane the structure is centred in.</summary>
            public float CrossMin { get; }

            /// <summary>The high edge of it.</summary>
            public float CrossMax { get; }

            /// <summary>How much ground the span covers, which is a structure's size budget.</summary>
            public float Area => (AlongMax - AlongMin) * (CrossMax - CrossMin);

            /// <summary>The same span over a shorter run along the map.</summary>
            public LaneSpan Between(float alongMin, float alongMax) =>
                new LaneSpan(Lane, alongMin, alongMax, CrossMin, CrossMax);
        }
    }
}
