using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class PlanarTextGeometryTests
{
    public static TheoryData<Vector3, Vector3> FloorRotations => new()
    {
        { Vector3.UnitY, Vector3.UnitX },
        { Vector3.UnitY, Vector3.UnitZ },
        { Vector3.UnitY, -Vector3.UnitX },
        { Vector3.UnitY, -Vector3.UnitZ },
    };

    public static TheoryData<Vector3, Vector3> WallOrientations => new()
    {
        { Vector3.UnitX, -Vector3.UnitZ },
        { -Vector3.UnitX, Vector3.UnitZ },
        { Vector3.UnitZ, Vector3.UnitX },
        { -Vector3.UnitZ, -Vector3.UnitX },
    };

    [Theory]
    [MemberData(nameof(FloorRotations))]
    public void FloorTextPreservesGlyphOrderAndProperFrame(
        Vector3 normal,
        Vector3 readingDirection)
        => AssertTextFrame("ABC123", normal, readingDirection);

    [Theory]
    [MemberData(nameof(WallOrientations))]
    public void WallTextPreservesGlyphOrderAndProperFrame(
        Vector3 normal,
        Vector3 readingDirection)
        => AssertTextFrame("LOADING 02", normal, readingDirection);

    [Fact]
    public void DeriveFrameNormalizesAndRemovesNormalComponentWithoutReflection()
    {
        var (right, up, normal) = PlanarTextGeometry.DeriveFrame(
            Vector3.UnitY * 4f,
            Vector3.UnitX * 3f + Vector3.UnitY * 2f);

        AssertVector(Vector3.UnitX, right);
        AssertVector(-Vector3.UnitZ, up);
        AssertVector(Vector3.UnitY, normal);
        Assert.True(Vector3.Dot(Vector3.Cross(right, up), normal) > .9999f);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f, 0f, 2f, 0f)]
    public void InvalidFrameIsRejected(
        float nx, float ny, float nz,
        float rx, float ry, float rz)
    {
        Assert.Throws<ArgumentException>(() => PlanarTextGeometry.DeriveFrame(
            new Vector3(nx, ny, nz),
            new Vector3(rx, ry, rz)));
    }

    [Fact]
    public void InvalidGeometryArgumentsAreRejected()
    {
        var mesh = new StationModuleMesh();
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanarTextGeometry.Add(
            mesh, "R7", Vector3.Zero, Vector3.UnitY, Vector3.UnitX, 0f, Color.White));
        Assert.Throws<ArgumentException>(() => PlanarTextGeometry.Add(
            mesh, "R7", new Vector3(float.NaN, 0f, 0f),
            Vector3.UnitY, Vector3.UnitX, 1f, Color.White));
    }

    private static void AssertTextFrame(
        string text,
        Vector3 requestedNormal,
        Vector3 requestedReadingDirection)
    {
        const float pixelSize = .25f;
        Vector3 origin = new(17f, -11f, 29f);
        var mesh = new StationModuleMesh();
        PlanarTextGeometry.Add(mesh, text, origin,
            requestedNormal, requestedReadingDirection, pixelSize, Color.White);
        var (vertices, indices) = mesh.ToArrays();
        var (right, up, normal) = PlanarTextGeometry.DeriveFrame(
            requestedNormal, requestedReadingDirection);

        List<(int X, int Y)> expected = ExpectedPixels(text);
        Assert.Equal(expected.Count * 4, vertices.Length);
        Assert.Equal(expected.Count * 6, indices.Length);

        for (int pixel = 0; pixel < expected.Count; pixel++)
        {
            int vertexBase = pixel * 4;
            Vector3 centre = (vertices[vertexBase].Position
                + vertices[vertexBase + 1].Position
                + vertices[vertexBase + 2].Position
                + vertices[vertexBase + 3].Position) * .25f;
            Vector3 offset = centre - origin;
            float actualX = Vector3.Dot(offset, right) / pixelSize - .5f;
            float actualY = BitmapFonts.CharH - .5f - Vector3.Dot(offset, up) / pixelSize;

            Assert.InRange(MathF.Abs(actualX - expected[pixel].X), 0f, .001f);
            Assert.InRange(MathF.Abs(actualY - expected[pixel].Y), 0f, .001f);
            for (int vertex = 0; vertex < 4; vertex++)
            {
                Assert.True(IsFinite(vertices[vertexBase + vertex].Position));
                AssertVector(normal, vertices[vertexBase + vertex].Normal);
            }

            Vector3 a = vertices[indices[pixel * 6]].Position;
            Vector3 b = vertices[indices[pixel * 6 + 1]].Position;
            Vector3 c = vertices[indices[pixel * 6 + 2]].Position;
            Vector3 triangleNormal = Vector3.Cross(b - a, c - a);
            Assert.True(triangleNormal.LengthSquared() > 1e-12f);
            // StationModuleMesh uses clockwise front faces, so indexed triangle geometry
            // points opposite the visible/vertex normal when inspected by a right-hand cross.
            Assert.True(Vector3.Dot(triangleNormal, normal) < 0f);
        }
    }

    private static List<(int X, int Y)> ExpectedPixels(string text)
    {
        var expected = new List<(int X, int Y)>();
        int cursor = 0;
        foreach (char character in text.ToUpperInvariant())
        {
            if (BitmapFonts.HasGlyph(character))
            {
                for (int row = 0; row < BitmapFonts.CharH; row++)
                for (int column = 0; column < BitmapFonts.CharW; column++)
                    if (BitmapFonts.IsLit(character, column, row))
                        expected.Add((cursor + column, row));
            }
            cursor += BitmapFonts.CharW + 1;
        }
        return expected;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, .0001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, .0001f);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, .0001f);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
