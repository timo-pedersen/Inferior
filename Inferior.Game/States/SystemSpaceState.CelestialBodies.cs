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

    // ── Opaque pass ───────────────────────────────────────────────────────────

    private void DrawStarBody()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);
        float   radius    = StarApparentRadius(renderPos);
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        // Star surface colour — white base tinted toward LightColor by a per-class factor.
        // Hot stars (O/B) stay near-white; cool stars (K/M) show clear yellow/orange/red.
        Color bodyColor = Color.Lerp(Color.White, _star.LightColor, _star.BodyTintStrength);
        DrawSphere(renderPos, radius, bodyColor, false);
        _effect.LightingEnabled = true;
    }

    private void DrawPlanetBody(OrbitalBody body, DVec3 universePos)
    {
        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        if (renderPos.Length() > 30_000f) return;

        float radius = PlanetApparentRadius(body, renderPos);

        if (_planetSpheres.TryGetValue(body, out var cbSphere))
        {
            _effect.LightingEnabled    = false;
            _effect.VertexColorEnabled = true;
            _effect.DiffuseColor       = Vector3.One;
            _effect.Alpha              = 1f;

            _effect.World = Matrix.CreateScale(radius)
                          * Matrix.CreateFromQuaternion(body.Orientation)
                          * Matrix.CreateTranslation(renderPos);

            _gd.SetVertexBuffer(cbSphere.vb);
            _gd.Indices = cbSphere.ib;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, cbSphere.triCount);
            }

            _effect.VertexColorEnabled = false;
            _effect.LightingEnabled    = true;
        }
        else
        {
            DrawSphere(renderPos, radius, BodyColor(body), lit: true);
        }
    }

    // ── Star glow (3D billboard, additive) ───────────────────────────────────

    private void DrawStarGlow3D()
    {
        Vector3 renderPos = _camera.ToRenderSpace(DVec3.Zero);
        if (Vector4.Transform(new Vector4(renderPos, 1f),
                              _camera.ViewMatrix * _camera.ProjectionMatrix).W <= 0f) return;

        float baseRU = StarApparentRadius(renderPos);
        var   right  = _camera.Right;
        var   up     = _camera.Up;

        _effect.TextureEnabled     = true;
        _effect.VertexColorEnabled = true;
        _effect.LightingEnabled    = false;
        _effect.Texture            = _starGlowTex;
        _effect.World              = Matrix.Identity;

        DrawGlowBillboard(renderPos, baseRU * 14f,  right, up, _star.GlowColor * 0.07f);
        DrawGlowBillboard(renderPos, baseRU * 6f,   right, up, _star.GlowColor * 0.28f);
        DrawGlowBillboard(renderPos, baseRU * 2.5f, right, up, _star.GlowColor * 0.65f);
        DrawGlowBillboard(renderPos, baseRU * 1.1f, right, up, Color.White     * 0.90f);

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
    }

    private void DrawGlowBillboard(Vector3 center, float radius, Vector3 right, Vector3 up, Color color)
    {
        if (radius < 0.0001f) return;
        var tl = center + (-right + up) * radius;
        var tr = center + ( right + up) * radius;
        var bl = center + (-right - up) * radius;
        var br = center + ( right - up) * radius;
        _glowVerts[0] = new(tl, color, new Vector2(0, 0));
        _glowVerts[1] = new(tr, color, new Vector2(1, 0));
        _glowVerts[2] = new(bl, color, new Vector2(0, 1));
        _glowVerts[3] = new(tr, color, new Vector2(1, 0));
        _glowVerts[4] = new(br, color, new Vector2(1, 1));
        _glowVerts[5] = new(bl, color, new Vector2(0, 1));
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _glowVerts, 0, 2);
        }
    }

    // Gaussian radial gradient baked into a texture — reused for every glow layer.
    private static Texture2D CreateStarGlowTexture(GraphicsDevice gd, int size)
    {
        var   tex  = new Texture2D(gd, size, size);
        var   data = new Color[size * size];
        float r    = size * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float t     = MathF.Min(MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r, 1f);
            float alpha = MathF.Exp(-t * t * 3f); // gaussian: 1.0 at center → ~0.05 at edge
            data[y * size + x] = Color.White * alpha;
        }

        tex.SetData(data);
        return tex;
    }

    // Cubic-falloff radial gradient for nav light / strobe glow — bright centre, soft edge.
    private static Texture2D CreateNavGlowTexture(GraphicsDevice gd, int size = 64)
    {
        var   tex  = new Texture2D(gd, size, size);
        var   data = new Color[size * size];
        float r    = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist  = MathF.Sqrt((x - r) * (x - r) + (y - r) * (y - r));
            float t     = MathF.Max(0f, 1f - dist / r);
            float alpha = t * t * t;  // cubic: full brightness at centre, zero at rim
            data[y * size + x] = Color.White * alpha;
        }
        tex.SetData(data);
        return tex;
    }

    /// <summary>
    /// Minimum render-space radius for a planet within boost range.
    /// Beyond PlanetMaxBoostDist the planet is left to shrink and vanish naturally.
    /// </summary>
    private float PlanetApparentRadius(OrbitalBody body, Vector3 renderPos)
    {
        float dist       = renderPos.Length();
        float baseRadius = VisualRadius(body);
        if (dist > PlanetMaxBoostDist) return baseRadius;

        float projScale      = _gd.Viewport.Height
                             / (2f * MathF.Tan(MathHelper.ToRadians(30f)));
        float minRenderRadius = PlanetMinPixels * dist / projScale;
        return System.Math.Max(baseRadius, minRenderRadius);
    }

    /// <summary>
    /// Minimum render-space radius that keeps the star at least <see cref="StarMinPixels"/>
    /// pixels across at any distance. Grows with distance so the star is always visible;
    /// never shrinks below StarVisualRadius when close.
    /// </summary>
    private float StarApparentRadius(Vector3 renderPos)
    {
        float dist = renderPos.Length();
        if (dist < 0.001f) return StarVisualRadius;

        // projScale converts render-space size at unit distance to screen pixels.
        // For a symmetric frustum: projScale = screenHeight / (2 * tan(halfFov))
        float projScale = _gd.Viewport.Height
                        / (2f * MathF.Tan(MathHelper.ToRadians(60f))); // half of 60°

        float minRenderRadius = StarMinPixels * dist / projScale;
        return System.Math.Max(StarVisualRadius, minRenderRadius);
    }

    private void DrawAtmosphere(OrbitalBody body, DVec3 universePos)
    {
        if (body.AtmosphereType == AtmosphereType.None || body.AtmosphereHeight <= 0) return;
        if (_atmosEffect == null) return;

        Vector3 renderPos = _camera.ToRenderSpace(universePos);
        float   camDist   = renderPos.Length();
        if (camDist > 30_000f) return;

        // Physical atmosphere radius preserving the planet/atmosphere ratio under the
        // per-pixel visual size boost applied to distant planets.
        float physPlanetR  = VisualRadius(body);
        float physAtmosR   = (float)((body.RadiusMeters + body.AtmosphereHeight) * Camera3D.RenderScale);
        float planetRadius = PlanetApparentRadius(body, renderPos);
        float atmosRadius  = physPlanetR > 0f ? physAtmosR * (planetRadius / physPlanetR) : planetRadius;

        // Minimum visual thickness so the glow gradient is visible even for thin atmospheres.
        // drawRadius is derived from shaderAtmosRadius so the billboard always exceeds the fade zone.
        float shaderAtmosRadius = MathF.Max(atmosRadius, planetRadius * 1.05f);
        float drawRadius        = shaderAtmosRadius * 1.15f;

        // Billboard half-size: cover the draw sphere's projected circle from outside,
        // or cover the full sky from inside.
        float billHalf;
        if (camDist <= drawRadius)
        {
            // Camera inside — billboard must subtend > 180°, so use 3× the distance
            // to the planet (or 2× the draw radius if planet is very close).
            billHalf = MathF.Max(2f * drawRadius, camDist * 3f);
        }
        else
        {
            // Exact projected angular radius of the draw sphere, 15 % extra margin.
            float sinHA = drawRadius / camDist;
            float tanHA = sinHA / MathF.Sqrt(1f - sinHA * sinHA);
            billHalf    = camDist * tanHA * 1.15f;
        }

        // Camera-aligned billboard centred at the planet's render-space position.
        // CW winding (TL→TR→BL, TR→BR→BL) — front faces are CW under CullCounterClockwise.
        var     right = _camera.Right;
        var     up    = _camera.Up;
        Vector3 tl    = renderPos + (-right + up) * billHalf;
        Vector3 tr    = renderPos + ( right + up) * billHalf;
        Vector3 bl    = renderPos + (-right - up) * billHalf;
        Vector3 br    = renderPos + ( right - up) * billHalf;

        _atmosQuadVerts[0] = new(tl, Vector2.Zero);
        _atmosQuadVerts[1] = new(tr, Vector2.Zero);
        _atmosQuadVerts[2] = new(bl, Vector2.Zero);
        _atmosQuadVerts[3] = new(tr, Vector2.Zero);
        _atmosQuadVerts[4] = new(br, Vector2.Zero);
        _atmosQuadVerts[5] = new(bl, Vector2.Zero);

        _atmosEffect.Parameters["ViewProjection"].SetValue(_camera.ViewMatrix * _camera.ProjectionMatrix);
        _atmosEffect.Parameters["PlanetCenter"].SetValue(renderPos);
        _atmosEffect.Parameters["PlanetRadius"].SetValue(planetRadius * 0.98f); // slight inset to close gap at limb
        _atmosEffect.Parameters["AtmosRadius"].SetValue(shaderAtmosRadius);
        _atmosEffect.Parameters["AtmosphereColor"].SetValue(body.AtmosphereColor.ToVector3());
        _atmosEffect.Parameters["Opacity"].SetValue(OpacityFor(body.AtmosphereType));
        _atmosEffect.Parameters["LightDirection"].SetValue(_effect.DirectionalLight0.Direction);

        _gd.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in _atmosEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _atmosQuadVerts, 0, 2);
        }
        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    private static float OpacityFor(AtmosphereType type) => type switch
    {
        AtmosphereType.Thin       => 0.45f,
        AtmosphereType.Breathable => 0.65f,
        AtmosphereType.Thick      => 0.85f,
        AtmosphereType.Toxic      => 0.75f,
        AtmosphereType.Corrosive  => 0.95f,
        _                         => 0.65f,
    };

    private void DrawSphere(Vector3 renderPos, float radius, Color color, bool lit)
    {
        _effect.LightingEnabled = lit;
        _effect.DiffuseColor    = color.ToVector3();
        _effect.Alpha           = color.A / 255f;

        _effect.World = Matrix.CreateScale(radius)
                      * Matrix.CreateTranslation(renderPos);

        _gd.SetVertexBuffer(_sphereVb);
        _gd.Indices = _sphereIb;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                baseVertex: 0,
                startIndex: 0,
                primitiveCount: _sphereTriCount);
        }
    }

    private void DrawOrbitRings()
    {
        // Disable lighting for line drawing
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        foreach (var planet in _system.Planets)
        {
            float ringRadius = (float)(planet.OrbitalRadius * Camera3D.RenderScale);

            // Skip rings too small to see (inside star or sub-pixel)
            if (ringRadius < StarVisualRadius * 1.5f) continue;
            if (ringRadius > 25_000f) continue; // too far

            // Colour ring by distance from camera for depth feel
            Color col = ColOrbitRing;

            // Apply ecliptic tilt so rings lie in the system's orbital plane, not the galaxy plane
            _effect.World = Matrix.CreateScale(ringRadius) * _eclipticRotation;
            DrawRingRaw(col);

            // Moon orbit rings — centred on the planet's tilted position
            if (planet.Children.Count > 0)
            {
                DVec3 planetUniverse = EclipticToGalaxy(planet.GetPosition(_gameTimeSeconds, DVec3.Zero));
                Vector3 planetRender = _camera.ToRenderSpace(planetUniverse);

                foreach (var moon in planet.Children)
                {
                    float moonRingR = (float)(moon.OrbitalRadius * Camera3D.RenderScale);
                    if (moonRingR < 0.01f) continue;

                    // Scale → tilt → translate to planet position
                    _effect.World = Matrix.CreateScale(moonRingR)
                                  * _eclipticRotation
                                  * Matrix.CreateTranslation(planetRender);

                    DrawRingRaw(new Color(20, 28, 44, 140));
                }
            }
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    private void DrawRing(float radius, Color color)
    {
        _effect.World = Matrix.CreateScale(radius);
        DrawRingRaw(color);
    }

    private void DrawRingRaw(Color color)
    {
        // Set colour on all vertices
        for (int i = 0; i < _ringVerts.Length; i++)
            _ringVerts[i].Color = color;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(
                PrimitiveType.LineStrip,
                _ringVerts, 0,
                _ringVerts.Length - 1); // n-1 lines from n+1 verts (closed loop)
        }
    }

    private const float PlanetVisualScale = 1f;

    private static float VisualRadius(OrbitalBody body) =>
        (float)(body.RadiusMeters * Camera3D.RenderScale * PlanetVisualScale);

    private static Color BodyColor(OrbitalBody body) => body.BodyType switch
    {
        BodyType.EarthLike   => new Color(80,  140, 200),
        BodyType.OceanPlanet => new Color(40,  100, 200),
        BodyType.Desert      => new Color(200, 160,  80),
        BodyType.Volcanic    => new Color(200,  60,  20),
        BodyType.RockyPlanet => new Color(140, 130, 120),
        BodyType.IcePlanet   => new Color(200, 220, 240),
        BodyType.IceGiant    => new Color(100, 180, 220),
        BodyType.GasGiant    => new Color(200, 160, 100),
        BodyType.Moon        => new Color(160, 155, 150),
        _                    => new Color(150, 150, 150),
    };

    // ── Planet checkerboard sphere builder ────────────────────────────────────

    private (VertexBuffer vb, IndexBuffer ib, int triCount) BuildPlanetSphere(OrbitalBody body)
    {
        const int Rings    = 64;
        const int Segments = 128;

        PlanetType type    = body.Planet!.Type;
        bool       gasMode = type == PlanetType.GasGiant || type == PlanetType.IceGiant;
        Vector3    sunDir  = SceneLighting.SunDirection;
        float      ambient = SceneLighting.Ambient;

        int vertCount  = (Rings + 1) * (Segments + 1);
        int indexCount = Rings * Segments * 6;
        int triCount   = Rings * Segments * 2;

        var verts   = new VertexPositionColor[vertCount];
        var indices = new int[indexCount];

        int v = 0;
        for (int ring = 0; ring <= Rings; ring++)
        {
            float phi = MathF.PI * ring / Rings;
            for (int seg = 0; seg <= Segments; seg++)
            {
                float   theta  = MathF.PI * 2f * seg / Segments;
                float   nx     = MathF.Sin(phi) * MathF.Cos(theta);
                float   ny     = MathF.Cos(phi);
                float   nz     = MathF.Sin(phi) * MathF.Sin(theta);
                Vector3 normal = new(nx, ny, nz);

                double lat = System.Math.Asin(System.Math.Clamp(ny, -1f, 1f)) * (180.0 / System.Math.PI);
                double lon = System.Math.Atan2(nz, nx) * (180.0 / System.Math.PI);

                Color baseColor = GetSphereVertexColor(lat, lon, type, gasMode);

                float lightFactor = MathF.Max(Vector3.Dot(normal, sunDir), ambient);
                Color litColor    = new(
                    (byte)MathF.Min(baseColor.R * lightFactor, 255f),
                    (byte)MathF.Min(baseColor.G * lightFactor, 255f),
                    (byte)MathF.Min(baseColor.B * lightFactor, 255f));

                verts[v++] = new VertexPositionColor(normal, litColor);
            }
        }

        int idx = 0;
        for (int ring = 0; ring < Rings; ring++)
        for (int seg  = 0; seg  < Segments; seg++)
        {
            int a = ring       * (Segments + 1) + seg;
            int b = (ring + 1) * (Segments + 1) + seg;
            int c = (ring + 1) * (Segments + 1) + seg + 1;
            int d = ring       * (Segments + 1) + seg + 1;
            indices[idx++] = a; indices[idx++] = b; indices[idx++] = c;
            indices[idx++] = a; indices[idx++] = c; indices[idx++] = d;
        }

        var vb = new VertexBuffer(_gd, VertexPositionColor.VertexDeclaration, vertCount, BufferUsage.WriteOnly);
        vb.SetData(verts);
        var ib = new IndexBuffer(_gd, IndexElementSize.ThirtyTwoBits, indexCount, BufferUsage.WriteOnly);
        ib.SetData(indices);

        return (vb, ib, triCount);
    }

    private static Color GetSphereVertexColor(double lat, double lon, PlanetType type, bool gasMode)
    {
        // White pole caps
        if (System.Math.Abs(lat) > 85.0) return new Color(235, 238, 245);

        // Equator stripe
        if (System.Math.Abs(lat) < 0.4) return GetEquatorColor(type);

        bool darkCell;
        if (gasMode)
        {
            int latCell = (int)System.Math.Floor((lat + 90.0) / 5.0);
            darkCell = latCell % 2 == 0;
        }
        else
        {
            int latCell = (int)System.Math.Floor((lat + 90.0)  / 5.0);
            int lonCell = (int)System.Math.Floor((lon + 180.0) / 5.0);
            darkCell = (latCell + lonCell) % 2 == 0;
        }

        return darkCell ? GetDarkColor(type) : GetLightColor(type);
    }

    private static Color GetDarkColor(PlanetType type) => type switch
    {
        PlanetType.Barren   => new Color( 75,  75,  75),
        PlanetType.Lava     => new Color(100,  20,  20),
        PlanetType.Rocky    => new Color( 80,  70,  50),
        PlanetType.Ocean    => new Color( 20,  50, 110),
        PlanetType.IcyRocky => new Color( 50,  80,  95),
        PlanetType.GasGiant => new Color(120, 100,  70),
        PlanetType.IceGiant => new Color( 50,  80, 130),
        _                   => new Color( 80,  80,  80),
    };

    private static Color GetLightColor(PlanetType type) => type switch
    {
        PlanetType.Barren   => new Color(140, 140, 140),
        PlanetType.Lava     => new Color(190,  80,  30),
        PlanetType.Rocky    => new Color(165, 140, 110),
        PlanetType.Ocean    => new Color( 40, 120, 170),
        PlanetType.IcyRocky => new Color(175, 200, 215),
        PlanetType.GasGiant => new Color(200, 180, 140),
        PlanetType.IceGiant => new Color(110, 150, 195),
        _                   => new Color(140, 140, 140),
    };

    private static Color GetEquatorColor(PlanetType type) => type switch
    {
        PlanetType.Barren   => new Color(180, 180, 160),
        PlanetType.Lava     => new Color(220, 120,  40),
        PlanetType.Rocky    => new Color(190, 170, 130),
        PlanetType.Ocean    => new Color( 60, 150, 190),
        PlanetType.IcyRocky => new Color(210, 225, 235),
        PlanetType.GasGiant => GetLightColor(PlanetType.GasGiant),
        PlanetType.IceGiant => GetLightColor(PlanetType.IceGiant),
        _                   => new Color(180, 180, 160),
    };
}
