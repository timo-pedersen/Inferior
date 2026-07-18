using Inferior.Core.Math;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Hull;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

public readonly record struct ShipRenderTransformDiagnostic(
    DVec3 ShipPosition,
    Quaternion ShipOrientation,
    DVec3 CameraPosition,
    Quaternion CameraOrientation,
    Matrix CameraView,
    Matrix AppliedView,
    Matrix AppliedProjection,
    Vector3 CameraRelativeRenderPosition,
    Vector3 WorldTranslation,
    ShipHullRenderPath RenderPath);

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
    private readonly BasicEffect _debugLineEffect;
    private readonly Dictionary<string, SemanticHullGpuMesh> _semanticMeshCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string GeometryId, bool Mirrored), EngineGpuMesh> _engineMeshCache = [];

    private LegacyFallbackMesh? _legacyFallback;

    public ShipMeshRenderer(GraphicsDevice gd, MeshRenderer meshRenderer)
    {
        _gd = gd;
        _meshRenderer = meshRenderer;
        _debugLineEffect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
    }

    // currentView is the already-rolled view matrix. currentProjection is the active
    // pass projection. Do not read camera.ViewMatrix/camera.ProjectionMatrix here.
    // level is accepted but not yet used; no ship mesh LOD variants exist yet.
    public ShipRenderTransformDiagnostic Draw(
        Camera3D camera,
        Matrix currentView,
        Matrix currentProjection,
        string hullTypeId,
        DVec3 shipPosition,
        Quaternion shipOrientation,
        DetailLevel level,
        SemanticHullDebugMode debugMode = SemanticHullDebugMode.Normal,
        IReadOnlyList<EngineMountPresentationSnapshot>? engineMounts = null,
        bool engineModuleDebug = false)
    {
        float renderScale = (float)Camera3D.RenderScale;
        Vector3 renderPos = camera.ToRenderSpace(shipPosition);
        var sunColour = new Color(SceneLighting.SunColour);
        ShipHullRenderPath renderPath;
        Matrix world;

        if (HullDefinitionLibrary.TryGet(hullTypeId, out var hullDefinition)
            && hullDefinition?.VisualGeometry is not null)
        {
            renderPath = ShipHullRenderPath.SemanticHull;
            world = BuildSemanticWorldTransform(renderScale, renderPos, shipOrientation);
            DrawSemanticHull(
                hullDefinition,
                world,
                currentView,
                currentProjection,
                sunColour,
                debugMode);
        }
        else
        {
            renderPath = ShipHullRenderPath.LegacyFallback;
            world = BuildLegacyWorldTransform(renderScale, renderPos, shipOrientation);
            DrawLegacyFallback(world, currentView, currentProjection, sunColour);
        }

        if (engineMounts is not null)
        {
            DrawInstalledEngines(
                engineMounts,
                world,
                currentView,
                currentProjection,
                sunColour);
            if (engineModuleDebug)
                DrawEngineModuleDebug(engineMounts, world, currentView, currentProjection);
        }

        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;

        return new ShipRenderTransformDiagnostic(
            shipPosition,
            shipOrientation,
            camera.UniversePosition,
            camera.Orientation,
            camera.ViewMatrix,
            currentView,
            currentProjection,
            renderPos,
            world.Translation,
            renderPath);
    }

    public static ShipHullRenderPath SelectRenderPath(HullDefinition? hullDefinition)
        => hullDefinition?.VisualGeometry is not null
            ? ShipHullRenderPath.SemanticHull
            : ShipHullRenderPath.LegacyFallback;

    private void DrawSemanticHull(
        HullDefinition hullDefinition,
        Matrix world,
        Matrix currentView,
        Matrix projection,
        Color sunColour,
        SemanticHullDebugMode debugMode)
    {
        SemanticHullGpuMesh mesh = GetOrCreateSemanticMesh(hullDefinition);

        foreach (var part in mesh.Parts)
        {
            if (debugMode == SemanticHullDebugMode.SurfaceRoles)
            {
                foreach (var face in part.FaceRanges)
                {
                    _meshRenderer.DrawDynamicLitRange(
                        part.VertexBuffer,
                        part.IndexBuffer,
                        face.StartIndex,
                        face.IndexCount,
                        world,
                        currentView,
                        projection,
                        DebugColourForRole(face.SurfaceRole),
                        SceneLighting.SunDirection,
                        sunColour,
                        SceneLighting.Ambient);
                }
            }
            else
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

        if (debugMode == SemanticHullDebugMode.SurfaceRoles)
            DrawSemanticDebugLines(hullDefinition.VisualGeometry!, world, currentView, projection);
    }

    public static Color DebugColourForRole(HullSurfaceRole role)
        => role switch
        {
            HullSurfaceRole.PanelSeat => new Color(72, 150, 210),
            HullSurfaceRole.ExposedStructure => new Color(105, 105, 105),
            HullSurfaceRole.ServiceSurface => new Color(224, 174, 52),
            HullSurfaceRole.EngineMount => new Color(216, 76, 58),
            HullSurfaceRole.CargoDoor => new Color(82, 174, 105),
            HullSurfaceRole.CockpitFrame => new Color(190, 104, 205),
            HullSurfaceRole.CockpitGlass => new Color(55, 205, 218),
            _ => Color.Magenta,
        };

    private void DrawSemanticDebugLines(
        SemanticHullGeometry geometry,
        Matrix world,
        Matrix view,
        Matrix projection)
    {
        Vector3 min = new(
            (float)geometry.Vertices.Min(vertex => vertex.Position.X),
            (float)geometry.Vertices.Min(vertex => vertex.Position.Y),
            (float)geometry.Vertices.Min(vertex => vertex.Position.Z));
        Vector3 max = new(
            (float)geometry.Vertices.Max(vertex => vertex.Position.X),
            (float)geometry.Vertices.Max(vertex => vertex.Position.Y),
            (float)geometry.Vertices.Max(vertex => vertex.Position.Z));

        var lines = BuildDebugLines(min, max);
        _debugLineEffect.World = world;
        _debugLineEffect.View = view;
        _debugLineEffect.Projection = projection;

        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        foreach (var pass in _debugLineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineList, lines, 0, lines.Length / 2);
        }
    }

    private static VertexPositionColor[] BuildDebugLines(Vector3 min, Vector3 max)
    {
        var vertices = new List<VertexPositionColor>(30);
        AddLine(vertices, Vector3.Zero, Vector3.UnitX * 2.5f, Color.Red);
        AddLine(vertices, Vector3.Zero, Vector3.UnitY * 2.5f, Color.LimeGreen);
        AddLine(vertices, Vector3.Zero, -Vector3.UnitZ * 2.5f, Color.Cyan);

        Vector3[] corners =
        [
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        ];
        int[] edges =
        [
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];
        for (int i = 0; i < edges.Length; i += 2)
            AddLine(vertices, corners[edges[i]], corners[edges[i + 1]], Color.White);

        return vertices.ToArray();
    }

    private static void AddLine(List<VertexPositionColor> vertices, Vector3 start, Vector3 end, Color colour)
    {
        vertices.Add(new VertexPositionColor(start, colour));
        vertices.Add(new VertexPositionColor(end, colour));
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

    private void DrawInstalledEngines(
        IReadOnlyList<EngineMountPresentationSnapshot> engineMounts,
        Matrix shipWorld,
        Matrix view,
        Matrix projection,
        Color sunColour)
    {
        foreach (EnginePresentationSnapshot engine in engineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EnginePresentationSnapshot>())
        {
            EngineGpuMesh mesh = GetOrCreateEngineMesh(
                engine.VisualGeometry,
                engine.GeometryTransform.MirroredAcrossHullX);
            Matrix world = engine.GeometryTransform.LocalToHull * shipWorld;

            foreach (EngineGpuMeshPart part in mesh.Parts)
            {
                _meshRenderer.DrawDynamicLit(
                    part.VertexBuffer,
                    part.IndexBuffer,
                    world,
                    view,
                    projection,
                    EngineMaterialColour(part.Material, engine.DamageFraction),
                    SceneLighting.SunDirection,
                    sunColour,
                    SceneLighting.Ambient);
            }
        }
    }

    private EngineGpuMesh GetOrCreateEngineMesh(EngineVisualGeometry geometry, bool mirrored)
    {
        var key = (geometry.GeometryId, mirrored);
        if (_engineMeshCache.TryGetValue(key, out EngineGpuMesh? mesh))
            return mesh;

        mesh = EngineGpuMesh.Create(_gd, EngineMeshBuilder.Build(geometry, mirrored));
        _engineMeshCache.Add(key, mesh);
        return mesh;
    }

    public static Color EngineMaterialColour(EngineVisualMaterial material, double damageFraction)
    {
        Color baseColour = material switch
        {
            EngineVisualMaterial.Structural => new Color(55, 61, 62),
            EngineVisualMaterial.Casing => new Color(92, 96, 91),
            EngineVisualMaterial.Nozzle => new Color(40, 43, 44),
            EngineVisualMaterial.Accent => new Color(190, 145, 42),
            _ => Color.Magenta,
        };
        float condition = MathHelper.Lerp(1.0f, 0.45f, (float)damageFraction);
        return new Color(baseColour.ToVector3() * condition);
    }

    private void DrawEngineModuleDebug(
        IReadOnlyList<EngineMountPresentationSnapshot> engineMounts,
        Matrix shipWorld,
        Matrix view,
        Matrix projection)
    {
        var lines = new List<VertexPositionColor>();
        foreach (EngineMountPresentationSnapshot mount in engineMounts)
        {
            Matrix mountTransform =
                Matrix.CreateFromQuaternion(mount.Pose.Orientation)
                * Matrix.CreateTranslation(mount.Pose.Position.ToVector3());
            AddTransformAxes(lines, mountTransform, 1.0f);

            EnginePresentationSnapshot? engine = mount.InstalledEngine;
            if (engine is null)
                continue;

            Matrix engineTransform = engine.GeometryTransform.LocalToHull;
            AddTransformAxes(lines, engineTransform, 0.75f);
            bool mirrored = engine.GeometryTransform.MirroredAcrossHullX;
            foreach (EngineExhaustDefinition exhaust in engine.VisualGeometry.Exhausts)
            {
                Vector3 start = Vector3.Transform(
                    EngineMeshBuilder.ToVector3(exhaust.Position, mirrored),
                    engineTransform);
                Vector3 direction = Vector3.TransformNormal(
                    EngineMeshBuilder.ToVector3(exhaust.Direction, mirrored),
                    engineTransform);
                AddLine(lines, start, start + Vector3.Normalize(direction) * 1.8f, Color.OrangeRed);
            }
            foreach (EngineLightDefinition light in engine.VisualGeometry.Lights)
            {
                Vector3 position = Vector3.Transform(
                    EngineMeshBuilder.ToVector3(light.Position, mirrored),
                    engineTransform);
                AddCross(lines, position, 0.18f, new Color(light.Colour.ToVector3()));
            }
        }

        if (lines.Count == 0)
            return;

        _debugLineEffect.World = shipWorld;
        _debugLineEffect.View = view;
        _debugLineEffect.Projection = projection;
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        foreach (EffectPass pass in _debugLineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineList, lines.ToArray(), 0, lines.Count / 2);
        }
    }

    private static void AddTransformAxes(List<VertexPositionColor> lines, Matrix transform, float length)
    {
        Vector3 origin = transform.Translation;
        AddLine(lines, origin, Vector3.Transform(Vector3.UnitX * length, transform), Color.Red);
        AddLine(lines, origin, Vector3.Transform(Vector3.UnitY * length, transform), Color.LimeGreen);
        AddLine(lines, origin, Vector3.Transform(-Vector3.UnitZ * length, transform), Color.Cyan);
    }

    private static void AddCross(
        List<VertexPositionColor> lines,
        Vector3 position,
        float radius,
        Color colour)
    {
        AddLine(lines, position - Vector3.UnitX * radius, position + Vector3.UnitX * radius, colour);
        AddLine(lines, position - Vector3.UnitY * radius, position + Vector3.UnitY * radius, colour);
        AddLine(lines, position - Vector3.UnitZ * radius, position + Vector3.UnitZ * radius, colour);
    }

    public static Matrix BuildSemanticWorldTransform(
        float renderScale,
        Vector3 cameraRelativeRenderPosition,
        Quaternion shipOrientation)
        => Matrix.CreateScale(renderScale)
         * Matrix.CreateFromQuaternion(shipOrientation)
         * Matrix.CreateTranslation(cameraRelativeRenderPosition);

    private static Matrix BuildLegacyWorldTransform(
        float renderScale,
        Vector3 cameraRelativeRenderPosition,
        Quaternion shipOrientation)
        => Matrix.CreateScale(renderScale)
         * Matrix.CreateRotationY(MathF.PI)
         * Matrix.CreateFromQuaternion(shipOrientation)
         * Matrix.CreateTranslation(cameraRelativeRenderPosition);

    private void DrawLegacyFallback(
        Matrix world,
        Matrix currentView,
        Matrix projection,
        Color sunColour)
    {
        LegacyFallbackMesh legacy = GetOrCreateLegacyFallback();

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
        foreach (var mesh in _engineMeshCache.Values)
            mesh.Dispose();

        _legacyFallback?.Dispose();
        _debugLineEffect.Dispose();
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

public enum SemanticHullDebugMode
{
    Normal,
    SurfaceRoles,
}
