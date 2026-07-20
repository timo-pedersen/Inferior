namespace Inferior.Persistence.Data;

public enum CockpitRotationStepRecord
{
    Deg0,
    Deg90,
    Deg180,
    Deg270,
}

public sealed record InstalledCockpitRecord
{
    public string MountId { get; init; } = "";
    public string DefinitionId { get; init; } = "";
    public CockpitRotationStepRecord InstallationRotation { get; init; }
    public bool CanopyLightsOn { get; init; }
    public bool CockpitLightsOn { get; init; }
}
