using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationPlanarMaskRect(
    BoundaryFaceKey Face,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV);

/// <summary>
/// Canonical CPU-side connected coplanar substrate shared by G1 attachments,
/// G2 native infrastructure, and Fabric Structures. Identity deliberately retains the accepted G1
/// attachments:v1 namespace so extracting this layer cannot perturb G1 output.
/// </summary>
public sealed record MegastationPlanarRegion(
    string StableId,
    string ZoneId,
    int ZoneSeed,
    MegastationZoneRole ZoneRole,
    GridDirection Direction,
    int PlaneGridCoordinate,
    float PlaneCoordinateMetres,
    Vector3 SurfaceOrigin,
    Vector3 OutwardNormal,
    Vector3 TangentU,
    Vector3 TangentV,
    Vector3 PhysicalCentre,
    IReadOnlyList<BoundaryFaceKey> Faces,
    IReadOnlyList<MegastationPlanarMaskRect> ExactMask,
    IReadOnlyList<BoundaryEdgeKey> BoundaryEdges,
    IReadOnlyList<BoundaryFaceKey> AdjacentFaces,
    float MinU,
    float MaxU,
    float MinV,
    float MaxV,
    float PhysicalArea,
    Vector2 PhysicalExtents,
    float Prominence,
    float Exposure,
    float RelativeDepth,
    float Height,
    float Concavity,
    float Extremity);

public static class MegastationPlanarRegionExtractor
{
    // Compatibility identity: these strings previously came directly from the
    // G1 attachment extractor and are part of accepted placement identities.
    private const string StableIdentityNamespace = "attachments:v1";

    public static MegastationPlanarRegion[] Extract(
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
        var result = new List<MegastationPlanarRegion>();

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
                Vector3 tangentU = Vector3.Normalize(p1 - p0);
                Vector3 tangentV = Vector3.Normalize(p3 - p0);
                Vector3 normal = BoundaryTopologyBuilder.Normal(first.Direction);
                MegastationPlanarMaskRect[] mask = component
                    .Select(face => MaskRect(grid, topology.FaceByKey[face], tangentU, tangentV))
                    .ToArray();
                float minU = mask.Min(rect => rect.MinU);
                float maxU = mask.Max(rect => rect.MaxU);
                float minV = mask.Min(rect => rect.MinV);
                float maxV = mask.Max(rect => rect.MaxV);
                float area = mask.Sum(rect =>
                    (rect.MaxU - rect.MinU) * (rect.MaxV - rect.MinV));
                Vector2 extents = new(maxU - minU, maxV - minV);
                Vector3 desired = normal * Vector3.Dot(p0, normal)
                    + tangentU * ((minU + maxU) * 0.5f)
                    + tangentV * ((minV + maxV) * 0.5f);
                BoundaryFaceKey centreFace = component
                    .OrderBy(face => Vector3.DistanceSquared(
                        FaceCentre(grid, topology.FaceByKey[face]), desired))
                    .ThenBy(face => face)
                    .First();
                Vector3 centre = FaceCentre(grid, topology.FaceByKey[centreFace]);
                string faceKey = string.Join('|', component.Select(FaceIdentity));
                int signature = MegastationSeed.Derive(zone.Seed, faceKey);
                string stableId = $"{zone.Identity}/{StableIdentityNamespace}/plane:{first.Direction}:" +
                    $"{PlaneCoordinate(first)}:{component.Min.X},{component.Min.Y},{component.Min.Z}:" +
                    $"{unchecked((uint)signature):X8}";
                var componentSet = component.ToHashSet();
                BoundaryEdgeKey[] boundaryEdges = component
                    .SelectMany(face => topology.FaceByKey[face].Edges)
                    .Distinct()
                    .Where(edge => topology.EdgeByKey[edge].IncidentFaces.Any(
                        face => !componentSet.Contains(face)))
                    .OrderBy(edge => edge)
                    .ToArray();
                BoundaryFaceKey[] adjacentFaces = boundaryEdges
                    .SelectMany(edge => topology.EdgeByKey[edge].IncidentFaces)
                    .Where(face => !componentSet.Contains(face))
                    .Distinct()
                    .OrderBy(face => face)
                    .ToArray();

                result.Add(new(
                    stableId,
                    zone.Identity,
                    zone.Seed,
                    zone.Role,
                    first.Direction,
                    PlaneCoordinate(first),
                    Vector3.Dot(p0, normal),
                    p0,
                    normal,
                    tangentU,
                    tangentV,
                    centre,
                    component.ToArray(),
                    mask,
                    boundaryEdges,
                    adjacentFaces,
                    minU,
                    maxU,
                    minV,
                    maxV,
                    area,
                    extents,
                    zone.Metrics.Prominence,
                    zone.Metrics.ImmediateExposure,
                    zone.Metrics.RelativeDepth,
                    zone.Metrics.LocalHeight,
                    zone.Metrics.ConcavityContext,
                    zone.Metrics.Extremity));
            }
        }

        return result.OrderBy(region => region.StableId, StringComparer.Ordinal).ToArray();
    }

    public static bool ContainsFootprint(
        MegastationPlanarRegion region,
        float minU,
        float maxU,
        float minV,
        float maxV,
        float margin = 0f)
    {
        minU -= margin;
        maxU += margin;
        minV -= margin;
        maxV += margin;
        float required = (maxU - minU) * (maxV - minV);
        if (required <= 0f)
            return false;
        float covered = 0f;
        foreach (MegastationPlanarMaskRect rect in region.ExactMask)
        {
            float overlapU = MathF.Max(0f, MathF.Min(maxU, rect.MaxU) - MathF.Max(minU, rect.MinU));
            float overlapV = MathF.Max(0f, MathF.Min(maxV, rect.MaxV) - MathF.Max(minV, rect.MinV));
            covered += overlapU * overlapV;
        }
        return covered >= required - MathF.Max(0.001f, required * 0.0001f);
    }

    private static MegastationPlanarMaskRect MaskRect(
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
}
