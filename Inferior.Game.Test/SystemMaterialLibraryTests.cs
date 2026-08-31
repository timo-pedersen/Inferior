using System.Security.Cryptography;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class SystemMaterialLibraryTests
{
    private const string Nova = "Oranae:Oranae I:Nova Anchorage";
    private static readonly SystemMaterialAssignmentContext Context =
        SystemMaterialCpuLibraryGenerator.CreateAssignmentContext(
            GalaxyGenerator.SystemSeed(
                StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star).Seed);
    private static readonly Lazy<MegastationPrototypeCpuResult> MaterialNova =
        new(() => MegastationPrototypeGenerator.GenerateCpu(Nova, systemMaterials: Context));

    [Fact]
    public void SharedLibraryDefinesM1AndB2WearNeutralFamilies()
    {
        Assert.Equal(
            [
                SystemMaterialFamilyId.DullStructuralMetal,
                SystemMaterialFamilyId.PaintedCoatedMetal,
                SystemMaterialFamilyId.HeavyIndustrialPlate,
                SystemMaterialFamilyId.CleanTechnicalAlloy,
                SystemMaterialFamilyId.PolishedMetal,
                SystemMaterialFamilyId.BrushedMetal,
                SystemMaterialFamilyId.AgedMetal,
                SystemMaterialFamilyId.ErodedMetal,
            ],
            SystemMaterialRecipes.All.Select(recipe => recipe.FamilyId));
        Assert.All(SystemMaterialRecipes.All, recipe =>
        {
            Assert.Equal(512, recipe.TextureSize);
            Assert.Equal(0f, recipe.Wear);
            Assert.True(recipe.TileSizeMeters > 0f);
            Assert.True(recipe.SpecularStrength > 0f);
            Assert.True(recipe.SpecularShininess > 0f);
            Assert.True(recipe.BumpStrength > 0f);
        });
        Assert.Equal(8, SystemMaterialRecipes.All.Select(r => r.SpecularStrength).Distinct().Count());
        Assert.Equal(8, SystemMaterialRecipes.All.Select(r => r.SpecularShininess).Distinct().Count());
    }

    [Fact]
    public void B2MetalFamiliesUseContinuousCharacterWithoutPanelSemantics()
    {
        SystemMaterialRecipe[] b2 = SystemMaterialRecipes.All
            .Skip(4)
            .ToArray();

        Assert.Equal(4, b2.Length);
        Assert.DoesNotContain(b2,
            recipe => recipe.SurfaceCharacter == SystemMaterialSurfaceCharacter.Panelled);
        Assert.All(b2, recipe =>
        {
            SystemMaterialCpuResource resource =
                SystemMaterialCpuLibraryGenerator.GenerateFamily(482901, recipe.FamilyId);
            Assert.Equal(recipe.TextureSize * recipe.TextureSize, resource.Albedo.Length);
            Assert.Equal(recipe.TextureSize * recipe.TextureSize, resource.MaterialMap.Length);
            Assert.True(resource.MaterialMap.Select(pixel => pixel.R).Distinct().Count() > 8);
            Assert.True(resource.MaterialMap.Select(pixel => pixel.G).Distinct().Count() > 8);
        });
    }

    [Fact]
    public void SameSystemAndFamilyProducesIdenticalPixelsAndSignature()
    {
        SystemMaterialCpuResource first = SystemMaterialCpuLibraryGenerator.GenerateFamily(
            123456, SystemMaterialFamilyId.HeavyIndustrialPlate);
        SystemMaterialCpuResource second = SystemMaterialCpuLibraryGenerator.GenerateFamily(
            123456, SystemMaterialFamilyId.HeavyIndustrialPlate);

        Assert.Equal(first.PixelSignature, second.PixelSignature);
        Assert.Equal(first.Albedo, second.Albedo);
        Assert.Equal(first.MaterialMap, second.MaterialMap);
    }

    [Fact]
    public void DifferentSystemsProduceDifferentMaterialCharacter()
    {
        SystemMaterialCpuResource first = SystemMaterialCpuLibraryGenerator.GenerateFamily(
            123456, SystemMaterialFamilyId.DullStructuralMetal);
        SystemMaterialCpuResource second = SystemMaterialCpuLibraryGenerator.GenerateFamily(
            654321, SystemMaterialFamilyId.DullStructuralMetal);

        Assert.NotEqual(first.PixelSignature, second.PixelSignature);
    }

    [Fact]
    public void FamilyGenerationIsIndependentOfRegistryTraversal()
    {
        IReadOnlyList<SystemMaterialCpuResource> all =
            SystemMaterialCpuLibraryGenerator.Generate(778899);
        SystemMaterialCpuResource isolated = SystemMaterialCpuLibraryGenerator.GenerateFamily(
            778899, SystemMaterialFamilyId.CleanTechnicalAlloy);

        Assert.Equal(
            all.Single(resource => resource.Recipe.FamilyId == isolated.Recipe.FamilyId)
                .PixelSignature,
            isolated.PixelSignature);
    }

    [Fact]
    public void WorkerAssignmentContextCannotRetainPreviousSystemGpuResources()
    {
        Assert.DoesNotContain(
            typeof(SystemMaterialAssignmentContext).GetProperties(),
            property => typeof(Texture2D).IsAssignableFrom(property.PropertyType)
                || typeof(GraphicsResource).IsAssignableFrom(property.PropertyType));
        Assert.Equal(typeof(int),
            typeof(SystemMaterialAssignmentContext).GetProperty(
                nameof(SystemMaterialAssignmentContext.LibrarySeed))!.PropertyType);
    }

    [Fact]
    public void OrdinaryStationTextureFixtureRemainsByteIdentical()
    {
        var palette = new TexturePalette
        {
            BaseColour = new Color(120, 115, 108),
            AccentColour = new Color(200, 140, 40),
            GrimeColour = new Color(28, 22, 15),
            NoiseStrength = .18f,
            SubPanelContrast = .16f,
            GrimeStrength = .38f,
            NameFont = FontStyle.Stencil,
            TextColour = new Color(220, 180, 60),
        };
        StationTextureRegistry.TexturePixels pixels = Assert.Single(
            StationTextureRegistry.GenerateVariantPixels(
                SurfaceTexture.IndustrialPanel, palette,
                "material-regression-fixture", .35f, count: 1));

        Assert.Equal(
            "7C5BC81484F9D4383891F16444B2612E140326B96F4BF3204D8CECFA7DDF4D46",
            PixelSignature(pixels.Albedo, pixels.Material));
    }

    [Theory]
    [InlineData(GridDirection.NegativeX)]
    [InlineData(GridDirection.PositiveX)]
    [InlineData(GridDirection.NegativeY)]
    [InlineData(GridDirection.PositiveY)]
    [InlineData(GridDirection.NegativeZ)]
    [InlineData(GridDirection.PositiveZ)]
    public void StructuralProjectionUsesMetresAndSharedCoplanarPhase(GridDirection direction)
    {
        (Vector3 u, Vector3 v) = MegastationPrototypeMeshBuilder.CanonicalUvAxes(direction);
        Vector3 normal = direction switch
        {
            GridDirection.NegativeX => -Vector3.UnitX,
            GridDirection.PositiveX => Vector3.UnitX,
            GridDirection.NegativeY => -Vector3.UnitY,
            GridDirection.PositiveY => Vector3.UnitY,
            GridDirection.NegativeZ => -Vector3.UnitZ,
            _ => Vector3.UnitZ,
        };
        Vector3 origin = normal * 37f + u * 10f + v * 15f;
        var mesh = new StationModuleMesh
        {
            CurrentMaterialFamily = SystemMaterialFamilyId.DullStructuralMetal,
        };
        mesh.AddQuadProjected(origin, origin + u * 20f, origin + u * 20f + v * 10f,
            origin + v * 10f, normal, u, v, 5f, Color.White);
        Vector3 adjacent = origin + u * 20f;
        mesh.AddQuadProjected(adjacent, adjacent + u * 20f,
            adjacent + u * 20f + v * 10f, adjacent + v * 10f,
            normal, u, v, 5f, Color.White);
        var (vertices, _) = mesh.ToIntArrays();

        Assert.Equal(4f, vertices[1].TextureCoordinate.X - vertices[0].TextureCoordinate.X, 4);
        Assert.Equal(2f, vertices[3].TextureCoordinate.Y - vertices[0].TextureCoordinate.Y, 4);
        Assert.Equal(vertices[1].TextureCoordinate, vertices[4].TextureCoordinate);
    }

    [Fact]
    public void FabricScaleRepeatsTwentyMetreWallFourTimesAfterRotation()
    {
        static float Repetitions(float angle)
        {
            Vector3 right = Vector3.Transform(Vector3.UnitX,
                Matrix.CreateRotationZ(angle));
            Vector3 up = Vector3.Transform(Vector3.UnitY,
                Matrix.CreateRotationZ(angle));
            var mesh = new StationModuleMesh { CurrentUvScaleMeters = 5f };
            mesh.AddQuad(Vector3.Zero, right * 20f, right * 20f + up * 10f,
                up * 10f, Color.White);
            var (vertices, _) = mesh.ToIntArrays();
            return Vector2.Distance(vertices[0].TextureCoordinate,
                vertices[1].TextureCoordinate);
        }

        Assert.Equal(4f, Repetitions(0f), 4);
        Assert.Equal(4f, Repetitions(MathHelper.PiOver4), 4);
    }

    [Fact]
    public void MaterialGroupingPreservesAllTrianglesAsBoundedDeterministicRanges()
    {
        static SystemMaterialMeshCpuData Build()
        {
            var mesh = new StationModuleMesh();
            foreach (SystemMaterialFamilyId family in new[]
                     {
                         SystemMaterialFamilyId.HeavyIndustrialPlate,
                         SystemMaterialFamilyId.DullStructuralMetal,
                         SystemMaterialFamilyId.HeavyIndustrialPlate,
                         SystemMaterialFamilyId.CleanTechnicalAlloy,
                     })
            {
                mesh.CurrentMaterialFamily = family;
                float x = mesh.IndexCount;
                mesh.AddQuad(new Vector3(x, 0, 0), new Vector3(x + 1, 0, 0),
                    new Vector3(x + 1, 1, 0), new Vector3(x, 1, 0), Color.White);
            }
            return Assert.IsType<SystemMaterialMeshCpuData>(mesh.PrepareMaterialGroups());
        }

        SystemMaterialMeshCpuData first = Build();
        SystemMaterialMeshCpuData second = Build();
        Assert.Equal(24, first.Mesh.Indices.Length);
        Assert.Equal(3, first.Ranges.Count);
        Assert.True(first.Ranges.Count <= SystemMaterialRecipes.All.Count);
        Assert.Equal(first.Mesh.Indices, second.Mesh.Indices);
        Assert.Equal(first.Ranges, second.Ranges);
        Assert.Equal(24, first.Ranges.Sum(range => range.IndexCount));
        Assert.All(first.Ranges, range =>
            Assert.InRange(range.StartIndex + range.IndexCount, 1, first.Mesh.Indices.Length));
    }

    [Fact]
    public void MegastationStructuralAndFabricLayersUseAtMostOneRangePerFamily()
    {
        MegastationPrototypeCpuResult cpu = MaterialNova.Value;
        PlacedModule structure = MegastationPrototypeGenerator.CreatePlacedModule(cpu);
        PlacedModule fabric = Assert.IsType<PlacedModule>(
            MegastationPrototypeGenerator.CreateFabricModule(cpu));

        Assert.InRange(structure.HullMaterialRanges.Count, 1, 4);
        Assert.InRange(fabric.DecorationMaterialRanges.Count, 1, 4);
        Assert.Equal(cpu.Mesh.IndexCount,
            structure.HullMaterialRanges.Sum(range => range.IndexCount));
        Assert.Equal(cpu.FabricMesh.IndexCount,
            fabric.DecorationMaterialRanges.Sum(range => range.IndexCount));
        Assert.Equal(structure.HullMaterialRanges.Count,
            structure.HullMaterialRanges.Select(range => range.FamilyId).Distinct().Count());
        Assert.Equal(fabric.DecorationMaterialRanges.Count,
            fabric.DecorationMaterialRanges.Select(range => range.FamilyId).Distinct().Count());
        Dictionary<string, MegastationPlanarRegion> regions = cpu.PlanarRegions
            .ToDictionary(region => region.StableId);
        var sunlit = cpu.FabricPlan.Instances
            .Where(instance => regions[instance.SurfaceStableId].Direction
                == GridDirection.PositiveY)
            .GroupBy(instance => instance.SurfaceStableId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Count = group.Count(),
                Centre = new Vector3(
                    group.Average(instance => instance.SurfacePosition.X),
                    group.Average(instance => instance.SurfacePosition.Y),
                    group.Average(instance => instance.SurfacePosition.Z)),
            })
            .First();
        Console.WriteLine(
            $"Nova materials palette={cpu.MaterialAssignment!.Palette.Signature}; " +
            $"structural={string.Join(',', structure.HullMaterialRanges.Select(r => $"{r.FamilyId}:{r.TriangleCount}"))}; " +
            $"fabric={string.Join(',', fabric.DecorationMaterialRanges.Select(r => $"{r.FamilyId}:{r.TriangleCount}"))}; " +
            $"sunlitPositiveY={sunlit.Count}@{sunlit.Centre}");
    }

    private static string PixelSignature(Color[] albedo, Color[] material)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (Color colour in albedo)
            hash.AppendData(BitConverter.GetBytes(colour.PackedValue));
        foreach (Color colour in material)
            hash.AppendData(BitConverter.GetBytes(colour.PackedValue));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
