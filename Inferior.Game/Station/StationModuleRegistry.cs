using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

public static class StationModuleRegistry
{
    // ── core-hub ──────────────────────────────────────────────────────────────
    // 20×20×20 central block. Six-way branching. The station root.
    public static readonly StationModuleDefinition CoreHub = new()
    {
        Id          = "core-hub",
        Category    = "core",
        BoundingBox = new Vector3(20, 20, 20),
        MinScale    = StationScale.Outpost,
        Ports       =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+10, 0, 0),  OutwardNormal = Vector3.UnitX,  Size = PortSize.Medium },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-10, 0, 0),  OutwardNormal = -Vector3.UnitX, Size = PortSize.Medium },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +10),  OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -10),  OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +10, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              AcceptsCategories = ["connector", "science", "military"] },
            new StationPort { Id = "ny", LocalPosition = new Vector3(0, -10, 0),  OutwardNormal = -Vector3.UnitY, Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── connector-long ────────────────────────────────────────────────────────
    // 8×8×40 spine segment. Extends along Z. Branching port on top.
    public static readonly StationModuleDefinition ConnectorLong = new()
    {
        Id           = "connector-long",
        Category     = "connector",
        BoundingBox  = new Vector3(8, 8, 40),
        MinScale     = StationScale.Outpost,
        SelectWeight = 2f,
        Ports        =
        [
            new StationPort { Id = "front", LocalPosition = new Vector3(0, 0, +20), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "back",  LocalPosition = new Vector3(0, 0, -20), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "top",   LocalPosition = new Vector3(0, +4, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              AcceptsCategories = ["hab", "science", "cargo", "military"] },
        ]
    };

    // ── connector-short ───────────────────────────────────────────────────────
    // 6×6×16 short spine segment. Two ends only.
    public static readonly StationModuleDefinition ConnectorShort = new()
    {
        Id           = "connector-short",
        Category     = "connector",
        BoundingBox  = new Vector3(6, 6, 16),
        MinScale     = StationScale.Outpost,
        SelectWeight = 1.5f,
        Ports        =
        [
            new StationPort { Id = "front", LocalPosition = new Vector3(0, 0, +8), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "back",  LocalPosition = new Vector3(0, 0, -8), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
        ]
    };

    // ── hab-block ─────────────────────────────────────────────────────────────
    // 18×14×18 habitation block. Four lateral ports for chaining; top for extras.
    public static readonly StationModuleDefinition HabBlock = new()
    {
        Id           = "hab-block",
        Category     = "hab",
        BoundingBox  = new Vector3(18, 14, 18),
        MinScale     = StationScale.Outpost,
        SelectWeight = 3f,
        Ports        =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+9, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Medium },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-9, 0, 0), OutwardNormal = -Vector3.UnitX, Size = PortSize.Medium },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +9), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -9), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +7, 0), OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              AcceptsCategories = ["connector", "science"] },
        ]
    };

    // ── cargo-bay ─────────────────────────────────────────────────────────────
    // 24×12×20 cargo block. Broad faces for container access; side branching.
    public static readonly StationModuleDefinition CargoBay = new()
    {
        Id          = "cargo-bay",
        Category    = "cargo",
        BoundingBox = new Vector3(24, 12, 20),
        MinScale    = StationScale.Station,
        Ports       =
        [
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +10),  OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -10),  OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "px", LocalPosition = new Vector3(+12, 0, 0),  OutwardNormal = Vector3.UnitX,  Size = PortSize.Medium,
                              AcceptsCategories = ["connector", "cargo"] },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-12, 0, 0),  OutwardNormal = -Vector3.UnitX, Size = PortSize.Medium,
                              AcceptsCategories = ["connector", "cargo"] },
        ]
    };

    // ── docking-arm ───────────────────────────────────────────────────────────
    // 5×5×32 thin arm with a landing pad at the far end.
    public static readonly StationModuleDefinition DockingArm = new()
    {
        Id          = "docking-arm",
        Category    = "docking",
        BoundingBox = new Vector3(5, 5, 32),
        MinScale    = StationScale.Outpost,
        Ports       =
        [
            new StationPort { Id = "attach", LocalPosition = new Vector3(0, 0, -16), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "pad",    LocalPosition = new Vector3(0, 0, +16), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium,
                              IsDocking = true, IsTerminal = true },
        ]
    };

    // ── science-block ─────────────────────────────────────────────────────────
    // 14×14×14 compact science module.
    public static readonly StationModuleDefinition ScienceBlock = new()
    {
        Id          = "science-block",
        Category    = "science",
        BoundingBox = new Vector3(14, 14, 14),
        MinScale    = StationScale.Station,
        Ports       =
        [
            new StationPort { Id = "attach", LocalPosition = new Vector3(0, 0, -7),  OutwardNormal = -Vector3.UnitZ, Size = PortSize.Small },
            new StationPort { Id = "side",   LocalPosition = new Vector3(+7, 0, 0),  OutwardNormal = Vector3.UnitX,  Size = PortSize.Small,
                              AcceptsCategories = ["connector", "science"] },
            new StationPort { Id = "top",    LocalPosition = new Vector3(0, +7, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── industrial-block ──────────────────────────────────────────────────────
    // 22×18×22 large processing block. Full lateral branching; top terminates.
    public static readonly StationModuleDefinition IndustrialBlock = new()
    {
        Id          = "industrial-block",
        Category    = "industrial",
        BoundingBox = new Vector3(22, 18, 22),
        MinScale    = StationScale.Station,
        Ports       =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+11, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Large },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-11, 0, 0), OutwardNormal = -Vector3.UnitX, Size = PortSize.Large },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +11), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -11), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +9, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── core-hub-large ────────────────────────────────────────────────────────
    // 40×40×40 — twice the standard core. Imposing central mass for Port+ stations.
    public static readonly StationModuleDefinition CoreHubLarge = new()
    {
        Id           = "core-hub-large",
        Category     = "core",
        BoundingBox  = new Vector3(40, 40, 40),
        MinScale     = StationScale.Port,
        SelectWeight = 1.5f,
        Ports        =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+20, 0, 0),  OutwardNormal = Vector3.UnitX,  Size = PortSize.Large },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-20, 0, 0),  OutwardNormal = -Vector3.UnitX, Size = PortSize.Large },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +20),  OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -20),  OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +20, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Medium,
                              AcceptsCategories = ["connector", "science", "military"] },
            new StationPort { Id = "ny", LocalPosition = new Vector3(0, -20, 0),  OutwardNormal = -Vector3.UnitY, Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── connector-long-large ──────────────────────────────────────────────────
    // 80×16×16 — twice the standard connector. The spine on large linear stations.
    public static readonly StationModuleDefinition ConnectorLongLarge = new()
    {
        Id           = "connector-long-large",
        Category     = "connector",
        BoundingBox  = new Vector3(80, 16, 16),
        MinScale     = StationScale.Port,
        SelectWeight = 2.0f,
        Ports        =
        [
            new StationPort { Id = "front", LocalPosition = new Vector3(0, 0, +40), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "back",  LocalPosition = new Vector3(0, 0, -40), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "top",   LocalPosition = new Vector3(0, +8, 0),  OutwardNormal = Vector3.UnitY,  Size = PortSize.Medium,
                              AcceptsCategories = ["hab", "science", "cargo", "military", "industrial"] },
        ]
    };

    // ── hab-block-large ───────────────────────────────────────────────────────
    // 36×28×36 — large residential/commercial block.
    public static readonly StationModuleDefinition HabBlockLarge = new()
    {
        Id           = "hab-block-large",
        Category     = "hab",
        BoundingBox  = new Vector3(36, 28, 36),
        MinScale     = StationScale.Station,
        SelectWeight = 1.2f,
        Ports        =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+18, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Large },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-18, 0, 0), OutwardNormal = -Vector3.UnitX, Size = PortSize.Large },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +18), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -18), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +14, 0), OutwardNormal = Vector3.UnitY,  Size = PortSize.Medium,
                              AcceptsCategories = ["connector", "science"] },
        ]
    };

    // ── cargo-bay-large ───────────────────────────────────────────────────────
    // 48×24×40 — freight handling at scale.
    public static readonly StationModuleDefinition CargoBayLarge = new()
    {
        Id           = "cargo-bay-large",
        Category     = "cargo",
        BoundingBox  = new Vector3(48, 24, 40),
        MinScale     = StationScale.Port,
        SelectWeight = 1.3f,
        Ports        =
        [
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +20),  OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -20),  OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "px", LocalPosition = new Vector3(+24, 0, 0),  OutwardNormal = Vector3.UnitX,  Size = PortSize.Large,
                              AcceptsCategories = ["connector", "cargo"] },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-24, 0, 0),  OutwardNormal = -Vector3.UnitX, Size = PortSize.Large,
                              AcceptsCategories = ["connector", "cargo"] },
        ]
    };

    // ── industrial-block-large ────────────────────────────────────────────────
    // 44×36×44 — massive processing facility.
    public static readonly StationModuleDefinition IndustrialBlockLarge = new()
    {
        Id           = "industrial-block-large",
        Category     = "industrial",
        BoundingBox  = new Vector3(44, 36, 44),
        MinScale     = StationScale.Port,
        SelectWeight = 1.2f,
        Ports        =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+22, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Large },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-22, 0, 0), OutwardNormal = -Vector3.UnitX, Size = PortSize.Large },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +22), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Large },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -22), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Large },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +18, 0), OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── hab-block-octagonal ───────────────────────────────────────────────────
    // Same footprint as hab-block with an octagonal cross-section.
    // Lighting shades the 8 side faces differently, simulating roundness.
    public static readonly StationModuleDefinition HabBlockOctagonal = new()
    {
        Id           = "hab-block-octagonal",
        Category     = "hab",
        BoundingBox  = new Vector3(18, 14, 18),
        MinScale     = StationScale.Station,
        SelectWeight = 0.8f,
        MeshFactory  = seed =>
        {
            var mesh = new StationModuleMesh();
            AddOctagonalPrism(mesh, radius: 9f, height: 14f, color: new Color(200, 195, 185));
            mesh.BaseFaceCount = mesh.FaceCount;
            return mesh;
        },
        Ports        =
        [
            new StationPort { Id = "px", LocalPosition = new Vector3(+9, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Medium },
            new StationPort { Id = "nx", LocalPosition = new Vector3(-9, 0, 0), OutwardNormal = -Vector3.UnitX, Size = PortSize.Medium },
            new StationPort { Id = "pz", LocalPosition = new Vector3(0, 0, +9), OutwardNormal = Vector3.UnitZ,  Size = PortSize.Medium },
            new StationPort { Id = "nz", LocalPosition = new Vector3(0, 0, -9), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Medium },
            new StationPort { Id = "py", LocalPosition = new Vector3(0, +7, 0), OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              AcceptsCategories = ["connector", "science"] },
        ]
    };

    // ── science-block-octagonal ───────────────────────────────────────────────
    public static readonly StationModuleDefinition ScienceBlockOctagonal = new()
    {
        Id           = "science-block-octagonal",
        Category     = "science",
        BoundingBox  = new Vector3(14, 14, 14),
        MinScale     = StationScale.Station,
        SelectWeight = 0.7f,
        MeshFactory  = seed =>
        {
            var mesh = new StationModuleMesh();
            AddOctagonalPrism(mesh, radius: 7f, height: 14f, color: new Color(155, 165, 175));
            mesh.BaseFaceCount = mesh.FaceCount;
            return mesh;
        },
        Ports        =
        [
            new StationPort { Id = "attach", LocalPosition = new Vector3(0, 0, -7), OutwardNormal = -Vector3.UnitZ, Size = PortSize.Small },
            new StationPort { Id = "side",   LocalPosition = new Vector3(+7, 0, 0), OutwardNormal = Vector3.UnitX,  Size = PortSize.Small,
                              AcceptsCategories = ["connector", "science"] },
            new StationPort { Id = "top",    LocalPosition = new Vector3(0, +7, 0), OutwardNormal = Vector3.UnitY,  Size = PortSize.Small,
                              IsTerminal = true },
        ]
    };

    // ── Octagonal prism mesh factory ──────────────────────────────────────────
    // 8 quad side faces + top triangle fan. No bottom cap (always internal/terminal).
    private static void AddOctagonalPrism(StationModuleMesh mesh,
                                           float radius, float height, Color color,
                                           float uvScale = 5.0f)
    {
        float h     = height * 0.5f;
        const int sides = 8;

        var topRing = new Vector3[sides];
        var botRing = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle  = i * MathF.Tau / sides + MathF.PI / sides;
            float x      = MathF.Cos(angle) * radius;
            float z      = MathF.Sin(angle) * radius;
            topRing[i]   = new Vector3(x, +h, z);
            botRing[i]   = new Vector3(x, -h, z);
        }

        // Side faces — each is a quad with its own computed outward normal
        for (int i = 0; i < sides; i++)
        {
            int     next      = (i + 1) % sides;
            float   midAngle  = (i + 0.5f) * MathF.Tau / sides + MathF.PI / sides;
            Vector3 faceNorm  = new Vector3(MathF.Cos(midAngle), 0, MathF.Sin(midAngle));
            mesh.AddQuad(botRing[i], botRing[next], topRing[next], topRing[i],
                         faceNorm, color, uvScale);
        }

        // Top face — fan of triangles from centre
        Vector3 topCentre = new Vector3(0, +h, 0);
        for (int i = 0; i < sides; i++)
            mesh.AddTriangle(topCentre, topRing[i], topRing[(i + 1) % sides],
                             Vector3.UnitY, color, uvScale);
    }

    public static IEnumerable<StationModuleDefinition> GetByCategory(string category)
        => All.Where(m => m.Category == category);

    public static readonly StationModuleDefinition[] All =
    [
        CoreHub, ConnectorLong, ConnectorShort, HabBlock,
        CargoBay, DockingArm, ScienceBlock, IndustrialBlock,
        CoreHubLarge, ConnectorLongLarge, HabBlockLarge, CargoBayLarge, IndustrialBlockLarge,
        HabBlockOctagonal, ScienceBlockOctagonal,
    ];

    public static Color CategoryColor(string category) => category switch
    {
        "core"       => new Color(160, 160, 160),
        "connector"  => new Color(80,  80,  80),
        "hab"        => new Color(220, 210, 190),
        "cargo"      => new Color(180, 150, 80),
        "docking"    => new Color(160, 180, 200),
        "science"    => new Color(150, 170, 210),
        "industrial" => new Color(130, 110, 70),
        _            => new Color(150, 150, 150),
    };
}
