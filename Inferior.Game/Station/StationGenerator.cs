using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Galaxy;

namespace Inferior.Game.StationGen;

/// <summary>
/// Procedural station builder. Grows a station by attaching modules port-to-port,
/// computing exact 3D alignment for each connection and rejecting overlaps.
/// </summary>
public sealed class StationGenerator
{
    private readonly SeededRandom     _rng;
    private readonly List<PlacedModule> _placed = [];

    private StationGenerator(int seed) { _rng = new SeededRandom(seed); }

    public static List<PlacedModule> Generate(Galaxy.Station station, GraphicsDevice gd,
                                               double gameTime = 0.0)
    {
        int seed = NameHash(station.Name);
        var gen  = new StationGenerator(seed);

        StationScale scale = station.Size switch
        {
            StationSize.Small  => StationScale.Outpost,
            StationSize.Medium => StationScale.Station,
            StationSize.Large  => StationScale.Port,
            _                  => StationScale.Outpost,
        };

        var modules = gen.Run(station);
        ValidatePlacement(modules);
        PopulateLandingPads(station, modules);

        var profile = StationProfile.Generate(seed, scale);
        var palette = TexturePalette.From(profile);
        AssignTextures(modules, gd, palette, station.Name);

        StationDecorator.Decorate(modules);
        var sysQ      = station.GetOrientation(gameTime);
        var stRotQ    = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
        BakeLighting(modules, Matrix.CreateFromQuaternion(stRotQ));
        StationDecorator.ApplyAmbientOcclusion(modules);
        return modules;
    }

    private static void AssignTextures(
        List<PlacedModule> modules,
        GraphicsDevice     gd,
        TexturePalette     palette,
        string             stationName)
    {
        foreach (var mod in modules)
        {
            var surface = SurfaceFor(mod.Definition.Category);
            mod.TextureInstance = StationTextureRegistry.GetOrCreate(gd, surface, palette, mod.Seed);
        }

        // Overlay the station name on the core module's face texture.
        if (modules.Count > 0)
            modules[0].TextureInstance = GenerateNameFaceTexture(gd, modules[0].TextureInstance, stationName, palette);
    }

    private static Texture2D GenerateNameFaceTexture(
        GraphicsDevice gd,
        Texture2D?     baseTex,
        string         name,
        TexturePalette palette)
    {
        const int Size = 512;
        var pixels = new Color[Size * Size];

        if (baseTex != null)
            baseTex.GetData(pixels);
        else
            Array.Fill(pixels, palette.BaseColour);

        // Render name in two font scales: large centred, with a backing bar.
        int scale  = 4;
        int textW  = TextPainter.MeasureText(name, scale);
        int textH  = TextPainter.MeasureHeight(scale);
        int startX = Math.Clamp((Size - textW) / 2, 4, Size - textW - 4);
        int startY = (Size - textH) / 2;

        // Dark backing strip
        Color barColor = TexturePalette.LerpColor(palette.BaseColour, Color.Black, 0.45f);
        int pad = 8;
        for (int y = startY - pad; y < startY + textH + pad; y++)
        for (int x = 0; x < Size; x++)
            if ((uint)y < Size)
                pixels[y * Size + x] = TexturePalette.LerpColor(pixels[y * Size + x], barColor, 0.70f);

        // Name text
        TextPainter.DrawText(pixels, Size, Size, name, startX, startY, palette.TextColour, scale, alpha: 0.90f);

        var tex = new Texture2D(gd, Size, Size);
        tex.SetData(pixels);
        return tex;
    }

    private static SurfaceTexture SurfaceFor(string category) => category switch
    {
        "hab" or "luxury"                    => SurfaceTexture.CleanPanel,
        "science" or "military" or "core"    => SurfaceTexture.TechPanel,
        "industrial" or "fuel"               => SurfaceTexture.IndustrialPanel,
        "cargo"                              => SurfaceTexture.CargoPanel,
        _                                    => SurfaceTexture.CleanPanel,
    };

    // Bakes SceneLighting into each module's decoration vertex colours.
    // stationRot: the station's world-space rotation at bake time. Combined with each
    // module's local rotation so the N·L is computed in world space, matching the
    // hull pass which applies modRot * stationRot via the BasicEffect World matrix.
    // Must run after Decorate() (so meshes exist) and before Build() (so GPU buffers
    // pick up the modified colours).
    private static void BakeLighting(List<PlacedModule> modules, Matrix stationRot)
    {
        foreach (var mod in modules)
        {
            if (mod.Mesh == null) continue;
            mod.Transform.Decompose(out _, out Quaternion rot, out _);
            Matrix modRot = Matrix.CreateFromQuaternion(rot);
            mod.Mesh.ApplyLighting(
                modRot * stationRot,
                SceneLighting.SunDirection,
                SceneLighting.Ambient,
                SceneLighting.SunColour);
        }
    }

    // Asserts that every module except the core (index 0) has a non-null AttachmentPort.
    // A null AttachmentPort means the module was placed without a parent connection — an orphan.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void ValidatePlacement(List<PlacedModule> placed)
    {
        for (int i = 1; i < placed.Count; i++)
        {
            var mod = placed[i];
            if (mod.AttachmentPort != null) continue;

            throw new InvalidOperationException(
                $"Orphan module at placed[{i}]: '{mod.Definition.Id}' " +
                $"depth={mod.Depth} " +
                $"aabb=[({mod.AabbMin.X:F1},{mod.AabbMin.Y:F1},{mod.AabbMin.Z:F1})" +
                $"..({mod.AabbMax.X:F1},{mod.AabbMax.Y:F1},{mod.AabbMax.Z:F1})]");
        }
    }

    // ── Growth loop ───────────────────────────────────────────────────────────

    private List<PlacedModule> Run(Galaxy.Station station)
    {
        StationScale stationScale = station.Size switch
        {
            StationSize.Small  => StationScale.Outpost,
            StationSize.Medium => StationScale.Station,
            StationSize.Large  => StationScale.Port,
            _                  => StationScale.Outpost,
        };

        // "core" category is the station root — never attach a second core as a child module.
        var availableModules = StationModuleRegistry.All
            .Where(m => m.MinScale <= stationScale && m.Category != "core")
            .ToList();

        if (availableModules.Count == 0) return _placed;

        int moduleLimit = stationScale switch
        {
            StationScale.Outpost     => 8  + _rng.NextInt(12),
            StationScale.Station     => 15 + _rng.NextInt(20),
            StationScale.Port        => 25 + _rng.NextInt(35),
            StationScale.Megastation => 50 + _rng.NextInt(80),
            _                        => 8,
        };

        var archetype = StationArchetypeRegistry.Pick(_rng);

        // Core hub — always placed at the station origin
        var coreDefn = StationModuleRegistry.CoreHub;
        var (coreMin, coreMax) = ComputeWorldAabb(Matrix.Identity, coreDefn.BoundingBox);
        var core = new PlacedModule
        {
            Definition = coreDefn,
            Transform  = Matrix.Identity,
            Seed       = _rng.NextInt(0, 999999),
            Depth      = 0,
            AabbMin    = coreMin,
            AabbMax    = coreMax,
        };
        foreach (var p in coreDefn.Ports)
        {
            if (!p.IsTerminal)
                core.OpenPorts.Add(ToWorldPort(core, p, 0));
        }
        _placed.Add(core);

        // Priority frontier: higher archetype score → expanded sooner.
        // PriorityQueue is a min-heap, so negate the score.
        var frontier = new PriorityQueue<OpenPort, float>();
        foreach (var op in core.OpenPorts)
            frontier.Enqueue(op, -archetype.ScorePort(op, _placed.Count));

        const int MaxAttemptsPerPort = 6;

        while (frontier.Count > 0 && _placed.Count < moduleLimit)
        {
            var port = frontier.Dequeue();

            for (int attempt = 0; attempt < MaxAttemptsPerPort; attempt++)
            {
                var candidate = WeightedPickModule(availableModules, archetype, port.Depth, stationScale);
                var placed    = TryAttach(candidate, port);
                if (placed != null)
                {
                    _placed.Add(placed);
                    foreach (var op in placed.OpenPorts)
                        frontier.Enqueue(op, -archetype.ScorePort(op, _placed.Count));
                    break;
                }
            }
        }

        return _placed;
    }

    // Weighted random module pick factoring in SelectWeight, archetype bias, and station scale.
    private StationModuleDefinition WeightedPickModule(
        List<StationModuleDefinition> candidates,
        IStationArchetype             archetype,
        int                           depth,
        StationScale                  stationScale)
    {
        double total = 0;
        foreach (var m in candidates)
        {
            float sizeBonus = stationScale >= StationScale.Port && m.Id.EndsWith("-large") ? 2.5f : 1.0f;
            total += m.SelectWeight * sizeBonus * archetype.CategoryBias(m.Category, depth);
        }

        double roll  = _rng.NextDouble() * total;
        double accum = 0;
        foreach (var m in candidates)
        {
            float sizeBonus = stationScale >= StationScale.Port && m.Id.EndsWith("-large") ? 2.5f : 1.0f;
            accum += m.SelectWeight * sizeBonus * archetype.CategoryBias(m.Category, depth);
            if (roll < accum) return m;
        }
        return candidates[^1];
    }

    // ── Attachment attempt ────────────────────────────────────────────────────

    private PlacedModule? TryAttach(StationModuleDefinition candidate, OpenPort parentPort)
    {
        // Parent port's AcceptsCategories restricts which module categories can attach
        if (parentPort.Definition.AcceptsCategories.Length > 0
            && !parentPort.Definition.AcceptsCategories.Contains(candidate.Category))
            return null;

        var attachPort = SelectAttachmentPort(candidate, parentPort);
        if (attachPort == null) return null;

        float twist     = _rng.NextInt(3) * (MathF.PI / 2f);   // 0°, 90°, 180°, or 270°
        var   transform = ComputeAttachmentTransform(parentPort, attachPort, twist);

        var (min, max) = ComputeWorldAabb(transform, candidate.BoundingBox);
        if (IntersectsAny(min, max)) return null;

        var placed = new PlacedModule
        {
            Definition     = candidate,
            Transform      = transform,
            Seed           = _rng.NextInt(0, 999999),
            Depth          = parentPort.Depth + 1,
            AabbMin        = min,
            AabbMax        = max,
            AttachmentPort = attachPort,
        };

        // Mark the parent's port as consumed
        parentPort.ParentModule.ChildPorts.Add(parentPort.Definition);

        foreach (var port in candidate.Ports)
        {
            if (port == attachPort) continue;
            if (port.IsTerminal) continue;
            placed.OpenPorts.Add(ToWorldPort(placed, port, placed.Depth));
        }

        return placed;
    }

    // Selects which port on the candidate module will serve as the attachment point.
    private StationPort? SelectAttachmentPort(StationModuleDefinition candidate, OpenPort parentPort)
    {
        var eligible = candidate.Ports
            .Where(p => !p.IsTerminal)
            .Where(p => (int)p.Size <= (int)parentPort.Definition.Size)
            .Where(p => p.AcceptsCategories.Length == 0
                     || p.AcceptsCategories.Contains(parentPort.ParentModule.Definition.Category))
            .ToList();

        if (eligible.Count == 0) return null;
        return eligible[_rng.NextInt(eligible.Count - 1)];
    }

    // ── Port-alignment math ───────────────────────────────────────────────────

    // Computes the world transform for a child module so its chosen attachment
    // port meets the parent port flush, normals opposing.
    private static Matrix ComputeAttachmentTransform(
        OpenPort    parentPort,
        StationPort childAttachPort,
        float       twistAngle)
    {
        Vector3 childLocalNormal = childAttachPort.OutwardNormal;
        Vector3 targetNormal     = -parentPort.WorldNormal;   // child normal must oppose parent normal

        Quaternion r1 = RotationBetween(childLocalNormal, targetNormal);
        Quaternion r2 = Quaternion.CreateFromAxisAngle(-parentPort.WorldNormal, twistAngle);
        Quaternion combinedRot = Quaternion.Normalize(r2 * r1);

        Vector3 rotatedAttachPos = Vector3.Transform(childAttachPort.LocalPosition, combinedRot);
        Vector3 childWorldOrigin = parentPort.WorldPosition - rotatedAttachPos;

        return Matrix.CreateFromQuaternion(combinedRot)
             * Matrix.CreateTranslation(childWorldOrigin);
    }

    // Computes the shortest rotation quaternion from unit vector `from` to unit vector `to`.
    private static Quaternion RotationBetween(Vector3 from, Vector3 to)
    {
        float dot = Vector3.Dot(from, to);

        if (dot >= 0.9999f)
            return Quaternion.Identity;

        // Exactly opposite — rotate 180° around any perpendicular axis
        if (dot <= -0.9999f)
        {
            Vector3 perp = MathF.Abs(from.X) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(from, Vector3.UnitX))
                : Vector3.Normalize(Vector3.Cross(from, Vector3.UnitY));
            return Quaternion.CreateFromAxisAngle(perp, MathF.PI);
        }

        Vector3 axis  = Vector3.Normalize(Vector3.Cross(from, to));
        float   angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(axis, angle));
    }

    // Computes world-space OpenPort from a placed module's transform.
    private static OpenPort ToWorldPort(PlacedModule parent, StationPort port, int depth)
    {
        Vector3 worldPos    = Vector3.Transform(port.LocalPosition, parent.Transform);
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(port.OutwardNormal, parent.Transform));
        return new OpenPort
        {
            ParentModule  = parent,
            Definition    = port,
            WorldPosition = worldPos,
            WorldNormal   = worldNormal,
            Depth         = depth,
        };
    }

    // ── AABB utilities ────────────────────────────────────────────────────────

    // Computes the world-space AABB for a module at the given transform.
    // BoundingBox is the full extents in metres (e.g. (20, 20, 20) for a 20m cube).
    private static (Vector3 min, Vector3 max) ComputeWorldAabb(Matrix transform, Vector3 boundingBox)
    {
        Vector3 half = boundingBox * 0.5f;

        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = new Vector3(-half.X, -half.Y, -half.Z);
        corners[1] = new Vector3(+half.X, -half.Y, -half.Z);
        corners[2] = new Vector3(-half.X, +half.Y, -half.Z);
        corners[3] = new Vector3(+half.X, +half.Y, -half.Z);
        corners[4] = new Vector3(-half.X, -half.Y, +half.Z);
        corners[5] = new Vector3(+half.X, -half.Y, +half.Z);
        corners[6] = new Vector3(-half.X, +half.Y, +half.Z);
        corners[7] = new Vector3(+half.X, +half.Y, +half.Z);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var corner in corners)
        {
            Vector3 world = Vector3.Transform(corner, transform);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }

        return (min, max);
    }

    // Returns true if a candidate AABB overlaps any placed module's AABB.
    // A small margin prevents flush-touching faces from registering as an overlap.
    private bool IntersectsAny(Vector3 candidateMin, Vector3 candidateMax, float margin = 0.5f)
    {
        var shrunkMin = candidateMin + new Vector3(margin);
        var shrunkMax = candidateMax - new Vector3(margin);

        foreach (var m in _placed)
        {
            if (shrunkMax.X > m.AabbMin.X && shrunkMin.X < m.AabbMax.X &&
                shrunkMax.Y > m.AabbMin.Y && shrunkMin.Y < m.AabbMax.Y &&
                shrunkMax.Z > m.AabbMin.Z && shrunkMin.Z < m.AabbMax.Z)
                return true;
        }
        return false;
    }

    // Fills LocalPosition/LocalNormal on each LandingPad from the matching docking port
    // in world space. Simple positional mapping — pads assigned in module iteration order.
    private static void PopulateLandingPads(Galaxy.Station station, List<PlacedModule> modules)
    {
        int padIdx = 0;
        foreach (var mod in modules)
        {
            if (mod.Definition.Category != "docking") continue;
            foreach (var port in mod.Definition.Ports)
            {
                if (!port.IsDocking) continue;
                if (padIdx >= station.LandingPads.Count) return;
                var pad = station.LandingPads[padIdx++];
                Vector3 wp = Vector3.Transform(port.LocalPosition, mod.Transform);
                Vector3 wn = Vector3.Normalize(Vector3.TransformNormal(port.OutwardNormal, mod.Transform));
                pad.LocalPosition = new DVec3(wp.X, wp.Y, wp.Z);
                pad.LocalNormal   = new DVec3(wn.X, wn.Y, wn.Z);

                // Compute pad-forward = visual arrow direction, using module-local normal
                // (port.OutwardNormal) for the tangent frame — same as StationDecorator.TangentFrame.
                // Transforming module-local "up" to station-local space gives the correct forward axis.
                Vector3 localN  = port.OutwardNormal;
                Vector3 hint    = MathF.Abs(localN.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
                Vector3 localR  = Vector3.Normalize(Vector3.Cross(hint, localN));
                Vector3 localUp = Vector3.Normalize(Vector3.Cross(localN, localR));
                Vector3 wf      = Vector3.Normalize(Vector3.TransformNormal(localUp, mod.Transform));
                pad.LocalForward = new DVec3(wf.X, wf.Y, wf.Z);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int NameHash(string name)
    {
        int h = 17;
        foreach (char c in name) h = h * 31 + c;
        return h;
    }
}
