using System.Linq;
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
    // Decoration caster, separate from the hull caster above (Phase C) — one extra draw per
    // module's deco mesh, composed from whichever DecorClass ranges are enabled for
    // _casterStage. Absent for modules with nothing enabled. Rebuilt on stage change
    // (RebuildDecoCasterMeshesForStage) without touching the hull casters.
    private readonly Dictionary<PlacedModule, (VertexBuffer vb, IndexBuffer ib, int triCount)> _decoCasterMeshes = [];
    // Module-local caster AABBs (Phase C), used by FitStationShadowLight instead of
    // Definition.BoundingBox — computed once from the actual caster vertex data (not the
    // approximate envelope box) so the fit is exact for MeshFactory hulls too, and grows to
    // cover whichever decoration is currently casting. Hull bounds are set alongside the hull
    // caster in BuildStationShadowCasterMeshes (stage-independent); deco bounds are set
    // alongside the deco caster in BuildModuleDecoCasterMesh (rebuilt on stage change).
    private readonly Dictionary<PlacedModule, (Vector3 min, Vector3 max)> _shadowCasterHullBounds = [];
    private readonly Dictionary<PlacedModule, (Vector3 min, Vector3 max)> _shadowCasterDecoBounds = [];
    private StationShadowContext? _stationShadowContext;
    private bool _showStationShadowOverlay;
    private bool _freezeStationShadowMap;
    private bool _stationShadowBinaryView;
    // Signed receiver-minus-stored depth-delta view (green=0, red=+/shadowed,
    // blue=-/lit, saturating at +-0.5m) — see ShadowDeltaColour in LitSurface.fx.
    private bool _stationShadowDeltaView;
    private bool _stationShadowLogged;
    private bool _stationShadowFreezeLogged;

    // Ctrl+F6 caster-stage debug cycle (Docs/station-lighting-pipeline-spec.md §10 Phase C
    // "Rollout"). Independent of DecorCastingPolicy — this previews a stage ahead of its
    // landing there, so a not-yet-landed class can be gated (F8 overlay + screenshots)
    // before its bool flips to true for real. Default tracks whatever's currently landed in
    // DecorCastingPolicy, so normal play needs no keypress to see the production result.
    private enum CasterStage { HullOnly, PlusC1, PlusC2, PlusC3, AllClasses }
    private CasterStage _casterStage = DefaultCasterStage();

    private static CasterStage DefaultCasterStage()
    {
        bool Any(IEnumerable<DecorClass> classes) =>
            classes.Any(c => StationDecorator.DecorCastingPolicy[c]);
        if (Any(StationDecorator.C4Classes)) return CasterStage.AllClasses;
        if (Any(StationDecorator.C3Classes)) return CasterStage.PlusC3;
        if (Any(StationDecorator.C2Classes)) return CasterStage.PlusC2;
        if (Any(StationDecorator.C1Classes)) return CasterStage.PlusC1;
        return CasterStage.HullOnly;
    }

    private static IEnumerable<DecorClass> ClassesForStage(CasterStage stage) => stage switch
    {
        CasterStage.HullOnly   => [],
        CasterStage.PlusC1     => StationDecorator.C1Classes,
        CasterStage.PlusC2     => StationDecorator.C1Classes.Concat(StationDecorator.C2Classes),
        CasterStage.PlusC3     => StationDecorator.C1Classes.Concat(StationDecorator.C2Classes)
                                                             .Concat(StationDecorator.C3Classes),
        CasterStage.AllClasses => StationDecorator.C1Classes.Concat(StationDecorator.C2Classes)
                                                             .Concat(StationDecorator.C3Classes)
                                                             .Concat(StationDecorator.C4Classes),
        _ => [],
    };

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
        foreach (var v in _decoCasterMeshes.Values)
        {
            v.vb.Dispose();
            v.ib.Dispose();
        }
        _decoCasterMeshes.Clear();
        _shadowCasterHullBounds.Clear();
        _shadowCasterDecoBounds.Clear();
        _stationShadowMap?.Dispose();
        _stationShadowMap = null;
        _shadowCasterEffect = null;
        _stationShadowContext = null;
    }

    // Pure decision logic, no GraphicsDevice — any MeshFactory module (docking-bay,
    // hab-block-octagonal, science-block-octagonal, any future one) casts its base hull
    // faces [0, BaseFaceCount) once StationDecorator.Decorate has built its Mesh. This is
    // NOT specific to docking-bay: docking-bay just happened to be the first MeshFactory
    // module to get a caster, and an earlier fix wrongly encoded that as a category check
    // instead of the general MeshFactory condition, silently leaving every other
    // MeshFactory module (octagonal blocks) with no hull caster while their decoration
    // still composed — floating greeble shadows with nothing underneath. `internal` so the
    // regression test (Inferior.Game.Test) can assert this directly without a live
    // GraphicsDevice. Box modules (MeshFactory == null) are handled separately, below, via
    // BuildHullMesh — always unconditional, never this path.
    internal static bool TryGetMeshFactoryHullFaceRange(PlacedModule mod, out int baseFaceCount)
    {
        baseFaceCount = 0;
        if (mod.Definition.MeshFactory == null || mod.Mesh == null) return false;
        baseFaceCount = mod.Mesh.BaseFaceCount;
        return baseFaceCount > 0;
    }

    private void BuildStationShadowCasterMeshes(IEnumerable<PlacedModule> modules)
    {
        var enabled = new HashSet<DecorClass>(ClassesForStage(_casterStage));
        var moduleList = modules as IReadOnlyList<PlacedModule> ?? modules.ToList();

        foreach (var mod in moduleList)
        {
            if (mod.Definition.MeshFactory == null)
            {
                _shadowCasterMeshes[mod] = BuildHullMesh(_gd, mod);
                // BuildHullMesh's chamfered panel geometry recovers exactly
                // Definition.BoundingBox's extents (every face-normal axis is reached
                // un-inset by the opposite face's panel) — exact, not an approximation.
                var h = mod.Definition.BoundingBox * 0.5f;
                _shadowCasterHullBounds[mod] = (-h, h);
            }
            else if (TryGetMeshFactoryHullFaceRange(mod, out int baseFaceCount))
            {
                // Generalized MeshFactory hull caster — docking-bay is one example of this,
                // not a special case of it. Decoration appended separately (below) now
                // casts too, per the enabled DecorClass set.
                var hull = mod.Mesh!.BuildFaceRange(_gd, 0, baseFaceCount);
                if (hull.HasValue)
                    _shadowCasterMeshes[mod] = hull.Value;

                // Real vertex bounds, not the definition's approximate envelope box — a
                // MeshFactory hull's true extent doesn't always exactly match the nominal
                // envelope used to size it (e.g. docking-bay's wall thickness/door frame).
                var bounds = mod.Mesh.ComputeFaceRangeBounds(0, baseFaceCount);
                if (bounds.HasValue)
                    _shadowCasterHullBounds[mod] = bounds.Value;
            }

            BuildModuleDecoCasterMesh(mod, enabled);
        }

        // Safety net: a module with decoration casting but no hull caster produces floating
        // shadows with nothing underneath them — exactly the bug this fix addresses. Warn
        // loudly instead of silently missing it again for some future module shape.
        foreach (var mod in moduleList)
        {
            if (_shadowCasterMeshes.ContainsKey(mod)) continue;
            DataBus.System.Publish(Topics.System.All, new SystemMessage(
                $"Station shadow: module '{mod.Definition.Id}' (category '{mod.Definition.Category}') " +
                "has no hull shadow caster — its decoration may cast unattached shadows.",
                SystemMessagePriority.NB));
        }
    }

    // Composes one caster index buffer for a module's decoration from the ranges of
    // whichever DecorClass values are in `enabled` (Phase C). Separate draw from the hull
    // caster above — see the field comment on _decoCasterMeshes. Also (re)computes the
    // module-local deco bounds used by FitStationShadowLight; cleared when nothing's enabled
    // so a stage rollback doesn't leave stale bounds behind.
    private void BuildModuleDecoCasterMesh(PlacedModule mod, HashSet<DecorClass> enabled)
    {
        if (mod.Mesh == null || enabled.Count == 0)
        {
            _shadowCasterDecoBounds.Remove(mod);
            return;
        }

        List<(int indexStart, int indexCount)>? ranges = null;
        foreach (var (indexStart, indexCount, decorClass) in mod.Mesh.DecorClassRanges)
        {
            if (!enabled.Contains(decorClass)) continue;
            ranges ??= [];
            ranges.Add((indexStart, indexCount));
        }
        if (ranges == null)
        {
            _shadowCasterDecoBounds.Remove(mod);
            return;
        }

        var built = mod.Mesh.BuildIndexRanges(_gd, ranges);
        if (built.HasValue)
            _decoCasterMeshes[mod] = built.Value;

        var bounds = mod.Mesh.ComputeIndexRangeBounds(ranges);
        if (bounds.HasValue)
            _shadowCasterDecoBounds[mod] = bounds.Value;
        else
            _shadowCasterDecoBounds.Remove(mod);
    }

    // Ctrl+F6 handler: rebuilds only the decoration casters for the new stage, across every
    // station's geometry (cheap — a rare keypress, not a per-frame cost). Hull casters are
    // untouched, matching the brief's "hull shadows from Phase B unchanged."
    private void RebuildDecoCasterMeshesForStage()
    {
        foreach (var v in _decoCasterMeshes.Values)
        {
            v.vb.Dispose();
            v.ib.Dispose();
        }
        _decoCasterMeshes.Clear();

        var enabled = new HashSet<DecorClass>(ClassesForStage(_casterStage));
        if (enabled.Count == 0) return;
        foreach (var modules in _stationGeometry.Values)
            foreach (var mod in modules)
                BuildModuleDecoCasterMesh(mod, enabled);
    }

    private void UpdateStationShadowInput(KeyboardState keys)
    {
        // F6 checked for conflicts before wiring (the Ctrl+C lesson): grep found F3, F7-F9,
        // F10-F12 (plain and Ctrl+F12) already bound elsewhere in this codebase; F6 was free.
        bool f6 = keys.IsKeyDown(Keys.F6) && !_prevKeys.IsKeyDown(Keys.F6);
        bool f7 = keys.IsKeyDown(Keys.F7) && !_prevKeys.IsKeyDown(Keys.F7);
        bool f8 = keys.IsKeyDown(Keys.F8) && !_prevKeys.IsKeyDown(Keys.F8);
        bool f9 = keys.IsKeyDown(Keys.F9) && !_prevKeys.IsKeyDown(Keys.F9);

        // Ctrl+F6 checked for conflicts the same way (the Ctrl+C lesson again): grep of
        // LeftControl/RightControl usage across Inferior.Game found only Ctrl+C (screenshot)
        // and Ctrl+F12 (window-mode/station-cycle chord) — Ctrl+F6 was free.
        bool ctrlDown = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        bool prevCtrlDown = _prevKeys.IsKeyDown(Keys.LeftControl) || _prevKeys.IsKeyDown(Keys.RightControl);
        bool ctrlF6 = ctrlDown && keys.IsKeyDown(Keys.F6) && !(prevCtrlDown && _prevKeys.IsKeyDown(Keys.F6));

        if (ctrlF6)
        {
            _casterStage = (CasterStage)(((int)_casterStage + 1) % 5);
            RebuildDecoCasterMeshesForStage();
            DataBus.System.Publish(Topics.System.All,
                new SystemMessage($"Station shadow caster stage: {_casterStage}", SystemMessagePriority.NB));
        }

        if (f6 && !ctrlDown)
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
            Matrix moduleToLightView = mod.Transform * lightView;
            fx.Parameters["ModuleToLightView"].SetValue(moduleToLightView);

            if (_shadowCasterMeshes.TryGetValue(mod, out var caster))
            {
                _gd.SetVertexBuffer(caster.vb);
                _gd.Indices = caster.ib;
                foreach (var pass in fx.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, caster.triCount);
                }
            }

            // Phase C: decoration caster, a separate draw from the hull above (same
            // ModuleToLightView — deco ranges are recorded in the same module-local space).
            if (_decoCasterMeshes.TryGetValue(mod, out var deco))
            {
                _gd.SetVertexBuffer(deco.vb);
                _gd.Indices = deco.ib;
                foreach (var pass in fx.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, deco.triCount);
                }
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

    // Phase C: fits from each module's actual enabled caster geometry (_shadowCasterHullBounds
    // unioned with _shadowCasterDecoBounds when present), not Definition.BoundingBox — so a
    // landmark antenna mast or dish extending past the module's envelope box is no longer
    // clipped out of the light frustum. Both bounds dictionaries are precomputed once when the
    // caster meshes are (re)built, not per frame — this just transforms the 8 corners of the
    // cached box through the current lightView, same cost shape as before.
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
            if (!_shadowCasterHullBounds.TryGetValue(mod, out var hullBounds)) continue;

            Vector3 boundsMin = hullBounds.min;
            Vector3 boundsMax = hullBounds.max;
            if (_shadowCasterDecoBounds.TryGetValue(mod, out var decoBounds))
            {
                boundsMin = Vector3.Min(boundsMin, decoBounds.min);
                boundsMax = Vector3.Max(boundsMax, decoBounds.max);
            }

            for (int ix = 0; ix <= 1; ix++)
            for (int iy = 0; iy <= 1; iy++)
            for (int iz = 0; iz <= 1; iz++)
            {
                Vector3 local = new(
                    ix == 0 ? boundsMin.X : boundsMax.X,
                    iy == 0 ? boundsMin.Y : boundsMax.Y,
                    iz == 0 ? boundsMin.Z : boundsMax.Z);
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
            "Keys F6 delta, F7 binary, F8 overlay, F9 freeze, Ctrl+F6 caster stage.";
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
