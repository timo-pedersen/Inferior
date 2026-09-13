using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

/// <summary>
/// Geometry tests for the projected-surface-marking clipping/tessellation engine
/// (Inferior.Rendering/SurfaceMarkingProjector.cs, SurfaceMarkingGeometry.cs). Small synthetic
/// fixtures rather than production-scale container generation, per testing-ai.md.
///
/// Fixture convention: every receiver triangle below is built so that, read in its own index
/// order (v0, v1, v2), cross(v1 - v0, v2 - v0) == -normal — the same relationship
/// StationModuleMesh.AddTriangle/GeometryBuilder.BuildDynamic already establish everywhere else
/// in this codebase (see their "swap 1<->2" index-emission comments) — so these fixtures are
/// representative of real container/station output, not an arbitrary convention invented here.
/// </summary>
public sealed class SurfaceMarkingGeometryTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    // A flat 8x4 quad on the z=0 plane (x in [0,8], y in [0,4]), outward normal +Z.
    private static (VertexPositionNormalColorTexture[] verts, short[] indices) FlatQuad()
    {
        Vector3 normal = new(0, 0, 1);
        Vector3 p00 = new(0, 0, 0), p10 = new(8, 0, 0), p01 = new(0, 4, 0), p11 = new(8, 4, 0);
        var verts = new[]
        {
            V(p00, normal), V(p10, normal), V(p01, normal), V(p11, normal),
        };
        // (p00,p01,p10) and (p10,p01,p11): cross(v1-v0,v2-v0) = -normal for both, matching the
        // documented fixture convention.
        short[] indices = [0, 2, 1,  1, 2, 3];
        return (verts, indices);
    }

    // The flat quad above, plus a perpendicular wall dropping from its x=8 edge down to z=-2
    // (outward normal +X) — an inset-wall analogue, sharing the y in [0,4] edge with the quad.
    private static (VertexPositionNormalColorTexture[] verts, short[] indices) FlatQuadWithPerpendicularWall()
    {
        (var flatVerts, var flatIndices) = FlatQuad();

        Vector3 wallNormal = new(1, 0, 0);
        Vector3 q00 = new(8, 0, 0), q01 = new(8, 4, 0), q0b = new(8, 0, -2), q1b = new(8, 4, -2);
        var wallVerts = new[]
        {
            V(q00, wallNormal), V(q01, wallNormal), V(q0b, wallNormal), V(q1b, wallNormal),
        };
        // (q00,q01,q0b) and (q01,q1b,q0b): cross(v1-v0,v2-v0) = -wallNormal for both.
        short baseIndex = (short)flatVerts.Length;
        short[] wallIndices =
        [
            (short)(baseIndex + 0), (short)(baseIndex + 1), (short)(baseIndex + 2),
            (short)(baseIndex + 1), (short)(baseIndex + 3), (short)(baseIndex + 2),
        ];

        var verts = new VertexPositionNormalColorTexture[flatVerts.Length + wallVerts.Length];
        flatVerts.CopyTo(verts, 0);
        wallVerts.CopyTo(verts, flatVerts.Length);
        var indices = new short[flatIndices.Length + wallIndices.Length];
        flatIndices.CopyTo(indices, 0);
        wallIndices.CopyTo(indices, flatIndices.Length);
        return (verts, indices);
    }

    // Three separate 2x4 quads side by side on z=0 (x in [0,2],[2,4],[4,6], y in [0,4]) —
    // six independent triangles, none sharing vertices, so a projector spanning all three
    // must pull geometry from more than one source triangle.
    private static (VertexPositionNormalColorTexture[] verts, short[] indices) ThreeSeparateQuads()
    {
        Vector3 normal = new(0, 0, 1);
        var verts = new List<VertexPositionNormalColorTexture>();
        var indices = new List<short>();
        for (int i = 0; i < 3; i++)
        {
            float x0 = i * 2f, x1 = x0 + 2f;
            short b = (short)verts.Count;
            verts.Add(V(new Vector3(x0, 0, 0), normal));
            verts.Add(V(new Vector3(x1, 0, 0), normal));
            verts.Add(V(new Vector3(x0, 4, 0), normal));
            verts.Add(V(new Vector3(x1, 4, 0), normal));
            indices.AddRange([b, (short)(b + 2), (short)(b + 1), (short)(b + 1), (short)(b + 2), (short)(b + 3)]);
        }
        return (verts.ToArray(), indices.ToArray());
    }

    private static VertexPositionNormalColorTexture V(Vector3 position, Vector3 normal)
        => new(position, normal, Color.White, Vector2.Zero);

    // A projector on the flat quad's own plane: origin at its corner, forward into -Z (the
    // shared convention for "into the object" for these fixtures), up +Y, right +X (verified
    // in the implementation notes: Create(forward=(0,0,-1), upReference=(0,1,0)) derives
    // Right = (1,0,0)).
    private static SurfaceMarkingProjector MakeProjector(float width, float height, float depth)
        => SurfaceMarkingProjector.Create(
            origin: Vector3.Zero,
            forward: new Vector3(0, 0, -1),
            upReference: new Vector3(0, 1, 0),
            width, height, depth,
            atlasRegion: new Vector4(0, 0, 1, 1),
            tint: Color.White);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CreatedProjectorHasTheExpectedOrthonormalFrame()
    {
        SurfaceMarkingProjector projector = MakeProjector(10, 4, 3);

        Assert.Equal(new Vector3(1, 0, 0), projector.Right);
        Assert.Equal(new Vector3(0, 1, 0), projector.Up);
        Assert.Equal(new Vector3(0, 0, -1), projector.Forward);
    }

    [Fact]
    public void GeneratedVerticesAreFinite()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var (outVerts, _) = SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, indices);

        Assert.NotEmpty(outVerts);
        foreach (var v in outVerts)
        {
            Assert.True(IsFinite(v.Position), $"Non-finite position {v.Position}");
            Assert.True(IsFinite(v.Normal), $"Non-finite normal {v.Normal}");
            Assert.True(float.IsFinite(v.TextureCoordinate.X) && float.IsFinite(v.TextureCoordinate.Y),
                $"Non-finite UV {v.TextureCoordinate}");
        }
    }

    [Fact]
    public void GeneratedTrianglesHaveNonZeroArea()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, indices);

        Assert.True(outIndices.Length % 3 == 0);
        for (int i = 0; i < outIndices.Length; i += 3)
        {
            Vector3 a = outVerts[outIndices[i]].Position;
            Vector3 b = outVerts[outIndices[i + 1]].Position;
            Vector3 c = outVerts[outIndices[i + 2]].Position;
            float area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            Assert.True(area > 1e-6f, $"Degenerate triangle, area={area}");
        }
    }

    [Fact]
    public void UvCoordinatesStayWithinTheAtlasRegion()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var projector = MakeProjector(10, 4, 3) with { AtlasRegion = new Vector4(0.25f, 0.1f, 0.75f, 0.9f) };
        var (outVerts, _) = SurfaceMarkingGeometry.Project(projector, verts, indices);

        Assert.NotEmpty(outVerts);
        foreach (var v in outVerts)
        {
            Assert.InRange(v.TextureCoordinate.X, 0.25f - 1e-4f, 0.75f + 1e-4f);
            Assert.InRange(v.TextureCoordinate.Y, 0.1f - 1e-4f, 0.9f + 1e-4f);
        }
    }

    [Fact]
    public void ProjectorUpMapsToTheLargerAtlasVNotTheSmaller()
    {
        // Pins the atlas orientation convention explicitly (see the comment in
        // SurfaceMarkingGeometry.MakeVertex): the source atlas this system was built for
        // (TextMarkingAtlas, backed by TextPainter.DrawText) turned out to already store its
        // content with glyph-row-0 (the visual top) toward the LARGEST buffer row, i.e.
        // already vertically flipped relative to plain top-left-origin convention — so
        // projector Up maps to the LARGER atlasV, not the smaller. Getting this backwards
        // renders every marking upside down without necessarily being otherwise detectable.
        (var verts, var indices) = FlatQuad();
        var projector = MakeProjector(8, 4, 1) with { AtlasRegion = new Vector4(0f, 0.2f, 1f, 0.8f) };
        var (outVerts, _) = SurfaceMarkingGeometry.Project(projector, verts, indices);

        Assert.NotEmpty(outVerts);
        float atlasVAtBottom = outVerts.Where(v => v.Position.Y < 0.01f).Min(v => v.TextureCoordinate.Y);
        float atlasVAtTop    = outVerts.Where(v => v.Position.Y > 3.99f).Max(v => v.TextureCoordinate.Y);

        Assert.Equal(0.2f, atlasVAtBottom, 3);
        Assert.Equal(0.8f, atlasVAtTop, 3);
    }

    [Fact]
    public void ProjectorCrossingMultipleReceiverTrianglesProducesGeometryOnEachOne()
    {
        (var verts, var indices) = ThreeSeparateQuads();
        // Wide enough to span all three quads (x in [0,6]).
        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(MakeProjector(6, 4, 1), verts, indices);

        Assert.NotEmpty(outIndices);
        float minX = outIndices.Select(i => outVerts[i].Position.X).Min();
        float maxX = outIndices.Select(i => outVerts[i].Position.X).Max();
        Assert.True(minX < 0.5f, $"Expected coverage starting near x=0, got minX={minX}");
        Assert.True(maxX > 5.5f, $"Expected coverage extending near x=6, got maxX={maxX}");
    }

    [Fact]
    public void ClippingNeverEmitsGeometryOutsideTheProjectorVolume()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var projector = MakeProjector(width: 5f, height: 2f, depth: 1f);
        var (outVerts, _) = SurfaceMarkingGeometry.Project(projector, verts, indices);

        Assert.NotEmpty(outVerts);
        const float epsilon = 1e-4f;
        foreach (var v in outVerts)
        {
            Vector3 offset = v.Position - projector.Origin;
            float u = Vector3.Dot(offset, projector.Right);
            float vv = Vector3.Dot(offset, projector.Up);
            float w = Vector3.Dot(offset, projector.Forward);
            Assert.InRange(u, -epsilon, projector.Width + epsilon);
            Assert.InRange(vv, -epsilon, projector.Height + epsilon);
            Assert.InRange(w, -epsilon, projector.Depth + epsilon);
        }
    }

    [Fact]
    public void PerpendicularWallReceivesGeometryDespiteNoIncidenceAngleCutoff()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        // Isolate just the wall's two triangles (indices 6..11 in the combined buffer).
        short[] wallOnlyIndices = indices[6..];
        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, wallOnlyIndices);

        Assert.NotEmpty(outIndices);
        // The wall sits at x=8, perpendicular to the primary plane (whose normal the projector
        // is built around) — its w-coordinate (dot against Forward=(0,0,-1)) varies with the
        // wall's own z while its u-coordinate (x) stays essentially constant at 8, exactly the
        // "projected coordinate becomes constant in one axis" case the design calls for.
        foreach (short i in outIndices)
            Assert.Equal(8f, outVerts[i].Position.X, 3);
    }

    [Fact]
    public void ReceiverNormalsArePreservedPerTriangle()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, indices);

        foreach (short i in outIndices)
        {
            Vector3 n = outVerts[i].Normal;
            bool matchesFlatFace = Vector3.Distance(n, new Vector3(0, 0, 1)) < 1e-4f;
            bool matchesWall     = Vector3.Distance(n, new Vector3(1, 0, 0)) < 1e-4f;
            Assert.True(matchesFlatFace || matchesWall, $"Unexpected normal {n}");
        }
    }

    [Fact]
    public void WindingMatchesTheProjectsConvention()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, indices);

        Assert.NotEmpty(outIndices);
        for (int i = 0; i < outIndices.Length; i += 3)
        {
            Vector3 a = outVerts[outIndices[i]].Position;
            Vector3 b = outVerts[outIndices[i + 1]].Position;
            Vector3 c = outVerts[outIndices[i + 2]].Position;
            Vector3 normal = outVerts[outIndices[i]].Normal;
            Vector3 cross = Vector3.Cross(b - a, c - a);

            // Same fixture convention documented at the top of this file: cross(v1-v0,v2-v0)
            // == -normal for a correctly-wound triangle in this project.
            Assert.True(Vector3.Dot(cross, -normal) > 0,
                $"Triangle at output index {i} winds opposite to the project's convention " +
                $"(cross={cross}, normal={normal})");
        }
    }

    [Fact]
    public void SameInputProducesDeterministicOutput()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var projector = MakeProjector(10, 4, 3);

        var (verts1, indices1) = SurfaceMarkingGeometry.Project(projector, verts, indices);
        var (verts2, indices2) = SurfaceMarkingGeometry.Project(projector, verts, indices);

        Assert.Equal(verts1.Length, verts2.Length);
        for (int i = 0; i < verts1.Length; i++)
        {
            Assert.Equal(verts1[i].Position, verts2[i].Position);
            Assert.Equal(verts1[i].Normal, verts2[i].Normal);
            Assert.Equal(verts1[i].TextureCoordinate, verts2[i].TextureCoordinate);
        }
        Assert.Equal(indices1, indices2);
    }

    [Fact]
    public void ReceiverGeometryItselfIsUnchanged()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        var originalVerts = (VertexPositionNormalColorTexture[])verts.Clone();
        var originalIndices = (short[])indices.Clone();

        SurfaceMarkingGeometry.Project(MakeProjector(10, 4, 3), verts, indices);

        Assert.Equal(originalIndices, indices);
        for (int i = 0; i < verts.Length; i++)
        {
            Assert.Equal(originalVerts[i].Position, verts[i].Position);
            Assert.Equal(originalVerts[i].Normal, verts[i].Normal);
            Assert.Equal(originalVerts[i].TextureCoordinate, verts[i].TextureCoordinate);
        }
    }

    [Fact]
    public void ReceiverTrianglePredicateExcludesTaggedTriangles()
    {
        (var verts, var indices) = FlatQuadWithPerpendicularWall();
        // Triangle indices 0,1 are the flat face; 2,3 are the wall (see fixture layout).
        var (_, onlyFlatIndices) = SurfaceMarkingGeometry.Project(
            MakeProjector(10, 4, 3), verts, indices, isReceiverTriangle: t => t < 2);
        var (outVerts, _) = SurfaceMarkingGeometry.Project(
            MakeProjector(10, 4, 3), verts, indices, isReceiverTriangle: t => t < 2);

        Assert.NotEmpty(onlyFlatIndices);
        foreach (var v in outVerts)
            Assert.Equal(0f, v.Position.Z, 3); // wall (z in [-2,0), nonzero) excluded
    }

    [Fact]
    public void OutOfRangeProjectorProducesNoGeometry()
    {
        (var verts, var indices) = FlatQuad();
        var farAwayProjector = SurfaceMarkingProjector.Create(
            origin: new Vector3(1000, 1000, 1000),
            forward: new Vector3(0, 0, -1),
            upReference: new Vector3(0, 1, 0),
            width: 1, height: 1, depth: 1,
            atlasRegion: new Vector4(0, 0, 1, 1),
            tint: Color.White);

        var (outVerts, outIndices) = SurfaceMarkingGeometry.Project(farAwayProjector, verts, indices);

        Assert.Empty(outVerts);
        Assert.Empty(outIndices);
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
