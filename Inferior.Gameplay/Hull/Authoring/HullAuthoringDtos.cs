using System.Text.Json.Serialization;
using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull.Authoring;

public sealed class ShipAuthoringDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string AssetId { get; set; } = "";
    public string ObjectKind { get; set; } = "ship";
    public HullAuthoringDto Hull { get; set; } = new();
}

public sealed class HullAuthoringDto
{
    public string DisplayName { get; set; } = "";
    public ShipSizeClass SizeClass { get; set; }
    public double HullMass { get; set; }
    public HullDimensionsDto? Dimensions { get; set; }
    public string? PrimaryDesignBias { get; set; }
    public string? SecondaryDesignBias { get; set; }
    public double AerodynamicLift { get; set; }
    public double AerodynamicBrakeFront { get; set; }
    public double AerodynamicBrakeLateral { get; set; }
    public Vec3Dto CockpitOffset { get; set; }
    public PoseDto CockpitPose { get; set; } = new();
    public List<CockpitMountDto> CockpitMounts { get; set; } = [];
    public List<HullSlotDto> Slots { get; set; } = [];
    public CargoArrangementDto? CargoArrangement { get; set; }
    public SemanticHullGeometryDto VisualGeometry { get; set; } = new();
}

public readonly record struct Vec3Dto(double X, double Y, double Z)
{
    public DVec3 ToDVec3() => new(X, Y, Z);
    public static Vec3Dto From(DVec3 value) => new(value.X, value.Y, value.Z);
}

public readonly record struct QuaternionDto(float X, float Y, float Z, float W)
{
    public Quaternion ToQuaternion() => new(X, Y, Z, W);
    public static QuaternionDto From(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}

public sealed class PoseDto
{
    public Vec3Dto Position { get; set; }
    public QuaternionDto Orientation { get; set; } = QuaternionDto.From(Quaternion.Identity);
}

public sealed class HullDimensionsDto
{
    public double LengthMeters { get; set; }
    public double WidthMeters { get; set; }
    public double HeightMeters { get; set; }
    public double StructuralHullWidthMeters { get; set; }
    public double StructuralHullHeightMeters { get; set; }
}

public sealed class CockpitMountDto
{
    public string MountId { get; set; } = "";
    public CockpitMountClass MountClass { get; set; }
    public Vec3Dto ShipLocalPosition { get; set; }
    public QuaternionDto ShipLocalOrientation { get; set; } = QuaternionDto.From(Quaternion.Identity);
    public Vec3Dto SocketSizeMeters { get; set; }
    public MountFacing Facing { get; set; }
    public List<CockpitRotationStep> AllowedRotations { get; set; } = [];
    public string? DefaultCockpitDefinitionId { get; set; }
}

public sealed class HullSlotDto
{
    public string SlotId { get; set; } = "";
    public string Label { get; set; } = "";
    public SlotCategory Category { get; set; }
    public int MaxComponentClass { get; set; }
    public bool Required { get; set; }
    public string? DefaultComponentDefinitionId { get; set; }
}

public sealed class CargoArrangementDto
{
    public int ContainerCapacity { get; set; }
    public string Arrangement { get; set; } = "";
    public Vec3Dto StackBoundsMeters { get; set; }
    public Vec3Dto DesignVolumeCenterMeters { get; set; }
    public Vec3Dto DesignVolumeBoundsMeters { get; set; }
    public string CargoDoorAssemblyId { get; set; } = "";
    public Vec3Dto RearOpeningBoundsMeters { get; set; }
    public Vec3Dto TransferAxis { get; set; }
    public List<CargoContainerPlacementDto> ContainerPlacements { get; set; } = [];
    public BoundsDto LoadingClearanceBoundsMeters { get; set; } = new();
}

public sealed class CargoContainerPlacementDto
{
    public string PlacementId { get; set; } = "";
    public Vec3Dto CenterMeters { get; set; }
    public Vec3Dto BoundsMeters { get; set; }
    public BoundsDto OccupiedBoundsMeters { get; set; } = new();
}

public sealed class BoundsDto
{
    public Vec3Dto Min { get; set; }
    public Vec3Dto Max { get; set; }
}

public sealed class SemanticHullGeometryDto
{
    public bool RequireClosedHull { get; set; }
    public List<SemanticHullVertexDto> Vertices { get; set; } = [];
    public List<SemanticHullFaceDto> Faces { get; set; } = [];
    public List<SemanticAssemblyDto> Assemblies { get; set; } = [];
    public List<AttachmentPortDto> AttachmentPorts { get; set; } = [];
}

public sealed class SemanticHullVertexDto
{
    public string Id { get; set; } = "";
    public Vec3Dto Position { get; set; }
}

public sealed class SemanticHullFaceDto
{
    public string Id { get; set; } = "";
    public List<string> VertexIds { get; set; } = [];
    public HullSurfaceRole Role { get; set; }
    public string MaterialGroup { get; set; } = "";
    public Vec3Dto OutwardNormal { get; set; }
    public string? PanelSlotId { get; set; }
    public string? AssemblyId { get; set; }
    public bool ContributesToClosedHull { get; set; } = true;
}

public sealed class SemanticAssemblyDto
{
    public string AssemblyId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string FaceId { get; set; } = "";
    public string ClosedPose { get; set; } = "";
    public List<string> OpeningPolygonVertexIds { get; set; } = [];
    public string MovementConcept { get; set; } = "";
    public List<Vec3Dto> MovementAxes { get; set; } = [];
    public List<BoundsDto> MovementClearanceVolumes { get; set; } = [];
}

public sealed class AttachmentPortDto
{
    public string PortId { get; set; } = "";
    public Vec3Dto Position { get; set; }
    public Vec3Dto Normal { get; set; }
    public AttachmentCapability Capabilities { get; set; }
    public Vec3Dto Up { get; set; } = Vec3Dto.From(DVec3.UnitY);
    public string? ComponentSlotId { get; set; }
    public string? EngineMountStandardId { get; set; }
    public EngineMountSide? EngineMountSide { get; set; }
    public Vec3Dto? MountRootPosition { get; set; }
    public Vec3Dto? AttachmentInterfacePosition { get; set; }
    public Vec3Dto FootprintMeters { get; set; }
    public Vec3Dto ClearanceMinMeters { get; set; }
    public Vec3Dto ClearanceMaxMeters { get; set; }
}
