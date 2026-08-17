using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Core.Random;

namespace Inferior.Game.StationGen;

public static class StationTextureRegistry
{
    public sealed record TexturePixels(Color[] Albedo, Color[] Material);

    private static bool _initialized;

    public static Texture2D White { get; private set; } = null!;

    // ── Initialization ────────────────────────────────────────────────────────

    public static void Initialize(GraphicsDevice gd)
    {
        if (_initialized) return;
        _initialized = true;

        White = MakeFlat(gd, Color.White);
    }

    // ── Per-station panel variant sets (Brief S2b-1) ──────────────────────────
    // Replaces the old process-lifetime GetOrCreate/_cache (Report S2a §1/§6: shared
    // across every module of a (surface, economy) pair, galaxy-wide, ~24 textures total,
    // ever — mod.Seed threaded in and ignored by the cache key). Each station now owns
    // its own N-texture variant set per surface it actually uses. Prepared pixels are
    // uploaded on the render thread and the resident StationVisualPackage owns disposal;
    // these are NOT cached here.
    //
    // Gate-fix history: the first cut of this method passed the SAME TexturePalette to
    // every Generate() call — all N variants converged to the same mean colour, only
    // per-pixel noise/seam/streak *positions* differed (confirmed via the S2b-1 gate:
    // "no visible per-module difference"; ruled out mod.Seed % N degeneracy via
    // StationPanelVariantIndexDistributionTests — the index spread was fine, the colour
    // wasn't varying at all). OffsetPaletteForVariant below is the fix: each variant gets
    // its own hue/brightness/saturation-jittered palette, bounded by a colourSpread the
    // caller supplies (Brief S2b-2: StationEconomyVariance.Profiles, economy-keyed —
    // military tight, hippie/independent wide) instead of S2b-1's single fixed constant
    // for every station alike.
    public const int DefaultVariantCount = 20;

    // Independent salt from ContainerSeedRoot and any other per-station stream.
    private const int VariantSeedRoot = 0x50414E4C; // "PANL"

    /// <summary>
    /// Pure, GraphicsDevice-free: which seeds a station rolls for its N variants of one
    /// surface. Deterministic per station identity (PersistenceId, not station name —
    /// string.GetHashCode() is process-randomized in .NET, forbidden by
    /// !invariants.md §6; SeededRandom.Derive uses a stable string hash instead). Same
    /// station ⇒ same seeds, every load. Exposed separately from GenerateVariantSet so
    /// determinism is testable without a live GraphicsDevice.
    /// </summary>
    public static int[] RollVariantSeeds(string persistenceId, SurfaceTexture surface, int count = DefaultVariantCount)
    {
        var rng = new SeededRandom(VariantSeedRoot).Derive(persistenceId).Derive(surface.ToString());
        var seeds = new int[count];
        for (int i = 0; i < count; i++)
            seeds[i] = rng.NextInt(0, int.MaxValue - 1);
        return seeds;
    }

    /// <summary>
    /// Builds this station's own N-texture variant set for one surface — each variant now
    /// a (albedo, material) pair (Brief S2c-1: material is the RGBA gloss/height carrier,
    /// same size as albedo, same UV). Not cached or shared — the caller owns every
    /// returned texture (both elements of both pair) and must dispose them all when the
    /// station unloads. The residency path folds them into its package disposal manifest.
    /// colourSpread bounds how far each variant's
    /// seeded colour offset may wander from palette.BaseColour (Brief S2b-2:
    /// StationEconomyVariance.Profiles, economy-keyed).
    /// </summary>
    public static (Texture2D Albedo, Texture2D Material)[] GenerateVariantSet(
        GraphicsDevice gd, SurfaceTexture surface, TexturePalette palette,
        string persistenceId, float colourSpread, int count = DefaultVariantCount)
    {
        var prepared = GenerateVariantPixels(
            surface, palette, persistenceId, colourSpread, count);
        var variants = new (Texture2D, Texture2D)[prepared.Length];
        for (int i = 0; i < prepared.Length; i++)
            variants[i] = Upload(gd, prepared[i]);
        return variants;
    }

    public static TexturePixels[] GenerateVariantPixels(
        SurfaceTexture surface,
        TexturePalette palette,
        string persistenceId,
        float colourSpread,
        int count = DefaultVariantCount,
        CancellationToken cancellationToken = default)
    {
        var seeds = RollVariantSeeds(persistenceId, surface, count);
        var variants = new TexturePixels[count];
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variantPalette = OffsetPaletteForVariant(palette, seeds[i], colourSpread);
            variants[i] = GeneratePixels(surface, variantPalette, seeds[i]);
        }
        return variants;
    }

    public static (Texture2D Albedo, Texture2D Material) Upload(
        GraphicsDevice gd,
        TexturePixels pixels)
    {
        var albedo = new Texture2D(gd, Size, Size);
        var material = new Texture2D(gd, Size, Size);
        try
        {
            albedo.SetData(pixels.Albedo);
            material.SetData(pixels.Material);
            return (albedo, material);
        }
        catch
        {
            albedo.Dispose();
            material.Dispose();
            throw;
        }
    }

    // Brief S2b-2 item 1: biases mod.Seed's variant pick so the dominant variant (index
    // 0) claims baseShareRatio of modules — cohesive stations (military/corporate) mostly
    // render one dominant variant with occasional outliers; individualistic ones
    // (hippie/independent) spread thin across many variants. Two independent seeded
    // draws from the same seed (different salts): the dominant/non-dominant decision
    // must not correlate with which non-dominant index gets picked, same salt discipline
    // as OffsetPaletteForVariant. Deterministic — same seed, same pick, every load.
    internal static int SelectVariantIndex(int seed, int variantCount, float baseShareRatio)
    {
        if (variantCount <= 1) return 0;
        var dominantRoll = new System.Random(seed ^ 0x424D5348); // "BMSH" — base-share roll salt
        if (dominantRoll.NextDouble() < baseShareRatio) return 0;
        // Derived semantic seeds are signed and may legitimately be negative. Preserve
        // the existing mapping for positive seeds while keeping the selected index valid.
        return 1 + (int)((uint)seed % (uint)(variantCount - 1));
    }

    // Builds a per-variant TexturePalette whose BaseColour/GrimeColour are hue-rotated
    // and brightness/saturation-jittered from the station's own palette — the SAME
    // rotation applied to both, so grime still reads as "dirt relative to this variant's
    // base" instead of a mismatched leftover hue. AccentColour/TextColour/the numeric
    // wear knobs are untouched: AccentColour has no reader inside Generate() at all
    // (Report S2a), and TextColour only tints the small military stencil fragments —
    // leaving both alone keeps this to exactly "colour must move per variant."
    // Independent RNG stream from Generate()'s own pixel-drawing rng (different salt),
    // so this offset is deterministic per variant seed without perturbing pixel layout.
    // internal, not private: StationPanelVariantTests asserts variants actually differ in
    // colour, not just position — the exact regression the S2b-1 gate-fix addressed.
    //
    // Brief P1 Fix A: BaseColour's HSV value is floored at MinVariantBaseValue (D-NovaTank —
    // Nova Anchorage's Independent-economy docking-bay rolled a variant whose brightnessDelta
    // crushed V to 0, producing a literal (0,0,0) BaseColour and a ~17-mean-luminance texture;
    // both the hull (PS_DynamicLit) and every decoration pass (PS_BakedColorLit) multiply
    // through that same bitmap, so a black variant reads as "no surface" rather than "a dark
    // module"). The floor applies to BaseColour only, not GrimeColour — grime is deliberately
    // near-black already by design (e.g. Industrial's GrimeColour has V≈0.11, already below
    // this floor), so flooring it too would brighten ordinary wear patches as a side effect
    // rather than only fixing the pathological all-black case. Hue/saturation spread are
    // untouched either way — the wide colour variance on high-spread economies (Independent,
    // Agricultural) is intentional and must survive.
    internal static TexturePalette OffsetPaletteForVariant(TexturePalette basePalette, int seed, float colourSpread)
    {
        var rng = new System.Random(seed ^ 0x484F4655); // "HOFU" — colour-offset salt
        float hueDeltaDegrees = ((float)rng.NextDouble() * 2f - 1f) * 180f * colourSpread;
        float saturationDelta = ((float)rng.NextDouble() * 2f - 1f) * 0.5f * colourSpread;
        float brightnessDelta = ((float)rng.NextDouble() * 2f - 1f) * 0.5f * colourSpread;
        // Brief S2c-1: per-variant base gloss (fresh/glossy vs. duller), before wear —
        // one more draw from this SAME seeded stream, not a new RNG convention. [0.4, 1.0)
        // so "duller" still reads as a material, never flat-zero from the base roll alone
        // (wear is what can actually drive a texel toward true matte).
        float baseGloss = 0.4f + (float)rng.NextDouble() * 0.6f;

        return new TexturePalette
        {
            BaseColour       = ApplyHsvOffset(basePalette.BaseColour, hueDeltaDegrees, saturationDelta, brightnessDelta, MinVariantBaseValue),
            AccentColour     = basePalette.AccentColour,
            GrimeColour      = ApplyHsvOffset(basePalette.GrimeColour, hueDeltaDegrees, saturationDelta, brightnessDelta),
            NoiseStrength    = basePalette.NoiseStrength,
            SubPanelContrast = basePalette.SubPanelContrast,
            GrimeStrength    = basePalette.GrimeStrength,
            NameFont         = basePalette.NameFont,
            TextColour       = basePalette.TextColour,
            BaseGloss        = baseGloss,
        };
    }

    // Brief P1 Fix A: the texture is a multiplier on everything sampled through it (hull
    // albedo, decoration albedo, and — via the material map's height/gloss channels —
    // specular and bump too), so its brightness sets the ceiling on how much of any of that
    // can ever reach the screen. At V=0 a module doesn't read as "a black module," it reads
    // as "a module with no surface." 0.15 sits in the brief's suggested 0.12-0.18 range: dark
    // enough that a floored module still reads as dramatically darker than a normal
    // neighbour, bright enough that panel grid/seams/wear/specular/bump still show through.
    internal const float MinVariantBaseValue = 0.15f;

    private static Color ApplyHsvOffset(
        Color c, float hueDeltaDegrees, float saturationDelta, float brightnessDelta, float minValue = 0f)
    {
        RgbToHsv(c, out float h, out float s, out float v);
        h = (h + hueDeltaDegrees + 360f) % 360f;
        s = Math.Clamp(s + saturationDelta, 0f, 1f);
        v = Math.Clamp(v + brightnessDelta, minValue, 1f);
        return HsvToRgb(h, s, v);
    }

    private static void RgbToHsv(Color c, out float h, out float s, out float v)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;

        v = max;
        s = max <= 0f ? 0f : delta / max;

        if (delta < 1e-6f) { h = 0f; return; }
        if (max == r)      h = 60f * (((g - b) / delta) % 6f);
        else if (max == g) h = 60f * (((b - r) / delta) + 2f);
        else               h = 60f * (((r - g) / delta) + 4f);
        if (h < 0f) h += 360f;
    }

    private static Color HsvToRgb(float h, float s, float v)
    {
        float chroma = v * s;
        float x = chroma * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - chroma;

        (float r, float g, float b) = h switch
        {
            < 60f  => (chroma, x, 0f),
            < 120f => (x, chroma, 0f),
            < 180f => (0f, chroma, x),
            < 240f => (0f, x, chroma),
            < 300f => (x, 0f, chroma),
            _      => (chroma, 0f, x),
        };

        return new Color(
            (byte)Math.Clamp((r + m) * 255f, 0f, 255f),
            (byte)Math.Clamp((g + m) * 255f, 0f, 255f),
            (byte)Math.Clamp((b + m) * 255f, 0f, 255f));
    }

    // ── Texture generation pipeline ───────────────────────────────────────────

    private const int Size = 512;

    // Brief S2c-1/S2c-2: R=height (mid-value = flat reference plane; below = recessed,
    // above = raised — S2c-2), G=gloss (S2c-1), B/A reserved, written 0/255.
    private const byte MaterialNeutralHeight = 128;

    // Brief S2c-2 relief depths — eye-tuned starting points, not derived: structural
    // (seams/panels) reads as manufacturing joins, wear (scratches/oxidation) rides on
    // top of it. Streaks/edge-grime stay gloss-only (S2c-1's existing effect) — they're
    // surface residue, not relief, and adding height there didn't seem to earn its keep;
    // revisit if the in-engine gate wants more depth cues.
    private const float SeamGrooveDepth      = 0.15f;
    private const float SubPanelHeightRange  = 0.03f;
    private const float ScratchIncisionDepth = 0.08f;
    private const float OxidationRaiseAmount = 0.06f;
    private const float OxidationNoiseAmount = 0.03f;

    private static TexturePixels GeneratePixels(
        SurfaceTexture surface,
        TexturePalette palette,
        int            seed)
    {
        var pixels = new Color[Size * Size];
        var rng    = new System.Random(seed ^ HashPalette(palette, surface));

        // Brief S2c-1: gloss starts at this variant's rolled base and is only ever
        // reduced (never raised) by the GrimeStrength-driven wear passes below — same
        // coordinates/weights those passes already compute for the albedo darkening they
        // do, so "when a pass darkens albedo at a texel, it also lowers gloss there" is
        // literal, not a second, possibly-diverging computation.
        var gloss = new float[Size * Size];
        Array.Fill(gloss, palette.BaseGloss);

        // Brief S2c-2: height starts at the flat reference plane (same numeric neutral as
        // the old constant fill, so an untouched texel packs identically to before) and is
        // only ever displaced by the structural/wear passes below.
        var height = new float[Size * Size];
        Array.Fill(height, MaterialNeutralHeight / 255f);

        // Step 1 — base noise
        FillBaseNoise(pixels, palette, rng);

        // Step 2 + 3 — sub-panel grid with seam lines
        int[] gridX = BuildGrid(rng, Size, surface);
        int[] gridY = BuildGrid(rng, Size, surface);
        ApplySubPanels(pixels, height, gridX, gridY, palette, rng);
        ApplySeamLines(pixels, height, gridX, gridY, palette);

        // Step 4b — weathering streaks (before edge grime)
        if (palette.GrimeStrength > 0.15f)
        {
            int streakCount = 3 + (int)(palette.GrimeStrength * 12f);
            for (int s = 0; s < streakCount; s++)
            {
                int   sx        = rng.Next(Size);
                int   sy        = rng.Next(Size / 3);
                int   length    = Size / 3 + rng.Next(Size / 2);
                int   width     = 1 + rng.Next(3);
                float alpha     = 0.15f + (float)rng.NextDouble() * 0.30f;
                Color streakCol = BlendColor(palette.BaseColour, palette.GrimeColour, 0.85f);
                for (int dy = 0; dy < length; dy++)
                {
                    int drift = (int)(MathF.Sin(dy * 0.08f) * 1.5f);
                    for (int dx = -width; dx <= width; dx++)
                    {
                        int px = sx + dx + drift;
                        int py = sy + dy;
                        if (px < 0 || px >= Size || py < 0 || py >= Size) continue;
                        float fade = 1f - (float)dy / length;
                        int   idx  = py * Size + px;
                        float t    = alpha * fade;
                        pixels[idx] = BlendColor(pixels[idx], streakCol, t);
                        gloss[idx] *= 1f - t;
                    }
                }
            }
        }

        // Step 4c — oxidation patches
        if (palette.GrimeStrength > 0.40f)
        {
            int   patchCount = 2 + rng.Next(5);
            Color rustCol    = new Color(118, 72, 38);
            for (int p = 0; p < patchCount; p++)
            {
                int   cx    = rng.Next(Size);
                int   cy    = rng.Next(Size);
                float rx    = 15f + (float)rng.NextDouble() * 40f;
                float ry    = 8f  + (float)rng.NextDouble() * 25f;
                float alpha = 0.20f + (float)rng.NextDouble() * 0.35f;
                for (int y = Math.Max(0, cy - (int)ry); y < Math.Min(Size, cy + (int)ry); y++)
                for (int x = Math.Max(0, cx - (int)rx); x < Math.Min(Size, cx + (int)rx); x++)
                {
                    float ddx  = (x - cx) / rx;
                    float ddy  = (y - cy) / ry;
                    float dist = ddx * ddx + ddy * ddy;
                    if (dist > 1f) continue;
                    float fade = 1f - dist;
                    int   idx  = y * Size + x;
                    float t    = alpha * fade;
                    pixels[idx] = BlendColor(pixels[idx], rustCol, t);
                    gloss[idx] *= 1f - t;
                    // Irregular roughness: a coordinate hash, not another rng draw — an
                    // extra per-pixel System.Random consumption here would shift every
                    // subsequent draw's stream position (edge grime, scratches, stencil
                    // text), silently changing their output. PixelNoise01 is a stable
                    // function of (x, y) alone, so it costs nothing downstream.
                    float noise = (PixelNoise01(x, y) * 2f - 1f) * OxidationNoiseAmount;
                    height[idx] += (OxidationRaiseAmount + noise) * fade;
                }
            }
        }

        // Step 4 — edge grime
        ApplyEdgeGrime(pixels, gloss, gridX, gridY, palette);

        // Step 5a — scratch lines (high-wear surfaces only)
        if (palette.GrimeStrength > 0.25f)
            AddScratchLines(pixels, gloss, height, palette, rng);

        // Step 5b — military stencil fragments (Military economy only) — text is a
        // deliberate printed marking, not wear; doesn't touch gloss.
        if (palette.NameFont == FontStyle.Military)
        {
            string[] fragments   = ["A7", "R3", "SEC", "RESTRICTED", "ZN4", "06"];
            int      fragCount   = 2 + rng.Next(3);
            for (int f = 0; f < fragCount; f++)
            {
                string frag  = fragments[rng.Next(fragments.Length)];
                int    fx    = rng.Next(Size / 4) + (f % 2) * Size / 2;
                int    fy    = rng.Next(Size / 4) + (f / 2) * Size / 3;
                float  fa    = 0.25f + (float)rng.NextDouble() * 0.25f;
                TextPainter.DrawText(pixels, Size, Size, frag, fx, fy,
                    palette.TextColour, pixelScale: 3, alpha: fa);
            }
        }

        // Gate-fix (aliased/pixelated bump lines): every height pass above wrote a hard
        // 1-2px edge (seam grooves, sub-panel steps, scratch incisions) — a near-
        // discontinuous height step differentiates to a near-discontinuous gradient,
        // which the shader's derivative-bump amplifies straight into visible aliasing.
        // Blurring only height (not albedo/gloss — their own hard edges are a colour/
        // reflectance cue, not a relief one) ramps every edge over a few texels so
        // ddx/ddy sees a smooth slope instead of a step. Wrap-around, matching
        // MaterialSampler's own Wrap addressing.
        height = BlurHeight(height, radius: 2);

        var materialPixels = new Color[Size * Size];
        for (int i = 0; i < materialPixels.Length; i++)
        {
            byte h = (byte)Math.Clamp(MathF.Round(height[i] * 255f), 0f, 255f);
            byte g = (byte)Math.Clamp(MathF.Round(gloss[i] * 255f), 0f, 255f);
            materialPixels[i] = new Color(h, g, (byte)0, (byte)255);
        }
        return new TexturePixels(pixels, materialPixels);
    }

    // ── Pipeline steps ────────────────────────────────────────────────────────

    private static void FillBaseNoise(Color[] pixels, TexturePalette p, System.Random rng)
    {
        float ns = p.NoiseStrength * 255f;
        for (int i = 0; i < pixels.Length; i++)
        {
            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * ns;
            pixels[i] = ShiftLuminance(p.BaseColour, jitter);
        }
    }

    // Builds a sorted array of grid line positions across [0, size).
    // Panel count varies by surface type.
    private static int[] BuildGrid(System.Random rng, int size, SurfaceTexture surface)
    {
        int minCount = surface switch
        {
            SurfaceTexture.IndustrialPanel => 4,
            SurfaceTexture.CargoPanel      => 4,
            SurfaceTexture.TechPanel       => 7,
            _                              => 5,
        };
        int maxCount = minCount + 3;
        int count    = rng.Next(minCount, maxCount + 1);

        // Pick random split points, then sort them
        var lines = new List<int>(count);
        for (int i = 0; i < count; i++)
            lines.Add(rng.Next(24, size - 24));
        lines.Sort();

        // Merge lines that are too close together (< 24px apart)
        var merged = new List<int> { lines[0] };
        for (int i = 1; i < lines.Count; i++)
            if (lines[i] - merged[^1] >= 24)
                merged.Add(lines[i]);

        return [.. merged];
    }

    private static void ApplySubPanels(
        Color[]        pixels,
        float[]        height,
        int[]          gridX,
        int[]          gridY,
        TexturePalette p,
        System.Random  rng)
    {
        // For each cell defined by the grid, apply a slight uniform brightness offset.
        var xBounds = GetBounds(gridX, Size);
        var yBounds = GetBounds(gridY, Size);

        float contrast = p.SubPanelContrast * 255f;

        foreach (var (x0, x1) in xBounds)
        {
            foreach (var (y0, y1) in yBounds)
            {
                float shift = (float)(rng.NextDouble() * 2.0 - 1.0) * contrast;
                // Brief S2c-2: reuses this SAME draw (normalized back to [-1,1]) for a
                // per-panel height step instead of a fresh rng.NextDouble() call — an
                // extra draw here would shift the rng stream position for every pass that
                // runs after this one (seams, streaks, oxidation, scratches, stencil
                // text), changing their output even though none of their own logic
                // changed. Correlating height with the brightness roll is also a
                // reasonable reading: the same per-panel manufacturing variance plausibly
                // drives both.
                float heightOffset = contrast > 0.01f ? (shift / contrast) * SubPanelHeightRange : 0f;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int idx = y * Size + x;
                    pixels[idx] = ShiftLuminance(pixels[idx], shift);
                    height[idx] += heightOffset;
                }
            }
        }
    }

    // Returns (start, end) pairs for each cell between grid lines plus the edges.
    private static List<(int start, int end)> GetBounds(int[] lines, int size)
    {
        var result = new List<(int, int)>(lines.Length + 1);
        int prev = 0;
        foreach (int line in lines)
        {
            result.Add((prev, line));
            prev = line + 2;   // +2 to skip the seam itself
        }
        result.Add((prev, size));
        return result;
    }

    private static void ApplySeamLines(
        Color[]        pixels,
        float[]        height,
        int[]          gridX,
        int[]          gridY,
        TexturePalette p)
    {
        Color seamColor = TexturePalette.LerpColor(p.BaseColour, p.GrimeColour, 0.55f);

        foreach (int lx in gridX)
            for (int y = 0; y < Size; y++)
            {
                if (lx     < Size) { pixels[y * Size + lx    ] = seamColor; height[y * Size + lx    ] -= SeamGrooveDepth; }
                if (lx + 1 < Size) { pixels[y * Size + lx + 1] = BlendColor(pixels[y * Size + lx + 1], seamColor, 0.5f); height[y * Size + lx + 1] -= SeamGrooveDepth * 0.5f; }
            }

        foreach (int ly in gridY)
            for (int x = 0; x < Size; x++)
            {
                if (ly     < Size) { pixels[ly       * Size + x] = seamColor; height[ly       * Size + x] -= SeamGrooveDepth; }
                if (ly + 1 < Size) { pixels[(ly + 1) * Size + x] = BlendColor(pixels[(ly + 1) * Size + x], seamColor, 0.5f); height[(ly + 1) * Size + x] -= SeamGrooveDepth * 0.5f; }
            }
    }

    private static void ApplyEdgeGrime(
        Color[]        pixels,
        float[]        gloss,
        int[]          gridX,
        int[]          gridY,
        TexturePalette p)
    {
        if (p.GrimeStrength < 0.01f) return;

        // Build distance-to-nearest-seam maps, clamped to [0, falloff].
        const int falloff = 14;
        var distX = SeamDistance(gridX, Size, falloff);
        var distY = SeamDistance(gridY, Size, falloff);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int   nearest = Math.Min(distX[x], distY[y]);
            float t       = 1f - (nearest / (float)falloff);         // 1 at seam, 0 at falloff
            t = t * t * p.GrimeStrength;
            if (t < 0.005f) continue;
            int idx = y * Size + x;
            pixels[idx] = BlendColor(pixels[idx], p.GrimeColour, t);
            gloss[idx] *= 1f - t;
        }
    }

    // Precomputes min distance to any seam line, clamped to [0, falloff].
    private static int[] SeamDistance(int[] lines, int size, int falloff)
    {
        var dist = new int[size];
        for (int i = 0; i < size; i++) dist[i] = falloff;
        foreach (int line in lines)
            for (int d = 0; d <= falloff; d++)
            {
                if (line - d >= 0)   dist[line - d] = Math.Min(dist[line - d], d);
                if (line + d < size) dist[line + d] = Math.Min(dist[line + d], d);
            }
        return dist;
    }

    private static void AddScratchLines(Color[] pixels, float[] gloss, float[] height, TexturePalette p, System.Random rng)
    {
        int count = (int)(p.GrimeStrength * 18f) + 4;
        Color scratchColor = TexturePalette.LerpColor(p.BaseColour, Color.White, 0.3f);
        const float scratchWeight = 0.6f;

        for (int i = 0; i < count; i++)
        {
            int   x0    = rng.Next(Size);
            int   y0    = rng.Next(Size);
            float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
            int   len   = rng.Next(20, 90);

            float dx = MathF.Cos(angle);
            float dy = MathF.Sin(angle);

            for (int s = 0; s < len; s++)
            {
                int px = (int)(x0 + dx * s);
                int py = (int)(y0 + dy * s);
                if ((uint)px < Size && (uint)py < Size)
                {
                    int idx = py * Size + px;
                    pixels[idx] = BlendColor(pixels[idx], scratchColor, scratchWeight);
                    gloss[idx] *= 1f - scratchWeight;
                    height[idx] -= ScratchIncisionDepth;
                }
            }
        }
    }

    // Brief S2c-2: a stable, seed-free integer hash for per-pixel "irregular roughness"
    // noise — !invariants.md §6 forbids HashCode.Combine/GetHashCode for procedural
    // generation (process-randomized for strings), but this is a plain arithmetic
    // function of two ints, not an object hash, so it's exempt and always reproducible.
    internal static float PixelNoise01(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // Separable box blur, wrap-around (matches MaterialSampler's Wrap addressing — a
    // clamped blur would visibly seam at the UV wrap boundary). Flat regions (the vast
    // majority of the buffer) are unchanged by a box blur; only the structural/wear edges
    // this brief writes get smoothed.
    private static float[] BlurHeight(float[] height, int radius)
    {
        int windowSize = radius * 2 + 1;

        var horizontal = new float[height.Length];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float sum = 0f;
            for (int d = -radius; d <= radius; d++)
                sum += height[y * Size + (((x + d) % Size + Size) % Size)];
            horizontal[y * Size + x] = sum / windowSize;
        }

        var blurred = new float[height.Length];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float sum = 0f;
            for (int d = -radius; d <= radius; d++)
                sum += horizontal[(((y + d) % Size + Size) % Size) * Size + x];
            blurred[y * Size + x] = sum / windowSize;
        }
        return blurred;
    }

    // ── Colour helpers ────────────────────────────────────────────────────────

    private static Color ShiftLuminance(Color c, float delta)
    {
        return new Color(
            Math.Clamp(c.R + (int)delta, 0, 255),
            Math.Clamp(c.G + (int)delta, 0, 255),
            Math.Clamp(c.B + (int)delta, 0, 255));
    }

    private static Color BlendColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // ── RNG seed mixing ───────────────────────────────────────────────────────
    // No longer a cache key (the shared static cache is gone, Brief S2b-1) — still used
    // by Generate() to mix a rolled variant seed with the station-wide palette so two
    // different surfaces/palettes never collide onto the same RNG stream.
    private static int HashPalette(TexturePalette p, SurfaceTexture surface)
    {
        int h = 17;
        h = h * 31 + surface.GetHashCode();
        h = h * 31 + p.BaseColour.PackedValue.GetHashCode();
        h = h * 31 + p.AccentColour.PackedValue.GetHashCode();
        h = h * 31 + p.GrimeColour.PackedValue.GetHashCode();
        h = h * 31 + p.NoiseStrength.GetHashCode();
        h = h * 31 + p.SubPanelContrast.GetHashCode();
        h = h * 31 + p.GrimeStrength.GetHashCode();
        return h;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static Texture2D MakeFlat(GraphicsDevice gd, Color c)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { c });
        return tex;
    }
}
