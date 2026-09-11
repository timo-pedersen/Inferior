namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationConnectivityReport(
    int ConnectedComponentsBeforeValidation,
    int RemovedDisconnectedCells,
    bool HasSealedCavity);

public static class MegastationConnectivity
{
    private static readonly (int dx, int dy, int dz)[] Neighbours =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    public static MegastationConnectivityReport Validate(StructuralOccupancy occupancy)
    {
        int components = CountOccupiedComponents(occupancy);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        bool hasSealedCavity = false;
        var grid = occupancy.Grid;
        for (int x = 0; x < grid.XCount && !hasSealedCavity; x++)
        for (int y = 0; y < grid.YCount && !hasSealedCavity; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            if (!occupancy.IsOccupied(x, y, z) && !occupancy.IsExternallyAccessible(x, y, z))
            {
                hasSealedCavity = true;
                break;
            }
        }

        return new MegastationConnectivityReport(components, 0, hasSealedCavity);
    }

    private static int CountOccupiedComponents(StructuralOccupancy occupancy)
    {
        var grid = occupancy.Grid;
        var seen = new bool[grid.CellCount];
        int components = 0;

        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            int index = grid.Index(x, y, z);
            if (seen[index] || !occupancy.IsOccupied(x, y, z)) continue;
            components++;
            Flood(occupancy, x, y, z, seen);
        }

        return components;
    }

    private static void Flood(StructuralOccupancy occupancy, int sx, int sy, int sz, bool[] seen)
    {
        var grid = occupancy.Grid;
        var q = new Queue<(int x, int y, int z)>();
        q.Enqueue((sx, sy, sz));
        seen[grid.Index(sx, sy, sz)] = true;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var n in Neighbours)
            {
                int x = c.x + n.dx, y = c.y + n.dy, z = c.z + n.dz;
                if (!grid.Contains(x, y, z) || !occupancy.IsOccupied(x, y, z)) continue;
                int index = grid.Index(x, y, z);
                if (seen[index]) continue;
                seen[index] = true;
                q.Enqueue((x, y, z));
            }
        }
    }
}
