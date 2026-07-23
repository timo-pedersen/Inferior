using System.Linq;
using Microsoft.Xna.Framework;
using Inferior.Game.StationGen;
using Xunit;

namespace Inferior.Game.Test;

// Brief S2b-1: StationTextureRegistry.RollVariantSeeds is the pure, GraphicsDevice-free
// half of per-station panel-variant generation (GenerateVariantSet does the actual GPU
// texture creation and isn't testable here) — same split as StationGenerator.FindDockingBay
// vs. the full Generate(), for the same reason.
public sealed class StationPanelVariantTests
{
    [Fact]
    public void RollVariantSeeds_SamePersistenceId_ProducesSameSeeds()
    {
        var first  = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);
        var second = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RollVariantSeeds_DifferentPersistenceId_ProducesDifferentSeeds()
    {
        var a = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);
        var b = StationTextureRegistry.RollVariantSeeds("Sol:star:Beta Station", SurfaceTexture.IndustrialPanel);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RollVariantSeeds_DifferentSurface_ProducesDifferentSeeds()
    {
        // Same station, different surface — must not collapse onto the same stream
        // (each surface gets its own independent variant set even on one station).
        var industrial = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);
        var clean       = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.CleanPanel);

        Assert.NotEqual(industrial, clean);
    }

    [Fact]
    public void RollVariantSeeds_RespectsRequestedCount()
    {
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.TechPanel, count: 7);

        Assert.Equal(7, seeds.Length);
    }

    [Fact]
    public void RollVariantSeeds_DefaultCountMatchesDocumentedDefault()
    {
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.CargoPanel);

        Assert.Equal(StationTextureRegistry.DefaultVariantCount, seeds.Length);
    }

    private const float TestSpread = 0.35f; // arbitrary fixed value, decoupled from the
                                             // real StationEconomyVariance table so these
                                             // tests don't break if that's re-tuned later.

    private static TexturePalette SamplePalette() => new()
    {
        BaseColour       = new Color(120, 115, 108),
        AccentColour     = new Color(200, 140, 40),
        GrimeColour      = new Color(28, 22, 15),
        NoiseStrength    = 0.18f,
        SubPanelContrast = 0.16f,
        GrimeStrength    = 0.38f,
        NameFont         = FontStyle.Stencil,
        TextColour       = new Color(220, 180, 60),
    };

    // Regression test for the S2b-1 gate failure: the first cut of GenerateVariantSet
    // passed one shared TexturePalette to every variant, so all N converged to the same
    // mean colour ("no visible per-module difference" — index distribution was fine,
    // colour wasn't varying at all). OffsetPaletteForVariant is the fix; this asserts the
    // actual bug condition (BaseColour identical across variants) can't silently return.
    [Fact]
    public void OffsetPaletteForVariant_ProducesDifferentBaseColoursAcrossSeeds()
    {
        var basePalette = SamplePalette();
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);

        var baseColours = seeds
            .Select(seed => StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed, TestSpread).BaseColour)
            .Distinct()
            .Count();

        // Not asserting all N are distinct (HSV rounding can coincidentally collide) —
        // asserting the set isn't the degenerate case of "every variant identical."
        Assert.True(baseColours > 1,
            $"Expected varied BaseColour across {seeds.Length} variants, got {baseColours} distinct value(s)");
    }

    [Fact]
    public void OffsetPaletteForVariant_KeepsGrimeRelativeToBase()
    {
        // AccentColour/TextColour/the numeric wear knobs must pass through untouched —
        // this fix is scoped to "colour must move per variant" for BaseColour/GrimeColour
        // only (Report S2a: AccentColour has no reader in Generate() at all).
        var basePalette = SamplePalette();

        var variant = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 12345, TestSpread);

        Assert.Equal(basePalette.AccentColour, variant.AccentColour);
        Assert.Equal(basePalette.TextColour, variant.TextColour);
        Assert.Equal(basePalette.NoiseStrength, variant.NoiseStrength);
        Assert.Equal(basePalette.SubPanelContrast, variant.SubPanelContrast);
        Assert.Equal(basePalette.GrimeStrength, variant.GrimeStrength);
        Assert.Equal(basePalette.NameFont, variant.NameFont);
    }

    [Fact]
    public void OffsetPaletteForVariant_ZeroSpread_ProducesIdenticalBaseColour()
    {
        // Sanity check on the spread bound itself: spread=0 must mean "no variance" —
        // otherwise a very tight economy (e.g. Military at 0.08) couldn't be trusted to
        // actually read as cohesive.
        var basePalette = SamplePalette();
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);

        var baseColours = seeds
            .Select(seed => StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed, colourSpread: 0f).BaseColour)
            .Distinct()
            .Count();

        Assert.Equal(1, baseColours);
    }

    [Fact]
    public void OffsetPaletteForVariant_WiderSpread_ProducesMoreDistinctColours()
    {
        // The whole point of Brief S2b-2: a wider spread must actually read as more
        // varied than a narrow one, not just "different RNG draws that land similarly."
        var basePalette = SamplePalette();
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel, count: 40);

        int Distinctness(float spread) => seeds
            .Select(seed => StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed, spread).BaseColour)
            .Select(c => (c.R / 24, c.G / 24, c.B / 24)) // bucket — raw byte-equality is too strict a bar
            .Distinct()
            .Count();

        int narrow = Distinctness(StationEconomyVariance.Profiles[StationEconomy.Military].ColourSpread);
        int wide   = Distinctness(StationEconomyVariance.Profiles[StationEconomy.Independent].ColourSpread);

        Assert.True(wide > narrow, $"Expected Independent's wide spread ({wide} buckets) to beat Military's narrow spread ({narrow} buckets)");
    }

    [Theory]
    [InlineData(StationEconomy.Industrial)]
    [InlineData(StationEconomy.Commercial)]
    [InlineData(StationEconomy.Military)]
    [InlineData(StationEconomy.Scientific)]
    [InlineData(StationEconomy.Luxury)]
    [InlineData(StationEconomy.Agricultural)]
    [InlineData(StationEconomy.Independent)]
    public void StationEconomyVariance_HasAnEntryForEveryEconomy(StationEconomy economy)
    {
        // All 7 StationEconomy values are real and reachable (StationProfile.Generate's
        // NextInt(0, economyCount-1) is SeededRandom's [min,max] INCLUSIVE overload —
        // Independent (index 6) is not excluded; an earlier report claiming otherwise was
        // a misreading of NextInt's semantics, corrected in this brief). Every one needs a
        // variance-profile entry or AssignTextures throws a KeyNotFoundException the
        // first time that economy is actually rolled.
        Assert.True(StationEconomyVariance.Profiles.ContainsKey(economy));
    }

    [Fact]
    public void SelectVariantIndex_HighBaseShare_MostlyPicksDominantVariant()
    {
        int dominantCount = 0;
        const int trials = 500;
        for (int seed = 0; seed < trials; seed++)
            if (StationTextureRegistry.SelectVariantIndex(seed, variantCount: 20, baseShareRatio: 0.9f) == 0)
                dominantCount++;

        double fraction = dominantCount / (double)trials;
        Assert.True(fraction is > 0.8 and < 1.0,
            $"Expected ~90% dominant-variant picks at baseShareRatio=0.9, got {fraction:P0}");
    }

    [Fact]
    public void SelectVariantIndex_LowBaseShare_MostlySpreadsAcrossOthers()
    {
        int dominantCount = 0;
        const int trials = 500;
        for (int seed = 0; seed < trials; seed++)
            if (StationTextureRegistry.SelectVariantIndex(seed, variantCount: 20, baseShareRatio: 0.15f) == 0)
                dominantCount++;

        double fraction = dominantCount / (double)trials;
        Assert.True(fraction is > 0.0 and < 0.3,
            $"Expected ~15% dominant-variant picks at baseShareRatio=0.15, got {fraction:P0}");
    }

    [Fact]
    public void SelectVariantIndex_NonDominantPicks_SpreadAcrossAllOtherIndices()
    {
        var nonDominantIndices = Enumerable.Range(0, 2000)
            .Select(seed => StationTextureRegistry.SelectVariantIndex(seed, variantCount: 20, baseShareRatio: 0.15f))
            .Where(idx => idx != 0)
            .Distinct()
            .Count();

        // 19 non-dominant slots (1..19) — over 2000 seeds, all should be reachable.
        Assert.Equal(19, nonDominantIndices);
    }

    [Fact]
    public void SelectVariantIndex_SameSeed_IsDeterministic()
    {
        var first  = StationTextureRegistry.SelectVariantIndex(seed: 54321, variantCount: 20, baseShareRatio: 0.5f);
        var second = StationTextureRegistry.SelectVariantIndex(seed: 54321, variantCount: 20, baseShareRatio: 0.5f);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(200)]
    public void TexturePalette_From_OlderStationReadsGrimierThanFreshOne(int age)
    {
        // Brief S2b-2 item 4: StationProfile.Age was generated and unused before this
        // brief. Confirms it's actually read now — an old station of the same economy
        // must have >= GrimeStrength than a fresh one, strictly greater once age moves
        // off the 5-year floor.
        var fresh = TexturePalette.From(new StationProfile { Economy = StationEconomy.Industrial, Age = 5 });
        var older = TexturePalette.From(new StationProfile { Economy = StationEconomy.Industrial, Age = age });

        Assert.True(older.GrimeStrength >= fresh.GrimeStrength,
            $"Age {age}: expected GrimeStrength >= the age-5 baseline ({fresh.GrimeStrength}), got {older.GrimeStrength}");
        if (age > 5)
            Assert.True(older.GrimeStrength > fresh.GrimeStrength,
                $"Age {age}: expected strictly grimier than the age-5 baseline");
    }

    [Fact]
    public void TexturePalette_From_SameEconomySameAge_IsDeterministic()
    {
        var a = TexturePalette.From(new StationProfile { Economy = StationEconomy.Luxury, Age = 42 });
        var b = TexturePalette.From(new StationProfile { Economy = StationEconomy.Luxury, Age = 42 });

        Assert.Equal(a.GrimeStrength, b.GrimeStrength);
        Assert.Equal(a.BaseColour, b.BaseColour);
    }

    // Brief S2b-2 item 5: a science-block module must read sciency regardless of the
    // hosting station's own economy — this is the override, tested independent of any
    // GraphicsDevice/texture generation.
    [Theory]
    [InlineData(StationEconomy.Industrial)]
    [InlineData(StationEconomy.Military)]
    [InlineData(StationEconomy.Agricultural)]
    [InlineData(StationEconomy.Independent)]
    public void EconomyForModule_ScienceCategory_AlwaysResolvesToScientific(StationEconomy hostingEconomy)
    {
        Assert.Equal(StationEconomy.Scientific, StationGenerator.EconomyForModule("science", hostingEconomy));
    }

    [Theory]
    [InlineData("hab")]
    [InlineData("cargo")]
    [InlineData("industrial")]
    [InlineData("core")]
    [InlineData("military")]
    [InlineData("connector")]
    [InlineData("docking")]
    public void EconomyForModule_NonSpecialCategory_UsesHostingEconomy(string category)
    {
        Assert.Equal(StationEconomy.Agricultural, StationGenerator.EconomyForModule(category, StationEconomy.Agricultural));
    }

    // Brief S2c-1: per-variant base gloss rides OffsetPaletteForVariant's existing seeded
    // stream (not a new RNG convention) — same GraphicsDevice-free split as everything
    // else here; the actual wear-reduces-gloss pixel logic lives inside the private
    // Generate() (real Texture2D creation, needs a GPU) and isn't unit-testable directly,
    // same pre-existing limitation as the albedo pixel passes.
    [Fact]
    public void OffsetPaletteForVariant_SameSeed_ProducesSameBaseGloss()
    {
        var basePalette = SamplePalette();

        var a = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 777, TestSpread);
        var b = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 777, TestSpread);

        Assert.Equal(a.BaseGloss, b.BaseGloss);
    }

    [Fact]
    public void OffsetPaletteForVariant_DifferentSeeds_ProduceVariedBaseGloss()
    {
        var basePalette = SamplePalette();
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel, count: 20);

        var distinctGloss = seeds
            .Select(seed => StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed, TestSpread).BaseGloss)
            .Distinct()
            .Count();

        Assert.True(distinctGloss > 1,
            $"Expected varied BaseGloss across {seeds.Length} variants (fresh/glossy vs. duller), got {distinctGloss} distinct value(s)");
    }

    [Fact]
    public void OffsetPaletteForVariant_BaseGloss_StaysWithinDocumentedRange()
    {
        var basePalette = SamplePalette();
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel, count: 100);

        foreach (var seed in seeds)
        {
            float gloss = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed, TestSpread).BaseGloss;
            Assert.InRange(gloss, 0.4f, 1.0f);
        }
    }

    [Fact]
    public void OffsetPaletteForVariant_BaseGloss_IsIndependentOfColourSpread()
    {
        // Base gloss and colour spread are two different draws from the same stream —
        // changing colourSpread must not accidentally perturb which gloss value comes out
        // (that would make gloss variance secretly depend on economy spread tuning).
        var basePalette = SamplePalette();

        var narrow = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 42, colourSpread: 0.08f);
        var wide   = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 42, colourSpread: 0.90f);

        Assert.Equal(narrow.BaseGloss, wide.BaseGloss);
    }

    // Brief S2c-2: per-texel height (material map R channel) is computed inside the
    // private Generate() — same GraphicsDevice-free-testing limitation already accepted
    // for gloss above (real Texture2D creation needs a GPU, not available in this test
    // project). PixelNoise01 is the one new source of per-pixel variation S2c-2 adds
    // (the "irregular roughness" on oxidation patches) and the only piece of the new
    // height math that's a pure function testable in isolation — every other
    // height-writing pass either uses no RNG at all (seam grooves, a pure function of
    // grid line position) or rides an already-deterministic existing draw (sub-panel
    // height reuses ApplySubPanels' brightness-shift roll), so PixelNoise01's own
    // determinism is what closes the loop: same PersistenceId -> same variant seeds
    // (already covered by RollVariantSeeds' tests) -> same (x, y) noise lookups -> same
    // height, with no new nondeterministic input anywhere in the chain.
    [Fact]
    public void PixelNoise01_SameCoordinates_ProducesSameValue()
    {
        float a = StationTextureRegistry.PixelNoise01(137, 402);
        float b = StationTextureRegistry.PixelNoise01(137, 402);

        Assert.Equal(a, b);
    }

    [Fact]
    public void PixelNoise01_DifferentCoordinates_ProduceVariedValues()
    {
        var distinct = new HashSet<float>();
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            distinct.Add(StationTextureRegistry.PixelNoise01(x, y));

        Assert.True(distinct.Count > 100,
            $"Expected varied noise across a 32x32 coordinate grid, got {distinct.Count} distinct value(s)");
    }

    [Fact]
    public void PixelNoise01_StaysWithinUnitRange()
    {
        for (int y = 0; y < 64; y += 7)
        for (int x = 0; x < 64; x += 7)
            Assert.InRange(StationTextureRegistry.PixelNoise01(x, y), 0f, 1f);
    }
}
