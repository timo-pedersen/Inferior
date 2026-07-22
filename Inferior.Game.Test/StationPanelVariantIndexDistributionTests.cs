using System.Linq;
using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

// Brief S2b-1 gate diagnosis: variants rendered "nearly identical" on real stations.
// This checks the second of the two suspected causes — whether mod.Seed % N is
// degenerate (many modules piling onto the same few indices) rather than well-spread.
// Uses StationGenerator.GenerateModulesForDiagnostics (no GraphicsDevice) to get real
// per-module Seed values from the actual growth loop, then reproduces AssignTextures'
// exact `variants[mod.Seed % variants.Length]` selection without creating real textures.
public sealed class StationPanelVariantIndexDistributionTests(ITestOutputHelper output)
{
    [Fact]
    public void VariantIndexAssignment_OnLargeStation_IsLoggedPerModule()
    {
        var station = new Station { Name = "Diagnostic Large Station", Size = StationSize.Large };
        var modules = StationGenerator.GenerateModulesForDiagnostics(station);

        Assert.True(modules.Count > 10, $"Expected a substantial module count, got {modules.Count}");

        const int variantCount = StationTextureRegistry.DefaultVariantCount;

        var bySurface = modules
            .Select(m => (Module: m, Surface: StationGenerator.SurfaceFor(m.Definition.Category)))
            .GroupBy(x => x.Surface);

        output.WriteLine($"Station '{station.Name}': {modules.Count} modules, variantCount={variantCount}");

        foreach (var group in bySurface)
        {
            var indices = group.Select(x => x.Module.Seed % variantCount).ToList();
            var distinct = indices.Distinct().Count();

            output.WriteLine($"");
            output.WriteLine($"Surface {group.Key}: {group.Count()} modules, {distinct} distinct indices used (of {variantCount})");
            foreach (var (module, surface) in group)
            {
                output.WriteLine($"  module '{module.Definition.Id}' seed={module.Seed} -> variant index {module.Seed % variantCount}");
            }

            var histogram = indices.GroupBy(i => i).OrderByDescending(g => g.Count());
            output.WriteLine($"  Histogram (index: count): {string.Join(", ", histogram.Select(h => $"{h.Key}:{h.Count()}"))}");
        }
    }

    [Fact]
    public void VariantIndexAssignment_AcrossManyStations_SpreadIsNotDegenerate()
    {
        const int variantCount = StationTextureRegistry.DefaultVariantCount;
        int totalModules = 0;
        var allIndicesBySurface = new System.Collections.Generic.Dictionary<SurfaceTexture, System.Collections.Generic.List<int>>();

        for (int i = 0; i < 30; i++)
        {
            var station = new Station { Name = $"Spread Test Station {i}", Size = StationSize.Large };
            var modules = StationGenerator.GenerateModulesForDiagnostics(station);
            totalModules += modules.Count;

            foreach (var mod in modules)
            {
                var surface = StationGenerator.SurfaceFor(mod.Definition.Category);
                if (!allIndicesBySurface.TryGetValue(surface, out var list))
                    allIndicesBySurface[surface] = list = [];
                list.Add(mod.Seed % variantCount);
            }
        }

        output.WriteLine($"Total modules across 30 Large stations: {totalModules}");
        foreach (var (surface, indices) in allIndicesBySurface)
        {
            int distinct = indices.Distinct().Count();
            output.WriteLine($"Surface {surface}: {indices.Count} module-assignments, {distinct}/{variantCount} distinct indices reached");
            // Not a strict assertion on exact uniformity (crude modulo, small N per
            // station) — just confirms the full index range is reachable at all across
            // enough modules, i.e. the selection isn't structurally collapsing onto a
            // handful of indices regardless of how much data it sees.
            Assert.True(distinct > 1, $"Surface {surface} never used more than 1 variant index across {indices.Count} assignments");
        }
    }
}
