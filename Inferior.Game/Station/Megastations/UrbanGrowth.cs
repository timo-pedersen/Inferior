namespace Inferior.Game.StationGen.Megastations;

public sealed record UrbanDistrict(int Id, int MinU, int MaxU, int MinV, int MaxV, int BaseDepth, int MaxDepth);

public sealed class UrbanGrowthResult
{
    public required SurfacePatch Patch { get; init; }
    public required int[,] Depths { get; init; }
    public required IReadOnlyList<UrbanDistrict> Districts { get; init; }
    public int MaximumDepth => Depths.Cast<int>().DefaultIfEmpty(0).Max();
}

public static class UrbanGrowth
{
    public static UrbanGrowthResult Generate(
        StructuralOccupancy occupancy,
        SurfacePatch patch,
        MegastationPrototypeSettings settings,
        int seed)
    {
        int width = patch.WidthCells;
        int height = patch.HeightCells;
        var depths = new int[width, height];
        var districts = SplitDistricts(patch, settings, seed);

        foreach (var district in districts)
            FillDistrictDepth(depths, patch, district, settings, seed);

        ApplyCoherence(depths, settings.MaximumUrbanDepth);
        WriteOccupancy(occupancy, patch, depths);

        return new UrbanGrowthResult
        {
            Patch = patch,
            Depths = depths,
            Districts = districts,
        };
    }

    private static IReadOnlyList<UrbanDistrict> SplitDistricts(SurfacePatch patch, MegastationPrototypeSettings settings, int seed)
    {
        var rng = new Random(seed);
        int reserve = settings.ReservedPatchEdgeCells;
        if (patch.MinU + reserve > patch.MaxU - reserve || patch.MinV + reserve > patch.MaxV - reserve)
            return [];

        var rects = new List<(int minU, int maxU, int minV, int maxV)>
        {
            (patch.MinU + reserve, patch.MaxU - reserve, patch.MinV + reserve, patch.MaxV - reserve),
        };

        int target = settings.DistrictCount.Roll(rng);
        while (rects.Count < target)
        {
            int index = PickLargest(rects);
            var r = rects[index];
            int w = r.maxU - r.minU + 1;
            int h = r.maxV - r.minV + 1;
            bool splitU = w >= h;
            if ((splitU ? w : h) < settings.MinimumDistrictCells * 2 + 1)
                break;

            int minSplit = (splitU ? r.minU : r.minV) + settings.MinimumDistrictCells;
            int maxSplit = (splitU ? r.maxU : r.maxV) - settings.MinimumDistrictCells;
            if (minSplit > maxSplit) break;

            int split = rng.Next(minSplit, maxSplit + 1);
            rects.RemoveAt(index);
            if (splitU)
            {
                rects.Add((r.minU, split - 1, r.minV, r.maxV));
                rects.Add((split, r.maxU, r.minV, r.maxV));
            }
            else
            {
                rects.Add((r.minU, r.maxU, r.minV, split - 1));
                rects.Add((r.minU, r.maxU, split, r.maxV));
            }
        }

        return rects
            .OrderBy(r => r.minU).ThenBy(r => r.minV)
            .Select((r, i) =>
            {
                var drng = new Random(MegastationSeed.Derive(seed, $"district:{i}:{r.minU},{r.minV},{r.maxU},{r.maxV}"));
                int baseDepth = settings.BaseUrbanDepth.Roll(drng);
                int extra = drng.Next(2, Math.Max(3, settings.MaximumUrbanDepth - baseDepth + 1));
                return new UrbanDistrict(i, r.minU, r.maxU, r.minV, r.maxV, baseDepth, Math.Min(settings.MaximumUrbanDepth, baseDepth + extra));
            })
            .ToArray();
    }

    private static int PickLargest(List<(int minU, int maxU, int minV, int maxV)> rects)
    {
        int best = 0;
        int bestArea = -1;
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            int area = (r.maxU - r.minU + 1) * (r.maxV - r.minV + 1);
            if (area > bestArea)
            {
                best = i;
                bestArea = area;
            }
        }
        return best;
    }

    private static void FillDistrictDepth(
        int[,] depths,
        SurfacePatch patch,
        UrbanDistrict district,
        MegastationPrototypeSettings settings,
        int rootSeed)
    {
        var rng = new Random(MegastationSeed.Derive(rootSeed, $"tower-layout:{district.Id}"));
        for (int u = district.MinU; u <= district.MaxU; u++)
        for (int v = district.MinV; v <= district.MaxV; v++)
            depths[u - patch.MinU, v - patch.MinV] = district.BaseDepth;

        int towers = settings.TowerCountPerDistrict.Roll(rng);
        for (int i = 0; i < towers; i++)
        {
            if (district.MaxDepth <= district.BaseDepth + 1)
                break;

            int cu = rng.Next(district.MinU, district.MaxU + 1);
            int cv = rng.Next(district.MinV, district.MaxV + 1);
            int radius = settings.TowerRadiusCells.Roll(rng);
            int peak = rng.Next(district.BaseDepth + 2, district.MaxDepth + 1);

            for (int u = Math.Max(district.MinU, cu - radius); u <= Math.Min(district.MaxU, cu + radius); u++)
            for (int v = Math.Max(district.MinV, cv - radius); v <= Math.Min(district.MaxV, cv + radius); v++)
            {
                int d = Math.Abs(u - cu) + Math.Abs(v - cv);
                if (d > radius) continue;
                int height = Math.Max(district.BaseDepth, peak - d / 2);
                int x = u - patch.MinU, y = v - patch.MinV;
                depths[x, y] = Math.Min(settings.MaximumUrbanDepth, Math.Max(depths[x, y], height));
            }
        }

        var trenchRng = new Random(MegastationSeed.Derive(rootSeed, $"trench-courtyard:{district.Id}"));
        int area = (district.MaxU - district.MinU + 1) * (district.MaxV - district.MinV + 1);
        int trenchCount = Math.Max(1, (int)(area * settings.TrenchDensity.Roll(trenchRng) / 10f));
        for (int i = 0; i < trenchCount; i++)
            CarveTrench(depths, patch, district, trenchRng);

        int courtyardCount = Math.Max(1, (int)(area * settings.CourtyardDensity.Roll(trenchRng) / 30f));
        for (int i = 0; i < courtyardCount; i++)
            CarveCourtyard(depths, patch, district, trenchRng);
    }

    private static void CarveTrench(int[,] depths, SurfacePatch patch, UrbanDistrict district, Random rng)
    {
        int u = rng.Next(district.MinU, district.MaxU + 1);
        int v = rng.Next(district.MinV, district.MaxV + 1);
        bool alongU = rng.NextDouble() < 0.5;
        int length = rng.Next(4, Math.Max(5, alongU ? district.MaxU - district.MinU + 1 : district.MaxV - district.MinV + 1));
        int width = rng.Next(1, 3);
        for (int i = 0; i < length; i++)
        {
            for (int w = -width; w <= width; w++)
            {
                int cu = alongU ? u + i : u + w;
                int cv = alongU ? v + w : v + i;
                if (cu < district.MinU || cu > district.MaxU || cv < district.MinV || cv > district.MaxV) continue;
                depths[cu - patch.MinU, cv - patch.MinV] = Math.Max(0, depths[cu - patch.MinU, cv - patch.MinV] - rng.Next(2, 5));
            }
        }
    }

    private static void CarveCourtyard(int[,] depths, SurfacePatch patch, UrbanDistrict district, Random rng)
    {
        int w = rng.Next(2, Math.Max(3, (district.MaxU - district.MinU + 1) / 4));
        int h = rng.Next(2, Math.Max(3, (district.MaxV - district.MinV + 1) / 4));
        int minU = rng.Next(district.MinU, Math.Max(district.MinU + 1, district.MaxU - w + 2));
        int minV = rng.Next(district.MinV, Math.Max(district.MinV + 1, district.MaxV - h + 2));
        for (int u = minU; u <= Math.Min(district.MaxU, minU + w); u++)
        for (int v = minV; v <= Math.Min(district.MaxV, minV + h); v++)
            depths[u - patch.MinU, v - patch.MinV] = 0;
    }

    private static void ApplyCoherence(int[,] depths, int maxDepth)
    {
        int w = depths.GetLength(0);
        int h = depths.GetLength(1);
        var copy = (int[,])depths.Clone();
        for (int x = 1; x < w - 1; x++)
        for (int y = 1; y < h - 1; y++)
        {
            int c = copy[x, y];
            int n0 = copy[x - 1, y], n1 = copy[x + 1, y], n2 = copy[x, y - 1], n3 = copy[x, y + 1];
            int minN = Math.Min(Math.Min(n0, n1), Math.Min(n2, n3));
            int maxN = Math.Max(Math.Max(n0, n1), Math.Max(n2, n3));
            if (c > maxN + 3) depths[x, y] = maxN + 2;
            if (c == 0 && minN >= 3) depths[x, y] = Math.Min(maxDepth, minN - 1);
        }
    }

    private static void WriteOccupancy(StructuralOccupancy occupancy, SurfacePatch patch, int[,] depths)
    {
        var (dx, dy, dz) = Direction.Offset(patch.Direction);
        foreach (var cell in patch.Cells)
        {
            int u = SurfacePatchFinder.Coordinate(cell, patch.UAxis) - patch.MinU;
            int v = SurfacePatchFinder.Coordinate(cell, patch.VAxis) - patch.MinV;
            int depth = depths[u, v];
            for (int layer = 1; layer <= depth; layer++)
            {
                int x = cell.X + dx * layer;
                int y = cell.Y + dy * layer;
                int z = cell.Z + dz * layer;
                if (!occupancy.Grid.Contains(x, y, z))
                    break;
                if (!occupancy.Grid.IsFaceRegion(x, y, z, patch.Direction))
                    break;
                occupancy.MarkUrban(x, y, z, MegacellOwner.FaceInterior, RegionIdentity.Face(patch.Direction));
            }
        }
    }
}
