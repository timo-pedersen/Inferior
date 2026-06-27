namespace Inferior.Core.DataBus;

/// <summary>
/// Topic name constants — the value-name portion of a topic string.
/// Full topic: $"ComponentName.{Topics.PowerCore.PowerLoad}"
/// Multiple instances: $"PowerCore_2.{Topics.PowerCore.PowerLoad}"
/// </summary>
public static class Topics
{
    public static class PowerCore
    {
        public const string PowerLoad   = "PowerLoad";
        public const string PowerOutput = "PowerOutput";
        public const string SafeRange   = "SafeRange";
        public const string DamagePercent = "DamagePercent";
    }

    public static class Thermal
    {
        public const string Load        = "Load";
        public const string DamagePercent = "DamagePercent";
    }

    public static class Shield
    {
        public const string Name = "Shield";
        public const string Capacitor   = "Capacitor";
        public const string DamagePercent = "DamagePercent";
    }

    public static class Drive
    {
        public const string Offset        = "Offset";
        public const string FuelRemaining = "FuelRemaining";
        public const string DamagePercent = "DamagePercent";
    }

    public static class System
    {
        // Single-channel bus — all system messages use this topic
        public const string All = "system";
    }

    public static class Radar
    {
        // Single-channel bus — all radar contacts and losses use this topic
        public const string All = "radar";
    }

    public static class Commander
    {
        public const string Sanity = "Sanity";
    }

    public static class GravitySensor
    {
        public const string Strength   = "Strength";    // m/s² — net gravitational acceleration
        public const string DirectionX = "DirectionX";  // normalised gravity vector components
        public const string DirectionY = "DirectionY";
        public const string DirectionZ = "DirectionZ";
    }

    public static class Engine
    {
        public const string Throttle   = "Throttle";    // -1..1
        public const string Thrust     = "Thrust";      // newtons
        public const string DownThrust = "DownThrust";  // newtons
        public const string AlphaRed   = "AlphaRed";    // watts
        public const string Damage     = "Damage";      // 0..1
    }

    public static class ArtificialGravity
    {
        public const string Power = "Power";  // watts drawn
    }

    public static class Gyro
    {
        public const string TorqueFactor = "TorqueFactor";  // multiplier
        public const string Damage       = "Damage";
    }

    public static class LifeSupport
    {
        public const string Pressure    = "Pressure";    // Pa
        public const string Temperature = "Temperature"; // K
        public const string Oxygen      = "Oxygen";      // 0..1 fraction
    }

    public static class CoolantSystem
    {
        public const string Level    = "Level";    // 0..1 fill fraction
        public const string FlowRate = "FlowRate"; // watts being transported
    }

    public static class Exhaust
    {
        public const string BuildUp = "BuildUp";  // 0..1 dimensionless
    }

    public static class RadiationSensor
    {
        public const string Flux = "Flux";  // W/m²
    }

    public static class MagneticField
    {
        public const string Strength = "Strength";  // Tesla
        public const string X        = "X";
        public const string Y        = "Y";
        public const string Z        = "Z";
    }

    public static class ExternalPressure
    {
        public const string Value = "Pressure";  // Pa
    }

    public static class ExternalTemperature
    {
        public const string Value = "Temperature";  // K
    }

    public static class HeatSink
    {
        public const string Fill        = "Fill";        // 0..1 stored/capacity
        public const string Dissipation = "Dissipation"; // watts current rate
    }

    public static class AtmosphericPressure
    {
        public const string Value = "Pressure";  // Pa — published by active AtmosphericPressureSensor on demand
    }

    public static class PlanetCoord
    {
        public const string Altitude      = "PlanetCoord.Altitude";       // metres above surface
        public const string Latitude      = "PlanetCoord.Latitude";       // degrees, +N/-S
        public const string Longitude     = "PlanetCoord.Longitude";      // degrees, +E/-W
        public const string Heading       = "PlanetCoord.Heading";        // degrees, 0–360
        public const string GroundSpeed   = "PlanetCoord.GroundSpeed";    // m/s horizontal
        public const string VerticalSpeed = "PlanetCoord.VerticalSpeed";  // m/s, +up/-down
        public const string Temperature   = "PlanetCoord.Temperature";    // kelvin
    }

    public static class SolarSpectrum
    {
        public const string Data = "Data";  // double[] — 10-bin normalised Planck spectrum (0-1 per bin)
    }

    public static class Target
    {
        public const string Changed = "Target.Changed";   // RadarContact; empty Id = cleared
    }

    public static class Docking
    {
        public const string PadTargeted   = "Docking.PadTargeted";    // 1.0 when a pad is targeted, 0.0 otherwise
        public const string PadDistance   = "Docking.PadDistance";    // metres to pad world position
        public const string PadDirectionX = "Docking.PadDirectionX";  // ship-frame direction to pad (normalised)
        public const string PadDirectionY = "Docking.PadDirectionY";
        public const string PadDirectionZ = "Docking.PadDirectionZ";
        public const string PadSizeClass  = "Docking.PadSizeClass";   // 0.0 = Small, 1.0 = Large
    }

    /// <summary>
    /// LandingSupportSystem component — approach geometry relative to targeted pad.
    /// All numeric topics published on DataBus.Instruments every sim tick.
    /// </summary>
    public static class LandingSupport
    {
        public const string PadTargeted        = "LandingSupport.PadTargeted";        // 1.0 when active, 0.0 otherwise
        public const string PadSizeClass       = "LandingSupport.PadSizeClass";       // 0.0 = Small, 1.0 = Large
        public const string PadDistance        = "LandingSupport.PadDistance";        // metres, Euclidean
        public const string HeightAbovePad     = "LandingSupport.HeightAbovePad";     // metres along pad normal; positive = correct side
        public const string LateralOffset      = "LandingSupport.LateralOffset";      // metres along pad short axis
        public const string LongitudinalOffset = "LandingSupport.LongitudinalOffset"; // metres along pad forward axis
        public const string HeadingDeviation   = "LandingSupport.HeadingDeviation";   // degrees; 0 = aligned with pad forward
        public const string PitchDeviation     = "LandingSupport.PitchDeviation";     // degrees; 0 = face-on
        public const string UpsideDown         = "LandingSupport.UpsideDown";         // 1.0 = inverted relative to pad
    }

    public static class Ship
    {
        /// <summary>Sum of all component ThermalNode.LastHeatInputW — total heat generation rate (watts).</summary>
        public const string ThermalSignature = "Ship.ThermalSignature";
    }

    /// <summary>Debug / development sensors — not present in release builds.</summary>
    public static class Debug
    {
        public const string Heartbeat = "Heartbeat";  // sine-wave 0–100, for meter demo
        public const string SimTime   = "SimTime";    // growing clock, for meter demo
    }
}
