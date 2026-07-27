namespace Inferior.Game.StationGen.Megastations;

[Flags]
public enum MegacellFlags : byte
{
    None = 0,
    Structural = 1,
    Urban = 2,
    ExternallyAccessible = 4,
}

public sealed class StructuralOccupancy
{
    private readonly MegacellFlags[] _cells;

    public StructuralOccupancy(SliceGrid grid)
    {
        Grid = grid;
        _cells = new MegacellFlags[grid.CellCount];
    }

    public SliceGrid Grid { get; }
    public int StructuralOccupiedCount { get; private set; }
    public int UrbanOccupiedCount { get; private set; }
    public int TotalOccupiedCount => _cells.Count(c => (c & (MegacellFlags.Structural | MegacellFlags.Urban)) != 0);
    public int ExternallyAccessibleEmptyCount => _cells.Count(c => (c & MegacellFlags.ExternallyAccessible) != 0);

    public MegacellFlags this[int x, int y, int z]
    {
        get => _cells[Grid.Index(x, y, z)];
        private set => _cells[Grid.Index(x, y, z)] = value;
    }

    public bool IsOccupied(int x, int y, int z)
        => Grid.Contains(x, y, z) && (this[x, y, z] & (MegacellFlags.Structural | MegacellFlags.Urban)) != 0;

    public bool IsExternallyAccessible(int x, int y, int z)
        => !Grid.Contains(x, y, z) || (this[x, y, z] & MegacellFlags.ExternallyAccessible) != 0;

    public bool IsUrban(int x, int y, int z)
        => Grid.Contains(x, y, z) && (this[x, y, z] & MegacellFlags.Urban) != 0;

    public void FillCore()
    {
        for (int x = Grid.CoreX.Start.Value; x < Grid.CoreX.End.Value; x++)
        for (int y = Grid.CoreY.Start.Value; y < Grid.CoreY.End.Value; y++)
        for (int z = Grid.CoreZ.Start.Value; z < Grid.CoreZ.End.Value; z++)
            MarkStructural(x, y, z);
    }

    public void MarkStructural(int x, int y, int z)
    {
        var current = this[x, y, z];
        if ((current & MegacellFlags.Structural) == 0) StructuralOccupiedCount++;
        this[x, y, z] = current | MegacellFlags.Structural;
    }

    public void MarkUrban(int x, int y, int z)
    {
        var current = this[x, y, z];
        if ((current & (MegacellFlags.Structural | MegacellFlags.Urban)) == 0) UrbanOccupiedCount++;
        this[x, y, z] = current | MegacellFlags.Urban;
    }

    public void ClearExternalFlags()
    {
        for (int i = 0; i < _cells.Length; i++)
            _cells[i] &= ~MegacellFlags.ExternallyAccessible;
    }

    public void MarkExternallyAccessible(int x, int y, int z)
        => this[x, y, z] |= MegacellFlags.ExternallyAccessible;
}
