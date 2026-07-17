using Inferior.Game.Containers;
using Inferior.Rendering;
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
            var baseRng         = new System.Random(mod.Seed);
            var windowRng       = new System.Random(baseRng.Next());
            var hatchRng        = new System.Random(baseRng.Next());
            var antennaRng      = new System.Random(baseRng.Next());
            var pipeRng         = new System.Random(baseRng.Next());
            var lightRng        = new System.Random(baseRng.Next());
            var chimneyRng      = new System.Random(baseRng.Next());
            var surfacePipeRng  = new System.Random(baseRng.Next());
            var seamRng         = new System.Random(baseRng.Next());
            var ventRng         = new System.Random(baseRng.Next());
            var greebleRng      = new System.Random(baseRng.Next());
            var edgeTrimRng     = new System.Random(baseRng.Next());
            var ambientLightRng = new System.Random(baseRng.Next());
            var cableRng        = new System.Random(baseRng.Next());

            // If the module has a custom hull factory, seed its mesh now so
            // ComputeFaces can read the actual face normals and centres.
            StationModuleMesh mesh;
            if (mod.Definition.MeshFactory != null)
            {
                mesh        = mod.Definition.MeshFactory(mod.Seed);
                mesh.Texture = TextureFor(mod.Definition.Category);
                mod.Mesh    = mesh;   // expose early for ComputeFaces
            }
            else
            {
                mesh = new StationModuleMesh { Texture = TextureFor(mod.Definition.Category) };
            }
            var glassMesh = new StationModuleMesh { Texture = SurfaceTexture.Glass };

            FaceInfo[] faces = ComputeFaces(mod);

            // Pass 0: panel seams — flat surface decoration that gets AO applied.
            // Must run first so BaseFaceCount captures only these faces.
            mesh.CurrentDecorClass = DecorClass.PanelSeams;
            foreach (var face in faces)
                GeneratePanelSeams(mod, face, seamRng, mesh);
            // For box modules: advance BaseFaceCount to include panel seams (AO target).
            // For custom-mesh modules: leave BaseFaceCount = hull face count set by factory.
            if (mod.Definition.MeshFactory == null)
                mesh.BaseFaceCount = mesh.FaceCount;

            // Pass 1-N: raised decoration (not included in AO range).
            var tankRng      = new System.Random(baseRng.Next());
            var dishRng      = new System.Random(baseRng.Next());
            var containerRng = new System.Random(baseRng.Next());

            foreach (var face in faces)
            {
                // Landing pad faces must be clear — markings and lights are added by
                // GenerateLandingPads. Skip all decoration passes so nothing lands on them.
                if (IsDockingPadFace(mod, face)) continue;

                var occupancy         = new FaceOccupancy();
                var greeblePlacements = new List<PlacedGreebleInfo>();
                mesh.CurrentDecorClass = DecorClass.Windows;
                GenerateWindows     (mod, face, windowRng,      mesh, glassMesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.Hatches;
                GenerateHatches     (mod, face, hatchRng,       mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.Antennas;
                GenerateAntennas    (mod, face, antennaRng,     mesh, mod.GlowLights, occupancy, greeblePlacements);
                mesh.CurrentDecorClass = DecorClass.Dishes;
                GenerateDishes      (mod, face, dishRng,        mesh, occupancy, greeblePlacements);
                mesh.CurrentDecorClass = DecorClass.Chimneys;
                GenerateChimneys    (mod, face, chimneyRng,     mesh, mod.GlowLights, mod);
                mesh.CurrentDecorClass = DecorClass.SurfacePipes;
                GenerateSurfacePipes(mod, face, surfacePipeRng, mesh);
                mesh.CurrentDecorClass = DecorClass.VentGrilles;
                GenerateVentGrilles (mod, face, ventRng,        mesh, occupancy);
                mesh.CurrentDecorClass = DecorClass.Greebles;
                GenerateGreebles    (mod, face, greebleRng,     mesh, occupancy, greeblePlacements);
                mesh.CurrentDecorClass = DecorClass.Tanks;
                GenerateTanks       (mod, face, mesh, occupancy, new System.Random(tankRng.Next()));
                mesh.CurrentDecorClass = DecorClass.Containers;
                GenerateContainers  (mod, face, mesh, occupancy, new System.Random(containerRng.Next()));

                if (face.IsExposed && greeblePlacements.Count >= 2 && face.Width * face.Height >= 4f)
                {
                    mesh.CurrentDecorClass = DecorClass.Cables;
                    StationCableGenerator.GenerateFaceCables(
                        face.LocalNormal, face.LocalRight, face.LocalUp,
                        face.Width, face.Height, face.LocalCenter,
                        greeblePlacements, mesh,
                        new System.Random(cableRng.Next()));
                }
            }

            mesh.CurrentDecorClass = DecorClass.Pipes;
            GeneratePipes         (mod, faces, pipeRng,     mesh);
            mesh.CurrentDecorClass = DecorClass.Lights;
            GenerateLights        (mod, faces, lightRng,    mesh);
            mesh.CurrentDecorClass = DecorClass.EdgeTrim;
            GenerateEdgeTrimStrips(mod, mesh);
            // Guidance lights + hazard placard — same "tiny and/or emissive" never-casts
            // bucket as GenerateLights per the Phase C policy table.
            mesh.CurrentDecorClass = DecorClass.Lights;
            PlaceDockingBayDoorDecoration(mod, mesh);

            // No mesh geometry — only registers StationLightInfo glow entries.
            RegisterModuleAmbientLights(mod, faces, ambientLightRng);

            if (!mesh.IsEmpty)
                mod.Mesh = mesh;
            if (!glassMesh.IsEmpty)
                mod.GlassMesh = glassMesh;
        }

        // Station-wide passes that need all modules to be decorated first.
        int stationSeed = modules.Count > 0 ? modules[0].Seed : 42;
        PlaceLandmarkAntenna(modules, new System.Random(stationSeed ^ 0x12345678));
        RunSolarPanelPass   (modules, new System.Random(stationSeed ^ unchecked((int)0xABCDEF01)));
        RunLargeDishPass    (modules, new System.Random(stationSeed ^ 0x44157A3B));
        GenerateLandingPads (modules, new System.Random(stationSeed ^ 0x3D4E5F60));
    }

    // ── Phase C casting policy ────────────────────────────────────────────────
    //
    // Rollout stage groupings — same grouping as DecorCastingPolicy's comments below,
    // expressed as data so SystemSpaceState.Shadows.cs's Ctrl+F6 debug cycle can preview a
    // stage ahead of its landing in the policy table.
    public static readonly DecorClass[] C1Classes =
        [DecorClass.Pipes, DecorClass.SurfacePipes, DecorClass.PipeBrackets];
    public static readonly DecorClass[] C2Classes =
        [DecorClass.Tanks, DecorClass.Containers, DecorClass.Greebles,
         DecorClass.Chimneys, DecorClass.VentGrilles, DecorClass.SolarPanels];
    public static readonly DecorClass[] C3Classes =
        [DecorClass.Dishes, DecorClass.Antennas];
    public static readonly DecorClass[] C4Classes =
        [DecorClass.Windows, DecorClass.Hatches];

    // Docs/station-lighting-pipeline-spec.md §10 Phase C: the executable form of the
    // "documented casting policy." SystemSpaceState.Shadows.cs reads this to decide which
    // DecorClassRanges feed the per-module shadow-caster index buffer. Rollout is one
    // stage at a time (C1 → C2 → C3 → C4), each landed here only after its own in-engine
    // gate (F8 overlay + Timo's screenshots) — see Brief-LightingPhaseC.md "Rollout".
    // The Ctrl+F6 debug cycle can preview a later stage without this table saying true;
    // this table is what actually casts in the normal game.
    public static readonly IReadOnlyDictionary<DecorClass, bool> DecorCastingPolicy =
        new Dictionary<DecorClass, bool>
        {
            // Fail-safe default — nothing should reach this once Decorate wraps every pass.
            [DecorClass.Unclassified] = false,

            // Never casts (documented exclusions).
            [DecorClass.PanelSeams]         = false, // flat surface decoration, no height
            [DecorClass.EdgeTrim]           = false, // hugs the hull silhouette the hull already casts
            [DecorClass.Cables]             = false, // sub-texel thickness; proven inconsistent caster in the failed 2026 experiment
            [DecorClass.Lights]             = false, // lights/lenses, bay guidance lights, placards — tiny and/or emissive
            [DecorClass.Glass]              = false, // separate transparent mesh, per spec
            [DecorClass.LandingPadMarkings] = false, // flat; pad faces are kept clear anyway

            // C1 — structural. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            [DecorClass.Pipes]        = true,
            [DecorClass.SurfacePipes] = true,
            [DecorClass.PipeBrackets] = true,

            // C2 — equipment. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            [DecorClass.Tanks]       = true,
            [DecorClass.Containers]  = true,
            [DecorClass.Greebles]    = true,
            [DecorClass.Chimneys]    = true,
            [DecorClass.VentGrilles] = true,
            [DecorClass.SolarPanels] = true,

            // C3 — antennas. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            // Covers parabolic dishes + feed assemblies (RunLargeDishPass included) and yagi
            // masts/booms/elements (PlaceLandmarkAntenna included) as whole assemblies — no
            // finer-grained DecorClass split for individual yagi elements yet. Thin-element
            // rule (brief §"Thin-element rule"): if individual yagi elements prove
            // inconsistent casters at the station's texel density, excluding just the
            // elements while masts/booms keep casting is the documented correct outcome —
            // would need a new DecorClass (e.g. AntennaElements) split out from Antennas to
            // express. Not yet needed as of C3 landing; revisit if Timo's F8 check shows it.
            [DecorClass.Dishes]   = true,
            [DecorClass.Antennas] = true,

            // C4 — windows. Landed: gated via F8 overlay + Timo's in-engine screenshots.
            // Covers window frames/braces/cupolas and hatches. Glass itself stays excluded —
            // separate GlassMesh, transparent, never a caster candidate (see [DecorClass.Glass]
            // above), unaffected by this class landing.
            [DecorClass.Windows] = true,
            [DecorClass.Hatches] = true,
        };

    // Darkens decoration vertices on occluded module faces.
    // Per-face algorithm: each seam face gets a darkening factor based on how many
    // of its 4 adjacent face normals are blocked (internal/connected).
    // Only processes BaseFaceCount faces — not raised decoration geometry.
    // Call after BakeLighting so lighting colours are already baked in.
    public static void ApplyAmbientOcclusion(IReadOnlyList<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            if (mod.Mesh == null) continue;

            var internalNormals = BuildInternalNormalSet(mod);
            int limit = mod.Mesh.BaseFaceCount;

            for (int faceIdx = 0; faceIdx < limit; faceIdx++)
            {
                Vector3 faceNormal = mod.Mesh.LocalFaceNormal(faceIdx);
                if (internalNormals.Contains(faceNormal)) continue;

                int enclosed = AdjacentNormalsFor(faceNormal)
                    .Count(n => internalNormals.Contains(n));

                float aoFactor = enclosed switch { 1 => 0.90f, 2 => 0.76f, 3 => 0.60f, _ => 1.00f };
                if (aoFactor >= 1.0f) continue;
                mod.Mesh.MultiplyFaceColor(faceIdx, aoFactor);
            }
        }
    }

    // Returns the local-space normals for all blocked (internal/connected) faces.
    private static HashSet<Vector3> BuildInternalNormalSet(PlacedModule mod)
    {
        var result = new HashSet<Vector3>();
        foreach (var face in ComputeFaces(mod))
            if (!face.IsExposed) result.Add(face.LocalNormal);
        return result;
    }

    // Returns the 4 axis-aligned normals adjacent to a given face normal.
    private static Vector3[] AdjacentNormalsFor(Vector3 n)
    {
        if (MathF.Abs(n.X) > 0.9f)
            return [Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        if (MathF.Abs(n.Y) > 0.9f)
            return [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];
        return [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY];
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
        // Custom-mesh modules: derive face info from the hull mesh geometry.
        if (mod.Definition.MeshFactory != null && mod.Mesh != null)
        {
            int limit  = mod.Mesh.BaseFaceCount;
            var result = new FaceInfo[limit];
            for (int i = 0; i < limit; i++)
            {
                Vector3 n              = mod.Mesh.LocalFaceNormal(i);
                var (center, w, h)     = mod.Mesh.GetFaceBounds(i);
                var (right, up)        = TangentFrame(n);
                bool blocked           = IsFaceBlocked(mod, n);
                result[i]              = new FaceInfo(n, center, right, up, w, h, !blocked);
            }
            return result;
        }

        // Default: derive 6 axis-aligned faces from the bounding box.
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

        var res = new FaceInfo[6];
        for (int i = 0; i < 6; i++)
        {
            var (n, w, h)   = faceData[i];
            Vector3 center  = new(n.X * half.X, n.Y * half.Y, n.Z * half.Z);
            var (right, up) = TangentFrame(n);
            bool blocked    = IsFaceBlocked(mod, n);
            res[i]          = new FaceInfo(n, center, right, up, w, h, !blocked);
        }
        return res;
    }

    private static bool IsFaceBlocked(PlacedModule mod, Vector3 faceNormal)
    {
        foreach (var port in mod.Definition.Ports)
        {
            if (Vector3.Dot(port.OutwardNormal, faceNormal) < 0.9f) continue;
            if (port == mod.AttachmentPort)   return true;
            if (mod.ChildPorts.Contains(port)) return true;
        }
        return false;
    }

    // Returns true when the face is a ship-landing surface on a docking module.
    private static bool IsDockingPadFace(PlacedModule mod, FaceInfo face)
    {
        foreach (var port in mod.Definition.Ports)
            if (port.IsDocking && Vector3.Dot(face.LocalNormal, port.OutwardNormal) > 0.9f)
                return true;
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

    // Transforms a module-local face point to station-relative space via mod.Transform.
    // Used to compute WorldPosition values for StationLightInfo.
    private static Vector3 StationPoint(PlacedModule mod, FaceInfo face, float u, float v, float offset)
        => Vector3.Transform(LocalPointAbs(face, u, v, offset), mod.Transform);

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

    private static readonly Color WarmWhiteColor    = new(255, 250, 220);
    private static readonly Color NeutralWhiteColor = new(240, 240, 248);
    private static readonly Color CoolBlueColor     = new(210, 225, 255);
    private static readonly Color DimAmberColor     = new(200, 170, 100);
    private static readonly Color DarkWindowColor   = new( 31,  30,  26);  // WarmWhite × 0.12f

    private static Color PickWindowColor(string category, System.Random rng)
    {
        double r = rng.NextDouble();
        return category switch
        {
            "hab" => r < 0.55 ? WarmWhiteColor
                   : r < 0.70 ? NeutralWhiteColor
                   : r < 0.75 ? CoolBlueColor
                   : r < 0.90 ? DimAmberColor
                   :             DarkWindowColor,

            "science" => r < 0.20 ? WarmWhiteColor
                       : r < 0.40 ? NeutralWhiteColor
                       : r < 0.80 ? CoolBlueColor
                       : r < 0.85 ? DimAmberColor
                       :             DarkWindowColor,

            "industrial" => r < 0.15 ? WarmWhiteColor
                          : r < 0.35 ? NeutralWhiteColor
                          : r < 0.40 ? CoolBlueColor
                          : r < 0.80 ? DimAmberColor
                          :             DarkWindowColor,

            "cargo" => r < 0.10 ? WarmWhiteColor
                      : r < 0.50 ? NeutralWhiteColor
                      : r < 0.60 ? CoolBlueColor
                      : r < 0.80 ? DimAmberColor
                      :             DarkWindowColor,

            _ => r < 0.30 ? WarmWhiteColor
               : r < 0.60 ? NeutralWhiteColor
               : r < 0.75 ? CoolBlueColor
               : r < 0.90 ? DimAmberColor
               :             DarkWindowColor,
        };
    }

    private static void GenerateWindows(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, StationModuleMesh glassMesh,
        FaceOccupancy occupancy)
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

        bool  canPorthole = mod.Definition.Category is "hab" or "science";
        Color hullColor   = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color frameColor  = Color.Lerp(hullColor, new Color(50, 48, 44), 0.20f);

        // Detect near-horizontal face in world space — gradient is wrong on ceiling/floor windows.
        mod.Transform.Decompose(out _, out Quaternion modRot, out _);
        Vector3 worldFaceN      = Vector3.TransformNormal(face.LocalNormal, Matrix.CreateFromQuaternion(modRot));
        bool    isHorizontalFace = MathF.Abs(worldFaceN.Y) > 0.8f;

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            if (rng.NextDouble() < 0.20) continue;

            float cu = startU + col * gridW;
            float cv = startV + row * gridH;
            if (!occupancy.TryOccupy(cu, cv, winW * 0.5f, winH * 0.5f)) continue;

            Color   winCol  = PickWindowColor(mod.Definition.Category, rng);
            Vector3 facePos = face.LocalCenter
                + face.LocalRight * cu
                + face.LocalUp    * cv;

            if (canPorthole && rng.NextDouble() < 0.20)
            {
                float portholeSize = MathF.Min(winW, winH);
                if (rng.NextDouble() < 0.25)
                    AddCupola(mesh, glassMesh, facePos, face.LocalNormal, face.LocalUp,
                              portholeSize, winCol, frameColor, isHorizontalFace);
                else
                    AddOctagonPorthole(mesh, glassMesh, facePos,
                                       face.LocalNormal, face.LocalUp, portholeSize,
                                       winCol, frameColor, isHorizontalFace);
            }
            else
            {
                // Frame backing quad — blended dark neutral, goes through lighting pass
                float frameBorder = Math.Clamp(MathF.Min(winW, winH) * 0.12f, 0.08f, 0.25f);
                mesh.AddQuad(facePos + face.LocalNormal * 0.01f, face.LocalNormal, face.LocalUp,
                             winW + frameBorder * 2f, winH + frameBorder * 2f, frameColor);

                // Glass quad proud of frame (+0.05m from face surface, emissive — no lighting pass)
                AddGlassQuad(glassMesh, facePos + face.LocalNormal * 0.05f,
                             face.LocalNormal, face.LocalUp, winW, winH, winCol, isHorizontalFace);

                if (rng.NextDouble() < 0.55)
                    AddWindowBraces(mesh, facePos, face.LocalNormal, face.LocalUp,
                                    winW, winH, frameColor);
            }
        }
    }

    // 8-sided porthole: frame backing ring in mesh (shaded), glass fan in glassMesh (emissive).
    // facePos is the un-offset face-surface position; offsets are applied internally.
    private static void AddOctagonPorthole(StationModuleMesh mesh, StationModuleMesh glassMesh,
        Vector3 facePos, Vector3 normal, Vector3 up, float size, Color color, Color frameColor,
        bool flatGlass)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float r = size * 0.5f;

        // Frame backing polygon at +0.01m, scaled out by 1.15x (frameFraction = 0.15f)
        const float frameFraction = 0.15f;
        Vector3 frameCenter = facePos + normal * 0.01f;
        float   frameR      = r * (1f + frameFraction);
        var     framePts    = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = MathF.PI / 8f + i * (MathF.PI / 4f);
            framePts[i] = frameCenter + right * (frameR * MathF.Cos(angle)) + up * (frameR * MathF.Sin(angle));
        }
        for (int i = 0; i < 8; i++)
            mesh.AddTriangle(frameCenter, framePts[i], framePts[(i + 1) % 8], frameColor);

        // Glass fan at +0.05m — emissive, gradient or flat
        Vector3 glassCenter = facePos + normal * 0.05f;
        var     glassPts    = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float angle = MathF.PI / 8f + i * (MathF.PI / 4f);
            glassPts[i] = glassCenter + right * (r * MathF.Cos(angle)) + up * (r * MathF.Sin(angle));
        }

        if (flatGlass)
        {
            for (int i = 0; i < 8; i++)
                glassMesh.AddTriangle(glassCenter, glassPts[i], glassPts[(i + 1) % 8], color);
        }
        else
        {
            // Gradient range: full radius along face.LocalUp
            float ctrProj  = Vector3.Dot(glassCenter, up);
            float minProj  = ctrProj - r;
            float maxProj  = ctrProj + r;
            Color ctrColor = GlassGradientColor(color, glassCenter, up, minProj, maxProj);
            for (int i = 0; i < 8; i++)
            {
                Color ci  = GlassGradientColor(color, glassPts[i],          up, minProj, maxProj);
                Color ci1 = GlassGradientColor(color, glassPts[(i + 1) % 8], up, minProj, maxProj);
                glassMesh.AddTriangleGradient(glassCenter, ctrColor, glassPts[i], ci,
                                              glassPts[(i + 1) % 8], ci1);
            }
        }
    }

    // Cross-pane dividers: raised thin quads 2cm proud of glass (+0.07m from face surface).
    // faceBase is the un-offset face-surface position of the window centre.
    private static void AddWindowBraces(StationModuleMesh mesh, Vector3 faceBase,
        Vector3 normal, Vector3 up, float winW, float winH, Color color)
    {
        const float barThick = 0.03f;
        Vector3 pos = faceBase + normal * 0.07f;
        mesh.AddQuad(pos, normal, up, winW,     barThick, color);
        mesh.AddQuad(pos, normal, up, barThick, winH,     color);
    }

    // Pyramid viewport: 4 triangular glass panels (glassMesh) + dark recess base + frame + braces (mesh).
    // facePos is the un-offset face-surface position; glass goes to +0.05m, frame to +0.01m.
    private static void AddCupola(StationModuleMesh mesh, StationModuleMesh glassMesh,
        Vector3 facePos, Vector3 normal, Vector3 up, float size, Color glassColor,
        Color frameColor, bool flatGlass)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float hw = size * 0.5f;

        // Glass at +0.05m
        Vector3 glassBase = facePos + normal * 0.05f;
        Vector3 apex      = glassBase + normal * hw;
        Vector3[] base4 =
        [
            glassBase - right * hw - up * hw,  // BL
            glassBase - right * hw + up * hw,  // TL
            glassBase + right * hw + up * hw,  // TR
            glassBase + right * hw - up * hw,  // BR
        ];

        // Frame backing: for each triangle, scale vertices 1.15x from centroid, shifted to +0.01m
        const float frameFraction = 0.15f;
        float frameShift = 0.01f - 0.05f;  // -0.04m along normal
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = base4[i];
            Vector3 b = apex;
            Vector3 c = base4[(i + 1) % 4];
            // Shift to +0.01m level
            Vector3 fa = a + normal * frameShift;
            Vector3 fb = b + normal * frameShift;
            Vector3 fc = c + normal * frameShift;
            Vector3 fCentroid = (fa + fb + fc) / 3f;
            Vector3 sfa = fCentroid + (fa - fCentroid) * (1f + frameFraction);
            Vector3 sfb = fCentroid + (fb - fCentroid) * (1f + frameFraction);
            Vector3 sfc = fCentroid + (fc - fCentroid) * (1f + frameFraction);
            mesh.AddTriangle(sfa, sfb, sfc, frameColor);
        }

        // Dark inner base behind frame, at face surface level
        Vector3 darkBase = facePos;
        Vector3[] darkCorners =
        [
            darkBase - right * hw - up * hw,
            darkBase - right * hw + up * hw,
            darkBase + right * hw + up * hw,
            darkBase + right * hw - up * hw,
        ];
        mesh.AddQuad(darkCorners[0], darkCorners[3], darkCorners[2], darkCorners[1], new Color(20, 22, 28));

        // Glass panels with gradient, and edge braces
        float minProj = Vector3.Dot(glassBase - up * hw, up);
        float maxProj = Vector3.Dot(apex, up);
        if (maxProj < minProj) (minProj, maxProj) = (maxProj, minProj);

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = base4[i];
            Vector3 b = apex;
            Vector3 c = base4[(i + 1) % 4];

            // Glass triangle
            if (flatGlass)
            {
                glassMesh.AddTriangle(a, b, c, glassColor);
            }
            else
            {
                Color ca = GlassGradientColor(glassColor, a, up, minProj, maxProj);
                Color cb = GlassGradientColor(glassColor, b, up, minProj, maxProj);
                Color cc = GlassGradientColor(glassColor, c, up, minProj, maxProj);
                glassMesh.AddTriangleGradient(a, ca, b, cb, c, cc);
            }

            // Edge braces: one per edge, angled inward toward triangle interior
            AddTriangleBrace(mesh, a, b, c, normal, frameColor);  // edge a→b, third vert = c
            AddTriangleBrace(mesh, b, c, a, normal, frameColor);  // edge b→c, third vert = a
            AddTriangleBrace(mesh, c, a, b, normal, frameColor);  // edge c→a, third vert = b
        }
    }

    // Adds a single structural brace quad along edge A→B, leaning inward toward thirdVertex.
    private static void AddTriangleBrace(StationModuleMesh mesh,
        Vector3 a, Vector3 b, Vector3 thirdVertex, Vector3 faceNormal, Color color)
    {
        Vector3 edgeDir  = Vector3.Normalize(b - a);
        Vector3 inward   = Vector3.Normalize(Vector3.Cross(faceNormal, edgeDir));
        // Ensure inward points toward the triangle interior
        if (Vector3.Dot(inward, thirdVertex - a) < 0f) inward = -inward;

        const float braceWidth  = 0.03f;
        const float braceHeight = 0.02f;
        Vector3 v2 = b + inward * braceWidth + faceNormal * braceHeight;
        Vector3 v3 = a + inward * braceWidth + faceNormal * braceHeight;
        mesh.AddQuad(a, b, v2, v3, color);
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
        System.Random rng, StationModuleMesh mesh, List<StationLightInfo> lights,
        FaceOccupancy occupancy, List<PlacedGreebleInfo> placements)
    {
        if (!face.IsExposed)           return;
        if (face.LocalNormal.Y < -0.3f) return;
        if (rng.NextDouble() > 0.35)   return;

        Color baseCol    = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        // Maritime white — roughly 1-in-4 antenna clusters are painted white like
        // the superstructure masts on an ocean-going vessel.
        bool faceIsWhite = rng.NextDouble() < 0.25;
        if (faceIsWhite) antennaCol = new Color(228, 232, 230);

        int count = rng.Next(1, 3);
        for (int i = 0; i < count; i++)
        {
            float u = (float)(rng.NextDouble() - 0.5) * face.Width  * 0.5f;
            float v = (float)(rng.NextDouble() - 0.5) * face.Height * 0.5f;

            Vector3 basePos = face.LocalCenter
                + face.LocalRight * u
                + face.LocalUp    * v;

            float plateSize;
            if (rng.NextDouble() < 0.30)
            {
                var yp = YagiAntennaParams.Generate(new System.Random(rng.Next()), faceIsWhite);
                StationYagiAntenna.Build(yp, basePos, face.LocalNormal, mesh);
                plateSize = 0.30f;
            }
            else
            {
                float length = AntennaHeight(rng);
                float radius = (float)(rng.NextDouble() * 0.12 + 0.04);

                mesh.AddSpike(basePos, face.LocalNormal, length, radius, antennaCol);

                if (length > 2.5f)
                {
                    Vector3 tipLocal = basePos + face.LocalNormal * length;
                    lights.Add(new StationLightInfo(
                        WorldPosition: Vector3.Transform(tipLocal, mod.Transform),
                        Colour:        new Color(220, 25, 25),
                        Type:          GlowType.AviationWarning,
                        BaseIntensity: 1.0f,
                        Rate:          0.65f,
                        Phase:         (float)rng.NextDouble(),
                        Pattern:       LightPattern.Strobe));
                }

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

                plateSize = MathF.Max(0.22f, radius * 3.5f);
            }

            // Base mount plate — makes the cable connection look intentional
            float   plateH = 0.09f;
            var     plateT = FaceLocalTransform(face, basePos + face.LocalNormal * (plateH * 0.5f));
            mesh.AddOrientedBox(plateT, new Vector3(plateSize * 2, plateSize * 2, plateH), new Color(50, 50, 55));
            occupancy.Occupy(u - plateSize, v - plateSize, u + plateSize, v + plateSize);
            placements.Add(new PlacedGreebleInfo(
                new Vector2(u, v),
                new Vector2(plateSize * 2, plateSize * 2),
                isConnectable: true));
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
        bestMod.Mesh.CurrentDecorClass = DecorClass.Antennas;

        Color baseCol    = StationModuleRegistry.CategoryColor(bestMod.Definition.Category);
        Color antennaCol = DarkenColor(baseCol, 0.45f);

        float height = (float)(rng.NextDouble() * 9.0 + 18.0); // 18–27 m
        bestMod.Mesh.AddSpike(f.LocalCenter, f.LocalNormal, height, 0.15f, antennaCol);

        const float stemLen = 1.5f;
        Vector3 stemCenter = f.LocalCenter + f.LocalNormal * (height + stemLen * 0.5f);
        bestMod.Mesh.AddOrientedBox(stemCenter, f.LocalNormal, stemLen, 0.25f, 0.25f, antennaCol);
        Vector3 tipCenter = f.LocalCenter + f.LocalNormal * (height + stemLen + 0.3f);
        bestMod.Mesh.AddBox(tipCenter, new Vector3(2.0f, 0.6f, 2.0f), antennaCol);

        // Aviation warning light — landmark antennas are always tall enough to warrant it.
        Vector3 tipLocal = f.LocalCenter + f.LocalNormal * (height + stemLen + 0.6f);
        bestMod.GlowLights.Add(new StationLightInfo(
            WorldPosition: Vector3.Transform(tipLocal, bestMod.Transform),
            Colour:        new Color(220, 25, 25),
            Type:          GlowType.AviationWarning,
            BaseIntensity: 1.0f,
            Rate:          0.65f,
            Phase:         (float)rng.NextDouble(),
            Pattern:       LightPattern.Strobe));
    }

    // ── Pass 3b: Parabolic dishes ────────────────────────────────────────────

    private static readonly HashSet<string> DishCategories = ["science", "core", "military", "connector"];

    private static Color DishSurfaceColor(float radius) => radius > 3f
        ? new Color(252, 250, 244)   // large: near-white, faint warm tint
        : new Color(248, 246, 242);  // small/medium: near-white, slightly cooler

    private static readonly Color DishStructureColor = new Color(58, 55, 52);

    private static void GenerateDishes(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> placements)
    {
        if (!face.IsExposed) return;
        if (!DishCategories.Contains(mod.Definition.Category)) return;

        mod.Transform.Decompose(out _, out Quaternion rot, out _);
        Vector3 worldNormal = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
        if (worldNormal.Y < -0.25f) return;

        float faceArea = face.Width * face.Height;

        // ── Medium dishes ───────────────────────────────────────────────────
        float medProb = mod.Definition.Category switch
        {
            "science"  => 0.55f,
            "military" => 0.30f,
            "core"     => 0.20f,
            _          => 0.10f,
        };
        if (faceArea > 60f && rng.NextDouble() < medProb)
        {
            float r      = 1.0f + (float)rng.NextDouble() * 1.8f;
            float maxOff = MathF.Max(0.01f, face.Width  * 0.5f - r * 1.5f);
            float maxOffV= MathF.Max(0.01f, face.Height * 0.5f - r * 1.5f);
            float cu = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOff;
            float cv = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOffV;
            if (occupancy.TryOccupy(cu, cv, r * 1.4f, r * 1.4f))
            {
                AddParabolicDish(mesh, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, r,
                    tiltDegrees:    15f + (float)rng.NextDouble() * 30f,
                    bearingDegrees: (float)rng.NextDouble() * 360f,
                    sides:          11,
                    dishColor:      DishSurfaceColor(r),
                    structureColor: DishStructureColor);
                var medPlateT = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, 0.04f));
                mesh.AddOrientedBox(medPlateT, new Vector3(0.60f, 0.60f, 0.08f), new Color(50, 50, 55));
                placements.Add(new PlacedGreebleInfo(
                    new Vector2(cu, cv),
                    new Vector2(r * 2.8f, r * 2.8f),
                    isConnectable: true));
            }
        }

        // ── Small dishes ────────────────────────────────────────────────────
        int smallCount = mod.Definition.Category == "science" ? rng.Next(1, 4) : rng.Next(0, 2);
        for (int i = 0; i < smallCount; i++)
        {
            float r      = 0.35f + (float)rng.NextDouble() * 0.55f;
            float maxOff = MathF.Max(0.01f, face.Width  * 0.5f - r * 1.25f);
            float maxOffV= MathF.Max(0.01f, face.Height * 0.5f - r * 1.25f);
            float cu = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOff;
            float cv = ((float)(rng.NextDouble() - 0.5)) * 2f * maxOffV;
            if (!occupancy.TryOccupy(cu, cv, r * 1.2f, r * 1.2f)) continue;
            AddParabolicDish(mesh, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, r,
                tiltDegrees:    10f + (float)rng.NextDouble() * 50f,
                bearingDegrees: (float)rng.NextDouble() * 360f,
                sides:          9,
                dishColor:      DishSurfaceColor(r),
                structureColor: DishStructureColor);
            var smPlateT = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, 0.04f));
            mesh.AddOrientedBox(smPlateT, new Vector3(0.35f, 0.35f, 0.06f), new Color(50, 50, 55));
            placements.Add(new PlacedGreebleInfo(
                new Vector2(cu, cv),
                new Vector2(r * 2.4f, r * 2.4f),
                isConnectable: true));
        }
    }

    // Finds the largest eligible exposed face and places a single landmark large dish.
    private static void RunLargeDishPass(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        bool qualified = modules.Any(m => m.Definition.Category is "science" or "military");
        if (!qualified || rng.NextDouble() >= 0.22) return;

        PlacedModule? bestMod  = null;
        FaceInfo?     bestFace = null;
        float         bestArea = 180f;

        foreach (var mod in modules)
        {
            if (!DishCategories.Contains(mod.Definition.Category)) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            foreach (var face in ComputeFaces(mod))
            {
                if (!face.IsExposed) continue;
                Vector3 worldN = Vector3.Normalize(Vector3.Transform(face.LocalNormal, rot));
                if (worldN.Y < -0.25f) continue;
                float area = face.Width * face.Height;
                if (area > bestArea) { bestArea = area; bestFace = face; bestMod = mod; }
            }
        }

        if (bestMod == null || bestFace == null) return;

        var   f = bestFace.Value;
        float r = 4.0f + (float)rng.NextDouble() * 4.0f;
        bestMod.Mesh ??= new StationModuleMesh();
        bestMod.Mesh.CurrentDecorClass = DecorClass.Dishes;
        AddParabolicDish(bestMod.Mesh, f.LocalCenter, f.LocalNormal, r,
            tiltDegrees:    8f + (float)rng.NextDouble() * 28f,
            bearingDegrees: (float)rng.NextDouble() * 360f,
            sides:          13,
            dishColor:      DishSurfaceColor(r),
            structureColor: DishStructureColor);
    }

    // ── Landing pad geometry ──────────────────────────────────────────────────

    // Adds a flat visual landing pad at each IsDocking port of every docking module.
    // No physics or interaction — purely cosmetic for Step 1.
    private static void GenerateLandingPads(IReadOnlyList<PlacedModule> modules, System.Random rng)
    {
        int bayNumber = 1;
        foreach (var mod in modules)
        {
            if (mod.Definition.Category != "docking") continue;
            mod.Mesh ??= new StationModuleMesh { Texture = TextureFor(mod.Definition.Category) };
            mod.Mesh.CurrentDecorClass = DecorClass.LandingPadMarkings;
            var faces = ComputeFaces(mod);
            foreach (var port in mod.Definition.Ports)
            {
                if (!port.IsDocking) continue;
                FaceInfo? padFace = null;
                foreach (var f in faces)
                {
                    if (Vector3.Dot(f.LocalNormal, port.OutwardNormal) > 0.9f)
                    { padFace = f; break; }
                }
                if (padFace is not FaceInfo face) continue;
                AddLandingPadGeometry(mod.Mesh, face, mod, bayNumber++, rng);
            }
        }
    }

    private static void AddLandingPadGeometry(StationModuleMesh mesh, FaceInfo face,
        PlacedModule mod, int bayNumber, System.Random rng)
    {
        float padW = face.Width  - 1.0f;
        float padH = face.Height - 1.0f;
        if (padW < 1f || padH < 1f) return;

        const float raise = 0.06f;
        Vector3 normal = face.LocalNormal;
        Vector3 up     = face.LocalUp;
        Vector3 origin = face.LocalCenter + normal * raise;

        Color padColor    = new Color(158, 153, 145);   // concrete grey
        Color stripeColor = new Color(215, 148, 12);    // amber-yellow
        Color markColor   = new Color(228, 223, 213);   // off-white

        // Pad surface
        mesh.AddQuad(origin, normal, up, padW, padH, padColor);

        // Perimeter stripe — 4 border segments at raise + 1 cm
        float   stripeW = MathF.Max(0.28f, padW * 0.08f);
        float   innerH  = padH - stripeW * 2f;
        Vector3 sp      = origin + normal * 0.01f;
        mesh.AddQuad(sp + up * ( padH * 0.5f - stripeW * 0.5f), normal, up, padW,   stripeW, stripeColor);
        mesh.AddQuad(sp + up * (-padH * 0.5f + stripeW * 0.5f), normal, up, padW,   stripeW, stripeColor);
        mesh.AddQuad(sp + face.LocalRight * ( padW * 0.5f - stripeW * 0.5f), normal, up, stripeW, innerH, stripeColor);
        mesh.AddQuad(sp + face.LocalRight * (-padW * 0.5f + stripeW * 0.5f), normal, up, stripeW, innerH, stripeColor);

        // Central cross marking
        float crossBarW = padW * 0.11f;
        float crossBarL = padH * 0.29f;
        Vector3 cp = origin + normal * 0.02f;
        mesh.AddQuad(cp, normal, up, crossBarW, crossBarL, markColor);   // vertical bar
        mesh.AddQuad(cp, normal, up, crossBarL, crossBarW, markColor);   // horizontal bar

        // Direction arrow — stem + head pointing +up, positioned in upper half
        float   stemW = padW * 0.08f;
        float   stemH = padH * 0.17f;
        float   headW = padW * 0.16f;
        float   headH = padH * 0.09f;
        Vector3 ap    = origin + normal * 0.025f + up * (padH * 0.15f);
        mesh.AddQuad(ap + up * (stemH * 0.5f),           normal, up, stemW, stemH, markColor);
        mesh.AddQuad(ap + up * (stemH + headH * 0.5f),   normal, up, headW, headH, markColor);

        // Corner SlowPulse amber lights — at pad corners
        Color   amber  = new Color(255, 148, 10);
        Color   amberH = new Color(40, 30, 8);
        float   cU     = padW * 0.44f;
        float   cV     = padH * 0.44f;
        float   phase0 = (float)rng.NextDouble();
        (Vector3, float)[] corners =
        [
            (face.LocalCenter + face.LocalRight *  cU + face.LocalUp *  cV, phase0),
            (face.LocalCenter + face.LocalRight * -cU + face.LocalUp *  cV, (phase0 + 0.25f) % 1f),
            (face.LocalCenter + face.LocalRight * -cU + face.LocalUp * -cV, (phase0 + 0.50f) % 1f),
            (face.LocalCenter + face.LocalRight *  cU + face.LocalUp * -cV, (phase0 + 0.75f) % 1f),
        ];
        foreach (var (lpos, lphase) in corners)
        {
            var (_, glowPos) = AddLight(mesh, lpos + normal * (raise + 0.10f), normal, 0.22f, amberH, amber);
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        amber,
                Type:          GlowType.AmbientMarker,
                BaseIntensity: 0.65f,
                Rate:          0.8f,
                Phase:         lphase,
                Pattern:       LightPattern.SlowPulse));
        }

        // Threshold Strobe white lights — two at the approach end (bottom edge)
        Color   white  = new Color(228, 238, 255);
        Color   whiteH = new Color(34, 34, 42);
        (Vector3, float)[] thresholds =
        [
            (face.LocalCenter + face.LocalUp * -cV + face.LocalRight *  (padW * 0.22f), 0.0f),
            (face.LocalCenter + face.LocalUp * -cV + face.LocalRight * -(padW * 0.22f), 0.5f),
        ];
        foreach (var (lpos, lphase) in thresholds)
        {
            var (_, glowPos) = AddLight(mesh, lpos + normal * (raise + 0.10f), normal, 0.22f, whiteH, white);
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        white,
                Type:          GlowType.WarningStrobe,
                BaseIntensity: 0.80f,
                Rate:          1.2f,
                Phase:         lphase,
                Pattern:       LightPattern.Strobe));
        }
    }

    // Returns N ring vertices for one concentric band of the parabolic dish.
    private static Vector3[] GenerateDishRing(Vector3 centre, Vector3 dishAxis,
        Vector3 right, Vector3 up, float radius, float depth, int sides)
    {
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float   angle  = i * MathF.Tau / sides;
            Vector3 radial = right * MathF.Cos(angle) + up * MathF.Sin(angle);
            ring[i] = centre + radial * radius + dishAxis * depth;
        }
        return ring;
    }

    private static void AddParabolicDish(StationModuleMesh mesh,
        Vector3 mountPoint, Vector3 faceNormal,
        float radius, float tiltDegrees, float bearingDegrees,
        int sides, Color dishColor, Color structureColor)
    {
        float maxDepth  = radius * 0.28f;
        float armLength = radius * 0.85f + 0.6f;
        const float shellThick = 0.015f;

        // Build tilted dish axis
        Vector3 arbitrary = MathF.Abs(faceNormal.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tiltAxis  = Vector3.Normalize(Vector3.Cross(faceNormal, arbitrary));
        tiltAxis  = Vector3.Transform(tiltAxis,
            Quaternion.CreateFromAxisAngle(faceNormal, bearingDegrees * MathF.PI / 180f));
        Vector3 dishAxis = Vector3.Normalize(Vector3.Transform(faceNormal,
            Quaternion.CreateFromAxisAngle(tiltAxis, tiltDegrees * MathF.PI / 180f)));

        // dishBack: where the support arm ends (the back/deepest point of the dish)
        // dishCenter: the rim centre — maxDepth further out along dishAxis from dishBack
        // The bowl opens in the +dishAxis direction (toward the receiver / space)
        Vector3 dishBack   = mountPoint + faceNormal * armLength;
        Vector3 dishCenter = dishBack + dishAxis * maxDepth;

        Vector3 dishArb   = MathF.Abs(dishAxis.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 dishRight = Vector3.Normalize(Vector3.Cross(dishAxis, dishArb));
        Vector3 dishUp    = Vector3.Normalize(Vector3.Cross(dishRight, dishAxis));

        // Depths from dishCenter (rim plane) going BACKWARD (-dishAxis) into the bowl.
        float[] ringRadii  = [radius, radius * 0.62f, radius * 0.28f];
        float[] ringDepths = [0f, -(maxDepth * 0.38f), -(maxDepth * 0.86f)];
        var rings = new Vector3[3][];
        for (int ri = 0; ri < 3; ri++)
            rings[ri] = GenerateDishRing(dishCenter, dishAxis, dishRight, dishUp,
                                          ringRadii[ri], ringDepths[ri], sides);
        Vector3 dishCentreTip = dishBack;  // = dishCenter - dishAxis * maxDepth

        // Back shell rings: same positions shifted by shellThick in -dishAxis
        var backRings = new Vector3[3][];
        for (int ri = 0; ri < 3; ri++)
        {
            backRings[ri] = new Vector3[sides];
            for (int i = 0; i < sides; i++)
                backRings[ri][i] = rings[ri][i] - dishAxis * shellThick;
        }
        Vector3 backCentreTip = dishCentreTip - dishAxis * shellThick;

        Color rimColor = DarkenColor(dishColor, 0.68f);

        // Front surface: concave inner bowl, faces +dishAxis (toward receiver)
        for (int ri = 0; ri < 2; ri++)
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                mesh.AddQuad(rings[ri][i], rings[ri][next],
                             rings[ri+1][next], rings[ri+1][i], dishColor);
            }
        for (int i = 0; i < sides; i++)
            mesh.AddTriangle(dishCentreTip, rings[2][i], rings[2][(i+1)%sides], dishColor);

        // Back surface: convex outer bowl, reversed winding → faces -dishAxis
        for (int ri = 0; ri < 2; ri++)
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                mesh.AddQuad(backRings[ri][next], backRings[ri][i],
                             backRings[ri+1][i], backRings[ri+1][next], dishColor);
            }
        for (int i = 0; i < sides; i++)
            mesh.AddTriangle(backCentreTip, backRings[2][(i+1)%sides], backRings[2][i], dishColor);

        // Rim strip: connects front rim ring to back rim ring, faces radially outward
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            mesh.AddQuad(rings[0][i], backRings[0][i], backRings[0][next], rings[0][next], rimColor);
        }

        // Support arm from mount to dish back
        mesh.AddPrismPipe(mountPoint, dishBack, 0.06f + radius * 0.018f, 4, structureColor);

        // Diagonal brace for medium and large dishes: low on arm → rim centre
        if (radius > 1.5f)
        {
            Vector3 braceRoot = mountPoint + faceNormal * armLength * 0.28f;
            mesh.AddPrismPipe(braceRoot, dishCenter, 0.045f, 4, structureColor);
        }

        AddDishFeedAssembly(mesh, rings[0], dishCentreTip, dishAxis, sides, structureColor);
    }

    // Feed mast, feed box, and struts from rim to focal point.
    private static void AddDishFeedAssembly(StationModuleMesh mesh,
        Vector3[] rimRing, Vector3 dishTip, Vector3 dishAxis, int sides,
        Color structureColor)
    {
        Vector3 rimCenter = Vector3.Zero;
        foreach (var v in rimRing) rimCenter += v;
        rimCenter /= sides;

        float   mastLength = Vector3.Distance(rimCenter, dishTip) * 0.55f;
        Vector3 feedTip    = rimCenter + dishAxis * mastLength;

        mesh.AddPrismPipe(dishTip, feedTip, 0.03f, 4, structureColor);
        mesh.AddOrientedBox(feedTip, dishAxis, 0.12f, 0.14f, 0.14f, structureColor);

        int strutCount = sides >= 11 ? 4 : 3;
        for (int s = 0; s < strutCount; s++)
        {
            int rimIdx = (int)((float)s / strutCount * sides);
            mesh.AddPrismPipe(rimRing[rimIdx], feedTip, 0.025f, 4, structureColor);
        }
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
            mod.Mesh.CurrentDecorClass = DecorClass.SolarPanels;
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
        System.Random rng, StationModuleMesh mesh, List<StationLightInfo> lights,
        PlacedModule owner)
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
            AddChimney(mesh, basePos, face.LocalNormal, rng, baseCol, owner, lights);
        }
    }

    private static void AddChimney(StationModuleMesh mesh, Vector3 basePos,
        Vector3 normal, System.Random rng, Color baseCol,
        PlacedModule mod, List<StationLightInfo> lights)
    {
        Color chimCol = DarkenColor(baseCol, 0.55f);
        Color tipCol  = new(50, 45, 40);

        float height;
        if (rng.NextDouble() < 0.55)
        {
            height = (float)(rng.NextDouble() * 4.0 + 2.5);
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
            height = (float)(rng.NextDouble() * 1.5 + 1.0);
            mesh.AddOrientedBox(basePos + normal * (height * 0.3f),
                normal, height * 0.6f, baseR * 2, baseR * 2, chimCol);
            mesh.AddOrientedBox(basePos + normal * (height * 0.8f),
                normal, height * 0.4f, exitR * 2, exitR * 2, tipCol);
        }

        Vector3 chimTip = basePos + normal * (height + 0.15f);
        lights.Add(new StationLightInfo(
            WorldPosition: Vector3.Transform(chimTip, mod.Transform),
            Colour:        new Color(210, 30, 20),
            Type:          GlowType.AviationWarning,
            BaseIntensity: 1.0f,
            Rate:          0.65f,
            Phase:         (float)rng.NextDouble(),
            Pattern:       LightPattern.Strobe));
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

            // Both ends sit mid-face with margin — genuinely floating, not touching
            // anything (unlike GeneratePipes' full-edge-length runs, which continue
            // past the module edge).
            mesh.AddPrismPipe(pipeStart, pipeEnd, radius, sides, colour, capStart: true, capEnd: true);
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

    // Chamfer depth now varies per module (mod.ChamferDepth, seeded — see
    // StationGenerator.ChamferDepthForSeed), not a shared constant — the flat hull panel
    // is inset by mod.ChamferDepth * 0.707f on each side to make room for the beveled
    // edge trim, so anything else that assumes it knows the panel's true extent (seams
    // here) needs the same inset or it runs past the flat surface and onto the bevel.

    private static void GeneratePanelSeams(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 25f) return;

        Color baseCol   = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color seamColor = DarkenColor(baseCol, 0.72f);
        const float seamWidth  = 0.038f;
        const float seamOffset = 0.028f;

        // Edge trim (Pass 6b) only insets standard box modules — custom-mesh modules
        // (octagonal etc.) have no box chamfer to stay clear of.
        float chamferInset = mod.Definition.MeshFactory == null
            ? mod.ChamferDepth * 0.707f
            : 0f;
        float hw = face.Width  * 0.5f - chamferInset;
        float hh = face.Height * 0.5f - chamferInset;
        float hs = seamWidth * 0.5f;

        // Defensive floor — an unusually narrow face can pass the 25m² area check above
        // while still being thin enough in one dimension that the inset consumes it entirely.
        if (hw <= 0f || hh <= 0f) return;

        int hSeams = rng.NextDouble() < 0.55 ? 2 : 1;
        for (int i = 0; i < hSeams; i++)
        {
            float t    = hSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float vOff = -face.Height * 0.5f + face.Height * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Height * 0.08f;

            AddSeamStrip(mesh, face, horizontal: true, vOff, hw, hs, seamOffset, seamColor, rng);
        }

        int vSeams = face.Width > 20f ? (rng.NextDouble() < 0.6 ? 2 : 1) : 1;
        for (int i = 0; i < vSeams; i++)
        {
            float t    = vSeams == 1 ? 0.5f : (i == 0 ? 0.33f : 0.67f);
            float uOff = -face.Width * 0.5f + face.Width * t
                       + ((float)rng.NextDouble() - 0.5f) * face.Width * 0.08f;

            AddSeamStrip(mesh, face, horizontal: false, uOff, hh, hs, seamOffset, seamColor, rng);
        }
    }

    // Subdivides a seam into ~1.5m segments with slightly varied brightness (±15%),
    // matching the existing wear-pattern philosophy of subtle per-region variation
    // rather than a single flat-colour line. Deterministic per station seed — uses the
    // same seeded rng GeneratePanelSeams was already given, not per-frame randomness.
    private static void AddSeamStrip(StationModuleMesh mesh, FaceInfo face,
        bool horizontal, float centerOffset, float halfLen, float halfWidth, float zOffset,
        Color baseColor, System.Random rng)
    {
        float totalLen = halfLen * 2f;
        int   segments = Math.Max(1, (int)(totalLen / 1.5f));  // ~1.5m per segment

        for (int s = 0; s < segments; s++)
        {
            float u0 = -halfLen + totalLen * s       / segments;
            float u1 = -halfLen + totalLen * (s + 1) / segments;

            float variation = 0.85f + (float)rng.NextDouble() * 0.30f;  // ±15% brightness
            Color segColor  = new Color(
                (byte)Math.Clamp(baseColor.R * variation, 0, 255),
                (byte)Math.Clamp(baseColor.G * variation, 0, 255),
                (byte)Math.Clamp(baseColor.B * variation, 0, 255),
                baseColor.A);

            Vector3 v0, v1, v2, v3;
            if (horizontal)
            {
                v0 = LocalPointAbs(face, u0, centerOffset - halfWidth, zOffset);
                v1 = LocalPointAbs(face, u1, centerOffset - halfWidth, zOffset);
                v2 = LocalPointAbs(face, u1, centerOffset + halfWidth, zOffset);
                v3 = LocalPointAbs(face, u0, centerOffset + halfWidth, zOffset);
            }
            else
            {
                v0 = LocalPointAbs(face, centerOffset - halfWidth, u0, zOffset);
                v1 = LocalPointAbs(face, centerOffset + halfWidth, u0, zOffset);
                v2 = LocalPointAbs(face, centerOffset + halfWidth, u1, zOffset);
                v3 = LocalPointAbs(face, centerOffset - halfWidth, u1, zOffset);
            }
            mesh.AddQuad(v0, v1, v2, v3, segColor);
        }
    }

    // ── Pass 6b: Edge trim strips ─────────────────────────────────────────────

    private static void GenerateEdgeTrimStrips(PlacedModule mod, StationModuleMesh mesh)
    {
        // Octagonal and other custom-mesh modules use their own geometry — no box chamfers
        if (mod.Definition.MeshFactory != null) return;

        Vector3 half = mod.Definition.BoundingBox * 0.5f;
        Color trimColor = LightenColor(
            StationModuleRegistry.CategoryColor(mod.Definition.Category), 1.12f);
        AddChamferEdgeTrim(mesh, half, mod.ChamferDepth, trimColor);
    }

    // Adds the 45°-bevel edge strips (12 edges) and corner-fill triangles (8 corners) for an
    // axis-aligned box of the given half-extents and chamfer depth. Shared by GenerateEdgeTrimStrips
    // (standard box modules) and DockingBayHull (a MeshFactory module that builds its own chamfered
    // hull and so bypasses GenerateEdgeTrimStrips' MeshFactory-guard entirely).
    internal static void AddChamferEdgeTrim(StationModuleMesh mesh, Vector3 half, float chamferDepth, Color trimColor)
    {
        float inset = chamferDepth * 0.707f;

        // Width of each strip = diagonal of the inset square (√2 × inset).
        // Strips are shortened at both ends by inset so adjacent strips don't overlap at corners.
        float stripWidth = inset * MathF.Sqrt(2f);

        foreach (var (faceA, faceB, edgeDir, cornerSign) in BoxEdgeInfos)
        {
            float edgeHalfLen = edgeDir.X != 0 ? half.X
                              : edgeDir.Y != 0 ? half.Y : half.Z;

            Vector3 edgeMid = new(
                edgeDir.X != 0 ? 0 : cornerSign.X * half.X,
                edgeDir.Y != 0 ? 0 : cornerSign.Y * half.Y,
                edgeDir.Z != 0 ? 0 : cornerSign.Z * half.Z);

            // Strip centre sits on the 45° bisector of the two face planes, inset from the edge.
            // Vertices land exactly on the inset edges of the hull face panels — no gap, no lift needed.
            Vector3 outwardNorm = Vector3.Normalize(faceA + faceB);
            Vector3 stripCenter = edgeMid - (faceA + faceB) * (inset * 0.5f);
            mesh.AddQuad(stripCenter, outwardNorm, edgeDir, stripWidth, (edgeHalfLen - inset) * 2f, trimColor);
        }

        // Corner triangles — fill the small triangular hole at each of the 8 corners where
        // the shortened strips and inset hull face panels leave a gap.
        (int sx, int sy, int sz)[] corners =
            [(1,1,1),(1,1,-1),(1,-1,1),(1,-1,-1),(-1,1,1),(-1,1,-1),(-1,-1,1),(-1,-1,-1)];

        foreach (var (sx, sy, sz) in corners)
        {
            // One vertex per adjacent edge strip endpoint, coinciding with the hull panel corner.
            Vector3 c1 = new(sx * (half.X - inset), sy * half.Y,             sz * (half.Z - inset));
            Vector3 c2 = new(sx * half.X,           sy * (half.Y - inset),    sz * (half.Z - inset));
            Vector3 c3 = new(sx * (half.X - inset), sy * (half.Y - inset),    sz * half.Z);

            // Cross(c2-c1, c3-c1) is inward when sx*sy*sz > 0 — swap v1/v2 to face outward.
            if (sx * sy * sz > 0)
                mesh.AddTriangle(c1, c3, c2, trimColor);
            else
                mesh.AddTriangle(c1, c2, c3, trimColor);
        }
    }

    // ── Pass 6c: Vent grilles ─────────────────────────────────────────────────

    private enum VentStyle { HorizontalBars, Louvered, ScreenMesh }

    private static VentStyle SelectVentStyle(System.Random rng) => rng.NextDouble() switch
    {
        < 0.45 => VentStyle.HorizontalBars,
        < 0.80 => VentStyle.Louvered,
        _      => VentStyle.ScreenMesh,
    };

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

            switch (SelectVentStyle(rng))
            {
                case VentStyle.HorizontalBars:
                    AddHBarVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
                case VentStyle.Louvered:
                    AddLouVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
                case VentStyle.ScreenMesh:
                    AddScreenVentGrille(mod, face, cu, cv, ventW, ventH, rng, mesh);
                    break;
            }
        }
    }

    // Shared: dark recess behind grille opening.
    private static void AddVentBacking(FaceInfo face, float cu, float cv,
        float ventW, float ventH, Color col, StationModuleMesh mesh)
    {
        const float shadowOff = 0.018f;
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv - hh, shadowOff),
            LocalPointAbs(face, cu + hw, cv + hh, shadowOff),
            LocalPointAbs(face, cu - hw, cv + hh, shadowOff), col);
    }

    // Shared: thin raised border around a vent opening.
    private static void AddVentFrame(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, StationModuleMesh mesh)
    {
        Color frameCol  = DarkenColor(StationModuleRegistry.CategoryColor(mod.Definition.Category), 0.58f);
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        const float fw = 0.12f;   // frame width
        const float fo = 0.025f;  // frame Z offset

        // Top bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv + hh,      fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh,      fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh + fw, fo),
            LocalPointAbs(face, cu - hw - fw, cv + hh + fw, fo), frameCol);

        // Bottom bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv - hh - fw, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh - fw, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh,      fo),
            LocalPointAbs(face, cu - hw - fw, cv - hh,      fo), frameCol);

        // Left bar
        mesh.AddQuad(
            LocalPointAbs(face, cu - hw - fw, cv - hh, fo),
            LocalPointAbs(face, cu - hw,      cv - hh, fo),
            LocalPointAbs(face, cu - hw,      cv + hh, fo),
            LocalPointAbs(face, cu - hw - fw, cv + hh, fo), frameCol);

        // Right bar
        mesh.AddQuad(
            LocalPointAbs(face, cu + hw,      cv - hh, fo),
            LocalPointAbs(face, cu + hw + fw, cv - hh, fo),
            LocalPointAbs(face, cu + hw + fw, cv + hh, fo),
            LocalPointAbs(face, cu + hw,      cv + hh, fo), frameCol);
    }

    // Existing grille style: horizontal or vertical bars.
    private static void AddHBarVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color barCol   = DarkenColor(baseCol, 0.45f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(12, 12, 14), mesh);

        bool horizontal = rng.NextDouble() < 0.6;
        int  barCount   = rng.Next(3, 8);
        float hw = ventW * 0.5f, hh = ventH * 0.5f;
        const float barThick = 0.04f;
        const float barOff   = 0.030f;

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

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

    // Louvered vent: angled slats like venetian blinds.
    private static void AddLouVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color slatCol  = DarkenColor(baseCol, 0.50f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(10, 10, 12), mesh);

        int   slats     = rng.Next(4, 8);
        float slatsH    = ventH / slats;
        const float slabThick = 0.045f;
        float hh = ventH * 0.5f, hw = ventW * 0.5f;

        for (int s = 0; s < slats; s++)
        {
            float vCentre = cv - hh + slatsH * (s + 0.5f);
            float vBot    = vCentre - slatsH * 0.3f;
            float vTop    = vCentre + slatsH * 0.3f;

            // Slat slopes in the normal direction from bottom to top — gives angled appearance.
            Vector3 s0 = LocalPointAbs(face, cu - hw, vBot, 0.022f);
            Vector3 s1 = LocalPointAbs(face, cu + hw, vBot, 0.022f);
            Vector3 s2 = LocalPointAbs(face, cu + hw, vTop, 0.022f + slabThick);
            Vector3 s3 = LocalPointAbs(face, cu - hw, vTop, 0.022f + slabThick);
            mesh.AddQuad(s0, s1, s2, s3, slatCol);
        }

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

    // Screen mesh vent: fine grid of thin bars in both directions.
    private static void AddScreenVentGrille(PlacedModule mod, FaceInfo face,
        float cu, float cv, float ventW, float ventH, System.Random rng,
        StationModuleMesh mesh)
    {
        Color baseCol  = StationModuleRegistry.CategoryColor(mod.Definition.Category);
        Color wireCol  = DarkenColor(baseCol, 0.45f);

        AddVentBacking(face, cu, cv, ventW, ventH, new Color(8, 8, 10), mesh);

        const float wireThick = 0.025f;
        const float wireOff   = 0.026f;
        float hw = ventW * 0.5f, hh = ventH * 0.5f;

        int hCount = Math.Max(1, (int)(ventW / 0.35f));
        int vCount = Math.Max(1, (int)(ventH / 0.35f));

        // Horizontal wires
        for (int i = 1; i < vCount; i++)
        {
            float vPos = cv - hh + ventH * ((float)i / vCount);
            float hs   = wireThick * 0.5f;
            mesh.AddQuad(
                LocalPointAbs(face, cu - hw, vPos - hs, wireOff),
                LocalPointAbs(face, cu + hw, vPos - hs, wireOff),
                LocalPointAbs(face, cu + hw, vPos + hs, wireOff),
                LocalPointAbs(face, cu - hw, vPos + hs, wireOff), wireCol);
        }

        // Vertical wires (offset slightly forward so they cross over horizontals)
        for (int i = 1; i < hCount; i++)
        {
            float uPos = cu - hw + ventW * ((float)i / hCount);
            float hs   = wireThick * 0.5f;
            mesh.AddQuad(
                LocalPointAbs(face, uPos - hs, cv - hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos + hs, cv - hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos + hs, cv + hh, wireOff + 0.005f),
                LocalPointAbs(face, uPos - hs, cv + hh, wireOff + 0.005f), wireCol);
        }

        AddVentFrame(mod, face, cu, cv, ventW, ventH, mesh);
    }

    // ── Pass 6d: Greeble boxes ────────────────────────────────────────────────

    private enum GreebleType
    {
        JunctionBox, EquipmentHousing, ConduitEntry, SensorPod, TechPanel, ValveAssembly, YagiAntenna
    }

    private static GreebleType SelectGreebleType(string category, System.Random rng)
    {
        return category switch
        {
            "industrial" or "core" => (GreebleType)rng.Next(0, 7),
            "cargo"      or "fuel" => rng.NextDouble() < 0.5
                ? GreebleType.ValveAssembly : GreebleType.ConduitEntry,
            "science"              => rng.NextDouble() switch {
                                        < 0.15 => GreebleType.YagiAntenna,
                                        < 0.45 => GreebleType.SensorPod,
                                        < 0.75 => GreebleType.JunctionBox,
                                        _      => GreebleType.TechPanel,
                                     },
            "hab"                  => rng.NextDouble() < 0.65
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
        GreebleType.YagiAntenna      => (0.25f, 0.25f),
        _                            => (0.35f, 0.35f),
    };

    private static bool IsConnectableGreeble(GreebleType type) => type switch
    {
        GreebleType.JunctionBox      => true,
        GreebleType.EquipmentHousing => true,
        GreebleType.ConduitEntry     => true,
        GreebleType.ValveAssembly    => true,
        GreebleType.YagiAntenna      => true,
        _                            => false,
    };

    private static void GenerateGreebles(PlacedModule mod, FaceInfo face,
        System.Random rng, StationModuleMesh mesh, FaceOccupancy occupancy,
        List<PlacedGreebleInfo> placements)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 12f) return;

        float prob = mod.Definition.Category switch
        {
            "industrial" or "core" => 0.90f,
            "cargo"      or "fuel" => 0.70f,
            "science"              => 0.75f,
            "connector"            => 0.55f,
            "hab"                  => 0.60f,
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

            placements.Add(new PlacedGreebleInfo(
                new Vector2(cu, cv),
                new Vector2(hw * 2, hh * 2),
                IsConnectableGreeble(type)));

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
                float boxH = 0.30f + (float)rng.NextDouble() * 0.15f;
                var t = FaceLocalTransform(face,
                    LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(t, new Vector3(hw * 2, hh * 2, boxH), greebleCol);
                mesh.AddQuad(
                    LocalPointAbs(face, cu - hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv - 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu + hw, cv + 0.02f, boxH + 0.005f),
                    LocalPointAbs(face, cu - hw, cv + 0.02f, boxH + 0.005f), darkCol);
                break;
            }

            case GreebleType.EquipmentHousing:
            {
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
                float boxH = 0.35f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                float pipeLen = hw * 1.4f;
                // stubStart is the exposed far end; stubEnd sits at the equipment box
                // (same point as its own centre) and is covered by it.
                Vector3 stubStart = LocalPointAbs(face, cu - pipeLen, cv, boxH * 0.5f);
                Vector3 stubEnd   = LocalPointAbs(face, cu,           cv, boxH * 0.5f);
                mesh.AddPrismPipe(stubStart, stubEnd, 0.08f, 6, detailCol, capStart: true);
                break;
            }

            case GreebleType.SensorPod:
            {
                float podH = 0.50f + (float)rng.NextDouble() * 0.15f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, podH), greebleCol);

                float lensR = MathF.Min(hw, hh) * 0.5f;
                var lt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, podH + 0.04f));
                mesh.AddOrientedBox(lt, new Vector3(lensR * 2, lensR * 2, 0.08f),
                    new Color(60, 80, 100));
                break;
            }

            case GreebleType.TechPanel:
            {
                float panH = 0.12f + (float)rng.NextDouble() * 0.06f;
                var pt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, panH * 0.5f));
                mesh.AddOrientedBox(pt, new Vector3(hw * 2, hh * 2, panH), greebleCol);

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
                float boxH = 0.38f;
                var bt = FaceLocalTransform(face, LocalPointAbs(face, cu, cv, boxH * 0.5f));
                mesh.AddOrientedBox(bt, new Vector3(hw * 2, hh * 2, boxH), greebleCol);

                float armLen = hw * 0.9f;
                float armW   = 0.06f;
                float armH   = boxH + 0.10f;

                // Found during the AddPrismPipe end-cap sweep: all four spoke tips
                // are genuinely free-floating (nothing covers them), same category
                // as ConduitEntry's stub and the surface pipe runs.
                Vector3 hArmA = LocalPointAbs(face, cu - armLen, cv, armH);
                Vector3 hArmB = LocalPointAbs(face, cu + armLen, cv, armH);
                mesh.AddPrismPipe(hArmA, hArmB, armW, 4, darkCol, capStart: true, capEnd: true);

                Vector3 vArmA = LocalPointAbs(face, cu, cv - armLen, armH);
                Vector3 vArmB = LocalPointAbs(face, cu, cv + armLen, armH);
                mesh.AddPrismPipe(vArmA, vArmB, armW, 4, darkCol, capStart: true, capEnd: true);
                break;
            }

            case GreebleType.YagiAntenna:
            {
                var p = YagiAntennaParams.Generate(new System.Random(rng.Next()));
                StationYagiAntenna.Build(p, LocalPointAbs(face, cu, cv, 0f), face.LocalNormal, mesh);
                break;
            }
        }
    }

    // ── Pass 6e: Storage tanks ────────────────────────────────────────────────

    // Returns body colour, stripe colour, stripe count, and palette index (0-6).
    private static (Color body, Color stripe, int stripes, int idx) TankPalette(int seed)
    {
        var rng = new System.Random(seed);
        int idx = rng.Next(7);
        return idx switch
        {
            0 => (new Color(205, 48,  42),  new Color(220, 190, 28),  2, 0),  // red/yellow — fuel
            1 => (new Color(218, 188, 28),  new Color(20,  20,  20),  3, 1),  // yellow/black — hazard
            2 => (new Color(225, 223, 218), new Color(55,  95,  200), 1, 2),  // white/blue — LOX
            3 => (new Color(50,  95,  195), new Color(220, 220, 215), 2, 3),  // blue/white — coolant
            4 => (new Color(218, 118, 38),  new Color(220, 220, 215), 2, 4),  // orange — industrial
            5 => (new Color(65,  128, 68),  new Color(220, 220, 215), 1, 5),  // green — chemical
            6 => (new Color(198, 198, 192), new Color(75,  75,  75),  2, 6),  // silver — high-pressure
            _ => (new Color(198, 198, 192), new Color(75,  75,  75),  2, 6),
        };
    }

    private static string PickSubstanceName(int paletteIdx, System.Random rng)
    {
        string[] names = paletteIdx switch
        {
            0 => ["JP-5", "RP-1", "CH4", "LH2"],
            1 => ["N204", "MMH", "HAZ"],
            2 => ["LOX", "LO2", "OX"],
            3 => ["H2O", "CLT", "COOL"],
            4 => ["PROC", "FEED", "REAC"],
            5 => ["N2H4", "HYD", "CHEM"],
            6 => ["N2", "HPA", "GAS"],
            _ => ["TANK"],
        };
        return names[rng.Next(names.Length)];
    }

    // Returns start and end cross-section rings for an N-sided prism.
    // Mirrors AddPrismPipe's ring generation so caps align perfectly with the body.
    private static (Vector3[] start, Vector3[] end) GetPrismRings(
        Vector3 start, Vector3 end, float radius, int sides)
    {
        Vector3 dir       = Vector3.Normalize(end - start);
        Vector3 arbitrary = MathF.Abs(dir.Y) < 0.85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right     = Vector3.Normalize(Vector3.Cross(dir, arbitrary));
        Vector3 up        = Vector3.Normalize(Vector3.Cross(right, dir));

        var startRing = new Vector3[sides];
        var endRing   = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float   a      = i * MathF.Tau / sides;
            Vector3 offset = right * MathF.Cos(a) * radius + up * MathF.Sin(a) * radius;
            startRing[i] = start + offset;
            endRing[i]   = end   + offset;
        }
        return (startRing, endRing);
    }

    // Truncated-pyramid cap for one end of a tank body.
    // Both caps: pass the appropriate ring + flipLaterals=true.
    //   Start cap: reversed startRing so the tip triangles wind outward toward -axis;
    //              flipLaterals=true because reversing the ring gives it the same
    //              winding orientation (CW from outside) as endRing, so the lateral
    //              quads need the same flip to produce outward-facing normals.
    //   End cap:   non-reversed endRing + flipLaterals=true.
    // The tip face winding is the same for both ends.
    private static void AddTankCap(StationModuleMesh mesh,
        Vector3[] bodyRing, Vector3 outDir, float tipRadius, float capDepth, Color color,
        bool flipLaterals = false)
    {
        int     N          = bodyRing.Length;
        Vector3 bodyCenter = Vector3.Zero;
        foreach (var v in bodyRing) bodyCenter += v;
        bodyCenter /= N;

        Vector3 tipCenter = bodyCenter + outDir * capDepth;

        var tipRing = new Vector3[N];
        for (int i = 0; i < N; i++)
        {
            Vector3 outward = Vector3.Normalize(bodyRing[i] - bodyCenter);
            tipRing[i] = tipCenter + outward * tipRadius;
        }

        for (int i = 0; i < N; i++)
        {
            int next = (i + 1) % N;
            if (flipLaterals)
                mesh.AddQuad(bodyRing[next], bodyRing[i], tipRing[i], tipRing[next], color);
            else
                mesh.AddQuad(bodyRing[i], bodyRing[next], tipRing[next], tipRing[i], color);
        }

        for (int i = 0; i < N; i++)
            mesh.AddTriangle(tipCenter, tipRing[(i + 1) % N], tipRing[i], color);
    }

    // Pixel-art text rendered as tiny raised quads on a planar surface.
    private static void AddTextGeometry(StationModuleMesh mesh,
        string text, Vector3 origin, Vector3 textRight, Vector3 textUp, Vector3 textNormal,
        float pixelSize, Color textColor)
    {
        float cx = 0f;
        foreach (char ch in text.ToUpperInvariant())
        {
            if (!BitmapFonts.HasGlyph(ch)) { cx += (BitmapFonts.CharW + 1) * pixelSize; continue; }

            for (int row = 0; row < BitmapFonts.CharH; row++)
            for (int col = 0; col < BitmapFonts.CharW; col++)
            {
                if (!BitmapFonts.IsLit(ch, col, row)) continue;
                float px = cx + (col + 0.5f) * pixelSize;
                float py = (BitmapFonts.CharH - row - 0.5f) * pixelSize;  // row 0 = top in font → flip Y
                mesh.AddQuad(origin + textRight * px + textUp * py,
                             textNormal, textUp, pixelSize * 0.88f, pixelSize * 0.88f, textColor);
            }
            cx += (BitmapFonts.CharW + 1) * pixelSize;
        }
    }

    // Metal plate label (substance name + 2-digit ID) on the outward face of a tank.
    private static void AddTankLabel(StationModuleMesh mesh,
        Vector3 tankMidPoint, Vector3 labelNormal, Vector3 labelUp,
        float tankRadius, string substance, int tankId,
        Color textColor, Color plateColor)
    {
        if (tankRadius < 0.45f) return;

        Vector3 labelRight = Vector3.Normalize(Vector3.Cross(labelUp, labelNormal));

        // Fit substance name to one octagon face width (~0.765 × radius)
        float faceW     = 2f * tankRadius * MathF.Sin(MathF.PI / 8f);
        float pixelSize = Math.Clamp(
            faceW * 0.68f / MathF.Max(1, substance.Length * (BitmapFonts.CharW + 1)),
            0.028f, 0.18f);

        float  nameW = substance.Length      * (BitmapFonts.CharW + 1) * pixelSize;
        float  lineH = BitmapFonts.CharH    * pixelSize;
        float  idSz  = pixelSize * 0.68f;
        string idStr = $"{tankId:D2}";
        float  idW   = idStr.Length * (BitmapFonts.CharW + 1) * idSz;

        float plateW = MathF.Max(nameW, idW) + pixelSize * 3f;
        float plateH = lineH + BitmapFonts.CharH * idSz + pixelSize * 4f;

        float   plateOff = tankRadius + 0.012f;
        Vector3 plateCtr = tankMidPoint + labelNormal * plateOff;
        mesh.AddQuad(plateCtr, labelNormal, labelUp, plateW, plateH, plateColor);

        // Top border strip
        mesh.AddQuad(plateCtr + labelUp * (plateH * 0.5f + pixelSize * 0.2f),
                     labelNormal, labelUp, plateW * 1.07f, pixelSize * 0.6f,
                     DarkenColor(plateColor, 0.60f));

        // Substance name (upper line)
        const float raise = 0.014f;
        Vector3 nameOrig = plateCtr + labelNormal * raise
                         + labelRight * (-nameW * 0.5f)
                         + labelUp    * (pixelSize * 1.5f);
        AddTextGeometry(mesh, substance, nameOrig, labelRight, labelUp, labelNormal, pixelSize, textColor);

        // 2-digit ID (lower line, smaller)
        Vector3 idOrig = plateCtr + labelNormal * raise
                       + labelRight * (-idW * 0.5f)
                       - labelUp    * (lineH + pixelSize * 0.8f);
        AddTextGeometry(mesh, idStr, idOrig, labelRight, labelUp, labelNormal,
                        idSz, DarkenColor(textColor, 0.70f));
    }

    // Junction boxes, valve wheels, and pipe stubs on the tank barrel.
    private static void AddTankGreebles(StationModuleMesh mesh,
        Vector3 tankStart, Vector3 tankEnd, float radius,
        Vector3 outNormal, Color baseColor, Color pipeColor, System.Random rng)
    {
        Vector3 axis    = Vector3.Normalize(tankEnd - tankStart);
        Vector3 csRight = Vector3.Normalize(Vector3.Cross(axis,
                              MathF.Abs(Vector3.Dot(axis, outNormal)) < 0.85f
                              ? outNormal : Vector3.UnitY));
        Vector3 csUp    = Vector3.Normalize(Vector3.Cross(csRight, axis));

        float outAngle = MathF.Atan2(Vector3.Dot(csUp, outNormal),
                                      Vector3.Dot(csRight, outNormal));
        Color boxCol  = DarkenColor(baseColor, 0.56f);
        Color darkCol = DarkenColor(baseColor, 0.37f);

        int count = rng.Next(2, 5);
        for (int i = 0; i < count; i++)
        {
            float   t      = 0.1f + (float)rng.NextDouble() * 0.8f;
            float   angle  = outAngle + ((float)rng.NextDouble() - 0.5f) * MathF.PI * 0.75f;
            float   boxSz  = radius * (0.10f + (float)rng.NextDouble() * 0.12f);
            Vector3 ctrPt  = Vector3.Lerp(tankStart, tankEnd, t);
            Vector3 surfPt = ctrPt + csRight * MathF.Cos(angle) * radius
                                   + csUp    * MathF.Sin(angle) * radius;
            Vector3 outDir = Vector3.Normalize(surfPt - ctrPt);

            switch (rng.Next(3))
            {
                case 0: // Junction box + indicator LED
                    mesh.AddOrientedBox(surfPt + outDir * (boxSz * 0.5f),
                        outDir, boxSz * 0.5f, boxSz * 1.4f, boxSz, boxCol);
                    mesh.AddOrientedBox(surfPt + outDir * (boxSz + 0.01f),
                        outDir, 0.02f, boxSz * 0.22f, boxSz * 0.22f, new Color(20, 200, 40));
                    break;

                case 1: // Pipe stub with elbow cap
                    Vector3 stubEnd = surfPt + outDir * (radius * 0.32f);
                    mesh.AddPrismPipe(surfPt, stubEnd, boxSz * 0.32f, 6, pipeColor);
                    mesh.AddOrientedBox(stubEnd, outDir,
                        boxSz * 0.22f, boxSz * 0.65f, boxSz * 0.65f, darkCol);
                    break;

                case 2: // Valve wheel (stem + two crossed spokes)
                    Vector3 stemEnd = surfPt + outDir * (boxSz * 0.85f);
                    mesh.AddOrientedBox((surfPt + stemEnd) * 0.5f, outDir,
                        boxSz * 0.85f, boxSz * 0.14f, boxSz * 0.14f, darkCol);
                    mesh.AddOrientedBox(stemEnd, csRight,
                        boxSz * 1.35f, boxSz * 0.10f, boxSz * 0.10f, darkCol);
                    mesh.AddOrientedBox(stemEnd, csUp,
                        boxSz * 1.35f, boxSz * 0.10f, boxSz * 0.10f, darkCol);
                    break;
            }
        }
    }

    // Radius picker biased toward small/medium with rare large tanks.
    private static float PickTankRadius(System.Random rng) => rng.NextDouble() switch
    {
        < 0.55 => 0.30f + (float)rng.NextDouble() * 0.55f,   // 0.30–0.85 m: common small
        < 0.80 => 0.85f + (float)rng.NextDouble() * 0.85f,   // 0.85–1.70 m: medium
        < 0.93 => 1.70f + (float)rng.NextDouble() * 0.80f,   // 1.70–2.50 m: large
        _      => 2.50f + (float)rng.NextDouble() * 2.50f,   // 2.50–5.00 m: rare
    };

    private static void AddTank(StationModuleMesh mesh,
        Vector3 start, Vector3 end, float bodyRadius,
        Color bodyColor, Color stripeColor, int stripeCount,
        Color pipeColor, Vector3 attachPoint,
        Vector3 labelNormal, Vector3 labelUp,
        string substanceName, int tankId,
        System.Random rng)
    {
        const int sides = 8;
        mesh.AddPrismPipe(start, end, bodyRadius, sides, bodyColor);

        var (startRing, endRing) = GetPrismRings(start, end, bodyRadius, sides);
        float   tipRadius = bodyRadius * 0.28f;
        float   capDepth  = bodyRadius * 0.50f;
        Vector3 axis      = Vector3.Normalize(end - start);

        // Reverse startRing so the tip triangles wind outward for the start cap.
        // Both caps use flipLaterals=true: after reversal, startRingRev is CW from
        // outside (like endRing from its outside), so the same lateral flip applies.
        var startRingRev = new Vector3[sides];
        for (int i = 0; i < sides; i++) startRingRev[i] = startRing[sides - 1 - i];

        AddTankCap(mesh, startRingRev, -axis, tipRadius, capDepth, bodyColor, flipLaterals: true);
        AddTankCap(mesh, endRing,       axis, tipRadius, capDepth, bodyColor, flipLaterals: true);

        float stripeW = MathF.Max(bodyRadius * 0.08f, 0.04f);
        for (int s = 1; s <= stripeCount; s++)
        {
            float   t   = (float)s / (stripeCount + 1);
            Vector3 ctr = Vector3.Lerp(start, end, t);
            mesh.AddPrismPipe(ctr - axis * stripeW, ctr + axis * stripeW,
                              bodyRadius * 1.04f, sides, stripeColor);
        }

        // Connecting pipe from nearest cap tip to module surface, in stripe colour
        bool    useStart = Vector3.Distance(start, attachPoint) < Vector3.Distance(end, attachPoint);
        Vector3 capTip   = useStart ? start - axis * capDepth : end + axis * capDepth;
        mesh.AddPrismPipe(capTip, attachPoint, bodyRadius * 0.18f, 6, stripeColor);

        // Label plate + pixel text
        AddTankLabel(mesh, (start + end) * 0.5f, labelNormal, labelUp,
                     bodyRadius, substanceName, tankId, stripeColor,
                     DarkenColor(bodyColor, 0.62f));

        // Surface greebles
        AddTankGreebles(mesh, start, end, bodyRadius, labelNormal, bodyColor, stripeColor, rng);
    }

    private static void PlaceTankRow(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes)
    {
        int   maxCount = mod.Definition.Category is "fuel" or "industrial" or "military" ? 6 : 4;
        int   count    = 2 + rng.Next(maxCount - 1);
        float radius   = PickTankRadius(rng);
        float length   = radius * 2.0f + (float)rng.NextDouble() * radius * 2.5f;
        float gap      = radius * 0.14f;
        float step     = radius * 2 + gap;

        // Clamp count so the row fits the face width
        int maxFit = Math.Max(2, (int)((face.Width * 0.88f + gap) / step));
        count = Math.Min(count, maxFit);
        float totalU = count * step - gap;
        if (totalU > face.Width) return;

        float vOff = -face.Height * 0.5f + radius + 0.8f;
        if (!occupancy.TryOccupy(0, vOff, totalU * 0.5f + 0.3f, radius + 0.4f)) return;

        Color pipeColor  = DarkenColor(stripeColor, 0.75f);
        Color strutColor = new Color(80, 75, 70);
        float startU     = -totalU * 0.5f + radius;

        var cuArr = new float[count];
        for (int i = 0; i < count; i++)
        {
            float   cu        = startU + i * step;
            cuArr[i]          = cu;
            Vector3 centre    = LocalPointAbs(face, cu, vOff, radius * 0.5f);
            Vector3 tankStart = centre - face.LocalRight * (length * 0.5f);
            Vector3 tankEnd   = centre + face.LocalRight * (length * 0.5f);
            AddTank(mesh, tankStart, tankEnd, radius,
                    bodyColor, stripeColor, stripes, pipeColor,
                    LocalPointAbs(face, cu, vOff, 0),
                    face.LocalNormal, face.LocalUp,
                    substance, i + 1, rng);
        }

        // Banding straps across the whole row (top and bottom)
        float cu0 = cuArr[0], cu1 = cuArr[count - 1];
        foreach (float vStrap in new[] { vOff + radius * 0.62f, vOff - radius * 0.62f })
        {
            mesh.AddPrismPipe(
                LocalPointAbs(face, cu0 - radius, vStrap, radius + 0.04f),
                LocalPointAbs(face, cu1 + radius, vStrap, radius + 0.04f),
                radius * 0.042f, 4, strutColor);
        }

        // Diagonal cross-braces between adjacent tanks for larger clusters
        if (count >= 3 && radius >= 0.55f)
        {
            for (int i = 0; i < count - 1; i++)
            {
                Vector3 topA = LocalPointAbs(face, cuArr[i],     vOff + radius * 0.58f, radius + 0.05f);
                Vector3 botB = LocalPointAbs(face, cuArr[i + 1], vOff - radius * 0.58f, radius + 0.05f);
                mesh.AddPrismPipe(topA, botB, radius * 0.032f, 4, strutColor);
            }
        }
    }

    private static void PlaceSingleTank(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes)
    {
        float radius = MathF.Max(0.80f, PickTankRadius(rng));
        float length = radius * 1.8f + (float)rng.NextDouble() * radius * 3f;
        float cu     = ((float)rng.NextDouble() - 0.5f) * MathF.Max(0.1f, face.Width  - radius * 2.5f);
        float cv     = ((float)rng.NextDouble() - 0.5f) * MathF.Max(0.1f, face.Height - radius * 2.5f);

        if (!occupancy.TryOccupy(cu, cv, radius * 1.3f, radius * 1.3f)) return;

        Color pipeColor = DarkenColor(stripeColor, 0.75f);
        AddTank(mesh,
                LocalPointAbs(face, cu, cv, 0.2f),
                LocalPointAbs(face, cu, cv, length + 0.2f),
                radius, bodyColor, stripeColor, stripes, pipeColor,
                LocalPointAbs(face, cu, cv, 0),
                face.LocalRight, face.LocalUp,            // label on the side of the silo
                substance, 1, rng);
    }

    private static void PlaceTankPair(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng,
        string substance, Color bodyColor, Color stripeColor, int stripes)
    {
        float radius  = PickTankRadius(rng);
        float length  = radius * 2.5f + (float)rng.NextDouble() * radius * 2.5f;
        float spacing = radius * 2.4f;

        if (spacing + radius > face.Width * 0.5f) return;
        if (!occupancy.TryOccupy(0, 0, spacing * 0.5f + radius + 0.3f, length * 0.5f + 0.4f)) return;

        Color pipeColor = DarkenColor(stripeColor, 0.75f);

        for (int side = -1; side <= 1; side += 2)
        {
            float   cu     = side * spacing * 0.5f;
            Vector3 centre = LocalPointAbs(face, cu, 0, radius * 0.5f);
            AddTank(mesh,
                    centre - face.LocalUp * (length * 0.5f),
                    centre + face.LocalUp * (length * 0.5f),
                    radius, bodyColor, stripeColor, stripes, pipeColor,
                    LocalPointAbs(face, cu, 0, 0),
                    face.LocalNormal, face.LocalUp,
                    substance, side < 0 ? 1 : 2, rng);
        }

        // Cross-pipe in stripe colour between the two tanks
        mesh.AddPrismPipe(
            LocalPointAbs(face, -spacing * 0.5f, 0, radius),
            LocalPointAbs(face, +spacing * 0.5f, 0, radius),
            radius * 0.14f, 6, stripeColor);
    }

    private static void PlaceTankCluster(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng, int clusterIdx)
    {
        int paletteSeed = mod.Seed ^ (0xAB12 + clusterIdx * 0x2F1B);
        var (bodyColor, stripeColor, stripes, paletteIdx) = TankPalette(paletteSeed);
        string substance = PickSubstanceName(paletteIdx, new System.Random(paletteSeed ^ 0x5C3A));

        double typeRoll = rng.NextDouble();
        if (face.Width * face.Height > 100f && typeRoll < 0.28)
            PlaceSingleTank(mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes);
        else if (typeRoll < 0.65)
            PlaceTankRow   (mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes);
        else
            PlaceTankPair  (mod, face, mesh, occupancy, rng, substance, bodyColor, stripeColor, stripes);
    }

    private static void GenerateTanks(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng)
    {
        if (!face.IsExposed) return;
        if (face.Width * face.Height < 12f) return;

        float firstProb = mod.Definition.Category switch
        {
            "fuel"     or "military"  => 0.97f,
            "industrial"              => 0.90f,
            "cargo"    or "core"      => 0.75f,
            "science"  or "connector" => 0.45f,
            "hab"                     => 0.28f,
            _                         => 0.20f,
        };
        if (rng.NextDouble() > firstProb) return;

        int maxClusters = mod.Definition.Category switch
        {
            "fuel"     or "military"  => 3,
            "industrial"              => 3,
            "cargo"    or "core"      => 2,
            _                         => 1,
        };

        PlaceTankCluster(mod, face, mesh, occupancy, rng, 0);

        float nextProb = 0.55f;
        for (int extra = 1; extra < maxClusters; extra++)
        {
            if (rng.NextDouble() > nextProb) break;
            nextProb *= 0.65f;
            PlaceTankCluster(mod, face, mesh, occupancy, rng, extra);
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

    // Returns the lens quad's vertex base (for AnimTags) and the position callers should
    // register as StationLightInfo.WorldPosition for the glow sprite. That position is
    // deliberately a little proud of the lens quad itself (see glowForwardBias below),
    // not merely coincident with it — registering the flush `position`, or even the
    // lens's own exact surface position, left the glow sprite sitting at essentially the
    // same depth as the glass geometry it's meant to shine through. DepthRead (added
    // once real depth-testing was needed) then clipped the sprite against that glass
    // from head-on angles, where perspective gives the flat glass quad's surface a
    // depth that's equal to or nearer than the sprite's single point at some pixels —
    // "gradually clips out of glass moving to the side" is exactly that near-equal-
    // depth ambiguity resolving itself as the viewing angle changes. Sitting reliably
    // in front of the glass avoids the ambiguity entirely rather than relying on a
    // razor-thin coincident-depth comparison.
    private static (int vb, Vector3 glowPosition) AddLight(StationModuleMesh mesh,
        Vector3 position, Vector3 normal, float size, Color housing, Color lens)
    {
        const float depth = 0.15f;
        // Shifts the housing off the surface — position sits flush with the hull/panel
        // behind it, so an unshifted housing's outer face (at position, zero offset)
        // z-fights with that panel. raise also doubles as the lens-proud-of-housing gap
        // below, preserving the original 0.01m lens-proud-of-flush-housing relationship
        // now that the housing's outer face itself sits at position + raise, not position.
        const float raise = 0.01f;
        const float glowForwardBias = 0.05f; // clear of the glass, not just coincident with it
        mesh.AddOrientedBox(position + normal * (raise - depth * 0.5f), normal,
            depth, size * 1.4f, size * 1.4f, housing);
        Vector3 lensCenter = position + normal * (raise * 2f);
        int vb = mesh.AddQuad(lensCenter, normal, TangentFrame(normal).up, size, size, lens);
        return (vb, lensCenter + normal * glowForwardBias);
    }

    private static void PlaceNavigationLights(PlacedModule mod, StationModuleMesh mesh)
    {
        Vector3 bb   = mod.Definition.BoundingBox;
        Vector3 half = bb * 0.5f;

        (Vector3 normal, Vector3 pos, Color lens)[] navLights =
        [
            ( Vector3.UnitX,  new Vector3(+half.X, 0, 0),  new Color( 60, 230,  80)),
            (-Vector3.UnitX,  new Vector3(-half.X, 0, 0),  new Color(230,  55,  55)),
            (-Vector3.UnitZ,  new Vector3(0, half.Y * 0.5f, -half.Z), new Color(210, 220, 255)),
        ];

        Color housing = new(40, 40, 40);
        foreach (var (normal, pos, lens) in navLights)
        {
            if (IsFaceBlocked(mod, normal)) continue;
            var (vb, glowPos) = AddLight(mesh, pos, normal, 0.4f, housing, lens);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Steady,
                VertexBase = vb,
                OnColor    = lens,
                OffColor   = DarkenColor(lens, 0.1f),
                Period     = 1f,
            });
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        lens,
                Type:          GlowType.NavigationLight,
                BaseIntensity: 0.80f,
                Rate:          0f,
                Phase:         0f,
                Pattern:       LightPattern.Continuous));
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
            var (vb, glowPos) = AddLight(mesh, pos, Vector3.UnitY, 0.5f, housing, amber);
            mesh.AnimTags.Add(new AnimTag
            {
                Type       = AnimType.Strobe,
                VertexBase = vb,
                OnColor    = amber,
                OffColor   = new Color(30, 12, 0),
                Period     = 1.4f,
                Phase      = phase,
            });
            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                Colour:        amber,
                Type:          GlowType.WarningStrobe,
                BaseIntensity: 0.80f,
                Rate:          1f / 1.4f,
                Phase:         phase,
                Pattern:       LightPattern.Strobe));
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

                var (vb, _) = AddLight(mesh, pos, face.LocalNormal, 0.3f, housing, amber);
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

                var (vb, _) = AddLight(mesh, pos, df.LocalNormal, 0.35f, housing, white);
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

    // Guidance lights + hazard signage around the docking-bay's door. Can't reuse
    // PlaceBayGuidanceLights as-is: that function finds its target via the generic
    // ComputeFaces/base-face list, but the docking-bay's door is a framed opening, not a
    // solid base face, so it never appears in that list. The door's own geometry (position,
    // size) is known directly from the definition instead, so this calls AddLight the same
    // way, just without going through the face lookup.
    private static void PlaceDockingBayDoorDecoration(PlacedModule mod, StationModuleMesh mesh)
    {
        if (mod.Definition.Category != "docking-bay") return;

        float halfDepth = mod.Definition.BoundingBox.Z * 0.5f;
        float doorW     = mod.Definition.DoorOpening.X;
        float doorH     = mod.Definition.DoorOpening.Y;

        Vector3 doorNormal = -Vector3.UnitZ;
        Vector3 doorCenter = new(0, 0, -halfDepth);
        var (right, up)    = TangentFrame(doorNormal);

        // Guidance lights — 4 pulsing white lights above the opening and 4 below, mounted flush
        // on the door THROAT (the recessed prism wall between the frame's outer and inner
        // faces — see DockingBayHull.AddDoorThroat), not on the frame's outward-facing strips.
        // A light flush with the frame face only really reads face-on from outside; recessed
        // into the throat, facing inward (perpendicular to the door's own normal), it's visible
        // looking down the tunnel from either side. Z sits at the throat's approximate
        // mid-depth — NominalWallThickness is the same non-seeded approximation already used to
        // size the envelope (StationModuleRegistry.CreateDockingBay), not the real per-module
        // seeded thickness, which this decoration pass has no access to and doesn't need to
        // match exactly. u stays well inside the flat strip, away from the chamfered corners.
        Color white   = new(230, 240, 255);
        Color housing = new(35, 35, 35);
        float throatMidZ = -halfDepth + StationModuleRegistry.NominalWallThickness * 0.5f;
        foreach (float rowSign in new[] { 1f, -1f })
        {
            Vector3 mountNormal = -up * rowSign;   // top row faces down into the throat, bottom row faces up
            for (int i = 0; i < 4; i++)
            {
                float u = ((float)i / 3f - 0.5f) * doorW * 0.7f;
                Vector3 pos = right * u + up * rowSign * (doorH * 0.5f)
                            + new Vector3(0, 0, throatMidZ) + mountNormal * 0.05f;

                var (vb, glowPos) = AddLight(mesh, pos, mountNormal, 0.35f, housing, white);
                mesh.AnimTags.Add(new AnimTag
                {
                    Type       = AnimType.Pulse,
                    VertexBase = vb,
                    OnColor    = white,
                    OffColor   = new Color(20, 22, 30),
                    Period     = 2f,
                    Phase      = (float)i / 4f,
                });
                // Missing previously — the pulsing housing/lens geometry existed, but with no
                // GlowLights entry the billboard glow sprite (ComputeGlowIntensity /
                // SystemSpaceState.Stations.cs) never drew, so the light never actually "shone".
                mod.GlowLights.Add(new StationLightInfo(
                    WorldPosition: Vector3.Transform(glowPos, mod.Transform),
                    Colour:        white,
                    Type:          GlowType.DockGuidance,
                    BaseIntensity: 1.0f,
                    Rate:          1f / 2f,
                    Phase:         (float)i / 4f,
                    Pattern:       LightPattern.SlowPulse));
            }
        }

        // Hazard signage — a color-coded placard on the frame above the opening, reusing the
        // same per-pixel bitmap-font geometry already proven on shipping containers rather
        // than a new rendering capability. The guidance lights no longer sit on this face (moved
        // into the throat above), so this offset is just clearance from the opening itself now.
        const string text      = "CAUTION - BAY";
        Color        signColor = new(230, 180, 40);
        const float signMargin = 0.6f;
        float pixelSize = System.Math.Clamp(
            doorW * 0.6f / (text.Length * (BitmapFonts.CharW + 1)), 0.05f, 0.30f);
        float textW = text.Length * (BitmapFonts.CharW + 1) * pixelSize;
        Vector3 textOrigin = doorCenter - right * (textW * 0.5f)
                            + up * (doorH * 0.5f + signMargin)
                            + doorNormal * 0.02f;   // proud of the frame surface, avoids z-fighting

        ShippingContainerFactory.AddTextGeometry(mesh, text, textOrigin,
            textRight: right, textUp: up, textNormal: doorNormal, pixelSize, signColor);
    }

    // ── Pass 8: Ambient position markers ─────────────────────────────────────

    private static void RegisterModuleAmbientLights(PlacedModule mod, FaceInfo[] faces,
        System.Random rng)
    {
        if (rng.NextDouble() > 0.60) return;

        int count    = rng.Next(1, 3);
        var eligible = faces.Where(f => f.IsExposed).ToList();
        if (eligible.Count == 0) return;

        for (int i = 0; i < count && i < eligible.Count; i++)
        {
            var face  = eligible[rng.Next(eligible.Count)];
            float cu  = ((float)rng.NextDouble() - 0.5f) * face.Width  * 0.6f;
            float cv  = ((float)rng.NextDouble() - 0.5f) * face.Height * 0.6f;
            Color col = PickMarkerColour(mod.Definition.Category, rng);
            float intensity = 0.18f + (float)rng.NextDouble() * 0.14f;

            mod.GlowLights.Add(new StationLightInfo(
                WorldPosition: StationPoint(mod, face, cu, cv, 0.05f),
                Colour:        col,
                Type:          GlowType.AmbientMarker,
                BaseIntensity: intensity,
                Rate:          0f,
                Phase:         0f,
                Pattern:       LightPattern.Continuous));
        }
    }

    private static Color PickMarkerColour(string category, System.Random rng)
    {
        double r = rng.NextDouble();
        return category switch
        {
            "science"  => r < 0.50 ? new Color(150, 200, 255)
                        : r < 0.80 ? new Color(200, 240, 255)
                        :            new Color(130, 255, 160),
            "docking"  => r < 0.60 ? new Color(255, 200, 80)
                        :            new Color(200, 220, 255),
            "military" => r < 0.70 ? new Color(255, 80, 80)
                        :            new Color(200, 200, 200),
            "hab" or "luxury"
                       => r < 0.50 ? new Color(255, 240, 200)
                        : r < 0.75 ? new Color(255, 220, 140)
                        :            new Color(200, 220, 255),
            _          => r < 0.40 ? new Color(255, 220, 140)
                        : r < 0.70 ? new Color(220, 225, 255)
                        :            new Color(255, 255, 240),
        };
    }

    // ── Texture helpers ───────────────────────────────────────────────────────

    private static SurfaceTexture TextureFor(string category) => category switch
    {
        "hab" or "luxury"            => SurfaceTexture.CleanPanel,
        "science" or "military"
        or "core"                    => SurfaceTexture.TechPanel,
        "industrial" or "fuel"       => SurfaceTexture.IndustrialPanel,
        "cargo"                      => SurfaceTexture.CargoPanel,
        _                            => SurfaceTexture.CleanPanel,
    };

    // ── Glass gradient helpers ────────────────────────────────────────────────

    private static Color GlassTopColor(Color c) => Color.Lerp(c, Color.White, 0.18f);

    private static Color GlassBottomColor(Color c) => new Color(
        (byte)MathF.Min(c.R * 0.72f, 255f),
        (byte)MathF.Min(c.G * 0.72f, 255f),
        (byte)Math.Min((int)(c.B * 0.72f) + 8, 255),
        c.A);

    // Maps a vertex position along `upDir` to a gradient colour between bottom and top.
    private static Color GlassGradientColor(Color c, Vector3 v, Vector3 upDir, float minY, float maxY)
    {
        float t = maxY > minY ? (Vector3.Dot(v, upDir) - minY) / (maxY - minY) : 0.5f;
        return Color.Lerp(GlassBottomColor(c), GlassTopColor(c), Math.Clamp(t, 0f, 1f));
    }

    // Emits a glass quad into glassMesh with a vertical gradient (bottom darker/cooler, top lighter).
    // Skips the gradient on near-horizontal faces where up-direction is ambiguous.
    private static void AddGlassQuad(StationModuleMesh glassMesh,
        Vector3 center, Vector3 normal, Vector3 up,
        float width, float height, Color winCol, bool flatColor)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, normal));
        float   hw    = width  * 0.5f;
        float   hh    = height * 0.5f;
        Vector3 bl    = center - right * hw - up * hh;
        Vector3 br    = center + right * hw - up * hh;
        Vector3 tr    = center + right * hw + up * hh;
        Vector3 tl    = center - right * hw + up * hh;

        if (flatColor)
        {
            glassMesh.AddQuad(bl, br, tr, tl, winCol);
            return;
        }

        // BL/BR share the bottom projection; TL/TR share the top.
        float minProj = Vector3.Dot(bl, up);
        float maxProj = Vector3.Dot(tl, up);
        glassMesh.AddQuadGradient(
            bl, GlassGradientColor(winCol, bl, up, minProj, maxProj),
            br, GlassGradientColor(winCol, br, up, minProj, maxProj),
            tr, GlassGradientColor(winCol, tr, up, minProj, maxProj),
            tl, GlassGradientColor(winCol, tl, up, minProj, maxProj));
    }

    // ── Colour helpers ────────────────────────────────────────────────────────

    private static Color DarkenColor(Color c, float factor) => new(
        (int)(c.R * factor),
        (int)(c.G * factor),
        (int)(c.B * factor),
        c.A);

    internal static Color LightenColor(Color c, float factor) => new(
        (byte)Math.Min(c.R * factor, 255),
        (byte)Math.Min(c.G * factor, 255),
        (byte)Math.Min(c.B * factor, 255),
        c.A);

    // ── Shipping containers ───────────────────────────────────────────────────

    // Container body: 6.0 × 2.5 × 2.5 m (L × W × H).
    // Placed flat on module faces, one long side resting on the surface.
    // Orientation: long axis horizontal (60%) or vertical (40%) relative to face axes.

    private const float ContainerL = 6.0f;
    private const float ContainerS = 2.5f;  // short dimension (square cross-section)

    // Per-station colour palette derived from station seed; varies by category.
    private static readonly Color[] ContainerColorsBase =
    [
        new Color(180, 55,  40),   // freight red
        new Color( 40, 80, 160),   // shipping blue
        new Color( 50,115,  50),   // industrial green
        new Color(145,120,  40),   // mustard yellow
        new Color( 90, 90,  90),   // neutral grey
        new Color(155, 80,  30),   // rust orange
        new Color( 60,100,130),    // slate blue
        new Color(100, 55,  55),   // dark red
    ];

    private static void GenerateContainers(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng)
    {
        if (!face.IsExposed) return;
        // Need at least enough space for one container laid on its smallest footprint
        if (face.Width < ContainerS + 0.6f || face.Height < ContainerS + 0.6f) return;

        float prob = mod.Definition.Category switch
        {
            "cargo"      => 0.85f,
            "docking"    => 0.70f,
            "industrial" => 0.60f,
            "core"       => 0.40f,
            "military"   => 0.35f,
            "fuel"       => 0.20f,
            "hab"        => 0.15f,
            "connector"  => 0.08f,
            _            => 0.12f,
        };
        if (rng.NextDouble() > prob) return;

        int maxContainers = (face.Width * face.Height) switch
        {
            >= 60f => 4,
            >= 30f => 3,
            >= 15f => 2,
            _      => 1,
        };

        // Pick 2–3 colours from the palette so containers on the same face have variety
        Color[] palette = PickContainerPalette(mod.Seed, rng);

        int placed = 0;
        double nextProb = 1.0;
        while (placed < maxContainers && rng.NextDouble() < nextProb)
        {
            PlaceContainer(mod, face, mesh, occupancy, rng, palette[rng.Next(palette.Length)]);
            placed++;
            nextProb = placed == 1 ? 0.60 : 0.35;
        }
    }

    private static Color[] PickContainerPalette(int modSeed, System.Random rng)
    {
        // 2–3 randomly chosen colours, offset by mod seed so different stations have variety
        var pool = ContainerColorsBase;
        int startIdx = (modSeed & 0x7FFF) % pool.Length;
        return
        [
            pool[startIdx % pool.Length],
            pool[(startIdx + 1 + rng.Next(3)) % pool.Length],
            pool[(startIdx + 3 + rng.Next(2)) % pool.Length],
        ];
    }

    private static void PlaceContainer(PlacedModule mod, FaceInfo face,
        StationModuleMesh mesh, FaceOccupancy occupancy, System.Random rng, Color color)
    {
        // Decide orientation: long axis along Right (horizontal) or Up (vertical)
        bool longHoriz = rng.NextDouble() < 0.6;

        // Footprint on the face
        float footRight = longHoriz ? ContainerL : ContainerS;
        float footUp    = longHoriz ? ContainerS : ContainerL;
        float halfFR    = footRight * 0.5f;
        float halfFU    = footUp    * 0.5f;

        // Check it can fit inside the face at all — stay clear of the chamfer bevel the
        // same way GeneratePanelSeams does, box modules only.
        float chamferInset = mod.Definition.MeshFactory == null ? mod.ChamferDepth * 0.707f : 0f;
        float marginR = face.Width  * 0.5f - chamferInset - halfFR;
        float marginU = face.Height * 0.5f - chamferInset - halfFU;
        if (marginR < 0f || marginU < 0f) return;

        // Try a few random positions
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float cu = (float)(rng.NextDouble() * 2 - 1) * MathF.Max(marginR - 0.3f, 0f);
            float cv = (float)(rng.NextDouble() * 2 - 1) * MathF.Max(marginU - 0.3f, 0f);

            if (!occupancy.TryOccupy(cu, cv, halfFR, halfFU, 0.20f)) continue;

            // Module-local centre of the container
            // Container sits on the face: stick out ContainerS * 0.5 in normal direction
            Vector3 centre = face.LocalCenter
                + face.LocalRight  * cu
                + face.LocalUp     * cv
                + face.LocalNormal * (ContainerS * 0.5f);

            // Build the oriented-box transform.
            // X axis = long axis of container, Z axis = face normal (depth off surface).
            Matrix t;
            if (longHoriz)
            {
                // Long axis along face.Right
                t = new Matrix(
                    face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                    face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                    face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                    centre.X,           centre.Y,           centre.Z,           1);
            }
            else
            {
                // Long axis along face.Up
                t = new Matrix(
                    face.LocalUp.X,     face.LocalUp.Y,     face.LocalUp.Z,     0,
                    face.LocalRight.X,  face.LocalRight.Y,  face.LocalRight.Z,  0,
                    face.LocalNormal.X, face.LocalNormal.Y, face.LocalNormal.Z, 0,
                    centre.X,           centre.Y,           centre.Z,           1);
            }

            // Same factory used for standalone/debug-spawn containers — station-placed
            // greeble containers now get the identical chamfer/inset/fastener/text/wear
            // geometry instead of a separately hand-maintained reimplementation (that
            // reimplementation had drifted from the factory's own conventions, which is
            // why station-placed container text mirrored differently than standalone).
            // MergeTransformed detects and corrects handedness automatically, so the old
            // manual axisY-flip check that used to live here is gone — t is passed through
            // unchanged. No lighting pre-rotation is needed any more either: the sun term
            // is computed per frame in LitSurface.fx from each vertex's real world normal
            // (t maps container-local space into module-local space; the module's own
            // rotation and the station's spin are applied once, at draw time, to every
            // vertex uniformly — there is no separate bake-time basis to get wrong).
            var (verts, indices) = ShippingContainerFactory.GenerateVertices(
                color, wear: (float)(0.1 + rng.NextDouble() * 0.5), sidePatternSeed: rng.Next(),
                text: null, lockGrade: LockGrade.Civilian);
            mesh.MergeTransformed(verts, indices, t);
            break;
        }
    }
}
