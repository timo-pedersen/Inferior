using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record SurfaceCell(int X, int Y, int Z);

public sealed class SurfacePatch
{
    public required string Id { get; init; }
    public required GridDirection Direction { get; init; }
    public required int PlaneIndex { get; init; }
    public required IReadOnlyList<SurfaceCell> Cells { get; init; }
    public required GridAxis UAxis { get; init; }
    public required GridAxis VAxis { get; init; }
    public required int MinU { get; init; }
    public required int MaxU { get; init; }
    public required int MinV { get; init; }
    public required int MaxV { get; init; }

    public Vector3 Normal => Direction switch
    {
        GridDirection.NegativeX => -Vector3.UnitX,
        GridDirection.PositiveX => Vector3.UnitX,
        GridDirection.NegativeY => -Vector3.UnitY,
        GridDirection.PositiveY => Vector3.UnitY,
        GridDirection.NegativeZ => -Vector3.UnitZ,
        _                       => Vector3.UnitZ,
    };

    public int WidthCells => MaxU - MinU + 1;
    public int HeightCells => MaxV - MinV + 1;
}

public static class SurfacePatchFinder
{
    public static IReadOnlyList<SurfacePatch> FindPatches(StructuralOccupancy occupancy)
    {
        var patches = new List<SurfacePatch>();
        foreach (GridDirection direction in Enum.GetValues<GridDirection>())
            FindDirectionPatches(occupancy, direction, patches);
        patches.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
        return patches;
    }

    private static void FindDirectionPatches(StructuralOccupancy occupancy, GridDirection direction, List<SurfacePatch> patches)
    {
        var grid = occupancy.Grid;
        var seen = new bool[grid.CellCount];

        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            int startIndex = grid.Index(x, y, z);
            if (seen[startIndex] || !ExteriorSpace.IsFaceExposed(occupancy, x, y, z, direction))
                continue;

            var cells = new List<SurfaceCell>();
            var q = new Queue<(int x, int y, int z)>();
            q.Enqueue((x, y, z));
            seen[startIndex] = true;

            while (q.Count > 0)
            {
                var c = q.Dequeue();
                cells.Add(new SurfaceCell(c.x, c.y, c.z));
                foreach (var n in TangentNeighbours(direction))
                {
                    int nx = c.x + n.dx, ny = c.y + n.dy, nz = c.z + n.dz;
                    if (!grid.Contains(nx, ny, nz)) continue;
                    int ni = grid.Index(nx, ny, nz);
                    if (seen[ni] || !ExteriorSpace.IsFaceExposed(occupancy, nx, ny, nz, direction))
                        continue;
                    seen[ni] = true;
                    q.Enqueue((nx, ny, nz));
                }
            }

            patches.Add(BuildPatch(direction, cells));
        }
    }

    private static SurfacePatch BuildPatch(GridDirection direction, IReadOnlyList<SurfaceCell> cells)
    {
        (GridAxis uAxis, GridAxis vAxis) = direction switch
        {
            GridDirection.NegativeX or GridDirection.PositiveX => (GridAxis.Z, GridAxis.Y),
            GridDirection.NegativeY or GridDirection.PositiveY => (GridAxis.X, GridAxis.Z),
            _                                                  => (GridAxis.X, GridAxis.Y),
        };

        int plane = Coordinate(cells[0], Direction.PrimaryAxis(direction));
        int minU = cells.Min(c => Coordinate(c, uAxis));
        int maxU = cells.Max(c => Coordinate(c, uAxis));
        int minV = cells.Min(c => Coordinate(c, vAxis));
        int maxV = cells.Max(c => Coordinate(c, vAxis));
        string id = $"{direction}:p{plane}:u{minU}-{maxU}:v{minV}-{maxV}";

        return new SurfacePatch
        {
            Id = id,
            Direction = direction,
            PlaneIndex = plane,
            Cells = cells.OrderBy(c => Coordinate(c, uAxis)).ThenBy(c => Coordinate(c, vAxis)).ToArray(),
            UAxis = uAxis,
            VAxis = vAxis,
            MinU = minU,
            MaxU = maxU,
            MinV = minV,
            MaxV = maxV,
        };
    }

    internal static int Coordinate(SurfaceCell cell, GridAxis axis) => axis switch
    {
        GridAxis.X => cell.X,
        GridAxis.Y => cell.Y,
        _          => cell.Z,
    };

    private static IEnumerable<(int dx, int dy, int dz)> TangentNeighbours(GridDirection direction) => direction switch
    {
        GridDirection.NegativeX or GridDirection.PositiveX => [(0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)],
        GridDirection.NegativeY or GridDirection.PositiveY => [(-1, 0, 0), (1, 0, 0), (0, 0, -1), (0, 0, 1)],
        _                                                  => [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0)],
    };
}
