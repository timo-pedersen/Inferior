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
            new DVec3(7.0, 8.0, 9.0));

        Assert.Contains("[ShipMarker]", log);
        Assert.Contains("Sim position:", log);
        Assert.Contains("Camera position:", log);
        Assert.Contains("Ship render position (snapshot source):", log);
        Assert.Contains("    X: 1.25", log);
        Assert.Contains("    Z: -6.125", log);
        Assert.Contains("    Y: 8", log);
    }
}
