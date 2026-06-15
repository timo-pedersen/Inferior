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
            var baseRng        = new System.Random(mod.Seed);
            var windowRng      = new System.Random(baseRng.Next());
            var hatchRng       = new System.Random(baseRng.Next());
            var antennaRng     = new System.Random(baseRng.Next());
            var pipeRng        = new System.Random(baseRng.Next());
            var lightRng       = new System.Random(baseRng.Next());
            var chimneyRng     = new System.Random(baseRng.Next());
            var surfacePipeRng = new System.Random(baseRng.Next());
            // New passes — appended so existing seeds are unchanged
            var seamRng        = new System.Random(baseRng.Next());
            var ventRng        = new System.Random(baseRng.Next());
            var greebleRng     = new System.Random(baseRng.Next());
            var edgeTrimRng    = new System.Random(baseRng.Next());

            FaceInfo[] faces = ComputeFaces(mod);
            var mesh = new StationModuleMesh();

            foreach (var face in faces)
            {
                var occupancy = new FaceOccupancy();
                GeneratePanelSeams   (mod, face, seamRng,        mesh);
                GenerateWindows      (mod, face, windowRng,      mesh, occupancy);
                GenerateHatches      (mod, face, hatchRng,       mesh, occupancy);
                GenerateAntennas     (mod, face, antennaRng,     mesh);
                GenerateChimneys     (mod, face, chimneyRng,     mesh);
                GenerateSurfacePipes (mod, face, surfacePipeRng, mesh);
                GenerateVentGrilles  (mod, face, ventRng,        mesh, occupancy);
                GenerateGreebles     (mod, face, greebleRng,     mesh, occupancy);
            }

            GeneratePipes          (mod, faces, pipeRng,     mesh);
            GenerateLights         (mod, faces, lightRng,    mesh);
            GenerateEdgeTrimStrips (mod, faces, edgeTrimRng, mesh);

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

    // Returns a local-space point on a face using absolute metre offsets from face centre.
    private static Vector3 LocalPointAbs(FaceInfo face, float u, float v, float offset)
        => face.LocalCenter
         + face.LocalRight  * u
         + face.LocalUp     * v
         + face.LocalNormal * offset;

    // ── Face occupancy ────────────────────────────────────────────────────────

    // Tracks rectangular regions already occupied on a face (absolute metre offsets).
    private sealed class FaceOccupancy
    {
        private readonly List<(float u0, float v0, float u1, float v1)> _regions = [];

        public bool IsClear(float u0, float v0, float u1, float v1, float margin = 0.15f)
        {
            float mu0 = u0 - margin, mv0 = v0 - margin;
            float mu1 = u1 + margin, mv1 = v1 + margin;
            return !_regions.Any(r =>
                mu1 > r.u0 && mu0 < r.u1 &&
                mv1 > r.v0 && mv0 < r.v1);
        }

        public void Occupy(float u0, float v0, float u1, float v1)
            => _regions.Add((u0, v0, u1, v1));

        public bool TryOccupy(float cu, float cv, float halfW, float halfH, float margin = 0.15f)
        {
            if (!IsClear(cu - halfW, cv - halfH, cu + halfW, cv + halfH, margin))
                return false;
            Occupy(cu - halfW, cv - halfH, cu + halfW, cv + halfH);
            return true;
        }
    }

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
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
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

            float cu = startU + col * gridW;
            float cv = startV + row * gridH;
            if (!occupancy.TryOccupy(cu, cv, winW * 0.5f, winH * 0.5f)) continue;

            Vector3 center = face.LocalCenter
                + face.LocalRight * cu
                + face.LocalUp    * cv
                + face.LocalNormal * Z_OFFSET;

            if (canPorthole && rng.NextDouble() < 0.20)
            {
                float portholeSize = MathF.Min(winW, winH);
                if (rng.NextDouble() < 0.25)
                    AddCupola(mesh, center, face.LocalNormal, face.LocalUp, portholeSize, winCol);
                else
                    AddOctagonPorthole(mesh, center, face.LocalNormal, face.LocalUp, portholeSize, winCol);
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

    // Pyramid viewport: 4 triangular glass panels meeting at a raised apex point.
    private static void AddCupola(StationModuleMesh mesh,
        Vector3 center, Vector3 normal, Vector3 up, float size, Color glassColor)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float hw = size * 0.5f;
        Vector3 apex = center + normal * (size * 0.5f);
        // Base corners in CCW order when viewed from normal side (matches octagon convention).
        Vector3[] base4 =
        [
            center - right * hw - up * hw,  // BL
            center - right * hw + up * hw,  // TL
            center + right * hw + up * hw,  // TR
            center + right * hw - up * hw,  // BR
        ];
        for (int i = 0; i < 4; i++)
            mesh.AddTriangle(apex, base4[i], base4[(i + 1) % 4], glassColor);
        // Dark inner base so the opening reads as a recess.
        mesh.AddQuad(base4[0], base4[3], base4[2], base4[1], new Color(20, 22, 28));
    }

    // ── Pass 2: Hatches ───────────────────────────────────────────────────────

    private static void GenerateHatches(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
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
            float u  = (float)(rng.NextDouble() - 0.5) * (face.Width  - 1.5f);
            float v  = (float)(rng.NextDouble() - 0.5) * (face.Height - 1.5f);
            float hw = (float)(rng.NextDouble() * 0.3f + 0.4f);
            float hh = (float)(rng.NextDouble() * 0.5f + 0.5f);

            if (!occupancy.TryOccupy(u, v, hw, hh)) continue;

            Vector3 center = face.LocalCenter
                + face.LocalRight  * u
                + face.LocalUp     * v
                + face.LocalNormal * 0.3f;

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

    // Per-edge: faceA normal, faceB normal, axis direction, corner signs (0 on axis dimension).
    private static readonly (Vector3 faceA, Vector3 faceB, Vector3 edgeDir, Vector3 cornerSign)[] BoxEdgeInfos =
    [
        // X-axis edges
        (-Vector3.UnitY, -Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, -1, -1)),
        ( Vector3.UnitY, -Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, +1, -1)),
        (-Vector3.UnitY,  Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, -1, +1)),
        ( Vector3.UnitY,  Vector3.UnitZ,  Vector3.UnitX, new Vector3( 0, +1, +1)),
        // Y-axis edges
        ( Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY, new Vector3(+1,  0, -1)),
        (-Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY, new Vector3(-1,  0, -1)),
        ( Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY, new Vector3(+1,  0, +1)),
        (-Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY, new Vector3(-1,  0, +1)),
        // Z-axis edges
        ( Vector3.UnitX, -Vector3.UnitY,  Vector3.UnitZ, new Vector3(+1, -1,  0)),
        (-Vector3.UnitX, -Vector3.UnitY,  Vector3.UnitZ, new Vector3(-1, -1,  0)),
        ( Vector3.UnitX,  Vector3.UnitY,  Vector3.UnitZ, new Vector3(+1, +1,  0)),
        (-Vector3.UnitX,  Vector3.UnitY,  Vector3.UnitZ, new Vector3(-1, +1,  0)),
    ];

    private static int PipeSides(System.Random rng) => rng.NextDouble() switch
    {
        < 0.40 => 4,
        < 0.75 => 6,
        _      => 8,
    };

    private static (float radius, int sides, Color colour) PipeSpec(string category, System.Random rng)
    {
        double roll = rng.NextDouble();
        return category switch
        {
            "industrial" or "fuel" => roll < 0.20
                ? (0.80f, 8, new Color(80,  80,  80))
                : roll < 0.55
                ? (0.45f, 6, new Color(95,  90,  85))
                : (0.22f, 6, new Color(120, 120, 120)),

            "core" => roll < 0.15
                ? (0.90f, 8, new Color(75,  75,  80))
                : roll < 0.50
                ? (0.35f, 6, new Color(100, 100, 110))
                : (0.18f, 4, new Color(125, 125, 130)),

            "cargo" => roll < 0.30
                ? (0.50f, 6, new Color(155, 100, 50))
                : (0.28f, 4, new Color(165, 110, 55)),

            _ => roll < 0.25
                ? (0.22f, 6, new Color(120, 120, 120))
                : (0.10f, 4, new Color(135, 135, 140)),
        };
    }

    private static void GeneratePipes(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        if (mod.Definition.Category is not ("industrial" or "cargo" or "connector" or "core"))
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
            var (radius, sides, pipeColor) = PipeSpec(mod.Definition.Category, rng);

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

            Vector3 pipeDir = Vector3.Normalize(dir);
            Vector3 center  = mid + outward * (radius + 0.05f);
            mesh.AddPrismPipe(center - pipeDir * (len * 0.5f),
                              center + pipeDir * (len * 0.5f),
                              radius, sides, pipeColor);

            if (len > 6f)
            {
                int   brackets    = (int)(len / 4f);
                float bracketSize = radius * 3.6f;  // 1.8× diameter
                for (int k = 1; k <= brackets; k++)
                {
                    float   t          = (float)k / (brackets + 1);
                    Vector3 bracketPos = a + dir * t + outward * (radius + 0.02f);
                    mesh.AddOrientedBox(bracketPos, pipeDir,
                        radius * 1.2f, bracketSize, bracketSize,
                        DarkenColor(pipeColor, 0.8f));
                }
            }
        }
    }

    // ── Surface pipe runs ─────────────────────────────────────────────────────

    private static Color SurfacePipeColour(string category, System.Random rng) => category switch
    {
        "industrial" or "fuel" => rng.NextDouble() < 0.5
            ? new Color(160, 105, 50)
            : new Color(85,  85,  85),
        "science"    => new Color(100, 130, 160),
        "cargo"      => new Color(155, 100, 50),
        _            => new Color(118, 118, 118),
    };

    private static void GenerateSurfacePipes(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 40f) return;
        if (rng.NextDouble() > 0.45) return;

        int runCount = rng.Next(1, 4);

        for (int i = 0; i < runCount; i++)
        {
            bool    horizontal  = rng.NextDouble() < 0.5;
            Vector3 runDir      = horizontal ? face.LocalRight : face.LocalUp;
            Vector3 perpDir     = horizontal ? face.LocalUp    : face.LocalRight;
            float   runSpan     = horizontal ? face.Width  : face.Height;
            float   perpSpan    = horizontal ? face.Height : face.Width;

            float maxPerpOff = (perpSpan - 3f) * 0.5f;
            if (maxPerpOff <= 0f) continue;
            float perpOff = (float)(rng.NextDouble() - 0.5) * 2f * maxPerpOff;

            float runHalfLen = runSpan * 0.5f - 1.5f;
            if (runHalfLen <= 0.5f) continue;

            double sizeRoll = rng.NextDouble();
            float  radius   = sizeRoll < 0.35 ? 0.10f : sizeRoll < 0.70 ? 0.22f : 0.40f;
            int    sides    = PipeSides(rng);
            Color  colour   = SurfacePipeColour(mod.Definition.Category, rng);
            float  bracketH = radius + 0.35f + (float)rng.NextDouble() * 0.45f;

            Vector3 pipeCtr   = face.LocalCenter + perpDir * perpOff + face.LocalNormal * bracketH;
            Vector3 pipeStart = pipeCtr - runDir * runHalfLen;
            Vector3 pipeEnd   = pipeCtr + runDir * runHalfLen;

            mesh.AddPrismPipe(pipeStart, pipeEnd, radius, sides, colour);
            AddPipeBrackets(mesh, pipeStart, pipeEnd, runDir, perpDir,
                            face.LocalNormal, radius, bracketH, colour, rng);
        }
    }

    private static void AddPipeBrackets(StationModuleMesh mesh,
        Vector3 pipeStart, Vector3 pipeEnd,
        Vector3 runDir,    Vector3 perpDir, Vector3 faceNormal,
        float pipeRadius,  float bracketHeight, Color pipeColour,
        System.Random rng)
    {
        const float legThick = 0.055f;
        float   legHeight  = MathF.Max(0.1f, bracketHeight - pipeRadius);
        float   runLength  = Vector3.Distance(pipeStart, pipeEnd);
        float   spacing    = 3.5f + (float)rng.NextDouble() * 2f;
        int     count      = Math.Max(2, (int)(runLength / spacing));
        Color   col        = DarkenColor(pipeColour, 0.65f);

        for (int b = 0; b <= count; b++)
        {
            float   t         = (float)b / count;
            Vector3 pipePos   = Vector3.Lerp(pipeStart, pipeEnd, t);
            Vector3 basePoint = pipePos - faceNormal * bracketHeight;  // ~face surface

            // Left leg
            Vector3 lBase = basePoint - perpDir * pipeRadius;
            mesh.AddOrientedBox(lBase + faceNormal * (legHeight * 0.5f),
                faceNormal, legHeight, legThick, legThick, col);

            // Right leg
            Vector3 rBase = basePoint + perpDir * pipeRadius;
            mesh.AddOrientedBox(rBase + faceNormal * (legHeight * 0.5f),
                faceNormal, legHeight, legThick, legThick, col);

            // Crossbar connecting leg tops
            Vector3 crossCenter = basePoint + faceNormal * legHeight;
            mesh.AddOrientedBox(crossCenter, perpDir,
                pipeRadius * 2f + legThick, legThick, legThick, col);
        }
    }

    // ── Pass 6a: Panel seam lines ─────────────────────────────────────────────

    private static void GeneratePanelSeams(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 25f) return;

        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color seamColor = DarkenColor(baseCol, 0.48f);
        const float seamWidth  = 0.038f;
        const float seamOffset = 0.012f;

        float hw = face.Width  * 0.5f;
        float hh = face.Height * 0.5f;
        float hs = seamWidth * 0.5f;

        int hSeams = rng.NextDouble() < 0.55 ? 2 : 1;
        for (int i = 0; i < hSeams; i++)
        {
            float t    = hSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float vOff = -hh + face.Height * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Height * 0.08f;

            Vector3 v0 = LocalPointAbs(face, -hw,  vOff - hs, seamOffset);
            Vector3 v1 = LocalPointAbs(face, +hw,  vOff - hs, seamOffset);
            Vector3 v2 = LocalPointAbs(face, +hw,  vOff + hs, seamOffset);
            Vector3 v3 = LocalPointAbs(face, -hw,  vOff + hs, seamOffset);
            mesh.AddQuad(v0, v1, v2, v3, seamColor);
        }

        int vSeams = face.Width > 20f ? (rng.NextDouble() < 0.6 ? 2 : 1) : 1;
        for (int i = 0; i < vSeams; i++)
        {
            float t    = vSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float uOff = -hw + face.Width * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Width * 0.08f;

            Vector3 v0 = LocalPointAbs(face, uOff - hs, -hh, seamOffset);
            Vector3 v1 = LocalPointAbs(face, uOff + hs, -hh, seamOffset);
            Vector3 v2 = LocalPointAbs(face, uOff + hs, +hh, seamOffset);
            Vector3 v3 = LocalPointAbs(face, uOff - hs, +hh, seamOffset);
            mesh.AddQuad(v0, v1, v2, v3, seamColor);
        }
    }

    // ── Pass 6b: Edge trim strips ─────────────────────────────────────────────

    private static void GenerateEdgeTrimStrips(PlacedModule mod, FaceInfo[] faces,
        System.Random rng, StationModuleMesh mesh)
    {
        Vector3 half = mod.Definition.BoundingBox * 0.5f;

        // Build a fast set of exposed face normals.
        var exposed = new HashSet<Vector3>();
        foreach (var f in faces)
            if (f.IsExposed) exposed.Add(f.LocalNormal);

        Color trimColor = LightenColor(
            StationModuleRegistry.CategoryColor(mod.Definition.Category), 1.12f);
        const float chamferW = 0.38f;
        float inset = chamferW * 0.707f;

        foreach (var (faceA, faceB, edgeDir, cornerSign) in BoxEdgeInfos)
        {
            if (!exposed.Contains(faceA) || !exposed.Contains(faceB)) continue;

            float edgeHalfLen = edgeDir.X != 0 ? half.X
                              : edgeDir.Y != 0 ? half.Y : half.Z;

            Vector3 edgeMid = new(
                edgeDir.X != 0 ? 0 : cornerSign.X * half.X,
                edgeDir.Y != 0 ? 0 : cornerSign.Y * half.Y,
                edgeDir.Z != 0 ? 0 : cornerSign.Z * half.Z);

            Vector3 intoA = -faceA;
            Vector3 intoB = -faceB;

            Vector3 a0 = edgeMid - edgeDir * edgeHalfLen + intoA * inset + faceA * 0.01f;
            Vector3 a1 = edgeMid + edgeDir * edgeHalfLen + intoA * inset + faceA * 0.01f;
            Vector3 b0 = edgeMid - edgeDir * edgeHalfLen + intoB * inset + faceB * 0.01f;
            Vector3 b1 = edgeMid + edgeDir * edgeHalfLen + intoB * inset + faceB * 0.01f;

            mesh.AddQuad(a0, a1, b1, b0, trimColor);
        }
    }

    // ── Pass 6c: Vent grilles ─────────────────────────────────────────────────

    private static void GenerateVentGrilles(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 15f) return;

        float prob = mod.Definition.Category switch
        {
            "industrial" or "core" => 0.65f,
            "cargo"      or "fuel" => 0.45f,
            "connector"            => 0.35f,
            _                      => 0.20f,
        };
        if (rng.NextDouble() > prob) return;

        int remaining = rng.Next(1, 4);
        int attempts  = remaining * 4;
        float margin  = 1.2f;

        for (int i = 0; i < attempts && remaining > 0; i++)
        {
            float ventW = 0.8f  + (float)rng.NextDouble() * 1.4f;
            float ventH = 0.45f + (float)rng.NextDouble() * 0.7f;

            float cu = ((float)rng.NextDouble() - 0.5f) * (face.Width  - margin * 2 - ventW);
            float cv = ((float)rng.NextDouble() - 0.5f) * (face.Height - margin * 2 - ventH);

            if (!occupancy.TryOccupy(cu, cv, ventW * 0.5f, ventH * 0.5f)) continue;
            remaining--;

            AddVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
        }
    }

    private static void AddVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol   = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color frameCol  = DarkenColor(baseCol, 0.58f);
        Color shadowCol = new Color(12, 12, 14);
        Color barCol    = DarkenColor(baseCol, 0.45f);

        float hw = ventW * 0.5f;
        float hh = ventH * 0.5f;
        const float frameW   = 0.12f;
        const float frameOff = 0.025f;
        const float shadowOff = 0.018f;
        const float barOff   = 0.030f;

        // Frame — top, bottom, left, right bars
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - frameW, cv + hh,          frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv + hh,          frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv + hh + frameW, frameOff),
            LocalPointAbs(face, cu - hw - frameW, cv + hh + frameW, frameOff), frameCol);

        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - frameW, cv - hh - frameW, frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv - hh - frameW, frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv - hh,          frameOff),
            LocalPointAbs(face, cu - hw - frameW, cv - hh,          frameOff), frameCol);

        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - frameW, cv - hh, frameOff),
            LocalPointAbs(face, cu - hw,          cv - hh, frameOff),
            LocalPointAbs(face, cu - hw,          cv + hh, frameOff),
            LocalPointAbs(face, cu - hw - frameW, cv + hh, frameOff), frameCol);

        mesh.AddQuad(
            LocalPointAbs(face, cu + hw,          cv - hh, frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv - hh, frameOff),
            LocalPointAbs(face, cu + hw + frameW, cv + hh, frameOff),
            LocalPointAbs(face, cu + hw,          cv + hh, frameOff), frameCol);

        // Dark recess
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv + hh, shadowOff),
            LocalPointAbs(face, cu - hw, cv + hh, shadowOff), shadowCol);

        // Grille bars
        bool horizontal = rng.NextDouble() < 0.6;
        int  barCount   = rng.Next(3, 8);
        const float barThick = 0.04f;

        for (int b = 0; b < barCount; b++)
        {
            float t   = (b + 0.5f) / barCount;
            float pos = horizontal
                ? cv - hh + ventH * t
                : cu - hw + ventW * t;

            float b0u = horizontal ? cu - hw  : pos - barThick * 0.5f;
            float b0v = horizontal ? pos - barThick * 0.5f : cv - hh;
            float b1u = horizontal ? cu + hw  : pos + barThick * 0.5f;
            float b1v = horizontal ? pos + barThick * 0.5f : cv + hh;

            mesh.AddQuad(
                LocalPointAbs(face, b0u, b0v, barOff),
                LocalPointAbs(face, b1u, b0v, barOff),
                LocalPointAbs(face, b1u, b1v, barOff),
                LocalPointAbs(face, b0u, b1v, barOff), barCol);
        }
    }

    // ── Pass 6d: Greeble boxes ────────────────────────────────────────────────

    private enum GreebleType
    {
        JunctionBox, EquipmentHousing, ConduitEntry, SensorPod, TechPanel, ValveAssembly
    }

    private static GreebleType SelectGreebleType(string category, System.Random rng)
    {
        return category switch
        {
            "industrial" or "core" => (GreebleType)rng.Next(0, 6),
            "cargo"      or "fuel" => rng.NextDouble() < 0.5
                ? GreebleType.ValveAssembly : GreebleType.ConduitEntry,
            "science"              => rng.NextDouble() < 0.6
                ? GreebleType.SensorPod : GreebleType.TechPanel,
            "hab"                  => rng.NextDouble() < 0.5
                ? GreebleType.JunctionBox : GreebleType.TechPanel,
            _                      => (GreebleType)rng.Next(0, 3),
        };
    }

    private static (float halfW, float halfH) GreebleFootprint(GreebleType type, System.Random rng) => type switch
    {
        GreebleType.JunctionBox      => (0.40f + (float)rng.NextDouble() * 0.10f,
                                         0.30f + (float)rng.NextDouble() * 0.10f),
        GreebleType.EquipmentHousing => (0.75f + (float)rng.NextDouble() * 0.20f,
                                         0.50f + (float)rng.NextDouble() * 0.15f),
        GreebleType.ConduitEntry     => (0.35f, 0.35f),
        GreebleType.SensorPod        => (0.30f, 0.30f),
        GreebleType.TechPanel        => (0.60f + (float)rng.NextDouble() * 0.20f,
                                         0.45f + (float)rng.NextDouble() * 0.15f),
        GreebleType.ValveAssembly    => (0.45f, 0.45f),
        _                            => (0.35f, 0.35f),
    };

    private static void GenerateGreebles(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 12f) return;

        float prob = mod.Definition.Category switch
        {
            "industrial" or "core" => 0.90f,
            "cargo"      or "fuel" => 0.70f,
            "science"              => 0.60f,
            "connector"            => 0.50f,
            "hab"                  => 0.35f,
            _                      => 0.20f,
        };
        if (rng.NextDouble() > prob) return;

        int count    = rng.Next(2, 7);
        int attempts = count * 5;

        for (int i = 0; i < attempts && count > 0; i++)
        {
            var type       = SelectGreebleType(mod.Definition.Category, rng);
            var (hw, hh)   = GreebleFootprint(type, rng);
            const float margin = 0.8f;

            float cu = ((float)rng.NextDouble() - 0.5f) * (face.Width  - margin * 2 - hw * 2);
            float cv = ((float)rng.NextDouble() - 0.5f) * (face.Height - margin * 2 - hh * 2);

            if (!occupancy.TryOccupy(cu, cv, hw, hh, 0.10f)) continue;
            count--;

            AddGreeble(mod, face, cu, cv, hw, hh, type, rng, mesh);
        }
    }

    private static void AddGreeble(PlacedModule mod, FaceInfo face,
        float cu, float cv, float hw, float hh,
        GreebleType type, System.Random rng, StationModuleMesh mesh)
    {
        Color baseCol    = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color greebleCol = DarkenColor(baseCol, 0.72f);
        Color detailCol  = DarkenColor(baseCol, 0.55f);
        Color darkCol    = DarkenColor(baseCol, 0.38f);

        switch (type)
        {
            case GreebleType.JunctionBox:
            {
                // Small box with lid seam
                float boxH = 0.30f + (float)rng.NextDouble() * 0.15f;
                var t = FaceLocalTransform(face,
                    LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(t, new Vector3(hw * 2, hh * 2, boxH), greebleCol);
                // Lid seam (thin strip across middle)
                mesh.AddQuad(
                    LocalPointAbs(face, cu - hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv + 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu - hw, cv + 0.02f, boxH + 0.005f), darkCol);
                break;
            }

            case GreebleType.EquipmentHousing:
            {
                // Large base box with a smaller raised top section
                float baseH = 0.40f + (float)rng.NextDouble() * 0.15f;
                float topH  = 0.20f;
                float topW  = hw * 0.6f;
                float topHH = hh * 0.5f;

                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, baseH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, baseH), greebleCol);

                float topOffset = cu + ((float)rng.NextDouble() - 0.5f) * hw * 0.4f;
                var tt = FaceLocalTransform(face,
                    LocalPointAbs(face, topOffset, cv, baseH + topH * 0.5f));
                mesh.AddOrientedBox(tt, new Vector3(topW * 2, topHH * 2, topH), detailCol);
                break;
            }

            case GreebleType.ConduitEntry:
            {
                // Box with a pipe stub entering from the side
                float boxH = 0.35f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                // Short pipe stub along the face
                float pipeLen = hw * 1.4f;
                Vector3 stubStart = LocalPointAbs(face, cu - pipeLen, cv, boxH * 0.5f);
                Vector3 stubEnd   = LocalPointAbs(face, cu,           cv, boxH * 0.5f);
                mesh.AddPrismPipe(stubStart, stubEnd, 0.08f, 6, detailCol);
                break;
            }

            case GreebleType.SensorPod:
            {
                // Tall box with a small disc "lens" on top
                float podH = 0.50f + (float)rng.NextDouble() * 0.15f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, podH), greebleCol);

                // Lens: very flat box on top
                float lensR = MathF.Min(hw, hh) * 0.5f;
                var lt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH + 0.04f));
                mesh.AddOrientedBox(lt, new Vector3(lensR * 2, lensR * 2, 0.08f),
                    new Color(60, 80, 100));
                break;
            }

            case GreebleType.TechPanel:
            {
                // Thin flat panel with two sub-boxes implying controls
                float panH = 0.12f + (float)rng.NextDouble() * 0.06f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, panH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, panH), greebleCol);

                // Two small raised buttons
                for (int b = 0; b < 2; b++)
                {
                    float btnU  = cu + (b == 0 ? -hw * 0.4f : hw * 0.4f);
                    float btnSz = MathF.Min(hw, hh) * 0.3f;
                    var bt = FaceLocalTransform(face,
                        LocalPointAbs(face, btnU, cv, panH + 0.04f));
                    mesh.AddOrientedBox(bt, new Vector3(btnSz * 2, btnSz * 2, 0.07f),
                        b == 0 ? new Color(60, 100, 60) : detailCol);
                }
                break;
            }

            case GreebleType.ValveAssembly:
            {
                // Box with a cross-shaped handle on top
                float boxH = 0.38f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                float armLen = hw * 0.9f;
                float armW   = 0.06f;
                float armH   = boxH + 0.10f;

                // Horizontal arm
                Vector3 hArmA = LocalPointAbs(face, cu - armLen, cv, armH);
                Vector3 hArmB = LocalPointAbs(face, cu + armLen, cv, armH);
                mesh.AddPrismPipe(hArmA, hArmB, armW, 4, darkCol);

                // Vertical arm
                Vector3 vArmA = LocalPointAbs(face, cu, cv - armLen, armH);
                Vector3 vArmB = LocalPointAbs(face, cu, cv + armLen, armH);
                mesh.AddPrismPipe(vArmA, vArmB, armW, 4, darkCol);
                break;
            }
        }
    }

    // Build a transform matrix with Z aligned to face.LocalNormal, positioned at `center`.
    private static Matrix FaceLocalTransform(FaceInfo face, Vector3 center) => new(
        face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
        face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
        face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
        center.X,           center.Y,           center.Z,           1);

    // ── Pass 7: Lights ────────────────────────────────────────────────────────

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

    private static Color LightenColor(Color c, float factor) => new(
        (byte)Math.Min(c.R * factor, 255),
        (byte)Math.Min(c.G * factor, 255),
        (byte)Math.Min(c.B * factor, 255),
        c.A);
}
