using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Measures a generated map: who can see where, what can be walked to, and whether the result
    /// is worth playing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of ArenaForge that a procedural generator is usually missing. Placement
    /// rules say a crate is legal where it stands; nothing in them says the map that came out has a
    /// sightline down its whole length, a spawn overlooked from the other team's balcony, or a
    /// corner of floor walled off from the rest. Those are properties of the finished map, and the
    /// only way to know them is to measure it.
    /// </para>
    /// <para>
    /// It runs on a resolved document, so a map a user has hand-edited is measured as it actually
    /// is rather than as the generator left it.
    /// </para>
    /// <para>
    /// No engine, and in particular no physics: visibility is segment-versus-rectangle arithmetic
    /// over <see cref="Occluder"/>. A raycast would tie the metric to a scene being loaded and a
    /// collider being present on every prefab, and would put the thousand-seed suite behind the
    /// editor's frame loop.
    /// </para>
    /// </remarks>
    public static class MapAnalyzer
    {
        /// <summary>RNG fork label the observer sample is drawn from.</summary>
        const string ObserverStreamLabel = "analysis/observers";

        /// <summary>
        /// Measures a map and judges it against the thresholds.
        /// </summary>
        /// <param name="doc">The document to measure. Its overrides are applied first.</param>
        /// <param name="catalog">The catalog the document's logical ids refer to.</param>
        /// <param name="parameters">How to measure. Defaults are used when this is null.</param>
        /// <param name="thresholds">What counts as playable. Defaults are used when this is null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="doc"/> or <paramref name="catalog"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">An analysis parameter is outside its supported range.</exception>
        public static MapReport Analyze(
            WorldDoc doc,
            Catalog catalog,
            AnalysisParams parameters = null,
            MapThresholds thresholds = null)
        {
            if (doc == null)
            {
                throw new ArgumentNullException(nameof(doc));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            AnalysisParams settings = parameters ?? new AnalysisParams();
            MapThresholds limits = thresholds ?? new MapThresholds();
            Validate(settings);

            ArenaLayout layout = ArenaLayout.Build(doc.Parameters);
            IReadOnlyList<PlacedObject> objects = doc.Resolve().Objects;

            var structures = new List<Rect2>();
            var cover = new List<Rect2>();
            var occluders = new List<Occluder>();
            Collect(objects, catalog, settings.EyeHeight, structures, cover, occluders);

            WalkableGrid walkable = WalkableGrid.Build(layout, structures);
            ExposureMap exposure = MeasureExposure(
                walkable, occluders.ToArray(), doc.Parameters.Seed, settings.ObserverSamples,
                out float maxOpenSightline);

            ConnectivityReport connectivity = MeasureConnectivity(walkable, layout, objects);

            return new MapReport(
                exposure,
                limits,
                layout.SpawnSeparation,
                limits.MinSpawnSeparationFraction * layout.LongExtent,
                MathF.Abs(
                    exposure.MeanWithin(layout.SpawnAreaA.Center, settings.SpawnAnalysisRadius) -
                    exposure.MeanWithin(layout.SpawnAreaB.Center, settings.SpawnAnalysisRadius)),
                MeasureCoverCoverage(walkable, cover, settings.CoverRadius),
                maxOpenSightline,
                limits.MaxOpenSightlineFraction * layout.Playfield.Size.Length,
                connectivity,
                PlacementStats.ReadFrom(doc.Metadata, CoverPlacer.StatsPrefix));
        }

        static void Validate(AnalysisParams parameters)
        {
            if (!(parameters.EyeHeight > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.EyeHeight, "Eye height must be positive.");
            }

            if (parameters.ObserverSamples < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.ObserverSamples,
                    "Exposure needs at least one observer to be a fraction of anything.");
            }

            if (!(parameters.SpawnAnalysisRadius > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.SpawnAnalysisRadius,
                    "The spawn analysis radius must be positive.");
            }

            if (!(parameters.CoverRadius > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(parameters), parameters.CoverRadius, "The cover radius must be positive.");
            }
        }

        /// <summary>
        /// Sorts the resolved objects into what blocks movement, what counts as cover, and what
        /// blocks sight, in one pass over the document.
        /// </summary>
        static void Collect(
            IReadOnlyList<PlacedObject> objects,
            Catalog catalog,
            float eyeHeight,
            List<Rect2> structures,
            List<Rect2> cover,
            List<Occluder> occluders)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                PlacedObject placed = objects[i];
                CatalogEntry entry = catalog.Find(placed.LogicalId);
                if (entry == null)
                {
                    // A document naming art the catalog no longer carries. The map is still
                    // measurable without it, and reporting on what is there beats refusing to
                    // report at all.
                    continue;
                }

                if (HasTag(placed, ArenaLayoutGenerator.StructureTag))
                {
                    structures.Add(WorldFootprint(placed, entry));
                }
                else if (IsCover(placed))
                {
                    cover.Add(WorldFootprint(placed, entry));
                }

                if (Occluder.TryCreate(placed, entry, eyeHeight, out Occluder occluder))
                {
                    occluders.Add(occluder);
                }
            }

            // Biggest first. Evaluation stops at the first occluder that blocks a segment, and a
            // building blocks far more sightlines than a barrier does, so testing it first is what
            // makes the common case one test rather than twenty.
            occluders.Sort((a, b) => b.Area.CompareTo(a.Area));
        }

        /// <summary>
        /// Runs the visibility sweep: every walkable cell against every sampled observer.
        /// </summary>
        /// <remarks>
        /// Cell-major rather than observer-major, so the one write per cell happens once and the
        /// observer positions — a couple of kilobytes — stay in cache across the whole inner loop.
        /// Nothing in the loop allocates.
        /// </remarks>
        static ExposureMap MeasureExposure(
            WalkableGrid walkable,
            Occluder[] occluders,
            ulong seed,
            int observerSamples,
            out float maxOpenSightline)
        {
            var values = new float[walkable.Count];
            maxOpenSightline = 0f;

            if (walkable.Count == 0)
            {
                return new ExposureMap(walkable, values, 0);
            }

            Vec2[] observers = SampleObservers(walkable, seed, observerSamples);
            float inverseObservers = 1f / observers.Length;
            float longestSquared = 0f;

            for (int cell = 0; cell < values.Length; cell++)
            {
                Vec2 target = walkable.CentreOf(cell);
                int seen = 0;

                for (int o = 0; o < observers.Length; o++)
                {
                    Vec2 eye = observers[o];
                    if (IsBlocked(occluders, eye, target))
                    {
                        continue;
                    }

                    seen++;

                    float dx = eye.X - target.X;
                    float dz = eye.Y - target.Y;
                    float distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared > longestSquared)
                    {
                        longestSquared = distanceSquared;
                    }
                }

                values[cell] = seen * inverseObservers;
            }

            maxOpenSightline = MathF.Sqrt(longestSquared);
            return new ExposureMap(walkable, values, observers.Length);
        }

        /// <summary>
        /// Draws the observer positions: a partial Fisher-Yates over the walkable indices, so the
        /// sample holds no duplicates and comes out of the seeded stream rather than out of a set's
        /// iteration order.
        /// </summary>
        static Vec2[] SampleObservers(WalkableGrid walkable, ulong seed, int wanted)
        {
            if (wanted >= walkable.Count)
            {
                var all = new Vec2[walkable.Count];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = walkable.CentreOf(i);
                }

                return all;
            }

            var pool = new int[walkable.Count];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = i;
            }

            Rng stream = new Rng(seed).Fork(ObserverStreamLabel);
            var observers = new Vec2[wanted];
            for (int i = 0; i < wanted; i++)
            {
                int pick = stream.NextRange(i, pool.Length);
                int swap = pool[i];
                pool[i] = pool[pick];
                pool[pick] = swap;
                observers[i] = walkable.CentreOf(pool[i]);
            }

            return observers;
        }

        /// <summary>True if anything stands between the two points at eye height.</summary>
        static bool IsBlocked(Occluder[] occluders, Vec2 from, Vec2 to)
        {
            float minX = MathF.Min(from.X, to.X);
            float maxX = MathF.Max(from.X, to.X);
            float minZ = MathF.Min(from.Y, to.Y);
            float maxZ = MathF.Max(from.Y, to.Y);

            for (int i = 0; i < occluders.Length; i++)
            {
                if (occluders[i].MightBlock(minX, minZ, maxX, maxZ) &&
                    occluders[i].Blocks(from, to))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Share of the walkable floor within <paramref name="radius"/> of a piece of cover.</summary>
        static float MeasureCoverCoverage(WalkableGrid walkable, List<Rect2> cover, float radius)
        {
            if (walkable.Count == 0)
            {
                return 0f;
            }

            if (cover.Count == 0)
            {
                return 0f;
            }

            // Distance to the prop's footprint rather than to its pivot, so a long barrier covers
            // the ground along its length and not just the ground beside its middle.
            var grown = new Rect2[cover.Count];
            for (int i = 0; i < cover.Count; i++)
            {
                grown[i] = cover[i].Expanded(radius);
            }

            int covered = 0;
            for (int cell = 0; cell < walkable.Count; cell++)
            {
                Vec2 centre = walkable.CentreOf(cell);
                for (int i = 0; i < grown.Length; i++)
                {
                    if (grown[i].Contains(centre))
                    {
                        covered++;
                        break;
                    }
                }
            }

            return (float)covered / walkable.Count;
        }

        /// <summary>Floods out from spawn A and reports what it found.</summary>
        static ConnectivityReport MeasureConnectivity(
            WalkableGrid walkable, ArenaLayout layout, IReadOnlyList<PlacedObject> objects)
        {
            if (walkable.Count == 0)
            {
                return new ConnectivityReport(false, 0, 0, 0f);
            }

            bool[] reached = walkable.ReachableFrom(layout.SpawnAreaA);

            int reachedCount = 0;
            for (int i = 0; i < reached.Length; i++)
            {
                if (reached[i])
                {
                    reachedCount++;
                }
            }

            List<Rect2> doorways = Doorways(objects);
            int reachableDoorways = 0;
            for (int i = 0; i < doorways.Count; i++)
            {
                // A doorway rectangle straddles the wall, so half of it is inside the structure and
                // therefore not walkable. Reaching any walkable cell of it is reaching the door.
                if (walkable.AnyReached(doorways[i], reached))
                {
                    reachableDoorways++;
                }
            }

            return new ConnectivityReport(
                walkable.AnyReached(layout.SpawnAreaB, reached),
                doorways.Count,
                reachableDoorways,
                (float)reachedCount / walkable.Count);
        }

        /// <summary>
        /// Every doorway rectangle the map's structures declare, in document order.
        /// </summary>
        /// <remarks>
        /// Read off the resolved objects rather than the generated ones, so a building a user
        /// deleted by hand takes its doorways with it instead of leaving the validator looking for
        /// a way into a building that is no longer there.
        /// </remarks>
        static List<Rect2> Doorways(IReadOnlyList<PlacedObject> objects)
        {
            var doorways = new List<Rect2>();
            for (int i = 0; i < objects.Count; i++)
            {
                IReadOnlyDictionary<string, string> metadata = objects[i].Metadata;
                if (!metadata.TryGetValue(ArenaLayoutGenerator.DoorwayCountKey, out string countText) ||
                    !int.TryParse(
                        countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    continue;
                }

                for (int d = 0; d < count; d++)
                {
                    string key = ArenaLayoutGenerator.DoorwayKeyPrefix +
                                 d.ToString("00", CultureInfo.InvariantCulture);
                    if (metadata.TryGetValue(key, out string rect) &&
                        RectMetadata.TryParse(rect, out Rect2 doorway))
                    {
                        doorways.Add(doorway);
                    }
                }
            }

            return doorways;
        }

        /// <summary>
        /// The world-space bounds of a placed object's footprint.
        /// </summary>
        /// <remarks>
        /// The axis-aligned box, not the oriented rectangle. Its callers are the walkable set and
        /// the cover radius, and both want the conservative answer: a cell a rotated barrier
        /// clips is floor that plays as blocked, and cover you are diagonally behind still counts.
        /// </remarks>
        static Rect2 WorldFootprint(PlacedObject placed, CatalogEntry entry)
        {
            Pose pose = placed.Pose;
            Rect2 local = entry.Footprint;

            Vec3 a = pose.TransformPoint(new Vec3(local.MinX, 0f, local.MinZ));
            Vec3 b = pose.TransformPoint(new Vec3(local.MaxX, 0f, local.MinZ));
            Vec3 c = pose.TransformPoint(new Vec3(local.MaxX, 0f, local.MaxZ));
            Vec3 d = pose.TransformPoint(new Vec3(local.MinX, 0f, local.MaxZ));

            return new Rect2(
                MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)),
                MathF.Min(MathF.Min(a.Z, b.Z), MathF.Min(c.Z, d.Z)),
                MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)),
                MathF.Max(MathF.Max(a.Z, b.Z), MathF.Max(c.Z, d.Z)));
        }

        /// <summary>
        /// True for everything that counts towards a cell being within reach of cover.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Two tags, because two things on a map are cover and one of them was not placed
        /// as cover.</strong> A crate is what <see cref="CoverPlacer"/> scattered and a bench is
        /// what <see cref="RoadFurniture"/> stood on a verge, and in a sixty-metre arena the second
        /// is not decoration: a player pinned in the open beside a road reaches the bench, and it
        /// is the only thing to reach. So a piece of street furniture fulfils the cover a stretch of
        /// floor needs in exactly the way the crate it stands in place of would.
        /// </para>
        /// <para>
        /// <strong>The kerbing does not, and that is the same judgement made the other way.</strong>
        /// A kerb is a line of stone a few centimetres proud of the road; nobody takes cover behind
        /// one. It carries no cover tag, so nothing here has to exclude it — which is worth saying
        /// because the two are placed by neighbouring stages from neighbouring folders, and the
        /// difference between them is what a player can get behind rather than where they came
        /// from.
        /// </para>
        /// <para>
        /// This is only the count. Whether a piece of either kind blocks a sightline is a separate
        /// question and is asked separately, of every object on the map, by
        /// <see cref="Occluder.TryCreate"/> — which is why a bench shelters the ground beside it
        /// here and still does not block a standing player's view over it.
        /// </para>
        /// </remarks>
        static bool IsCover(PlacedObject placed) =>
            HasTag(placed, CoverPlacer.CoverTag) ||
            HasTag(placed, RoadFurniture.FurnitureTag);

        static bool HasTag(PlacedObject placed, string tag)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (string.Equals(placed.Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
