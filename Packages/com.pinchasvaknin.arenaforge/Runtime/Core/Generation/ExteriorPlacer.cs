using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Dresses the outside of the structures a map has already placed: a run of planting hugging
    /// the walls, a few tight clusters of clutter against them, and a fence round the house's yard
    /// that you can always walk through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three passes over the same short list of buildings, and they are three rather than one
    /// because the art is filed in three folders — and a folder in this tool says where a prop may
    /// be put, not what it is. <c>OutDecor/ContinueAround</c> is tiled end to end along the
    /// footprint, so a bed of planting reads as one thing that runs round the building.
    /// <c>OutDecor/UniqueGroup</c> lands as a handful of pieces in one spot, so a stack of barrels
    /// reads as somebody's stack of barrels rather than as barrels evenly spaced round a wall.
    /// <c>fence/WoodFence</c> is neither, because it goes round the yard rather than round the
    /// wall.
    /// </para>
    /// <para>
    /// <strong>Nothing here snaps to the placement grid.</strong> What makes a hedge look like a
    /// hedge is that each piece starts where the last one stopped, and a grid a metre across turns
    /// that into a row of separate bushes with the gaps rounded off. So a run is laid out from the
    /// wall it belongs to, by <see cref="WallRun"/>, and the constraint set is what says whether
    /// each piece is legal. The cursor lives there rather than here because
    /// <see cref="PerimeterFence"/> walks the same one round the edge of the map.
    /// </para>
    /// <para>
    /// <strong>The clusters go down before the planting.</strong> Both want the strip of ground
    /// against the wall and only one of them is a feature: a stack of barrels somewhere specific is
    /// the thing a player reads, and a run of hedge is the filler between such things. Running the
    /// filler first would leave the feature nowhere to stand — and a hedge that stops short of a
    /// stack of crates and picks up again after it is exactly what "semi-continuous" means.
    /// </para>
    /// <para>
    /// <strong>Which is why a heap hands the run the wall it took.</strong> Order alone does not
    /// give the sentence above: a heap is three separate pieces, so the ground beside a barrel and
    /// between two of them overlaps nothing at all, and a run seated flush tiles straight into it —
    /// planting threaded through somebody's stack of barrels rather than stopping short of it. So
    /// every heap reserves the ground it covers, carried back to the wall, and the run treats those
    /// rectangles as gaps decided before its walk. The heap is also seated flush against the wall
    /// for the same reason it is the feature: a heap standing a hand's width off leaves a strip
    /// behind it exactly the width of a bush.
    /// </para>
    /// <para>
    /// <strong>Every piece of a heap has its back to the wall.</strong> The turn is the face's,
    /// through <see cref="WallFacing.AwayFrom"/>, and no draw is taken for it. What a workspace
    /// files in <c>UniqueGroup</c> is furniture — cabinets, lockers, stacked crates — and a
    /// quarter turn out of the stream faced three of every four of them into the wall they were
    /// put against. Indoors that rule has to find the wall first; out here the wall was handed in,
    /// so there is nothing to measure and nothing to be within reach of.
    /// </para>
    /// <para>
    /// <strong>The run leaves the doorways alone, by rectangle rather than by rule.</strong> The
    /// approach to every doorway a structure declares is a gap in the run computed before the walk
    /// begins — the same mechanism the yard fence below opens its gates with, and for the same
    /// reason. A doorway a hedge has grown across is a building nobody can get into, which is the
    /// same class of failure as a fence that closes and deserves the same kind of guarantee.
    /// </para>
    /// <para>
    /// <strong>A fence may never close.</strong> The gaps are not what is left over after the
    /// tiling; they are decided first, as rectangles, and the tiling skips any piece that would
    /// stand in one. One gap is opened straight out from each of the house's own doorways, so the
    /// way in is the way in, and one more is drawn on a face of the ring — which is both what makes
    /// a yard read as a yard somebody walks through and what makes the guarantee unconditional
    /// rather than a consequence of the house having declared a door. See <see cref="GatesFor"/>.
    /// </para>
    /// <para>
    /// A catalog with none of this art places nothing and draws nothing, so a project that has
    /// never filled the exterior folders generates the map it always generated, down to the byte.
    /// </para>
    /// </remarks>
    public static class ExteriorPlacer
    {
        /// <summary>Tag a catalog entry must carry to be tiled along the outside of a structure.</summary>
        /// <remarks>
        /// What a workspace files in <c>Props/PropBuilding/Decor/OutDecor/ContinueAround</c>: the
        /// planting, trim and skirting that reads as a continuous line rather than as a set of
        /// objects. The folder is the whole contract — a piece filed there says "lay me end to end
        /// against a wall", and nothing about it has to be true of the prefab itself.
        /// </remarks>
        public const string ContinuousTag = "propbuilding/decor/outdecor/continuearound";

        /// <summary>Tag a catalog entry must carry to be dropped in a cluster against a wall.</summary>
        /// <remarks>
        /// The other half of <c>OutDecor</c>, and it is a different tag because it is a different
        /// question. A barrel is not laid out along anything: what it is doing outside a building
        /// is standing with two other barrels where somebody left them, and one query returning
        /// both folders would have made the two one pass — which is the pass that spaces barrels
        /// evenly round a building like a fence made of barrels.
        /// </remarks>
        public const string ClusterTag = "propbuilding/decor/outdecor/uniquegroup";

        /// <summary>Tag a catalog entry must carry to be tiled round a house's yard.</summary>
        public const string FenceTag = "fence/woodfence";

        /// <summary>Prefix of the world metadata keys holding this stage's placement statistics.</summary>
        public const string StatsPrefix = "exterior_";

        /// <summary>Stable id segment naming one piece of the run round a structure.</summary>
        public const string AroundSegment = "/around_";

        /// <summary>Stable id segment naming one cluster of exterior clutter.</summary>
        public const string GroupSegment = "/group_";

        /// <summary>Stable id segment naming one piece inside a cluster.</summary>
        public const string ItemSegment = "/item_";

        /// <summary>Stable id segment naming one segment of a house's fence.</summary>
        public const string FenceSegment = "/fence_";

        /// <summary>How far out of a house's footprint its fence stands, in metres.</summary>
        /// <remarks>
        /// A yard rather than a hoarding round the walls. Two and a half metres is wide enough to
        /// walk a lap of the house inside its own fence and narrow enough that the fence still
        /// reads as belonging to the house rather than as a wall across the lane.
        /// </remarks>
        public const float YardMargin = 2.5f;

        /// <summary>How much wider than a doorway the gap in front of it is, each side, in metres.</summary>
        /// <remarks>
        /// The clearance a map already keeps in front of a doorway, and the same number for the
        /// same reason: a gap exactly as wide as the door is a gap you have to line yourself up
        /// with. Aligned with the door and wider than it is what makes the route through the fence
        /// the obvious one.
        /// </remarks>
        public const float GateClearance = CoverPlacer.DoorwayClearance;

        /// <summary>How wide the gap drawn on the ring itself is, in metres.</summary>
        /// <remarks>
        /// Not measured from a doorway, because this one is not in front of anything — it is the
        /// gap somebody walks through because it is there. Three metres is about what the doorway
        /// gaps come out at, so a fence has no opening that reads as narrower than the rest.
        /// </remarks>
        public const float GateWidth = 3f;

        /// <summary>How far along a wall one cluster of clutter may spread, in metres.</summary>
        public const float ClusterSpan = 2.5f;

        /// <summary>How far out from a wall one cluster of clutter may spread, in metres.</summary>
        /// <remarks>
        /// Barely more than one piece deep. A cluster is a heap against a wall, and a heap two and
        /// a half metres out into the lane is a roadblock — which is a different thing, placed by a
        /// different pass, out of a different folder.
        /// </remarks>
        public const float ClusterDepth = 1.2f;

        /// <summary>How many pieces one cluster is made of.</summary>
        public const int ClusterSize = 3;

        /// <summary>Clear ground the dressing keeps from a spawn area, in metres.</summary>
        /// <remarks>
        /// What a structure itself is held to rather than what cover is, because this is a strip of
        /// planting against a wall the structure was already allowed to put there. Holding it to
        /// the cover rule would carve a piece out of a hedge for standing where the building it is
        /// growing against is standing.
        /// </remarks>
        public const float SpawnClearance = ArenaLayoutGenerator.SpawnClearance;

        /// <summary>Wall a structure carries before it is worth a second cluster, in metres.</summary>
        /// <remarks>
        /// Perimeter rather than area: what decides how many separate heaps a building can hold is
        /// how much wall there is to put them against, and a long thin building has more of it than
        /// a square one covering the same ground.
        /// </remarks>
        const float MetresPerCluster = 18f;

        /// <summary>Candidates tried for one piece of a cluster before the piece is given up on.</summary>
        const int AttemptsPerClusterItem = 6;

        /// <summary>Spots on a wall tried for one cluster before the wall is given up on.</summary>
        const int AnchorsPerCluster = 3;

        /// <summary>Label the stream one structure's clusters are drawn from is forked under.</summary>
        /// <remarks>
        /// Per structure and per pass, so filling one exterior folder cannot move what came out of
        /// another: <see cref="Rng.Fork"/> is a function of a stream's seed rather than of its
        /// position, which is the whole of why a catalog that gains a hedge keeps its barrels where
        /// they were. CLAUDE.md rule 2, one level below the map's own streams.
        /// </remarks>
        const string ClusterLabel = "exterior/cluster/";

        /// <summary>Label the stream one structure's continuous run is drawn from is forked under.</summary>
        const string ContinuousLabel = "exterior/around/";

        /// <summary>Label the stream one house's fence is drawn from is forked under.</summary>
        const string FenceLabel = "exterior/fence/";

        /// <summary>
        /// Dresses the outside of every structure on the map and returns everything it stood up, in
        /// generation order, so the cover stage can keep out of it.
        /// </summary>
        /// <param name="doc">The document to add the dressing to. Its parameters drive the draws.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="terrain">The ground, with the structures' foundations already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="structures">The structures already committed, with their world footprints.</param>
        /// <param name="boundary">
        /// The fence round the edge of the map, already standing. It goes down before this stage
        /// does — see <see cref="PerimeterFence"/> — so everything here places round it.
        /// </param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<Placement> Place(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            Catalog catalog,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> boundary)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (structures == null)
            {
                throw new ArgumentNullException(nameof(structures));
            }

            if (boundary == null)
            {
                throw new ArgumentNullException(nameof(boundary));
            }

            var placed = new List<Placement>();

            IReadOnlyList<CatalogEntry> continuous = WallRun.Tileable(catalog.Query(TagQuery.All(ContinuousTag)));
            IReadOnlyList<CatalogEntry> clustered = catalog.Query(TagQuery.All(ClusterTag));
            IReadOnlyList<CatalogEntry> fencing = WallRun.Tileable(catalog.Query(TagQuery.All(FenceTag)));

            if (continuous.Count == 0 && clustered.Count == 0 && fencing.Count == 0)
            {
                // Nothing filed in any of the exterior folders. Returning before a single draw is
                // what lets a project that has never used them generate the map it always did.
                return placed;
            }

            IReadOnlyList<Rect2> doorways = ArenaLayoutGenerator.Doorways(doc);
            var stats = new PlacementStats();

            for (int i = 0; i < structures.Count; i++)
            {
                MapStructure site = structures[i];

                // The heaps hand the run the stretches of wall they took, so it stops before each
                // one and picks up after it. See Cluster and HugWalls.
                var reserved = new List<Rect2>();

                Cluster(
                    doc, layout, terrain, site, structures, boundary, doorways, clustered, placed,
                    reserved, stats);
                HugWalls(
                    doc, layout, terrain, site, structures, boundary, doorways, continuous, placed,
                    reserved, stats);

                if (site.IsHouse)
                {
                    FenceYard(
                        doc, layout, terrain, site, structures, boundary, doorways, fencing, placed,
                        stats);
                }
            }

            stats.WriteTo(doc.Metadata, StatsPrefix);
            return placed;
        }

        /// <summary>
        /// Stands a few tight clusters of clutter against a structure's walls.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One cluster per face at most, and the faces are walked in an order the structure's own
        /// stream draws. That is what makes the clusters distinct rather than merely nearby: a rule
        /// that only said "against a wall" would let every cluster a building is allowed pile into
        /// the same two metres of it, which is one big heap wearing three stable ids. It is the
        /// argument <see cref="BuildingGenerator"/> makes about a room's four corners, outdoors.
        /// </para>
        /// <para>
        /// Within a face the spot is drawn — see <see cref="DrawAnchor"/> — and drawn again up to
        /// <see cref="AnchorsPerCluster"/> times while nothing has been stood up. Half of a
        /// structure's faces have a doorway in them and a heap is shallower than that doorway's
        /// approach clearance is deep, so a heap anchored near one is refused piece by piece and
        /// never appears; the wall a few metres along usually had room for it the whole time.
        /// </para>
        /// <para>
        /// <strong>A heap that stood up takes its stretch of wall with it.</strong> Every heap
        /// adds a rectangle to <paramref name="reserved"/>, reaching from the wall out to the
        /// heap's own far edge, and <see cref="HugWalls"/> tiles against those as gaps decided
        /// before its walk. Leaving it to <see cref="ConstraintKind.NoOverlap"/> alone was not
        /// enough: the run is seated flush and a heap is not one solid block, so the strip of
        /// ground beside a barrel and between two of them overlaps nothing and takes a bush —
        /// which is planting threaded through somebody's stack of barrels. The heap is the
        /// feature, so the heap is what the wall belongs to for as long as it is standing there.
        /// </para>
        /// </remarks>
        static void Cluster(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            MapStructure site,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> boundary,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<CatalogEntry> entries,
            List<Placement> placed,
            List<Rect2> reserved,
            PlacementStats stats)
        {
            if (entries.Count == 0)
            {
                return;
            }

            Rect2 footprint = site.Footprint;
            int target = Math.Min(
                Math.Max(1, (int)MathF.Round(
                    2f * (footprint.Width + footprint.Depth) / MetresPerCluster,
                    MidpointRounding.AwayFromZero)),
                WallRun.FaceCount);

            Rng stream = new Rng(doc.Parameters.Seed).Fork(ClusterLabel + site.Placed.StableId);

            List<WallRun.Face> faces = WallRun.Faces(footprint);
            Shuffle(faces, ref stream);

            ConstraintSet constraints = Rules(
                layout,
                new[]
                {
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.ClearOfSpawn(SpawnClearance),
                    PlacementConstraint.NotBlockingDoorway(CoverPlacer.DoorwayClearance),

                    // Flush, not spaced, for the reason the run uses zero: two rectangles that
                    // merely touch do not overlap, so this is what lets a heap stand against the
                    // wall it is a heap against rather than a hand's width off it.
                    PlacementConstraint.NoOverlap(0f),
                },
                structures,
                boundary,
                doorways,
                placed);

            for (int group = 0; group < target; group++)
            {
                WallRun.Face face = faces[group];
                string prefix = site.Placed.StableId + GroupSegment + Text(group);
                int first = placed.Count;
                int stood = 0;

                // Only while nothing has been stood up. A heap that got one piece down keeps the
                // spot it got it in: carrying on at a second anchor would put the rest of the heap
                // somewhere else along the wall, which is two heaps sharing one stable id.
                for (int attempt = 0; attempt < AnchorsPerCluster && stood == 0; attempt++)
                {
                    float anchor = DrawAnchor(face, ref stream);

                    for (int item = 0; item < ClusterSize; item++)
                    {
                        if (TryStandInCluster(
                                doc, terrain, site, face, anchor, entries, constraints, placed, stats,
                                prefix + ItemSegment + Text(stood), stood == 0, ref stream))
                        {
                            stood++;
                        }
                    }
                }

                if (stood > 0)
                {
                    reserved.Add(Reservation(face, placed, first));
                }
            }
        }

        /// <summary>
        /// The stretch of wall one heap takes for itself: the ground its pieces cover, carried back
        /// to the wall they stand against.
        /// </summary>
        /// <remarks>
        /// Back to the wall rather than the box round the art, because the gap this is meant to
        /// open in the run is a gap in a line seated flush — and a heap standing a few centimetres
        /// off the wall would otherwise leave a strip in front of it that a bush fits in exactly.
        /// Along the wall it is the art's own extent and no more: a heap reserves the wall it is
        /// standing on, not the wall it might have stood on.
        /// </remarks>
        static Rect2 Reservation(WallRun.Face face, IReadOnlyList<Placement> placed, int first)
        {
            Rect2 box = placed[first].Footprint;
            for (int i = first + 1; i < placed.Count; i++)
            {
                Rect2 next = placed[i].Footprint;
                box = new Rect2(
                    MathF.Min(box.MinX, next.MinX), MathF.Min(box.MinZ, next.MinZ),
                    MathF.Max(box.MaxX, next.MaxX), MathF.Max(box.MaxZ, next.MaxZ));
            }

            return face.AlongX
                ? new Rect2(
                    box.MinX, MathF.Min(box.MinZ, face.Line),
                    box.MaxX, MathF.Max(box.MaxZ, face.Line))
                : new Rect2(
                    MathF.Min(box.MinX, face.Line), box.MinZ,
                    MathF.Max(box.MaxX, face.Line), box.MaxZ);
        }

        /// <summary>Where along a face a heap is centred.</summary>
        /// <remarks>
        /// Half a span in from each end, so a heap sits well inside the face it was given rather
        /// than piled into a corner. Not a whole span in: that would draw every anchor into the
        /// middle third of the wall, which is exactly where the doorway is.
        ///
        /// It keeps the <em>centre</em> off the corners and no more than that — a piece is drawn
        /// half a span either side of the anchor and then reaches half its own size past where it
        /// was drawn, so what holds a heap's art on its own face is the clamp in
        /// <see cref="TryStandInCluster"/> rather than this.
        ///
        /// Inverted rather than clamped when the face is shorter than one span. The draw still
        /// happens, and what it yields is a point near the middle of a short wall, which is where
        /// the only heap it can hold belongs.
        /// </remarks>
        static float DrawAnchor(WallRun.Face face, ref Rng stream)
        {
            float low = face.Min + ClusterSpan * 0.5f;
            float high = face.Max - ClusterSpan * 0.5f;
            return stream.NextRange(MathF.Min(low, high), MathF.Max(low, high));
        }

        /// <summary>Stands one piece of a cluster, or nothing when the ground beside the wall is full.</summary>
        /// <param name="flush">
        /// True for the piece a heap is anchored on, which is seated hard against the wall. The
        /// pieces after it are drawn out from the wall and pile up in front of it.
        /// </param>
        static bool TryStandInCluster(
            WorldDoc doc,
            TerrainField terrain,
            MapStructure site,
            WallRun.Face face,
            float anchor,
            IReadOnlyList<CatalogEntry> entries,
            ConstraintSet constraints,
            List<Placement> placed,
            PlacementStats stats,
            string stableId,
            bool flush,
            ref Rng stream)
        {
            for (int attempt = 0; attempt < AttemptsPerClusterItem; attempt++)
            {
                CatalogEntry entry = stream.WeightedPick(entries, e => e.Weight);

                // Turned by the wall and not drawn. A heap is a stack of cabinets, crates and
                // barrels standing against somebody's house, and a quarter turn out of the stream
                // put three of every four of them face-first into the plaster — the same thing a
                // random turn did to the furniture indoors, outdoors, and worse for being read
                // against a wall the player is walking past rather than one they are inside of.
                // There is nothing to measure here: the face is the wall this piece was given, so
                // the turn is a function of it. See WallFacing.AwayFrom.
                int quarterTurns = WallFacing.AwayFrom(face.AlongX, face.Outward);
                Rect2 local = QuarterTurn.Rotate(entry.Footprint, quarterTurns);

                // Kept on the face it was given, art and all. DrawAnchor holds a heap's centre
                // half a span off the corners, which is not the same thing: a piece is drawn half a
                // span either side of that and then reaches half its own size past where it was
                // drawn, so the far piece of a heap laps the corner and stands on the wall the next
                // heap was given. Two heaps that meet round a corner are one heap bent through
                // ninety degrees, which is the one thing a heap must not be — and it is what seating
                // them flush made reachable, since a heap held a hand's width off the wall could
                // not quite touch the run of the wall beside it.
                float half = face.AlongSize(local) * 0.5f;
                float low = face.Min + half;
                float high = face.Max - half;
                float along = MathF.Min(
                    MathF.Max(
                        anchor + stream.NextRange(-ClusterSpan * 0.5f, ClusterSpan * 0.5f),
                        MathF.Min(low, high)),
                    MathF.Max(low, high));

                // The piece a heap is anchored on touches the wall exactly, at the same zero
                // clearance the continuous run is seated at, and only the pieces after it are
                // drawn out from it. A heap that started a hand's width off the wall left a strip
                // of open ground behind it the width of a bush, and the run tiled straight through
                // it — a hedge growing behind a stack of barrels, which is two lines of dressing
                // where the art says there is one.
                float gap = flush ? 0f : stream.NextRange(0f, ClusterDepth);

                Placement candidate = Placement.AtQuarterTurn(
                    entry,
                    face.Point(face.Centred(local, along), face.Against(local, gap)),
                    quarterTurns);

                ConstraintResult result = constraints.Evaluate(candidate);
                stats.Record(result);

                if (!result.IsOk)
                {
                    continue;
                }

                Commit(
                    doc, site, constraints, candidate, Standing(terrain, candidate), placed,
                    stableId);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Lays a run of planting end to end along every face of a structure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A cursor walks each face and every piece starts where the last one stopped, so the run is
        /// continuous by construction rather than by luck — there is no sampler here and no spacing
        /// to tune. What breaks a run is a rule, and every break therefore means something: the
        /// clearance in front of a doorway, the heap of barrels the cluster pass already stood
        /// there, the edge of the playfield.
        /// </para>
        /// <para>
        /// A slot the drawn art will not fit in is skipped rather than shrunk. Nothing in this tool
        /// rescales art to fill a gap — see ARCHITECTURE.md section 3 — so the alternative to a gap
        /// the width of one bush is a bush that is not the bush the catalog describes.
        /// </para>
        /// <para>
        /// <strong>The two breaks that have to happen are rectangles rather than rejections.</strong>
        /// A doorway gap and a heap's stretch of wall are computed before a single piece is drawn,
        /// and the walk skips any piece that would stand in one — exactly as
        /// <see cref="FenceYard"/> opens its gates, and for the same reason. Left to
        /// <see cref="ConstraintKind.NotBlockingDoorway"/> alone the doorway very nearly was clear:
        /// the rule keeps a piece's <em>footprint</em> off the approach, and a footprint is a box
        /// somebody drew round a bush while the thing a player walks into is its foliage. A doorway
        /// a hedge has grown across is a building with no way in, which is the same class of
        /// failure as a fence that closes, and it gets the same treatment.
        /// </para>
        /// </remarks>
        static void HugWalls(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            MapStructure site,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> boundary,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<CatalogEntry> entries,
            List<Placement> placed,
            IReadOnlyList<Rect2> reserved,
            PlacementStats stats)
        {
            if (entries.Count == 0)
            {
                return;
            }

            Rng stream = new Rng(doc.Parameters.Seed).Fork(ContinuousLabel + site.Placed.StableId);
            List<Rect2> gates = Openings(site, doorways, reserved, WallRun.ThickestSegment(entries));

            ConstraintSet constraints = Rules(
                layout,
                new[]
                {
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.ClearOfSpawn(SpawnClearance),
                    PlacementConstraint.NotBlockingDoorway(CoverPlacer.DoorwayClearance),

                    // Flush, not spaced. Two rectangles that merely touch do not overlap, so a zero
                    // margin is what lets one piece of a run start exactly where the last stopped.
                    PlacementConstraint.NoOverlap(0f),
                },
                structures,
                boundary,
                doorways,
                placed);

            List<WallRun.Face> faces = WallRun.Faces(site.Footprint);
            int stood = 0;

            for (int i = 0; i < faces.Count; i++)
            {
                stood += WallRun.Tile(
                    faces[i], entries, gates, constraints, stats, WallRun.Flush, stood,
                    (candidate, index) => Commit(
                        doc, site, constraints, candidate, Standing(terrain, candidate), placed,
                        site.Placed.StableId + AroundSegment + Text(index)),
                    ref stream);
            }
        }

        /// <summary>
        /// Where the run round a structure may not stand: in front of each of its own doorways,
        /// and on the wall any heap has already taken.
        /// </summary>
        /// <remarks>
        /// A doorway gap is built the way <see cref="FenceYard"/> builds one — see
        /// <see cref="GateOut"/> — over a ring that is the structure's own footprint rather than
        /// its yard, because this run is seated against the wall rather than out at the fence. The
        /// deepest piece on offer is how far past the wall the rectangle has to reach for a flush
        /// piece to stand in it.
        /// </remarks>
        static List<Rect2> Openings(
            MapStructure site,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Rect2> reserved,
            float depth)
        {
            Rect2 shell = site.Footprint;
            var gates = new List<Rect2>(doorways.Count + reserved.Count);

            for (int i = 0; i < doorways.Count; i++)
            {
                // A doorway straddles the face it is cut into, so the half of it inside the
                // structure is what says whose doorway it is.
                if (shell.Overlaps(doorways[i]))
                {
                    gates.Add(GateOut(shell, shell, doorways[i], depth));
                }
            }

            for (int i = 0; i < reserved.Count; i++)
            {
                gates.Add(reserved[i]);
            }

            return gates;
        }

        /// <summary>
        /// Fences the yard round a house, leaving a gap at each of its doorways and one more
        /// wherever the stream puts it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The ring is the house's footprint pushed out by <see cref="YardMargin"/> and the fence
        /// straddles it, so what the segments describe is a line rather than a wall with a side to
        /// be on. Each face is shortened by half the thickest piece at both ends, so the four runs
        /// stop clear of one another and a corner comes out as a corner rather than as whichever
        /// run reached it first and a piece-wide hole in the other.
        /// </para>
        /// <para>
        /// <strong>The gaps come first.</strong> They are rectangles computed before a single
        /// segment is drawn, and the tiling skips a segment that would stand in one — so the fence
        /// is permeable however the draws fall, rather than permeable because a rule happened to
        /// reject the right piece. That is the difference between a property and a tendency, and it
        /// is the one property in this stage whose loss a player would notice: a closed fence is a
        /// house you cannot get into.
        /// </para>
        /// <para>
        /// <strong>The segments are planted in the ground rather than stood on it</strong>, one
        /// height per panel and no tilt — see <see cref="TerrainField.Planted"/>. It is the same
        /// treatment <see cref="PerimeterFence"/> gives the boundary, because it is the same kind
        /// of art: a fence that follows a slope is built as a flight of upright panels stepping
        /// down it, with a foundation under each one covering the wedge the step opens.
        /// </para>
        /// </remarks>
        static void FenceYard(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            MapStructure site,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> boundary,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<CatalogEntry> entries,
            List<Placement> placed,
            PlacementStats stats)
        {
            if (entries.Count == 0)
            {
                return;
            }

            Rect2 ring = site.Footprint.Expanded(YardMargin);
            float thickness = WallRun.ThickestSegment(entries);

            Rng stream = new Rng(doc.Parameters.Seed).Fork(FenceLabel + site.Placed.StableId);
            List<Rect2> gates = GatesFor(site, ring, doorways, thickness, ref stream);

            ConstraintSet constraints = Rules(
                layout,
                new[]
                {
                    PlacementConstraint.InsidePlayfield(),
                    PlacementConstraint.ClearOfSpawn(SpawnClearance),
                    PlacementConstraint.NotBlockingDoorway(CoverPlacer.DoorwayClearance),
                    PlacementConstraint.NoOverlap(0f),
                },
                structures,
                boundary,
                doorways,
                placed);

            List<WallRun.Face> faces = Ring(ring, thickness);
            int stood = 0;

            for (int i = 0; i < faces.Count; i++)
            {
                stood += WallRun.Tile(
                    faces[i], entries, gates, constraints, stats,
                    WallRun.Astride, stood,
                    (candidate, index) => Commit(
                        doc, site, constraints, candidate,
                        terrain.Planted(candidate.Pose, candidate.Pose.Position.Xz), placed,
                        site.Placed.StableId + FenceSegment + Text(index)),
                    ref stream);
            }
        }

        /// <summary>
        /// The four sides of the yard ring, each holding one of its own two corners and leaving the
        /// other to the run that turns there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two runs reach every corner of the ring and only one of them may have it. Shortening both
        /// at both ends — which is what this did — keeps them out of each other's way and leaves a
        /// square of open ground at all four corners, so a yard that reads as fenced from any side
        /// has a way in at every angle of it.
        /// </para>
        /// <para>
        /// So the four are slid round like a pinwheel instead: half a thickest segment forward for
        /// the two that take their low corner, half a segment back for the two that take their high
        /// one. Half rather than whole because these runs straddle their line — see
        /// <see cref="WallRun.Astride"/> — so the band a run occupies reaches half a thickness either
        /// side of the ring, and half a thickness is all a neighbour has to keep clear of, plus
        /// <see cref="WallRun.CornerSlack"/> so that exactly touching is not a thing two different
        /// float sums have to agree on.
        /// </para>
        /// </remarks>
        static List<WallRun.Face> Ring(Rect2 ring, float thickness)
        {
            float half = thickness * 0.5f + WallRun.CornerSlack;
            List<WallRun.Face> faces = WallRun.Faces(ring);

            // WallRun.Faces goes low Z, high X, high Z, low X. The first two take the corner at their
            // low end and the last two the corner at their high end, which claims each of the four
            // exactly once.
            faces[0] = faces[0].Shifted(-half);
            faces[1] = faces[1].Shifted(-half);
            faces[2] = faces[2].Shifted(half);
            faces[3] = faces[3].Shifted(half);

            return faces;
        }

        /// <summary>
        /// Where a fence may not stand: one gap straight out from each of the house's own doorways,
        /// and one more drawn on the ring.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A doorway gap is the doorway rectangle itself, widened by <see cref="GateClearance"/> and
        /// extruded from the middle of the house out past the ring — so it is aligned with the way
        /// in by construction, whichever face the door ended up on and however the house was turned.
        /// </para>
        /// <para>
        /// The drawn gap is added every time rather than only when there are no doorways. A fence
        /// with an opening in a stretch no door faces is what a yard somebody actually uses looks
        /// like, and it is also what makes "this fence is not a closed loop" a fact about the
        /// algorithm rather than a fact about the metadata the structure stage happened to write.
        /// </para>
        /// </remarks>
        static List<Rect2> GatesFor(
            MapStructure site, Rect2 ring, IReadOnlyList<Rect2> doorways, float thickness, ref Rng stream)
        {
            var gates = new List<Rect2>();
            Rect2 house = site.Footprint;

            for (int i = 0; i < doorways.Count; i++)
            {
                // A doorway straddles the face it is cut into, so the half of it inside the house is
                // what says whose doorway it is.
                if (house.Overlaps(doorways[i]))
                {
                    gates.Add(GateOut(house, ring, doorways[i], thickness));
                }
            }

            WallRun.Face face = WallRun.Faces(ring)[stream.NextRange(0, WallRun.FaceCount)];
            float half = GateWidth * 0.5f;
            float low = face.Min + half;
            float high = face.Max - half;
            float at = stream.NextRange(MathF.Min(low, high), MathF.Max(low, high));

            gates.Add(Rect2.FromCorners(
                face.Point(at - half, face.Line - thickness),
                face.Point(at + half, face.Line + thickness)));

            return gates;
        }

        /// <summary>The gap a doorway opens in the ring, as a rectangle from the house out past it.</summary>
        static Rect2 GateOut(Rect2 house, Rect2 ring, Rect2 doorway, float thickness)
        {
            Vec2 delta = doorway.Center - house.Center;

            // Which face the doorway is on, measured as a share of the half extent it sits along
            // rather than in metres: a door two metres off the centre of a four-metre face is on
            // that face, and the same two metres on a twenty-metre face is not.
            bool outwardAlongX = MathF.Abs(delta.X) * house.Depth >= MathF.Abs(delta.Y) * house.Width;

            if (outwardAlongX)
            {
                float beyond = delta.X >= 0f ? ring.MaxX + thickness : ring.MinX - thickness;
                return Rect2.FromCorners(
                    new Vec2(house.Center.X, doorway.MinZ - GateClearance),
                    new Vec2(beyond, doorway.MaxZ + GateClearance));
            }

            float past = delta.Y >= 0f ? ring.MaxZ + thickness : ring.MinZ - thickness;
            return Rect2.FromCorners(
                new Vec2(doorway.MinX - GateClearance, house.Center.Y),
                new Vec2(doorway.MaxX + GateClearance, past));
        }

        /// <summary>
        /// Where a piece of dressing stands: on top of the ground under its own pivot.
        /// </summary>
        /// <remarks>
        /// The rules are two-dimensional and the ground is not, so the height is settled once a
        /// candidate has been accepted — as the cover stage does, and for the same reason. Added
        /// to the pose rather than replacing it, because
        /// <see cref="Placement.AtQuarterTurn"/> has already lifted the piece onto its own base:
        /// a barrel goes on the ground, where a fence panel goes into it.
        /// </remarks>
        static Pose Standing(TerrainField terrain, Placement candidate) =>
            candidate.Pose.WithPosition(
                candidate.Pose.Position +
                new Vec3(0f, terrain.HeightAt(candidate.Pose.Position.Xz), 0f));

        /// <summary>
        /// Accepts a candidate: into the constraint set, into the running list the cover stage is
        /// given, and into the document at the pose the caller seated it at.
        /// </summary>
        static void Commit(
            WorldDoc doc,
            MapStructure site,
            ConstraintSet constraints,
            Placement candidate,
            Pose pose,
            List<Placement> placed,
            string stableId)
        {
            constraints.Commit(candidate);
            placed.Add(candidate);

            var metadata = new Dictionary<string, string>();
            string lane = site.LaneId;
            if (lane.Length > 0)
            {
                metadata[ArenaLayoutGenerator.LaneKey] = lane;
            }

            doc.GeneratedObjects.Add(new PlacedObject(
                stableId, candidate.LogicalId, pose, TagArray(candidate.Tags), metadata));
        }

        /// <summary>
        /// The rules one pass places under, cheapest first, over everything already on the ground.
        /// </summary>
        /// <remarks>
        /// The structures are committed rather than kept clear of by a distance: what this stage is
        /// for is putting art <em>against</em> a wall, so the only thing left to say about a
        /// building is that nothing may stand inside it. Every other structure on the map is
        /// committed too, and so is everything the exterior stage has already stood up round any of
        /// them, which is what keeps two buildings' dressing from meeting in the middle — and so is
        /// the boundary, which was standing before this stage began.
        /// </remarks>
        static ConstraintSet Rules(
            ArenaLayout layout,
            PlacementConstraint[] rules,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> boundary,
            IReadOnlyList<Rect2> doorways,
            IReadOnlyList<Placement> placed)
        {
            var constraints = new ConstraintSet(layout, rules);

            for (int i = 0; i < structures.Count; i++)
            {
                constraints.Commit(structures[i].Ground);
            }

            for (int i = 0; i < boundary.Count; i++)
            {
                constraints.Commit(boundary[i]);
            }

            for (int i = 0; i < placed.Count; i++)
            {
                constraints.Commit(placed[i]);
            }

            for (int i = 0; i < doorways.Count; i++)
            {
                constraints.AddDoorway(doorways[i]);
            }

            return constraints;
        }

        /// <summary>Fisher-Yates from the caller's stream, in place and walking downward.</summary>
        static void Shuffle(List<WallRun.Face> items, ref Rng rng)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = rng.NextRange(0, i + 1);
                WallRun.Face swap = items[i];
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

        static string Text(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    }
}
