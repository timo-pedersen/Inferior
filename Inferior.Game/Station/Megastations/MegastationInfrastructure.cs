using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationInfrastructureFamily
{
    MachineryHousing,
    Ventilation,
    Tank,
}

public enum MegastationInfrastructureArchetype
{
    ServiceCluster,
    TankServiceCluster,
    IndustrialPlant,
    UtilityNode,
}

public enum MegastationShadowPolicy
{
    None,
    Simplified,
    ConditionalSubstantial,
}

public sealed record MegastationShadowFamilyDiagnostics(
    string Family,
    MegastationShadowPolicy Policy,
    int VisibleInstanceCount,
    int ShadowCastingInstanceCount,
    int VisibleTriangleCount,
    int CasterVertexCount,
    int CasterTriangleCount);

public sealed record MegastationInfrastructureSurface(
    MegastationPlanarRegion Region,
    float TopologySuitability);

public sealed record MegastationInfrastructureInstance(
    string Identity,
    string ClusterIdentity,
    MegastationInfrastructureFamily Family,
    int Variant,
    Vector3 SurfacePosition,
    Vector3 Normal,
    Vector3 TangentU,
    Vector3 TangentV,
    float Width,
    float Height,
    float Depth,
    Color PrimaryColour,
    Color SecondaryColour,
    bool CastsShadow);

public sealed record MegastationInfrastructureCluster(
    string Identity,
    string SurfaceStableId,
    string ZoneId,
    MegastationZoneRole ZoneRole,
    int CellU,
    int CellV,
    MegastationInfrastructureArchetype Archetype,
    Vector3 SurfacePosition,
    Vector3 Normal,
    Vector3 TangentU,
    Vector3 TangentV,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV,
    Vector3 AabbMin,
    Vector3 AabbMax,
    IReadOnlyList<MegastationInfrastructureInstance> Instances);

public sealed record MegastationInfrastructureRoleDiagnostics(
    int ClusterCount,
    int HousingCount,
    int VentCount,
    int TankCount);

public sealed record MegastationInfrastructureDiagnostics(
    float CandidateArea,
    float ActiveArea,
    int CandidateRegionCount,
    int CandidateCellCount,
    int ClusterCount,
    int PrimitiveCount,
    int HousingCount,
    int VentCount,
    int TankCount,
    int ExactMaskRejectCount,
    int G1RejectCount,
    int WindowRejectCount,
    int LightRejectCount,
    int SpacingRejectCount,
    int TopologyUnsuitableCount,
    int RoleDensityRejectCount,
    int StationCapRejectCount,
    int ZoneCapRejectCount,
    long PlanningMilliseconds,
    int VisibleVertexCount,
    int VisibleTriangleCount,
    long VisibleMeshBytes,
    int ShadowVertexCount,
    int ShadowTriangleCount,
    long ShadowMeshBytes,
    long MeshBuildMilliseconds,
    IReadOnlyList<MegastationShadowFamilyDiagnostics> ShadowByFamily,
    IReadOnlyDictionary<MegastationZoneRole, MegastationInfrastructureRoleDiagnostics> ByRole);

public sealed record MegastationInfrastructurePlan(
    IReadOnlyList<MegastationInfrastructureSurface> Surfaces,
    IReadOnlyList<MegastationInfrastructureCluster> Clusters,
    IReadOnlyList<MegastationInfrastructureInstance> Instances,
    MegastationInfrastructureDiagnostics Diagnostics);

public sealed record MegastationInfrastructureMeshBuildResult(
    StationModuleMesh Mesh,
    MegastationInfrastructureDiagnostics Diagnostics);

internal sealed record MegastationInfrastructureTuning(
    float CellSizeMetres,
    float CellJitterMetres,
    float FootprintMarginMetres,
    float MinimumClusterSeparationMetres,
    float WindowMarginMetres,
    float LightExclusionRadiusMetres,
    int StationClusterCap,
    int ZoneClusterCap,
    IReadOnlyDictionary<MegastationZoneRole, int> RoleCaps,
    IReadOnlyDictionary<MegastationZoneRole, float> RoleDensity)
{
    public static MegastationInfrastructureTuning Default { get; } = new(
        CellSizeMetres: 96f,
        CellJitterMetres: 18f,
        FootprintMarginMetres: 1f,
        MinimumClusterSeparationMetres: 52f,
        WindowMarginMetres: 0.75f,
        LightExclusionRadiusMetres: 2f,
        StationClusterCap: 280,
        ZoneClusterCap: 36,
        RoleCaps: new Dictionary<MegastationZoneRole, int>
        {
            [MegastationZoneRole.Structural] = 0,
            [MegastationZoneRole.Habitation] = 8,
            [MegastationZoneRole.Industrial] = 120,
            [MegastationZoneRole.Logistics] = 36,
            [MegastationZoneRole.Utilities] = 130,
            [MegastationZoneRole.Strategic] = 5,
        },
        RoleDensity: new Dictionary<MegastationZoneRole, float>
        {
            [MegastationZoneRole.Structural] = 0f,
            [MegastationZoneRole.Habitation] = 0.025f,
            [MegastationZoneRole.Industrial] = 0.56f,
            [MegastationZoneRole.Logistics] = 0.19f,
            [MegastationZoneRole.Utilities] = 0.46f,
            [MegastationZoneRole.Strategic] = 0.015f,
        });
}

public static class MegastationInfrastructurePlanner
{
    private const string AlgorithmKey = "infrastructure:v1";

    private sealed record Candidate(
        string Identity,
        int Seed,
        MegastationPlanarRegion Region,
        int CellU,
        int CellV,
        float U,
        float V,
        float Priority,
        float MinU,
        float MaxU,
        float MinV,
        float MaxV,
        float MaximumOutwardDepth,
        MegastationInfrastructureArchetype Archetype,
        Vector3 SurfacePosition,
        Vector3 AabbMin,
        Vector3 AabbMax);

    public static MegastationInfrastructurePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> planarRegions,
        MegastationAttachmentPlan attachmentPlan,
        MegastationWindowPlan windowPlan,
        MegastationLightPlan lightPlan,
        CancellationToken cancellationToken = default)
        => Plan(
            planarRegions,
            attachmentPlan,
            windowPlan,
            lightPlan,
            MegastationInfrastructureTuning.Default,
            cancellationToken);

    internal static MegastationInfrastructurePlan Plan(
        IReadOnlyList<MegastationPlanarRegion> planarRegions,
        MegastationAttachmentPlan attachmentPlan,
        MegastationWindowPlan windowPlan,
        MegastationLightPlan lightPlan,
        MegastationInfrastructureTuning tuning,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        MegastationInfrastructureSurface[] surfaces = planarRegions
            .Where(region => region.ZoneRole != MegastationZoneRole.Structural)
            .Select(region => new MegastationInfrastructureSurface(
                region, TopologySuitability(region)))
            .OrderBy(surface => surface.Region.StableId, StringComparer.Ordinal)
            .ToArray();
        var candidates = new List<Candidate>();
        int candidateCells = 0;
        int exactMaskRejects = 0;
        int topologyRejects = 0;
        int densityRejects = 0;

        foreach (MegastationInfrastructureSurface infrastructureSurface in surfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MegastationPlanarRegion region = infrastructureSurface.Region;
            if (infrastructureSurface.TopologySuitability < 0.12f)
            {
                topologyRejects++;
                continue;
            }
            float cellSize = CellSizeForRole(tuning.CellSizeMetres, region.ZoneRole);
            float jitter = tuning.CellJitterMetres * (cellSize / tuning.CellSizeMetres);
            int firstU = (int)MathF.Floor(region.MinU / cellSize);
            int lastU = (int)MathF.Ceiling(region.MaxU / cellSize) - 1;
            int firstV = (int)MathF.Floor(region.MinV / cellSize);
            int lastV = (int)MathF.Ceiling(region.MaxV / cellSize) - 1;
            int infrastructureSeed = MegastationSeed.Derive(region.ZoneSeed, AlgorithmKey);
            int surfaceSeed = MegastationSeed.Derive(infrastructureSeed, region.StableId);

            for (int cellV = firstV; cellV <= lastV; cellV++)
            for (int cellU = firstU; cellU <= lastU; cellU++)
            {
                candidateCells++;
                int cellSeed = MegastationSeed.Derive(surfaceSeed, $"cell:{cellU}:{cellV}");
                float u = (cellU + 0.5f) * cellSize
                    + SignedSample(cellSeed, "jitter-u") * jitter;
                float v = (cellV + 0.5f) * cellSize
                    + SignedSample(cellSeed, "jitter-v") * jitter;
                MegastationInfrastructureArchetype archetype = SelectArchetype(
                    region.ZoneRole,
                    MegastationSeed.Derive(cellSeed, "archetype"));
                (float width, float height, float outwardDepth) = ClusterEnvelope(archetype);
                float minU = u - width * 0.5f;
                float maxU = u + width * 0.5f;
                float minV = v - height * 0.5f;
                float maxV = v + height * 0.5f;
                if (!MegastationPlanarRegionExtractor.ContainsFootprint(
                        region, minU, maxU, minV, maxV, tuning.FootprintMarginMetres))
                {
                    exactMaskRejects++;
                    continue;
                }
                float density = tuning.RoleDensity.GetValueOrDefault(region.ZoneRole);
                float g1Affinity = NearG1(region, u, v, attachmentPlan) ? 0.14f : 0f;
                float threshold = Math.Clamp(
                    density * infrastructureSurface.TopologySuitability + g1Affinity, 0f, 0.90f);
                if (Sample(cellSeed, "selected") >= threshold)
                {
                    densityRejects++;
                    continue;
                }

                Vector3 surfacePosition = region.OutwardNormal * region.PlaneCoordinateMetres
                    + region.TangentU * u + region.TangentV * v;
                (Vector3 aabbMin, Vector3 aabbMax) = WorldBounds(
                    surfacePosition,
                    region.OutwardNormal,
                    region.TangentU,
                    region.TangentV,
                    width,
                    height,
                    outwardDepth);
                string identity = $"{region.StableId}/{AlgorithmKey}/cell:{cellU}:{cellV}/cluster:0";
                candidates.Add(new(
                    identity,
                    MegastationSeed.Derive(cellSeed, "cluster:0"),
                    region,
                    cellU,
                    cellV,
                    u,
                    v,
                    infrastructureSurface.TopologySuitability * (0.75f + 0.25f * Sample(cellSeed, "priority")),
                    minU,
                    maxU,
                    minV,
                    maxV,
                    outwardDepth,
                    archetype,
                    surfacePosition,
                    aabbMin,
                    aabbMax));
            }
        }

        var accepted = new List<MegastationInfrastructureCluster>();
        var zoneCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var roleCounts = new Dictionary<MegastationZoneRole, int>();
        int g1Rejects = 0;
        int windowRejects = 0;
        int lightRejects = 0;
        int spacingRejects = 0;
        int stationCapRejects = 0;
        int zoneCapRejects = 0;

        foreach (Candidate candidate in candidates
                     .OrderByDescending(candidate => candidate.Priority)
                     .ThenBy(candidate => candidate.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted.Count >= tuning.StationClusterCap)
            {
                stationCapRejects++;
                continue;
            }
            int zoneCount = zoneCounts.GetValueOrDefault(candidate.Region.ZoneId);
            int roleCount = roleCounts.GetValueOrDefault(candidate.Region.ZoneRole);
            if (zoneCount >= tuning.ZoneClusterCap
                || roleCount >= tuning.RoleCaps.GetValueOrDefault(candidate.Region.ZoneRole))
            {
                zoneCapRejects++;
                continue;
            }
            if (OverlapsG1(candidate, attachmentPlan))
            {
                g1Rejects++;
                continue;
            }
            if (OverlapsWindow(candidate, windowPlan.Windows, tuning.WindowMarginMetres))
            {
                windowRejects++;
                continue;
            }
            if (OverlapsLight(candidate, lightPlan.Lights, tuning.LightExclusionRadiusMetres))
            {
                lightRejects++;
                continue;
            }
            if (accepted.Any(cluster => Collides(candidate, cluster,
                    SeparationForRole(tuning.MinimumClusterSeparationMetres, candidate.Region.ZoneRole))))
            {
                spacingRejects++;
                continue;
            }

            IReadOnlyList<MegastationInfrastructureInstance> instances = BuildClusterInstances(candidate);
            accepted.Add(new(
                candidate.Identity,
                candidate.Region.StableId,
                candidate.Region.ZoneId,
                candidate.Region.ZoneRole,
                candidate.CellU,
                candidate.CellV,
                candidate.Archetype,
                candidate.SurfacePosition,
                candidate.Region.OutwardNormal,
                candidate.Region.TangentU,
                candidate.Region.TangentV,
                candidate.MinU,
                candidate.MaxU,
                candidate.MinV,
                candidate.MaxV,
                candidate.AabbMin,
                candidate.AabbMax,
                instances));
            zoneCounts[candidate.Region.ZoneId] = zoneCount + 1;
            roleCounts[candidate.Region.ZoneRole] = roleCount + 1;
        }

        MegastationInfrastructureCluster[] orderedClusters = accepted
            .OrderBy(cluster => cluster.Identity, StringComparer.Ordinal)
            .ToArray();
        MegastationInfrastructureInstance[] orderedInstances = orderedClusters
            .SelectMany(cluster => cluster.Instances)
            .OrderBy(instance => instance.Identity, StringComparer.Ordinal)
            .ToArray();
        stopwatch.Stop();
        IReadOnlyDictionary<MegastationZoneRole, MegastationInfrastructureRoleDiagnostics> byRole =
            Enum.GetValues<MegastationZoneRole>().ToDictionary(
                role => role,
                role => new MegastationInfrastructureRoleDiagnostics(
                    orderedClusters.Count(cluster => cluster.ZoneRole == role),
                    orderedInstances.Count(instance => ClusterRole(orderedClusters, instance.ClusterIdentity) == role
                        && instance.Family == MegastationInfrastructureFamily.MachineryHousing),
                    orderedInstances.Count(instance => ClusterRole(orderedClusters, instance.ClusterIdentity) == role
                        && instance.Family == MegastationInfrastructureFamily.Ventilation),
                    orderedInstances.Count(instance => ClusterRole(orderedClusters, instance.ClusterIdentity) == role
                        && instance.Family == MegastationInfrastructureFamily.Tank)));
        var diagnostics = new MegastationInfrastructureDiagnostics(
            surfaces.Sum(surface => surface.Region.PhysicalArea),
            orderedClusters.Sum(cluster =>
                (cluster.MaxU - cluster.MinU) * (cluster.MaxV - cluster.MinV)),
            surfaces.Length,
            candidateCells,
            orderedClusters.Length,
            orderedInstances.Length,
            orderedInstances.Count(instance => instance.Family == MegastationInfrastructureFamily.MachineryHousing),
            orderedInstances.Count(instance => instance.Family == MegastationInfrastructureFamily.Ventilation),
            orderedInstances.Count(instance => instance.Family == MegastationInfrastructureFamily.Tank),
            exactMaskRejects,
            g1Rejects,
            windowRejects,
            lightRejects,
            spacingRejects,
            topologyRejects,
            densityRejects,
            stationCapRejects,
            zoneCapRejects,
            stopwatch.ElapsedMilliseconds,
            0, 0, 0, 0, 0, 0, 0, [],
            byRole);
        return new(surfaces, orderedClusters, orderedInstances, diagnostics);
    }

    public static float TopologySuitability(MegastationPlanarRegion region)
    {
        if (region.ZoneRole == MegastationZoneRole.Structural)
            return 0f;
        float sheltered = 0.24f
            + region.Concavity * 0.34f
            + region.RelativeDepth * 0.42f
            + (1f - region.Prominence) * 0.22f
            + (1f - region.Exposure) * 0.16f;
        float extremityPenalty = region.ZoneRole == MegastationZoneRole.Strategic
            ? region.Extremity * 0.04f
            : region.Extremity * 0.18f;
        float roleBias = region.ZoneRole switch
        {
            MegastationZoneRole.Industrial => 0.10f,
            MegastationZoneRole.Utilities => 0.08f,
            MegastationZoneRole.Logistics => 0.02f,
            MegastationZoneRole.Habitation => -0.08f,
            MegastationZoneRole.Strategic => -0.10f,
            _ => -1f,
        };
        return Math.Clamp(sheltered - extremityPenalty + roleBias, 0f, 1f);
    }

    private static MegastationInfrastructureArchetype SelectArchetype(
        MegastationZoneRole role,
        int seed)
    {
        float sample = Sample(seed, "choice");
        return role switch
        {
            MegastationZoneRole.Industrial => sample < 0.34f
                ? MegastationInfrastructureArchetype.TankServiceCluster
                : sample < 0.78f
                    ? MegastationInfrastructureArchetype.IndustrialPlant
                    : MegastationInfrastructureArchetype.ServiceCluster,
            MegastationZoneRole.Utilities => sample < 0.52f
                ? MegastationInfrastructureArchetype.UtilityNode
                : sample < 0.78f
                    ? MegastationInfrastructureArchetype.ServiceCluster
                    : MegastationInfrastructureArchetype.IndustrialPlant,
            MegastationZoneRole.Logistics => sample < 0.48f
                ? MegastationInfrastructureArchetype.ServiceCluster
                : sample < 0.78f
                    ? MegastationInfrastructureArchetype.TankServiceCluster
                    : MegastationInfrastructureArchetype.IndustrialPlant,
            MegastationZoneRole.Habitation => sample < 0.72f
                ? MegastationInfrastructureArchetype.ServiceCluster
                : MegastationInfrastructureArchetype.UtilityNode,
            _ => MegastationInfrastructureArchetype.ServiceCluster,
        };
    }

    private static (float Width, float Height, float OutwardDepth) ClusterEnvelope(
        MegastationInfrastructureArchetype archetype) => archetype switch
        {
            MegastationInfrastructureArchetype.ServiceCluster => (34f, 26f, 10f),
            MegastationInfrastructureArchetype.TankServiceCluster => (46f, 34f, 11f),
            MegastationInfrastructureArchetype.IndustrialPlant => (52f, 40f, 12f),
            _ => (40f, 30f, 10f),
        };

    private static IReadOnlyList<MegastationInfrastructureInstance> BuildClusterInstances(
        Candidate candidate)
    {
        var result = new List<MegastationInfrastructureInstance>();
        (Color primary, Color secondary) = Palette(candidate.Seed);
        int primitive = 0;

        void Add(
            MegastationInfrastructureFamily family,
            float offsetU,
            float offsetV,
            float width,
            float height,
            float depth,
            int variant,
            bool casts)
        {
            string identity = $"{candidate.Identity}/primitive:{primitive}:{family}";
            int seed = MegastationSeed.Derive(candidate.Seed, identity);
            float scale = 0.88f + Sample(seed, "scale") * 0.24f;
            Vector3 position = candidate.SurfacePosition
                + candidate.Region.TangentU * offsetU
                + candidate.Region.TangentV * offsetV;
            result.Add(new(
                identity,
                candidate.Identity,
                family,
                variant,
                position,
                candidate.Region.OutwardNormal,
                candidate.Region.TangentU,
                candidate.Region.TangentV,
                width * scale,
                height * scale,
                depth * scale,
                primary,
                secondary,
                casts));
            primitive++;
        }

        switch (candidate.Archetype)
        {
            case MegastationInfrastructureArchetype.ServiceCluster:
                Add(MegastationInfrastructureFamily.MachineryHousing, -7f, 0f, 12f, 9f, 7f,
                    candidate.Region.ZoneRole == MegastationZoneRole.Strategic ? 3 : 0, true);
                Add(MegastationInfrastructureFamily.MachineryHousing, 5f, 5f, 4.2f, 3.2f, 2.2f, 1, false);
                Add(MegastationInfrastructureFamily.MachineryHousing, 6f, -5f, 3.6f, 3.0f, 1.8f, 2, false);
                Add(MegastationInfrastructureFamily.Ventilation, 13f, 5f, 5.4f, 3.2f, 0.38f, 0, false);
                Add(MegastationInfrastructureFamily.Ventilation, 13f, 0f, 5.8f, 3.4f, 0.42f, 1, false);
                Add(MegastationInfrastructureFamily.Ventilation, 13f, -5f, 5.0f, 3.0f, 0.36f, 2, false);
                break;
            case MegastationInfrastructureArchetype.TankServiceCluster:
            {
                int tankCount = 2 + PositiveMod(MegastationSeed.Derive(candidate.Seed, "tank-count"), 2);
                float spacing = 9.0f;
                for (int tank = 0; tank < tankCount; tank++)
                    Add(MegastationInfrastructureFamily.Tank,
                        (tank - (tankCount - 1) * 0.5f) * spacing,
                        4f,
                        5.2f,
                        14f,
                        5.2f,
                        2 + tank % 3,
                        true);
                Add(MegastationInfrastructureFamily.MachineryHousing, -6f, -11f, 11f, 7f, 5.5f, 1, true);
                Add(MegastationInfrastructureFamily.MachineryHousing, 7f, -11f, 4.5f, 3.4f, 2.2f, 2, false);
                Add(MegastationInfrastructureFamily.Ventilation, 15f, -11f, 6.0f, 3.4f, 0.42f, 0, false);
                Add(MegastationInfrastructureFamily.Ventilation, 15f, -5.5f, 5.4f, 3.0f, 0.38f, 1, false);
                break;
            }
            case MegastationInfrastructureArchetype.IndustrialPlant:
                Add(MegastationInfrastructureFamily.MachineryHousing, -11f, 0f, 15f, 12f, 8f, 2, true);
                Add(MegastationInfrastructureFamily.MachineryHousing, 0f, -11f, 5.2f, 4.0f, 2.5f, 1, false);
                Add(MegastationInfrastructureFamily.MachineryHousing, 0f, 11f, 4.6f, 3.6f, 2.1f, 0, false);
                Add(MegastationInfrastructureFamily.Tank, 11f, -7f, 5.4f, 15f, 5.4f, 1, true);
                Add(MegastationInfrastructureFamily.Tank, 11f, 7f, 4.8f, 13f, 4.8f, 1, true);
                Add(MegastationInfrastructureFamily.Ventilation, 21f, -11f, 6.6f, 3.6f, 0.45f, 2, false);
                Add(MegastationInfrastructureFamily.Ventilation, 21f, 0f, 6.0f, 3.4f, 0.42f, 1, false);
                Add(MegastationInfrastructureFamily.Ventilation, 21f, 11f, 5.6f, 3.2f, 0.38f, 0, false);
                break;
            default:
                Add(MegastationInfrastructureFamily.MachineryHousing, -7f, 0f, 12f, 9f, 6.5f, 1, true);
                Add(MegastationInfrastructureFamily.MachineryHousing, 5f, 6f, 4.5f, 3.4f, 2.2f, 0, false);
                Add(MegastationInfrastructureFamily.MachineryHousing, 5f, -6f, 4.0f, 3.2f, 2.0f, 2, false);
                Add(MegastationInfrastructureFamily.Tank, 14f, 0f, 4.6f, 11f, 4.6f, 1, true);
                Add(MegastationInfrastructureFamily.Ventilation, 10f, 8f, 6.0f, 3.4f, 0.42f, 2, false);
                Add(MegastationInfrastructureFamily.Ventilation, 10f, -8f, 5.4f, 3.2f, 0.38f, 1, false);
                Add(MegastationInfrastructureFamily.Ventilation, 17f, 8f, 5.0f, 3.0f, 0.36f, 0, false);
                break;
        }
        return result;
    }

    private static bool OverlapsG1(Candidate candidate, MegastationAttachmentPlan plan)
    {
        foreach (MegastationAttachmentReservation reservation in plan.Reservations)
        {
            if (Vector3.Dot(reservation.Normal, candidate.Region.OutwardNormal) < 0.999f
                || MathF.Abs(reservation.PlaneCoordinateMetres - candidate.Region.PlaneCoordinateMetres) > 0.1f)
                continue;
            if (candidate.MinU < reservation.MaxU + 1f
                && candidate.MaxU > reservation.MinU - 1f
                && candidate.MinV < reservation.MaxV + 1f
                && candidate.MaxV > reservation.MinV - 1f)
                return true;
        }
        return plan.Placements.Any(placement => Intersects(
            candidate.AabbMin, candidate.AabbMax, placement.AabbMin, placement.AabbMax));
    }

    private static bool OverlapsWindow(
        Candidate candidate,
        IReadOnlyList<MegastationWindowInstance> windows,
        float margin)
    {
        foreach (MegastationWindowInstance window in windows)
        {
            if (Vector3.Dot(window.Normal, candidate.Region.OutwardNormal) < 0.999f
                || MathF.Abs(Vector3.Dot(window.Centre, candidate.Region.OutwardNormal)
                    - candidate.Region.PlaneCoordinateMetres) > 0.2f)
                continue;
            Vector3 right = Vector3.Normalize(Vector3.Cross(window.Up, window.Normal));
            Vector3[] corners =
            [
                window.Centre - right * window.Width * 0.5f - window.Up * window.Height * 0.5f,
                window.Centre + right * window.Width * 0.5f - window.Up * window.Height * 0.5f,
                window.Centre + right * window.Width * 0.5f + window.Up * window.Height * 0.5f,
                window.Centre - right * window.Width * 0.5f + window.Up * window.Height * 0.5f,
            ];
            float minU = corners.Min(point => Vector3.Dot(point, candidate.Region.TangentU));
            float maxU = corners.Max(point => Vector3.Dot(point, candidate.Region.TangentU));
            float minV = corners.Min(point => Vector3.Dot(point, candidate.Region.TangentV));
            float maxV = corners.Max(point => Vector3.Dot(point, candidate.Region.TangentV));
            if (candidate.MinU < maxU + margin && candidate.MaxU > minU - margin
                && candidate.MinV < maxV + margin && candidate.MaxV > minV - margin)
                return true;
        }
        return false;
    }

    private static bool OverlapsLight(
        Candidate candidate,
        IReadOnlyList<MegastationLightInstance> lights,
        float radius)
    {
        foreach (MegastationLightInstance light in lights)
        {
            if (Vector3.Dot(light.Normal, candidate.Region.OutwardNormal) < 0.999f
                || MathF.Abs(Vector3.Dot(light.SurfacePosition, candidate.Region.OutwardNormal)
                    - candidate.Region.PlaneCoordinateMetres) > 0.2f)
                continue;
            float u = Vector3.Dot(light.SurfacePosition, candidate.Region.TangentU);
            float v = Vector3.Dot(light.SurfacePosition, candidate.Region.TangentV);
            if (u >= candidate.MinU - radius && u <= candidate.MaxU + radius
                && v >= candidate.MinV - radius && v <= candidate.MaxV + radius)
                return true;
        }
        return false;
    }

    private static bool Collides(
        Candidate candidate,
        MegastationInfrastructureCluster accepted,
        float separation)
    {
        if (Vector3.DistanceSquared(candidate.SurfacePosition, accepted.SurfacePosition)
            < separation * separation)
            return true;
        return Intersects(candidate.AabbMin, candidate.AabbMax, accepted.AabbMin, accepted.AabbMax);
    }

    private static float CellSizeForRole(float baseSize, MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Industrial => baseSize * 0.62f,
        MegastationZoneRole.Utilities => baseSize * 0.66f,
        MegastationZoneRole.Logistics => baseSize * 0.82f,
        MegastationZoneRole.Habitation => baseSize * 1.15f,
        MegastationZoneRole.Strategic => baseSize * 1.20f,
        _ => baseSize,
    };

    private static float SeparationForRole(float baseSeparation, MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Industrial => baseSeparation * 0.55f,
        MegastationZoneRole.Utilities => baseSeparation * 0.58f,
        MegastationZoneRole.Logistics => baseSeparation * 0.78f,
        MegastationZoneRole.Habitation => baseSeparation * 1.15f,
        MegastationZoneRole.Strategic => baseSeparation * 1.35f,
        _ => baseSeparation,
    };

    private static bool NearG1(
        MegastationPlanarRegion region,
        float u,
        float v,
        MegastationAttachmentPlan plan)
    {
        const float influence = 110f;
        foreach (MegastationAttachmentReservation reservation in plan.Reservations)
        {
            if (Vector3.Dot(reservation.Normal, region.OutwardNormal) < 0.999f
                || MathF.Abs(reservation.PlaneCoordinateMetres - region.PlaneCoordinateMetres) > 0.1f)
                continue;
            float du = MathF.Max(reservation.MinU - u, MathF.Max(0f, u - reservation.MaxU));
            float dv = MathF.Max(reservation.MinV - v, MathF.Max(0f, v - reservation.MaxV));
            if (du * du + dv * dv <= influence * influence)
                return true;
        }
        return false;
    }

    private static (Vector3 Min, Vector3 Max) WorldBounds(
        Vector3 surfacePosition,
        Vector3 normal,
        Vector3 tangentU,
        Vector3 tangentV,
        float width,
        float height,
        float depth)
    {
        Vector3 halfU = tangentU * width * 0.5f;
        Vector3 halfV = tangentV * height * 0.5f;
        Vector3 outward = normal * depth;
        Vector3[] corners =
        [
            surfacePosition - halfU - halfV,
            surfacePosition + halfU - halfV,
            surfacePosition + halfU + halfV,
            surfacePosition - halfU + halfV,
            surfacePosition + outward - halfU - halfV,
            surfacePosition + outward + halfU - halfV,
            surfacePosition + outward + halfU + halfV,
            surfacePosition + outward - halfU + halfV,
        ];
        return (new(
            corners.Min(point => point.X),
            corners.Min(point => point.Y),
            corners.Min(point => point.Z)), new(
            corners.Max(point => point.X),
            corners.Max(point => point.Y),
            corners.Max(point => point.Z)));
    }

    private static (Color Primary, Color Secondary) Palette(int seed)
    {
        (Color Primary, Color Secondary)[] palettes =
        [
            (new Color(105, 112, 112), new Color(170, 152, 105)),
            (new Color(130, 110, 70), new Color(188, 174, 135)),
            (new Color(145, 145, 135), new Color(96, 105, 110)),
            (new Color(110, 125, 140), new Color(178, 132, 92)),
            (new Color(120, 90, 65), new Color(172, 164, 142)),
        ];
        return palettes[PositiveMod(MegastationSeed.Derive(seed, "palette"), palettes.Length)];
    }

    private static MegastationZoneRole ClusterRole(
        IReadOnlyList<MegastationInfrastructureCluster> clusters,
        string identity)
        => clusters.First(cluster => cluster.Identity == identity).ZoneRole;

    private static bool Intersects(
        Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        => aMin.X < bMax.X && aMax.X > bMin.X
            && aMin.Y < bMax.Y && aMax.Y > bMin.Y
            && aMin.Z < bMax.Z && aMax.Z > bMin.Z;

    private static int PositiveMod(int value, int modulus)
        => (int)((uint)value % (uint)modulus);

    private static float Sample(int seed, string key)
        => unchecked((uint)MegastationSeed.Derive(seed, key)) / (float)uint.MaxValue;

    private static float SignedSample(int seed, string key)
        => Sample(seed, key) * 2f - 1f;
}

public static class MegastationInfrastructureMeshBuilder
{
    public static MegastationInfrastructureMeshBuildResult Build(
        MegastationInfrastructurePlan plan,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var mesh = new StationModuleMesh();
        var familyRanges = Enum.GetValues<MegastationInfrastructureFamily>()
            .ToDictionary(family => family, _ => new List<(int indexStart, int indexCount)>());
        var visibleTriangles = Enum.GetValues<MegastationInfrastructureFamily>()
            .ToDictionary(family => family, _ => 0);
        var casterInstances = Enum.GetValues<MegastationInfrastructureFamily>()
            .ToDictionary(family => family, _ => 0);
        foreach (MegastationInfrastructureInstance instance in plan.Instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rangeStart = mesh.DecorClassRanges.Count;
            int indexStart = mesh.IndexCount;
            mesh.BreakDecorClassRange();
            MegastationInfrastructurePrimitives.Emit(instance, mesh);
            visibleTriangles[instance.Family] += (mesh.IndexCount - indexStart) / 3;
            var casterRanges = mesh.DecorClassRanges.Skip(rangeStart)
                .Where(range => range.decorClass == DecorClass.MegastationInfrastructureMajor)
                .Select(range => (range.indexStart, range.indexCount))
                .ToArray();
            if (casterRanges.Length > 0)
            {
                casterInstances[instance.Family]++;
                familyRanges[instance.Family].AddRange(casterRanges);
            }
        }
        mesh.ApplyIlluminationFlags();
        StationMeshCpuData? shadow = mesh.PrepareIndexRanges(
            mesh.DecorClassRanges
                .Where(range => range.decorClass == DecorClass.MegastationInfrastructureMajor)
                .Select(range => (range.indexStart, range.indexCount))
                .ToArray());
        stopwatch.Stop();
        int shadowVertices = shadow?.Vertices.Length ?? 0;
        int shadowIndices = shadow?.Indices.Length ?? 0;
        MegastationShadowFamilyDiagnostics[] shadowByFamily =
            Enum.GetValues<MegastationInfrastructureFamily>().Select(family =>
            {
                StationMeshCpuData? familyShadow = mesh.PrepareIndexRanges(familyRanges[family]);
                return new MegastationShadowFamilyDiagnostics(
                    family.ToString(),
                    MegastationInfrastructurePrimitives.ShadowPolicies[family],
                    plan.Instances.Count(instance => instance.Family == family),
                    casterInstances[family],
                    visibleTriangles[family],
                    familyShadow?.Vertices.Length ?? 0,
                    (familyShadow?.Indices.Length ?? 0) / 3);
            }).ToArray();
        MegastationInfrastructureDiagnostics diagnostics = plan.Diagnostics with
        {
            VisibleVertexCount = mesh.VertexCount,
            VisibleTriangleCount = mesh.IndexCount / 3,
            VisibleMeshBytes = MeshBytes(mesh.VertexCount, mesh.IndexCount),
            ShadowVertexCount = shadowVertices,
            ShadowTriangleCount = shadowIndices / 3,
            ShadowMeshBytes = MeshBytes(shadowVertices, shadowIndices),
            MeshBuildMilliseconds = stopwatch.ElapsedMilliseconds,
            ShadowByFamily = shadowByFamily,
        };
        return new(mesh, diagnostics);
    }

    private static long MeshBytes(int vertices, int indices)
        => (long)vertices * 36L + (long)indices * 4L;
}

internal static class MegastationInfrastructureDebug
{
    public static VertexPositionColor[] BuildLines(MegastationInfrastructurePlan plan)
    {
        var lines = new List<VertexPositionColor>();
        foreach (MegastationInfrastructureCluster cluster in plan.Clusters)
        {
            float plane = Vector3.Dot(cluster.SurfacePosition, cluster.Normal) + 0.20f;
            Vector3 Point(float u, float v) => cluster.Normal * plane
                + cluster.TangentU * u + cluster.TangentV * v;
            AddRectangle(lines,
                Point(cluster.MinU, cluster.MinV), Point(cluster.MaxU, cluster.MinV),
                Point(cluster.MaxU, cluster.MaxV), Point(cluster.MinU, cluster.MaxV),
                Color.Magenta);
        }
        foreach (MegastationInfrastructureInstance instance in plan.Instances)
        {
            Color colour = instance.Family switch
            {
                MegastationInfrastructureFamily.MachineryHousing => Color.Cyan,
                MegastationInfrastructureFamily.Ventilation => Color.Lime,
                MegastationInfrastructureFamily.Tank => Color.Orange,
                _ => Color.White,
            };
            float halfU = instance.Width * 0.5f;
            float halfV = instance.Height * 0.5f;
            if (instance.Family == MegastationInfrastructureFamily.Tank
                && (instance.Variant & 1) != 0)
                (halfU, halfV) = (halfV, halfU);
            Vector3 p0 = instance.SurfacePosition - instance.TangentU * halfU - instance.TangentV * halfV
                + instance.Normal * 0.24f;
            Vector3 p1 = instance.SurfacePosition + instance.TangentU * halfU - instance.TangentV * halfV
                + instance.Normal * 0.24f;
            Vector3 p2 = instance.SurfacePosition + instance.TangentU * halfU + instance.TangentV * halfV
                + instance.Normal * 0.24f;
            Vector3 p3 = instance.SurfacePosition - instance.TangentU * halfU + instance.TangentV * halfV
                + instance.Normal * 0.24f;
            AddRectangle(lines, p0, p1, p2, p3, colour);
            AddLine(lines, instance.SurfacePosition + instance.Normal * 0.24f,
                instance.SurfacePosition + instance.Normal * DebugHeight(instance), colour);
        }
        return lines.ToArray();
    }

    private static float DebugHeight(MegastationInfrastructureInstance instance)
    {
        if (instance.Family == MegastationInfrastructureFamily.MachineryHousing)
        {
            float depth = MathF.Min(instance.Depth, MathF.Max(1f, instance.Width * 0.45f));
            return depth * 1.42f;
        }
        if (instance.Family == MegastationInfrastructureFamily.Ventilation)
            return MathF.Max(0.22f, instance.Depth) + 0.10f;
        float radius = Math.Clamp(instance.Width * 0.5f, 0.6f, 3.4f);
        return radius * 2f + 0.35f;
    }

    private static void AddRectangle(List<VertexPositionColor> lines,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color colour)
    {
        AddLine(lines, p0, p1, colour);
        AddLine(lines, p1, p2, colour);
        AddLine(lines, p2, p3, colour);
        AddLine(lines, p3, p0, colour);
    }

    private static void AddLine(List<VertexPositionColor> lines,
        Vector3 from, Vector3 to, Color colour)
    {
        lines.Add(new(from, colour));
        lines.Add(new(to, colour));
    }
}

public static class MegastationInfrastructurePrimitives
{
    public static IReadOnlyDictionary<MegastationInfrastructureFamily, MegastationShadowPolicy>
        ShadowPolicies { get; } = new Dictionary<MegastationInfrastructureFamily, MegastationShadowPolicy>
        {
            [MegastationInfrastructureFamily.MachineryHousing] = MegastationShadowPolicy.ConditionalSubstantial,
            [MegastationInfrastructureFamily.Ventilation] = MegastationShadowPolicy.None,
            [MegastationInfrastructureFamily.Tank] = MegastationShadowPolicy.Simplified,
        };

    // Components emitted inside a family also have an explicit policy. This prevents a
    // future extraction or family addition from silently inheriting no-shadow.
    public static IReadOnlyDictionary<string, MegastationShadowPolicy> ComponentShadowPolicies { get; }
        = new Dictionary<string, MegastationShadowPolicy>(StringComparer.Ordinal)
        {
            ["EquipmentHousing"] = MegastationShadowPolicy.Simplified,
            ["LargeServiceStructure"] = MegastationShadowPolicy.Simplified,
            ["TankBodyAndMajorSupports"] = MegastationShadowPolicy.Simplified,
            ["JunctionBox"] = MegastationShadowPolicy.None,
            ["ConduitEntry"] = MegastationShadowPolicy.None,
            ["VentLouverGrille"] = MegastationShadowPolicy.None,
            ["TinyIncidentalDetail"] = MegastationShadowPolicy.None,
        };

    public static void Emit(
        MegastationInfrastructureInstance instance,
        StationModuleMesh targetMesh)
    {
        SurfaceFrame frame = SurfaceFrame.From(instance);
        switch (instance.Family)
        {
            case MegastationInfrastructureFamily.MachineryHousing:
                EmitMachineryHousing(frame, instance, targetMesh);
                break;
            case MegastationInfrastructureFamily.Ventilation:
                EmitVent(frame, instance, targetMesh);
                break;
            case MegastationInfrastructureFamily.Tank:
                EmitTank(frame, instance, targetMesh);
                break;
        }
    }

    private static void EmitMachineryHousing(
        SurfaceFrame frame,
        MegastationInfrastructureInstance instance,
        StationModuleMesh mesh)
    {
        StationSurfaceFrame surface = frame.Shared;
        Color body = instance.PrimaryColour;
        Color detail = instance.SecondaryColour;
        Color dark = Darken(instance.PrimaryColour, 0.38f);
        mesh.CurrentDecorClass = instance.CastsShadow
            ? DecorClass.MegastationInfrastructureMajor
            : DecorClass.MegastationInfrastructureMinor;
        float baseDepth = MathF.Min(instance.Depth, MathF.Max(1.0f, instance.Width * 0.45f));
        float topDepth = MathF.Max(0.35f, baseDepth * 0.42f);
        StationIndustrialPrimitives.EmitEquipmentHousing(mesh, surface,
            instance.Width, instance.Height, baseDepth, topDepth,
            instance.Width * (instance.Variant == 2 ? 0.72f : 0.60f),
            instance.Height * (instance.Variant == 3 ? 0.48f : 0.56f),
            instance.Width * (((instance.Variant & 1) == 0) ? -0.08f : 0.10f),
            body, detail);

        mesh.CurrentDecorClass = DecorClass.MegastationInfrastructureMinor;
        StationIndustrialPrimitives.EmitJunctionBox(mesh,
            surface with { Origin = surface.Point(instance.Width * 0.29f, -instance.Height * 0.22f, baseDepth) },
            instance.Width * 0.22f, instance.Height * 0.28f,
            MathF.Max(0.20f, baseDepth * 0.10f), detail, dark);
        StationIndustrialPrimitives.EmitConduitEntry(mesh,
            surface with { Origin = surface.Point(-instance.Width * 0.30f, instance.Height * 0.20f, baseDepth) },
            instance.Width * 0.18f, instance.Height * 0.22f,
            MathF.Max(0.22f, baseDepth * 0.11f), instance.Width * 0.20f,
            MathF.Max(0.08f, instance.Width * 0.018f), body, detail);
    }

    private static void EmitVent(
        SurfaceFrame frame,
        MegastationInfrastructureInstance instance,
        StationModuleMesh mesh)
    {
        mesh.CurrentDecorClass = DecorClass.MegastationInfrastructureMinor;
        float plinthDepth = MathF.Max(0.22f, instance.Depth);
        AddFrameBox(mesh, frame, frame.Origin + frame.Normal * plinthDepth * 0.5f,
            instance.Width + 0.35f, instance.Height + 0.35f, plinthDepth,
            Darken(instance.PrimaryColour, 0.62f));
        StationSurfaceFrame ventSurface = frame.Shared with
        {
            Origin = frame.Origin + frame.Normal * plinthDepth,
        };
        Color frameColour = Darken(instance.PrimaryColour, 0.58f);
        Color detailColour = Darken(instance.PrimaryColour,
            instance.Variant == 1 ? 0.50f : 0.45f);
        int count = Math.Clamp((int)(instance.Height / 0.42f), 4, 8);
        if (instance.Variant == 1)
            StationIndustrialPrimitives.EmitLouveredVent(mesh, ventSurface,
                instance.Width, instance.Height, count, frameColour, detailColour);
        else if (instance.Variant == 2)
            StationIndustrialPrimitives.EmitScreenVent(mesh, ventSurface,
                instance.Width, instance.Height, frameColour, detailColour);
        else
            StationIndustrialPrimitives.EmitHorizontalBarVent(mesh, ventSurface,
                instance.Width, instance.Height, true, count, frameColour, detailColour);
    }

    private static void EmitTank(
        SurfaceFrame frame,
        MegastationInfrastructureInstance instance,
        StationModuleMesh mesh)
    {
        Vector3 axis = (instance.Variant & 1) == 0 ? frame.V : frame.U;
        float radius = Math.Clamp(instance.Width * 0.5f, 0.6f, 3.4f);
        float length = Math.Clamp(instance.Height, 2.0f, 15.5f);
        Vector3 centre = frame.Origin + frame.Normal * (radius + 0.35f);
        Vector3 start = centre - axis * length * 0.5f;
        Vector3 end = centre + axis * length * 0.5f;

        mesh.CurrentDecorClass = instance.CastsShadow
            ? DecorClass.MegastationInfrastructureMajor
            : DecorClass.MegastationInfrastructureMinor;
        (Color tankBody, Color tankStripe, int stripes) = instance.Variant switch
        {
            0 => (new Color(218, 118, 38), new Color(220, 220, 215), 2),
            1 => (new Color(198, 198, 192), new Color(75, 75, 75), 2),
            _ => (new Color(50, 95, 195), new Color(220, 220, 215), 2),
        };
        // Keep the major connection normal to the support plane, as in ordinary
        // station tank placement; a diagonal connector's circular ring can cross
        // behind the exact support plane.
        Vector3 attachPoint = frame.Origin + axis * (length * 0.5f + radius * 0.50f)
            + frame.Normal * 0.08f;
        StationIndustrialPrimitives.EmitTankCore(mesh, start, end, radius,
            tankBody, tankStripe, stripes, attachPoint,
            DecorClass.MegastationInfrastructureMinor);
        mesh.CurrentDecorClass = instance.CastsShadow
            ? DecorClass.MegastationInfrastructureMajor
            : DecorClass.MegastationInfrastructureMinor;
        Vector3 cross = Vector3.Normalize(Vector3.Cross(axis, frame.Normal));
        for (int support = -1; support <= 1; support += 2)
        {
            Vector3 supportCentre = centre + axis * (length * 0.28f * support)
                - frame.Normal * (radius * 0.5f);
            Matrix transform = FrameMatrix(cross, axis, frame.Normal, supportCentre);
            mesh.AddOrientedBox(transform,
                new Vector3(radius * 0.42f, radius * 1.45f, radius + 0.7f),
                Darken(tankBody, 0.58f));
        }
    }

    private readonly record struct SurfaceFrame(
        Vector3 Origin,
        Vector3 Normal,
        Vector3 U,
        Vector3 V)
    {
        public StationSurfaceFrame Shared => new(Origin, Normal, U, V);

        public static SurfaceFrame From(MegastationInfrastructureInstance instance)
        {
            Vector3 normal = Vector3.Normalize(instance.Normal);
            Vector3 u = Vector3.Normalize(instance.TangentU);
            Vector3 v = Vector3.Normalize(instance.TangentV);
            if (Vector3.Dot(Vector3.Cross(u, v), normal) < 0f)
                v = -v;
            return new(instance.SurfacePosition, normal, u, v);
        }
    }

    private static void AddFrameBox(
        StationModuleMesh mesh,
        SurfaceFrame frame,
        Vector3 centre,
        float width,
        float height,
        float depth,
        Color colour)
        => mesh.AddOrientedBox(
            FrameMatrix(frame.U, frame.V, frame.Normal, centre),
            new Vector3(width, height, depth),
            colour);

    private static Matrix FrameMatrix(Vector3 u, Vector3 v, Vector3 normal, Vector3 centre)
        => new(
            u.X, u.Y, u.Z, 0f,
            v.X, v.Y, v.Z, 0f,
            normal.X, normal.Y, normal.Z, 0f,
            centre.X, centre.Y, centre.Z, 1f);

    private static Color Darken(Color colour, float factor)
        => new(
            (byte)Math.Clamp((int)(colour.R * factor), 0, 255),
            (byte)Math.Clamp((int)(colour.G * factor), 0, 255),
            (byte)Math.Clamp((int)(colour.B * factor), 0, 255),
            colour.A);
}
