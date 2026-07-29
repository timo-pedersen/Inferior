using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationPrototypeTests
{
    private const string StationId = "Test Star:Test Parent:Prototype Station";
    private const string StarterStationId = "Oranae:Oranae I:Nova Anchorage";
    private static readonly MegastationPrototypeSettings RawMassingSettings =
        MegastationPrototypeSettings.Default with { EnableTopologyRegularisation = false };

    [Fact]
    public void SliceGrid_IsDeterministicPositiveAndMatchesCoreDimensions()
    {
        var a = GenerateRawCpu(StationId).Grid;
        var b = GenerateRawCpu(StationId).Grid;
        var c = GenerateRawCpu(StationId + " other").Grid;

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
        var grid = GenerateRawCpu(StationId).Grid;
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
        var grid = GenerateRawCpu(StationId).Grid;
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
        var result = GenerateRawCpu(StationId);
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
        var result = GenerateRawCpu(StationId);
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
        var result = GenerateRawCpu(StationId);
        Assert.Equal(4, result.Diagnostics.GeneratorVersion);
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
                "4C2603CB147B0DC3BF280D4D21558DDA6FEA75D572E1B73293335A1F7EEC2E5E",
                "BDF65C8AA6211665A4538F136F6F04C0D6ACE2F0B16166FD6E0BBB7954549C43",
                "CE435ACF5AF0BE63CC8CD60F724AA94EFCC557EE9166C7F367F3B935E8E1465B",
                "E36C0EF1F4D190D1CB011F24A80B377CC8D0C9C33CE337F08ACF3D70B70933BF",
                "3D9CC8EB3D43C72AB651FE59C7F21E1A282CF7E384F637D1D6DE8B0CBA5F14DB",
                "733ED14AE0D25742A7C0C528670B65C7D0A7631B7ADD75014F0A5E691C222EB7",
                7728, 6083, 5731, 482, 25, 25, 0, 0, 0, 1, false),
            new MassingFixture(
                "representative",
                StationId,
                "D507F9DCBF6EA8A0A39A30CA752FB52D83EEE1DB0A9451951E8A3FFD53CC6581",
                "CA3560409FDA6B54281415730A84F77DF1EE66E2F7998C425612385AFDB73F5C",
                "F2AC444329E4EE09E93EEF214E568BD7BA85FF282DF95280C3E1FB13A620752C",
                "207D2E70186629E818AC6524BA31E0392A2BB47F0E6A70CB4EA1D7DA7DC77B9F",
                "666EDB5EF626AF943533154BF7187BC2D27038473C9D7B760C7C145909FB53F6",
                "BC714D42D83CEEDF74038CDCF642B006AAF20446AC3135FE5276969C310C28F2",
                11200, 7344, 3412, 364, 63, 59, 0, 0, 0, 1, false),
            new MassingFixture(
                "strong edge",
                "Test Star:Test Parent:Edge Strong 0001",
                "74E544AD6B2AB243BB3130AECFF4F524DFB04E6AD2B84CBE3D01623E4DA74739",
                "BB89097CC731B836DE9AA1909F644BA5E51B59BAD6F67BB7D21EC7126A45ED85",
                "2631049EC90414E57CD073A7313BA95DB59278312C04310CA13EFCA38241B245",
                "5B09F96CE77FE99324C8D435D20359D1AC5D9779A5FBF096A6BC878F7B099D5C",
                "AC1FB670B5D9BDF53C9312F77F623052C68224CCC52D5A18893A7A6A8C4E5CEA",
                "A74E8524E66856E0C22921935FA614E4208C593C7B3B20B258CF59B785F26ED7",
                8550, 6517, 5027, 575, 48, 51, 0, 0, 0, 1, false),
            new MassingFixture(
                "broken edge",
                "Test Star:Test Parent:Broken Edge 0001",
                "5916392866F1FD8A694781F752B4DA246E4E6B22DB763D47A5453211F0431F5A",
                "B11ED8D9B5D73068E8CEA7F81CC74AD600EAD1CCABCF1F4283D95A58DCD41B85",
                "8C9F3040F0DADE91EF561F12B904EE707F8AB2F41D7C9202B9B45DBD915E0256",
                "50C3A2EAB26308AF9B743BAF5CB7072F54F8B1D10C50B0241E5911DAF968AA85",
                "68055B3D2327D89390953360907243F9CD5845CFD6428027930A489FB5D05AA2",
                "2C4A4D56D8EA51AF210E1A8117C72806459774E3251A6A52AFCD144B139102B8",
                9216, 5882, 1872, 121, 49, 51, 0, 0, 0, 1, false),
        };

        foreach (var fixture in fixtures)
        {
            var result = MegastationPrototypeGenerator.GenerateCpu(fixture.StationId);
            var signature = MegastationMassingSignatureBuilder.Compute(result);

            var structuralSolidSignature = MegastationMassingSignatureBuilder.ComputeStructuralSolid(result);

            Assert.Equal(4, result.Diagnostics.GeneratorVersion);
            Assert.Equal(1, result.Diagnostics.SeedCompatibilityVersion);
            Assert.Equal(1, result.Diagnostics.TopologyRegularisationAlgorithmVersion);
            Assert.Equal(1, result.Diagnostics.BoundaryTopologyAlgorithmVersion);
            Assert.Equal(1, result.Diagnostics.StructuralChamferAlgorithmVersion);
            Assert.Equal(fixture.CompleteSignature, signature.Complete);
            Assert.Equal(fixture.BodySignature, signature.Body);
            Assert.Equal(fixture.SliceGridSignature, signature.SliceGrid);
            Assert.Equal(fixture.PositiveYDepthMapSignature, signature.PositiveYDepthMap);
            Assert.Equal(fixture.StructuralSolidSignature, structuralSolidSignature.Body);
            Assert.Equal(fixture.BoundaryTopologySignature, result.Diagnostics.BoundaryTopologySignature);
            Assert.Equal(fixture.StructuralCells, result.Occupancy.StructuralOccupiedCount);
            Assert.Equal(fixture.FaceCells, result.Occupancy.FaceRegionOccupiedCount);
            Assert.Equal(fixture.EdgeCells, result.Occupancy.EdgeRegionOccupiedCount);
            Assert.Equal(fixture.CornerCells, result.Occupancy.CornerRegionOccupiedCount);
            Assert.Equal(fixture.EdgeCriticalBefore, result.TopologyRegularisation.EdgeCriticalBefore);
            Assert.Equal(fixture.EdgeCriticalAfter, result.TopologyRegularisation.EdgeCriticalAfter);
            Assert.Equal(fixture.VertexCriticalBefore, result.TopologyRegularisation.VertexCriticalBefore);
            Assert.Equal(fixture.VertexCriticalAfter, result.TopologyRegularisation.VertexCriticalAfter);
            Assert.Equal(fixture.RepairAddedCells, result.TopologyRegularisation.RepairAddedCells);
            Assert.Equal(0, result.TopologyRegularisation.RepairRemovedCells);
            Assert.Equal(result.Occupancy.TotalOccupiedCount + fixture.RepairAddedCells, result.RegularisedOccupancy.TotalOccupiedCount);
            Assert.Equal(fixture.ConnectedComponents, result.Diagnostics.ConnectedComponentsBeforeValidation);
            Assert.Equal(fixture.ConnectedComponents, result.TopologyRegularisation.ConnectedComponentsAfter);
            Assert.Equal(fixture.HasSealedCavity, result.Diagnostics.HasSealedCavity);
            Assert.Equal(fixture.HasSealedCavity, result.TopologyRegularisation.SealedCavityAfter);
        }
    }

    [Fact]
    public void PrototypeC2Version4_UsesSeedCompatibilityVersion1ForAcceptedRawMassing()
    {
        var current = GenerateRawCpu(StationId);
        var explicitCompatibility = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            RawMassingSettings with { GeneratorVersion = 99, SeedCompatibilityVersion = 1 });
        var incompatibleSeed = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            RawMassingSettings with { GeneratorVersion = 4, SeedCompatibilityVersion = 2 });

        Assert.Equal(4, current.Diagnostics.GeneratorVersion);
        Assert.Equal(1, current.Diagnostics.SeedCompatibilityVersion);
        Assert.Equal(MegastationMassingSignatureBuilder.Compute(current).Body,
            MegastationMassingSignatureBuilder.Compute(explicitCompatibility).Body);
        Assert.NotEqual(MegastationMassingSignatureBuilder.Compute(current).Body,
            MegastationMassingSignatureBuilder.Compute(incompatibleSeed).Body);
    }

    [Fact]
    public void SubsystemVersionChanges_AreIsolatedUntilExplicitlyUsedByGeneration()
    {
        var baseline = GenerateRawCpu(StationId);
        string baselinePositiveY = MegastationMassingSignatureBuilder.Compute(baseline).PositiveYDepthMap;
        string baselineGrid = MegastationMassingSignatureBuilder.Compute(baseline).SliceGrid;

        var edgeChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            RawMassingSettings with { EdgeAlgorithmVersion = 99 });
        var cornerChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            RawMassingSettings with { CornerAlgorithmVersion = 99 });
        var faceChanged = MegastationPrototypeGenerator.GenerateCpu(
            StationId,
            RawMassingSettings with { FaceUrbanAlgorithmVersion = 99 });

        Assert.Equal(baselinePositiveY, MegastationMassingSignatureBuilder.Compute(edgeChanged).PositiveYDepthMap);
        Assert.Equal(baselineGrid, MegastationMassingSignatureBuilder.Compute(cornerChanged).SliceGrid);
        Assert.Equal(baselinePositiveY, MegastationMassingSignatureBuilder.Compute(faceChanged).PositiveYDepthMap);
    }

    [Fact]
    public void DebugColorMode_DoesNotAlterGeneratedMassing()
    {
        var result = GenerateRawCpu(StationId);
        string before = MegastationMassingSignatureBuilder.Compute(result).Body;

        foreach (MegastationDebugColorMode mode in Enum.GetValues<MegastationDebugColorMode>())
            MegastationPrototypeMeshBuilder.Build(result.Occupancy, new StationModuleMesh(), mode, RawMassingSettings);

        Assert.Equal(before, MegastationMassingSignatureBuilder.Compute(result).Body);
    }

    [Fact]
    public void DevelopmentSelection_IsNotPartOfGeometrySeed()
    {
        Assert.Equal(MegastationPrototypeSelectionMode.Frequent, MegastationPrototypeSettings.DevelopmentSelection.Mode);
        Assert.Equal(0.50, MegastationPrototypeSettings.DevelopmentSelection.MegastationProbability);
        Assert.True(MegastationPrototypeSettings.DevelopmentSelection.ForceStarterStation);

        var canonical = GenerateRawCpu(StarterStationId);
        var forced = GenerateRawCpu(StarterStationId);

        Assert.Equal(
            MegastationMassingSignatureBuilder.Compute(canonical).Body,
            MegastationMassingSignatureBuilder.Compute(forced).Body);
    }

    [Fact]
    public void TowerSeedChange_DoesNotAlterSliceGrid()
    {
        var settingsA = RawMassingSettings with { BaseUrbanDepth = new IntRange(2, 4) };
        var settingsB = RawMassingSettings with { TowerCountPerDistrict = new IntRange(4, 6) };
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
        var result = GenerateRawCpu(StationId);

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
        Assert.Equal(1, result.TopologyRegularisation.ConnectedComponentsAfter);
        Assert.False(result.TopologyRegularisation.SealedCavityAfter);
        Assert.Empty(TopologyRegulariser.FindCriticalContacts(result.RegularisedOccupancy));
        Assert.Equal(result.RegularisedOccupancy.TotalOccupiedCount, CountConnectedOccupied(result.RegularisedOccupancy));
    }

    [Fact]
    public void BoundaryTopology_ClassifiesRegularisedFixtureAsValidManifoldInput()
    {
        var result = MegastationPrototypeGenerator.GenerateCpu(StationId);

        Assert.Equal(0, result.Diagnostics.EdgeCriticalConfigurationsAfterRegularisation);
        Assert.Equal(0, result.Diagnostics.VertexCriticalConfigurationsAfterRegularisation);
        Assert.Equal(0, result.Diagnostics.InvalidDiagonalEdgeCount);
        Assert.Equal(0, result.Diagnostics.NonManifoldVertexCount);
        Assert.True(result.Diagnostics.SharpBoundaryValidation.IsValid);
        Assert.True(result.Diagnostics.ChamferedBoundaryValidation.IsValid);
        Assert.Equal(result.Diagnostics.BoundaryFaceCount, result.Diagnostics.ExposedQuadCount);
        Assert.Equal(MegastationMeshPath.Chamfered, result.Diagnostics.MeshPath);
        Assert.True(result.Diagnostics.BevelQuadCount > 0);
        Assert.True(result.Diagnostics.CornerCapCount > 0);
        Assert.True(result.Diagnostics.ChamferedBoundaryValidation.TriangleCount > result.Diagnostics.SharpBoundaryValidation.TriangleCount);
    }

    [Fact]
    public void BoundaryTopologySignature_IsDeterministicAndIndependentOfRawMassingSignature()
    {
        var a = MegastationPrototypeGenerator.GenerateCpu(StationId);
        var b = MegastationPrototypeGenerator.GenerateCpu(StationId);

        Assert.Equal(a.Diagnostics.BoundaryTopologySignature, b.Diagnostics.BoundaryTopologySignature);
        Assert.Equal(
            MegastationMassingSignatureBuilder.Compute(a).Body,
            MegastationMassingSignatureBuilder.Compute(b).Body);
    }

    [Fact]
    public void BoundaryTopology_DetectsInvalidDiagonalBeforeRegularisation()
    {
        var grid = UnitGrid(2, 2, 1);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, MegacellOwner.FaceInterior, "a");
        occupancy.MarkUrban(1, 1, 0, MegacellOwner.FaceInterior, "b");

        var topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);

        Assert.True(topology.Stats.InvalidDiagonalCount > 0);
    }

    [Fact]
    public void SingleCellBoundary_EmitsValidatedChamfersAndCornerCaps()
    {
        var grid = UnitGrid(1, 1, 1);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, MegacellOwner.FaceInterior, "single");

        var mesh = new StationModuleMesh();
        var stats = MegastationPrototypeMeshBuilder.Build(occupancy, mesh);

        Assert.Equal(6, stats.BoundaryFaceCount);
        Assert.Equal(12, stats.ConvexExteriorCount);
        Assert.Equal(8, stats.SimpleConvexVertexCount);
        Assert.Equal(12, stats.EligibleChamferSegmentCount);
        Assert.Equal(12, stats.BevelQuadCount);
        Assert.Equal(8, stats.CornerCapCount);
        Assert.True(stats.SharpValidation.IsValid);
        Assert.True(stats.ChamferedValidation.IsValid);
        Assert.True(stats.ChamferedValidation.TriangleCount > stats.SharpValidation.TriangleCount);
    }

    [Fact]
    public void BoundaryMeshValidator_CatchesOpenAndTJunctionGeometry()
    {
        var open = new StationModuleMesh();
        open.AddTriangle(new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), Color.White);
        var openReport = BoundaryMeshValidator.Validate(open);
        Assert.False(openReport.IsValid);
        Assert.True(openReport.OpenEdgeCount > 0);

        var tJunction = new StationModuleMesh();
        tJunction.AddTriangle(new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 1, 0), Color.White);
        tJunction.AddTriangle(new Vector3(1, 0, 0), new Vector3(2, 1, 0), new Vector3(0, 1, 0), Color.White);
        var tJunctionReport = BoundaryMeshValidator.Validate(tJunction);
        Assert.False(tJunctionReport.IsValid);
        Assert.True(tJunctionReport.TJunctionCount > 0);
    }

    [Fact]
    public void TowerOnBase_KeepsRootConcaveSharpAndValidatesFinalMesh()
    {
        var grid = UnitGrid(5, 3, 5);
        var occupancy = new StructuralOccupancy(grid);
        for (int x = 0; x < 5; x++)
        for (int z = 0; z < 5; z++)
            occupancy.MarkUrban(x, 0, z, MegacellOwner.FaceInterior, "base");
        occupancy.MarkUrban(2, 1, 2, MegacellOwner.FaceInterior, "tower");
        occupancy.MarkUrban(2, 2, 2, MegacellOwner.FaceInterior, "tower");

        var topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        var mesh = new StationModuleMesh();
        var stats = MegastationPrototypeMeshBuilder.Build(occupancy, mesh);

        Assert.True(topology.Stats.ConcaveExteriorCount > 0);
        Assert.True(stats.BevelQuadCount > 0);
        Assert.True(stats.ChamferedValidation.IsValid);
        Assert.Equal(0, topology.Stats.InvalidDiagonalCount);
    }

    [Fact]
    public void RoofInset_KeepsInnerConcavePerimeterSharpAndFinalMeshClosed()
    {
        var grid = UnitGrid(5, 1, 5);
        var occupancy = new StructuralOccupancy(grid);
        for (int x = 0; x < 5; x++)
        for (int z = 0; z < 5; z++)
        {
            if (x is >= 1 and <= 3 && z is >= 1 and <= 3) continue;
            occupancy.MarkUrban(x, 0, z, MegacellOwner.FaceInterior, "inset");
        }

        var topology = BoundaryTopologyBuilder.Build(occupancy, MegastationPrototypeSettings.Default);
        var mesh = new StationModuleMesh();
        var stats = MegastationPrototypeMeshBuilder.Build(occupancy, mesh);

        Assert.True(topology.Stats.ConcaveExteriorCount > 0);
        Assert.True(stats.ChamferedValidation.IsValid);
        Assert.Equal(0, stats.ChamferedValidation.OpenEdgeCount);
        Assert.Equal(0, stats.ChamferedValidation.TJunctionCount);
    }

    [Fact]
    public void TopologyRegularisation_FillsEdgeDiagonalContactsWithoutRemovingRawMass()
    {
        var grid = UnitGrid(2, 2, 1);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, MegacellOwner.FaceInterior, "a");
        occupancy.MarkUrban(1, 1, 0, MegacellOwner.FaceInterior, "b");

        var regularised = TopologyRegulariser.Regularise(occupancy, MegastationPrototypeSettings.Default);

        Assert.Equal(1, regularised.Report.EdgeCriticalBefore);
        Assert.Equal(0, regularised.Report.EdgeCriticalAfter);
        Assert.Equal(0, regularised.Report.RepairRemovedCells);
        Assert.Equal(1, regularised.Report.RepairAddedCells);
        Assert.Equal(occupancy.TotalOccupiedCount + 1, regularised.Occupancy.TotalOccupiedCount);
        Assert.Empty(TopologyRegulariser.FindCriticalContacts(regularised.Occupancy));
    }

    [Fact]
    public void TopologyRegularisation_AuditsAndRepairsVertexOnlyContacts()
    {
        var grid = UnitGrid(2, 2, 2);
        var occupancy = new StructuralOccupancy(grid);
        occupancy.MarkUrban(0, 0, 0, MegacellOwner.FaceInterior, "a");
        occupancy.MarkUrban(1, 1, 1, MegacellOwner.FaceInterior, "b");

        var regularised = TopologyRegulariser.Regularise(occupancy, MegastationPrototypeSettings.Default);

        Assert.Equal(0, regularised.Report.EdgeCriticalBefore);
        Assert.True(regularised.Report.VertexCriticalBefore > 0);
        Assert.Equal(0, regularised.Report.EdgeCriticalAfter);
        Assert.Equal(0, regularised.Report.VertexCriticalAfter);
        Assert.Equal(0, regularised.Report.RepairRemovedCells);
        Assert.True(regularised.Report.RepairAddedCells > 0);
        Assert.Empty(TopologyRegulariser.FindCriticalContacts(regularised.Occupancy));
    }

    [Fact]
    public void Mesh_HasFiniteNonDegenerateFacesAndContainsVerticesInBounds()
    {
        var result = GenerateRawCpu(StationId);
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
            EnableTopologyRegularisation = false,
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

    private static MegastationPrototypeCpuResult GenerateRawCpu(string stationId)
        => MegastationPrototypeGenerator.GenerateCpu(stationId, RawMassingSettings);

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
        string StructuralSolidSignature,
        string BoundaryTopologySignature,
        int StructuralCells,
        int FaceCells,
        int EdgeCells,
        int CornerCells,
        int RepairAddedCells,
        int EdgeCriticalBefore,
        int EdgeCriticalAfter,
        int VertexCriticalBefore,
        int VertexCriticalAfter,
        int ConnectedComponents,
        bool HasSealedCavity);

    private static SliceGrid UnitGrid(int x, int y, int z)
        => new(
            Enumerable.Repeat(1f, x).ToArray(),
            Enumerable.Repeat(1f, y).ToArray(),
            Enumerable.Repeat(1f, z).ToArray(),
            0..x,
            0..y,
            0..z);
}
