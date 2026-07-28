namespace Inferior.Game.StationGen.Megastations;

[Flags]
public enum MegacellFlags : byte
{
    None = 0,
    Structural = 1,
    Urban = 2,
    ExternallyAccessible = 4,
}

public enum MegacellOwner : byte
{
    None,
    StructuralCore,
    FaceInterior,
    EdgeRegion,
    CornerRegion,
    TopologyRegularisation,
}

public sealed class StructuralOccupancy
{
    private readonly MegacellFlags[] _cells;
    private readonly MegacellOwner[] _owners;
    private readonly string?[] _regionIds;

    public StructuralOccupancy(SliceGrid grid)
    {
        Grid = grid;
        _cells = new MegacellFlags[grid.CellCount];
        _owners = new MegacellOwner[grid.CellCount];
        _regionIds = new string?[grid.CellCount];
    }

    public SliceGrid Grid { get; }
    public int StructuralOccupiedCount { get; private set; }
    public int UrbanOccupiedCount { get; private set; }
    public int TotalOccupiedCount => _cells.Count(c => (c & (MegacellFlags.Structural | MegacellFlags.Urban)) != 0);
    public int ExternallyAccessibleEmptyCount => _cells.Count(c => (c & MegacellFlags.ExternallyAccessible) != 0);
    public int FaceRegionOccupiedCount => CountOwner(MegacellOwner.FaceInterior);
    public int EdgeRegionOccupiedCount => CountOwner(MegacellOwner.EdgeRegion);
    public int CornerRegionOccupiedCount => CountOwner(MegacellOwner.CornerRegion);
    public int TopologyRegularisationOccupiedCount => CountOwner(MegacellOwner.TopologyRegularisation);

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

    public MegacellOwner Owner(int x, int y, int z) => _owners[Grid.Index(x, y, z)];
    public string? RegionId(int x, int y, int z) => _regionIds[Grid.Index(x, y, z)];

    public StructuralOccupancy Clone()
    {
        var clone = new StructuralOccupancy(Grid)
        {
            StructuralOccupiedCount = StructuralOccupiedCount,
            UrbanOccupiedCount = UrbanOccupiedCount,
        };
        Array.Copy(_cells, clone._cells, _cells.Length);
        Array.Copy(_owners, clone._owners, _owners.Length);
        Array.Copy(_regionIds, clone._regionIds, _regionIds.Length);
        return clone;
    }

    public void FillCore()
    {
        for (int x = Grid.CoreX.Start.Value; x < Grid.CoreX.End.Value; x++)
        for (int y = Grid.CoreY.Start.Value; y < Grid.CoreY.End.Value; y++)
        for (int z = Grid.CoreZ.Start.Value; z < Grid.CoreZ.End.Value; z++)
            MarkStructural(x, y, z);
    }

    public void MarkStructural(int x, int y, int z)
    {
        int index = Grid.Index(x, y, z);
        var current = this[x, y, z];
        if ((current & MegacellFlags.Structural) == 0) StructuralOccupiedCount++;
        this[x, y, z] = current | MegacellFlags.Structural;
        _owners[index] = MegacellOwner.StructuralCore;
        _regionIds[index] = "core";
    }

    public void MarkUrban(int x, int y, int z, MegacellOwner owner = MegacellOwner.FaceInterior, string? regionId = null)
    {
        if (owner is MegacellOwner.None or MegacellOwner.StructuralCore)
            throw new ArgumentException("Urban occupancy requires a generated region owner.", nameof(owner));

        int index = Grid.Index(x, y, z);
        var current = this[x, y, z];
        if ((current & (MegacellFlags.Structural | MegacellFlags.Urban)) == 0) UrbanOccupiedCount++;
        else if ((current & MegacellFlags.Urban) != 0 && _owners[index] != owner)
            throw new InvalidOperationException(
                $"Megastation cell ({x},{y},{z}) already owned by {_owners[index]} / '{_regionIds[index]}', cannot claim as {owner} / '{regionId}'.");

        this[x, y, z] = current | MegacellFlags.Urban;
        _owners[index] = owner;
        _regionIds[index] = regionId;
    }

    public void MarkTopologyRegularisation(int x, int y, int z, string? regionId = null)
        => MarkUrban(x, y, z, MegacellOwner.TopologyRegularisation, regionId ?? "topology-regularisation");

    public void ClearExternalFlags()
    {
        for (int i = 0; i < _cells.Length; i++)
            _cells[i] &= ~MegacellFlags.ExternallyAccessible;
    }

    public void MarkExternallyAccessible(int x, int y, int z)
        => this[x, y, z] |= MegacellFlags.ExternallyAccessible;

    public int CountOwner(MegacellOwner owner) => _owners.Count(o => o == owner);
}
