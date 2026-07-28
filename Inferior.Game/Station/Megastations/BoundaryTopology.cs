using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public readonly record struct GridVertexKey(int X, int Y, int Z) : IComparable<GridVertexKey>
{
    public int CompareTo(GridVertexKey other)
    {
        int c = X.CompareTo(other.X);
        if (c != 0) return c;
        c = Y.CompareTo(other.Y);
        return c != 0 ? c : Z.CompareTo(other.Z);
    }
}

public readonly record struct BoundaryFaceKey(int X, int Y, int Z, GridDirection Direction)
    : IComparable<BoundaryFaceKey>
{
    public int CompareTo(BoundaryFaceKey other)
    {
        int c = X.CompareTo(other.X);
        if (c != 0) return c;
        c = Y.CompareTo(other.Y);
        if (c != 0) return c;
        c = Z.CompareTo(other.Z);
        return c != 0 ? c : Direction.CompareTo(other.Direction);
    }
}

public readonly record struct BoundaryEdgeKey(GridAxis Axis, int A, int B, int Start)
    : IComparable<BoundaryEdgeKey>
{
    public int CompareTo(BoundaryEdgeKey other)
    {
        int c = Axis.CompareTo(other.Axis);
        if (c != 0) return c;
        c = A.CompareTo(other.A);
        if (c != 0) return c;
        c = B.CompareTo(other.B);
        return c != 0 ? c : Start.CompareTo(other.Start);
    }
}

public enum BoundaryEdgeClass
{
    Internal,
    FlatContinuation,
    ConvexExterior,
    ConcaveExterior,
    InvalidDiagonal,
}

public enum BoundaryVertexClass
{
    Empty,
    SimpleConvexCorner,
    SimpleConcaveCorner,
    StraightConvexContinuation,
    ComplexJunction,
    NonManifold,
}

public enum ChamferEligibility
{
    Ineligible,
    Eligible,
    SuppressedComplexEndpoint,
    SuppressedTooSmall,
    SuppressedInvalidNearby,
}

public sealed record BoundaryFace(
    BoundaryFaceKey Key,
    GridDirection Direction,
    GridVertexKey[] Vertices,
    BoundaryEdgeKey[] Edges,
    MegacellOwner Owner,
    string RegionId);

public sealed record BoundaryEdgeSegment(
    BoundaryEdgeKey Key,
    BoundaryEdgeClass Classification,
    IReadOnlyList<BoundaryFaceKey> IncidentFaces,
    GridVertexKey StartVertex,
    GridVertexKey EndVertex,
    ChamferEligibility ChamferEligibility,
    float ChamferWidth);

public sealed record BoundaryVertex(
    GridVertexKey Key,
    BoundaryVertexClass Classification,
    IReadOnlyList<BoundaryFaceKey> IncidentFaces,
    IReadOnlyList<BoundaryEdgeKey> IncidentEdges);

public sealed record BoundaryTopologyStats(
    int BoundaryFaceCount,
    int CanonicalEdgeSegmentCount,
    int FlatContinuationCount,
    int ConvexExteriorCount,
    int ConcaveExteriorCount,
    int InvalidDiagonalCount,
    int SimpleConvexVertexCount,
    int StraightConvexContinuationVertexCount,
    int SimpleConcaveVertexCount,
    int ComplexVertexCount,
    int NonManifoldVertexCount,
    int EligibleChamferSegmentCount,
    int SuppressedConvexSegmentCount);

public sealed class BoundaryTopology
{
    public required IReadOnlyList<BoundaryFace> Faces { get; init; }
    public required IReadOnlyDictionary<BoundaryFaceKey, BoundaryFace> FaceByKey { get; init; }
    public required IReadOnlyList<BoundaryEdgeSegment> EdgeSegments { get; init; }
    public required IReadOnlyDictionary<BoundaryEdgeKey, BoundaryEdgeSegment> EdgeByKey { get; init; }
    public required IReadOnlyList<BoundaryVertex> Vertices { get; init; }
    public required IReadOnlyDictionary<GridVertexKey, BoundaryVertex> VertexByKey { get; init; }
    public required BoundaryTopologyStats Stats { get; init; }
}

public static class BoundaryTopologyBuilder
{
    public static BoundaryTopology Build(
        StructuralOccupancy occupancy,
        MegastationPrototypeSettings settings)
    {
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);

        var faces = BuildFaces(occupancy);
        var faceByKey = faces.ToDictionary(f => f.Key);
        var incidentFacesByEdge = new Dictionary<BoundaryEdgeKey, List<BoundaryFaceKey>>();
        var incidentFacesByVertex = new Dictionary<GridVertexKey, List<BoundaryFaceKey>>();
        var incidentEdgesByVertex = new Dictionary<GridVertexKey, List<BoundaryEdgeKey>>();

        foreach (var face in faces)
        {
            foreach (var edge in face.Edges)
            {
                if (!incidentFacesByEdge.TryGetValue(edge, out var list))
                {
                    list = [];
                    incidentFacesByEdge[edge] = list;
                }
                list.Add(face.Key);
            }

            foreach (var vertex in face.Vertices)
            {
                if (!incidentFacesByVertex.TryGetValue(vertex, out var list))
                {
                    list = [];
                    incidentFacesByVertex[vertex] = list;
                }
                list.Add(face.Key);
            }

            for (int i = 0; i < 4; i++)
            {
                GridVertexKey a = face.Vertices[i];
                GridVertexKey b = face.Vertices[(i + 1) % 4];
                AddIncidentEdge(incidentEdgesByVertex, a, face.Edges[i]);
                AddIncidentEdge(incidentEdgesByVertex, b, face.Edges[i]);
            }
        }

        var preliminaryEdges = incidentFacesByEdge.Keys
            .Select(key => BuildEdge(occupancy, key, incidentFacesByEdge[key]))
            .OrderBy(e => e.Key)
            .ToArray();
        var preliminaryEdgeByKey = preliminaryEdges.ToDictionary(e => e.Key);

        var vertices = incidentFacesByVertex.Keys
            .Select(key => BuildVertex(occupancy, key, incidentFacesByVertex, incidentEdgesByVertex, preliminaryEdgeByKey))
            .OrderBy(v => v.Key)
            .ToArray();
        var vertexByKey = vertices.ToDictionary(v => v.Key);

        var edges = preliminaryEdges
            .Select(edge => edge with
            {
                ChamferEligibility = ResolveEligibility(occupancy.Grid, settings, edge, vertexByKey, out float width),
                ChamferWidth = width,
            })
            .OrderBy(e => e.Key)
            .ToArray();
        var edgeByKey = edges.ToDictionary(e => e.Key);

        var stats = new BoundaryTopologyStats(
            faces.Length,
            edges.Length,
            edges.Count(e => e.Classification == BoundaryEdgeClass.FlatContinuation),
            edges.Count(e => e.Classification == BoundaryEdgeClass.ConvexExterior),
            edges.Count(e => e.Classification == BoundaryEdgeClass.ConcaveExterior),
            edges.Count(e => e.Classification == BoundaryEdgeClass.InvalidDiagonal),
            vertices.Count(v => v.Classification == BoundaryVertexClass.SimpleConvexCorner),
            vertices.Count(v => v.Classification == BoundaryVertexClass.StraightConvexContinuation),
            vertices.Count(v => v.Classification == BoundaryVertexClass.SimpleConcaveCorner),
            vertices.Count(v => v.Classification == BoundaryVertexClass.ComplexJunction),
            vertices.Count(v => v.Classification == BoundaryVertexClass.NonManifold),
            edges.Count(e => e.ChamferEligibility == ChamferEligibility.Eligible),
            edges.Count(e => e.Classification == BoundaryEdgeClass.ConvexExterior && e.ChamferEligibility != ChamferEligibility.Eligible));

        return new BoundaryTopology
        {
            Faces = faces,
            FaceByKey = faceByKey,
            EdgeSegments = edges,
            EdgeByKey = edgeByKey,
            Vertices = vertices,
            VertexByKey = vertexByKey,
            Stats = stats,
        };
    }

    private static BoundaryFace[] BuildFaces(StructuralOccupancy occupancy)
    {
        var grid = occupancy.Grid;
        var faces = new List<BoundaryFace>();
        foreach (GridDirection direction in Enum.GetValues<GridDirection>())
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            if (!occupancy.IsOccupied(x, y, z)) continue;
            if (!ExteriorSpace.IsFaceExposed(occupancy, x, y, z, direction)) continue;

            var vertices = FaceVertices(x, y, z, direction);
            var edges = new BoundaryEdgeKey[4];
            for (int i = 0; i < 4; i++)
                edges[i] = EdgeKey(vertices[i], vertices[(i + 1) % 4]);

            faces.Add(new BoundaryFace(
                new BoundaryFaceKey(x, y, z, direction),
                direction,
                vertices,
                edges,
                occupancy.Owner(x, y, z),
                occupancy.RegionId(x, y, z) ?? string.Empty));
        }

        return faces.OrderBy(f => f.Key).ToArray();
    }

    private static GridVertexKey[] FaceVertices(int x, int y, int z, GridDirection d)
        => d switch
        {
            GridDirection.PositiveX => [new(x + 1, y, z + 1), new(x + 1, y, z), new(x + 1, y + 1, z), new(x + 1, y + 1, z + 1)],
            GridDirection.NegativeX => [new(x, y, z), new(x, y, z + 1), new(x, y + 1, z + 1), new(x, y + 1, z)],
            GridDirection.PositiveY => [new(x, y + 1, z + 1), new(x + 1, y + 1, z + 1), new(x + 1, y + 1, z), new(x, y + 1, z)],
            GridDirection.NegativeY => [new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1), new(x, y, z + 1)],
            GridDirection.PositiveZ => [new(x, y, z + 1), new(x + 1, y, z + 1), new(x + 1, y + 1, z + 1), new(x, y + 1, z + 1)],
            _                       => [new(x + 1, y, z), new(x, y, z), new(x, y + 1, z), new(x + 1, y + 1, z)],
        };

    public static BoundaryEdgeKey EdgeKey(GridVertexKey a, GridVertexKey b)
    {
        if (a.X != b.X)
        {
            int start = Math.Min(a.X, b.X);
            return new BoundaryEdgeKey(GridAxis.X, a.Y, a.Z, start);
        }
        if (a.Y != b.Y)
        {
            int start = Math.Min(a.Y, b.Y);
            return new BoundaryEdgeKey(GridAxis.Y, a.X, a.Z, start);
        }
        if (a.Z != b.Z)
        {
            int start = Math.Min(a.Z, b.Z);
            return new BoundaryEdgeKey(GridAxis.Z, a.X, a.Y, start);
        }
        throw new ArgumentException("Boundary edge endpoints must differ on exactly one axis.");
    }

    private static void AddIncidentEdge(Dictionary<GridVertexKey, List<BoundaryEdgeKey>> map, GridVertexKey vertex, BoundaryEdgeKey edge)
    {
        if (!map.TryGetValue(vertex, out var list))
        {
            list = [];
            map[vertex] = list;
        }
        if (!list.Contains(edge))
            list.Add(edge);
    }

    private static BoundaryEdgeSegment BuildEdge(
        StructuralOccupancy occupancy,
        BoundaryEdgeKey key,
        IReadOnlyList<BoundaryFaceKey> incidentFaces)
    {
        var (q0, q1, q2, q3) = OccupiedQuadrants(occupancy, key);
        int count = (q0 ? 1 : 0) + (q1 ? 1 : 0) + (q2 ? 1 : 0) + (q3 ? 1 : 0);
        BoundaryEdgeClass classification = count switch
        {
            0 => BoundaryEdgeClass.Internal,
            1 => BoundaryEdgeClass.ConvexExterior,
            2 when (q0 && q3) || (q1 && q2) => BoundaryEdgeClass.InvalidDiagonal,
            2 => BoundaryEdgeClass.FlatContinuation,
            3 => BoundaryEdgeClass.ConcaveExterior,
            _ => BoundaryEdgeClass.Internal,
        };

        var (start, end) = VerticesFor(key);
        return new BoundaryEdgeSegment(
            key,
            classification,
            incidentFaces.OrderBy(f => f).ToArray(),
            start,
            end,
            ChamferEligibility.Ineligible,
            0f);
    }

    private static (bool, bool, bool, bool) OccupiedQuadrants(StructuralOccupancy occupancy, BoundaryEdgeKey key)
    {
        return key.Axis switch
        {
            GridAxis.X => (
                occupancy.IsOccupied(key.Start, key.A - 1, key.B - 1),
                occupancy.IsOccupied(key.Start, key.A, key.B - 1),
                occupancy.IsOccupied(key.Start, key.A - 1, key.B),
                occupancy.IsOccupied(key.Start, key.A, key.B)),
            GridAxis.Y => (
                occupancy.IsOccupied(key.A - 1, key.Start, key.B - 1),
                occupancy.IsOccupied(key.A, key.Start, key.B - 1),
                occupancy.IsOccupied(key.A - 1, key.Start, key.B),
                occupancy.IsOccupied(key.A, key.Start, key.B)),
            _ => (
                occupancy.IsOccupied(key.A - 1, key.B - 1, key.Start),
                occupancy.IsOccupied(key.A, key.B - 1, key.Start),
                occupancy.IsOccupied(key.A - 1, key.B, key.Start),
                occupancy.IsOccupied(key.A, key.B, key.Start)),
        };
    }

    public static (GridVertexKey start, GridVertexKey end) VerticesFor(BoundaryEdgeKey key)
        => key.Axis switch
        {
            GridAxis.X => (new GridVertexKey(key.Start, key.A, key.B), new GridVertexKey(key.Start + 1, key.A, key.B)),
            GridAxis.Y => (new GridVertexKey(key.A, key.Start, key.B), new GridVertexKey(key.A, key.Start + 1, key.B)),
            _          => (new GridVertexKey(key.A, key.B, key.Start), new GridVertexKey(key.A, key.B, key.Start + 1)),
        };

    private static BoundaryVertex BuildVertex(
        StructuralOccupancy occupancy,
        GridVertexKey key,
        IReadOnlyDictionary<GridVertexKey, List<BoundaryFaceKey>> incidentFacesByVertex,
        IReadOnlyDictionary<GridVertexKey, List<BoundaryEdgeKey>> incidentEdgesByVertex,
        IReadOnlyDictionary<BoundaryEdgeKey, BoundaryEdgeSegment> edgeByKey)
    {
        var faces = incidentFacesByVertex.TryGetValue(key, out var fl) ? fl.OrderBy(f => f).ToArray() : [];
        var edges = incidentEdgesByVertex.TryGetValue(key, out var el) ? el.OrderBy(e => e).ToArray() : [];
        int occupiedOctants = CountOccupiedOctants(occupancy, key);
        var convexEdges = edges.Where(e => edgeByKey[e].Classification == BoundaryEdgeClass.ConvexExterior).ToArray();
        var invalidEdges = edges.Where(e => edgeByKey[e].Classification == BoundaryEdgeClass.InvalidDiagonal).ToArray();
        bool hasConcave = edges.Any(e => edgeByKey[e].Classification == BoundaryEdgeClass.ConcaveExterior);

        BoundaryVertexClass classification;
        if (invalidEdges.Length > 0)
            classification = BoundaryVertexClass.NonManifold;
        else if (occupiedOctants == 1)
            classification = BoundaryVertexClass.SimpleConvexCorner;
        else if (occupiedOctants == 7)
            classification = BoundaryVertexClass.SimpleConcaveCorner;
        else if (!hasConcave && occupiedOctants == 2 && convexEdges.Length == 2 && convexEdges[0].Axis == convexEdges[1].Axis)
            classification = BoundaryVertexClass.StraightConvexContinuation;
        else if (faces.Length == 0)
            classification = BoundaryVertexClass.Empty;
        else
            classification = BoundaryVertexClass.ComplexJunction;

        return new BoundaryVertex(key, classification, faces, edges);
    }

    private static int CountOccupiedOctants(StructuralOccupancy occupancy, GridVertexKey vertex)
    {
        int count = 0;
        for (int dx = -1; dx <= 0; dx++)
        for (int dy = -1; dy <= 0; dy++)
        for (int dz = -1; dz <= 0; dz++)
            if (occupancy.IsOccupied(vertex.X + dx, vertex.Y + dy, vertex.Z + dz))
                count++;
        return count;
    }

    private static ChamferEligibility ResolveEligibility(
        SliceGrid grid,
        MegastationPrototypeSettings settings,
        BoundaryEdgeSegment edge,
        IReadOnlyDictionary<GridVertexKey, BoundaryVertex> vertices,
        out float width)
    {
        width = 0f;
        if (edge.Classification == BoundaryEdgeClass.InvalidDiagonal)
            return ChamferEligibility.SuppressedInvalidNearby;
        if (edge.Classification != BoundaryEdgeClass.ConvexExterior)
            return ChamferEligibility.Ineligible;
        if (edge.IncidentFaces.Count != 2)
            return ChamferEligibility.SuppressedInvalidNearby;

        BoundaryVertex start = vertices[edge.StartVertex];
        BoundaryVertex end = vertices[edge.EndVertex];
        if (!EndpointSupportsChamfer(start, edge) || !EndpointSupportsChamfer(end, edge))
            return ChamferEligibility.SuppressedComplexEndpoint;

        float shortest = ShortestRelevantSpan(grid, edge);
        width = MathF.Min(settings.DesiredStructuralChamferMetres, shortest * settings.StructuralChamferSpanFraction);
        if (width < settings.MinimumStructuralChamferMetres)
        {
            width = 0f;
            return ChamferEligibility.SuppressedTooSmall;
        }

        return ChamferEligibility.Eligible;
    }

    private static bool EndpointSupportsChamfer(BoundaryVertex vertex, BoundaryEdgeSegment edge)
    {
        if (vertex.Classification != BoundaryVertexClass.SimpleConvexCorner)
            return false;
        return vertex.IncidentEdges
            .Where(e => e != edge.Key)
            .All(e => e.Axis != edge.Key.Axis);
    }

    private static float ShortestRelevantSpan(SliceGrid grid, BoundaryEdgeSegment edge)
    {
        float min = grid.GetCellSize(edge.Key.Axis, edge.Key.Start);
        foreach (GridAxis axis in Enum.GetValues<GridAxis>())
        {
            if (axis == edge.Key.Axis) continue;
            int vertexCoord = axis switch
            {
                GridAxis.X => edge.StartVertex.X,
                GridAxis.Y => edge.StartVertex.Y,
                _          => edge.StartVertex.Z,
            };
            if (vertexCoord > 0)
                min = MathF.Min(min, grid.GetCellSize(axis, vertexCoord - 1));
            if (vertexCoord < grid.Count(axis))
                min = MathF.Min(min, grid.GetCellSize(axis, vertexCoord));
        }
        return min;
    }

    public static Vector3 Position(SliceGrid grid, GridVertexKey vertex)
        => new(
            Coordinate(grid, GridAxis.X, vertex.X),
            Coordinate(grid, GridAxis.Y, vertex.Y),
            Coordinate(grid, GridAxis.Z, vertex.Z));

    private static float Coordinate(SliceGrid grid, GridAxis axis, int vertexIndex)
        => vertexIndex == grid.Count(axis)
            ? grid.GetCellMaximum(axis, vertexIndex - 1)
            : grid.GetCellMinimum(axis, vertexIndex);

    public static Vector3 Normal(GridDirection direction)
        => direction switch
        {
            GridDirection.NegativeX => -Vector3.UnitX,
            GridDirection.PositiveX => Vector3.UnitX,
            GridDirection.NegativeY => -Vector3.UnitY,
            GridDirection.PositiveY => Vector3.UnitY,
            GridDirection.NegativeZ => -Vector3.UnitZ,
            _                       => Vector3.UnitZ,
        };
}
