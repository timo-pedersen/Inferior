using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

public static class AntegaHullDefinitionFactory
{
    public const string HullId = "antega";

    private const string CargoDoorId = $"{HullId}.forward.cargo-hatch.01";
    private static readonly DVec3 CockpitMountPosition = new(0.0, 6.0, 42.0);

    public static HullDefinition Create() => new()
    {
        HullTypeId = HullId,
        DisplayName = "Antega",
        SizeClass = ShipSizeClass.Large,
        HullMass = 3_200_000.0,
        CockpitMounts =
        [
            new CockpitMountDefinition
            {
                MountId = $"{HullId}.cockpit.dorsal-aft.01",
                MountClass = CockpitMountClass.C5,
                ShipLocalPosition = CockpitMountPosition,
                ShipLocalOrientation = Quaternion.Identity,
                SocketSizeMeters = new DVec3(4.0, 2.0, 6.0),
                Facing = MountFacing.Up,
                AllowedRotations = new HashSet<CockpitRotationStep>
                {
                    CockpitRotationStep.Deg0,
                },
                DefaultCockpitDefinitionId =
                    CockpitDefinitionLibrary.AntegaCivilianBridgeId,
            },
        ],
        CockpitOffset = new DVec3(0.0, 9.15, 38.05),
        CockpitPose = new CockpitPoseDefinition(
            new DVec3(0.0, 9.15, 38.05),
            Quaternion.CreateFromYawPitchRoll(
                0.0f,
                MathHelper.ToRadians(-5.0f),
                0.0f)),

        Dimensions = new HullDimensions(
            LengthMeters: 99.0,
            WidthMeters: 18.0,
            HeightMeters: 12.0,
            StructuralHullWidthMeters: 18.0,
            StructuralHullHeightMeters: 11.4),
        PrimaryDesignBias = "Mass container freight",
        SecondaryDesignBias = "Long-haul civilian economy",
        CargoArrangement = CreateCargoArrangement(),

        AerodynamicLift = 0.10,
        AerodynamicBrakeFront = 3.80,
        AerodynamicBrakeLateral = 8.50,

        Slots = CreateSlots(),
        VisualGeometry = BuildGeometry(),
    };

    private static CargoArrangementDefinition CreateCargoArrangement()
    {
        var placements = new List<CargoContainerPlacementDefinition>(120);
        double[] xPositions = [-5.0, -2.5, 0.0, 2.5, 5.0];
        double[] yPositions = [-1.75, 0.75];
        int index = 0;
        for (int foreAft = 0; foreAft < 12; foreAft++)
        {
            double z = -33.0 + foreAft * 6.0;
            foreach (double y in yPositions)
            {
                foreach (double x in xPositions)
                {
                    index++;
                    DVec3 center = new(x, y, z);
                    DVec3 bounds = new(2.5, 2.5, 6.0);
                    placements.Add(new CargoContainerPlacementDefinition(
                        $"{HullId}.cargo.{index:000}",
                        center,
                        bounds,
                        new SemanticBounds(center - bounds / 2.0, center + bounds / 2.0)));
                }
            }
        }

        return new CargoArrangementDefinition(
            ContainerCapacity: 120,
            Arrangement: "twelve fore/aft by five across by two high",
            StackBoundsMeters: new DVec3(12.5, 5.0, 72.0),
            DesignVolumeCenterMeters: new DVec3(0.0, -0.50, 0.0),
            DesignVolumeBoundsMeters: new DVec3(13.2, 5.6, 73.0),
            CargoDoorAssemblyId: CargoDoorId,
            RearOpeningBoundsMeters: new DVec3(13.6, 5.8, 0.0),
            TransferAxis: DVec3.UnitZ)
        {
            ContainerPlacements = placements,
            LoadingClearanceBoundsMeters = new SemanticBounds(
                new DVec3(-6.25, -3.0, -50.2),
                new DVec3(6.25, 2.0, 36.0)),
        };
    }

    private static IReadOnlyList<HullSlot> CreateSlots() =>
    [
        new() { SlotId = "reactor", Label = "Power Reactor", Category = SlotCategory.PowerReactor, MaxComponentClass = 8, Required = true },
        new() { SlotId = "power_bus", Label = "Power Bus", Category = SlotCategory.PowerBus, MaxComponentClass = 8, Required = true },
        EngineSlot("engine.port.upper.01", "Port Upper Atlas"),
        EngineSlot("engine.port.lower.01", "Port Lower Atlas"),
        EngineSlot("engine.starboard.upper.01", "Starboard Upper Atlas"),
        EngineSlot("engine.starboard.lower.01", "Starboard Lower Atlas"),
        new() { SlotId = "shield_top", Label = "Top Shield", Category = SlotCategory.Shield, MaxComponentClass = 8, Required = false },
        new() { SlotId = "shield_bottom", Label = "Bottom Shield", Category = SlotCategory.Shield, MaxComponentClass = 8, Required = false },
        new() { SlotId = "heat_sink", Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink, MaxComponentClass = 8, Required = true },
        new() { SlotId = "coolant", Label = "Coolant System", Category = SlotCategory.CoolantSystem, MaxComponentClass = 8, Required = true },
        new() { SlotId = "life_support", Label = "Bridge Life Support", Category = SlotCategory.LifeSupport, MaxComponentClass = 6, Required = true },
        new() { SlotId = "sensor", Label = "Long-Haul Navigation Sensors", Category = SlotCategory.Sensor, MaxComponentClass = 6, Required = false },
        new() { SlotId = "exhaust", Label = "Exhaust System", Category = SlotCategory.Exhaust, MaxComponentClass = 8, Required = true },
        new() { SlotId = "cargo", Label = "120-Container Cargo Bay", Category = SlotCategory.Cargo, MaxComponentClass = 8, Required = true },
        new() { SlotId = "internal_lights", Label = "Internal Lighting", Category = SlotCategory.InternalLights, MaxComponentClass = 6, Required = false },
        new() { SlotId = "external_lights", Label = "External Lighting", Category = SlotCategory.ExternalLights, MaxComponentClass = 6, Required = false },
        new() { SlotId = "flyability_mon", Label = "Flyability Monitor", Category = SlotCategory.FlyabilityMonitor, MaxComponentClass = 8, Required = true },
    ];

    private static HullSlot EngineSlot(string id, string label) => new()
    {
        SlotId = id,
        Label = label,
        Category = SlotCategory.Engine,
        MaxComponentClass = 8,
        Required = true,
        DefaultComponentDefinitionId = AtlasEngineDefinitionFactory.H10VariantId,
    };

    private static SemanticHullGeometry BuildGeometry()
    {
        var vertices = new Dictionary<string, DVec3>(StringComparer.Ordinal);
        var faces = new List<SemanticHullFace>();

        AddClosedCargoBody(vertices, faces);
        AddBox(
            vertices,
            faces,
            $"{HullId}.aft.lower-step",
            new DVec3(-7.4, -3.9, 35.0),
            new DVec3(7.4, 4.8, 49.5),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.dorsal.service-spine",
            new DVec3(-2.2, 5.65, -34.0),
            new DVec3(2.2, 6.0, 37.0),
            HullSurfaceRole.ServiceSurface,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.bridge.armoured-connection",
            new DVec3(-6.6, 4.7, 35.5),
            new DVec3(6.6, 6.05, 48.0),
            HullSurfaceRole.ExposedStructure,
            "bridge-armour");

        AddForwardCargoHatch(vertices, faces);
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
                    MovementConcept = "Future segmented hatch retracting into bow shoulders",
                    MovementAxes = [-DVec3.UnitX, DVec3.UnitX, -DVec3.UnitY, DVec3.UnitY],
                    MovementClearanceVolumes =
                    [
                        new SemanticBounds(
                            new DVec3(-9.0, -3.4, -50.0),
                            new DVec3(-6.2, 2.4, -49.0)),
                        new SemanticBounds(
                            new DVec3(6.2, -3.4, -50.0),
                            new DVec3(9.0, 2.4, -49.0)),
                    ],
                },
            ],
            AttachmentPorts = CreateEnginePorts(),
        };
    }

    private static void AddClosedCargoBody(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        (double x, double y)[] section =
        [
            (-7.6, -5.7),
            ( 7.6, -5.7),
            ( 9.0, -4.3),
            ( 9.0,  4.3),
            ( 7.6,  5.7),
            (-7.6,  5.7),
            (-9.0,  4.3),
            (-9.0, -4.3),
        ];
        const double frontZ = -49.5;
        const double rearZ = 40.0;
        for (int i = 0; i < section.Length; i++)
        {
            vertices.Add(BodyVertexId("front", i), new DVec3(section[i].x, section[i].y, frontZ));
            vertices.Add(BodyVertexId("rear", i), new DVec3(section[i].x, section[i].y, rearZ));
        }

        string frontId = $"{HullId}.cargo-body.front";
        AddFace(
            faces,
            vertices,
            frontId,
            Enumerable.Range(0, section.Length).Select(i => BodyVertexId("front", i)).ToArray(),
            HullSurfaceRole.ExposedStructure,
            "structural-hull",
            -DVec3.UnitZ,
            null);
        string rearId = $"{HullId}.cargo-body.rear";
        AddFace(
            faces,
            vertices,
            rearId,
            Enumerable.Range(0, section.Length).Select(i => BodyVertexId("rear", i)).ToArray(),
            HullSurfaceRole.ExposedStructure,
            "structural-hull",
            DVec3.UnitZ,
            null);

        for (int i = 0; i < section.Length; i++)
        {
            int next = (i + 1) % section.Length;
            string faceId = $"{HullId}.cargo-body.side.{i + 1:00}";
            AddFace(
                faces,
                vertices,
                faceId,
                [
                    BodyVertexId("front", i),
                    BodyVertexId("rear", i),
                    BodyVertexId("rear", next),
                    BodyVertexId("front", next),
                ],
                i is 3 or 4 ? HullSurfaceRole.ServiceSurface : HullSurfaceRole.PanelSeat,
                i is 3 or 4 ? "service-spine" : "panel-exterior",
                new DVec3(
                    section[i].x + section[next].x,
                    section[i].y + section[next].y,
                    0.0),
                i is 3 or 4 ? null : faceId);
        }
    }

    private static void AddForwardCargoHatch(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        AddPlaneQuad(vertices, CargoDoorId, -6.80, 6.80, -3.20, 2.60, -49.56);
        AddFace(
            faces,
            vertices,
            CargoDoorId,
            PlaneVertexIds(CargoDoorId),
            HullSurfaceRole.CargoDoor,
            "cargo-door-structure",
            -DVec3.UnitZ,
            null,
            CargoDoorId,
            contributesToClosedHull: false);

        for (int row = 0; row < 2; row++)
        {
            double minY = row == 0 ? -3.02 : -0.18;
            double maxY = row == 0 ? -0.32 : 2.42;
            for (int column = 0; column < 5; column++)
            {
                double minX = -6.62 + column * 2.65;
                double maxX = minX + 2.48;
                string id = $"{CargoDoorId}.segment.{row + 1}.{column + 1}";
                AddPlaneQuad(vertices, id, minX, maxX, minY, maxY, -49.64);
                AddFace(
                    faces,
                    vertices,
                    id,
                    PlaneVertexIds(id),
                    HullSurfaceRole.CargoDoor,
                    "cargo-door-panel",
                    -DVec3.UnitZ,
                    null,
                    CargoDoorId,
                    contributesToClosedHull: false);
            }
        }

        var frame = new List<(string id, double minX, double maxX, double minY, double maxY)>
        {
            ($"{CargoDoorId}.frame.top", -7.15, 7.15, 2.55, 2.95),
            ($"{CargoDoorId}.frame.bottom", -7.15, 7.15, -3.55, -3.15),
            ($"{CargoDoorId}.frame.port", -7.20, -6.80, -3.20, 2.60),
            ($"{CargoDoorId}.frame.starboard", 6.80, 7.20, -3.20, 2.60),
            ($"{CargoDoorId}.frame.mid", -6.80, 6.80, -0.36, -0.14),
        };
        for (int column = 1; column < 5; column++)
        {
            double x = -6.8 + column * 2.72;
            frame.Add(($"{CargoDoorId}.frame.vertical.{column}", x - 0.11, x + 0.11, -3.20, 2.60));
        }

        foreach ((string id, double minX, double maxX, double minY, double maxY) in frame)
        {
            AddPlaneQuad(vertices, id, minX, maxX, minY, maxY, -49.72);
            AddFace(
                faces,
                vertices,
                id,
                PlaneVertexIds(id),
                HullSurfaceRole.ExposedStructure,
                "cargo-door-frame",
                -DVec3.UnitZ,
                null,
                CargoDoorId,
                contributesToClosedHull: false);
        }
    }

    private static void AddEngineSupports(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        foreach ((string sideName, double side) in new[] { ("port", -1.0), ("starboard", 1.0) })
        {
            double innerX = side < 0.0 ? -11.85 : 8.55;
            double outerX = side < 0.0 ? -8.55 : 11.85;
            foreach ((string level, double y) in new[] { ("upper", 3.20), ("lower", -4.10) })
            {
                foreach ((string region, double z) in new[] { ("forward", -8.0), ("aft", 27.0) })
                {
                    AddBox(
                        vertices,
                        faces,
                        $"{HullId}.{sideName}.{level}.engine-support.{region}",
                        new DVec3(innerX, y - 1.15, z - 2.8),
                        new DVec3(outerX, y + 1.15, z + 2.8),
                        HullSurfaceRole.EngineMount,
                        "engine-mount-structure");
                }
                AddBox(
                    vertices,
                    faces,
                    $"{HullId}.{sideName}.{level}.engine-support.torque-beam",
                    new DVec3(
                        side < 0.0 ? -10.20 : 8.55,
                        y - 0.48,
                        -5.2),
                    new DVec3(
                        side < 0.0 ? -8.55 : 10.20,
                        y + 0.48,
                        24.2),
                    HullSurfaceRole.EngineMount,
                    "engine-mount-structure");
            }
        }
    }

    private static IReadOnlyList<AttachmentPortDefinition> CreateEnginePorts()
    {
        var ports = new List<AttachmentPortDefinition>(4);
        foreach ((string sideName, EngineMountSide side, double x, double rootX, double interfaceX, DVec3 normal)
                 in new[]
                 {
                     ("port", EngineMountSide.Port, -14.40, -8.70, -11.90, -DVec3.UnitX),
                     ("starboard", EngineMountSide.Starboard, 14.40, 8.70, 11.90, DVec3.UnitX),
                 })
        {
            foreach ((string level, double y) in new[] { ("upper", 3.20), ("lower", -4.10) })
            {
                string slotId = $"engine.{sideName}.{level}.01";
                ports.Add(new AttachmentPortDefinition(
                    $"{HullId}.{sideName}.{level}.engine-root.01",
                    new DVec3(x, y, 9.0),
                    normal,
                    AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    ComponentSlotId = slotId,
                    EngineMountStandardId = EngineMountStandardIds.H10,
                    EngineMountSide = side,
                    MountRootPosition = new DVec3(rootX, y, 9.0),
                    AttachmentInterfacePosition = new DVec3(interfaceX, y, 9.0),
                    FootprintMeters = new DVec3(5.8, 8.0, 0.0),
                    ClearanceMinMeters = new DVec3(
                        side == EngineMountSide.Port ? -17.1 : 11.45,
                        y - 2.75,
                        -20.5),
                    ClearanceMaxMeters = new DVec3(
                        side == EngineMountSide.Port ? -11.45 : 17.1,
                        y + 2.75,
                        38.5),
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

    private static string BodyVertexId(string ring, int index)
        => $"{HullId}.cargo-body.{ring}.{index + 1:00}";

    private static string PlaneVertexId(string faceId, int index)
        => $"{faceId}.corner.{index:00}";

    private static string[] PlaneVertexIds(string faceId)
        => Enumerable.Range(1, 4).Select(index => PlaneVertexId(faceId, index)).ToArray();
}
