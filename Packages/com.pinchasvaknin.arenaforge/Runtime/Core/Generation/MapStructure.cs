using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// A structure standing on a map: the object the document holds, and the exact ground it
    /// covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves travel together because every stage that places <em>around</em> a building
    /// needs both, and neither can be recovered from the other without giving something up. A
    /// <see cref="PlacedObject"/> carries the stable id, the lane and the doorway rectangles, but
    /// only a pose — turning that back into a footprint means rotating four corners through a
    /// quaternion, which lands a rounding error away from the exact axis swap the placement was
    /// made with. A <see cref="Placement"/> carries that exact footprint and the catalog's tags,
    /// and knows nothing about where in the document the structure was filed.
    /// </para>
    /// <para>
    /// Pairing them at the moment of placement, where both are in hand, is cheaper than either
    /// half re-deriving the other and cannot drift from what was actually put down.
    /// </para>
    /// </remarks>
    public readonly struct MapStructure
    {
        /// <summary>Pairs a placed structure with the candidate it was committed from.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="placed"/> is null.</exception>
        public MapStructure(PlacedObject placed, Placement ground)
        {
            if (placed == null)
            {
                throw new ArgumentNullException(nameof(placed));
            }

            Placed = placed;
            Ground = ground;
        }

        /// <summary>The structure as the document holds it.</summary>
        public PlacedObject Placed { get; }

        /// <summary>The candidate it was committed from, with its exact world footprint.</summary>
        public Placement Ground { get; }

        /// <summary>The ground the structure stands on.</summary>
        public Rect2 Footprint => Ground.Footprint;

        /// <summary>The lane it was placed in, or an empty string when it names none.</summary>
        public string LaneId =>
            Placed.Metadata.TryGetValue(ArenaLayoutGenerator.LaneKey, out string lane)
                ? lane
                : string.Empty;

        /// <summary>True when this is the flank house, which is what a garden fence goes round.</summary>
        public bool IsHouse => Ground.HasTag(ArenaLayoutGenerator.HouseTag);

        /// <inheritdoc />
        public override string ToString() => Placed.ToString();
    }
}
