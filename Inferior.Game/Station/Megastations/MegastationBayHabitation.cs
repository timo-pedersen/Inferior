using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationBayWallKind
{
    Left,
    Right,
    Rear,
    Entrance,
}

public enum MegastationBayWindowPattern
{
    ShortRow,
    DoubleRow,
    CompactCluster,
    ObservationStrip,
}

public sealed record MegastationBayWallSurface(
    string Identity,
    MegastationBayWallKind Kind,
    Vector3 Centre,
    Vector3 Normal,
    Vector3 Right,
    Vector3 Up,
    float Width,
    float Height,
    bool IsEligible);

public sealed record MegastationBayHabitationRegion(
    string Identity,
    string WallIdentity,
    Vector2 Centre,
    Vector2 Size,
    Color DominantColour,
    int GroupCount);

public sealed record MegastationBayHabitationWindow(
    string Identity,
    string WallIdentity,
    string RegionIdentity,
    MegastationBayWindowPattern Pattern,
    Vector3 Centre,
    Vector3 Normal,
    Vector3 Up,
    float Width,
    float Height,
    MegastationWindowState State,
    Color Colour,
    float Illumination);

public sealed record MegastationBayHabitationDiagnostics(
    int AlgorithmVersion,
    int WallCount,
    int ActiveWallCount,
    int BlankWallCount,
    int RegionCount,
    int WindowGroupCount,
    int WindowCount,
    int LitWindowCount,
    int DimWindowCount,
    int DarkWindowCount,
    long PlanningMilliseconds,
    int MeshVertexCount,
    int MeshTriangleCount,
    long MeshBytes,
    string WallSummary,
    string Signature);

public sealed record MegastationBayHabitationPlan(
    IReadOnlyList<MegastationBayWallSurface> Walls,
    IReadOnlyList<MegastationBayHabitationRegion> Regions,
    IReadOnlyList<MegastationBayHabitationWindow> Windows,
    MegastationBayHabitationDiagnostics Diagnostics);

public static class MegastationBayHabitationPlanner
{
    public const int AlgorithmVersion = 2;
    private const float MinimumFloorClearance = 32f;
    private const float CeilingClearance = 18f;
    private const float HorizontalMargin = 14f;
    private const float RegionSeparation = 16f;

    public static MegastationBayHabitationPlan Plan(
        MegastationInteriorPlan interior,
        MegastationLandingDistrictPlan landingDistrict,
        StructuralOccupancy? occupancy = null,
        BoundaryTopology? topology = null)
    {
        var stopwatch = Stopwatch.StartNew();
        int seed = MegastationSeed.Derive(interior.Seed, "bay-wall-habitation:v2");
        MegastationBayWallSurface[] walls = CreateWalls(interior);
        var regions = new List<MegastationBayHabitationRegion>();
        var windows = new List<MegastationBayHabitationWindow>();
        Dictionary<string, int> targets = walls.Where(wall => wall.IsEligible)
            .ToDictionary(wall => wall.Identity, wall => TargetRegionCount(wall, seed),
                StringComparer.Ordinal);

        foreach (MegastationBayWallSurface wall in walls)
        {
            if (!wall.IsEligible)
                continue;
            int wallSeed = MegastationSeed.Derive(seed, wall.Identity);
            int targetCount = targets[wall.Identity];
            for (int index = 0; index < targetCount; index++)
            {
                int regionSeed = MegastationSeed.Derive(wallSeed, $"region:{index}");
                if (!TryPlanRegion(wall, regionSeed, index,
                        regions.Where(region => region.WallIdentity == wall.Identity).ToArray(),
                        landingDistrict.ServiceBuildings,
                        interior.AddedStructuralSolids ?? [],
                        occupancy, topology,
                        out MegastationBayHabitationRegion? region)
                    || region is null)
                    continue;
                regions.Add(region);
                PlanWindows(wall, region, regionSeed, windows);
            }
        }

        float usableArea = walls.Where(wall => wall.IsEligible).Sum(UsableArea);
        int minimumRegions = MinimumRegionBudget(usableArea);
        if (regions.Count < minimumRegions)
        {
            MegastationBayWallSurface[] orderedWalls = walls.Where(item => item.IsEligible)
                .OrderBy(item => Unit(MegastationSeed.Derive(seed, item.Identity),
                    "minimum-priority"))
                .ToArray();
            for (int round = 0; round < 8 && regions.Count < minimumRegions; round++)
            foreach (MegastationBayWallSurface wall in orderedWalls)
            {
                int wallSeed = MegastationSeed.Derive(seed, wall.Identity);
                int index = targets[wall.Identity] + round;
                int regionSeed = MegastationSeed.Derive(wallSeed, $"region:{index}");
                if (!TryPlanRegion(wall, regionSeed, index,
                        regions.Where(region => region.WallIdentity == wall.Identity).ToArray(),
                        landingDistrict.ServiceBuildings,
                        interior.AddedStructuralSolids ?? [],
                        occupancy, topology,
                        out MegastationBayHabitationRegion? planned)
                    || planned is null)
                    continue;
                MegastationBayHabitationRegion region = planned with
                {
                    GroupCount = Math.Max(2, planned.GroupCount),
                };
                regions.Add(region);
                PlanWindows(wall, region, regionSeed, windows);
            }
        }

        stopwatch.Stop();
        return CreatePlan(walls, regions, windows, stopwatch.ElapsedMilliseconds);
    }

    public static MegastationBayHabitationPlan RetainRegions(
        MegastationBayHabitationPlan source,
        IReadOnlySet<string> retainedRegionIdentities,
        IReadOnlySet<string>? architecturallyOccupiedRegionIdentities = null)
    {
        MegastationBayHabitationRegion[] regions = source.Regions
            .Where(region => retainedRegionIdentities.Contains(region.Identity)).ToArray();
        MegastationBayHabitationWindow[] windows = source.Windows
            .Where(window => retainedRegionIdentities.Contains(window.RegionIdentity)
                && !(architecturallyOccupiedRegionIdentities?.Contains(window.RegionIdentity)
                    ?? false))
            .ToArray();
        return CreatePlan(source.Walls, regions, windows,
            source.Diagnostics.PlanningMilliseconds);
    }

    private static MegastationBayHabitationPlan CreatePlan(
        IReadOnlyList<MegastationBayWallSurface> walls,
        IReadOnlyList<MegastationBayHabitationRegion> regions,
        IReadOnlyList<MegastationBayHabitationWindow> windows,
        long planningMilliseconds)
    {
        string wallSummary = string.Join(",", walls.Select(wall =>
        {
            int regionCount = regions.Count(region => region.WallIdentity == wall.Identity);
            int groupCount = regions.Where(region => region.WallIdentity == wall.Identity)
                .Sum(region => region.GroupCount);
            return $"{wall.Kind}:{regionCount}r/{groupCount}g";
        }));
        string signature = Signature(walls, regions, windows);
        var diagnostics = new MegastationBayHabitationDiagnostics(
            AlgorithmVersion,
            walls.Count,
            regions.Select(region => region.WallIdentity).Distinct(StringComparer.Ordinal).Count(),
            walls.Count - regions.Select(region => region.WallIdentity)
                .Distinct(StringComparer.Ordinal).Count(),
            regions.Count,
            regions.Sum(region => region.GroupCount),
            windows.Count,
            windows.Count(window => window.State == MegastationWindowState.Lit),
            windows.Count(window => window.State == MegastationWindowState.Dim),
            windows.Count(window => window.State == MegastationWindowState.Dark),
            planningMilliseconds,
            0, 0, 0,
            wallSummary,
            signature);
        return new(walls, regions, windows, diagnostics);
    }

    private static int TargetRegionCount(MegastationBayWallSurface wall, int rootSeed)
    {
        int wallSeed = MegastationSeed.Derive(rootSeed, wall.Identity);
        if (Unit(wallSeed, "presence") >= .88f)
            return 0;
        int areaCount = (int)(UsableArea(wall) / 35_000f);
        int variation = Unit(wallSeed, "region-count") >= .5f ? 1 : 0;
        return Math.Clamp(2 + areaCount + variation, 2, 6);
    }

    private static float UsableArea(MegastationBayWallSurface wall)
        => MathF.Max(0f, wall.Width - HorizontalMargin * 2f)
            * MathF.Max(0f, wall.Height - MinimumFloorClearance - CeilingClearance);

    internal static int MinimumRegionBudget(float usableArea)
        => usableArea <= 0f
            ? 0
            : Math.Clamp((int)MathF.Ceiling(usableArea / 30_000f), 6, 12);

    internal static MegastationBayWallSurface[] CreateWalls(MegastationInteriorPlan interior)
    {
        Vector3 right = Vector3.Normalize(interior.PortalRight);
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 inward = Vector3.Normalize(-interior.OutwardNormal);
        (float rMin, float rMax) = Span(interior.CavityEnvelope, right);
        (float uMin, float uMax) = Span(interior.CavityEnvelope, up);
        (float dMin, float dMax) = Span(interior.CavityEnvelope, inward);
        float rMid = (rMin + rMax) * .5f;
        float uMid = (uMin + uMax) * .5f;
        float dMid = (dMin + dMax) * .5f;

        MegastationBayWallSurface Wall(
            MegastationBayWallKind kind,
            Vector3 normal,
            Vector3 centre,
            float width,
            bool eligible)
        {
            Vector3 wallRight = Vector3.Normalize(Vector3.Cross(up, normal));
            return new($"bay-wall:{kind.ToString().ToLowerInvariant()}", kind,
                centre, normal, wallRight, up, width, uMax - uMin, eligible);
        }

        return
        [
            Wall(MegastationBayWallKind.Left, right,
                right * rMin + inward * dMid + up * uMid, dMax - dMin, true),
            Wall(MegastationBayWallKind.Right, -right,
                right * rMax + inward * dMid + up * uMid, dMax - dMin, true),
            Wall(MegastationBayWallKind.Rear, -inward,
                right * rMid + inward * dMax + up * uMid, rMax - rMin, true),
            // The entrance wall is deliberately represented but blank in L3a. Its exact
            // portal/throat subtraction is authoritative and not approximated with windows.
            Wall(MegastationBayWallKind.Entrance, inward,
                right * rMid + inward * dMin + up * uMid, rMax - rMin, false),
        ];
    }

    private static bool TryPlanRegion(
        MegastationBayWallSurface wall,
        int seed,
        int index,
        IReadOnlyList<MegastationBayHabitationRegion> accepted,
        IReadOnlyList<MegastationLandingServiceBuilding> buildings,
        IReadOnlyList<MegastationInteriorStructuralSolid> structuralSolids,
        StructuralOccupancy? occupancy,
        BoundaryTopology? topology,
        out MegastationBayHabitationRegion? region)
    {
        float minX = -wall.Width * .5f + HorizontalMargin;
        float maxX = wall.Width * .5f - HorizontalMargin;
        float minY = -wall.Height * .5f + MinimumFloorClearance;
        float maxY = wall.Height * .5f - CeilingClearance;
        float availableWidth = maxX - minX;
        float availableHeight = maxY - minY;
        if (availableWidth < 34f || availableHeight < 10f)
        {
            region = null;
            return false;
        }

        for (int attempt = 0; attempt < 24; attempt++)
        {
            int candidateSeed = MegastationSeed.Derive(seed, $"candidate:{attempt}");
            float width = MathF.Min(availableWidth,
                30f + Unit(candidateSeed, "width") * 42f);
            float height = MathF.Min(availableHeight,
                8f + Unit(candidateSeed, "height") * 12f);
            float x = Lerp(minX + width * .5f, maxX - width * .5f,
                Unit(candidateSeed, "x"));
            float y = Lerp(minY + height * .5f, maxY - height * .5f,
                Unit(candidateSeed, "y"));
            if (accepted.Any(other => Overlaps(
                    new(x, y), new(width, height), other.Centre, other.Size,
                    RegionSeparation)))
                continue;
            if (buildings.Any(building => OverlapsBuilding(
                    wall, new(x, y), new(width, height), building)))
                continue;
            if (structuralSolids.Any(solid => OverlapsStructuralRoot(
                    wall, new(x, y), new(width, height), solid)))
                continue;
            if (occupancy is not null && topology is not null
                && !TryFindSupportingFace(wall, new(x, y), new(width, height),
                    1.5f, occupancy.Grid, topology, out _))
                continue;

            int groups = 1 + (Unit(candidateSeed, "groups") > .42f ? 1 : 0)
                + (Unit(candidateSeed, "third-group") > .86f ? 1 : 0);
            float colourRoll = Unit(candidateSeed, "colour");
            Color dominant = colourRoll < .72f ? StationWindowVisuals.WarmWhite
                : colourRoll < .92f ? StationWindowVisuals.NeutralWhite
                : StationWindowVisuals.CoolBlue;
            region = new(
                $"{wall.Identity}/habitation:{index}", wall.Identity,
                new(x, y), new(width, height), dominant, groups);
            return true;
        }

        region = null;
        return false;
    }

    internal static bool TryFindSupportingFace(
        MegastationBayWallSurface wall,
        Vector2 centre,
        Vector2 size,
        float margin,
        SliceGrid grid,
        BoundaryTopology topology,
        out BoundaryFace? supportingFace)
    {
        supportingFace = topology.Faces
            .Where(face => face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction), wall.Normal) > .9999f)
            .Where(face =>
            {
                Vector3[] points = face.Vertices
                    .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray();
                if (MathF.Abs(Vector3.Dot(points[0] - wall.Centre, wall.Normal)) >= .01f)
                    return false;
                float minX = points.Min(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float maxX = points.Max(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float minY = points.Min(point => Vector3.Dot(point - wall.Centre, wall.Up));
                float maxY = points.Max(point => Vector3.Dot(point - wall.Centre, wall.Up));
                return centre.X - size.X * .5f - margin >= minX
                    && centre.X + size.X * .5f + margin <= maxX
                    && centre.Y - size.Y * .5f - margin >= minY
                    && centre.Y + size.Y * .5f + margin <= maxY;
            })
            .OrderBy(face => face.Key)
            .FirstOrDefault();
        return supportingFace is not null;
    }

    private static bool OverlapsStructuralRoot(
        MegastationBayWallSurface wall,
        Vector2 centre,
        Vector2 size,
        MegastationInteriorStructuralSolid solid)
    {
        Vector3 half = solid.Right * (solid.Size.X * .5f);
        Vector3 halfUp = solid.Up * (solid.Size.Y * .5f);
        Vector3 halfForward = solid.Forward * (solid.Size.Z * .5f);
        Vector3[] corners =
        [
            solid.Centre - half - halfUp - halfForward,
            solid.Centre - half - halfUp + halfForward,
            solid.Centre - half + halfUp - halfForward,
            solid.Centre - half + halfUp + halfForward,
            solid.Centre + half - halfUp - halfForward,
            solid.Centre + half - halfUp + halfForward,
            solid.Centre + half + halfUp - halfForward,
            solid.Centre + half + halfUp + halfForward,
        ];
        float normalMin = corners.Min(point => Vector3.Dot(point - wall.Centre, wall.Normal));
        float normalMax = corners.Max(point => Vector3.Dot(point - wall.Centre, wall.Normal));
        if (normalMin > .01f || normalMax < -.01f)
            return false;
        float minX = corners.Min(point => Vector3.Dot(point - wall.Centre, wall.Right));
        float maxX = corners.Max(point => Vector3.Dot(point - wall.Centre, wall.Right));
        float minY = corners.Min(point => Vector3.Dot(point - wall.Centre, wall.Up));
        float maxY = corners.Max(point => Vector3.Dot(point - wall.Centre, wall.Up));
        return centre.X - size.X * .5f - RegionSeparation < maxX
            && centre.X + size.X * .5f + RegionSeparation > minX
            && centre.Y - size.Y * .5f - RegionSeparation < maxY
            && centre.Y + size.Y * .5f + RegionSeparation > minY;
    }

    private static void PlanWindows(
        MegastationBayWallSurface wall,
        MegastationBayHabitationRegion region,
        int seed,
        List<MegastationBayHabitationWindow> windows)
    {
        for (int group = 0; group < region.GroupCount; group++)
        {
            int groupSeed = MegastationSeed.Derive(seed, $"group:{group}");
            MegastationBayWindowPattern pattern = (MegastationBayWindowPattern)
                Math.Min(3, (int)(Unit(groupSeed, "pattern") * 4f));
            (int rows, int columns) = pattern switch
            {
                MegastationBayWindowPattern.ShortRow => (1, 3 + (int)(Unit(groupSeed, "columns") * 4f)),
                MegastationBayWindowPattern.DoubleRow => (2, 4 + (int)(Unit(groupSeed, "columns") * 4f)),
                MegastationBayWindowPattern.CompactCluster => (2, 3 + (int)(Unit(groupSeed, "columns") * 3f)),
                _ => (1, 7 + (int)(Unit(groupSeed, "columns") * 5f)),
            };
            float windowWidth = pattern == MegastationBayWindowPattern.ObservationStrip
                ? 1.55f : 1.18f + Unit(groupSeed, "width") * .42f;
            float windowHeight = pattern == MegastationBayWindowPattern.ObservationStrip
                ? 1.12f : .96f + Unit(groupSeed, "height") * .34f;
            float spacingX = windowWidth + .72f + Unit(groupSeed, "spacing") * .55f;
            float spacingY = windowHeight + .9f;
            float slotWidth = region.Size.X / region.GroupCount;
            float groupWidth = (columns - 1) * spacingX + windowWidth;
            if (groupWidth > slotWidth - 2f)
            {
                columns = Math.Max(2, (int)((slotWidth - 2f - windowWidth) / spacingX) + 1);
                groupWidth = (columns - 1) * spacingX + windowWidth;
            }
            float slotCentre = region.Centre.X - region.Size.X * .5f
                + slotWidth * (group + .5f);
            float maximumJitter = MathF.Max(0f, (slotWidth - groupWidth) * .35f);
            float groupX = slotCentre + (Unit(groupSeed, "x-jitter") * 2f - 1f) * maximumJitter;
            float verticalRange = MathF.Max(0f,
                region.Size.Y - rows * windowHeight - (rows - 1) * .9f - 2f);
            float groupY = region.Centre.Y
                + (Unit(groupSeed, "vertical") * 2f - 1f) * verticalRange * .5f;

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int windowSeed = MegastationSeed.Derive(groupSeed, $"window:{row}:{column}");
                float stateRoll = Unit(windowSeed, "state");
                MegastationWindowState state = stateRoll < .17f
                    ? MegastationWindowState.Dark
                    : stateRoll < .37f ? MegastationWindowState.Dim : MegastationWindowState.Lit;
                float colourRoll = Unit(windowSeed, "colour");
                Color colour = colourRoll < .82f ? region.DominantColour
                    : colourRoll < .93f ? StationWindowVisuals.WarmWhite
                    : StationWindowVisuals.NeutralWhite;
                float illumination = state switch
                {
                    MegastationWindowState.Lit => .58f,
                    MegastationWindowState.Dim => .22f,
                    _ => .015f,
                };
                if (state == MegastationWindowState.Dark)
                    colour = new Color(18, 20, 21);
                else if (state == MegastationWindowState.Dim)
                    colour = Color.Lerp(colour, Color.Black, .42f);

                float x = groupX + (column - (columns - 1) * .5f) * spacingX;
                float y = groupY + (row - (rows - 1) * .5f) * spacingY;
                Vector3 centre = wall.Centre + wall.Right * x + wall.Up * y
                    + wall.Normal * .045f;
                windows.Add(new(
                    $"{region.Identity}/group:{group}/window:{row}:{column}",
                    wall.Identity, region.Identity, pattern,
                    centre, wall.Normal, wall.Up,
                    windowWidth, windowHeight, state, colour, illumination));
            }
        }
    }

    private static bool Overlaps(
        Vector2 centreA, Vector2 sizeA,
        Vector2 centreB, Vector2 sizeB,
        float margin)
        => MathF.Abs(centreA.X - centreB.X) * 2f < sizeA.X + sizeB.X + margin * 2f
            && MathF.Abs(centreA.Y - centreB.Y) * 2f < sizeA.Y + sizeB.Y + margin * 2f;

    private static bool OverlapsBuilding(
        MegastationBayWallSurface wall,
        Vector2 regionCentre,
        Vector2 regionSize,
        MegastationLandingServiceBuilding building)
    {
        Vector3 buildingRight = Vector3.Normalize(building.Frontage.Right);
        Vector3 buildingUp = Vector3.Normalize(building.Frontage.Up);
        Vector3 buildingForward = Vector3.Normalize(building.Frontage.Normal);
        float Radius(Vector3 axis) =>
            MathF.Abs(Vector3.Dot(buildingRight, axis)) * building.Size.X * .5f
            + MathF.Abs(Vector3.Dot(buildingUp, axis)) * building.Size.Y * .5f
            + MathF.Abs(Vector3.Dot(buildingForward, axis)) * building.Size.Z * .5f;
        float planeDistance = MathF.Abs(Vector3.Dot(
            building.Centre - wall.Centre, wall.Normal));
        if (planeDistance > Radius(wall.Normal) + 2f)
            return false;

        Vector2 projectedCentre = new(
            Vector3.Dot(building.Centre - wall.Centre, wall.Right),
            Vector3.Dot(building.Centre - wall.Centre, wall.Up));
        Vector2 projectedSize = new(Radius(wall.Right) * 2f, Radius(wall.Up) * 2f);
        return Overlaps(regionCentre, regionSize, projectedCentre, projectedSize, 4f);
    }

    private static (float Min, float Max) Span(MegastationInteriorVolume volume, Vector3 axis)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new(
                x == 0 ? volume.Minimum.X : volume.Maximum.X,
                y == 0 ? volume.Minimum.Y : volume.Maximum.Y,
                z == 0 ? volume.Minimum.Z : volume.Maximum.Z);
            float projection = Vector3.Dot(corner, axis);
            min = MathF.Min(min, projection);
            max = MathF.Max(max, projection);
        }
        return (min, max);
    }

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static float Lerp(float a, float b, float amount)
        => a + (b - a) * amount;

    private static string Signature(
        IReadOnlyList<MegastationBayWallSurface> walls,
        IReadOnlyList<MegastationBayHabitationRegion> regions,
        IReadOnlyList<MegastationBayHabitationWindow> windows)
    {
        var text = new StringBuilder();
        foreach (MegastationBayWallSurface wall in walls)
            text.Append(wall.Identity).Append(':').Append(wall.Centre).Append(':')
                .Append(wall.Normal).Append(':').Append(wall.Right).Append('|');
        foreach (MegastationBayHabitationRegion region in regions)
            text.Append(region.Identity).Append(':').Append(region.Centre).Append(':')
                .Append(region.Size).Append(':').Append(region.GroupCount).Append('|');
        foreach (MegastationBayHabitationWindow window in windows)
            text.Append(window.Identity).Append(':').Append(window.Centre).Append(':')
                .Append(window.State).Append(':')
                .Append(window.Illumination.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed record MegastationBayHabitationMeshResult(
    MegastationBayHabitationDiagnostics Diagnostics,
    IReadOnlyList<(int Face, float Illumination)> IlluminationFaces);

public static class MegastationBayHabitationMeshBuilder
{
    public static MegastationBayHabitationMeshResult Append(
        StationModuleMesh mesh,
        MegastationBayHabitationPlan plan,
        MegastationBayFacilityPlan? facilities = null,
        CancellationToken cancellationToken = default)
    {
        int firstVertex = mesh.VertexCount;
        int firstIndex = mesh.IndexCount;
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        mesh.CurrentMaterialFamily = SystemMaterialFamilyId.CleanTechnicalAlloy;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(
            SystemMaterialFamilyId.CleanTechnicalAlloy).TileSizeMeters;
        var illumination = new List<(int Face, float Value)>(plan.Windows.Count);

        HashSet<string> enhancedRegions = facilities?.Facilities
            .Select(facility => facility.RegionIdentity)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (MegastationBayHabitationWindow window in plan.Windows.Where(window =>
                     !enhancedRegions.Contains(window.RegionIdentity)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int face = mesh.FaceCount;
            MegastationWindowMeshBuilder.AppendWindow(
                mesh, window.Centre, window.Normal, window.Up,
                window.Width, window.Height, window.Colour);
            illumination.Add((face, window.Illumination));
        }

        int vertices = mesh.VertexCount - firstVertex;
        int triangles = (mesh.IndexCount - firstIndex) / 3;
        MegastationBayHabitationDiagnostics diagnostics = plan.Diagnostics with
        {
            MeshVertexCount = vertices,
            MeshTriangleCount = triangles,
            MeshBytes = (long)vertices * 36L + (long)triangles * 3L * sizeof(int),
        };
        return new(diagnostics, illumination);
    }
}
