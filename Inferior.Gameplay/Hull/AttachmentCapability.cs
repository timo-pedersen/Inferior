namespace Inferior.Gameplay.Hull;

[Flags]
public enum AttachmentCapability
{
    None = 0,
    Engine = 1 << 0,
    Weapon = 1 << 1,
    Sensor = 1 << 2,
    Utility = 1 << 3,
    LandingGear = 1 << 4,
    NavigationLight = 1 << 5,
    BeamLight = 1 << 6,
    Container = 1 << 7,
}
