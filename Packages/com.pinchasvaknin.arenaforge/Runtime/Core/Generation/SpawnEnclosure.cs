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
    /// <strong>Two thirds of it is fence.</strong> Over forty seeds at the art pack's own panel
    /// length, 65.7% of a ring's perimeter carries a panel, 23.9% is the four gates and the
    /// remaining 10.4% is tail and corner handover — ground where no whole panel fits. So the ring
    /// reads as an enclosure with ways out of it rather than as either a pen or a token. The gate
    /// is a fixed width and the ring is sized off the pad, so a smaller map makes the gaps a larger
    /// share: what stays constant is that a person can always get out, which is the point of them.
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

        /// <summary>How much of a face a gate may be placed in, measured from its middle.</summary>
        /// <remarks>
        /// Three fifths. A gate anywhere in the outer fifths is a gate at a corner, which is both
        /// hard to see from inside the ring and the place two runs are already handing panels over
        /// — and a corner that is a gap on one face and a panel on the other reads as a mistake
        /// rather than as a way out.
        /// </remarks>
        const float GateShare = 0.6f;

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
            Rect2 ring = Square(ArenaLayoutGenerator.SpawnPadRadius(area), area.Center, thickness);

            // One height for the whole ring, read at the middle of it. The pad the spawn stands on
            // is graded dead flat before this runs and the ring is inside the pad, so every point
            // under it is this height — the single sample is the whole answer rather than an
            // approximation of it.
            float elevation = terrain.HeightAt(ring.Center);

            List<Rect2> gates = Gates(ring, doc.Parameters.PathWidth, ref stream);
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

        /// <summary>The square the ring runs round: the one inscribed in the spawn's graded disc.</summary>
        /// <remarks>
        /// A square inscribed in a circle of radius r has a half-side of r over root two, and a
        /// panel is seated astride its line, so half a thickness comes off on top to keep the art
        /// itself on the pad rather than merely its centreline. That also settles what used to be a
        /// separate problem: the band reaches the playfield boundary on its outer side, so a ring on
        /// the band put one of its four runs exactly where <see cref="PerimeterFence"/> had already
        /// tiled the world's edge and every panel of that run was refused. The inscribed square is
        /// well inside it.
        /// </remarks>
        static Rect2 Square(float radius, Vec2 centre, float thickness)
        {
            float half = MathF.Max(
                thickness, radius * 0.70710678f - thickness * 0.5f);

            return new Rect2(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
        }

        /// <summary>One gate per face, each in the middle <see cref="GateShare"/> of its own side.</summary>
        /// <remarks>
        /// The four draws are taken in face order and always all four, so the stream reads the same
        /// whatever the tiling then makes of them — a gate that happens to fall where no panel would
        /// have gone still costs its draw.
        /// </remarks>
        static List<Rect2> Gates(Rect2 ring, float width, ref Rng stream)
        {
            float half = width * 0.5f;
            float insetX = ring.Width * (1f - GateShare) * 0.5f;
            float insetZ = ring.Depth * (1f - GateShare) * 0.5f;

            float lowZ = stream.NextRange(ring.MinX + insetX, ring.MaxX - insetX);
            float highX = stream.NextRange(ring.MinZ + insetZ, ring.MaxZ - insetZ);
            float highZ = stream.NextRange(ring.MinX + insetX, ring.MaxX - insetX);
            float lowX = stream.NextRange(ring.MinZ + insetZ, ring.MaxZ - insetZ);

            return new List<Rect2>(WallRun.FaceCount)
            {
                new Rect2(lowZ - half, ring.MinZ - half, lowZ + half, ring.MinZ + half),
                new Rect2(ring.MaxX - half, highX - half, ring.MaxX + half, highX + half),
                new Rect2(highZ - half, ring.MaxZ - half, highZ + half, ring.MaxZ + half),
                new Rect2(ring.MinX - half, lowX - half, ring.MinX + half, lowX + half),
            };
        }

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

            doc.GeneratedObjects.Add(new PlacedObject(
                $"map/spawn_{team}/fence_{index.ToString("00", CultureInfo.InvariantCulture)}",
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
