using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Closes the map: a fence tiled along all four edges of the playfield, so the arena has a
    /// physical boundary rather than a coordinate one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other stage in the tool asks where a thing may go. This one asks where the map stops,
    /// which is a different question and has a different answer: the four sides of
    /// <see cref="ArenaLayout.Playfield"/>, every time, whatever the seed. Nothing is sampled and
    /// nothing is drawn about <em>where</em> — the stream decides only which piece of art fills each
    /// slot of the run.
    /// </para>
    /// <para>
    /// <strong>The four runs face inward</strong>, and that is the one thing this stage does
    /// differently from every other run in the tool. A hedge is laid on the outside of the wall it
    /// hugs; a boundary is laid on the inside of the line it marks, because the line is the edge of
    /// the world and there is no outside to lay anything on. So each segment is seated flush from
    /// the playfield's own edge and grows inward by its own thickness — which is what makes a panel
    /// a tenth of a metre thick sit exactly as flush as one three tenths thick, where a run
    /// straddling a common line would leave the thin one a sliver of ground behind it.
    /// </para>
    /// <para>
    /// <strong>There is no <see cref="ConstraintKind.ClearOfSpawn"/> rule here, and that is not an
    /// oversight.</strong> A spawn area spans the full width of the map at its own end, so the low
    /// and high edges of the playfield lie <em>inside</em> one — a boundary held clear of the spawns
    /// would be a boundary with its two ends missing, which are exactly the two ends a player runs
    /// at. Neither is there a <see cref="ConstraintKind.NotBlockingDoorway"/> rule: a hole in a
    /// hedge is a feature and a hole in a world boundary is a way out of the level.
    /// </para>
    /// <para>
    /// <strong>Stone, and only stone.</strong> The two fence folders are two different things and
    /// this stage uses one of them: <c>fence/StoneFence</c> is the edge of the level and
    /// <c>fence/WoodFence</c> is somebody's garden, which is what <see cref="ExteriorPlacer"/>
    /// fences a yard with. Mixing the shorter wood into the run to close the tail of a face bought
    /// a metre of tidiness and sold the distinction: a garden panel spliced into the wall round the
    /// world reads as a gap that somebody patched. A workspace with nothing in
    /// <c>Props/fence/StoneFence</c> gets no boundary and draws nothing, so a project that has
    /// never filled that folder generates the map it always generated.
    /// </para>
    /// <para>
    /// <strong>The ring is dead level, and it is planted rather than stood.</strong> Everything
    /// else this tool puts down is lifted by <see cref="CatalogEntry.BaseOffset"/> so the underside
    /// of the art lands on the surface, which is right for a crate and wrong for a boundary: stone
    /// boundary art is modelled with a deep foundation under the pivot, and standing that on the
    /// ground leaves the wall hovering with its own footings on show. So the pivot goes on the
    /// ground and the foundation goes under it — and the height every panel takes is
    /// <see cref="RingHeight"/>, the highest ground anywhere on the path, rather than the ground
    /// under that panel.
    /// </para>
    /// <para>
    /// Seating each panel on its own ground gives a wall that steps down every slope it crosses,
    /// which is how a garden fence is built and is not how the edge of a level is built: a stepped
    /// top reads as a series of walls of different heights, and each step is a ledge to be climbed.
    /// One height for the whole ring gives a level top all the way round — and the deep foundations
    /// the art already carries are what fills in underneath where the ground falls away. What that
    /// costs is a wall that stands taller than a panel over the low ground, which is the right way
    /// for the edge of the world to fail.
    /// </para>
    /// <para>
    /// <strong>It runs first of the three anchored stages</strong> — before
    /// <see cref="ExteriorPlacer"/> and long before <see cref="CoverPlacer"/> — and hands its
    /// segments to both, so they place round it. It used to run second, on the reading that a
    /// boundary tiles past whatever the dressing left on the edge, and that reading is wrong for a
    /// reason the art decides rather than the code: boundary art is the longest art on a map, and a
    /// pack whose panels are thirty metres tiles a sixty-metre edge two panels at a time. A lamp
    /// post the dressing had already stood in the way of one of them cost half a side of the level.
    /// A hedge can be moved along a metre and a hole in the edge of the world cannot, so the
    /// boundary goes down first and everything else works round it.
    /// </para>
    /// <para>
    /// The one thing it still yields to is a structure standing on the edge: a building placed
    /// flush against the playfield boundary is the boundary along its own wall, and tiling a fence
    /// through it would be two pieces of art in one place.
    /// </para>
    /// </remarks>
    public static class PerimeterFence
    {
        /// <summary>Tag a catalog entry must carry to be tiled along the edge of the map.</summary>
        /// <remarks>
        /// What a workspace files in <c>Props/fence/StoneFence</c>. The heavier of the two fence
        /// folders, which is why it is the one the world boundary is named for: a stone wall reads
        /// as the edge of the level where a garden fence reads as somebody's garden.
        /// </remarks>
        public const string StoneFenceTag = "fence/stonefence";

        /// <summary>Prefix of the world metadata keys holding this stage's placement statistics.</summary>
        public const string StatsPrefix = "boundary_";

        /// <summary>Stable id prefix of every segment of the boundary.</summary>
        /// <remarks>
        /// Under <c>map/boundary</c> rather than under a lane, because the boundary is the one thing
        /// on the map that belongs to no lane — it runs round the ends of all of them.
        /// </remarks>
        public const string IdPrefix = "map/boundary/fence_";

        /// <summary>Label the stream the boundary's art is drawn from is forked under.</summary>
        const string StreamLabel = "boundary/fence";

        /// <summary>How far apart the samples that find the ring's height are, in metres.</summary>
        /// <remarks>
        /// A metre, which is finer than the ground's smallest feature and far finer than a panel is
        /// long: what is being looked for is the highest point anywhere on the path, and a sample
        /// spacing as coarse as the art would step over a rise between two panels and leave the
        /// wall buried in it. The four corners are sampled whatever the spacing works out at.
        /// </remarks>
        const float HeightSampleStep = 1f;

        /// <summary>
        /// Fences the edge of the playfield and returns every segment it stood up, in generation
        /// order, so the cover stage can keep out of it.
        /// </summary>
        /// <param name="doc">The document to add the boundary to. Its parameters drive the draws.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="terrain">The ground, with the structures' foundations already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="structures">The structures already committed, with their world footprints.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<Placement> Place(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            Catalog catalog,
            IReadOnlyList<MapStructure> structures)
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

            var placed = new List<Placement>();

            IReadOnlyList<CatalogEntry> palette =
                WallRun.Tileable(catalog.Query(TagQuery.All(StoneFenceTag)));

            if (palette.Count == 0)
            {
                // Nothing filed in the stone fence folder. Returning before a single draw is what
                // lets a project that has never used it generate the map it always did.
                return placed;
            }

            ConstraintSet constraints = Rules(layout, structures);
            Rng stream = new Rng(doc.Parameters.Seed).Fork(StreamLabel);
            var stats = new PlacementStats();

            // Once, before a single panel goes down, because it is a fact about the whole ring:
            // every segment stands at this height whichever face it is on and whatever the ground
            // under it does.
            float elevation = RingHeight(terrain, layout.Playfield);

            List<WallRun.Face> edges = Edges(layout.Playfield, WallRun.ThickestSegment(palette));
            int stood = 0;

            for (int i = 0; i < edges.Count; i++)
            {
                stood += WallRun.Tile(
                    edges[i], palette, WallRun.NoGates, constraints, stats, WallRun.Flush, stood,
                    (candidate, index) => Commit(doc, elevation, constraints, candidate, placed, index),
                    ref stream);
            }

            stats.WriteTo(doc.Metadata, StatsPrefix);
            return placed;
        }

        /// <summary>
        /// The four sides of the playfield as runs facing into it, each holding one of its own two
        /// corners and leaving the other to the run that turns there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built here rather than by <see cref="WallRun.Faces"/>, which points a rectangle's faces
        /// outward — the right way round for the three runs that dress a building from outside, and
        /// exactly the wrong way round for the one that closes the map from inside.
        /// </para>
        /// <para>
        /// <strong>The four runs go round like a pinwheel, and that is what closes the corners.</strong>
        /// Two runs meet at every corner of the playfield, and only one of them may have it: a
        /// segment reaching into ground the other run can occupy is a rejection, and a rejection at
        /// a corner is a hole in the boundary a metre from the one place a player looks for one.
        /// Every run therefore starts on its own corner and stops a thickest-segment short of the
        /// next, going round — the low Z run takes the low X corner and gives up the high X one, the
        /// high X run takes that corner and gives up the far one, and so on round the four. Each
        /// corner is claimed exactly once, and the ring closes.
        /// </para>
        /// <para>
        /// A thickest segment rather than half of one, because these runs are seated
        /// <see cref="WallRun.Flush"/>: a segment reaches a full thickness in from its line rather
        /// than half of one either side of it. <see cref="ExteriorPlacer"/> straddles instead and
        /// hands its corners over half a thickness at a time, which is the same arithmetic about a
        /// differently seated band. Both add <see cref="WallRun.CornerSlack"/> on top, so the
        /// handover is not decided by the last bit of a float.
        /// </para>
        /// </remarks>
        static List<WallRun.Face> Edges(Rect2 field, float thickness)
        {
            float over = thickness + WallRun.CornerSlack;

            return new List<WallRun.Face>(WallRun.FaceCount)
            {
                new WallRun.Face(true, 1, field.MinZ, field.MinX, field.MaxX - over),
                new WallRun.Face(false, -1, field.MaxX, field.MinZ, field.MaxZ - over),
                new WallRun.Face(true, -1, field.MaxZ, field.MinX + over, field.MaxX),
                new WallRun.Face(false, 1, field.MinX, field.MinZ + over, field.MaxZ),
            };
        }

        /// <summary>
        /// The one height the whole ring stands at: the highest ground anywhere along the path it
        /// runs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The highest rather than the average, and that is the whole point of measuring it. A ring
        /// at the average height is a ring the ground rises through somewhere, and the boundary of
        /// a level is the one thing on the map that may not have a gap in it — a rise a panel is
        /// buried in is a ramp over the edge of the world. At the highest, the wall stands clear
        /// everywhere and the deep foundations the art carries fill in the low ground behind it.
        /// </para>
        /// <para>
        /// Sampled on a fixed lattice, as <see cref="TerrainField.MeanHeightOn"/> is and for the
        /// same reason: what this has to be is the same answer every time it is asked, not an exact
        /// maximum of a continuous field. The four corners are included whatever the spacing works
        /// out at, because a corner is where two faces meet and a panel each side of it has to
        /// agree.
        /// </para>
        /// <para>
        /// The structures have already graded their foundations into the field by the time this
        /// runs, so a building sitting on a pad against the edge of the playfield raises the ring
        /// with it. That is right: the pad is ground now, and a boundary the pad rises through
        /// would be a boundary you could walk over from the building's doorstep.
        /// </para>
        /// </remarks>
        static float RingHeight(TerrainField terrain, Rect2 field)
        {
            float highest = float.NegativeInfinity;

            int alongX = Samples(field.Width);
            for (int i = 0; i <= alongX; i++)
            {
                float x = field.MinX + (field.MaxX - field.MinX) * (i / (float)alongX);
                highest = MathF.Max(highest, terrain.HeightAt(new Vec2(x, field.MinZ)));
                highest = MathF.Max(highest, terrain.HeightAt(new Vec2(x, field.MaxZ)));
            }

            int alongZ = Samples(field.Depth);
            for (int i = 0; i <= alongZ; i++)
            {
                float z = field.MinZ + (field.MaxZ - field.MinZ) * (i / (float)alongZ);
                highest = MathF.Max(highest, terrain.HeightAt(new Vec2(field.MinX, z)));
                highest = MathF.Max(highest, terrain.HeightAt(new Vec2(field.MaxX, z)));
            }

            return highest;
        }

        /// <summary>How many steps of <see cref="HeightSampleStep"/> cover a face, at least one.</summary>
        static int Samples(float length) =>
            Math.Max(1, (int)MathF.Ceiling(length / HeightSampleStep));

        /// <summary>
        /// The two rules the boundary places under, over everything already on the ground.
        /// </summary>
        /// <remarks>
        /// Two rather than the four the exterior dressing uses, and the class remarks say why the
        /// other two are absent. The margin on the overlap rule is zero for the reason every tiled
        /// run uses zero: two rectangles that merely touch do not overlap, so a zero margin is what
        /// lets one segment start exactly where the last one stopped.
        /// </remarks>
        static ConstraintSet Rules(ArenaLayout layout, IReadOnlyList<MapStructure> structures)
        {
            var constraints = new ConstraintSet(layout, new[]
            {
                PlacementConstraint.InsidePlayfield(),
                PlacementConstraint.NoOverlap(0f),
            });

            for (int i = 0; i < structures.Count; i++)
            {
                constraints.Commit(structures[i].Ground);
            }

            return constraints;
        }

        /// <summary>
        /// Accepts a segment: into the constraint set, into the running list the cover stage is
        /// given, and into the document at the height of the ground under it.
        /// </summary>
        static void Commit(
            WorldDoc doc,
            float elevation,
            ConstraintSet constraints,
            Placement candidate,
            List<Placement> placed,
            int index)
        {
            constraints.Commit(candidate);
            placed.Add(candidate);

            // The rules are two-dimensional and the ground is not, so the height is settled once
            // the candidate has been accepted — as the exterior and cover stages do, for the same
            // reason. Planted rather than stood, and planted at the ring's own height rather than
            // at the ground's: the pivot goes on that level and whatever the art has below it goes
            // under. See RingHeight.
            Pose pose = candidate.Pose.WithPosition(
                new Vec3(candidate.Pose.Position.X, elevation, candidate.Pose.Position.Z));

            doc.GeneratedObjects.Add(new PlacedObject(
                // Three digits rather than the two a lane's objects use: a sixty-metre boundary is a
                // hundred-odd segments, and an id that ran from fence_98 to fence_100 would sort
                // into an order nobody reading it expects.
                IdPrefix + index.ToString("000", CultureInfo.InvariantCulture),
                candidate.LogicalId,
                pose,
                TagArray(candidate.Tags),
                new Dictionary<string, string>()));
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
    }
}
