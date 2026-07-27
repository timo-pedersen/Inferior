namespace Inferior.Game.StationGen.Megastations;

public static class ExteriorSpace
{
    private static readonly (int dx, int dy, int dz)[] Neighbours =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    public static void ClassifyExternallyAccessibleEmpty(StructuralOccupancy occupancy)
    {
        occupancy.ClearExternalFlags();
        var grid = occupancy.Grid;
        var queue = new Queue<(int x, int y, int z)>();

        void EnqueueIfEmpty(int x, int y, int z)
        {
            if (!grid.Contains(x, y, z) || occupancy.IsOccupied(x, y, z) || occupancy.IsExternallyAccessible(x, y, z))
                return;
            occupancy.MarkExternallyAccessible(x, y, z);
            queue.Enqueue((x, y, z));
        }

        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        {
            EnqueueIfEmpty(x, y, 0);
            EnqueueIfEmpty(x, y, grid.ZCount - 1);
        }
        for (int x = 0; x < grid.XCount; x++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            EnqueueIfEmpty(x, 0, z);
            EnqueueIfEmpty(x, grid.YCount - 1, z);
        }
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            EnqueueIfEmpty(0, y, z);
            EnqueueIfEmpty(grid.XCount - 1, y, z);
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in Neighbours)
                EnqueueIfEmpty(c.x + n.dx, c.y + n.dy, c.z + n.dz);
        }
    }

    public static bool IsFaceExposed(StructuralOccupancy occupancy, int x, int y, int z, GridDirection direction)
    {
        var (dx, dy, dz) = Direction.Offset(direction);
        return occupancy.IsOccupied(x, y, z) && occupancy.IsExternallyAccessible(x + dx, y + dy, z + dz);
    }
}

public static class Direction
{
    public static (int dx, int dy, int dz) Offset(GridDirection direction) => direction switch
    {
        GridDirection.NegativeX => (-1, 0, 0),
        GridDirection.PositiveX => (1, 0, 0),
        GridDirection.NegativeY => (0, -1, 0),
        GridDirection.PositiveY => (0, 1, 0),
        GridDirection.NegativeZ => (0, 0, -1),
        _                       => (0, 0, 1),
    };

    public static GridAxis PrimaryAxis(GridDirection direction) => direction switch
    {
        GridDirection.NegativeX or GridDirection.PositiveX => GridAxis.X,
        GridDirection.NegativeY or GridDirection.PositiveY => GridAxis.Y,
        _                                                  => GridAxis.Z,
    };

    public static int Sign(GridDirection direction) => direction is GridDirection.PositiveX or GridDirection.PositiveY or GridDirection.PositiveZ ? 1 : -1;
}
