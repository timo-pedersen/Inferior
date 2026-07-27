using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationMeshStats(int ExposedQuadCount, int TriangleCount, int VertexCount, int MeshPageCount);

public static class MegastationPrototypeMeshBuilder
{
    private static readonly Color StructuralColor = new(86, 96, 104);
    private static readonly Color UrbanColor = new(128, 111, 90);

    public static MegastationMeshStats Build(StructuralOccupancy occupancy, StationModuleMesh mesh)
    {
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        int quads = 0;
        var grid = occupancy.Grid;

        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            if (!occupancy.IsOccupied(x, y, z)) continue;
            foreach (GridDirection direction in Enum.GetValues<GridDirection>())
            {
                if (!ExteriorSpace.IsFaceExposed(occupancy, x, y, z, direction)) continue;
                AddCellFace(grid, occupancy, mesh, x, y, z, direction);
                quads++;
            }
        }

        mesh.ApplyIlluminationFlags();
        return new MegastationMeshStats(quads, quads * 2, quads * 4, 1);
    }

    private static void AddCellFace(SliceGrid grid, StructuralOccupancy occupancy, StationModuleMesh mesh, int x, int y, int z, GridDirection d)
    {
        float x0 = grid.GetCellMinimum(GridAxis.X, x), x1 = grid.GetCellMaximum(GridAxis.X, x);
        float y0 = grid.GetCellMinimum(GridAxis.Y, y), y1 = grid.GetCellMaximum(GridAxis.Y, y);
        float z0 = grid.GetCellMinimum(GridAxis.Z, z), z1 = grid.GetCellMaximum(GridAxis.Z, z);
        Color color = occupancy.IsUrban(x, y, z) ? UrbanColor : StructuralColor;

        switch (d)
        {
            case GridDirection.PositiveX:
                mesh.AddQuad(new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), color);
                break;
            case GridDirection.NegativeX:
                mesh.AddQuad(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), color);
                break;
            case GridDirection.PositiveY:
                mesh.AddQuad(new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, y1, z0), color);
                break;
            case GridDirection.NegativeY:
                mesh.AddQuad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), color);
                break;
            case GridDirection.PositiveZ:
                mesh.AddQuad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), color);
                break;
            default:
                mesh.AddQuad(new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), color);
                break;
        }
    }
}
