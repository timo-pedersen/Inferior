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
    private const float PlanetMinPixels    = 1f;
    private const float PlanetMaxBoostDist = 4500f; // ~30 AU — no boost beyond this
    private const float PlanetVisualScale  = 1f;

    // Brief B1 Fix 1 / B1a Fix 2: the glare stack's per-layer relative SIZE (radius as a
    // fraction of the outermost, GlareLayer4Radius, since Fix 2 decoupled glare from disc
    // size entirely — see GlareOuterRadius/DrawStarGlow) — not live-tunable, only each
    // layer's ALPHA is (Brief B2 Fix 3, SunTuning.GlareLayer0-4Alpha; these consts are now
    // just each layer's DEFAULT alpha, read by SunTuning's own defaults, and are kept here as
    // the historical/documentation record of the relative-radius shape, which stays fixed).
    private const float GlareLayer0Radius = 1.1f; // white, innermost
    private const float GlareLayer1Radius = 2.5f; // coloured
    private const float GlareLayer2Radius = 6f;   // coloured
    private const float GlareLayer3Radius = 14f;  // coloured
    private const float GlareLayer4Radius = 30f;  // coloured, outermost — long falloff tail

    // Brief B1a Fix 2: glare size driven by the star's own apparent brightness
    // (Luminosity/distanceAU^2), NOT disc size — see DrawStarGlow/GlareOuterRadius. Brief B2
    // Fix 3 generalised the compression from a fixed sqrt to a live-tunable exponent
    // (SunTuning.GlareCompressionExponent, default 0.5 = sqrt, unchanged behaviour) and added
    // a live size multiplier (SunTuning.GlareSizeMultiplier) on top of this fixed baseline
    // scale. Reference: a Sol-like G star (Luminosity~1) at 1 AU with the default 0.5
    // exponent and 1x multiplier gives brightnessFactor=1, so GlareOuterScale IS the
    // outermost layer's radius in pixels at that reference point — chosen (500px) for a
    // dramatic near-approach halo per B1a's brief ("a small disc inside an enormous halo").
    // GlareFloorPixels/GlareMaxPixels are NOT in Brief B2's tunable-parameter list (only the
    // multiplier is) — they stay fixed safety/visibility bounds; the multiplier is applied
    // AFTER this clamp (Brief B2: "scales computed glare radius"), so it can push the
    // effective size above or below these reference bounds deliberately.
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

    /// <summary>Brief B2 Fix 3: readout data for the live tuning panel.</summary>
    public readonly record struct StarRenderMetrics(
        double DistanceAU, float DiscRadiusPixels, bool DiscFloorBound, float GlareOuterRadiusPixels);

    /// <summary>
    /// Brief B2 Fix 3: computes the SAME disc/glare metrics DrawStar/DrawStarGlow actually
    /// use, without drawing anything — the tuning panel's readout calls this once per frame
    /// so it reports exactly what the real formulas produce, not a second, independently
    /// re-derived copy (the exact "two copies of a formula silently drift apart" class of bug
    /// Brief D-SunSize found and traced to its root).
    /// </summary>
    public StarRenderMetrics GetStarMetrics(Camera3D camera, Star star)
    {
        Vector3 renderPos = camera.ToRenderSpace(DVec3.Zero);
        float   dist      = renderPos.Length();
        double  distAU    = dist / (Units.AU * Camera3D.RenderScale);

        float physRadius   = (float)(star.RadiusMeters * Camera3D.RenderScale);
        float discRadiusRU = StarApparentRadius(renderPos, star.RadiusMeters);
        bool  floorBound   = discRadiusRU > physRadius + 1e-6f;

        float glareRadiusRU = GlareOuterRadius(renderPos, star.Luminosity);

        float projScale = ProjScale();
        float discPx  = dist < 0.001f ? discRadiusRU  : discRadiusRU  * projScale / dist;
        float glarePx = dist < 0.001f ? glareRadiusRU : glareRadiusRU * projScale / dist;

        return new StarRenderMetrics(distAU, discPx, floorBound, glarePx);
    }

    // level is accepted but not yet used — planets/star already render as a single
    // cheap representation; no LOD variants exist yet.
    //
    // Brief B2 Fix 2: caller now draws this BEFORE DrawStarGlow (was after) — additive
    // blending only ever adds, so drawing the disc first means the glare's brightest, closest
    // layer can brighten the disc's own pixels toward white where they overlap; the old
    // opaque-disc-LAST ordering structurally prevented the core from ever reaching white (an
    // opaque draw can only replace what's under it, never add to it).
    public void DrawStar(Camera3D camera, Star star, DetailLevel level)
    {
        Vector3 renderPos = camera.ToRenderSpace(DVec3.Zero);
        float   radius    = StarApparentRadius(renderPos, star.RadiusMeters);

        // Star surface colour — white base tinted toward LightColor by a per-class factor.
        // Hot stars (O/B) stay near-white; cool stars (K/M) show clear yellow/orange/red.
        // This IS the limb colour, below — the sphere is the tinted base the overlay reveals
        // at the edge, not a separate "background".
        _gd.BlendState = BlendState.Opaque;
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = false;
        Color bodyColor = Color.Lerp(Color.White, star.LightColor, star.BodyTintStrength);
        DrawSphere(renderPos, radius, bodyColor, false);
        _effect.LightingEnabled = true;

        // Brief B2 Fix 2: limb-darkening overlay — the SAME shared white/Gaussian-alpha
        // texture the glare billboards use (_starGlowTex), drawn ALPHA-BLENDED (not additive)
        // directly on top of the tinted sphere at the disc's own radius. High alpha at centre
        // reads as saturated white; near-zero alpha at the edge lets the tinted sphere
        // underneath show through untouched — "saturated white at centre, falling to the
        // star-class tint toward the limb," with no shader and no second texture asset.
        // SunTuning.LimbDarkeningStrength scales the overlay's peak alpha; at 0 this is a
        // no-op (Color*0 has zero effect) and the disc is the flat tinted circle pre-B2
        // shipped. DepthRead (not Default) matches the glare billboards' own depth handling —
        // test against what's already there (so a foreground planet still occludes correctly)
        // without writing new depth from a flat billboard sitting at the sphere's centre.
        _gd.BlendState        = BlendState.AlphaBlend;
        _gd.DepthStencilState = DepthStencilState.DepthRead;
        _effect.TextureEnabled     = true;
        _effect.VertexColorEnabled = true;
        _effect.Texture            = _starGlowTex;
        _effect.World              = Matrix.Identity;
        DrawGlowBillboard(renderPos, radius, camera.Right, camera.Up,
            Color.White * SunTuning.LimbDarkeningStrength);
        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
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
        // of by disc size. Brief B2 Fix 3: each layer's alpha is now SunTuning's live value
        // (not the old fixed const), further scaled by GlareIntensityMultiplier uniformly —
        // "scales all layer alphas together" on top of each layer's own individual control.
        float intensity = SunTuning.GlareIntensityMultiplier;
        DrawGlowBillboard(renderPos, outerRU,                                          right, up, star.GlowColor * (SunTuning.GlareLayer4Alpha * intensity));
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer3Radius / GlareLayer4Radius), right, up, star.GlowColor * (SunTuning.GlareLayer3Alpha * intensity));
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer2Radius / GlareLayer4Radius), right, up, star.GlowColor * (SunTuning.GlareLayer2Alpha * intensity));
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer1Radius / GlareLayer4Radius), right, up, star.GlowColor * (SunTuning.GlareLayer1Alpha * intensity));
        DrawGlowBillboard(renderPos, outerRU * (GlareLayer0Radius / GlareLayer4Radius), right, up, Color.White    * (SunTuning.GlareLayer0Alpha * intensity));

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
    }

    /// <summary>
    /// Brief B1a Fix 2: the outermost glare layer's render-space radius, driven by the star's
    /// apparent brightness (Luminosity/distanceAU^2) rather than disc size — decoupled
    /// entirely from <see cref="StarApparentRadius"/>. Floored in PIXELS (converted to
    /// render-space via the same <see cref="ProjScale"/> technique StarApparentRadius uses,
    /// so it doesn't drift with resolution or FOV) so the sun always carries a halo distinctly
    /// larger than a background starfield point regardless of class or distance; capped as a
    /// safety net against extreme luminosity or extreme proximity.
    ///
    /// Brief B2 Fix 1: routed through the shared, corrected ProjScale() — this function's own
    /// inline copy had the SAME tan(60°) bug as StarApparentRadius's (copied from it in B1a,
    /// before the bug was known), so the glare's absolute pixel targets were likewise ~3x
    /// their stated values; now corrected, and now the panel (Fix 3) can retune GlareOuterScale
    /// against real numbers instead of bug-inflated ones.
    ///
    /// Brief B2 Fix 3: the compression exponent is now SunTuning.GlareCompressionExponent
    /// (default 0.5, reproducing the original fixed sqrt exactly: (L/d²)^0.5 = sqrt(L)/d), and
    /// the result is scaled by SunTuning.GlareSizeMultiplier AFTER the floor/cap clamp — "scales
    /// COMPUTED glare radius," a multiplier on the already-bounded value, not on the raw
    /// brightness ratio feeding into it.
    /// </summary>
    private float GlareOuterRadius(Vector3 renderPos, double luminosity)
    {
        float distRU = renderPos.Length();
        double distAU = distRU / (Units.AU * Camera3D.RenderScale);
        double ratio = luminosity > 0.0
            ? luminosity / (System.Math.Max(distAU, 0.001) * System.Math.Max(distAU, 0.001))
            : 0.0;
        double brightnessFactor = ratio > 0.0
            ? System.Math.Pow(ratio, SunTuning.GlareCompressionExponent)
            : 0.0;

        float outerPixels = System.Math.Clamp(
            GlareOuterScale * (float)brightnessFactor, GlareFloorPixels, GlareMaxPixels);
        outerPixels *= SunTuning.GlareSizeMultiplier;

        if (distRU < 0.001f) return outerPixels; // camera essentially at the star; pixels ~= RU here, degenerate case

        return outerPixels * distRU / ProjScale();
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
    /// Brief B2 Fix 1: single shared, correct projScale implementation — every caller (disc
    /// floor, glare floor, planet floor, and the tuning-panel readout) now goes through this
    /// ONE place instead of each duplicating (and risking re-diverging) the tan()/half-angle
    /// math. Camera3D's real vertical FOV is 60 degrees as the FULL angle (confirmed:
    /// Camera3D.SetProjection and SystemSpaceState's own per-pass CreatePerspectiveFieldOfView
    /// call both pass ToRadians(60f) directly to that API, whose first parameter is
    /// documented, MonoGame/XNA standard, as the full vertical FOV) — the half-angle for this
    /// formula is therefore 30 degrees, not 60. Brief D-SunSize found exactly this bug in the
    /// (now-deleted) duplicate inline copy inside StarApparentRadius/GlareOuterRadius: the
    /// comment there said "half of 60°" but the code used 60 directly instead of 30.
    /// PlanetApparentRadius already had this right; it's now just routed through here too.
    /// </summary>
    private float ProjScale() => _gd.Viewport.Height / (2f * MathF.Tan(MathHelper.ToRadians(30f)));

    /// <summary>
    /// Minimum render-space radius for a planet within boost range.
    /// Beyond PlanetMaxBoostDist the planet is left to shrink and vanish naturally.
    /// </summary>
    private float PlanetApparentRadius(OrbitalBody body, Vector3 renderPos)
    {
        float dist       = renderPos.Length();
        float baseRadius = VisualRadius(body);
        if (dist > PlanetMaxBoostDist) return baseRadius;

        float minRenderRadius = PlanetMinPixels * dist / ProjScale();
        return System.Math.Max(baseRadius, minRenderRadius);
    }

    /// <summary>
    /// Brief B1 Fix 3: true angular-size render-space radius derived from the star's actual
    /// RadiusMeters, replacing the old flat StarVisualRadius constant that rendered every
    /// star class at the same size (a red giant and a dwarf looked identical — D-Bright's
    /// own finding). Floored to a minimum screen size (<see cref="SunTuning.DiscFloorPixels"/>,
    /// Brief B2 Fix 3 — live-tunable, was the fixed StarMinPixels constant) so a distant or
    /// genuinely tiny star doesn't vanish — the floor is pixel-based (grows with distance),
    /// not a fixed render-space constant, so a small star up close still shows its true small
    /// size rather than being inflated to match a bigger class.
    ///
    /// Brief B2 Fix 1: this formula's OWN projScale used to duplicate <see cref="ProjScale"/>
    /// inline with a real bug (tan(60°) where the correct half-angle is tan(30°) — see
    /// ProjScale's own comment; Brief D-SunSize found and quantified it, exactly 3x). Now
    /// routed through the single shared, correct ProjScale() — physically-bound discs were
    /// ALREADY rendering correctly on screen even with the bug (this function returns
    /// physRadius untouched in that regime, and the real camera projection matrix was always
    /// correct), so this fix changes nothing visible there; it corrects the FLOOR's own
    /// world-radius (previously ~3x too large — a "1px" floor rendered at ~3px) and the
    /// CROSSOVER distance (previously 3x too close). Corrected crossovers, DiscFloorPixels=1:
    /// M-dwarf ~0.68 AU, K ~3.38 AU, G ~4.48 AU, O-giant ~67.4 AU (all exactly 3x the B1a-era
    /// figures) — meaning a giant now correctly still shows true physical size at 37 AU,
    /// where B1a's own (bug-affected) report had it floor-locked.
    /// </summary>
    private float StarApparentRadius(Vector3 renderPos, double radiusMeters)
    {
        float physRadius = (float)(radiusMeters * Camera3D.RenderScale);
        float dist        = renderPos.Length();
        if (dist < 0.001f) return physRadius;

        float minRenderRadius = SunTuning.DiscFloorPixels * dist / ProjScale();
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
