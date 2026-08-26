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

    // Regression test: station shadow preparation used to special-case
    // Category == "docking-bay" instead of the general "any MeshFactory module" condition,
    // so every other MeshFactory module (hab-block-octagonal, science-block-octagonal, ...)
    // silently got no hull caster while its decoration still composed — floating greeble
    // shadows with nothing underneath. HasMeshFactoryHull is the exact decision
    // the upload plan uses to pick the hull caster; this exercises it
    // directly against real octagonal modules, no GraphicsDevice required
    // (StationDecorator.Decorate is pure CPU-side geometry accumulation). Brief U1: reads
    // mod.HullMesh (a separate mesh) instead of a face range within mod.Mesh — same
    // regression coverage, updated API shape.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OctagonalMeshFactoryModules_YieldNonEmptyHullMesh(int seed)
    {
        var hab     = BuildModule(StationModuleRegistry.HabBlockOctagonal, seed);
        var science = BuildModule(StationModuleRegistry.ScienceBlockOctagonal, seed * 7);

        StationDecorator.Decorate([hab, science]);

        foreach (var mod in new[] { hab, science })
        {
            Assert.NotEqual("docking-bay", mod.Definition.Category);
            Assert.True(
                SystemSpaceState.HasMeshFactoryHull(mod),
                $"Expected a non-empty hull mesh for '{mod.Definition.Id}' " +
                $"(category '{mod.Definition.Category}')");

            // Same call the real caster composition makes — must also succeed, not just the
            // presence check, since a bogus/empty mesh would still fail here.
            var bounds = mod.HullMesh!.ComputeFaceRangeBounds(0, mod.HullMesh.FaceCount);
            Assert.True(bounds.HasValue);
        }
    }

    [Fact]
    public void BoxModule_IsNotRoutedThroughMeshFactoryHullPath()
    {
        // Sanity check on the other branch: an ordinary box module (MeshFactory == null) is
        // handled unconditionally by BuildHullMesh instead — a different code path entirely
        // — and must not report a MeshFactory hull.
        var box = BuildModule(StationModuleRegistry.HabBlock, 99);
        StationDecorator.Decorate([box]);

        Assert.False(SystemSpaceState.HasMeshFactoryHull(box));
        Assert.Null(box.HullMesh);
    }

    [Fact]
    public void ProductionCasterStageExplicitlyIncludesNativeMegastationMajorClasses()
    {
        DecorClass[] enabled = SystemSpaceState.ClassesForStage(
            SystemSpaceState.CasterStage.AllClasses).ToArray();

        Assert.Contains(DecorClass.MegastationInfrastructureMajor, enabled);
        Assert.Contains(DecorClass.MegastationMegaGreebleMajor, enabled);
        Assert.Contains(DecorClass.MegastationFabricMajor, enabled);
        Assert.DoesNotContain(DecorClass.MegastationInfrastructureMinor, enabled);
        Assert.DoesNotContain(DecorClass.MegastationMegaGreebleMinor, enabled);
        Assert.DoesNotContain(DecorClass.MegastationFabricMinor, enabled);
    }

    [Fact]
    public void HullLessPresentationCasterStillContributesShadowFitBounds()
    {
        var decoration = (min: new Vector3(-80f, -20f, 3f), max: new Vector3(90f, 25f, 70f));

        bool included = SystemSpaceState.TryCombineStationShadowCasterBounds(
            hullBounds: null,
            decorationBounds: decoration,
            out Vector3 min,
            out Vector3 max);

        Assert.True(included);
        Assert.Equal(decoration.min, min);
        Assert.Equal(decoration.max, max);
    }
}
