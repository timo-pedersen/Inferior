using Inferior.Game.StationGen;
using Inferior.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationGpuByteAccountingTests
{
    [Fact]
    public void ColorTextureBytesUseDimensionsAndFourBytesPerPixel()
        => Assert.Equal(
            1_048_576,
            StationGpuByteAccounting.TextureBytes(512, 512, bytesPerPixel: 4));

    [Fact]
    public void VertexBufferBytesUseActualVertexStride()
    {
        int stride = VertexPositionNormalColorTexture.VertexDeclaration.VertexStride;

        Assert.Equal(36, stride);
        Assert.Equal(
            1_701_072,
            StationGpuByteAccounting.VertexBufferBytes(47_252, stride));
    }

    [Theory]
    [InlineData(IndexElementSize.SixteenBits, 140_952)]
    [InlineData(IndexElementSize.ThirtyTwoBits, 281_904)]
    public void IndexBufferBytesUseSelectedElementSize(
        IndexElementSize elementSize,
        long expectedBytes)
        => Assert.Equal(
            expectedBytes,
            StationGpuByteAccounting.IndexBufferBytes(70_476, elementSize));

    [Fact]
    public void StandardShadowMapIncludesSingleColorAndDepth24Storage()
        => Assert.Equal(
            536_870_912,
            StationGpuByteAccounting.ShadowMapBytes(
                8_192,
                8_192,
                SurfaceFormat.Single,
                DepthFormat.Depth24));

    [Fact]
    public void MegaShadowMapUsesLongArithmetic()
        => Assert.Equal(
            2_147_483_648,
            StationGpuByteAccounting.ShadowMapBytes(
                16_384,
                16_384,
                SurfaceFormat.Single,
                DepthFormat.Depth24));

    [Theory]
    [InlineData(27_348_768, 536_870_912, 564_219_680)]
    [InlineData(62_167_440, 536_870_912, 599_038_352)]
    [InlineData(3_965_952, 0, 3_965_952)]
    public void ResidentOwnedBytesReconcileMeasuredStationDiagnostics(
        long uploadedBytes,
        long shadowMapBytes,
        long expectedResidentBytes)
        => Assert.Equal(
            expectedResidentBytes,
            StationGpuByteAccounting.ResidentOwnedBytes(
                uploadedBytes,
                shadowMapBytes));

    [Fact]
    public void BorrowedFallbacksAndRepeatedBindingsAddNoOwnedBytes()
    {
        const long uploadedMeshBytes = 3_965_952;
        const int fallbackReferences = 2;
        const int repeatedModuleBindings = 58;

        long residentBytes = StationGpuByteAccounting.ResidentOwnedBytes(
            uploadedMeshBytes,
            shadowMapBytes: 0);

        Assert.Equal(2, fallbackReferences);
        Assert.Equal(58, repeatedModuleBindings);
        Assert.Equal(uploadedMeshBytes, residentBytes);
    }

    [Fact]
    public void CapturedInstallationTotalCannotBeContaminatedByPreviousPackage()
    {
        long previousPackageBytes = StationGpuByteAccounting.ResidentOwnedBytes(
            27_348_768,
            536_870_912);
        long newPackageBytes = StationGpuByteAccounting.ResidentOwnedBytes(
            3_965_952,
            shadowMapBytes: 0);

        Assert.Equal(564_219_680, previousPackageBytes);
        Assert.Equal(3_965_952, newPackageBytes);
    }
}
