using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

// Brief P1 Fix A regression test. D-NovaTank measured Nova Anchorage's docking-bay rolling
// a variant whose brightness delta crushed HSV value to 0 — a literal (0,0,0) BaseColour
// and a ~17-mean-luminance texture, darkening both the hull (PS_DynamicLit) and every
// decoration pass (PS_BakedColorLit) that multiply through it. This station is deterministic
// (fixed galaxy generation + StarterSystemSelector), so this exact case can be pinned
// permanently: if OffsetPaletteForVariant's floor ever regresses, this fails immediately
// instead of waiting for another diagnostic chain to rediscover it.
public sealed class NovaAnchorageVariantFloorTests(ITestOutputHelper output)
{
    [Fact]
    public void NovaAnchorageDockingBay_VariantBaseColourAndTexture_StayAboveFloor()
    {
        Star star = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star;
        StarSystem system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        Station starter = StarterSystemSelector.SelectStarterStation(system.Stations)!;

        // Canary: if galaxy generation ever changes such that the starter station is no
        // longer Nova Anchorage, this assertion fails first and loudly, rather than this
        // test silently measuring the wrong station.
        Assert.Equal("Nova Anchorage", starter.Name);

        var bayDefinition = StationGenerator.FindDockingBay(starter);
        Assert.NotNull(bayDefinition);

        using var game = new HeadlessGpuGame();
        game.RunOneFrame();
        Assert.NotNull(game.Gd);
        var gd = game.Gd!;

        StationTextureRegistry.Initialize(gd);
        var result = StationGenerator.Generate(starter, gd);
        try
        {
            var bay = result.Modules.Find(m => m.Definition.Category == "docking-bay");
            Assert.NotNull(bay);

            var profile = StationProfile.Generate(
                StationGenerator.NameHash(starter.Name),
                starter.Size switch
                {
                    StationSize.Small  => StationScale.Outpost,
                    StationSize.Medium => StationScale.Station,
                    StationSize.Large  => StationScale.Port,
                    _                  => StationScale.Outpost,
                });
            var basePalette = TexturePalette.From(profile);
            var variance = StationEconomyVariance.Profiles[profile.Economy];

            var surface = StationGenerator.SurfaceFor(bay!.Definition.Category);
            var seeds = StationTextureRegistry.RollVariantSeeds(starter.PersistenceId!, surface);
            int idx = StationTextureRegistry.SelectVariantIndex(bay.Seed, seeds.Length, variance.BaseShareRatio);
            var variantPalette = StationTextureRegistry.OffsetPaletteForVariant(basePalette, seeds[idx], variance.ColourSpread);

            float baseColourValue = System.Math.Max(variantPalette.BaseColour.R,
                System.Math.Max(variantPalette.BaseColour.G, variantPalette.BaseColour.B)) / 255f;
            output.WriteLine($"Variant index {idx}/{seeds.Length}, post-offset BaseColour={variantPalette.BaseColour}, value={baseColourValue:F3}");
            Assert.True(baseColourValue >= StationTextureRegistry.MinVariantBaseValue - 0.001f,
                $"Expected variant BaseColour value >= floor ({StationTextureRegistry.MinVariantBaseValue}), got {baseColourValue:F3} ({variantPalette.BaseColour})");

            Assert.NotNull(bay.TextureInstance);
            var pixels = new Color[bay.TextureInstance!.Width * bay.TextureInstance.Height];
            bay.TextureInstance.GetData(pixels);
            double meanLum = 0;
            foreach (var p in pixels)
                meanLum += 0.3 * p.R + 0.59 * p.G + 0.11 * p.B;
            meanLum /= pixels.Length;
            output.WriteLine($"Texture mean luminance={meanLum:F1} (D-NovaTank pre-fix: 17.2)");

            // D-NovaTank measured 17.2 pre-fix — must be well above that now, not just
            // technically higher (a floor that barely moves the needle isn't a real fix).
            Assert.True(meanLum > 30.0,
                $"Expected Nova Anchorage bay texture mean luminance well above D-NovaTank's pre-fix 17.2, got {meanLum:F1}");
        }
        finally
        {
            foreach (var t in result.PanelTextures) t.Dispose();
        }
    }

    private sealed class HeadlessGpuGame : Microsoft.Xna.Framework.Game
    {
        private readonly GraphicsDeviceManager _graphics;
        public GraphicsDevice? Gd { get; private set; }

        public HeadlessGpuGame()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 64,
                PreferredBackBufferHeight = 64,
                SynchronizeWithVerticalRetrace = false,
            };
            IsFixedTimeStep = false;
            IsMouseVisible = false;
        }

        protected override void Initialize()
        {
            Window.AllowUserResizing = false;
            Window.IsBorderless = true;
            Window.Position = new Point(-32000, -32000);
            base.Initialize();
            Gd = base.GraphicsDevice;
        }

        protected override void Draw(GameTime gameTime)
        {
            Exit();
        }
    }
}
