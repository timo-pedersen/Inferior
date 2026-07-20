using Inferior.Core.Math;
using Inferior.Gameplay.Cockpit;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

public static class AsteriskHullDefinitionFactory
{
    public const string HullId = "asterisk";

    private const string CargoDoorId = $"{HullId}.front.cargo-door.01";
    private const string EngineSlotId = "engine.port.01";
    private static readonly DVec3 CockpitMountPosition = new(1.40, 0.15, -0.70);
    private static readonly Quaternion CockpitMountOrientation =
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathHelper.PiOver2);
    private static readonly Quaternion CockpitCameraOrientation =
        Quaternion.CreateFromYawPitchRoll(MathHelper.ToRadians(-30.0f), 0.0f, 0.0f);

    public static HullDefinition Create() => new()
    {
        HullTypeId = HullId,
        DisplayName = "Asterisk",
        SizeClass = ShipSizeClass.Small,
        HullMass = 12_000.0,
        CockpitMounts =
        [
            new CockpitMountDefinition
            {
                MountId = $"{HullId}.cockpit.starboard.01",
                MountClass = CockpitMountClass.C2,
                ShipLocalPosition = CockpitMountPosition,
                ShipLocalOrientation = CockpitMountOrientation,
                SocketSizeMeters = new DVec3(1.5, 1.5, 1.0),
                Facing = MountFacing.Starboard,
                AllowedRotations = new HashSet<CockpitRotationStep>
                {
                    CockpitRotationStep.Deg0,
                    CockpitRotationStep.Deg90,
                    CockpitRotationStep.Deg180,
                    CockpitRotationStep.Deg270,
                },
                DefaultCockpitDefinitionId =
                    CockpitDefinitionLibrary.AsteriskStarboardCockpitId,
            },
        ],
        CockpitOffset = new DVec3(1.82, 0.15, -1.30),
        CockpitPose = new CockpitPoseDefinition(
            new DVec3(1.82, 0.15, -1.30),
            CockpitCameraOrientation),

        Dimensions = new HullDimensions(
            LengthMeters: 8.6,
            WidthMeters: 5.7,
            HeightMeters: 3.5,
            StructuralHullWidthMeters: 2.8,
            StructuralHullHeightMeters: 3.2),
        PrimaryDesignBias = "Minimum-cost freight",
        SecondaryDesignBias = "Single-container utility",
        CargoArrangement = new CargoArrangementDefinition(
            ContainerCapacity: 1,
            Arrangement: "one standard container, longitudinal",
            StackBoundsMeters: new DVec3(2.5, 2.5, 6.0),
            DesignVolumeCenterMeters: new DVec3(0.0, 0.0, 0.25),
            DesignVolumeBoundsMeters: new DVec3(2.65, 2.65, 6.35),
            CargoDoorAssemblyId: CargoDoorId,
            RearOpeningBoundsMeters: new DVec3(2.35, 2.35, 0.0),
            TransferAxis: DVec3.UnitZ)
        {
            ContainerPlacements =
            [
                new CargoContainerPlacementDefinition(
                    $"{HullId}.cargo.01",
                    new DVec3(0.0, 0.0, 0.25),
                    new DVec3(2.5, 2.5, 6.0),
                    new SemanticBounds(
                        new DVec3(-1.25, -1.25, -2.75),
                        new DVec3(1.25, 1.25, 3.25))),
            ],
            LoadingClearanceBoundsMeters = new SemanticBounds(
                new DVec3(-1.25, -1.25, -5.30),
                new DVec3(1.25, 1.25, 3.25)),
        },

        AerodynamicLift = 0.18,
        AerodynamicBrakeFront = 0.85,
        AerodynamicBrakeLateral = 1.75,

        Slots =
        [
            new() { SlotId = "reactor", Label = "Power Reactor", Category = SlotCategory.PowerReactor, MaxComponentClass = 1, Required = true },
            new() { SlotId = "power_bus", Label = "Power Bus", Category = SlotCategory.PowerBus, MaxComponentClass = 1, Required = true },
            new() { SlotId = EngineSlotId, Label = "Port Engine", Category = SlotCategory.Engine, MaxComponentClass = 2, Required = true, DefaultComponentDefinitionId = MuleEngineDefinitionFactory.H2VariantId },
            new() { SlotId = "shield", Label = "Shield", Category = SlotCategory.Shield, MaxComponentClass = 1, Required = false },
            new() { SlotId = "heat_sink", Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink, MaxComponentClass = 1, Required = true },
            new() { SlotId = "coolant", Label = "Coolant System", Category = SlotCategory.CoolantSystem, MaxComponentClass = 1, Required = false },
            new() { SlotId = "life_support", Label = "Life Support", Category = SlotCategory.LifeSupport, MaxComponentClass = 1, Required = true },
            new() { SlotId = "sensor", Label = "Utility Sensor", Category = SlotCategory.Sensor, MaxComponentClass = 1, Required = false },
            new() { SlotId = "exhaust", Label = "Exhaust System", Category = SlotCategory.Exhaust, MaxComponentClass = 2, Required = true },
            new() { SlotId = "cargo.01", Label = "Cargo Bay", Category = SlotCategory.Cargo, MaxComponentClass = 1, Required = true },
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
        AddRing(vertices, "front", -4.30);
        AddRing(vertices, "rear", 4.30);

        var sideSpecs = new (string region, int edge, HullSurfaceRole role)[]
        {
            ("top", 0, HullSurfaceRole.PanelSeat),
            ("starboard-upper", 1, HullSurfaceRole.PanelSeat),
            ("starboard-cockpit-service", 2, HullSurfaceRole.ServiceSurface),
            ("starboard-lower", 3, HullSurfaceRole.PanelSeat),
            ("underside", 4, HullSurfaceRole.PanelSeat),
            ("port-lower", 5, HullSurfaceRole.PanelSeat),
            ("port-engine-root", 6, HullSurfaceRole.EngineMount),
            ("port-upper", 7, HullSurfaceRole.PanelSeat),
        };
        foreach ((string region, int edge, HullSurfaceRole role) in sideSpecs)
        {
            string[] ids =
            [
                RingVertexId("front", edge),
                RingVertexId("rear", edge),
                RingVertexId("rear", (edge + 1) % 8),
                RingVertexId("front", (edge + 1) % 8),
            ];
            string faceId = $"{HullId}.{region}.shell.01";
            AddFace(
                faces,
                vertices,
                faceId,
                ids,
                role,
                role == HullSurfaceRole.EngineMount
                    ? "engine-mount-structure"
                    : "panel-exterior",
                EdgeNormal(edge),
                role == HullSurfaceRole.PanelSeat ? faceId : null);
        }

        AddFace(
            faces,
            vertices,
            CargoDoorId,
            Enumerable.Range(0, 8).Select(i => RingVertexId("front", i)).ToArray(),
            HullSurfaceRole.CargoDoor,
            "cargo-door-structure",
            -DVec3.UnitZ,
            null,
            CargoDoorId);
        string rearFaceId = $"{HullId}.rear.service-wall.01";
        AddFace(
            faces,
            vertices,
            rearFaceId,
            Enumerable.Range(0, 8).Select(i => RingVertexId("rear", i)).ToArray(),
            HullSurfaceRole.PanelSeat,
            "panel-exterior",
            DVec3.UnitZ,
            rearFaceId);

        AddDoorDecoration(vertices, faces);
        AddBox(
            vertices,
            faces,
            $"{HullId}.port.engine-mount",
            new DVec3(-1.52, -0.68, 0.25),
            new DVec3(-1.22, 0.68, 2.05),
            HullSurfaceRole.EngineMount,
            "engine-mount-structure");
        AddBox(
            vertices,
            faces,
            $"{HullId}.starboard.cockpit-collar",
            new DVec3(1.22, -0.68, -1.62),
            new DVec3(1.50, 0.68, 0.25),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");
        AddBox(
            vertices,
            faces,
            $"{HullId}.top.rear-service-cover",
            new DVec3(-0.82, 1.42, 2.65),
            new DVec3(0.82, 1.74, 4.12),
            HullSurfaceRole.ExposedStructure,
            "structural-hull");

        return new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices = vertices
                .Select(pair => new SemanticHullVertex(pair.Key, pair.Value))
                .ToArray(),
            Faces = faces,
            Assemblies =
            [
                new SemanticAssemblyDefinition(CargoDoorId, "CargoDoor", CargoDoorId)
                {
                    ClosedPose = "Closed",
                    OpeningPolygonVertexIds = Enumerable.Range(0, 8)
                        .Select(i => RingVertexId("front", i))
                        .ToArray(),
                    MovementConcept = "Future forward-opening framed door",
                    MovementAxes = [-DVec3.UnitZ],
                    MovementClearanceVolumes =
                    [
                        new SemanticBounds(
                            new DVec3(-1.30, -1.30, -5.30),
                            new DVec3(1.30, 1.30, -4.25)),
                    ],
                },
            ],
            AttachmentPorts =
            [
                new AttachmentPortDefinition(
                    $"{HullId}.port.engine-root.01",
                    new DVec3(-2.25, 0.0, 1.10),
                    -DVec3.UnitX,
                    AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    ComponentSlotId = EngineSlotId,
                    EngineMountStandardId = EngineMountStandardIds.H2,
                    EngineMountSide = EngineMountSide.Port,
                    MountRootPosition = new DVec3(-1.25, 0.0, 1.10),
                    AttachmentInterfacePosition = new DVec3(-1.45, 0.0, 1.10),
                    FootprintMeters = new DVec3(1.35, 1.80, 0.0),
                    ClearanceMinMeters = new DVec3(-3.45, -1.10, -1.85),
                    ClearanceMaxMeters = new DVec3(-1.35, 1.10, 4.50),
                },
            ],
        };
    }

    private static void AddDoorDecoration(
        Dictionary<string, DVec3> vertices,
        List<SemanticHullFace> faces)
    {
        var strips = new (string id, double minX, double maxX, double minY, double maxY)[]
        {
            ($"{HullId}.front.cargo-door-frame.top.01", -1.18, 1.18, 1.08, 1.22),
            ($"{HullId}.front.cargo-door-frame.bottom.01", -1.18, 1.18, -1.22, -1.08),
            ($"{HullId}.front.cargo-door-frame.port.01", -1.22, -1.08, -1.08, 1.08),
            ($"{HullId}.front.cargo-door-frame.starboard.01", 1.08, 1.22, -1.08, 1.08),
            ($"{HullId}.front.cargo-door-lock.01", -0.07, 0.07, -0.90, 0.90),
        };
        foreach ((string id, double minX, double maxX, double minY, double maxY) in strips)
        {
            AddPlaneQuad(vertices, id, minX, maxX, minY, maxY, -4.335);
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

    private static void AddRing(
        Dictionary<string, DVec3> vertices,
        string ringName,
        double z)
    {
        DVec3[] points =
        [
            new(-1.05,  1.60, z),
            new( 1.05,  1.60, z),
            new( 1.40,  1.25, z),
            new( 1.40, -1.25, z),
            new( 1.05, -1.60, z),
            new(-1.05, -1.60, z),
            new(-1.40, -1.25, z),
            new(-1.40,  1.25, z),
        ];
        for (int i = 0; i < points.Length; i++)
            vertices.Add(RingVertexId(ringName, i), points[i]);
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

    private static string RingVertexId(string ringName, int index)
        => $"{HullId}.{ringName}.perimeter.{index + 1:00}";

    private static string PlaneVertexId(string faceId, int index)
        => $"{faceId}.corner.{index:00}";

    private static string[] PlaneVertexIds(string faceId)
        => Enumerable.Range(1, 4).Select(index => PlaneVertexId(faceId, index)).ToArray();
}
