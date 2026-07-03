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

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Station drawing ───────────────────────────────────────────────────────

    private static float StationPhysicalRadius(Galaxy.Station s) => s.Size switch
    {
        Galaxy.StationSize.Small  =>  250f,
        Galaxy.StationSize.Medium =>  800f,
        Galaxy.StationSize.Large  => 2500f,
        _                         =>  250f,
    };

    private void DrawStations()
    {
        if (_stationPositions.Count == 0) return;

        float rs = (float)Camera3D.RenderScale;

        // Hull pass — real-time BasicEffect N·L lighting with procedural texture.
        // Uses VertexPositionNormalTexture so normals are in the vertex data.
        // DirectionalLight0.Direction is already set from the star position in Draw().
        _effect.LightingEnabled                = true;
        _effect.TextureEnabled                 = true;
        _effect.VertexColorEnabled             = false;
        _effect.DiffuseColor                   = Vector3.One;
        _effect.DirectionalLight0.DiffuseColor = SceneLighting.SunColour;
        _effect.AmbientLightColor              = new Vector3(SceneLighting.Ambient);

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;
            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                if (mod.TextureInstance == null) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);
                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _effect.Texture = mod.TextureInstance;

                _gd.SetVertexBuffer(hull.vb);
                _gd.Indices = hull.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: hull.triCount);
                }
            }
        }

        // Decoration pass — pre-baked lighting in vertex colours; texture modulates.
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = true;

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_decoMeshes.TryGetValue(mod, out var deco)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _effect.Texture = mod.TextureInstance ?? StationTextureRegistry.Get(mod.Mesh!.Texture);

                _gd.SetVertexBuffer(deco.vb);
                _gd.Indices = deco.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: deco.triCount);
                }
            }
        }

        // Glass pass — windows, portholes; White texture so vertex colours are unmodified.
        _effect.Texture = StationTextureRegistry.White;

        foreach (var (station, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            if (!_stationGeometry.TryGetValue(station, out var modules)) continue;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _gd.SetVertexBuffer(glass.vb);
                _gd.Indices = glass.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: glass.triCount);
                }
            }
        }

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Builds a VertexPositionNormalTexture hull mesh for one module (6 box faces, 24 verts).
    // Normals are local-space outward per face; BasicEffect transforms them at draw time.
    // UV uses the same tangent-frame projection as StationModuleMesh.AddQuad (5 m/tile).
    private static (VertexBuffer vb, IndexBuffer ib, int triCount) BuildHullMesh(
        GraphicsDevice gd, PlacedModule mod)
    {
        const float UvScale      = 5.0f;
        const float ChamferInset = 0.38f * 0.707f;  // matches StationDecorator chamferW * 0.707f
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

    // Draws additive screen-space glow sprites over all station nav lights and warning strobes.
    // Must be called after DrawStations() so the additive blend brightens visible geometry.
    private void DrawStationGlows(SpriteBatch sb)
    {
        if (_stationPositions.Count == 0) return;

        Matrix   viewProj  = _effect.View * _camera.ProjectionMatrix;
        Viewport viewport  = _gd.Viewport;
        Vector2  texCentre = new(_navGlowTex.Width * 0.5f, _navGlowTex.Height * 0.5f);

        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive);
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
                        _                                   => 400f,
                    };
                    float size  = MathHelper.Clamp(baseSize / distance, 6f, 140f);
                    float scale = size / _navGlowTex.Width;

                    sb.Draw(_navGlowTex, screen.Value, null,
                            light.Colour * intensity, 0f, texCentre, scale,
                            SpriteEffects.None, 0f);
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
