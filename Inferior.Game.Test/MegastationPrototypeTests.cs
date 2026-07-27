using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationPrototypeTests
{
    private const string StationId = "Test Star:Test Parent:Prototype Station";

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
        UrbanGrowthResult positiveY = PositiveYFace(result);

        int rootSeed = MegastationSeed.Root(StationId, MegastationPrototypeSettings.Default.GeneratorVersion);
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
}
