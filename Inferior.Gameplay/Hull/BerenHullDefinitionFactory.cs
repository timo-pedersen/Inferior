using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

public static class BerenHullDefinitionFactory
{
    public const string HullId = "beren";

    private const string CargoDoorId = $"{HullId}.aft.cargo-door.01";
    private static readonly DVec3 CockpitMountPosition = new(0.0, -1.25, -10.90);
    private static readonly Quaternion CockpitMountOrientation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.Pi);

    public static HullDefinition Create() => new()
    {
        HullTypeId = HullId,
        DisplayName = "Beren",
        SizeClass = ShipSizeClass.Medium,
        HullMass = 180_000.0,
        CockpitMounts =
        [
            new CockpitMountDefinition
            {
                MountId = $"{HullId}.cockpit.underslung.01",
                MountClass = CockpitMountClass.C2,
                ShipLocalPosition = CockpitMountPosition,
                ShipLocalOrientation = CockpitMountOrientation,
                SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
                Facing = MountFacing.Down,
                AllowedRotations = new HashSet<CockpitRotationStep>
                {
                    CockpitRotationStep.Deg0,
                    CockpitRotationStep.Deg90,
                    CockpitRotationStep.Deg180,
                    CockpitRotationStep.Deg270,
                },
                DefaultCockpitDefinitionId =
                    CockpitDefinitionLibrary.BerenUnderslungCockpitId,
            },
        ],
        CockpitOffset = new DVec3(0.0, -2.80, -11.50),
        CockpitPose = new CockpitPoseDefinition(
            new DVec3(0.0, -2.80, -11.50),
            Quaternion.CreateFromYawPitchRoll(
                0.0f,
                MathHelper.ToRadians(-10.0f),
                0.0f)),

        Dimensions = new HullDimensions(
            LengthMeters: 27.0,
            WidthMeters: 20.0,
            HeightMeters: 6.2,
            StructuralHullWidthMeters: 19.0,
            StructuralHullHeightMeters: 2.5),
        PrimaryDesignBias = "Medium freight",
        SecondaryDesignBias = "Underslung modular utility",
        CargoArrangement = CreateCargoArrangement(),

        AerodynamicLift = 0.52,
        AerodynamicBrakeFront = 1.20,
        AerodynamicBrakeLateral = 3.10,

        Slots = CreateSlots(),
        VisualGeometry = BuildGeometry(),
    };

    private static CargoArrangementDefinition CreateCargoArrangement()
    {
        var placements = new List<CargoContainerPlacementDefinition>();
        int index = 0;
        double[] xPositions = [-2.5, 0.0, 2.5];
        double[] zPositions = [-6.0, 0.0, 6.0];
        foreach (double z in zPositions)
        {
            foreach (double x in xPositions)
            {
                index++;
                DVec3 center = new(x, -2.80, z);
                placements.Add(new CargoContainerPlacementDefinition(
                    $"{HullId}.cargo.{index:00}",
                    center,
                    new DVec3(2.5, 2.5, 6.0),
                    new SemanticBounds(
                        center - new DVec3(1.25, 1.25, 3.0),
                        center + new DVec3(1.25, 1.25, 3.0))));
            }
        }

        return new CargoArrangementDefinition(
            ContainerCapacity: 9,
            Arrangement: "three across by three fore/aft by one layer",
            StackBoundsMeters: new DVec3(7.5, 2.5, 18.0),
            DesignVolumeCenterMeters: new DVec3(0.0, -2.80, 0.0),
            DesignVolumeBoundsMeters: new DVec3(7.9, 2.9, 18.4),
            CargoDoorAssemblyId: CargoDoorId,
            RearOpeningBoundsMeters: new DVec3(7.4, 2.8, 0.0),
            TransferAxis: DVec3.UnitZ)
        {
            ContainerPlacements = placements,
            LoadingClearanceBoundsMeters = new SemanticBounds(
                new DVec3(-3.75, -4.05, -9.0),
                new DVec3(3.75, -1.55, 13.6)),
        };
    }

    private static IReadOnlyList<HullSlot> CreateSlots() =>
    [
        new() { SlotId = "reactor", Label = "Power Reactor", Category = SlotCategory.PowerReactor, MaxComponentClass = 4, Required = true },
        new() { SlotId = "power_bus", Label = "Power Bus", Category = SlotCategory.PowerBus, MaxComponentClass = 4, Required = true },
        EngineSlot("engine.port.upper.01", "Port Upper Engine"),
        EngineSlot("engine.port.lower.01", "Port Lower Engine"),
        EngineSlot("engine.starboard.upper.01", "Starboard Upper Engine"),
        EngineSlot("engine.starboard.lower.01", "Starboard Lower Engine"),
        new() { SlotId = "shield_top", Label = "Top Shield", Category = SlotCategory.Shield, MaxComponentClass = 4, Required = false },
        new() { SlotId = "shield_bottom", Label = "Bottom Shield", Category = SlotCategory.Shield, MaxComponentClass = 4, Required = false },
        new() { SlotId = "heat_sink", Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink, MaxComponentClass = 4, Required = true },
        new() { SlotId = "coolant", Label = "Coolant System", Category = SlotCategory.CoolantSystem, MaxComponentClass = 4, Required = false },
        new() { SlotId = "life_support", Label = "Life Support", Category = SlotCategory.LifeSupport, MaxComponentClass = 3, Required = true },
        new() { SlotId = "sensor", Label = "Navigation Sensors", Category = SlotCategory.Sensor, MaxComponentClass = 3, Required = false },
        new() { SlotId = "exhaust", Label = "Exhaust System", Category = SlotCategory.Exhaust, MaxComponentClass = 4, Required = true },
        new() { SlotId = "cargo", Label = "Nine-Container Cargo Bay", Category = SlotCategory.Cargo, MaxComponentClass = 4, Required = true },
        new() { SlotId = "internal_lights", Label = "Internal Lighting", Category = SlotCategory.InternalLights, MaxComponentClass = 3, Required = false },
        new() { SlotId = "external_lights", Label = "External Lighting", Category = SlotCategory.ExternalLights, MaxComponentClass = 3, Required = false },
        new() { SlotId = "flyability_mon", Label = "Flyability Monitor", Category = SlotCategory.FlyabilityMonitor, MaxComponentClass = 4, Required = true },
    ];

    private static HullSlot EngineSlot(string id, string label) => new()
    {
        SlotId = id,
        Label = label,
        Category = SlotCategory.Engine,
        MaxComponentClass = 4,
        Required = true,
        DefaultComponentDefinitionId = NeedleEngineDefinitionFactory.H2VariantId,
    };

    private static SemanticHullGeometry BuildGeometry()
    {
        var vertices = new Dictionary<string, DVec3>(StringComparer.Ordinal);
        var faces = new List<SemanticHullFace>();
        AddUpperPlatform(vertices, faces);

        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.cargo-bay",
            new DVec3(-4.10, -4.35, -9.35),
            new DVec3(4.10, -1.10, 13.15),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.port-bay-beam",
            new DVec3(-4.48, -4.48, -9.55),
            new DVec3(-4.02, -0.95, 12.75),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.starboard-bay-beam",
            new DVec3(4.02, -4.48, -9.55),
            new DVec3(4.48, -0.95, 12.75),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.cockpit-collar",
            new DVec3(-1.05, -1.62, -11.85),
            new DVec3(1.05, -0.92, -9.65),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.port-service",
            new DVec3(-6.20, -3.25, -7.60),
            new DVec3(-4.45, -1.05, -1.10),
            HullSurfaceRole.ServiceSurface,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.underside.starboard-service",
            new DVec3(4.45, -3.25, -7.60),
            new DVec3(6.20, -1.05, -1.10),
            HullSurfaceRole.ServiceSurface,
            "structural-hull");

        AddCargoDoor(vertices, faces);
        AddEngineSupports(vertices, faces);

        return new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices = vertices.Select(pair =>
                new SemanticHullVertex(pair.Key, pair.Value)).ToArray(),
            Faces = faces,
            Assemblies =
            [
                new SemanticAssemblyDefinition(CargoDoorId, "CargoDoor", CargoDoorId)
                {
                    ClosedPose = "Closed",
                    OpeningPolygonVertexIds = PlaneVertexIds(CargoDoorId),
                    MovementConcept = "Future split door retracting laterally",
                    MovementAxes = [-DVec3.UnitX, DVec3.UnitX],
                    MovementClearanceVolumes =
                    [
                        new SemanticBounds(
                            new DVec3(-7.2, -4.25, 12.95),
                            new DVec3(-3.65, -1.25, 13.55)),
                        new SemanticBounds(
                            new DVec3(3.65, -4.25, 12.95),
                            new DVec3(7.2, -1.25, 13.55)),
                    ],
                },
            ],
            AttachmentPorts = CreateEnginePorts(),
        };
    }

    private static void AddUpperPlatform(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        (double x, double z)[] plan =
        [
            (0.0, -13.5),
            (6.8, -10.8),
            (9.5, -3.0),
            (8.5, 7.5),
            (5.8, 13.5),
            (-5.8, 13.5),
            (-8.5, 7.5),
            (-9.5, -3.0),
            (-6.8, -10.8),
        ];
        for (int i = 0; i < plan.Length; i++)
        {
            vertices.Add(
                PlatformVertexId("top", i),
                new DVec3(plan[i].x, 1.25, plan[i].z));
            vertices.Add(
                PlatformVertexId("bottom", i),
                new DVec3(plan[i].x, -1.25, plan[i].z));
        }

        string topId = $"{HullId}.top.platform.01";
        AddFace(
            faces,
            vertices,
            topId,
            Enumerable.Range(0, plan.Length)
                .Select(i => PlatformVertexId("top", i))
                .ToArray(),
            HullSurfaceRole.PanelSeat,
            "panel-exterior",
            DVec3.UnitY,
            topId);
        string bottomId = $"{HullId}.underside.platform.01";
        AddFace(
            faces,
            vertices,
            bottomId,
            Enumerable.Range(0, plan.Length)
                .Select(i => PlatformVertexId("bottom", i))
                .ToArray(),
            HullSurfaceRole.PanelSeat,
            "panel-exterior",
            -DVec3.UnitY,
            bottomId);

        for (int i = 0; i < plan.Length; i++)
        {
            int next = (i + 1) % plan.Length;
            string faceId = $"{HullId}.edge.platform.{i + 1:00}";
            DVec3 outward = new(
                plan[next].z - plan[i].z,
                0.0,
                plan[i].x - plan[next].x);
            AddFace(
                faces,
                vertices,
                faceId,
                [
                    PlatformVertexId("top", i),
                    PlatformVertexId("bottom", i),
                    PlatformVertexId("bottom", next),
                    PlatformVertexId("top", next),
                ],
                HullSurfaceRole.PanelSeat,
                "panel-exterior",
                outward,
                faceId);
        }
    }

    private static void AddCargoDoor(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        AddPlaneQuad(vertices, CargoDoorId, -3.70, 3.70, -4.10, -1.35, 13.20);
        AddFace(
            faces,
            vertices,
            CargoDoorId,
            PlaneVertexIds(CargoDoorId),
            HullSurfaceRole.CargoDoor,
            "cargo-door-structure",
            DVec3.UnitZ,
            null,
            CargoDoorId,
            contributesToClosedHull: false);

        var frame = new (string id, double minX, double maxX, double minY, double maxY)[]
        {
            ($"{CargoDoorId}.frame.top", -3.92, 3.92, -1.34, -1.12),
            ($"{CargoDoorId}.frame.bottom", -3.92, 3.92, -4.34, -4.12),
            ($"{CargoDoorId}.frame.port", -3.94, -3.72, -4.12, -1.34),
            ($"{CargoDoorId}.frame.starboard", 3.72, 3.94, -4.12, -1.34),
            ($"{CargoDoorId}.lock-spine", -0.10, 0.10, -3.90, -1.55),
        };
        foreach ((string id, double minX, double maxX, double minY, double maxY) in frame)
        {
            AddPlaneQuad(vertices, id, minX, maxX, minY, maxY, 13.235);
            AddFace(
                faces,
                vertices,
                id,
                PlaneVertexIds(id),
                HullSurfaceRole.ExposedStructure,
                "cargo-door-frame",
                DVec3.UnitZ,
                null,
                CargoDoorId,
                contributesToClosedHull: false);
        }
    }

    private static void AddEngineSupports(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        foreach (double side in new[] { -1.0, 1.0 })
        {
            string sideName = side < 0.0 ? "port" : "starboard";
            double minX = side < 0.0 ? -8.25 : 7.72;
            double maxX = side < 0.0 ? -7.72 : 8.25;
            AddBox(
                vertices,
                faces,
                $"{HullId}.{sideName}.engine-pod-spine",
                new DVec3(minX, -3.45, 4.45),
                new DVec3(maxX, 0.35, 10.35),
                HullSurfaceRole.EngineMount,
                "engine-mount-structure");

            foreach ((string level, double y) in new[] { ("upper", -0.45), ("lower", -2.65) })
            {
                AddBox(
                    vertices,
                    faces,
                    $"{HullId}.{sideName}.{level}.engine-collar",
                    new DVec3(minX - (side < 0.0 ? 0.18 : 0.0), y - 0.62, 6.35),
                    new DVec3(maxX + (side > 0.0 ? 0.18 : 0.0), y + 0.62, 8.65),
                    HullSurfaceRole.EngineMount,
                    "engine-mount-structure");
            }
        }
    }

    private static IReadOnlyList<AttachmentPortDefinition> CreateEnginePorts()
    {
        var ports = new List<AttachmentPortDefinition>();
        foreach ((string sideName, EngineMountSide side, double x, double rootX, double interfaceX, DVec3 normal)
                 in new[]
                 {
                     ("port", EngineMountSide.Port, -9.0, -7.80, -8.20, -DVec3.UnitX),
                     ("starboard", EngineMountSide.Starboard, 9.0, 7.80, 8.20, DVec3.UnitX),
                 })
        {
            foreach ((string level, double y) in new[] { ("upper", -0.45), ("lower", -2.65) })
            {
                string slotId = $"engine.{sideName}.{level}.01";
                ports.Add(new AttachmentPortDefinition(
                    $"{HullId}.{sideName}.{level}.engine-root.01",
                    new DVec3(x, y, 7.50),
                    normal,
                    AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    ComponentSlotId = slotId,
                    EngineMountStandardId = EngineMountStandardIds.H2,
                    EngineMountSide = side,
                    MountRootPosition = new DVec3(rootX, y, 7.50),
                    AttachmentInterfacePosition = new DVec3(interfaceX, y, 7.50),
                    FootprintMeters = new DVec3(1.25, 2.30, 0.0),
                    ClearanceMinMeters = new DVec3(
                        side == EngineMountSide.Port ? -9.85 : 8.15,
                        y - 0.85,
                        4.20),
                    ClearanceMaxMeters = new DVec3(
                        side == EngineMountSide.Port ? -8.15 : 9.85,
                        y + 0.85,
                        11.25),
                });
            }
        }
        return ports;
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
                null,
                contributesToClosedHull: false);
        }
    }

    private static void AddPlaneQuad(
        Dictionary<string, DVec3> vertices,
        string faceId,
        double minX,
        double maxX,
        double minY,
        double maxY,
        double z)
    {
        vertices.Add(PlaneVertexId(faceId, 1), new DVec3(minX, minY, z));
        vertices.Add(PlaneVertexId(faceId, 2), new DVec3(maxX, minY, z));
        vertices.Add(PlaneVertexId(faceId, 3), new DVec3(maxX, maxY, z));
        vertices.Add(PlaneVertexId(faceId, 4), new DVec3(minX, maxY, z));
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
        string? assemblyId = null,
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
        faces.Add(new SemanticHullFace(
            id,
            vertexIds,
            role,
            material,
            normal,
            panelSlotId,
            assemblyId)
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

    private static string PlatformVertexId(string layer, int index)
        => $"{HullId}.platform.{layer}.{index + 1:00}";

    private static string PlaneVertexId(string faceId, int index)
        => $"{faceId}.corner.{index:00}";

    private static string[] PlaneVertexIds(string faceId)
        => Enumerable.Range(1, 4).Select(index => PlaneVertexId(faceId, index)).ToArray();
}
