using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Engines;

public sealed class EnginePairDefinition
{
    public EnginePairDefinition(
        string pairDefinitionId,
        EngineVariantDefinition variant)
    {
        if (string.IsNullOrWhiteSpace(pairDefinitionId))
            throw new ArgumentException("Engine pair definition id must not be empty.", nameof(pairDefinitionId));

        PairDefinitionId = pairDefinitionId;
        Variant = variant ?? throw new ArgumentNullException(nameof(variant));
    }

    public string PairDefinitionId { get; }
    public EngineVariantDefinition Variant { get; }
    public Quaternion HullLocalOrientation { get; init; } = Quaternion.Identity;
}

public sealed record GeneratedEnginePair(
    EngineInstance Left,
    EngineInstance Right)
{
    public IReadOnlyList<EngineInstance> Engines => [Left, Right];
}

public static class EnginePairGenerator
{
    public static GeneratedEnginePair Generate(
        EnginePairDefinition pairDefinition,
        EngineMount portMount,
        EngineMount starboardMount)
    {
        ArgumentNullException.ThrowIfNull(pairDefinition);
        ArgumentNullException.ThrowIfNull(portMount);
        ArgumentNullException.ThrowIfNull(starboardMount);

        if (portMount.Side != EngineMountSide.Port || starboardMount.Side != EngineMountSide.Starboard)
            throw new ArgumentException("Engine pair generation requires port then starboard mounts.");
        if (!AreMirrored(portMount, starboardMount))
            throw new ArgumentException("Engine pair mounts must have mirrored hull-local poses.");
        if (!portMount.CanAccept(pairDefinition.Variant)
            || !starboardMount.CanAccept(pairDefinition.Variant))
        {
            throw new InvalidOperationException(
                $"Engine variant '{pairDefinition.Variant.VariantId}' is not compatible with both mounts.");
        }

        var left = new EngineInstance(Guid.NewGuid().ToString("D"), pairDefinition.Variant);
        var right = new EngineInstance(Guid.NewGuid().ToString("D"), pairDefinition.Variant);
        var leftTransform = new EngineGeometryTransform(
            portMount.Pose.Position,
            pairDefinition.HullLocalOrientation,
            MirroredAcrossHullX: true);
        var rightTransform = new EngineGeometryTransform(
            starboardMount.Pose.Position,
            pairDefinition.HullLocalOrientation,
            MirroredAcrossHullX: false);

        if (!portMount.TryInstall(left, leftTransform)
            || !starboardMount.TryInstall(right, rightTransform))
            throw new InvalidOperationException("Engine pair installation failed after compatibility validation.");

        return new GeneratedEnginePair(left, right);
    }

    internal static bool AreMirrored(
        EngineMount portMount,
        EngineMount starboardMount,
        double tolerance = 1e-6)
    {
        var left = portMount.Pose;
        var right = starboardMount.Pose;
        return Near(left.Position.X, -right.Position.X, tolerance)
            && Near(left.Position.Y, right.Position.Y, tolerance)
            && Near(left.Position.Z, right.Position.Z, tolerance)
            && Near(left.OutwardNormal.X, -right.OutwardNormal.X, tolerance)
            && Near(left.OutwardNormal.Y, right.OutwardNormal.Y, tolerance)
            && Near(left.OutwardNormal.Z, right.OutwardNormal.Z, tolerance)
            && Near(left.Up.X, -right.Up.X, tolerance)
            && Near(left.Up.Y, right.Up.Y, tolerance)
            && Near(left.Up.Z, right.Up.Z, tolerance);
    }

    private static bool Near(double left, double right, double tolerance)
        => Math.Abs(left - right) <= tolerance;
}
