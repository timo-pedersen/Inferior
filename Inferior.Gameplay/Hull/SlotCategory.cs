namespace Inferior.Gameplay.Hull;

/// <summary>
/// Determines which component types may be installed in a hull slot.
/// The fitting screen enforces this; the simulation does not.
/// </summary>
public enum SlotCategory
{
    PowerReactor,
    PowerBus,
    Connector,
    Converter,
    Engine,
    ArtificialGravity,
    Gyro,
    Shield,
    Sensor,
    LifeSupport,
    HeatSink,
    CoolantSystem,
    Exhaust,
    FlyabilityMonitor,
    InternalLights,
    ExternalLights,
    Weapon,
    Cargo,
    Generic,
}
