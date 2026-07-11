using Inferior.Game.StationGen;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationShadowMathTests
{
    [Fact]
    public void ShadowMapSizeIsFixedAt2048()
    {
        Assert.Equal(2048, StationShadowMath.GetStationShadowMapSize());
    }

    [Fact]
    public void PaddingExpandsBounds()
    {
        var bounds = new StationShadowBounds(new Vector3(-1, -2, -3), new Vector3(4, 5, 6));

        var expanded = StationShadowMath.ExpandBounds(bounds, 2f);

        Assert.Equal(new Vector3(-3, -4, -5), expanded.Min);
        Assert.Equal(new Vector3(6, 7, 8), expanded.Max);
    }

    [Fact]
    public void LightProjectionContainsBoundsCorners()
    {
        var bounds = new StationShadowBounds(new Vector3(-25, -10, -40), new Vector3(35, 20, 45));
        Matrix view = StationShadowMath.CreateLightView(Vector3.Normalize(new Vector3(1, 2, -1)), bounds);
        Matrix projection = StationShadowMath.CreateLightProjection(bounds, view);
        Matrix viewProjection = view * projection;

        foreach (Vector3 corner in Corners(bounds))
        {
            Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), viewProjection);
            Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);

            Assert.InRange(ndc.X, -1.0001f, 1.0001f);
            Assert.InRange(ndc.Y, -1.0001f, 1.0001f);
            Assert.InRange(ndc.Z, -0.0001f, 1.0001f);
        }
    }

    [Fact]
    public void LightClipCoordinatesConvertToShadowTextureCoordinates()
    {
        Vector3 tex = StationShadowMath.ToShadowTextureCoordinate(new Vector3(-1f, 1f, 0.25f));

        Assert.Equal(0f, tex.X, 5);
        Assert.Equal(0f, tex.Y, 5);
        Assert.Equal(0.25f, tex.Z, 5);
    }

    [Fact]
    public void NormalizedDepthMapsNearFarAndMidpoint()
    {
        var range = new StationShadowDepthRange(Near: 10f, Far: 30f, ZPadding: 0f);

        Assert.Equal(0f, StationShadowMath.NormalizeLightDepth(-10f, range), 6);
        Assert.Equal(0.5f, StationShadowMath.NormalizeLightDepth(-20f, range), 6);
        Assert.Equal(1f, StationShadowMath.NormalizeLightDepth(-30f, range), 6);
    }

    [Fact]
    public void StationSunDirectionPutsSunwardSideNearerCamera()
    {
        var bounds = new StationShadowBounds(new Vector3(-10f), new Vector3(10f));
        Vector3 stationToSun = Vector3.UnitZ;
        Matrix view = StationShadowMath.CreateLightView(stationToSun, bounds);

        float sunwardDepth = -Vector3.Transform(Vector3.UnitZ * 5f, view).Z;
        float antiSunwardDepth = -Vector3.Transform(-Vector3.UnitZ * 5f, view).Z;

        Assert.True(sunwardDepth < antiSunwardDepth);
    }

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(-35f, 1f)]
    public void NormalizedDepthClampsSafely(float lightViewZ, float expectedDepth)
    {
        var range = new StationShadowDepthRange(Near: 10f, Far: 30f, ZPadding: 0f);

        Assert.Equal(expectedDepth, StationShadowMath.NormalizeLightDepth(lightViewZ, range), 6);
    }

    [Fact]
    public void ZPaddingExpandsFittedDepthRange()
    {
        var bounds = new StationShadowBounds(new Vector3(-10, -10, -10), new Vector3(10, 10, 10));
        Matrix lightView = Matrix.CreateLookAt(new Vector3(0, 0, 100), Vector3.Zero, Vector3.Up);

        _ = StationShadowMath.CreateLightProjection(bounds, lightView, xyPadding: 0f, zPadding: 0f,
            out StationShadowDepthRange unpadded);
        _ = StationShadowMath.CreateLightProjection(bounds, lightView, xyPadding: 0f, zPadding: 2f,
            out StationShadowDepthRange padded);

        Assert.True(padded.Near < unpadded.Near);
        Assert.True(padded.Far > unpadded.Far);
        Assert.Equal(2f, padded.ZPadding, 6);
    }

    [Fact]
    public void FacingLightReceiverBiasReturnsBaseBias()
    {
        float bias = StationShadowMath.ComputeReceiverBias(
            normalDotLight: 1f, baseBias: 0.004f, slopeBias: 0.008f, maxBias: 0.010f);

        Assert.Equal(0.004f, bias, 6);
    }

    [Fact]
    public void ReceiverBiasIncreasesAsNormalTurnsFromLight()
    {
        float facing = StationShadowMath.ComputeReceiverBias(1f, 0.004f, 0.008f, 0.020f);
        float angled = StationShadowMath.ComputeReceiverBias(0.5f, 0.004f, 0.008f, 0.020f);
        float grazing = StationShadowMath.ComputeReceiverBias(0f, 0.004f, 0.008f, 0.020f);

        Assert.True(facing < angled);
        Assert.True(angled < grazing);
    }

    [Fact]
    public void ReceiverBiasNeverExceedsMaximum()
    {
        float bias = StationShadowMath.ComputeReceiverBias(
            normalDotLight: 0f, baseBias: 0.004f, slopeBias: 0.008f, maxBias: 0.010f);

        Assert.Equal(0.010f, bias, 6);
    }

    [Theory]
    [InlineData(2.0f, 0.004f)]
    [InlineData(-1.0f, 0.010f)]
    public void ReceiverBiasClampsInvalidDotValues(float normalDotLight, float expectedBias)
    {
        float bias = StationShadowMath.ComputeReceiverBias(
            normalDotLight, baseBias: 0.004f, slopeBias: 0.008f, maxBias: 0.010f);

        Assert.Equal(expectedBias, bias, 6);
    }

    private static IEnumerable<Vector3> Corners(StationShadowBounds bounds)
    {
        yield return new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Min.Z);
        yield return new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Min.Z);
        yield return new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Min.Z);
        yield return new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Min.Z);
        yield return new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Max.Z);
        yield return new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Max.Z);
        yield return new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Max.Z);
        yield return new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Max.Z);
    }
}
