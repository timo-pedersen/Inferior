using Inferior.Core.Math;
using Inferior.Gameplay.Ship;

namespace Inferior.Gameplay.Hull;

public static class Type1HullDefinitionFactory
{
    public static HullDefinition Create() => new()
    {
        HullTypeId   = "type1",
        DisplayName  = "Type-1",
        SizeClass    = ShipSizeClass.Medium,
        HullMass     = 50_000.0,
        CockpitOffset = DVec3.Zero,

        AerodynamicLift         = 1.2,
        AerodynamicBrakeFront   = 1.00,
        AerodynamicBrakeLateral = 2.50,

        Slots =
        [
            new() { SlotId = "reactor",             Label = "Power Reactor",        Category = SlotCategory.PowerReactor,     MaxComponentClass = 4, Required = true  },
            new() { SlotId = "power_bus",           Label = "Power Bus",            Category = SlotCategory.PowerBus,         MaxComponentClass = 4, Required = true  },
            new() { SlotId = "engine.port.01",      Label = "Port Engine",          Category = SlotCategory.Engine,           MaxComponentClass = 4, Required = true  },
            new() { SlotId = "engine.starboard.01", Label = "Starboard Engine",     Category = SlotCategory.Engine,           MaxComponentClass = 4, Required = true  },
            new() { SlotId = "shield_top",          Label = "Top Shield",           Category = SlotCategory.Shield,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "shield_bottom",       Label = "Bottom Shield",        Category = SlotCategory.Shield,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "heat_sink",           Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink,         MaxComponentClass = 4, Required = true  },
            new() { SlotId = "coolant",             Label = "Coolant System",       Category = SlotCategory.CoolantSystem,    MaxComponentClass = 4, Required = false },
            new() { SlotId = "life_support",        Label = "Life Support",         Category = SlotCategory.LifeSupport,      MaxComponentClass = 4, Required = true  },
            new() { SlotId = "sensor_gravity",      Label = "Gravity Sensor",       Category = SlotCategory.Sensor,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "sensor_radiation",    Label = "Radiation Sensor",     Category = SlotCategory.Sensor,           MaxComponentClass = 4, Required = false },
            new() { SlotId = "exhaust",             Label = "Exhaust System",       Category = SlotCategory.Exhaust,          MaxComponentClass = 4, Required = true  },
            new() { SlotId = "internal_lights",     Label = "Internal Lighting",    Category = SlotCategory.InternalLights,   MaxComponentClass = 4, Required = false },
            new() { SlotId = "external_lights",     Label = "External Lighting",    Category = SlotCategory.ExternalLights,   MaxComponentClass = 4, Required = false },
            new() { SlotId = "flyability_mon",      Label = "Flyability Monitor",   Category = SlotCategory.FlyabilityMonitor,MaxComponentClass = 4, Required = true  },
        ],

        VisualGeometry = null,
    };
}
