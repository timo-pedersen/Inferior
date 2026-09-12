using Inferior.Galaxy;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

public sealed class MegastationVisualLodTests(ITestOutputHelper output)
{
    [Fact]
    public void ProjectedSizeUsesBoundingSphereDiameter()
    {
        double diameter = StationProjectedSize.DiameterPixels(
            radiusMeters: 1_000,
            centreDistanceMeters: 100_000,
            verticalProjectionScale: 1.0f,
            viewportHeight: 1_000);

        Assert.Equal(10.0, diameter, 8);
    }

    [Fact]
    public void LodSelectionRetainsMacroWhileCompleteIsUnavailable()
    {
        var state = new MegastationVisualLodState(
            MegastationVisualLodPolicy.Default);

        Assert.Equal(
            MegastationVisualLod.Point,
            state.Update(1.0, macroAvailable: true, completeAvailable: false));
        Assert.Equal(
            MegastationVisualLod.Macro,
            state.Update(40.0, macroAvailable: true, completeAvailable: false));
        Assert.True(state.CompleteResidencyDesired);
        Assert.Equal(
            MegastationVisualLod.Complete,
            state.Update(40.0, macroAvailable: true, completeAvailable: true));
    }

    [Fact]
    public void CompleteAndMacroTransitionsUseHysteresis()
    {
        var state = new MegastationVisualLodState(
            MegastationVisualLodPolicy.Default);

        Assert.Equal(MegastationVisualLod.Complete,
            state.Update(40.0, macroAvailable: true, completeAvailable: true));
        Assert.Equal(MegastationVisualLod.Complete,
            state.Update(30.0, macroAvailable: true, completeAvailable: true));
        Assert.Equal(MegastationVisualLod.Macro,
            state.Update(20.0, macroAvailable: true, completeAvailable: true));
        Assert.Equal(MegastationVisualLod.Macro,
            state.Update(1.5, macroAvailable: true, completeAvailable: true));
        Assert.Equal(MegastationVisualLod.Point,
            state.Update(1.0, macroAvailable: true, completeAvailable: true));
    }

    [Fact]
    public void SystemSelectionAllowsAtMostOneMegastation()
    {
        Station[] stations =
        [
            Station("system:station-a", MegastationArchetype.Standard),
            Station("system:station-b", MegastationArchetype.Bolon),
            Station("system:station-c", MegastationArchetype.RedBolon),
        ];
        var selection = new MegastationDevelopmentSelection(
            MegastationPrototypeSelectionMode.Frequent,
            MegastationProbability: 1.0,
            ForceStarterStation: false);

        IReadOnlyDictionary<Station, MegastationSelection> resolved =
            MegastationDevelopmentPolicy.ResolveSystem(
                stations,
                starterStation: null,
                selection);

        Assert.Equal(1, resolved.Count(pair => pair.Value.IsMegastation));
    }

    [Fact]
    public void ForcedStarterWinsSystemMegastationSelection()
    {
        Station[] stations =
        [
            Station("system:station-a", MegastationArchetype.Standard),
            Station("system:station-b", MegastationArchetype.Bolon),
        ];
        var selection = new MegastationDevelopmentSelection(
            MegastationPrototypeSelectionMode.Frequent,
            MegastationProbability: 1.0,
            ForceStarterStation: true);

        IReadOnlyDictionary<Station, MegastationSelection> resolved =
            MegastationDevelopmentPolicy.ResolveSystem(
                stations,
                starterStation: stations[1],
                selection);

        Assert.True(resolved[stations[1]].IsMegastation);
        Assert.False(resolved[stations[0]].IsMegastation);
    }

    [Theory]
    [InlineData(MegastationArchetype.Standard)]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    [Trait("Category", "Slow")]
    public void MacroPackageContainsOnlyVisibleHullAndMatchingCaster(
        MegastationArchetype archetype)
    {
        var systemMaterials = new SystemMaterialAssignmentContext(
            LibrarySeed: 813,
            DominantTintBasis: new Color(92, 96, 100),
            SecondaryTintBasis: new Color(105, 108, 112),
            AccentTintBasis: new Color(118, 122, 126),
            LibrarySignature: "macro-test-materials");
        StationGenerationCpuResult macro =
            StationGenerator.PrepareMegastationMacroCpu(
                Station("macro-package", archetype),
                archetype,
                systemMaterials: systemMaterials);

        PlacedModule module = Assert.Single(macro.Modules);
        Assert.NotNull(module.HullMesh);
        Assert.Same(module.HullMesh, module.HullShadowMesh);
        Assert.Null(module.Mesh);
        Assert.Null(module.GlassMesh);
        Assert.Empty(macro.Textures);
        if (archetype == MegastationArchetype.Standard)
        {
            SystemMaterialDrawRange range = Assert.Single(module.HullMaterialRanges);
            Assert.Equal(SystemMaterialFamilyId.DullStructuralMetal, range.FamilyId);
            Assert.Equal(module.HullMesh.IndexCount, range.IndexCount);
        }
        else
        {
            Assert.Empty(module.HullMaterialRanges);
        }
        AssertFiniteNonDegenerate(module.HullMesh);
        output.WriteLine(
            $"{archetype} macro CPU: {macro.GenerationMilliseconds:F1} ms, " +
            $"{module.HullMesh.VertexCount} vertices, " +
            $"{module.HullMesh.IndexCount / 3} triangles");
        Assert.Equal(
            [
                StationVisualUploadResourceKind.HullMesh,
                StationVisualUploadResourceKind.ShadowHullMesh,
            ],
            macro.UploadPlan.Select(item => item.Kind));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void RectilinearMacroAndCompleteUseIdenticalAcceptedMassing()
    {
        const string identity = "lod-structural-identity";
        MegastationPrototypeMacroCpuResult macro =
            MegastationPrototypeGenerator.GenerateMacroCpu(identity);
        MegastationPrototypeCpuResult complete =
            MegastationPrototypeGenerator.GenerateCpu(identity);

        output.WriteLine(
            $"Rectilinear macro: massing={macro.StructuralData.RawMassingMilliseconds} ms, " +
            $"entrance={macro.StructuralData.EntranceCarveMilliseconds} ms, " +
            $"boundary={macro.BoundaryTopologyBuildMilliseconds} ms, " +
            $"mesh={macro.MeshBuildMilliseconds} ms, " +
            $"total={macro.GenerationMilliseconds:F1} ms");

        Assert.Equal(macro.StructuralData.RootSeed, complete.Diagnostics.RootSeed);
        Assert.Equal(
            macro.StructuralData.Occupancy.TotalOccupiedCount,
            complete.Occupancy.TotalOccupiedCount);
        SliceGrid grid = complete.Grid;
        for (int x = 0; x < grid.XCount; x++)
        for (int y = 0; y < grid.YCount; y++)
        for (int z = 0; z < grid.ZCount; z++)
        {
            Assert.Equal(
                macro.StructuralData.Occupancy[x, y, z],
                complete.Occupancy[x, y, z]);
            Assert.Equal(
                macro.StructuralData.MacroOccupancy.VoidKind(x, y, z),
                complete.RegularisedOccupancy.VoidKind(x, y, z));
            if (macro.StructuralData.MacroOccupancy.IsOccupied(x, y, z))
                Assert.True(complete.RegularisedOccupancy.IsOccupied(x, y, z));
        }
    }

    [Theory]
    [InlineData(MegastationArchetype.Bolon)]
    [InlineData(MegastationArchetype.RedBolon)]
    [Trait("Category", "Slow")]
    public void MolecularMacroAndCompleteUseIdenticalStructuralPlan(
        MegastationArchetype archetype)
    {
        string identity = $"lod-molecular-identity:{archetype}";
        BolonMegastationMacroCpuResult macro =
            BolonMegastationGenerator.GenerateMacroCpu(identity, archetype);
        BolonMegastationCpuResult complete =
            BolonMegastationGenerator.GenerateCpu(identity, archetype);

        Assert.Equal(
            complete.Diagnostics.StructuralSignature,
            macro.Plan.StructuralSignature);
    }

    private static Station Station(string identity, MegastationArchetype archetype)
        => new()
        {
            Name = identity,
            PersistenceId = identity,
            Size = StationSize.Large,
            MegastationArchetype = archetype,
        };

    private static void AssertFiniteNonDegenerate(StationModuleMesh mesh)
    {
        var (vertices, indices) = mesh.ToIntArrays();
        Assert.All(vertices, vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.True(float.IsFinite(vertex.Position.Y));
            Assert.True(float.IsFinite(vertex.Position.Z));
            Assert.True(float.IsFinite(vertex.Normal.X));
            Assert.True(float.IsFinite(vertex.Normal.Y));
            Assert.True(float.IsFinite(vertex.Normal.Z));
        });
        for (int index = 0; index < indices.Length; index += 3)
        {
            Vector3 a = vertices[indices[index]].Position;
            Vector3 b = vertices[indices[index + 1]].Position;
            Vector3 c = vertices[indices[index + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).LengthSquared() > 1e-8f);
        }
    }
}
