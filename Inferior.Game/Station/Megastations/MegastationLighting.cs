using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationLightingSurfaceRegion(
    string Identity,
    string ZoneIdentity,
    int ZoneSeed,
    MegastationZoneRole Role,
    GridDirection Direction,
    int PlaneGridCoordinate,
    float PlaneCoordinateMetres,
    Vector3 Normal,
    Vector3 Right,
    Vector3 Up,
    IReadOnlyList<BoundaryFaceKey> Faces,
    float PhysicalArea,
    float Prominence,
    float Extremity,
    float Exposure,
    float RelativeDepth,
    float Concavity);

public sealed record MegastationLightCluster(
    string Identity,
    string ZoneIdentity,
    string RegionIdentity,
    MegastationZoneRole Role,
    BoundaryFaceKey SurfaceFace,
    int LightCount);

public sealed record MegastationLightInstance(
    string Identity,
    string ClusterIdentity,
    string RegionIdentity,
    MegastationZoneRole Role,
    BoundaryFaceKey SurfaceFace,
    Vector3 SurfacePosition,
    Vector3 Normal,
    Vector3 GlowPosition,
    Color Colour,
    GlowType GlowType,
    float BaseIntensity,
    float Rate,
    float Phase,
    LightPattern Pattern)
{
    public StationLightInfo ToStationLightInfo() => new(
        GlowPosition,
        Colour,
        GlowType,
        BaseIntensity,
        Rate,
        Phase,
        Pattern)
    {
        SurfaceNormal = Normal,
    };
}

public sealed record MegastationLightingDiagnostics(
    int IndustrialZoneCount,
    int LogisticsZoneCount,
    int UtilitiesZoneCount,
    int StrategicZoneCount,
    int IndustrialLightCount,
    int LogisticsLightCount,
    int UtilitiesLightCount,
    int StrategicLightCount,
    int IndustrialClusterCount,
    int LogisticsClusterCount,
    int UtilitiesClusterCount,
    int StrategicClusterCount,
    int IndustrialActiveRegionCount,
    int LogisticsActiveRegionCount,
    int UtilitiesActiveRegionCount,
    int StrategicActiveRegionCount,
    float IndustrialEligibleArea,
    float LogisticsEligibleArea,
    float UtilitiesEligibleArea,
    float StrategicEligibleArea,
    int ClusterCount,
    int SteadyLightCount,
    int AnimatedLightCount,
    long PlanningMilliseconds);

public sealed record MegastationLightPlan(
    IReadOnlyList<MegastationLightingSurfaceRegion> Regions,
    IReadOnlyList<MegastationLightCluster> Clusters,
    IReadOnlyList<MegastationLightInstance> Lights,
    MegastationLightingDiagnostics Diagnostics);

internal sealed record MegastationLightingTuning(
    float IndustrialAreaPerCluster,
    float LogisticsAreaPerCluster,
    float UtilitiesAreaPerCluster,
    float StrategicAreaPerCluster,
    int IndustrialMaximumClustersPerZone,
    int LogisticsMaximumClustersPerZone,
    int UtilitiesMaximumClustersPerZone,
    int StrategicMaximumClustersPerZone,
    float IndustrialMinimumClusterSeparation,
    float LogisticsMinimumClusterSeparation,
    float UtilitiesMinimumClusterSeparation,
    float StrategicMinimumClusterSeparation)
{
    public static MegastationLightingTuning Default { get; } = new(
        IndustrialAreaPerCluster: 32_000f,
        LogisticsAreaPerCluster: 30_000f,
        UtilitiesAreaPerCluster: 70_000f,
        StrategicAreaPerCluster: 70_000f,
        IndustrialMaximumClustersPerZone: 40,
        LogisticsMaximumClustersPerZone: 32,
        UtilitiesMaximumClustersPerZone: 20,
        StrategicMaximumClustersPerZone: 10,
        IndustrialMinimumClusterSeparation: 32f,
        LogisticsMinimumClusterSeparation: 45f,
        UtilitiesMinimumClusterSeparation: 28f,
        StrategicMinimumClusterSeparation: 90f);
}

public static class MegastationLightingPlanner
{
    private const string AlgorithmKey = "lighting:v1";
    private const float GlowOffsetMetres = 0.06f;

    public static MegastationLightPlan Plan(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
        => Plan(grid, topology, zoning, MegastationLightingTuning.Default, cancellationToken);

    internal static MegastationLightPlan Plan(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        MegastationLightingTuning tuning,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        MegastationLightingSurfaceRegion[] regions = ExtractRegions(
            grid, topology, zoning, cancellationToken);
        var clusters = new List<MegastationLightCluster>();
        var lights = new List<MegastationLightInstance>();

        foreach (MegastationSemanticZone zone in zoning.Zones
                     .Where(zone => IsLitRole(zone.Role))
                     .OrderBy(zone => zone.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MegastationLightingSurfaceRegion[] zoneRegions = regions
                .Where(region => region.ZoneIdentity == zone.Identity)
                .OrderByDescending(region => RegionScore(zone.Role, region))
                .ThenBy(region => region.Identity, StringComparer.Ordinal)
                .ToArray();
            if (zoneRegions.Length == 0)
                continue;

            int clusterCount = ClusterCount(zone, tuning);
            int lightingSeed = MegastationSeed.Derive(zone.Seed, AlgorithmKey);
            float minimumSeparation = MinimumClusterSeparation(zone.Role, tuning);
            var acceptedCentres = new List<Vector3>(clusterCount);
            int candidateLimit = Math.Max(clusterCount * 12, 24);
            for (int candidateOrdinal = 0;
                 candidateOrdinal < candidateLimit && acceptedCentres.Count < clusterCount;
                 candidateOrdinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MegastationLightingSurfaceRegion region = zoneRegions[candidateOrdinal % zoneRegions.Length];
                int clusterSeed = MegastationSeed.Derive(
                    lightingSeed,
                    $"{region.Identity}/cluster:{candidateOrdinal}");
                BoundaryFaceKey surfaceFace = SelectSurfaceFace(
                    zone.Role, region, zoning, clusterSeed, candidateOrdinal);
                int lampCount = LampsPerCluster(zone.Role, clusterSeed);
                string clusterIdentity = $"{zone.Identity}/{AlgorithmKey}/{region.Identity}/cluster:{candidateOrdinal}";
                MegastationLightInstance[] clusterLights = PlaceCluster(
                    grid,
                    topology.FaceByKey[surfaceFace],
                    region,
                    zone.Role,
                    clusterIdentity,
                    lampCount,
                    clusterSeed);
                if (clusterLights.Length == 0)
                    continue;

                Vector3 centre = ClusterCentre(clusterLights);
                if (acceptedCentres.Any(existing =>
                        Vector3.DistanceSquared(existing, centre)
                        < minimumSeparation * minimumSeparation))
                    continue;

                acceptedCentres.Add(centre);
                clusters.Add(new(
                    clusterIdentity,
                    zone.Identity,
                    region.Identity,
                    zone.Role,
                    surfaceFace,
                    clusterLights.Length));
                lights.AddRange(clusterLights);
            }
        }

        stopwatch.Stop();
        MegastationLightCluster[] orderedClusters = clusters
            .OrderBy(cluster => cluster.Identity, StringComparer.Ordinal)
            .ToArray();
        MegastationLightInstance[] orderedLights = lights
            .OrderBy(light => light.Identity, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = new MegastationLightingDiagnostics(
            ZoneCount(zoning, MegastationZoneRole.Industrial),
            ZoneCount(zoning, MegastationZoneRole.Logistics),
            ZoneCount(zoning, MegastationZoneRole.Utilities),
            ZoneCount(zoning, MegastationZoneRole.Strategic),
            LightCount(orderedLights, MegastationZoneRole.Industrial),
            LightCount(orderedLights, MegastationZoneRole.Logistics),
            LightCount(orderedLights, MegastationZoneRole.Utilities),
            LightCount(orderedLights, MegastationZoneRole.Strategic),
            ClusterCount(orderedClusters, MegastationZoneRole.Industrial),
            ClusterCount(orderedClusters, MegastationZoneRole.Logistics),
            ClusterCount(orderedClusters, MegastationZoneRole.Utilities),
            ClusterCount(orderedClusters, MegastationZoneRole.Strategic),
            ActiveRegionCount(orderedClusters, MegastationZoneRole.Industrial),
            ActiveRegionCount(orderedClusters, MegastationZoneRole.Logistics),
            ActiveRegionCount(orderedClusters, MegastationZoneRole.Utilities),
            ActiveRegionCount(orderedClusters, MegastationZoneRole.Strategic),
            EligibleArea(regions, MegastationZoneRole.Industrial),
            EligibleArea(regions, MegastationZoneRole.Logistics),
            EligibleArea(regions, MegastationZoneRole.Utilities),
            EligibleArea(regions, MegastationZoneRole.Strategic),
            orderedClusters.Length,
            orderedLights.Count(light => light.Pattern == LightPattern.Continuous),
            orderedLights.Count(light => light.Pattern != LightPattern.Continuous),
            stopwatch.ElapsedMilliseconds);
        return new(regions, orderedClusters, orderedLights, diagnostics);
    }

    public static MegastationLightingSurfaceRegion[] ExtractRegions(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
    {
        var candidateFaces = zoning.Zones
            .Where(zone => IsLitRole(zone.Role))
            .SelectMany(zone => zone.Faces.Select(face => (zone, face)))
            .OrderBy(pair => pair.face)
            .ToArray();
        var regions = new List<MegastationLightingSurfaceRegion>();

        foreach (var planeGroup in candidateFaces.GroupBy(pair => (
                     pair.zone.Identity,
                     pair.face.Direction,
                     PlaneCoordinate(pair.face))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var byFace = planeGroup.ToDictionary(pair => pair.face, pair => pair.zone);
            var remaining = new SortedSet<BoundaryFaceKey>(byFace.Keys);
            while (remaining.Count > 0)
            {
                BoundaryFaceKey first = remaining.Min;
                var component = new SortedSet<BoundaryFaceKey>();
                var queue = new Queue<BoundaryFaceKey>();
                remaining.Remove(first);
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    BoundaryFaceKey current = queue.Dequeue();
                    component.Add(current);
                    foreach (BoundaryEdgeKey edge in topology.FaceByKey[current].Edges)
                    foreach (BoundaryFaceKey neighbour in topology.EdgeByKey[edge].IncidentFaces)
                    {
                        if (byFace.ContainsKey(neighbour) && remaining.Remove(neighbour))
                            queue.Enqueue(neighbour);
                    }
                }

                MegastationSemanticZone zone = byFace[first];
                BoundaryFace firstFace = topology.FaceByKey[first];
                Vector3 p0 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[0]);
                Vector3 p1 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[1]);
                Vector3 p3 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[3]);
                Vector3 right = Vector3.Normalize(p1 - p0);
                Vector3 up = Vector3.Normalize(p3 - p0);
                Vector3 normal = BoundaryTopologyBuilder.Normal(first.Direction);
                MegastationSemanticSurface[] surfaces = component
                    .Select(face => zoning.SurfaceByFace[face])
                    .ToArray();
                float totalArea = surfaces.Sum(surface => surface.Metrics.PhysicalArea);
                string maskKey = string.Join('|', component.Select(FaceIdentity));
                int signature = MegastationSeed.Derive(zone.Seed, maskKey);
                string identity = $"{zone.Identity}/lighting-plane:{first.Direction}:{PlaneCoordinate(first)}:" +
                    $"{component.Min.X},{component.Min.Y},{component.Min.Z}:{unchecked((uint)signature):X8}";
                regions.Add(new(
                    identity,
                    zone.Identity,
                    zone.Seed,
                    zone.Role,
                    first.Direction,
                    PlaneCoordinate(first),
                    Vector3.Dot(p0, normal),
                    normal,
                    right,
                    up,
                    component.ToArray(),
                    totalArea,
                    WeightedMetric(surfaces, totalArea, metrics => metrics.Prominence),
                    WeightedMetric(surfaces, totalArea, metrics => metrics.Extremity),
                    WeightedMetric(surfaces, totalArea, metrics => metrics.ImmediateExposure),
                    WeightedMetric(surfaces, totalArea, metrics => metrics.RelativeDepth),
                    WeightedMetric(surfaces, totalArea, metrics => metrics.ConcavityContext)));
            }
        }

        return regions.OrderBy(region => region.Identity, StringComparer.Ordinal).ToArray();
    }

    public static bool IsLitRole(MegastationZoneRole role)
        => role is MegastationZoneRole.Industrial
            or MegastationZoneRole.Logistics
            or MegastationZoneRole.Utilities
            or MegastationZoneRole.Strategic;

    private static MegastationLightInstance[] PlaceCluster(
        SliceGrid grid,
        BoundaryFace face,
        MegastationLightingSurfaceRegion region,
        MegastationZoneRole role,
        string clusterIdentity,
        int requestedCount,
        int clusterSeed)
    {
        Vector3[] vertices = face.Vertices
            .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex))
            .ToArray();
        float[] uValues = vertices.Select(position => Vector3.Dot(position, region.Right)).ToArray();
        float[] vValues = vertices.Select(position => Vector3.Dot(position, region.Up)).ToArray();
        float minU = uValues.Min();
        float maxU = uValues.Max();
        float minV = vValues.Min();
        float maxV = vValues.Max();
        float width = maxU - minU;
        float height = maxV - minV;
        const float margin = 0.75f;
        if (width <= margin * 2f || height <= margin * 2f)
            return [];

        bool lineAlongU = width >= height;
        float longMinimum = lineAlongU ? minU : minV;
        float longMaximum = lineAlongU ? maxU : maxV;
        float crossMinimum = lineAlongU ? minV : minU;
        float crossMaximum = lineAlongU ? maxV : maxU;
        float availableLength = longMaximum - longMinimum - margin * 2f;
        int count = Math.Min(requestedCount, Math.Max(1, (int)(availableLength / 2f) + 1));
        float desiredSpacing = role switch
        {
            MegastationZoneRole.Industrial => Lerp(6f, 12f, Sample(clusterSeed, "spacing")),
            MegastationZoneRole.Logistics => Lerp(14f, 26f, Sample(clusterSeed, "spacing")),
            MegastationZoneRole.Utilities => Lerp(4f, 8f, Sample(clusterSeed, "spacing")),
            _ => Lerp(12f, 24f, Sample(clusterSeed, "spacing")),
        };
        float spacing = count <= 1 ? 0f : MathF.Min(desiredSpacing, availableLength / (count - 1));
        float longCentre = Lerp(
            longMinimum + margin + spacing * (count - 1) * 0.5f,
            longMaximum - margin - spacing * (count - 1) * 0.5f,
            Sample(clusterSeed, "long-centre"));
        float crossFraction = role switch
        {
            MegastationZoneRole.Logistics => Sample(clusterSeed, "edge-side") < 0.5f ? 0.14f : 0.86f,
            MegastationZoneRole.Strategic => Sample(clusterSeed, "edge-side") < 0.5f ? 0.20f : 0.80f,
            MegastationZoneRole.Utilities => Lerp(0.40f, 0.60f, Sample(clusterSeed, "cross")),
            _ => Lerp(0.32f, 0.68f, Sample(clusterSeed, "cross")),
        };
        float cross = Lerp(crossMinimum + margin, crossMaximum - margin, crossFraction);
        var lights = new MegastationLightInstance[count];
        for (int i = 0; i < count; i++)
        {
            float longitudinal = longCentre + (i - (count - 1) * 0.5f) * spacing;
            float u = lineAlongU ? longitudinal : cross;
            float v = lineAlongU ? cross : longitudinal;
            Vector3 surfacePosition = region.Normal * region.PlaneCoordinateMetres
                + region.Right * u
                + region.Up * v;
            int lightSeed = MegastationSeed.Derive(clusterSeed, $"light:{i}");
            (Color colour, GlowType glowType, float intensity, float rate, float phase, LightPattern pattern) =
                Presentation(role, clusterSeed, lightSeed, i);
            lights[i] = new(
                $"{clusterIdentity}/light:{i}",
                clusterIdentity,
                region.Identity,
                role,
                face.Key,
                surfacePosition,
                region.Normal,
                surfacePosition + region.Normal * GlowOffsetMetres,
                colour,
                glowType,
                intensity,
                rate,
                phase,
                pattern);
        }
        return lights;
    }

    private static (Color, GlowType, float, float, float, LightPattern) Presentation(
        MegastationZoneRole role,
        int clusterSeed,
        int seed,
        int ordinal)
    {
        float colourRoll = Sample(clusterSeed, "cluster-colour");
        Color colour = role switch
        {
            MegastationZoneRole.Industrial => colourRoll < 0.45f ? StationWindowVisuals.DimAmber
                : colourRoll < 0.75f ? StationWindowVisuals.WarmWhite
                : StationWindowVisuals.NeutralWhite,
            MegastationZoneRole.Logistics => colourRoll < 0.55f ? StationWindowVisuals.NeutralWhite
                : StationWindowVisuals.DimAmber,
            MegastationZoneRole.Utilities => colourRoll < 0.75f ? new Color(255, 160, 0)
                : new Color(210, 55, 45),
            _ => colourRoll < 0.68f ? new Color(220, 25, 25)
                : new Color(210, 220, 255),
        };

        if (role == MegastationZoneRole.Strategic)
        {
            bool animated = Sample(seed, "animated") < 0.12f;
            return (
                colour,
                GlowType.AviationWarning,
                Lerp(0.55f, 0.78f, Sample(seed, "intensity")),
                animated ? Lerp(0.24f, 0.42f, Sample(seed, "rate")) : 0f,
                Sample(seed, $"phase:{ordinal}"),
                animated ? LightPattern.Strobe : LightPattern.Continuous);
        }

        bool utilityPulse = role == MegastationZoneRole.Utilities && Sample(seed, "animated") < 0.02f;
        return (
            colour,
            GlowType.AmbientMarker,
            role == MegastationZoneRole.Utilities
                ? Lerp(0.16f, 0.24f, Sample(seed, "intensity"))
                : Lerp(0.20f, 0.32f, Sample(seed, "intensity")),
            utilityPulse ? Lerp(0.18f, 0.30f, Sample(seed, "rate")) : 0f,
            utilityPulse ? Sample(seed, $"phase:{ordinal}") : 0f,
            utilityPulse ? LightPattern.SlowPulse : LightPattern.Continuous);
    }

    private static BoundaryFaceKey SelectSurfaceFace(
        MegastationZoneRole role,
        MegastationLightingSurfaceRegion region,
        MegastationSemanticZoningResult zoning,
        int seed,
        int ordinal)
    {
        BoundaryFaceKey[] ranked = region.Faces
            .OrderByDescending(face => SurfaceScore(role, zoning.SurfaceByFace[face].Metrics))
            .ThenBy(face => face)
            .ToArray();
        int selectionBand = Math.Min(
            ranked.Length,
            Math.Max(3, Math.Min(8, (int)MathF.Ceiling(ranked.Length * 0.35f))));
        int index = selectionBand <= 1
            ? 0
            : (int)(Sample(seed, $"surface:{ordinal}") * selectionBand) % selectionBand;
        return ranked[index];
    }

    private static float RegionScore(MegastationZoneRole role, MegastationLightingSurfaceRegion region)
        => role switch
        {
            MegastationZoneRole.Industrial => region.PhysicalArea * 0.00001f
                + (1f - region.Prominence) * 0.8f + region.Concavity * 0.6f,
            MegastationZoneRole.Logistics => region.PhysicalArea * 0.000012f
                + region.Exposure * 0.5f + region.Extremity * 0.3f,
            MegastationZoneRole.Utilities => region.Concavity * 1.4f
                + region.RelativeDepth * 1.2f + (1f - region.Prominence) * 0.5f,
            _ => region.Extremity * 1.5f + region.Prominence * 1.2f + region.Exposure,
        };

    private static float SurfaceScore(MegastationZoneRole role, MegastationSurfaceMetrics metrics)
        => role switch
        {
            MegastationZoneRole.Industrial => metrics.PhysicalArea * 0.00002f
                + (1f - metrics.Prominence) + metrics.ConcavityContext * 0.7f,
            MegastationZoneRole.Logistics => metrics.PhysicalArea * 0.000025f
                + metrics.ImmediateExposure * 0.5f,
            MegastationZoneRole.Utilities => metrics.ConcavityContext * 1.5f
                + metrics.RelativeDepth * 1.2f,
            _ => metrics.Extremity * 1.5f + metrics.Prominence * 1.2f
                + metrics.ImmediateExposure,
        };

    private static int ClusterCount(MegastationSemanticZone zone, MegastationLightingTuning tuning)
    {
        (float areaPerCluster, int maximum) = zone.Role switch
        {
            MegastationZoneRole.Industrial => (tuning.IndustrialAreaPerCluster, tuning.IndustrialMaximumClustersPerZone),
            MegastationZoneRole.Logistics => (tuning.LogisticsAreaPerCluster, tuning.LogisticsMaximumClustersPerZone),
            MegastationZoneRole.Utilities => (tuning.UtilitiesAreaPerCluster, tuning.UtilitiesMaximumClustersPerZone),
            _ => (tuning.StrategicAreaPerCluster, tuning.StrategicMaximumClustersPerZone),
        };
        float expected = zone.TotalPhysicalArea / areaPerCluster;
        if (expected < 0.35f)
            return 0;

        float varied = expected * Lerp(
            0.85f,
            1.15f,
            Sample(MegastationSeed.Derive(zone.Seed, AlgorithmKey), "cluster-density"));
        int whole = (int)MathF.Floor(varied);
        float remainder = varied - whole;
        int rounded = whole + (Sample(zone.Seed, "lighting-density-round") < remainder ? 1 : 0);
        return Math.Clamp(Math.Max(1, rounded), 1, maximum);
    }

    private static float MinimumClusterSeparation(
        MegastationZoneRole role,
        MegastationLightingTuning tuning)
        => role switch
        {
            MegastationZoneRole.Industrial => tuning.IndustrialMinimumClusterSeparation,
            MegastationZoneRole.Logistics => tuning.LogisticsMinimumClusterSeparation,
            MegastationZoneRole.Utilities => tuning.UtilitiesMinimumClusterSeparation,
            _ => tuning.StrategicMinimumClusterSeparation,
        };

    private static Vector3 ClusterCentre(IReadOnlyList<MegastationLightInstance> lights)
    {
        Vector3 sum = Vector3.Zero;
        foreach (MegastationLightInstance light in lights)
            sum += light.SurfacePosition;
        return sum / lights.Count;
    }

    private static int LampsPerCluster(MegastationZoneRole role, int seed)
    {
        int range = role switch
        {
            MegastationZoneRole.Industrial => 3,
            MegastationZoneRole.Logistics => 3,
            _ => 2,
        };
        int minimum = role is MegastationZoneRole.Industrial or MegastationZoneRole.Logistics ? 2 : 1;
        return minimum + (int)(Sample(seed, "lamp-count") * range) % range;
    }

    private static float WeightedMetric(
        IReadOnlyList<MegastationSemanticSurface> surfaces,
        float totalArea,
        Func<MegastationSurfaceMetrics, float> selector)
        => totalArea <= 0f
            ? 0f
            : surfaces.Sum(surface => selector(surface.Metrics) * surface.Metrics.PhysicalArea) / totalArea;

    private static int PlaneCoordinate(BoundaryFaceKey face) => face.Direction switch
    {
        GridDirection.PositiveX => face.X + 1,
        GridDirection.NegativeX => face.X,
        GridDirection.PositiveY => face.Y + 1,
        GridDirection.NegativeY => face.Y,
        GridDirection.PositiveZ => face.Z + 1,
        _ => face.Z,
    };

    private static string FaceIdentity(BoundaryFaceKey face)
        => $"{face.X},{face.Y},{face.Z},{(int)face.Direction}";

    private static int ZoneCount(MegastationSemanticZoningResult zoning, MegastationZoneRole role)
        => zoning.Zones.Count(zone => zone.Role == role);

    private static int LightCount(IReadOnlyList<MegastationLightInstance> lights, MegastationZoneRole role)
        => lights.Count(light => light.Role == role);

    private static int ClusterCount(
        IReadOnlyList<MegastationLightCluster> clusters,
        MegastationZoneRole role)
        => clusters.Count(cluster => cluster.Role == role);

    private static int ActiveRegionCount(
        IReadOnlyList<MegastationLightCluster> clusters,
        MegastationZoneRole role)
        => clusters
            .Where(cluster => cluster.Role == role)
            .Select(cluster => cluster.RegionIdentity)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static float EligibleArea(
        IReadOnlyList<MegastationLightingSurfaceRegion> regions,
        MegastationZoneRole role)
        => regions.Where(region => region.Role == role).Sum(region => region.PhysicalArea);

    private static float Sample(int seed, string key)
        => unchecked((uint)MegastationSeed.Derive(seed, key)) / (float)uint.MaxValue;

    private static float Lerp(float minimum, float maximum, float t)
        => minimum + (maximum - minimum) * t;
}
