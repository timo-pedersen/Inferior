using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Game.States;
using Inferior.Galaxy;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationTextureCompactionTests
{
    [Fact]
    public void CompactionKeepsOnlyReferencedPreparedTextureIdentitiesInEncounterOrder()
    {
        PreparedStationTexture[] generated = Enumerable.Range(0, 5)
            .Select(index => Texture(new Color(index, index + 1, index + 2, index + 3)))
            .ToArray();
        PlacedModule first = Module("first");
        PlacedModule second = Module("second");
        StationTextureAssignment[] assignments =
        [
            new(first, AlbedoTextureIndex: 4, MaterialTextureIndex: 1),
            new(second, AlbedoTextureIndex: 4, MaterialTextureIndex: 3),
        ];

        StationTextureCompactionResult compacted =
            StationGenerator.CompactSelectedTextures(generated, assignments);

        Assert.Equal(3, compacted.Textures.Count);
        Assert.Same(generated[4], compacted.Textures[0]);
        Assert.Same(generated[1], compacted.Textures[1]);
        Assert.Same(generated[3], compacted.Textures[2]);
        Assert.Equal(
            [(first, 0, 1), (second, 0, 2)],
            compacted.Assignments.Select(assignment => (
                assignment.Module,
                assignment.AlbedoTextureIndex,
                assignment.MaterialTextureIndex)));
    }

    [Fact]
    public void CompactionPreservesSelectedAlbedoAndMaterialPixelsExactly()
    {
        PreparedStationTexture[] generated =
        [
            Texture(new Color(10, 20, 30, 40), new Color(50, 60, 70, 80)),
            Texture(new Color(90, 100, 110, 120), new Color(130, 140, 150, 160)),
            Texture(new Color(170, 180, 190, 200), new Color(210, 220, 230, 240)),
        ];
        PlacedModule module = Module("pixels");
        StationTextureAssignment original = new(module, 2, 0);

        StationTextureCompactionResult compacted =
            StationGenerator.CompactSelectedTextures(generated, [original]);
        StationTextureAssignment remapped = Assert.Single(compacted.Assignments);

        Assert.Equal(
            generated[original.AlbedoTextureIndex].Pixels,
            compacted.Textures[remapped.AlbedoTextureIndex].Pixels);
        Assert.Equal(
            generated[original.MaterialTextureIndex].Pixels,
            compacted.Textures[remapped.MaterialTextureIndex].Pixels);
        Assert.Same(
            generated[original.AlbedoTextureIndex].Pixels,
            compacted.Textures[remapped.AlbedoTextureIndex].Pixels);
        Assert.Same(
            generated[original.MaterialTextureIndex].Pixels,
            compacted.Textures[remapped.MaterialTextureIndex].Pixels);
    }

    [Fact]
    public void CompactionDiagnosticsDistinguishTexturesPairsAndBindings()
    {
        PreparedStationTexture[] generated = Enumerable.Range(0, 6)
            .Select(index => Texture(new Color(index, 0, 0, 0)))
            .ToArray();
        PlacedModule first = Module("first");
        PlacedModule second = Module("second");

        StationTextureCompactionResult compacted =
            StationGenerator.CompactSelectedTextures(
                generated,
                [new(first, 4, 1), new(second, 4, 3)]);

        Assert.Equal(6, compacted.Diagnostics.GeneratedTextureCount);
        Assert.Equal(3, compacted.Diagnostics.GeneratedVariantPairCount);
        Assert.Equal(3, compacted.Diagnostics.SelectedUniqueTextureCount);
        Assert.Equal(2, compacted.Diagnostics.SelectedUniqueTexturePairCount);
        Assert.Equal(3, compacted.Diagnostics.DiscardedTextureCount);
        Assert.Equal(1, compacted.Diagnostics.UploadedAlbedoTextureCount);
        Assert.Equal(2, compacted.Diagnostics.UploadedMaterialTextureCount);
        Assert.Equal(4, compacted.Diagnostics.ModuleTextureBindingCount);
        Assert.Equal(0, compacted.Diagnostics.SharedFallbackReferenceCount);
    }

    [Fact]
    public void CompactionIsDeterministicAndRejectsInvalidIndices()
    {
        PreparedStationTexture[] generated = Enumerable.Range(0, 4)
            .Select(index => Texture(new Color(index, 0, 0, 0)))
            .ToArray();
        PlacedModule module = Module("deterministic");
        StationTextureAssignment[] assignments = [new(module, 3, 1)];

        StationTextureCompactionResult first =
            StationGenerator.CompactSelectedTextures(generated, assignments);
        StationTextureCompactionResult second =
            StationGenerator.CompactSelectedTextures(generated, assignments);

        Assert.Equal(first.Textures, second.Textures);
        Assert.Equal(first.Assignments, second.Assignments);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
        Assert.Throws<InvalidOperationException>(() =>
            StationGenerator.CompactSelectedTextures(
                generated,
                [new(module, 4, 1)]));
    }

    [Fact]
    public void MegastationPreparationBorrowsStructuralFallbacksAndCompactsSecondaryTextures()
    {
        Star star = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star;
        StarSystem system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        Station station = StarterSystemSelector.SelectStarterStation(system.Stations)!;

        Assert.Equal("Nova Anchorage", station.Name);

        StationGenerationCpuResult prepared = StationGenerator.PrepareCpu(
            station,
            useMegastationPrototype: true,
            enabledShadowCasterClasses: SystemSpaceState.ClassesForStage(
                SystemSpaceState.CasterStage.AllClasses).ToHashSet());

        Assert.True(prepared.UsesSharedMegastationFallbackTextures);
        Assert.True(prepared.Modules.Count > 1);
        Assert.NotEmpty(prepared.Textures);
        Assert.Equal(prepared.Modules.Count - 5, prepared.TextureAssignments.Count);
        Assert.Equal(prepared.Textures.Count,
            prepared.UploadPlan.Count(item => item.Texture != null));
        PlacedModule structural = prepared.Modules[0];
        PlacedModule megaGreeble = Assert.Single(prepared.Modules,
            module => module.HasNativeMegastationMegaGreeble);
        PlacedModule fabric = Assert.Single(prepared.Modules,
            module => module.HasNativeMegastationFabric);
        PlacedModule serviceChannels = Assert.Single(prepared.Modules,
            module => module.HasNativeMegastationServiceChannels);
        PlacedModule interior = Assert.Single(prepared.Modules,
            module => module.HasNativeMegastationInterior);
        Assert.DoesNotContain(prepared.TextureAssignments,
            assignment => ReferenceEquals(assignment.Module, structural)
                || ReferenceEquals(assignment.Module, megaGreeble)
                || ReferenceEquals(assignment.Module, fabric)
                || ReferenceEquals(assignment.Module, serviceChannels)
                || ReferenceEquals(assignment.Module, interior));
        Assert.All(prepared.Modules.Where(module => !ReferenceEquals(module, structural)
            && !ReferenceEquals(module, megaGreeble)
            && !ReferenceEquals(module, fabric)
            && !ReferenceEquals(module, serviceChannels)
            && !ReferenceEquals(module, interior)), module => Assert.Single(
            prepared.TextureAssignments,
            assignment => ReferenceEquals(assignment.Module, module)));
        Assert.All(prepared.Modules, module =>
        {
            Assert.Null(module.TextureInstance);
            Assert.Null(module.MaterialInstance);
        });
        Assert.True(prepared.TextureDiagnostics.GeneratedTextureCount
            > prepared.TextureDiagnostics.SelectedUniqueTextureCount);
        Assert.Equal(prepared.Textures.Count,
            prepared.TextureDiagnostics.SelectedUniqueTextureCount);
        Assert.Equal(prepared.TextureAssignments.Count * 2 + 10,
            prepared.TextureDiagnostics.ModuleTextureBindingCount);
        Assert.Equal(10, prepared.TextureDiagnostics.SharedFallbackReferenceCount);
        MegastationAttachmentDiagnostics attachments = Assert.IsType<MegastationAttachmentDiagnostics>(
            prepared.MegastationAttachmentDiagnostics);
        Assert.Equal(prepared.Modules.Count - 5, attachments.PlacedModuleCount);
        MegastationWindowDiagnostics windows = Assert.IsType<MegastationWindowDiagnostics>(
            prepared.MegastationWindowDiagnostics);
        StationVisualUploadPlanItem glass = Assert.Single(
            prepared.UploadPlan,
            item => item.Kind == StationVisualUploadResourceKind.GlassMesh
                && ReferenceEquals(item.Module, structural));
        Assert.Equal(windows.MeshBytes, glass.EstimatedBytes);
        MegastationInfrastructureDiagnostics infrastructure =
            Assert.IsType<MegastationInfrastructureDiagnostics>(
                prepared.MegastationInfrastructureDiagnostics);
        Assert.Same(structural.Mesh, prepared.Modules[0].Mesh);
        Assert.True(structural.HasNativeMegastationInfrastructure);
        Assert.DoesNotContain(prepared.FlatDecorationMeshes.Keys,
            module => ReferenceEquals(module, structural));
        StationVisualUploadPlanItem infrastructureVisible = Assert.Single(
            prepared.UploadPlan,
            item => item.DiagnosticPurpose ==
                StationVisualUploadDiagnosticPurpose.MegastationInfrastructureVisible);
        StationVisualUploadPlanItem infrastructureShadow = Assert.Single(
            prepared.UploadPlan,
            item => item.DiagnosticPurpose ==
                StationVisualUploadDiagnosticPurpose.MegastationInfrastructureShadow);
        Assert.Same(structural, infrastructureVisible.Module);
        Assert.Same(structural, infrastructureShadow.Module);
        Assert.Equal(infrastructure.VisibleMeshBytes, infrastructureVisible.EstimatedBytes);
        Assert.Equal(infrastructure.ShadowMeshBytes, infrastructureShadow.EstimatedBytes);
        MegastationMegaGreebleDiagnostics megaGreebleDiagnostics =
            Assert.IsType<MegastationMegaGreebleDiagnostics>(prepared.MegastationMegaGreebleDiagnostics);
        StationVisualUploadPlanItem megaGreebleVisible = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose == StationVisualUploadDiagnosticPurpose.MegastationMegaGreebleVisible);
        StationVisualUploadPlanItem megaGreebleShadow = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose == StationVisualUploadDiagnosticPurpose.MegastationMegaGreebleShadow);
        Assert.Same(megaGreeble, megaGreebleVisible.Module);
        Assert.Same(megaGreeble, megaGreebleShadow.Module);
        Assert.Equal(megaGreebleDiagnostics.VisibleMeshBytes, megaGreebleVisible.EstimatedBytes);
        Assert.Equal(megaGreebleDiagnostics.ShadowMeshBytes, megaGreebleShadow.EstimatedBytes);
        MegastationFabricDiagnostics fabricDiagnostics =
            Assert.IsType<MegastationFabricDiagnostics>(prepared.MegastationFabricDiagnostics);
        StationVisualUploadPlanItem fabricVisible = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose == StationVisualUploadDiagnosticPurpose.MegastationFabricVisible);
        StationVisualUploadPlanItem fabricShadow = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose == StationVisualUploadDiagnosticPurpose.MegastationFabricShadow);
        Assert.Same(fabric, fabricVisible.Module);
        Assert.Same(fabric, fabricShadow.Module);
        Assert.Equal(fabricDiagnostics.VisibleMeshBytes, fabricVisible.EstimatedBytes);
        Assert.Equal(fabricDiagnostics.ShadowMeshBytes, fabricShadow.EstimatedBytes);
        MegastationServiceChannelDiagnostics serviceDiagnostics =
            Assert.IsType<MegastationServiceChannelDiagnostics>(
                prepared.MegastationServiceChannelDiagnostics);
        StationVisualUploadPlanItem serviceVisible = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose ==
                StationVisualUploadDiagnosticPurpose.MegastationServiceChannelVisible);
        StationVisualUploadPlanItem serviceShadow = Assert.Single(prepared.UploadPlan,
            item => item.DiagnosticPurpose ==
                StationVisualUploadDiagnosticPurpose.MegastationServiceChannelShadow);
        Assert.Same(serviceChannels, serviceVisible.Module);
        Assert.Same(serviceChannels, serviceShadow.Module);
        Assert.Equal(serviceDiagnostics.VisibleMeshBytes, serviceVisible.EstimatedBytes);
        Assert.Equal(serviceDiagnostics.ShadowMeshBytes, serviceShadow.EstimatedBytes);
        Assert.True(prepared.UploadPlan.Sum(item => item.EstimatedBytes)
            > 3_965_952 + windows.MeshBytes);
        Console.WriteLine(
            $"G1 Nova preparation modules={prepared.Modules.Count}; " +
            $"ownedTextures={prepared.Textures.Count}; textureSetDataCalls={prepared.Textures.Count}; " +
            $"uploadResources={prepared.UploadPlan.Count}; gpuBuffers=" +
            $"{prepared.UploadPlan.Count(item => item.Mesh != null) * 2}; " +
            $"uploadedResourceGpuBytes={prepared.UploadPlan.Sum(item => item.EstimatedBytes)}; " +
            $"largestUploadBytes={prepared.UploadPlan.Max(item => item.EstimatedBytes)}");
    }

    [Fact]
    public void RendererFallbackValuesRemainExactAndSemanticallyDistinct()
    {
        Assert.Equal(Color.White, MeshRenderer.WhiteFallbackColor);
        Assert.Equal(new Color(128, 255, 0, 0), MeshRenderer.StationFallbackMaterialColor);
        Assert.NotEqual(new Color(128, 255, 0, 255), MeshRenderer.StationFallbackMaterialColor);
    }

    [Fact]
    public void RealOrdinaryStationUploadsOnlyItsCompactedSelectedTextures()
    {
        Star star = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star;
        StarSystem system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        Station station = Assert.Single(
            system.Stations,
            candidate => candidate.Name == "High Base");

        StationGenerationCpuResult prepared = StationGenerator.PrepareCpu(station);
        StationTexturePreparationDiagnostics diagnostics = prepared.TextureDiagnostics;
        StationVisualUploadPlanItem[] textureUploads = prepared.UploadPlan
            .Where(item => item.Texture != null)
            .ToArray();

        Assert.False(prepared.UsesSharedMegastationFallbackTextures);
        Assert.Equal(161, diagnostics.GeneratedTextureCount);
        Assert.Equal(80, diagnostics.GeneratedVariantPairCount);
        Assert.Equal(30, diagnostics.SelectedUniqueTextureCount);
        Assert.Equal(15, diagnostics.SelectedUniqueTexturePairCount);
        Assert.Equal(131, diagnostics.DiscardedTextureCount);
        Assert.Equal(15, diagnostics.UploadedAlbedoTextureCount);
        Assert.Equal(15, diagnostics.UploadedMaterialTextureCount);
        Assert.Equal(58, diagnostics.ModuleTextureBindingCount);
        Assert.True(diagnostics.GeneratedTextureCount > diagnostics.SelectedUniqueTextureCount);
        Assert.True(diagnostics.DiscardedTextureCount > 0);
        Assert.Equal(diagnostics.SelectedUniqueTextureCount, prepared.Textures.Count);
        Assert.Equal(prepared.Textures.Count, textureUploads.Length);
        Assert.Equal(
            62_167_440,
            prepared.UploadPlan.Sum(item => item.EstimatedBytes));
        Assert.Equal(prepared.TextureAssignments.Count * 2, diagnostics.ModuleTextureBindingCount);
        Assert.All(prepared.TextureAssignments, assignment =>
        {
            Assert.InRange(assignment.AlbedoTextureIndex, 0, prepared.Textures.Count - 1);
            Assert.InRange(assignment.MaterialTextureIndex, 0, prepared.Textures.Count - 1);
        });
    }

    private static PreparedStationTexture Texture(params Color[] pixels)
        => new(pixels.Length, 1, pixels);

    private static PlacedModule Module(string id)
        => new()
        {
            Definition = new StationModuleDefinition
            {
                Id = id,
                Category = "test",
                BoundingBox = Vector3.One,
                MinScale = StationScale.Outpost,
                Ports = [],
            },
            Transform = Matrix.Identity,
            Seed = 1,
            ChamferDepth = 0.1f,
        };
}
