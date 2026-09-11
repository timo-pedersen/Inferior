using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationBayUtilityRunLevel { Trunk, Branch }
public enum MegastationBayUtilityNodeKind { JunctionBox, ServiceBox }

public sealed record MegastationBayCableBundle(
    string Identity, string NetworkIdentity, string WallIdentity,
    MegastationBayUtilityRunLevel Level, Vector2 Start, Vector2 End,
    int CableCount, float CableRadius, float BundleSpacing, float SurfaceOffset,
    float ClampSpacing, string? StructuralSupportIdentity = null)
{
    public float Length => Vector2.Distance(Start, End);
    public bool IsHorizontal => MathF.Abs(End.X - Start.X) >= MathF.Abs(End.Y - Start.Y);
}

public sealed record MegastationBayUtilityNode(
    string Identity, string NetworkIdentity, string WallIdentity,
    MegastationBayUtilityNodeKind Kind, Vector2 Position, Vector2 Size,
    float Depth, float SurfaceOffset, IReadOnlyList<string> IncidentBundleIdentities);

public sealed record MegastationBayUtilityNetwork(
    string Identity, string WallIdentity, int Seed,
    IReadOnlyList<MegastationBayCableBundle> Bundles,
    IReadOnlyList<MegastationBayUtilityNode> Nodes,
    int ColourVariant);

public sealed record MegastationBayUtilityDiagnostics(
    int AlgorithmVersion, int EligibleWallCount, int ActiveWallCount,
    int NetworkCount, int TrunkCount, int BranchCount,
    int JunctionBoxCount, int ServiceBoxCount, int CableCount,
    int ClampCount, float TotalRunLength, float MinimumTrunkLength,
    float MedianTrunkLength, float MaximumTrunkLength,
    int HardConflictRejectCount, int WindowConflictScore,
    long PlanningMilliseconds, int VisibleVertexCount, int VisibleTriangleCount,
    int MajorCasterTriangleCount, string WallSummary, string Signature);

public sealed record MegastationBayUtilityPlan(
    int AlgorithmVersion, int Seed,
    IReadOnlyList<MegastationBayUtilityNetwork> Networks,
    MegastationBayUtilityDiagnostics Diagnostics)
{
    public IReadOnlyList<MegastationBayCableBundle> Bundles
        => Networks.SelectMany(network => network.Bundles).ToArray();
    public IReadOnlyList<MegastationBayUtilityNode> Nodes
        => Networks.SelectMany(network => network.Nodes).ToArray();
}

public sealed record MegastationBayUtilityMeshResult(
    IReadOnlyList<int> ReceiverFaces,
    MegastationBayUtilityDiagnostics Diagnostics);

public static class MegastationBayUtilityPlanner
{
    public const int AlgorithmVersion = 1;

    public static MegastationBayUtilityPlan Plan(
        MegastationInteriorPlan interior,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationLandingDistrictPlan landing,
        MegastationBayFacilityPlan facilities,
        MegastationBayStructuralTrussPlan structuralTrusses)
    {
        var stopwatch = Stopwatch.StartNew();
        int seed = MegastationSeed.Derive(interior.Seed, "bay-wall-utilities:v1");
        MegastationBayWallSurface[] walls = MegastationBayHabitationPlanner.CreateWalls(interior)
            .Where(wall => wall.Kind != MegastationBayWallKind.Entrance).ToArray();
        var networks = new List<MegastationBayUtilityNetwork>();
        int hardRejects = 0;
        int windowScore = 0;

        foreach (MegastationBayWallSurface wall in walls)
        {
            int wallSeed = MegastationSeed.Derive(seed, $"network:{wall.Identity}");
            float areaFactor = MathHelper.Clamp(wall.Width * wall.Height / 180_000f, 0f, 1f);
            if (Unit(wallSeed, "presence") > .48f + areaFactor * .30f)
                continue;

            int desiredTrunks = wall.Width >= 500f && Unit(wallSeed, "second-trunk") < .56f
                ? 2 : 1;
            var bundles = new List<MegastationBayCableBundle>();
            var nodes = new List<MegastationBayUtilityNode>();
            for (int trunkIndex = 0; trunkIndex < desiredTrunks; trunkIndex++)
            {
                int trunkSeed = MegastationSeed.Derive(wallSeed, $"trunk:{trunkIndex}");
                TrunkCandidate? trunk = FindTrunk(wall, trunkIndex, desiredTrunks, trunkSeed,
                    occupancy.Grid, topology, landing, facilities, ref hardRejects);
                if (trunk is null)
                    continue;
                windowScore += trunk.WindowScore;
                string networkIdentity = $"interior/bay-utilities:v1/{wall.Identity}/network:{trunkIndex}";
                string trunkIdentity = $"{networkIdentity}/trunk:0";
                int cableCount = 5 + (int)((uint)MegastationSeed.Derive(trunkSeed, "cables") % 4u);
                float radius = MathHelper.Lerp(.095f, .17f, Unit(trunkSeed, "radius"));
                float spacing = radius * MathHelper.Lerp(2.45f, 3.15f, Unit(trunkSeed, "spacing"));
                float surfaceOffset = .42f + radius;
                bundles.Add(new(trunkIdentity, networkIdentity, wall.Identity,
                    MegastationBayUtilityRunLevel.Trunk,
                    new(trunk.MinimumX, trunk.Y), new(trunk.MaximumX, trunk.Y),
                    cableCount, radius, spacing, surfaceOffset,
                    MathHelper.Lerp(7.6f, 10.8f, Unit(trunkSeed, "clamps"))));

                int desiredBranches = Math.Clamp((int)(trunk.Length / 105f), 2, 4);
                for (int branchIndex = 0; branchIndex < desiredBranches; branchIndex++)
                {
                    int branchSeed = MegastationSeed.Derive(trunkSeed, $"branch:{branchIndex}");
                    float x = MathHelper.Lerp(trunk.MinimumX, trunk.MaximumX,
                        (branchIndex + 1f) / (desiredBranches + 1f));
                    MegastationBayStructuralTruss? nearbyTruss = structuralTrusses.Trusses
                        .Where(item => item.HostKind == MegastationBayStructuralTrussHostKind.Wall
                            && item.HostIdentity == wall.Identity)
                        .OrderBy(item => MathF.Abs(Vector3.Dot(
                            item.Spec.Transform.Translation - wall.Centre, wall.Right) - x))
                        .FirstOrDefault();
                    bool useTruss = nearbyTruss is not null
                        && Unit(branchSeed, "truss-supported") < .32f;
                    if (useTruss)
                        x = Vector3.Dot(nearbyTruss!.Spec.Transform.Translation - wall.Centre,
                            wall.Right);
                    BranchCandidate? branch = FindBranch(wall, x, trunk.Y, branchSeed,
                        occupancy.Grid, topology, landing, facilities, ref hardRejects);
                    if (branch is null)
                        continue;
                    windowScore += branch.WindowScore;
                    string branchIdentity = $"{networkIdentity}/branch:{branchIndex}";
                    int branchCableCount = 2 + (int)((uint)MegastationSeed.Derive(
                        branchSeed, "cables") % 3u);
                    float branchRadius = MathHelper.Lerp(.065f, .115f, Unit(branchSeed, "radius"));
                    float supportSurface = useTruss ? nearbyTruss!.Spec.Height + .35f : .08f;
                    float branchOffset = supportSurface + .32f + branchRadius;
                    bundles.Add(new(branchIdentity, networkIdentity, wall.Identity,
                        MegastationBayUtilityRunLevel.Branch,
                        new(branch.X, branch.StartY), new(branch.X, branch.EndY),
                        branchCableCount, branchRadius, branchRadius * 2.75f, branchOffset,
                        MathHelper.Lerp(5.6f, 8.4f, Unit(branchSeed, "clamps")),
                        useTruss ? nearbyTruss!.Identity : null));
                    string junctionIdentity = $"{networkIdentity}/junction:{branchIndex}";
                    nodes.Add(new(junctionIdentity, networkIdentity, wall.Identity,
                        MegastationBayUtilityNodeKind.JunctionBox,
                        new(branch.X, trunk.Y), new(3.2f, 3.8f),
                        useTruss ? supportSurface + 1.10f : 1.15f,
                        .08f, [trunkIdentity, branchIdentity]));
                    nodes.Add(new($"{networkIdentity}/service:{branchIndex}", networkIdentity,
                        wall.Identity, MegastationBayUtilityNodeKind.ServiceBox,
                        new(branch.X, branch.EndY), new(2.0f, 2.5f), .72f, supportSurface,
                        [branchIdentity]));
                }
                nodes.Add(new($"{networkIdentity}/service:start", networkIdentity,
                    wall.Identity, MegastationBayUtilityNodeKind.ServiceBox,
                    new(trunk.MinimumX, trunk.Y), new(2.5f, 3f), .82f, .08f,
                    [trunkIdentity]));
                nodes.Add(new($"{networkIdentity}/service:end", networkIdentity,
                    wall.Identity, MegastationBayUtilityNodeKind.ServiceBox,
                    new(trunk.MaximumX, trunk.Y), new(2.5f, 3f), .82f, .08f,
                    [trunkIdentity]));
                networks.Add(new(networkIdentity, wall.Identity, trunkSeed,
                    bundles.ToArray(), nodes.ToArray(),
                    (int)((uint)MegastationSeed.Derive(trunkSeed, "colour") % 4u)));
                bundles.Clear();
                nodes.Clear();
            }
        }

        if (networks.Count == 0 && walls.Length > 0)
        {
            // Deterministic minimum proof: retry the widest wall without the density gate.
            MegastationBayWallSurface wall = walls.OrderByDescending(item => item.Width).First();
            int wallSeed = MegastationSeed.Derive(seed, $"network:{wall.Identity}:minimum");
            TrunkCandidate? trunk = FindTrunk(wall, 0, 1, wallSeed, occupancy.Grid, topology,
                landing, facilities, ref hardRejects);
            if (trunk is not null)
            {
                string identity = $"interior/bay-utilities:v1/{wall.Identity}/network:minimum";
                string run = $"{identity}/trunk:0";
                var bundle = new MegastationBayCableBundle(run, identity, wall.Identity,
                    MegastationBayUtilityRunLevel.Trunk, new(trunk.MinimumX, trunk.Y),
                    new(trunk.MaximumX, trunk.Y), 6, .13f, .36f, .55f, 9.2f);
                networks.Add(new(identity, wall.Identity, wallSeed, [bundle],
                    [new($"{identity}/service:start", identity, wall.Identity,
                            MegastationBayUtilityNodeKind.ServiceBox, bundle.Start,
                            new(2.5f, 3f), .82f, .08f, [run]),
                        new($"{identity}/service:end", identity, wall.Identity,
                            MegastationBayUtilityNodeKind.ServiceBox, bundle.End,
                            new(2.5f, 3f), .82f, .08f, [run])], 0));
            }
        }

        stopwatch.Stop();
        MegastationBayCableBundle[] allBundles = networks.SelectMany(item => item.Bundles).ToArray();
        float[] trunks = allBundles.Where(item => item.Level == MegastationBayUtilityRunLevel.Trunk)
            .Select(item => item.Length).Order().ToArray();
        int clampCount = allBundles.Sum(item => Math.Max(2,
            (int)MathF.Floor(item.Length / item.ClampSpacing) + 1));
        string summary = string.Join(",", walls.Select(wall =>
            $"{wall.Kind}:{networks.Count(network => network.WallIdentity == wall.Identity)}n/"
            + $"{allBundles.Count(bundle => bundle.WallIdentity == wall.Identity)}r"));
        string signature = Signature(networks);
        var diagnostics = new MegastationBayUtilityDiagnostics(
            AlgorithmVersion, walls.Length,
            networks.Select(network => network.WallIdentity).Distinct().Count(),
            networks.Count,
            allBundles.Count(item => item.Level == MegastationBayUtilityRunLevel.Trunk),
            allBundles.Count(item => item.Level == MegastationBayUtilityRunLevel.Branch),
            networks.SelectMany(item => item.Nodes).Count(node =>
                node.Kind == MegastationBayUtilityNodeKind.JunctionBox),
            networks.SelectMany(item => item.Nodes).Count(node =>
                node.Kind == MegastationBayUtilityNodeKind.ServiceBox),
            allBundles.Sum(item => item.CableCount), clampCount,
            allBundles.Sum(item => item.Length), Minimum(trunks), Median(trunks), Maximum(trunks),
            hardRejects, windowScore, stopwatch.ElapsedMilliseconds,
            0, 0, 0, summary, signature);
        return new(AlgorithmVersion, seed, networks, diagnostics);
    }

    private static TrunkCandidate? FindTrunk(
        MegastationBayWallSurface wall, int trunkIndex, int trunkCount, int seed,
        SliceGrid grid, BoundaryTopology topology, MegastationLandingDistrictPlan landing,
        MegastationBayFacilityPlan facilities, ref int hardRejects)
    {
        float minY = -wall.Height * .5f + 26f;
        float maxY = wall.Height * .5f - 20f;
        TrunkCandidate? best = null;
        for (int attempt = 0; attempt < 28; attempt++)
        {
            float bandT = (trunkIndex + 1f) / (trunkCount + 1f);
            float y = MathHelper.Lerp(minY, maxY, bandT)
                + (Unit(seed, $"y:{attempt}") - .5f) * (maxY - minY) * .72f;
            if (!TrySupportedHorizontalRun(wall, y, 4.5f, grid, topology,
                    out float start, out float end) || end - start < 90f)
                continue;
            start += 8f; end -= 8f;
            Vector2 centre = new((start + end) * .5f, y);
            Vector2 size = new(end - start, 4.5f);
            if (HardConflict(wall.Identity, centre, size, landing, facilities))
            {
                hardRejects++;
                continue;
            }
            int windows = SoftWindowScore(wall.Identity, centre, size, facilities);
            float score = windows * 1000f - (end - start);
            if (best is null || score < best.Score)
                best = new(start, end, y, windows, score);
        }
        return best;
    }

    private static BranchCandidate? FindBranch(
        MegastationBayWallSurface wall, float x, float trunkY, int seed,
        SliceGrid grid, BoundaryTopology topology, MegastationLandingDistrictPlan landing,
        MegastationBayFacilityPlan facilities, ref int hardRejects)
    {
        BranchCandidate? best = null;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float direction = ((attempt + ((uint)seed & 1u)) & 1) == 0 ? 1f : -1f;
            float length = MathHelper.Lerp(24f, 66f, Unit(seed, $"length:{attempt}"));
            float endY = trunkY + direction * length;
            float low = MathF.Min(trunkY, endY), high = MathF.Max(trunkY, endY);
            if (low < -wall.Height * .5f + 18f || high > wall.Height * .5f - 14f
                || !TrySupportedVerticalRun(wall, x, low, high, 3.5f, grid, topology))
                continue;
            Vector2 centre = new(x, (trunkY + endY) * .5f);
            Vector2 size = new(3.5f, MathF.Abs(endY - trunkY));
            if (HardConflict(wall.Identity, centre, size, landing, facilities))
            {
                hardRejects++;
                continue;
            }
            int windows = SoftWindowScore(wall.Identity, centre, size, facilities);
            float score = windows * 1000f - length;
            if (best is null || score < best.Score)
                best = new(x, trunkY, endY, windows, score);
        }
        return best;
    }

    private static bool HardConflict(
        string wallIdentity, Vector2 centre, Vector2 size,
        MegastationLandingDistrictPlan landing, MegastationBayFacilityPlan facilities)
        => facilities.Reservations.Any(item => item.WallIdentity == wallIdentity
                && item.Kind != MegastationBayHabitationReservationKind.PlainWindowBand
                && Overlaps(centre, size, item.Centre, item.Size, 2f))
            || landing.SiteReservations.Any(item => item.HostWallIdentity == wallIdentity
                && Overlaps(centre, size, item.WallCentre, item.WallSize, 3f));

    private static int SoftWindowScore(
        string wallIdentity, Vector2 centre, Vector2 size,
        MegastationBayFacilityPlan facilities)
        => facilities.Reservations.Count(item => item.WallIdentity == wallIdentity
            && item.Kind == MegastationBayHabitationReservationKind.PlainWindowBand
            && Overlaps(centre, size, item.Centre, item.Size, 1f));

    private static bool TrySupportedHorizontalRun(
        MegastationBayWallSurface wall, float y, float height, SliceGrid grid,
        BoundaryTopology topology, out float minimum, out float maximum)
        => TrySupportedRun(wall, y, height, horizontal: true, grid, topology,
            out minimum, out maximum);

    private static bool TrySupportedVerticalRun(
        MegastationBayWallSurface wall, float x, float low, float high, float width,
        SliceGrid grid, BoundaryTopology topology)
    {
        bool found = TrySupportedRun(wall, x, width, horizontal: false, grid, topology,
            out float minimum, out float maximum);
        return found && low >= minimum - .01f && high <= maximum + .01f;
    }

    private static bool TrySupportedRun(
        MegastationBayWallSurface wall, float cross, float requiredCrossSize, bool horizontal,
        SliceGrid grid, BoundaryTopology topology, out float minimum, out float maximum)
    {
        var intervals = topology.Faces.Where(face =>
                face.SpaceKind == MegastationBoundarySpaceKind.InteriorBoundary
                && Vector3.Dot(BoundaryTopologyBuilder.Normal(face.Direction), wall.Normal) > .9999f)
            .Select(face => (Face: face, Points: face.Vertices
                .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex)).ToArray()))
            .Where(item => MathF.Abs(Vector3.Dot(item.Points[0] - wall.Centre, wall.Normal)) < .01f)
            .Select(item =>
            {
                float minX = item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float maxX = item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Right));
                float minY = item.Points.Min(point => Vector3.Dot(point - wall.Centre, wall.Up));
                float maxY = item.Points.Max(point => Vector3.Dot(point - wall.Centre, wall.Up));
                return horizontal
                    ? (Min: minX, Max: maxX, CrossMin: minY, CrossMax: maxY)
                    : (Min: minY, Max: maxY, CrossMin: minX, CrossMax: maxX);
            })
            .Where(item => cross - requiredCrossSize * .5f >= item.CrossMin - .01f
                && cross + requiredCrossSize * .5f <= item.CrossMax + .01f)
            .OrderBy(item => item.Min).ToArray();
        minimum = maximum = 0f;
        if (intervals.Length == 0) return false;
        int start = 0;
        while (start < intervals.Length)
        {
            float runMin = intervals[start].Min, runMax = intervals[start].Max;
            int next = start + 1;
            while (next < intervals.Length && intervals[next].Min <= runMax + .02f)
            {
                runMax = MathF.Max(runMax, intervals[next].Max);
                next++;
            }
            if (runMax - runMin > maximum - minimum)
            {
                minimum = runMin; maximum = runMax;
            }
            start = next;
        }
        return maximum > minimum;
    }

    private static bool Overlaps(Vector2 a, Vector2 aSize, Vector2 b, Vector2 bSize, float margin)
        => MathF.Abs(a.X - b.X) < (aSize.X + bSize.X) * .5f + margin
            && MathF.Abs(a.Y - b.Y) < (aSize.Y + bSize.Y) * .5f + margin;
    private static float Unit(int seed, string domain)
        => (uint)MegastationSeed.Derive(seed, domain) / (float)uint.MaxValue;
    private static float Minimum(float[] values) => values.Length == 0 ? 0f : values[0];
    private static float Median(float[] values) => values.Length == 0 ? 0f : values[values.Length / 2];
    private static float Maximum(float[] values) => values.Length == 0 ? 0f : values[^1];

    private static string Signature(IEnumerable<MegastationBayUtilityNetwork> networks)
    {
        var text = new StringBuilder(1024).Append(AlgorithmVersion);
        foreach (MegastationBayUtilityNetwork network in networks)
        {
            text.Append('|').Append(network.Identity).Append(':').Append(network.ColourVariant);
            foreach (MegastationBayCableBundle bundle in network.Bundles)
                text.Append('|').Append(bundle.Identity).Append(':').Append((int)bundle.Level)
                    .Append(':').Append(bundle.Start).Append(':').Append(bundle.End)
                    .Append(':').Append(bundle.CableCount).Append(':')
                    .Append(bundle.CableRadius.ToString("R", CultureInfo.InvariantCulture));
            foreach (MegastationBayUtilityNode node in network.Nodes)
                text.Append('|').Append(node.Identity).Append(':').Append((int)node.Kind)
                    .Append(':').Append(node.Position);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed record TrunkCandidate(float MinimumX, float MaximumX, float Y,
        int WindowScore, float Score)
    {
        public float Length => MaximumX - MinimumX;
    }
    private sealed record BranchCandidate(float X, float StartY, float EndY,
        int WindowScore, float Score);
}

public static class MegastationBayUtilityMeshBuilder
{
    public static MegastationBayUtilityMeshResult Append(
        StationModuleMesh mesh, MegastationBayUtilityPlan plan,
        IReadOnlyList<MegastationBayWallSurface> walls,
        MegastationSystemMaterialAssignment? materials)
    {
        int firstVertex = mesh.VertexCount, firstTriangle = mesh.IndexCount / 3;
        int majorTriangles = 0;
        var receiverFaces = new List<int>();
        foreach (MegastationBayUtilityNetwork network in plan.Networks)
        {
            MegastationBayWallSurface wall = walls.Single(item => item.Identity == network.WallIdentity);
            Color cable = CableColour(network.ColourVariant);
            Color metal = materials?.Palette.SecondaryTint ?? new Color(84, 88, 88);
            Color cabinet = ProceduralMaterialCpuGenerator.Blend(metal, cable, .16f);
            foreach (MegastationBayCableBundle bundle in network.Bundles)
            {
                mesh.CurrentMaterialFamily = SystemMaterialFamilyId.CleanTechnicalAlloy;
                mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
                Vector2 direction2 = Vector2.Normalize(bundle.End - bundle.Start);
                Vector3 direction = wall.Right * direction2.X + wall.Up * direction2.Y;
                Vector3 across = bundle.IsHorizontal ? wall.Up : wall.Right;
                for (int cableIndex = 0; cableIndex < bundle.CableCount; cableIndex++)
                {
                    float offset = (cableIndex - (bundle.CableCount - 1) * .5f)
                        * bundle.BundleSpacing;
                    float radius = bundle.CableRadius * (.90f + .20f * Sample(
                        bundle.Identity, $"radius:{cableIndex}"));
                    Vector3 start = Point(wall, bundle.Start, bundle.SurfaceOffset)
                        + across * offset;
                    Vector3 end = Point(wall, bundle.End, bundle.SurfaceOffset)
                        + across * offset;
                    AddReceiverGeometry(() => mesh.AddPrismPipe(
                        start, end, radius, 6, cable, true, true));
                }
                int clamps = Math.Max(2, (int)MathF.Floor(bundle.Length / bundle.ClampSpacing) + 1);
                float bundleWidth = (bundle.CableCount - 1) * bundle.BundleSpacing
                    + bundle.CableRadius * 2.8f;
                for (int clamp = 0; clamp < clamps; clamp++)
                {
                    float t = clamps == 1 ? .5f : clamp / (float)(clamps - 1);
                    Vector2 position = Vector2.Lerp(bundle.Start, bundle.End, t);
                    Vector3 centre = Point(wall, position, bundle.SurfaceOffset - bundle.CableRadius * .8f);
                    Vector3 frameAcross = Vector3.Normalize(Vector3.Cross(wall.Normal, direction));
                    Matrix frame = Frame(frameAcross, wall.Normal, direction, centre);
                    mesh.CurrentMaterialFamily = SystemMaterialFamilyId.DullStructuralMetal;
                    AddReceiverGeometry(() => mesh.AddOrientedBox(frame,
                        new(bundleWidth + .24f, .18f, .34f), metal));
                    Matrix strap = Frame(frameAcross, wall.Normal, direction,
                        centre + wall.Normal * (bundle.CableRadius * 1.75f));
                    AddReceiverGeometry(() => mesh.AddOrientedBox(strap,
                        new(bundleWidth + .38f, .12f, .18f), metal));
                }
            }
            foreach (MegastationBayUtilityNode node in network.Nodes)
            {
                bool major = node.Kind == MegastationBayUtilityNodeKind.JunctionBox;
                mesh.CurrentMaterialFamily = major
                    ? SystemMaterialFamilyId.HeavyIndustrialPlate
                    : SystemMaterialFamilyId.PaintedCoatedMetal;
                mesh.CurrentDecorClass = major
                    ? DecorClass.MegastationInteriorMajor
                    : DecorClass.MegastationInteriorMinor;
                Vector3 centre = Point(wall, node.Position,
                    node.SurfaceOffset + node.Depth * .5f);
                Matrix frame = Frame(-wall.Right, wall.Normal, wall.Up, centre);
                int before = mesh.IndexCount;
                AddReceiverGeometry(() => mesh.AddOrientedBox(frame,
                    new(node.Size.X, node.Depth, node.Size.Y), cabinet));
                if (major) majorTriangles += (mesh.IndexCount - before) / 3;
                mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
                mesh.CurrentMaterialFamily = SystemMaterialFamilyId.CleanTechnicalAlloy;
                Matrix face = frame;
                face.Translation = centre + wall.Normal * (node.Depth * .5f + .035f);
                AddReceiverGeometry(() => mesh.AddOrientedBox(face,
                    new(node.Size.X * .64f, .07f, node.Size.Y * .16f),
                    ProceduralMaterialCpuGenerator.ShiftLuminance(cable, 20f)));
            }
        }
        int vertices = mesh.VertexCount - firstVertex;
        int triangles = mesh.IndexCount / 3 - firstTriangle;
        return new(receiverFaces, plan.Diagnostics with
        {
            VisibleVertexCount = vertices,
            VisibleTriangleCount = triangles,
            MajorCasterTriangleCount = majorTriangles,
        });

        void AddReceiverGeometry(Action add)
        {
            int first = mesh.FaceCount;
            add();
            for (int face = first; face < mesh.FaceCount; face++)
                receiverFaces.Add(face);
        }
    }

    private static Vector3 Point(MegastationBayWallSurface wall, Vector2 local, float offset)
        => wall.Centre + wall.Right * local.X + wall.Up * local.Y + wall.Normal * offset;
    private static Matrix Frame(Vector3 x, Vector3 y, Vector3 z, Vector3 centre) => new(
        x.X,x.Y,x.Z,0f, y.X,y.Y,y.Z,0f, z.X,z.Y,z.Z,0f,
        centre.X,centre.Y,centre.Z,1f);
    private static Color CableColour(int variant) => variant switch
    {
        1 => new Color(43, 46, 48),
        2 => new Color(61, 53, 48),
        3 => new Color(67, 45, 39),
        _ => new Color(31, 34, 36),
    };
    private static float Sample(string identity, string domain)
        => (uint)MegastationSeed.Derive(MegastationSeed.Root(identity, 1), domain)
            / (float)uint.MaxValue;
}
