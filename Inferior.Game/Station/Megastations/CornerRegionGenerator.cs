namespace Inferior.Game.StationGen.Megastations;

public static class CornerRegionGenerator
{
    public static IReadOnlyList<CornerRegionPlan> PlanCorners(
        SliceGrid grid,
        MegastationPrototypeSettings settings,
        MegastationUrbanStyle style,
        int rootSeed)
    {
        var plans = new List<CornerRegionPlan>(8);
        foreach (GridDirection x in new[] { GridDirection.NegativeX, GridDirection.PositiveX })
        foreach (GridDirection y in new[] { GridDirection.NegativeY, GridDirection.PositiveY })
        foreach (GridDirection z in new[] { GridDirection.NegativeZ, GridDirection.PositiveZ })
        {
            string id = RegionIdentity.Corner(x, y, z);
            var rng = new Random(MegastationSeed.Derive(rootSeed, $"corner-region:{id}"));
            int maxX = Direction.AvailableLayers(grid, x);
            int maxY = Direction.AvailableLayers(grid, y);
            int maxZ = Direction.AvailableLayers(grid, z);
            float strength = style.CornerMassStrength * Next(rng, 0.65f, 1.25f);
            int dx = Depth(rng, maxX, strength);
            int dy = Depth(rng, maxY, strength);
            int dz = Depth(rng, maxZ, strength);
            string summary = strength > 1.2f ? "fortified" : strength < 0.85f ? "low" : rng.NextDouble() < 0.45 ? "stepped" : "tower";
            plans.Add(new CornerRegionPlan(id, x, y, z, dx, dy, dz, summary));
        }
        return plans.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
    }

    public static void Apply(StructuralOccupancy occupancy, IEnumerable<CornerRegionPlan> plans)
    {
        foreach (var p in plans)
        {
            for (int a = 1; a <= p.DepthA; a++)
            for (int b = 1; b <= p.DepthB; b++)
            for (int c = 1; c <= p.DepthC; c++)
            {
                if (a + b + c > p.DepthA + p.DepthB + p.DepthC - 1 && p.Summary != "fortified")
                    continue;
                int x = Coordinate(occupancy.Grid, p.A, a, p.B, b, p.C, c, GridAxis.X);
                int y = Coordinate(occupancy.Grid, p.A, a, p.B, b, p.C, c, GridAxis.Y);
                int z = Coordinate(occupancy.Grid, p.A, a, p.B, b, p.C, c, GridAxis.Z);
                if (occupancy.Grid.IsCornerRegion(x, y, z, p.A, p.B, p.C))
                    occupancy.MarkUrban(x, y, z, MegacellOwner.CornerRegion, p.Id);
            }
        }
    }

    public static CornerRegionPlan Find(
        IReadOnlyList<CornerRegionPlan> plans,
        GridDirection a,
        GridDirection b,
        GridDirection c)
    {
        string id = RegionIdentity.Corner(a, b, c);
        return plans.First(p => p.Id == id);
    }

    private static int Coordinate(SliceGrid grid, GridDirection aDir, int aLayer, GridDirection bDir, int bLayer, GridDirection cDir, int cLayer, GridAxis axis)
    {
        if (Direction.PrimaryAxis(aDir) == axis) return Direction.OutwardIndex(grid, aDir, aLayer);
        if (Direction.PrimaryAxis(bDir) == axis) return Direction.OutwardIndex(grid, bDir, bLayer);
        return Direction.OutwardIndex(grid, cDir, cLayer);
    }

    private static int Depth(Random rng, int available, float strength)
    {
        if (available <= 0) return 0;
        int min = Math.Min(available, Math.Max(1, (int)MathF.Round(available * 0.25f * strength)));
        int max = Math.Min(available, Math.Max(min, (int)MathF.Round(available * 0.80f * strength)));
        return rng.Next(min, max + 1);
    }

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
