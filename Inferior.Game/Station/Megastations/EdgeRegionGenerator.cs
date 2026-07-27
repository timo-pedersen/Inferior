namespace Inferior.Game.StationGen.Megastations;

public static class EdgeRegionGenerator
{
    public static IReadOnlyList<EdgeRegionPlan> PlanEdges(
        SliceGrid grid,
        MegastationPrototypeSettings settings,
        MegastationUrbanStyle style,
        IReadOnlyList<CornerRegionPlan> corners,
        int rootSeed)
    {
        var plans = new List<EdgeRegionPlan>(12);
        foreach (GridAxis lengthAxis in Enum.GetValues<GridAxis>())
        {
            GridAxis[] outsideAxes = Enum.GetValues<GridAxis>().Where(a => a != lengthAxis).ToArray();
            foreach (int signA in new[] { -1, 1 })
            foreach (int signB in new[] { -1, 1 })
            {
                GridDirection a = signA < 0 ? Direction.Negative(outsideAxes[0]) : Direction.Positive(outsideAxes[0]);
                GridDirection b = signB < 0 ? Direction.Negative(outsideAxes[1]) : Direction.Positive(outsideAxes[1]);
                plans.Add(PlanEdge(grid, style, corners, rootSeed, a, b, lengthAxis));
            }
        }
        return plans.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
    }

    public static void Apply(StructuralOccupancy occupancy, IEnumerable<EdgeRegionPlan> plans)
    {
        foreach (var p in plans)
        {
            Range core = occupancy.Grid.CoreRange(p.LengthAxis);
            for (int i = 0; i < p.DepthA.Length; i++)
            {
                int lengthIndex = core.Start.Value + i;
                for (int a = 1; a <= p.DepthA[i]; a++)
                for (int b = 1; b <= p.DepthB[i]; b++)
                {
                    int x = Coordinate(occupancy.Grid, p.LengthAxis, lengthIndex, p.A, a, p.B, b, GridAxis.X);
                    int y = Coordinate(occupancy.Grid, p.LengthAxis, lengthIndex, p.A, a, p.B, b, GridAxis.Y);
                    int z = Coordinate(occupancy.Grid, p.LengthAxis, lengthIndex, p.A, a, p.B, b, GridAxis.Z);
                    if (occupancy.Grid.IsEdgeRegion(x, y, z, p.A, p.B))
                    {
                        MarkSupportShoulders(occupancy, p, lengthIndex, a, b);
                        occupancy.MarkUrban(x, y, z, MegacellOwner.EdgeRegion, p.Id);
                    }
                }
            }
        }
    }

    private static void MarkSupportShoulders(
        StructuralOccupancy occupancy,
        EdgeRegionPlan plan,
        int lengthIndex,
        int aLayer,
        int bLayer)
    {
        string supportId = $"{plan.Id}.support";
        for (int a = 1; a <= aLayer; a++)
        {
            int x = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.A, a, plan.B, GridAxis.X);
            int y = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.A, a, plan.B, GridAxis.Y);
            int z = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.A, a, plan.B, GridAxis.Z);
            if (occupancy.Grid.IsFaceRegion(x, y, z, plan.A) && !occupancy.IsOccupied(x, y, z))
                occupancy.MarkUrban(x, y, z, MegacellOwner.FaceInterior, supportId);
        }

        for (int b = 1; b <= bLayer; b++)
        {
            int x = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.B, b, plan.A, GridAxis.X);
            int y = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.B, b, plan.A, GridAxis.Y);
            int z = CoordinateWithCoreBoundary(occupancy.Grid, plan.LengthAxis, lengthIndex, plan.B, b, plan.A, GridAxis.Z);
            if (occupancy.Grid.IsFaceRegion(x, y, z, plan.B) && !occupancy.IsOccupied(x, y, z))
                occupancy.MarkUrban(x, y, z, MegacellOwner.FaceInterior, supportId);
        }
    }

    private static EdgeRegionPlan PlanEdge(
        SliceGrid grid,
        MegastationUrbanStyle style,
        IReadOnlyList<CornerRegionPlan> corners,
        int rootSeed,
        GridDirection a,
        GridDirection b,
        GridAxis lengthAxis)
    {
        string id = RegionIdentity.Edge(a, b);
        var rng = new Random(MegastationSeed.Derive(rootSeed, $"edge-region:{id}"));
        Range core = grid.CoreRange(lengthAxis);
        int count = core.End.Value - core.Start.Value;
        var depthA = new int[count];
        var depthB = new int[count];
        GridDirection startDir = Direction.Negative(lengthAxis);
        GridDirection endDir = Direction.Positive(lengthAxis);
        var startCorner = CornerRegionGenerator.Find(corners, a, b, startDir);
        var endCorner = CornerRegionGenerator.Find(corners, a, b, endDir);

        int startA = CornerDepthFor(startCorner, a);
        int startB = CornerDepthFor(startCorner, b);
        int endA = CornerDepthFor(endCorner, a);
        int endB = CornerDepthFor(endCorner, b);

        float strength = style.EdgeSpineStrength * Next(rng, 0.55f, 1.35f);
        string profile = strength > 1.15f ? "strong spine"
            : strength < 0.78f ? "low structural band"
            : rng.NextDouble() < style.FragmentationTendency ? "broken spine"
            : rng.NextDouble() < 0.45 ? "irregular towers"
            : "mostly open edge";

        int maxA = Direction.AvailableLayers(grid, a);
        int maxB = Direction.AvailableLayers(grid, b);
        int segments = Math.Max(3, count / rng.Next(5, 9));
        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0f : i / (float)(count - 1);
            float endpointA = Lerp(startA, endA, t);
            float endpointB = Lerp(startB, endB, t);
            int segment = Math.Min(segments - 1, i * segments / Math.Max(1, count));
            var srng = new Random(MegastationSeed.Derive(rootSeed, $"edge-segment:{id}:{segment}"));
            float open = profile == "mostly open edge" && srng.NextDouble() < 0.45 ? 0.25f : 1f;
            float broken = profile == "broken spine" && srng.NextDouble() < 0.35 ? 0.35f : 1f;
            float tower = profile == "irregular towers" && srng.NextDouble() < 0.40 ? 1.35f : 1f;
            int baseA = Math.Clamp((int)MathF.Round((endpointA * 0.55f + maxA * 0.32f * strength) * open * broken * tower), 0, maxA);
            int baseB = Math.Clamp((int)MathF.Round((endpointB * 0.55f + maxB * 0.32f * strength) * open * broken * tower), 0, maxB);
            depthA[i] = Math.Max(Math.Min(startA, 1), baseA);
            depthB[i] = Math.Max(Math.Min(startB, 1), baseB);
        }

        depthA[0] = Math.Max(depthA[0], Math.Min(maxA, startA));
        depthB[0] = Math.Max(depthB[0], Math.Min(maxB, startB));
        depthA[^1] = Math.Max(depthA[^1], Math.Min(maxA, endA));
        depthB[^1] = Math.Max(depthB[^1], Math.Min(maxB, endB));
        Cleanup(depthA);
        Cleanup(depthB);

        return new EdgeRegionPlan(id, a, b, lengthAxis, startA, startB, endA, endB, depthA, depthB, profile);
    }

    private static int Coordinate(SliceGrid grid, GridAxis lengthAxis, int lengthIndex, GridDirection aDir, int aLayer, GridDirection bDir, int bLayer, GridAxis axis)
    {
        if (axis == lengthAxis) return lengthIndex;
        if (Direction.PrimaryAxis(aDir) == axis) return Direction.OutwardIndex(grid, aDir, aLayer);
        return Direction.OutwardIndex(grid, bDir, bLayer);
    }

    private static int CoordinateWithCoreBoundary(
        SliceGrid grid,
        GridAxis lengthAxis,
        int lengthIndex,
        GridDirection outwardDir,
        int outwardLayer,
        GridDirection coreBoundaryDir,
        GridAxis axis)
    {
        if (axis == lengthAxis) return lengthIndex;
        if (Direction.PrimaryAxis(outwardDir) == axis) return Direction.OutwardIndex(grid, outwardDir, outwardLayer);
        if (Direction.PrimaryAxis(coreBoundaryDir) == axis) return Direction.CoreBoundaryIndex(grid, coreBoundaryDir);
        throw new InvalidOperationException("Axis is not represented by this edge.");
    }

    private static int CornerDepthFor(CornerRegionPlan corner, GridDirection direction)
    {
        if (corner.A == direction) return corner.DepthA;
        if (corner.B == direction) return corner.DepthB;
        if (corner.C == direction) return corner.DepthC;
        return 0;
    }

    private static void Cleanup(int[] values)
    {
        if (values.Length < 3) return;
        var copy = values.ToArray();
        for (int i = 1; i < values.Length - 1; i++)
        {
            if (copy[i] > copy[i - 1] + 2 && copy[i] > copy[i + 1] + 2)
                values[i] = Math.Max(copy[i - 1], copy[i + 1]) + 1;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Next(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
}
