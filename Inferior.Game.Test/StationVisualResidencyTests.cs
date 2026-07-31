using Inferior.Game.StationGen;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationVisualResidencyTests
{
    private static readonly StationVisualResidencyPolicy Policy =
        StationVisualResidencyPolicy.Default;

    [Fact]
    public void NoStationLoadsOutsideDefaultBoundary()
    {
        var state = new StationVisualResidencyState(Policy);

        Assert.Empty(state.Evaluate([Candidate("a", 100_001)]));
        Assert.Null(state.PendingIdentity);
    }

    [Fact]
    public void StationLoadsAtExactDefaultBoundary()
    {
        var state = new StationVisualResidencyState(Policy);

        StationVisualResidencyAction action =
            Assert.Single(state.Evaluate([Candidate("a", 100_000)]));

        Assert.Equal(StationVisualResidencyActionKind.RequestLoad, action.Kind);
        Assert.Equal("a", state.PendingIdentity);
    }

    [Fact]
    public void ResidentRemainsLoadedBetweenBoundaries()
    {
        var state = Installed("a", 100_000);

        Assert.Empty(state.Evaluate([Candidate("a", 125_000)]));
        Assert.Equal("a", state.ResidentIdentity);
    }

    [Fact]
    public void ResidentUnloadsAtExactUnloadBoundary()
    {
        var state = Installed("a", 100_000);

        StationVisualResidencyAction action =
            Assert.Single(state.Evaluate([Candidate("a", 150_000)]));

        Assert.Equal(StationVisualResidencyActionKind.Unload, action.Kind);
        Assert.Null(state.ResidentIdentity);
    }

    [Fact]
    public void MovementAroundLoadBoundaryDoesNotThrashResident()
    {
        var state = Installed("a", 100_000);

        Assert.Empty(state.Evaluate([Candidate("a", 100_001)]));
        Assert.Empty(state.Evaluate([Candidate("a", 99_999)]));
        Assert.Empty(state.Evaluate([Candidate("a", 100_002)]));
        Assert.Equal("a", state.ResidentIdentity);
    }

    [Fact]
    public void SlightlyNearerStationDoesNotReplaceValidResident()
    {
        var state = Installed("a", 90_000);

        Assert.Empty(state.Evaluate([
            Candidate("a", 90_000),
            Candidate("b", 89_999),
        ]));
        Assert.Equal("a", state.ResidentIdentity);
    }

    [Fact]
    public void NearestEligibleStationIsSelectedWithoutResident()
    {
        var state = new StationVisualResidencyState(Policy);

        StationVisualResidencyAction action = Assert.Single(state.Evaluate([
            Candidate("far", 99_000),
            Candidate("near", 10_000),
        ]));

        Assert.Equal("near", action.Identity);
    }

    [Fact]
    public void IdentityBreaksEqualDistanceTieDeterministically()
    {
        var state = new StationVisualResidencyState(Policy);

        StationVisualResidencyAction action = Assert.Single(state.Evaluate([
            Candidate("zeta", 50_000),
            Candidate("alpha", 50_000),
        ]));

        Assert.Equal("alpha", action.Identity);
    }

    [Fact]
    public void ExplicitRelocationSupersedesResident()
    {
        var state = Installed("a", 5_000);

        IReadOnlyList<StationVisualResidencyAction> actions =
            state.RequestExplicit(Candidate("b", 2_000), "arrival");

        Assert.Equal(2, actions.Count);
        Assert.Equal(StationVisualResidencyActionKind.Unload, actions[0].Kind);
        Assert.Equal(StationVisualResidencyActionKind.RequestLoad, actions[1].Kind);
        Assert.Equal("b", state.PendingIdentity);
        Assert.Null(state.ResidentIdentity);
    }

    [Fact]
    public void SystemChangeClearsResident()
    {
        var state = Installed("a", 5_000);

        StationVisualResidencyAction action =
            Assert.Single(state.Reset("system change"));

        Assert.Equal(StationVisualResidencyActionKind.Unload, action.Kind);
        Assert.Null(state.ResidentIdentity);
    }

    [Fact]
    public void PackageSlotAllowsAtMostOneInstalledPackage()
    {
        using var slot = new StationVisualPackageSlot<FakePackage>();
        slot.Install(new FakePackage());

        Assert.Equal(1, slot.LiveCount);
        Assert.Throws<InvalidOperationException>(
            () => slot.Install(new FakePackage()));
    }

    [Fact]
    public void StalePreparationCannotInstall()
    {
        var state = new StationVisualResidencyState(Policy);
        StationVisualResidencyAction first =
            Assert.Single(state.Evaluate([Candidate("a", 10_000)]));
        IReadOnlyList<StationVisualResidencyAction> replacement =
            state.RequestExplicit(Candidate("b", 10_000), "cycle");

        Assert.Contains(
            replacement,
            action => action.Kind == StationVisualResidencyActionKind.CancelPreparation);
        Assert.False(state.TryInstall("a", first.RequestSequence));
        StationVisualResidencyAction second = replacement.Single(
            action => action.Kind == StationVisualResidencyActionKind.RequestLoad);
        Assert.True(state.TryInstall("b", second.RequestSequence));
    }

    [Fact]
    public void FailedPreparationDoesNotRetryUntilEligibilityResets()
    {
        var state = new StationVisualResidencyState(Policy);
        StationVisualResidencyAction request =
            Assert.Single(state.Evaluate([Candidate("a", 10_000)]));

        Assert.True(state.ReportGenerationFailure("a", request.RequestSequence));
        Assert.Empty(state.Evaluate([Candidate("a", 10_000)]));
        Assert.Empty(state.Evaluate([Candidate("a", 150_000)]));
        Assert.Single(state.Evaluate([Candidate("a", 10_000)]));
    }

    [Fact]
    public void VisualClassDistanceOverrideDoesNotDependOnIdentity()
    {
        var policy = new StationVisualResidencyPolicy(
            overrides: new Dictionary<StationVisualClassification, StationVisualDistanceRange>
            {
                [StationVisualClassification.Megastation] = new(200_000, 300_000),
            });
        var state = new StationVisualResidencyState(policy);

        StationVisualResidencyAction action = Assert.Single(state.Evaluate([
            Candidate("ordinary", 150_000),
            Candidate("any-mega-id", 200_000, StationVisualClassification.Megastation),
        ]));

        Assert.Equal("any-mega-id", action.Identity);
    }

    [Fact]
    public void LightweightStationCandidatesRemainAvailableWithoutDetailedResidency()
    {
        var state = new StationVisualResidencyState(Policy);
        StationVisualResidencyCandidate[] lightweightStations =
        [
            Candidate("a", 500_000),
            Candidate("b", 600_000),
            Candidate("c", 700_000),
        ];

        Assert.Empty(state.Evaluate(lightweightStations));
        Assert.Equal(3, lightweightStations.Length);
        Assert.All(lightweightStations, station => Assert.NotEmpty(station.Identity));
    }

    [Fact]
    public void RepeatedPackageSwitchingDisposesEveryReplacedPackage()
    {
        using var slot = new StationVisualPackageSlot<FakePackage>();
        var packages = new List<FakePackage>();

        for (int i = 0; i < 20; i++)
        {
            slot.Clear();
            var package = new FakePackage();
            packages.Add(package);
            slot.Install(package);
            Assert.Equal(1, slot.LiveCount);
        }
        slot.Clear();

        Assert.All(packages, package => Assert.Equal(1, package.DisposeCount));
        Assert.Equal(0, slot.LiveCount);
    }

    private static StationVisualResidencyState Installed(string identity, double surfaceDistance)
    {
        var state = new StationVisualResidencyState(Policy);
        StationVisualResidencyAction request =
            Assert.Single(state.Evaluate([Candidate(identity, surfaceDistance)]));
        Assert.True(state.TryInstall(identity, request.RequestSequence));
        return state;
    }

    private static StationVisualResidencyCandidate Candidate(
        string identity,
        double surfaceDistance,
        StationVisualClassification classification = StationVisualClassification.Standard)
        => new(
            identity,
            classification,
            surfaceDistance + 1_000,
            surfaceDistance);

    private sealed class FakePackage : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
