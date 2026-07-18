namespace Inferior.Gameplay.Engines;

/// <summary>A unique physical engine with instance-owned mutable condition state.</summary>
public sealed class EngineInstance
{
    public EngineInstance(string instanceId, EngineVariantDefinition variant)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Engine instance id must not be empty.", nameof(instanceId));

        InstanceId = instanceId;
        Variant = variant ?? throw new ArgumentNullException(nameof(variant));
    }

    public string InstanceId { get; }
    public EngineVariantDefinition Variant { get; }
    public double DamageFraction { get; private set; }
    public double WearFraction { get; private set; }
    public EngineGeometryTransform? GeometryTransform { get; private set; }
    public string? InstalledMountId { get; private set; }

    public bool IsInstalled => InstalledMountId is not null;

    public void SetDamageFraction(double value)
        => DamageFraction = ValidateFraction(value, nameof(value));

    public void SetWearFraction(double value)
        => WearFraction = ValidateFraction(value, nameof(value));

    internal void Install(string mountId, EngineGeometryTransform geometryTransform)
    {
        if (IsInstalled)
            throw new InvalidOperationException(
                $"Engine instance '{InstanceId}' is already installed on '{InstalledMountId}'.");

        InstalledMountId = mountId;
        GeometryTransform = geometryTransform;
    }

    private static double ValidateFraction(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(parameterName, "Condition fraction must be within [0, 1].");
        return value;
    }
}
