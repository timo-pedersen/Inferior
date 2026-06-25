using Inferior.Core.Math;
using Inferior.Core.Random;
using Microsoft.Xna.Framework;

namespace Inferior.Galaxy;

/// <summary>
/// Generates Keplerian orbital elements and PlanetData for a planet slot.
/// Called from StarSystem.Generate() for each planet; moons and asteroids use OrbitalBody.Generate().
/// </summary>
public static class PlanetFactory
{
    private const double G       = 6.674e-11;
    private const double SolarGM = 1.327e20;  // m³/s²

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Generate a fully-populated OrbitalBody for a planet slot.
    /// rng should be derived from (systemSeed, planetIndex) for determinism.
    /// Legacy fields (OrbitalRadius, Period, PhaseOffset, BodyType, AtmosphereType, AtmosphereHeight,
    /// AtmosphereColor) are set so existing code that doesn't yet read PlanetData continues to work.
    /// </summary>
    public static OrbitalBody Generate(
        int index, string name,
        SeededRandom rng,
        double semiMajorAxis,
        double starMassKg,
        double starRadius,
        double starTemperature)
    {
        double starGM = G * starMassKg;

        var (sma, ecc, inc, lan, argP, m0) = GenerateOrbitalElements(rng, semiMajorAxis, starGM);
        var data = GeneratePlanetData(rng, sma, starGM, starRadius, starTemperature);

        // Period (Kepler III) and legacy PhaseOffset (≈ MeanAnomalyEpoch for nearly-circular orbits)
        double period = 2.0 * Math.PI * Math.Sqrt((sma * sma * sma) / starGM);

        return new OrbitalBody
        {
            BodyIndex          = index,
            Name               = name,
            BodyType           = MapPlanetTypeToBodyType(data.Type),
            ParentMassKg       = starMassKg,

            // Keplerian elements
            SemiMajorAxis      = sma,
            Eccentricity       = ecc,
            Inclination        = inc,
            AscendingNode      = lan,
            PeriapsisArgument  = argP,
            MeanAnomalyEpoch   = m0,

            // Physical (from PlanetData)
            MassKg             = data.Mass,
            RadiusMeters       = data.Radius,
            AxialTilt          = data.AxialTilt,

            // Atmosphere — legacy fields bridged from PlanetData for existing atmosphere physics
            AtmosphereType     = MapCompositionToAtmosphereType(data.Composition, data.SurfacePressureBars),
            AtmosphereHeight   = data.AtmosphereHeight,
            AtmosphereColor    = data.AtmosphereColor,

            // Planet data (authoritative atmosphere and surface data for Brief B+)
            Planet             = data,

            // Legacy circular-orbit fields — set to circular equivalent so moon/station code works
            OrbitalRadius      = sma,
            Period             = period,
            PhaseOffset        = m0,        // close enough for the circular approximation used by stations

            RotationPeriod     = Math.Abs(data.RotationPeriod),
            RotationAxis       = new DVec3(data.PoleDirection.X, data.PoleDirection.Y, data.PoleDirection.Z),
        };
    }

    // ── Orbital element generation ────────────────────────────────────────────

    private static (double sma, double ecc, double inc, double lan, double argP, double m0)
        GenerateOrbitalElements(SeededRandom rng, double semiMajorAxis, double starGM)
    {
        // ── Eccentricity ──────────────────────────────────────────────────────
        double eRoll = rng.NextDouble();
        double e;
        if      (eRoll < 0.03)  e = 0.25 + rng.NextDouble() * 0.55;  // 3%:  high-e captured body (0.25–0.80)
        else if (eRoll < 0.15)  e = 0.05 + rng.NextDouble() * 0.20;  // 12%: moderate (0.05–0.25)
        else                    e =         rng.NextDouble() * 0.05;  // 85%: nearly circular (0–0.05)

        // ── Inclination ───────────────────────────────────────────────────────
        // AU equivalent scaled by star mass proxy
        double auEquiv = semiMajorAxis / (1.5e11 * Math.Sqrt(starGM / SolarGM));
        double iRoll   = rng.NextDouble();
        double incDeg;
        if (auEquiv < 3.0)
        {
            // Inner planets — tight to ecliptic
            incDeg = iRoll < 0.01
                ? 15.0 + rng.NextDouble() * 30.0     // 1%:  15–45°
                : (rng.NextDouble() - 0.5) * 10.0;  // 99%: ±5°
        }
        else
        {
            // Outer planets — slightly more spread; rare retrograde
            if      (iRoll < 0.03)  incDeg = 90.0 + (rng.NextDouble() - 0.5) * 90.0;
            else if (iRoll < 0.12)  incDeg = (rng.NextDouble() - 0.5) * 30.0;
            else                    incDeg = (rng.NextDouble() - 0.5) * 14.0;
        }

        return (
            sma:  semiMajorAxis,
            ecc:  e,
            inc:  incDeg * Math.PI / 180.0,
            lan:  rng.NextDouble() * Math.PI * 2.0,
            argP: rng.NextDouble() * Math.PI * 2.0,
            m0:   rng.NextDouble() * Math.PI * 2.0);
    }

    // ── PlanetData assembly ───────────────────────────────────────────────────

    private static PlanetData GeneratePlanetData(
        SeededRandom rng, double semiMajorAxis, double starGM,
        double starRadius, double starTemperature)
    {
        ulong planetSeed = ((ulong)(uint)rng.NextInt(int.MinValue, int.MaxValue) << 32)
                         | (uint)rng.NextInt(int.MinValue, int.MaxValue);

        var type = SelectType(rng, semiMajorAxis, starGM);
        var (radius, mass)  = GenerateSizeAndMass(rng, type);
        var (hasAtmo, atmoHeight, pressure, scaleHeight, comp, atmoColor, wind)
                            = GenerateAtmosphere(rng, type);
        var avgTemp         = ComputeAverageTemperature(comp, pressure,
                                 semiMajorAxis, starRadius, starTemperature);
        var (axialTilt, poleDir, rotPeriod, rotEpoch) = GenerateRotation(rng, type);

        bool hasLiquid = hasAtmo && type is PlanetType.Ocean or PlanetType.Rocky
                         && avgTemp is > 273 and < 373;

        return new PlanetData
        {
            Type                = type,
            Radius              = radius,
            Mass                = mass,
            AxialTilt           = axialTilt,
            PoleDirection       = poleDir,
            RotationPeriod      = rotPeriod,
            RotationEpoch       = rotEpoch,
            HasAtmosphere       = hasAtmo,
            AtmosphereHeight    = atmoHeight,
            SurfacePressureBars = pressure,
            PressureScaleHeight = scaleHeight,
            Composition         = comp,
            AtmosphereColor     = atmoColor,
            MaxWindSpeed        = wind,
            AverageTemperature  = avgTemp,
            HasLiquidSurface    = hasLiquid,
            PlanetSeed          = planetSeed,
        };
    }

    // ── Planet type selection ─────────────────────────────────────────────────

    private static PlanetType SelectType(SeededRandom rng, double semiMajorAxis, double starGM)
    {
        double au = semiMajorAxis / (1.5e11 * Math.Sqrt(starGM / SolarGM));
        double r  = rng.NextDouble();
        return au switch
        {
            < 0.4  => r < 0.70 ? PlanetType.Lava     : PlanetType.Barren,
            < 0.8  => r < 0.40 ? PlanetType.Barren   : PlanetType.Rocky,
            < 1.6  => r < 0.45 ? PlanetType.Rocky    : r < 0.75 ? PlanetType.Ocean : PlanetType.Barren,
            < 3.0  => r < 0.50 ? PlanetType.IcyRocky : PlanetType.Barren,
            < 12.0 => r < 0.60 ? PlanetType.GasGiant : PlanetType.IceGiant,
            _      => r < 0.40 ? PlanetType.IceGiant  : PlanetType.IcyRocky,
        };
    }

    // ── Radius and mass ───────────────────────────────────────────────────────

    private static (double radius, double mass) GenerateSizeAndMass(SeededRandom rng, PlanetType type)
    {
        (double minRkm, double maxRkm, double minDens, double maxDens) = type switch
        {
            PlanetType.Barren   => (  500,   5_000, 3000, 5500),
            PlanetType.Lava     => (3_000,   8_000, 4000, 6000),
            PlanetType.Rocky    => (3_000,   9_000, 4000, 6000),
            PlanetType.Ocean    => (5_000,  12_000, 2000, 4000),
            PlanetType.IcyRocky => (1_000,   7_000, 1500, 3000),
            PlanetType.GasGiant => (30_000, 120_000,  500, 1500),
            PlanetType.IceGiant => (15_000,  40_000, 1000, 2000),
            _                   => ( 1_000,   5_000, 3000, 5000),
        };
        double radius  = (minRkm + rng.NextDouble() * (maxRkm - minRkm)) * 1000.0;
        double density = minDens + rng.NextDouble() * (maxDens - minDens);
        double mass    = density * (4.0 / 3.0) * Math.PI * radius * radius * radius;
        return (radius, mass);
    }

    // ── Atmosphere ────────────────────────────────────────────────────────────

    private static (bool has, double height, double pressure, double scaleHeight,
                    AtmosphereCompositionType comp, Color color, double wind)
        GenerateAtmosphere(SeededRandom rng, PlanetType type)
    {
        bool has;
        AtmosphereCompositionType comp;
        double pressureBar;
        double heightKm;

        switch (type)
        {
            case PlanetType.Barren:
                return (false, 0, 0, 0, AtmosphereCompositionType.None, Color.Transparent, 0);

            case PlanetType.Lava:
                has  = rng.NextDouble() < 0.30;
                if (!has) return (false, 0, 0, 0, AtmosphereCompositionType.None, Color.Transparent, 0);
                comp        = AtmosphereCompositionType.CarbonDioxide;
                pressureBar = 10.0 + rng.NextDouble() * 140.0;
                heightKm    = 30.0 + rng.NextDouble() * 50.0;
                break;

            case PlanetType.Rocky:
                has = rng.NextDouble() < 0.80;
                if (!has) return (false, 0, 0, 0, AtmosphereCompositionType.None, Color.Transparent, 0);
                comp = rng.NextDouble() < 0.40
                    ? AtmosphereCompositionType.NitrogenOxygen
                    : AtmosphereCompositionType.CarbonDioxide;
                pressureBar = 0.001 + rng.NextDouble() * 4.999;
                heightKm    = 20.0  + rng.NextDouble() * 80.0;
                break;

            case PlanetType.Ocean:
                has         = true;
                comp        = AtmosphereCompositionType.NitrogenOxygen;
                pressureBar = 0.5 + rng.NextDouble() * 2.5;
                heightKm    = 50.0 + rng.NextDouble() * 100.0;
                break;

            case PlanetType.IcyRocky:
                has = rng.NextDouble() < 0.20;
                if (!has) return (false, 0, 0, 0, AtmosphereCompositionType.None, Color.Transparent, 0);
                comp        = rng.NextDouble() < 0.50
                    ? AtmosphereCompositionType.Trace
                    : AtmosphereCompositionType.Methane;
                pressureBar = rng.NextDouble() * 0.1;
                heightKm    = rng.NextDouble() * 30.0;
                break;

            case PlanetType.GasGiant:
                has         = true;
                comp        = AtmosphereCompositionType.Hydrogen;
                pressureBar = 500.0 + rng.NextDouble() * 99_500.0;
                heightKm    = 500.0 + rng.NextDouble() * 1500.0;
                break;

            case PlanetType.IceGiant:
                has         = true;
                comp        = rng.NextDouble() < 0.60
                    ? AtmosphereCompositionType.Methane
                    : AtmosphereCompositionType.Ammonia;
                pressureBar = 100.0 + rng.NextDouble() * 9_900.0;
                heightKm    = 200.0 + rng.NextDouble() * 300.0;
                break;

            default:
                return (false, 0, 0, 0, AtmosphereCompositionType.None, Color.Transparent, 0);
        }

        double scaleHeightKm = comp switch
        {
            AtmosphereCompositionType.None           => 0,
            AtmosphereCompositionType.Trace          => 10,
            AtmosphereCompositionType.CarbonDioxide  => pressureBar < 0.1 ? 7 : 15,
            AtmosphereCompositionType.NitrogenOxygen => 8,
            AtmosphereCompositionType.Methane        => 20,
            AtmosphereCompositionType.Ammonia        => 12,
            AtmosphereCompositionType.Hydrogen       => 27,
            _                                        => 8,
        };

        float hueShift = (float)(rng.NextDouble() * 0.12 - 0.06);
        Color baseColor = comp switch
        {
            AtmosphereCompositionType.None           => Color.Transparent,
            AtmosphereCompositionType.Trace          => new Color(220, 220, 230),
            AtmosphereCompositionType.CarbonDioxide  => new Color(200, 160, 120),
            AtmosphereCompositionType.NitrogenOxygen => new Color(130, 160, 220),
            AtmosphereCompositionType.Methane        => rng.NextDouble() < 0.5
                                                         ? new Color(100, 160, 180)
                                                         : new Color(220, 160, 80),
            AtmosphereCompositionType.Ammonia        => new Color(220, 210, 170),
            AtmosphereCompositionType.Hydrogen       => new Color(200, 210, 230),
            _                                        => Color.White,
        };

        Color atmoColor = new Color(
            Math.Clamp((int)(baseColor.R + hueShift * 255), 0, 255),
            Math.Clamp((int)(baseColor.G + hueShift * 200), 0, 255),
            Math.Clamp((int)(baseColor.B - hueShift * 255), 0, 255));

        double wind = comp switch
        {
            AtmosphereCompositionType.None     => 0,
            AtmosphereCompositionType.Trace    => rng.NextDouble() * 50,
            AtmosphereCompositionType.Hydrogen => 100 + rng.NextDouble() * 500,
            _                                  => 10 + rng.NextDouble() * 150,
        };

        return (true, heightKm * 1000.0, pressureBar, scaleHeightKm * 1000.0, comp, atmoColor, wind);
    }

    // ── Temperature ───────────────────────────────────────────────────────────

    private static double ComputeAverageTemperature(
        AtmosphereCompositionType comp, double surfacePressureBar,
        double semiMajorAxis, double starRadius, double starTemperature)
    {
        double albedo = comp switch
        {
            AtmosphereCompositionType.None           => 0.10,
            AtmosphereCompositionType.Trace          => 0.15,
            AtmosphereCompositionType.CarbonDioxide  => surfacePressureBar < 0.1 ? 0.25 : 0.65,
            AtmosphereCompositionType.NitrogenOxygen => 0.30,
            AtmosphereCompositionType.Methane        => 0.20,
            AtmosphereCompositionType.Ammonia        => 0.25,
            AtmosphereCompositionType.Hydrogen       => 0.35,
            _                                        => 0.30,
        };
        double greenhouse = comp switch
        {
            AtmosphereCompositionType.None           => 0,
            AtmosphereCompositionType.Trace          => 5,
            AtmosphereCompositionType.CarbonDioxide  => surfacePressureBar < 0.1 ? 20 : 470,
            AtmosphereCompositionType.NitrogenOxygen => 33,
            AtmosphereCompositionType.Methane        => 15,
            AtmosphereCompositionType.Ammonia        => 20,
            AtmosphereCompositionType.Hydrogen       => 0,
            _                                        => 0,
        };

        // Equilibrium temperature — Stefan-Boltzmann / inverse-square law
        double T_eq = starTemperature
            * Math.Sqrt(starRadius / (2.0 * semiMajorAxis))
            * Math.Pow(1.0 - albedo, 0.25);

        return T_eq + greenhouse;
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private static (double axialTilt, Vector3 poleDir, double rotPeriod, double rotEpoch)
        GenerateRotation(SeededRandom rng, PlanetType type)
    {
        double tiltRoll = rng.NextDouble();
        double tiltDeg;
        if      (tiltRoll < 0.05)  tiltDeg = 70.0 + rng.NextDouble() * 30.0;
        else if (tiltRoll < 0.25)  tiltDeg = 15.0 + rng.NextDouble() * 55.0;
        else                       tiltDeg =         rng.NextDouble() * 15.0;

        double axialTilt = tiltDeg * Math.PI / 180.0;

        double tiltAzimuth = rng.NextDouble() * Math.PI * 2.0;
        var tiltAxis = new Vector3((float)Math.Sin(tiltAzimuth), 0f, (float)Math.Cos(tiltAzimuth));
        var poleDir  = Vector3.Normalize(
            Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(tiltAxis, (float)axialTilt)));

        double hours = type switch
        {
            PlanetType.GasGiant  =>  5.0 + rng.NextDouble() * 15.0,
            PlanetType.IceGiant  => 10.0 + rng.NextDouble() * 10.0,
            PlanetType.Rocky     => 10.0 + rng.NextDouble() * 50.0,
            PlanetType.Ocean     => 15.0 + rng.NextDouble() * 30.0,
            _                    => 20.0 + rng.NextDouble() * 200.0,
        };

        // 5% chance of retrograde
        if (rng.NextDouble() < 0.05) hours = -hours;
        double period = hours * 3600.0;

        double epoch = rng.NextDouble() * Math.PI * 2.0;
        return (axialTilt, poleDir, period, epoch);
    }

    // ── Bridge mappings ───────────────────────────────────────────────────────

    private static BodyType MapPlanetTypeToBodyType(PlanetType type) => type switch
    {
        PlanetType.Barren   => BodyType.RockyPlanet,
        PlanetType.Lava     => BodyType.Volcanic,
        PlanetType.Rocky    => BodyType.RockyPlanet,
        PlanetType.Ocean    => BodyType.OceanPlanet,
        PlanetType.IcyRocky => BodyType.IcePlanet,
        PlanetType.GasGiant => BodyType.GasGiant,
        PlanetType.IceGiant => BodyType.IceGiant,
        _                   => BodyType.RockyPlanet,
    };

    private static AtmosphereType MapCompositionToAtmosphereType(
        AtmosphereCompositionType comp, double pressureBar) => comp switch
    {
        AtmosphereCompositionType.None           => AtmosphereType.None,
        AtmosphereCompositionType.Trace          => AtmosphereType.Thin,
        AtmosphereCompositionType.NitrogenOxygen => AtmosphereType.Breathable,
        AtmosphereCompositionType.CarbonDioxide  => pressureBar < 0.1 ? AtmosphereType.Thin : AtmosphereType.Toxic,
        AtmosphereCompositionType.Methane        => AtmosphereType.Toxic,
        AtmosphereCompositionType.Ammonia        => AtmosphereType.Toxic,
        AtmosphereCompositionType.Hydrogen       => AtmosphereType.Thick,
        _                                        => AtmosphereType.None,
    };
}
