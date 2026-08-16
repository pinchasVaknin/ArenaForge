using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArenaForge.Core
{
    /// <summary>
    /// Turns parameters and a catalog into a small competitive arena: lane bands, two opposing
    /// spawns, the structures that anchor the middle and one flank, and the cover between them.
    /// </summary>
    /// <remarks>
    /// The shape follows how these maps are actually built. Two spawns at either end, a handful of
    /// roughly parallel routes between them, a strong point of interest in the contested middle
    /// lane, and something softer on a flank. Cover is scattered last, by
    /// <see cref="CoverPlacer"/>, because it has to place around everything else.
    /// </remarks>
    public static class ArenaLayoutGenerator
    {
        /// <summary>Metadata key naming the lane an object belongs to.</summary>
        public const string LaneKey = "lane";

        /// <summary>Metadata key naming the side a spawn marker belongs to — <c>a</c> or <c>b</c>.</summary>
        public const string TeamKey = "team";

        /// <summary>Metadata key holding a spawn marker's area as a rectangle.</summary>
        public const string SpawnAreaKey = "spawn_area";

        /// <summary>Metadata key holding how many doorway rectangles a structure declares.</summary>
        public const string DoorwayCountKey = "doorway_count";

        /// <summary>Prefix of the metadata keys holding a structure's doorway rectangles.</summary>
        public const string DoorwayKeyPrefix = "doorway_";

        /// <summary>Tag a catalog entry must carry to be used as a spawn marker.</summary>
        public const string SpawnMarkerTag = "spawn";

        /// <summary>Tag every structure carries, whichever slot it fills.</summary>
        public const string StructureTag = "structure";

        /// <summary>Tag a catalog entry must carry to be used as the middle-lane building.</summary>
        public const string BuildingTag = "structure/building";

        /// <summary>Tag a catalog entry must carry to be used as the flank house.</summary>
        public const string HouseTag = "structure/house";

        /// <summary>Clear space a structure keeps from a spawn area, in metres.</summary>
        public const float SpawnClearance = 2f;

        /// <summary>Clear space two structures keep from each other, in metres.</summary>
        public const float StructureClearance = 2f;

        /// <summary>Share of its lane a structure may cover at <c>StructureDensity</c> of 1.</summary>
        const float BaselineLaneShare = 0.25f;

        /// <summary>How far from the centre of the long axis the middle structure may sit.</summary>
        const float BuildingCentreSpan = 0.15f;

        const float DoorwayWidth = 2.5f;
        const float DoorwayDepth = 1f;
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
        /// The catalog cannot supply a required piece, or a structure will not fit anywhere in its lane.
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
            var doc = new WorldDoc { Parameters = parameters.Clone() };

            EmitSpawnMarkers(doc, layout, catalog);
            List<Placement> structures = EmitStructures(doc, layout, catalog, parameters);
            CoverPlacer.Place(doc, layout, catalog, structures);

            return doc;
        }

        static void EmitSpawnMarkers(WorldDoc doc, ArenaLayout layout, Catalog catalog)
        {
            CatalogEntry marker = RequireEntry(catalog, SpawnMarkerTag);

            // Each spawn faces its opposite number: the low-end marker looks up the long axis, the
            // high-end one looks back down it.
            int facingUp = layout.LanesRunAlongZ ? 0 : 1;

            doc.GeneratedObjects.Add(SpawnMarker(marker, "a", layout.SpawnAreaA, facingUp));
            doc.GeneratedObjects.Add(SpawnMarker(marker, "b", layout.SpawnAreaB, facingUp + 2));
        }

        static PlacedObject SpawnMarker(CatalogEntry entry, string team, Rect2 area, int quarterTurns) =>
            new PlacedObject(
                $"map/spawn_{team}/marker",
                entry.LogicalId,
                new Pose(area.Center.ToVec3(0f), QuarterTurn.Rotation(quarterTurns), 1f),
                ToArray(entry.Tags),
                new Dictionary<string, string>
                {
                    { TeamKey, team },
                    { SpawnAreaKey, RectMetadata.Format(area) },
                });

        /// <summary>The structures placed, with their world footprints, for the cover stage.</summary>
        static List<Placement> EmitStructures(
            WorldDoc doc, ArenaLayout layout, Catalog catalog, ArenaParams parameters)
        {
            Rng stream = new Rng(parameters.Seed).Fork("structures");
            var committed = new List<PlacedStructure>(2);
            var placements = new List<Placement>(2);

            ArenaLane middle = layout.Lanes[layout.MiddleLaneIndex];
            float centre = layout.AlongOf(layout.Playfield.Center);
            float reach = layout.LongExtent * BuildingCentreSpan;

            CatalogEntry building = SelectStructure(catalog, BuildingTag, middle, parameters, ref stream);
            doc.GeneratedObjects.Add(PlaceStructure(
                layout, middle, building, centre - reach, centre + reach, committed, placements, ref stream));

            // A flank rather than the middle, so the two structures anchor different routes. A
            // one-lane map has no flank, and the house shares the middle lane with the building.
            ArenaLane flank = PickFlankLane(layout, ref stream);
            float betweenSpawnsMin = layout.AlongOf(layout.SpawnAreaA.Max) + SpawnClearance;
            float betweenSpawnsMax = layout.AlongOf(layout.SpawnAreaB.Min) - SpawnClearance;

            CatalogEntry house = SelectStructure(catalog, HouseTag, flank, parameters, ref stream);
            doc.GeneratedObjects.Add(PlaceStructure(
                layout, flank, house, betweenSpawnsMin, betweenSpawnsMax, committed, placements, ref stream));

            return placements;
        }

        static ArenaLane PickFlankLane(ArenaLayout layout, ref Rng rng)
        {
            var flanks = new List<ArenaLane>(layout.Lanes.Count);
            for (int i = 0; i < layout.Lanes.Count; i++)
            {
                if (i != layout.MiddleLaneIndex)
                {
                    flanks.Add(layout.Lanes[i]);
                }
            }

            return flanks.Count > 0 ? rng.Pick(flanks) : layout.Lanes[layout.MiddleLaneIndex];
        }

        /// <summary>
        /// Chooses which piece of art fills a structure slot: a weighted pick over the entries that
        /// fit the lane's size budget, or the smallest entry when none of them do.
        /// </summary>
        /// <remarks>
        /// This is what <see cref="ArenaParams.StructureDensity"/> controls. A density of 1 lets a
        /// structure cover a quarter of its lane; halving it restricts the generator to entries
        /// half that size, so a sparser map is a map of smaller buildings rather than a map with
        /// the same building scaled down.
        /// </remarks>
        static CatalogEntry SelectStructure(
            Catalog catalog, string tag, ArenaLane lane, ArenaParams parameters, ref Rng rng)
        {
            IReadOnlyList<CatalogEntry> matches = RequireEntries(catalog, tag);
            float budget = parameters.StructureDensity * BaselineLaneShare * lane.Band.Area;

            var affordable = new List<CatalogEntry>(matches.Count);
            CatalogEntry smallest = matches[0];
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Footprint.Area <= budget)
                {
                    affordable.Add(matches[i]);
                }

                if (matches[i].Footprint.Area < smallest.Footprint.Area)
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

        static PlacedObject PlaceStructure(
            ArenaLayout layout,
            ArenaLane lane,
            CatalogEntry entry,
            float alongMin,
            float alongMax,
            List<PlacedStructure> committed,
            List<Placement> placements,
            ref Rng rng)
        {
            float cross = layout.SnapCross(layout.CrossOf(lane.Band.Center));

            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                int quarterTurns = rng.NextRange(0, QuarterTurn.Count);
                Rect2 local = QuarterTurn.Rotate(entry.Footprint, quarterTurns);

                float low = alongMin - layout.AlongOf(local.Min);
                float high = alongMax - layout.AlongOf(local.Max);
                if (high < low)
                {
                    // This orientation is longer than the span allows. Draw anyway so the stream
                    // advances identically whichever orientation came up, then reject.
                    rng.NextFloat();
                    continue;
                }

                float along = layout.SnapAlong(rng.NextRange(low, high));
                if (TryCommit(layout, lane, entry, along, cross, quarterTurns, committed, placements,
                        out PlacedObject placed))
                {
                    return placed;
                }
            }

            // Every draw was rejected. Sweep the span instead, so a tight map produces a structure
            // in the one place it fits rather than failing because the draws missed it.
            for (int quarterTurns = 0; quarterTurns < QuarterTurn.Count; quarterTurns++)
            {
                Rect2 local = QuarterTurn.Rotate(entry.Footprint, quarterTurns);
                float low = layout.SnapAlong(alongMin - layout.AlongOf(local.Min));
                float high = alongMax - layout.AlongOf(local.Max);
                int steps = (int)MathF.Floor((high - low) / layout.Grid.CellSize);

                // Stepped from the start rather than accumulated, so a fractional cell size cannot
                // drift the sweep off the grid.
                for (int step = 0; step <= steps; step++)
                {
                    float along = low + step * layout.Grid.CellSize;
                    if (TryCommit(layout, lane, entry, along, cross, quarterTurns, committed, placements,
                            out PlacedObject placed))
                    {
                        return placed;
                    }
                }
            }

            throw new InvalidOperationException(
                $"'{entry.LogicalId}' does not fit anywhere in {lane.Id} between " +
                $"{alongMin.ToString("0.##", CultureInfo.InvariantCulture)} and " +
                $"{alongMax.ToString("0.##", CultureInfo.InvariantCulture)} along the map. " +
                "Either the playfield is too small for this catalog or the lane count is too high.");
        }

        static bool TryCommit(
            ArenaLayout layout,
            ArenaLane lane,
            CatalogEntry entry,
            float along,
            float cross,
            int quarterTurns,
            List<PlacedStructure> committed,
            List<Placement> placements,
            out PlacedObject placed)
        {
            placed = null;

            Vec2 position = layout.ToWorld(along, cross);
            Placement candidate = Placement.AtQuarterTurn(entry, position, quarterTurns);
            Rect2 world = candidate.Footprint;

            if (!layout.Playfield.Contains(world))
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

            int slot = 0;
            for (int i = 0; i < committed.Count; i++)
            {
                if (string.Equals(committed[i].LaneId, lane.Id, StringComparison.Ordinal))
                {
                    slot++;
                }
            }

            var metadata = new Dictionary<string, string> { { LaneKey, lane.Id } };
            AddDoorways(metadata, layout, world);

            placed = new PlacedObject(
                $"map/{lane.Id}/structure_{slot.ToString("00", CultureInfo.InvariantCulture)}",
                candidate.LogicalId,
                candidate.Pose,
                ToArray(candidate.Tags),
                metadata);

            committed.Add(new PlacedStructure(lane.Id, world));
            placements.Add(candidate);
            return true;
        }

        /// <summary>
        /// Records where a structure can be entered, as rectangles in world space.
        /// </summary>
        /// <remarks>
        /// Doorways are derived from the footprint rather than declared by the catalog entry: the
        /// catalog describes art, and a doorway is a fact about where this structure sits on this
        /// map. Both go on the faces across the lane, so the structure can be run through along
        /// the route it blocks. Cover placement reads these back and keeps them clear.
        /// </remarks>
        static void AddDoorways(IDictionary<string, string> metadata, ArenaLayout layout, Rect2 world)
        {
            float face = layout.LanesRunAlongZ ? world.Width : world.Depth;
            float width = MathF.Min(DoorwayWidth, face * DoorwayShareOfFace);
            float cross = layout.CrossOf(world.Center);

            metadata[DoorwayCountKey] = "2";
            metadata[DoorwayKeyPrefix + "00"] =
                RectMetadata.Format(Doorway(layout, layout.AlongOf(world.Min), cross, width));
            metadata[DoorwayKeyPrefix + "01"] =
                RectMetadata.Format(Doorway(layout, layout.AlongOf(world.Max), cross, width));
        }

        static Rect2 Doorway(ArenaLayout layout, float along, float cross, float width) => Rect2.FromCorners(
            layout.ToWorld(along - DoorwayDepth * 0.5f, cross - width * 0.5f),
            layout.ToWorld(along + DoorwayDepth * 0.5f, cross + width * 0.5f));

        static CatalogEntry RequireEntry(Catalog catalog, string tag) => RequireEntries(catalog, tag)[0];

        static IReadOnlyList<CatalogEntry> RequireEntries(Catalog catalog, string tag)
        {
            IReadOnlyList<CatalogEntry> matches = catalog.Query(TagQuery.All(tag));
            if (matches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The catalog has no entry tagged '{tag}', so this map cannot be generated.");
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

        /// <summary>A structure already on the map, for the overlap and slot-numbering checks.</summary>
        readonly struct PlacedStructure
        {
            public PlacedStructure(string laneId, Rect2 footprint)
            {
                LaneId = laneId;
                Footprint = footprint;
            }

            public string LaneId { get; }

            public Rect2 Footprint { get; }
        }
    }
}
