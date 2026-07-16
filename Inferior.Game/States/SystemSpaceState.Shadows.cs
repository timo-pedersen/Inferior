using Inferior.Core.DataBus;
using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private const int StationShadowMapSize = 2048;
    private const float StationShadowPaddingMetres = 5f;
    private const float StationShadowCorrectionLimit = 0.01f;
    // Constant tie-break bias — first rung past zero bias, per the spec's acne ladder.
    // Ceiling is 10mm; this is the first attempt at 5mm. Expressed in metres here;
    // divided by the frame's ShadowDepthSpan at each shadowed draw call to convert into
    // the normalized depth units LitSurface.fx's ShadowBiasDepth compares in.
    private const float StationShadowBiasMetres = 0.005f;

    private Effect? _shadowCasterEffect;
    private RenderTarget2D? _stationShadowMap;
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _shadowCasterMeshes = [];
    private StationShadowContext? _stationShadowContext;
    private bool _showStationShadowOverlay;
    private bool _freezeStationShadowMap;
    private bool _stationShadowBinaryView;
    // Signed receiver-minus-stored depth-delta view (green=0, red=+/shadowed,
    // blue=-/lit, saturating at +-0.5m) — see ShadowDeltaColour in LitSurface.fx.
    private bool _stationShadowDeltaView;
    private bool _stationShadowLogged;
    private bool _stationShadowFreezeLogged;

    private sealed record StationShadowContext(
        Galaxy.Station Station,
        Matrix StationRotation,
        Matrix StationLocalToLightView,
        Matrix LightProjection,
        Vector2 MinXY,
        Vector2 InvSize,
        float Near,
        float DepthSpan,
        double BuildMilliseconds);

    private void InitializeStationShadows()
    {
        _shadowCasterEffect = _content.Load<Effect>("Effects/ShadowCaster");
        _stationShadowMap = new RenderTarget2D(_gd, StationShadowMapSize, StationShadowMapSize,
            false, SurfaceFormat.Single, DepthFormat.Depth24);
        _stationShadowContext = null;
        _stationShadowLogged = false;
        _stationShadowFreezeLogged = false;
    }

    private void DisposeStationShadows()
    {
        foreach (var v in _shadowCasterMeshes.Values)
        {
            v.vb.Dispose();
            v.ib.Dispose();
        }
        _shadowCasterMeshes.Clear();
        _stationShadowMap?.Dispose();
        _stationShadowMap = null;
        _shadowCasterEffect = null;
        _stationShadowContext = null;
    }

    private void BuildStationShadowCasterMeshes(IEnumerable<PlacedModule> modules)
    {
        foreach (var mod in modules)
        {
            if (mod.Definition.MeshFactory == null)
            {
                _shadowCasterMeshes[mod] = BuildHullMesh(_gd, mod);
                continue;
            }

            // Phase B caster policy: docking-bay hull only. Its MeshFactory writes the
            // closed bay hull as the mesh base faces; decoration appended later remains a
            // receiver but does not cast until Phase C.
            if (mod.Definition.Category == "docking-bay" && mod.Mesh != null)
            {
                var bayHull = mod.Mesh.BuildFaceRange(_gd, 0, mod.Mesh.BaseFaceCount);
                if (bayHull.HasValue)
                    _shadowCasterMeshes[mod] = bayHull.Value;
            }
        }
    }

    private void UpdateStationShadowInput(KeyboardState keys)
    {
        // F6 checked for conflicts before wiring (the Ctrl+C lesson): grep found F3, F7-F9,
        // F10-F12 (plain and Ctrl+F12) already bound elsewhere in this codebase; F6 was free.
        bool f6 = keys.IsKeyDown(Keys.F6) && !_prevKeys.IsKeyDown(Keys.F6);
        bool f7 = keys.IsKeyDown(Keys.F7) && !_prevKeys.IsKeyDown(Keys.F7);
        bool f8 = keys.IsKeyDown(Keys.F8) && !_prevKeys.IsKeyDown(Keys.F8);
        bool f9 = keys.IsKeyDown(Keys.F9) && !_prevKeys.IsKeyDown(Keys.F9);

        if (f6)
        {
            _stationShadowDeltaView = !_stationShadowDeltaView;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"Station shadow delta view {(_stationShadowDeltaView ? "ON" : "OFF")}",
                    SystemMessagePriority.NB));
        }
        if (f7)
        {
            _stationShadowBinaryView = !_stationShadowBinaryView;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"Station shadow binary view {(_stationShadowBinaryView ? "ON" : "OFF")}",
                    SystemMessagePriority.NB));
        }
        if (f8)
        {
            _showStationShadowOverlay = !_showStationShadowOverlay;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"Station shadow overlay {(_showStationShadowOverlay ? "ON" : "OFF")}",
                    SystemMessagePriority.NB));
        }
        if (f9)
        {
            _freezeStationShadowMap = !_freezeStationShadowMap;
            _stationShadowFreezeLogged = false;
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"Station shadow map {(_freezeStationShadowMap ? "FROZEN" : "LIVE")}",
                    SystemMessagePriority.NB));
        }
    }

    private void RenderStationShadowMap()
    {
        if (_stationShadowMap == null || _shadowCasterEffect == null) return;
        if (_freezeStationShadowMap && _stationShadowContext != null)
        {
            LogStationShadowFreeze(_stationShadowContext);
            return;
        }

        var target = SelectShadowedStation();
        if (target == null)
        {
            _stationShadowContext = null;
            return;
        }

        var (station, _) = target.Value;
        if (!_stationGeometry.TryGetValue(station, out var modules)) return;

        var sysQ = station.GetOrientation(_gameTimeSeconds);
        var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
        Matrix stationRotation = Matrix.CreateFromQuaternion(stRotQ);

        Vector3 localSun = Vector3.Transform(SceneLighting.SunDirection, Quaternion.Conjugate(stRotQ));
        if (localSun.LengthSquared() < 1e-8f)
            localSun = Vector3.UnitZ;
        localSun.Normalize();

        Matrix lightView = Matrix.CreateLookAt(localSun * 5000f, Vector3.Zero, ChooseLightUp(localSun));
        if (!FitStationShadowLight(modules, lightView, out var minXY, out var maxXY, out float near, out float far))
            return;

        float width = Math.Max(1f, maxXY.X - minXY.X);
        float height = Math.Max(1f, maxXY.Y - minXY.Y);
        float span = Math.Max(1f, far - near);
        Matrix lightProjection = Matrix.CreateOrthographicOffCenter(
            minXY.X, maxXY.X, minXY.Y, maxXY.Y, near, far);

        var oldTargets = _gd.GetRenderTargets();
        var oldViewport = _gd.Viewport;
        var oldBlend = _gd.BlendState;
        var oldDepth = _gd.DepthStencilState;
        var oldRasterizer = _gd.RasterizerState;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _gd.SetRenderTarget(_stationShadowMap);
        _gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        // CullNone, not back-face-only: pure back-face casting (the original D2 scheme)
        // stores only the caster's far side, so at contact lines — where a caster's base
        // meets a receiving surface, e.g. module-to-module seams — the receiver compares
        // against a depth that's structurally farther from the light than the true
        // occluding surface. That reads as lit across a band roughly as wide as the
        // caster's own thickness along the light, i.e. a lit seam right where the contact
        // shadow should be hardest. Casting both faces lets the nearest surface win the
        // depth test instead, fixing the seam. The receiver-plane correction (D4) remains
        // the acne defense for ordinary self-shadowing; if grazing-angle acne shows up
        // that it doesn't cover, the next allowed rung is a constant bias <= 3mm
        // equivalent — never a normal offset, never something that moves a contact shadow.
        _gd.RasterizerState = RasterizerState.CullNone;

        var fx = _shadowCasterEffect;
        fx.CurrentTechnique = fx.Techniques["ShadowCaster"];
        fx.Parameters["LightProjection"].SetValue(lightProjection);
        fx.Parameters["ShadowNear"].SetValue(near);
        fx.Parameters["ShadowDepthSpan"].SetValue(span);

        foreach (var mod in modules)
        {
            if (!_shadowCasterMeshes.TryGetValue(mod, out var caster)) continue;
            Matrix moduleToLightView = mod.Transform * lightView;
            fx.Parameters["ModuleToLightView"].SetValue(moduleToLightView);

            _gd.SetVertexBuffer(caster.vb);
            _gd.Indices = caster.ib;
            foreach (var pass in fx.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, caster.triCount);
            }
        }

        sw.Stop();
        _gd.SetRenderTargets(oldTargets);
        _gd.Viewport = oldViewport;
        _gd.BlendState = oldBlend;
        _gd.DepthStencilState = oldDepth;
        _gd.RasterizerState = oldRasterizer;

        _stationShadowContext = new StationShadowContext(
            station, stationRotation, lightView, lightProjection,
            minXY, new Vector2(1f / width, 1f / height), near, span,
            sw.Elapsed.TotalMilliseconds);
        LogStationShadowFirstGeneration(_stationShadowContext);
    }

    private (Galaxy.Station station, Core.Math.DVec3 pos)? SelectShadowedStation()
    {
        (Galaxy.Station station, Core.Math.DVec3 pos)? best = null;
        float bestRenderDistance = float.MaxValue;

        foreach (var entry in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(entry.pos);
            float dist = renderPos.Length();
            if (dist > 30_000f) continue;
            if (dist < bestRenderDistance)
            {
                bestRenderDistance = dist;
                best = entry;
            }
        }

        return best;
    }

    private bool FitStationShadowLight(
        IReadOnlyList<PlacedModule> modules, Matrix lightView,
        out Vector2 minXY, out Vector2 maxXY, out float near, out float far)
    {
        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        float minDepth = float.MaxValue;
        float maxDepth = float.MinValue;
        bool any = false;

        foreach (var mod in modules)
        {
            if (!_shadowCasterMeshes.ContainsKey(mod)) continue;

            var h = mod.Definition.BoundingBox * 0.5f;
            for (int ix = -1; ix <= 1; ix += 2)
            for (int iy = -1; iy <= 1; iy += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                Vector3 local = new(h.X * ix, h.Y * iy, h.Z * iz);
                Vector3 stationLocal = Vector3.Transform(local, mod.Transform);
                Vector3 light = Vector3.Transform(stationLocal, lightView);
                min.X = MathF.Min(min.X, light.X);
                min.Y = MathF.Min(min.Y, light.Y);
                max.X = MathF.Max(max.X, light.X);
                max.Y = MathF.Max(max.Y, light.Y);
                float d = -light.Z;
                minDepth = MathF.Min(minDepth, d);
                maxDepth = MathF.Max(maxDepth, d);
                any = true;
            }
        }

        if (!any)
        {
            minXY = maxXY = Vector2.Zero;
            near = far = 0f;
            return false;
        }

        minXY = min - new Vector2(StationShadowPaddingMetres);
        maxXY = max + new Vector2(StationShadowPaddingMetres);
        near = MathF.Max(0.01f, minDepth - StationShadowPaddingMetres);
        far = maxDepth + StationShadowPaddingMetres;
        return far > near;
    }

    private static Vector3 ChooseLightUp(Vector3 localSun)
    {
        Vector3 up = MathF.Abs(Vector3.Dot(localSun, Vector3.Up)) > 0.9f
            ? Vector3.Right
            : Vector3.Up;
        return up;
    }

    private void LogStationShadowFirstGeneration(StationShadowContext ctx)
    {
        if (_stationShadowLogged) return;
        _stationShadowLogged = true;
        PublishStationShadowDiagnostic("StationMap", ctx);
    }

    private void LogStationShadowFreeze(StationShadowContext ctx)
    {
        if (_stationShadowFreezeLogged) return;
        _stationShadowFreezeLogged = true;
        PublishStationShadowDiagnostic("StationMap frozen", ctx);
    }

    private void PublishStationShadowDiagnostic(string label, StationShadowContext ctx)
    {
        float width = 1f / ctx.InvSize.X;
        float height = 1f / ctx.InvSize.Y;
        string message =
            $"[{label}] {ctx.Station.Name}: {width:F1} x {height:F1} m, " +
            $"depth {ctx.DepthSpan:F1} m, {ctx.BuildMilliseconds:F2} ms. " +
            "Keys F6 delta, F7 binary, F8 overlay, F9 freeze.";
        System.Console.WriteLine(message);
        DataBus.System.Publish(Topics.System.All, new SystemMessage(message, SystemMessagePriority.NB));
    }

    private void DrawStationShadowOverlay(SpriteBatch sb)
    {
        if (!_showStationShadowOverlay || _stationShadowMap == null) return;

        int size = Math.Min(256, Math.Min(_gd.Viewport.Width, _gd.Viewport.Height) / 3);
        if (size <= 0) return;

        var rect = new Rectangle(12, 12, size, size);
        sb.Draw(_stationShadowMap, rect, Color.White);
        sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, rect.Width + 2, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Bottom, rect.Width + 2, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(rect.X - 1, rect.Y - 1, 1, rect.Height + 2), Color.White);
        sb.Draw(_pixel, new Rectangle(rect.Right, rect.Y - 1, 1, rect.Height + 2), Color.White);
    }
}
