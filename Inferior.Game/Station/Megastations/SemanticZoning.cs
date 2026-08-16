using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationZoneRole
{
    Structural,
    Habitation,
    Industrial,
    Logistics,
    Utilities,
    Strategic,
}

[Flags]
public enum MegastationZoneCapabilities
{
    None = 0,
    HighExposure = 1,
    Extremity = 2,
    CommsCandidate = 4,
    StrategicCandidate = 8,
}

public enum MegastationStructuralAnchorKind
{
    CoreComponent,
    FaceDistrict,
    EdgeRegion,
    CornerRegion,
    RepairInherited,
    RepairComponent,
}

public sealed record MegastationStructuralAnchor(
    string Identity,
    MegastationStructuralAnchorKind Kind,
    GridDirection? SourceFace);

public sealed record MegastationSurfaceMetrics(
    float PhysicalArea,
    Vector3 PhysicalCentre,
    float Prominence,
    float Extremity,
    float ImmediateExposure,
    float RelativeDepth,
    float LocalHeight,
    float PlanarConnectedArea,
    float ConcavityContext);

public sealed record MegastationSemanticSurface(
    BoundaryFaceKey Face,
    string StructuralAnchorIdentity,
    GridDirection? SourceFace,
    MegastationSurfaceMetrics Metrics,
    IReadOnlyList<BoundaryFaceKey> AdjacentFaces,
    IReadOnlyList<BoundaryFaceKey> CoplanarNeighbours);

public sealed record MegastationSemanticZone(
    string Identity,
    MegastationStructuralAnchor Anchor,
    MegastationZoneRole Role,
    MegastationZoneCapabilities Capabilities,
    IReadOnlyList<BoundaryFaceKey> Faces,
    float TotalPhysicalArea,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    MegastationSurfaceMetrics Metrics,
    int Seed);

public sealed record MegastationSemanticIndexGroup(
    MegastationZoneRole Role,
    IReadOnlyList<int> Indices);

public sealed record MegastationSemanticZoningDiagnostics(
    int SurfaceFaceCount,
    int ZoneCount,
    float TotalSurfaceArea,
    int CoreAnchorCount,
    int FaceDistrictAnchorCount,
    int EdgeAnchorCount,
    int CornerAnchorCount,
    int RepairFragmentCount,
    int FragmentsMerged,
    long ZoningMilliseconds,
    IReadOnlyDictionary<MegastationZoneRole, float> AreaByRole);

public sealed class MegastationSemanticZoningResult
{
    public required IReadOnlyList<MegastationStructuralAnchor> Anchors { get; init; }
    public required IReadOnlyList<MegastationSemanticSurface> Surfaces { get; init; }
    public required IReadOnlyDictionary<BoundaryFaceKey, MegastationSemanticSurface> SurfaceByFace { get; init; }
    public required IReadOnlyList<MegastationSemanticZone> Zones { get; init; }
    public required IReadOnlyDictionary<BoundaryFaceKey, MegastationSemanticZone> ZoneByFace { get; init; }
    public required IReadOnlyList<MegastationSemanticIndexGroup> DebugIndexGroups { get; init; }
    public required MegastationSemanticZoningDiagnostics Diagnostics { get; init; }
}

public static class MegastationSemanticZoningBuilder
{
    private const int AlgorithmVersion = 1;
    private static readonly (int dx, int dy, int dz)[] CellNeighbours =
    [
        (-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1),
    ];

    public static MegastationSemanticZoningResult Build(
        int rootSeed,
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        IReadOnlyList<UrbanGrowthResult> faceGrowth)
    {
        var stopwatch = Stopwatch.StartNew();
        var adjacency = BuildAdjacency(topology);
        var anchorByFace = new Dictionary<BoundaryFaceKey, MegastationStructuralAnchor>();
        var repairFaces = new HashSet<BoundaryFaceKey>();

        foreach (BoundaryFace face in topology.Faces.OrderBy(f => f.Key))
        {
            MegastationStructuralAnchor? anchor = DirectAnchor(face, faceGrowth);
            if (anchor == null)
                repairFaces.Add(face.Key);
            else
                anchorByFace.Add(face.Key, anchor);
        }

        int repairFragmentCount = 0;
        int fragmentsMerged = 0;
        foreach (BoundaryFaceKey[] component in ConnectedComponents(repairFaces, adjacency))
        {
            repairFragmentCount++;
            HashSet<BoundaryFaceKey> componentSet = component.ToHashSet();
            var sharedLengths = new Dictionary<string, float>(StringComparer.Ordinal);
            var anchors = new Dictionary<string, MegastationStructuralAnchor>(StringComparer.Ordinal);
            foreach (BoundaryFaceKey key in component)
            foreach (BoundaryEdgeKey edgeKey in topology.FaceByKey[key].Edges)
            foreach (BoundaryFaceKey neighbour in topology.EdgeByKey[edgeKey].IncidentFaces)
            {
                if (componentSet.Contains(neighbour) || !anchorByFace.TryGetValue(neighbour, out var candidate))
                    continue;
                sharedLengths[candidate.Identity] = sharedLengths.GetValueOrDefault(candidate.Identity)
                    + EdgeLength(occupancy.Grid, edgeKey);
                anchors[candidate.Identity] = candidate;
            }

            MegastationStructuralAnchor inherited;
            if (sharedLengths.Count > 0)
            {
                string selected = SelectFragmentAnchor(sharedLengths);
                MegastationStructuralAnchor source = anchors[selected];
                inherited = new(
                    source.Identity,
                    MegastationStructuralAnchorKind.RepairInherited,
                    source.SourceFace);
                fragmentsMerged++;
            }
            else
            {
                BoundaryFaceKey first = component.Min();
                inherited = new(
                    $"repair/component:{first.X},{first.Y},{first.Z},{Direction.Id(first.Direction)}",
                    MegastationStructuralAnchorKind.RepairComponent,
                    null);
            }

            foreach (BoundaryFaceKey key in component)
                anchorByFace.Add(key, inherited);
        }

        var preliminaryMetrics = topology.Faces.ToDictionary(
            face => face.Key,
            face => BaseMetrics(occupancy, topology, face, anchorByFace[face.Key], adjacency[face.Key]));
        var planarAreas = ComputePlanarConnectedAreas(topology, adjacency, preliminaryMetrics);
        var surfaces = topology.Faces
            .OrderBy(face => face.Key)
            .Select(face =>
            {
                MegastationSurfaceMetrics metric = preliminaryMetrics[face.Key];
                BoundaryFaceKey[] neighbours = adjacency[face.Key].OrderBy(k => k).ToArray();
                float neighbourProminence = neighbours.Length == 0
                    ? metric.Prominence
                    : neighbours.Average(n => preliminaryMetrics[n].Prominence);
                metric = metric with
                {
                    LocalHeight = metric.Prominence - neighbourProminence,
                    PlanarConnectedArea = planarAreas[face.Key],
                };
                return new MegastationSemanticSurface(
                    face.Key,
                    anchorByFace[face.Key].Identity,
                    anchorByFace[face.Key].SourceFace,
                    metric,
                    neighbours,
                    neighbours.Where(n => n.Direction == face.Direction).ToArray());
            })
            .ToArray();
        var surfaceByFace = surfaces.ToDictionary(surface => surface.Face);

        int zoningSeed = MegastationSeed.Derive(rootSeed, $"semantic-zoning:v{AlgorithmVersion}");
        var zones = anchorByFace
            .GroupBy(pair => pair.Value.Identity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => BuildZone(
                zoningSeed,
                CanonicalAnchor(group.Select(pair => pair.Value)),
                occupancy.Grid,
                topology,
                surfaceByFace,
                group.Select(pair => pair.Key).OrderBy(key => key).ToArray()))
            .ToArray();
        var zoneByFace = zones
            .SelectMany(zone => zone.Faces.Select(face => (face, zone)))
            .ToDictionary(pair => pair.face, pair => pair.zone);

        MegastationSemanticIndexGroup[] indexGroups = BuildDebugIndexGroups(topology, zoneByFace);
        stopwatch.Stop();
        float totalArea = zones.Sum(zone => zone.TotalPhysicalArea);
        var areaByRole = Enum.GetValues<MegastationZoneRole>()
            .ToDictionary(role => role, role => zones.Where(zone => zone.Role == role).Sum(zone => zone.TotalPhysicalArea));
        MegastationStructuralAnchor[] anchorsResult = zones.Select(zone => zone.Anchor).ToArray();
        var diagnostics = new MegastationSemanticZoningDiagnostics(
            topology.Faces.Count,
            zones.Length,
            totalArea,
            anchorsResult.Count(a => a.Kind == MegastationStructuralAnchorKind.CoreComponent),
            anchorsResult.Count(a => a.Kind == MegastationStructuralAnchorKind.FaceDistrict),
            anchorsResult.Count(a => a.Kind == MegastationStructuralAnchorKind.EdgeRegion),
            anchorsResult.Count(a => a.Kind == MegastationStructuralAnchorKind.CornerRegion),
            repairFragmentCount,
            fragmentsMerged,
            stopwatch.ElapsedMilliseconds,
            areaByRole);

        return new MegastationSemanticZoningResult
        {
            Anchors = anchorsResult,
            Surfaces = surfaces,
            SurfaceByFace = surfaceByFace,
            Zones = zones,
            ZoneByFace = zoneByFace,
            DebugIndexGroups = indexGroups,
            Diagnostics = diagnostics,
        };
    }

    internal static IReadOnlyDictionary<MegastationZoneRole, float> RoleWeights(
        MegastationStructuralAnchorKind kind,
        MegastationSurfaceMetrics metrics)
    {
        var weights = Enum.GetValues<MegastationZoneRole>().ToDictionary(role => role, _ => 0.5f);
        weights[MegastationZoneRole.Structural] = 7.0f;
        weights[MegastationZoneRole.Habitation] = 1.0f;
        weights[MegastationZoneRole.Industrial] = 1.3f;
        weights[MegastationZoneRole.Logistics] = 1.1f;
        weights[MegastationZoneRole.Utilities] = 0.6f;
        weights[MegastationZoneRole.Strategic] = 0.25f;

        if (kind == MegastationStructuralAnchorKind.CoreComponent)
            weights[MegastationZoneRole.Structural] += 10f;
        if (kind == MegastationStructuralAnchorKind.EdgeRegion)
        {
            weights[MegastationZoneRole.Structural] += 4f;
            weights[MegastationZoneRole.Industrial] += 1.5f;
        }
        if (kind == MegastationStructuralAnchorKind.CornerRegion)
        {
            weights[MegastationZoneRole.Structural] += 3f;
            weights[MegastationZoneRole.Strategic] += 1.5f;
        }

        float high = Math.Clamp(metrics.Prominence + MathF.Max(0f, metrics.LocalHeight), 0f, 1.5f);
        float recessed = Math.Clamp((1f - metrics.Prominence) * 0.55f + metrics.ConcavityContext, 0f, 1.5f);
        float broad = Math.Clamp(metrics.PlanarConnectedArea / MathF.Max(1f, metrics.PhysicalArea * 8f), 0f, 2f);
        weights[MegastationZoneRole.Habitation] += high * 4f + metrics.ImmediateExposure;
        weights[MegastationZoneRole.Industrial] += broad * 1.8f + (1f - high) * 1.2f;
        weights[MegastationZoneRole.Logistics] += broad * 2.2f + metrics.Extremity * 0.8f;
        weights[MegastationZoneRole.Utilities] += recessed * 2.75f;
        weights[MegastationZoneRole.Strategic] += metrics.Extremity * 2.2f
            + metrics.ImmediateExposure * 1.8f + high * 1.4f;
        return weights;
    }

    internal static string SelectFragmentAnchor(IReadOnlyDictionary<string, float> sharedPhysicalEdgeLengths)
    {
        if (sharedPhysicalEdgeLengths.Count == 0)
            throw new ArgumentException("At least one candidate anchor is required.", nameof(sharedPhysicalEdgeLengths));
        return sharedPhysicalEdgeLengths
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First().Key;
    }

    private static MegastationSemanticZone BuildZone(
        int zoningSeed,
        MegastationStructuralAnchor anchor,
        SliceGrid grid,
        BoundaryTopology topology,
        IReadOnlyDictionary<BoundaryFaceKey, MegastationSemanticSurface> surfaces,
        BoundaryFaceKey[] faces)
    {
        float area = faces.Sum(face => surfaces[face].Metrics.PhysicalArea);
        MegastationSurfaceMetrics aggregate = Aggregate(faces.Select(face => surfaces[face].Metrics), area);
        int seed = MegastationSeed.Derive(zoningSeed, anchor.Identity);
        MegastationZoneRole role = SelectRole(seed, RoleWeights(anchor.Kind, aggregate));
        MegastationZoneCapabilities capabilities = CapabilitiesFor(aggregate, role);
        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);
        foreach (BoundaryFaceKey faceKey in faces)
        foreach (GridVertexKey vertex in topology.FaceByKey[faceKey].Vertices)
        {
            Vector3 position = BoundaryTopologyBuilder.Position(grid, vertex);
            boundsMin = Vector3.Min(boundsMin, position);
            boundsMax = Vector3.Max(boundsMax, position);
        }

        return new MegastationSemanticZone(
            $"zone/{anchor.Identity}",
            anchor,
            role,
            capabilities,
            faces,
            area,
            boundsMin,
            boundsMax,
            aggregate,
            seed);
    }

    private static MegastationStructuralAnchor CanonicalAnchor(IEnumerable<MegastationStructuralAnchor> anchors)
    {
        MegastationStructuralAnchor source = anchors
            .OrderBy(anchor => anchor.Kind == MegastationStructuralAnchorKind.RepairInherited ? 1 : 0)
            .ThenBy(anchor => anchor.Kind)
            .First();
        return source.Kind == MegastationStructuralAnchorKind.RepairInherited
            ? source
            : source;
    }

    private static MegastationStructuralAnchor? DirectAnchor(
        BoundaryFace face,
        IReadOnlyList<UrbanGrowthResult> faceGrowth)
    {
        if (face.Owner == MegacellOwner.TopologyRegularisation)
            return null;
        if (face.Owner == MegacellOwner.StructuralCore)
            return new("core/component:000", MegastationStructuralAnchorKind.CoreComponent, null);
        if (face.Owner == MegacellOwner.EdgeRegion || face.RegionId.StartsWith("edge.", StringComparison.Ordinal))
        {
            string identity = face.RegionId.EndsWith(".support", StringComparison.Ordinal)
                ? face.RegionId[..^".support".Length]
                : face.RegionId;
            return new(identity, MegastationStructuralAnchorKind.EdgeRegion, null);
        }
        if (face.Owner == MegacellOwner.CornerRegion || face.RegionId.StartsWith("corner.", StringComparison.Ordinal))
            return new(face.RegionId, MegastationStructuralAnchorKind.CornerRegion, null);

        UrbanGrowthResult? growth = faceGrowth.FirstOrDefault(result =>
            face.RegionId == RegionIdentity.Face(result.Patch.Direction));
        if (growth == null)
            return null;
        int u = Coordinate(face.Key, growth.Patch.UAxis);
        int v = Coordinate(face.Key, growth.Patch.VAxis);
        UrbanDistrict? district = growth.Districts.FirstOrDefault(d =>
            u >= d.MinU && u <= d.MaxU && v >= d.MinV && v <= d.MaxV);
        if (district == null)
            return null;
        return new(
            $"{RegionIdentity.Face(growth.Patch.Direction)}/district:{district.Id:D2}",
            MegastationStructuralAnchorKind.FaceDistrict,
            growth.Patch.Direction);
    }

    private static MegastationSurfaceMetrics BaseMetrics(
        StructuralOccupancy occupancy,
        BoundaryTopology topology,
        BoundaryFace face,
        MegastationStructuralAnchor anchor,
        IReadOnlySet<BoundaryFaceKey> adjacent)
    {
        SliceGrid grid = occupancy.Grid;
        GridAxis normalAxis = Direction.PrimaryAxis(face.Direction);
        GridAxis[] tangent = Enum.GetValues<GridAxis>().Where(axis => axis != normalAxis).ToArray();
        int[] coords = [face.Key.X, face.Key.Y, face.Key.Z];
        float area = grid.GetCellSize(tangent[0], coords[(int)tangent[0]])
            * grid.GetCellSize(tangent[1], coords[(int)tangent[1]]);
        Vector3 centre = new(
            grid.GetCellCentre(GridAxis.X, face.Key.X),
            grid.GetCellCentre(GridAxis.Y, face.Key.Y),
            grid.GetCellCentre(GridAxis.Z, face.Key.Z));
        float prominence = anchor.SourceFace is { } source
            ? OutwardFraction(grid, source, coords[(int)Direction.PrimaryAxis(source)])
            : grid.ExteriorDirections(face.Key.X, face.Key.Y, face.Key.Z)
                .Select(direction => OutwardFraction(grid, direction, coords[(int)Direction.PrimaryAxis(direction)]))
                .DefaultIfEmpty(0f)
                .Max();
        float extremity = Math.Max(
            Math.Abs(centre.X) / Math.Max(1f, grid.Dimension(GridAxis.X) * 0.5f),
            Math.Max(
                Math.Abs(centre.Y) / Math.Max(1f, grid.Dimension(GridAxis.Y) * 0.5f),
                Math.Abs(centre.Z) / Math.Max(1f, grid.Dimension(GridAxis.Z) * 0.5f)));
        int emptyNeighbours = CellNeighbours.Count(offset =>
            !occupancy.IsOccupied(face.Key.X + offset.dx, face.Key.Y + offset.dy, face.Key.Z + offset.dz));
        float concavity = face.Edges.Count(edge =>
            topology.EdgeByKey[edge].Classification == BoundaryEdgeClass.ConcaveExterior) / 4f;
        return new(
            area,
            centre,
            prominence,
            Math.Clamp(extremity, 0f, 1f),
            emptyNeighbours / 6f,
            1f - prominence,
            0f,
            area,
            concavity);
    }

    private static Dictionary<BoundaryFaceKey, float> ComputePlanarConnectedAreas(
        BoundaryTopology topology,
        IReadOnlyDictionary<BoundaryFaceKey, HashSet<BoundaryFaceKey>> adjacency,
        IReadOnlyDictionary<BoundaryFaceKey, MegastationSurfaceMetrics> metrics)
    {
        var result = new Dictionary<BoundaryFaceKey, float>();
        var unseen = topology.Faces.Select(face => face.Key).ToHashSet();
        while (unseen.Count > 0)
        {
            BoundaryFaceKey start = unseen.Min();
            var component = new List<BoundaryFaceKey>();
            var queue = new Queue<BoundaryFaceKey>();
            queue.Enqueue(start);
            unseen.Remove(start);
            while (queue.Count > 0)
            {
                BoundaryFaceKey current = queue.Dequeue();
                component.Add(current);
                foreach (BoundaryFaceKey next in adjacency[current].OrderBy(key => key))
                    if (next.Direction == start.Direction && unseen.Remove(next))
                        queue.Enqueue(next);
            }
            float area = component.Sum(face => metrics[face].PhysicalArea);
            foreach (BoundaryFaceKey face in component)
                result.Add(face, area);
        }
        return result;
    }

    private static Dictionary<BoundaryFaceKey, HashSet<BoundaryFaceKey>> BuildAdjacency(BoundaryTopology topology)
    {
        var result = topology.Faces.ToDictionary(face => face.Key, _ => new HashSet<BoundaryFaceKey>());
        foreach (BoundaryEdgeSegment edge in topology.EdgeSegments)
        foreach (BoundaryFaceKey a in edge.IncidentFaces)
        foreach (BoundaryFaceKey b in edge.IncidentFaces)
            if (a != b)
                result[a].Add(b);
        return result;
    }

    private static IEnumerable<BoundaryFaceKey[]> ConnectedComponents(
        IReadOnlySet<BoundaryFaceKey> candidates,
        IReadOnlyDictionary<BoundaryFaceKey, HashSet<BoundaryFaceKey>> adjacency)
    {
        var unseen = candidates.ToHashSet();
        while (unseen.Count > 0)
        {
            BoundaryFaceKey start = unseen.Min();
            var component = new List<BoundaryFaceKey>();
            var queue = new Queue<BoundaryFaceKey>();
            queue.Enqueue(start);
            unseen.Remove(start);
            while (queue.Count > 0)
            {
                BoundaryFaceKey current = queue.Dequeue();
                component.Add(current);
                foreach (BoundaryFaceKey next in adjacency[current].OrderBy(key => key))
                    if (unseen.Remove(next))
                        queue.Enqueue(next);
            }
            yield return component.OrderBy(key => key).ToArray();
        }
    }

    private static MegastationSurfaceMetrics Aggregate(
        IEnumerable<MegastationSurfaceMetrics> source,
        float totalArea)
    {
        MegastationSurfaceMetrics[] values = source.ToArray();
        float Weighted(Func<MegastationSurfaceMetrics, float> selector) =>
            values.Sum(value => selector(value) * value.PhysicalArea) / Math.Max(0.001f, totalArea);
        return new(
            totalArea,
            new Vector3(Weighted(m => m.PhysicalCentre.X), Weighted(m => m.PhysicalCentre.Y), Weighted(m => m.PhysicalCentre.Z)),
            Weighted(m => m.Prominence),
            Weighted(m => m.Extremity),
            Weighted(m => m.ImmediateExposure),
            Weighted(m => m.RelativeDepth),
            Weighted(m => m.LocalHeight),
            values.Max(m => m.PlanarConnectedArea),
            Weighted(m => m.ConcavityContext));
    }

    private static MegastationZoneRole SelectRole(
        int seed,
        IReadOnlyDictionary<MegastationZoneRole, float> weights)
    {
        var rng = new Random(seed);
        float total = weights.Values.Sum();
        double roll = rng.NextDouble() * total;
        foreach (MegastationZoneRole role in Enum.GetValues<MegastationZoneRole>())
        {
            roll -= weights[role];
            if (roll <= 0)
                return role;
        }
        return MegastationZoneRole.Structural;
    }

    private static MegastationZoneCapabilities CapabilitiesFor(
        MegastationSurfaceMetrics metrics,
        MegastationZoneRole role)
    {
        MegastationZoneCapabilities capabilities = MegastationZoneCapabilities.None;
        if (metrics.ImmediateExposure >= 0.32f)
            capabilities |= MegastationZoneCapabilities.HighExposure;
        if (metrics.Extremity >= 0.78f)
            capabilities |= MegastationZoneCapabilities.Extremity;
        if ((capabilities & (MegastationZoneCapabilities.HighExposure | MegastationZoneCapabilities.Extremity)) != 0
            && metrics.Prominence >= 0.45f)
            capabilities |= MegastationZoneCapabilities.StrategicCandidate;
        if ((capabilities & MegastationZoneCapabilities.StrategicCandidate) != 0
            && (role == MegastationZoneRole.Strategic || metrics.ImmediateExposure >= 0.48f))
            capabilities |= MegastationZoneCapabilities.CommsCandidate;
        return capabilities;
    }

    private static MegastationSemanticIndexGroup[] BuildDebugIndexGroups(
        BoundaryTopology topology,
        IReadOnlyDictionary<BoundaryFaceKey, MegastationSemanticZone> zoneByFace)
    {
        var indicesByRole = Enum.GetValues<MegastationZoneRole>()
            .ToDictionary(role => role, _ => new List<int>());
        for (int i = 0; i < topology.Faces.Count; i++)
        {
            MegastationZoneRole role = zoneByFace[topology.Faces[i].Key].Role;
            int vertex = i * 4;
            indicesByRole[role].AddRange([vertex, vertex + 2, vertex + 1, vertex, vertex + 3, vertex + 2]);
        }
        return Enum.GetValues<MegastationZoneRole>()
            .Select(role => new MegastationSemanticIndexGroup(role, indicesByRole[role].ToArray()))
            .ToArray();
    }

    private static float EdgeLength(SliceGrid grid, BoundaryEdgeKey edge)
        => grid.GetCellSize(edge.Axis, edge.Start);

    private static float OutwardFraction(SliceGrid grid, GridDirection direction, int coordinate)
    {
        GridAxis axis = Direction.PrimaryAxis(direction);
        Range core = grid.CoreRange(axis);
        int layer = Direction.Sign(direction) > 0
            ? Math.Max(0, coordinate - core.End.Value + 1)
            : Math.Max(0, core.Start.Value - coordinate);
        return Math.Clamp(layer / (float)Math.Max(1, Direction.AvailableLayers(grid, direction)), 0f, 1f);
    }

    private static int Coordinate(BoundaryFaceKey key, GridAxis axis) => axis switch
    {
        GridAxis.X => key.X,
        GridAxis.Y => key.Y,
        _ => key.Z,
    };
}
