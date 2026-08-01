using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Game;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class TargetingSystemTests
{
    [Fact]
    public void ContactUpdatesRefreshSelectedTargetDistance()
    {
        var targeting = new TargetingSystem();
        var viewport = new Viewport(0, 0, 800, 600);

        targeting.OnContactUpdated(new RadarContact(
            "station:test",
            "Test Station",
            new Vector3(0f, 0f, 10f),
            Vector3.Zero,
            ContactType.Station,
            ShipDistanceMeters: 1000f));
        targeting.SelectClosestObjectToReticle(Matrix.Identity, viewport);

        targeting.OnContactUpdated(new RadarContact(
            "station:test",
            "Test Station",
            new Vector3(0f, 0f, 10f),
            Vector3.Zero,
            ContactType.Station,
            ShipDistanceMeters: 750f));

        Assert.Equal(750f, targeting.CurrentRadarTarget!.Value.EffectiveShipDistanceMeters);
    }

    [Fact]
    public void SelectClosestNavToReticleCanSelectStation()
    {
        var targeting = new TargetingSystem();
        var viewport = new Viewport(0, 0, 800, 600);
        var viewProjection = Matrix.CreateLookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.Up)
            * Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 4f / 3f, 0.1f, 1000f);
        var star = new Star { Name = "Test Star" };
        var station = new Station { Name = "Test Station", Size = StationSize.Small };
        var body = new OrbitalBody { Name = "Test Planet", BodyType = BodyType.RockyPlanet };

        DataBus.Drain();
        targeting.SelectClosestNavToReticle(
            viewProjection,
            viewport,
            new DVec3(1000.0, 0.0, 0.0),
            star,
            [(body, new DVec3(1000.5, 0.0, -10.0))],
            [(station, new DVec3(1000.0, 0.0, -10.0))]);
        DataBus.Drain();

        Assert.Same(station, targeting.NavStationTarget);
        Assert.Equal("Test Station", targeting.NavTargetName);
    }

    [Fact]
    public void ClearNavTargetLeavesObjectTargetUntouched()
    {
        var targeting = new TargetingSystem();
        var viewport = new Viewport(0, 0, 800, 600);
        var station = new Station { Name = "Test Station", Size = StationSize.Small };

        DataBus.Drain();
        targeting.OnContactUpdated(new RadarContact(
            "station:test",
            "Test Station",
            new Vector3(0f, 0f, 10f),
            Vector3.Zero,
            ContactType.Station,
            ShipDistanceMeters: 1000f));
        targeting.SelectClosestObjectToReticle(Matrix.Identity, viewport);
        targeting.SetNavTarget(station);

        targeting.ClearNavTarget();
        DataBus.Drain();

        Assert.True(targeting.HasRadarTarget);
        Assert.False(targeting.HasNavTarget);
    }
}
