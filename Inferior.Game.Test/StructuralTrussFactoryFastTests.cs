using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StructuralTrussFactoryFastTests
{
    [Theory]
    [InlineData(17f)]
    [InlineData(43f)]
    [InlineData(137f)]
    [InlineData(286f)]
    public void FactoryAcceptsArbitraryLengthAndFillsRequestedEndpointPlanes(float length)
    {
        StructuralTrussSpec spec = Spec(StructuralTrussCrossSection.Box,
            Matrix.Identity) with { Length = length };
        var mesh = new StationModuleMesh();

        StructuralTrussDiagnostics diagnostics = StructuralTrussFactory.Append(
            mesh, spec, Color.Gray);
        var (vertices, _) = mesh.ToIntArrays();

        Assert.Equal(-length * .5f, diagnostics.StartPlane);
        Assert.Equal(length * .5f, diagnostics.EndPlane);
        Assert.Equal(-length * .5f, vertices.Min(vertex => vertex.Position.Z), 4);
        Assert.Equal(length * .5f, vertices.Max(vertex => vertex.Position.Z), 4);
    }

    [Theory]
    [InlineData(StructuralTrussCrossSection.Box, 4)]
    [InlineData(StructuralTrussCrossSection.Triangular, 3)]
    public void FactoryHasExactEndpointsUniformBaysAndCompleteFrames(
        StructuralTrussCrossSection crossSection,
        int expectedChords)
    {
        StructuralTrussSpec spec = Spec(crossSection, Matrix.Identity);
        var mesh = new StationModuleMesh();

        StructuralTrussDiagnostics diagnostics = StructuralTrussFactory.Append(
            mesh, spec, Color.Gray);

        Assert.Equal(14, diagnostics.BayCount);
        Assert.Equal(-68.5f, diagnostics.StartPlane);
        Assert.Equal(68.5f, diagnostics.EndPlane);
        Assert.Equal((spec.Length - spec.BraceThickness) / diagnostics.BayCount,
            diagnostics.ActualBayLength, 4);
        Assert.Equal(expectedChords, diagnostics.ChordCount);
        Assert.Equal((diagnostics.BayCount + 1) * expectedChords,
            diagnostics.TransverseMemberCount);
        Assert.Equal(diagnostics.BayCount * expectedChords, diagnostics.BraceCount);
        Assert.Equal(diagnostics.MemberCount * 24, diagnostics.AddedVertexCount);
        Assert.Equal(diagnostics.MemberCount * 12, diagnostics.AddedTriangleCount);
    }

    [Fact]
    public void FactoryIsDeterministicAndHonoursRotatedTranslatedEnvelope()
    {
        Matrix transform = Matrix.CreateRotationY(.73f)
            * Matrix.CreateRotationX(-.31f)
            * Matrix.CreateTranslation(21f, -17f, 43f);
        StructuralTrussSpec spec = Spec(StructuralTrussCrossSection.Box, transform);
        var first = new StationModuleMesh();
        var second = new StationModuleMesh();

        StructuralTrussFactory.Append(first, spec, new Color(90, 110, 120));
        StructuralTrussFactory.Append(second, spec, new Color(90, 110, 120));
        var a = first.ToIntArrays();
        var b = second.ToIntArrays();

        Assert.Equal(a.verts, b.verts);
        Assert.Equal(a.indices, b.indices);
        Matrix inverse = Matrix.Invert(transform);
        Vector3[] local = a.verts.Select(vertex =>
            Vector3.Transform(vertex.Position, inverse)).ToArray();
        Assert.InRange(local.Min(point => point.X), -spec.Width * .5f - .001f,
            -spec.Width * .5f + .001f);
        Assert.InRange(local.Max(point => point.X), spec.Width * .5f - .001f,
            spec.Width * .5f + .001f);
        Assert.InRange(local.Min(point => point.Y), -spec.Height * .5f - .001f,
            -spec.Height * .5f + .001f);
        Assert.InRange(local.Max(point => point.Y), spec.Height * .5f - .001f,
            spec.Height * .5f + .001f);
        Assert.InRange(local.Min(point => point.Z), -spec.Length * .5f - .001f,
            -spec.Length * .5f + .001f);
        Assert.InRange(local.Max(point => point.Z), spec.Length * .5f - .001f,
            spec.Length * .5f + .001f);
    }

    [Theory]
    [InlineData(StructuralTrussCrossSection.Box)]
    [InlineData(StructuralTrussCrossSection.Triangular)]
    public void FactoryProducesFiniteNonDegenerateConsistentlyWoundGeometry(
        StructuralTrussCrossSection crossSection)
    {
        var mesh = new StationModuleMesh();
        StructuralTrussFactory.Append(mesh, Spec(crossSection, Matrix.Identity), Color.Gray);
        var (vertices, indices) = mesh.ToIntArrays();

        Assert.NotEmpty(vertices);
        Assert.Equal(0, indices.Length % 3);
        Assert.All(vertices, vertex =>
        {
            Assert.True(Finite(vertex.Position));
            Assert.True(Finite(vertex.Normal));
            Assert.InRange(vertex.Normal.Length(), .999f, 1.001f);
        });
        Assert.All(indices, index => Assert.InRange(index, 0, vertices.Length - 1));
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]];
            var b = vertices[indices[i + 1]];
            var c = vertices[indices[i + 2]];
            Vector3 geometric = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            Assert.True(geometric.LengthSquared() > 1e-8f);
            // StationModuleMesh stores DirectX-clockwise indices, opposite the
            // right-handed cross product used to author its outward normal.
            Assert.True(Vector3.Dot(geometric, a.Normal) < 0f);
        }
    }

    private static StructuralTrussSpec Spec(
        StructuralTrussCrossSection crossSection,
        Matrix transform) => new(
            Length: 137f,
            Width: 8f,
            Height: 6f,
            CrossSection: crossSection,
            ChordThickness: .8f,
            BraceThickness: .4f,
            TargetBayLength: 10f,
            Transform: transform);

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
