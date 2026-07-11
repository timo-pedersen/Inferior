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
        ShadowMapDepth,
        ZeroBiasShadow,
        ReceiverDepth,
        SampledCasterDepth,
        DepthDifference,
        SlopeFactor,
        FinalBiasedShadow,
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
            _stationShadowEffect.Parameters["ShadowMap"]?.SetValue(shadow.Texture);

            foreach (var mod in modules)
            {
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                if (mod.TextureInstance == null) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationHull", hull.vb, hull.ib, hull.triCount, world,
                    mod.Transform, mod.TextureInstance, emissive: false);
            }

            foreach (var mod in modules)
            {
                if (!decoMeshesForLevel.TryGetValue(mod, out var deco)) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationBaked", deco.vb, deco.ib, deco.triCount, world,
                    mod.Transform, mod.TextureInstance ?? StationTextureRegistry.Get(mod.Mesh!.Texture),
                    emissive: false);
            }

            foreach (var mod in modules)
            {
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                Matrix world = StationRenderWorld(mod, stationRot, renderPos, rs);
                DrawStationMesh("StationBaked", glass.vb, glass.ib, glass.triCount, world,
                    mod.Transform, StationTextureRegistry.White, emissive: true);
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
        Matrix world, Matrix stationLocalWorld, Texture2D texture, bool emissive)
    {
        _stationShadowEffect.CurrentTechnique = _stationShadowEffect.Techniques[technique];
        _stationShadowEffect.Parameters["World"]?.SetValue(world);
        _stationShadowEffect.Parameters["StationLocalWorld"]?.SetValue(stationLocalWorld);
        _stationShadowEffect.Parameters["DiffuseTexture"]?.SetValue(texture);
        _stationShadowEffect.Parameters["EmissiveSurface"]?.SetValue(emissive ? 1f : 0f);

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

    private void DrawStationShadowDebugView(SpriteBatch sb)
    {
        if (!_showStationShadowDebug || _stationShadows.Count == 0)
            return;

        if (!TrySelectStationShadowDebugTarget(out var station, out var shadow) || shadow == null)
            return;

        int size = Math.Min(256, Math.Min(_gd.Viewport.Width, _gd.Viewport.Height) / 3);
        if (size <= 0) return;

        sb.Draw(shadow.Texture, new Rectangle(12, 12, size, size), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12, size, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12 + size - 1, size, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(12, 12, 1, size), Color.White);
        sb.Draw(_pixel, new Rectangle(12 + size - 1, 12, 1, size), Color.White);

        float depthMetres = shadow.DepthRange.Length;
        float baseBiasMetres = StationBaseShadowBias * depthMetres;
        float slopeBiasMetres = StationSlopeShadowBias * depthMetres;
        float maxBiasMetres = StationMaxShadowBias * depthMetres;
        string stationName = station?.Name ?? "<unknown>";
        string text =
            $"Station shadow debug: {stationName} / {StationShadowDebugModeName(_stationShadowDebugMode)}\n" +
            "F9 show/hide, F8 cycle\n" +
            $"target={shadow.Texture.Width}x{shadow.Texture.Height} format={shadow.SurfaceFormat}\n" +
            $"near={shadow.DepthRange.Near:0.###}m far={shadow.DepthRange.Far:0.###}m span={depthMetres:0.###}m\n" +
            $"bias norm base={StationBaseShadowBias:0.000000} slope={StationSlopeShadowBias:0.000000} max={StationMaxShadowBias:0.000000}\n" +
            $"bias metres base={baseBiasMetres:0.###} slope={slopeBiasMetres:0.###} max={maxBiasMetres:0.###}\n" +
            $"normal offset={StationNormalShadowOffsetMetres:0.###}m * slope\n" +
            $"diff scale={StationShadowDebugDifferenceScale:0.#}";
        Vector2 pos = new(12, 20 + size);
        sb.DrawString(_font, text, pos + new Vector2(1, 1), Color.Black);
        sb.DrawString(_font, text, pos, Color.White);
    }

    private bool TrySelectStationShadowDebugTarget(out Galaxy.Station? station, out StationShadowMap? shadow)
    {
        station = null;
        shadow = null;

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
            StationShadowDebugMode.ZeroBiasShadow => 1,
            StationShadowDebugMode.ReceiverDepth => 2,
            StationShadowDebugMode.SampledCasterDepth => 3,
            StationShadowDebugMode.DepthDifference => 4,
            StationShadowDebugMode.SlopeFactor => 5,
            StationShadowDebugMode.FinalBiasedShadow => 6,
            _ => 0,
        };
    }

    private static StationShadowDebugMode NextStationShadowDebugMode(StationShadowDebugMode mode)
        => mode == StationShadowDebugMode.FinalBiasedShadow
            ? StationShadowDebugMode.ShadowMapDepth
            : (StationShadowDebugMode)((int)mode + 1);

    private static string StationShadowDebugModeName(StationShadowDebugMode mode)
        => mode switch
        {
            StationShadowDebugMode.ShadowMapDepth => "stored shadow-map depth",
            StationShadowDebugMode.ZeroBiasShadow => "zero-bias shadow factor",
            StationShadowDebugMode.ReceiverDepth => "receiver normalized light depth",
            StationShadowDebugMode.SampledCasterDepth => "sampled caster depth",
            StationShadowDebugMode.DepthDifference => "receiver minus caster depth",
            StationShadowDebugMode.SlopeFactor => "ndotl / slope factor",
            StationShadowDebugMode.FinalBiasedShadow => "final biased shadow factor",
            _ => mode.ToString(),
        };

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
