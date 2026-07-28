using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationMeshStats(
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
    int BevelQuadCount,
    int CornerCapCount,
    long TopologyBuildMilliseconds,
    long MeshBuildMilliseconds,
    BoundaryMeshValidationReport SharpValidation,
    BoundaryMeshValidationReport ChamferedValidation,
    BoundaryTopologySignature TopologySignature);

public enum MegastationDebugColorMode
{
    StructuralVsUrban,
    RegionOwner,
    OutwardNormal,
    EdgeClassification,
    ChamferEligibility,
    VertexComplexity,
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

        var sharpMesh = new StationModuleMesh();
        AddSharpStructuralFaces(topology, occupancy, sharpMesh, debugColorMode);
        sharpMesh.ApplyIlluminationFlags();
        BoundaryMeshValidationReport sharpValidation = BoundaryMeshValidator.Validate(sharpMesh);
        if (requireValidStructuralBoundary && !sharpValidation.IsValid)
            throw new InvalidOperationException($"Sharp megastation boundary mesh is invalid: {sharpValidation.Summary}.");

        stopwatch.Restart();
        bool attemptChamfer = topology.Stats.FlatContinuationCount == 0
            && topology.Stats.EligibleChamferSegmentCount > 0;
        int faceQuads;
        int bevelQuads;
        int cornerCaps;
        BoundaryMeshValidationReport chamferedValidation;
        if (attemptChamfer)
        {
            AddStructuralFaces(topology, occupancy, mesh, debugColorMode);
            bevelQuads = AddBevels(topology, occupancy.Grid, mesh, debugColorMode);
            cornerCaps = AddCornerCaps(topology, occupancy.Grid, mesh, debugColorMode);
            mesh.ApplyIlluminationFlags();
            faceQuads = topology.Faces.Count;
            chamferedValidation = BoundaryMeshValidator.Validate(mesh);
            if (!chamferedValidation.IsValid)
                throw new InvalidOperationException($"Chamfered megastation boundary mesh is invalid: {chamferedValidation.Summary}.");
        }
        else
        {
            AddSharpStructuralFaces(topology, occupancy, mesh, debugColorMode);
            mesh.ApplyIlluminationFlags();
            faceQuads = topology.Faces.Count;
            bevelQuads = 0;
            cornerCaps = 0;
            chamferedValidation = BoundaryMeshValidator.Validate(mesh);
            if (requireValidStructuralBoundary && !chamferedValidation.IsValid)
                throw new InvalidOperationException($"Sharp fallback megastation boundary mesh is invalid: {chamferedValidation.Summary}.");
        }
        stopwatch.Stop();

        var (_, indices) = mesh.ToIntArrays();
        return new MegastationMeshStats(
            faceQuads,
            indices.Length / 3,
            chamferedValidation.VertexCount,
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
            topology.Stats.EligibleChamferSegmentCount,
            topology.Stats.SuppressedConvexSegmentCount,
            bevelQuads,
            cornerCaps,
            topologyMs,
            stopwatch.ElapsedMilliseconds,
            sharpValidation,
            chamferedValidation,
            BoundaryTopologySignatureBuilder.Compute(topology, settings));
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
        MegastationDebugColorMode debugColorMode)
    {
        foreach (var face in topology.Faces)
        {
            var p = new Vector3[4];
            for (int i = 0; i < 4; i++)
                p[i] = RetractedFaceVertex(topology, occupancy.Grid, face, face.Vertices[i]);

            Color color = ColorFor(topology, occupancy, face, debugColorMode);
            AddQuad(mesh, p[0], p[1], p[2], p[3], BoundaryTopologyBuilder.Normal(face.Direction), color);
        }
        return topology.Faces.Count;
    }

    private static Vector3 Retraction(BoundaryTopology topology, BoundaryFace face, BoundaryEdgeKey edgeKey)
    {
        var edge = topology.EdgeByKey[edgeKey];
        if (edge.ChamferEligibility != ChamferEligibility.Eligible) return Vector3.Zero;
        foreach (BoundaryFaceKey incident in edge.IncidentFaces)
        {
            if (incident == face.Key) continue;
            return -BoundaryTopologyBuilder.Normal(incident.Direction) * edge.ChamferWidth;
        }
        return Vector3.Zero;
    }

    private static Vector3 RetractedFaceVertex(
        BoundaryTopology topology,
        SliceGrid grid,
        BoundaryFace face,
        GridVertexKey vertex)
    {
        int index = Array.IndexOf(face.Vertices, vertex);
        if (index < 0)
            throw new ArgumentException("Vertex is not part of the face.", nameof(vertex));

        BoundaryEdgeKey before = face.Edges[(index + 3) % 4];
        BoundaryEdgeKey after = face.Edges[index];
        return BoundaryTopologyBuilder.Position(grid, vertex)
            + Retraction(topology, face, before)
            + Retraction(topology, face, after);
    }

    private static int AddBevels(
        BoundaryTopology topology,
        SliceGrid grid,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode)
    {
        int count = 0;
        foreach (var edge in topology.EdgeSegments.Where(e => e.ChamferEligibility == ChamferEligibility.Eligible))
        {
            BoundaryFaceKey aKey = edge.IncidentFaces[0];
            BoundaryFaceKey bKey = edge.IncidentFaces[1];
            BoundaryFace aFace = topology.FaceByKey[aKey];
            BoundaryFace bFace = topology.FaceByKey[bKey];
            Vector3 normal = Vector3.Normalize(BoundaryTopologyBuilder.Normal(aKey.Direction) + BoundaryTopologyBuilder.Normal(bKey.Direction));
            Vector3 a0 = RetractedFaceVertex(topology, grid, aFace, edge.StartVertex);
            Vector3 a1 = RetractedFaceVertex(topology, grid, aFace, edge.EndVertex);
            Vector3 b1 = RetractedFaceVertex(topology, grid, bFace, edge.EndVertex);
            Vector3 b0 = RetractedFaceVertex(topology, grid, bFace, edge.StartVertex);
            AddQuad(mesh, a0, a1, b1, b0, normal, DebugColorForEdge(edge, debugColorMode, StructuralColor));
            count++;
        }
        return count;
    }

    private static int AddCornerCaps(
        BoundaryTopology topology,
        SliceGrid grid,
        StationModuleMesh mesh,
        MegastationDebugColorMode debugColorMode)
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
                .Where(e => e.ChamferEligibility == ChamferEligibility.Eligible)
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
                    offset -= normals[j] * edge.ChamferWidth;
                }
                points[i] = p + offset;
            }
            AddTriangle(mesh, points[0], points[1], points[2], expected, DebugColorForVertex(vertex, debugColorMode, CornerColor));
            count++;
        }
        return count;
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
        if (mode == MegastationDebugColorMode.ChamferEligibility)
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
}
