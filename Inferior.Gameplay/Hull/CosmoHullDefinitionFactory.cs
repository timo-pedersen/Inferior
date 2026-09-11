using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

public static class CosmoHullDefinitionFactory
{
    public const string HullId = "cosmo";

    private const string EngineSlotId = "engine.dorsal.01";
    private static readonly DVec3 CockpitPosition = new(0.0, 1.12, -2.12);
    private static readonly Quaternion CockpitOrientation = Quaternion.Identity;

    public static HullDefinition Create() => new()
    {
        HullTypeId = HullId,
        DisplayName = "Cosmo",
        SizeClass = ShipSizeClass.Small,
        HullMass = 12_000.0,
        SingleEngineEfficiency = new DesignedSingleEngineEfficiency(
            forward: 0.75,
            maneuvering: 0.75,
            rotation: 0.60),
        CockpitMounts =
        [
            new CockpitMountDefinition
            {
                MountId = $"{HullId}.cockpit.top.01",
                MountClass = CockpitMountClass.C1,
                ShipLocalPosition = CockpitPosition,
                ShipLocalOrientation = CockpitOrientation,
                SocketSizeMeters = new DVec3(1.0, 1.0, 1.0),
                Facing = MountFacing.Up,
                AllowedRotations = new HashSet<CockpitRotationStep>
                {
                    CockpitRotationStep.Deg0,
                    CockpitRotationStep.Deg90,
                    CockpitRotationStep.Deg180,
                    CockpitRotationStep.Deg270,
                },
                DefaultCockpitDefinitionId = CockpitDefinitionLibrary.CosmoC1SportCockpitId,
            },
        ],
        CockpitOffset = CockpitPosition,
        CockpitPose = new CockpitPoseDefinition(CockpitPosition, CockpitOrientation),

        Dimensions = new HullDimensions(
            LengthMeters: 8.0,
            WidthMeters: 3.1,
            HeightMeters: 2.1,
            StructuralHullWidthMeters: 3.1,
            StructuralHullHeightMeters: 2.1),
        PrimaryDesignBias = "Sport",
        SecondaryDesignBias = "No-cargo courier",
        CargoArrangement = new CargoArrangementDefinition(
            ContainerCapacity: 0,
            Arrangement: "no cargo bay",
            StackBoundsMeters: DVec3.Zero,
            DesignVolumeCenterMeters: DVec3.Zero,
            DesignVolumeBoundsMeters: DVec3.Zero,
            CargoDoorAssemblyId: "",
            RearOpeningBoundsMeters: DVec3.Zero,
            TransferAxis: DVec3.UnitZ),

        AerodynamicLift = 0.95,
        AerodynamicBrakeFront = 0.65,
        AerodynamicBrakeLateral = 1.45,

        Slots =
        [
            new() { SlotId = "reactor", Label = "Power Reactor", Category = SlotCategory.PowerReactor, MaxComponentClass = 1, Required = true },
            new() { SlotId = "power_bus", Label = "Power Bus", Category = SlotCategory.PowerBus, MaxComponentClass = 1, Required = true },
            new() { SlotId = EngineSlotId, Label = "Dorsal Engine", Category = SlotCategory.Engine, MaxComponentClass = 2, Required = true, DefaultComponentDefinitionId = NeedleEngineDefinitionFactory.H2VariantId },
            new() { SlotId = "shield", Label = "Shield", Category = SlotCategory.Shield, MaxComponentClass = 1, Required = false },
            new() { SlotId = "heat_sink", Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink, MaxComponentClass = 1, Required = true },
            new() { SlotId = "coolant", Label = "Coolant System", Category = SlotCategory.CoolantSystem, MaxComponentClass = 1, Required = false },
            new() { SlotId = "life_support", Label = "Life Support", Category = SlotCategory.LifeSupport, MaxComponentClass = 1, Required = true },
            new() { SlotId = "sensor", Label = "Utility Sensor", Category = SlotCategory.Sensor, MaxComponentClass = 1, Required = false },
            new() { SlotId = "exhaust", Label = "Exhaust System", Category = SlotCategory.Exhaust, MaxComponentClass = 2, Required = true },
            new() { SlotId = "internal_lights", Label = "Internal Lighting", Category = SlotCategory.InternalLights, MaxComponentClass = 1, Required = false },
            new() { SlotId = "external_lights", Label = "External Lighting", Category = SlotCategory.ExternalLights, MaxComponentClass = 1, Required = false },
            new() { SlotId = "flyability_mon", Label = "Flyability Monitor", Category = SlotCategory.FlyabilityMonitor, MaxComponentClass = 1, Required = true },
        ],

        VisualGeometry = BuildGeometry(),
    };

    private static SemanticHullGeometry BuildGeometry()
    {
        var vertices = new Dictionary<string, DVec3>(StringComparer.Ordinal);
        var faces = new List<SemanticHullFace>();
        string[] ringNames = ["front", "mid", "rear"];
        AddRing(vertices, ringNames[0], -4.0, topHalf: 0.72, sideHalf: 0.96, bottomHalf: 0.88, yTop: 0.72, yUpper: 0.38, yLower: -0.56, yBottom: -0.86);
        AddRing(vertices, ringNames[1], -1.45, topHalf: 1.26, sideHalf: 1.55, bottomHalf: 1.42, yTop: 1.02, yUpper: 0.54, yLower: -0.66, yBottom: -0.90);
        AddRing(vertices, ringNames[2], 4.0, topHalf: 1.08, sideHalf: 1.34, bottomHalf: 1.24, yTop: 0.96, yUpper: 0.50, yLower: -0.66, yBottom: -0.90);

        for (int section = 0; section < ringNames.Length - 1; section++)
        {
            for (int edge = 0; edge < 8; edge++)
            {
                string id = $"{HullId}.{SectionName(section)}.{EdgeName(edge)}.01";
                HullSurfaceRole role = edge == 0 && section == 1
                    ? HullSurfaceRole.EngineMount
                    : HullSurfaceRole.PanelSeat;
                AddSectionSurface(
                    faces,
                    vertices,
                    id,
                    VertexId(ringNames[section], edge),
                    VertexId(ringNames[section], (edge + 1) % 8),
                    VertexId(ringNames[section + 1], (edge + 1) % 8),
                    VertexId(ringNames[section + 1], edge),
                    role,
                    role == HullSurfaceRole.EngineMount
                        ? "engine-mount-structure"
                        : "panel-exterior",
                    EdgeNormal(edge),
                    role == HullSurfaceRole.PanelSeat ? id : null);
            }
        }

        AddFace(
            faces,
            vertices,
            $"{HullId}.front.armoured-nose.01",
            Enumerable.Range(0, 8).Select(i => VertexId("front", i)).ToArray(),
            HullSurfaceRole.PanelSeat,
            "panel-exterior",
            -DVec3.UnitZ,
            $"{HullId}.front.armoured-nose.01");
        AddFace(
            faces,
            vertices,
            $"{HullId}.rear.service-plate.01",
            Enumerable.Range(0, 8).Select(i => VertexId("rear", i)).ToArray(),
            HullSurfaceRole.PanelSeat,
            "panel-exterior",
            DVec3.UnitZ,
            $"{HullId}.rear.service-plate.01");

        AddBox(
            vertices,
            faces,
            $"{HullId}.dorsal.engine-strut",
            new DVec3(-0.20, 1.00, 1.40),
            new DVec3(0.20, 2.50, 2.18),
            HullSurfaceRole.EngineMount,
            "engine-mount-structure");

        return new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices = vertices.Select(pair => new SemanticHullVertex(pair.Key, pair.Value)).ToArray(),
            Faces = faces,
            AttachmentPorts =
            [
                new AttachmentPortDefinition(
                    $"{HullId}.dorsal.engine-root.01",
                    new DVec3(0.0, 3.35, 1.80),
                    DVec3.UnitY,
                    AttachmentCapability.Engine)
                {
                    Up = -DVec3.UnitX,
                    ComponentSlotId = EngineSlotId,
                    EngineMountStandardId = EngineMountStandardIds.H2,
                    EngineMountSide = EngineMountSide.Starboard,
                    MountRootPosition = new DVec3(-0.80, 1.85, 1.80),
                    AttachmentInterfacePosition = new DVec3(-0.80, 3.35, 1.80),
                    FootprintMeters = new DVec3(1.2, 0.9, 0.0),
                    ClearanceMinMeters = new DVec3(-1.0, 2.40, -1.60),
                    ClearanceMaxMeters = new DVec3(1.0, 4.10, 4.95),
                },
                new AttachmentPortDefinition(
                    $"{HullId}.underside.landing-foot.01",
                    new DVec3(-0.95, -0.98, -2.85),
                    -DVec3.UnitY,
                    AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.55, 0.42, 0.0),
                    ClearanceMinMeters = new DVec3(-1.25, -1.20, -3.10),
                    ClearanceMaxMeters = new DVec3(-0.65, -0.85, -2.60),
                },
                new AttachmentPortDefinition(
                    $"{HullId}.underside.landing-foot.02",
                    new DVec3(0.95, -0.98, -2.85),
                    -DVec3.UnitY,
                    AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.55, 0.42, 0.0),
                    ClearanceMinMeters = new DVec3(0.65, -1.20, -3.10),
                    ClearanceMaxMeters = new DVec3(1.25, -0.85, -2.60),
                },
                new AttachmentPortDefinition(
                    $"{HullId}.underside.landing-foot.03",
                    new DVec3(0.0, -0.98, 2.75),
                    -DVec3.UnitY,
                    AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.60, 0.45, 0.0),
                    ClearanceMinMeters = new DVec3(-0.35, -1.20, 2.45),
                    ClearanceMaxMeters = new DVec3(0.35, -0.85, 3.05),
                },
            ],
            MarkerLights =
            [
                new($"{HullId}.port.navigation-light.01", new DVec3(-1.25, 0.35, -3.35), -DVec3.UnitX, "red", 0.14, 0.8, "continuous"),
                new($"{HullId}.starboard.navigation-light.01", new DVec3(1.25, 0.35, -3.35), DVec3.UnitX, "green", 0.14, 0.8, "continuous"),
                new($"{HullId}.rear.position-light.01", new DVec3(0.0, 0.40, 4.05), DVec3.UnitZ, "white", 0.16, 0.75, "continuous"),
            ],
        };
    }

    private static void AddRing(
        Dictionary<string, DVec3> vertices,
        string ringName,
        double z,
        double topHalf,
        double sideHalf,
        double bottomHalf,
        double yTop,
        double yUpper,
        double yLower,
        double yBottom)
    {
        DVec3[] points =
        [
            new(-topHalf, yTop, z),
            new( topHalf, yTop, z),
            new( sideHalf, yUpper, z),
            new( sideHalf, yLower, z),
            new( bottomHalf, yBottom, z),
            new(-bottomHalf, yBottom, z),
            new(-sideHalf, yLower, z),
            new(-sideHalf, yUpper, z),
        ];
        for (int i = 0; i < points.Length; i++)
            vertices.Add(VertexId(ringName, i), points[i]);
    }

    private static void AddBox(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces,
        string prefix,
        DVec3 min,
        DVec3 max,
        HullSurfaceRole role,
        string material)
    {
        DVec3[] points =
        [
            new(min.X, min.Y, min.Z),
            new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z),
            new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z),
            new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z),
            new(min.X, max.Y, max.Z),
        ];
        for (int i = 0; i < points.Length; i++)
            vertices.Add($"{prefix}.vertex.{i + 1:00}", points[i]);

        var specs = new (string name, int[] indices, DVec3 normal)[]
        {
            ("front", [0, 3, 2, 1], -DVec3.UnitZ),
            ("rear", [4, 5, 6, 7], DVec3.UnitZ),
            ("port", [0, 4, 7, 3], -DVec3.UnitX),
            ("starboard", [1, 2, 6, 5], DVec3.UnitX),
            ("bottom", [0, 1, 5, 4], -DVec3.UnitY),
            ("top", [3, 7, 6, 2], DVec3.UnitY),
        };
        foreach ((string name, int[] indices, DVec3 normal) in specs)
        {
            AddFace(
                faces,
                vertices,
                $"{prefix}.{name}",
                indices.Select(index => $"{prefix}.vertex.{index + 1:00}").ToArray(),
                role,
                material,
                normal,
                panelSlotId: null,
                contributesToClosedHull: false);
        }
    }

    private static void AddSectionSurface(
        List<SemanticHullFace> faces,
        IReadOnlyDictionary<string, DVec3> vertices,
        string id,
        string a,
        string b,
        string c,
        string d,
        HullSurfaceRole role,
        string material,
        DVec3 desiredNormal,
        string? panelSlotId)
    {
        AddFace(
            faces,
            vertices,
            $"{id}.tri-a",
            [a, b, c],
            role,
            material,
            desiredNormal,
            panelSlotId is null ? null : $"{panelSlotId}.tri-a");
        AddFace(
            faces,
            vertices,
            $"{id}.tri-b",
            [a, c, d],
            role,
            material,
            desiredNormal,
            panelSlotId is null ? null : $"{panelSlotId}.tri-b");
    }

    private static void AddFace(
        List<SemanticHullFace> faces,
        IReadOnlyDictionary<string, DVec3> vertices,
        string id,
        string[] vertexIds,
        HullSurfaceRole role,
        string material,
        DVec3 desiredNormal,
        string? panelSlotId,
        bool contributesToClosedHull = true)
    {
        if (DVec3.Dot(
                ComputePolygonNormal(vertexIds.Select(vertexId => vertices[vertexId]).ToArray()),
                desiredNormal) < 0.0)
        {
            Array.Reverse(vertexIds);
        }

        DVec3 normal = ComputePolygonNormal(
            vertexIds.Select(vertexId => vertices[vertexId]).ToArray()).Normalized();
        faces.Add(new SemanticHullFace(id, vertexIds, role, material, normal, panelSlotId)
        {
            ContributesToClosedHull = contributesToClosedHull,
        });
    }

    private static DVec3 ComputePolygonNormal(IReadOnlyList<DVec3> positions)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        for (int i = 0; i < positions.Count; i++)
        {
            DVec3 current = positions[i];
            DVec3 next = positions[(i + 1) % positions.Count];
            x += (current.Y - next.Y) * (current.Z + next.Z);
            y += (current.Z - next.Z) * (current.X + next.X);
            z += (current.X - next.X) * (current.Y + next.Y);
        }
        return new DVec3(x, y, z);
    }

    private static DVec3 EdgeNormal(int edge) => edge switch
    {
        0 => DVec3.UnitY,
        1 => new DVec3(1.0, 1.0, 0.0).Normalized(),
        2 => DVec3.UnitX,
        3 => new DVec3(1.0, -1.0, 0.0).Normalized(),
        4 => -DVec3.UnitY,
        5 => new DVec3(-1.0, -1.0, 0.0).Normalized(),
        6 => -DVec3.UnitX,
        7 => new DVec3(-1.0, 1.0, 0.0).Normalized(),
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    private static string VertexId(string ringName, int index)
        => $"{HullId}.{ringName}.perimeter.{index + 1:00}";

    private static string SectionName(int section)
        => section == 0 ? "forward" : "aft";

    private static string EdgeName(int edge) => edge switch
    {
        0 => "top",
        1 => "starboard-upper",
        2 => "starboard-side",
        3 => "starboard-lower",
        4 => "underside",
        5 => "port-lower",
        6 => "port-side",
        7 => "port-upper",
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };
}
