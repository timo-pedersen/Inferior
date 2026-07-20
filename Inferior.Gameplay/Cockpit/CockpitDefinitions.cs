using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public enum CockpitMountClass
{
    C1,
    C2,
    C3,
    C4,
    C5,
}

public enum MountFacing
{
    Forward,
    Aft,
    Up,
    Down,
    Port,
    Starboard,
}

public enum CockpitRotationStep
{
    Deg0,
    Deg90,
    Deg180,
    Deg270,
}

public sealed record CockpitMountDefinition
{
    public required string MountId { get; init; }
    public required CockpitMountClass MountClass { get; init; }
    public required DVec3 ShipLocalPosition { get; init; }
    public required Quaternion ShipLocalOrientation { get; init; }
    public required DVec3 SocketSizeMeters { get; init; }
    public required MountFacing Facing { get; init; }
    public required IReadOnlySet<CockpitRotationStep> AllowedRotations { get; init; }
    public string? DefaultCockpitDefinitionId { get; init; }
}

public sealed record CockpitModuleDefinition
{
    public required string DefinitionId { get; init; }
    public required string DisplayName { get; init; }
    public required CockpitMountClass RequiredMountClass { get; init; }
    public required DVec3 PilotLocalPosition { get; init; }
    public required Quaternion PilotLocalOrientation { get; init; }
    public required DVec3 CameraLocalPosition { get; init; }
    public required Quaternion CameraLocalOrientation { get; init; }
    public DVec3? CanopyLocalPosition { get; init; }
    public Quaternion? CanopyLocalOrientation { get; init; }
    public MountFacing? PreferredFacing { get; init; }
    public bool HasCanopyLights { get; init; }
    public bool HasCockpitLights { get; init; }
    public CockpitVisualGeometry? VisualGeometry { get; init; }
}
