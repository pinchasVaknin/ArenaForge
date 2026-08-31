using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Stands a broken ring of wooden fencing round each spawn, so a spawn reads as a base somebody
    /// holds rather than as a patch of ground with a marker on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Wooden fencing and not the boundary's stone.</strong> The two fence folders already
    /// mean different things — <see cref="PerimeterFence.StoneFenceTag"/> is the edge of the world
    /// and <see cref="ExteriorPlacer.FenceTag"/> is somebody's garden — and what a spawn wants is
    /// the second. A base is a place a team has fenced off, not a place the level stops.
    /// </para>
    /// <para>
    /// <strong>The ring is the square inscribed in the spawn's graded disc.</strong> A spawn area
    /// runs the full width of the map, so fencing its outline would put three of its four sides on
    /// top of the boundary; and the ground held flat is the disc round the marker rather than the
    /// band, so a square any larger than the disc's own inscribed one puts its four corners on the
    /// slope outside the pad. Inscribed, every panel stands on ground held at one height, which is
    /// why the ring can take a single elevation instead of stepping.
    /// </para>
    /// <para>
    /// <strong>It is broken on purpose, one gap a side.</strong> A closed ring is a pen. Each face
    /// gets one gate, placed by this stage's own stream somewhere in the middle
    /// <see cref="GateShare"/> of that face, so there is always a way out on every side and never
    /// one in the corner where it would be hard to find. The gate is
    /// <see cref="ArenaParams.PathWidth"/> across — the narrowest way through the map the generator
    /// lays anywhere, which is the width it already believes a person moves through.
    /// </para>
    /// <para>
    /// <strong>Three quarters of it is fence, and every gap in it is meant.</strong> The ring is
    /// sized to a whole number of panels and each gate takes a whole slot, so over forty seeds every
    /// map stands the same 24 panels: 74.5% of a ring's perimeter carries one, 24.8% is the four
    /// gates and 0.7% is the corner handover. It was 65.7% against 10.4% of tail when the ring was
    /// sized to the pad instead of to the art — a face whose length is not a whole number of panels
    /// ends in a stretch too short for anything in the palette, and on a ring this small each of
    /// those four ends is a visible hole.
    /// </para>
    /// <para>
    /// The gate is one panel wide and the ring is four panels a side, so the openings are a quarter
    /// of it. That share is a consequence of the pad's size rather than a target: on a bigger pad
    /// the ring takes more slots and the same four gates are a smaller fraction of it. What does not
    /// change is that every side has exactly one way through.
    /// </para>
    /// <para>
    /// <strong>Nothing here blocks a road.</strong> A network is routed round structures and not
    /// round fences, which is true of the garden fencing <see cref="ExteriorPlacer"/> already puts
    /// up and is true of this. A road may therefore cross a gap or cross a panel; what stops it
    /// being a problem is that the ring is inside the spawn pad, which is flat, and a road that
    /// reaches a spawn arrives at the marker rather than skirting it.
    /// </para>
    /// <para>
    /// <strong>A workspace with no wooden fence art generates the map it always did.</strong> The
    /// palette is read before a single draw is taken, and an empty one returns before the stream is
    /// forked.
    /// </para>
    /// </remarks>
    public static class SpawnEnclosure
    {
        /// <summary>Prefix of the world metadata keys holding this stage's placement statistics.</summary>
        public const string StatsPrefix = "spawn_fence_";

        /// <summary>Label the stream this stage's art is drawn from is forked under.</summary>
        const string StreamLabel = "spawn/fence";

        /// <summary>The diagonal of a unit square, halved: the inscribed square's half-side per unit radius.</summary>
        const float InscribedHalf = 0.70710678f;

        /// <summary>How much longer than its panels a face is made, in metres.</summary>
        /// <remarks>
        /// <para>
        /// A face exactly as long as the panels that fill it leaves the walk's own "does the next
        /// one still fit" test sitting on the boundary, where the last few bits of an accumulated
        /// cursor decide it — and it decided against about two slots a map. A hair of slack settles
        /// it in favour of the panel.
        /// </para>
        /// <para>
        /// <strong>Half of <see cref="WallRun.EndTolerance"/>, and the ceiling is the point.</strong>
        /// Slack the run considers bare ground is slack the closing pass fills, and it fills a
        /// sliver with a whole panel: at a millimetre it laid a second panel a millimetre along the
        /// first on all four faces of both rings — eight panels stacked on eight others. Under the
        /// tolerance the residue is not a gap at all and nothing is laid across it. The floor is the
        /// cursor's own arithmetic, some twenty times smaller again.
        /// </para>
        /// </remarks>
        const float FitSlack = WallRun.EndTolerance * 0.5f;

        /// <summary>
        /// Fences both spawns and returns every segment it stood up, in generation order, so the
        /// stages after it can keep out of them.
        /// </summary>
        /// <param name="doc">The document to add the rings to. Its parameters drive the draws.</param>
        /// <param name="layout">The layout the document was generated against.</param>
        /// <param name="terrain">The ground, with the spawn pads already graded into it.</param>
        /// <param name="catalog">The art to draw from.</param>
        /// <param name="structures">The structures already committed, with their world footprints.</param>
        /// <param name="standing">What is already up that a panel may not stand in.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static List<Placement> Place(
            WorldDoc doc,
            ArenaLayout layout,
            TerrainField terrain,
            Catalog catalog,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> standing)
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

            if (standing == null)
            {
                throw new ArgumentNullException(nameof(standing));
            }

            var placed = new List<Placement>();

            IReadOnlyList<CatalogEntry> palette =
                WallRun.Tileable(catalog.Query(TagQuery.All(ExteriorPlacer.FenceTag)));

            if (palette.Count == 0)
            {
                // Nothing filed in the wooden fence folder. Returning before a single draw is what
                // lets a project that has never used it generate the map it always did.
                return placed;
            }

            ConstraintSet constraints = Rules(layout, structures, standing);
            Rng stream = new Rng(doc.Parameters.Seed).Fork(StreamLabel);
            var stats = new PlacementStats();

            Ring(doc, terrain, palette, constraints, stats, "a", layout.SpawnAreaA, placed, ref stream);
            Ring(doc, terrain, palette, constraints, stats, "b", layout.SpawnAreaB, placed, ref stream);

            stats.WriteTo(doc.Metadata, StatsPrefix);
            return placed;
        }

        /// <summary>Fences one spawn.</summary>
        static void Ring(
            WorldDoc doc,
            TerrainField terrain,
            IReadOnlyList<CatalogEntry> palette,
            ConstraintSet constraints,
            PlacementStats stats,
            string team,
            Rect2 area,
            List<Placement> placed,
            ref Rng stream)
        {
            float thickness = WallRun.ThickestSegment(palette);
            float panel = WallRun.ShortestRun(palette);
            float over = thickness * 0.5f + WallRun.CornerSlack;

            int slots = Slots(ArenaLayoutGenerator.SpawnPadRadius(area), thickness, over, panel);
            if (slots < 1)
            {
                // The pad is smaller than one panel. Nothing can be built here that is a ring.
                return;
            }

            Rect2 ring = Square(area.Center, slots * panel + over + FitSlack);

            // One height for the whole ring, read at the middle of it. The pad the spawn stands on
            // is graded dead flat before this runs and the ring is inside the pad, so every point
            // under it is this height — the single sample is the whole answer rather than an
            // approximation of it.
            float elevation = terrain.HeightAt(ring.Center);

            List<Rect2> gates = Gates(ring, over, panel, slots, thickness, ref stream);
            List<WallRun.Face> faces = Edges(ring, thickness);
            var stood = 0;

            for (int i = 0; i < faces.Count; i++)
            {
                stood += WallRun.Tile(
                    faces[i], palette, gates, constraints, stats, WallRun.Astride, stood,
                    (candidate, index) =>
                        Commit(doc, team, elevation, constraints, candidate, placed, index),
                    ref stream);
            }
        }

        /// <summary>
        /// How many whole panels fit along one face of the largest ring the pad will take.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A square inscribed in a circle of radius r has a side of r times root two, and a panel is
        /// seated astride its line, so half a thickness comes off to keep the art itself on the pad
        /// rather than merely its centreline. What is left is divided by the shortest panel on
        /// offer, and the remainder is thrown away rather than left as bare ground.
        /// </para>
        /// <para>
        /// <strong>Sizing the ring to the art is what makes it a ring rather than four fences.</strong>
        /// A face whose length is not a whole number of panels ends in a stretch too short for
        /// anything in the palette, and the closing pass cannot fill it either — four corners of
        /// ragged nothing, on a ring small enough that each one is a visible hole.
        /// </para>
        /// </remarks>
        static int Slots(float radius, float thickness, float over, float panel)
        {
            if (!(panel > 0f))
            {
                return 0;
            }

            float widest = radius * InscribedHalf * 2f - thickness;

            return (int)MathF.Floor((widest - over - FitSlack) / panel);
        }

        /// <summary>The square of a given side, on a point.</summary>
        static Rect2 Square(Vec2 centre, float side)
        {
            float half = side * 0.5f;

            return new Rect2(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
        }

        /// <summary>One gate per face, each taking a whole panel slot of its own side.</summary>
        /// <remarks>
        /// <para>
        /// <strong>On the panel grid, not anywhere along the face.</strong> A gate dropped at an
        /// arbitrary offset splits its run into two stretches that are each a fraction of a panel
        /// too long, so a face gives up ground at the gate as well as at the gate itself. Taking a
        /// whole slot leaves the rest of the face an exact number of panels, and every one of them
        /// stands.
        /// </para>
        /// <para>
        /// <strong>An inner slot wherever there is a choice.</strong> A gate in an end slot is a
        /// gate at a corner, which is hard to see from inside the ring and is where two runs are
        /// already handing panels over — a corner that is a gap on one face and a panel on the
        /// other reads as a mistake rather than as a way out.
        /// </para>
        /// <para>
        /// The four draws are taken in face order and always all four, so the stream reads the same
        /// whatever the tiling then makes of them.
        /// </para>
        /// </remarks>
        static List<Rect2> Gates(
            Rect2 ring, float over, float panel, int slots, float thickness, ref Rng stream)
        {
            // Each face runs from its own corner, and two of the four start a handover in — see
            // Edges. A gate has to be measured from the same place its panels are.
            float lowZ = ring.MinX + Slot(slots, ref stream) * panel;
            float highX = ring.MinZ + Slot(slots, ref stream) * panel;
            float highZ = ring.MinX + over + Slot(slots, ref stream) * panel;
            float lowX = ring.MinZ + over + Slot(slots, ref stream) * panel;

            return new List<Rect2>(WallRun.FaceCount)
            {
                new Rect2(lowZ, ring.MinZ - thickness, lowZ + panel, ring.MinZ + thickness),
                new Rect2(ring.MaxX - thickness, highX, ring.MaxX + thickness, highX + panel),
                new Rect2(highZ, ring.MaxZ - thickness, highZ + panel, ring.MaxZ + thickness),
                new Rect2(ring.MinX - thickness, lowX, ring.MinX + thickness, lowX + panel),
            };
        }

        /// <summary>Which slot of a face its gate takes.</summary>
        static int Slot(int slots, ref Rng stream) =>
            slots >= 3 ? stream.NextRange(1, slots - 1) : stream.NextRange(0, slots);

        /// <summary>
        /// The four sides of the ring as runs facing out of it, going round like a pinwheel so each
        /// corner is claimed exactly once.
        /// </summary>
        /// <remarks>
        /// The same handover <see cref="PerimeterFence"/> makes at the edge of the map, outward
        /// rather than inward and over half a thickness rather than a whole one — these runs are
        /// seated <see cref="WallRun.Astride"/>, so a segment reaches half a thickness either side
        /// of its line instead of a full thickness in from it. <see cref="WallRun.CornerSlack"/> on
        /// top, so the handover is not decided by the last bit of a float.
        /// </remarks>
        static List<WallRun.Face> Edges(Rect2 ring, float thickness)
        {
            float over = thickness * 0.5f + WallRun.CornerSlack;

            return new List<WallRun.Face>(WallRun.FaceCount)
            {
                new WallRun.Face(true, -1, ring.MinZ, ring.MinX, ring.MaxX - over),
                new WallRun.Face(false, 1, ring.MaxX, ring.MinZ, ring.MaxZ - over),
                new WallRun.Face(true, 1, ring.MaxZ, ring.MinX + over, ring.MaxX),
                new WallRun.Face(false, -1, ring.MinX, ring.MinZ + over, ring.MaxZ),
            };
        }

        /// <summary>The rules a panel of the ring places under, over everything already standing.</summary>
        static ConstraintSet Rules(
            ArenaLayout layout,
            IReadOnlyList<MapStructure> structures,
            IReadOnlyList<Placement> standing)
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

            for (int i = 0; i < standing.Count; i++)
            {
                constraints.Commit(standing[i]);
            }

            return constraints;
        }

        /// <summary>
        /// Accepts a panel: into the constraint set, into the running list the later stages are
        /// given, and into the document at the ring's own height.
        /// </summary>
        static void Commit(
            WorldDoc doc,
            string team,
            float elevation,
            ConstraintSet constraints,
            Placement candidate,
            List<Placement> placed,
            int index)
        {
            constraints.Commit(candidate);
            placed.Add(candidate);

            // Planted rather than stood, as the boundary is: the pivot goes on the pad and whatever
            // the art carries below it goes under. A fence panel modelled with a foundation is
            // hovering over its own footings if it is stood on the ground instead.
            Pose pose = candidate.Pose.WithPosition(
                new Vec3(candidate.Pose.Position.X, elevation, candidate.Pose.Position.Z));

            // The ground it stands on, recorded so the road stage can keep off it. A fence is the
            // one piece of dressing a route may not simply be laid through — see
            // ArenaLayoutGenerator.BarrierKey.
            var metadata = new Dictionary<string, string>
            {
                { ArenaLayoutGenerator.BarrierKey, RectMetadata.Format(candidate.Footprint) },
            };

            doc.GeneratedObjects.Add(new PlacedObject(
                $"map/spawn_{team}/fence_{index.ToString("00", CultureInfo.InvariantCulture)}",
                candidate.LogicalId,
                pose,
                TagArray(candidate.Tags),
                metadata));
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
