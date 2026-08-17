using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationAttachmentMaskRect(
    BoundaryFaceKey Face,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV);

public sealed record MegastationAttachmentSurface(
    string StableId,
    string ZoneId,
    int ZoneSeed,
    MegastationZoneRole ZoneRole,
    GridDirection Direction,
    int PlaneGridCoordinate,
    float PlaneCoordinateMetres,
    Vector3 OutwardNormal,
    Vector3 TangentU,
    Vector3 TangentV,
    Vector3 PhysicalCentre,
    IReadOnlyList<BoundaryFaceKey> Faces,
    IReadOnlyList<MegastationAttachmentMaskRect> SupportMask,
    float PhysicalArea,
    Vector2 PhysicalExtents,
    float Prominence,
    float Exposure,
    float Concavity,
    float Extremity,
    Vector2 MaximumSupportedFootprint,
    float ExteriorClearanceDepth);

public sealed record MegastationAttachmentReservation(
    string PlacementIdentity,
    GridDirection Direction,
    float PlaneCoordinateMetres,
    Vector3 Normal,
    Vector3 TangentU,
    Vector3 TangentV,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV)
{
    public bool Contains(Vector3 surfacePosition, Vector3 surfaceNormal, float margin = 0f)
    {
        if (Vector3.Dot(Normal, surfaceNormal) < 0.999f)
            return false;
        if (MathF.Abs(Vector3.Dot(surfacePosition, Normal) - PlaneCoordinateMetres) > 0.1f)
            return false;
        float u = Vector3.Dot(surfacePosition, TangentU);
        float v = Vector3.Dot(surfacePosition, TangentV);
        return u >= MinU - margin && u <= MaxU + margin
            && v >= MinV - margin && v <= MaxV + margin;
    }
}

public sealed record MegastationAttachmentPlacement(
    string Identity,
    string SurfaceStableId,
    string ZoneId,
    MegastationZoneRole ZoneRole,
    string ModuleDefinitionId,
    string AttachmentPortId,
    int QuarterTurn,
    int ModuleSeed,
    Matrix Transform,
    Vector3 AabbMin,
    Vector3 AabbMax,
    MegastationAttachmentReservation Reservation);

public sealed record MegastationAttachmentDiagnostics(
    int CandidateSurfaceCount,
    int SelectedCandidateCount,
    int PlacedModuleCount,
    int RejectedSupportCount,
    int RejectedClearanceCount,
    int RejectedSemanticCount,
    int HabitationCount,
    int IndustrialCount,
    int LogisticsCount,
    int UtilitiesCount,
    int StrategicCount,
    int SuppressedWindowCount,
    int SuppressedLightCount,
    long PlanningMilliseconds,
    long ClearanceMilliseconds,
    IReadOnlyDictionary<string, int> ModuleFamilyCounts);

public sealed record MegastationAttachmentPlan(
    IReadOnlyList<MegastationAttachmentSurface> CandidateSurfaces,
    IReadOnlyList<MegastationAttachmentPlacement> Placements,
    IReadOnlyList<MegastationAttachmentReservation> Reservations,
    MegastationAttachmentDiagnostics Diagnostics);

public static class MegastationAttachmentTransform
{
    public static Matrix Solve(
        Vector3 sitePosition,
        Vector3 siteOutwardNormal,
        StationPort childAttachmentPort,
        int quarterTurn)
    {
        if (quarterTurn is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(quarterTurn));
        Vector3 normal = Vector3.Normalize(siteOutwardNormal);
        Quaternion align = RotationBetween(
            Vector3.Normalize(childAttachmentPort.OutwardNormal),
            -normal);
        Quaternion twist = Quaternion.CreateFromAxisAngle(-normal, quarterTurn * MathHelper.PiOver2);
        Quaternion rotation = Quaternion.Normalize(twist * align);
        Vector3 rotatedPort = Vector3.Transform(childAttachmentPort.LocalPosition, rotation);
        return Matrix.CreateFromQuaternion(rotation)
            * Matrix.CreateTranslation(sitePosition - rotatedPort);
    }

    private static Quaternion RotationBetween(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);
        if (dot >= 0.9999f) return Quaternion.Identity;
        if (dot <= -0.9999f)
        {
            Vector3 perpendicular = MathF.Abs(from.X) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(from, Vector3.UnitX))
                : Vector3.Normalize(Vector3.Cross(from, Vector3.UnitY));
            return Quaternion.CreateFromAxisAngle(perpendicular, MathF.PI);
        }
        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(dot, -1f, 1f)));
    }
}

public static class MegastationAttachmentPlanner
{
    private const string AlgorithmKey = "attachments:v1";
    private const float SupportMarginMetres = 1.0f;
    private const float MinimumSurfaceArea = 700f;
    private const float MinimumSurfaceSpan = 18f;
    private const float ContactTolerance = 0.02f;

    public static MegastationAttachmentPlan Plan(
        SliceGrid grid,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        MegastationAttachmentSurface[] surfaces = ExtractCandidateSurfaces(
            grid, topology, zoning, cancellationToken);
        var placements = new List<MegastationAttachmentPlacement>();
        var reservations = new List<MegastationAttachmentReservation>();
        var occupiedSecondaryBounds = new List<(Vector3 Min, Vector3 Max)>();
        int selected = 0;
        int rejectedSupport = 0;
        int rejectedClearance = 0;
        int rejectedSemantic = 0;
        long clearanceTicks = 0;

        foreach (MegastationAttachmentSurface surface in surfaces
                     .OrderByDescending(SurfaceScore)
                     .ThenBy(surface => surface.StableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int surfaceSeed = MegastationSeed.Derive(
                MegastationSeed.Derive(surface.ZoneSeed, AlgorithmKey),
                surface.StableId);
            if (Sample(surfaceSeed, "selected") >= SelectionProbability(surface))
                continue;
            selected++;

            StationModuleDefinition[] definitions = DefinitionsFor(surface.ZoneRole)
                .OrderBy(definition => definition.Id, StringComparer.Ordinal)
                .ToArray();
            if (definitions.Length == 0)
            {
                rejectedSemantic++;
                continue;
            }
            int definitionOffset = PositiveMod(
                MegastationSeed.Derive(surfaceSeed, "definition"), definitions.Length);
            bool placed = false;
            bool sawSupported = false;
            for (int definitionOrdinal = 0;
                 definitionOrdinal < definitions.Length && !placed;
                 definitionOrdinal++)
            {
                StationModuleDefinition definition = definitions[
                    (definitionOrdinal + definitionOffset) % definitions.Length];
                StationPort[] ports = definition.Ports
                    .Where(IsSafeAttachmentPort)
                    .OrderBy(port => port.Id, StringComparer.Ordinal)
                    .ToArray();
                if (ports.Length == 0)
                    continue;
                int portOffset = PositiveMod(
                    MegastationSeed.Derive(surfaceSeed, $"port:{definition.Id}"), ports.Length);
                for (int portOrdinal = 0; portOrdinal < ports.Length && !placed; portOrdinal++)
                {
                    StationPort port = ports[(portOrdinal + portOffset) % ports.Length];
                    int turnOffset = PositiveMod(
                        MegastationSeed.Derive(surfaceSeed, $"turn:{definition.Id}:{port.Id}"), 4);
                    for (int turnOrdinal = 0; turnOrdinal < 4 && !placed; turnOrdinal++)
                    {
                        int quarterTurn = (turnOrdinal + turnOffset) % 4;
                        Matrix transform = MegastationAttachmentTransform.Solve(
                            surface.PhysicalCentre,
                            surface.OutwardNormal,
                            port,
                            quarterTurn);
                        (Vector3 min, Vector3 max) = WorldAabb(transform, definition.BoundingBox);
                        if (!TryReservation(surface, min, max, out MegastationAttachmentReservation reservation))
                            continue;
                        sawSupported = true;
                        long clearanceStart = Stopwatch.GetTimestamp();
                        bool clear = HasExteriorClearance(grid, occupancy, min, max, surface.OutwardNormal)
                            && !occupiedSecondaryBounds.Any(bounds => Intersects(min, max, bounds.Min, bounds.Max));
                        clearanceTicks += Stopwatch.GetTimestamp() - clearanceStart;
                        if (!clear)
                            continue;

                        int moduleSeed = MegastationSeed.Derive(
                            surfaceSeed,
                            $"placement:0/{definition.Id}/{port.Id}/{quarterTurn}");
                        string identity = $"{surface.StableId}/placement:0/{definition.Id}:{port.Id}:{quarterTurn}";
                        reservation = reservation with { PlacementIdentity = identity };
                        placements.Add(new(
                            identity,
                            surface.StableId,
                            surface.ZoneId,
                            surface.ZoneRole,
                            definition.Id,
                            port.Id,
                            quarterTurn,
                            moduleSeed,
                            transform,
                            min,
                            max,
                            reservation));
                        reservations.Add(reservation);
                        occupiedSecondaryBounds.Add((min, max));
                        placed = true;
                    }
                }
            }
            if (!placed)
            {
                if (sawSupported) rejectedClearance++;
                else rejectedSupport++;
            }
        }

        stopwatch.Stop();
        MegastationAttachmentPlacement[] orderedPlacements = placements
            .OrderBy(placement => placement.Identity, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = new MegastationAttachmentDiagnostics(
            surfaces.Length,
            selected,
            orderedPlacements.Length,
            rejectedSupport,
            rejectedClearance,
            rejectedSemantic,
            CountRole(orderedPlacements, MegastationZoneRole.Habitation),
            CountRole(orderedPlacements, MegastationZoneRole.Industrial),
            CountRole(orderedPlacements, MegastationZoneRole.Logistics),
            CountRole(orderedPlacements, MegastationZoneRole.Utilities),
            CountRole(orderedPlacements, MegastationZoneRole.Strategic),
            0,
            0,
            stopwatch.ElapsedMilliseconds,
            (long)TimeSpan.FromSeconds(clearanceTicks / (double)Stopwatch.Frequency).TotalMilliseconds,
            orderedPlacements
                .GroupBy(placement => ModuleFamily(placement.ModuleDefinitionId), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        return new(surfaces, orderedPlacements, reservations.ToArray(), diagnostics);
    }

    public static MegastationAttachmentSurface[] ExtractCandidateSurfaces(
        SliceGrid grid,
        BoundaryTopology topology,
        MegastationSemanticZoningResult zoning,
        CancellationToken cancellationToken = default)
    {
        var eligible = zoning.Zones
            .Where(zone => zone.Role != MegastationZoneRole.Structural)
            .SelectMany(zone => zone.Faces.Select(face => (zone, face)))
            .OrderBy(pair => pair.face)
            .ToArray();
        var result = new List<MegastationAttachmentSurface>();
        foreach (var planeGroup in eligible.GroupBy(pair => (
                     pair.zone.Identity, pair.face.Direction, PlaneCoordinate(pair.face))))
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
                        if (byFace.ContainsKey(neighbour) && remaining.Remove(neighbour))
                            queue.Enqueue(neighbour);
                }

                MegastationSemanticZone zone = byFace[first];
                BoundaryFace firstFace = topology.FaceByKey[first];
                Vector3 p0 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[0]);
                Vector3 p1 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[1]);
                Vector3 p3 = BoundaryTopologyBuilder.Position(grid, firstFace.Vertices[3]);
                Vector3 tangentU = Vector3.Normalize(p1 - p0);
                Vector3 tangentV = Vector3.Normalize(p3 - p0);
                Vector3 normal = BoundaryTopologyBuilder.Normal(first.Direction);
                MegastationAttachmentMaskRect[] mask = component
                    .Select(face => MaskRect(grid, topology.FaceByKey[face], tangentU, tangentV))
                    .ToArray();
                float minU = mask.Min(rect => rect.MinU);
                float maxU = mask.Max(rect => rect.MaxU);
                float minV = mask.Min(rect => rect.MinV);
                float maxV = mask.Max(rect => rect.MaxV);
                float area = mask.Sum(rect => (rect.MaxU - rect.MinU) * (rect.MaxV - rect.MinV));
                Vector2 extents = new(maxU - minU, maxV - minV);
                if (area < MinimumSurfaceArea
                    || MathF.Min(extents.X, extents.Y) < MinimumSurfaceSpan)
                    continue;

                Vector3 desired = normal * Vector3.Dot(p0, normal)
                    + tangentU * ((minU + maxU) * 0.5f)
                    + tangentV * ((minV + maxV) * 0.5f);
                BoundaryFaceKey centreFace = component
                    .OrderBy(face => Vector3.DistanceSquared(FaceCentre(grid, topology.FaceByKey[face]), desired))
                    .ThenBy(face => face)
                    .First();
                Vector3 centre = FaceCentre(grid, topology.FaceByKey[centreFace]);
                string faceKey = string.Join('|', component.Select(FaceIdentity));
                int signature = MegastationSeed.Derive(zone.Seed, faceKey);
                string stableId = $"{zone.Identity}/{AlgorithmKey}/plane:{first.Direction}:" +
                    $"{PlaneCoordinate(first)}:{component.Min.X},{component.Min.Y},{component.Min.Z}:" +
                    $"{unchecked((uint)signature):X8}";
                result.Add(new(
                    stableId,
                    zone.Identity,
                    zone.Seed,
                    zone.Role,
                    first.Direction,
                    PlaneCoordinate(first),
                    Vector3.Dot(p0, normal),
                    normal,
                    tangentU,
                    tangentV,
                    centre,
                    component.ToArray(),
                    mask,
                    area,
                    extents,
                    zone.Metrics.Prominence,
                    zone.Metrics.ImmediateExposure,
                    zone.Metrics.ConcavityContext,
                    zone.Metrics.Extremity,
                    extents - new Vector2(SupportMarginMetres * 2f),
                    MaximumGridDimension(grid)));
            }
        }
        return result.OrderBy(surface => surface.StableId, StringComparer.Ordinal).ToArray();
    }

    public static List<PlacedModule> CreatePlacedModules(MegastationAttachmentPlan plan)
    {
        var definitions = SafeDefinitions()
            .ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        var modules = new List<PlacedModule>(plan.Placements.Count);
        foreach (MegastationAttachmentPlacement placement in plan.Placements)
        {
            StationModuleDefinition definition = definitions[placement.ModuleDefinitionId];
            StationPort port = definition.Ports.Single(candidate => candidate.Id == placement.AttachmentPortId);
            modules.Add(new PlacedModule
            {
                Definition = definition,
                Transform = placement.Transform,
                Seed = placement.ModuleSeed,
                ChamferDepth = StationGenerator.ChamferDepthForSeed(placement.ModuleSeed),
                Depth = 1,
                AabbMin = placement.AabbMin,
                AabbMax = placement.AabbMax,
                AttachmentPort = port,
            });
        }
        return modules;
    }

    public static MegastationWindowPlan SuppressWindows(
        MegastationWindowPlan plan,
        IReadOnlyList<MegastationAttachmentReservation> reservations,
        out int suppressed)
    {
        MegastationWindowInstance[] windows = plan.Windows
            .Where(window => !reservations.Any(reservation =>
                OverlapsWindow(reservation, window)))
            .ToArray();
        suppressed = plan.Windows.Count - windows.Length;
        HashSet<string> survivingBlocks = windows.Select(window => window.BlockIdentity).ToHashSet(StringComparer.Ordinal);
        MegastationWindowDiagnostics diagnostics = plan.Diagnostics with
        {
            BlockCount = survivingBlocks.Count,
            WindowCount = windows.Length,
            LitWindowCount = windows.Count(window => window.State == MegastationWindowState.Lit),
            DimWindowCount = windows.Count(window => window.State == MegastationWindowState.Dim),
            DarkWindowCount = windows.Count(window => window.State == MegastationWindowState.Dark),
        };
        return plan with
        {
            Blocks = plan.Blocks.Where(block => survivingBlocks.Contains(block.Identity)).ToArray(),
            Windows = windows,
            Diagnostics = diagnostics,
        };
    }

    public static MegastationLightPlan SuppressLights(
        MegastationLightPlan plan,
        IReadOnlyList<MegastationAttachmentReservation> reservations,
        out int suppressed)
    {
        MegastationLightInstance[] lights = plan.Lights
            .Where(light => !reservations.Any(reservation =>
                reservation.Contains(light.SurfacePosition, light.Normal, 0.5f)))
            .ToArray();
        suppressed = plan.Lights.Count - lights.Length;
        MegastationLightCluster[] clusters = plan.Clusters
            .Select(cluster => cluster with
            {
                LightCount = lights.Count(light => light.ClusterIdentity == cluster.Identity),
            })
            .Where(cluster => cluster.LightCount > 0)
            .ToArray();
        MegastationLightingDiagnostics d = plan.Diagnostics with
        {
            IndustrialLightCount = lights.Count(light => light.Role == MegastationZoneRole.Industrial),
            LogisticsLightCount = lights.Count(light => light.Role == MegastationZoneRole.Logistics),
            UtilitiesLightCount = lights.Count(light => light.Role == MegastationZoneRole.Utilities),
            StrategicLightCount = lights.Count(light => light.Role == MegastationZoneRole.Strategic),
            IndustrialClusterCount = clusters.Count(cluster => cluster.Role == MegastationZoneRole.Industrial),
            LogisticsClusterCount = clusters.Count(cluster => cluster.Role == MegastationZoneRole.Logistics),
            UtilitiesClusterCount = clusters.Count(cluster => cluster.Role == MegastationZoneRole.Utilities),
            StrategicClusterCount = clusters.Count(cluster => cluster.Role == MegastationZoneRole.Strategic),
            ClusterCount = clusters.Length,
            SteadyLightCount = lights.Count(light => light.Pattern == LightPattern.Continuous),
            AnimatedLightCount = lights.Count(light => light.Pattern != LightPattern.Continuous),
        };
        return plan with { Clusters = clusters, Lights = lights, Diagnostics = d };
    }

    public static MegastationAttachmentPlan WithSuppressionCounts(
        MegastationAttachmentPlan plan,
        int suppressedWindows,
        int suppressedLights)
        => plan with
        {
            Diagnostics = plan.Diagnostics with
            {
                SuppressedWindowCount = suppressedWindows,
                SuppressedLightCount = suppressedLights,
            },
        };

    public static bool ContainsFootprint(
        MegastationAttachmentSurface surface,
        float minU,
        float maxU,
        float minV,
        float maxV,
        float margin = SupportMarginMetres)
    {
        minU -= margin;
        maxU += margin;
        minV -= margin;
        maxV += margin;
        float required = (maxU - minU) * (maxV - minV);
        float covered = 0f;
        foreach (MegastationAttachmentMaskRect rect in surface.SupportMask)
        {
            float overlapU = MathF.Max(0f, MathF.Min(maxU, rect.MaxU) - MathF.Max(minU, rect.MinU));
            float overlapV = MathF.Max(0f, MathF.Min(maxV, rect.MaxV) - MathF.Max(minV, rect.MinV));
            covered += overlapU * overlapV;
        }
        return covered >= required - MathF.Max(0.001f, required * 0.0001f);
    }

    public static bool HasExteriorClearance(
        SliceGrid grid,
        StructuralOccupancy occupancy,
        Vector3 moduleMin,
        Vector3 moduleMax,
        Vector3 outwardNormal)
    {
        Vector3 min = moduleMin + outwardNormal * ContactTolerance;
        Vector3 max = moduleMax + outwardNormal * ContactTolerance;
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            if (!occupancy.IsOccupied(x, y, z)) continue;
            Vector3 cellMin = new(
                grid.GetCellMinimum(GridAxis.X, x),
                grid.GetCellMinimum(GridAxis.Y, y),
                grid.GetCellMinimum(GridAxis.Z, z));
            Vector3 cellMax = new(
                grid.GetCellMaximum(GridAxis.X, x),
                grid.GetCellMaximum(GridAxis.Y, y),
                grid.GetCellMaximum(GridAxis.Z, z));
            if (Intersects(min, max, cellMin, cellMax))
                return false;
        }
        return true;
    }

    public static IReadOnlyList<StationModuleDefinition> SafeDefinitions()
        => Enum.GetValues<MegastationZoneRole>()
            .SelectMany(DefinitionsFor)
            .DistinctBy(definition => definition.Id)
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<StationModuleDefinition> DefinitionsFor(MegastationZoneRole role)
        => role switch
        {
            MegastationZoneRole.Habitation =>
                [StationModuleRegistry.HabBlockLarge, StationModuleRegistry.HabBlock,
                 StationModuleRegistry.ScienceBlock, StationModuleRegistry.HabBlockOctagonal,
                 StationModuleRegistry.ScienceBlockOctagonal],
            MegastationZoneRole.Industrial =>
                [StationModuleRegistry.IndustrialBlockLarge, StationModuleRegistry.IndustrialBlock,
                 StationModuleRegistry.ConnectorLongLarge, StationModuleRegistry.ConnectorShort],
            MegastationZoneRole.Logistics =>
                [StationModuleRegistry.CargoBayLarge, StationModuleRegistry.CargoBay,
                 StationModuleRegistry.ConnectorLongLarge, StationModuleRegistry.ConnectorShort],
            MegastationZoneRole.Utilities =>
                [StationModuleRegistry.IndustrialBlock, StationModuleRegistry.ConnectorShort,
                 StationModuleRegistry.ConnectorLong],
            MegastationZoneRole.Strategic =>
                [StationModuleRegistry.ScienceBlock, StationModuleRegistry.ScienceBlockOctagonal],
            _ => [],
        };

    private static bool TryReservation(
        MegastationAttachmentSurface surface,
        Vector3 min,
        Vector3 max,
        out MegastationAttachmentReservation reservation)
    {
        Vector3[] corners = AabbCorners(min, max);
        float minU = corners.Min(corner => Vector3.Dot(corner, surface.TangentU));
        float maxU = corners.Max(corner => Vector3.Dot(corner, surface.TangentU));
        float minV = corners.Min(corner => Vector3.Dot(corner, surface.TangentV));
        float maxV = corners.Max(corner => Vector3.Dot(corner, surface.TangentV));
        if (!ContainsFootprint(surface, minU, maxU, minV, maxV))
        {
            reservation = null!;
            return false;
        }
        reservation = new(
            string.Empty,
            surface.Direction,
            surface.PlaneCoordinateMetres,
            surface.OutwardNormal,
            surface.TangentU,
            surface.TangentV,
            minU,
            maxU,
            minV,
            maxV);
        return true;
    }

    private static bool OverlapsWindow(
        MegastationAttachmentReservation reservation,
        MegastationWindowInstance window)
    {
        if (Vector3.Dot(reservation.Normal, window.Normal) < 0.999f
            || MathF.Abs(Vector3.Dot(window.Centre, reservation.Normal)
                - reservation.PlaneCoordinateMetres) > 0.1f)
            return false;

        Vector3 right = Vector3.Normalize(Vector3.Cross(window.Up, window.Normal));
        Vector3 halfRight = right * (window.Width * 0.5f);
        Vector3 halfUp = window.Up * (window.Height * 0.5f);
        Vector3[] corners =
        [
            window.Centre - halfRight - halfUp,
            window.Centre + halfRight - halfUp,
            window.Centre + halfRight + halfUp,
            window.Centre - halfRight + halfUp,
        ];
        float minU = corners.Min(point => Vector3.Dot(point, reservation.TangentU));
        float maxU = corners.Max(point => Vector3.Dot(point, reservation.TangentU));
        float minV = corners.Min(point => Vector3.Dot(point, reservation.TangentV));
        float maxV = corners.Max(point => Vector3.Dot(point, reservation.TangentV));
        return minU < reservation.MaxU && maxU > reservation.MinU
            && minV < reservation.MaxV && maxV > reservation.MinV;
    }

    private static bool IsSafeAttachmentPort(StationPort port)
        => !port.IsDocking;

    private static float SelectionProbability(MegastationAttachmentSurface surface)
    {
        float areaPerPlacement = surface.ZoneRole switch
        {
            MegastationZoneRole.Habitation => 45_000f,
            MegastationZoneRole.Industrial => 32_000f,
            MegastationZoneRole.Logistics => 42_000f,
            MegastationZoneRole.Utilities => 95_000f,
            MegastationZoneRole.Strategic => 150_000f,
            _ => float.MaxValue,
        };
        float areaProbability = 1f - MathF.Exp(-surface.PhysicalArea / areaPerPlacement);
        float density = surface.ZoneRole switch
        {
            MegastationZoneRole.Habitation => 0.33f,
            MegastationZoneRole.Industrial => 0.44f,
            MegastationZoneRole.Logistics => 0.38f,
            MegastationZoneRole.Utilities => 0.18f,
            MegastationZoneRole.Strategic => 0.14f,
            _ => 0f,
        };
        float probability = areaProbability * density;
        float topologyBias = surface.ZoneRole switch
        {
            MegastationZoneRole.Habitation => 0.7f + 0.5f * surface.Prominence,
            MegastationZoneRole.Industrial => 0.8f + 0.4f * (1f - surface.Prominence),
            MegastationZoneRole.Logistics => 0.8f + 0.3f * surface.Exposure,
            MegastationZoneRole.Utilities => 0.6f + 0.5f * surface.Concavity,
            MegastationZoneRole.Strategic => 0.5f + 0.8f * surface.Extremity,
            _ => 0f,
        };
        return Math.Clamp(probability * topologyBias, 0f, 0.65f);
    }

    private static float SurfaceScore(MegastationAttachmentSurface surface)
        => surface.PhysicalArea * (0.6f + surface.Exposure * 0.2f + surface.Extremity * 0.2f);

    private static (Vector3 Min, Vector3 Max) WorldAabb(Matrix transform, Vector3 size)
    {
        Vector3 half = size * 0.5f;
        Vector3[] corners = AabbCorners(-half, half)
            .Select(corner => Vector3.Transform(corner, transform))
            .ToArray();
        return (new(
            corners.Min(corner => corner.X),
            corners.Min(corner => corner.Y),
            corners.Min(corner => corner.Z)),
            new(
            corners.Max(corner => corner.X),
            corners.Max(corner => corner.Y),
            corners.Max(corner => corner.Z)));
    }

    private static Vector3[] AabbCorners(Vector3 min, Vector3 max) =>
    [
        new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
        new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
        new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
        new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z),
    ];

    private static bool Intersects(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        => aMin.X < bMax.X - ContactTolerance && aMax.X > bMin.X + ContactTolerance
            && aMin.Y < bMax.Y - ContactTolerance && aMax.Y > bMin.Y + ContactTolerance
            && aMin.Z < bMax.Z - ContactTolerance && aMax.Z > bMin.Z + ContactTolerance;

    private static MegastationAttachmentMaskRect MaskRect(
        SliceGrid grid,
        BoundaryFace face,
        Vector3 tangentU,
        Vector3 tangentV)
    {
        Vector3[] positions = face.Vertices
            .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex))
            .ToArray();
        return new(
            face.Key,
            positions.Min(position => Vector3.Dot(position, tangentU)),
            positions.Max(position => Vector3.Dot(position, tangentU)),
            positions.Min(position => Vector3.Dot(position, tangentV)),
            positions.Max(position => Vector3.Dot(position, tangentV)));
    }

    private static Vector3 FaceCentre(SliceGrid grid, BoundaryFace face)
        => face.Vertices
            .Select(vertex => BoundaryTopologyBuilder.Position(grid, vertex))
            .Aggregate(Vector3.Zero, (sum, position) => sum + position) / 4f;

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

    private static int CountRole(
        IReadOnlyList<MegastationAttachmentPlacement> placements,
        MegastationZoneRole role)
        => placements.Count(placement => placement.ZoneRole == role);

    private static string ModuleFamily(string definitionId)
    {
        StationModuleDefinition definition = SafeDefinitions()
            .Single(candidate => candidate.Id == definitionId);
        return definition.Category;
    }

    private static int PositiveMod(int value, int modulus)
        => (int)((uint)value % (uint)modulus);

    private static float Sample(int seed, string key)
        => unchecked((uint)MegastationSeed.Derive(seed, key)) / (float)uint.MaxValue;

    private static float MaximumGridDimension(SliceGrid grid)
        => MathF.Max(grid.Dimension(GridAxis.X),
            MathF.Max(grid.Dimension(GridAxis.Y), grid.Dimension(GridAxis.Z)));
}
