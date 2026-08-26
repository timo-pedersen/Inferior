using System.Diagnostics;
using System.Security.Cryptography;
using Inferior.Core.Random;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen.Megastations;

public enum SystemMaterialFamilyId
{
    DullStructuralMetal,
    PaintedCoatedMetal,
    HeavyIndustrialPlate,
    CleanTechnicalAlloy,
}

public enum SystemMaterialTintPolicy
{
    DominantStructural,
    ColourBearing,
    Industrial,
    TechnicalAccent,
}

public sealed record SystemMaterialGenerationParameters(
    byte NeutralAlbedo,
    float NoiseAmplitude,
    int PanelColumns,
    int PanelRows,
    int SeamWidthPixels,
    float SeamDarkening,
    float PanelVariation,
    float HeightAmplitude,
    float BaseGloss,
    float GlossVariation);

public sealed record SystemMaterialRecipe(
    SystemMaterialFamilyId FamilyId,
    int GeneratorVersion,
    int TextureSize,
    float TileSizeMeters,
    float SpecularStrength,
    float SpecularShininess,
    float BumpStrength,
    SystemMaterialGenerationParameters Generation,
    SystemMaterialTintPolicy TintPolicy,
    float Wear);

public sealed record SystemMaterialCpuResource(
    SystemMaterialRecipe Recipe,
    Color[] Albedo,
    Color[] MaterialMap,
    Color TintBasis,
    string PixelSignature);

public readonly record struct SystemMaterialAssignmentContext(
    int LibrarySeed,
    Color DominantTintBasis,
    Color SecondaryTintBasis,
    Color AccentTintBasis,
    string LibrarySignature);

public sealed record SystemMaterialStationPalette(
    Color DominantTint,
    Color SecondaryTint,
    Color AccentTint,
    string Signature);

public readonly record struct SystemMaterialBinding(
    SystemMaterialFamilyId FamilyId,
    Color Tint);

public sealed record SystemMaterialDrawRange(
    SystemMaterialFamilyId FamilyId,
    int StartIndex,
    int IndexCount)
{
    public int TriangleCount => IndexCount / 3;
}

public sealed record SystemMaterialMeshCpuData(
    StationMeshCpuData Mesh,
    IReadOnlyList<SystemMaterialDrawRange> Ranges);

public sealed record MegastationSystemMaterialDiagnostics(
    SystemMaterialStationPalette Palette,
    IReadOnlyDictionary<SystemMaterialFamilyId, int> StructuralTriangles,
    IReadOnlyDictionary<SystemMaterialFamilyId, int> FabricTriangles,
    int StructuralRangeCount,
    int FabricRangeCount);

/// <summary>
/// Reusable, graphics-free operations shared by ordinary station texture generation and
/// system material generation. Keeping these primitives below both callers avoids routing
/// system materials through economy, age, surface-category, variant, or wear concepts.
/// </summary>
internal static class ProceduralMaterialCpuGenerator
{
    public static float PixelNoise01(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    public static float PixelNoise01(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    public static Color ShiftLuminance(Color colour, float delta)
        => new(
            Math.Clamp(colour.R + (int)delta, 0, 255),
            Math.Clamp(colour.G + (int)delta, 0, 255),
            Math.Clamp(colour.B + (int)delta, 0, 255));

    public static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    public static Color PackMaterial(float height, float gloss)
        => new(
            Math.Clamp((int)MathF.Round(height * 255f), 0, 255),
            Math.Clamp((int)MathF.Round(gloss * 255f), 0, 255),
            0,
            255);
}

public static class SystemMaterialRecipes
{
    public const int LibraryVersion = 1;
    public const int TextureSize = 512;

    private static readonly IReadOnlyDictionary<SystemMaterialFamilyId, SystemMaterialRecipe> AllRecipes =
        new Dictionary<SystemMaterialFamilyId, SystemMaterialRecipe>
        {
            [SystemMaterialFamilyId.DullStructuralMetal] = new(
                SystemMaterialFamilyId.DullStructuralMetal, LibraryVersion, TextureSize,
                TileSizeMeters: 16f, SpecularStrength: .16f, SpecularShininess: 18f,
                BumpStrength: .22f,
                new(224, .045f, 6, 5, 2, .11f, .055f, .075f, .58f, .08f),
                SystemMaterialTintPolicy.DominantStructural, Wear: 0f),
            [SystemMaterialFamilyId.PaintedCoatedMetal] = new(
                SystemMaterialFamilyId.PaintedCoatedMetal, LibraryVersion, TextureSize,
                TileSizeMeters: 6f, SpecularStrength: .24f, SpecularShininess: 28f,
                BumpStrength: .10f,
                new(242, .022f, 4, 4, 1, .045f, .025f, .025f, .70f, .045f),
                SystemMaterialTintPolicy.ColourBearing, Wear: 0f),
            [SystemMaterialFamilyId.HeavyIndustrialPlate] = new(
                SystemMaterialFamilyId.HeavyIndustrialPlate, LibraryVersion, TextureSize,
                TileSizeMeters: 10f, SpecularStrength: .34f, SpecularShininess: 32f,
                BumpStrength: .32f,
                new(210, .055f, 8, 7, 3, .18f, .075f, .13f, .61f, .11f),
                SystemMaterialTintPolicy.Industrial, Wear: 0f),
            [SystemMaterialFamilyId.CleanTechnicalAlloy] = new(
                SystemMaterialFamilyId.CleanTechnicalAlloy, LibraryVersion, TextureSize,
                TileSizeMeters: 8f, SpecularStrength: .48f, SpecularShininess: 72f,
                BumpStrength: .14f,
                new(248, .018f, 5, 5, 1, .035f, .018f, .035f, .82f, .035f),
                SystemMaterialTintPolicy.TechnicalAccent, Wear: 0f),
        };

    public static IReadOnlyList<SystemMaterialRecipe> All { get; } =
        Enum.GetValues<SystemMaterialFamilyId>().Select(Get).ToArray();

    public static SystemMaterialRecipe Get(SystemMaterialFamilyId family)
        => AllRecipes[family];
}

public static class SystemMaterialCpuLibraryGenerator
{
    public static IReadOnlyList<SystemMaterialCpuResource> Generate(
        int systemSeed,
        CancellationToken cancellationToken = default)
    {
        var resources = new List<SystemMaterialCpuResource>(SystemMaterialRecipes.All.Count);
        foreach (SystemMaterialRecipe recipe in SystemMaterialRecipes.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resources.Add(GenerateFamily(systemSeed, recipe.FamilyId, cancellationToken));
        }
        return resources;
    }

    public static SystemMaterialCpuResource GenerateFamily(
        int systemSeed,
        SystemMaterialFamilyId family,
        CancellationToken cancellationToken = default)
    {
        SeededRandom librarySeed = new SeededRandom(systemSeed).Derive("material-library:v1");
        (Color dominant, Color secondary, Color accent) = SystemTintBasis(librarySeed);
        SystemMaterialRecipe recipe = SystemMaterialRecipes.Get(family);
        int familySeed = librarySeed.Derive(family.ToString()).Seed;
        (Color[] albedo, Color[] material) = GeneratePixels(recipe, familySeed, cancellationToken);
        Color tintBasis = recipe.TintPolicy switch
        {
            SystemMaterialTintPolicy.DominantStructural => dominant,
            SystemMaterialTintPolicy.ColourBearing => secondary,
            SystemMaterialTintPolicy.Industrial => ProceduralMaterialCpuGenerator.Blend(
                dominant, secondary, .48f),
            _ => accent,
        };
        return new(recipe, albedo, material, tintBasis,
            Signature(recipe, albedo, material));
    }

    public static SystemMaterialAssignmentContext CreateAssignmentContext(
        int systemSeed,
        IReadOnlyList<SystemMaterialCpuResource>? resources = null)
    {
        SeededRandom librarySeed = new SeededRandom(systemSeed).Derive("material-library:v1");
        (Color dominant, Color secondary, Color accent) = SystemTintBasis(librarySeed);
        string signature = resources == null
            ? $"seed:{unchecked((uint)librarySeed.Seed):X8}"
            : CombinedSignature(resources);
        return new(librarySeed.Seed, dominant, secondary, accent, signature);
    }

    private static (Color[] Albedo, Color[] Material) GeneratePixels(
        SystemMaterialRecipe recipe,
        int seed,
        CancellationToken cancellationToken)
    {
        int size = recipe.TextureSize;
        var albedo = new Color[size * size];
        var material = new Color[size * size];
        SystemMaterialGenerationParameters p = recipe.Generation;
        int panelWidth = size / p.PanelColumns;
        int panelHeight = size / p.PanelRows;
        int phaseX = PositiveMod(new SeededRandom(seed).Derive("panel-phase-x").Seed, panelWidth);
        int phaseY = PositiveMod(new SeededRandom(seed).Derive("panel-phase-y").Seed, panelHeight);
        int noiseSeed = new SeededRandom(seed).Derive("pixel-character").Seed;

        for (int y = 0; y < size; y++)
        {
            if ((y & 31) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int localY = PositiveMod(y + phaseY, panelHeight);
            int cellY = (y + phaseY) / panelHeight;
            for (int x = 0; x < size; x++)
            {
                int localX = PositiveMod(x + phaseX, panelWidth);
                int cellX = (x + phaseX) / panelWidth;
                float fine = ProceduralMaterialCpuGenerator.PixelNoise01(x, y, noiseSeed) * 2f - 1f;
                float broad = ProceduralMaterialCpuGenerator.PixelNoise01(x / 16, y / 16, noiseSeed ^ 0x4C4F5746) * 2f - 1f;
                float cell = ProceduralMaterialCpuGenerator.PixelNoise01(cellX, cellY, seed ^ 0x50414E4C) * 2f - 1f;
                bool seam = localX < p.SeamWidthPixels || localY < p.SeamWidthPixels;
                float luminance = p.NeutralAlbedo
                    + 255f * (fine * p.NoiseAmplitude + broad * p.NoiseAmplitude * .35f
                        + cell * p.PanelVariation);
                if (seam)
                    luminance *= 1f - p.SeamDarkening;
                byte value = (byte)Math.Clamp((int)MathF.Round(luminance), 0, 255);
                albedo[y * size + x] = new Color((int)value, value, value, 255);

                float height = .5f + cell * p.HeightAmplitude * .35f
                    + broad * p.HeightAmplitude * .12f;
                if (seam)
                    height -= p.HeightAmplitude;
                float gloss = p.BaseGloss + fine * p.GlossVariation
                    + cell * p.GlossVariation * .35f;
                if (seam)
                    gloss *= .72f;
                material[y * size + x] = ProceduralMaterialCpuGenerator.PackMaterial(
                    Math.Clamp(height, 0f, 1f), Math.Clamp(gloss, 0f, 1f));
            }
        }
        return (albedo, material);
    }

    private static (Color Dominant, Color Secondary, Color Accent) SystemTintBasis(SeededRandom seed)
    {
        (Color dominant, Color secondary, Color accent)[] palettes =
        [
            (new(150, 166, 178), new(103, 125, 142), new(190, 205, 211)),
            (new(156, 158, 158), new(104, 111, 116), new(202, 200, 190)),
            (new(112, 119, 124), new(72, 79, 84), new(166, 178, 183)),
            (new(182, 177, 161), new(122, 103, 82), new(211, 199, 172)),
            (new(139, 153, 142), new(88, 108, 96), new(187, 195, 180)),
            (new(171, 169, 160), new(119, 125, 132), new(205, 213, 218)),
        ];
        int index = PositiveMod(seed.Derive("tint-basis").Seed, palettes.Length);
        return palettes[index];
    }

    internal static string CombinedSignature(IReadOnlyList<SystemMaterialCpuResource> resources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (SystemMaterialCpuResource resource in resources.OrderBy(r => r.Recipe.FamilyId))
            hash.AppendData(Convert.FromHexString(resource.PixelSignature));
        return Convert.ToHexString(hash.GetHashAndReset())[..16];
    }

    private static string Signature(
        SystemMaterialRecipe recipe,
        Color[] albedo,
        Color[] material)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes((int)recipe.FamilyId));
        hash.AppendData(BitConverter.GetBytes(recipe.GeneratorVersion));
        hash.AppendData(BitConverter.GetBytes(recipe.TileSizeMeters));
        foreach (Color colour in albedo)
            hash.AppendData(BitConverter.GetBytes(colour.PackedValue));
        foreach (Color colour in material)
            hash.AppendData(BitConverter.GetBytes(colour.PackedValue));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int PositiveMod(int value, int modulus)
        => (int)((uint)value % (uint)modulus);
}

public sealed class MegastationSystemMaterialAssignment
{
    private readonly SystemMaterialAssignmentContext _context;
    private readonly int _stationSeed;

    private MegastationSystemMaterialAssignment(
        SystemMaterialAssignmentContext context,
        int stationSeed,
        SystemMaterialStationPalette palette)
    {
        _context = context;
        _stationSeed = stationSeed;
        Palette = palette;
    }

    public SystemMaterialStationPalette Palette { get; }

    public static MegastationSystemMaterialAssignment Create(
        SystemMaterialAssignmentContext context,
        string stationIdentity)
    {
        int stationSeed = new SeededRandom(context.LibrarySeed)
            .Derive("material-assignment:v1")
            .Derive(stationIdentity)
            .Seed;
        var rng = new SeededRandom(stationSeed);
        Color dominant = Jitter(context.DominantTintBasis, rng.Derive("dominant"), 12);
        Color secondary = Jitter(context.SecondaryTintBasis, rng.Derive("secondary"), 15);
        Color accent = Jitter(context.AccentTintBasis, rng.Derive("accent"), 10);
        string signature = $"{unchecked((uint)stationSeed):X8}:" +
            $"{dominant.PackedValue:X8}:{secondary.PackedValue:X8}:{accent.PackedValue:X8}";
        return new(context, stationSeed,
            new SystemMaterialStationPalette(dominant, secondary, accent, signature));
    }

    public SystemMaterialBinding StructuralBinding(MegastationSemanticZone zone)
    {
        int roll = PositiveMod(new SeededRandom(_stationSeed)
            .Derive("structural-zone")
            .Derive(zone.Identity).Seed, 100);
        SystemMaterialFamilyId family = zone.Role switch
        {
            MegastationZoneRole.Industrial => roll < 55
                ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Utilities => roll < 45
                ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : roll < 62 ? SystemMaterialFamilyId.CleanTechnicalAlloy
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Habitation => roll < 22
                ? SystemMaterialFamilyId.PaintedCoatedMetal
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Logistics => roll < 28
                ? SystemMaterialFamilyId.PaintedCoatedMetal
                : roll < 42 ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Strategic => roll < 28
                ? SystemMaterialFamilyId.CleanTechnicalAlloy
                : roll < 42 ? SystemMaterialFamilyId.PaintedCoatedMetal
                : SystemMaterialFamilyId.DullStructuralMetal,
            _ => roll < 8
                ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : SystemMaterialFamilyId.DullStructuralMetal,
        };
        return new(family, TintFor(family));
    }

    public SystemMaterialBinding FabricBinding(MegastationFabricInstance instance)
    {
        int roll = PositiveMod(new SeededRandom(_stationSeed)
            .Derive("fabric-building")
            .Derive(instance.Identity).Seed, 100);
        SystemMaterialFamilyId family = instance.ZoneRole switch
        {
            MegastationZoneRole.Habitation => roll < 66
                ? SystemMaterialFamilyId.PaintedCoatedMetal
                : SystemMaterialFamilyId.CleanTechnicalAlloy,
            MegastationZoneRole.Industrial => roll < 68
                ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Logistics => roll < 56
                ? SystemMaterialFamilyId.PaintedCoatedMetal
                : SystemMaterialFamilyId.DullStructuralMetal,
            MegastationZoneRole.Utilities => roll < 62
                ? SystemMaterialFamilyId.HeavyIndustrialPlate
                : SystemMaterialFamilyId.CleanTechnicalAlloy,
            MegastationZoneRole.Strategic => roll < 64
                ? SystemMaterialFamilyId.CleanTechnicalAlloy
                : SystemMaterialFamilyId.PaintedCoatedMetal,
            _ => SystemMaterialFamilyId.DullStructuralMetal,
        };
        return new(family, TintFor(family));
    }

    public SystemMaterialBinding DefaultStructuralBinding
        => new(SystemMaterialFamilyId.DullStructuralMetal, Palette.DominantTint);

    private Color TintFor(SystemMaterialFamilyId family) => family switch
    {
        SystemMaterialFamilyId.DullStructuralMetal => Palette.DominantTint,
        SystemMaterialFamilyId.PaintedCoatedMetal => Palette.SecondaryTint,
        SystemMaterialFamilyId.HeavyIndustrialPlate => ProceduralMaterialCpuGenerator.Blend(
            Palette.DominantTint, Palette.SecondaryTint, .58f),
        _ => Palette.AccentTint,
    };

    private static Color Jitter(Color colour, SeededRandom seed, int magnitude)
    {
        int delta = seed.NextInt(-magnitude, magnitude);
        return ProceduralMaterialCpuGenerator.ShiftLuminance(colour, delta);
    }

    private static int PositiveMod(int value, int modulus)
        => (int)((uint)value % (uint)modulus);
}

public sealed class SystemMaterialResource : IDisposable
{
    private bool _disposed;

    internal SystemMaterialResource(
        SystemMaterialCpuResource cpu,
        Texture2D albedo,
        Texture2D materialMap)
    {
        Recipe = cpu.Recipe;
        TintBasis = cpu.TintBasis;
        PixelSignature = cpu.PixelSignature;
        Albedo = albedo;
        MaterialMap = materialMap;
    }

    public SystemMaterialRecipe Recipe { get; }
    public Color TintBasis { get; }
    public string PixelSignature { get; }
    public Texture2D Albedo { get; }
    public Texture2D MaterialMap { get; }
    internal bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Albedo.Dispose();
        MaterialMap.Dispose();
    }
}

public sealed record SystemMaterialLibraryDiagnostics(
    string SystemIdentity,
    int LibraryVersion,
    int FamilyCount,
    int TextureCount,
    int TextureSize,
    int SetDataCallCount,
    long UploadedBytes,
    double CpuGenerationMilliseconds,
    double TextureConstructionMilliseconds,
    double SetDataMilliseconds,
    double MaximumSetDataMilliseconds,
    double TotalMilliseconds,
    string Signature);

public sealed class SystemMaterialLibrary : IDisposable
{
    private readonly Dictionary<SystemMaterialFamilyId, SystemMaterialResource> _resources;
    private bool _disposed;

    private SystemMaterialLibrary(
        Dictionary<SystemMaterialFamilyId, SystemMaterialResource> resources,
        SystemMaterialAssignmentContext assignmentContext,
        SystemMaterialLibraryDiagnostics diagnostics)
    {
        _resources = resources;
        AssignmentContext = assignmentContext;
        Diagnostics = diagnostics;
    }

    public SystemMaterialAssignmentContext AssignmentContext { get; }
    public SystemMaterialLibraryDiagnostics Diagnostics { get; }
    public IReadOnlyDictionary<SystemMaterialFamilyId, SystemMaterialResource> Resources => _resources;
    internal bool IsDisposed => _disposed;

    public SystemMaterialResource Get(SystemMaterialFamilyId family)
        => _resources.TryGetValue(family, out SystemMaterialResource? resource)
            ? resource
            : throw new InvalidOperationException($"System material {family} is unavailable.");

    public static SystemMaterialLibrary Create(
        GraphicsDevice graphicsDevice,
        string systemIdentity,
        int systemSeed)
    {
        var total = Stopwatch.StartNew();
        var cpuTimer = Stopwatch.StartNew();
        IReadOnlyList<SystemMaterialCpuResource> cpu =
            SystemMaterialCpuLibraryGenerator.Generate(systemSeed);
        cpuTimer.Stop();
        var resources = new Dictionary<SystemMaterialFamilyId, SystemMaterialResource>();
        double constructionMs = 0, setDataMs = 0, maximumSetDataMs = 0;
        try
        {
            foreach (SystemMaterialCpuResource prepared in cpu)
            {
                Texture2D? albedo = null;
                Texture2D? material = null;
                try
                {
                    var constructorTimer = Stopwatch.StartNew();
                    albedo = new Texture2D(graphicsDevice, prepared.Recipe.TextureSize,
                        prepared.Recipe.TextureSize, false, SurfaceFormat.Color);
                    material = new Texture2D(graphicsDevice, prepared.Recipe.TextureSize,
                        prepared.Recipe.TextureSize, false, SurfaceFormat.Color);
                    constructorTimer.Stop();
                    constructionMs += constructorTimer.Elapsed.TotalMilliseconds;

                    Upload(albedo, prepared.Albedo);
                    Upload(material, prepared.MaterialMap);
                    resources.Add(prepared.Recipe.FamilyId,
                        new SystemMaterialResource(prepared, albedo, material));
                    albedo = null;
                    material = null;
                }
                finally
                {
                    albedo?.Dispose();
                    material?.Dispose();
                }
            }
        }
        catch
        {
            foreach (SystemMaterialResource resource in resources.Values)
                resource.Dispose();
            throw;
        }
        total.Stop();
        string signature = SystemMaterialCpuLibraryGenerator.CombinedSignature(cpu);
        var diagnostics = new SystemMaterialLibraryDiagnostics(
            systemIdentity,
            SystemMaterialRecipes.LibraryVersion,
            cpu.Count,
            cpu.Count * 2,
            SystemMaterialRecipes.TextureSize,
            cpu.Count * 2,
            (long)cpu.Count * 2 * SystemMaterialRecipes.TextureSize
                * SystemMaterialRecipes.TextureSize * 4,
            cpuTimer.Elapsed.TotalMilliseconds,
            constructionMs,
            setDataMs,
            maximumSetDataMs,
            total.Elapsed.TotalMilliseconds,
            signature);
        return new(resources,
            SystemMaterialCpuLibraryGenerator.CreateAssignmentContext(systemSeed, cpu),
            diagnostics);

        void Upload(Texture2D texture, Color[] pixels)
        {
            var timer = Stopwatch.StartNew();
            texture.SetData(pixels);
            timer.Stop();
            double elapsed = timer.Elapsed.TotalMilliseconds;
            setDataMs += elapsed;
            maximumSetDataMs = Math.Max(maximumSetDataMs, elapsed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (SystemMaterialResource resource in _resources.Values)
            resource.Dispose();
        _resources.Clear();
    }
}
