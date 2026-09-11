using Inferior.Game.StationGen.Megastations;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MegastationInteriorHazeFastTests
{
    private static readonly Vector3 CavityMinimum = new(-100f, -80f, -500f);
    private static readonly Vector3 CavityMaximum = new(100f, 80f, -60f);
    private static readonly Vector3 ThroatMinimum = new(-30f, -20f, -60f);
    private static readonly Vector3 ThroatMaximum = new(30f, 20f, 0f);
    private static readonly Vector3 Mouth = Vector3.Zero;
    private static readonly Vector3 Outward = Vector3.UnitZ;

    [Fact]
    public void Activation_IsZeroOutsideAuthoritativeBay()
    {
        Assert.Equal(0f, Activation(new Vector3(0f, 0f, 10f)));
        Assert.Equal(0f, Activation(new Vector3(50f, 0f, -20f)));
    }

    [Fact]
    public void Activation_IsOneDeepInsideBay()
        => Assert.Equal(1f, Activation(new Vector3(0f, 0f, -100f)));

    [Fact]
    public void Activation_IsExactZeroWhenDevelopmentToggleIsOff()
        => Assert.Equal(0f, MegastationInteriorHaze.ResolveActivation(
            new Vector3(0f, 0f, -100f),
            CavityMinimum,
            CavityMaximum,
            ThroatMinimum,
            ThroatMaximum,
            Mouth,
            Outward,
            enabled: false));

    [Fact]
    public void Activation_TransitionsSmoothlyAndIsBoundedAtMouth()
    {
        float outside = Activation(new Vector3(0f, 0f, 1f));
        float near = Activation(new Vector3(0f, 0f, -15f));
        float middle = Activation(new Vector3(0f, 0f, -30f));
        float deep = Activation(new Vector3(0f, 0f, -45f));

        Assert.Equal(0f, outside);
        Assert.InRange(near, 0f, middle);
        Assert.InRange(middle, near, deep);
        Assert.InRange(deep, middle, 1f);
        Assert.Equal(.5f, middle, 4);
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(500f, float.NaN)]
    [InlineData(-100f, 1f)]
    [InlineData(100000f, 2f)]
    public void DistanceBlend_IsFiniteAndClamped(float distance, float activation)
    {
        float blend = MegastationInteriorHaze.ResolveDistanceBlend(distance, activation);
        Assert.True(float.IsFinite(blend));
        Assert.InRange(blend, 0f, MegastationInteriorHaze.MaximumBlend);
    }

    [Fact]
    public void SameInputsProduceSameActivation()
    {
        var camera = new Vector3(4f, 3f, -37f);
        Assert.Equal(Activation(camera), Activation(camera));
    }

    [Fact]
    public void StrengthPresetsAreExplicitAndMonotonic()
    {
        float off = MegastationInteriorHaze.MaximumBlendFor(
            MegastationInteriorHazeStrength.Off);
        float subtle = MegastationInteriorHaze.MaximumBlendFor(
            MegastationInteriorHazeStrength.Subtle);
        float medium = MegastationInteriorHaze.MaximumBlendFor(
            MegastationInteriorHazeStrength.Medium);
        float strong = MegastationInteriorHaze.MaximumBlendFor(
            MegastationInteriorHazeStrength.Strong);

        Assert.Equal(0f, off);
        Assert.True(off < subtle && subtle < medium && medium < strong);
        Assert.InRange(strong, 0f, 1f);
    }

    private static float Activation(Vector3 camera)
        => MegastationInteriorHaze.ResolveActivation(
            camera,
            CavityMinimum,
            CavityMaximum,
            ThroatMinimum,
            ThroatMaximum,
            Mouth,
            Outward);
}
