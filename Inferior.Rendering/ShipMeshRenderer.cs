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

        var lines = BuildSemanticDebugLines(min, max);
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

    public static VertexPositionColor[] BuildSemanticDebugLines(Vector3 min, Vector3 max)
    {
        var vertices = new List<VertexPositionColor>(300);
        const float axisLength = 4.5f;
        Vector3 xTip = Vector3.UnitX * axisLength;
        Vector3 yTip = Vector3.UnitY * axisLength;
        Vector3 forwardTip = -Vector3.UnitZ * axisLength;
        AddArrow(vertices, Vector3.Zero, xTip, Vector3.UnitY, Color.Red);
        AddArrow(vertices, Vector3.Zero, yTip, Vector3.UnitX, Color.LimeGreen);
        AddArrow(vertices, Vector3.Zero, forwardTip, Vector3.UnitX, Color.Cyan);
        AddDebugLabel(vertices, "+X STARBOARD", new Vector3(4.8f, -0.3f, 0f), Vector3.UnitX, Vector3.UnitY, Color.Red);
        AddDebugLabel(vertices, "+Y UP", new Vector3(0.3f, 4.8f, 0f), Vector3.UnitX, Vector3.UnitY, Color.LimeGreen);
        AddDebugLabel(vertices, "-Z FORWARD", new Vector3(-3.0f, -0.3f, -4.8f), Vector3.UnitX, Vector3.UnitY, Color.Cyan);

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

    private static void AddArrow(
        List<VertexPositionColor> vertices,
        Vector3 start,
        Vector3 tip,
        Vector3 wingAxis,
        Color colour)
    {
        AddLine(vertices, start, tip, colour);
        Vector3 direction = Vector3.Normalize(tip - start);
        Vector3 wingBase = tip - direction * 0.45f;
        AddLine(vertices, tip, wingBase + wingAxis * 0.22f, colour);
        AddLine(vertices, tip, wingBase - wingAxis * 0.22f, colour);
    }

    private static void AddDebugLabel(
        List<VertexPositionColor> vertices,
        string text,
        Vector3 origin,
        Vector3 right,
        Vector3 up,
        Color colour)
    {
        const float pixelSize = 0.12f;
        int cursor = 0;
        foreach (char character in text)
        {
            string[] glyph = DebugGlyph(character);
            for (int row = 0; row < glyph.Length; row++)
            {
                for (int column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] != '1')
                        continue;

                    Vector3 pixelStart =
                        origin
                        + right * ((cursor + column) * pixelSize)
                        + up * ((4 - row) * pixelSize);
                    AddLine(vertices, pixelStart, pixelStart + right * (pixelSize * 0.8f), colour);
                }
            }
            cursor += 4;
        }
    }

    private static string[] DebugGlyph(char character)
        => character switch
        {
            ' ' => ["000", "000", "000", "000", "000"],
            '+' => ["000", "010", "111", "010", "000"],
            '-' => ["000", "000", "111", "000", "000"],
            'A' => ["010", "101", "111", "101", "101"],
            'B' => ["110", "101", "110", "101", "110"],
            'D' => ["110", "101", "101", "101", "110"],
            'F' => ["111", "100", "110", "100", "100"],
            'O' => ["010", "101", "101", "101", "010"],
            'P' => ["110", "101", "110", "100", "100"],
            'R' => ["110", "101", "110", "101", "101"],
            'S' => ["011", "100", "010", "001", "110"],
            'T' => ["111", "010", "010", "010", "010"],
            'U' => ["101", "101", "101", "101", "111"],
            'W' => ["101", "101", "111", "111", "101"],
            'X' => ["101", "101", "010", "101", "101"],
            'Y' => ["101", "101", "010", "010", "010"],
            'Z' => ["111", "001", "010", "100", "111"],
            _ => ["111", "001", "010", "000", "010"],
        };

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
            EngineVisualMaterial.LightWhite => new Color(225, 240, 245),
            EngineVisualMaterial.LightRed => new Color(225, 42, 35),
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
        VertexPositionColor[] lines = BuildEngineModuleDebugLines(engineMounts);
        if (lines.Length == 0)
            return;

        _debugLineEffect.World = shipWorld;
        _debugLineEffect.View = view;
        _debugLineEffect.Projection = projection;
        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.DepthStencilState = DepthStencilState.Default;
        foreach (EffectPass pass in _debugLineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineList, lines, 0, lines.Length / 2);
        }
    }

    public static VertexPositionColor[] BuildEngineModuleDebugLines(
        IReadOnlyList<EngineMountPresentationSnapshot> engineMounts)
    {
        var lines = new List<VertexPositionColor>();
        foreach (EngineMountPresentationSnapshot mount in engineMounts)
        {
            if (mount.HullRootPosition is { } root)
            {
                Matrix rootTransform =
                    Matrix.CreateFromQuaternion(mount.Pose.Orientation)
                    * Matrix.CreateTranslation(root.ToVector3());
                AddTransformAxes(lines, rootTransform, 0.55f);
            }

            DVec3 interfacePosition = mount.AttachmentInterfacePosition ?? mount.Pose.Position;
            Matrix interfaceTransform =
                Matrix.CreateFromQuaternion(mount.Pose.Orientation)
                * Matrix.CreateTranslation(interfacePosition.ToVector3());
            AddTransformAxes(lines, interfaceTransform, 0.85f);
            if (mount.HullRootPosition is { } hullRoot)
                AddLine(lines, hullRoot.ToVector3(), interfacePosition.ToVector3(), Color.Yellow);

            EnginePresentationSnapshot? engine = mount.InstalledEngine;
            if (engine is null)
                continue;

            Matrix engineTransform = engine.GeometryTransform.LocalToHull;
            AddCross(lines, engineTransform.Translation, 0.16f, Color.White);
            AddTransformAxes(lines, engineTransform, 0.45f);
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
                Vector3 direction = Vector3.TransformNormal(
                    EngineMeshBuilder.ToVector3(light.Direction, mirrored),
                    engineTransform);
                AddLine(
                    lines,
                    position,
                    position + Vector3.Normalize(direction) * 0.45f,
                    new Color(light.Colour.ToVector3()));
            }
        }

        return lines.ToArray();
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
