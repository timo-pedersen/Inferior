using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Gameplay.Physics;
using Inferior.Gameplay.Sensors;
using SensorEnvironment = Inferior.Gameplay.SensorData.Environment;
using Xunit;

namespace Inferior.Gameplay.Test;

public sealed class SolarHeatSensorTests
{
    private const double SolarRadiusMetres = 6.957e8;

    [Fact]
    public void SolarReferenceAtOneAuIsApproximatelyEarthIrradiance()
    {
        var star = StarBody(SolarRadiusMetres, 5_778.0);

        double irradiance = SensorEnvironment.StellarHeatIrradiance(
            star,
            new DVec3(Units.AU, 0.0, 0.0));

        Assert.InRange(irradiance, 1_350.0, 1_375.0);
    }

    [Fact]
    public void IrradianceUsesActualTemperatureRadiusAndInverseSquareDistance()
    {
        var baseline = StarBody(radius: 10.0, temperatureKelvin: 1_000.0);
        var hotterAndLarger = StarBody(radius: 20.0, temperatureKelvin: 2_000.0);
        var observer = new DVec3(1_000.0, 0.0, 0.0);

        double baselineFlux = SensorEnvironment.StellarHeatIrradiance(baseline, observer);
        double changedFlux = SensorEnvironment.StellarHeatIrradiance(hotterAndLarger, observer);
        double twiceAsFar = SensorEnvironment.StellarHeatIrradiance(
            baseline,
            new DVec3(2_000.0, 0.0, 0.0));

        Assert.Equal(64.0, changedFlux / baselineFlux, precision: 10);
        Assert.Equal(0.25, twiceAsFar / baselineFlux, precision: 10);
    }

    [Fact]
    public void LiveCelestialBodyPreservesGeneratedStarTemperature()
    {
        var star = new Star
        {
            MassKg = 1.5e30,
            RadiusMeters = 4.2e8,
            Temperature = 6_123.45,
            SpectralClass = SpectralClass.G,
        };

        CelestialBody live = CelestialBody.FromStar(star, new DVec3(1.0, 2.0, 3.0));

        Assert.Equal(star.Temperature, live.SurfaceTemperatureKelvin);
        Assert.Equal(star.RadiusMeters, live.Radius);
        Assert.Equal(star.SpectralClass, live.Class);
    }

    [Fact]
    public void InstalledSensorPublishesIrradianceOnScalarTelemetry()
    {
        SimWorld oldWorld = SensorEnvironment.World;
        DVec3 oldPosition = SensorEnvironment.ShipPosition;
        DVec3 oldVelocity = SensorEnvironment.ShipVelocity;
        string deviceId = $"SolarHeat-{Guid.NewGuid():N}";
        string topic = $"{deviceId}.{Topics.SolarHeat.Irradiance}";
        var world = new SimWorld();
        world.MassiveBodies.Add(StarBody(SolarRadiusMetres, 5_778.0));
        var readings = new List<double>();
        TelemetryInfo? publishedInfo = null;
        using IDisposable subscription = DataBus.ScalarTelemetry.Subscribe(
            topic,
            readings.Add,
            ReplayMode.None);
        using IDisposable infoSubscription = DataBus.TelemetryInfo.Subscribe(
            topic,
            info => publishedInfo = info,
            ReplayMode.None);
        var sensor = new SolarHeatSensor(deviceId);

        try
        {
            SensorEnvironment.UpdateFromSimThread(
                world,
                new DVec3(Units.AU, 0.0, 0.0),
                DVec3.Zero);
            sensor.ActivateBus();
            sensor.PowerOn = true;
            sensor.NotifyPowerAvailable();
            sensor.Tick(1.0 / 60.0);
            DataBus.Drain();

            Assert.Single(readings);
            Assert.InRange(readings[0], 1_350.0, 1_375.0);
            Assert.NotNull(publishedInfo);
            Assert.Equal(PhysicalQuantity.Irradiance, publishedInfo.Quantity);
            Assert.Equal(PublicationMode.Periodic, publishedInfo.Publication.Mode);
            Assert.Equal(SolarHeatSensor.ReportFrequencyHz,
                publishedInfo.Publication.NominalFrequencyHz);
        }
        finally
        {
            sensor.DeactivateBus();
            DataBus.RemoveDevice(deviceId);
            SensorEnvironment.UpdateFromSimThread(oldWorld, oldPosition, oldVelocity);
        }
    }

    private static CelestialBody StarBody(double radius, double temperatureKelvin)
        => new()
        {
            Position = DVec3.Zero,
            Radius = radius,
            Mass = Units.SolarMass,
            Class = SpectralClass.G,
            SurfaceTemperatureKelvin = temperatureKelvin,
        };
}
