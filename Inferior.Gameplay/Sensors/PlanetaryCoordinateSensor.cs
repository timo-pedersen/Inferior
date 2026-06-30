using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Sensors;

public readonly record struct PlanetaryCoordinates(
    double Altitude,
    double Latitude,
    double Longitude,
    double Heading,
    double GroundSpeed,
    double VerticalSpeed,
    double Temperature,
    double Pressure);

public sealed class PlanetaryCoordinateSensor
{
    public void Tick(DVec3 shipPos, DVec3 shipVel, OrbitalBody body, DVec3 bodyPos, double dt)
    {
        if (body.Planet == null) return;

        var coords = Compute(shipPos, shipVel, body, bodyPos);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Altitude,      coords.Altitude);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Latitude,      coords.Latitude);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Longitude,     coords.Longitude);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Heading,       coords.Heading);
        DataBus.Instruments.Publish(Topics.PlanetCoord.GroundSpeed,   coords.GroundSpeed);
        DataBus.Instruments.Publish(Topics.PlanetCoord.VerticalSpeed, coords.VerticalSpeed);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Temperature,   coords.Temperature);
        DataBus.Instruments.Publish(Topics.PlanetCoord.Pressure,      coords.Pressure);
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

        double lat = System.Math.Asin(System.Math.Clamp(localPos.Y / localLen, -1.0, 1.0))
                     * (180.0 / System.Math.PI);
        double lon = System.Math.Atan2(localPos.Z, localPos.X) * (180.0 / System.Math.PI);

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
        double heading = System.Math.Atan2(vE, vN) * (180.0 / System.Math.PI);
        if (heading < 0.0) heading += 360.0;

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

        // Pressure: barometric formula (bar) using PlanetData if available, legacy density otherwise
        double pressure = body.DensityAtAltitude(altitude);

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
}
