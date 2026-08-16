using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// The floor a player can stand on: the playfield grid with the cells a structure occupies
    /// taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Structures only. Cover is something you walk round or vault, not something that removes a
    /// route, and a model that deleted the cell under every crate would report a well-furnished
    /// lane as a broken one. What cover does to the map is a visibility question, and
    /// <see cref="Occluder"/> answers that separately.
    /// </para>
    /// <para>
    /// Cells are numbered twice over. The grid index is the position on the playfield and is what
    /// the flood fill walks; the walkable index is a dense 0..<see cref="Count"/> numbering, which
    /// is what the exposure array is sized by. Keeping both means neither the analysis nor the
    /// heatmap has to carry blocked cells around.
    /// </para>
    /// </remarks>
    public sealed class WalkableGrid
    {
        readonly bool[] _walkable;
        readonly int[] _walkableIndexOfCell;
        readonly int[] _cellOfWalkableIndex;
        readonly Vec2[] _centres;

        WalkableGrid(
            ArenaGrid grid,
            bool[] walkable,
            int[] walkableIndexOfCell,
            int[] cellOfWalkableIndex,
            Vec2[] centres)
        {
            Grid = grid;
            _walkable = walkable;
            _walkableIndexOfCell = walkableIndexOfCell;
            _cellOfWalkableIndex = cellOfWalkableIndex;
            _centres = centres;
        }

        /// <summary>The playfield grid this was derived from.</summary>
        public ArenaGrid Grid { get; }

        /// <summary>How many cells are walkable.</summary>
        public int Count => _centres.Length;

        /// <summary>Walkable floor area in square metres.</summary>
        public float Area => Count * Grid.CellArea;

        /// <summary>Centre of each walkable cell, in walkable-index order.</summary>
        public IReadOnlyList<Vec2> Centres => _centres;

        /// <summary>
        /// Builds the walkable set for a layout, blocking every cell a structure footprint covers.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        public static WalkableGrid Build(ArenaLayout layout, IReadOnlyList<Rect2> blocked)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (blocked == null)
            {
                throw new ArgumentNullException(nameof(blocked));
            }

            ArenaGrid grid = layout.Grid;
            var walkable = new bool[grid.CellCount];
            var walkableIndexOfCell = new int[grid.CellCount];
            int count = 0;

            for (int z = 0; z < grid.CountZ; z++)
            {
                for (int x = 0; x < grid.CountX; x++)
                {
                    int cell = z * grid.CountX + x;
                    Rect2 bounds = grid.CellBounds(x, z);

                    bool free = true;
                    for (int i = 0; i < blocked.Count && free; i++)
                    {
                        // Overlap, not centre containment: a cell a wall runs through is not floor
                        // you can stand on, whichever side of it the centre happens to fall.
                        free = !bounds.Overlaps(blocked[i]);
                    }

                    walkable[cell] = free;
                    walkableIndexOfCell[cell] = free ? count++ : -1;
                }
            }

            var cellOfWalkableIndex = new int[count];
            var centres = new Vec2[count];
            for (int cell = 0; cell < walkable.Length; cell++)
            {
                int index = walkableIndexOfCell[cell];
                if (index >= 0)
                {
                    cellOfWalkableIndex[index] = cell;
                    centres[index] = grid.CellCenter(cell % grid.CountX, cell / grid.CountX);
                }
            }

            return new WalkableGrid(grid, walkable, walkableIndexOfCell, cellOfWalkableIndex, centres);
        }

        /// <summary>True if the cell at these grid coordinates is walkable. Cells off the grid are not.</summary>
        public bool IsWalkable(int x, int z) =>
            x >= 0 && x < Grid.CountX && z >= 0 && z < Grid.CountZ && _walkable[z * Grid.CountX + x];

        /// <summary>
        /// The walkable index of a grid cell, or -1 if the cell is blocked or off the grid.
        /// </summary>
        public int IndexAt(int x, int z) =>
            x >= 0 && x < Grid.CountX && z >= 0 && z < Grid.CountZ
                ? _walkableIndexOfCell[z * Grid.CountX + x]
                : -1;

        /// <summary>The walkable index of the cell containing a point, or -1 if there is none.</summary>
        public int IndexAt(Vec2 point) => IndexAt(
            (int)MathF.Floor((point.X - Grid.Bounds.MinX) / Grid.CellSize),
            (int)MathF.Floor((point.Y - Grid.Bounds.MinZ) / Grid.CellSize));

        /// <summary>Grid X of a walkable cell.</summary>
        public int CellX(int walkableIndex) => _cellOfWalkableIndex[walkableIndex] % Grid.CountX;

        /// <summary>Grid Z of a walkable cell.</summary>
        public int CellZ(int walkableIndex) => _cellOfWalkableIndex[walkableIndex] / Grid.CountX;

        /// <summary>Centre of a walkable cell.</summary>
        public Vec2 CentreOf(int walkableIndex) => _centres[walkableIndex];

        /// <summary>
        /// Flood fills the walkable cells reachable on foot from anywhere inside
        /// <paramref name="from"/>, returning one flag per walkable index.
        /// </summary>
        /// <remarks>
        /// Four-connected. A diagonal step between two cells that share only a corner is not a
        /// route a player can take, and treating it as one would report a map as connected across
        /// a gap nobody can walk through.
        /// </remarks>
        public bool[] ReachableFrom(Rect2 from)
        {
            var reached = new bool[Count];
            var frontier = new Stack<int>();

            for (int index = 0; index < Count; index++)
            {
                if (!reached[index] && from.Contains(_centres[index]))
                {
                    reached[index] = true;
                    frontier.Push(index);
                }
            }

            while (frontier.Count > 0)
            {
                int index = frontier.Pop();
                int x = CellX(index);
                int z = CellZ(index);

                Visit(x - 1, z, reached, frontier);
                Visit(x + 1, z, reached, frontier);
                Visit(x, z - 1, reached, frontier);
                Visit(x, z + 1, reached, frontier);
            }

            return reached;
        }

        /// <summary>
        /// True if any walkable cell whose centre falls inside <paramref name="area"/> is flagged
        /// in <paramref name="reached"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="reached"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="reached"/> is not one flag per walkable cell.</exception>
        public bool AnyReached(Rect2 area, bool[] reached)
        {
            if (reached == null)
            {
                throw new ArgumentNullException(nameof(reached));
            }

            if (reached.Length != Count)
            {
                throw new ArgumentException(
                    $"Expected {Count} flags, one per walkable cell; got {reached.Length}.", nameof(reached));
            }

            for (int index = 0; index < Count; index++)
            {
                if (reached[index] && area.Contains(_centres[index]))
                {
                    return true;
                }
            }

            return false;
        }

        void Visit(int x, int z, bool[] reached, Stack<int> frontier)
        {
            int index = IndexAt(x, z);
            if (index >= 0 && !reached[index])
            {
                reached[index] = true;
                frontier.Push(index);
            }
        }
    }
}
