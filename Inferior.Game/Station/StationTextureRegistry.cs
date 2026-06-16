using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

public static class StationTextureRegistry
{
    // Type-level fallback registry (flat 1×1 placeholders, overridable via SetTexture).
    private static readonly Dictionary<SurfaceTexture, Texture2D> _textures = [];
    private static readonly Dictionary<SurfaceTexture, Vector3>   _colors   = [];
    private static bool _initialized;

    // Procedural texture cache: (surface type, palette hash) → generated 512×512.
    private static readonly Dictionary<(SurfaceTexture, int), Texture2D> _cache = [];

    public static Texture2D White { get; private set; } = null!;

    // ── Initialization ────────────────────────────────────────────────────────

    public static void Initialize(GraphicsDevice gd)
    {
        if (_initialized) return;
        _initialized = true;

        White = MakeFlat(gd, Color.White);

        // Flat placeholders — color values used for the base box DiffuseColor pass.
        Register(gd, SurfaceTexture.CleanPanel,      new Color(200, 195, 185));
        Register(gd, SurfaceTexture.TechPanel,       new Color(155, 165, 175));
        Register(gd, SurfaceTexture.IndustrialPanel, new Color(120, 115, 108));
        Register(gd, SurfaceTexture.CargoPanel,      new Color(148, 132, 108));
        Register(gd, SurfaceTexture.WornPanel,       new Color(130, 125, 118));
        _textures[SurfaceTexture.Glass] = White;
        _colors  [SurfaceTexture.Glass] = Vector3.One;
    }

    // ── Type-level accessors (fallback / base-box pass) ───────────────────────

    public static Texture2D Get(SurfaceTexture t)      => _textures[t];
    public static Vector3   GetColor(SurfaceTexture t) => _colors[t];

    internal static void SetTexture(SurfaceTexture st, Texture2D texture)
    {
        _textures[st] = texture;
    }

    // ── Procedural per-module texture ─────────────────────────────────────────

    /// Returns a cached 512×512 procedural texture for (surface, palette).
    /// Thread-safe only if called from the main thread (Texture2D creation requires GL context).
    public static Texture2D GetOrCreate(
        GraphicsDevice gd,
        SurfaceTexture surface,
        TexturePalette palette,
        int            seed)
    {
        int hash = HashPalette(palette, surface);
        if (_cache.TryGetValue((surface, hash), out var cached))
            return cached;

        var tex = Generate(gd, surface, palette, seed);
        _cache[(surface, hash)] = tex;
        return tex;
    }

    // ── Texture generation pipeline ───────────────────────────────────────────

    private const int Size = 512;

    private static Texture2D Generate(
        GraphicsDevice gd,
        SurfaceTexture surface,
        TexturePalette palette,
        int            seed)
    {
        var pixels = new Color[Size * Size];
        var rng    = new System.Random(seed ^ HashPalette(palette, surface));

        // Step 1 — base noise
        FillBaseNoise(pixels, palette, rng);

        // Step 2 + 3 — sub-panel grid with seam lines
        int[] gridX = BuildGrid(rng, Size, surface);
        int[] gridY = BuildGrid(rng, Size, surface);
        ApplySubPanels(pixels, gridX, gridY, palette, rng);
        ApplySeamLines(pixels, gridX, gridY, palette);

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
                        pixels[py * Size + px] = BlendColor(
                            pixels[py * Size + px], streakCol, alpha * fade);
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
                    pixels[y * Size + x] = BlendColor(
                        pixels[y * Size + x], rustCol, alpha * fade);
                }
            }
        }

        // Step 4 — edge grime
        ApplyEdgeGrime(pixels, gridX, gridY, palette);

        // Step 5a — scratch lines (high-wear surfaces only)
        if (palette.GrimeStrength > 0.25f)
            AddScratchLines(pixels, palette, rng);

        // Step 5b — military stencil fragments (Military economy only)
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

        var tex = new Texture2D(gd, Size, Size);
        tex.SetData(pixels);
        return tex;
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
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    pixels[y * Size + x] = ShiftLuminance(pixels[y * Size + x], shift);
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
        int[]          gridX,
        int[]          gridY,
        TexturePalette p)
    {
        Color seamColor = TexturePalette.LerpColor(p.BaseColour, p.GrimeColour, 0.55f);

        foreach (int lx in gridX)
            for (int y = 0; y < Size; y++)
            {
                if (lx     < Size) pixels[y * Size + lx    ] = seamColor;
                if (lx + 1 < Size) pixels[y * Size + lx + 1] = BlendColor(pixels[y * Size + lx + 1], seamColor, 0.5f);
            }

        foreach (int ly in gridY)
            for (int x = 0; x < Size; x++)
            {
                if (ly     < Size) pixels[ly       * Size + x] = seamColor;
                if (ly + 1 < Size) pixels[(ly + 1) * Size + x] = BlendColor(pixels[(ly + 1) * Size + x], seamColor, 0.5f);
            }
    }

    private static void ApplyEdgeGrime(
        Color[]        pixels,
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
            pixels[y * Size + x] = BlendColor(pixels[y * Size + x], p.GrimeColour, t);
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

    private static void AddScratchLines(Color[] pixels, TexturePalette p, System.Random rng)
    {
        int count = (int)(p.GrimeStrength * 18f) + 4;
        Color scratchColor = TexturePalette.LerpColor(p.BaseColour, Color.White, 0.3f);

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
                    pixels[py * Size + px] = BlendColor(pixels[py * Size + px], scratchColor, 0.6f);
            }
        }
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

    // ── Cache helpers ─────────────────────────────────────────────────────────

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

    private static void Register(GraphicsDevice gd, SurfaceTexture t, Color c)
    {
        _textures[t] = MakeFlat(gd, c);
        _colors[t]   = new Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    private static Texture2D MakeFlat(GraphicsDevice gd, Color c)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { c });
        return tex;
    }
}
