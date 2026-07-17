using Inferior.Core.Math;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Draws the player ship in third-person view.
/// Caller decides when to call Draw() and passes the active view/projection for the
/// current depth tier. Rendering is selected from hull capabilities: semantic visual
/// geometry when available, otherwise a documented temporary legacy fallback.
/// </summary>
public sealed class ShipMeshRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly MeshRenderer _meshRenderer;
    private readonly Dictionary<string, SemanticHullGpuMesh> _semanticMeshCache = new(StringComparer.OrdinalIgnoreCase);

    private LegacyFallbackMesh? _legacyFallback;

    public ShipMeshRenderer(GraphicsDevice gd, MeshRenderer meshRenderer)
    {
        _gd = gd;
        _meshRenderer = meshRenderer;
    }

    // currentView is the already-rolled view matrix. currentProjection is the active
    // pass projection. Do not read camera.ViewMatrix/camera.ProjectionMatrix here.
    // level is accepted but not yet used; no ship mesh LOD variants exist yet.
    public void Draw(
        Camera3D camera,
        Matrix currentView,
        Matrix currentProjection,
        string hullTypeId,
        DVec3 shipPosition,
        Quaternion shipOrientation,
        DetailLevel level)
    {
        float renderScale = (float)Camera3D.RenderScale;
        Vector3 renderPos = camera.ToRenderSpace(shipPosition);
        var sunColour = new Color(SceneLighting.SunColour);

        if (HullDefinitionLibrary.TryGet(hullTypeId, out var hullDefinition)
            && hullDefinition?.VisualGeometry is not null)
        {
            DrawSemanticHull(hullDefinition, renderScale, renderPos, shipOrientation, currentView, currentProjection, sunColour);
        }
        else
        {
            DrawLegacyFallback(renderScale, renderPos, shipOrientation, currentView, currentProjection, sunColour);
        }

        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    public static ShipHullRenderPath SelectRenderPath(HullDefinition? hullDefinition)
        => hullDefinition?.VisualGeometry is not null
            ? ShipHullRenderPath.SemanticHull
            : ShipHullRenderPath.LegacyFallback;

    private void DrawSemanticHull(
        HullDefinition hullDefinition,
        float renderScale,
        Vector3 renderPos,
        Quaternion shipOrientation,
        Matrix currentView,
        Matrix projection,
        Color sunColour)
    {
        SemanticHullGpuMesh mesh = GetOrCreateSemanticMesh(hullDefinition);

        Matrix world = Matrix.CreateScale(renderScale)
                     * Matrix.CreateFromQuaternion(shipOrientation)
                     * Matrix.CreateTranslation(renderPos);

        foreach (var part in mesh.Parts)
        {
            _meshRenderer.DrawDynamicLit(
                part.VertexBuffer,
                part.IndexBuffer,
                world,
                currentView,
                projection,
                part.MaterialColour,
                SceneLighting.SunDirection,
                sunColour,
                SceneLighting.Ambient);
        }
    }

    private SemanticHullGpuMesh GetOrCreateSemanticMesh(HullDefinition hullDefinition)
    {
        if (_semanticMeshCache.TryGetValue(hullDefinition.HullTypeId, out var mesh))
            return mesh;

        var cpuMesh = SemanticHullMeshBuilder.Build(hullDefinition.VisualGeometry!);
        mesh = SemanticHullGpuMesh.Create(_gd, cpuMesh);
        _semanticMeshCache.Add(hullDefinition.HullTypeId, mesh);
        return mesh;
    }

    private void DrawLegacyFallback(
        float renderScale,
        Vector3 renderPos,
        Quaternion shipOrientation,
        Matrix currentView,
        Matrix projection,
        Color sunColour)
    {
        LegacyFallbackMesh legacy = GetOrCreateLegacyFallback();

        // The legacy mesh is authored +Z-forward, so it needs this correction. Hulls
        // with semantic visual geometry, including Aries/type-1, do not use this path.
        Matrix world = Matrix.CreateScale(renderScale)
                     * Matrix.CreateRotationY(MathF.PI)
                     * Matrix.CreateFromQuaternion(shipOrientation)
                     * Matrix.CreateTranslation(renderPos);

        _meshRenderer.DrawDynamicLit(legacy.HullVb, legacy.HullIb, world, currentView, projection,
            Type1HullFactory.HullColour, SceneLighting.SunDirection, sunColour, SceneLighting.Ambient);
        _meshRenderer.DrawDynamicLit(legacy.NacelleVb, legacy.NacelleIb, world, currentView, projection,
            Type1HullFactory.NacelleColour, SceneLighting.SunDirection, sunColour, SceneLighting.Ambient);
        _meshRenderer.DrawDynamicLit(legacy.PylonVb, legacy.PylonIb, world, currentView, projection,
            Type1HullFactory.PylonColour, SceneLighting.SunDirection, sunColour, SceneLighting.Ambient);
    }

    private LegacyFallbackMesh GetOrCreateLegacyFallback()
    {
        if (_legacyFallback is not null)
            return _legacyFallback;

        var (hullMesh, nacelleMesh, pylonMesh) = Type1HullFactory.BuildAll(_gd);
        _legacyFallback = new LegacyFallbackMesh(
            hullMesh.vb,
            hullMesh.ib,
            nacelleMesh.vb,
            nacelleMesh.ib,
            pylonMesh.vb,
            pylonMesh.ib);
        return _legacyFallback;
    }

    public void Dispose()
    {
        foreach (var mesh in _semanticMeshCache.Values)
            mesh.Dispose();

        _legacyFallback?.Dispose();
    }

    private sealed record LegacyFallbackMesh(
        VertexBuffer HullVb,
        IndexBuffer HullIb,
        VertexBuffer NacelleVb,
        IndexBuffer NacelleIb,
        VertexBuffer PylonVb,
        IndexBuffer PylonIb) : IDisposable
    {
        public void Dispose()
        {
            HullVb.Dispose();
            HullIb.Dispose();
            NacelleVb.Dispose();
            NacelleIb.Dispose();
            PylonVb.Dispose();
            PylonIb.Dispose();
        }
    }
}

public enum ShipHullRenderPath
{
    SemanticHull,
    LegacyFallback,
}
