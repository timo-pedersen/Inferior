namespace Inferior.Game.StationGen.Megastations;

public enum GridAxis { X, Y, Z }
public enum GridDirection { NegativeX, PositiveX, NegativeY, PositiveY, NegativeZ, PositiveZ }

public sealed class SliceGrid
{
    private readonly float[][] _widths;
    private readonly float[][] _edges;

    public SliceGrid(float[] xWidths, float[] yWidths, float[] zWidths, Range coreX, Range coreY, Range coreZ)
    {
        _widths = [xWidths, yWidths, zWidths];
        _edges = [BuildEdges(xWidths), BuildEdges(yWidths), BuildEdges(zWidths)];
        CoreX = coreX;
        CoreY = coreY;
        CoreZ = coreZ;
    }

    public Range CoreX { get; }
    public Range CoreY { get; }
    public Range CoreZ { get; }
    public int XCount => _widths[0].Length;
    public int YCount => _widths[1].Length;
    public int ZCount => _widths[2].Length;
    public int CellCount => XCount * YCount * ZCount;

    public int Count(GridAxis axis) => _widths[(int)axis].Length;
    public float Dimension(GridAxis axis) => _edges[(int)axis][^1] - _edges[(int)axis][0];
    public float GetCellMinimum(GridAxis axis, int index) => _edges[(int)axis][index];
    public float GetCellMaximum(GridAxis axis, int index) => _edges[(int)axis][index + 1];
    public float GetCellCentre(GridAxis axis, int index) => (GetCellMinimum(axis, index) + GetCellMaximum(axis, index)) * 0.5f;
    public float GetCellSize(GridAxis axis, int index) => _widths[(int)axis][index];
    public Range CoreRange(GridAxis axis) => axis switch { GridAxis.X => CoreX, GridAxis.Y => CoreY, _ => CoreZ };
    public bool IsInCoreRange(GridAxis axis, int index)
    {
        Range range = CoreRange(axis);
        return index >= range.Start.Value && index < range.End.Value;
    }

    public int Index(int x, int y, int z) => (y * ZCount + z) * XCount + x;
    public bool Contains(int x, int y, int z) => (uint)x < XCount && (uint)y < YCount && (uint)z < ZCount;
    public IReadOnlyList<GridDirection> ExteriorDirections(int x, int y, int z)
    {
        var directions = new List<GridDirection>(3);
        AddAxisExterior(directions, GridAxis.X, x);
        AddAxisExterior(directions, GridAxis.Y, y);
        AddAxisExterior(directions, GridAxis.Z, z);
        return directions;
    }

    public int ExteriorAxisCount(int x, int y, int z) => ExteriorDirections(x, y, z).Count;

    public bool IsFaceRegion(int x, int y, int z, GridDirection direction)
    {
        var dirs = ExteriorDirections(x, y, z);
        return dirs.Count == 1 && dirs[0] == direction;
    }

    public bool IsEdgeRegion(int x, int y, int z, GridDirection a, GridDirection b)
    {
        var dirs = ExteriorDirections(x, y, z);
        return dirs.Count == 2 && dirs.Contains(a) && dirs.Contains(b);
    }

    public bool IsCornerRegion(int x, int y, int z, GridDirection a, GridDirection b, GridDirection c)
    {
        var dirs = ExteriorDirections(x, y, z);
        return dirs.Count == 3 && dirs.Contains(a) && dirs.Contains(b) && dirs.Contains(c);
    }

    private void AddAxisExterior(List<GridDirection> directions, GridAxis axis, int index)
    {
        Range range = CoreRange(axis);
        if (index < range.Start.Value)
            directions.Add(axis switch
            {
                GridAxis.X => GridDirection.NegativeX,
                GridAxis.Y => GridDirection.NegativeY,
                _          => GridDirection.NegativeZ,
            });
        else if (index >= range.End.Value)
            directions.Add(axis switch
            {
                GridAxis.X => GridDirection.PositiveX,
                GridAxis.Y => GridDirection.PositiveY,
                _          => GridDirection.PositiveZ,
            });
    }

    internal static SliceGrid Create(MegastationPrototypeSettings settings, int seed)
    {
        var rng = new Random(seed);
        int xCore = settings.CoreXSlices.Roll(rng);
        int yCore = settings.CoreYSlices.Roll(rng);
        int zCore = settings.CoreZSlices.Roll(rng);

        int xNeg = settings.NegativeGrowthLayers.Roll(rng);
        int yNeg = settings.NegativeGrowthLayers.Roll(rng);
        int zNeg = settings.NegativeGrowthLayers.Roll(rng);
        int xPos = settings.PositiveGrowthLayers.Roll(rng);
        int yPos = settings.PositiveGrowthLayers.Roll(rng);
        int zPos = settings.PositiveGrowthLayers.Roll(rng);

        float[] x = BuildAxis(settings.CoreDimensions.X, xCore, xNeg, xPos, settings.SliceJitter, rng);
        float[] y = BuildAxis(settings.CoreDimensions.Y, yCore, yNeg, yPos, settings.SliceJitter, rng);
        float[] z = BuildAxis(settings.CoreDimensions.Z, zCore, zNeg, zPos, settings.SliceJitter, rng);

        return new SliceGrid(x, y, z, xNeg..(xNeg + xCore), yNeg..(yNeg + yCore), zNeg..(zNeg + zCore));
    }

    private static float[] BuildAxis(float coreDimension, int coreCount, int negativeGrowth, int positiveGrowth, FloatRange jitter, Random rng)
    {
        float nominal = coreDimension / coreCount;
        var result = new float[negativeGrowth + coreCount + positiveGrowth];
        FillRange(result, 0, negativeGrowth, nominal, jitter, rng);
        FillRange(result, negativeGrowth, coreCount, nominal, jitter, rng);
        FillRange(result, negativeGrowth + coreCount, positiveGrowth, nominal, jitter, rng);
        Normalize(result, negativeGrowth, coreCount, coreDimension);
        return result;
    }

    private static void FillRange(float[] target, int start, int count, float nominal, FloatRange jitter, Random rng)
    {
        for (int i = 0; i < count; i++)
            target[start + i] = MathF.Max(1f, nominal * jitter.Roll(rng));
    }

    private static void Normalize(float[] target, int start, int count, float dimension)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += target[start + i];
        float scale = dimension / sum;
        float adjusted = 0f;
        for (int i = 0; i < count - 1; i++)
        {
            target[start + i] *= scale;
            adjusted += target[start + i];
        }
        target[start + count - 1] = dimension - adjusted;
    }

    private static float[] BuildEdges(float[] widths)
    {
        var edges = new float[widths.Length + 1];
        float origin = -widths.Sum() * 0.5f;
        edges[0] = origin;
        for (int i = 0; i < widths.Length; i++)
            edges[i + 1] = edges[i] + widths[i];
        return edges;
    }
}
