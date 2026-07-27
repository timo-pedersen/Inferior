using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationPrototypeTests
{
    private const string StationId = "Test Star:Test Parent:Prototype Station";
    private const string StarterStationId = "Oranae:Oranae I:Nova Anchorage";

    [Fact]
    public void SliceGrid_IsDeterministicPositiveAndMatchesCoreDimensions()
    {
        var a = MegastationPrototypeGenerator.GenerateCpu(StationId).Grid;
        var b = MegastationPrototypeGenerator.GenerateCpu(StationId).Grid;
        var c = MegastationPrototypeGenerator.GenerateCpu(StationId + " other").Grid;

        Assert.Equal(a.XCount, b.XCount);
        Assert.Equal(a.YCount, b.YCount);
        Assert.Equal(a.ZCount, b.ZCount);
        Assert.NotEqual((a.XCount, a.YCount, a.ZCount), (c.XCount, c.YCount, c.ZCount));

        AssertAxis(a, GridAxis.X, MegastationPrototypeSettings.Default.CoreDimensions.X);
        AssertAxis(a, GridAxis.Y, MegastationPrototypeSettings.Default.CoreDimensions.Y);
        AssertAxis(a, GridAxis.Z, MegastationPrototypeSettings.Default.CoreDimensions.Z);
    }

    [Fact]
    public void StructuralCore_IsFilledConnectedAndExteriorLayersStartEmpty()
    {
        var grid = MegastationPrototypeGenerator.GenerateCpu(StationId).Grid;
        var occupancy = new CuboidStructuralVolumeGenerator().Generate(grid);

        int expected = (grid.CoreX.End.Value - grid.CoreX.Start.Value)
                     * (grid.CoreY.End.Value - grid.CoreY.Start.Value)
                     * (grid.CoreZ.End.Value - grid.CoreZ.Start.Value);
        Assert.Equal(expected, occupancy.StructuralOccupiedCount);

        for (int x = grid.CoreX.Start.Value; x < grid.CoreX.End.Value; x++)
        for (int y = grid.CoreY.Start.Value; y < grid.CoreY.End.Value; y++)
        for (int z = grid.CoreZ.Start.Value; z < grid.CoreZ.End.Value; z++)
            Assert.True(occupancy.IsOccupied(x, y, z));

        Assert.False(occupancy.IsOccupied(grid.CoreX.Start.Value - 1, grid.CoreY.Start.Value, grid.CoreZ.Start.Value));
        Assert.Equal(expected, CountConnectedOccupied(occupancy));
    }

    [Fact]
    public void PlainCuboid_HasSixExternalPatchesAndNoSealedCavities()
    {
        var grid = MegastationPrototypeGenerator.GenerateCpu(StationId).Grid;
        var occupancy = new CuboidStructuralVolumeGenerator().Generate(grid);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);
        var patches = SurfacePatchFinder.FindPatches(occupancy);

        Assert.Equal(6, patches.Count);
        Assert.All(patches, p => Assert.NotEmpty(p.Cells));
        Assert.DoesNotContain(Enumerable.Range(0, grid.XCount), x =>
            Enumerable.Range(0, grid.YCount).Any(y =>
            Enumerable.Range(0, grid.ZCount).Any(z =>
                !occupancy.IsOccupied(x, y, z) && !occupancy.IsExternallyAccessible(x, y, z))));
    }

    [Fact]
    public void ExteriorFloodFill_DistinguishesSealedCavity()
    {
        var grid = new SliceGrid(
            Enumerable.Repeat(10f, 5).ToArray(),
            Enumerable.Repeat(10f, 5).ToArray(),
            Enumerable.Repeat(10f, 5).ToArray(),
            1..4,
            1..4,
            1..4);
        var occupancy = new StructuralOccupancy(grid);
        for (int x = 1; x <= 3; x++)
        for (int y = 1; y <= 3; y++)
        for (int z = 1; z <= 3; z++)
            if ((x, y, z) != (2, 2, 2))
                occupancy.MarkStructural(x, y, z);

        ExteriorSpace.ClassifyExternallyAccessibleEmpty(occupancy);

        Assert.False(occupancy.IsExternallyAccessible(2, 2, 2));
        Assert.False(ExteriorSpace.IsFaceExposed(occupancy, 2, 2, 1, GridDirection.PositiveZ));
    }

    [Fact]
    public void FaceGrowth_UrbanizesAllSixFacesAndLeavesReservedBoundaryClear()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);
        Assert.Equal(6, result.Faces.Count);
        Assert.Equal(6, result.Diagnostics.UrbanizedFaceCount);
        int reserve = MegastationPrototypeSettings.Default.ReservedPatchEdgeCells;

        foreach (var face in result.Faces)
        {
            var patch = face.Patch;
            foreach (var cell in patch.Cells)
            {
                int u = SurfacePatchFinder.Coordinate(cell, patch.UAxis);
                int v = SurfacePatchFinder.Coordinate(cell, patch.VAxis);
                bool reserved = u < patch.MinU + reserve || u > patch.MaxU - reserve
                             || v < patch.MinV + reserve || v > patch.MaxV - reserve;
                if (reserved)
                    Assert.Equal(0, face.Depths[u - patch.MinU, v - patch.MinV]);
            }
        }

        for (int x = 0; x < result.Grid.XCount; x++)
        for (int y = 0; y < result.Grid.YCount; y++)
        for (int z = 0; z < result.Grid.ZCount; z++)
        {
            if (result.Occupancy.Owner(x, y, z) != MegacellOwner.FaceInterior) continue;
            Assert.Equal(1, result.Grid.ExteriorAxisCount(x, y, z));
        }
    }

    [Fact]
    public void FaceGrowth_IsMonotonicAndConnectedToCore()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);
        Assert.True(result.Faces.Max(f => f.MaximumDepth) <= 16);

        foreach (var face in result.Faces)
        {
            Assert.All(face.Districts, d =>
            {
                Assert.True(d.MaxU - d.MinU + 1 >= MegastationPrototypeSettings.Default.MinimumDistrictCells);
                Assert.True(d.MaxV - d.MinV + 1 >= MegastationPrototypeSettings.Default.MinimumDistrictCells);
            });

            var (dx, dy, dz) = Direction.Offset(face.Patch.Direction);
            foreach (var cell in face.Patch.Cells)
            {
                int depth = face.Depths[
                    SurfacePatchFinder.Coordinate(cell, face.Patch.UAxis) - face.Patch.MinU,
                    SurfacePatchFinder.Coordinate(cell, face.Patch.VAxis) - face.Patch.MinV];
                for (int layer = 1; layer <= depth; layer++)
                    Assert.True(result.Occupancy.IsUrban(cell.X + dx * layer, cell.Y + dy * layer, cell.Z + dz * layer));
            }
        }

        Assert.Equal(result.Occupancy.TotalOccupiedCount, CountConnectedOccupied(result.Occupancy));
    }

    [Fact]
    public void PositiveYAcceptedFace_DepthMapMatchesPrototypeASeedPath()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);
        Assert.Equal(2, result.Diagnostics.GeneratorVersion);
        Assert.Equal(1, result.Diagnostics.SeedCompatibilityVersion);
        UrbanGrowthResult positiveY = PositiveYFace(result);

        int rootSeed = MegastationSeed.Root(StationId, MegastationPrototypeSettings.Default.SeedCompatibilityVersion);
        var legacyGrid = SliceGrid.Create(MegastationPrototypeSettings.Default, MegastationSeed.Derive(rootSeed, "slice-grid layout"));
        var legacyOccupancy = new CuboidStructuralVolumeGenerator().Generate(legacyGrid);
        ExteriorSpace.ClassifyExternallyAccessibleEmpty(legacyOccupancy);
        SurfacePatch legacyPatch = SurfacePatchFinder.FindPatches(legacyOccupancy)
            .Single(p => p.Direction == GridDirection.PositiveY);
        UrbanGrowthResult legacy = UrbanGrowth.Generate(
            legacyOccupancy,
            legacyPatch,
            MegastationPrototypeSettings.Default,
            MegastationSeed.Derive(rootSeed, "district layout"));

        Assert.Equal(HashDepths(legacy.Depths), HashDepths(positiveY.Depths));
    }

    [Fact]
    public void MassingSignatures_FreezeAcceptedPrototypeBFixtures()
    {
        var fixtures = new[]
        {
            new MassingFixture(
                "forced starter",
                StarterStationId,
                "4A8DC8BAE3EFFE8ABA512AA70CBA58840A8335C55A1DC2C60F380E1C2FCF736E",
                "BDF65C8AA6211665A4538F136F6F04C0D6ACE2F0B16166FD6E0BBB7954549C43",
                "CE435ACF5AF0BE63CC8CD60F724AA94EFCC557EE9166C7F367F3B935E8E1465B",
                "E36C0EF1F4D190D1CB011F24A80B377CC8D0C9C33CE337F08ACF3D70B70933BF",
                7728, 6083, 5731, 482, 1, false),
            new MassingFixture(
                "representative",
                StationId,
                "3C3184C6582FDB5D7FE14C217E5BF5212CB68EF22E11BF3E5A9B154C3FBFE76A",
                "CA3560409FDA6B54281415730A84F77DF1EE66E2F7998C425612385AFDB73F5C",
                "F2AC444329E4EE09E93EEF214E568BD7BA85FF282DF95280C3E1FB13A620752C",
                "207D2E70186629E818AC6524BA31E0392A2BB47F0E6A70CB4EA1D7DA7DC77B9F",
                11200, 7344, 3412, 364, 1, false),
            new MassingFixture(
                "strong edge",
                "Test Star:Test Parent:Edge Strong 0001",
                "64D43B0876B9104604A1392C1E2074BFA602BA88C8FC1F2C800C03897D057D1A",
                "BB89097CC731B836DE9AA1909F644BA5E51B59BAD6F67BB7D21EC7126A45ED85",
                "2631049EC90414E57CD073A7313BA95DB59278312C04310CA13EFCA38241B245",
                "5B09F96CE77FE99324C8D435D20359D1AC5D9779A5FBF096A6BC878F7B099D5C",
                8550, 6517, 5027, 575, 1, false),
            new MassingFixture(
                "broken edge",
                "Test Star:Test Parent:Broken Edge 0001",
                "6159FD22BC7597BA614738C631DDDC5481844C8238E9A87F54FD4DC3DFFD9A62",
                "B11ED8D9B5D73068E8CEA7F81CC74AD600EAD1CCABCF1F4283D95A58DCD41B85",
                "8C9F3040F0DADE91EF561F12B904EE707F8AB2F41D7C9202B9B45DBD915E0256",
                "50C3A2EAB26308AF9B743BAF5CB7072F54F8B1D10C50B0241E5911DAF968AA85",
                9216, 5882, 1872, 121, 1, false),
        };

        foreach (var fixture in fixtures)
        {
            var result = MegastationPrototypeGenerator.GenerateCpu(fixture.StationId);
            var signature = MegastationMassingSignatureBuilder.Compute(result);

            Assert.Equal(2, result.Diagnostics.GeneratorVersion);
            Assert.Equal(1, result.Diagnostics.SeedCompatibilityVersion);
            Assert.Equal(fixture.CompleteSignature, signature.Complete);
            Assert.Equal(fixture.BodySignature, signature.Body);
            Assert.Equal(fixture.SliceGridSignature, signature.SliceGrid);
            Assert.Equal(fixture.PositiveYDepthMapSignature, signature.PositiveYDepthMap);
            Assert.Equal(fixture.StructuralCells, result.Occupancy.StructuralOccupiedCount);
            Assert.Equal(fixture.FaceCells, result.Occupancy.FaceRegionOccupiedCount);
            Assert.Equal(fixture.EdgeCells, result.Occupancy.EdgeRegionOccupiedCount);
            Assert.Equal(fixture.CornerCells, result.Occupancy.CornerRegionOccupiedCount);
            Assert.Equal(fixture.ConnectedComponents, result.Diagnostics.ConnectedComponentsBeforeValidation);
            Assert.Equal(fixture.HasSealedCavity, result.Diagnostics.HasSealedCavity);
        }
    }

    [Fact]
    public void PrototypeBVersion2_UsesSeedCompatibilityVersion1ForAcceptedMassing()
    {
        var current = MegastationPrototypeGenerator.GenerateCpu(StationId);
        var explicitCompatibility = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            MegastationPrototypeSettings.Default with { GeneratorVersion = 99, SeedCompatibilityVersion = 1 });
        var incompatibleSeed = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            MegastationPrototypeSettings.Default with { GeneratorVersion = 2, SeedCompatibilityVersion = 2 });

        Assert.Equal(2, current.Diagnostics.GeneratorVersion);
        Assert.Equal(1, current.Diagnostics.SeedCompatibilityVersion);
        Assert.Equal(MegastationMassingSignatureBuilder.Compute(current).Body,
            MegastationMassingSignatureBuilder.Compute(explicitCompatibility).Body);
        Assert.NotEqual(MegastationMassingSignatureBuilder.Compute(current).Body,
            MegastationMassingSignatureBuilder.Compute(incompatibleSeed).Body);
    }

    [Fact]
    public void SubsystemVersionChanges_AreIsolatedUntilExplicitlyUsedByGeneration()
    {
        var baseline = MegastationPrototypeGenerator.GenerateCpu(StationId);
        string baselinePositiveY = MegastationMassingSignatureBuilder.Compute(baseline).PositiveYDepthMap;
        string baselineGrid = MegastationMassingSignatureBuilder.Compute(baseline).SliceGrid;

        var edgeChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            MegastationPrototypeSettings.Default with { EdgeAlgorithmVersion = 99 });
        var cornerChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            MegastationPrototypeSettings.Default with { CornerAlgorithmVersion = 99 });
        var faceChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            MegastationPrototypeSettings.Default with { FaceUrbanAlgorithmVersion = 99 });

        Assert.Equal(baselinePositiveY, MegastationMassingSignatureBuilder.Compute(edgeChanged).PositiveYDepthMap);
        Assert.Equal(baselineGrid, MegastationMassingSignatureBuilder.Compute(cornerChanged).SliceGrid);
        Assert.Equal(baselinePositiveY, MegastationMassingSignatureBuilder.Compute(faceChanged).PositiveYDepthMap);
    }

    [Fact]
    public void DebugColorMode_DoesNotAlterGeneratedMassing()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);
        string before = MegastationMassingSignatureBuilder.Compute(result).Body;

        foreach (MegastationDebugColorMode mode in Enum.GetValues<MegastationDebugColorMode>())
            MegastationPrototypeMeshBuilder.Build(result.Occupancy, new StationModuleMesh(), mode);

        Assert.Equal(before, MegastationMassingSignatureBuilder.Compute(result).Body);
    }

    [Fact]
    public void DevelopmentSelection_IsNotPartOfGeometrySeed()
    {
        Assert.Equal(MegastationPrototypeSelectionMode.Frequent, MegastationPrototypeSettings.DevelopmentSelection.Mode);
        Assert.Equal(0.50, MegastationPrototypeSettings.DevelopmentSelection.MegastationProbability);
        Assert.True(MegastationPrototypeSettings.DevelopmentSelection.ForceStarterStation);

        var canonical = MegastationPrototypeGenerator.GenerateCpu(StarterStationId);
        var forced = MegastationPrototypeGenerator.GenerateCpu(StarterStationId);

        Assert.Equal(
            MegastationMassingSignatureBuilder.Compute(canonical).Body,
            MegastationMassingSignatureBuilder.Compute(forced).Body);
    }

    [Fact]
    public void TowerSeedChange_DoesNotAlterSliceGrid()
    {
        var settingsA = MegastationPrototypeSettings.Default with { BaseUrbanDepth = new IntRange(2, 4) };
        var settingsB = MegastationPrototypeSettings.Default with { TowerCountPerDistrict = new IntRange(4, 6) };
        var a = MegastationPrototypeGenerator.GenerateCpu(StationId, settingsA).Grid;
        var b = MegastationPrototypeGenerator.GenerateCpu(StationId, settingsB).Grid;

        Assert.Equal(a.XCount, b.XCount);
        Assert.Equal(a.YCount, b.YCount);
        Assert.Equal(a.ZCount, b.ZCount);
        for (int i = 0; i < a.XCount; i++) Assert.Equal(a.GetCellSize(GridAxis.X, i), b.GetCellSize(GridAxis.X, i));
        for (int i = 0; i < a.YCount; i++) Assert.Equal(a.GetCellSize(GridAxis.Y, i), b.GetCellSize(GridAxis.Y, i));
        for (int i = 0; i < a.ZCount; i++) Assert.Equal(a.GetCellSize(GridAxis.Z, i), b.GetCellSize(GridAxis.Z, i));
    }

    [Fact]
    public void EdgeAndCornerRegions_AreGeneratedWithStableOwnership()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);

        Assert.Equal(12, result.Edges.Count);
        Assert.Equal(8, result.Corners.Count);
        Assert.Equal(result.Edges.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count(), result.Edges.Count);
        Assert.Equal(result.Corners.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count(), result.Corners.Count);
        Assert.True(result.Occupancy.EdgeRegionOccupiedCount > 0);
        Assert.True(result.Occupancy.CornerRegionOccupiedCount > 0);

        for (int x = 0; x < result.Grid.XCount; x++)
        for (int y = 0; y < result.Grid.YCount; y++)
        for (int z = 0; z < result.Grid.ZCount; z++)
        {
            var owner = result.Occupancy.Owner(x, y, z);
            if (owner == MegacellOwner.EdgeRegion)
                Assert.Equal(2, result.Grid.ExteriorAxisCount(x, y, z));
            if (owner == MegacellOwner.CornerRegion)
                Assert.Equal(3, result.Grid.ExteriorAxisCount(x, y, z));
        }
    }

    [Fact]
    public void WholeVolume_IsConnectedAndHasNoSealedCavities()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);

        Assert.Equal(1, result.Diagnostics.ConnectedComponentsBeforeValidation);
        Assert.Equal(0, result.Diagnostics.RemovedDisconnectedCells);
        Assert.False(result.Diagnostics.HasSealedCavity);
        Assert.Equal(result.Occupancy.TotalOccupiedCount, CountConnectedOccupied(result.Occupancy));
    }

    [Fact]
    public void Mesh_HasFiniteNonDegenerateFacesAndContainsVerticesInBounds()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);
        var (verts, indices) = result.Mesh.ToIntArrays();
        Assert.NotEmpty(verts);
        Assert.NotEmpty(indices);

        Vector3 half = new(
            result.Grid.Dimension(GridAxis.X) * 0.5f,
            result.Grid.Dimension(GridAxis.Y) * 0.5f,
            result.Grid.Dimension(GridAxis.Z) * 0.5f);

        foreach (var v in verts)
        {
            AssertFinite(v.Position);
            AssertFinite(v.Normal);
            Assert.True(v.Position.X >= -half.X - 0.001f && v.Position.X <= half.X + 0.001f);
            Assert.True(v.Position.Y >= -half.Y - 0.001f && v.Position.Y <= half.Y + 0.001f);
            Assert.True(v.Position.Z >= -half.Z - 0.001f && v.Position.Z <= half.Z + 0.001f);
        }

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = verts[indices[i]].Position;
            Vector3 b = verts[indices[i + 1]].Position;
            Vector3 c = verts[indices[i + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).Length() > 0.001f);
        }
    }

    [Fact]
    public void StressConfiguration_ExceedsSixteenBitMeshSafelyByUsingStationModuleMeshBuildPath()
    {
        var settings = MegastationPrototypeSettings.Default with
        {
            CoreXSlices = new IntRange(42, 46),
            CoreYSlices = new IntRange(18, 22),
            CoreZSlices = new IntRange(36, 40),
            PositiveGrowthLayers = new IntRange(14, 16),
            MaximumUrbanDepth = 15,
        };
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId, settings);

        Assert.True(result.MeshStats.VertexCount > short.MaxValue);
        Assert.Equal(1, result.MeshStats.MeshPageCount);
    }

    private static void AssertAxis(SliceGrid grid, GridAxis axis, float expectedCoreDimension)
    {
        Range core = grid.CoreRange(axis);
        float previous = grid.GetCellMinimum(axis, 0);
        for (int i = 0; i < grid.Count(axis); i++)
        {
            Assert.True(grid.GetCellSize(axis, i) > 0.001f);
            Assert.Equal(previous, grid.GetCellMinimum(axis, i), 5);
            Assert.True(grid.GetCellMaximum(axis, i) > grid.GetCellMinimum(axis, i));
            previous = grid.GetCellMaximum(axis, i);
        }

        float coreWidth = grid.GetCellMaximum(axis, core.End.Value - 1) - grid.GetCellMinimum(axis, core.Start.Value);
        Assert.Equal(expectedCoreDimension, coreWidth, 3);
    }

    private static UrbanGrowthResult PositiveYFace(MegastationPrototypeCpuResult result)
        => result.Faces.Single(f => f.Patch.Direction == GridDirection.PositiveY);

    private static string HashDepths(int[,] depths)
    {
        unchecked
        {
            uint hash = 2166136261u;
        for (int x = 0; x < depths.GetLength(0); x++)
        for (int y = 0; y < depths.GetLength(1); y++)
            {
                hash ^= (uint)depths[x, y];
                hash *= 16777619u;
            }
            return hash.ToString("X8");
        }
    }

    private static int CountConnectedOccupied(StructuralOccupancy occupancy)
    {
        var grid = occupancy.Grid;
        (int x, int y, int z)? start = null;
        for (int x = 0; x < grid.XCount && start == null; x++)
        for (int y = 0; y < grid.YCount && start == null; y++)
        for (int z = 0; z < grid.ZCount; z++)
            if (occupancy.IsOccupied(x, y, z))
            {
                start = (x, y, z);
                break;
            }

        if (start == null) return 0;
        var seen = new HashSet<(int x, int y, int z)> { start.Value };
        var q = new Queue<(int x, int y, int z)>();
        q.Enqueue(start.Value);
        (int dx, int dy, int dz)[] offsets = [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var o in offsets)
            {
                var n = (c.x + o.dx, c.y + o.dy, c.z + o.dz);
                if (!grid.Contains(n.Item1, n.Item2, n.Item3) || !occupancy.IsOccupied(n.Item1, n.Item2, n.Item3) || !seen.Add(n))
                    continue;
                q.Enqueue(n);
            }
        }
        return seen.Count;
    }

    private static void AssertFinite(Vector3 value)
    {
        Assert.False(float.IsNaN(value.X) || float.IsInfinity(value.X));
        Assert.False(float.IsNaN(value.Y) || float.IsInfinity(value.Y));
        Assert.False(float.IsNaN(value.Z) || float.IsInfinity(value.Z));
    }

    private sealed record MassingFixture(
        string Name,
        string StationId,
        string CompleteSignature,
        string BodySignature,
        string SliceGridSignature,
        string PositiveYDepthMapSignature,
        int StructuralCells,
        int FaceCells,
        int EdgeCells,
        int CornerCells,
        int ConnectedComponents,
        bool HasSealedCavity);
}
