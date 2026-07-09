using Inferior.Game;
using Xunit;

namespace Inferior.Game.Test;

public class LkmClassificationTests
{
    [Fact]
    public void ClassifyLkm_AtOuterBoundary_IsOutsideZone()
    {
        var classification = SpaceSimulation.ClassifyLkm(8_000.0);

        Assert.Equal(0, classification.Zone);
        Assert.Equal(int.MaxValue, classification.MaxGear);
    }

    [Fact]
    public void ClassifyLkm_JustInsideOuterBoundary_IsLkm1()
    {
        var classification = SpaceSimulation.ClassifyLkm(7_999.999);

        Assert.Equal(1, classification.Zone);
        Assert.Equal(5, classification.MaxGear);
    }

    [Fact]
    public void ClassifyLkm_JustInsideMiddleBoundary_IsLkm2()
    {
        var classification = SpaceSimulation.ClassifyLkm(1_999.999);

        Assert.Equal(2, classification.Zone);
        Assert.Equal(3, classification.MaxGear);
    }

    [Fact]
    public void ClassifyLkm_JustInsideInnerBoundary_IsLkm3()
    {
        var classification = SpaceSimulation.ClassifyLkm(499.999);

        Assert.Equal(3, classification.Zone);
        Assert.Equal(1, classification.MaxGear);
    }
}
