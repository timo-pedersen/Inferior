using Inferior.Galaxy;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StarterSystemSelectorTests
{
    [Fact]
    public void SelectStarIsDeterministicAcrossRuns()
    {
        var first  = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate());
        var second = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate());

        Assert.Equal(first.Star.GalaxyIndex, second.Star.GalaxyIndex);
        Assert.Equal(first.Star.Name, second.Star.Name);
        Assert.Equal(first.Diagnostic, second.Diagnostic);
    }

    [Fact]
    public void SelectedStarterSystemHasAtLeastMinStationCountUnlessFallbackDiagnosed()
    {
        var result = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate());
        var system = StarSystem.Generate(result.Star, GalaxyGenerator.SystemSeed(result.Star));

        if (result.Diagnostic == null)
            Assert.True(system.Stations.Count >= StarterSystemSelector.MinStationCount);
    }

    [Fact]
    public void SelectStarterStationResolvesToNonEmptyPersistenceId()
    {
        var result = StarterSystemSelector.SelectStar(GalaxyGenerator.Generate());
        var system = StarSystem.Generate(result.Star, GalaxyGenerator.SystemSeed(result.Star));

        var station = StarterSystemSelector.SelectStarterStation(system.Stations);

        Assert.NotNull(station);
        Assert.False(string.IsNullOrWhiteSpace(station!.PersistenceId));
    }

    [Fact]
    public void SelectStarterStationPicksLargestSizeRegardlessOfListPosition()
    {
        var small  = new Station { Name = "S", Size = StationSize.Small,  PersistenceId = "s" };
        var large  = new Station { Name = "L", Size = StationSize.Large,  PersistenceId = "l" };
        var medium = new Station { Name = "M", Size = StationSize.Medium, PersistenceId = "m" };

        var picked = StarterSystemSelector.SelectStarterStation([small, medium, large]);

        Assert.Same(large, picked);
    }

    [Fact]
    public void SelectStarterStationBreaksSizeTiesByOrdinalPersistenceId()
    {
        var zebra = new Station { Name = "Zebra", Size = StationSize.Large, PersistenceId = "zzz" };
        var alpha = new Station { Name = "Alpha", Size = StationSize.Large, PersistenceId = "aaa" };

        var picked = StarterSystemSelector.SelectStarterStation([zebra, alpha]);

        Assert.Same(alpha, picked);
    }

    [Fact]
    public void SelectStarterStationReturnsNullForEmptyList()
        => Assert.Null(StarterSystemSelector.SelectStarterStation([]));
}
