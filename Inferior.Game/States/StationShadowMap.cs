using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics;

namespace Inferior.Game.States;

internal sealed class StationShadowMap : IDisposable
{
    private readonly GraphicsDevice _gd;

    public RenderTarget2D Texture { get; }
    public RenderTarget2D LightCameraSolidTexture { get; }
    public RenderTarget2D CasterCoverageTexture { get; }
    public RenderTarget2D CasterOwnerTexture { get; }
    public RenderTarget2D SelectedModuleHullDepthTexture { get; }
    public RenderTarget2D Module5HullFaceOwnerTexture { get; }
    public SurfaceFormat SurfaceFormat => Texture.Format;
    public Matrix LightView { get; private set; }
    public Matrix LightProjection { get; private set; }
    public Matrix LightViewProjection { get; private set; }
    public Vector2 LightProjectionSize { get; private set; }
    public StationShadowDepthRange DepthRange { get; private set; }
    public StationShadowBounds Bounds { get; private set; }
    public Vector3 StationLocalSunDirection { get; private set; }

    public StationShadowMap(GraphicsDevice gd, int size)
    {
        _gd = gd;
        Texture = CreateDepthTarget(gd, size);
        LightCameraSolidTexture = CreateDiagnosticTarget(gd, size);
        CasterCoverageTexture = CreateDiagnosticTarget(gd, size);
        CasterOwnerTexture = CreateDiagnosticTarget(gd, size);
        SelectedModuleHullDepthTexture = CreateDepthTarget(gd, size);
        Module5HullFaceOwnerTexture = CreateDiagnosticTarget(gd, size);
    }

    public void Build(
        string stationIdentity,
        Effect effect,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> decoMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> glassMeshes,
        Vector3 stationLocalSunDirection,
        float receiverNormalOffsetMetres)
    {
        StationLocalSunDirection = Vector3.Normalize(stationLocalSunDirection);
        Bounds = StationShadowMath.ComputeStationBounds(modules, padding: 0f);
        // SceneLighting.SunDirection points from the station toward the star. Put the
        // shadow camera on that sunward side so nearest depth is the first surface hit by light.
        LightView = StationShadowMath.CreateLightView(StationLocalSunDirection, Bounds);
        LightProjection = StationShadowMath.CreateLightProjection(
            Bounds, LightView, xyPadding: 25f, zPadding: 2f, out StationShadowDepthRange depthRange);
        DepthRange = depthRange;
        LightViewProjection = LightView * LightProjection;
        LightProjectionSize = new Vector2(2f / LightProjection.M11, 2f / LightProjection.M22);

        CountSubmissions(modules, hullMeshes, decoMeshes, glassMeshes,
            out int hullDraws, out int decoDraws, out int glassDraws,
            out int hullPrimitives, out int decoPrimitives, out int glassPrimitives);
        int totalDraws = hullDraws + decoDraws + glassDraws;
        int totalPrimitives = hullPrimitives + decoPrimitives + glassPrimitives;

        Debug.WriteLine(
            "[StationShadowDiagnostic] " +
            $"station=\"{stationIdentity}\" owner={GetHashCode():X8} " +
            $"target={Texture.Width}x{Texture.Height} format={Texture.Format} requestedFormat=Single clear=White depth=Depth24 " +
            $"solidTarget={LightCameraSolidTexture.Width}x{LightCameraSolidTexture.Height} format={LightCameraSolidTexture.Format} clear=Black " +
            $"coverageTarget={CasterCoverageTexture.Width}x{CasterCoverageTexture.Height} format={CasterCoverageTexture.Format} clear=Black " +
            $"ownerTarget={CasterOwnerTexture.Width}x{CasterOwnerTexture.Height} format={CasterOwnerTexture.Format} clear=Black " +
            $"selectedModuleHullDepthTarget={SelectedModuleHullDepthTexture.Width}x{SelectedModuleHullDepthTexture.Height} format={SelectedModuleHullDepthTexture.Format} clear=White moduleIndex=5 meshClass=Hull " +
            $"module5HullFaceOwnerTarget={Module5HullFaceOwnerTexture.Width}x{Module5HullFaceOwnerTexture.Height} format={Module5HullFaceOwnerTexture.Format} clear=Black moduleIndex=5 meshClass=Hull " +
            $"near={DepthRange.Near:0.###}m far={DepthRange.Far:0.###}m span={DepthRange.Length:0.###}m " +
            $"stationLocalSun={FormatVector(StationLocalSunDirection)} boundsMin={FormatVector(Bounds.Min)} boundsMax={FormatVector(Bounds.Max)} " +
            $"expectedModules={modules.Count} casterDraws={totalDraws} casterPrimitives={totalPrimitives} " +
            $"hullDraws={hullDraws} hullPrimitives={hullPrimitives} " +
            $"decoDraws={decoDraws} decoPrimitives={decoPrimitives} " +
            $"glassDraws={glassDraws} glassPrimitives={glassPrimitives}");
        Debug.WriteLine($"[StationShadowDiagnostic] LightView {FormatMatrix(LightView)}");
        Debug.WriteLine($"[StationShadowDiagnostic] LightProjection {FormatMatrix(LightProjection)}");
        Debug.WriteLine($"[StationShadowDiagnostic] LightViewProjection {FormatMatrix(LightViewProjection)}");

        RenderTargetBinding[] oldTargets = _gd.GetRenderTargets();
        Viewport oldViewport = _gd.Viewport;
        RasterizerState oldRasterizer = _gd.RasterizerState;
        DepthStencilState oldDepth = _gd.DepthStencilState;
        BlendState oldBlend = _gd.BlendState;

        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        RenderDiagnosticTarget(effect, LightCameraSolidTexture, "LightCameraSolid", modules,
            hullMeshes, decoMeshes, glassMeshes);
        RenderDiagnosticTarget(effect, CasterCoverageTexture, "CasterCoverage", modules,
            hullMeshes, decoMeshes, glassMeshes);
        RenderCasterOwnerTarget(effect, CasterOwnerTexture, modules,
            hullMeshes, decoMeshes, glassMeshes);
        RenderSelectedModuleHullDepthTarget(effect, SelectedModuleHullDepthTexture, selectedModuleIndex: 5,
            modules, hullMeshes);
        RenderModule5HullFaceOwnerTarget(effect, Module5HullFaceOwnerTexture, selectedModuleIndex: 5,
            modules, hullMeshes);
        LogSelectedModuleHullOffsetDiagnostics(stationIdentity, selectedModuleIndex: 5, modules,
            receiverNormalOffsetMetres);

        _gd.SetRenderTarget(Texture);
        _gd.Clear(Color.White);
        Debug.Assert(IsActiveRenderTarget(Texture), "Production shadow depth pass is not bound to the production shadow texture.");

        effect.CurrentTechnique = effect.Techniques["ShadowDepth"];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        foreach (var mod in modules)
        {
            effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);

            if (hullMeshes.TryGetValue(mod, out var hull))
                Draw(effect, hull.vb, hull.ib, hull.triCount);
            if (decoMeshes.TryGetValue(mod, out var deco))
                Draw(effect, deco.vb, deco.ib, deco.triCount);
            if (glassMeshes.TryGetValue(mod, out var glass))
                Draw(effect, glass.vb, glass.ib, glass.triCount);
        }

        _gd.SetRenderTargets(oldTargets);
        _gd.Viewport = oldViewport;
        _gd.RasterizerState = oldRasterizer;
        _gd.DepthStencilState = oldDepth;
        _gd.BlendState = oldBlend;
    }

    public void Dispose()
    {
        Texture.Dispose();
        LightCameraSolidTexture.Dispose();
        CasterCoverageTexture.Dispose();
        CasterOwnerTexture.Dispose();
        SelectedModuleHullDepthTexture.Dispose();
        Module5HullFaceOwnerTexture.Dispose();
    }

    private static RenderTarget2D CreateDepthTarget(GraphicsDevice gd, int size)
    {
        return new RenderTarget2D(gd, size, size, false, SurfaceFormat.Single, DepthFormat.Depth24);
    }

    private static RenderTarget2D CreateDiagnosticTarget(GraphicsDevice gd, int size)
    {
        return new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
    }

    private bool IsActiveRenderTarget(RenderTarget2D expected)
    {
        RenderTargetBinding[] targets = _gd.GetRenderTargets();
        return targets.Length == 1 && ReferenceEquals(targets[0].RenderTarget, expected);
    }

    private void RenderDiagnosticTarget(
        Effect effect,
        RenderTarget2D target,
        string technique,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> decoMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> glassMeshes)
    {
        _gd.SetRenderTarget(target);
        _gd.Clear(Color.Black);
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        effect.CurrentTechnique = effect.Techniques[technique];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        foreach (var mod in modules)
        {
            effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);

            if (hullMeshes.TryGetValue(mod, out var hull))
                Draw(effect, hull.vb, hull.ib, hull.triCount);
            if (decoMeshes.TryGetValue(mod, out var deco))
                Draw(effect, deco.vb, deco.ib, deco.triCount);
            if (glassMeshes.TryGetValue(mod, out var glass))
                Draw(effect, glass.vb, glass.ib, glass.triCount);
        }
    }

    private void RenderCasterOwnerTarget(
        Effect effect,
        RenderTarget2D target,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> decoMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> glassMeshes)
    {
        _gd.SetRenderTarget(target);
        _gd.Clear(Color.Black);
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        effect.CurrentTechnique = effect.Techniques["CasterOwner"];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            var mod = modules[moduleIndex];
            effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);
            effect.Parameters["ShadowDebugSolidColor"]?.SetValue(SystemSpaceState.ModuleDebugColor(moduleIndex).ToVector4());

            if (hullMeshes.TryGetValue(mod, out var hull))
                Draw(effect, hull.vb, hull.ib, hull.triCount);
            if (decoMeshes.TryGetValue(mod, out var deco))
                Draw(effect, deco.vb, deco.ib, deco.triCount);
            if (glassMeshes.TryGetValue(mod, out var glass))
                Draw(effect, glass.vb, glass.ib, glass.triCount);
        }
    }

    private void RenderSelectedModuleHullDepthTarget(
        Effect effect,
        RenderTarget2D target,
        int selectedModuleIndex,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes)
    {
        _gd.SetRenderTarget(target);
        _gd.Clear(Color.White);
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        effect.CurrentTechnique = effect.Techniques["ShadowDepth"];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        if ((uint)selectedModuleIndex >= (uint)modules.Count)
        {
            Debug.WriteLine(
                $"[StationShadowSelectedHullDepth] skipped moduleIndex={selectedModuleIndex} moduleCount={modules.Count}");
            return;
        }

        var mod = modules[selectedModuleIndex];
        effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);
        if (hullMeshes.TryGetValue(mod, out var hull))
        {
            Draw(effect, hull.vb, hull.ib, hull.triCount);
            Debug.WriteLine(
                $"[StationShadowSelectedHullDepth] moduleIndex={selectedModuleIndex} definition={mod.Definition.Id} meshClass=Hull primitives={hull.triCount}");
        }
        else
        {
            Debug.WriteLine(
                $"[StationShadowSelectedHullDepth] missing hull mesh moduleIndex={selectedModuleIndex} definition={mod.Definition.Id}");
        }
    }

    private void RenderModule5HullFaceOwnerTarget(
        Effect effect,
        RenderTarget2D target,
        int selectedModuleIndex,
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes)
    {
        _gd.SetRenderTarget(target);
        _gd.Clear(Color.Black);
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.BlendState = BlendState.Opaque;

        effect.CurrentTechnique = effect.Techniques["CasterOwner"];
        effect.Parameters["LightView"]?.SetValue(LightView);
        effect.Parameters["LightViewProjection"]?.SetValue(LightViewProjection);
        effect.Parameters["LightDepthNear"]?.SetValue(DepthRange.Near);
        effect.Parameters["LightDepthFar"]?.SetValue(DepthRange.Far);

        if ((uint)selectedModuleIndex >= (uint)modules.Count)
        {
            Debug.WriteLine(
                $"[StationShadowModule5HullFaceOwner] skipped moduleIndex={selectedModuleIndex} moduleCount={modules.Count}");
            return;
        }

        var mod = modules[selectedModuleIndex];
        effect.Parameters["StationLocalWorld"]?.SetValue(mod.Transform);
        if (!hullMeshes.TryGetValue(mod, out var hull))
        {
            Debug.WriteLine(
                $"[StationShadowModule5HullFaceOwner] missing hull mesh moduleIndex={selectedModuleIndex} definition={mod.Definition.Id}");
            return;
        }

        for (int hullFace = 0; hullFace < 6; hullFace++)
        {
            int faceId = Module5HullFaceIdFromHullFace(hullFace);
            effect.Parameters["ShadowDebugSolidColor"]?.SetValue(
                SystemSpaceState.Module5HullFaceDebugColor(faceId).ToVector4());
            DrawRange(effect, hull.vb, hull.ib, startIndex: hullFace * 6, primitiveCount: 2);
        }

        Debug.WriteLine(
            $"[StationShadowModule5HullFaceOwner] moduleIndex={selectedModuleIndex} definition={mod.Definition.Id} meshClass=Hull faces=6 primitives=12");
    }

    private void LogSelectedModuleHullOffsetDiagnostics(
        string stationIdentity,
        int selectedModuleIndex,
        IReadOnlyList<PlacedModule> modules,
        float receiverNormalOffsetMetres)
    {
        if ((uint)selectedModuleIndex >= (uint)modules.Count)
            return;

        var mod = modules[selectedModuleIndex];
        foreach (var sample in EnumerateHullFaceSamples(mod))
        {
            Vector3 stationPoint = Vector3.Transform(sample.LocalPoint, mod.Transform);
            Vector3 stationNormal = Vector3.Normalize(Vector3.TransformNormal(sample.LocalNormal, mod.Transform));
            float ndotl = MathHelper.Clamp(Vector3.Dot(stationNormal, StationLocalSunDirection), 0f, 1f);
            float slopeFactor = 1f - ndotl;
            float offsetMetres = receiverNormalOffsetMetres * slopeFactor;
            Vector3 offsetPoint = stationPoint + stationNormal * offsetMetres;

            Vector4 unoffset = ProjectShadowPoint(stationPoint);
            Vector4 offset = ProjectShadowPoint(offsetPoint);
            Vector2 uv = ShadowUv(unoffset);
            Vector2 offsetUv = ShadowUv(offset);
            Vector2 displacementTexels = (offsetUv - uv) * Texture.Width;
            float depth = NormalizeLightDepth(Vector3.Transform(stationPoint, LightView).Z);
            float offsetDepth = NormalizeLightDepth(Vector3.Transform(offsetPoint, LightView).Z);
            float depthDelta = offsetDepth - depth;

            Debug.WriteLine(
                "[StationShadowNormalOffsetDiagnostic] " +
                $"station=\"{stationIdentity}\" moduleIndex={selectedModuleIndex} definition={mod.Definition.Id} " +
                $"sample={sample.Name} normal={FormatVector(stationNormal)} slope={slopeFactor:0.######} offsetMetres={offsetMetres:0.######} " +
                $"uv=({uv.X:0.######},{uv.Y:0.######}) offsetUv=({offsetUv.X:0.######},{offsetUv.Y:0.######}) " +
                $"uvDisplacementTexels=({displacementTexels.X:0.###},{displacementTexels.Y:0.###}) " +
                $"receiverDepthDisplacement={depthDelta:0.########}");
        }
    }

    private readonly record struct HullFaceSample(string Name, Vector3 LocalPoint, Vector3 LocalNormal);

    private static IEnumerable<HullFaceSample> EnumerateHullFaceSamples(PlacedModule mod)
    {
        float si = mod.ChamferDepth * 0.707f;
        Vector3 h = mod.Definition.BoundingBox * 0.5f;

        yield return new("+Z center", new(0f, 0f, +h.Z), Vector3.UnitZ);
        yield return new("+Z corner0", new(-h.X + si, -h.Y + si, +h.Z), Vector3.UnitZ);
        yield return new("+Z corner2", new(+h.X - si, +h.Y - si, +h.Z), Vector3.UnitZ);
        yield return new("-Z center", new(0f, 0f, -h.Z), -Vector3.UnitZ);
        yield return new("-Z corner0", new(+h.X - si, -h.Y + si, -h.Z), -Vector3.UnitZ);
        yield return new("-Z corner2", new(-h.X + si, +h.Y - si, -h.Z), -Vector3.UnitZ);
        yield return new("-X center", new(-h.X, 0f, 0f), -Vector3.UnitX);
        yield return new("-X corner0", new(-h.X, -h.Y + si, -h.Z + si), -Vector3.UnitX);
        yield return new("-X corner2", new(-h.X, +h.Y - si, +h.Z - si), -Vector3.UnitX);
        yield return new("+X center", new(+h.X, 0f, 0f), Vector3.UnitX);
        yield return new("+X corner0", new(+h.X, -h.Y + si, +h.Z - si), Vector3.UnitX);
        yield return new("+X corner2", new(+h.X, +h.Y - si, -h.Z + si), Vector3.UnitX);
        yield return new("+Y center", new(0f, +h.Y, 0f), Vector3.UnitY);
        yield return new("+Y corner0", new(-h.X + si, +h.Y, +h.Z - si), Vector3.UnitY);
        yield return new("+Y corner2", new(+h.X - si, +h.Y, -h.Z + si), Vector3.UnitY);
        yield return new("-Y center", new(0f, -h.Y, 0f), -Vector3.UnitY);
        yield return new("-Y corner0", new(-h.X + si, -h.Y, -h.Z + si), -Vector3.UnitY);
        yield return new("-Y corner2", new(+h.X - si, -h.Y, +h.Z - si), -Vector3.UnitY);
    }

    private Vector4 ProjectShadowPoint(Vector3 stationPoint)
        => Vector4.Transform(new Vector4(stationPoint, 1f), LightViewProjection);

    private float NormalizeLightDepth(float lightViewZ)
        => MathHelper.Clamp((-lightViewZ - DepthRange.Near) / MathF.Max(DepthRange.Length, 0.000001f), 0f, 1f);

    private static Vector2 ShadowUv(Vector4 shadowCoord)
    {
        Vector3 proj = new(shadowCoord.X / shadowCoord.W, shadowCoord.Y / shadowCoord.W, shadowCoord.Z / shadowCoord.W);
        return new Vector2(proj.X * 0.5f + 0.5f, -proj.Y * 0.5f + 0.5f);
    }

    private static void CountSubmissions(
        IReadOnlyList<PlacedModule> modules,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> hullMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> decoMeshes,
        IReadOnlyDictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> glassMeshes,
        out int hullDraws,
        out int decoDraws,
        out int glassDraws,
        out int hullPrimitives,
        out int decoPrimitives,
        out int glassPrimitives)
    {
        hullDraws = decoDraws = glassDraws = 0;
        hullPrimitives = decoPrimitives = glassPrimitives = 0;

        foreach (var mod in modules)
        {
            if (hullMeshes.TryGetValue(mod, out var hull))
            {
                hullDraws++;
                hullPrimitives += hull.triCount;
            }
            if (decoMeshes.TryGetValue(mod, out var deco))
            {
                decoDraws++;
                decoPrimitives += deco.triCount;
            }
            if (glassMeshes.TryGetValue(mod, out var glass))
            {
                glassDraws++;
                glassPrimitives += glass.triCount;
            }
        }
    }

    private static string FormatVector(Vector3 v)
        => $"({v.X:0.######},{v.Y:0.######},{v.Z:0.######})";

    private static string FormatMatrix(Matrix m)
        => $"[{m.M11:0.######},{m.M12:0.######},{m.M13:0.######},{m.M14:0.######};" +
           $"{m.M21:0.######},{m.M22:0.######},{m.M23:0.######},{m.M24:0.######};" +
           $"{m.M31:0.######},{m.M32:0.######},{m.M33:0.######},{m.M34:0.######};" +
           $"{m.M41:0.######},{m.M42:0.######},{m.M43:0.######},{m.M44:0.######}]";

    private static int Module5HullFaceIdFromHullFace(int hullFace)
        => hullFace switch
        {
            0 => 4, // +Z
            1 => 5, // -Z
            2 => 1, // -X
            3 => 0, // +X
            4 => 2, // +Y
            5 => 3, // -Y
            _ => -1,
        };

    private void Draw(Effect effect, VertexBuffer vb, IndexBuffer ib, int triCount)
    {
        _gd.SetVertexBuffer(vb);
        _gd.Indices = ib;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triCount);
        }
    }

    private void DrawRange(Effect effect, VertexBuffer vb, IndexBuffer ib, int startIndex, int primitiveCount)
    {
        _gd.SetVertexBuffer(vb);
        _gd.Indices = ib;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                startIndex: startIndex,
                primitiveCount: primitiveCount);
        }
    }
}
