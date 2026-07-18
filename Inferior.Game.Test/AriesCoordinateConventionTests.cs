using Inferior.Core.Math;
using Inferior.Game.Ships;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class AriesCoordinateConventionTests
{
    [Fact]
    public void AuthoredLandmarks_UseStarboardUpAndNegativeZForward()
    {
        HullDefinition hull = HullDefinitionLibrary.Get("type-1");
        SemanticHullGeometry geometry = hull.VisualGeometry!;
        var vertices = geometry.Vertices.ToDictionary(vertex => vertex.Id, vertex => vertex.Position);
        SemanticHullFace nose = geometry.Faces.Single(face => face.Id == "type-1.front.armoured-head.01");
        SemanticHullFace rear = geometry.Faces.Single(face => face.Id == "type-1.rear.cargo-door.01");

        double noseCentreZ = nose.VertexIds.Average(id => vertices[id].Z);
        double rearCentreZ = rear.VertexIds.Average(id => vertices[id].Z);
        Assert.True(noseCentreZ < rearCentreZ);
        Assert.True(geometry.Vertices.Max(vertex => vertex.Position.X) > 0.0);
        Assert.True(geometry.Vertices.Min(vertex => vertex.Position.X) < 0.0);
        Assert.True(geometry.Vertices.Max(vertex => vertex.Position.Y) > 0.0);
    }

    [Fact]
    public void SemanticMeshConstruction_PreservesAuthoredCoordinateExtents()
    {
        SemanticHullGeometry geometry = HullDefinitionLibrary.Get("type-1").VisualGeometry!;
        SemanticHullCpuMesh mesh = SemanticHullMeshBuilder.Build(geometry);
        Vector3[] renderedVertices = mesh.Parts
            .SelectMany(part => part.Vertices)
            .Select(vertex => vertex.Position)
            .ToArray();

        Assert.Equal((float)geometry.Vertices.Min(vertex => vertex.Position.X), renderedVertices.Min(vertex => vertex.X));
        Assert.Equal((float)geometry.Vertices.Max(vertex => vertex.Position.X), renderedVertices.Max(vertex => vertex.X));
        Assert.Equal((float)geometry.Vertices.Min(vertex => vertex.Position.Y), renderedVertices.Min(vertex => vertex.Y));
        Assert.Equal((float)geometry.Vertices.Max(vertex => vertex.Position.Y), renderedVertices.Max(vertex => vertex.Y));
        Assert.Equal((float)geometry.Vertices.Min(vertex => vertex.Position.Z), renderedVertices.Min(vertex => vertex.Z));
        Assert.Equal((float)geometry.Vertices.Max(vertex => vertex.Position.Z), renderedVertices.Max(vertex => vertex.Z));
    }

    [Fact]
    public void SemanticWorldMatrix_DoesNotSwapOrReverseHullLocalAxes()
    {
        Matrix world = ShipMeshRenderer.BuildSemanticWorldTransform(
            renderScale: 1.0f,
            cameraRelativeRenderPosition: Vector3.Zero,
            shipOrientation: Quaternion.Identity);

        Assert.Equal(Vector3.UnitX, Vector3.TransformNormal(Vector3.UnitX, world));
        Assert.Equal(Vector3.UnitY, Vector3.TransformNormal(Vector3.UnitY, world));
        Assert.Equal(-Vector3.UnitZ, Vector3.TransformNormal(-Vector3.UnitZ, world));
    }

    [Fact]
    public void DebugAxes_ContainLabelledStarboardUpAndForwardArrows()
    {
        VertexPositionColor[] lines = ShipMeshRenderer.BuildSemanticDebugLines(
            new Vector3(-3.5f, -2.5f, -8.0f),
            new Vector3(3.5f, 2.5f, 8.1f));

        AssertContainsLine(lines, Vector3.Zero, Vector3.UnitX * 4.5f, Color.Red);
        AssertContainsLine(lines, Vector3.Zero, Vector3.UnitY * 4.5f, Color.LimeGreen);
        AssertContainsLine(lines, Vector3.Zero, -Vector3.UnitZ * 4.5f, Color.Cyan);
        Assert.Contains(lines, vertex => vertex.Color == Color.Red && vertex.Position.X > 4.8f);
        Assert.Contains(lines, vertex => vertex.Color == Color.LimeGreen && vertex.Position.Y > 4.8f);
        Assert.Contains(lines, vertex => vertex.Color == Color.Cyan && vertex.Position.Z < -4.7f);
    }

    [Fact]
    public void EngineMountsAndChildren_PreserveHullLocalSidesAndAftExhaust()
    {
        var ship = ShipBuilder.NewShip("type-1").Build();
        EngineMount port = ship.EngineMounts.Single(mount => mount.Side == EngineMountSide.Port);
        EngineMount starboard = ship.EngineMounts.Single(mount => mount.Side == EngineMountSide.Starboard);

        Assert.Equal(new DVec3(-5.00, 0.45, 2.75), port.Pose.Position);
        Assert.Equal(new DVec3(5.00, 0.45, 2.75), starboard.Pose.Position);
        Assert.Equal(-DVec3.UnitX, port.Pose.OutwardNormal);
        Assert.Equal(DVec3.UnitX, starboard.Pose.OutwardNormal);
        Assert.Equal(DVec3.UnitY, port.Pose.Up);
        Assert.Equal(DVec3.UnitY, starboard.Pose.Up);
        AssertDirection(port.Pose.OutwardNormal, Vector3.Transform(Vector3.UnitX, port.Pose.Orientation));
        AssertDirection(starboard.Pose.OutwardNormal, Vector3.Transform(Vector3.UnitX, starboard.Pose.Orientation));

        foreach (EngineMount mount in ship.EngineMounts)
        {
            EngineInstance engine = Assert.IsType<EngineInstance>(mount.InstalledEngine);
            EngineGeometryTransform transform = engine.GeometryTransform!;
            Assert.Equal(mount.Pose.Position, transform.Position);
            Assert.Equal(Quaternion.Identity, transform.Orientation);

            EngineExhaustDefinition exhaust = Assert.Single(engine.Variant.Engine.VisualGeometry!.Exhausts);
            Vector3 authoredDirection = EngineMeshBuilder.ToVector3(
                exhaust.Direction,
                transform.MirroredAcrossHullX);
            Vector3 hullDirection = Vector3.Normalize(
                Vector3.TransformNormal(authoredDirection, transform.LocalToHull));
            Assert.True(hullDirection.Z > 0.99f);
            Assert.True(MathF.Abs(hullDirection.X) < 0.01f);
            Assert.True(MathF.Abs(hullDirection.Y) < 0.01f);
        }
    }

    private static void AssertDirection(DVec3 expected, Vector3 actual)
    {
        Assert.Equal((float)expected.X, actual.X, 5);
        Assert.Equal((float)expected.Y, actual.Y, 5);
        Assert.Equal((float)expected.Z, actual.Z, 5);
    }

    private static void AssertContainsLine(
        IReadOnlyList<VertexPositionColor> vertices,
        Vector3 start,
        Vector3 end,
        Color colour)
    {
        for (int i = 0; i < vertices.Count; i += 2)
        {
            if (vertices[i].Position == start
                && vertices[i + 1].Position == end
                && vertices[i].Color == colour
                && vertices[i + 1].Color == colour)
            {
                return;
            }
        }

        Assert.Fail($"Expected debug line from {start} to {end} with colour {colour}.");
    }
}
