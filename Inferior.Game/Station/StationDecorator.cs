using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen;

// Adds per-module decoration geometry (windows, hatches, antennas, pipes, lights,
// chimneys, solar panels) to each PlacedModule.Mesh in local module space.
// Must be called after growth is complete so AttachmentPort and ChildPorts are
// fully populated. ApplyAmbientOcclusion is a separate pass run after BakeLighting.
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
            var chimneyRng = new System.Random(baseRng.Next());

            FaceInfo[] faces = ComputeFaces(mod);
            var mesh = new StationModuleMesh();

            foreach (var face in faces)
            {
                GenerateWindows (mod, face, windowRng,  mesh);
                GenerateHatches (mod, face, hatchRng,   mesh);
                GenerateAntennas(mod, face, antennaRng, mesh);
                GenerateChimneys(mod, face, chimneyRng, mesh);
            }

            GeneratePipes (mod, faces, pipeRng,  mesh);
            GenerateLights(mod, faces, lightRng, mesh);

            if (!mesh.IsEmpty)
                mod.Mesh = mesh;
        }

        // Station-wide passes that need all modules to be decorated first.
        int stationSeed = modules.Count > 0 ? modules[0].Seed : 42;
        PlaceLandmarkAntenna(modules, new System.Random(stationSeed ^ 0x12345678));
        RunSolarPanelPass   (modules, new System.Random(stationSeed ^ unchecked((int)0xABCDEF01)));
    }

    // Darkens decoration vertices that are on more-connected (occluded) modules.
    // Call after BakeLighting so lighting colours are already baked in.
    public static void ApplyAmbientOcclusion(IReadOnlyList<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            if (mod.Mesh == null) continue;
            FaceInfo[] faces = ComputeFaces(mod);
            int blockedFaces = 0;
            foreach (var f in faces) if (!f.IsExposed) blockedFaces++;

            float factor = blockedFaces switch
            {
                0 => 1.00f,
                1 => 0.92f,
                2 => 0.82f,
                _ => 0.70f,
            };
            if (factor >= 1.0f) continue;

            for (int i = 0; i < mod.Mesh.FaceCount; i++)
                mod.Mesh.MultiplyFaceColor(i, factor);
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
        Vector3 up    = Vector3.Normalize(Vector3.Cross(n, right));
        return (right, up);
    }

    // Returns a local-space point on a face using normalised UV coords in [-0.5, 0.5].
    private static Vector3 LocalPoint(FaceInfo face, float cu, float cv, float offset)
        => face.LocalCenter
         + face.LocalRight  * (cu * face.Width)
         + face.LocalUp     * (cv * face.Height)
         + face.LocalNormal * offset;

    // ── Pass 1: Windows ───────────────────────────────────────────────────────

    private static float WindowProbability(string category) => category switch
    {
        "hab"        => 0.80f,
        "science"    => 0.70f,
        "docking"    => 0.40f,
        "core"       => 0.30f,
        "industrial" => 0.30f,
        "connector"  => 0.20f,
        "cargo"      => 0.20f,
        _            => 0.25f,
    };

    private static Color PickWindowLightColor(string category, System.Random rng)
    {
        return category switch
        {
            "hab"        => rng.NextDouble() < 0.7
                             ? new Color(255, 240, 200)
                             : new Color(200, 220, 255),
            "science"    => rng.NextDouble() < 0.6
                             ? new Color(180, 210, 255)
                             : new Color(220, 255, 240),
            "industrial" => rng.NextDouble() < 0.5
                             ? new Color(255, 220, 160)
                             : new Color(200, 200, 180),
            "cargo"      => new Color(220, 210, 180),
            "docking"    => new Color(230, 240, 255),
            _            => new Color(210, 220, 230),
        };
    }

    private static void GenerateWindows(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed)  return;
        if (face.Width  < 3f) return;
        if (face.Height < 3f) return;
        if (rng.NextDouble() > WindowProbability(mod.Definition.Category)) return;
        if (rng.NextDouble() < 0.20) return;  // 20% blank face

        bool   sparse    = rng.NextDouble() < 0.30;
        float  gridW     = MathF.Max(2f, face.Width  / (sparse ? 3f : 5f));
        float  gridH     = MathF.Max(2f, face.Height / (sparse ? 3f : 4f));
        double sizeTier  = rng.NextDouble();
        float  sizeScale = sizeTier < 0.30 ? 0.55f : sizeTier < 0.70 ? 0.45f : 0.35f;
        float  winW      = gridW * sizeScale;
        float  winH      = gridH * sizeScale;

        int cols    = Math.Max(1, (int)(face.Width  / gridW));
        int rows    = Math.Max(1, (int)(face.Height / gridH));
        float startU = -(cols - 1) * gridW * 0.5f;
        float startV = -(rows - 1) * gridH * 0.5f;

        const float Z_OFFSET = 0.02f;
        bool canPorthole = mod.Definition.Category is "hab" or "science";
        Color winCol = PickWindowLightColor(mod.Definition.Category, rng);

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            if (rng.NextDouble() < 0.20) continue;

            Vector3 center = face.LocalCenter
                + face.LocalRight * (startU + col * gridW)
                + face.LocalUp    * (startV + row * gridH)
                + face.LocalNormal * Z_OFFSET;

            if (canPorthole && rng.NextDouble() < 0.20)
            {
                AddOctagonPorthole(mesh, center, face.LocalNormal, face.LocalUp,
                    MathF.Min(winW, winH), winCol);
            }
            else
            {
                mesh.AddQuad(center, face.LocalNormal, face.LocalUp, winW, winH, winCol);
                if (rng.NextDouble() < 0.55)
                    AddWindowBraces(mesh, center, face.LocalNormal, face.LocalUp,
                        winW, winH, DarkenColor(winCol, 0.30f));
            }
        }
    }

    // 8-sided porthole fan: AddTriangle(center, pts[i], pts[i+1]) with CCW angles.
    // Index [b,b+2,b+1] renders (center, pts[i+1], pts[i]) → CW in DirectX → visible.
    private static void AddOctagonPorthole(StationModuleMesh mesh,
        Vector3 center, Vector3 normal, Vector3 up, float size, Color color)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float r = size * 0.5f;
        var pts = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = MathF.PI / 8f + i * (MathF.PI / 4f);
            pts[i] = center + right * (r * MathF.Cos(angle)) + up * (r * MathF.Sin(angle));
        }
        for (int i = 0; i < 8; i++)
            mesh.AddTriangle(center, pts[i], pts[(i + 1) % 8], color);
    }

    // Cross-pane dividers within a window rect.
    private static void AddWindowBraces(StationModuleMesh mesh, Vector3 center,
        Vector3 normal, Vector3 up, float winW, float winH, Color color)
    {
        const float barThick = 0.04f;
        const float depth    = 0.025f;
        Vector3 pos = center + normal * depth;
        mesh.AddQuad(pos, normal, up, winW,     barThick, color);
        mesh.AddQuad(pos, normal, up, barThick, winH,     color);
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

    private static float AntennaHeight(System.Random rng)
    {
        double tier = rng.NextDouble();
        if (tier < 0.50) return (float)(rng.NextDouble() * 3.5 + 1.0);   // 1–4.5 m
        if (tier < 0.85) return (float)(rng.NextDouble() * 6.0 + 4.5);   // 4.5–10.5 m
        return (float)(rng.NextDouble() * 5.0 + 10.5);                    // 10.5–15.5 m
    }

    private static void GenerateAntennas(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed)           return;
        if (face.LocalNormal.Y < -0.3f) return;
        if (rng.NextDouble() > 0.35)   return;

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

            float length = AntennaHeight(rng);
            float radius = (float)(rng.NextDouble() * 0.12 + 0.04);

            mesh.AddSpike(basePos, face.LocalNormal, length, radius, antennaCol);

            if (rng.NextDouble() < 0.4)
            {
                const float stemLen = 1.2f;
                const float stemW   = 0.18f;
                Vector3 stemCenter = basePos + face.LocalNormal * (length + stemLen * 0.5f);
                mesh.AddOrientedBox(stemCenter, face.LocalNormal, stemLen, stemW, stemW, antennaCol);

                float   dishSize  = radius * 5f;
                Vector3 tipCenter = basePos + face.LocalNormal * (length + stemLen + 0.2f);
                mesh.AddBox(tipCenter, new Vector3(dishSize, dishSize * 0.3f, dishSize), antennaCol);
            }
        }
    }

    // Places one landmark antenna on the most sun-upward exposed science/core face.
    private static void PlaceLandmarkAntenna(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        FaceInfo?     bestFace = null;
        PlacedModule? bestMod  = null;
        float         bestDot  = float.MinValue;

        foreach (var mod in modules)
        {
            if (mod.Definition.Category is not ("science" or "core")) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                float dot = Vector3.Transform(face.LocalNormal, rot).Y;
                if (dot > bestDot) { bestDot = dot; bestFace = face; bestMod = mod; }
            }
        }

        if (bestMod == null || bestFace == null || bestDot < 0.5f) return;

        var f = bestFace.Value;
        bestMod.Mesh ??= new StationModuleMesh();

        Color baseCol    = StationModuleRegistry.CategoryColor(bestMod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        float height = (float)(rng.NextDouble() * 9.0 + 18.0); // 18–27 m
        bestMod.Mesh.AddSpike(f.LocalCenter, f.LocalNormal, height, 0.15f, antennaCol);

        const float stemLen = 1.5f;
        Vector3 stemCenter = f.LocalCenter + f.LocalNormal * (height + stemLen * 0.5f);
        bestMod.Mesh.AddOrientedBox(stemCenter, f.LocalNormal, stemLen, 0.25f, 0.25f, antennaCol);
        Vector3 tipCenter = f.LocalCenter + f.LocalNormal * (height + stemLen + 0.3f);
        bestMod.Mesh.AddBox(tipCenter, new Vector3(2.0f, 0.6f, 2.0f), antennaCol);
    }

    // ── Solar panels ─────────────────────────────────────────────────────────

    private static void RunSolarPanelPass(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        if (rng.NextDouble() > 0.20) return;

        Vector3 sunDir = SceneLighting.SunDirection;
        var candidates = new List<(PlacedModule mod, FaceInfo face, float dot)>();

        foreach (var mod in modules)
        {
            if (mod.Definition.Category is not ("core" or "connector")) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                Vector3 worldN = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
                float dot = Vector3.Dot(worldN, sunDir);
                if (dot > 0.25f) candidates.Add((mod, face, dot));
            }
        }

        if (candidates.Count == 0) return;
        candidates.Sort((a, b) => b.dot.CompareTo(a.dot));

        int count = Math.Min(rng.Next(1, 4), candidates.Count);
        for (int i = 0; i < count; i++)
        {
            var (mod, face, _) = candidates[i];
            mod.Mesh ??= new StationModuleMesh();
            AddSolarPanelArray(mod.Mesh, face, rng);
        }
    }

    private static void AddSolarPanelArray(StationModuleMesh mesh, FaceInfo face, System.Random rng)
    {
        Color frameCol = new(60, 60, 70);
        Color cellCol  = new(30, 50, 100);

        float armLen = (float)(rng.NextDouble() * 3.0 + 4.0);
        Vector3 armCenter = face.LocalCenter + face.LocalNormal * (armLen * 0.5f + 0.5f);
        mesh.AddOrientedBox(armCenter, face.LocalNormal, armLen, 0.3f, 0.3f, frameCol);

        float panelW = (float)(rng.NextDouble() * 4.0 + 6.0);
        float panelH = (float)(rng.NextDouble() * 1.5 + 2.0);
        Vector3 panelCenter = face.LocalCenter + face.LocalNormal * (armLen + 0.8f);

        var t = new Matrix(
            face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
            face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
            face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
            panelCenter.X,      panelCenter.Y,      panelCenter.Z,      1
        );
        mesh.AddOrientedBox(t, new Vector3(panelW, panelH, 0.10f), frameCol);

        int   cellCols = Math.Max(1, (int)(panelW / 2f));
        int   cellRows = Math.Max(1, (int)(panelH / 1f));
        float stepU    = panelW / cellCols;
        float stepV    = panelH / cellRows;
        float startCU  = -(cellCols - 1) * stepU * 0.5f;
        float startCV  = -(cellRows - 1) * stepV * 0.5f;
        float cellW    = stepU * 0.88f;
        float cellH    = stepV * 0.88f;

        for (int r = 0; r < cellRows; r++)
        for (int c = 0; c < cellCols; c++)
        {
            Vector3 cc = panelCenter
                + face.LocalRight * (startCU + c * stepU)
                + face.LocalUp    * (startCV + r * stepV)
                + face.LocalNormal * 0.06f;
            mesh.AddQuad(cc, face.LocalNormal, face.LocalUp, cellW, cellH, cellCol);
        }

        for (int r = 0; r < cellRows - 1; r++)
        {
            Vector3 dc = panelCenter
                + face.LocalUp    * (startCV + (r + 0.5f) * stepV)
                + face.LocalNormal * 0.07f;
            mesh.AddQuad(dc, face.LocalNormal, face.LocalUp, panelW * 0.98f, 0.07f, frameCol);
        }
        for (int c = 0; c < cellCols - 1; c++)
        {
            Vector3 dc = panelCenter
                + face.LocalRight  * (startCU + (c + 0.5f) * stepU)
                + face.LocalNormal * 0.07f;
            mesh.AddQuad(dc, face.LocalNormal, face.LocalUp, 0.07f, panelH * 0.98f, frameCol);
        }
    }

    // ── Pass 4: Chimneys & Exhausts ──────────────────────────────────────────

    private static void GenerateChimneys(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        float prob = mod.Definition.Category switch
        {
            "industrial" => 0.75f,
            "core"       => 0.35f,
            _            => 0f,
        };
        if (rng.NextDouble() > prob) return;

        Color baseCol = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        int   count   = rng.Next(1, mod.Definition.Category == "industrial" ? 4 : 3);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * face.Width  * 0.6f;
            float v = (float)(rng.NextDouble() - 0.5) * face.Height * 0.6f;
            Vector3 basePos = face.LocalCenter
                + face.LocalRight * u
                + face.LocalUp    * v;
            AddChimney(mesh, basePos, face.LocalNormal, rng, baseCol);
        }
    }

    private static void AddChimney(StationModuleMesh mesh, Vector3 basePos,
        Vector3 normal, System.Random rng, Color baseCol)
    {
        Color chimCol = DarkenColor(baseCol, 0.55f);
        Color tipCol  = new(50, 45, 40);

        if (rng.NextDouble() < 0.55)
        {
            float height = (float)(rng.NextDouble() * 4.0 + 2.5);
            float radius = (float)(rng.NextDouble() * 0.2  + 0.15);
            mesh.AddOrientedBox(basePos + normal * (height * 0.5f),
                normal, height, radius * 2, radius * 2, chimCol);
            mesh.AddOrientedBox(basePos + normal * (height + 0.12f),
                normal, 0.25f, radius * 2.2f, radius * 2.2f, tipCol);
        }
        else
        {
            float baseR  = (float)(rng.NextDouble() * 0.4 + 0.4);
            float exitR  = baseR * 0.5f;
            float height = (float)(rng.NextDouble() * 1.5 + 1.0);
            mesh.AddOrientedBox(basePos + normal * (height * 0.3f),
                normal, height * 0.6f, baseR * 2, baseR * 2, chimCol);
            mesh.AddOrientedBox(basePos + normal * (height * 0.8f),
                normal, height * 0.4f, exitR * 2, exitR * 2, tipCol);
        }
    }

    // ── Pass 5: Pipes & Conduits ──────────────────────────────────────────────

    private static readonly (int a, int b)[] BoxEdges =
    [
        (0,1),(1,2),(2,3),(3,0),
        (4,5),(5,6),(6,7),(7,4),
        (0,4),(1,5),(2,6),(3,7),
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
        if (mod.Definition.Category is not ("industrial" or "cargo" or "connector"))
            return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

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

            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            float   len = dir.Length();
            if (len < 0.5f) continue;

            Vector3 outward = Vector3.Normalize(new Vector3(
                MathF.Abs(mid.X) > 0.1f ? MathF.Sign(mid.X) : 0f,
                MathF.Abs(mid.Y) > 0.1f ? MathF.Sign(mid.Y) : 0f,
                MathF.Abs(mid.Z) > 0.1f ? MathF.Sign(mid.Z) : 0f
            ));
            if (outward == Vector3.Zero) outward = Vector3.UnitY;
            Vector3 center = mid + outward * (diameter * 0.5f + 0.05f);

            mesh.AddOrientedBox(center, Vector3.Normalize(dir), len, diameter, diameter, pipeColor);

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

    // ── Pass 6: Lights ────────────────────────────────────────────────────────

    private static void GenerateLights(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        PlaceNavigationLights  (mod, mesh);
        PlaceWarningStrobes    (mod, mesh, rng);
        PlaceJunctionStrips    (mod, faces, mesh);
        PlaceBayGuidanceLights (mod, faces, mesh);
    }

    private static int AddLight(StationModuleMesh mesh,
        Vector3 position, Vector3 normal, float size, Color housing, Color lens)
    {
        const float depth = 0.15f;
        mesh.AddOrientedBox(position - normal * (depth * 0.5f), normal,
            depth, size * 1.4f, size * 1.4f, housing);
        return mesh.AddQuad(position + normal * 0.01f, normal,
            TangentFrame(normal).up, size, size, lens);
    }

    private static void PlaceNavigationLights(PlacedModule mod, StationModuleMesh mesh)
    {
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        (Vector3 normal, Vector3 pos, Color lens)[] navLights =
        [
            ( Vector3.UnitX,  new Vector3(+half.X, 0, 0),  new Color(  0, 220,  80)),
            (-Vector3.UnitX,  new Vector3(-half.X, 0, 0),  new Color(220,  30,  30)),
            (-Vector3.UnitZ,  new Vector3(0, half.Y * 0.5f, -half.Z), new Color(230, 230, 230)),
        ];

        Color housing = new(40, 40, 40);
        foreach (var (normal, pos, lens) in navLights)
        {
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
            mod.GlowLights.Add(new StationLightInfo(pos, lens, GlowType.NavigationLight));
        }
    }

    private static void PlaceWarningStrobes(PlacedModule mod, StationModuleMesh mesh,
        System.Random rng)
    {
        if (mod.Definition.Category != "docking") return;

        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;
        Color amber   = new(255, 160, 0);
        Color housing = new(40, 40, 40);

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
            mod.GlowLights.Add(new StationLightInfo(pos, amber, GlowType.WarningStrobe));
            phase += 0.5f;
        }
    }

    private static void PlaceJunctionStrips(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        Color amber   = new(200, 130, 20);
        Color housing = new(35, 35, 35);

        foreach (var face in faces)
        {
            if (face.IsExposed) continue;

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

    private static void PlaceBayGuidanceLights(PlacedModule mod, FaceInfo[] faces,
        StationModuleMesh mesh)
    {
        if (mod.Definition.Category != "docking") return;

        Color white   = new(230, 240, 255);
        Color housing = new(35, 35, 35);

        foreach (var port in mod.Definition.Ports)
        {
            if (!port.IsDocking) continue;

            FaceInfo? dockFace = null;
            foreach (var f in faces)
            {
                if (Vector3.Dot(f.LocalNormal, port.OutwardNormal) > 0.9f)
                { dockFace = f; break; }
            }
            if (dockFace is not FaceInfo df) continue;

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
}
