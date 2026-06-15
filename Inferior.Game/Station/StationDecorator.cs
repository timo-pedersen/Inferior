using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Adds per-module decoration geometry (windows, hatches, antennas, pipes, lights)
// to each PlacedModule.Mesh in local module space. Must be called after growth is
// complete so that AttachmentPort and ChildPorts are fully populated.
public static class StationDecorator
{
    public static void Decorate(IReadOnlyList<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            var baseRng    = new System.Random(mod.Seed);
            var windowRng  = new System.Random(baseRng.Next());
            var hatchRng   = new System.Random(baseRng.Next());
            var antennaRng = new System.Random(baseRng.Next());
            var pipeRng    = new System.Random(baseRng.Next());
            var lightRng   = new System.Random(baseRng.Next());

            FaceInfo[] faces = ComputeFaces(mod);
            var mesh = new StationModuleMesh();

            foreach (var face in faces)
            {
                GenerateWindows (mod, face, windowRng,  mesh);
                GenerateHatches (mod, face, hatchRng,   mesh);
                GenerateAntennas(mod, face, antennaRng, mesh);
            }

            GeneratePipes (mod, faces, pipeRng,  mesh);
            GenerateLights(mod, faces, lightRng, mesh);

            if (!mesh.IsEmpty)
                mod.Mesh = mesh;
        }
    }

    // ── Face analysis ─────────────────────────────────────────────────────────

    readonly struct FaceInfo(
        Vector3 localNormal,
        Vector3 localCenter,
        Vector3 localRight,
        Vector3 localUp,
        float   width,
        float   height,
        bool    isExposed)
    {
        public readonly Vector3 LocalNormal = localNormal;
        public readonly Vector3 LocalCenter = localCenter;
        public readonly Vector3 LocalRight  = localRight;
        public readonly Vector3 LocalUp     = localUp;
        public readonly float   Width       = width;
        public readonly float   Height      = height;
        public readonly bool    IsExposed   = isExposed;
    }

    private static FaceInfo[] ComputeFaces(PlacedModule mod)
    {
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        (Vector3 n, float w, float h)[] faceData =
        [
            ( Vector3.UnitX,  bb.Z, bb.Y),
            (-Vector3.UnitX,  bb.Z, bb.Y),
            ( Vector3.UnitY,  bb.X, bb.Z),
            (-Vector3.UnitY,  bb.X, bb.Z),
            ( Vector3.UnitZ,  bb.X, bb.Y),
            (-Vector3.UnitZ,  bb.X, bb.Y),
        ];

        var result = new FaceInfo[6];
        for (int i = 0; i < 6; i++)
        {
            var (n, w, h)   = faceData[i];
            Vector3 center  = new(n.X * half.X, n.Y * half.Y, n.Z * half.Z);
            var (right, up) = TangentFrame(n);
            bool blocked    = IsFaceBlocked(mod, n);
            result[i]       = new FaceInfo(n, center, right, up, w, h, !blocked);
        }
        return result;
    }

    private static bool IsFaceBlocked(PlacedModule mod, Vector3 faceNormal)
    {
        foreach (var port in mod.Definition.Ports)
        {
            if (Vector3.Dot(port.OutwardNormal, faceNormal) < 0.9f) continue;
            if (port.IsTerminal || port.IsDocking)           return true;
            if (port == mod.AttachmentPort)                  return true;
            if (mod.ChildPorts.Contains(port))               return true;
        }
        return false;
    }

    private static (Vector3 right, Vector3 up) TangentFrame(Vector3 n)
    {
        Vector3 hint  = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(hint, n));
        Vector3 up    = Vector3.Normalize(Vector3.Cross(right, n));
        return (right, up);
    }

    // ── Pass 1: Windows ───────────────────────────────────────────────────────

    private static void GenerateWindows(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed)  return;
        if (face.Width  < 3f) return;
        if (face.Height < 3f) return;

        Color baseCol = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color winCol  = WindowColor(baseCol);

        float gridW = MathF.Max(2f, face.Width  / 5f);
        float gridH = MathF.Max(2f, face.Height / 4f);
        float winW  = gridW * 0.45f;
        float winH  = gridH * 0.45f;

        int cols = Math.Max(1, (int)(face.Width  / gridW));
        int rows = Math.Max(1, (int)(face.Height / gridH));

        float startU = -(cols - 1) * gridW * 0.5f;
        float startV = -(rows - 1) * gridH * 0.5f;

        const float Z_OFFSET = 0.02f;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            if (rng.NextDouble() < 0.2) continue;

            Vector3 center = face.LocalCenter
                + face.LocalRight * (startU + col * gridW)
                + face.LocalUp    * (startV + row * gridH)
                + face.LocalNormal * Z_OFFSET;

            mesh.AddQuad(center, face.LocalNormal, face.LocalUp, winW, winH, winCol);
        }
    }

    // ── Pass 2: Hatches ───────────────────────────────────────────────────────

    private static void GenerateHatches(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed)  return;
        if (face.Width < 2f)  return;
        if (face.Height < 2f) return;
        if (MathF.Abs(face.LocalNormal.Y) > 0.5f) return;

        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color hatchCol = DarkenColor(baseCol, 0.65f);

        int count = rng.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * (face.Width  - 1.5f);
            float v = (float)(rng.NextDouble() - 0.5) * (face.Height - 1.5f);

            Vector3 center = face.LocalCenter
                + face.LocalRight  * u
                + face.LocalUp     * v
                + face.LocalNormal * 0.3f;

            float hw = (float)(rng.NextDouble() * 0.3f + 0.4f);
            float hh = (float)(rng.NextDouble() * 0.5f + 0.5f);

            var t = new Matrix(
                face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                center.X,           center.Y,           center.Z,           1
            );
            mesh.AddOrientedBox(t, new Vector3(hw * 2, hh * 2, 0.3f), hatchCol);
        }
    }

    // ── Pass 3: Antennas ─────────────────────────────────────────────────────

    private static void GenerateAntennas(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed)          return;
        if (face.LocalNormal.Y < -0.3f) return;
        if (rng.NextDouble() > 0.35)  return;

        Color baseCol    = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        int count = rng.Next(1, 3);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * face.Width  * 0.5f;
            float v = (float)(rng.NextDouble() - 0.5) * face.Height * 0.5f;

            Vector3 basePos = face.LocalCenter
                + face.LocalRight * u
                + face.LocalUp    * v;

            float length = (float)(rng.NextDouble() * 3.5f + 1.5f);
            float radius = (float)(rng.NextDouble() * 0.12f + 0.04f);

            mesh.AddSpike(basePos, face.LocalNormal, length, radius, antennaCol);

            if (rng.NextDouble() < 0.4f)
            {
                Vector3 tipCenter = basePos + face.LocalNormal * (length + 0.2f);
                float   dishSize  = radius * 5f;
                mesh.AddBox(tipCenter, new Vector3(dishSize, dishSize * 0.3f, dishSize), antennaCol);
            }
        }
    }

    // ── Pass 4: Pipes & Conduits ──────────────────────────────────────────────

    // 12 edges of the bounding box, each as (corner-A-index, corner-B-index).
    // Corner indices: 0=(-x,-y,-z) 1=(+x,-y,-z) 2=(+x,+y,-z) 3=(-x,+y,-z)
    //                 4=(-x,-y,+z) 5=(+x,-y,+z) 6=(+x,+y,+z) 7=(-x,+y,+z)
    private static readonly (int a, int b)[] BoxEdges =
    [
        (0,1),(1,2),(2,3),(3,0),  // -Z face ring
        (4,5),(5,6),(6,7),(7,4),  // +Z face ring
        (0,4),(1,5),(2,6),(3,7),  // Z-direction struts
    ];

    private static (float diameter, Color color) PipeSpec(string category) => category switch
    {
        "industrial" => (0.55f, new Color( 90,  75,  50)),
        "cargo"      => (0.45f, new Color( 80,  90,  60)),
        "hab"        => (0.30f, new Color( 70,  80, 100)),
        "connector"  => (0.25f, new Color( 60,  60,  60)),
        _            => (0.20f, new Color( 55,  55,  55)),
    };

    private static void GeneratePipes(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        // Only industrial, cargo, and connector modules get pipes.
        if (mod.Definition.Category is not ("industrial" or "cargo" or "connector"))
            return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        // Build the 8 AABB corners in local module space.
        Span<Vector3> corners = stackalloc Vector3[8]
        {
            new(-half.X, -half.Y, -half.Z),
            new(+half.X, -half.Y, -half.Z),
            new(+half.X, +half.Y, -half.Z),
            new(-half.X, +half.Y, -half.Z),
            new(-half.X, -half.Y, +half.Z),
            new(+half.X, -half.Y, +half.Z),
            new(+half.X, +half.Y, +half.Z),
            new(-half.X, +half.Y, +half.Z),
        };

        var (diameter, pipeColor) = PipeSpec(mod.Definition.Category);

        int edgeCount = rng.Next(2, 5);
        // Shuffle edge indices cheaply with a partial Fisher-Yates
        Span<int> edgeOrder = stackalloc int[12];
        for (int i = 0; i < 12; i++) edgeOrder[i] = i;
        for (int i = 0; i < edgeCount; i++)
        {
            int j = rng.Next(i, 12);
            (edgeOrder[i], edgeOrder[j]) = (edgeOrder[j], edgeOrder[i]);
        }

        for (int ei = 0; ei < edgeCount; ei++)
        {
            var (ai, bi) = BoxEdges[edgeOrder[ei]];
            Vector3 a = corners[ai];
            Vector3 b = corners[bi];

            // Offset the pipe slightly off the surface using the outward edge midpoint
            Vector3 mid  = (a + b) * 0.5f;
            Vector3 dir  = b - a;
            float   len  = dir.Length();
            if (len < 0.5f) continue;

            // Nudge outward from box centre so pipes sit on the surface
            Vector3 outward = Vector3.Normalize(new Vector3(
                MathF.Abs(mid.X) > 0.1f ? MathF.Sign(mid.X) : 0f,
                MathF.Abs(mid.Y) > 0.1f ? MathF.Sign(mid.Y) : 0f,
                MathF.Abs(mid.Z) > 0.1f ? MathF.Sign(mid.Z) : 0f
            ));
            if (outward == Vector3.Zero) outward = Vector3.UnitY;
            Vector3 center = mid + outward * (diameter * 0.5f + 0.05f);

            mesh.AddOrientedBox(center, Vector3.Normalize(dir), len, diameter, diameter, pipeColor);

            // Bracket rings every ~4 m along long pipes
            if (len > 6f)
            {
                int brackets = (int)(len / 4f);
                for (int k = 1; k <= brackets; k++)
                {
                    float t = (float)k / (brackets + 1);
                    Vector3 bracketPos = a + dir * t + outward * (diameter * 0.5f + 0.02f);
                    mesh.AddOrientedBox(bracketPos, Vector3.Normalize(dir),
                        diameter * 0.6f, diameter * 1.8f, diameter * 1.8f,
                        DarkenColor(pipeColor, 0.8f));
                }
            }
        }
    }

    // ── Pass 5: Lights ────────────────────────────────────────────────────────

    private static void GenerateLights(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        PlaceNavigationLights  (mod, mesh);
        PlaceWarningStrobes    (mod, mesh, rng);
        PlaceJunctionStrips    (mod, faces, mesh);
        PlaceBayGuidanceLights (mod, faces, mesh);
    }

    // Small housing + coloured lens quad. Returns the vertex base index of the lens.
    private static int AddLight(StationModuleMesh mesh,
        Vector3 position, Vector3 normal, float size, Color housing, Color lens)
    {
        const float depth = 0.15f;
        mesh.AddOrientedBox(position - normal * (depth * 0.5f), normal,
            depth, size * 1.4f, size * 1.4f, housing);
        return mesh.AddQuad(position + normal * 0.01f, normal,
            TangentFrame(normal).up, size, size, lens);
    }

    // Red port / green starboard / white tail on the extremity faces.
    private static void PlaceNavigationLights(PlacedModule mod, StationModuleMesh mesh)
    {
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        // +X face → green (starboard), -X face → red (port), -Z face → white (aft)
        (Vector3 normal, Vector3 pos, Color lens)[] navLights =
        [
            ( Vector3.UnitX,  new Vector3(+half.X, 0, 0),  new Color(  0, 220,  80)),
            (-Vector3.UnitX,  new Vector3(-half.X, 0, 0),  new Color(220,  30,  30)),
            (-Vector3.UnitZ,  new Vector3(0, half.Y * 0.5f, -half.Z), new Color(230, 230, 230)),
        ];

        Color housing = new(40, 40, 40);
        foreach (var (normal, pos, lens) in navLights)
        {
            // Only place on exposed faces
            if (IsFaceBlocked(mod, normal)) continue;
            int vb = AddLight(mesh, pos, normal, 0.4f, housing, lens);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Steady,
                VertexBase = vb,
                OnColor    = lens,
                OffColor   = DarkenColor(lens, 0.1f),
                Period     = 1f,
            });
        }
    }

    // Amber strobes on docking modules.
    private static void PlaceWarningStrobes(PlacedModule mod, StationModuleMesh mesh,
        System.Random rng)
    {
        if (mod.Definition.Category != "docking") return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;
        Color amber  = new(255, 160, 0);
        Color housing = new(40, 40, 40);

        // Two strobes on the top face, offset along the arm length (Z axis)
        float[] offsets = [-half.Z * 0.4f, half.Z * 0.4f];
        float phase = 0f;
        foreach (float zOff in offsets)
        {
            Vector3 pos = new(0, half.Y, zOff);
            int vb = AddLight(mesh, pos, Vector3.UnitY, 0.5f, housing, amber);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Strobe,
                VertexBase = vb,
                OnColor    = amber,
                OffColor   = new Color(30, 12, 0),
                Period     = 1.4f,
                Phase      = phase,
            });
            phase += 0.5f;
        }
    }

    // Small amber strip lights at each module junction (shared edges with children/parent).
    private static void PlaceJunctionStrips(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        Color amber   = new(200, 130, 20);
        Color housing = new(35, 35, 35);

        foreach (var face in faces)
        {
            if (face.IsExposed) continue;   // only blocked (connected) faces

            // Two small lights flanking the port centre
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 pos = face.LocalCenter
                    + face.LocalRight * (side * face.Width * 0.25f)
                    + face.LocalNormal * 0.05f;

                int vb = AddLight(mesh, pos, face.LocalNormal, 0.3f, housing, amber);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Steady,
                    VertexBase = vb,
                    OnColor    = amber,
                    OffColor   = DarkenColor(amber, 0.05f),
                    Period     = 1f,
                });
            }
        }
    }

    // White guidance strip along the docking face of docking arms.
    private static void PlaceBayGuidanceLights(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        if (mod.Definition.Category != "docking") return;

        Color white   = new(230, 240, 255);
        Color housing = new(35, 35, 35);

        // Find the +Z face (the docking pad face) if it is a terminal/docking port
        foreach (var port in mod.Definition.Ports)
        {
            if (!port.IsDocking) continue;

            // Find the matching FaceInfo
            FaceInfo? dockFace = null;
            foreach (var f in faces)
            {
                if (Vector3.Dot(f.LocalNormal, port.OutwardNormal) > 0.9f)
                { dockFace = f; break; }
            }
            if (dockFace is not FaceInfo df) continue;

            // Row of 4 guidance lights across the face
            for (int i = 0; i < 4; i++)
            {
                float u = ((float)i / 3f - 0.5f) * df.Width * 0.7f;
                Vector3 pos = df.LocalCenter
                    + df.LocalRight * u
                    - df.LocalUp * (df.Height * 0.35f)
                    + df.LocalNormal * 0.05f;

                int vb = AddLight(mesh, pos, df.LocalNormal, 0.35f, housing, white);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Pulse,
                    VertexBase = vb,
                    OnColor    = white,
                    OffColor   = new Color(20, 22, 30),
                    Period     = 2f,
                    Phase      = (float)i / 4f,
                });
            }
        }
    }

    // ── Colour helpers ────────────────────────────────────────────────────────

    private static Color DarkenColor(Color c, float factor) => new(
        (int)(c.R * factor),
        (int)(c.G * factor),
        (int)(c.B * factor),
        c.A);

    private static Color WindowColor(Color c) => new(
        Math.Min(255, c.R + 55),
        Math.Min(255, c.G + 55),
        Math.Min(255, c.B + 80),
        220);
}
