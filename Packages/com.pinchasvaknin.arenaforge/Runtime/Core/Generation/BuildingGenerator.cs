using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Turns parameters and a catalog into a multi-storey building: a shell of floor slabs and
    /// walls over a partitioned plan, and each room inside it filled from the catalog under the
    /// same constraint set a lane is filled under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four things are interesting here, and placement is not one of them — that is
    /// <see cref="CoverPlacer"/>'s shape over a smaller rectangle.
    /// </para>
    /// <para>
    /// The first is where the randomness lives. Every floor draws from its own seed, stored in the
    /// document. Generating a building for the first time derives those seeds by forking the
    /// building seed, so a building is fully determined by its parameters; after that the stored
    /// seeds are what generation reads, and re-rolling one floor rewrites exactly one of them.
    /// Forking floor streams from the building seed at generation time would look identical on a
    /// first run and be useless on the second: there would be no value a re-roll could change
    /// except the building seed, and changing it would move every floor. It is the same argument
    /// CLAUDE.md rule 2 makes for forking per subsystem, one level down.
    /// </para>
    /// <para>
    /// The second is that a floor is a plan before it is a pile of crates. <see cref="FloorPlan"/>
    /// partitions the footprint into rooms and corridors and says where the walls go; this file
    /// turns that into objects, by asking the catalog for art tagged <see cref="FloorTileTag"/>,
    /// <see cref="WallTag"/> and <see cref="DoorwayTag"/> and tiling it along the plan's runs. No
    /// geometry is authored, exactly as ARCHITECTURE.md section 3 requires — a wall run is tiled
    /// from a wall piece at the size the catalog declares, never stretched, which is why the plan
    /// is laid out in modules of that size rather than in metres.
    /// </para>
    /// <para>
    /// A catalog with none of that art still builds: the floor comes back as one undivided room
    /// with nothing around it, which is what every catalog of crates and barriers produced before
    /// the shell existed. The shell is art the catalog either has or does not, not a feature that
    /// can be switched off.
    /// </para>
    /// <para>
    /// The third thing, added after the first shell flickered, is that a storey is a stack rather
    /// than a plane. The slab is laid at the storey's own height and everything else on the floor
    /// — walls, doorways, crates — stands on top of it, so what a piece has to fit under is the
    /// floor height less the slab's thickness. Standing all of it at the same height, as this
    /// generator first did, sinks every crate a slab-thickness into the floor and puts the top of
    /// every wall in the same plane as the top of the slab above it: two surfaces at one depth,
    /// over the whole length of every wall in the building, which is the worst flicker a
    /// generated building can have.
    /// </para>
    /// <para>
    /// The headroom is also the one measurement art is <em>fitted</em> to rather than merely
    /// selected by. It is a parameter the user typed less a thickness the catalog declared, so
    /// asking a wall to match it exactly is asking two unrelated decisions to agree — and the
    /// failure is quiet at both ends: a hair too tall and no wall art is offered at all, leaving
    /// every storey one undivided room; a hair too short and there is a strip of daylight along the
    /// top of every wall. So a wall within <see cref="HeadroomTolerance"/> of fitting is admitted
    /// and then stretched onto the ceiling — see <see cref="StretchTo"/> and
    /// <see cref="Pose.VerticalScale"/>. It is the only place in this file that rescales art, and
    /// it goes no further than the wall runs.
    /// </para>
    /// <para>
    /// The fourth is the stairwell, which is the only thing in this file that is decided above the
    /// floors rather than inside one. A shaft has to be in the same place on every storey or it is
    /// not a stairwell, and every storey is partitioned from its own stored seed — so
    /// <see cref="PlanStairwell"/> is a function of the parameters and the catalog alone, forked
    /// from the building seed, and each floor's partition works around the rectangle it returns.
    /// That leaves the per-floor re-roll exactly as it was: re-rolling a storey rearranges its
    /// rooms around the shaft rather than moving the shaft. It is also what makes a roof worth
    /// having — the top storey's slab, its parapet and the hole cut through it for the last flight
    /// are the same three ideas one level up.
    /// </para>
    /// </remarks>
    public static class BuildingGenerator
    {
        /// <summary>Metadata key naming the floor an object stands on, counting from 1.</summary>
        public const string FloorKey = "floor";

        /// <summary>Metadata key holding the elevation of an object's floor, in metres.</summary>
        public const string ElevationKey = "elevation";

        /// <summary>Metadata key naming the room a piece of content stands in.</summary>
        public const string RoomKey = "room";

        /// <summary>Value <see cref="FloorKey"/> carries on the roof, which is no storey's floor.</summary>
        public const string RoofFloor = "roof";

        /// <summary>Tag a catalog entry must carry to be laid as a floor slab.</summary>
        public const string FloorTileTag = "structure/floor";

        /// <summary>Tag a catalog entry must carry to be tiled along a wall run.</summary>
        public const string WallTag = "structure/wall";

        /// <summary>Tag a catalog entry must carry to fill the module a wall leaves open.</summary>
        public const string DoorwayTag = "structure/doorway";

        /// <summary>
        /// Tag a catalog entry must carry to be swapped into an outside wall run as a window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A window is a wall, and that is the whole of the contract: the piece has to measure what
        /// the run's <see cref="WallTag"/> art measures, because it is put into a module the run
        /// had already been tiled with rather than into space the plan set aside for it. A shorter
        /// piece is a gap in the middle of a run. It is the same bargain <see cref="DoorwayTag"/>
        /// makes, for the same reason.
        /// </para>
        /// <para>
        /// A catalog with no window art builds exactly the building it built before there was such
        /// a tag — byte for byte, not merely alike. See <see cref="SelectShell"/>.
        /// </para>
        /// </remarks>
        public const string WindowTag = "structure/window";

        /// <summary>Tag a catalog entry must carry to be stood in the stairwell.</summary>
        /// <remarks>
        /// One flight per storey, reaching from that storey's walking surface to the next one, so
        /// what a piece is measured against is the floor height rather than the headroom every
        /// other piece is held under. A flight that stopped at the ceiling would be a flight you
        /// cannot get off.
        /// </remarks>
        public const string StairsTag = "structure/stairs";

        /// <summary>Tag a catalog entry must carry to be tiled round the edge of the roof.</summary>
        public const string ParapetTag = "structure/parapet";

        /// <summary>Tag a catalog entry must carry to be placed as interior decor.</summary>
        /// <remarks>
        /// Deliberately not under <c>cover</c>. A plant is not something to fight from, and the
        /// rules it is placed under say so: decor goes against the walls and into the corners,
        /// where cover in the middle of a room is the whole point of cover.
        /// </remarks>
        public const string DecorTag = "prop/decor";

        /// <summary>The other tag interior decor may carry, spelled by the deeper workspace layout.</summary>
        /// <remarks>
        /// <para>
        /// The same art under a longer name. A workspace files corner decor in
        /// <c>Props/PropBuilding/Decor/Decoration</c>, which spells
        /// <c>propbuilding/decor/decoration</c> and not <c>prop/decor</c> — so a project whose
        /// decor was relocated into the new tree would have gone quiet under a query for the old
        /// tag alone, and the failure would have looked like art that stopped being found rather
        /// than a folder that moved.
        /// </para>
        /// <para>
        /// Both, rather than one replacing the other, because a folder move cannot be assumed to
        /// have happened: <c>Props/Decor</c> is what a project on an earlier layout still has, and
        /// it still means what it meant. The deeper name is matched at its full depth rather than
        /// at <c>propbuilding/decor</c>, which is the parent of the exterior folders as well —
        /// what goes round the outside of a building is not what stands in the corner of a room.
        /// </para>
        /// </remarks>
        public const string RoomDecorTag = "propbuilding/decor/decoration";

        /// <summary>Tag a catalog entry must carry to be stood in the middle of a room.</summary>
        /// <remarks>
        /// <para>
        /// The other half of what a workspace files under <c>Decor</c>, and it is a different tag
        /// because it is a different question. <c>Props/PropBuilding/Decor/Centerpieces</c> spells
        /// <c>propbuilding/decor/centerpieces</c>, and what goes in it is the sofa or the table a
        /// room is arranged around — so it is placed by <see cref="ConstraintKind.InCentre"/> where
        /// <see cref="RoomDecorTag"/> is placed by <see cref="ConstraintKind.InCorner"/>. One
        /// <c>Decor</c> query returning both would have made the two one pass, which is the pass
        /// that puts a pot plant in the middle of the floor and a sideboard across the doorway.
        /// </para>
        /// <para>
        /// <strong>Nothing tagged <see cref="InteriorCoverTag"/> can answer this query</strong>,
        /// and that is the point of the name. A crate is cover and a sofa is a centrepiece; they
        /// are placed by different passes under different rules, and a crate standing in the
        /// middle of a room as the thing it is arranged around is the failure this tag exists to
        /// rule out. The folder was called <c>Decor/Covers</c> once, one level under the tactical
        /// <c>Props/Covers</c>, and the two were kept apart by the sync's branch rule alone — which
        /// worked, and read like a coincidence waiting to stop working. The tag is an exact match on
        /// a name nothing else in a workspace carries.
        /// </para>
        /// </remarks>
        public const string CentrepieceTag = "propbuilding/decor/centerpieces";

        /// <summary>Tag a catalog entry must carry to be scattered across a room's floor.</summary>
        /// <remarks>
        /// <para>
        /// What a workspace files in <c>Props/PropBuilding/Decor/InteriorCovers</c>: the boxes,
        /// crates and clutter a room is fought through, as against the sofa it is arranged around
        /// and the pot plant in its corner.
        /// </para>
        /// <para>
        /// <strong>It is not <see cref="CoverPlacer.CoverTag"/>, and that is the whole of why it
        /// exists.</strong> A floor's contents were drawn from the map's own <c>Covers</c> folder,
        /// which reads as economy — cover is cover, and a crate is a crate wherever it stands — and
        /// is not: what is filed in <c>Props/Covers</c> is the tactical furniture of an outdoor
        /// arena, and the query returned dumpsters, concrete barriers and sandbags to be scattered
        /// through somebody's living room. The two folders answer two questions that only sound
        /// like one. <c>Covers</c> is now strictly the outdoor placer's, and a room's floor is
        /// strictly this.
        /// </para>
        /// <para>
        /// Matched at its full depth rather than at <c>propbuilding/decor</c>, which is the parent
        /// of the exterior folders as well — the same care <see cref="RoomDecorTag"/> takes, for
        /// the same reason.
        /// </para>
        /// </remarks>
        public const string InteriorCoverTag = "propbuilding/decor/interiorcovers";

        /// <summary>Tag a catalog entry must carry to be stood in a room's corner on two backs.</summary>
        /// <remarks>
        /// <para>
        /// What a workspace files in <c>Props/PropBuilding/Decor/Corners</c>: the furniture that is
        /// modelled as a corner rather than as a thing that can stand in one. An L-shaped sofa has
        /// two backs, and there is exactly one place in a room where both of them are supported and
        /// exactly one turn that supports them — see <see cref="WallFacing.IntoCorner"/>.
        /// </para>
        /// <para>
        /// A folder of its own rather than a flag on a row, because the folder is how this
        /// workspace says what a piece is for everywhere else, and because being a corner piece is
        /// a fact about where the art may go: nothing tagged this can answer the scatter's query,
        /// so an L-sofa cannot be drawn into the middle of a floor with its short back hanging over
        /// open ground. It is answered by the corner pass and by nothing else.
        /// </para>
        /// </remarks>
        public const string CornerPieceTag = "propbuilding/decor/corners";

        /// <summary>Prefix of the building metadata keys holding the placement statistics.</summary>
        public const string StatsPrefix = "content_";

        /// <summary>Building metadata key holding how many objects the density asked for.</summary>
        public const string TargetKey = StatsPrefix + "target";

        /// <summary>
        /// Metadata key holding how many exterior doorways the ground floor has.
        /// </summary>
        /// <remarks>
        /// The same key an arena writes on a structure it places, and deliberately: what an
        /// exported building carries out of here is read back in as the doorways of a piece of art,
        /// so the two ends of that journey have to spell it the same way. Aliased rather than
        /// re-declared so they cannot drift apart.
        /// </remarks>
        public const string DoorwayCountKey = ArenaLayoutGenerator.DoorwayCountKey;

        /// <summary>Prefix of the metadata keys holding those doorways' rectangles.</summary>
        /// <remarks>
        /// In the building's own space, which is where a catalog row's geometry is measured — the
        /// footprint centred on the origin, exactly as <see cref="BuildingDoc.Footprint"/> is.
        /// </remarks>
        public const string DoorwayKeyPrefix = ArenaLayoutGenerator.DoorwayKeyPrefix;

        /// <summary>Building metadata key holding how many rooms the partition produced.</summary>
        public const string RoomCountKey = "plan_rooms";

        /// <summary>Building metadata key holding how many of the leaves came out as corridors.</summary>
        public const string CorridorCountKey = "plan_corridors";

        /// <summary>Building metadata key holding how many objects the shell is built from.</summary>
        public const string StructureCountKey = "structure_objects";

        /// <summary>
        /// Building metadata key holding how far the roof stands above the top storey's ceiling,
        /// in metres. Absent when the catalog had nothing to cap the building with.
        /// </summary>
        public const string RoofHeightKey = "roof_height";

        /// <summary>Stable id prefix everything on the roof lives under.</summary>
        public const string RoofIdPrefix = BuildingDoc.IdPrefix + "/roof";

        /// <summary>Stable id segment naming one of the four runs round the edge of the roof.</summary>
        public const string ParapetSegment = "parapet";

        /// <summary>
        /// Stable id segment naming one of the runs fencing the stairwell opening in the roof.
        /// </summary>
        /// <remarks>
        /// Its own segment rather than a fifth and sixth <see cref="ParapetSegment"/>, because the
        /// two are different claims about the building: the ring round the edge is closed and there
        /// are always four sides of it, and this is fenced where there is a drop and nowhere else.
        /// </remarks>
        public const string StairwellRailSegment = "stairwell_rail";

        /// <summary>Walking space kept between two objects on a floor, in metres.</summary>
        public const float ContentMargin = 0.6f;

        /// <summary>Walking space kept between a piece of decor and anything else, in metres.</summary>
        /// <remarks>
        /// Half what two pieces of cover keep between them. Decor is furniture pushed back against
        /// a wall, and a room whose sideboard and armchair have to stand two-thirds of a metre
        /// apart is a room with one of them in it.
        /// </remarks>
        public const float DecorMargin = 0.3f;

        /// <summary>How close to a wall a piece of decor has to reach to count as against it.</summary>
        /// <remarks>
        /// Not zero, because the grid a candidate snaps to has no reason to divide the room
        /// exactly: a piece of decor whose pivot is on the nearest grid line to the wall can end up
        /// most of a cell short of it, and refusing that would leave the rule accepting almost
        /// nothing. One grid cell of the building's own default is the slack.
        /// </remarks>
        public const float DecorReach = 0.5f;

        /// <summary>Floor a centrepiece keeps between itself and every wall of its room, in metres.</summary>
        /// <remarks>
        /// The width the plan reserves for a walkway, and the same number for the same reason: a
        /// table in the middle of a room is a thing you walk round, and the room a person needs to
        /// get past it is the room a person needs anywhere else. Stating it twice would let the
        /// two disagree, and a centrepiece standing closer to a wall than a route is wide is a
        /// centrepiece with a dead end behind it.
        /// </remarks>
        public const float CentrepieceClearance = FloorPlan.PathWidth;

        /// <summary>How far past its own footprint a building's foundation is graded, in metres.</summary>
        /// <remarks>
        /// A building sitting exactly on the edge of its pad has a doorstep with a slope under it.
        /// A metre of flat ground all round is the doorstep.
        /// </remarks>
        public const float FoundationMargin = 1f;

        /// <summary>How far out of the pad the ground takes to reach its own height again, in metres.</summary>
        /// <remarks>
        /// A batter rather than a retaining wall. Four metres is shallow enough at any amplitude
        /// this tool's maps use that the grade is walkable, which matters because the apron is
        /// floor that cover is still placed on.
        /// </remarks>
        public const float FoundationApron = 4f;

        /// <summary>
        /// How far above the headroom a piece may measure and still be offered to a floor, in
        /// metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The filter it softens is an exact comparison between two floats that a user arrived at
        /// by arithmetic: a 0.2 m slab under a 3.1 m storey leaves a headroom that is a few parts
        /// in ten million short of 2.9, and a 2.9 m wall then does not fit under it. What that
        /// produces is not a slightly wrong building but an empty one — no wall art is offered at
        /// all, so every storey comes back as a single undivided room, which is the same outcome
        /// as a catalog with no walls in it and looks nothing like a rounding error.
        /// </para>
        /// <para>
        /// Five centimetres rather than an epsilon, because the two halves of this go together:
        /// what is admitted on tolerance is then <em>stretched to fit</em> by
        /// <see cref="StretchTo"/>, so a wall four centimetres too tall is squashed onto the
        /// ceiling rather than left standing through it. Sized to forgive the near miss a person
        /// makes typing a floor height, and not to forgive art that is simply too big for the
        /// storey: at ten times a rounding error and a fiftieth of a storey, a piece outside it was
        /// never meant to fit.
        /// </para>
        /// <para>
        /// A piece the tolerance admits and nothing stretches — a crate, a plant — may stand up to
        /// this far into the slab above it. That is the price of one number meaning one thing, and
        /// it is bounded: five centimetres of a crate inside a ceiling is a fraction of the art,
        /// where the alternative is a building with no walls.
        /// </para>
        /// </remarks>
        public const float HeadroomTolerance = 0.05f;

        /// <summary>How much clear floor a scattered piece may have behind it and still be turned by
        /// the wall, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Half a metre, which is <see cref="DecorReach"/> measuring a different thing for a
        /// different pass and coming out at the same number — the distance at which a person reads
        /// a piece of furniture as being against the wall rather than near it. A piece further out
        /// than this keeps the turn its stream drew, because a sofa in the middle of a floor has no
        /// wall to have its back to and any of the four is as good as any other.
        /// </para>
        /// <para>
        /// The piece's own reach from its pivot is added on top at the call site — see
        /// <see cref="WallFacing.Radius"/> — because this is floor left over and the pivot of a
        /// piece standing flush is half the piece away from the wall.
        /// </para>
        /// <para>
        /// Two separate constants rather than one shared: the corner rule is about whether a piece
        /// may stand somewhere, and this is about which way it faces once it does. They would move
        /// for different reasons.
        /// </para>
        /// </remarks>
        public const float WallFacingReach = 0.5f;

        /// <summary>Clear floor kept in front of a doorway, in metres.</summary>
        /// <remarks>
        /// Half what a map keeps around a structure's doorway. The rule is the same one and it is
        /// there for the same reason — an opening you cannot walk through is not a doorway — but
        /// inside a four-metre room a map's clearance would be most of the room.
        /// </remarks>
        public const float DoorwayClearance = 0.75f;

        /// <summary>Clear floor kept all round a stairwell, in metres.</summary>
        /// <remarks>
        /// <para>
        /// <strong>A flight of stairs is a doorway to the storey above, and this is its approach.</strong>
        /// A storey used to give up exactly the tiles the opening covers and not one centimetre
        /// more, which is the right rule for a hole in a floor and the wrong one for the way
        /// upstairs: a crate standing flush against the bottom step is a crate on the landing, and
        /// a room whose scatter closed in on all four sides of the shaft was a room a player had to
        /// squeeze into the stairs sideways. The shaft is a rectangle two or three metres across in
        /// a room that may be five, so the difference between "off the opening" and "clear of the
        /// stairs" is most of what makes the floor above reachable at a run.
        /// </para>
        /// <para>
        /// Twice <see cref="DoorwayClearance"/>, and the doubling is the point rather than a tuned
        /// figure. A door is a gap in a wall you walk through in one direction; a stairwell is
        /// approached, turned onto, and passed by, and it is the one thing on a floor that a player
        /// crossing the building has to line up with. What it costs is a metre and a half of the
        /// floor round the shaft, which is floor the room could not have put a crate on and left
        /// anybody a way upstairs.
        /// </para>
        /// </remarks>
        public const float StairClearance = 2f * DoorwayClearance;

        /// <summary>Label of the stream a floor's plan and its shell art are drawn from.</summary>
        const string PlanLabel = "plan";

        /// <summary>Label prefix of the stream one room's contents are drawn from.</summary>
        const string ContentsLabel = "contents/";

        /// <summary>Label prefix of the stream one room's decor is drawn from.</summary>
        /// <remarks>
        /// Its own fork rather than a continuation of the contents stream, so that adding decor to
        /// this generator did not move a single crate in any building anyone had already made —
        /// CLAUDE.md rule 2's fork-per-subsystem argument, one more level down.
        /// </remarks>
        const string DecorLabel = "decor/";

        /// <summary>Label prefix of the stream one room's centrepieces are drawn from.</summary>
        /// <remarks>
        /// Its own fork again, and the argument is the one <see cref="DecorLabel"/> makes: a
        /// catalog that has never had a piece of <see cref="CentrepieceTag"/> art in it builds the
        /// building it always built, down to the byte, because a fork is a function of a stream's
        /// seed rather than of how many draws have been taken off it.
        /// </remarks>
        const string CentreLabel = "centrepiece/";

        /// <summary>Label of the building-wide stream the stairwell is drawn from.</summary>
        /// <remarks>
        /// Forked from the <em>building</em> seed rather than from any floor's, which is the whole
        /// of how a stairwell aligns. A shaft drawn per floor would be a different shaft on every
        /// storey; a shaft drawn once is the same rectangle for every partition to work around,
        /// and re-rolling a floor moves that floor's walls without moving the stairs through them.
        /// </remarks>
        const string StairsLabel = "stairs";

        /// <summary>Label the stream the window art is drawn from is forked under.</summary>
        /// <remarks>
        /// A fork rather than another draw off the shell's own stream, and it is the difference
        /// between a feature and a regeneration. <see cref="Rng.Fork"/> is a function of a
        /// stream's seed rather than of its position, so a draw taken here moves nothing after it:
        /// a workspace that syncs a window into its catalog gets windows in the building it
        /// already had, rather than every storey of every building re-partitioned. It is the rule
        /// in CLAUDE.md section 2 doing exactly what it is there for.
        /// </remarks>
        const string WindowLabel = "windows";

        /// <summary>
        /// Objects per square metre of floor at a <c>ContentDensity</c> of 1.
        /// </summary>
        /// <remarks>
        /// One per eight square metres — four times a lane's density, because a floor is a room
        /// and a room is furnished more tightly than open ground.
        /// </remarks>
        const float BaselineContentPerSquareMetre = 1f / 8f;

        /// <summary>Pieces of decor per square metre of floor at a <c>ContentDensity</c> of 1.</summary>
        /// <remarks>
        /// Sparser than cover, and read off the same knob rather than a second one: how furnished
        /// a building is is one decision, and a room with a crate every eight square metres and a
        /// pot plant every twelve is one room rather than two settings to keep in step.
        /// </remarks>
        const float BaselineDecorPerSquareMetre = 1f / 12f;

        /// <summary>Centrepieces per square metre of floor at a <c>ContentDensity</c> of 1.</summary>
        /// <remarks>
        /// Sparser again, and off the same knob for the same reason. A centrepiece is the largest
        /// thing a room holds and there is only one middle for it to stand in, so a room is one
        /// table and a hall is three rather than a table every time the density is nudged.
        /// </remarks>
        const float BaselineCentrepiecePerSquareMetre = 1f / 20f;

        /// <summary>Most pieces of decor a room will stand in its corners.</summary>
        /// <remarks>
        /// A rectangle has four of them and each one is furnished at most once, so this is the
        /// count of corners rather than a budget: a fifth piece would have to share a corner with
        /// one of the first four, which is a pair of plants in a heap rather than a furnished
        /// room.
        /// </remarks>
        const int CornerDecorLimit = 4;

        /// <summary>Rough Poisson yield per unit area at unit radius. Measured, as in <see cref="CoverPlacer"/>.</summary>
        const float PoissonYield = 0.7f;

        /// <summary>How many more positions to sample than the target needs.</summary>
        const float SampleSurplus = 1.6f;

        /// <summary>Different catalog entries tried at one position before it is abandoned.</summary>
        const int EntriesPerPosition = 4;

        /// <summary>Candidate evaluations a room may spend per object it was asked for.</summary>
        const int AttemptsPerTargetObject = 8;

        /// <summary>Slack allowed when counting whole modules into a floor slab.</summary>
        const float FitTolerance = 1e-3f;

        /// <summary>What <see cref="RoomBehind"/> answers when no leaf holds the probe.</summary>
        /// <remarks>
        /// The leaves tile the storey exactly, so nothing should reach this — but a run whose
        /// module is not backed by a room is a run with no window in it rather than an exception,
        /// and a plan with no rooms at all is what a floor too small to divide produces.
        /// </remarks>
        const int NoRoom = -1;

        /// <summary>
        /// Generates a building whose floor seeds are derived from the building seed.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">The catalog has nothing that fits under a floor.</exception>
        public static BuildingDoc Generate(BuildingParams parameters, Catalog catalog) =>
            Generate(parameters, catalog, null);

        /// <summary>
        /// Generates a building, keeping the seeds of the floors <paramref name="existingFloors"/>
        /// already has and deriving fresh ones for any storey beyond it.
        /// </summary>
        /// <remarks>
        /// This is what regeneration and a per-floor re-roll both call. Adding a storey to a
        /// building leaves the ones below it byte-identical, for the same reason re-rolling one
        /// floor does: their seeds were already written down.
        /// </remarks>
        /// <param name="parameters">What to build.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="existingFloors">Floor seeds to carry over, or null to derive them all.</param>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> or <paramref name="catalog"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its supported range.</exception>
        /// <exception cref="InvalidOperationException">The catalog has nothing that fits under a floor.</exception>
        public static BuildingDoc Generate(
            BuildingParams parameters, Catalog catalog, IReadOnlyList<BuildingFloor> existingFloors)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            Validate(parameters);

            var doc = new BuildingDoc { Parameters = parameters.Clone() };
            doc.Floors.AddRange(DeriveFloors(parameters, existingFloors));

            Stairwell stairs = PlanStairwell(parameters, catalog);

            var stats = new PlacementStats();
            var tally = default(Tally);
            for (int floor = 0; floor < doc.Floors.Count; floor++)
            {
                tally = tally.Plus(BuildFloor(doc, floor, catalog, stairs, stats));
            }

            doc.Metadata[TargetKey] = Text(tally.Target);
            doc.Metadata[RoomCountKey] = Text(tally.Rooms);
            doc.Metadata[CorridorCountKey] = Text(tally.Corridors);
            doc.Metadata[StructureCountKey] = Text(tally.Structure);
            stats.WriteTo(doc.Metadata, StatsPrefix);

            return doc;
        }

        /// <summary>
        /// The plan one storey is laid out on: the rooms and corridors it is divided into, and the
        /// walls between them.
        /// </summary>
        /// <remarks>
        /// The same two steps <see cref="Generate"/> takes, from the same stream, so the plan this
        /// returns is the plan that storey was built on rather than a second one that resembles
        /// it. It is here because a plan is the only thing about a floor that is not visible in
        /// the object list: the walls are, but which leaf is a room and which is a corridor is
        /// not.
        /// </remarks>
        /// <param name="parameters">The parameters the building was generated with.</param>
        /// <param name="catalog">The art it was generated from.</param>
        /// <param name="floor">The storey, carrying the seed its plan is drawn from.</param>
        /// <param name="index">Which storey it is, counting from zero.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static FloorPlan PlanFloor(
            BuildingParams parameters, Catalog catalog, BuildingFloor floor, int index)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (floor == null)
            {
                throw new ArgumentNullException(nameof(floor));
            }

            Rng stream = new Rng(floor.Seed).Fork(PlanLabel);
            Shell shell = SelectShell(catalog, parameters.FloorHeight, ref stream);
            return Partition(parameters, shell, index, PlanStairwell(parameters, catalog), ref stream);
        }

        /// <summary>
        /// Where this building's stairwell stands and what it is built from, or a shaft that does
        /// not exist when the catalog has no stairs to put in one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A function of the parameters and the catalog and of nothing else — no floor is
        /// consulted, and that is the point. A stairwell has to be at the same place on every
        /// storey or it is not a stairwell, but each storey is partitioned from its own stored
        /// seed and re-rolling one has to leave the others alone. Those two facts only fit
        /// together if the shaft is decided above the floors rather than inside one of them: the
        /// building seed picks the rectangle, and every floor's partition then works around it.
        /// </para>
        /// <para>
        /// It is public for the same reason <see cref="PlanFloor"/> is: the shaft is the one thing
        /// about a building that is invisible in the object list until the stairs are standing in
        /// it, and a caller checking that the storeys agree needs to be able to ask.
        /// </para>
        /// <para>
        /// <strong>Where a flight may stand is the slab, not the clear floor inside the walls.</strong>
        /// The tile joints a flight is anchored to are laid out from the shell's own bounds, so on
        /// the common art pack — a wall as long as a floor tile is wide — the joints at the edge of
        /// the slab fall on the outside walls' centrelines. Holding the flight a whole wall clear of
        /// them, as <see cref="ShaftRegion"/> does, then leaves no joint but the middle ones, and a
        /// stairwell in the middle of a small floor is a floor of corridors: every cut of the region
        /// holding the shaft is pushed to an extreme, and each extreme is a one-module strip. So a
        /// flight may be buried half a wall thickness inside an outside wall — the same straddle
        /// every junction in the shell already makes.
        /// </para>
        /// <para>
        /// <strong>What it may not reach is the edge of the slab itself.</strong> Every storey above
        /// the ground has the tiles over the flight left out, so a flight flush with the edge takes
        /// the floor out from under the outside wall standing there and out from under the roof's
        /// parapet — and a wall whose slab has been cut away hangs a slab's thickness clear of the
        /// storey below it, which is a strip of daylight along the outside of the building at every
        /// floor line. So the flight is held clear of the slab's edge by
        /// <see cref="PerimeterSupport"/>, the strip the shell stands on. It costs about what it
        /// looks like it costs: the anchor snaps to a tile joint, so on the common pack a flight
        /// gives up the outermost ring of tiles and nothing more.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static Stairwell PlanStairwell(BuildingParams parameters, Catalog catalog)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            // Against the floor height rather than the headroom: a flight has to reach the storey
            // above, which is exactly what the hole in the slab is cut for.
            List<CatalogEntry> flights =
                ShortEnough(catalog.Query(TagQuery.All(StairsTag)), parameters.FloorHeight);
            if (flights.Count == 0)
            {
                return default;
            }

            SlabGrid grid = PlannedSlabGrid(parameters, catalog);
            Rect2 slab = grid.Exists ? grid.Extent : ShaftRegion(parameters, catalog);
            if (!(slab.Width > 0f) || !(slab.Depth > 0f))
            {
                return default;
            }

            // Held off the strip of slab the outside walls and the roof's parapet stand on. There
            // is no such strip to protect when the catalog has no floor art, because then there is
            // no slab to cut and nothing standing on one.
            Rect2 region = grid.Exists
                ? InsideThePerimeter(slab, PerimeterSupport(parameters, catalog))
                : slab;

            Rng rng = new Rng(parameters.Seed).Fork(StairsLabel);

            // The turn first, then the art that fits at it. Picking the piece first and then
            // finding it will not turn that way would mean either a second draw to fix it or a
            // building with no stairs because the one flight in the catalog came up sideways.
            int turns = rng.NextRange(0, QuarterTurn.Count);
            List<CatalogEntry> usable = FittingIn(flights, region, turns);
            if (usable.Count == 0)
            {
                // A floor too small to hold a flight clear of its own walls keeps its stairwell and
                // gives up the strip: a storey with no way up is the worse of the two failures, and
                // it is the only one a person cannot walk round. Nothing here has drawn yet beyond
                // the turn, so falling back costs the stream nothing.
                region = slab;
                usable = FittingIn(flights, region, turns);
            }

            if (usable.Count == 0)
            {
                return default;
            }

            CatalogEntry entry = rng.WeightedPick(usable, e => e.Weight);
            Rect2 footprint = QuarterTurn.Rotate(entry.Footprint, turns);

            // The low corner of the flight rather than its pivot, so it can be anchored on a slab
            // joint: an opening is a whole number of tiles whatever happens, and a flight whose
            // edge is on one of those joints needs the fewest of them and lands flush against the
            // floor at the edge of the one it makes.
            var corner = new Vec2(
                AnchorLowEdge(
                    grid.OriginX, grid.Cell.X, footprint.Width,
                    region.MinX, region.MaxX, parameters.GridSize, ref rng),
                AnchorLowEdge(
                    grid.OriginZ, grid.Cell.Y, footprint.Depth,
                    region.MinZ, region.MaxZ, parameters.GridSize, ref rng));

            var at = new Vec2(corner.X - footprint.MinX, corner.Y - footprint.MinZ);
            Rect2 flight = footprint.Translated(at);

            return new Stairwell(entry, at, turns, flight, grid.Cover(flight));
        }

        /// <summary>
        /// The floor list <paramref name="parameters"/> describes: the seeds
        /// <paramref name="existing"/> already holds, and a fork of the building seed for the rest.
        /// </summary>
        /// <remarks>
        /// Forked by floor id rather than by index, so the label a seed is derived from is the
        /// same string that appears in the stable ids of the things it places.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
        public static List<BuildingFloor> DeriveFloors(
            BuildingParams parameters, IReadOnlyList<BuildingFloor> existing)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var rng = new Rng(parameters.Seed);
            var floors = new List<BuildingFloor>(Math.Max(0, parameters.FloorCount));

            for (int i = 0; i < parameters.FloorCount; i++)
            {
                floors.Add(existing != null && i < existing.Count
                    ? existing[i]
                    : new BuildingFloor(rng.Fork(BuildingFloor.IdOf(i)).Seed));
            }

            return floors;
        }

        /// <summary>
        /// A copy of <paramref name="floors"/> with one floor's seed replaced.
        /// </summary>
        /// <remarks>
        /// The replacement seed comes from the caller rather than from the building seed, because
        /// a re-roll is the one thing in the tool that is meant not to be repeatable — the same
        /// bargain the map's seed re-roll makes. What stays deterministic is the document: once
        /// the number is written down, that floor generates the same way for ever.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="floors"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a floor.</exception>
        public static List<BuildingFloor> WithRerolledFloor(
            IReadOnlyList<BuildingFloor> floors, int index, ulong seed)
        {
            if (floors == null)
            {
                throw new ArgumentNullException(nameof(floors));
            }

            if (index < 0 || index >= floors.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, $"This building has {floors.Count} floor(s).");
            }

            var rerolled = new List<BuildingFloor>(floors);
            rerolled[index] = new BuildingFloor(seed);
            return rerolled;
        }

        // --- how a building meets the ground -------------------------------------------------

        /// <summary>The ground a building with this footprint has to have levelled under it.</summary>
        public static Rect2 FoundationPad(Rect2 footprint) => footprint.Expanded(FoundationMargin);

        /// <summary>
        /// Grades a flat foundation into <paramref name="terrain"/> under a building's footprint
        /// and returns the height the building then stands at.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A generated building is a stack of flat storeys with vertical walls between them, so
        /// there is exactly one height it can be put at and no sense in which it could follow a
        /// slope. Sampling the ground at the building's centre and standing it there would leave
        /// one corner buried and the opposite one in mid-air on any ground worth calling uneven;
        /// what a person does instead is cut and fill the ground into a pad first, and that is
        /// what this does.
        /// </para>
        /// <para>
        /// It lives here rather than in <see cref="ArenaLayoutGenerator"/>, which is what calls
        /// it, because how a building meets the ground is a fact about the building: the pad is
        /// its footprint plus its doorstep, and the apron is the grade its walls need around them.
        /// A building document has no terrain of its own — a building is generated in its own
        /// space at ground level and only meets a heightfield when a map places it — so this is
        /// the one piece of the building generator an arena calls into.
        /// </para>
        /// </remarks>
        /// <param name="terrain">The ground to grade.</param>
        /// <param name="footprint">The building's world footprint.</param>
        /// <returns>The height of the finished pad, in metres.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is null.</exception>
        public static float LevelFoundation(TerrainField terrain, Rect2 footprint)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            Rect2 pad = FoundationPad(footprint);
            float height = terrain.MeanHeightOn(pad);
            terrain.AddFoundation(pad, FoundationApron, height);
            return height;
        }

        static void Validate(BuildingParams parameters)
        {
            if (!(parameters.FootprintSize.X > 0f) || !(parameters.FootprintSize.Y > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.FootprintSize, "Footprint size must be positive.");
            }

            if (parameters.FloorCount < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.FloorCount, "A building needs at least one floor.");
            }

            if (!(parameters.FloorHeight > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.FloorHeight, "Floor height must be positive.");
            }

            if (!(parameters.GridSize > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.GridSize, "Grid size must be positive.");
            }

            if (parameters.WallMargin < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.WallMargin, "Wall margin must not be negative.");
            }

            if (!(parameters.ContentDensity >= 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.ContentDensity, "Content density must not be negative.");
            }

            Vec2 inside = parameters.FootprintSize - new Vec2(2f * parameters.WallMargin, 2f * parameters.WallMargin);
            if (!(inside.X > 0f) || !(inside.Y > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.WallMargin,
                    $"A wall margin of {parameters.WallMargin.ToString("0.##", CultureInfo.InvariantCulture)} m " +
                    $"leaves no floor inside a {parameters.FootprintSize} footprint.");
            }
        }

        /// <summary>
        /// The catalog entries a floor's contents may be drawn from: interior cover that fits
        /// under the slab of the storey above it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="InteriorCoverTag"/> and nothing else — see that tag for why a room's floor
        /// stopped being furnished out of the map's <c>Covers</c> folder.
        /// </para>
        /// <para>
        /// The height filter is what makes floors stack rather than intersect, and it belongs
        /// here rather than in a constraint because the constraint set is two-dimensional — every
        /// rule in it is a statement about a rectangle on the ground. Selecting for height before
        /// anything is proposed keeps it that way, and the shell is selected the same way for the
        /// same reason. What it is measured against is the headroom rather than the floor height:
        /// a crate as tall as the storey is one whose lid is inside the floor above.
        /// </para>
        /// </remarks>
        static IReadOnlyList<CatalogEntry> FloorContents(Catalog catalog, float headroom)
        {
            List<CatalogEntry> fits = ShortEnough(catalog.Query(TagQuery.All(InteriorCoverTag)), headroom);

            if (fits.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The catalog has nothing tagged '{InteriorCoverTag}' that stands under " +
                    $"{headroom.ToString("0.##", CultureInfo.InvariantCulture)} m of headroom, so this " +
                    "building would have nothing on any storey. Interior clutter is filed in " +
                    "Props/PropBuilding/Decor/InteriorCovers; Props/Covers is the outdoor arena's.");
            }

            return fits;
        }

        /// <summary>
        /// The entries that stand under a ceiling, measured from the surface they rest on rather
        /// than from their own pivot, and forgiven <see cref="HeadroomTolerance"/> of overshoot.
        /// </summary>
        /// <remarks>
        /// <see cref="CatalogEntry.StandingHeight"/> rather than <c>Height</c>, because a piece
        /// modelled around its centre is stood half its own depth higher than its pivot and would
        /// otherwise be measured a base offset short of how much room it actually needs.
        /// </remarks>
        static List<CatalogEntry> ShortEnough(IReadOnlyList<CatalogEntry> entries, float floorHeight)
        {
            float ceiling = floorHeight + HeadroomTolerance;

            var fits = new List<CatalogEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].StandingHeight <= ceiling)
                {
                    fits.Add(entries[i]);
                }
            }

            return fits;
        }

        /// <summary>
        /// How far to stretch a piece along its own Y so that it stands exactly
        /// <paramref name="headroom"/> tall.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Measured against <see cref="CatalogEntry.StandingHeight"/> — what the piece occupies
        /// from the surface it rests on — rather than against its height above its pivot, so a
        /// wall modelled around its own centre is fitted by the same arithmetic as one modelled on
        /// its base. <see cref="Structure"/> scales the lift that stands it up by the same factor,
        /// which is what keeps its underside on the floor while its top face reaches the ceiling.
        /// </para>
        /// <para>
        /// Both directions, deliberately. Stretching a wall that came up short closes a strip of
        /// daylight running the length of every wall in the building; squashing one that
        /// <see cref="HeadroomTolerance"/> let through keeps its top out of the slab above. The
        /// factor is a ratio of two numbers the storey already knows, so it is as deterministic as
        /// they are.
        /// </para>
        /// <para>
        /// A piece with no height cannot be stretched into one, so it is left alone: the ratio
        /// would be a division by zero, and art that occupies nothing has nothing to fit.
        /// </para>
        /// </remarks>
        static float StretchTo(CatalogEntry entry, float headroom) =>
            entry.StandingHeight > 0f && headroom > 0f ? headroom / entry.StandingHeight : 1f;

        /// <summary>The entries whose footprint fits inside a region at a given quarter turn.</summary>
        static List<CatalogEntry> FittingIn(IReadOnlyList<CatalogEntry> entries, Rect2 region, int quarterTurns)
        {
            var fits = new List<CatalogEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                Rect2 turned = QuarterTurn.Rotate(entries[i].Footprint, quarterTurns);
                if (turned.Width <= region.Width && turned.Depth <= region.Depth)
                {
                    fits.Add(entries[i]);
                }
            }

            return fits;
        }

        /// <summary>
        /// The rectangle a stairwell may stand in when there is no slab to anchor it to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The shaft is picked once for the building but the module grid it has to sit inside is
        /// picked per floor, so this is the intersection of every grid that floor could produce.
        /// A storey's walls enclose a rectangle of <c>floor((W - t) / m) * m</c>, centred in the
        /// footprint, and its inner faces are half a thickness inside that — so a rectangle
        /// <c>W - m - 2t</c> across is inside every one of them for any module <c>m</c> and
        /// thickness <c>t</c> the catalog can offer.
        /// </para>
        /// <para>
        /// Conservative rather than exact, and deliberately: an exact answer would need the shell
        /// of every floor, which would make where the stairs go depend on what the floors drew —
        /// and re-rolling a floor would then move the stairs on all the others.
        /// </para>
        /// <para>
        /// It is the fallback rather than the usual answer. A catalog with floor art puts the
        /// flight on the slab instead — see <see cref="PlanStairwell"/> for why holding it a whole
        /// module clear of the outside walls costs more than it buys once the flight has to land on
        /// a tile joint. Without floor art there is no joint to land on and no slab to cut, so
        /// there is nothing to trade and the conservative rectangle is simply right.
        /// </para>
        /// </remarks>
        static Rect2 ShaftRegion(BuildingParams parameters, Catalog catalog)
        {
            WallMetrics(parameters, catalog, out float module, out float thickness);

            float inset = module + 2f * thickness;
            Vec2 size = parameters.FootprintSize - new Vec2(inset, inset);

            return size.X > 0f && size.Y > 0f ? Rect2.FromCenterSize(Vec2.Zero, size) : Rect2.Zero;
        }

        /// <summary>
        /// The longest module and thickest wall any storey of this building could draw.
        /// </summary>
        /// <remarks>
        /// The largest of each, separately, because both are used to bound what every storey has
        /// in common. A catalog with one piece of wall art — which is what an art pack normally
        /// is — reports that piece exactly.
        /// </remarks>
        static void WallMetrics(
            BuildingParams parameters, Catalog catalog, out float module, out float thickness)
        {
            module = 0f;
            thickness = 0f;

            IReadOnlyList<CatalogEntry> walls = catalog.Query(TagQuery.All(WallTag));
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i].StandingHeight > parameters.FloorHeight)
                {
                    continue;
                }

                module = MathF.Max(module, LongSide(walls[i]));
                thickness = MathF.Max(thickness, ShortSide(walls[i]));
            }
        }

        /// <summary>
        /// How wide a strip of slab the shell stands on at the edge of a storey: the widest an
        /// outside wall or the roof's parapet could need under it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Half a thickness for a wall, because a run straddles its own centreline and the plan
        /// bounds it is laid on are the edge of the slab — so the half of it standing over the
        /// floor is the half that needs floor. A parapet is the whole of its depth, because
        /// <see cref="EmitParapet"/> insets a run by half its depth to bring its outer face flush
        /// with the roof, which puts all of it inboard of the edge.
        /// </para>
        /// <para>
        /// The largest of everything on offer rather than what a storey drew, for the reason
        /// <see cref="ShaftRegion"/> takes the largest module: which wall a storey draws is a fact
        /// about that storey, and the shaft is a fact about the building.
        /// </para>
        /// </remarks>
        static float PerimeterSupport(BuildingParams parameters, Catalog catalog)
        {
            WallMetrics(parameters, catalog, out _, out float thickness);
            float support = thickness * 0.5f;

            IReadOnlyList<CatalogEntry> parapets = catalog.Query(TagQuery.All(ParapetTag));
            for (int i = 0; i < parapets.Count; i++)
            {
                support = MathF.Max(support, ShortSide(parapets[i]));
            }

            return support;
        }

        /// <summary>
        /// <paramref name="slab"/> less the strip round its edge that the shell stands on, or the
        /// whole of it when there is not enough slab to give a strip up.
        /// </summary>
        static Rect2 InsideThePerimeter(Rect2 slab, float support)
        {
            if (!(support > 0f))
            {
                return slab;
            }

            Rect2 inside = slab.Expanded(-support);
            return inside.Width > 0f && inside.Depth > 0f ? inside : slab;
        }

        /// <summary>
        /// The grid the storeys' slabs are expected to be tiled on, as the stairwell has to assume
        /// it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Where the hole for the stairs goes is a fact about the building, but which tile a storey
        /// lays its slab from and how wide its walls make its plan are facts about that storey — so
        /// this answers with the grid the building is most likely to get: the largest floor tile
        /// the catalog offers, over the plan bounds the longest wall module gives. It is the same
        /// conservatism <see cref="ShaftRegion"/> uses and it is exact for the same catalogs: one
        /// piece of wall art and one floor tile, which is what an art pack normally is.
        /// </para>
        /// <para>
        /// A storey that drew something else still tiles on its own grid and still has its opening
        /// cut from the tiles the shaft laps — the opening simply comes out a tile larger there
        /// than it needed to be, which is the outcome every storey had before this existed.
        /// </para>
        /// </remarks>
        static SlabGrid PlannedSlabGrid(BuildingParams parameters, Catalog catalog)
        {
            CatalogEntry tile = LargestTile(
                ShortEnough(catalog.Query(TagQuery.All(FloorTileTag)), parameters.FloorHeight));
            if (tile == null)
            {
                return default;
            }

            WallMetrics(parameters, catalog, out float module, out float thickness);
            Rect2 footprint = Rect2.FromCenterSize(Vec2.Zero, parameters.FootprintSize);
            Rect2 bounds = module > 0f
                ? FloorPlan.BoundsOf(footprint, module, thickness)
                : footprint;

            return SlabGridOver(bounds, tile);
        }

        /// <summary>The entry covering the most ground, or null when there are none.</summary>
        /// <remarks>
        /// Ties go to the first, and the catalog holds its entries in logical-id order, so this is
        /// the same answer on every machine.
        /// </remarks>
        static CatalogEntry LargestTile(IReadOnlyList<CatalogEntry> entries)
        {
            CatalogEntry largest = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (largest == null || entries[i].Footprint.Area > largest.Footprint.Area)
                {
                    largest = entries[i];
                }
            }

            return largest;
        }

        /// <summary>The grid a slab of <paramref name="tile"/> tiles across <paramref name="bounds"/>.</summary>
        static SlabGrid SlabGridOver(Rect2 bounds, CatalogEntry tile)
        {
            if (tile == null)
            {
                return default;
            }

            Rect2 footprint = tile.Footprint;
            if (!(footprint.Width > 0f) || !(footprint.Depth > 0f))
            {
                return default;
            }

            int countX = (int)MathF.Floor(bounds.Width / footprint.Width + FitTolerance);
            int countZ = (int)MathF.Floor(bounds.Depth / footprint.Depth + FitTolerance);

            return new SlabGrid(bounds.Center, footprint.Size, countX, countZ);
        }

        /// <summary>
        /// Draws the low edge of something <paramref name="span"/> across, on the slab joints where
        /// one of them leaves it inside [<paramref name="min"/>, <paramref name="max"/>] and on the
        /// parameter grid where none does.
        /// </summary>
        /// <remarks>
        /// A joint is worth reaching for because it is what makes an opening exact: a flight that
        /// starts on one needs the fewest whole tiles taken out of the slab and ends up flush
        /// against the floor at the edge of the hole, rather than adrift in the middle of a hole
        /// two tiles wider than it is. Where no joint fits, the fall back is the draw this made
        /// before — a coarse opening beats no stairwell.
        /// </remarks>
        static float AnchorLowEdge(
            float origin, float cell, float span, float min, float max, float fallback, ref Rng rng)
        {
            if (cell > 0f)
            {
                int first = (int)MathF.Ceiling((min - origin) / cell - FitTolerance);
                int last = (int)MathF.Floor((max - span - origin) / cell + FitTolerance);

                if (last >= first)
                {
                    return origin + rng.NextRange(first, last + 1) * cell;
                }
            }

            return DrawOnGrid(min, max - span, fallback, ref rng);
        }

        /// <summary>
        /// Draws a coordinate in [<paramref name="min"/>, <paramref name="max"/>] on a grid of
        /// <paramref name="cellSize"/>, or the low end when the range holds no whole cell.
        /// </summary>
        /// <remarks>
        /// A whole number of cells from one end rather than a float in the range, because an
        /// integer draw is the same number on every runtime and a float scaled into a range is
        /// one rounding decision away from not being.
        /// </remarks>
        static float DrawOnGrid(float min, float max, float cellSize, ref Rng rng)
        {
            int steps = (int)MathF.Floor((max - min) / cellSize + FitTolerance);
            return steps <= 0 ? min : min + rng.NextRange(0, steps + 1) * cellSize;
        }

        // --- one storey --------------------------------------------------------------------

        /// <summary>Lays out one floor, builds its shell and fills its rooms.</summary>
        static Tally BuildFloor(
            BuildingDoc doc, int floor, Catalog catalog, Stairwell stairs, PlacementStats stats)
        {
            BuildingParams parameters = doc.Parameters;
            ulong seed = doc.Floors[floor].Seed;

            // Forked once more off the floor's stored seed, so adding a stage here cannot shift
            // the contents of a floor nobody re-rolled — and so the plan and the contents of one
            // floor draw independently of each other.
            Rng planStream = new Rng(seed).Fork(PlanLabel);
            Shell shell = SelectShell(catalog, parameters.FloorHeight, ref planStream);
            FloorPlan plan = Partition(parameters, shell, floor, stairs, ref planStream);

            var storey = new Storey(floor, seed, parameters.FloorHeight, shell.SlabHeight);
            IReadOnlyList<CatalogEntry> contents = FloorContents(catalog, shell.Headroom);
            IReadOnlyList<CatalogEntry> decor = ShortEnough(
                catalog.Query(TagQuery.Empty.WithAny(DecorTag, RoomDecorTag, CornerPieceTag)),
                shell.Headroom);
            IReadOnlyList<CatalogEntry> centrepieces = ShortEnough(
                catalog.Query(TagQuery.All(CentrepieceTag)), shell.Headroom);

            // The slab of every storey but the ground one has a hole in it where the stairs come
            // up through it, and the hole is the ground the flight covers rather than the region
            // reserved around it: what a storey has to give up to cut that is a fact about the tile
            // it drew, and two storeys that drew different tiles do not give up the same thing.
            // Cutting it is the difference between a flight of stairs and a
            // sculpture of one.
            Rect2 hole = floor > 0 ? stairs.Flight : Rect2.Zero;
            SlabGrid grid = SlabGridOver(plan.Bounds, shell.Tile);

            if (floor == 0)
            {
                AddExteriorDoorways(doc, plan);
            }

            int structure = EmitShell(doc, plan, shell, storey, grid, hole);
            structure += EmitStairs(doc, stairs, storey);

            if (floor == doc.Floors.Count - 1)
            {
                structure += EmitRoof(doc, plan, shell, stairs, storey, grid, catalog, ref planStream);
            }

            if (floor > 0 && stairs.Exists)
            {
                CatalogEntry defaultRail = Pick(catalog, ParapetTag, ref planStream);
                structure += EmitStairwellRail(doc, grid.Extent, stairs, defaultRail, storey.Elevation, storey.IdPrefix);
            }

            var tally = new Tally(0, 0, 0, structure);
            List<Rect2> doorways = Doorways(plan);

            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                PlanRoom room = plan.Rooms[i];
                if (room.IsCorridor)
                {
                    // Left clear on purpose. A corridor is what a person walks down to reach the
                    // rooms, and furnishing it would turn the one part of the plan that exists to
                    // be crossed into another part to be climbed over.
                    tally = tally.Plus(new Tally(0, 0, 1, 0));
                    continue;
                }

                int target = FurnishRoom(
                    doc, room, plan, doorways, contents, decor, centrepieces, stairs, parameters,
                    storey, stats);
                tally = tally.Plus(new Tally(target, 1, 0, 0));
            }

            return tally;
        }

        /// <summary>
        /// Records the ways into the building from outside, as rectangles in its own space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The ground floor's shell, and nothing else.</strong> Two things are being asked
        /// about a building and only one of them is answered here. Where a person may walk between
        /// two rooms is a fact about a floor, and every cut of every storey carries one — that is
        /// <see cref="Doorways(FloorPlan)"/>, and it is what the contents of a room are kept clear
        /// of. Where a person may walk in from the map outside is a fact about the
        /// <em>building</em>, and only the four runs at the head of a plan's wall list are outside
        /// walls at all; a doorway in any run past them opens into the next room along. Counting
        /// those as ways in is how a building comes to declare a dozen entrances, none of which
        /// reaches the map.
        /// </para>
        /// <para>
        /// The upper storeys are left out for the reason they have no entrance to leave out: a
        /// doorway in the outside wall of floor two would open onto a drop, so
        /// <see cref="Partition"/> never puts one there. Scanning them anyway would be reading a
        /// promise back rather than checking it, and the day the promise changed this would quietly
        /// start declaring first-floor windows as doors.
        /// </para>
        /// <para>
        /// The rectangle is the threshold rather than the gap in the wall: as wide as the module
        /// taken out of the run and <see cref="ArenaLayoutGenerator.DoorwayDepth"/> deep across it,
        /// so the part of it standing outside the wall is floor somebody can be measured standing
        /// on. A rectangle as deep as the wall is thick is a fifth of a metre of ground, which a
        /// walkable grid can step straight over without ever landing a cell in it — and a doorway
        /// no cell falls inside is a doorway the validator reports as unreachable.
        /// </para>
        /// </remarks>
        static void AddExteriorDoorways(BuildingDoc doc, FloorPlan plan)
        {
            List<Rect2> doorways = ExteriorDoorways(plan);

            for (int i = 0; i < doorways.Count; i++)
            {
                doc.Metadata[DoorwayKeyPrefix + Index(i)] = RectMetadata.Format(doorways[i]);
            }

            doc.Metadata[DoorwayCountKey] = Text(doorways.Count);
        }

        /// <summary>
        /// The ways into a building from outside, as rectangles in the building's own space: the
        /// doorways in the four outside runs of a ground-floor plan.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What <see cref="AddExteriorDoorways"/> writes down, as a list rather than as metadata,
        /// so the export that carries a building into a catalog can ask for it directly instead of
        /// re-deriving it. One implementation with two callers — the metadata a generation writes,
        /// and the answer an export needs when a document in a scene predates that metadata and has
        /// none. Two derivations of the same rule would be free to drift, which is the thing the
        /// metadata route was chosen to avoid.
        /// </para>
        /// <para>
        /// Handed a plan of any storey it answers that storey's outside doorways, and only the
        /// ground floor has any: <see cref="Partition"/> never cuts one into an upper storey's
        /// shell, because a door there opens onto a drop. See <see cref="AddExteriorDoorways"/> for
        /// why only the first <see cref="FloorPlan.PerimeterRuns"/> runs are outside walls, and
        /// why the rectangle is the threshold rather than the gap in the wall.
        /// </para>
        /// </remarks>
        /// <param name="plan">A floor's plan, normally the ground floor's.</param>
        /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
        public static List<Rect2> ExteriorDoorways(FloorPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float depth = MathF.Max(plan.Thickness, ArenaLayoutGenerator.DoorwayDepth);
            var doorways = new List<Rect2>(FloorPlan.Entrances);

            for (int i = 0; i < FloorPlan.PerimeterRuns && i < plan.Walls.Count; i++)
            {
                PlanWall wall = plan.Walls[i];
                if (wall.Doorway == PlanWall.NoDoorway)
                {
                    continue;
                }

                doorways.Add(Rect2.FromCenterSize(
                    wall.ModuleCenter(wall.Doorway),
                    wall.AlongX
                        ? new Vec2(wall.Module, depth)
                        : new Vec2(depth, wall.Module)));
            }

            return doorways;
        }

        /// <summary>
        /// The openings on a floor, as the rectangles a person walks through.
        /// </summary>
        /// <remarks>
        /// Every room is given all of them rather than only the ones in its own walls. A doorway
        /// in a shared wall belongs to both rooms it joins, and one in a far wall is further away
        /// than any clearance reaches — so working out which is which would cost a pass and buy
        /// the same answer.
        /// </remarks>
        static List<Rect2> Doorways(FloorPlan plan)
        {
            float thickness = plan.Thickness;
            var doorways = new List<Rect2>();

            for (int i = 0; i < plan.Walls.Count; i++)
            {
                PlanWall wall = plan.Walls[i];
                if (wall.Doorway == PlanWall.NoDoorway)
                {
                    continue;
                }

                doorways.Add(Rect2.FromCenterSize(
                    wall.ModuleCenter(wall.Doorway),
                    wall.AlongX
                        ? new Vec2(wall.Module, thickness)
                        : new Vec2(thickness, wall.Module)));
            }

            return doorways;
        }

        /// <summary>The plan for one storey, or one undivided room when there is no wall art.</summary>
        /// <remarks>
        /// <para>
        /// The entrance is on the ground floor and nowhere else, because it is the way into the
        /// building rather than the way onto a storey: a doorway in the outside wall of floor 2
        /// would still open onto a drop now that the way up is inside.
        /// </para>
        /// <para>
        /// The shaft is handed to the partition along with the clearance a doorway needs in front
        /// of it, and the two are separate on purpose. No cut may cross the shaft, or a wall would
        /// stand over the opening; but a wall <em>alongside</em> it is fine, and it is only a
        /// doorway in that wall that is a problem, because walking through it puts you into the
        /// shaft. Reserving the clearance against every cut instead pushes every split of the
        /// region holding the shaft out to its edges, and a floor of one-module strips is a floor
        /// of corridors.
        /// </para>
        /// <para>
        /// What is reserved is <see cref="Stairwell.Shaft"/> — the opening — rather than the
        /// flight standing in it. The part of the opening the stairs do not cover is a drop, and a
        /// wall across it or a door into it is worse than one over the flight, not better.
        /// </para>
        /// </remarks>
        static FloorPlan Partition(
            BuildingParams parameters, Shell shell, int floor, Stairwell stairs, ref Rng rng)
        {
            Rect2 footprint = Rect2.FromCenterSize(Vec2.Zero, parameters.FootprintSize);
            if (!shell.HasWalls)
            {
                return FloorPlan.Single(footprint);
            }

            return FloorPlan.Build(
                footprint,
                shell.Module,
                shell.Thickness,
                floor == 0,
                stairs.Exists ? stairs.Shaft : Rect2.Zero,
                DoorwayClearance,
                ref rng);
        }

        // --- the shell ---------------------------------------------------------------------

        /// <summary>
        /// Picks the art one storey's shell is built from: one slab, one wall, one doorway.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One of each per floor rather than a fresh draw per module, because a wall that changed
        /// material every two metres is not a building, and because the module the whole plan is
        /// laid out in comes from the wall that was picked — a run tiled from pieces of different
        /// lengths could not be a whole number of anything.
        /// </para>
        /// <para>
        /// The slab is drawn first because how thick it is decides how much of the storey is left
        /// for everything standing on it. A wall as tall as the whole floor height would have its
        /// top face in the same plane as the top of the slab above — coplanar, same normal, and
        /// over the wall's entire length, which is the worst flicker a building can have — so the
        /// slab's thickness comes off the headroom before the wall is chosen.
        /// </para>
        /// </remarks>
        static Shell SelectShell(Catalog catalog, float floorHeight, ref Rng rng)
        {
            CatalogEntry tile = PickFitting(catalog, FloorTileTag, floorHeight, ref rng);
            float slab = tile != null ? tile.Height : 0f;
            float headroom = floorHeight - slab;

            // Not a draw: the margin is the piece that has to reach into a gap the slab tile left,
            // so it is chosen for being the smallest the catalog has rather than for variety.
            CatalogEntry margin = SmallestTile(
                ShortEnough(catalog.Query(TagQuery.All(FloorTileTag)), floorHeight), tile);

            CatalogEntry wall = PickFitting(catalog, WallTag, headroom, ref rng);
            CatalogEntry doorway = PickFitting(catalog, DoorwayTag, headroom, ref rng);

            // Off a fork rather than off this stream, so a catalog that gains window art gets
            // windows in the buildings it already had instead of a different building. See
            // <see cref="WindowLabel"/>.
            Rng windows = rng.Fork(WindowLabel);
            CatalogEntry window = PickFitting(catalog, WindowTag, headroom, ref windows);

            if (wall == null)
            {
                // Nothing to divide the floor with and nothing to take a module from, so the
                // floor stays one open room.
                return new Shell(null, doorway, window, tile, margin, 0f, 0f, slab, headroom);
            }

            return new Shell(
                wall, doorway, window, tile, margin, LongSide(wall), ShortSide(wall), slab,
                headroom);
        }

        /// <summary>
        /// The entry covering the least ground, or null when none of them covers less than
        /// <paramref name="than"/>.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="LargestTile"/>, and deterministic for the same reason: ties go
        /// to the first, and the catalog holds its entries in logical-id order. A catalog with one
        /// floor tile — which is what an art pack normally is — answers null, and the slab is laid
        /// exactly as it was before there was a second tile to reach for.
        /// </remarks>
        static CatalogEntry SmallestTile(IReadOnlyList<CatalogEntry> entries, CatalogEntry than)
        {
            if (than == null)
            {
                return null;
            }

            CatalogEntry smallest = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Footprint.Area < than.Footprint.Area &&
                    (smallest == null || entries[i].Footprint.Area < smallest.Footprint.Area))
                {
                    smallest = entries[i];
                }
            }

            return smallest;
        }

        /// <summary>
        /// A weighted pick over the entries carrying a tag that stand under the ceiling, or null
        /// when the catalog has none.
        /// </summary>
        static CatalogEntry PickFitting(Catalog catalog, string tag, float floorHeight, ref Rng rng)
        {
            List<CatalogEntry> fits = ShortEnough(catalog.Query(TagQuery.All(tag)), floorHeight);
            return fits.Count > 0 ? rng.WeightedPick(fits, e => e.Weight) : null;
        }

        /// <summary>A weighted pick over everything carrying a tag, or null when nothing does.</summary>
        /// <remarks>
        /// No height filter, unlike <see cref="PickFitting"/>. The one thing selected this way is
        /// the parapet, and there is nothing above a roof for it to stand through.
        /// </remarks>
        static CatalogEntry Pick(Catalog catalog, string tag, ref Rng rng)
        {
            IReadOnlyList<CatalogEntry> entries = catalog.Query(TagQuery.All(tag));
            return entries.Count > 0 ? rng.WeightedPick(entries, e => e.Weight) : null;
        }

        /// <summary>Emits the slab and every wall run. Returns how many objects that took.</summary>
        static int EmitShell(
            BuildingDoc doc, FloorPlan plan, Shell shell, Storey storey, SlabGrid grid, Rect2 hole)
        {
            int placed = TileSlab(
                doc,
                grid,
                shell.Tile,
                shell.Margin,
                $"{storey.IdPrefix}/slab",
                storey.Metadata(),
                storey.SlabElevation,
                hole);

            for (int i = 0; i < plan.Walls.Count; i++)
            {
                placed += EmitWall(doc, plan, i, shell, storey);
            }

            return placed;
        }

        /// <summary>Stands one flight of stairs in the shaft. Returns how many objects that took.</summary>
        /// <remarks>
        /// One per storey, the top one included: the flight above the top floor is what reaches
        /// the roof, and the roof slab is cut for it exactly as every other slab is. A building
        /// whose parapet encloses somewhere nobody can get to would be a fence round a field.
        /// </remarks>
        static int EmitStairs(BuildingDoc doc, Stairwell stairs, Storey storey)
        {
            if (!stairs.Exists)
            {
                return 0;
            }

            doc.GeneratedObjects.Add(Structure(
                stairs.Entry,
                stairs.Position,
                stairs.QuarterTurns,
                $"{storey.IdPrefix}/stairs/flight",
                storey.Metadata(),
                storey.Elevation));

            return 1;
        }

        /// <summary>
        /// Caps the building: a final slab over the top storey, and a parapet round the edge of
        /// it. Returns how many objects that took.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built from the top storey's own slab art, because the roof is that storey's ceiling and
        /// a ceiling made of something else is a different building above the top floor. It is
        /// drawn from that floor's plan stream after the partition, so re-rolling the top storey
        /// re-roofs it and re-rolling any other storey does not.
        /// </para>
        /// <para>
        /// A catalog with no floor art gets no roof and therefore no parapet either. A parapet
        /// round a roof that was never laid is a fence in mid-air, and the shell is art the
        /// catalog either has or does not — the same bargain the walls and the doorways make.
        /// </para>
        /// </remarks>
        static int EmitRoof(
            BuildingDoc doc,
            FloorPlan plan,
            Shell shell,
            Stairwell stairs,
            Storey storey,
            SlabGrid grid,
            Catalog catalog,
            ref Rng rng)
        {
            if (shell.Tile == null)
            {
                return 0;
            }

            float slab = storey.CeilingElevation;
            Dictionary<string, string> metadata = RoofMetadata(slab);
            int placed = TileSlab(
                doc, grid, shell.Tile, shell.Margin, RoofIdPrefix + "/slab", metadata, slab,
                stairs.Flight);

            CatalogEntry parapet = Pick(catalog, ParapetTag, ref rng);
            float top = slab + shell.SlabHeight;

            // Round the slab that was just laid, not round the walls under it. The roof is what a
            // person stands on and falls off, and it is the tiles rather than the wall centrelines
            // that say where its edge is.
            placed += EmitParapet(doc, grid.Extent, parapet, top);
            placed += EmitStairwellRail(doc, grid.Extent, stairs, parapet, top, RoofIdPrefix);

            doc.Metadata[RoofHeightKey] = Number(
                shell.SlabHeight + (parapet != null ? parapet.Height : 0f));

            return placed;
        }

        /// <summary>
        /// Tiles a parapet round the four edges of the roof as one closed loop. Returns how many
        /// objects that took.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Round the <em>roof slab</em>, and on its own length rather than on the wall grid: a
        /// parapet is its own piece of art at its own size, and tiling it on a module it does not
        /// share would leave every piece either short of its neighbour or lapping over it.
        /// </para>
        /// <para>
        /// <strong>A run spans the whole side of the roof, and only its centreline is inset.</strong>
        /// That distinction is the whole fix, and getting it wrong is why the corners kept leaking.
        /// A run has to sit half a thickness in from the edge it guards, or its outer face hangs
        /// over the drop — but insetting it <em>along</em> its own travel as well shortens it at
        /// both ends by that same half thickness, and shortening it before counting whole pieces
        /// into it loses whatever else did not divide. Both losses land at the corners, which is
        /// exactly where the two runs were supposed to meet. So the inset applies across the run
        /// and the span is the full side, and the four runs close by burying each end in the middle
        /// of the next — the wall junction rule from <see cref="FloorPlan.Bounds"/>, one level up.
        /// </para>
        /// <para>
        /// The pieces are counted <strong>up</strong> for the same reason a railing's are: a run
        /// one piece short of its side is a gap in a fence, where a run a fraction of a piece long
        /// is an overhang nobody falls through. Art laid out on the same grid as the floor tile has
        /// neither — a metre-long parapet round a roof of metre tiles divides exactly — which is
        /// what the starter set in <see cref="ArenaAssetBuilder"/> is sized for.
        /// </para>
        /// </remarks>
        static int EmitParapet(BuildingDoc doc, Rect2 roof, CatalogEntry parapet, float elevation)
        {
            if (parapet == null || !(roof.Width > 0f) || !(roof.Depth > 0f))
            {
                return 0;
            }

            float piece = LongSide(parapet);
            float depth = ShortSide(parapet);
            if (!(piece > 0f))
            {
                return 0;
            }

            int countX = (int)MathF.Ceiling(roof.Width / piece - FitTolerance);
            int countZ = (int)MathF.Ceiling(roof.Depth / piece - FitTolerance);
            if (countX < 1 || countZ < 1)
            {
                return 0;
            }

            // What the runs are tiled along: the roof's own sides, grown to whole pieces about its
            // centre so a side the art does not divide overhangs evenly rather than falling short.
            Rect2 ring = Rect2.FromCenterSize(
                roof.Center, new Vec2(countX * piece, countZ * piece));

            // And across: half a thickness inside the edge, so each run's outer face is flush with
            // the roof rather than hanging off it.
            float inset = depth * 0.5f;

            int placed = EmitParapetRun(
                doc, parapet, Side(ParapetSegment, 0), true, roof.MinZ + inset, ring.MinX, countX, piece, elevation);
            placed += EmitParapetRun(
                doc, parapet, Side(ParapetSegment, 1), true, roof.MaxZ - inset, ring.MinX, countX, piece, elevation);
            placed += EmitParapetRun(
                doc, parapet, Side(ParapetSegment, 2), false, roof.MinX + inset, ring.MinZ, countZ, piece, elevation);
            placed += EmitParapetRun(
                doc, parapet, Side(ParapetSegment, 3), false, roof.MaxX - inset, ring.MinZ, countZ, piece, elevation);

            return placed;
        }

        /// <summary>The stable id prefix one run of fence on the roof lives under.</summary>
        static string Side(string what, int side) =>
            $"{RoofIdPrefix}/{what}_{side.ToString("00", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Tiles one side of the ring, from corner to corner. Returns how many objects that took.
        /// </summary>
        /// <param name="across">Where the run's centreline sits on the axis it does not travel.</param>
        /// <param name="start">Where the run begins on the axis it travels along.</param>
        static int EmitParapetRun(
            BuildingDoc doc,
            CatalogEntry parapet,
            string prefix,
            bool alongX,
            float across,
            float start,
            int count,
            float piece,
            float elevation)
        {
            int turns = QuarterTurnsAlong(parapet, alongX);

            for (int i = 0; i < count; i++)
            {
                float along = start + (i + 0.5f) * piece;
                Vec2 at = alongX ? new Vec2(along, across) : new Vec2(across, along);

                doc.GeneratedObjects.Add(Structure(
                    parapet,
                    PivotFor(parapet, turns, at),
                    turns,
                    $"{prefix}/piece_{i.ToString("00", CultureInfo.InvariantCulture)}",
                    RoofMetadata(elevation),
                    elevation));
            }

            return count;
        }

        /// <summary>
        /// Fences the flanks of the stairwell opening in the roof. Returns how many objects that
        /// took.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The opening is the one hole in a roof that is otherwise fenced all the way round, and
        /// what is under it is a flight dropping most of a storey — so a person walking the roof can
        /// step off the side of the stairs from any direction. This is the one place a ring of
        /// <c>structure/parapet</c> is worth standing round an opening: on a roof it is the art
        /// doing the job it was sized for, which is exactly why the same ring inside the building
        /// was taken out again — see ARCHITECTURE.md.
        /// </para>
        /// <para>
        /// <strong>The flanks and not the ends.</strong> One end of a flight is the way off it and
        /// the other is the drop, and nothing this tool can read says which — see
        /// <c>FUTURE.md</c> — so fencing both would be a fence between the roof and the only way
        /// down from it. A flank is a drop whichever way the flight climbs. A flight as wide as it
        /// runs does not say which of its sides are its flanks either, so it gets no rail at all
        /// rather than a rail across the head of the run.
        /// </para>
        /// <para>
        /// <strong>A flank against the edge of the roof is not railed.</strong> The rail stands
        /// outboard of the opening, so a flank close to the roof's own edge would put it inside the
        /// perimeter ring — two parapets over the same ground, with the coplanar faces that always
        /// come of that. Nothing is lost by leaving it out: what a person would fall over there is
        /// the perimeter, and the perimeter is already fenced. <see cref="PlanStairwell"/> keeps the
        /// opening off the edge in the first place, so this is the case that art coarser than the
        /// strip the shell stands on can still produce rather than the usual one.
        /// </para>
        /// </remarks>
        static int EmitStairwellRail(
            BuildingDoc doc, Rect2 roof, Stairwell stairs, CatalogEntry parapet, float elevation, string idPrefix)
        {
            if (parapet == null || !stairs.Exists)
            {
                return 0;
            }

            Rect2 hole = stairs.Flight;
            float piece = LongSide(parapet);
            float depth = ShortSide(parapet);
            if (!(piece > 0f) || MathF.Abs(hole.Width - hole.Depth) <= FitTolerance)
            {
                return 0;
            }

            // The run travels along the flight's own run, which is its longer side.
            bool alongX = hole.Width > hole.Depth;
            float span = alongX ? hole.Width : hole.Depth;
            int count = (int)MathF.Ceiling(span / piece - FitTolerance);
            if (count < 1)
            {
                return 0;
            }

            // Grown to whole pieces about the opening's own centre, so a flank the art does not
            // divide overhangs its ends evenly rather than falling short at one of them.
            Vec2 middle = hole.Center;
            float start = (alongX ? middle.X : middle.Y) - count * piece * 0.5f;

            // Half a depth outboard of the opening, so the rail's inner face is the edge a person
            // would otherwise walk over.
            float low = (alongX ? hole.MinZ : hole.MinX) - depth * 0.5f;
            float high = (alongX ? hole.MaxZ : hole.MaxX) + depth * 0.5f;

            // The roof inside its own ring: a run has to fit in here whole, ends included, or it
            // is standing in the perimeter rather than beside the opening.
            Rect2 inside = roof.Expanded(FitTolerance - depth);

            int placed = 0;
            Rect2 run = RunRect(alongX, low, start, count * piece, depth);
            if (inside.Contains(run))
            {
                placed += EmitParapetRun(
                    doc, parapet, $"{idPrefix}/{StairwellRailSegment}_00", alongX, low, start, count, piece,
                    elevation);
            }

            run = RunRect(alongX, high, start, count * piece, depth);
            if (inside.Contains(run))
            {
                placed += EmitParapetRun(
                    doc, parapet, $"{idPrefix}/{StairwellRailSegment}_01", alongX, high, start, count, piece,
                    elevation);
            }

            return placed;
        }

        /// <summary>The ground one run of fence covers, pieces counted up and all.</summary>
        static Rect2 RunRect(bool alongX, float across, float start, float length, float depth) =>
            alongX
                ? new Rect2(start, across - depth * 0.5f, start + length, across + depth * 0.5f)
                : new Rect2(across - depth * 0.5f, start, across + depth * 0.5f, start + length);

        /// <summary>What every object on the roof carries, since the roof is no storey's floor.</summary>
        static Dictionary<string, string> RoofMetadata(float elevation) =>
            new Dictionary<string, string>
            {
                { FloorKey, RoofFloor },
                { ElevationKey, elevation.ToString("R", CultureInfo.InvariantCulture) },
            };

        /// <summary>
        /// Tiles the floor slab across the plan, one piece per cell of the slab's own grid.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On the slab piece's own footprint rather than on the wall's module. Tiling the floor on
        /// the wall grid is only right when the art pack happens to make its floor tile exactly as
        /// wide as its wall is long; when it does not, every tile either misses its neighbour or
        /// laps over it — and two slabs laid over the same ground have coplanar top faces, which
        /// is the flicker that reads as the floor breaking up as you walk over it.
        /// </para>
        /// <para>
        /// The grid is centred in the plan, so a tile that does not divide the floor exactly
        /// leaves the same sliver of bare ground on both sides rather than a whole tile's worth
        /// against one wall. A sliver is a gap, and a gap is the failure the art pack can be seen
        /// to have caused; an overlap is one the tool would appear to have caused.
        /// </para>
        /// </remarks>
        /// <param name="hole">
        /// The ground the flight itself covers, to be left open — or an empty rectangle on a storey
        /// with nothing coming up through it. A tile that would lap it is left out rather than
        /// shrunk, because nothing here authors geometry; what the coarse tile had to give up to
        /// do that is floored again by <see cref="FloorTheMargin"/>.
        /// </param>
        /// <param name="margin">
        /// The finest floor tile the catalog has, laid in whatever the slab tile gave up around the
        /// opening, or null when the catalog offers nothing finer.
        /// </param>
        static int TileSlab(
            BuildingDoc doc,
            SlabGrid grid,
            CatalogEntry tile,
            CatalogEntry margin,
            string idPrefix,
            Dictionary<string, string> metadata,
            float elevation,
            Rect2 hole)
        {
            if (tile == null || !grid.Exists)
            {
                return 0;
            }

            bool cutting = hole.Width > 0f && hole.Depth > 0f;

            int placed = 0;
            for (int z = 0; z < grid.CountZ; z++)
            {
                for (int x = 0; x < grid.CountX; x++)
                {
                    Vec2 at = grid.CentreOf(x, z);
                    if (cutting && grid.CellAt(x, z).Overlaps(hole))
                    {
                        continue;
                    }

                    doc.GeneratedObjects.Add(Structure(
                        tile,
                        PivotFor(tile, 0, at),
                        0,
                        $"{idPrefix}/tile_{placed.ToString("000", CultureInfo.InvariantCulture)}",
                        metadata,
                        elevation));
                    placed++;
                }
            }

            if (!cutting)
            {
                return placed;
            }

            return placed + FloorTheMargin(
                doc, grid, tile, margin, idPrefix, metadata, elevation, hole, placed);
        }

        /// <summary>
        /// Floors the strip between the whole tiles the opening cost and the opening itself, from
        /// the finest tile the catalog has. Returns how many pieces that took.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A slab is laid in whole pieces, so a tile whose joints do not fall on the edge of the
        /// opening has to give up every piece the flight so much as laps — and on a two-metre tile
        /// a three-metre flight gives up four metres. The metre it cannot reach is not slack in a
        /// corner: it is the floor at the top and at the foot of the stairs, the one square you have
        /// to be able to stand on for a stairwell to be a way up rather than a hole to fall down.
        /// </para>
        /// <para>
        /// So the slab tile keeps its own grid, and the cells it gave up are tiled again from the
        /// finest tile the catalog offers, on a grid laid over exactly those cells. The two passes
        /// cannot overlap, because that region is the cells the first one skipped and nothing else.
        /// A catalog with one floor tile has nothing finer to reach for and lays its slab exactly as
        /// it did before this existed.
        /// </para>
        /// <para>
        /// The margin is laid so its <em>top</em> face is level with the slab's rather than its
        /// underside, because what a person walks on is the surface: two tiles of different
        /// thicknesses stood on one base is a step at the top of the flight, which would be moving
        /// the fault this exists to remove rather than removing it.
        /// </para>
        /// </remarks>
        static int FloorTheMargin(
            BuildingDoc doc,
            SlabGrid grid,
            CatalogEntry tile,
            CatalogEntry margin,
            string idPrefix,
            Dictionary<string, string> metadata,
            float elevation,
            Rect2 hole,
            int firstId)
        {
            if (margin == null)
            {
                return 0;
            }

            // Clipped to the grid, because a flight anchored on the grid the building planned for
            // can reach past the one a storey that drew a different tile actually laid — and floor
            // out there is floor hanging off the side of the building.
            Rect2 extent = grid.Extent;
            Rect2 cover = grid.Cover(hole);
            cover = new Rect2(
                MathF.Max(cover.MinX, extent.MinX), MathF.Max(cover.MinZ, extent.MinZ),
                MathF.Min(cover.MaxX, extent.MaxX), MathF.Min(cover.MaxZ, extent.MaxZ));

            // The opening already falls on the slab's own joints, so it gave nothing up.
            if (Same(cover, hole))
            {
                return 0;
            }

            SlabGrid fine = SlabGridOver(cover, margin);
            if (!fine.Exists)
            {
                return 0;
            }

            float at = elevation + tile.Height - margin.Height;

            int placed = 0;
            for (int z = 0; z < fine.CountZ; z++)
            {
                for (int x = 0; x < fine.CountX; x++)
                {
                    if (fine.CellAt(x, z).Overlaps(hole))
                    {
                        continue;
                    }

                    doc.GeneratedObjects.Add(Structure(
                        margin,
                        PivotFor(margin, 0, fine.CentreOf(x, z)),
                        0,
                        $"{idPrefix}/tile_{(firstId + placed).ToString("000", CultureInfo.InvariantCulture)}",
                        metadata,
                        at));
                    placed++;
                }
            }

            return placed;
        }

        /// <summary>True when two rectangles agree to within <see cref="FitTolerance"/>.</summary>
        static bool Same(Rect2 a, Rect2 b) =>
            MathF.Abs(a.MinX - b.MinX) <= FitTolerance && MathF.Abs(a.MinZ - b.MinZ) <= FitTolerance &&
            MathF.Abs(a.MaxX - b.MaxX) <= FitTolerance && MathF.Abs(a.MaxZ - b.MaxZ) <= FitTolerance;

        /// <summary>
        /// Tiles one run, putting the doorway piece in the module the plan left open for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A catalog with walls but no doorway art leaves that module empty rather than walling it
        /// up. An opening you can walk through is the point of the doorway; the frame around it is
        /// decoration the catalog either has or does not.
        /// </para>
        /// <para>
        /// Both pieces are stretched to the storey's headroom, which is the one place in this
        /// generator that rescales art. A wall is the only thing here whose height is set by a
        /// parameter rather than by the catalog — what it has to meet is the underside of the slab
        /// above, and where that is comes from the floor height the user typed. Left at its own
        /// size it is right only when the art pack and the floor height were chosen for each
        /// other, and wrong everywhere else by a strip of daylight running the length of every wall
        /// in the building. The doorway goes with it because the two stand in one run: a frame at
        /// its own height in a wall fitted to another is a step in the top of the wall.
        /// </para>
        /// </remarks>
        static int EmitWall(BuildingDoc doc, FloorPlan plan, int index, Shell shell, Storey storey)
        {
            PlanWall wall = plan.Walls[index];
            string prefix = $"{storey.IdPrefix}/wall_{index.ToString("00", CultureInfo.InvariantCulture)}";
            bool[] windows = shell.Window != null && index < FloorPlan.PerimeterRuns
                ? WindowsOn(plan, wall)
                : null;
            int placed = 0;

            for (int module = 0; module < wall.Modules; module++)
            {
                bool doorway = module == wall.Doorway;
                bool window = windows != null && windows[module];

                CatalogEntry entry = doorway ? shell.Doorway : window ? shell.Window : shell.Wall;
                if (entry == null)
                {
                    continue;
                }

                string name = doorway ? "doorway" : window ? "window" : "module";
                int turns = QuarterTurnsAlong(entry, wall.AlongX);
                doc.GeneratedObjects.Add(Structure(
                    entry,
                    PivotFor(entry, turns, wall.ModuleCenter(module)),
                    turns,
                    $"{prefix}/{name}_{module.ToString("00", CultureInfo.InvariantCulture)}",
                    storey.Metadata(),
                    storey.Elevation,
                    StretchTo(entry, shell.Headroom)));
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// Which modules of an outside run are windows: the middle one of each room's stretch of
        /// it, and no others.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>One window to a room, on each side of the building that room reaches.</strong>
        /// Windows are the one part of the shell where the obvious rule — put one wherever it
        /// fits — is the wrong one. A module is two metres, so a facade of glass every two metres
        /// is not a building, and every rule that spaces them by counting modules instead (every
        /// second one, never two adjacent) spaces them evenly against a wall that is not evenly
        /// divided: the count runs on past the corner of one room into the next, and where a
        /// window lands in a room comes out of arithmetic about the whole side rather than
        /// anything about the room. So the run is walked in the rooms behind it, and each room's
        /// stretch of the outside wall gets one window in the middle of it. A large room gets a
        /// window, a small room gets a window, and a room that turns a corner gets one on each
        /// side — which is what a person would draw.
        /// </para>
        /// <para>
        /// <strong>Three modules are refused outright, and a room that wanted one of them goes
        /// without.</strong> Not shifted along to the next module: a window that is not in the
        /// middle of the room's stretch is not the middle of anything, and a rule that says so is
        /// one nobody can predict by looking at the building.
        /// </para>
        /// <para>
        /// The <em>ends</em> of a run, for the reason <see cref="FloorPlan"/> keeps a doorway out
        /// of them: a run ends buried in the run it meets, so the end module is the corner of the
        /// building, and the corner is shared with the run coming the other way. Two rooms two
        /// modules deep in one corner would otherwise put a window on each side of it and leave a
        /// tenth of a metre of wall holding the corner up. It is also what makes the rule below
        /// enough on its own — a window at the end of one run cannot reach a window on another,
        /// because neither run puts one there.
        /// </para>
        /// <para>
        /// A module <em>any</em> doorway on the floor reaches, and not only the one in this run.
        /// The obvious half of that is the way into the building, because a room whose facade has
        /// the front door in the middle of it does not also need a window there. The half that is
        /// not obvious is the interior door: a cut ends buried in the outside wall it runs up to,
        /// so a cut two modules long with its door in the end module puts that door inside the
        /// outside wall — and a window in the same module of that wall is a window standing in a
        /// doorway. Rare and entirely visible when it happens, and cheap to rule out by measuring
        /// the rectangles rather than by comparing module indices that belong to different runs.
        /// </para>
        /// <para>
        /// And a module <em>next to a window already placed</em>. Two rooms one module wide,
        /// side by side, each want their one window and the two come out adjacent — four metres of
        /// uninterrupted glass, which is the look this rule exists to avoid whatever the reason for
        /// it. The first along the run keeps its window, which is a tie-break rather than a
        /// judgement: there is nothing to choose between two rooms with the same claim, and the
        /// alternative is refusing both.
        /// </para>
        /// <para>
        /// No draw is taken. Which room is behind which module is a fact about the partition that
        /// has already been made, so windows are a reading of the plan rather than a stage with a
        /// stream of its own — which is what lets the same building acquire windows without moving
        /// anything else in it.
        /// </para>
        /// </remarks>
        static bool[] WindowsOn(FloorPlan plan, PlanWall wall)
        {
            var windows = new bool[wall.Modules];
            List<Rect2> doorways = Doorways(plan);

            int start = 0;
            int room = RoomBehind(plan, wall, 0);

            // Far enough back that the first module is not treated as being next to anything.
            int last = -2;

            for (int module = 1; module <= wall.Modules; module++)
            {
                // Past the end of the run counts as a different room, which is what closes the
                // last stretch without repeating the body below.
                int here = module < wall.Modules ? RoomBehind(plan, wall, module) : NoRoom;
                if (here == room)
                {
                    continue;
                }

                int middle = (start + module - 1) / 2;
                if (room != NoRoom &&
                    middle > 0 && middle < wall.Modules - 1 &&
                    middle != wall.Doorway &&
                    !MeetsADoorway(plan, wall, middle, doorways) &&
                    middle - last > 1)
                {
                    windows[middle] = true;
                    last = middle;
                }

                start = module;
                room = here;
            }

            return windows;
        }

        /// <summary>True if any doorway on the floor reaches into one module of a run.</summary>
        /// <remarks>
        /// The module's own rectangle against each doorway's, which is the same comparison a
        /// person makes looking at the two pieces. Touching is not reaching: a doorway in the next
        /// module along shares an edge with this one and stands entirely beside it.
        /// </remarks>
        static bool MeetsADoorway(
            FloorPlan plan, PlanWall wall, int module, IReadOnlyList<Rect2> doorways)
        {
            Rect2 at = Rect2.FromCenterSize(
                wall.ModuleCenter(module),
                wall.AlongX
                    ? new Vec2(wall.Module, plan.Thickness)
                    : new Vec2(plan.Thickness, wall.Module));

            for (int i = 0; i < doorways.Count; i++)
            {
                if (at.Overlaps(doorways[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Which room one module of an outside run backs onto, or <see cref="NoRoom"/>.
        /// </summary>
        /// <remarks>
        /// Probed a quarter of a module inside the run rather than on its centreline, because the
        /// centreline is the boundary the rooms are measured to and a point on it belongs to
        /// nothing. A quarter of a module is inside the shallowest leaf the partition can produce,
        /// which is one module deep, and it never lands on a cut: cuts fall on module boundaries
        /// and this is at a module's middle on one axis and a quarter of one on the other.
        /// </remarks>
        static int RoomBehind(FloorPlan plan, PlanWall wall, int module)
        {
            float step = wall.Module * 0.25f;
            Vec2 centre = wall.ModuleCenter(module);
            Vec2 middle = plan.Bounds.Center;

            Vec2 at = wall.AlongX
                ? new Vec2(centre.X, centre.Y + (centre.Y < middle.Y ? step : -step))
                : new Vec2(centre.X + (centre.X < middle.X ? step : -step), centre.Y);

            for (int i = 0; i < plan.Rooms.Count; i++)
            {
                if (plan.Rooms[i].Bounds.Contains(at))
                {
                    return i;
                }
            }

            return NoRoom;
        }

        /// <summary>How far to turn a piece so its long axis lies along the run.</summary>
        static int QuarterTurnsAlong(CatalogEntry entry, bool alongX) =>
            (entry.Footprint.Width >= entry.Footprint.Depth) == alongX ? 0 : 1;

        /// <summary>
        /// Where a piece's pivot goes so that its footprint lands centred on <paramref name="at"/>.
        /// </summary>
        /// <remarks>
        /// The shell is laid out as art rather than as pivots: a slab tile belongs in the middle of
        /// its cell and a wall module in the middle of its module, and where the prefab's own origin
        /// happens to sit is not something the plan should have to know. For art modelled around its
        /// pivot this is the identity, which is why nothing moved when it arrived; for art that is
        /// not, it is the difference between a wall on its line and a wall beside it.
        /// </remarks>
        static Vec2 PivotFor(CatalogEntry entry, int quarterTurns, Vec2 at) =>
            at - QuarterTurn.Rotate(entry.Footprint, quarterTurns).Center;

        static float LongSide(CatalogEntry entry) =>
            MathF.Max(entry.Footprint.Width, entry.Footprint.Depth);

        static float ShortSide(CatalogEntry entry) =>
            MathF.Min(entry.Footprint.Width, entry.Footprint.Depth);

        /// <summary>One piece of the shell, raised to its storey and stretched to fit it.</summary>
        /// <param name="verticalScale">
        /// How far to stretch the piece along its own Y, from <see cref="StretchTo"/>. One for
        /// everything that is not fitted to a ceiling, which is everything but the wall runs.
        /// </param>
        /// <remarks>
        /// The lift that stands a piece on its own base is scaled with it. <see cref="Placement"/>
        /// puts the pivot a <see cref="CatalogEntry.BaseOffset"/> up so the underside of the art
        /// lands on the floor; stretch the art and it reaches that far times the factor below its
        /// pivot instead, so a pivot left at the unscaled offset would sink a squashed piece into
        /// the floor and hang a stretched one over it. Art modelled on its own base has no offset
        /// and is unaffected either way.
        /// </remarks>
        static PlacedObject Structure(
            CatalogEntry entry,
            Vec2 position,
            int quarterTurns,
            string id,
            Dictionary<string, string> metadata,
            float elevation,
            float verticalScale = 1f)
        {
            Placement placement = Placement.AtQuarterTurn(entry, position, quarterTurns);
            Pose pose = placement.Pose;
            var at = new Vec3(
                pose.Position.X, pose.Position.Y * verticalScale + elevation, pose.Position.Z);

            return new PlacedObject(
                id,
                placement.LogicalId,
                new Pose(at, pose.Rotation, pose.Scale, verticalScale),
                TagArray(placement.Tags),
                metadata);
        }

        // --- contents ----------------------------------------------------------------------

        /// <summary>
        /// Furnishes one room: its centrepieces, then cover scattered over what is left of the
        /// floor, then decor in its corners. Returns how many objects the density asked for, all
        /// three stages together.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three stages rather than one pass over a mixed list, because they are not the same
        /// question. A centrepiece is proposed into the middle of the room and nowhere else,
        /// cover is proposed anywhere the floor is open and judged on whether it fits, and decor
        /// is only ever proposed into a corner — a sofa marooned in the middle of a room reads as
        /// a mistake in a way a crate never does, and so does a pot plant standing there. They
        /// draw from separate streams for the reason every other pair of stages here does, and
        /// each is handed everything the ones before it committed so nothing ends up inside
        /// anything else.
        /// </para>
        /// <para>
        /// <strong>The centrepiece goes down first</strong>, where decor goes down last. It is
        /// what the room is arranged around, so it takes the floor it needs and the cover scatters
        /// into what is left; running it last would have it competing for the middle of the room
        /// with whatever the sampler happened to drop there, and losing about as often as not. It
        /// costs nothing to any building that already exists: a catalog with no
        /// <see cref="CentrepieceTag"/> art in it places nothing here, and a stage that places
        /// nothing commits nothing and draws nothing.
        /// </para>
        /// </remarks>
        static int FurnishRoom(
            BuildingDoc doc,
            PlanRoom room,
            FloorPlan plan,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<CatalogEntry> decor,
            IReadOnlyList<CatalogEntry> centrepieces,
            Stairwell stairs,
            BuildingParams parameters,
            Storey storey,
            PlacementStats stats)
        {
            string idPrefix = $"{storey.IdPrefix}/{room.Id}";
            var committed = new List<Placement>();

            // The stairwell is on the floor before anything is proposed onto it. Committing it
            // rather than declaring it a doorway is exact: it is something standing in a room, and
            // NoOverlap is the rule that says so.
            //
            // What is committed is Stairwell.Landing — the whole opening, plus the floor round it
            // somebody needs to get to the stairs. Not the flight standing in it: on every storey
            // above the ground the difference between the two is a hole, and a crate proposed into
            // a hole is a crate falling through the floor. And not the opening alone either, which
            // is what this used to commit: it left the way upstairs choked with the room's own
            // scatter, standing flush against the bottom step on every side.
            if (stairs.Exists)
            {
                committed.Add(new Placement(
                    stairs.Entry.LogicalId,
                    Pose.At(stairs.Landing.Center.ToVec3(storey.Elevation)),
                    stairs.Landing,
                    stairs.Entry.Tags));
            }

            // Before a single thing is proposed. Which floor has to stay walkable is decided by
            // where the room's doors are, so it is settled by the plan rather than negotiated
            // against whatever the cover pass happened to put down first — the two stages here
            // compete for the same floor, and the route is the one that is not allowed to lose.
            IReadOnlyList<Rect2> paths = room.Paths;

            int target = CentreRoom(
                doc, room, plan, doorways, paths, centrepieces, parameters, storey, stats, idPrefix,
                committed);

            target += FillRoom(
                doc, room, plan, doorways, paths, entries, parameters, storey, stats, idPrefix,
                committed);

            return target + DecorateRoom(
                doc, room, plan, doorways, paths, decor, parameters, storey, stats, idPrefix,
                committed);
        }

        /// <summary>Scatters cover across one room. Returns how many objects it was asked for.</summary>
        static int FillRoom(
            BuildingDoc doc,
            PlanRoom room,
            FloorPlan plan,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> paths,
            IReadOnlyList<CatalogEntry> entries,
            BuildingParams parameters,
            Storey storey,
            PlacementStats stats,
            string idPrefix,
            List<Placement> committed)
        {
            // A whole thickness rather than the half of one that actually stands in the room: a
            // hundredth of a metre of floor is a cheap price for not having to care which side of
            // a line a given wall belongs to.
            Rect2 interior = room.Bounds.Expanded(-(plan.Thickness + parameters.WallMargin));
            if (interior.Width < parameters.GridSize || interior.Depth < parameters.GridSize)
            {
                // A room smaller than one cell of the grid its contents would snap to.
                return 0;
            }

            var roomGrid = new ArenaGrid(interior, parameters.GridSize);
            float radius = SmallestRadius(entries);
            float margin = radius + parameters.GridSize * 0.5f;

            Rect2 sampleArea = interior.Expanded(-margin);
            if (sampleArea.Width <= 0f || sampleArea.Depth <= 0f)
            {
                // A room smaller than the smallest thing that could stand in it.
                return 0;
            }

            var grid = new PlacementGrid(roomGrid);
            var openCells = new List<Vec2>();
            grid.CollectOpenCentres(sampleArea, openCells);
            if (openCells.Count == 0)
            {
                return 0;
            }

            // Measured against the room's clear floor rather than against the positions a pivot
            // may occupy. The two are nearly the same over a lane and nothing like each other in
            // a four-metre room, where the margin a prop needs from the walls is most of the
            // room — and it is the floor, not the set of legal pivots, that the density means.
            int target = TargetCount(
                interior.Area, parameters.ContentDensity, BaselineContentPerSquareMetre);
            if (target <= 0)
            {
                return 0;
            }

            float openArea = openCells.Count * roomGrid.CellArea;

            // One stream per room, as a lane has one of its own in CoverPlacer: what a room holds
            // is then a function of that room and the floor's seed, and nothing else on the floor
            // can shift it.
            Rng stream = new Rng(storey.Seed).Fork(ContentsLabel + room.Id);

            ConstraintSet constraints = RoomConstraints(
                interior,
                new[]
                {
                    PlacementConstraint.OnGrid(parameters.GridSize),
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.NotBlockingDoorway(DoorwayClearance),
                    PlacementConstraint.OffReservedPath(),
                    PlacementConstraint.NoOverlap(ContentMargin),
                },
                doorways,
                paths,
                committed);

            float minSpacing = 2f * radius + ContentMargin;
            float sampleRadius = MathF.Max(
                minSpacing, MathF.Sqrt(PoissonYield * openArea / (target * SampleSurplus)));

            List<Vec2> positions = PoissonDisk.Sample(grid, sampleArea, sampleRadius, target * 4, ref stream);
            Shuffle(positions, ref stream);

            int budget = target * AttemptsPerTargetObject;
            int spent = 0;
            int placed = 0;

            for (int i = 0; i < positions.Count && placed < target && spent < budget; i++)
            {
                Vec2 position = roomGrid.Snap(positions[i]);

                for (int attempt = 0; attempt < EntriesPerPosition && spent < budget; attempt++)
                {
                    CatalogEntry entry = stream.WeightedPick(entries, e => e.Weight);
                    Placement candidate = Placement.AtQuarterTurn(
                        entry, position, stream.NextRange(0, QuarterTurn.Count));

                    // Turned by the wall it landed against, and left as it was drawn where there is
                    // no wall in reach. The draw is taken either way: which piece comes next out of
                    // a room's stream is not allowed to depend on where the last one happened to
                    // fall, or a seed would mean two different things on two runs of the same room.
                    int facing = WallFacing.Against(
                        interior,
                        position,
                        WallFacing.Radius(entry.Footprint) + WallFacingReach);

                    if (facing != WallFacing.Free)
                    {
                        candidate = Placement.AtQuarterTurn(entry, position, facing);
                    }

                    ConstraintResult result = constraints.Evaluate(candidate);
                    stats.Record(result);
                    spent++;

                    if (!result.IsOk)
                    {
                        continue;
                    }

                    constraints.Commit(candidate);
                    committed.Add(candidate);

                    Emit(doc, candidate, $"{idPrefix}/cover_", placed, room.Id, storey);
                    placed++;
                    break;
                }
            }

            return target;
        }

        /// <summary>
        /// Puts one piece of decor in each corner of a room. Returns how many pieces the density
        /// asked for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Corners and nothing else. What a workspace files in
        /// <c>Props/PropBuilding/Decor/Decoration</c> is the small stuff — a plant, a bin, a
        /// standard lamp — and a room reads as furnished when there is one of them in the corner
        /// and as cluttered when there is a row of them down the wall. The wall itself is not a
        /// second-best corner; it is where nothing goes, and the folder above this art says so.
        /// </para>
        /// <para>
        /// <strong>A corner is furnished at most once.</strong>
        /// <see cref="ConstraintKind.InCorner"/> says a piece has two walls within reach of it and
        /// says nothing about <em>which</em> two, so a pass that only checked the rule could stand
        /// its whole allowance in one corner — four plants a third of a metre apart, every one of
        /// them legally in a corner. The four are walked instead, in an order the room's own stream
        /// draws, and each is offered the open floor nearest it.
        /// </para>
        /// <para>
        /// The area is the room inset by one wall thickness rather than by the wall margin the
        /// cover uses. That margin exists to keep cover off the walls; decor's whole job is to be
        /// against them, and a plant half a metre out from the corner is a plant somebody has moved
        /// to hoover behind.
        /// </para>
        /// </remarks>
        static int DecorateRoom(
            BuildingDoc doc,
            PlanRoom room,
            FloorPlan plan,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> paths,
            IReadOnlyList<CatalogEntry> decor,
            BuildingParams parameters,
            Storey storey,
            PlacementStats stats,
            string idPrefix,
            List<Placement> committed)
        {
            if (decor.Count == 0)
            {
                return 0;
            }

            Rect2 area = room.Bounds.Expanded(-plan.Thickness);
            if (area.Width < parameters.GridSize || area.Depth < parameters.GridSize)
            {
                return 0;
            }

            int target = Math.Min(
                TargetCount(area.Area, parameters.ContentDensity, BaselineDecorPerSquareMetre),
                CornerDecorLimit);
            if (target <= 0)
            {
                return 0;
            }

            var grid = new ArenaGrid(area, parameters.GridSize);
            var positions = new List<Vec2>();
            new PlacementGrid(grid).CollectOpenCentres(area, positions);
            if (positions.Count == 0)
            {
                return 0;
            }

            Rng stream = new Rng(storey.Seed).Fork(DecorLabel + room.Id);

            // Which corner is furnished first, when the density asks for fewer than four. Drawn
            // rather than fixed, or every room in the building would fill the same corner and leave
            // the one diagonally opposite it bare.
            List<Vec2> corners = Corners(area);
            Shuffle(corners, ref stream);

            ConstraintSet constraints = RoomConstraints(
                area,
                new[]
                {
                    PlacementConstraint.OnGrid(parameters.GridSize),
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.InCorner(DecorReach),
                    PlacementConstraint.NotBlockingDoorway(DoorwayClearance),
                    PlacementConstraint.OffReservedPath(),
                    PlacementConstraint.NoOverlap(DecorMargin),
                },
                doorways,
                paths,
                committed);

            // No cell further than this from a corner can reach two of its walls, whatever art is
            // drawn and however it is turned, so offering one would only be a rejection nobody
            // could have read anything into.
            float zone = LargestExtent(decor) + DecorReach;

            int placed = 0;
            for (int i = 0; i < corners.Count && placed < target; i++)
            {
                if (PlaceInCorner(
                        doc, constraints, grid, area, positions, corners[i], zone, decor, idPrefix,
                        placed, room.Id, storey, stats, ref stream, committed))
                {
                    placed++;
                }
            }

            return target;
        }

        /// <summary>
        /// Stands one piece of decor in one corner, or nothing when none of the floor near it will
        /// take a piece. Returns whether it stood one up.
        /// </summary>
        /// <remarks>
        /// The open cells near the corner are offered nearest first, so a piece ends up as far into
        /// the corner as the art and the grid allow. <see cref="ConstraintKind.InCorner"/> is still
        /// the rule that accepts or refuses each one — pre-filtering to cells that must satisfy it
        /// would leave the rule in the evaluation order reporting no rejections, which reads as a
        /// rule that passed rather than one that was never asked.
        /// </remarks>
        static bool PlaceInCorner(
            BuildingDoc doc,
            ConstraintSet constraints,
            ArenaGrid grid,
            Rect2 area,
            IReadOnlyList<Vec2> positions,
            Vec2 corner,
            float zone,
            IReadOnlyList<CatalogEntry> entries,
            string idPrefix,
            int index,
            string roomId,
            Storey storey,
            PlacementStats stats,
            ref Rng stream,
            List<Placement> committed)
        {
            List<Vec2> near = Nearest(positions, corner, zone);

            for (int i = 0; i < near.Count; i++)
            {
                Vec2 position = grid.Snap(near[i]);

                // One turn and no draw, taken from where the piece actually stands rather than from
                // the corner this pass was walking. Which way a piece in a corner faces is settled
                // by the corner — there is exactly one of the four that puts its back to a wall and
                // a corner piece's second back to the other — so drawing one and hoping would be
                // three rejections in four for the pieces that have two backs, and a plant with its
                // face in the plaster for the ones that have one.
                //
                // The corner it stands in is not always the corner it was offered. The zone above
                // is sized off the largest piece of decor in the catalog, so one big piece widens
                // the offer for every piece — and art modelled well off its own pivot widens it to
                // the whole room. A piece that then lands in the far corner and carries this
                // corner's turn is furniture with its back to open floor and its face in the wall,
                // which is exactly the failure the rule exists to stop.
                int facing = WallFacing.IntoCorner(area, position);

                for (int attempt = 0; attempt < EntriesPerPosition; attempt++)
                {
                    CatalogEntry entry = stream.WeightedPick(entries, e => e.Weight);
                    Placement candidate = Placement.AtQuarterTurn(entry, position, facing);

                    ConstraintResult result = constraints.Evaluate(candidate);
                    stats.Record(result);

                    if (!result.IsOk)
                    {
                        continue;
                    }

                    constraints.Commit(candidate);
                    committed.Add(candidate);

                    Emit(doc, candidate, idPrefix + "/decor_", index, roomId, storey);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Stands the room's centrepieces in the middle of it. Returns how many the density asked
        /// for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The middle of the room, taken literally: the open cells are offered in order of how
        /// close they are to the centre of the floor, so the first piece lands as near the middle
        /// as anything will fit and the next ones settle around it. What
        /// <see cref="ConstraintKind.InCentre"/> then guarantees is the part the ordering cannot —
        /// that a room whose middle is spoken for does not quietly furnish its edge instead.
        /// </para>
        /// <para>
        /// The walkways win, as they win against everything: the chain of strips a plan reserves
        /// between a room's doors runs through exactly the floor a centrepiece wants, and a table
        /// across the only route between two doors is the failure that reservation exists to stop.
        /// So a room whose doors face each other gets its table beside the route rather than on it,
        /// and a room with no floor left over gets none — which is the right answer rather than a
        /// shortfall to be tuned away.
        /// </para>
        /// <para>
        /// The spacing is <see cref="ContentMargin"/> rather than <see cref="DecorMargin"/>. Two
        /// plants may stand shoulder to shoulder in a corner; two tables in the middle of a room
        /// with a third of a metre between them are one table with a gap in it.
        /// </para>
        /// </remarks>
        static int CentreRoom(
            BuildingDoc doc,
            PlanRoom room,
            FloorPlan plan,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> paths,
            IReadOnlyList<CatalogEntry> entries,
            BuildingParams parameters,
            Storey storey,
            PlacementStats stats,
            string idPrefix,
            List<Placement> committed)
        {
            if (entries.Count == 0)
            {
                return 0;
            }

            Rect2 area = room.Bounds.Expanded(-plan.Thickness);
            if (area.Width < parameters.GridSize || area.Depth < parameters.GridSize)
            {
                return 0;
            }

            // Off the room's whole floor rather than off the middle of it, for the reason the cover
            // target is measured that way: how furnished a room is is a fact about the room, and
            // measuring it on the strip left over after the clearance would ask a small room for
            // nothing at all.
            int target = TargetCount(
                area.Area, parameters.ContentDensity, BaselineCentrepiecePerSquareMetre);
            if (target <= 0)
            {
                return 0;
            }

            var grid = new ArenaGrid(area, parameters.GridSize);
            var positions = new List<Vec2>();

            // A pivot outside this cannot carry a footprint that keeps the clearance, so the cells
            // outside it are not offered. The rule still refuses the ones inside it the art is too
            // wide for, which in a small room is most of what it refuses.
            new PlacementGrid(grid).CollectOpenCentres(area.Expanded(-CentrepieceClearance), positions);
            if (positions.Count == 0)
            {
                return 0;
            }

            SortByDistanceFrom(positions, area.Center);

            Rng stream = new Rng(storey.Seed).Fork(CentreLabel + room.Id);

            ConstraintSet constraints = RoomConstraints(
                area,
                new[]
                {
                    PlacementConstraint.OnGrid(parameters.GridSize),
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.InCentre(CentrepieceClearance),
                    PlacementConstraint.NotBlockingDoorway(DoorwayClearance),
                    PlacementConstraint.OffReservedPath(),
                    PlacementConstraint.NoOverlap(ContentMargin),
                },
                doorways,
                paths,
                committed);

            int placed = 0;

            for (int i = 0; i < positions.Count && placed < target; i++)
            {
                Vec2 position = grid.Snap(positions[i]);

                for (int attempt = 0; attempt < EntriesPerPosition; attempt++)
                {
                    CatalogEntry entry = stream.WeightedPick(entries, e => e.Weight);
                    Placement candidate = Placement.AtQuarterTurn(
                        entry, position, stream.NextRange(0, QuarterTurn.Count));

                    ConstraintResult result = constraints.Evaluate(candidate);
                    stats.Record(result);

                    if (!result.IsOk)
                    {
                        continue;
                    }

                    constraints.Commit(candidate);
                    committed.Add(candidate);

                    Emit(doc, candidate, idPrefix + "/centrepiece_", placed, room.Id, storey);
                    placed++;
                    break;
                }
            }

            return target;
        }

        /// <summary>
        /// A set over one room's floor, told about the doorways, the walkways between them, and
        /// what is already down.
        /// </summary>
        static ConstraintSet RoomConstraints(
            Rect2 area,
            IReadOnlyList<PlacementConstraint> rules,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> paths,
            IReadOnlyList<Placement> committed)
        {
            var constraints = new ConstraintSet(area, rules);

            for (int i = 0; i < doorways.Count; i++)
            {
                constraints.AddDoorway(doorways[i]);
            }

            for (int i = 0; i < paths.Count; i++)
            {
                constraints.AddReservedPath(paths[i]);
            }

            for (int i = 0; i < committed.Count; i++)
            {
                constraints.Commit(committed[i]);
            }

            return constraints;
        }

        /// <summary>
        /// Writes an accepted candidate into the document, raised to its storey.
        /// </summary>
        /// <remarks>
        /// The constraints are evaluated flat, on the ground, because every one of them is a
        /// statement about a footprint; the storey is added here, on the way out. Two floors of one
        /// building never meet in a rule, and never need to.
        /// </remarks>
        static void Emit(
            BuildingDoc doc,
            Placement candidate,
            string idPrefix,
            int index,
            string roomId,
            Storey storey)
        {
            Dictionary<string, string> metadata = storey.Metadata();
            metadata[RoomKey] = roomId;

            doc.GeneratedObjects.Add(new PlacedObject(
                idPrefix + index.ToString("00", CultureInfo.InvariantCulture),
                candidate.LogicalId,
                candidate.Pose.WithPosition(
                    candidate.Pose.Position + new Vec3(0f, storey.Elevation, 0f)),
                TagArray(candidate.Tags),
                metadata));
        }

        static int TargetCount(float openArea, float density, float baseline)
        {
            if (openArea <= 0f || density <= 0f)
            {
                return 0;
            }

            return (int)MathF.Round(openArea * baseline * density, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The four corners of a rectangle, in a fixed order: low X first, then low Z.
        /// </summary>
        /// <remarks>
        /// The 90-degree wall junctions of a room, which is all four of them and always four: a
        /// floor is partitioned into rectangles, so every room is one and every room has exactly
        /// this many places a plant can stand in. The order is fixed so that shuffling it is the
        /// only thing that decides which corner is furnished first — an order that already depended
        /// on the room would make one seed's draw mean two different corners in two rooms.
        /// </remarks>
        static List<Vec2> Corners(Rect2 area) => new List<Vec2>(4)
        {
            new Vec2(area.MinX, area.MinZ),
            new Vec2(area.MinX, area.MaxZ),
            new Vec2(area.MaxX, area.MinZ),
            new Vec2(area.MaxX, area.MaxZ),
        };

        /// <summary>
        /// The points within <paramref name="zone"/> of <paramref name="at"/> on both axes, nearest
        /// first.
        /// </summary>
        /// <remarks>
        /// A square window rather than a radius, because what it stands in for is a rule about two
        /// perpendicular walls: a piece is within reach of the X wall or it is not, and how far
        /// along the Z wall it sits has nothing to do with it.
        /// </remarks>
        static List<Vec2> Nearest(IReadOnlyList<Vec2> points, Vec2 at, float zone)
        {
            var near = new List<Vec2>();

            for (int i = 0; i < points.Count; i++)
            {
                if (MathF.Abs(points[i].X - at.X) <= zone && MathF.Abs(points[i].Y - at.Y) <= zone)
                {
                    near.Add(points[i]);
                }
            }

            SortByDistanceFrom(near, at);
            return near;
        }

        /// <summary>Sorts points by how far they are from a place, nearest first.</summary>
        /// <remarks>
        /// Ties are broken on the coordinates themselves rather than left to the sort, because
        /// <see cref="List{T}.Sort(Comparison{T})"/> is not stable and the four cells around a
        /// room's centre are all exactly as close to it as each other. An unstable order there
        /// would be a building that came out differently on a different runtime, which is the one
        /// thing a seed is promising it will not do.
        /// </remarks>
        static void SortByDistanceFrom(List<Vec2> points, Vec2 at) => points.Sort((a, b) =>
        {
            int order = DistanceSquared(a, at).CompareTo(DistanceSquared(b, at));
            if (order != 0)
            {
                return order;
            }

            order = a.X.CompareTo(b.X);
            return order != 0 ? order : a.Y.CompareTo(b.Y);
        });

        static float DistanceSquared(Vec2 from, Vec2 to)
        {
            float x = from.X - to.X;
            float z = from.Y - to.Y;
            return x * x + z * z;
        }

        /// <summary>
        /// How far the largest entry reaches from its own pivot along one axis, whichever way it
        /// is turned.
        /// </summary>
        /// <remarks>
        /// Per axis rather than the diagonal <see cref="SmallestRadius"/> measures, because what it
        /// bounds is a rule stated per axis: a footprint reaches its region's X edge when its own
        /// low X does, and the furthest that edge can be from the pivot is this. A quarter turn
        /// swaps the two axes, so the larger of them is what either one may become.
        /// </remarks>
        static float LargestExtent(IReadOnlyList<CatalogEntry> entries)
        {
            float largest = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                Rect2 footprint = entries[i].Footprint;
                largest = MathF.Max(largest, MathF.Abs(footprint.MinX));
                largest = MathF.Max(largest, MathF.Abs(footprint.MaxX));
                largest = MathF.Max(largest, MathF.Abs(footprint.MinZ));
                largest = MathF.Max(largest, MathF.Abs(footprint.MaxZ));
            }

            return largest;
        }

        /// <summary>
        /// How far the smallest entry reaches from its own pivot, whichever way it is turned. The
        /// floor on the sample radius, for the reason <see cref="CoverPlacer"/> gives.
        /// </summary>
        static float SmallestRadius(IReadOnlyList<CatalogEntry> entries)
        {
            float smallest = float.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                Rect2 footprint = entries[i].Footprint;
                float x = MathF.Max(MathF.Abs(footprint.MinX), MathF.Abs(footprint.MaxX));
                float z = MathF.Max(MathF.Abs(footprint.MinZ), MathF.Abs(footprint.MaxZ));
                smallest = MathF.Min(smallest, MathF.Sqrt(x * x + z * z));
            }

            return smallest;
        }

        // Fisher-Yates from the room's own stream, as in CoverPlacer: Bridson's output grows
        // outward from its first sample, so taking it in order would crowd one corner of the room.
        static void Shuffle(List<Vec2> items, ref Rng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                Vec2 swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        static string[] TagArray(IReadOnlyList<string> tags)
        {
            var copy = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                copy[i] = tags[i];
            }

            return copy;
        }

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        static string Index(int value) => value.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>A metadata number that survives a round trip, as a serialised float does.</summary>
        static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// Where a building's stairwell stands, what it is built from, and the shaft it claims on
        /// every storey.
        /// </summary>
        /// <remarks>
        /// One shaft for the whole building rather than one per floor. Everything else about a
        /// storey is drawn from that storey's own seed — see <see cref="BuildingFloor"/> — and the
        /// stairwell is the one thing that cannot be, because a flight that came up in a different
        /// place on each floor would not be a stairwell. It is drawn from the building seed
        /// instead, which leaves the per-floor re-roll intact: re-rolling a storey rearranges its
        /// rooms around the shaft rather than moving the shaft.
        /// </remarks>
        public readonly struct Stairwell
        {
            /// <summary>Creates a stairwell.</summary>
            public Stairwell(
                CatalogEntry entry, Vec2 position, int quarterTurns, Rect2 flight, Rect2 shaft)
            {
                Entry = entry;
                Position = position;
                QuarterTurns = quarterTurns;
                Flight = flight;
                Shaft = shaft;
            }

            /// <summary>What one flight is built from, or null when the catalog has no stairs.</summary>
            public CatalogEntry Entry { get; }

            /// <summary>Where a flight's pivot goes, in the building's own space.</summary>
            public Vec2 Position { get; }

            /// <summary>How far a flight is turned.</summary>
            public int QuarterTurns { get; }

            /// <summary>The ground one flight of stairs itself covers.</summary>
            public Rect2 Flight { get; }

            /// <summary>
            /// The ground the stairwell claims: the whole tiles of the slab the building expects a
            /// storey to lay, which is what the partition keeps its cuts and its doorways out of and
            /// what no crate may be stood on.
            /// </summary>
            /// <remarks>
            /// <para>
            /// A whole number of tiles rather than the flight's own rectangle, because that is the
            /// most a storey can be asked to give up for the stairs: the flight is anchored flush
            /// into its low corner rather than left adrift in the middle of it, and everything else
            /// on the floor keeps off the lot.
            /// </para>
            /// <para>
            /// It is not what gets cut. A slab has its hole cut to <see cref="Flight"/> on the grid
            /// that storey actually laid, and whatever a coarse tile had to give up to do that is
            /// floored again from a finer one — so the difference between the two rectangles is
            /// floor you can stand on rather than the drop it used to be. See
            /// <see cref="FloorTheMargin"/>.
            /// </para>
            /// </remarks>
            public Rect2 Shaft { get; }

            /// <summary>
            /// The ground a storey's furniture is kept off: the shaft and the floor round it a
            /// player needs to reach the stairs at a run.
            /// </summary>
            /// <remarks>
            /// <para>
            /// <see cref="Shaft"/> pushed out by <see cref="StairClearance"/>, and it is a separate
            /// rectangle from the shaft rather than a wider shaft because the two are asked
            /// different questions. The shaft is what the floor gives up — the tiles the opening
            /// costs, the ground the partition may not cut through, the hole in the slab — and
            /// widening it would move walls and take floor out from under the storey above. This is
            /// only what nothing may be <em>stood on</em>, which is a rule about clutter and
            /// nothing else.
            /// </para>
            /// <para>
            /// It reaches past the room the stairs are in, wherever the shaft sits near a wall, and
            /// that costs nothing: a room is furnished against its own committed list, so a
            /// rectangle poking through a wall keeps the neighbouring room's crates off ground the
            /// wall was already keeping them off.
            /// </para>
            /// </remarks>
            public Rect2 Landing => Shaft.Expanded(StairClearance);

            /// <summary>True when this building has a stairwell at all.</summary>
            public bool Exists => Entry != null;
        }

        /// <summary>
        /// The grid a storey's slab is tiled on: a whole number of the tile's own footprint,
        /// centred in the rectangle the slab covers.
        /// </summary>
        /// <remarks>
        /// A type rather than four locals, because two callers need the same answer and they are
        /// nowhere near each other — the storey laying its slab, and the stairwell working out
        /// which tiles its opening costs. Getting those two out of step is a hole in the floor
        /// where the stairs are not, and a flight of stairs under a solid ceiling.
        /// </remarks>
        readonly struct SlabGrid
        {
            readonly Vec2 _centre;

            public SlabGrid(Vec2 centre, Vec2 cell, int countX, int countZ)
            {
                _centre = centre;
                Cell = cell;
                CountX = countX;
                CountZ = countZ;
            }

            /// <summary>Size of one tile, in metres.</summary>
            public Vec2 Cell { get; }

            /// <summary>How many tiles fit along X.</summary>
            public int CountX { get; }

            /// <summary>How many tiles fit along Z.</summary>
            public int CountZ { get; }

            /// <summary>True when this grid holds at least one whole tile.</summary>
            public bool Exists => CountX > 0 && CountZ > 0 && Cell.X > 0f && Cell.Y > 0f;

            /// <summary>Lowest X a tile edge sits on.</summary>
            public float OriginX => _centre.X - CountX * Cell.X * 0.5f;

            /// <summary>Lowest Z a tile edge sits on.</summary>
            public float OriginZ => _centre.Y - CountZ * Cell.Y * 0.5f;

            /// <summary>The ground the whole grid covers.</summary>
            public Rect2 Extent => new Rect2(
                OriginX, OriginZ, OriginX + CountX * Cell.X, OriginZ + CountZ * Cell.Y);

            /// <summary>The centre of one cell.</summary>
            public Vec2 CentreOf(int x, int z) => new Vec2(
                OriginX + (x + 0.5f) * Cell.X, OriginZ + (z + 0.5f) * Cell.Y);

            /// <summary>The ground one cell covers, which is what the tile laid in it fills.</summary>
            public Rect2 CellAt(int x, int z) => Rect2.FromCenterSize(CentreOf(x, z), Cell);

            /// <summary>
            /// The smallest rectangle of whole tiles that holds <paramref name="what"/>.
            /// </summary>
            /// <remarks>
            /// Never smaller than what it was given, so a rectangle reaching past the edge of the
            /// grid comes back whole — there is no tile out there to take out, but the caller's
            /// other uses of the answer, keeping the cuts and the crates off it, still hold.
            /// </remarks>
            public Rect2 Cover(Rect2 what)
            {
                if (!Exists)
                {
                    return what;
                }

                CoverAxis(OriginX, Cell.X, CountX, what.MinX, what.MaxX, out float minX, out float maxX);
                CoverAxis(OriginZ, Cell.Y, CountZ, what.MinZ, what.MaxZ, out float minZ, out float maxZ);
                return new Rect2(minX, minZ, maxX, maxZ);
            }

            static void CoverAxis(
                float origin, float cell, int count, float min, float max, out float lo, out float hi)
            {
                int first = Math.Max(0, (int)MathF.Floor((min - origin) / cell + FitTolerance));
                int last = Math.Min(count - 1, (int)MathF.Ceiling((max - origin) / cell - FitTolerance) - 1);

                lo = last >= first ? MathF.Min(min, origin + first * cell) : min;
                hi = last >= first ? MathF.Max(max, origin + (last + 1) * cell) : max;
            }
        }

        /// <summary>The art one storey's shell is built from, and the module it is laid out in.</summary>
        readonly struct Shell
        {
            public Shell(
                CatalogEntry wall,
                CatalogEntry doorway,
                CatalogEntry window,
                CatalogEntry tile,
                CatalogEntry margin,
                float module,
                float thickness,
                float slabHeight,
                float headroom)
            {
                Wall = wall;
                Doorway = doorway;
                Window = window;
                Tile = tile;
                Margin = margin;
                Module = module;
                Thickness = thickness;
                SlabHeight = slabHeight;
                Headroom = headroom;
            }

            /// <summary>What a wall run is tiled from, or null if the catalog has no wall art.</summary>
            public CatalogEntry Wall { get; }

            /// <summary>What fills the module a wall leaves open, or null.</summary>
            public CatalogEntry Doorway { get; }

            /// <summary>
            /// What is swapped into an outside module to put a window in it, or null when the
            /// catalog has no window art.
            /// </summary>
            public CatalogEntry Window { get; }

            /// <summary>What the slab is tiled from, or null.</summary>
            public CatalogEntry Tile { get; }

            /// <summary>
            /// The finest floor tile the catalog has, used to floor what is left over between a
            /// whole tile of <see cref="Tile"/> and the edge of the stairwell opening. Null when
            /// the catalog offers nothing smaller than <see cref="Tile"/>.
            /// </summary>
            /// <remarks>
            /// A slab is laid in whole pieces, so a coarse tile cannot reach an opening that does
            /// not fall on its own joints — and the strip it cannot reach is the floor at the top
            /// and the foot of the flight, which is the one piece of floor a stairwell cannot do
            /// without. See <see cref="TileSlab"/>.
            /// </remarks>
            public CatalogEntry Margin { get; }

            /// <summary>Length of one module, in metres. Zero when there is no shell at all.</summary>
            public float Module { get; }

            /// <summary>How thick a wall is, in metres. Zero when there is no wall art.</summary>
            public float Thickness { get; }

            /// <summary>How thick the slab is, in metres. Zero when the catalog has no floor art.</summary>
            public float SlabHeight { get; }

            /// <summary>
            /// Clear height between this storey's floor surface and the underside of the next
            /// storey's slab, in metres. What a wall or a crate has to stand under.
            /// </summary>
            public float Headroom { get; }

            /// <summary>True if this floor can be divided into rooms.</summary>
            public bool HasWalls => Wall != null && Module > 0f;
        }

        /// <summary>The per-storey facts every object emitted onto a floor carries.</summary>
        readonly struct Storey
        {
            readonly string _number;
            readonly string _elevationText;

            public Storey(int index, ulong seed, float floorHeight, float slabHeight)
            {
                Seed = seed;
                IdPrefix = BuildingDoc.FloorIdPrefix(index);
                SlabElevation = index * floorHeight;
                Elevation = SlabElevation + slabHeight;
                CeilingElevation = SlabElevation + floorHeight;
                _number = (index + 1).ToString(CultureInfo.InvariantCulture);
                _elevationText = Elevation.ToString("R", CultureInfo.InvariantCulture);
            }

            /// <summary>The seed this floor's plan and contents are drawn from.</summary>
            public ulong Seed { get; }

            /// <summary>Stable id prefix everything on this floor lives under.</summary>
            public string IdPrefix { get; }

            /// <summary>
            /// Where this storey's slab is laid, in metres above the building's own ground.
            /// </summary>
            /// <remarks>
            /// A whole number of floor heights up. The slab is the only thing on a storey placed
            /// here; everything else stands on top of it, which is what
            /// <see cref="Elevation"/> is. Placing the walls and the crates at the same height as
            /// the slab, as this generator used to, buries a tenth of a metre of each of them in
            /// the floor and leaves their underside in the same plane as the slab's.
            /// </remarks>
            public float SlabElevation { get; }

            /// <summary>Height of this floor's walking surface above the building's own ground.</summary>
            public float Elevation { get; }

            /// <summary>
            /// Where this storey's ceiling is laid: the next storey's slab, or the roof when there
            /// is no next storey.
            /// </summary>
            public float CeilingElevation { get; }

            /// <summary>A fresh metadata dictionary naming this storey.</summary>
            public Dictionary<string, string> Metadata() => new Dictionary<string, string>
            {
                { FloorKey, _number },
                { ElevationKey, _elevationText },
            };
        }

        /// <summary>What a storey added to the building, for the document's metadata.</summary>
        readonly struct Tally
        {
            public Tally(int target, int rooms, int corridors, int structure)
            {
                Target = target;
                Rooms = rooms;
                Corridors = corridors;
                Structure = structure;
            }

            public int Target { get; }

            public int Rooms { get; }

            public int Corridors { get; }

            public int Structure { get; }

            public Tally Plus(Tally other) => new Tally(
                Target + other.Target,
                Rooms + other.Rooms,
                Corridors + other.Corridors,
                Structure + other.Structure);
        }
    }
}
