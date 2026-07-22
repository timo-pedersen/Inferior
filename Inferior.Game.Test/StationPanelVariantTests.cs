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

    // Regression test for the S2b-1 gate failure: the first cut of GenerateVariantSet
    // passed one shared TexturePalette to every variant, so all N converged to the same
    // mean colour ("no visible per-module difference" — index distribution was fine,
    // colour wasn't varying at all). OffsetPaletteForVariant is the fix; this asserts the
    // actual bug condition (BaseColour identical across variants) can't silently return.
    [Fact]
    public void OffsetPaletteForVariant_ProducesDifferentBaseColoursAcrossSeeds()
    {
        var basePalette = new TexturePalette
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
        var seeds = StationTextureRegistry.RollVariantSeeds("Sol:star:Alpha Station", SurfaceTexture.IndustrialPanel);

        var baseColours = seeds
            .Select(seed => StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed).BaseColour)
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
        var basePalette = new TexturePalette
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

        var variant = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seed: 12345);

        Assert.Equal(basePalette.AccentColour, variant.AccentColour);
        Assert.Equal(basePalette.TextColour, variant.TextColour);
        Assert.Equal(basePalette.NoiseStrength, variant.NoiseStrength);
        Assert.Equal(basePalette.SubPanelContrast, variant.SubPanelContrast);
        Assert.Equal(basePalette.GrimeStrength, variant.GrimeStrength);
        Assert.Equal(basePalette.NameFont, variant.NameFont);
    }
}
