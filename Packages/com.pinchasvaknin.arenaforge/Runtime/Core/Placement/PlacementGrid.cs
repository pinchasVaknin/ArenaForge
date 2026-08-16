using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// Which cells of the arena grid are still open floor, and which an earlier stage has claimed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the walkable-space model cover sampling draws from. A structure claims its
    /// footprint plus the ring of floor kept clear around it, a spawn claims its area plus the
    /// clearance in front of it, and a doorway claims the ground a player needs to walk through
    /// it. What is left is where a prop may stand.
    /// </para>
    /// <para>
    /// The grid is a hint for the sampler, not the authority: <see cref="ConstraintSet"/> decides
    /// whether a candidate is legal. Keeping the two in step — claiming exactly the clearances the
    /// constraints enforce — is what stops the sampler from spending its candidates on positions
    /// that are going to be rejected anyway.
    /// </para>
    /// <para>
    /// A flat array of cells rather than a list of claimed rectangles, because the caller needs
    /// the open <em>area</em> of a lane to derive its cover target, and counting cells is the
    /// honest way to get that once claims can overlap one another.
    /// </para>
    /// </remarks>
    public sealed class PlacementGrid
    {
        readonly bool[] _free;

        /// <summary>Creates a grid with every cell open.</summary>
        public PlacementGrid(ArenaGrid grid)
        {
            Grid = grid;
            _free = new bool[grid.CellCount];
            for (int i = 0; i < _free.Length; i++)
            {
                _free[i] = true;
            }

            FreeCount = _free.Length;
        }

        /// <summary>The grid this covers.</summary>
        public ArenaGrid Grid { get; }

        /// <summary>How many cells are still open.</summary>
        public int FreeCount { get; private set; }

        /// <summary>Open floor area in square metres.</summary>
        public float FreeArea => FreeCount * Grid.CellArea;

        /// <summary>True if the cell is open. Indices outside the grid are not open.</summary>
        public bool IsFree(int x, int z) =>
            x >= 0 && x < Grid.CountX && z >= 0 && z < Grid.CountZ && _free[z * Grid.CountX + x];

        /// <summary>True if the cell containing the point is open.</summary>
        public bool IsFree(Vec2 point) => IsFree(
            (int)MathF.Floor((point.X - Grid.Bounds.MinX) / Grid.CellSize),
            (int)MathF.Floor((point.Y - Grid.Bounds.MinZ) / Grid.CellSize));

        /// <summary>
        /// Claims every cell the rectangle covers. A cell that only touches the rectangle's edge
        /// stays open, which matches <see cref="Rect2.Overlaps"/>: a claim and a placement may sit
        /// flush against one another.
        /// </summary>
        public void Claim(Rect2 area)
        {
            int minX = Math.Max(0, FloorCell(area.MinX, Grid.Bounds.MinX));
            int maxX = Math.Min(Grid.CountX - 1, CeilCell(area.MaxX, Grid.Bounds.MinX) - 1);
            int minZ = Math.Max(0, FloorCell(area.MinZ, Grid.Bounds.MinZ));
            int maxZ = Math.Min(Grid.CountZ - 1, CeilCell(area.MaxZ, Grid.Bounds.MinZ) - 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = z * Grid.CountX + x;
                    if (_free[index])
                    {
                        _free[index] = false;
                        FreeCount--;
                    }
                }
            }
        }

        /// <summary>
        /// Appends the centre of every open cell inside <paramref name="bounds"/> to
        /// <paramref name="centres"/>, walking the grid in a fixed order.
        /// </summary>
        /// <remarks>
        /// The fixed order is the point of the method: this list is what the sampler's first draw
        /// indexes into, so producing it from a set or a dictionary would make a generated map
        /// depend on hash layout.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="centres"/> is null.</exception>
        public void CollectOpenCentres(Rect2 bounds, List<Vec2> centres)
        {
            if (centres == null)
            {
                throw new ArgumentNullException(nameof(centres));
            }

            for (int z = 0; z < Grid.CountZ; z++)
            {
                for (int x = 0; x < Grid.CountX; x++)
                {
                    if (!_free[z * Grid.CountX + x])
                    {
                        continue;
                    }

                    Vec2 centre = Grid.CellCenter(x, z);
                    if (bounds.Contains(centre))
                    {
                        centres.Add(centre);
                    }
                }
            }
        }

        // A claim edge landing exactly on a grid line belongs to neither neighbour: the low edge
        // rounds into the cell above it and the high edge into the cell below, so a rectangle
        // flush with a cell boundary does not claim the cell on the far side of it.
        int FloorCell(float value, float origin) => (int)MathF.Floor((value - origin) / Grid.CellSize);

        int CeilCell(float value, float origin) => (int)MathF.Ceiling((value - origin) / Grid.CellSize);
    }
}
