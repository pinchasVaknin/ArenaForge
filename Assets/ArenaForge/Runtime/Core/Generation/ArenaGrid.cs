using System;

namespace ArenaForge.Core
{
    /// <summary>
    /// The placement grid covering a playfield: square cells of <see cref="CellSize"/> metres,
    /// anchored at the playfield's lower corner.
    /// </summary>
    /// <remarks>
    /// Snapping goes to cell <em>corners</em> rather than cell centres. Structure footprints are
    /// symmetric about their pivot and a whole number of cells across, so a pivot on a corner puts
    /// the footprint's edges on cell boundaries — which means the set of cells a structure covers
    /// is exact rather than a half-cell judgement call.
    /// </remarks>
    public readonly struct ArenaGrid
    {
        /// <summary>Area the grid covers.</summary>
        public Rect2 Bounds { get; }

        /// <summary>Cell edge length in metres.</summary>
        public float CellSize { get; }

        /// <summary>Number of whole cells along world X.</summary>
        public int CountX { get; }

        /// <summary>Number of whole cells along world Z.</summary>
        public int CountZ { get; }

        /// <summary>
        /// Creates a grid over <paramref name="bounds"/>. A playfield that is not a whole number of
        /// cells across leaves a sliver at its upper edge outside the last cell.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The cell size is not positive, or the bounds hold no whole cell.</exception>
        public ArenaGrid(Rect2 bounds, float cellSize)
        {
            if (!(cellSize > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize), cellSize, "Grid cell size must be positive.");
            }

            int countX = (int)MathF.Floor(bounds.Width / cellSize);
            int countZ = (int)MathF.Floor(bounds.Depth / cellSize);
            if (countX < 1 || countZ < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bounds), bounds,
                    $"A playfield of {bounds.Size} does not hold a single {cellSize} m cell.");
            }

            Bounds = bounds;
            CellSize = cellSize;
            CountX = countX;
            CountZ = countZ;
        }

        /// <summary>Number of cells in the grid.</summary>
        public int CellCount => CountX * CountZ;

        /// <summary>Area of one cell.</summary>
        public float CellArea => CellSize * CellSize;

        /// <summary>Bounds of one cell.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Either index is outside the grid.</exception>
        public Rect2 CellBounds(int x, int z)
        {
            if (x < 0 || x >= CountX)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Grid holds {CountX} cells along X.");
            }

            if (z < 0 || z >= CountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(z), z, $"Grid holds {CountZ} cells along Z.");
            }

            float minX = Bounds.MinX + x * CellSize;
            float minZ = Bounds.MinZ + z * CellSize;
            return new Rect2(minX, minZ, minX + CellSize, minZ + CellSize);
        }

        /// <summary>Centre of one cell.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Either index is outside the grid.</exception>
        public Vec2 CellCenter(int x, int z) => CellBounds(x, z).Center;

        /// <summary>Rounds one coordinate to the nearest grid line, measuring from the lower corner.</summary>
        public float SnapCoordinate(float value, float origin)
        {
            // AwayFromZero rather than the default ToEven so a coordinate exactly between two grid
            // lines always resolves the same way, whichever line happens to be even.
            float steps = MathF.Round((value - origin) / CellSize, MidpointRounding.AwayFromZero);
            return origin + steps * CellSize;
        }

        /// <summary>Rounds a point to the nearest grid intersection.</summary>
        public Vec2 Snap(Vec2 point) => new Vec2(
            SnapCoordinate(point.X, Bounds.MinX),
            SnapCoordinate(point.Y, Bounds.MinZ));

        /// <summary>True if the point lies on a grid intersection, within a tenth of a millimetre.</summary>
        public bool IsOnGrid(Vec2 point) =>
            MathF.Abs(point.X - SnapCoordinate(point.X, Bounds.MinX)) < 1e-4f &&
            MathF.Abs(point.Y - SnapCoordinate(point.Y, Bounds.MinZ)) < 1e-4f;
    }
}
