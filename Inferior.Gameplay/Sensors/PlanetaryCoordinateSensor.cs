using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Sensors;

public readonly record struct PlanetaryCoordinates(
    double AltitudeMeters,
    double LatitudeRadians,
    double LongitudeRadians,
    double HeadingRadians,
    double GroundSpeedMetersPerSecond,
    double VerticalSpeedMetersPerSecond,
    double TemperatureKelvin,
    double PressurePascals);

public sealed class PlanetaryCoordinateSensor
{
    private const string DeviceId = "PlanetaryCoordinateSensor";

    public PlanetaryCoordinateSensor()
    {
        Register(Topics.PlanetCoord.Altitude, PhysicalQuantity.Distance);
        Register(Topics.PlanetCoord.Latitude, PhysicalQuantity.Angle,
            new RangeValue(-Math.PI / 2.0, Math.PI / 2.0));
        Register(Topics.PlanetCoord.Longitude, PhysicalQuantity.Angle,
            new RangeValue(-Math.PI, Math.PI));
        Register(Topics.PlanetCoord.Heading, PhysicalQuantity.Angle,
            new RangeValue(0.0, Math.Tau));
        Register(Topics.PlanetCoord.GroundSpeed, PhysicalQuantity.Speed);
        Register(Topics.PlanetCoord.VerticalSpeed, PhysicalQuantity.Speed);
        Register(Topics.PlanetCoord.Temperature, PhysicalQuantity.Temperature);
        Register(Topics.PlanetCoord.Pressure, PhysicalQuantity.Pressure);

        DataBus.DeviceInfo.Publish(DeviceId, new DeviceInfo
        {
            DeviceId = DeviceId,
            PublishedTopics =
            [
                Topics.PlanetCoord.Altitude,
                Topics.PlanetCoord.Latitude,
                Topics.PlanetCoord.Longitude,
                Topics.PlanetCoord.Heading,
                Topics.PlanetCoord.GroundSpeed,
                Topics.PlanetCoord.VerticalSpeed,
                Topics.PlanetCoord.Temperature,
                Topics.PlanetCoord.Pressure,
            ],
            Power = new PowerProfile(0.0, 0.0),
        });
        DataBus.DeviceState.Publish(DeviceId, new DeviceState(
            DeviceId,
            DeviceOperationalStatus.Running,
            Damage: 0.0,
            Efficiency: 1.0,
            SimulationTime: GameClock.SimTime));
    }

    public void Tick(DVec3 shipPos, DVec3 shipVel, OrbitalBody body, DVec3 bodyPos, double dt)
    {
        if (body.Planet == null) return;

        var coords = Compute(shipPos, shipVel, body, bodyPos);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Altitude,      coords.AltitudeMeters);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Latitude,      coords.LatitudeRadians);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Longitude,     coords.LongitudeRadians);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Heading,       coords.HeadingRadians);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.GroundSpeed,   coords.GroundSpeedMetersPerSecond);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.VerticalSpeed, coords.VerticalSpeedMetersPerSecond);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Temperature,   coords.TemperatureKelvin);
        DataBus.ScalarTelemetry.Publish(Topics.PlanetCoord.Pressure,      coords.PressurePascals);
    }

    public static PlanetaryCoordinates Compute(DVec3 shipPos, DVec3 shipVel, OrbitalBody body, DVec3 bodyPos)
    {
        // Displacement from planet centre in galaxy space
        DVec3  relPos = shipPos - bodyPos;
        double radius = relPos.Length;
        if (radius < 1.0) radius = 1.0;
        double altitude = radius - body.RadiusMeters;

        // Transform into planet-local space to extract lat/lon
        Quaternion invOri   = Quaternion.Inverse(body.Orientation);
        var        relF     = new Vector3((float)relPos.X, (float)relPos.Y, (float)relPos.Z);
        Vector3    localPos = Vector3.Transform(relF, invOri);

        double localLen = System.Math.Sqrt(
            (double)localPos.X * localPos.X +
            (double)localPos.Y * localPos.Y +
            (double)localPos.Z * localPos.Z);
        if (localLen < 1.0) localLen = 1.0;

        double lat = System.Math.Asin(System.Math.Clamp(localPos.Y / localLen, -1.0, 1.0));
        double lon = System.Math.Atan2(localPos.Z, localPos.X);

        // Surface normal (up) in galaxy space
        DVec3 up = relPos / radius;

        // Planet north pole direction in galaxy space
        Vector3 poleF = Vector3.Transform(Vector3.UnitY, body.Orientation);
        var     pole  = new DVec3(poleF.X, poleF.Y, poleF.Z);

        // North = component of pole perpendicular to surface normal
        DVec3  northRaw = pole - up * DVec3.Dot(pole, up);
        double northLen = northRaw.Length;
        DVec3  north    = northLen > 0.01 ? northRaw / northLen : new DVec3(0, 0, 1);
        DVec3  east     = DVec3.Normalize(DVec3.Cross(north, up));

        // Heading from velocity projected onto surface plane
        DVec3  vHoriz  = shipVel - up * DVec3.Dot(shipVel, up);
        double vN      = DVec3.Dot(vHoriz, north);
        double vE      = DVec3.Dot(vHoriz, east);
        double heading = System.Math.Atan2(vE, vN);
        if (heading < 0.0) heading += System.Math.Tau;

        // Ground speed: ship velocity minus surface rotation, projected horizontal
        double rotPeriod = System.Math.Abs(body.RotationPeriod) > 0.01 ? body.RotationPeriod : body.Period;
        double omega     = 2.0 * System.Math.PI / rotPeriod;
        // Sign: negative rotation period = retrograde
        DVec3  bodyRot     = DVec3.Cross(pole * omega * (body.RotationPeriod < 0 ? -1.0 : 1.0), relPos);
        DVec3  velRel      = shipVel - bodyRot;
        double groundSpeed = (velRel - up * DVec3.Dot(velRel, up)).Length;

        // Vertical speed
        double verticalSpeed = DVec3.Dot(shipVel, up);

        // Temperature: base × altitude fraction × solar elevation factor
        double temperature = ComputeTemperature(altitude, body, bodyPos, up);

        // DensityAtAltitude is bar for generated planets; publish pressure in raw SI pascals.
        double pressure = body.DensityAtAltitude(altitude) * 100_000.0;

        return new PlanetaryCoordinates(altitude, lat, lon, heading, groundSpeed, verticalSpeed, temperature, pressure);
    }

    private static double ComputeTemperature(double altitude, OrbitalBody body, DVec3 bodyPos, DVec3 up)
    {
        if (body.Planet == null) return 0.0;

        double bodyDist    = bodyPos.Length;
        DVec3  sunDir      = bodyDist > 0 ? -bodyPos / bodyDist : new DVec3(0, 1, 0);
        double solarFactor = System.Math.Max(DVec3.Dot(sunDir, up), 0.0);
        double altFraction = System.Math.Clamp(1.0 - altitude / body.AtmosphereCeilingAltitude, 0.0, 1.0);

        return body.Planet.AverageTemperature * altFraction * (0.5 + 0.5 * solarFactor);
    }

    private static void Register(
        string topic,
        PhysicalQuantity quantity,
        RangeValue? operatingRange = null)
        => DataBus.PublishTelemetryInfo(new TelemetryInfo
        {
            Topic = topic,
            DeviceId = DeviceId,
            ValueKind = TelemetryValueKind.Scalar,
            Quantity = quantity,
            OperatingRange = operatingRange,
            SuggestedDisplayRange = operatingRange,
            Publication = new PublicationInfo(PublicationMode.EveryTick),
            TopicPolicy = TopicPolicy.LatestState,
        });
}
