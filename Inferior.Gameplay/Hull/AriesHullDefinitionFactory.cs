using Inferior.Core.Math;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Hull;

public static class AriesHullDefinitionFactory
{
    private const string HullId = "type-1";
    private const string CargoDoorId = $"{HullId}.rear.cargo-door.01";
    private const string CargoDoorPortPanelSeatId = $"{HullId}.rear.cargo-door.port.01";
    private const string CargoDoorStarboardPanelSeatId = $"{HullId}.rear.cargo-door.starboard.01";
    private static readonly DVec3 CockpitPosition = new(-1.25, 1.55, -5.9);
    private static readonly Quaternion CockpitOrientation = Quaternion.CreateFromYawPitchRoll(
        MathHelper.ToRadians(-3.0f),
        0.0f,
        0.0f);

    public static HullDefinition Create() => new()
    {
        HullTypeId   = HullId,
        DisplayName  = "Aries",
        SizeClass    = ShipSizeClass.Small,
        HullMass     = 72_000.0,
        CockpitOffset = CockpitPosition,
        CockpitPose = new CockpitPoseDefinition(CockpitPosition, CockpitOrientation),

        Dimensions = new HullDimensions(
            LengthMeters: 16.0,
            WidthMeters: 10.0,
            HeightMeters: 5.0,
            StructuralHullWidthMeters: 7.0,
            StructuralHullHeightMeters: 5.0),
        PrimaryDesignBias = "Utility",
        SecondaryDesignBias = "Light freight",
        CargoArrangement = new CargoArrangementDefinition(
            ContainerCapacity: 2,
            Arrangement: "two standard containers side by side",
            StackBoundsMeters: new DVec3(5.0, 2.5, 6.0),
            DesignVolumeCenterMeters: new DVec3(0.0, -0.05, 3.8),
            DesignVolumeBoundsMeters: new DVec3(6.0, 3.2, 7.2),
            CargoDoorAssemblyId: CargoDoorId,
            RearOpeningBoundsMeters: new DVec3(6.1, 3.25, 0.0),
            TransferAxis: DVec3.UnitZ)
        {
            ContainerPlacements =
            [
                CreateCargoContainerPlacement($"{HullId}.cargo.port.01", new DVec3(-1.25, -0.05, 3.8)),
                CreateCargoContainerPlacement($"{HullId}.cargo.starboard.01", new DVec3(1.25, -0.05, 3.8)),
            ],
            LoadingClearanceBoundsMeters = new SemanticBounds(
                new DVec3(-3.0, -1.65, 0.2),
                new DVec3(3.0, 1.55, 8.6)),
        },

        AerodynamicLift         = 0.65,
        AerodynamicBrakeFront   = 0.95,
        AerodynamicBrakeLateral = 2.20,

        Slots =
        [
            new() { SlotId = "reactor",             Label = "Power Reactor",        Category = SlotCategory.PowerReactor,     MaxComponentClass = 2, Required = true  },
            new() { SlotId = "power_bus",           Label = "Power Bus",            Category = SlotCategory.PowerBus,         MaxComponentClass = 2, Required = true  },
            new() { SlotId = "engine.port.01",      Label = "Port Engine",          Category = SlotCategory.Engine,           MaxComponentClass = 2, Required = true, DefaultComponentDefinitionId = MuleEngineDefinitionFactory.H2VariantId },
            new() { SlotId = "engine.starboard.01", Label = "Starboard Engine",     Category = SlotCategory.Engine,           MaxComponentClass = 2, Required = true, DefaultComponentDefinitionId = MuleEngineDefinitionFactory.H2VariantId },
            new() { SlotId = "shield_top",          Label = "Top Shield",           Category = SlotCategory.Shield,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "shield_bottom",       Label = "Bottom Shield",        Category = SlotCategory.Shield,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "heat_sink",           Label = "Hyperspace Heat Sink", Category = SlotCategory.HeatSink,         MaxComponentClass = 2, Required = true  },
            new() { SlotId = "coolant",             Label = "Coolant System",       Category = SlotCategory.CoolantSystem,    MaxComponentClass = 2, Required = false },
            new() { SlotId = "life_support",        Label = "Life Support",         Category = SlotCategory.LifeSupport,      MaxComponentClass = 2, Required = true  },
            new() { SlotId = "sensor_gravity",      Label = "Gravity Sensor",       Category = SlotCategory.Sensor,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "sensor_radiation",    Label = "Radiation Sensor",     Category = SlotCategory.Sensor,           MaxComponentClass = 2, Required = false },
            new() { SlotId = "exhaust",             Label = "Exhaust System",       Category = SlotCategory.Exhaust,          MaxComponentClass = 2, Required = true  },
            new() { SlotId = "cargo.port.01",       Label = "Port Cargo Rack",      Category = SlotCategory.Cargo,            MaxComponentClass = 2, Required = false },
            new() { SlotId = "cargo.starboard.01",  Label = "Starboard Cargo Rack", Category = SlotCategory.Cargo,            MaxComponentClass = 2, Required = false },
            new() { SlotId = "internal_lights",     Label = "Internal Lighting",    Category = SlotCategory.InternalLights,   MaxComponentClass = 2, Required = false },
            new() { SlotId = "external_lights",     Label = "External Lighting",    Category = SlotCategory.ExternalLights,   MaxComponentClass = 2, Required = false },
            new() { SlotId = "flyability_mon",      Label = "Flyability Monitor",   Category = SlotCategory.FlyabilityMonitor,MaxComponentClass = 2, Required = true  },
        ],

        VisualGeometry = BuildAriesGeometry(),
    };

    private static SemanticHullGeometry BuildAriesGeometry()
    {
        var vertices = new Dictionary<string, DVec3>(StringComparer.Ordinal);
        var faces = new List<SemanticHullFace>();

        string[] ringNames = ["head-front", "head-rear", "cargo-rear"];
        double[] z = [-8.0, -2.2, 8.0];
        double[] scale = [0.82, 1.0, 1.08];

        for (int ring = 0; ring < ringNames.Length; ring++)
        {
            AddRing(vertices, ringNames[ring], z[ring], scale[ring]);
        }

        AddRearPlaneQuad(vertices, CargoDoorPortPanelSeatId, -2.95, 0.0, -1.45, 1.45, 8.06);
        AddRearPlaneQuad(vertices, CargoDoorStarboardPanelSeatId, 0.0, 2.95, -1.45, 1.45, 8.06);
        AddRearPlaneQuad(vertices, $"{HullId}.rear.cargo-door-frame.top.01", -3.2, 3.2, 1.60, 2.05, 8.07);
        AddRearPlaneQuad(vertices, $"{HullId}.rear.cargo-door-frame.bottom.01", -3.2, 3.2, -2.05, -1.60, 8.07);
        AddRearPlaneQuad(vertices, $"{HullId}.rear.cargo-door-frame.port.01", -3.45, -3.05, -1.60, 1.60, 8.07);
        AddRearPlaneQuad(vertices, $"{HullId}.rear.cargo-door-frame.starboard.01", 3.05, 3.45, -1.60, 1.60, 8.07);

        var faceSpecs = new (string id, int section, int edge, HullSurfaceRole role, string material, string? assembly)[]
        {
            ($"{HullId}.top.cockpit-glass.01", 0, 0, HullSurfaceRole.CockpitGlass, "cockpit-glass", null),
            ($"{HullId}.starboard.head-armour.01", 0, 1, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.forward-side.01", 0, 2, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.lower-forward-service.01", 0, 3, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.underside.forward-service.01", 0, 4, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.lower-forward-service.01", 0, 5, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.forward-side.01", 0, 6, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.port.cockpit-frame.01", 0, 7, HullSurfaceRole.CockpitFrame, "cockpit-frame", null),

            ($"{HullId}.top.cargo.01", 1, 0, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.cargo-shoulder.01", 1, 1, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.engine-root.01", 1, 2, HullSurfaceRole.EngineMount, "structural-hull", null),
            ($"{HullId}.starboard.lower-rear-service.01", 1, 3, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.underside.cargo-service.01", 1, 4, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.lower-rear-service.01", 1, 5, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.engine-root.01", 1, 6, HullSurfaceRole.EngineMount, "structural-hull", null),
            ($"{HullId}.port.cargo-shoulder.01", 1, 7, HullSurfaceRole.PanelSeat, "panel-exterior", null),
        };

        foreach (var spec in faceSpecs)
        {
            string a = VertexId(ringNames[spec.section], spec.edge);
            string b = VertexId(ringNames[spec.section], (spec.edge + 1) % 8);
            string c = VertexId(ringNames[spec.section + 1], (spec.edge + 1) % 8);
            string d = VertexId(ringNames[spec.section + 1], spec.edge);
            DVec3 desiredNormal = EdgeNormal(spec.edge);
            AddFace(faces, vertices, spec.id, [a, b, c, d], spec.role, spec.material,
                desiredNormal, spec.role == HullSurfaceRole.PanelSeat ? spec.id : null, spec.assembly);
        }

        AddFace(faces, vertices, $"{HullId}.front.armoured-head.01",
            Enumerable.Range(0, 8).Select(i => VertexId("head-front", i)).ToArray(),
            HullSurfaceRole.PanelSeat, "panel-exterior", -DVec3.UnitZ, $"{HullId}.front.armoured-head.01", null);

        AddFace(faces, vertices, CargoDoorId,
            Enumerable.Range(0, 8).Select(i => VertexId("cargo-rear", i)).ToArray(),
            HullSurfaceRole.CargoDoor, "cargo-door-structure", DVec3.UnitZ, null, CargoDoorId);

        AddFace(faces, vertices, CargoDoorPortPanelSeatId,
            OverlayVertexIds(CargoDoorPortPanelSeatId), HullSurfaceRole.PanelSeat, "panel-exterior",
            DVec3.UnitZ, CargoDoorPortPanelSeatId, CargoDoorId, contributesToClosedHull: false);
        AddFace(faces, vertices, CargoDoorStarboardPanelSeatId,
            OverlayVertexIds(CargoDoorStarboardPanelSeatId), HullSurfaceRole.PanelSeat, "panel-exterior",
            DVec3.UnitZ, CargoDoorStarboardPanelSeatId, CargoDoorId, contributesToClosedHull: false);

        foreach (string frameFaceId in new[]
        {
            $"{HullId}.rear.cargo-door-frame.top.01",
            $"{HullId}.rear.cargo-door-frame.bottom.01",
            $"{HullId}.rear.cargo-door-frame.port.01",
            $"{HullId}.rear.cargo-door-frame.starboard.01",
        })
        {
            AddFace(faces, vertices, frameFaceId, OverlayVertexIds(frameFaceId),
                HullSurfaceRole.ExposedStructure, "cargo-door-frame", DVec3.UnitZ,
                null, CargoDoorId, contributesToClosedHull: false);
        }

        return new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices = vertices.Select(kvp => new SemanticHullVertex(kvp.Key, kvp.Value)).ToArray(),
            Faces = faces,
            Assemblies =
            [
                new(CargoDoorId, "CargoDoor", CargoDoorId)
                {
                    ClosedPose = "Closed",
                    OpeningPolygonVertexIds = Enumerable.Range(0, 8).Select(i => VertexId("cargo-rear", i)).ToArray(),
                    MovementConcept = "Two sliding leaves",
                    MovementAxes = [-DVec3.UnitX, DVec3.UnitX],
                    MovementClearanceVolumes =
                    [
                        new(new DVec3(-5.8, -1.8, 7.65), new DVec3(-3.1, 1.8, 8.6)),
                        new(new DVec3(3.1, -1.8, 7.65), new DVec3(5.8, 1.8, 8.6)),
                    ],
                    ArmourPanelSeats =
                    [
                        new(CargoDoorPortPanelSeatId, "port container lane", new DVec3(-1.5, 0.0, 8.05), new DVec3(2.8, 3.0, 0.08), DVec3.UnitZ),
                        new(CargoDoorStarboardPanelSeatId, "starboard container lane", new DVec3(1.5, 0.0, 8.05), new DVec3(2.8, 3.0, 0.08), DVec3.UnitZ),
                    ],
                },
            ],
            AttachmentPorts =
            [
                new($"{HullId}.port.engine-root.01", new DVec3(-4.05, -0.05, 2.75), -DVec3.UnitX, AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    ComponentSlotId = "engine.port.01",
                    EngineMountStandardId = EngineMountStandardIds.H2,
                    EngineMountSide = Engines.EngineMountSide.Port,
                    FootprintMeters = new DVec3(2.2, 2.4, 0.0),
                    ClearanceMinMeters = new DVec3(-5.0, -1.7, -1.8),
                    ClearanceMaxMeters = new DVec3(-3.65, 1.7, 7.4),
                },
                new($"{HullId}.starboard.engine-root.01", new DVec3(4.05, -0.05, 2.75), DVec3.UnitX, AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    ComponentSlotId = "engine.starboard.01",
                    EngineMountStandardId = EngineMountStandardIds.H2,
                    EngineMountSide = Engines.EngineMountSide.Starboard,
                    FootprintMeters = new DVec3(2.2, 2.4, 0.0),
                    ClearanceMinMeters = new DVec3(3.65, -1.7, -1.8),
                    ClearanceMaxMeters = new DVec3(5.0, 1.7, 7.4),
                },
                new($"{HullId}.underside.landing-foot.01", new DVec3(-1.85, -2.55, -6.15), -DVec3.UnitY, AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.75, 0.55, 0.0),
                    ClearanceMinMeters = new DVec3(-2.35, -2.85, -6.55),
                    ClearanceMaxMeters = new DVec3(-1.35, -2.35, -5.75),
                },
                new($"{HullId}.underside.landing-foot.02", new DVec3(1.85, -2.55, -6.15), -DVec3.UnitY, AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.75, 0.55, 0.0),
                    ClearanceMinMeters = new DVec3(1.35, -2.85, -6.55),
                    ClearanceMaxMeters = new DVec3(2.35, -2.35, -5.75),
                },
                new($"{HullId}.underside.landing-foot.03", new DVec3(0.0, -2.55, 6.25), -DVec3.UnitY, AttachmentCapability.LandingGear)
                {
                    FootprintMeters = new DVec3(0.9, 0.65, 0.0),
                    ClearanceMinMeters = new DVec3(-0.55, -2.85, 5.85),
                    ClearanceMaxMeters = new DVec3(0.55, -2.35, 6.65),
                },
                new($"{HullId}.top.service-sensor.01", new DVec3(-1.8, 2.2, -1.8), DVec3.UnitY, AttachmentCapability.Sensor | AttachmentCapability.Utility)
                {
                    Up = -DVec3.UnitZ,
                    FootprintMeters = new DVec3(0.7, 0.7, 0.0),
                    ClearanceMinMeters = new DVec3(-2.25, 2.1, -2.25),
                    ClearanceMaxMeters = new DVec3(-1.35, 2.75, -1.35),
                },
                new($"{HullId}.underside.utility-sensor.01", new DVec3(0.0, -2.5, -1.0), -DVec3.UnitY, AttachmentCapability.Sensor | AttachmentCapability.Utility)
                {
                    Up = DVec3.UnitZ,
                    FootprintMeters = new DVec3(0.8, 0.8, 0.0),
                    ClearanceMinMeters = new DVec3(-0.5, -2.85, -1.5),
                    ClearanceMaxMeters = new DVec3(0.5, -2.35, -0.5),
                },
                new($"{HullId}.port.utility-sensor.01", new DVec3(-3.05, 0.15, -3.8), -DVec3.UnitX, AttachmentCapability.Sensor | AttachmentCapability.Utility)
                {
                    Up = DVec3.UnitY,
                    FootprintMeters = new DVec3(0.8, 0.8, 0.0),
                    ClearanceMinMeters = new DVec3(-3.45, -0.35, -4.3),
                    ClearanceMaxMeters = new DVec3(-2.85, 0.65, -3.3),
                },
            ],
            MarkerLights =
            [
                new($"{HullId}.port.navigation-light.01", new DVec3(-3.95, 0.65, -5.8), -DVec3.UnitX, "red", 0.24, 1.0, "continuous"),
                new($"{HullId}.starboard.navigation-light.01", new DVec3(3.95, 0.65, -5.8), DVec3.UnitX, "green", 0.24, 1.0, "continuous"),
                new($"{HullId}.rear.position-light.01", new DVec3(0.0, 0.85, 8.12), DVec3.UnitZ, "white", 0.28, 0.9, "continuous"),
                new($"{HullId}.underside.marker.01", new DVec3(0.0, -1.95, 2.5), -DVec3.UnitY, "amber", 0.20, 0.65, "slow-pulse"),
            ],
            BeamLights =
            [
                new($"{HullId}.underside.beam-light.01", new DVec3(-0.85, -1.15, -7.55), new DVec3(0.0, -0.35, -1.0).Normalized(), 24.0, 700.0, 1.0, "warm-white"),
                new($"{HullId}.underside.beam-light.02", new DVec3(0.85, -1.15, -7.55), new DVec3(0.0, -0.35, -1.0).Normalized(), 24.0, 700.0, 1.0, "warm-white"),
            ],
        };
    }

    private static CargoContainerPlacementDefinition CreateCargoContainerPlacement(string placementId, DVec3 center)
    {
        var bounds = new DVec3(2.5, 2.5, 6.0);
        return new CargoContainerPlacementDefinition(
            placementId,
            center,
            bounds,
            new SemanticBounds(center - bounds / 2.0, center + bounds / 2.0));
    }

    private static void AddRing(Dictionary<string, DVec3> vertices, string ringName, double z, double scale)
    {
        const double sideHalf = 3.25;
        const double topHalf = 2.7;
        const double bottomHalf = 3.05;
        const double yTop = 2.25;
        const double yUpper = 1.35;
        const double yLower = -1.55;
        const double yBottom = -2.35;

        DVec3[] points =
        [
            new(-topHalf * scale, yTop * scale, z),
            new(topHalf * scale, yTop * scale, z),
            new(sideHalf * scale, yUpper * scale, z),
            new(sideHalf * scale, yLower * scale, z),
            new(bottomHalf * scale, yBottom * scale, z),
            new(-bottomHalf * scale, yBottom * scale, z),
            new(-sideHalf * scale, yLower * scale, z),
            new(-sideHalf * scale, yUpper * scale, z),
        ];

        for (int i = 0; i < points.Length; i++)
            vertices.Add(VertexId(ringName, i), points[i]);
    }

    private static string VertexId(string ringName, int index)
        => $"{HullId}.{ringName}.perimeter.{index + 1:00}";

    private static void AddRearPlaneQuad(
        Dictionary<string, DVec3> vertices,
        string faceId,
        double minX,
        double maxX,
        double minY,
        double maxY,
        double z)
    {
        vertices.Add(OverlayVertexId(faceId, 1), new DVec3(minX, minY, z));
        vertices.Add(OverlayVertexId(faceId, 2), new DVec3(maxX, minY, z));
        vertices.Add(OverlayVertexId(faceId, 3), new DVec3(maxX, maxY, z));
        vertices.Add(OverlayVertexId(faceId, 4), new DVec3(minX, maxY, z));
    }

    private static string[] OverlayVertexIds(string faceId)
        => [OverlayVertexId(faceId, 1), OverlayVertexId(faceId, 2), OverlayVertexId(faceId, 3), OverlayVertexId(faceId, 4)];

    private static string OverlayVertexId(string faceId, int index)
        => $"{faceId}.corner.{index:00}";

    private static DVec3 EdgeNormal(int edge) => edge switch
    {
        0 => DVec3.UnitY,
        1 => new DVec3(1, 1, 0).Normalized(),
        2 => DVec3.UnitX,
        3 => new DVec3(1, -1, 0).Normalized(),
        4 => -DVec3.UnitY,
        5 => new DVec3(-1, -1, 0).Normalized(),
        6 => -DVec3.UnitX,
        7 => new DVec3(-1, 1, 0).Normalized(),
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };

    private static void AddFace(List<SemanticHullFace> faces, Dictionary<string, DVec3> vertices,
        string id, string[] vertexIds, HullSurfaceRole role, string material, DVec3 desiredNormal,
        string? panelSlotId, string? assemblyId, bool contributesToClosedHull = true)
    {
        if (DVec3.Dot(ComputePolygonNormal(vertexIds.Select(v => vertices[v]).ToArray()), desiredNormal) < 0)
            Array.Reverse(vertexIds);

        DVec3 actualNormal = ComputePolygonNormal(vertexIds.Select(v => vertices[v]).ToArray()).Normalized();
        faces.Add(new SemanticHullFace(id, vertexIds, role, material, actualNormal, panelSlotId, assemblyId)
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
}
