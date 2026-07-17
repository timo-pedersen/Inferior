using Inferior.Game.StationGen;
using Inferior.Game.States;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public class StationShadowCasterMeshTests
{
    private static PlacedModule BuildModule(StationModuleDefinition definition, int seed)
        => new()
        {
            Definition   = definition,
            Transform    = Matrix.Identity,
            Seed         = seed,
            ChamferDepth = StationGenerator.ChamferDepthForSeed(seed),
        };

    // Regression test: BuildStationShadowCasterMeshes used to special-case
    // Category == "docking-bay" instead of the general "any MeshFactory module" condition,
    // so every other MeshFactory module (hab-block-octagonal, science-block-octagonal, ...)
    // silently got no hull caster while its decoration still composed — floating greeble
    // shadows with nothing underneath. TryGetMeshFactoryHullFaceRange is the exact decision
    // BuildStationShadowCasterMeshes uses to pick the hull caster's face range; this
    // exercises it directly against real octagonal modules, no GraphicsDevice required
    // (StationDecorator.Decorate is pure CPU-side geometry accumulation).
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OctagonalMeshFactoryModules_YieldNonEmptyHullFaceRange(int seed)
    {
        var hab     = BuildModule(StationModuleRegistry.HabBlockOctagonal, seed);
        var science = BuildModule(StationModuleRegistry.ScienceBlockOctagonal, seed * 7);

        StationDecorator.Decorate([hab, science]);

        foreach (var mod in new[] { hab, science })
        {
            Assert.NotEqual("docking-bay", mod.Definition.Category);
            Assert.True(
                SystemSpaceState.TryGetMeshFactoryHullFaceRange(mod, out int baseFaceCount),
                $"Expected a non-empty hull caster face range for '{mod.Definition.Id}' " +
                $"(category '{mod.Definition.Category}')");
            Assert.True(baseFaceCount > 0);

            // Same call the real caster composition makes — must also succeed, not just the
            // face-count check, since a bogus range (count > 0 but out of bounds) would
            // still fail here.
            var bounds = mod.Mesh!.ComputeFaceRangeBounds(0, baseFaceCount);
            Assert.True(bounds.HasValue);
        }
    }

    [Fact]
    public void BoxModule_IsNotRoutedThroughMeshFactoryHullPath()
    {
        // Sanity check on the other branch: an ordinary box module (MeshFactory == null) is
        // handled unconditionally by BuildHullMesh instead — a different code path entirely
        // — and must not report a MeshFactory hull face range.
        var box = BuildModule(StationModuleRegistry.HabBlock, 99);
        StationDecorator.Decorate([box]);

        Assert.False(SystemSpaceState.TryGetMeshFactoryHullFaceRange(box, out _));
    }
}
