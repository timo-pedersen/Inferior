using Inferior.Core.Math;
using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private const float StationShadowSunDirectionRefreshDot = 0.99999f;

    private void RebuildStationGeometry()
    {
        Vector3 srp = _camera.ToRenderSpace(DVec3.Zero);
        Vector3 ld = srp == Vector3.Zero ? -Vector3.UnitZ : Vector3.Normalize(-srp);
        SceneLighting.SunDirection = -ld;

        DisposeStationGeometry();

        foreach (var station in _system.Stations)
        {
            var modules = StationGenerator.Generate(station, _gd, _gameTimeSeconds);
            _stationGeometry[station] = modules;

            foreach (var mod in modules)
            {
                var flatGpu = mod.Mesh?.BuildWithNormals(_gd);
                if (flatGpu.HasValue)
                    _decoMeshesFlat[mod] = flatGpu.Value;
            }

            StationDecorator.ApplyAmbientOcclusion(modules);

            foreach (var mod in modules)
            {
                var gpu = mod.Mesh?.BuildWithNormals(_gd);
                if (gpu.HasValue)
                    _decoMeshes[mod] = gpu.Value;

                var glassGpu = mod.GlassMesh?.BuildWithNormals(_gd);
                if (glassGpu.HasValue)
                    _glassMeshes[mod] = glassGpu.Value;

                if (mod.Definition.MeshFactory == null)
                    _hullMeshes[mod] = BuildHullMesh(_gd, mod);
            }

            var sysQ = station.GetOrientation(_gameTimeSeconds);
            var stationRot = Matrix.CreateFromQuaternion(new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W));
            Matrix.Invert(ref stationRot, out Matrix inverseStationRot);
            Vector3 stationLocalSun = Vector3.Normalize(
                Vector3.TransformNormal(SceneLighting.SunDirection, inverseStationRot));

            var shadow = new StationShadowMap(_gd, StationShadowMath.GetStationShadowMapSize());
            shadow.Build(_stationShadowEffect, modules, _hullMeshes, _decoMeshes, _glassMeshes, stationLocalSun);
            _stationShadows[station] = shadow;
        }
    }

    private void RefreshStationShadowsForCurrentSun()
    {
        foreach (var station in _system.Stations)
        {
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;
            if (!_stationShadows.TryGetValue(station, out var shadow)) continue;

            Vector3 stationLocalSun = CurrentStationLocalSunDirection(station);
            if (Vector3.Dot(shadow.StationLocalSunDirection, stationLocalSun) >= StationShadowSunDirectionRefreshDot)
                continue;

            shadow.Build(_stationShadowEffect, modules, _hullMeshes, _decoMeshes, _glassMeshes, stationLocalSun);
        }
    }

    private Vector3 CurrentStationLocalSunDirection(Galaxy.Station station)
    {
        var sysQ = station.GetOrientation(_gameTimeSeconds);
        var stationRot = Matrix.CreateFromQuaternion(new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W));
        Matrix.Invert(ref stationRot, out Matrix inverseStationRot);
        return Vector3.Normalize(Vector3.TransformNormal(SceneLighting.SunDirection, inverseStationRot));
    }

    private void DisposeStationGeometry()
    {
        _stationGeometry.Clear();
        foreach (var v in _decoMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _decoMeshesFlat.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _glassMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var v in _hullMeshes.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        foreach (var shadow in _stationShadows.Values) shadow.Dispose();
        _decoMeshes.Clear();
        _decoMeshesFlat.Clear();
        _glassMeshes.Clear();
        _hullMeshes.Clear();
        _stationShadows.Clear();
    }
}
