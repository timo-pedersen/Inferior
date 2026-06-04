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
        public const string Strength = "Strength";   // m/s² — net gravitational acceleration
    }

    /// <summary>Debug / development sensors — not present in release builds.</summary>
    public static class Debug
    {
        public const string Heartbeat = "Heartbeat";  // sine-wave 0–100, for meter demo
        public const string SimTime   = "SimTime";    // growing clock, for meter demo
    }
}
