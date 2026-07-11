using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationModuleMeshNormalTests
{
    [Fact]
    public void QuadFaceUsesOneReceiverNormalAcrossBothTriangles()
    {
        var mesh = new StationModuleMesh();
        mesh.AddQuad(
            new Vector3(-10f, -10f, 0f),
            new Vector3( 10f, -10f, 0f),
            new Vector3( 10f,  10f, 0f),
            new Vector3(-10f,  10f, 0f),
            Color.White);

        var (verts, indices) = mesh.ToArraysWithNormals();

        Assert.Equal(4, verts.Length);
        Assert.Equal(6, indices.Length);
        Assert.All(verts, v => Assert.Equal(Vector3.UnitZ, v.Normal));

        Vector3 firstTriangleNormal = TriangleNormal(
            verts[indices[0]].Position,
            verts[indices[1]].Position,
            verts[indices[2]].Position);
        Vector3 secondTriangleNormal = TriangleNormal(
            verts[indices[3]].Position,
            verts[indices[4]].Position,
            verts[indices[5]].Position);

        Assert.Equal(firstTriangleNormal, secondTriangleNormal);
    }

    private static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        return Vector3.Normalize(normal);
    }
}
