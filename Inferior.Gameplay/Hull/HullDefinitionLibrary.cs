using Inferior.Core.Math;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

/// <summary>
/// Static registry of all hull definitions.
/// Loaded at startup; immutable once initialised.
///
/// Add new hulls via Register() in the static constructor.
/// The fitting screen, ShipBuilder, and FlyabilityMonitor all look up definitions here.
/// </summary>
public static class HullDefinitionLibrary
{
    private static readonly Dictionary<string, HullDefinition> _defs = new(StringComparer.OrdinalIgnoreCase);

    static HullDefinitionLibrary()
    {
        Register(AriesHullDefinitionFactory.Create());
        Register(AsteriskHullDefinitionFactory.Create());
        Register(Sidewinder());
        Register(Cobra());
    }

    public static HullDefinition Get(string hullTypeId)
    {
        if (_defs.TryGetValue(hullTypeId, out var def)) return def;
        throw new KeyNotFoundException($"No hull definition found for '{hullTypeId}'.");
    }

    public static bool TryGet(string hullTypeId, out HullDefinition? def)
        => _defs.TryGetValue(hullTypeId, out def);

    public static IReadOnlyCollection<HullDefinition> All => _defs.Values;

    private static void Register(HullDefinition def)
    {
        if (_defs.ContainsKey(def.HullTypeId))
            throw new InvalidOperationException($"Duplicate hull definition '{def.HullTypeId}'.");
        _defs[def.HullTypeId] = def;
    }

    private static HullDefinition Sidewinder() => new()
    {
        HullTypeId   = "sidewinder",
        DisplayName  = "Sidewinder",
        SizeClass    = ShipSizeClass.Small,
        HullMass     = 25_000.0,
        CockpitOffset = new DVec3(0, 3, -12),
        CockpitPose = new CockpitPoseDefinition(new DVec3(0, 3, -12), Quaternion.Identity),

        AerodynamicLift         = 0.8,
        AerodynamicBrakeFront   = 0.75,
        AerodynamicBrakeLateral = 2.00,

        Slots =
        [
            new() { SlotId = "reactor",           Label = "Power Reactor",        Category = SlotCategory.PowerReactor,     MaxComponentClass = 2, Required = true  },
            new() { SlotId = "power_bus",         Label = "Power Bus",            Category = SlotCategory.PowerBus,         MaxComponentClass = 2, Required = true  },
            new() { SlotId = "engine_main",       Label = "Main Engine",          Category = SlotCategory.Engine,           MaxComponentClass = 2, Required = true  },
            new() { SlotId = "shield_top",        Label = "Top Shield",           Category = SlotCategory.Shield,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "shield_bottom",     Label = "Bottom Shield",        Category = SlotCategory.Shield,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "heat_sink",         Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink,         MaxComponentClass = 2, Required = true  },
            new() { SlotId = "coolant",           Label = "Coolant System",       Category = SlotCategory.CoolantSystem,    MaxComponentClass = 2, Required = false },
            new() { SlotId = "life_support",      Label = "Life Support",         Category = SlotCategory.LifeSupport,      MaxComponentClass = 2, Required = true  },
            new() { SlotId = "sensor_gravity",    Label = "Gravity Sensor",       Category = SlotCategory.Sensor,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "sensor_radiation",  Label = "Radiation Sensor",     Category = SlotCategory.Sensor,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "exhaust",           Label = "Exhaust System",       Category = SlotCategory.Exhaust,          MaxComponentClass = 2, Required = true  },
            new() { SlotId = "internal_lights",   Label = "Internal Lighting",    Category = SlotCategory.InternalLights,   MaxComponentClass = 2, Required = false },
            new() { SlotId = "external_lights",   Label = "External Lighting",    Category = SlotCategory.ExternalLights,   MaxComponentClass = 2, Required = false },
            new() { SlotId = "flyability_mon",    Label = "Flyability Monitor",   Category = SlotCategory.FlyabilityMonitor,MaxComponentClass = 2, Required = true  },
        ],
    };

    private static HullDefinition Cobra() => new()
    {
        HullTypeId   = "cobra",
        DisplayName  = "Cobra Mk III",
        SizeClass    = ShipSizeClass.Medium,
        HullMass     = 80_000.0,
        CockpitOffset = new DVec3(0, 4, -18),
        CockpitPose = new CockpitPoseDefinition(new DVec3(0, 4, -18), Quaternion.Identity),

        AerodynamicLift         = 1.4,
        AerodynamicBrakeFront   = 1.10,
        AerodynamicBrakeLateral = 2.75,

        Slots =
        [
            new() { SlotId = "reactor",           Label = "Power Reactor",        Category = SlotCategory.PowerReactor,     MaxComponentClass = 4, Required = true  },
            new() { SlotId = "power_bus",         Label = "Power Bus",            Category = SlotCategory.PowerBus,         MaxComponentClass = 4, Required = true  },
            new() { SlotId = "engine.port.01",    Label = "Port Engine",          Category = SlotCategory.Engine,           MaxComponentClass = 4, Required = true  },
            new() { SlotId = "engine.starboard.01", Label = "Starboard Engine",   Category = SlotCategory.Engine,           MaxComponentClass = 3, Required = false },
            new() { SlotId = "shield_top",        Label = "Top Shield",           Category = SlotCategory.Shield,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "shield_bottom",     Label = "Bottom Shield",        Category = SlotCategory.Shield,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "heat_sink",         Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink,         MaxComponentClass = 4, Required = true  },
            new() { SlotId = "coolant",           Label = "Coolant System",       Category = SlotCategory.CoolantSystem,    MaxComponentClass = 4, Required = false },
            new() { SlotId = "art_gravity",       Label = "Artificial Gravity",   Category = SlotCategory.ArtificialGravity,MaxComponentClass = 3, Required = false },
            new() { SlotId = "gyro",              Label = "Gyroscope",            Category = SlotCategory.Gyro,             MaxComponentClass = 3, Required = false },
            new() { SlotId = "life_support",      Label = "Life Support",         Category = SlotCategory.LifeSupport,      MaxComponentClass = 4, Required = true  },
            new() { SlotId = "sensor_gravity",    Label = "Gravity Sensor",       Category = SlotCategory.Sensor,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "sensor_radiation",  Label = "Radiation Sensor",     Category = SlotCategory.Sensor,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "sensor_magnetic",   Label = "Magnetic Field Sensor",Category = SlotCategory.Sensor,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "exhaust",           Label = "Exhaust System",       Category = SlotCategory.Exhaust,          MaxComponentClass = 4, Required = true  },
            new() { SlotId = "cargo_1",           Label = "Cargo Bay 1",          Category = SlotCategory.Cargo,            MaxComponentClass = 4, Required = false },
            new() { SlotId = "cargo_2",           Label = "Cargo Bay 2",          Category = SlotCategory.Cargo,            MaxComponentClass = 4, Required = false },
            new() { SlotId = "internal_lights",   Label = "Internal Lighting",    Category = SlotCategory.InternalLights,   MaxComponentClass = 4, Required = false },
            new() { SlotId = "external_lights",   Label = "External Lighting",    Category = SlotCategory.ExternalLights,   MaxComponentClass = 4, Required = false },
            new() { SlotId = "flyability_mon",    Label = "Flyability Monitor",   Category = SlotCategory.FlyabilityMonitor,MaxComponentClass = 4, Required = true  },
        ],
    };
}
