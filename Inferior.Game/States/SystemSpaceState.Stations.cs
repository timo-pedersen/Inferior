using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private enum StationShadowDebugMode
    {
        LightCameraSolid,
        CasterCoverage,
        ReceiverUvGrid,
        ReceiverDepth,
        SampledCasterDepth,
        DepthDelta,
        SlopeFactor,
        ModuleId,
        MeshClass,
        CasterOwnerMatch,
        SelectedModuleHullDepthDelta,
        BiasNoBiasNoNormalOffset,
        BiasDepthOnlyNoNormalOffset,
        BiasNormalOffsetOnly,
        BiasProductionCombination,
        AnalyticPlaneCorrectedDepthDelta,
        AnalyticPlaneCorrectedBinary,
        AnalyticPreviewZeroBias,
        AnalyticPreviewSmallBias,
        Module5HullFaceOwner,
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Station drawing ───────────────────────────────────────────────────────

    private static float StationPhysicalRadius(Galaxy.Station s) => s.Size switch
    {
        Galaxy.StationSize.Small  =>  250f,
        Galaxy.StationSize.Medium =>  800f,
        Galaxy.StationSize.Large  => 2500f,
        _                         =>  250f,
    };

    // Keep normalized depth bias small; grazing self-shadowing is handled by a station-local
    // receiver normal offset expressed in metres so it does not scale with the fitted depth span.
    private const float StationBaseShadowBias  = 0.00008f;
    private const float StationSlopeShadowBias = 0.00012f;
    private const float StationMaxShadowBias   = 0.00020f;
    private const float StationNormalShadowOffsetMetres = 0.16f;
    private const float StationShadowDebugDifferenceScale = 500f;
    private const float StationAnalyticPreviewSmallBias = 0.00002f;

    private void DrawStations(DetailLevel level)
    {
        if (_stationPositions.Count == 0) return;

        float rs = (float)Camera3D.RenderScale;
        var decoMeshesForLevel = level == DetailLevel.Full ? _decoMeshes : _decoMeshesFlat;

        _stationShadowEffect.Parameters["View"]?.SetValue(_effect.View);
        _stationShadowEffect.Parameters["Projection"]?.SetValue(_effect.Projection);
        _stationShadowEffect.Parameters["SunDirection"]?.SetValue(SceneLighting.SunDirection);
        _stationShadowEffect.Parameters["SunColour"]?.SetValue(SceneLighting.SunColour);
        _stationShadowEffect.Parameters["Ambient"]?.SetValue(SceneLighting.Ambient);
        _stationShadowEffect.Parameters["BaseShadowBias"]?.SetValue(StationBaseShadowBias);
        _stationShadowEffect.Parameters["SlopeShadowBias"]?.SetValue(StationSlopeShadowBias);
        _stationShadowEffect.Parameters["MaxShadowBias"]?.SetValue(StationMaxShadowBias);
        _stationShadowEffect.Parameters["NormalShadowOffsetMetres"]?.SetValue(StationNormalShadowOffsetMetres);
        _stationShadowEffect.Parameters["ShadowDebugMode"]?.SetValue(ShaderStationShadowDebugMode());
        _stationShadowEffect.Parameters["ShadowDebugDifferenceScale"]?.SetValue(StationShadowDebugDifferenceScale);
        _stationShadowEffect.Parameters["AnalyticPreviewBias"]?.SetValue(
            StationShadowAnalyticPreviewBias(_stationShadowDebugMode));
        _stationShadowFaceOwnerEffect.Parameters["View"]?.SetValue(_effect.View);
        _stationShadowFaceOwnerEffect.Parameters["Projection"]?.SetValue(_effect.Projection);

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;
            if (!_stationShadows.TryGetValue(station, out var shadow)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            _stationShadowEffect.Parameters["LightViewProjection"]?.SetValue(shadow.LightViewProjection);
            _stationShadowEffect.Parameters["LightView"]?.SetValue(shadow.LightView);
            _stationShadowEffect.Parameters["LightDepthNear"]?.SetValue(shadow.DepthRange.Near);
            _stationShadowEffect.Parameters["LightDepthFar"]?.SetValue(shadow.DepthRange.Far);
            _stationShadowEffect.Parameters["ShadowMapSize"]?.SetValue((float)shadow.Texture.Width);
            _stationShadowEffect.Parameters["ShadowProjectionSize"]?.SetValue(shadow.LightProjectionSize);
            _stationShadowEffect.Parameters["ShadowMap"]?.SetValue(shadow.Texture);
            _stationShadowEffect.Parameters["CasterOwnerMap"]?.SetValue(shadow.CasterOwnerTexture);
            _stationShadowEffect.Parameters["SelectedHullDepthMap"]?.SetValue(shadow.SelectedModuleHullDepthTexture);
            _stationShadowEffect.Parameters["ShadowDebugTexture"]?.SetValue(
                _stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner
                    ? shadow.Module5HullFaceOwnerTexture
                    : _stationShadowUvGrid);
            _stationShadowFaceOwnerEffect.Parameters["LightViewProjection"]?.SetValue(shadow.LightViewProjection);
            _stationShadowFaceOwnerEffect.Parameters["FaceOwnerTexture"]?.SetValue(shadow.Module5HullFaceOwnerTexture);

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                if (_stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner && moduleIndex != 5)
                    continue;

                var mod = modules[moduleIndex];
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                if (mod.TextureInstance == null) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationHull", hull.vb, hull.ib, hull.triCount, world,
                    mod.Transform, mod.TextureInstance, emissive: false,
                    ModuleDebugColor(moduleIndex), MeshClassDebugColor(StationMeshClass.Hull));
            }

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                if (_stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner && moduleIndex != 5)
                    continue;

                var mod = modules[moduleIndex];
                if (!decoMeshesForLevel.TryGetValue(mod, out var deco)) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationBaked", deco.vb, deco.ib, deco.triCount, world,
                    mod.Transform, mod.TextureInstance ?? StationTextureRegistry.Get(mod.Mesh!.Texture),
                    emissive: false,
                    ModuleDebugColor(moduleIndex), MeshClassDebugColor(StationMeshClass.Decoration));
            }

            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                if (_stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner && moduleIndex != 5)
                    continue;

                var mod = modules[moduleIndex];
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationBaked", glass.vb, glass.ib, glass.triCount, world,
                    mod.Transform, StationTextureRegistry.White, emissive: true,
                    ModuleDebugColor(moduleIndex), MeshClassDebugColor(StationMeshClass.Glass));
            }
        }
    }

    private Matrix StationRenderWorld(PlacedModule mod, Matrix stationRot, Vector3 renderPos, float renderScale)
    {
        mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);
        return Matrix.CreateScale(renderScale)
             * Matrix.CreateFromQuaternion(modRot)
             * stationRot
             * Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * renderScale)
             * Matrix.CreateTranslation(renderPos);
    }

    private void DrawStationMesh(
        string technique, VertexBuffer vb, IndexBuffer ib, int triCount,
        Matrix world, Matrix stationLocalWorld, Texture2D texture, bool emissive,
        Color moduleDebugColor, Color meshClassDebugColor)
    {
        if (_stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner)
        {
            DrawStationFaceOwnerMesh(technique, vb, ib, triCount, world, stationLocalWorld);
            return;
        }

        technique = StationShadowTechniqueForDebugMode(technique);
        _stationShadowEffect.CurrentTechnique = _stationShadowEffect.Techniques[technique];
        _stationShadowEffect.Parameters["World"]?.SetValue(world);
        _stationShadowEffect.Parameters["StationLocalWorld"]?.SetValue(stationLocalWorld);
        _stationShadowEffect.Parameters["DiffuseTexture"]?.SetValue(texture);
        _stationShadowEffect.Parameters["EmissiveSurface"]?.SetValue(emissive ? 1f : 0f);
        _stationShadowEffect.Parameters["ShadowDebugSolidColor"]?.SetValue(
            ShaderStationShadowSolidColor(moduleDebugColor, meshClassDebugColor));

        _gd.SetVertexBuffer(vb);
        _gd.Indices = ib;
        foreach (var pass in _stationShadowEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0, startIndex: 0,
                primitiveCount: triCount);
        }
    }

    private void DrawStationFaceOwnerMesh(
        string technique, VertexBuffer vb, IndexBuffer ib, int triCount,
        Matrix world, Matrix stationLocalWorld)
    {
        string faceOwnerTechnique = technique switch
        {
            "StationHull" => "StationHullFaceOwner",
            "StationBaked" => "StationBakedFaceOwner",
            _ => "StationBakedFaceOwner",
        };

        _stationShadowFaceOwnerEffect.CurrentTechnique = _stationShadowFaceOwnerEffect.Techniques[faceOwnerTechnique];
        _stationShadowFaceOwnerEffect.Parameters["World"]?.SetValue(world);
        _stationShadowFaceOwnerEffect.Parameters["StationLocalWorld"]?.SetValue(stationLocalWorld);

        _gd.SetVertexBuffer(vb);
        _gd.Indices = ib;
        foreach (var pass in _stationShadowFaceOwnerEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0, startIndex: 0,
                primitiveCount: triCount);
        }
    }

    private void DrawStationShadowDebugView(SpriteBatch sb)
    {
        if (!_showStationShadowDebug || _stationShadows.Count == 0)
            return;

        if (!TrySelectStationShadowDebugTarget(out var station, out var shadow) || shadow == null)
            return;

        int size = Math.Min(256, Math.Min(_gd.Viewport.Width, _gd.Viewport.Height) / 3);
        if (size <= 0) return;

        sb.Draw(StationShadowDebugPanelTexture(shadow), new Rectangle(12, 12, size, size), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12, size, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12 + size - 1, size, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12, 1, size), Color.White);
        sb.Draw(_pixel, new Rectangle(12 + size - 1, 12, 1, size), Color.White);

        float depthMetres = shadow.DepthRange.Length;
        float baseBiasMetres = StationBaseShadowBias * depthMetres;
        float slopeBiasMetres = StationSlopeShadowBias * depthMetres;
        float maxBiasMetres = StationMaxShadowBias * depthMetres;
        string stationName = station?.Name ?? "<unknown>";
        bool isFrozen = ReferenceEquals(station, _stationShadowFrozenStation);
        string frozenMarker = isFrozen ? " - FROZEN" : "";
        string previewText = StationShadowAnalyticPreviewText(_stationShadowDebugMode, depthMetres);
        string text =
            $"Station shadow debug: {stationName} / {StationShadowDebugModeName(_stationShadowDebugMode)}{frozenMarker}\n" +
            "F9 show/hide, F8 cycle, Ctrl+Shift+F freeze\n" +
            previewText +
            $"target={shadow.Texture.Width}x{shadow.Texture.Height} format={shadow.SurfaceFormat}\n" +
            $"solid={shadow.LightCameraSolidTexture.Width}x{shadow.LightCameraSolidTexture.Height} coverage={shadow.CasterCoverageTexture.Width}x{shadow.CasterCoverageTexture.Height}\n" +
            $"near={shadow.DepthRange.Near:0.###}m far={shadow.DepthRange.Far:0.###}m span={depthMetres:0.###}m\n" +
            $"bias norm base={StationBaseShadowBias:0.000000} slope={StationSlopeShadowBias:0.000000} max={StationMaxShadowBias:0.000000}\n" +
            $"bias metres base={baseBiasMetres:0.###} slope={slopeBiasMetres:0.###} max={maxBiasMetres:0.###}\n" +
            $"normal offset={StationNormalShadowOffsetMetres:0.###}m * slope\n" +
            $"diff scale={StationShadowDebugDifferenceScale:0.#}";
        Vector2 pos = new(12, 20 + size);
        sb.DrawString(_font, text, pos + new Vector2(1, 1), Color.Black);
        sb.DrawString(_font, text, pos, Color.White);
        if (isFrozen)
            DrawStationShadowFrozenBadge(sb, pos + new Vector2(0, -18));

        if (_stationShadowDebugMode == StationShadowDebugMode.ModuleId &&
            station != null &&
            _stationGeometry.TryGetValue(station, out var modules))
        {
            DrawStationModuleDebugLegend(sb, modules, new Vector2(12, pos.Y + 118));
        }
        else if (_stationShadowDebugMode == StationShadowDebugMode.MeshClass)
        {
            DrawStationMeshClassLegend(sb, new Vector2(12, pos.Y + 118));
        }
        else if (_stationShadowDebugMode == StationShadowDebugMode.CasterOwnerMatch)
        {
            DrawStationCasterOwnerLegend(sb, new Vector2(12, pos.Y + 118));
        }
        else if (IsStationShadowBiasDecompositionMode(_stationShadowDebugMode))
        {
            DrawStationBiasDecompositionLegend(sb, new Vector2(12, pos.Y + 118));
        }
        else if (IsStationShadowAnalyticPlaneCorrectionMode(_stationShadowDebugMode))
        {
            DrawStationAnalyticPlaneCorrectionLegend(sb, new Vector2(12, pos.Y + 118));
        }
        else if (_stationShadowDebugMode == StationShadowDebugMode.Module5HullFaceOwner)
        {
            DrawStationModule5HullFaceOwnerLegend(sb, new Vector2(12, pos.Y + 118));
        }
    }

    private bool TrySelectStationShadowDebugTarget(out Galaxy.Station? station, out StationShadowMap? shadow)
    {
        station = null;
        shadow = null;

        if (_stationShadowFrozenStation != null &&
            _stationShadows.TryGetValue(_stationShadowFrozenStation, out shadow))
        {
            station = _stationShadowFrozenStation;
            return true;
        }

        Galaxy.Station? bestStation = null;
        StationShadowMap? bestShadow = null;
        int bestRank = int.MaxValue;
        double bestScore = double.MaxValue;

        foreach (var (candidate, universePos) in _stationPositions)
        {
            if (!_stationShadows.TryGetValue(candidate, out var candidateShadow)) continue;
            if (!TryGetStationDebugLocalView(candidate, universePos, out var localCamera, out var localForward)) continue;

            double distanceToBoundsSquared = DistanceSquaredToBounds(localCamera, candidateShadow.Bounds);
            bool rayHit = RayIntersectsBounds(localCamera, localForward, candidateShadow.Bounds, out float rayDistance);

            int rank;
            double score;
            if (distanceToBoundsSquared <= 50.0 * 50.0)
            {
                rank = 0;
                score = distanceToBoundsSquared;
            }
            else if (rayHit)
            {
                rank = 1;
                score = rayDistance;
            }
            else
            {
                rank = 2;
                score = distanceToBoundsSquared;
            }

            if (rank > bestRank || rank == bestRank && score >= bestScore) continue;

            bestRank = rank;
            bestScore = score;
            bestStation = candidate;
            bestShadow = candidateShadow;
        }

        if (bestShadow != null)
        {
            station = bestStation;
            shadow = bestShadow;
            return true;
        }

        if (_targeting.CurrentRadarTarget is { Type: ContactType.Station } contact &&
            TrySelectStationShadowDebugTargetByName(contact.DisplayName, out station, out shadow))
        {
            return true;
        }

        var padStation = _targeting.TargetedPadStation;
        if (padStation != null && _stationShadows.TryGetValue(padStation, out shadow))
        {
            station = padStation;
            return true;
        }

        var navStation = _targeting.NavStationTarget;
        if (navStation != null && _stationShadows.TryGetValue(navStation, out shadow))
        {
            station = navStation;
            return true;
        }

        return false;
    }

    private void ToggleStationShadowDiagnosticFreeze()
    {
        if (_stationShadowFrozenStation != null)
        {
            _stationShadowFrozenStation = null;
            return;
        }

        if (!_showStationShadowDebug)
            return;

        if (TrySelectStationShadowDebugTarget(out var station, out var shadow) &&
            station != null &&
            shadow != null)
        {
            _stationShadowFrozenStation = station;
        }
    }

    private bool TrySelectStationShadowDebugTargetByName(
        string stationName,
        out Galaxy.Station? station,
        out StationShadowMap? shadow)
    {
        foreach (var (candidate, _) in _stationPositions)
        {
            if (!string.Equals(candidate.Name, stationName, StringComparison.Ordinal)) continue;
            if (!_stationShadows.TryGetValue(candidate, out shadow)) continue;

            station = candidate;
            return true;
        }

        station = null;
        shadow = null;
        return false;
    }

    private bool TryGetStationDebugLocalView(
        Galaxy.Station station,
        DVec3 universePos,
        out Vector3 localCamera,
        out Vector3 localForward)
    {
        var sysQ = station.GetOrientation(_gameTimeSeconds);
        var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
        Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);
        Matrix.Invert(ref stationRot, out Matrix inverseStationRot);

        localCamera = Vector3.Transform((_camera.UniversePosition - universePos).ToVector3(), inverseStationRot);
        localForward = Vector3.TransformNormal(_camera.Forward, inverseStationRot);
        float len = localForward.Length();
        if (len < 1e-6f)
            return false;

        localForward /= len;
        return true;
    }

    private static double DistanceSquaredToBounds(Vector3 point, StationShadowBounds bounds)
    {
        double dx = DistanceToRange(point.X, bounds.Min.X, bounds.Max.X);
        double dy = DistanceToRange(point.Y, bounds.Min.Y, bounds.Max.Y);
        double dz = DistanceToRange(point.Z, bounds.Min.Z, bounds.Max.Z);
        return dx * dx + dy * dy + dz * dz;
    }

    private static double DistanceToRange(float value, float min, float max)
    {
        if (value < min) return min - value;
        if (value > max) return value - max;
        return 0.0;
    }

    private static bool RayIntersectsBounds(
        Vector3 origin,
        Vector3 direction,
        StationShadowBounds bounds,
        out float distance)
    {
        float tMin = 0f;
        float tMax = float.MaxValue;
        distance = 0f;

        if (!RaySlab(origin.X, direction.X, bounds.Min.X, bounds.Max.X, ref tMin, ref tMax)) return false;
        if (!RaySlab(origin.Y, direction.Y, bounds.Min.Y, bounds.Max.Y, ref tMin, ref tMax)) return false;
        if (!RaySlab(origin.Z, direction.Z, bounds.Min.Z, bounds.Max.Z, ref tMin, ref tMax)) return false;

        distance = tMin;
        return tMax >= 0f;
    }

    private static bool RaySlab(
        float origin,
        float direction,
        float min,
        float max,
        ref float tMin,
        ref float tMax)
    {
        const float Epsilon = 1e-6f;
        if (MathF.Abs(direction) < Epsilon)
            return origin >= min && origin <= max;

        float inv = 1f / direction;
        float t1 = (min - origin) * inv;
        float t2 = (max - origin) * inv;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }

    private int ShaderStationShadowDebugMode()
    {
        if (!_showStationShadowDebug)
            return 0;

        return _stationShadowDebugMode switch
        {
            StationShadowDebugMode.ReceiverUvGrid => 3,
            StationShadowDebugMode.ReceiverDepth => 4,
            StationShadowDebugMode.SampledCasterDepth => 5,
            StationShadowDebugMode.SlopeFactor => 6,
            StationShadowDebugMode.DepthDelta => 7,
            StationShadowDebugMode.ModuleId => 8,
            StationShadowDebugMode.MeshClass => 9,
            StationShadowDebugMode.CasterOwnerMatch => 10,
            StationShadowDebugMode.SelectedModuleHullDepthDelta => 11,
            StationShadowDebugMode.BiasNoBiasNoNormalOffset => 12,
            StationShadowDebugMode.BiasDepthOnlyNoNormalOffset => 13,
            StationShadowDebugMode.BiasNormalOffsetOnly => 14,
            StationShadowDebugMode.BiasProductionCombination => 15,
            StationShadowDebugMode.AnalyticPlaneCorrectedDepthDelta => 16,
            StationShadowDebugMode.AnalyticPlaneCorrectedBinary => 17,
            StationShadowDebugMode.AnalyticPreviewZeroBias => 18,
            StationShadowDebugMode.AnalyticPreviewSmallBias => 19,
            _ => 0,
        };
    }

    private static StationShadowDebugMode NextStationShadowDebugMode(StationShadowDebugMode mode)
        => mode == StationShadowDebugMode.Module5HullFaceOwner
            ? StationShadowDebugMode.LightCameraSolid
            : (StationShadowDebugMode)((int)mode + 1);

    private static string StationShadowDebugModeName(StationShadowDebugMode mode)
        => mode switch
        {
            StationShadowDebugMode.LightCameraSolid => "LightCameraSolid",
            StationShadowDebugMode.CasterCoverage => "CasterCoverage",
            StationShadowDebugMode.ReceiverUvGrid => "ReceiverUvGrid",
            StationShadowDebugMode.ReceiverDepth => "receiver normalized light depth",
            StationShadowDebugMode.SampledCasterDepth => "sampled caster depth",
            StationShadowDebugMode.DepthDelta => "receiver minus caster depth",
            StationShadowDebugMode.SlopeFactor => "slope factor",
            StationShadowDebugMode.ModuleId => "ModuleId",
            StationShadowDebugMode.MeshClass => "MeshClass",
            StationShadowDebugMode.CasterOwnerMatch => "CasterOwnerMatch",
            StationShadowDebugMode.SelectedModuleHullDepthDelta => "module #5 hull-only receiver minus caster depth",
            StationShadowDebugMode.BiasNoBiasNoNormalOffset => "A: binary no bias, no normal offset",
            StationShadowDebugMode.BiasDepthOnlyNoNormalOffset => "B: binary depth bias only",
            StationShadowDebugMode.BiasNormalOffsetOnly => "C: binary normal offset only",
            StationShadowDebugMode.BiasProductionCombination => "D: binary current production combination",
            StationShadowDebugMode.AnalyticPlaneCorrectedDepthDelta => "analytic plane-corrected receiver minus caster depth",
            StationShadowDebugMode.AnalyticPlaneCorrectedBinary => "analytic plane-corrected binary, no bias or offset",
            StationShadowDebugMode.AnalyticPreviewZeroBias => "AnalyticPreviewZeroBias",
            StationShadowDebugMode.AnalyticPreviewSmallBias => "AnalyticPreviewSmallBias",
            StationShadowDebugMode.Module5HullFaceOwner => "module #5 hull sampled caster face owner",
            _ => mode.ToString(),
        };

    private Texture2D StationShadowDebugPanelTexture(StationShadowMap shadow)
        => _stationShadowDebugMode switch
        {
            StationShadowDebugMode.LightCameraSolid => shadow.LightCameraSolidTexture,
            StationShadowDebugMode.CasterCoverage => shadow.CasterCoverageTexture,
            StationShadowDebugMode.ReceiverUvGrid => _stationShadowUvGrid,
            StationShadowDebugMode.SelectedModuleHullDepthDelta => shadow.SelectedModuleHullDepthTexture,
            StationShadowDebugMode.Module5HullFaceOwner => shadow.Module5HullFaceOwnerTexture,
            _ => shadow.Texture,
        };

    private static bool IsStationShadowBiasDecompositionMode(StationShadowDebugMode mode)
        => mode == StationShadowDebugMode.BiasNoBiasNoNormalOffset
        || mode == StationShadowDebugMode.BiasDepthOnlyNoNormalOffset
        || mode == StationShadowDebugMode.BiasNormalOffsetOnly
        || mode == StationShadowDebugMode.BiasProductionCombination;

    private static bool IsStationShadowAnalyticPlaneCorrectionMode(StationShadowDebugMode mode)
        => mode == StationShadowDebugMode.AnalyticPlaneCorrectedDepthDelta
        || mode == StationShadowDebugMode.AnalyticPlaneCorrectedBinary;

    private static string StationShadowAnalyticPreviewText(StationShadowDebugMode mode, float depthMetres)
    {
        return mode switch
        {
            StationShadowDebugMode.AnalyticPreviewZeroBias =>
                "preview=AnalyticPreviewZeroBias biasNorm=0.000000 biasMetres=0.000\n",
            StationShadowDebugMode.AnalyticPreviewSmallBias =>
                $"preview=AnalyticPreviewSmallBias biasNorm={StationAnalyticPreviewSmallBias:0.000000} biasMetres={StationAnalyticPreviewSmallBias * depthMetres:0.###}\n",
            _ => string.Empty,
        };
    }

    private static float StationShadowAnalyticPreviewBias(StationShadowDebugMode mode)
        => mode == StationShadowDebugMode.AnalyticPreviewSmallBias
            ? StationAnalyticPreviewSmallBias
            : 0f;

    private string StationShadowTechniqueForDebugMode(string technique)
    {
        if (_stationShadowDebugMode != StationShadowDebugMode.AnalyticPreviewZeroBias &&
            _stationShadowDebugMode != StationShadowDebugMode.AnalyticPreviewSmallBias)
        {
            return technique;
        }

        return technique switch
        {
            "StationHull" => "StationHullAnalyticPreview",
            "StationBaked" => "StationBakedAnalyticPreview",
            _ => technique,
        };
    }

    private enum StationMeshClass
    {
        Hull,
        Decoration,
        Glass,
    }

    internal static Color ModuleDebugColor(int moduleIndex)
    {
        ReadOnlySpan<Color> palette =
        [
            new Color(230, 60, 60),
            new Color(60, 190, 80),
            new Color(70, 120, 240),
            new Color(235, 205, 70),
            new Color(220, 90, 220),
            new Color(80, 220, 220),
            new Color(245, 140, 55),
            new Color(170, 110, 245),
            new Color(150, 220, 80),
            new Color(240, 120, 150),
            new Color(120, 170, 255),
            new Color(210, 210, 210),
        ];

        return palette[moduleIndex % palette.Length];
    }

    internal static Color Module5HullFaceDebugColor(int faceIndex)
    {
        ReadOnlySpan<Color> palette =
        [
            new Color(255, 70, 70),    // +X
            new Color(120, 40, 40),    // -X
            new Color(70, 255, 70),    // +Y
            new Color(35, 120, 35),    // -Y
            new Color(80, 120, 255),   // +Z
            new Color(35, 55, 125),    // -Z
        ];

        return (uint)faceIndex < (uint)palette.Length ? palette[faceIndex] : Color.Magenta;
    }

    internal static string Module5HullFaceName(int faceIndex)
        => faceIndex switch
        {
            0 => "+X",
            1 => "-X",
            2 => "+Y",
            3 => "-Y",
            4 => "+Z",
            5 => "-Z",
            _ => "<none>",
        };

    private static Color MeshClassDebugColor(StationMeshClass meshClass)
        => meshClass switch
        {
            StationMeshClass.Hull => Color.Red,
            StationMeshClass.Decoration => Color.Lime,
            StationMeshClass.Glass => Color.Blue,
            _ => Color.White,
        };

    private Vector4 ShaderStationShadowSolidColor(Color moduleDebugColor, Color meshClassDebugColor)
    {
        Color c = _stationShadowDebugMode == StationShadowDebugMode.MeshClass
            ? meshClassDebugColor
            : moduleDebugColor;
        return c.ToVector4();
    }

    private void DrawStationModuleDebugLegend(SpriteBatch sb, IReadOnlyList<PlacedModule> modules, Vector2 pos)
    {
        int count = Math.Min(modules.Count, 18);
        for (int i = 0; i < count; i++)
        {
            Color c = ModuleDebugColor(i);
            var row = pos + new Vector2(0, i * 16);
            sb.Draw(_pixel, new Rectangle((int)row.X, (int)row.Y + 3, 10, 10), c);
            string label = $"{i}: {modules[i].Definition.Id}";
            sb.DrawString(_font, label, row + new Vector2(14, 1), Color.Black);
            sb.DrawString(_font, label, row + new Vector2(13, 0), Color.White);
        }

        if (modules.Count > count)
        {
            string more = $"+ {modules.Count - count} more modules";
            var row = pos + new Vector2(0, count * 16);
            sb.DrawString(_font, more, row + new Vector2(1, 1), Color.Black);
            sb.DrawString(_font, more, row, Color.White);
        }
    }

    private void DrawStationMeshClassLegend(SpriteBatch sb, Vector2 pos)
    {
        DrawLegendRow(sb, pos + new Vector2(0, 0), Color.Red, "Hull");
        DrawLegendRow(sb, pos + new Vector2(0, 16), Color.Lime, "Decoration");
        DrawLegendRow(sb, pos + new Vector2(0, 32), Color.Blue, "Glass");
    }

    private void DrawStationCasterOwnerLegend(SpriteBatch sb, Vector2 pos)
    {
        DrawLegendRow(sb, pos + new Vector2(0, 0), Color.Lime, "sampled caster = receiver module");
        DrawLegendRow(sb, pos + new Vector2(0, 16), Color.Red, "sampled caster = other module");
        DrawLegendRow(sb, pos + new Vector2(0, 32), Color.Black, "no caster owner");
    }

    private void DrawStationModule5HullFaceOwnerLegend(SpriteBatch sb, Vector2 pos)
    {
        for (int i = 0; i < 6; i++)
            DrawLegendRow(sb, pos + new Vector2(0, i * 16), Module5HullFaceDebugColor(i),
                $"module #5 hull {Module5HullFaceName(i)} owns sampled texel");

        DrawLegendRow(sb, pos + new Vector2(0, 96), Color.Black, "no module #5 hull face owner");
    }

    private void DrawStationBiasDecompositionLegend(SpriteBatch sb, Vector2 pos)
    {
        DrawLegendRow(sb, pos + new Vector2(0, 0), Color.White, "white = lit by binary comparison");
        DrawLegendRow(sb, pos + new Vector2(0, 16), Color.Black, "black = shadowed by binary comparison");
        string mode = _stationShadowDebugMode switch
        {
            StationShadowDebugMode.BiasNoBiasNoNormalOffset => "A uses unoffset UV/depth and zero bias",
            StationShadowDebugMode.BiasDepthOnlyNoNormalOffset => "B uses unoffset UV/depth and current depth bias",
            StationShadowDebugMode.BiasNormalOffsetOnly => "C uses normal-offset UV/depth and zero bias",
            StationShadowDebugMode.BiasProductionCombination => "D uses normal-offset UV/depth and current depth bias",
            _ => string.Empty,
        };
        Vector2 row = pos + new Vector2(0, 32);
        sb.DrawString(_font, mode, row + new Vector2(1, 1), Color.Black);
        sb.DrawString(_font, mode, row, Color.White);
    }

    private void DrawStationAnalyticPlaneCorrectionLegend(SpriteBatch sb, Vector2 pos)
    {
        if (_stationShadowDebugMode == StationShadowDebugMode.AnalyticPlaneCorrectedDepthDelta)
        {
            DrawLegendRow(sb, pos + new Vector2(0, 0), Color.Gray, "gray = corrected receiver minus stored caster depth");
            DrawLegendRow(sb, pos + new Vector2(0, 16), Color.White, "fallback = uncorrected when light normal z is near zero");
            return;
        }

        DrawLegendRow(sb, pos + new Vector2(0, 0), Color.White, "white = lit after analytic plane correction");
        DrawLegendRow(sb, pos + new Vector2(0, 16), Color.Black, "black = shadowed after analytic plane correction");
        string mode = "uses texel-center receiver depth, zero bias, no normal offset";
        Vector2 row = pos + new Vector2(0, 32);
        sb.DrawString(_font, mode, row + new Vector2(1, 1), Color.Black);
        sb.DrawString(_font, mode, row, Color.White);
    }

    private void DrawLegendRow(SpriteBatch sb, Vector2 row, Color color, string label)
    {
        sb.Draw(_pixel, new Rectangle((int)row.X, (int)row.Y + 3, 10, 10), color);
        sb.DrawString(_font, label, row + new Vector2(14, 1), Color.Black);
        sb.DrawString(_font, label, row + new Vector2(13, 0), Color.White);
    }

    private void DrawStationShadowFrozenBadge(SpriteBatch sb, Vector2 pos)
    {
        const string label = "FROZEN";
        Vector2 size = _font.MeasureString(label);
        var rect = new Rectangle(
            (int)pos.X - 2,
            (int)pos.Y - 1,
            (int)MathF.Ceiling(size.X) + 8,
            (int)MathF.Ceiling(size.Y) + 2);
        sb.Draw(_pixel, rect, new Color(0, 0, 0, 220));
        sb.DrawString(_font, label, pos + new Vector2(5, 1), Color.Black);
        sb.DrawString(_font, label, pos + new Vector2(4, 0), Color.Yellow);
    }

    private void DrawStationShadowFreezeIndicator(SpriteBatch sb)
    {
        if (_stationShadowFrozenStation == null)
            return;

        const string label = "FROZEN";
        const float scale = 2.5f;
        Vector2 size = _font.MeasureString(label) * scale;
        Vector2 pos = new(
            (_gd.Viewport.Width - size.X) * 0.5f,
            18f);

        var rect = new Rectangle(
            (int)pos.X - 14,
            (int)pos.Y - 8,
            (int)MathF.Ceiling(size.X) + 28,
            (int)MathF.Ceiling(size.Y) + 16);

        sb.Draw(_pixel, rect, new Color(0, 0, 0, 230));
        sb.DrawString(_font, label, pos + new Vector2(4, 4), Color.Black, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sb.DrawString(_font, label, pos, Color.Yellow, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawStationShadowFreezeInputDiagnostic(SpriteBatch sb)
    {
        if (_stationShadowFreezeInputNoticeSeconds <= 0.0)
            return;

        string label = _stationShadowFreezeInputNoticeChord
            ? "CTRL+SHIFT+F DETECTED"
            : "CTRL+SHIFT+F NOT DETECTED";
        const float scale = 1.7f;
        Vector2 size = _font.MeasureString(label) * scale;
        Vector2 pos = new(
            (_gd.Viewport.Width - size.X) * 0.5f,
            92f);

        var rect = new Rectangle(
            (int)pos.X - 12,
            (int)pos.Y - 7,
            (int)MathF.Ceiling(size.X) + 24,
            (int)MathF.Ceiling(size.Y) + 14);

        sb.Draw(_pixel, rect, new Color(0, 0, 0, 225));
        sb.DrawString(_font, label, pos + new Vector2(3, 3), Color.Black, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        sb.DrawString(_font, label, pos, Color.Yellow, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private readonly record struct StationShadowReceiverHit(
        int ModuleIndex,
        PlacedModule Module,
        int FaceId,
        Vector3 LocalPoint,
        Vector3 LocalNormal,
        Vector3 StationPoint,
        Vector3 StationNormal);

    private void LogStationShadowCrosshairDiagnostic()
    {
        if (!_showStationShadowDebug)
            return;
        if (!TrySelectStationShadowDebugTarget(out var station, out var shadow) || station == null || shadow == null)
            return;
        if (!_stationGeometry.TryGetValue(station, out var modules))
            return;
        if (!TryFindStationUniversePosition(station, out var stationUniversePos))
            return;
        if (!TryGetStationDebugLocalView(station, stationUniversePos, out var stationLocalCamera, out var stationLocalForward))
            return;
        if (!TryFindStationShadowReceiverHit(modules, stationLocalCamera, stationLocalForward, out var hit))
            return;

        Vector4 lightViewPos = Vector4.Transform(new Vector4(hit.StationPoint, 1f), shadow.LightView);
        Vector4 shadowCoord = Vector4.Transform(new Vector4(hit.StationPoint, 1f), shadow.LightViewProjection);
        Vector2 uv = StationShadowUv(shadowCoord);
        int texelX = Math.Clamp((int)MathF.Floor(uv.X * shadow.Texture.Width), 0, shadow.Texture.Width - 1);
        int texelY = Math.Clamp((int)MathF.Floor(uv.Y * shadow.Texture.Height), 0, shadow.Texture.Height - 1);
        float receiverDepth = MathHelper.Clamp(
            (-lightViewPos.Z - shadow.DepthRange.Near) / MathF.Max(shadow.DepthRange.Length, 0.000001f),
            0f,
            1f);

        Vector3 normalLight = Vector3.Normalize(Vector3.TransformNormal(hit.StationNormal, shadow.LightView));
        bool fallback = MathF.Abs(normalLight.Z) < 0.0001f;
        float depthDu = 0f;
        float depthDv = 0f;
        float correctedReceiverDepth = receiverDepth;
        if (!fallback)
        {
            float span = MathF.Max(shadow.DepthRange.Length, 0.000001f);
            depthDu = shadow.LightProjectionSize.X * normalLight.X / (normalLight.Z * span);
            depthDv = -shadow.LightProjectionSize.Y * normalLight.Y / (normalLight.Z * span);
            Vector2 texelCenter = new(
                (MathF.Floor(uv.X * shadow.Texture.Width) + 0.5f) / shadow.Texture.Width,
                (MathF.Floor(uv.Y * shadow.Texture.Height) + 0.5f) / shadow.Texture.Height);
            correctedReceiverDepth = receiverDepth
                + depthDu * (texelCenter.X - uv.X)
                + depthDv * (texelCenter.Y - uv.Y);
        }

        float storedDepth = SampleShadowDepth(shadow.Texture, texelX, texelY);
        Color sampledCasterModuleColor = SampleShadowColor(shadow.CasterOwnerTexture, texelX, texelY);
        Color sampledCasterFaceColor = SampleShadowColor(shadow.Module5HullFaceOwnerTexture, texelX, texelY);
        int sampledCasterModuleIndex = DecodeModuleDebugColor(sampledCasterModuleColor, modules.Count);
        int sampledCasterFaceId = DecodeModule5HullFaceDebugColor(sampledCasterFaceColor);
        float correctedDelta = correctedReceiverDepth - storedDepth;

        System.Diagnostics.Debug.WriteLine(
            "[StationShadowCrosshair] " +
            $"station=\"{station.Name}\" " +
            $"receiverModuleIndex={hit.ModuleIndex} receiverDefinition={hit.Module.Definition.Id} " +
            $"receiverHullFace={Module5HullFaceName(hit.FaceId)} receiverLocalNormal={FormatStationShadowVector(hit.LocalNormal)} " +
            $"sampledCasterModuleIndex={sampledCasterModuleIndex} sampledCasterHullFace={Module5HullFaceName(sampledCasterFaceId)} " +
            $"uv=({uv.X:0.######},{uv.Y:0.######}) shadowTexel=({texelX},{texelY}) " +
            $"receiverDepth={receiverDepth:0.########} correctedReceiverDepth={correctedReceiverDepth:0.########} " +
            $"storedCasterDepth={storedDepth:0.########} correctedDelta={correctedDelta:0.########} " +
            $"normalLightZ={normalLight.Z:0.########} analyticFallback={fallback} " +
            $"depthDu={depthDu:0.########} depthDv={depthDv:0.########}");
    }

    private bool TryFindStationUniversePosition(Galaxy.Station station, out DVec3 universePos)
    {
        foreach (var (candidate, pos) in _stationPositions)
        {
            if (!ReferenceEquals(candidate, station)) continue;
            universePos = pos;
            return true;
        }

        universePos = DVec3.Zero;
        return false;
    }

    private static bool TryFindStationShadowReceiverHit(
        IReadOnlyList<PlacedModule> modules,
        Vector3 stationLocalCamera,
        Vector3 stationLocalForward,
        out StationShadowReceiverHit hit)
    {
        float bestT = float.MaxValue;
        StationShadowReceiverHit best = default;
        bool found = false;

        for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
        {
            var mod = modules[moduleIndex];
            Matrix moduleTransform = mod.Transform;
            Matrix.Invert(ref moduleTransform, out Matrix inverseModule);
            Vector3 localOrigin = Vector3.Transform(stationLocalCamera, inverseModule);
            Vector3 localDirection = Vector3.Normalize(Vector3.TransformNormal(stationLocalForward, inverseModule));

            foreach (var face in EnumerateStationShadowHullFaces(mod))
            {
                if (!TryIntersectStationShadowHullFace(localOrigin, localDirection, face, out float t, out Vector3 localPoint))
                    continue;
                if (t <= 0f || t >= bestT)
                    continue;

                Vector3 stationPoint = Vector3.Transform(localPoint, mod.Transform);
                Vector3 stationNormal = Vector3.Normalize(Vector3.TransformNormal(face.LocalNormal, mod.Transform));
                bestT = t;
                best = new StationShadowReceiverHit(
                    moduleIndex,
                    mod,
                    face.FaceId,
                    localPoint,
                    face.LocalNormal,
                    stationPoint,
                    stationNormal);
                found = true;
            }
        }

        hit = best;
        return found;
    }

    private readonly record struct StationShadowHullFace(
        int FaceId,
        Vector3 LocalNormal,
        float Plane,
        int Axis,
        float MinA,
        float MaxA,
        int AxisA,
        float MinB,
        float MaxB,
        int AxisB);

    private static IEnumerable<StationShadowHullFace> EnumerateStationShadowHullFaces(PlacedModule mod)
    {
        float si = mod.ChamferDepth * 0.707f;
        Vector3 h = mod.Definition.BoundingBox * 0.5f;

        yield return new(0, Vector3.UnitX, +h.X, 0, -h.Y + si, +h.Y - si, 1, -h.Z + si, +h.Z - si, 2);
        yield return new(1, -Vector3.UnitX, -h.X, 0, -h.Y + si, +h.Y - si, 1, -h.Z + si, +h.Z - si, 2);
        yield return new(2, Vector3.UnitY, +h.Y, 1, -h.X + si, +h.X - si, 0, -h.Z + si, +h.Z - si, 2);
        yield return new(3, -Vector3.UnitY, -h.Y, 1, -h.X + si, +h.X - si, 0, -h.Z + si, +h.Z - si, 2);
        yield return new(4, Vector3.UnitZ, +h.Z, 2, -h.X + si, +h.X - si, 0, -h.Y + si, +h.Y - si, 1);
        yield return new(5, -Vector3.UnitZ, -h.Z, 2, -h.X + si, +h.X - si, 0, -h.Y + si, +h.Y - si, 1);
    }

    private static bool TryIntersectStationShadowHullFace(
        Vector3 origin,
        Vector3 direction,
        StationShadowHullFace face,
        out float t,
        out Vector3 point)
    {
        const float Epsilon = 1e-6f;
        float denom = Component(direction, face.Axis);
        if (MathF.Abs(denom) < Epsilon)
        {
            t = 0f;
            point = default;
            return false;
        }

        t = (face.Plane - Component(origin, face.Axis)) / denom;
        if (t <= 0f)
        {
            point = default;
            return false;
        }

        point = origin + direction * t;
        float a = Component(point, face.AxisA);
        float b = Component(point, face.AxisB);
        return a >= face.MinA - Epsilon && a <= face.MaxA + Epsilon
            && b >= face.MinB - Epsilon && b <= face.MaxB + Epsilon;
    }

    private static float Component(Vector3 v, int axis)
        => axis switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };

    private static Vector2 StationShadowUv(Vector4 shadowCoord)
    {
        Vector3 proj = new(
            shadowCoord.X / shadowCoord.W,
            shadowCoord.Y / shadowCoord.W,
            shadowCoord.Z / shadowCoord.W);
        return new Vector2(proj.X * 0.5f + 0.5f, -proj.Y * 0.5f + 0.5f);
    }

    private static float SampleShadowDepth(RenderTarget2D texture, int x, int y)
    {
        float[] pixel = new float[1];
        texture.GetData(0, new Rectangle(x, y, 1, 1), pixel, 0, 1);
        return pixel[0];
    }

    private static Color SampleShadowColor(RenderTarget2D texture, int x, int y)
    {
        Color[] pixel = new Color[1];
        texture.GetData(0, new Rectangle(x, y, 1, 1), pixel, 0, 1);
        return pixel[0];
    }

    private static int DecodeModuleDebugColor(Color color, int moduleCount)
    {
        if (color.R == 0 && color.G == 0 && color.B == 0)
            return -1;

        int bestIndex = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < moduleCount; i++)
        {
            Color expected = ModuleDebugColor(i);
            int dr = color.R - expected.R;
            int dg = color.G - expected.G;
            int db = color.B - expected.B;
            int distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestDistance <= 16 ? bestIndex : -1;
    }

    private static int DecodeModule5HullFaceDebugColor(Color color)
    {
        if (color.R == 0 && color.G == 0 && color.B == 0)
            return -1;

        int bestIndex = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            Color expected = Module5HullFaceDebugColor(i);
            int dr = color.R - expected.R;
            int dg = color.G - expected.G;
            int db = color.B - expected.B;
            int distance = dr * dr + dg * dg + db * db;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestDistance <= 16 ? bestIndex : -1;
    }

    private static string FormatStationShadowVector(Vector3 v)
        => $"({v.X:0.######},{v.Y:0.######},{v.Z:0.######})";

    // Builds a VertexPositionNormalTexture hull mesh for one module (6 box faces, 24 verts).
    // Normals are local-space outward per face; BasicEffect transforms them at draw time.
    // UV uses the same tangent-frame projection as StationModuleMesh.AddQuad (5 m/tile).
    private static (VertexBuffer vb, IndexBuffer ib, int triCount) BuildHullMesh(
        GraphicsDevice gd, PlacedModule mod)
    {
        const float UvScale = 5.0f;
        float ChamferInset  = mod.ChamferDepth * 0.707f;  // single source of truth: mod.ChamferDepth
        var h  = mod.Definition.BoundingBox * 0.5f;
        float si = ChamferInset;

        var verts = new VertexPositionNormalTexture[24];
        var idx   = new int[36];

        // Per-face UV axes chosen so that U and V are always positive (0→4 for a 20 m face).
        // Cross(normal, arb) produces negative U on several faces of a standard box,
        // making texture V=0.5 (the name text) only partially sampled. Hardcoded axes avoid this.
        static void AddFace(VertexPositionNormalTexture[] v, int[] idx, int face,
                            Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n,
                            Vector3 uAxis, Vector3 vAxis)
        {
            int b = face * 4;
            v[b    ] = new VertexPositionNormalTexture(v0, n, Vector2.Zero);
            v[b + 1] = new VertexPositionNormalTexture(v1, n, new Vector2(
                Vector3.Dot(v1 - v0, uAxis) / UvScale, Vector3.Dot(v1 - v0, vAxis) / UvScale));
            v[b + 2] = new VertexPositionNormalTexture(v2, n, new Vector2(
                Vector3.Dot(v2 - v0, uAxis) / UvScale, Vector3.Dot(v2 - v0, vAxis) / UvScale));
            v[b + 3] = new VertexPositionNormalTexture(v3, n, new Vector2(
                Vector3.Dot(v3 - v0, uAxis) / UvScale, Vector3.Dot(v3 - v0, vAxis) / UvScale));

            int i = face * 6;
            idx[i    ] = b;     idx[i + 1] = b + 2; idx[i + 2] = b + 1;
            idx[i + 3] = b;     idx[i + 4] = b + 3; idx[i + 5] = b + 2;
        }

        // Each face panel is inset by ChamferInset in its two lateral axes so that
        // the chamfer strip running along each edge is not hidden behind the panel.
        // The face-normal axis stays at the full surface depth (±h.N unchanged).
        //                                                                             n               uAxis              vAxis
        AddFace(verts, idx, 0, new(-h.X+si,-h.Y+si,+h.Z), new(+h.X-si,-h.Y+si,+h.Z), new(+h.X-si,+h.Y-si,+h.Z), new(-h.X+si,+h.Y-si,+h.Z),  Vector3.UnitZ,  Vector3.UnitX,  Vector3.UnitY);  // +Z
        AddFace(verts, idx, 1, new(+h.X-si,-h.Y+si,-h.Z), new(-h.X+si,-h.Y+si,-h.Z), new(-h.X+si,+h.Y-si,-h.Z), new(+h.X-si,+h.Y-si,-h.Z), -Vector3.UnitZ, -Vector3.UnitX,  Vector3.UnitY);  // -Z
        AddFace(verts, idx, 2, new(-h.X,-h.Y+si,-h.Z+si), new(-h.X,-h.Y+si,+h.Z-si), new(-h.X,+h.Y-si,+h.Z-si), new(-h.X,+h.Y-si,-h.Z+si), -Vector3.UnitX,  Vector3.UnitZ,  Vector3.UnitY);  // -X
        AddFace(verts, idx, 3, new(+h.X,-h.Y+si,+h.Z-si), new(+h.X,-h.Y+si,-h.Z+si), new(+h.X,+h.Y-si,-h.Z+si), new(+h.X,+h.Y-si,+h.Z-si),  Vector3.UnitX, -Vector3.UnitZ,  Vector3.UnitY);  // +X
        AddFace(verts, idx, 4, new(-h.X+si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,+h.Z-si), new(+h.X-si,+h.Y,-h.Z+si), new(-h.X+si,+h.Y,-h.Z+si),  Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ);  // +Y
        AddFace(verts, idx, 5, new(-h.X+si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,-h.Z+si), new(+h.X-si,-h.Y,+h.Z-si), new(-h.X+si,-h.Y,+h.Z-si), -Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ);  // -Y

        var vb = new VertexBuffer(gd, VertexPositionNormalTexture.VertexDeclaration,
                                  24, BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, 36, BufferUsage.WriteOnly);
        ib.SetData(idx);
        return (vb, ib, 12);
    }

    private void DrawStationOrbitRings()
    {
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        var ringColor = new Color(20, 30, 50, 120);

        foreach (var (station, _) in _stationPositions)
        {
            // Station orbit ring is centred on its parent body's render pos
            DVec3 parentEcliptic = station.OrbitParent != null
                ? station.OrbitParent.GetPosition(_gameTimeSeconds, DVec3.Zero)
                : DVec3.Zero;
            DVec3   parentUniverse = EclipticToGalaxy(parentEcliptic);
            Vector3 parentRender   = _camera.ToRenderSpace(parentUniverse);

            float ringR = (float)(station.OrbitalRadius * Camera3D.RenderScale);
            if (ringR < 0.0001f || ringR > 5_000f) continue;

            _effect.World = Matrix.CreateScale(ringR)
                          * _eclipticRotation
                          * Matrix.CreateTranslation(parentRender);
            _ringPrimitive.Draw(_gd, _effect, ringColor);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Station dot icons — 3×3 pixel screen-space marker, visible up to 1 million km.
    // Drawn on top of all 3D geometry so stations are always locatable.
    private void DrawStationDots(SpriteBatch sb)
    {
        const float MaxDistRU = 1.0f;   // 1 million km → 1.0 render unit

        var viewProj = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
        int w = _gd.Viewport.Width;
        int h = _gd.Viewport.Height;

        foreach (var (_, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > MaxDistRU) continue;

            Vector4 clip = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
            if (clip.W <= 0f) continue;

            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;
            if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

            sb.Draw(_pixel, new Rectangle((int)sx - 1, (int)sy - 1, 3, 3), new Color(160, 190, 210, 220));
        }
    }

    // Draws additive screen-space glow sprites over all station nav lights and warning
    // strobes. Called once per render pass (see DrawFarPassContent/DrawMidPassContent/
    // DrawNearPassContent), filtered to that pass's own real-metre distance range —
    // required because each pass clears and rebuilds its own depth buffer, so a light's
    // glow can only be correctly depth-tested against the SAME pass that drew its host
    // geometry; testing it against a later pass's buffer would compare it against
    // "cleared to far" everywhere that pass didn't itself draw anything, i.e. almost
    // everywhere for lights outside that pass's own range, defeating the depth test.
    // Must run after DrawStations() in the same pass so the additive blend brightens
    // visible geometry and depth-tests against it correctly.
    private void DrawStationGlows(SpriteBatch sb, float nearBoundReal, float farBoundReal)
    {
        if (_stationPositions.Count == 0) return;

        // Active pass's projection (_effect.Projection), not camera.ProjectionMatrix —
        // that's only a representative mid-tier projection now that rendering uses three
        // independent per-pass projections. Same fix as ShipMeshRenderer/DrawTestContainers.
        Matrix   viewProj  = _effect.View * _effect.Projection;
        Viewport viewport  = _gd.Viewport;
        Vector2  texCentre = new(_navGlowTex.Width * 0.5f, _navGlowTex.Height * 0.5f);

        // DepthRead so these sprites are occluded by hull geometry in front of them —
        // read-only depth test (DepthBufferEnable=true, DepthBufferWriteEnable=false),
        // since they're a 2D overlay, not real geometry that should write new depth.
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.DepthRead);
        foreach (var (station, universePos) in _stationPositions)
        {
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;
            Vector3 stationRel = (universePos - _camera.UniversePosition).ToVector3(); // metres

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);

            foreach (var mod in modules)
            {
                foreach (var light in mod.GlowLights)
                {
                    Vector3 relPos   = stationRel + Vector3.Transform(light.WorldPosition, stRotQ);
                    float   distance = relPos.Length();
                    if (distance < 0.1f) continue;
                    if (distance < nearBoundReal || distance >= farBoundReal) continue;

                    Vector2? screen = TargetingSystem.ProjectToScreen(relPos, viewProj, viewport);
                    if (screen == null) continue;

                    float intensity = ComputeGlowIntensity(light);
                    if (intensity < 0.01f) continue;

                    float baseSize = light.Type switch
                    {
                        StationGen.GlowType.NavigationLight => 1200f,
                        StationGen.GlowType.WarningStrobe   => 700f,
                        StationGen.GlowType.AviationWarning => 800f,
                        StationGen.GlowType.AmbientMarker   => 400f,
                        StationGen.GlowType.DockGuidance    => 600f,   // AmbientMarker x1.5, per Timo's ask
                        _                                   => 400f,
                    };
                    float size  = MathHelper.Clamp(baseSize / distance, 6f, 140f);
                    float scale = size / _navGlowTex.Width;

                    // Real depth for this pass's depth test. Without this every sprite
                    // draws at layerDepth 0 (nearest possible depth value), which would
                    // always pass DepthRead regardless of what's actually in front of it —
                    // the state change alone (above) isn't sufficient without this.
                    Vector3 renderPos  = relPos * (float)Camera3D.RenderScale;
                    Vector4 clip       = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
                    float   layerDepth = MathHelper.Clamp(clip.Z / clip.W, 0f, 1f);

                    sb.Draw(_navGlowTex, screen.Value, null,
                            light.Colour * intensity, 0f, texCentre, scale,
                            SpriteEffects.None, layerDepth);
                }
            }
        }
        sb.End();
    }

    private static float ComputeGlowIntensity(StationLightInfo light)
    {
        if (light.Rate <= 0f) return light.BaseIntensity;
        float t = (float)((GameClock.SimTime * light.Rate + light.Phase) % 1.0);
        return light.Pattern switch
        {
            LightPattern.Strobe    => t < 0.18f ? light.BaseIntensity : 0f,
            LightPattern.SlowPulse => (MathF.Sin(t * MathF.Tau) * 0.5f + 0.5f) * light.BaseIntensity,
            LightPattern.Heartbeat => t < 0.10f ? light.BaseIntensity
                                    : t < 0.22f ? 0f
                                    : t < 0.32f ? light.BaseIntensity * 0.65f
                                    : 0f,
            _ => light.BaseIntensity,
        };
    }
}
