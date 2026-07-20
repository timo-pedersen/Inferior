using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Engines;

public static class EngineInstallationGenerator
{
    public static EngineInstance Install(
        EngineVariantDefinition variant,
        EngineMount mount,
        Quaternion? hullLocalOrientation = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(mount);

        if (!mount.CanAccept(variant))
        {
            throw new InvalidOperationException(
                $"Engine variant '{variant.VariantId}' cannot be installed on mount " +
                $"'{mount.MountId}' ({mount.MountStandardId}).");
        }

        var transform = new EngineGeometryTransform(
            mount.Pose.Position,
            hullLocalOrientation ?? Quaternion.Identity,
            MirroredAcrossHullX: mount.Side == EngineMountSide.Port);
        ValidatePhysicalInterface(mount, variant, transform);

        var engine = new EngineInstance(Guid.NewGuid().ToString("D"), variant);
        if (!mount.TryInstall(engine, transform))
            throw new InvalidOperationException(
                $"Engine installation on mount '{mount.MountId}' failed after validation.");

        return engine;
    }

    private static void ValidatePhysicalInterface(
        EngineMount mount,
        EngineVariantDefinition variant,
        EngineGeometryTransform transform)
    {
        if (mount.AttachmentInterfacePosition is not { } expected
            || variant.Engine.VisualGeometry is not { } geometry)
        {
            return;
        }

        DVec3 actual = transform.TransformVisualPoint(geometry.AttachmentInterfacePosition);
        if ((actual - expected).Length > 1e-5)
        {
            throw new InvalidOperationException(
                $"Engine variant '{variant.VariantId}' interface {actual} does not meet mount " +
                $"'{mount.MountId}' interface {expected}.");
        }
    }
}
