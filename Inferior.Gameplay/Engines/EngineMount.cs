using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Engines;

public enum EngineMountSide
{
    Port,
    Starboard,
}

public sealed class EngineMountPose
{
    public EngineMountPose(DVec3 position, DVec3 outwardNormal, DVec3 up)
    {
        if (!IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!IsFinite(outwardNormal) || outwardNormal.Length < 1e-9)
            throw new ArgumentOutOfRangeException(nameof(outwardNormal));
        if (!IsFinite(up) || up.Length < 1e-9)
            throw new ArgumentOutOfRangeException(nameof(up));

        DVec3 normal = outwardNormal.Normalized();
        DVec3 upAxis = up.Normalized();
        if (Math.Abs(DVec3.Dot(normal, upAxis)) > 0.999)
            throw new ArgumentException("Engine mount normal and up axis must not be parallel.");

        Position = position;
        OutwardNormal = normal;
        Up = upAxis;
    }

    public DVec3 Position { get; }
    public DVec3 OutwardNormal { get; }
    public DVec3 Up { get; }
    public Quaternion Orientation => CreateOrientation(OutwardNormal, Up);

    private static Quaternion CreateOrientation(DVec3 outwardNormal, DVec3 up)
    {
        Vector3 right = Vector3.Normalize(outwardNormal.ToVector3());
        Vector3 upAxis = Vector3.Normalize(up.ToVector3());
        Vector3 forward = Vector3.Normalize(Vector3.Cross(right, upAxis));
        upAxis = Vector3.Normalize(Vector3.Cross(forward, right));
        var basis = new Matrix(
            right.X, right.Y, right.Z, 0f,
            upAxis.X, upAxis.Y, upAxis.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(basis);
    }

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X)
        && double.IsFinite(value.Y)
        && double.IsFinite(value.Z);
}

public sealed record EngineGeometryTransform(
    DVec3 Position,
    Quaternion Orientation,
    bool MirroredAcrossHullX)
{
    public Matrix LocalToHull =>
        Matrix.CreateFromQuaternion(Orientation)
        * Matrix.CreateTranslation(Position.ToVector3());

    public DVec3 TransformVisualPoint(DVec3 point)
    {
        DVec3 corrected = MirroredAcrossHullX
            ? new DVec3(-point.X, point.Y, point.Z)
            : point;
        Vector3 transformed = Vector3.Transform(corrected.ToVector3(), LocalToHull);
        return new DVec3(transformed.X, transformed.Y, transformed.Z);
    }

    public DVec3 TransformDirection(DVec3 direction)
    {
        DVec3 corrected = MirroredAcrossHullX
            ? new DVec3(-direction.X, direction.Y, direction.Z)
            : direction;
        Vector3 transformed = Vector3.Transform(
            corrected.ToVector3(),
            Matrix.CreateFromQuaternion(Orientation));
        return new DVec3(transformed.X, transformed.Y, transformed.Z);
    }
}

/// <summary>A physical installation location owned by one live ship instance.</summary>
public sealed class EngineMount
{
    public EngineMount(
        string mountId,
        string componentSlotId,
        string mountStandardId,
        EngineMountSide side,
        EngineMountPose pose,
        DVec3? hullRootPosition = null,
        DVec3? attachmentInterfacePosition = null)
    {
        if (string.IsNullOrWhiteSpace(mountId))
            throw new ArgumentException("Engine mount id must not be empty.", nameof(mountId));
        if (string.IsNullOrWhiteSpace(componentSlotId))
            throw new ArgumentException("Engine component slot id must not be empty.", nameof(componentSlotId));
        if (string.IsNullOrWhiteSpace(mountStandardId))
            throw new ArgumentException("Engine mount standard id must not be empty.", nameof(mountStandardId));

        MountId = mountId;
        ComponentSlotId = componentSlotId;
        MountStandardId = mountStandardId;
        Side = side;
        Pose = pose ?? throw new ArgumentNullException(nameof(pose));
        if (hullRootPosition is { } root && !IsFinite(root))
            throw new ArgumentOutOfRangeException(nameof(hullRootPosition));
        if (attachmentInterfacePosition is { } attachment && !IsFinite(attachment))
            throw new ArgumentOutOfRangeException(nameof(attachmentInterfacePosition));
        HullRootPosition = hullRootPosition;
        AttachmentInterfacePosition = attachmentInterfacePosition;
    }

    public string MountId { get; }
    public string ComponentSlotId { get; }
    public string MountStandardId { get; }
    public EngineMountSide Side { get; }
    public EngineMountPose Pose { get; }
    public DVec3? HullRootPosition { get; }
    public DVec3? AttachmentInterfacePosition { get; }
    public EngineInstance? InstalledEngine { get; private set; }

    public bool CanAccept(EngineVariantDefinition variant)
        => InstalledEngine is null
        && variant.IsCompatibleWith(MountStandardId);

    public bool TryInstall(EngineInstance engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var transform = new EngineGeometryTransform(
            Pose.Position,
            Pose.Orientation,
            MirroredAcrossHullX: Side == EngineMountSide.Port);
        return TryInstall(engine, transform);
    }

    internal bool TryInstall(
        EngineInstance engine,
        EngineGeometryTransform geometryTransform)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(geometryTransform);
        if (engine.IsInstalled || !CanAccept(engine.Variant))
            return false;

        engine.Install(MountId, geometryTransform);
        InstalledEngine = engine;
        return true;
    }

    public EngineInstance? RemoveInstalledEngine()
    {
        EngineInstance? engine = InstalledEngine;
        if (engine is null)
            return null;

        InstalledEngine = null;
        engine.Uninstall();
        return engine;
    }

    private static bool IsFinite(DVec3 value)
        => double.IsFinite(value.X)
        && double.IsFinite(value.Y)
        && double.IsFinite(value.Z);
}
