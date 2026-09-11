using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationServiceChannelEndpoint { SealedCap, UtilityHousing }
public enum MegastationServiceChannelRunScale { Primary, Secondary }
public enum MegastationServiceChannelDensity { Light, ChannelRich }
public enum MegastationServiceChannelNodeVariant
{
    Exposed,
    ConverterHouse,
    SwitchingNode,
    HeavyDistribution,
}
public enum MegastationServiceChannelNodeKind
{
    DeadEnd,
    Inline,
    Turn,
    TJunction,
    FourWay,
}

public sealed record MegastationServiceChannelRun(
    string Identity, string RouteIdentity, MegastationServiceChannelRunScale Scale,
    Vector2 Start, Vector2 End, float Width, float ApparentDepth, int CableCount)
{
    public float Length => Vector2.Distance(Start, End);
    public bool AlongU => MathF.Abs(End.X - Start.X) >= MathF.Abs(End.Y - Start.Y);
}

public sealed record MegastationServiceChannelNode(
    string Identity, Vector2 Position, MegastationServiceChannelNodeKind Kind,
    MegastationServiceChannelNodeVariant Variant,
    bool MainAlongU, float HousingWidth, float HousingLength, float HousingHeight,
    IReadOnlyList<string> IncidentRunIdentities,
    MegastationServiceChannelEndpoint? Endpoint);

public sealed record MegastationServiceChannelBridge(
    string Identity, string RunIdentity, float PositionAlongRun, float DeckWidth);

public sealed record MegastationServiceChannelNetwork(
    string Identity, int Seed, string SurfaceStableId, string ZoneId,
    MegastationZoneRole ZoneRole, MegastationServiceChannelDensity Density,
    GridDirection Direction,
    float PlaneCoordinateMetres, Vector3 Normal, Vector3 TangentU, Vector3 TangentV,
    float ChannelWidth, IReadOnlyList<MegastationServiceChannelRun> Runs,
    IReadOnlyList<MegastationServiceChannelNode> Nodes,
    IReadOnlyList<MegastationServiceChannelBridge> Bridges);

public sealed record MegastationServiceChannelDebugRun(
    Vector3 Normal, Vector3 TangentU, Vector3 TangentV, float PlaneCoordinateMetres,
    Vector2 Start, Vector2 End, MegastationServiceChannelRunScale Scale);

public sealed record MegastationServiceChannelDiagnostics(
    float EligibleArea, int EligibleRegionCount, int CandidateSurfaceCount,
    int NetworkSurfaceCount, int PrimaryTrunkCount, int SecondaryBranchCount,
    int RunSegmentCount, int TurnCount, int TJunctionCount, int FourWayJunctionCount,
    int CoveredTJunctionCount, int UncoveredTJunctionCount, int CoveredFourWayJunctionCount,
    int DeadEndCount, int BridgeCount, float TotalChannelLength,
    float MinimumPrimaryLength, float MedianPrimaryLength, float MaximumPrimaryLength,
    int ExactMaskRejectCount, int G1RejectCount, int WindowRejectCount,
    int LightRejectCount, int G2RejectCount, int MegaGreebleRejectCount,
    int FabricRejectCount, int DensityRejectCount, int CapRejectCount,
    IReadOnlyDictionary<MegastationZoneRole, int> ByRole,
    int VisibleVertexCount, int VisibleTriangleCount, long VisibleMeshBytes,
    int ShadowVertexCount, int ShadowTriangleCount, long ShadowMeshBytes,
    int CoveredNodeVisibleVertexCount, int CoveredNodeVisibleTriangleCount,
    int CoveredNodeShadowVertexCount, int CoveredNodeShadowTriangleCount,
    long PlanningMilliseconds, long MeshBuildMilliseconds,
    int OwnedTextureDelta, int GpuBufferDelta, int MaterialRangeCount,
    string PlanSignature,
    int ChannelBearingSurfaceCount = 0,
    int RunsWithAdjacentG2Count = 0,
    int RunsWithAdjacentFabricCount = 0,
    int JunctionsWithDevelopmentCount = 0,
    int EndpointsWithDevelopmentCount = 0,
    int ParallelClearanceRejectCount = 0);

public sealed record MegastationServiceChannelPlan(
    IReadOnlyList<MegastationServiceChannelNetwork> Networks,
    MegastationServiceChannelDiagnostics Diagnostics,
    IReadOnlyList<MegastationServiceChannelDebugRun> DebugRuns)
{
    public IReadOnlyList<MegastationServiceChannelRun> Runs => Networks.SelectMany(n => n.Runs).ToArray();
    public IReadOnlyList<MegastationServiceChannelNode> Nodes => Networks.SelectMany(n => n.Nodes).ToArray();
    public IReadOnlyList<MegastationServiceChannelBridge> Bridges => Networks.SelectMany(n => n.Bridges).ToArray();
}

public sealed record MegastationServiceChannelMeshBuildResult(
    StationModuleMesh Mesh, MegastationServiceChannelDiagnostics Diagnostics);

public static class MegastationServiceChannelPlanner
{
    private const string AlgorithmKey = "service-channels:sc2";
    private const float ScanStep = 8f;
    private const int NetworkCap = 18;

    private enum BlockerKind { G1, Window, Light, G2, MegaGreeble, Fabric }
    private readonly record struct Rect(float MinU, float MaxU, float MinV, float MaxV);
    private readonly record struct Blocker(Rect Rect, BlockerKind Kind);
    private sealed record RawRoute(string Identity, MegastationServiceChannelRunScale Scale,
        float Width, float Depth, int CableCount, IReadOnlyList<Vector2> Points);
    private sealed record RawLeg(string RouteIdentity, MegastationServiceChannelRunScale Scale,
        float Width, float Depth, int CableCount, Vector2 Start, Vector2 End);
    private sealed record SurfaceCandidate(MegastationPlanarRegion Region, int Seed,
        float Priority, MegastationServiceChannelNetwork Network);
    private sealed class RejectCounts
    {
        public int Mask, G1, Window, Light, G2, Mega, Fabric, Parallel;
        public void Add(BlockerKind kind)
        {
            switch (kind)
            {
                case BlockerKind.G1: G1++; break;
                case BlockerKind.Window: Window++; break;
                case BlockerKind.Light: Light++; break;
                case BlockerKind.G2: G2++; break;
                case BlockerKind.MegaGreeble: Mega++; break;
                case BlockerKind.Fabric: Fabric++; break;
            }
        }
    }

    public static MegastationServiceChannelPlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments, MegastationWindowPlan windows,
        MegastationLightPlan lights, MegastationInfrastructurePlan infrastructure,
        MegastationMegaGreeblePlan megaGreeble, MegastationFabricPlan fabric,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var rejects = new RejectCounts();
        int densityRejects = 0;
        MegastationPlanarRegion[] eligible = regions.Where(r => RoleDensity(r.ZoneRole) > 0f
                && r.PhysicalArea >= 8_000f
                && MathF.Max(r.PhysicalExtents.X, r.PhysicalExtents.Y) >= 120f
                && MathF.Min(r.PhysicalExtents.X, r.PhysicalExtents.Y) >= 24f)
            .OrderBy(r => r.StableId, StringComparer.Ordinal).ToArray();
        var candidates = new List<SurfaceCandidate>();

        foreach (MegastationPlanarRegion region in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int seed = MegastationSeed.Derive(
                MegastationSeed.Derive(region.ZoneSeed, AlgorithmKey), region.StableId);
            float probability = Math.Clamp(RoleDensity(region.ZoneRole)
                * (.72f + Math.Clamp(region.PhysicalArea / 90_000f, 0f, .65f)), 0f, .58f);
            if (Sample(seed, "surface-selected") >= probability)
            {
                densityRejects++;
                continue;
            }
            Blocker[] blockers = BuildBlockers(region, attachments, windows, lights,
                infrastructure, megaGreeble, fabric);
            MegastationServiceChannelNetwork? network = BuildNetwork(region, seed, blockers,
                rejects, cancellationToken);
            if (network is null)
                continue;
            float primaryExtent = network.Runs
                .Where(run => run.Scale == MegastationServiceChannelRunScale.Primary)
                .Sum(run => run.Length);
            float priority = probability + Math.Clamp(primaryExtent / 2_000f, 0f, .5f)
                + Sample(seed, "surface-priority") * .08f;
            candidates.Add(new(region, seed, priority, network));
        }

        MegastationServiceChannelNetwork[] networks = candidates
            .OrderByDescending(c => c.Priority).ThenBy(c => c.Region.StableId, StringComparer.Ordinal)
            .Take(NetworkCap).Select(c => c.Network)
            .OrderBy(n => n.Identity, StringComparer.Ordinal).ToArray();
        int capRejects = Math.Max(0, candidates.Count - networks.Length);
        timer.Stop();
        float[] primaryLengths = networks.SelectMany(n => n.Runs)
            .Where(r => r.Scale == MegastationServiceChannelRunScale.Primary)
            .GroupBy(r => r.RouteIdentity).Select(g => g.Sum(r => r.Length)).Order().ToArray();
        MegastationServiceChannelNode[] nodes = networks.SelectMany(n => n.Nodes).ToArray();
        int coveredT = nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.TJunction
            && n.Variant != MegastationServiceChannelNodeVariant.Exposed);
        int uncoveredT = nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.TJunction
            && n.Variant == MegastationServiceChannelNodeVariant.Exposed);
        var diagnostics = new MegastationServiceChannelDiagnostics(
            eligible.Sum(r => r.PhysicalArea), eligible.Length, eligible.Length,
            networks.Length, primaryLengths.Length,
            networks.SelectMany(n => n.Runs).Where(r => r.Scale == MegastationServiceChannelRunScale.Secondary)
                .Select(r => r.RouteIdentity).Distinct(StringComparer.Ordinal).Count(),
            networks.Sum(n => n.Runs.Count),
            nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.Turn),
            nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.TJunction),
            nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.FourWay),
            coveredT, uncoveredT,
            nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.FourWay
                && n.Variant != MegastationServiceChannelNodeVariant.Exposed),
            nodes.Count(n => n.Kind == MegastationServiceChannelNodeKind.DeadEnd),
            networks.Sum(n => n.Bridges.Count), networks.Sum(n => n.Runs.Sum(r => r.Length)),
            Min(primaryLengths), Median(primaryLengths), Max(primaryLengths),
            rejects.Mask, rejects.G1, rejects.Window, rejects.Light, rejects.G2,
            rejects.Mega, rejects.Fabric, densityRejects, capRejects,
            Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role,
                role => networks.Count(n => n.ZoneRole == role)),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            timer.ElapsedMilliseconds, 0, 0, 0, 0,
            Signature(networks),
            ParallelClearanceRejectCount: rejects.Parallel);
#if DEBUG
        MegastationServiceChannelDebugRun[] debug = networks.SelectMany(network =>
            network.Runs.Select(run => new MegastationServiceChannelDebugRun(
                network.Normal, network.TangentU, network.TangentV,
                network.PlaneCoordinateMetres, run.Start, run.End, run.Scale))).ToArray();
#else
        MegastationServiceChannelDebugRun[] debug = [];
#endif
        return new(networks, diagnostics, debug);
    }

    private static MegastationServiceChannelNetwork? BuildNetwork(
        MegastationPlanarRegion region, int seed, IReadOnlyList<Blocker> blockers,
        RejectCounts rejects, CancellationToken cancellationToken)
    {
        float width = Lerp(10f, 23f, Sample(seed, "width"));
        float depth = Lerp(1.6f, 4.5f, Sample(seed, "depth"));
        bool primaryAlongU = region.PhysicalExtents.X > region.PhysicalExtents.Y * 1.18f
            || (region.PhysicalExtents.Y <= region.PhysicalExtents.X * 1.18f
                && Sample(seed, "dominant-axis") < .5f);
        (float start, float end, float cross)? interval = FindPrimaryInterval(
            region, blockers, primaryAlongU, width, seed, rejects);
        if (interval is not { } primary || primary.end - primary.start < 100f)
            return null;

        float inset = MathF.Min(5f, (primary.end - primary.start) * .025f);
        Vector2 primaryStart = primaryAlongU
            ? new(primary.start + inset, primary.cross) : new(primary.cross, primary.start + inset);
        Vector2 primaryEnd = primaryAlongU
            ? new(primary.end - inset, primary.cross) : new(primary.cross, primary.end - inset);
        string networkIdentity = $"{region.StableId}/{AlgorithmKey}/network";
        var routes = new List<RawRoute>();
        string primaryId = $"{networkIdentity}/primary:0";
        routes.Add(new(primaryId, MegastationServiceChannelRunScale.Primary, width, depth,
            CableCount(seed, "primary-cables"), [primaryStart, primaryEnd]));

        float primaryLength = Vector2.Distance(primaryStart, primaryEnd);
        bool channelRich = IsChannelRich(region, seed);
        int baseBranches = Math.Clamp((int)(primaryLength / 170f), 2, 5);
        int extraBranches = channelRich
            ? Math.Clamp(2 + (int)(primaryLength / 180f), 3, 6)
            : 0;
        int desiredBranches = baseBranches + extraBranches;
        for (int branch = 0; branch < desiredBranches; branch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool richExtra = branch >= baseBranches;
            int localBranch = richExtra ? branch - baseBranches : branch;
            string branchKey = richExtra ? $"rich-branch:{localBranch}" : $"branch:{localBranch}";
            int branchSeed = MegastationSeed.Derive(seed, branchKey);
            int layoutCount = richExtra ? extraBranches : baseBranches;
            float fraction = (localBranch + 1f) / (layoutCount + 1f)
                + Signed(branchSeed, "along-jitter") * (.16f / (layoutCount + 1f));
            Vector2 origin = Vector2.Lerp(primaryStart, primaryEnd, Math.Clamp(fraction, .12f, .88f));
            Vector2 perpendicular = primaryAlongU ? Vector2.UnitY : Vector2.UnitX;
            if ((branch & 1) != 0) perpendicular = -perpendicular;
            if (Sample(branchSeed, "side-flip") < .22f) perpendicular = -perpendicular;
            float available = DirectionalClearance(region, blockers, origin, perpendicular,
                width, rejects);
            if (!richExtra && localBranch == 0 && Sample(seed, "four-way") < .42f)
            {
                float oppositeAvailable = DirectionalClearance(region, blockers, origin,
                    -perpendicular, width, rejects);
                if (available >= 65f && oppositeAvailable >= 65f)
                {
                    float forwardLength = MathF.Min(available - 2f,
                        Lerp(65f, MathF.Min(220f, available - 2f), Sample(branchSeed, "cross-forward")));
                    float oppositeLength = MathF.Min(oppositeAvailable - 2f,
                        Lerp(65f, MathF.Min(220f, oppositeAvailable - 2f), Sample(branchSeed, "cross-opposite")));
                    routes.Add(new($"{networkIdentity}/secondary:{localBranch}",
                        MegastationServiceChannelRunScale.Secondary, width, depth,
                        CableCount(branchSeed, "cables"),
                        [origin - perpendicular * oppositeLength, origin,
                            origin + perpendicular * forwardLength]));
                    continue;
                }
            }
            if (available < 65f)
            {
                perpendicular = -perpendicular;
                available = DirectionalClearance(region, blockers, origin, perpendicular,
                    width, rejects);
            }
            if (available < 65f) continue;
            float branchLength = MathF.Min(available - 2f,
                Lerp(MathF.Min(85f, available - 2f), MathF.Min(320f, available - 2f),
                    Sample(branchSeed, "length")));
            if (branchLength < 60f) continue;
            Vector2 firstEnd = origin + perpendicular * branchLength;
            var points = new List<Vector2> { origin, firstEnd };
            if (Sample(branchSeed, "turn") < .48f)
            {
                Vector2 turnDirection = primaryAlongU ? Vector2.UnitX : Vector2.UnitY;
                if (Sample(branchSeed, "turn-direction") < .5f) turnDirection = -turnDirection;
                float turnAvailable = DirectionalClearance(region, blockers, firstEnd,
                    turnDirection, width, rejects);
                float turnLength = MathF.Min(turnAvailable - 2f,
                    Lerp(55f, MathF.Min(230f, turnAvailable - 2f), Sample(branchSeed, "turn-length")));
                Rect turnFootprint = new(firstEnd.X - width * .5f, firstEnd.X + width * .5f,
                    firstEnd.Y - width * .5f, firstEnd.Y + width * .5f);
                if (turnAvailable >= 62f && turnLength >= 50f
                    && IsFree(region, blockers, turnFootprint, rejects))
                    points.Add(firstEnd + turnDirection * turnLength);
            }
            string routeKind = richExtra ? "rich-secondary" : "secondary";
            routes.Add(new($"{networkIdentity}/{routeKind}:{localBranch}",
                MegastationServiceChannelRunScale.Secondary, width, depth,
                CableCount(branchSeed, "cables"), points));
        }
        if (routes.Count == 1)
            return null;

        (MegastationServiceChannelRun[] runs, MegastationServiceChannelNode[] nodes) =
            BuildTopology(networkIdentity, seed, routes, region, blockers, rejects);
        MegastationServiceChannelBridge[] bridges = PlanBridges(networkIdentity, seed, runs, nodes);
        return new(networkIdentity, seed, region.StableId, region.ZoneId, region.ZoneRole,
            channelRich ? MegastationServiceChannelDensity.ChannelRich
                : MegastationServiceChannelDensity.Light,
            region.Direction, region.PlaneCoordinateMetres, region.OutwardNormal,
            region.TangentU, region.TangentV, width, runs, nodes, bridges);
    }

    private static (float start, float end, float cross)? FindPrimaryInterval(
        MegastationPlanarRegion region, IReadOnlyList<Blocker> blockers, bool alongU,
        float width, int seed, RejectCounts rejects)
    {
        float minAlong = alongU ? region.MinU : region.MinV;
        float maxAlong = alongU ? region.MaxU : region.MaxV;
        float minCross = (alongU ? region.MinV : region.MinU) + width * .5f + 1f;
        float maxCross = (alongU ? region.MaxV : region.MaxU) - width * .5f - 1f;
        if (maxCross <= minCross) return null;
        (float start, float end, float cross)? best = null;
        for (int lane = 0; lane < 9; lane++)
        {
            float t = (lane + .5f) / 9f;
            float cross = Lerp(minCross, maxCross, t)
                + Signed(seed, $"lane:{lane}") * MathF.Min(9f, (maxCross - minCross) / 25f);
            float? activeStart = null;
            for (float position = minAlong + ScanStep * .5f;
                 position <= maxAlong - ScanStep * .5f + .01f; position += ScanStep)
            {
                Rect cell = alongU
                    ? new(position - ScanStep * .5f, position + ScanStep * .5f,
                        cross - width * .5f, cross + width * .5f)
                    : new(cross - width * .5f, cross + width * .5f,
                        position - ScanStep * .5f, position + ScanStep * .5f);
                bool free = IsFree(region, blockers, cell, rejects);
                if (free && activeStart is null) activeStart = position - ScanStep * .5f;
                if ((!free || position + ScanStep * .5f >= maxAlong - .01f) && activeStart is { } start)
                {
                    float end = free ? MathF.Min(maxAlong, position + ScanStep * .5f)
                        : position - ScanStep * .5f;
                    if (best is null || end - start > best.Value.end - best.Value.start)
                        best = (start, end, cross);
                    activeStart = null;
                }
            }
        }
        return best;
    }

    private static float DirectionalClearance(MegastationPlanarRegion region,
        IReadOnlyList<Blocker> blockers, Vector2 origin, Vector2 direction, float width,
        RejectCounts rejects)
    {
        float clear = 0f;
        float maximum = MathF.Max(region.PhysicalExtents.X, region.PhysicalExtents.Y);
        for (float distance = ScanStep; distance <= maximum; distance += ScanStep)
        {
            Vector2 centre = origin + direction * (distance - ScanStep * .5f);
            Rect cell = MathF.Abs(direction.X) > .5f
                ? new(centre.X - ScanStep * .5f, centre.X + ScanStep * .5f,
                    centre.Y - width * .5f, centre.Y + width * .5f)
                : new(centre.X - width * .5f, centre.X + width * .5f,
                    centre.Y - ScanStep * .5f, centre.Y + ScanStep * .5f);
            if (!IsFree(region, blockers, cell, rejects)) break;
            clear = distance;
        }
        return clear;
    }

    private static bool IsFree(MegastationPlanarRegion region,
        IReadOnlyList<Blocker> blockers, Rect rect, RejectCounts rejects)
    {
        if (!MegastationPlanarRegionExtractor.ContainsFootprint(
                region, rect.MinU, rect.MaxU, rect.MinV, rect.MaxV, .5f))
        {
            rejects.Mask++;
            return false;
        }
        foreach (Blocker blocker in blockers)
        {
            if (!Overlaps(rect, blocker.Rect, 1.25f)) continue;
            rejects.Add(blocker.Kind);
            return false;
        }
        return true;
    }

    private static Blocker[] BuildBlockers(MegastationPlanarRegion region,
        MegastationAttachmentPlan attachments, MegastationWindowPlan windows,
        MegastationLightPlan lights, MegastationInfrastructurePlan infrastructure,
        MegastationMegaGreeblePlan megaGreeble, MegastationFabricPlan fabric)
    {
        var result = new List<Blocker>();
        foreach (MegastationAttachmentReservation reservation in attachments.Reservations)
        {
            if (!Coplanar(region, reservation.Normal, reservation.PlaneCoordinateMetres)) continue;
            Vector3[] corners =
            [
                reservation.Normal * reservation.PlaneCoordinateMetres + reservation.TangentU * reservation.MinU + reservation.TangentV * reservation.MinV,
                reservation.Normal * reservation.PlaneCoordinateMetres + reservation.TangentU * reservation.MaxU + reservation.TangentV * reservation.MinV,
                reservation.Normal * reservation.PlaneCoordinateMetres + reservation.TangentU * reservation.MaxU + reservation.TangentV * reservation.MaxV,
                reservation.Normal * reservation.PlaneCoordinateMetres + reservation.TangentU * reservation.MinU + reservation.TangentV * reservation.MaxV,
            ];
            result.Add(new(Project(region, corners), BlockerKind.G1));
        }
        foreach (MegastationWindowInstance window in windows.Windows)
            if (Coplanar(region, window.Normal, Vector3.Dot(window.Centre, window.Normal)))
                result.Add(new(PointRect(region, window.Centre,
                    MathF.Max(window.Width, window.Height) * .5f + 1f), BlockerKind.Window));
        foreach (MegastationLightInstance light in lights.Lights)
            if (Coplanar(region, light.Normal, Vector3.Dot(light.SurfacePosition, light.Normal)))
                result.Add(new(PointRect(region, light.SurfacePosition, 2.5f), BlockerKind.Light));
        foreach (MegastationInfrastructureCluster cluster in infrastructure.Clusters)
            if (cluster.SurfaceStableId == region.StableId)
                result.Add(new(new(cluster.MinU, cluster.MaxU, cluster.MinV, cluster.MaxV), BlockerKind.G2));
        foreach (MegastationMegaGreebleInstance mega in megaGreeble.Instances)
            if (mega.SurfaceStableId == region.StableId)
                result.Add(new(new(mega.MinU, mega.MaxU, mega.MinV, mega.MaxV), BlockerKind.MegaGreeble));
        foreach (MegastationFabricInstance building in fabric.Instances)
            if (building.SurfaceStableId == region.StableId)
                result.Add(new(new(building.MinU, building.MaxU, building.MinV, building.MaxV), BlockerKind.Fabric));
        return result.ToArray();
    }

    private static (MegastationServiceChannelRun[] runs, MegastationServiceChannelNode[] nodes)
        BuildTopology(string networkIdentity, int seed, IReadOnlyList<RawRoute> routes,
            MegastationPlanarRegion region, IReadOnlyList<Blocker> blockers, RejectCounts rejects)
    {
        RawLeg[] rawLegs = routes.SelectMany(route => route.Points.Zip(route.Points.Skip(1),
                (a, b) => new RawLeg(route.Identity, route.Scale, route.Width, route.Depth,
                    route.CableCount, a, b)))
            .Where(leg => Vector2.DistanceSquared(leg.Start, leg.End) > 1f).ToArray();
        var acceptedLegs = new List<RawLeg>();
        foreach (RawLeg leg in rawLegs)
        {
            if (acceptedLegs.Any(existing => ParallelOverlapTooClose(existing, leg)))
            {
                rejects.Parallel++;
                continue;
            }
            acceptedLegs.Add(leg);
        }
        RawLeg[] legs = acceptedLegs.ToArray();
        var points = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        void AddPoint(Vector2 point) => points[PointKey(point)] = point;
        foreach (RawLeg leg in legs) { AddPoint(leg.Start); AddPoint(leg.End); }
        for (int a = 0; a < legs.Length; a++)
        for (int b = a + 1; b < legs.Length; b++)
            if (TryIntersection(legs[a], legs[b], out Vector2 intersection)) AddPoint(intersection);

        var provisional = new List<(RawLeg leg, Vector2 start, Vector2 end)>();
        foreach (RawLeg leg in legs)
        {
            Vector2[] onLeg = points.Values.Where(point => OnSegment(point, leg.Start, leg.End))
                .OrderBy(point => Vector2.DistanceSquared(leg.Start, point)).ToArray();
            for (int i = 0; i < onLeg.Length - 1; i++)
                if (Vector2.DistanceSquared(onLeg[i], onLeg[i + 1]) > 1f)
                    provisional.Add((leg, onLeg[i], onLeg[i + 1]));
        }
        var dedup = provisional.GroupBy(item => SegmentKey(item.start, item.end), StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.leg.RouteIdentity, StringComparer.Ordinal).First())
            .OrderBy(item => item.leg.RouteIdentity, StringComparer.Ordinal)
            .ThenBy(item => SegmentKey(item.start, item.end), StringComparer.Ordinal).ToArray();
        MegastationServiceChannelRun[] runs = dedup.Select((item, index) => new MegastationServiceChannelRun(
            $"{networkIdentity}/run:{index}", item.leg.RouteIdentity, item.leg.Scale,
            item.start, item.end, item.leg.Width, item.leg.Depth, item.leg.CableCount)).ToArray();

        var nodes = new List<MegastationServiceChannelNode>();
        var coveredNodeFootprints = new List<Rect>();
        foreach (Vector2 point in points.Values.OrderBy(PointKey, StringComparer.Ordinal))
        {
            MegastationServiceChannelRun[] incident = runs.Where(run => Near(run.Start, point)
                || Near(run.End, point)).ToArray();
            if (incident.Length == 0) continue;
            MegastationServiceChannelNodeKind kind = ClassifyNode(point, incident);
            int nodeSeed = MegastationSeed.Derive(seed, $"node:{PointKey(point)}");
            MegastationServiceChannelNodeVariant variant = ChooseNodeVariant(kind, nodeSeed);
            bool mainAlongU = incident.Count(run => run.AlongU) >= 2;
            float housingWidth = 0f, housingLength = 0f, housingHeight = 0f;
            if (variant != MegastationServiceChannelNodeVariant.Exposed)
            {
                float channelWidth = incident.Max(run => run.Width);
                float channelDepth = incident.Max(run => run.ApparentDepth);
                (housingWidth, housingLength, housingHeight) = NodeDimensions(
                    variant, kind, channelWidth, channelDepth);
                float halfU = (mainAlongU ? housingLength : housingWidth) * .5f + 2.3f;
                float halfV = (mainAlongU ? housingWidth : housingLength) * .5f + 2.3f;
                var nodeFootprint = new Rect(point.X - halfU, point.X + halfU,
                    point.Y - halfV, point.Y + halfV);
                if (!IsFree(region, blockers, nodeFootprint, rejects)
                    || coveredNodeFootprints.Any(existing => Overlaps(nodeFootprint, existing, 0f)))
                {
                    variant = MegastationServiceChannelNodeVariant.Exposed;
                    housingWidth = housingLength = housingHeight = 0f;
                }
                else
                {
                    coveredNodeFootprints.Add(nodeFootprint);
                }
            }
            MegastationServiceChannelEndpoint? endpoint = kind == MegastationServiceChannelNodeKind.DeadEnd
                ? (Sample(nodeSeed, "endpoint") < .48f
                    ? MegastationServiceChannelEndpoint.UtilityHousing
                    : MegastationServiceChannelEndpoint.SealedCap)
                : null;
            nodes.Add(new($"{networkIdentity}/node:{PointKey(point)}", point, kind, variant,
                mainAlongU, housingWidth, housingLength, housingHeight,
                incident.Select(run => run.Identity).Order(StringComparer.Ordinal).ToArray(), endpoint));
        }
        return (runs, nodes.ToArray());
    }

    private static MegastationServiceChannelBridge[] PlanBridges(string networkIdentity,
        int seed, IReadOnlyList<MegastationServiceChannelRun> runs,
        IReadOnlyList<MegastationServiceChannelNode> nodes)
    {
        var result = new List<MegastationServiceChannelBridge>();
        foreach (MegastationServiceChannelRun run in runs.Where(r =>
                     r.Scale == MegastationServiceChannelRunScale.Primary && r.Length >= 95f)
                 .OrderBy(r => r.Identity, StringComparer.Ordinal))
        {
            int bridgeSeed = MegastationSeed.Derive(seed, $"bridge:{run.Identity}");
            if (Sample(bridgeSeed, "selected") >= .32f) continue;
            float position = Lerp(.30f, .70f, Sample(bridgeSeed, "position"));
            Vector2 centre = Vector2.Lerp(run.Start, run.End, position);
            if (nodes.Any(node => node.Variant != MegastationServiceChannelNodeVariant.Exposed
                    && Vector2.Distance(node.Position, centre)
                        < MathF.Max(node.HousingWidth, node.HousingLength) * .5f + 8f))
                continue;
            result.Add(new($"{networkIdentity}/bridge:{result.Count}", run.Identity,
                position, run.Width + 5f));
            if (result.Count >= 2) break;
        }
        return result.ToArray();
    }

    private static MegastationServiceChannelNodeKind ClassifyNode(Vector2 point,
        IReadOnlyList<MegastationServiceChannelRun> incident)
    {
        if (incident.Count == 1) return MegastationServiceChannelNodeKind.DeadEnd;
        bool horizontal = incident.Any(run => run.AlongU);
        bool vertical = incident.Any(run => !run.AlongU);
        return incident.Count >= 4 ? MegastationServiceChannelNodeKind.FourWay
            : incident.Count == 3 ? MegastationServiceChannelNodeKind.TJunction
            : horizontal && vertical ? MegastationServiceChannelNodeKind.Turn
            : MegastationServiceChannelNodeKind.Inline;
    }

    private static MegastationServiceChannelNodeVariant ChooseNodeVariant(
        MegastationServiceChannelNodeKind kind, int seed)
    {
        if (kind == MegastationServiceChannelNodeKind.FourWay)
            return Sample(seed, "four-way-variant") < .72f
                ? MegastationServiceChannelNodeVariant.HeavyDistribution
                : MegastationServiceChannelNodeVariant.ConverterHouse;
        if (kind != MegastationServiceChannelNodeKind.TJunction)
            return MegastationServiceChannelNodeVariant.Exposed;
        float variant = Sample(seed, "junction-node-variant");
        return variant < .45f ? MegastationServiceChannelNodeVariant.ConverterHouse
            : variant < .82f ? MegastationServiceChannelNodeVariant.SwitchingNode
            : MegastationServiceChannelNodeVariant.HeavyDistribution;
    }

    private static (float width, float length, float height) NodeDimensions(
        MegastationServiceChannelNodeVariant variant, MegastationServiceChannelNodeKind kind,
        float channelWidth, float channelDepth)
    {
        (float acrossScale, float alongScale, float heightScale) = variant switch
        {
            MegastationServiceChannelNodeVariant.SwitchingNode => (1.14f, 1.42f, .92f),
            MegastationServiceChannelNodeVariant.HeavyDistribution => (1.42f, 1.88f, 1.28f),
            _ => (1.26f, 1.68f, 1.05f),
        };
        if (kind == MegastationServiceChannelNodeKind.FourWay)
        {
            float symmetric = MathF.Max(acrossScale, 1.48f);
            acrossScale = symmetric;
            alongScale = symmetric;
        }
        return (channelWidth * acrossScale, channelWidth * alongScale,
            Math.Clamp((channelDepth * 1.08f + 2.8f) * heightScale, 4.4f, 9.2f));
    }

    private static bool TryIntersection(RawLeg a, RawLeg b, out Vector2 point)
    {
        point = default;
        bool aHorizontal = MathF.Abs(a.End.X - a.Start.X) >= MathF.Abs(a.End.Y - a.Start.Y);
        bool bHorizontal = MathF.Abs(b.End.X - b.Start.X) >= MathF.Abs(b.End.Y - b.Start.Y);
        if (aHorizontal == bHorizontal) return false;
        RawLeg h = aHorizontal ? a : b, v = aHorizontal ? b : a;
        point = new(v.Start.X, h.Start.Y);
        return Between(point.X, h.Start.X, h.End.X) && Between(point.Y, v.Start.Y, v.End.Y);
    }

    private static bool ParallelOverlapTooClose(RawLeg a, RawLeg b)
    {
        bool aAlongU = MathF.Abs(a.End.X - a.Start.X) >= MathF.Abs(a.End.Y - a.Start.Y);
        bool bAlongU = MathF.Abs(b.End.X - b.Start.X) >= MathF.Abs(b.End.Y - b.Start.Y);
        if (aAlongU != bAlongU)
            return false;
        float aCross = aAlongU ? a.Start.Y : a.Start.X;
        float bCross = bAlongU ? b.Start.Y : b.Start.X;
        if (MathF.Abs(aCross - bCross) > MathF.Max(a.Width, b.Width))
            return false;
        float a0 = aAlongU ? MathF.Min(a.Start.X, a.End.X) : MathF.Min(a.Start.Y, a.End.Y);
        float a1 = aAlongU ? MathF.Max(a.Start.X, a.End.X) : MathF.Max(a.Start.Y, a.End.Y);
        float b0 = bAlongU ? MathF.Min(b.Start.X, b.End.X) : MathF.Min(b.Start.Y, b.End.Y);
        float b1 = bAlongU ? MathF.Max(b.Start.X, b.End.X) : MathF.Max(b.Start.Y, b.End.Y);
        return MathF.Min(a1, b1) - MathF.Max(a0, b0) > .01f;
    }

    private static bool OnSegment(Vector2 p, Vector2 a, Vector2 b)
        => MathF.Abs((p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X)) < .01f
            && Between(p.X, a.X, b.X) && Between(p.Y, a.Y, b.Y);
    private static bool Between(float value, float a, float b)
        => value >= MathF.Min(a, b) - .01f && value <= MathF.Max(a, b) + .01f;
    private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) < .01f;
    private static string PointKey(Vector2 p) => $"{p.X.ToString("F3", CultureInfo.InvariantCulture)},{p.Y.ToString("F3", CultureInfo.InvariantCulture)}";
    private static string SegmentKey(Vector2 a, Vector2 b)
        => string.CompareOrdinal(PointKey(a), PointKey(b)) <= 0
            ? $"{PointKey(a)}>{PointKey(b)}" : $"{PointKey(b)}>{PointKey(a)}";
    private static bool Coplanar(MegastationPlanarRegion region, Vector3 normal, float plane)
        => Vector3.Dot(region.OutwardNormal, normal) > .999f
            && MathF.Abs(region.PlaneCoordinateMetres - plane) < .2f;
    private static Rect PointRect(MegastationPlanarRegion region, Vector3 point, float radius)
    {
        float u = Vector3.Dot(point, region.TangentU), v = Vector3.Dot(point, region.TangentV);
        return new(u - radius, u + radius, v - radius, v + radius);
    }
    private static Rect Project(MegastationPlanarRegion region, IReadOnlyList<Vector3> points)
    {
        float[] u = points.Select(p => Vector3.Dot(p, region.TangentU)).ToArray();
        float[] v = points.Select(p => Vector3.Dot(p, region.TangentV)).ToArray();
        return new(u.Min(), u.Max(), v.Min(), v.Max());
    }
    private static bool Overlaps(Rect a, Rect b, float margin)
        => a.MinU < b.MaxU + margin && a.MaxU > b.MinU - margin
            && a.MinV < b.MaxV + margin && a.MaxV > b.MinV - margin;
    private static float RoleDensity(MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Industrial => .34f,
        MegastationZoneRole.Utilities => .39f,
        MegastationZoneRole.Logistics => .18f,
        MegastationZoneRole.Habitation => .035f,
        MegastationZoneRole.Strategic => .025f,
        _ => 0f,
    };
    private static bool IsChannelRich(MegastationPlanarRegion region, int seed)
    {
        float chance = region.ZoneRole switch
        {
            MegastationZoneRole.Utilities => .78f,
            MegastationZoneRole.Industrial => .68f,
            MegastationZoneRole.Logistics => .36f,
            _ => .12f,
        };
        chance += Math.Clamp(region.PhysicalArea / 180_000f, 0f, .16f);
        return Sample(seed, "network-density") < chance;
    }
    private static int CableCount(int seed, string key)
        => 4 + (int)((uint)MegastationSeed.Derive(seed, key) % 5u);
    private static float Sample(int seed, string key)
        => (uint)MegastationSeed.Derive(seed, key) / (float)uint.MaxValue;
    private static float Signed(int seed, string key) => Sample(seed, key) * 2f - 1f;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float Min(float[] values) => values.Length == 0 ? 0f : values[0];
    private static float Median(float[] values) => values.Length == 0 ? 0f : values[values.Length / 2];
    private static float Max(float[] values) => values.Length == 0 ? 0f : values[^1];

    private static string Signature(IEnumerable<MegastationServiceChannelNetwork> networks)
    {
        var text = new StringBuilder("service-channels:sc2\n");
        foreach (MegastationServiceChannelNetwork network in networks)
        {
            text.Append(network.Identity).Append('|').Append(network.Seed).Append('\n');
            foreach (MegastationServiceChannelRun run in network.Runs)
                text.Append(run.Identity).Append('|').Append(run.RouteIdentity).Append('|')
                    .Append(run.Scale).Append('|').Append(PointKey(run.Start)).Append('|')
                    .Append(PointKey(run.End)).Append('|')
                    .Append(run.Width.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(run.ApparentDepth.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(run.CableCount).Append('\n');
            foreach (MegastationServiceChannelNode node in network.Nodes)
                text.Append(node.Identity).Append('|').Append(node.Kind).Append('|')
                    .Append(node.Variant).Append('|').Append(node.Endpoint).Append('|')
                    .Append(node.MainAlongU).Append('|')
                    .Append(node.HousingWidth.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(node.HousingLength.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(node.HousingHeight.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .AppendJoin(',', node.IncidentRunIdentities).Append('\n');
            foreach (MegastationServiceChannelBridge bridge in network.Bridges)
                text.Append(bridge.Identity).Append('|').Append(bridge.RunIdentity).Append('|')
                    .Append(bridge.PositionAlongRun.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public static class MegastationServiceChannelMeshBuilder
{
    private readonly record struct Frame(Vector3 Origin, Vector3 Normal, Vector3 Across, Vector3 Along);
    private readonly record struct NodeGeometryStats(
        int VisibleVertices, int VisibleTriangles, int ShadowVertices, int ShadowTriangles)
    {
        public static NodeGeometryStats operator +(NodeGeometryStats a, NodeGeometryStats b)
            => new(a.VisibleVertices + b.VisibleVertices,
                a.VisibleTriangles + b.VisibleTriangles,
                a.ShadowVertices + b.ShadowVertices,
                a.ShadowTriangles + b.ShadowTriangles);
    }

    public static MegastationServiceChannelMeshBuildResult Build(
        MegastationServiceChannelPlan plan,
        MegastationSystemMaterialAssignment? materialAssignment,
        CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();
        var mesh = new StationModuleMesh();
        NodeGeometryStats nodeGeometry = default;
        foreach (MegastationServiceChannelNetwork network in plan.Networks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodeGeometry += EmitNetwork(network, mesh, materialAssignment);
        }
        mesh.ApplyIlluminationFlags();
        StationMeshCpuData? shadow = mesh.PrepareIndexRanges(mesh.DecorClassRanges
            .Where(r => r.decorClass == DecorClass.MegastationServiceChannelMajor)
            .Select(r => (r.indexStart, r.indexCount)).ToArray());
        var materialGroups = mesh.PrepareMaterialGroups();
        timer.Stop();
        int sv = shadow?.Vertices.Length ?? 0, si = shadow?.Indices.Length ?? 0;
        return new(mesh, plan.Diagnostics with
        {
            VisibleVertexCount = mesh.VertexCount,
            VisibleTriangleCount = mesh.IndexCount / 3,
            VisibleMeshBytes = Bytes(mesh.VertexCount, mesh.IndexCount),
            ShadowVertexCount = sv,
            ShadowTriangleCount = si / 3,
            ShadowMeshBytes = Bytes(sv, si),
            CoveredNodeVisibleVertexCount = nodeGeometry.VisibleVertices,
            CoveredNodeVisibleTriangleCount = nodeGeometry.VisibleTriangles,
            CoveredNodeShadowVertexCount = nodeGeometry.ShadowVertices,
            CoveredNodeShadowTriangleCount = nodeGeometry.ShadowTriangles,
            MeshBuildMilliseconds = timer.ElapsedMilliseconds,
            OwnedTextureDelta = 0,
            GpuBufferDelta = mesh.IsEmpty ? 0 : 4,
            MaterialRangeCount = materialGroups?.Ranges.Count ?? 0,
        });
    }

    private static NodeGeometryStats EmitNetwork(MegastationServiceChannelNetwork network,
        StationModuleMesh mesh, MegastationSystemMaterialAssignment? assignment)
    {
        Color dominant = assignment?.Palette.DominantTint ?? new Color(70, 73, 72);
        Color secondary = assignment?.Palette.SecondaryTint ?? new Color(91, 91, 84);
        Color accent = assignment?.Palette.AccentTint ?? new Color(112, 105, 75);
        Color floor = ProceduralMaterialCpuGenerator.ShiftLuminance(dominant, -58f);
        Color structure = ProceduralMaterialCpuGenerator.ShiftLuminance(dominant, -10f);
        Color internalColour = ProceduralMaterialCpuGenerator.Blend(
            ProceduralMaterialCpuGenerator.ShiftLuminance(secondary, -24f), accent, .20f);
        NodeGeometryStats nodeGeometry = default;
        foreach (MegastationServiceChannelRun run in network.Runs)
        {
            MegastationServiceChannelNode startNode = FindEndpointNode(network, run, run.Start);
            MegastationServiceChannelNode endNode = FindEndpointNode(network, run, run.End);
            EmitRun(network, run, startNode, endNode, mesh, floor, structure, internalColour);
        }
        foreach (MegastationServiceChannelNode node in network.Nodes)
            nodeGeometry += EmitNode(network, node, mesh, floor, structure,
                secondary, internalColour, accent);
        foreach (MegastationServiceChannelBridge bridge in network.Bridges)
        {
            MegastationServiceChannelRun run = network.Runs.Single(r => r.Identity == bridge.RunIdentity);
            EmitBridge(network, run, bridge, mesh, structure, secondary);
        }
        return nodeGeometry;
    }

    private static MegastationServiceChannelNode FindEndpointNode(
        MegastationServiceChannelNetwork network, MegastationServiceChannelRun run,
        Vector2 endpoint)
        => network.Nodes.Single(node => node.IncidentRunIdentities.Contains(run.Identity)
            && node.Position == endpoint);

    private static void EmitRun(MegastationServiceChannelNetwork network,
        MegastationServiceChannelRun run, MegastationServiceChannelNode startNode,
        MegastationServiceChannelNode endNode, StationModuleMesh mesh,
        Color floor, Color structure, Color internalColour)
    {
        Vector2 direction2 = Vector2.Normalize(run.End - run.Start);
        float trimStart = startNode.Kind == MegastationServiceChannelNodeKind.DeadEnd ? 0f : run.Width * .5f;
        float trimEnd = endNode.Kind == MegastationServiceChannelNodeKind.DeadEnd ? 0f : run.Width * .5f;
        Vector2 bodyStart = run.Start + direction2 * trimStart;
        Vector2 bodyEnd = run.End - direction2 * trimEnd;
        if (Vector2.Distance(bodyStart, bodyEnd) < 1f) return;
        Frame frame = FrameAt(network, Vector2.Lerp(bodyStart, bodyEnd, .5f), direction2);
        float bodyLength = Vector2.Distance(bodyStart, bodyEnd);
        float lipWidth = Math.Clamp(run.Width * .115f, 1.15f, 2.6f);

        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMinor;
        Box(mesh, frame, 0f, 0f, .075f, run.Width - lipWidth * 1.25f,
            bodyLength, .12f, floor);
        mesh.BreakDecorClassRange();
        SetMaterial(mesh, SystemMaterialFamilyId.DullStructuralMetal);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMajor;
        float lipOffset = run.Width * .5f - lipWidth * .5f;
        Box(mesh, frame, -lipOffset, 0f, run.ApparentDepth * .5f,
            lipWidth, bodyLength, run.ApparentDepth, structure);
        Box(mesh, frame, lipOffset, 0f, run.ApparentDepth * .5f,
            lipWidth, bodyLength, run.ApparentDepth, structure);

        mesh.BreakDecorClassRange();
        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMinor;
        Frame cableFrame = FrameAt(network, Vector2.Lerp(run.Start, run.End, .5f), direction2);
        float usableWidth = run.Width - lipWidth * 3f;
        for (int cable = 0; cable < run.CableCount; cable++)
        {
            float across = run.CableCount == 1 ? 0f
                : (-.5f + cable / (float)(run.CableCount - 1)) * usableWidth;
            float radius = .22f + .08f * Sample(run.Identity, $"radius:{cable}");
            Vector3 start = Point(network, run.Start) + cableFrame.Across * across
                + network.Normal * (.25f + radius);
            Vector3 end = Point(network, run.End) + cableFrame.Across * across
                + network.Normal * (.25f + radius);
            mesh.AddPrismPipe(start, end, radius, 6, internalColour, true, true);
        }
    }

    private static NodeGeometryStats EmitNode(MegastationServiceChannelNetwork network,
        MegastationServiceChannelNode node, StationModuleMesh mesh,
        Color floor, Color structure, Color secondary, Color technical, Color accent)
    {
        MegastationServiceChannelRun[] incident = network.Runs
            .Where(run => node.IncidentRunIdentities.Contains(run.Identity)).ToArray();
        float width = incident.Max(run => run.Width);
        float depth = incident.Max(run => run.ApparentDepth);
        Vector2 direction = Vector2.Normalize((Near(incident[0].Start, node.Position)
            ? incident[0].End : incident[0].Start) - node.Position);
        Frame frame = FrameAt(network, node.Position, direction);
        if (node.Kind == MegastationServiceChannelNodeKind.DeadEnd)
        {
            EmitEndpoint(mesh, frame, width, depth,
                node.Endpoint ?? MegastationServiceChannelEndpoint.SealedCap, structure, secondary);
            return default;
        }
        if (node.Variant != MegastationServiceChannelNodeVariant.Exposed
            && node.Kind is MegastationServiceChannelNodeKind.TJunction
                or MegastationServiceChannelNodeKind.FourWay)
        {
            return EmitCoveredNode(network, node, incident, mesh,
                structure, secondary, technical, accent);
        }

        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMinor;
        Box(mesh, frame, 0f, 0f, .075f, width, width, .12f, floor);
        mesh.BreakDecorClassRange();
        SetMaterial(mesh, SystemMaterialFamilyId.DullStructuralMetal);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMajor;
        float corner = width * .40f, size = Math.Clamp(width * .16f, 1.4f, 3.2f);
        Box(mesh, frame, -corner, -corner, depth * .5f, size, size, depth, structure);
        Box(mesh, frame, corner, -corner, depth * .5f, size, size, depth, structure);
        Box(mesh, frame, -corner, corner, depth * .5f, size, size, depth, structure);
        Box(mesh, frame, corner, corner, depth * .5f, size, size, depth, structure);
        if (node.Kind is MegastationServiceChannelNodeKind.Turn
            or MegastationServiceChannelNodeKind.TJunction
            or MegastationServiceChannelNodeKind.FourWay)
        {
            float roofThickness = Math.Clamp(width * .045f, .45f, .90f);
            float roofSpan = corner * 2f;
            // Roof corners meet the pier centres; half the roof thickness is recessed
            // below the pier tops so their outer corners remain visible from above.
            Box(mesh, frame, 0f, 0f, depth, roofSpan, roofSpan,
                roofThickness, secondary);
        }
        return default;
    }

    private static NodeGeometryStats EmitCoveredNode(
        MegastationServiceChannelNetwork network, MegastationServiceChannelNode node,
        IReadOnlyList<MegastationServiceChannelRun> incident, StationModuleMesh mesh,
        Color structure, Color secondary, Color technical, Color accent)
    {
        Vector2 mainDirection = node.MainAlongU ? Vector2.UnitX : Vector2.UnitY;
        Frame frame = FrameAt(network, node.Position, mainDirection);
        float channelWidth = incident.Max(run => run.Width);
        float bodyWidth = node.HousingWidth;
        float bodyLength = node.HousingLength;
        float bodyHeight = node.HousingHeight;
        int visibleVertices = 0, visibleTriangles = 0;
        int shadowVertices = 0, shadowTriangles = 0;

        void AddNodeBox(SystemMaterialFamilyId material, DecorClass decorClass,
            float across, float along, float height, float width, float length,
            float thickness, Color colour)
        {
            mesh.BreakDecorClassRange();
            SetMaterial(mesh, material);
            mesh.CurrentDecorClass = decorClass;
            int beforeVertices = mesh.VertexCount, beforeIndices = mesh.IndexCount;
            Box(mesh, frame, across, along, height, width, length, thickness, colour);
            int addedVertices = mesh.VertexCount - beforeVertices;
            int addedTriangles = (mesh.IndexCount - beforeIndices) / 3;
            visibleVertices += addedVertices;
            visibleTriangles += addedTriangles;
            if (decorClass == DecorClass.MegastationServiceChannelMajor)
            {
                shadowVertices += addedVertices;
                shadowTriangles += addedTriangles;
            }
            mesh.BreakDecorClassRange();
        }

        Color bodyColour = node.Variant == MegastationServiceChannelNodeVariant.SwitchingNode
            ? ProceduralMaterialCpuGenerator.ShiftLuminance(technical, -10f)
            : secondary;
        AddNodeBox(SystemMaterialFamilyId.HeavyIndustrialPlate,
            DecorClass.MegastationServiceChannelMajor,
            0f, 0f, .22f + bodyHeight * .5f, bodyWidth, bodyLength,
            bodyHeight, bodyColour);

        foreach (Vector2 arm in incident.Select(run => Vector2.Normalize(
                     (Near(run.Start, node.Position) ? run.End : run.Start) - node.Position))
                 .GroupBy(direction => (MathF.Abs(direction.X) > .5f,
                     MathF.Sign(MathF.Abs(direction.X) > .5f ? direction.X : direction.Y)))
                 .Select(group => group.First()))
        {
            float alongComponent = Vector3.Dot(
                network.TangentU * arm.X + network.TangentV * arm.Y, frame.Along);
            float acrossComponent = Vector3.Dot(
                network.TangentU * arm.X + network.TangentV * arm.Y, frame.Across);
            if (MathF.Abs(alongComponent) > .5f)
                AddNodeBox(SystemMaterialFamilyId.DullStructuralMetal,
                    DecorClass.MegastationServiceChannelMajor,
                    0f, MathF.Sign(alongComponent) * (bodyLength * .5f + 1.1f),
                    bodyHeight * .38f, channelWidth * .88f, 2.2f,
                    bodyHeight * .68f, structure);
            else
                AddNodeBox(SystemMaterialFamilyId.DullStructuralMetal,
                    DecorClass.MegastationServiceChannelMajor,
                    MathF.Sign(acrossComponent) * (bodyWidth * .5f + 1.1f), 0f,
                    bodyHeight * .38f, 2.2f, channelWidth * .88f,
                    bodyHeight * .68f, structure);
        }

        int banks = node.Variant == MegastationServiceChannelNodeVariant.HeavyDistribution
            ? 3 : 2;
        for (int bank = 0; bank < banks; bank++)
        {
            float t = banks == 1 ? 0f : (-.5f + bank / (float)(banks - 1));
            float bankWidth = bodyWidth * (node.Variant == MegastationServiceChannelNodeVariant.SwitchingNode
                ? .22f : .25f);
            float bankLength = bodyLength * (.30f + Sample(node.Identity, $"bank-length:{bank}") * .12f);
            float bankHeight = 1.25f + Sample(node.Identity, $"bank-height:{bank}") * 1.9f;
            AddNodeBox(SystemMaterialFamilyId.CleanTechnicalAlloy,
                DecorClass.MegastationServiceChannelMajor,
                t * bodyWidth * .52f, 0f, bodyHeight + bankHeight * .5f,
                bankWidth, bankLength, bankHeight,
                bank == banks - 1 ? accent : technical);
        }

        AddNodeBox(SystemMaterialFamilyId.PaintedCoatedMetal,
            DecorClass.MegastationServiceChannelMinor,
            0f, -bodyLength * .18f, bodyHeight + 1.1f,
            bodyWidth * .56f, .45f, .32f,
            ProceduralMaterialCpuGenerator.ShiftLuminance(structure, -34f));
        return new(visibleVertices, visibleTriangles, shadowVertices, shadowTriangles);
    }

    private static void EmitEndpoint(StationModuleMesh mesh, Frame frame, float width,
        float depth, MegastationServiceChannelEndpoint endpoint, Color structure, Color secondary)
    {
        SetMaterial(mesh, SystemMaterialFamilyId.DullStructuralMetal);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMajor;
        if (endpoint == MegastationServiceChannelEndpoint.SealedCap)
        {
            Box(mesh, frame, 0f, 0f, depth * .5f, width, 1.8f, depth, structure);
            return;
        }
        float length = Math.Clamp(width * .48f, 5f, 10f);
        Box(mesh, frame, 0f, 0f, depth * .72f, width * .72f, length,
            depth * 1.44f, secondary);
        Box(mesh, frame, 0f, 0f, depth * 1.48f, width * .52f, length * .72f,
            .35f, structure);
    }

    private static void EmitBridge(MegastationServiceChannelNetwork network,
        MegastationServiceChannelRun run, MegastationServiceChannelBridge bridge,
        StationModuleMesh mesh, Color structure, Color deckColour)
    {
        Vector2 direction = Vector2.Normalize(run.End - run.Start);
        Vector2 position = Vector2.Lerp(run.Start, run.End, bridge.PositionAlongRun);
        Frame frame = FrameAt(network, position, direction);
        float lipWidth = Math.Clamp(run.Width * .115f, 1.15f, 2.6f);
        float span = bridge.DeckWidth + lipWidth * 1.4f;
        float deckLength = 5.5f, deckHeight = run.ApparentDepth + 1.35f;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        mesh.CurrentDecorClass = DecorClass.MegastationServiceChannelMajor;
        Box(mesh, frame, 0f, 0f, deckHeight, span, deckLength, .55f, deckColour);
        Box(mesh, frame, 0f, -deckLength * .43f, deckHeight + .65f,
            span, .38f, 1.15f, structure);
        Box(mesh, frame, 0f, deckLength * .43f, deckHeight + .65f,
            span, .38f, 1.15f, structure);
        float abutment = run.Width * .5f + lipWidth;
        Box(mesh, frame, -abutment, 0f, deckHeight * .5f,
            lipWidth * 1.5f, deckLength + 1.8f, deckHeight, structure);
        Box(mesh, frame, abutment, 0f, deckHeight * .5f,
            lipWidth * 1.5f, deckLength + 1.8f, deckHeight, structure);
        Box(mesh, frame, 0f, 0f, deckHeight - .65f, span * .88f, .75f, .75f, structure);
    }

    private static Frame FrameAt(MegastationServiceChannelNetwork network,
        Vector2 position, Vector2 direction)
    {
        Vector3 along = Vector3.Normalize(network.TangentU * direction.X
            + network.TangentV * direction.Y);
        Vector3 across = Vector3.Normalize(Vector3.Cross(along, network.Normal));
        return new(Point(network, position), network.Normal, across, along);
    }
    private static Vector3 Point(MegastationServiceChannelNetwork network, Vector2 p)
        => network.Normal * network.PlaneCoordinateMetres
            + network.TangentU * p.X + network.TangentV * p.Y;
    private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) < .01f;
    private static float Sample(string identity, string key)
        => (uint)MegastationSeed.Derive(MegastationSeed.Root(identity, 1), key) / (float)uint.MaxValue;
    private static void SetMaterial(StationModuleMesh mesh, SystemMaterialFamilyId family)
    {
        mesh.CurrentMaterialFamily = family;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(family).TileSizeMeters;
    }
    private static void Box(StationModuleMesh mesh, Frame frame, float across, float along,
        float height, float width, float length, float thickness, Color colour)
    {
        Vector3 centre = frame.Origin + frame.Across * across + frame.Along * along
            + frame.Normal * height;
        mesh.AddOrientedBox(new Matrix(
            frame.Across.X, frame.Across.Y, frame.Across.Z, 0f,
            frame.Along.X, frame.Along.Y, frame.Along.Z, 0f,
            frame.Normal.X, frame.Normal.Y, frame.Normal.Z, 0f,
            centre.X, centre.Y, centre.Z, 1f), new(width, length, thickness), colour);
    }
    private static long Bytes(int vertices, int indices) =>
        (long)vertices * Inferior.Rendering.VertexPositionNormalColorTexture.VertexDeclaration.VertexStride
        + (long)indices * 4L;
}

internal static class MegastationServiceChannelDebug
{
    public static VertexPositionColor[] BuildLines(MegastationServiceChannelPlan plan)
    {
        var lines = new List<VertexPositionColor>();
        foreach (MegastationServiceChannelDebugRun run in plan.DebugRuns)
        {
            Vector3 Point(Vector2 p) => run.Normal * (run.PlaneCoordinateMetres + .25f)
                + run.TangentU * p.X + run.TangentV * p.Y;
            Color colour = run.Scale == MegastationServiceChannelRunScale.Primary
                ? Color.DeepSkyBlue : Color.Cyan;
            lines.Add(new(Point(run.Start), colour));
            lines.Add(new(Point(run.End), colour));
        }
        foreach (MegastationServiceChannelNetwork network in plan.Networks)
        foreach (MegastationServiceChannelNode node in network.Nodes)
        {
            Color colour = node.Kind switch
            {
                MegastationServiceChannelNodeKind.Turn => Color.Yellow,
                MegastationServiceChannelNodeKind.TJunction => Color.Orange,
                MegastationServiceChannelNodeKind.FourWay => Color.Magenta,
                MegastationServiceChannelNodeKind.DeadEnd => Color.White,
                _ => Color.Gray,
            };
            Vector3 centre = network.Normal * (network.PlaneCoordinateMetres + .35f)
                + network.TangentU * node.Position.X + network.TangentV * node.Position.Y;
            lines.Add(new(centre - network.TangentU * 2f, colour));
            lines.Add(new(centre + network.TangentU * 2f, colour));
            lines.Add(new(centre - network.TangentV * 2f, colour));
            lines.Add(new(centre + network.TangentV * 2f, colour));
        }
        return lines.ToArray();
    }
}
