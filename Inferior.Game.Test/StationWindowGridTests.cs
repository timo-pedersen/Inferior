using Inferior.Game.StationGen;
using Xunit;

namespace Inferior.Game.Test;

// Brief "Absolute Window Sizing": StationDecorator.ComputeWindowGrid is the pure,
// rng-free grid-sizing math extracted from GenerateWindows so it's directly measurable —
// same GraphicsDevice-free testable-helper pattern as StationTextureRegistry's
// OffsetPaletteForVariant/SelectVariantIndex. Expected values below were confirmed by
// running ComputeWindowGrid against the five small-module faces (hab 18x14, core 20x20,
// science 14x14, industrial 22x18, cargo 24x12-face) and a real mega docking-bay wall
// (station seed 11, StationScale.Port -> 238x238x32m cavity, the largest reachable via
// normal generation), not estimated.
public sealed class StationWindowGridTests
{
    // gridW/gridH/cols/rows/winW/winH for each of the five small-module faces, dense
    // (sparse=false) and a representative sizeScale (0.45, the middle tier). Hab and
    // science are byte-identical to the pre-brief (floor-only) formula on every tier —
    // neither ceiling ever binds below ~22m. Core (gridH 5.00->4.50) and cargo (gridW
    // 4.80->4.50) are the only two of the five where the ceiling binds at all, and both
    // are small (<=10%) — exactly the "near-identical" outcome the brief expects.
    [Theory]
    [InlineData("hab",        18f, 14f, 3.60f, 3.50f, 5, 4)]
    [InlineData("core",       20f, 20f, 4.00f, 4.50f, 5, 4)]
    [InlineData("science",    14f, 14f, 2.80f, 3.50f, 5, 4)]
    [InlineData("industrial", 22f, 18f, 4.40f, 4.50f, 5, 4)]
    [InlineData("cargo",      24f, 12f, 4.50f, 3.00f, 5, 4)]
    public void ComputeWindowGrid_SmallModuleFaces_DenseMatchesMeasuredBaseline(
        string _, float faceWidth, float faceHeight,
        float expectedGridW, float expectedGridH, int expectedCols, int expectedRows)
    {
        var (gridW, gridH, cols, rows, _, _) =
            StationDecorator.ComputeWindowGrid(faceWidth, faceHeight, sparse: false, sizeScale: 0.45f);

        Assert.Equal(expectedGridW, gridW, 2);
        Assert.Equal(expectedGridH, gridH, 2);
        Assert.Equal(expectedCols, cols);
        Assert.Equal(expectedRows, rows);
    }

    // Neither MaxWindowSpacingDense (4.5) nor MaxWindowSpacingSparse (7.5) binds below
    // roughly a 22m face — this is what makes the fix universal rather than needing a
    // mega-module branch. hab/science are the two faces here small enough that NEITHER
    // tier's ceiling ever binds, on any sizeScale.
    [Theory]
    [InlineData(18f, 14f)]
    [InlineData(14f, 14f)]
    public void ComputeWindowGrid_OrdinarySmallFaces_NeitherCeilingBinds(float faceWidth, float faceHeight)
    {
        foreach (bool sparse in new[] { false, true })
        foreach (float sizeScale in new[] { 0.55f, 0.45f, 0.35f })
        {
            var (gridW, gridH, _, _, winW, winH) =
                StationDecorator.ComputeWindowGrid(faceWidth, faceHeight, sparse, sizeScale);

            float oldGridW = MathF.Max(2f, faceWidth  / (sparse ? 3f : 5f));
            float oldGridH = MathF.Max(2f, faceHeight / (sparse ? 3f : 4f));

            Assert.Equal(oldGridW, gridW, 3);
            Assert.Equal(oldGridH, gridH, 3);
            Assert.Equal(oldGridW * sizeScale, winW, 3);
            Assert.Equal(oldGridH * sizeScale, winH, 3);
        }
    }

    // The specific regression risk called out in the brief: sparse must stay visibly
    // sparser (fewer, bigger windows) than dense on ordinary modules, not converge toward
    // it once a ceiling is introduced.
    [Theory]
    [InlineData(18f, 14f)]
    [InlineData(20f, 20f)]
    [InlineData(14f, 14f)]
    [InlineData(22f, 18f)]
    [InlineData(24f, 12f)]
    public void ComputeWindowGrid_Sparse_StaysSparserThanDense(float faceWidth, float faceHeight)
    {
        var dense  = StationDecorator.ComputeWindowGrid(faceWidth, faceHeight, sparse: false, sizeScale: 0.45f);
        var sparse = StationDecorator.ComputeWindowGrid(faceWidth, faceHeight, sparse: true,  sizeScale: 0.45f);

        Assert.True(sparse.cols * sparse.rows < dense.cols * dense.rows,
            $"Expected sparse ({sparse.cols}x{sparse.rows}) to have fewer cells than dense ({dense.cols}x{dense.rows})");
        Assert.True(sparse.gridW >= dense.gridW && sparse.gridH >= dense.gridH,
            "Expected sparse spacing to be at least as wide as dense on both axes");
    }

    // The mega-scale case this brief exists for: a real docking-bay wall (238x238m cavity,
    // the largest reachable via normal station generation — see the class comment) would
    // naturally want a ~48x48 grid of ~9m windows under the old floor-only formula. The
    // ceiling bounds spacing, and the safety cap further widens it once cols*rows would
    // exceed MaxWindowCountPerFace, so the result stays an even, human-scale grid instead
    // of either a runaway quad count or a half-populated one.
    [Fact]
    public void ComputeWindowGrid_MegaFace_StaysBoundedAndEven()
    {
        // Measured face bounds from StationModuleRegistry.CreateDockingBay(11, Port)'s hull
        // (DockingBayHull.Build): the two largest wall shapes on that bay.
        var longWall   = StationDecorator.ComputeWindowGrid(239.47f, 33.47f,  sparse: false, sizeScale: 0.45f);
        var squareWall = StationDecorator.ComputeWindowGrid(239.47f, 239.47f, sparse: false, sizeScale: 0.45f);

        foreach (var grid in new[] { longWall, squareWall })
        {
            Assert.True(grid.cols * grid.rows <= 300, $"Expected the safety cap to hold, got {grid.cols}x{grid.rows}");
            Assert.True(grid.cols > 1 && grid.rows > 1, "Expected a populated grid, not a degenerate 1x1");
            Assert.True(grid.winW <= 4.25f + 0.001f && grid.winH <= 4.25f + 0.001f,
                $"Expected MaxWindowSize to bound window size, got {grid.winW}x{grid.winH}");
        }
    }

    [Fact]
    public void ComputeWindowGrid_IsPureAndDeterministic()
    {
        var a = StationDecorator.ComputeWindowGrid(239.47f, 239.47f, sparse: false, sizeScale: 0.45f);
        var b = StationDecorator.ComputeWindowGrid(239.47f, 239.47f, sparse: false, sizeScale: 0.45f);

        Assert.Equal(a, b);
    }
}
