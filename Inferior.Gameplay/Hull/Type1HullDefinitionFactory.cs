using Inferior.Core.Math;
using Inferior.Gameplay.Ship;

namespace Inferior.Gameplay.Hull;

public static class Type1HullDefinitionFactory
{
    private const string HullId = "type-1";
    private const string CargoDoorId = $"{HullId}.rear.cargo-door.01";

    public static HullDefinition Create() => new()
    {
        HullTypeId   = HullId,
        DisplayName  = "Aries",
        SizeClass    = ShipSizeClass.Small,
        HullMass     = 72_000.0,
        CockpitOffset = new DVec3(1.25, 1.85, -5.75),

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
            TransferAxis: DVec3.UnitZ),

        AerodynamicLift         = 0.65,
        AerodynamicBrakeFront   = 0.95,
        AerodynamicBrakeLateral = 2.20,

        Slots =
        [
            new() { SlotId = "reactor",             Label = "Power Reactor",        Category = SlotCategory.PowerReactor,     MaxComponentClass = 2, Required = true  },
            new() { SlotId = "power_bus",           Label = "Power Bus",            Category = SlotCategory.PowerBus,         MaxComponentClass = 2, Required = true  },
            new() { SlotId = "engine.port.01",      Label = "Port Engine",          Category = SlotCategory.Engine,           MaxComponentClass = 2, Required = true  },
            new() { SlotId = "engine.starboard.01", Label = "Starboard Engine",     Category = SlotCategory.Engine,           MaxComponentClass = 2, Required = true  },
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

        var faceSpecs = new (string id, int section, int edge, HullSurfaceRole role, string material, string? assembly)[]
        {
            ($"{HullId}.top.cockpit-glass.01", 0, 0, HullSurfaceRole.CockpitGlass, "cockpit-glass", null),
            ($"{HullId}.starboard.cockpit-frame.01", 0, 1, HullSurfaceRole.CockpitFrame, "cockpit-frame", null),
            ($"{HullId}.starboard.engine-mount.01", 0, 2, HullSurfaceRole.EngineMount, "structural-hull", null),
            ($"{HullId}.starboard.service.01", 0, 3, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.underside.head.01", 0, 4, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.port.service.01", 0, 5, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.engine-mount.01", 0, 6, HullSurfaceRole.EngineMount, "structural-hull", null),
            ($"{HullId}.top.head-armour.01", 0, 7, HullSurfaceRole.PanelSeat, "panel-exterior", null),

            ($"{HullId}.top.cargo.01", 1, 0, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.cargo-shoulder.01", 1, 1, HullSurfaceRole.PanelSeat, "panel-exterior", null),
            ($"{HullId}.starboard.engine-mount.02", 1, 2, HullSurfaceRole.EngineMount, "structural-hull", null),
            ($"{HullId}.starboard.lower-service.01", 1, 3, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.underside.cargo.01", 1, 4, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.lower-service.01", 1, 5, HullSurfaceRole.ServiceSurface, "structural-hull", null),
            ($"{HullId}.port.engine-mount.02", 1, 6, HullSurfaceRole.EngineMount, "structural-hull", null),
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

        return new SemanticHullGeometry
        {
            RequireClosedHull = true,
            Vertices = vertices.Select(kvp => new SemanticHullVertex(kvp.Key, kvp.Value)).ToArray(),
            Faces = faces,
            Assemblies =
            [
                new(CargoDoorId, "CargoDoor", CargoDoorId),
            ],
            AttachmentPorts =
            [
                new($"{HullId}.port.engine-root.01", new DVec3(-4.05, -0.05, 2.75), -DVec3.UnitX, AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    FootprintMeters = new DVec3(2.2, 2.4, 0.0),
                    ClearanceMinMeters = new DVec3(-5.0, -1.7, -1.8),
                    ClearanceMaxMeters = new DVec3(-3.65, 1.7, 7.4),
                },
                new($"{HullId}.starboard.engine-root.01", new DVec3(4.05, -0.05, 2.75), DVec3.UnitX, AttachmentCapability.Engine)
                {
                    Up = DVec3.UnitY,
                    FootprintMeters = new DVec3(2.2, 2.4, 0.0),
                    ClearanceMinMeters = new DVec3(3.65, -1.7, -1.8),
                    ClearanceMaxMeters = new DVec3(5.0, 1.7, 7.4),
                },
                new($"{HullId}.port.forward-landing-foot.01", new DVec3(-1.85, -2.55, -6.15), -DVec3.UnitY, AttachmentCapability.LandingGear),
                new($"{HullId}.starboard.forward-landing-foot.01", new DVec3(1.85, -2.55, -6.15), -DVec3.UnitY, AttachmentCapability.LandingGear),
                new($"{HullId}.rear.landing-foot.01", new DVec3(0.0, -2.55, 6.25), -DVec3.UnitY, AttachmentCapability.LandingGear),
                new($"{HullId}.top.service-sensor.01", new DVec3(-1.8, 2.2, -1.8), DVec3.UnitY, AttachmentCapability.Sensor | AttachmentCapability.Utility),
            ],
            MarkerLights =
            [
                new($"{HullId}.port.marker.01", new DVec3(-3.95, 0.65, -5.8), -DVec3.UnitX, "red", 0.24, 1.0, "steady"),
                new($"{HullId}.starboard.marker.01", new DVec3(3.95, 0.65, -5.8), DVec3.UnitX, "green", 0.24, 1.0, "steady"),
                new($"{HullId}.rear.marker.01", new DVec3(0.0, 0.55, 8.05), DVec3.UnitZ, "white", 0.28, 0.9, "steady"),
                new($"{HullId}.underside.marker.01", new DVec3(0.0, -1.95, 2.5), -DVec3.UnitY, "amber", 0.20, 0.65, "slow-pulse"),
            ],
            BeamLights =
            [
                new($"{HullId}.front.beam-light.01", new DVec3(-0.85, -0.95, -8.05), -DVec3.UnitZ, 18.0, 650.0, 1.0, "warm-white"),
                new($"{HullId}.front.beam-light.02", new DVec3(0.85, -0.95, -8.05), -DVec3.UnitZ, 18.0, 650.0, 1.0, "warm-white"),
            ],
        };
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
        => $"{HullId}.v.{ringName}.{index + 1:00}";

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
        string? panelSlotId, string? assemblyId)
    {
        if (DVec3.Dot(ComputePolygonNormal(vertexIds.Select(v => vertices[v]).ToArray()), desiredNormal) < 0)
            Array.Reverse(vertexIds);

        DVec3 actualNormal = ComputePolygonNormal(vertexIds.Select(v => vertices[v]).ToArray()).Normalized();
        faces.Add(new SemanticHullFace(id, vertexIds, role, material, actualNormal, panelSlotId, assemblyId));
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
