using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Draws stars, planets/moons, their glow/atmosphere billboards, and orbit rings.
/// Constructed fresh per SystemSpaceState.OnEnter (matching CockpitUI's lifecycle) —
/// StarSystem is stored rather than passed per-call because a new instance always
/// gets a fresh, correct system; there's no mid-session reference-swap staleness
/// risk the way there is for Camera3D/Star, which SystemSpaceState can reassign or
/// reset out from under a stored reference (debug-cam Home reset, EnterSystem).
/// </summary>
public sealed class CelestialBodyRenderer : IDisposable
{
    // ── Borrowed dependencies (not owned/disposed here) ─────────────────────────
    private readonly GraphicsDevice     _gd;
    private readonly BasicEffect        _effect;
    private readonly Effect?            _atmosEffect;
    private readonly RingPrimitive      _ringPrimitive;
    private readonly Func<DVec3, DVec3> _eclipticToGalaxy;
    private readonly StarSystem         _system;

    // ── Owned GPU resources ──────────────────────────────────────────────────────
    private readonly VertexBuffer _sphereVb;
    private readonly IndexBuffer  _sphereIb;
    private readonly int          _sphereTriCount;
    private readonly Texture2D    _starGlowTex;
    private readonly Dictionary<OrbitalBody, (VertexBuffer vb, IndexBuffer ib, int triCount)> _planetSpheres = [];

    // Reused per glow billboard draw — avoids per-frame allocation
    private readonly VertexPositionColorTexture[] _glowVerts      = new VertexPositionColorTexture[6];
    // Reused per atmosphere billboard draw — 6 verts (2 triangles)
    private readonly VertexPositionTexture[]      _atmosQuadVerts = new VertexPositionTexture[6];

    // ── Visual constants (duplicated from SystemSpaceState — plain constants,
    // cheap to duplicate rather than plumb through as parameters) ───────────────
    private const float StarVisualRadius   = 8f;
    // Brief B1a Fix 1: KEPT at 1, not raised to the brief's illustrative "~2px at 37 AU."
    // D-Bright/B1 measured the disc's floor CROSSOVER (where true angular size stops
    // dominating and the floor locks in) at ~1.5 AU for a G-class star with this 1px floor —
    // meaning most of the 1-37+ AU range Timo actually flies in was already floor-bound at a
    // CONSTANT pixel size, which is why "the same star renders at the same size at 1 AU and
    // 37 AU": cause (2) from the brief, the floor binding at both distances, not (1) distance
    // going unused — StarApparentRadius's math already derives true angular size from live
    // distance every frame (confirmed by direct read), it's just that a realistic star's true
    // angular size is only ~1-2px at typical in-system range under this camera's FOV, so the
    // floor dominates almost everywhere except close approach or genuinely large (giant)
    // stars. Verified directly, not assumed: raising this to 2 (matching the brief's literal
    // "~2px" text) was tried and measured — it makes the defect WORSE for common classes, not
    // better, because a 2px floor exceeds a G-class star's true 1AU size (1.49px), pulling
    // 1 AU ITSELF into floor-bound territory (both distances then read as an identical,
    // flat 2px — zero shrink). Kept at the brief's own stated LOWER bound ("never below
    // ~1px") instead, which preserves the real (if modest, ~1.5x) shrink for common classes;
    // see StarApparentRadius's own comment for the full per-class numbers this produces, and
    // GlareOuterRadius for how Fix 2 carries the primary "how far away" signal instead.
    private const float StarMinPixels      = 1f;
    private const float PlanetMinPixels    = 1f;
    private const float PlanetMaxBoostDist = 4500f; // ~30 AU — no boost beyond this
    private const float PlanetVisualScale  = 1f;

    // Brief B1 Fix 1 (superseded by B1a Fix 2 below): the glare stack's per-layer relative
    // size, radius multiples of a shared brightness-driven base rather than the disc's own
    // apparent radius. D-Bright measured the pre-B1 stack (14x/6x/2.5x/1.1x at alpha
    // 0.07/0.28/0.65/0.90) as "spatially present but far too faint" — B1 raised every layer's
    // alpha substantially and added a fifth, much larger, very faint layer for a long
    // falloff tail. B1a found the glare was STILL negligible despite that, because tying
    // glare radius to disc radius means a physically tiny disc (true angular size, ~1-2px at
    // typical range) gets a proportionally tiny glare too — real glare is a property of
    // source brightness and the optical system, not how large the source APPEARS, which is
    // why a star light-years away still shows a point of glare rather than nothing. B1a Fix
    // 2 decouples these radii entirely (see DrawStarGlow) — these five constants now express
    // each layer's SIZE as a fraction of the outermost (GlareLayer4Radius), and ALPHA is
    // unchanged from B1 (already correct: near-white core, star-class hue in the halo).
    private const float GlareLayer0Radius = 1.1f;   private const float GlareLayer0Alpha = 0.95f; // white, innermost
    private const float GlareLayer1Radius = 2.5f;   private const float GlareLayer1Alpha = 0.80f; // coloured
    private const float GlareLayer2Radius = 6f;     private const float GlareLayer2Alpha = 0.45f; // coloured
    private const float GlareLayer3Radius = 14f;    private const float GlareLayer3Alpha = 0.22f; // coloured — was the "primary offender" at 0.07
    private const float GlareLayer4Radius = 30f;    private const float GlareLayer4Alpha = 0.06f; // coloured, outermost — long falloff tail

    // Brief B1a Fix 2: glare size driven by the star's own apparent brightness
    // (Luminosity/distanceAU^2), NOT disc size — see DrawStarGlow/GlareOuterRadiusPixels.
    // sqrt compression (chosen over log or raw): sqrt(Luminosity/distAU^2) =
    // sqrt(Luminosity)/distAU, turning inverse-SQUARE brightness falloff into inverse-LINEAR
    // radius falloff — smoother/less aggressive than log at extreme ranges, preserves
    // ordering (closer is always bigger), and is simple to reason about and tune. Reference:
    // a Sol-like G star (Luminosity~1) at 1 AU gives brightnessFactor=1, so
    // GlareOuterScale IS the outermost layer's radius in pixels at that reference point —
    // chosen (500px) for a dramatic near-approach halo per the brief's own ask ("a small
    // disc inside an enormous halo"). At 37 AU the same star's brightnessFactor is 1/37, so
    // outer radius would be ~13.5px unfloored — GlareFloorPixels (20px radius = 40px
    // diameter) takes over there, matching the brief's explicit "~40px across at 37 AU"
    // starting target. GlareMaxPixels is a safety cap only reachable at extreme luminosity
    // (O-class, Luminosity in the tens of thousands) or extreme close proximity — not a
    // tuned value, just a guard against a pathologically huge billboard.
    private const float GlareOuterScale  = 500f;
    private const float GlareFloorPixels = 20f;
    private const float GlareMaxPixels   = 2000f;

    private static readonly Color ColOrbitRing = new(25, 35, 55, 180);

    public CelestialBodyRenderer(
        GraphicsDevice gd, BasicEffect effect, Effect? atmosEffect,
        RingPrimitive ringPrimitive, Func<DVec3, DVec3> eclipticToGalaxy,
        StarSystem system)
    {
        _gd               = gd;
        _effect           = effect;
        _atmosEffect      = atmosEffect;
        _ringPrimitive    = ringPrimitive;
        _eclipticToGalaxy = eclipticToGalaxy;
        _system           = system;

        var (vb, ib) = MeshFactory.CreateSphere(gd, rings: 24, segments: 24);
        _sphereVb       = vb;
        _sphereIb       = ib;
        _sphereTriCount = 24 * 24 * 2;

        _starGlowTex = CreateStarGlowTexture(_gd, 128);

        foreach (var planet in system.Planets)
            if (planet.Planet != null)
                _planetSpheres[planet] = BuildPlanetSphere(planet);
    }

    public void Dispose()
    {
        _sphereVb?.Dispose();
        _sphereIb?.Dispose();
        _starGlowTex?.Dispose();
        foreach (var v in _planetSpheres.Values) { v.vb.Dispose(); v.ib.Dispose(); }
        _planetSpheres.Clear();
    }

    // ── Opaque pass ───────────────────────────────────────────────────────────

    // level is accepted but not yet used — planets/star already render as a single
    // cheap representation; no LOD variants exist yet.
    public void DrawStar(Camera3D camera, Star star, DetailLevel level)
    {
        Vector3 renderPos = camera.ToRenderSpace(DVec3.Zero);
        float   radius    = StarApparentRadius(renderPos, star.RadiusMeters);
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        // Star surface colour — white base tinted toward LightColor by a per-class factor.
        // Hot stars (O/B) stay near-white; cool stars (K/M) show clear yellow/orange/red.
        Color bodyColor = Color.Lerp(Color.White, star.LightColor, star.BodyTintStrength);
        DrawSphere(renderPos, radius, bodyColor, false);
        _effect.LightingEnabled = true;
    }

    public void DrawPlanet(Camera3D camera, OrbitalBody body, DVec3 universePos, DetailLevel level)
    {
        Vector3 renderPos = camera.ToRenderSpace(universePos);
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

    public void DrawStarGlow(Camera3D camera, Star star, DetailLevel level)
    {
        Vector3 renderPos = camera.ToRenderSpace(DVec3.Zero);
        if (Vector4.Transform(new Vector4(renderPos, 1f),
                              camera.ViewMatrix * camera.ProjectionMatrix).W <= 0f) return;

        float outerRU = GlareOuterRadius(renderPos, star.Luminosity);
        var   right   = camera.Right;
        var   up      = camera.Up;

        _effect.TextureEnabled     = true;
        _effect.VertexColorEnabled = true;
        _effect.LightingEnabled    = false;
        _effect.Texture            = _starGlowTex;
        _effect.World              = Matrix.Identity;

        // Brief B1a Fix 2: each layer is a fixed fraction of the outermost (GlareLayer4Radius
        // is the normalising denominator, not a disc-relative multiplier any more) — same
        // relative shape as B1's stack, now scaled as a whole by brightness/distance instead
        // of by disc size.
        DrawGlowBillboard(renderPos, outerRU,                                          right, up, star.GlowColor * GlareLayer4Alpha);
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer3Radius / GlareLayer4Radius), right, up, star.GlowColor * GlareLayer3Alpha);
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer2Radius / GlareLayer4Radius), right, up, star.GlowColor * GlareLayer2Alpha);
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer1Radius / GlareLayer4Radius), right, up, star.GlowColor * GlareLayer1Alpha);
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer0Radius / GlareLayer4Radius), right, up, Color.White    * GlareLayer0Alpha);

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
    }

    /// <summary>
    /// Brief B1a Fix 2: the outermost glare layer's render-space radius, driven by the
    /// star's apparent brightness (Luminosity/distanceAU^2, sqrt-compressed to
    /// sqrt(Luminosity)/distanceAU) rather than disc size — decoupled entirely from
    /// <see cref="StarApparentRadius"/>. Floored in PIXELS (converted to render-space via the
    /// same distance/projScale technique StarApparentRadius uses, so it doesn't drift with
    /// resolution or FOV) so the sun always carries a halo distinctly larger than a
    /// background starfield point regardless of class or distance; capped as a safety net
    /// against extreme luminosity or extreme proximity.
    /// </summary>
    private float GlareOuterRadius(Vector3 renderPos, double luminosity)
    {
        float distRU = renderPos.Length();
        double distAU = distRU / (Units.AU * Camera3D.RenderScale);
        double brightnessFactor = luminosity > 0.0
            ? System.Math.Sqrt(luminosity) / System.Math.Max(distAU, 0.001)
            : 0.0;

        float outerPixels = System.Math.Clamp(
            GlareOuterScale * (float)brightnessFactor, GlareFloorPixels, GlareMaxPixels);

        if (distRU < 0.001f) return outerPixels; // camera essentially at the star; pixels ~= RU here, degenerate case

        float projScale = _gd.Viewport.Height / (2f * MathF.Tan(MathHelper.ToRadians(60f)));
        return outerPixels * distRU / projScale;
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
    /// Brief B1 Fix 3: true angular-size render-space radius derived from the star's actual
    /// RadiusMeters, replacing the old flat StarVisualRadius constant that rendered every
    /// star class at the same size (a red giant and a dwarf looked identical — D-Bright's
    /// own finding). Still floored to a minimum <see cref="StarMinPixels"/> screen size so a
    /// distant or genuinely tiny star doesn't vanish — the floor is pixel-based (grows with
    /// distance), not the old fixed render-space constant, so a small star up close still
    /// shows its true small size rather than being inflated to match a bigger class.
    ///
    /// Brief B1a: this formula was already distance-live and resolution/FOV-independent —
    /// confirmed by direct read, not assumed — the "doesn't shrink" defect B1a diagnosed was
    /// that the FLOOR's crossover point (where physRadius stops exceeding the floor and locks
    /// flat) sits at only ~1.5 AU for a G-class star, and even closer for dimmer M/K classes
    /// — so most of the 1-37+ AU range a player actually flies in was floor-locked at a
    /// constant pixel size regardless of distance. This is a real consequence of a realistic
    /// star's true angular size being only ~1-2px at typical in-system range under this
    /// camera's FOV, not a bug in the live-distance math itself. Measured crossover
    /// distances (StarMinPixels=1 — raising it to the brief's illustrative "~2px" was tried
    /// and measured to make things WORSE for common classes, see StarMinPixels' own comment):
    /// M-dwarf crosses over at ~0.23 AU, K at ~1.13 AU, G at ~1.49 AU — all effectively
    /// floor-locked by 37 AU, but each still shows a real (if modest, ~1.1-1.5x) shrink
    /// between 1 AU and 37 AU rather than none; a large O-class giant crosses over at
    /// ~22.5 AU, giving an 11x difference between 1 AU (22.5px) and 37 AU (1px) — giants read
    /// as giants over a materially wider range, though every class eventually converges to
    /// the same 1px floor far enough out. This asymmetry, and the small absolute pixel counts
    /// even at 1 AU for common classes, is exactly why Brief B1a's Fix 2 makes GLARE (not
    /// disc) carry the primary "how far away, and how bright" signal — see GlareOuterRadius.
    /// </summary>
    private float StarApparentRadius(Vector3 renderPos, double radiusMeters)
    {
        float physRadius = (float)(radiusMeters * Camera3D.RenderScale);
        float dist        = renderPos.Length();
        if (dist < 0.001f) return physRadius;

        // projScale converts render-space size at unit distance to screen pixels.
        // For a symmetric frustum: projScale = screenHeight / (2 * tan(halfFov))
        float projScale = _gd.Viewport.Height
                        / (2f * MathF.Tan(MathHelper.ToRadians(60f))); // half of 60°

        float minRenderRadius = StarMinPixels * dist / projScale;
        return System.Math.Max(physRadius, minRenderRadius);
    }

    public void DrawAtmosphere(Camera3D camera, OrbitalBody body, DVec3 universePos, DetailLevel level)
    {
        if (body.AtmosphereType == AtmosphereType.None || body.AtmosphereHeight <= 0) return;
        if (_atmosEffect == null) return;

        Vector3 renderPos = camera.ToRenderSpace(universePos);
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
        var     right = camera.Right;
        var     up    = camera.Up;
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

        _atmosEffect.Parameters["ViewProjection"].SetValue(_effect.View * _effect.Projection);
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

    public void DrawOrbitRings(Camera3D camera, Matrix eclipticRotation, double gameTimeSeconds, DetailLevel level)
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
            _effect.World = Matrix.CreateScale(ringRadius) * eclipticRotation;
            _ringPrimitive.Draw(_gd, _effect, col);

            // Moon orbit rings — centred on the planet's tilted position
            if (planet.Children.Count > 0)
            {
                DVec3 planetUniverse = _eclipticToGalaxy(planet.GetPosition(gameTimeSeconds, DVec3.Zero));
                Vector3 planetRender = camera.ToRenderSpace(planetUniverse);

                foreach (var moon in planet.Children)
                {
                    float moonRingR = (float)(moon.OrbitalRadius * Camera3D.RenderScale);
                    if (moonRingR < 0.01f) continue;

                    // Scale → tilt → translate to planet position
                    _effect.World = Matrix.CreateScale(moonRingR)
                                  * eclipticRotation
                                  * Matrix.CreateTranslation(planetRender);

                    _ringPrimitive.Draw(_gd, _effect, new Color(20, 28, 44, 140));
                }
            }
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

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
