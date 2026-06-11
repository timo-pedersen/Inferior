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

    public static class SolarSpectrum
    {
        public const string Data = "Data";  // double[] — 10-bin normalised Planck spectrum (0-1 per bin)
    }

    /// <summary>Debug / development sensors — not present in release builds.</summary>
    public static class Debug
    {
        public const string Heartbeat = "Heartbeat";  // sine-wave 0–100, for meter demo
        public const string SimTime   = "SimTime";    // growing clock, for meter demo
    }
}
