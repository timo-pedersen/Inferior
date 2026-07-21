using Inferior.Gameplay.Cockpit;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull.Authoring;

public static class ShipAuthoringConverter
{
    public static ShipAuthoringDocument FromHullDefinition(HullDefinition hull)
    {
        ArgumentNullException.ThrowIfNull(hull);
        if (hull.VisualGeometry is null)
            throw new ArgumentException("Only semantic hull definitions can be exported.", nameof(hull));

        return new ShipAuthoringDocument
        {
            SchemaVersion = 1,
            AssetId = hull.HullTypeId,
            ObjectKind = "ship",
            Hull = new HullAuthoringDto
            {
                DisplayName = hull.DisplayName,
                SizeClass = hull.SizeClass,
                HullMass = hull.HullMass,
                Dimensions = hull.Dimensions is null ? null : new HullDimensionsDto
                {
                    LengthMeters = hull.Dimensions.LengthMeters,
                    WidthMeters = hull.Dimensions.WidthMeters,
                    HeightMeters = hull.Dimensions.HeightMeters,
                    StructuralHullWidthMeters = hull.Dimensions.StructuralHullWidthMeters,
                    StructuralHullHeightMeters = hull.Dimensions.StructuralHullHeightMeters,
                },
                PrimaryDesignBias = hull.PrimaryDesignBias,
                SecondaryDesignBias = hull.SecondaryDesignBias,
                AerodynamicLift = hull.AerodynamicLift,
                AerodynamicBrakeFront = hull.AerodynamicBrakeFront,
                AerodynamicBrakeLateral = hull.AerodynamicBrakeLateral,
                CockpitOffset = Vec3Dto.From(hull.CockpitOffset),
                CockpitPose = new PoseDto
                {
                    Position = Vec3Dto.From(hull.CockpitPose.Position),
                    Orientation = QuaternionDto.From(hull.CockpitPose.Orientation),
                },
                CockpitMounts = hull.CockpitMounts.Select(FromCockpitMount).ToList(),
                Slots = hull.Slots.Select(slot => new HullSlotDto
                {
                    SlotId = slot.SlotId,
                    Label = slot.Label,
                    Category = slot.Category,
                    MaxComponentClass = slot.MaxComponentClass,
                    Required = slot.Required,
                    DefaultComponentDefinitionId = slot.DefaultComponentDefinitionId,
                }).ToList(),
                CargoArrangement = hull.CargoArrangement is null ? null : FromCargoArrangement(hull.CargoArrangement),
                VisualGeometry = FromGeometry(hull.VisualGeometry),
            },
        };
    }

    public static HullDefinition ToHullDefinition(ShipAuthoringDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        HullAuthoringDto hull = document.Hull;
        return new HullDefinition
        {
            HullTypeId = document.AssetId,
            DisplayName = hull.DisplayName,
            SizeClass = hull.SizeClass,
            HullMass = hull.HullMass,
            Dimensions = hull.Dimensions is null
                ? null
                : new HullDimensions(
                    hull.Dimensions.LengthMeters,
                    hull.Dimensions.WidthMeters,
                    hull.Dimensions.HeightMeters,
                    hull.Dimensions.StructuralHullWidthMeters,
                    hull.Dimensions.StructuralHullHeightMeters),
            PrimaryDesignBias = hull.PrimaryDesignBias,
            SecondaryDesignBias = hull.SecondaryDesignBias,
            AerodynamicLift = hull.AerodynamicLift,
            AerodynamicBrakeFront = hull.AerodynamicBrakeFront,
            AerodynamicBrakeLateral = hull.AerodynamicBrakeLateral,
            CockpitOffset = hull.CockpitOffset.ToDVec3(),
            CockpitPose = new CockpitPoseDefinition(
                hull.CockpitPose.Position.ToDVec3(),
                Normalize(hull.CockpitPose.Orientation.ToQuaternion())),
            CockpitMounts = hull.CockpitMounts.Select(ToCockpitMount).ToArray(),
            Slots = hull.Slots.Select(slot => new HullSlot
            {
                SlotId = slot.SlotId,
                Label = slot.Label,
                Category = slot.Category,
                MaxComponentClass = slot.MaxComponentClass,
                Required = slot.Required,
                DefaultComponentDefinitionId = slot.DefaultComponentDefinitionId,
            }).ToArray(),
            CargoArrangement = hull.CargoArrangement is null ? null : ToCargoArrangement(hull.CargoArrangement),
            VisualGeometry = ToGeometry(hull.VisualGeometry),
        };
    }

    private static CockpitMountDto FromCockpitMount(CockpitMountDefinition mount) => new()
    {
        MountId = mount.MountId,
        MountClass = mount.MountClass,
        ShipLocalPosition = Vec3Dto.From(mount.ShipLocalPosition),
        ShipLocalOrientation = QuaternionDto.From(mount.ShipLocalOrientation),
        SocketSizeMeters = Vec3Dto.From(mount.SocketSizeMeters),
        Facing = mount.Facing,
        AllowedRotations = mount.AllowedRotations.OrderBy(rotation => rotation).ToList(),
        DefaultCockpitDefinitionId = mount.DefaultCockpitDefinitionId,
    };

    private static CockpitMountDefinition ToCockpitMount(CockpitMountDto mount) => new()
    {
        MountId = mount.MountId,
        MountClass = mount.MountClass,
        ShipLocalPosition = mount.ShipLocalPosition.ToDVec3(),
        ShipLocalOrientation = Normalize(mount.ShipLocalOrientation.ToQuaternion()),
        SocketSizeMeters = mount.SocketSizeMeters.ToDVec3(),
        Facing = mount.Facing,
        AllowedRotations = mount.AllowedRotations.ToHashSet(),
        DefaultCockpitDefinitionId = mount.DefaultCockpitDefinitionId,
    };

    private static CargoArrangementDto FromCargoArrangement(CargoArrangementDefinition cargo) => new()
    {
        ContainerCapacity = cargo.ContainerCapacity,
        Arrangement = cargo.Arrangement,
        StackBoundsMeters = Vec3Dto.From(cargo.StackBoundsMeters),
        DesignVolumeCenterMeters = Vec3Dto.From(cargo.DesignVolumeCenterMeters),
        DesignVolumeBoundsMeters = Vec3Dto.From(cargo.DesignVolumeBoundsMeters),
        CargoDoorAssemblyId = cargo.CargoDoorAssemblyId,
        RearOpeningBoundsMeters = Vec3Dto.From(cargo.RearOpeningBoundsMeters),
        TransferAxis = Vec3Dto.From(cargo.TransferAxis),
        ContainerPlacements = cargo.ContainerPlacements.Select(placement => new CargoContainerPlacementDto
        {
            PlacementId = placement.PlacementId,
            CenterMeters = Vec3Dto.From(placement.CenterMeters),
            BoundsMeters = Vec3Dto.From(placement.BoundsMeters),
            OccupiedBoundsMeters = FromBounds(placement.OccupiedBoundsMeters),
        }).ToList(),
        LoadingClearanceBoundsMeters = FromBounds(cargo.LoadingClearanceBoundsMeters),
    };

    private static CargoArrangementDefinition ToCargoArrangement(CargoArrangementDto cargo) => new(
        cargo.ContainerCapacity,
        cargo.Arrangement,
        cargo.StackBoundsMeters.ToDVec3(),
        cargo.DesignVolumeCenterMeters.ToDVec3(),
        cargo.DesignVolumeBoundsMeters.ToDVec3(),
        cargo.CargoDoorAssemblyId,
        cargo.RearOpeningBoundsMeters.ToDVec3(),
        cargo.TransferAxis.ToDVec3())
    {
        ContainerPlacements = cargo.ContainerPlacements.Select(placement => new CargoContainerPlacementDefinition(
            placement.PlacementId,
            placement.CenterMeters.ToDVec3(),
            placement.BoundsMeters.ToDVec3(),
            ToBounds(placement.OccupiedBoundsMeters))).ToArray(),
        LoadingClearanceBoundsMeters = ToBounds(cargo.LoadingClearanceBoundsMeters),
    };

    private static SemanticHullGeometryDto FromGeometry(SemanticHullGeometry geometry) => new()
    {
        RequireClosedHull = geometry.RequireClosedHull,
        Vertices = geometry.Vertices.Select(vertex => new SemanticHullVertexDto
        {
            Id = vertex.Id,
            Position = Vec3Dto.From(vertex.Position),
        }).ToList(),
        Faces = geometry.Faces.Select(face => new SemanticHullFaceDto
        {
            Id = face.Id,
            VertexIds = face.VertexIds.ToList(),
            Role = face.Role,
            MaterialGroup = face.MaterialGroup,
            OutwardNormal = Vec3Dto.From(face.OutwardNormal),
            PanelSlotId = face.PanelSlotId,
            AssemblyId = face.AssemblyId,
            ContributesToClosedHull = face.ContributesToClosedHull,
        }).ToList(),
        Assemblies = geometry.Assemblies.Select(assembly => new SemanticAssemblyDto
        {
            AssemblyId = assembly.AssemblyId,
            Kind = assembly.Kind,
            FaceId = assembly.FaceId,
            ClosedPose = assembly.ClosedPose,
            OpeningPolygonVertexIds = assembly.OpeningPolygonVertexIds.ToList(),
            MovementConcept = assembly.MovementConcept,
            MovementAxes = assembly.MovementAxes.Select(Vec3Dto.From).ToList(),
            MovementClearanceVolumes = assembly.MovementClearanceVolumes.Select(FromBounds).ToList(),
        }).ToList(),
        AttachmentPorts = geometry.AttachmentPorts.Select(port => new AttachmentPortDto
        {
            PortId = port.PortId,
            Position = Vec3Dto.From(port.Position),
            Normal = Vec3Dto.From(port.Normal),
            Capabilities = port.Capabilities,
            Up = Vec3Dto.From(port.Up),
            ComponentSlotId = port.ComponentSlotId,
            EngineMountStandardId = port.EngineMountStandardId,
            EngineMountSide = port.EngineMountSide,
            MountRootPosition = port.MountRootPosition is { } root ? Vec3Dto.From(root) : null,
            AttachmentInterfacePosition = port.AttachmentInterfacePosition is { } iface ? Vec3Dto.From(iface) : null,
            FootprintMeters = Vec3Dto.From(port.FootprintMeters),
            ClearanceMinMeters = Vec3Dto.From(port.ClearanceMinMeters),
            ClearanceMaxMeters = Vec3Dto.From(port.ClearanceMaxMeters),
        }).ToList(),
    };

    private static SemanticHullGeometry ToGeometry(SemanticHullGeometryDto geometry) => new()
    {
        RequireClosedHull = geometry.RequireClosedHull,
        Vertices = geometry.Vertices.Select(vertex => new SemanticHullVertex(
            vertex.Id,
            vertex.Position.ToDVec3())).ToArray(),
        Faces = geometry.Faces.Select(face => new SemanticHullFace(
            face.Id,
            face.VertexIds.ToArray(),
            face.Role,
            face.MaterialGroup,
            face.OutwardNormal.ToDVec3(),
            face.PanelSlotId,
            face.AssemblyId)
        {
            ContributesToClosedHull = face.ContributesToClosedHull,
        }).ToArray(),
        Assemblies = geometry.Assemblies.Select(assembly => new SemanticAssemblyDefinition(
            assembly.AssemblyId,
            assembly.Kind,
            assembly.FaceId)
        {
            ClosedPose = assembly.ClosedPose,
            OpeningPolygonVertexIds = assembly.OpeningPolygonVertexIds.ToArray(),
            MovementConcept = assembly.MovementConcept,
            MovementAxes = assembly.MovementAxes.Select(axis => axis.ToDVec3()).ToArray(),
            MovementClearanceVolumes = assembly.MovementClearanceVolumes.Select(ToBounds).ToArray(),
        }).ToArray(),
        AttachmentPorts = geometry.AttachmentPorts.Select(port => new AttachmentPortDefinition(
            port.PortId,
            port.Position.ToDVec3(),
            port.Normal.ToDVec3(),
            port.Capabilities)
        {
            Up = port.Up.ToDVec3(),
            ComponentSlotId = port.ComponentSlotId,
            EngineMountStandardId = port.EngineMountStandardId,
            EngineMountSide = port.EngineMountSide,
            MountRootPosition = port.MountRootPosition?.ToDVec3(),
            AttachmentInterfacePosition = port.AttachmentInterfacePosition?.ToDVec3(),
            FootprintMeters = port.FootprintMeters.ToDVec3(),
            ClearanceMinMeters = port.ClearanceMinMeters.ToDVec3(),
            ClearanceMaxMeters = port.ClearanceMaxMeters.ToDVec3(),
        }).ToArray(),
    };

    private static BoundsDto FromBounds(SemanticBounds bounds) => new()
    {
        Min = Vec3Dto.From(bounds.Min),
        Max = Vec3Dto.From(bounds.Max),
    };

    private static SemanticBounds ToBounds(BoundsDto bounds)
        => new(bounds.Min.ToDVec3(), bounds.Max.ToDVec3());

    private static Quaternion Normalize(Quaternion quaternion)
    {
        if (!float.IsFinite(quaternion.X)
            || !float.IsFinite(quaternion.Y)
            || !float.IsFinite(quaternion.Z)
            || !float.IsFinite(quaternion.W)
            || quaternion.LengthSquared() <= 1e-12f)
        {
            return quaternion;
        }

        return Quaternion.Normalize(quaternion);
    }
}
