using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationMeshStats(
    MegastationMeshPath MeshPath,
    int ExposedQuadCount,
    int TriangleCount,
    int VertexCount,
    int MeshPageCount,
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
    int SuppressedConvexSegmentCount,
    int ChamferRunCount,
    int SuppressedChamferRunCount,
    int BevelQuadCount,
    int CornerCapCount,
    long TopologyBuildMilliseconds,
    long MeshBuildMilliseconds,
    BoundaryMeshValidationReport SharpValidation,
    BoundaryMeshValidationReport ChamferedValidation,
    BoundaryTopologySignature TopologySignature,
    ChamferSemanticValidationReport ChamferSemanticValidation,
    IReadOnlyList<ChamferRunDiagnostics> ChamferRuns);

public sealed record ChamferSemanticValidationReport(
    int TaperOnlyRenderedRunCount,
    int NearZeroAreaRenderedRunCount,
    int MissingFaceRetractionRunCount)
{
    public bool IsValid => TaperOnlyRenderedRunCount == 0
        && NearZeroAreaRenderedRunCount == 0
        && MissingFaceRetractionRunCount == 0;
}

public sealed record ChamferRunDiagnostics(
    string Identity,
    GridAxis EdgeAxis,
    GridDirection IncidentNormalA,
    GridDirection IncidentNormalB,
    int CanonicalSegmentCount,
    float PhysicalRunLength,
    float ResolvedChamferWidth,
    BoundaryVertexClass StartEndpointClassification,
    BoundaryVertexClass EndEndpointClassification,
    float StartTaperLength,
    float EndTaperLength,
    float FullWidthCentreLength,
    float FaceAMaximumRetraction,
    float FaceBMaximumRetraction,
    int BevelQuadCount,
    int BevelTriangleCount,
    float BevelSurfaceArea,
    bool Rendered,
    string SuppressedReason);

public enum MegastationMeshPath
{
    SharpFallback,
    Chamfered,
    TopologyDebug,
}

public enum MegastationDebugColorMode
{
    StructuralVsUrban,
    RegionOwner,
    OutwardNormal,
    EdgeClassification,
    ChamferEligibility,
    VertexComplexity,
    RunValidation,
}

public static class MegastationPrototypeMeshBuilder
{
    private static readonly Color StructuralColor = new(86, 96, 104);
    private static readonly Color UrbanColor = new(128, 111, 90);
    private static readonly Color FaceColor = new(132, 113, 92);
    private static readonly Color EdgeColor = new(118, 128, 104);
    private static readonly Color CornerColor = new(136, 98, 108);

    public static MegastationMeshStats Build(
        StructuralOccupancy occupancy,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode = MegastationDebugColorMode.StructuralVsUrban,
        MegastationPrototypeSettings? settings = null)
    {
        settings ??= MegastationPrototypeSettings.Default;
        var stopwatch = Stopwatch.StartNew();
        BoundaryTopology topology = BoundaryTopologyBuilder.Build(occupancy, settings);
        stopwatch.Stop();
        long topologyMs = stopwatch.ElapsedMilliseconds;

        bool requireValidStructuralBoundary = settings.EnableTopologyRegularisation;
        if (requireValidStructuralBoundary && topology.Stats.InvalidDiagonalCount != 0)
            throw new InvalidOperationException($"Regularised megastation boundary still contains {topology.Stats.InvalidDiagonalCount} invalid diagonal edge configurations.");

        Vector3 boundsHalf = new(
            occupancy.Grid.Dimension(GridAxis.X) * 0.5f,
            occupancy.Grid.Dimension(GridAxis.Y) * 0.5f,
            occupancy.Grid.Dimension(GridAxis.Z) * 0.5f);
        Vector3 boundsMin = -boundsHalf;
        Vector3 boundsMax = boundsHalf;

        var sharpMesh = new StationModuleMesh();
        AddSharpStructuralFaces(topology, occupancy, sharpMesh, debugColorMode);
        sharpMesh.ApplyIlluminationFlags();
        BoundaryMeshValidationReport sharpValidation = BoundaryMeshValidator.Validate(sharpMesh, boundsMin, boundsMax);
        if (requireValidStructuralBoundary && !sharpValidation.IsValid)
            throw new InvalidOperationException($"Sharp megastation boundary mesh is invalid: {sharpValidation.Summary}.");

        stopwatch.Restart();
        ChamferPlan chamferPlan = BuildChamferPlan(topology, occupancy.Grid, settings);
        int faceQuads = AddStructuralFaces(topology, occupancy, mesh, debugColorMode, chamferPlan);
        int bevelQuads = AddBevels(topology, occupancy.Grid, mesh, debugColorMode, chamferPlan);
        int cornerCaps = AddCornerCaps(topology, occupancy.Grid, mesh, debugColorMode, chamferPlan);
        mesh.ApplyIlluminationFlags();
        stopwatch.Stop();

        var (_, indices) = mesh.ToIntArrays();
        BoundaryMeshValidationReport finalValidation = BoundaryMeshValidator.Validate(mesh, boundsMin, boundsMax);
        if (requireValidStructuralBoundary && !finalValidation.IsValid)
            throw new InvalidOperationException($"Final megastation render mesh is invalid: {finalValidation.Summary}.");
        ChamferSemanticValidationReport chamferValidation = ValidateChamferSemantics(chamferPlan);
        if (requireValidStructuralBoundary && !chamferValidation.IsValid)
            throw new InvalidOperationException(
                $"Final megastation chamfer semantics are invalid: taperOnly={chamferValidation.TaperOnlyRenderedRunCount}, nearZeroArea={chamferValidation.NearZeroAreaRenderedRunCount}, missingRetraction={chamferValidation.MissingFaceRetractionRunCount}.");

        MegastationMeshPath path = IsTopologyDebug(debugColorMode)
            ? MegastationMeshPath.TopologyDebug
            : bevelQuads > 0 || cornerCaps > 0 ? MegastationMeshPath.Chamfered : MegastationMeshPath.SharpFallback;
        return new MegastationMeshStats(
            path,
            faceQuads,
            indices.Length / 3,
            finalValidation.VertexCount,
            1,
            topology.Stats.BoundaryFaceCount,
            topology.Stats.CanonicalEdgeSegmentCount,
            topology.Stats.FlatContinuationCount,
            topology.Stats.ConvexExteriorCount,
            topology.Stats.ConcaveExteriorCount,
            topology.Stats.InvalidDiagonalCount,
            topology.Stats.SimpleConvexVertexCount,
            topology.Stats.StraightConvexContinuationVertexCount,
            topology.Stats.SimpleConcaveVertexCount,
            topology.Stats.ComplexVertexCount,
            topology.Stats.NonManifoldVertexCount,
            chamferPlan.AcceptedEdges.Count,
            topology.Stats.ConvexExteriorCount - chamferPlan.AcceptedEdges.Count,
            chamferPlan.AcceptedRunCount,
            chamferPlan.SuppressedRunCount,
            bevelQuads,
            cornerCaps,
            topologyMs,
            stopwatch.ElapsedMilliseconds,
            sharpValidation,
            finalValidation,
            BoundaryTopologySignatureBuilder.Compute(topology, settings),
            chamferValidation,
            chamferPlan.Diagnostics);
    }

    private static int AddSharpStructuralFaces(
        BoundaryTopology topology,
        StructuralOccupancy occupancy,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode)
    {
        foreach (var face in topology.Faces)
        {
            var p = face.Vertices.Select(v => BoundaryTopologyBuilder.Position(occupancy.Grid, v)).ToArray();
            Color color = ColorFor(topology, occupancy, face, debugColorMode);
            AddQuad(mesh, p[0], p[1], p[2], p[3], BoundaryTopologyBuilder.Normal(face.Direction), color);
        }
        return topology.Faces.Count;
    }

    private static int AddStructuralFaces(
        BoundaryTopology topology,
        StructuralOccupancy occupancy,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode,
        ChamferPlan chamferPlan)
    {
        foreach (var face in topology.Faces)
        {
            var p = new Vector3[4];
            for (int i = 0; i < 4; i++)
                p[i] = RetractedFaceVertex(topology, occupancy.Grid, face, face.Vertices[i], chamferPlan);

            Color color = ColorFor(topology, occupancy, face, debugColorMode);
            AddQuad(mesh, p[0], p[1], p[2], p[3], BoundaryTopologyBuilder.Normal(face.Direction), color);
        }
        return topology.Faces.Count;
    }

    private static Vector3 Retraction(BoundaryTopology topology, BoundaryFace face, BoundaryEdgeKey edgeKey, ChamferPlan chamferPlan)
    {
        if (!chamferPlan.AcceptedEdges.TryGetValue(edgeKey, out float width)) return Vector3.Zero;
        var edge = topology.EdgeByKey[edgeKey];
        foreach (BoundaryFaceKey incident in edge.IncidentFaces)
        {
            if (incident == face.Key) continue;
            return -BoundaryTopologyBuilder.Normal(incident.Direction) * width;
        }
        return Vector3.Zero;
    }

    private static Vector3 RetractedFaceVertex(
        BoundaryTopology topology,
        SliceGrid grid,
        BoundaryFace face,
        GridVertexKey vertex,
        ChamferPlan chamferPlan)
    {
        int index = Array.IndexOf(face.Vertices, vertex);
        if (index < 0)
            throw new ArgumentException("Vertex is not part of the face.", nameof(vertex));
        if (chamferPlan.CollapsedEndpoints.Contains(vertex))
            return BoundaryTopologyBuilder.Position(grid, vertex);

        BoundaryEdgeKey before = face.Edges[(index + 3) % 4];
        BoundaryEdgeKey after = face.Edges[index];
        return BoundaryTopologyBuilder.Position(grid, vertex)
            + Retraction(topology, face, before, chamferPlan)
            + Retraction(topology, face, after, chamferPlan);
    }

    private static int AddBevels(
        BoundaryTopology topology,
        SliceGrid grid,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode,
        ChamferPlan chamferPlan)
    {
        int count = 0;
        foreach (var edge in topology.EdgeSegments.Where(e => chamferPlan.AcceptedEdges.ContainsKey(e.Key)))
        {
            BoundaryFaceKey aKey = edge.IncidentFaces[0];
            BoundaryFaceKey bKey = edge.IncidentFaces[1];
            BoundaryFace aFace = topology.FaceByKey[aKey];
            BoundaryFace bFace = topology.FaceByKey[bKey];
            Vector3 normal = Vector3.Normalize(BoundaryTopologyBuilder.Normal(aKey.Direction) + BoundaryTopologyBuilder.Normal(bKey.Direction));
            Vector3 a0 = RetractedFaceVertex(topology, grid, aFace, edge.StartVertex, chamferPlan);
            Vector3 a1 = RetractedFaceVertex(topology, grid, aFace, edge.EndVertex, chamferPlan);
            Vector3 b1 = RetractedFaceVertex(topology, grid, bFace, edge.EndVertex, chamferPlan);
            Vector3 b0 = RetractedFaceVertex(topology, grid, bFace, edge.StartVertex, chamferPlan);
            if (NearlySame(a0, b0) && NearlySame(a1, b1))
                continue;
            if (NearlySame(a0, b0))
                AddTriangle(mesh, a0, a1, b1, normal, DebugColorForEdge(edge, debugColorMode, StructuralColor));
            else if (NearlySame(a1, b1))
                AddTriangle(mesh, a0, a1, b0, normal, DebugColorForEdge(edge, debugColorMode, StructuralColor));
            else
                AddQuad(mesh, a0, a1, b1, b0, normal, DebugColorForEdge(edge, debugColorMode, StructuralColor));
            count++;
        }
        return count;
    }

    private static int AddCornerCaps(
        BoundaryTopology topology,
        SliceGrid grid,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode,
        ChamferPlan chamferPlan)
    {
        int count = 0;
        foreach (var vertex in topology.Vertices.Where(v => v.Classification == BoundaryVertexClass.SimpleConvexCorner))
        {
            var faces = vertex.IncidentFaces
                .Select(f => topology.FaceByKey[f])
                .GroupBy(f => f.Direction)
                .Select(g => g.First())
                .ToArray();
            var normals = faces.Select(f => BoundaryTopologyBuilder.Normal(f.Direction)).ToArray();
            var edges = vertex.IncidentEdges
                .Select(e => topology.EdgeByKey[e])
                .Where(e => chamferPlan.AcceptedEdges.ContainsKey(e.Key))
                .ToArray();
            if (normals.Length != 3 || edges.Length != 3) continue;

            Vector3 p = BoundaryTopologyBuilder.Position(grid, vertex.Key);
            Vector3 expected = Vector3.Normalize(normals[0] + normals[1] + normals[2]);
            var points = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = Vector3.Zero;
                for (int j = 0; j < 3; j++)
                {
                    if (i == j) continue;
                    BoundaryEdgeSegment edge = edges.Single(e => faces[i].Edges.Contains(e.Key) && faces[j].Edges.Contains(e.Key));
                    offset -= normals[j] * chamferPlan.AcceptedEdges[edge.Key];
                }
                points[i] = p + offset;
            }
            AddTriangle(mesh, points[0], points[1], points[2], expected, DebugColorForVertex(vertex, debugColorMode, CornerColor));
            count++;
        }
        return count;
    }

    private static int AddRunTerminationCaps(
        BoundaryTopology topology,
        SliceGrid grid,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode,
        ChamferPlan chamferPlan)
    {
        int count = 0;
        var acceptedAtVertex = AcceptedEdgeCountsByVertex(topology, chamferPlan);
        foreach (var run in chamferPlan.AcceptedRuns)
        foreach (GridVertexKey endpoint in run.Endpoints)
        {
            if (acceptedAtVertex.GetValueOrDefault(endpoint) != 1) continue;
            BoundaryEdgeSegment edge = run.Edges.First(e => e.StartVertex == endpoint || e.EndVertex == endpoint);
            BoundaryFace aFace = topology.FaceByKey[edge.IncidentFaces[0]];
            BoundaryFace bFace = topology.FaceByKey[edge.IncidentFaces[1]];
            Vector3 p = BoundaryTopologyBuilder.Position(grid, endpoint);
            Vector3 a = RetractedFaceVertex(topology, grid, aFace, endpoint, chamferPlan);
            Vector3 b = RetractedFaceVertex(topology, grid, bFace, endpoint, chamferPlan);
            GridVertexKey other = edge.StartVertex == endpoint ? edge.EndVertex : edge.StartVertex;
            Vector3 expected = Vector3.Normalize(p - BoundaryTopologyBuilder.Position(grid, other));
            AddTriangle(mesh, p, a, b, expected, DebugColorForVertex(topology.VertexByKey[endpoint], debugColorMode, CornerColor));
            count++;
        }
        return count;
    }

    private static ChamferPlan BuildChamferPlan(
        BoundaryTopology topology,
        SliceGrid grid,
        MegastationPrototypeSettings settings)
    {
        var candidates = topology.EdgeSegments
            .Where(e => e.Classification == BoundaryEdgeClass.ConvexExterior && e.IncidentFaces.Count == 2)
            .Select(e => new ChamferCandidate(e, RunKeyFor(topology, grid, settings, e), ResolveWidth(grid, settings, e)))
            .Where(c => c.Width >= settings.MinimumStructuralChamferMetres)
            .ToArray();

        var candidatesByEdge = candidates.ToDictionary(c => c.Edge.Key);
        var candidatesByVertex = new Dictionary<GridVertexKey, List<ChamferCandidate>>();
        foreach (var candidate in candidates)
        {
            AddCandidate(candidatesByVertex, candidate.Edge.StartVertex, candidate);
            AddCandidate(candidatesByVertex, candidate.Edge.EndVertex, candidate);
        }

        var seen = new HashSet<BoundaryEdgeKey>();
        var runs = new List<ChamferRun>();
        foreach (var candidate in candidates.OrderBy(c => c.Edge.Key))
        {
            if (!seen.Add(candidate.Edge.Key)) continue;
            var edges = new List<BoundaryEdgeSegment>();
            var q = new Queue<ChamferCandidate>();
            q.Enqueue(candidate);
            while (q.Count > 0)
            {
                var current = q.Dequeue();
                edges.Add(current.Edge);
                foreach (GridVertexKey vertex in new[] { current.Edge.StartVertex, current.Edge.EndVertex })
                {
                    if (!candidatesByVertex.TryGetValue(vertex, out var adjacent)) continue;
                    foreach (var next in adjacent)
                    {
                        if (next.Key != candidate.Key) continue;
                        if (!seen.Add(next.Edge.Key)) continue;
                        q.Enqueue(next);
                    }
                }
            }

            GridVertexKey[] endpoints = EndpointsForRun(edges);
            float width = edges.Min(e => candidatesByEdge[e.Key].Width);
            float physicalLength = RunPhysicalLength(grid, edges);
            float taper = MathF.Min(width, physicalLength * 0.25f);
            float fullWidthCentreLength = physicalLength - taper - taper;
            string suppressedReason = SuppressionReason(topology, endpoints, fullWidthCentreLength, settings);
            bool accepted = suppressedReason.Length == 0;
            runs.Add(new ChamferRun(
                candidate.Key,
                edges.OrderBy(e => e.Key).ToArray(),
                endpoints,
                width,
                physicalLength,
                taper,
                taper,
                fullWidthCentreLength,
                accepted,
                suppressedReason));
        }

        bool changed;
        do
        {
            changed = false;
            var currentlyAcceptedEdges = runs.Where(r => r.Accepted).SelectMany(r => r.Edges).ToDictionary(e => e.Key);
            foreach (var vertex in topology.Vertices)
            {
                int acceptedAtVertex = vertex.IncidentEdges.Count(currentlyAcceptedEdges.ContainsKey);
                if (VertexSupportsAcceptedEdgeCount(topology, vertex, currentlyAcceptedEdges, acceptedAtVertex)) continue;

                foreach (var run in runs.Where(r => r.Accepted && r.Endpoints.Contains(vertex.Key)))
                {
                    run.Accepted = false;
                    run.SuppressedReason = "vertex-conflict";
                    changed = true;
                }
            }
        }
        while (changed);

        var acceptedEdges = new Dictionary<BoundaryEdgeKey, float>();
        foreach (var run in runs.Where(r => r.Accepted))
        foreach (var edge in run.Edges)
            acceptedEdges[edge.Key] = run.Width;

        var acceptedRuns = runs.Where(r => r.Accepted).OrderBy(r => r.Edges[0].Key).ToArray();
        var diagnostics = runs
            .OrderBy(r => r.Edges[0].Key)
            .Select(r => BuildRunDiagnostics(topology, r))
            .ToArray();

        return new ChamferPlan(
            acceptedEdges,
            acceptedRuns,
            CollapsedEndpoints(topology, acceptedEdges),
            diagnostics,
            runs.Count(r => r.Accepted),
            runs.Count(r => !r.Accepted));
    }

    private static IReadOnlySet<GridVertexKey> CollapsedEndpoints(
        BoundaryTopology topology,
        IReadOnlyDictionary<BoundaryEdgeKey, float> acceptedEdges)
        => topology.Vertices
            .Where(v => v.Classification == BoundaryVertexClass.SimpleConvexCorner)
            .Where(v =>
            {
                int count = v.IncidentEdges.Count(acceptedEdges.ContainsKey);
                return count is 1 or 2;
            })
            .Select(v => v.Key)
            .ToHashSet();

    private static string SuppressionReason(
        BoundaryTopology topology,
        IReadOnlyList<GridVertexKey> endpoints,
        float fullWidthCentreLength,
        MegastationPrototypeSettings settings)
    {
        if (endpoints.Count != 2)
            return "not-a-complete-open-run";
        if (fullWidthCentreLength <= settings.MinimumStructuralChamferMetres)
            return "taper-only-or-too-short";
        if (!endpoints.All(e => EndpointSupportsCompleteRun(topology.VertexByKey[e].Classification)))
            return "complex-endpoint";
        return string.Empty;
    }

    private static bool EndpointSupportsCompleteRun(BoundaryVertexClass classification)
        => classification is BoundaryVertexClass.SimpleConvexCorner or BoundaryVertexClass.StraightConvexContinuation;

    private static bool VertexSupportsAcceptedEdgeCount(
        BoundaryTopology topology,
        BoundaryVertex vertex,
        IReadOnlyDictionary<BoundaryEdgeKey, BoundaryEdgeSegment> acceptedEdges,
        int acceptedAtVertex)
    {
        if (acceptedAtVertex == 0) return true;
        return vertex.Classification switch
        {
            BoundaryVertexClass.SimpleConvexCorner => acceptedAtVertex <= 3,
            BoundaryVertexClass.StraightConvexContinuation => acceptedAtVertex == 2
                && vertex.IncidentEdges.Where(acceptedEdges.ContainsKey).Select(e => e.Axis).Distinct().Count() == 1,
            _ => false,
        };
    }

    private static Dictionary<GridVertexKey, int> AcceptedEdgeCountsByVertex(BoundaryTopology topology, ChamferPlan plan)
    {
        var counts = new Dictionary<GridVertexKey, int>();
        foreach (var edge in topology.EdgeSegments.Where(e => plan.AcceptedEdges.ContainsKey(e.Key)))
        {
            counts[edge.StartVertex] = counts.GetValueOrDefault(edge.StartVertex) + 1;
            counts[edge.EndVertex] = counts.GetValueOrDefault(edge.EndVertex) + 1;
        }
        return counts;
    }

    private static void AddCandidate(Dictionary<GridVertexKey, List<ChamferCandidate>> map, GridVertexKey vertex, ChamferCandidate candidate)
    {
        if (!map.TryGetValue(vertex, out var list))
        {
            list = [];
            map[vertex] = list;
        }
        list.Add(candidate);
    }

    private static GridVertexKey[] EndpointsForRun(IReadOnlyList<BoundaryEdgeSegment> edges)
    {
        var counts = new Dictionary<GridVertexKey, int>();
        foreach (var edge in edges)
        {
            counts[edge.StartVertex] = counts.GetValueOrDefault(edge.StartVertex) + 1;
            counts[edge.EndVertex] = counts.GetValueOrDefault(edge.EndVertex) + 1;
        }
        return counts.Where(kv => kv.Value == 1).Select(kv => kv.Key).Order().ToArray();
    }

    private static ChamferRunKey RunKeyFor(
        BoundaryTopology topology,
        SliceGrid grid,
        MegastationPrototypeSettings settings,
        BoundaryEdgeSegment edge)
    {
        var normals = edge.IncidentFaces
            .Select(f => f.Direction)
            .Order()
            .ToArray();
        return new ChamferRunKey(edge.Key.Axis, edge.Key.A, edge.Key.B, normals[0], normals[1]);
    }

    private static float ResolveWidth(SliceGrid grid, MegastationPrototypeSettings settings, BoundaryEdgeSegment edge)
    {
        float shortest = ShortestRelevantSpan(grid, edge);
        return MathF.Min(settings.DesiredStructuralChamferMetres, shortest * settings.StructuralChamferSpanFraction);
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

    private static float RunPhysicalLength(SliceGrid grid, IReadOnlyList<BoundaryEdgeSegment> edges)
        => edges.Sum(e => grid.GetCellSize(e.Key.Axis, e.Key.Start));

    private static ChamferRunDiagnostics BuildRunDiagnostics(BoundaryTopology topology, ChamferRun run)
    {
        BoundaryEdgeSegment first = run.Edges[0];
        var normals = first.IncidentFaces.Select(f => f.Direction).Order().ToArray();
        float surfaceArea = run.Accepted
            ? run.Edges.Sum(e => SegmentLength(run, e) * run.Width * MathF.Sqrt(2f))
            : 0f;
        return new ChamferRunDiagnostics(
            RunIdentity(run),
            first.Key.Axis,
            normals[0],
            normals[1],
            run.Edges.Count,
            run.PhysicalLength,
            run.Width,
            run.Endpoints.Count > 0 ? topology.VertexByKey[run.Endpoints[0]].Classification : BoundaryVertexClass.Empty,
            run.Endpoints.Count > 1 ? topology.VertexByKey[run.Endpoints[^1]].Classification : BoundaryVertexClass.Empty,
            run.Accepted ? 0f : run.StartTaperLength,
            run.Accepted ? 0f : run.EndTaperLength,
            run.Accepted ? run.PhysicalLength : run.FullWidthCentreLength,
            run.Accepted ? run.Width : 0f,
            run.Accepted ? run.Width : 0f,
            run.Accepted ? run.Edges.Count : 0,
            0,
            surfaceArea,
            run.Accepted,
            run.Accepted ? string.Empty : run.SuppressedReason);
    }

    private static float SegmentLength(ChamferRun run, BoundaryEdgeSegment edge)
        => run.PhysicalLength <= 0f ? 0f : run.PhysicalLength / run.Edges.Count;

    private static string RunIdentity(ChamferRun run)
    {
        BoundaryEdgeSegment first = run.Edges[0];
        BoundaryEdgeSegment last = run.Edges[^1];
        return $"{first.Key.Axis}:{first.Key.A}:{first.Key.B}:{first.Key.Start}-{last.Key.Start + 1}:{run.Key.NormalA}/{run.Key.NormalB}";
    }

    private static ChamferSemanticValidationReport ValidateChamferSemantics(ChamferPlan plan)
    {
        int taperOnly = plan.Diagnostics.Count(r => r.Rendered && r.FullWidthCentreLength <= 0.0001f);
        int nearZeroArea = plan.Diagnostics.Count(r => r.Rendered && r.BevelSurfaceArea <= 0.0001f);
        int missingRetraction = plan.Diagnostics.Count(r => r.Rendered
            && (r.FaceAMaximumRetraction < r.ResolvedChamferWidth - 0.0001f
                || r.FaceBMaximumRetraction < r.ResolvedChamferWidth - 0.0001f));
        return new ChamferSemanticValidationReport(taperOnly, nearZeroArea, missingRetraction);
    }

    private static void AddQuad(StationModuleMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 expectedNormal, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) < 0f)
            mesh.AddQuad(a, d, c, b, color);
        else
            mesh.AddQuad(a, b, c, d, color);
    }

    private static void AddTriangle(StationModuleMesh mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 expectedNormal, Color color)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) < 0f)
            mesh.AddTriangle(a, c, b, color);
        else
            mesh.AddTriangle(a, b, c, color);
    }

    private static bool NearlySame(Vector3 a, Vector3 b)
        => Vector3.DistanceSquared(a, b) < 0.000001f;

    private static Color ColorFor(
        BoundaryTopology topology,
        StructuralOccupancy occupancy,
        BoundaryFace face,
        MegastationDebugColorMode mode)
    {
        return mode switch
        {
            MegastationDebugColorMode.RegionOwner => face.Owner switch
            {
                MegacellOwner.FaceInterior => FaceColor,
                MegacellOwner.EdgeRegion   => EdgeColor,
                MegacellOwner.CornerRegion => CornerColor,
                _                          => StructuralColor,
            },
            MegastationDebugColorMode.OutwardNormal => face.Direction switch
            {
                GridDirection.PositiveX => new Color(165, 70, 70),
                GridDirection.NegativeX => new Color(90, 35, 35),
                GridDirection.PositiveY => new Color(70, 150, 80),
                GridDirection.NegativeY => new Color(35, 90, 45),
                GridDirection.PositiveZ => new Color(80, 95, 165),
                _                       => new Color(40, 50, 95),
            },
            MegastationDebugColorMode.EdgeClassification => FaceEdgeDebugColor(topology, face),
            MegastationDebugColorMode.ChamferEligibility => FaceChamferDebugColor(topology, face),
            MegastationDebugColorMode.VertexComplexity => FaceVertexDebugColor(topology, face),
            _ => occupancy.IsUrban(face.Key.X, face.Key.Y, face.Key.Z) ? UrbanColor : StructuralColor,
        };
    }

    private static Color FaceEdgeDebugColor(BoundaryTopology topology, BoundaryFace face)
        => DebugColorForEdge(face.Edges.Select(e => topology.EdgeByKey[e]).FirstOrDefault(e => e.Classification != BoundaryEdgeClass.FlatContinuation),
            MegastationDebugColorMode.EdgeClassification,
            new Color(90, 90, 90));

    private static Color FaceChamferDebugColor(BoundaryTopology topology, BoundaryFace face)
        => face.Edges.Any(e => topology.EdgeByKey[e].ChamferEligibility == ChamferEligibility.Eligible)
            ? new Color(235, 210, 80)
            : new Color(80, 80, 80);

    private static Color FaceVertexDebugColor(BoundaryTopology topology, BoundaryFace face)
    {
        var vertex = face.Vertices
            .Select(v => topology.VertexByKey[v])
            .OrderByDescending(v => VertexComplexityRank(v.Classification))
            .First();
        return DebugColorForVertex(vertex, MegastationDebugColorMode.VertexComplexity, StructuralColor);
    }

    private static int VertexComplexityRank(BoundaryVertexClass classification)
        => classification switch
        {
            BoundaryVertexClass.NonManifold => 5,
            BoundaryVertexClass.ComplexJunction => 4,
            BoundaryVertexClass.SimpleConcaveCorner => 3,
            BoundaryVertexClass.StraightConvexContinuation => 2,
            BoundaryVertexClass.SimpleConvexCorner => 1,
            _ => 0,
        };

    private static Color DebugColorForEdge(BoundaryEdgeSegment? edge, MegastationDebugColorMode mode, Color fallback)
    {
        if (edge is null) return fallback;
        if (mode is MegastationDebugColorMode.ChamferEligibility or MegastationDebugColorMode.RunValidation)
            return edge.ChamferEligibility == ChamferEligibility.Eligible ? new Color(235, 210, 80) : new Color(80, 80, 80);
        if (mode != MegastationDebugColorMode.EdgeClassification)
            return fallback;
        return edge.Classification switch
        {
            BoundaryEdgeClass.ConvexExterior => new Color(220, 180, 65),
            BoundaryEdgeClass.ConcaveExterior => new Color(65, 120, 220),
            BoundaryEdgeClass.FlatContinuation => new Color(90, 90, 90),
            BoundaryEdgeClass.InvalidDiagonal => new Color(255, 0, 255),
            _ => fallback,
        };
    }

    private static Color DebugColorForVertex(BoundaryVertex vertex, MegastationDebugColorMode mode, Color fallback)
    {
        if (mode != MegastationDebugColorMode.VertexComplexity)
            return fallback;
        return vertex.Classification switch
        {
            BoundaryVertexClass.SimpleConvexCorner => new Color(90, 180, 90),
            BoundaryVertexClass.StraightConvexContinuation => new Color(90, 150, 200),
            BoundaryVertexClass.SimpleConcaveCorner => new Color(80, 80, 180),
            BoundaryVertexClass.ComplexJunction => new Color(225, 130, 45),
            BoundaryVertexClass.NonManifold => new Color(255, 0, 255),
            _ => new Color(80, 80, 80),
        };
    }

    private static bool IsTopologyDebug(MegastationDebugColorMode mode)
        => mode is MegastationDebugColorMode.EdgeClassification
            or MegastationDebugColorMode.ChamferEligibility
            or MegastationDebugColorMode.VertexComplexity
            or MegastationDebugColorMode.RunValidation;

    private sealed record ChamferPlan(
        IReadOnlyDictionary<BoundaryEdgeKey, float> AcceptedEdges,
        IReadOnlyList<ChamferRun> AcceptedRuns,
        IReadOnlySet<GridVertexKey> CollapsedEndpoints,
        IReadOnlyList<ChamferRunDiagnostics> Diagnostics,
        int AcceptedRunCount,
        int SuppressedRunCount);

    private sealed record ChamferCandidate(
        BoundaryEdgeSegment Edge,
        ChamferRunKey Key,
        float Width);

    private sealed class ChamferRun(
        ChamferRunKey key,
        IReadOnlyList<BoundaryEdgeSegment> edges,
        IReadOnlyList<GridVertexKey> endpoints,
        float width,
        float physicalLength,
        float startTaperLength,
        float endTaperLength,
        float fullWidthCentreLength,
        bool accepted,
        string suppressedReason)
    {
        public ChamferRunKey Key { get; } = key;
        public IReadOnlyList<BoundaryEdgeSegment> Edges { get; } = edges;
        public IReadOnlyList<GridVertexKey> Endpoints { get; } = endpoints;
        public float Width { get; } = width;
        public float PhysicalLength { get; } = physicalLength;
        public float StartTaperLength { get; } = startTaperLength;
        public float EndTaperLength { get; } = endTaperLength;
        public float FullWidthCentreLength { get; } = fullWidthCentreLength;
        public bool Accepted { get; set; } = accepted;
        public string SuppressedReason { get; set; } = suppressedReason;
    }

    private readonly record struct ChamferRunKey(
        GridAxis Axis,
        int LineA,
        int LineB,
        GridDirection NormalA,
        GridDirection NormalB);
}
