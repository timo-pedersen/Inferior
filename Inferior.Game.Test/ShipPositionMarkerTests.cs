using Inferior.Core.Math;
using Inferior.Game.States;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ShipPositionMarkerTests
{
    [Fact]
    public void MarkerGeometry_ContainsWireCubeAndThreeWorldAxes()
    {
        var lines = SystemSpaceState.BuildShipPositionMarkerLines();

        Assert.Equal(30, lines.Length);
        Assert.Equal(24, lines.Count(vertex => vertex.Color == Color.Yellow));
        Assert.Contains(lines, vertex => vertex.Position == Vector3.UnitX * 40.0f && vertex.Color == Color.Red);
        Assert.Contains(lines, vertex => vertex.Position == Vector3.UnitY * 40.0f && vertex.Color == Color.LimeGreen);
        Assert.Contains(lines, vertex => vertex.Position == Vector3.UnitZ * 40.0f && vertex.Color == Color.Cyan);
        Assert.Equal(-12.0f, lines.Where(vertex => vertex.Color == Color.Yellow).Min(vertex => vertex.Position.X));
        Assert.Equal(12.0f, lines.Where(vertex => vertex.Color == Color.Yellow).Max(vertex => vertex.Position.X));
    }

    [Fact]
    public void PositionLog_ReportsSimulationCameraAndRenderSources()
    {
        string log = SystemSpaceState.FormatShipPositionMarkerLog(
            new DVec3(1.25, -2.5, 3.75),
            new DVec3(4.5, 5.25, -6.125),
            new DVec3(7.0, 8.0, 9.0),
            new DVec3(10.0, 11.0, 12.0),
            new SystemSpaceState.ChaseCameraTargets(
                new DVec3(13.0, 14.0, 15.0),
                new DVec3(16.0, 17.0, 18.0)));

        Assert.Contains("[ShipMarker]", log);
        Assert.Contains("Sim position:", log);
        Assert.Contains("Snapshot ship position:", log);
        Assert.Contains("Presentation ship position / render source:", log);
        Assert.Contains("Camera desired position:", log);
        Assert.Contains("Camera target:", log);
        Assert.Contains("Camera position:", log);
        Assert.Contains("    X: 1.25", log);
        Assert.Contains("    Z: 12", log);
        Assert.Contains("    Y: 8", log);
    }

    [Fact]
    public void ChaseTargets_UseSnapshotBasisAsMetreOffsets()
    {
        var shipPosition = new DVec3(1000, 2000, 3000);
        var targets = SystemSpaceState.CalculateChaseCameraTargets(
            shipPosition,
            new DVec3(0, 0, -1),
            DVec3.UnitY);

        Assert.Equal(new DVec3(1000, 2030, 3080), targets.DesiredPosition);
        Assert.Equal(new DVec3(1000, 2000, 2992), targets.LookTarget);
        Assert.Equal(Math.Sqrt(80 * 80 + 30 * 30), (targets.DesiredPosition - shipPosition).Length, 9);
    }

    [Fact]
    public void ChaseOrientation_PointsCameraAtCalculatedTarget()
    {
        var targets = SystemSpaceState.CalculateChaseCameraTargets(
            new DVec3(1000, 2000, 3000),
            new DVec3(0.6, 0.0, -0.8),
            DVec3.UnitY);
        DVec3 lookDirection = (targets.LookTarget - targets.DesiredPosition).Normalized();

        Quaternion orientation = SystemSpaceState.QuatLookAtWithUp(
            lookDirection,
            DVec3.UnitY);
        Vector3 cameraForward = Vector3.Normalize(
            Vector3.Transform(-Vector3.UnitZ, orientation));
        var expected = new Vector3(
            (float)lookDirection.X,
            (float)lookDirection.Y,
            (float)lookDirection.Z);

        Assert.True(Vector3.Dot(cameraForward, expected) > 0.9999f);
    }
}
