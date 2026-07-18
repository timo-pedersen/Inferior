namespace Inferior.Gameplay.Engines;

public sealed record EnginePresentationSnapshot(
    string InstanceId,
    string VariantId,
    EngineVisualGeometry VisualGeometry,
    EngineVisualDefinition? VisualDefinition,
    EngineVisualState VisualState,
    EngineGeometryTransform GeometryTransform,
    double DamageFraction,
    double WearFraction);

public sealed record EngineMountPresentationSnapshot(
    string MountId,
    string ComponentSlotId,
    string MountStandardId,
    EngineMountSide Side,
    EngineMountPose Pose,
    Inferior.Core.Math.DVec3? HullRootPosition,
    Inferior.Core.Math.DVec3? AttachmentInterfacePosition,
    EnginePresentationSnapshot? InstalledEngine);
