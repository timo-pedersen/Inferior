using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class SemanticHullMeshBuilderTests
{
    [Fact]
    public void ConvexNGon_TriangulatesToNMinusTwoTrianglesAndKeepsFaceRange()
    {
        var geometry = PentagonalFaceGeometry();

        var mesh = SemanticHullMeshBuilder.Build(geometry);
        var range = Assert.Single(mesh.FaceRanges);

        Assert.Equal(3, mesh.TriangleCount);
        Assert.Equal(9, mesh.Indices.Count);
        Assert.Equal("sample.top.panel.01", range.FaceId);
        Assert.Equal("structural", range.MaterialGroup);
        Assert.Equal(HullSurfaceRole.PanelSeat, range.SurfaceRole);
        Assert.Equal(0, range.StartIndex);
        Assert.Equal(9, range.IndexCount);
    }

    [Fact]
    public void EmittedTriangles_AreNonDegenerateAndPreserveSemanticNormal()
    {
        var mesh = SemanticHullMeshBuilder.Build(PentagonalFaceGeometry());

        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]].Position;
            var b = mesh.Vertices[mesh.Indices[i + 2]].Position;
            var c = mesh.Vertices[mesh.Indices[i + 1]].Position;
            var cross = Vector3.Cross(b - a, c - a);

            Assert.True(cross.LengthSquared() > 1e-8f);
            Assert.True(Vector3.Dot(Vector3.Normalize(cross), Vector3.UnitZ) > 0.999f);
        }
    }

    [Fact]
    public void FlatNormals_ArePerFaceNotSharedAcrossAdjacentFaces()
    {
        var geometry = new SemanticHullGeometry
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(1, 0, 0)),
                new("sample.v.03", new DVec3(1, 1, 0)),
                new("sample.v.04", new DVec3(0, 1, 0)),
                new("sample.v.05", new DVec3(0, 0, 1)),
                new("sample.v.06", new DVec3(1, 0, 1)),
            ],
            Faces =
            [
                new("sample.top.panel.01", ["sample.v.01", "sample.v.02", "sample.v.03", "sample.v.04"], HullSurfaceRole.PanelSeat, "structural", DVec3.UnitZ, "sample.top.panel.01"),
                new("sample.side.service.01", ["sample.v.01", "sample.v.02", "sample.v.06", "sample.v.05"], HullSurfaceRole.ServiceSurface, "service", -DVec3.UnitY),
            ],
        };

        var mesh = SemanticHullMeshBuilder.Build(geometry);
        var firstFaceNormal = mesh.Vertices[0].Normal;
        var secondFaceNormal = mesh.Vertices[6].Normal;

        Assert.Equal(Vector3.UnitZ, firstFaceNormal);
        Assert.Equal(-Vector3.UnitY, secondFaceNormal);
        Assert.Contains(mesh.Vertices, v => v.Position == Vector3.Zero && v.Normal == Vector3.UnitZ);
        Assert.Contains(mesh.Vertices, v => v.Position == Vector3.Zero && v.Normal == -Vector3.UnitY);
    }

    [Fact]
    public void Aries_EverySemanticFaceYieldsRecoverableRenderRange()
    {
        var geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;

        var mesh = SemanticHullMeshBuilder.Build(geometry);

        Assert.Equal(geometry.Faces.Count, mesh.FaceRanges.Count);
        Assert.Equal(
            geometry.Faces.Select(f => f.Id).Order(StringComparer.Ordinal),
            mesh.FaceRanges.Select(r => r.FaceId).Order(StringComparer.Ordinal));
        Assert.All(mesh.FaceRanges, range => Assert.True(range.IndexCount >= 3));
    }

    private static SemanticHullGeometry PentagonalFaceGeometry()
        => new()
        {
            Vertices =
            [
                new("sample.v.01", new DVec3(0, 0, 0)),
                new("sample.v.02", new DVec3(2, 0, 0)),
                new("sample.v.03", new DVec3(3, 1, 0)),
                new("sample.v.04", new DVec3(1, 2, 0)),
                new("sample.v.05", new DVec3(-1, 1, 0)),
            ],
            Faces =
            [
                new("sample.top.panel.01",
                    ["sample.v.01", "sample.v.02", "sample.v.03", "sample.v.04", "sample.v.05"],
                    HullSurfaceRole.PanelSeat,
                    "structural",
                    DVec3.UnitZ,
                    "sample.top.panel.01"),
            ],
        };
}
