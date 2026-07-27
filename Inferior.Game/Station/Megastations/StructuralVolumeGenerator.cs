namespace Inferior.Game.StationGen.Megastations;

public interface IStructuralVolumeGenerator
{
    StructuralOccupancy Generate(SliceGrid grid);
}

public sealed class CuboidStructuralVolumeGenerator : IStructuralVolumeGenerator
{
    public StructuralOccupancy Generate(SliceGrid grid)
    {
        var occupancy = new StructuralOccupancy(grid);
        occupancy.FillCore();
        return occupancy;
    }
}
