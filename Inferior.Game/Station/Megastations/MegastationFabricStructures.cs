using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationFabricArchetype
{
    UtilityHall,
    SteppedBlock,
    Warehouse,
    TechnicalTower,
    MachineryBlock,
    ServiceCompound,
}

public enum MegastationFabricPattern
{
    LooseField,
    Rows,
    Cluster,
    EdgeAssociated,
    G1Associated,
}

public sealed record MegastationFabricInstance(
    string Identity, int Seed, MegastationFabricArchetype Archetype,
    MegastationFabricPattern Pattern, string SurfaceStableId, string ZoneId,
    MegastationZoneRole ZoneRole, Vector3 SurfacePosition, Vector3 Normal,
    Vector3 TangentU, Vector3 TangentV, float MinU, float MaxU, float MinV, float MaxV,
    float Width, float Length, float Height, int QuarterTurn,
    Color PrimaryColour, Color SecondaryColour, Color AccentColour,
    Vector3 AabbMin, Vector3 AabbMax,
    MegastationChannelAssociationKind ChannelAssociation = MegastationChannelAssociationKind.Independent,
    string? ChannelFeatureIdentity = null);

public sealed record MegastationFabricRegionSummary(
    string SurfaceStableId, string ZoneId, MegastationZoneRole Role,
    GridDirection Direction, Vector3 Centre, int StructureCount);

public sealed record MegastationFabricDebugFootprint(
    string Identity, MegastationFabricArchetype Archetype, Vector3 Normal,
    Vector3 TangentU, Vector3 TangentV, float PlaneCoordinateMetres,
    float MinU, float MaxU, float MinV, float MaxV, bool Accepted);

public sealed record MegastationFabricDiagnostics(
    float EligibleArea, int EligibleRegionCount, int CandidateCount, int AcceptedCount,
    int ExactMaskRejectCount, int G1RejectCount, int WindowRejectCount, int LightRejectCount,
    int G2RejectCount, int MegaGreebleRejectCount, int SelfRejectCount,
    int DensityRejectCount, int StructuralCollisionRejectCount,
    IReadOnlyDictionary<MegastationFabricArchetype, int> ByArchetype,
    IReadOnlyDictionary<MegastationZoneRole, int> ByRole,
    IReadOnlyDictionary<MegastationFabricPattern, int> ByPattern,
    float MinimumWidth, float MedianWidth, float MaximumWidth,
    float MinimumLength, float MedianLength, float MaximumLength,
    float MinimumHeight, float MedianHeight, float MaximumHeight,
    int VisibleVertexCount, int VisibleTriangleCount, long VisibleMeshBytes,
    int ShadowVertexCount, int ShadowTriangleCount, long ShadowMeshBytes,
    long PlanningMilliseconds, long MeshBuildMilliseconds,
    int OwnedTextureDelta, int GpuBufferDelta, string PlanSignature,
    IReadOnlyList<MegastationFabricRegionSummary> DensestRegions,
    int IndependentStructureCount = 0,
    int ChannelRowStructureCount = 0,
    int ChannelClusterStructureCount = 0,
    int ChannelNodeStructureCount = 0,
    int ChannelEndpointStructureCount = 0,
    int RejectedChannelAwareAttemptCount = 0);

public sealed record MegastationFabricPlan(
    IReadOnlyList<MegastationFabricInstance> Instances,
    MegastationFabricDiagnostics Diagnostics,
    IReadOnlyList<MegastationFabricDebugFootprint> DebugFootprints);

public sealed record MegastationFabricMeshBuildResult(
    StationModuleMesh Mesh, MegastationFabricDiagnostics Diagnostics);

public static class MegastationFabricPlanner
{
    private const string AlgorithmKey = "fabric-structures:v1";
    private const float CellSize = 46f;

    private sealed record Candidate(
        string Identity, int Seed, MegastationPlanarRegion Region,
        MegastationFabricArchetype Archetype, MegastationFabricPattern Pattern,
        float U, float V, float MinU, float MaxU, float MinV, float MaxV,
        float Width, float Length, float Height, int QuarterTurn,
        float Priority, Vector3 Position, Vector3 AabbMin, Vector3 AabbMax,
        MegastationChannelAssociationKind ChannelAssociation,
        string? ChannelFeatureIdentity);

    public static MegastationFabricPlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        MegastationMegaGreeblePlan megaGreeble,
        StructuralOccupancy occupancy,
        CancellationToken cancellationToken = default)
        => PlanCore(regions, attachments, windows, lights, infrastructure, megaGreeble,
            occupancy, null, cancellationToken);

    public static MegastationFabricPlan Plan(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        MegastationMegaGreeblePlan megaGreeble,
        StructuralOccupancy occupancy,
        MegastationServiceChannelPlan serviceChannels,
        CancellationToken cancellationToken = default)
        => PlanCore(regions, attachments, windows, lights, infrastructure, megaGreeble,
            occupancy, serviceChannels, cancellationToken);

    private static MegastationFabricPlan PlanCore(
        IReadOnlyList<MegastationPlanarRegion> regions,
        MegastationAttachmentPlan attachments,
        MegastationWindowPlan windows,
        MegastationLightPlan lights,
        MegastationInfrastructurePlan infrastructure,
        MegastationMegaGreeblePlan megaGreeble,
        StructuralOccupancy occupancy,
        MegastationServiceChannelPlan? serviceChannels,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        MegastationPlanarRegion[] eligible = regions
            .Where(r => r.ZoneRole != MegastationZoneRole.Structural
                && r.PhysicalExtents.X >= 12f && r.PhysicalExtents.Y >= 12f)
            .OrderBy(r => r.StableId, StringComparer.Ordinal).ToArray();
        var candidates = new List<Candidate>();
        int exact = 0, density = 0, rejectedChannelAware = 0;

        foreach (MegastationPlanarRegion region in eligible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int surfaceSeed = MegastationSeed.Derive(
                MegastationSeed.Derive(region.ZoneSeed, AlgorithmKey), region.StableId);
            int firstU = (int)MathF.Floor(region.MinU / CellSize);
            int lastU = (int)MathF.Ceiling(region.MaxU / CellSize) - 1;
            int firstV = (int)MathF.Floor(region.MinV / CellSize);
            int lastV = (int)MathF.Ceiling(region.MaxV / CellSize) - 1;
            for (int cv = firstV; cv <= lastV; cv++)
            for (int cu = firstU; cu <= lastU; cu++)
            {
                int cellSeed = MegastationSeed.Derive(surfaceSeed, $"cell:{cu}:{cv}");
                int districtU = FloorDiv(cu, 4), districtV = FloorDiv(cv, 4);
                int districtSeed = MegastationSeed.Derive(surfaceSeed, $"district:{districtU}:{districtV}");
                MegastationFabricPattern pattern = SelectPattern(region.ZoneRole, districtSeed);
                float districtStrength = DistrictStrength(districtSeed, pattern);
                float threshold = Math.Clamp(RoleDensity(region.ZoneRole)
                    * TopologySuitability(region) * districtStrength, 0f, .92f);
                if (Sample(cellSeed, "selected") >= threshold) { density++; continue; }

                MegastationFabricArchetype archetype = SelectArchetype(
                    region.ZoneRole, MegastationSeed.Derive(cellSeed, "archetype"));
                (float width, float length, float height) = Dimensions(archetype, cellSeed);
                int turn = PositiveMod(MegastationSeed.Derive(cellSeed, "quarter-turn"), 2);
                if (turn != 0) (width, length) = (length, width);
                (float jitterU, float jitterV) = PatternJitter(pattern, cellSeed, cu, cv, districtU, districtV);
                float u = (cu + .5f) * CellSize + jitterU;
                float v = (cv + .5f) * CellSize + jitterV;
                bool nearG1 = NearG1(region, u, v, attachments);
                if (pattern == MegastationFabricPattern.G1Associated && !nearG1
                    && Sample(cellSeed, "g1-associated-filter") > .16f)
                { density++; continue; }
                if (nearG1 && Sample(cellSeed, "g1-associated-promote") < .38f)
                    pattern = MegastationFabricPattern.G1Associated;
                float originalU = u, originalV = v;
                float originalWidth = width, originalLength = length;
                int originalTurn = turn;
                MegastationChannelAssociationKind association =
                    MegastationChannelAssociationKind.Independent;
                string? channelFeatureIdentity = null;
                if (serviceChannels is not null
                    && MegastationChannelComposition.TryPlace(
                        region, serviceChannels, cellSeed, u, v,
                        MathF.Max(width, length), MathF.Min(width, length),
                        ChannelAllocation(region.ZoneRole), true, out var channelPlacement))
                {
                    u = channelPlacement.U;
                    v = channelPlacement.V;
                    association = channelPlacement.Kind;
                    channelFeatureIdentity = channelPlacement.FeatureIdentity;
                    float major = MathF.Max(width, length);
                    float minor = MathF.Min(width, length);
                    width = channelPlacement.AlongU ? major : minor;
                    length = channelPlacement.AlongU ? minor : major;
                    turn = channelPlacement.AlongU ? 0 : 1;
                    pattern = association == MegastationChannelAssociationKind.ChannelEdge
                        ? (Sample(cellSeed, "sc3:edge-pattern") < .62f
                            ? MegastationFabricPattern.Rows : MegastationFabricPattern.Cluster)
                        : MegastationFabricPattern.Cluster;
                }
                float minU = u - width * .5f, maxU = u + width * .5f;
                float minV = v - length * .5f, maxV = v + length * .5f;
                if (!MegastationPlanarRegionExtractor.ContainsFootprint(
                        region, minU, maxU, minV, maxV, 1.25f)
                    || (serviceChannels is not null
                        && MegastationChannelComposition.OverlapsReserved(
                            region, serviceChannels, minU, maxU, minV, maxV, 3.6f)))
                {
                    if (association != MegastationChannelAssociationKind.Independent)
                    {
                        rejectedChannelAware++;
                        association = MegastationChannelAssociationKind.Independent;
                        channelFeatureIdentity = null;
                        u = originalU;
                        v = originalV;
                        width = originalWidth;
                        length = originalLength;
                        turn = originalTurn;
                        minU = u - width * .5f; maxU = u + width * .5f;
                        minV = v - length * .5f; maxV = v + length * .5f;
                    }
                    if (!MegastationPlanarRegionExtractor.ContainsFootprint(
                            region, minU, maxU, minV, maxV, 1.25f)
                        || (serviceChannels is not null
                            && MegastationChannelComposition.OverlapsReserved(
                                region, serviceChannels, minU, maxU, minV, maxV, 3.6f)))
                    { exact++; continue; }
                }
                Vector3 position = region.OutwardNormal * region.PlaneCoordinateMetres
                    + region.TangentU * u + region.TangentV * v;
                (Vector3 aabbMin, Vector3 aabbMax) = Bounds(position, region.OutwardNormal,
                    region.TangentU, region.TangentV, width, length, height);
                string identity = $"{region.StableId}/{AlgorithmKey}/cell:{cu}:{cv}";
                float priority = threshold + .15f * Sample(cellSeed, "priority");
                candidates.Add(new(identity, cellSeed, region, archetype, pattern,
                    u, v, minU, maxU, minV, maxV, width, length, height, turn,
                    priority, position, aabbMin, aabbMax, association,
                    channelFeatureIdentity));
            }
        }

        var accepted = new List<Candidate>();
        int g1 = 0, window = 0, light = 0, g2 = 0, mega = 0, self = 0, structural = 0;
        foreach (Candidate c in candidates
                     .OrderBy(c => c.ChannelAssociation ==
                         MegastationChannelAssociationKind.Independent ? 1 : 0)
                     .ThenByDescending(c => c.Priority)
                     .ThenBy(c => c.Identity, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OverlapsG1(c, attachments)) { g1++; continue; }
            if (OverlapsWindows(c, windows.Windows, 1.5f)) { window++; continue; }
            if (OverlapsLights(c, lights.Lights, 3f)) { light++; continue; }
            if (infrastructure.Clusters.Any(x => Intersects(c.AabbMin, c.AabbMax, x.AabbMin, x.AabbMax)))
            { g2++; continue; }
            if (megaGreeble.Instances.Any(x => Intersects(c.AabbMin, c.AabbMax, MegaBounds(x).min, MegaBounds(x).max)))
            { mega++; continue; }
            if (accepted.Any(x => IntersectsInflated(c.AabbMin, c.AabbMax, x.AabbMin, x.AabbMax, 2f)))
            { self++; continue; }
            if (CollidesStructuralMass(c, occupancy)) { structural++; continue; }
            accepted.Add(c);
        }

        MegastationFabricInstance[] instances = accepted
            .OrderBy(c => c.Identity, StringComparer.Ordinal)
            .Select(c =>
            {
                (Color primary, Color secondary, Color accent) = Palette(c.Seed);
                return new MegastationFabricInstance(c.Identity, c.Seed, c.Archetype, c.Pattern,
                    c.Region.StableId, c.Region.ZoneId, c.Region.ZoneRole, c.Position,
                    c.Region.OutwardNormal, c.Region.TangentU, c.Region.TangentV,
                    c.MinU, c.MaxU, c.MinV, c.MaxV, c.Width, c.Length, c.Height, c.QuarterTurn,
                    primary, secondary, accent, c.AabbMin, c.AabbMax,
                    c.ChannelAssociation, c.ChannelFeatureIdentity);
            }).ToArray();
        timer.Stop();
        float[] widths = instances.Select(i => i.Width).Order().ToArray();
        float[] lengths = instances.Select(i => i.Length).Order().ToArray();
        float[] heights = instances.Select(i => i.Height).Order().ToArray();
        MegastationFabricRegionSummary[] dense = instances.GroupBy(i => i.SurfaceStableId)
            .Select(group =>
            {
                MegastationFabricInstance first = group.First();
                return new MegastationFabricRegionSummary(first.SurfaceStableId, first.ZoneId,
                    first.ZoneRole, eligible.First(r => r.StableId == first.SurfaceStableId).Direction,
                    new Vector3(group.Average(i => i.SurfacePosition.X),
                        group.Average(i => i.SurfacePosition.Y), group.Average(i => i.SurfacePosition.Z)),
                    group.Count());
            }).OrderByDescending(x => x.StructureCount).ThenBy(x => x.SurfaceStableId).Take(3).ToArray();
        var diagnostics = new MegastationFabricDiagnostics(
            eligible.Sum(r => r.PhysicalArea), eligible.Length, candidates.Count + density + exact,
            instances.Length, exact, g1, window, light, g2, mega, self, density, structural,
            Enum.GetValues<MegastationFabricArchetype>().ToDictionary(a => a, a => instances.Count(i => i.Archetype == a)),
            Enum.GetValues<MegastationZoneRole>().ToDictionary(r => r, r => instances.Count(i => i.ZoneRole == r)),
            Enum.GetValues<MegastationFabricPattern>().ToDictionary(p => p, p => instances.Count(i => i.Pattern == p)),
            Min(widths), Median(widths), Max(widths), Min(lengths), Median(lengths), Max(lengths),
            Min(heights), Median(heights), Max(heights), 0, 0, 0, 0, 0, 0,
            timer.ElapsedMilliseconds, 0, 0, 0, Signature(instances), dense,
            instances.Count(i => i.ChannelAssociation == MegastationChannelAssociationKind.Independent),
            instances.Count(i => i.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge
                && i.Pattern == MegastationFabricPattern.Rows),
            instances.Count(i => i.ChannelAssociation == MegastationChannelAssociationKind.ChannelEdge
                && i.Pattern != MegastationFabricPattern.Rows),
            instances.Count(i => i.ChannelAssociation == MegastationChannelAssociationKind.ChannelNode),
            instances.Count(i => i.ChannelAssociation == MegastationChannelAssociationKind.ChannelEndpoint),
            rejectedChannelAware
                + candidates.Count(candidate => candidate.ChannelAssociation !=
                    MegastationChannelAssociationKind.Independent)
                - instances.Count(instance => instance.ChannelAssociation !=
                    MegastationChannelAssociationKind.Independent));
#if DEBUG
        HashSet<string> acceptedIdentities = instances.Select(i => i.Identity)
            .ToHashSet(StringComparer.Ordinal);
        MegastationFabricDebugFootprint[] debugFootprints = candidates.Select(c =>
            new MegastationFabricDebugFootprint(
            c.Identity, c.Archetype, c.Region.OutwardNormal, c.Region.TangentU,
            c.Region.TangentV, c.Region.PlaneCoordinateMetres, c.MinU, c.MaxU,
            c.MinV, c.MaxV, acceptedIdentities.Contains(c.Identity))).ToArray();
#else
        MegastationFabricDebugFootprint[] debugFootprints = [];
#endif
        return new(instances, diagnostics, debugFootprints);
    }

    private static MegastationFabricPattern SelectPattern(MegastationZoneRole role, int seed)
    {
        int n = PositiveMod(seed, 100);
        if (role is MegastationZoneRole.Industrial or MegastationZoneRole.Utilities)
            return n < 24 ? MegastationFabricPattern.Rows : n < 52 ? MegastationFabricPattern.Cluster
                : n < 72 ? MegastationFabricPattern.G1Associated : n < 86
                    ? MegastationFabricPattern.EdgeAssociated : MegastationFabricPattern.LooseField;
        return n < 35 ? MegastationFabricPattern.LooseField : n < 55
            ? MegastationFabricPattern.Rows : n < 72 ? MegastationFabricPattern.Cluster
                : n < 86 ? MegastationFabricPattern.EdgeAssociated : MegastationFabricPattern.G1Associated;
    }

    private static MegastationFabricArchetype SelectArchetype(MegastationZoneRole role, int seed)
    {
        int n = PositiveMod(seed, 100);
        return role switch
        {
            MegastationZoneRole.Habitation => n < 32 ? MegastationFabricArchetype.UtilityHall
                : n < 56 ? MegastationFabricArchetype.SteppedBlock : n < 77
                    ? MegastationFabricArchetype.Warehouse : n < 90
                        ? MegastationFabricArchetype.TechnicalTower : MegastationFabricArchetype.ServiceCompound,
            MegastationZoneRole.Strategic => n < 42 ? MegastationFabricArchetype.TechnicalTower
                : n < 70 ? MegastationFabricArchetype.MachineryBlock : MegastationFabricArchetype.SteppedBlock,
            MegastationZoneRole.Logistics => n < 38 ? MegastationFabricArchetype.Warehouse
                : n < 64 ? MegastationFabricArchetype.UtilityHall : n < 84
                    ? MegastationFabricArchetype.ServiceCompound : MegastationFabricArchetype.MachineryBlock,
            _ => (MegastationFabricArchetype)PositiveMod(seed, 6),
        };
    }

    private static (float width, float length, float height) Dimensions(MegastationFabricArchetype a, int seed)
    {
        float A(string key) => Sample(seed, key);
        return a switch
        {
            MegastationFabricArchetype.UtilityHall => (Lerp(16, 30, A("w")), Lerp(28, 58, A("l")), Lerp(8, 18, A("h"))),
            MegastationFabricArchetype.SteppedBlock => (Lerp(16, 34, A("w")), Lerp(16, 38, A("l")), Lerp(12, 30, A("h"))),
            MegastationFabricArchetype.Warehouse => (Lerp(22, 40, A("w")), Lerp(30, 62, A("l")), Lerp(9, 18, A("h"))),
            MegastationFabricArchetype.TechnicalTower => (Lerp(10, 22, A("w")), Lerp(10, 24, A("l")), Lerp(24, 58, A("h"))),
            MegastationFabricArchetype.MachineryBlock => (Lerp(14, 30, A("w")), Lerp(16, 34, A("l")), Lerp(14, 34, A("h"))),
            _ => (Lerp(28, 48, A("w")), Lerp(30, 58, A("l")), Lerp(10, 26, A("h"))),
        };
    }

    private static (float u, float v) PatternJitter(MegastationFabricPattern p, int seed,
        int cu, int cv, int du, int dv) => p switch
    {
        MegastationFabricPattern.Rows => (Signed(seed, "ju") * 3f, 0f),
        MegastationFabricPattern.Cluster => (Signed(seed, "ju") * 8f, Signed(seed, "jv") * 8f),
        MegastationFabricPattern.EdgeAssociated => (Signed(seed, "ju") * 10f, Signed(seed, "jv") * 4f),
        MegastationFabricPattern.G1Associated => (Signed(seed, "ju") * 9f, Signed(seed, "jv") * 9f),
        _ => (Signed(seed, "ju") * 12f, Signed(seed, "jv") * 12f),
    };

    private static float RoleDensity(MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Industrial => .58f,
        MegastationZoneRole.Utilities => .62f,
        MegastationZoneRole.Logistics => .46f,
        MegastationZoneRole.Habitation => .25f,
        MegastationZoneRole.Strategic => .14f,
        _ => 0f,
    };

    private static float ChannelAllocation(MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Industrial => .54f,
        MegastationZoneRole.Utilities => .55f,
        MegastationZoneRole.Logistics => .48f,
        MegastationZoneRole.Habitation => .24f,
        MegastationZoneRole.Strategic => .22f,
        _ => 0f,
    };

    private static float TopologySuitability(MegastationPlanarRegion r)
        => Math.Clamp(.65f + .28f * r.Concavity + .18f * Math.Clamp(-r.RelativeDepth, 0f, 1f)
            + .10f * (1f - r.Extremity), .35f, 1.15f);

    private static float DistrictStrength(int seed, MegastationFabricPattern pattern)
    {
        float s = Sample(seed, "strength");
        float baseValue = s < .18f ? .12f : s < .48f ? .62f : s < .82f ? 1.05f : 1.38f;
        return pattern is MegastationFabricPattern.Cluster or MegastationFabricPattern.G1Associated
            ? baseValue * 1.12f : baseValue;
    }

    private static bool OverlapsG1(Candidate c, MegastationAttachmentPlan plan)
        => plan.EffectiveProtectedVolumes.Any(volume => volume.Intersects(c.AabbMin, c.AabbMax))
            || plan.Placements.Any(p => Intersects(c.AabbMin, c.AabbMax, p.AabbMin, p.AabbMax))
            || plan.Reservations.Any(r => Vector3.Dot(r.Normal, c.Region.OutwardNormal) > .999f
                && MathF.Abs(r.PlaneCoordinateMetres - c.Region.PlaneCoordinateMetres) < .2f
                && Rects(c.MinU, c.MaxU, c.MinV, c.MaxV, r.MinU - 2, r.MaxU + 2, r.MinV - 2, r.MaxV + 2));

    private static bool NearG1(MegastationPlanarRegion region, float u, float v,
        MegastationAttachmentPlan plan)
    {
        const float influence = 95f;
        return plan.Reservations.Any(r => Vector3.Dot(r.Normal, region.OutwardNormal) > .999f
            && MathF.Abs(r.PlaneCoordinateMetres - region.PlaneCoordinateMetres) < .2f
            && u >= r.MinU - influence && u <= r.MaxU + influence
            && v >= r.MinV - influence && v <= r.MaxV + influence);
    }

    private static bool CollidesStructuralMass(Candidate c, StructuralOccupancy occupancy)
    {
        float[] us = [c.MinU, (c.MinU + c.MaxU) * .5f, c.MaxU];
        float[] vs = [c.MinV, (c.MinV + c.MaxV) * .5f, c.MaxV];
        float[] hs = [MathF.Min(1f, c.Height * .15f), c.Height * .5f, c.Height * .95f];
        foreach (float u in us)
        foreach (float v in vs)
        foreach (float h in hs)
        {
            Vector3 point = c.Region.OutwardNormal * c.Region.PlaneCoordinateMetres
                + c.Region.TangentU * u + c.Region.TangentV * v
                + c.Region.OutwardNormal * h;
            if (OccupiedAt(occupancy, point))
                return true;
        }
        return false;
    }

    private static bool OccupiedAt(StructuralOccupancy occupancy, Vector3 point)
    {
        SliceGrid grid = occupancy.Grid;
        int x = CoordinateIndex(grid, GridAxis.X, point.X);
        int y = CoordinateIndex(grid, GridAxis.Y, point.Y);
        int z = CoordinateIndex(grid, GridAxis.Z, point.Z);
        return x >= 0 && y >= 0 && z >= 0 && occupancy.IsOccupied(x, y, z);
    }

    private static int CoordinateIndex(SliceGrid grid, GridAxis axis, float coordinate)
    {
        for (int i = 0; i < grid.Count(axis); i++)
            if (coordinate >= grid.GetCellMinimum(axis, i)
                && coordinate < grid.GetCellMaximum(axis, i))
                return i;
        return -1;
    }

    private static bool OverlapsWindows(Candidate c, IReadOnlyList<MegastationWindowInstance> windows, float margin)
        => windows.Any(w => Vector3.Dot(w.Normal, c.Region.OutwardNormal) > .999f
            && MathF.Abs(Vector3.Dot(w.Centre, c.Region.OutwardNormal) - c.Region.PlaneCoordinateMetres) < .2f
            && PointIn(c, Vector3.Dot(w.Centre, c.Region.TangentU), Vector3.Dot(w.Centre, c.Region.TangentV),
                MathF.Max(w.Width, w.Height) * .5f + margin));

    private static bool OverlapsLights(Candidate c, IReadOnlyList<MegastationLightInstance> lights, float margin)
        => lights.Any(l => Vector3.Dot(l.Normal, c.Region.OutwardNormal) > .999f
            && MathF.Abs(Vector3.Dot(l.SurfacePosition, c.Region.OutwardNormal) - c.Region.PlaneCoordinateMetres) < .2f
            && PointIn(c, Vector3.Dot(l.SurfacePosition, c.Region.TangentU),
                Vector3.Dot(l.SurfacePosition, c.Region.TangentV), margin));

    private static bool PointIn(Candidate c, float u, float v, float margin)
        => u >= c.MinU - margin && u <= c.MaxU + margin && v >= c.MinV - margin && v <= c.MaxV + margin;

    private static (Vector3 min, Vector3 max) MegaBounds(MegastationMegaGreebleInstance i)
        => Bounds(i.SurfacePosition, i.Normal, i.TangentU, i.TangentV,
            i.MaxU - i.MinU, i.MaxV - i.MinV, i.Protrusion);

    private static (Vector3 min, Vector3 max) Bounds(Vector3 p, Vector3 n, Vector3 u, Vector3 v,
        float width, float length, float height)
    {
        Vector3 half = Abs(u) * (width * .5f) + Abs(v) * (length * .5f) + Abs(n) * (height * .5f);
        Vector3 centre = p + n * (height * .5f);
        return (centre - half, centre + half);
    }

    private static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
    private static bool Rects(float a0,float a1,float a2,float a3,float b0,float b1,float b2,float b3)
        => a0 < b1 && a1 > b0 && a2 < b3 && a3 > b2;
    private static bool Intersects(Vector3 amin,Vector3 amax,Vector3 bmin,Vector3 bmax)
        => amin.X < bmax.X && amax.X > bmin.X && amin.Y < bmax.Y && amax.Y > bmin.Y && amin.Z < bmax.Z && amax.Z > bmin.Z;
    private static bool IntersectsInflated(Vector3 amin,Vector3 amax,Vector3 bmin,Vector3 bmax,float m)
        => amin.X < bmax.X+m && amax.X > bmin.X-m && amin.Y < bmax.Y+m && amax.Y > bmin.Y-m && amin.Z < bmax.Z+m && amax.Z > bmin.Z-m;
    private static (Color,Color,Color) Palette(int seed)
    {
        (Color,Color,Color)[] p =
        [
            (new(67,70,68),new(42,45,45),new(122,91,55)),
            (new(92,91,82),new(51,54,53),new(140,116,68)),
            (new(61,68,72),new(37,42,45),new(118,70,48)),
            (new(101,96,82),new(54,53,49),new(90,109,102)),
        ];
        return p[PositiveMod(MegastationSeed.Derive(seed,"palette"),p.Length)];
    }
    private static string Signature(IEnumerable<MegastationFabricInstance> instances)
    {
        var text = new StringBuilder();
        foreach (var i in instances) text.Append(i.Identity).Append('|').Append(i.Archetype)
            .Append('|').Append(i.Pattern).Append('|').Append(i.Width).Append('|').Append(i.Length)
            .Append('|').Append(i.Height).Append('|').Append(i.SurfacePosition)
            .Append('|').Append(i.ChannelAssociation).Append('|').Append(i.ChannelFeatureIdentity)
            .Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    private static int FloorDiv(int value,int divisor) => value >= 0 ? value/divisor : -((-value+divisor-1)/divisor);
    private static int PositiveMod(int value,int modulus)=>(int)((uint)value%(uint)modulus);
    private static float Sample(int seed,string key)=>unchecked((uint)MegastationSeed.Derive(seed,key))/(float)uint.MaxValue;
    private static float Signed(int seed,string key)=>Sample(seed,key)*2f-1f;
    private static float Lerp(float a,float b,float t)=>a+(b-a)*t;
    private static float Min(float[] a)=>a.Length==0?0:a[0];
    private static float Median(float[] a)=>a.Length==0?0:a[a.Length/2];
    private static float Max(float[] a)=>a.Length==0?0:a[^1];
}

public static class MegastationFabricMeshBuilder
{
    public static MegastationFabricMeshBuildResult Build(MegastationFabricPlan plan,
        CancellationToken cancellationToken = default)
        => Build(plan, null, cancellationToken);

    public static MegastationFabricMeshBuildResult Build(
        MegastationFabricPlan plan,
        MegastationSystemMaterialAssignment? materialAssignment,
        CancellationToken cancellationToken = default)
    {
        var timer=Stopwatch.StartNew();
        var mesh=new StationModuleMesh();
        foreach(var i in plan.Instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (materialAssignment is { } assignment)
            {
                SystemMaterialBinding binding = assignment.FabricBinding(i);
                mesh.CurrentMaterialFamily = binding.FamilyId;
                mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(binding.FamilyId).TileSizeMeters;
                Color secondary = ProceduralMaterialCpuGenerator.ShiftLuminance(binding.Tint, -18f);
                Color accent = ProceduralMaterialCpuGenerator.Blend(
                    binding.Tint, assignment.Palette.AccentTint, .38f);
                Emit(i, mesh, binding.Tint, secondary, accent);
            }
            else
            {
                Emit(i, mesh, i.PrimaryColour, i.SecondaryColour, i.AccentColour);
            }
        }
        mesh.ApplyIlluminationFlags();
        StationMeshCpuData? shadow=mesh.PrepareIndexRanges(mesh.DecorClassRanges
            .Where(r=>r.decorClass==DecorClass.MegastationFabricMajor)
            .Select(r=>(r.indexStart,r.indexCount)).ToArray());
        timer.Stop();
        int sv=shadow?.Vertices.Length??0, si=shadow?.Indices.Length??0;
        var d=plan.Diagnostics with
        {
            VisibleVertexCount=mesh.VertexCount, VisibleTriangleCount=mesh.IndexCount/3,
            VisibleMeshBytes=Bytes(mesh.VertexCount,mesh.IndexCount),
            ShadowVertexCount=sv, ShadowTriangleCount=si/3, ShadowMeshBytes=Bytes(sv,si),
            MeshBuildMilliseconds=timer.ElapsedMilliseconds, OwnedTextureDelta=0,
            GpuBufferDelta=mesh.IsEmpty?0:4,
        };
        return new(mesh,d);
    }

    private static void Emit(
        MegastationFabricInstance i,
        StationModuleMesh mesh,
        Color primaryColour,
        Color secondaryColour,
        Color accentColour)
    {
        Frame f=new(i.SurfacePosition,i.Normal,i.TangentU,i.TangentV);
        mesh.CurrentDecorClass=DecorClass.MegastationFabricMajor;
        switch(i.Archetype)
        {
            case MegastationFabricArchetype.UtilityHall:
                Box(mesh,f,0,0,i.Height*.46f,i.Width,i.Length,i.Height*.92f,primaryColour);
                Box(mesh,f,0,0,i.Height*.96f,i.Width*.82f,i.Length*.90f,i.Height*.08f,secondaryColour);
                Box(mesh,f,i.Width*.53f,-i.Length*.18f,i.Height*.26f,i.Width*.20f,
                    i.Length*.36f,i.Height*.52f,secondaryColour);
                break;
            case MegastationFabricArchetype.SteppedBlock:
                Box(mesh,f,-i.Width*.23f,0,i.Height*.28f,i.Width*.52f,i.Length,i.Height*.56f,primaryColour);
                Box(mesh,f,i.Width*.20f,0,i.Height*.45f,i.Width*.44f,i.Length*.84f,i.Height*.90f,secondaryColour);
                break;
            case MegastationFabricArchetype.Warehouse:
                Box(mesh,f,0,0,i.Height*.38f,i.Width,i.Length,i.Height*.76f,primaryColour);
                float roofRadius=MathF.Min(i.Width*.22f,i.Height*.28f);
                mesh.AddPrismPipe(f.Origin-f.V*i.Length*.46f+f.N*i.Height*.78f,
                    f.Origin+f.V*i.Length*.46f+f.N*i.Height*.78f,
                    roofRadius,8,secondaryColour,true,true);
                break;
            case MegastationFabricArchetype.TechnicalTower:
                Box(mesh,f,0,0,i.Height*.10f,i.Width,i.Length,i.Height*.20f,secondaryColour);
                Box(mesh,f,0,0,i.Height*.55f,i.Width*.58f,i.Length*.58f,i.Height*.90f,primaryColour);
                Box(mesh,f,0,0,i.Height*1.02f,i.Width*.78f,i.Length*.78f,i.Height*.08f,accentColour);
                break;
            case MegastationFabricArchetype.MachineryBlock:
                Box(mesh,f,0,0,i.Height*.28f,i.Width,i.Length,i.Height*.56f,primaryColour);
                float machineryRadius=MathF.Min(i.Width*.28f,i.Height*.24f);
                mesh.AddPrismPipe(f.Origin-f.V*i.Length*.34f+f.N*i.Height*.67f,
                    f.Origin+f.V*i.Length*.34f+f.N*i.Height*.67f,
                    machineryRadius,8,secondaryColour,true,true);
                Box(mesh,f,-i.Width*.24f,0,i.Height*.88f,i.Width*.30f,i.Length*.72f,i.Height*.08f,secondaryColour);
                Box(mesh,f,i.Width*.24f,0,i.Height*.88f,i.Width*.30f,i.Length*.72f,i.Height*.08f,secondaryColour);
                break;
            default:
                Box(mesh,f,-i.Width*.24f,-i.Length*.12f,i.Height*.40f,i.Width*.42f,i.Length*.70f,i.Height*.80f,primaryColour);
                Box(mesh,f,i.Width*.22f,i.Length*.14f,i.Height*.30f,i.Width*.38f,i.Length*.54f,i.Height*.60f,secondaryColour);
                Box(mesh,f,i.Width*.10f,-i.Length*.35f,i.Height*.55f,i.Width*.25f,i.Length*.20f,i.Height*1.10f,primaryColour);
                break;
        }
        mesh.BreakDecorClassRange();
        mesh.CurrentDecorClass=DecorClass.MegastationFabricMinor;
        int ribs=2+PositiveMod(MegastationSeed.Derive(i.Seed,"ribs"),4);
        for(int r=0;r<ribs;r++)
        {
            float u=(-.38f+.76f*(r/(float)Math.Max(1,ribs-1)))*i.Width;
            Box(mesh,f,u,i.Length*.501f,i.Height*.48f,MathF.Max(.35f,i.Width*.025f),.20f,
                i.Height*.52f,accentColour);
        }
        Box(mesh,f,0,-i.Length*.501f,i.Height*.30f,i.Width*.42f,.22f,i.Height*.20f,accentColour);
    }

    private readonly record struct Frame(Vector3 Origin,Vector3 N,Vector3 U,Vector3 V);
    private static void Box(StationModuleMesh mesh,Frame f,float u,float v,float n,
        float width,float length,float height,Color colour)
    {
        Vector3 centre=f.Origin+f.U*u+f.V*v+f.N*n;
        mesh.AddOrientedBox(new Matrix(f.U.X,f.U.Y,f.U.Z,0,f.V.X,f.V.Y,f.V.Z,0,
            f.N.X,f.N.Y,f.N.Z,0,centre.X,centre.Y,centre.Z,1),new(width,length,height),colour);
    }
    private static int PositiveMod(int value,int modulus)=>(int)((uint)value%(uint)modulus);
    private static long Bytes(int v,int i) =>
        (long)v * Inferior.Rendering.VertexPositionNormalColorTexture.VertexDeclaration.VertexStride
        + (long)i * 4L;
}

internal static class MegastationFabricDebug
{
    public static VertexPositionColor[] BuildLines(MegastationFabricPlan plan)
    {
        var lines=new List<VertexPositionColor>();
        foreach(var i in plan.Instances)
        {
            Color c=i.Archetype switch
            {
                MegastationFabricArchetype.UtilityHall=>Color.Cyan,
                MegastationFabricArchetype.SteppedBlock=>Color.Orange,
                MegastationFabricArchetype.Warehouse=>Color.Yellow,
                MegastationFabricArchetype.TechnicalTower=>Color.Magenta,
                MegastationFabricArchetype.MachineryBlock=>Color.Lime,
                _=>Color.CornflowerBlue,
            };
            Vector3 p(float u,float v,float h)=>i.Normal*(Vector3.Dot(i.SurfacePosition,i.Normal)+h)+i.TangentU*u+i.TangentV*v;
            Vector3 a=p(i.MinU,i.MinV,.25f),b=p(i.MaxU,i.MinV,.25f),d=p(i.MinU,i.MaxV,.25f),e=p(i.MaxU,i.MaxV,.25f);
            Add(lines,a,b,c);Add(lines,b,e,c);Add(lines,e,d,c);Add(lines,d,a,c);
            Vector3 top=i.SurfacePosition+i.Normal*i.Height;
            Add(lines,i.SurfacePosition,top,c);
        }
        foreach (MegastationFabricDebugFootprint footprint in plan.DebugFootprints
                     .Where(f => !f.Accepted))
        {
            Color c = new(55, 55, 62);
            Vector3 p(float u,float v)=>footprint.Normal*(footprint.PlaneCoordinateMetres+.15f)
                +footprint.TangentU*u+footprint.TangentV*v;
            Vector3 a=p(footprint.MinU,footprint.MinV),b=p(footprint.MaxU,footprint.MinV),
                d=p(footprint.MinU,footprint.MaxV),e=p(footprint.MaxU,footprint.MaxV);
            Add(lines,a,b,c);Add(lines,b,e,c);Add(lines,e,d,c);Add(lines,d,a,c);
        }
        return lines.ToArray();
    }
    private static void Add(List<VertexPositionColor> l,Vector3 a,Vector3 b,Color c){l.Add(new(a,c));l.Add(new(b,c));}
}
