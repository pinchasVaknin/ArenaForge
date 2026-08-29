using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Recovers the geometry of a placed object from the document and the catalog, the way an
    /// analysis pass or a validator would.
    /// </summary>
    /// <remarks>
    /// A world document stores a pose, not a footprint, so every suite that checks placement has
    /// to reconstruct one. Doing it from the yaw table only works because the generator places at
    /// table rotations and nowhere else, which the helper asserts rather than assumes.
    /// </remarks>
    static class PlacedGeometry
    {
        /// <summary>The yaw table step a placed object is rotated to.</summary>
        public static int YawSteps(PlacedObject placed)
        {
            for (int steps = 0; steps < YawStep.Count; steps++)
            {
                if (YawStep.Rotation(steps) == placed.Pose.Rotation)
                {
                    return steps;
                }
            }

            Assert.Fail($"{placed.StableId} is rotated off the yaw table: {placed.Pose.Rotation}");
            return -1;
        }

        /// <summary>The world-space ground footprint of a placed object.</summary>
        public static Rect2 WorldFootprint(PlacedObject placed, Catalog catalog)
        {
            CatalogEntry entry = catalog.Find(placed.LogicalId);
            Assert.That(entry, Is.Not.Null, $"{placed.StableId} names '{placed.LogicalId}', which the catalog lacks");

            return YawStep
                .Bounds(entry.Footprint, YawSteps(placed))
                .Translated(placed.Pose.Position.Xz);
        }

        /// <summary>True if the object is a piece of cover.</summary>
        /// <remarks>
        /// What <see cref="CoverPlacer"/> scattered, which is a narrower question than the one
        /// <c>MapAnalyzer.IsCover</c> asks: the analyser also counts street furniture towards the
        /// floor being within reach of cover, because a bench beside a road is something a player
        /// gets behind. The suites here are about the cover <em>stage</em> — its target, its
        /// spacing, its rules — so they want the pieces that stage put down and nothing else.
        /// </remarks>
        public static bool IsCover(PlacedObject placed) => HasTag(placed, CoverPlacer.CoverTag);

        /// <summary>True if the object is a structure.</summary>
        public static bool IsStructure(PlacedObject placed) =>
            HasTag(placed, ArenaLayoutGenerator.StructureTag);

        /// <summary>True if the object is attached to a socket of another object.</summary>
        /// <remarks>
        /// Read off the stable id, because that nesting is what the attachment <em>is</em>: a
        /// socket prop is the thing whose generation path runs through its parent's.
        /// </remarks>
        public static bool IsSocketProp(PlacedObject placed) => placed.StableId.Contains("/socket_");

        /// <summary>Everything that stands on the ground and therefore has to fit beside its neighbours.</summary>
        public static List<PlacedObject> GroundOccupants(WorldDoc doc)
        {
            var occupants = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (!IsSocketProp(placed) && (IsCover(placed) || IsStructure(placed)))
                {
                    occupants.Add(placed);
                }
            }

            return occupants;
        }

        /// <summary>The structures on a map, in generation order.</summary>
        /// <remarks>
        /// How many there are is decided by the ground rather than by a rule anything can be held
        /// to in advance — see <see cref="ArenaLayoutGenerator.StructureCells"/> — so a suite that
        /// wants "one pad per structure" or "the structures and nothing else" counts them here
        /// instead of adding up a composition.
        /// </remarks>
        public static List<PlacedObject> Structures(WorldDoc doc)
        {
            var structures = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (IsStructure(doc.GeneratedObjects[i]))
                {
                    structures.Add(doc.GeneratedObjects[i]);
                }
            }

            return structures;
        }

        /// <summary>Cover standing on the ground, in generation order.</summary>
        public static List<PlacedObject> GroundCover(WorldDoc doc)
        {
            var cover = new List<PlacedObject>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                PlacedObject placed = doc.GeneratedObjects[i];
                if (IsCover(placed) && !IsSocketProp(placed))
                {
                    cover.Add(placed);
                }
            }

            return cover;
        }

        /// <summary>Every doorway rectangle the document's structures declare.</summary>
        public static List<Rect2> Doorways(WorldDoc doc)
        {
            var doorways = new List<Rect2>();
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                IReadOnlyDictionary<string, string> metadata = doc.GeneratedObjects[i].Metadata;
                if (!metadata.TryGetValue(ArenaLayoutGenerator.DoorwayCountKey, out string countText))
                {
                    continue;
                }

                int count = int.Parse(countText);
                for (int d = 0; d < count; d++)
                {
                    doorways.Add(RectMetadata.Parse(
                        metadata[ArenaLayoutGenerator.DoorwayKeyPrefix + d.ToString("00")]));
                }
            }

            return doorways;
        }

        static bool HasTag(PlacedObject placed, string tag)
        {
            for (int i = 0; i < placed.Tags.Count; i++)
            {
                if (placed.Tags[i] == tag)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
