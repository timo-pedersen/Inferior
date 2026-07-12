using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Input;
using Inferior.Game.States;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationCycleControllerTests
{
    private const double StandOffMeters = 2_000.0;

    [Fact]
    public void CtrlF12IsEdgeTriggeredAndHoldProducesOneRequest()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object system = new();
        Station[] stations = [Station("Alpha", "a")];
        KeyboardState chord = KeysDown(Keys.LeftControl, Keys.F12);

        StationCycleResult first = Handle(controller, system, stations, chord, KeysDown(), requests);
        StationCycleResult held = Handle(controller, system, stations, chord, chord, requests);

        Assert.Equal(StationCycleResultKind.Requested, first.Kind);
        Assert.Equal(StationCycleResultKind.NoInput, held.Kind);
        Assert.Single(requests);
    }

    [Fact]
    public void ReleasingAndPressingAgainAdvancesOnce()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object system = new();
        Station[] stations = [Station("Alpha", "a"), Station("Beta", "b")];
        KeyboardState chord = KeysDown(Keys.LeftControl, Keys.F12);
        KeyboardState none = KeysDown();

        Handle(controller, system, stations, chord, none, requests);
        Handle(controller, system, stations, none, chord, requests);
        Handle(controller, system, stations, chord, none, requests);

        Assert.Equal(["a", "b"], requests.Select(request => request.StationPersistenceId));
    }

    [Fact]
    public void F12WithoutControlDoesNotCycle()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();

        StationCycleResult result = Handle(
            controller,
            new object(),
            [Station("Alpha", "a")],
            KeysDown(Keys.F12),
            KeysDown(),
            requests);

        Assert.Equal(StationCycleResultKind.NoInput, result.Kind);
        Assert.Empty(requests);
    }

    [Fact]
    public void EitherControlKeyActivatesCycle()
    {
        Assert.True(StationCycleController.IsCyclePressed(
            KeysDown(Keys.LeftControl, Keys.F12),
            KeysDown()));
        Assert.True(StationCycleController.IsCyclePressed(
            KeysDown(Keys.RightControl, Keys.F12),
            KeysDown()));
    }

    [Fact]
    public void StationsAreOrderedByNameThenPersistenceId()
    {
        Station[] stations =
        [
            Station("Beta", "b"),
            Station("alpha", "id-2"),
            Station("Alpha", "id-1"),
            Station("Gamma", "g"),
        ];

        string[] orderedIds = StationCycleController.OrderedStations(stations)
            .Select(station => station.PersistenceId!)
            .ToArray();

        Assert.Equal(["id-1", "id-2", "b", "g"], orderedIds);
    }

    [Fact]
    public void OrderingDoesNotChangeWithSimulationTime()
    {
        Station[] stations = [Station("Beta", "b"), Station("Alpha", "a")];
        string[] before = StationCycleController.OrderedStations(stations)
            .Select(station => station.PersistenceId!)
            .ToArray();

        GameClock.Reset();
        GameClock.Advance(12345.0);
        string[] after = StationCycleController.OrderedStations(stations)
            .Select(station => station.PersistenceId!)
            .ToArray();

        Assert.Equal(before, after);
    }

    [Fact]
    public void ActivationsSelectStationsInOrderAndWrap()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object system = new();
        Station[] stations =
        [
            Station("Beta", "b"),
            Station("Alpha", "a"),
            Station("Gamma", "g"),
        ];

        Press(controller, system, stations, requests);
        Press(controller, system, stations, requests);
        Press(controller, system, stations, requests);
        Press(controller, system, stations, requests);

        Assert.Equal(["a", "b", "g", "a"], requests.Select(request => request.StationPersistenceId));
        Assert.Equal([1, 2, 3, 1], requests.Select(request => request.OneBasedIndex));
        Assert.All(requests, request => Assert.Equal(3, request.TotalCount));
    }

    [Fact]
    public void OneStationSystemCyclesSafely()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object system = new();
        Station[] stations = [Station("Only", "only")];

        Press(controller, system, stations, requests);
        Press(controller, system, stations, requests);

        Assert.Equal(["only", "only"], requests.Select(request => request.StationPersistenceId));
    }

    [Fact]
    public void ZeroStationSystemDoesNotIssueRelocation()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();

        StationCycleResult result = Handle(
            controller,
            new object(),
            [],
            KeysDown(Keys.LeftControl, Keys.F12),
            KeysDown(),
            requests);

        Assert.Equal(StationCycleResultKind.NoStations, result.Kind);
        Assert.Empty(requests);
    }

    [Fact]
    public void SystemChangeResetsCursor()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object systemA = new();
        object systemB = new();
        Station[] stations = [Station("Alpha", "a"), Station("Beta", "b")];

        Press(controller, systemA, stations, requests);
        Press(controller, systemA, stations, requests);
        Press(controller, systemB, stations, requests);

        Assert.Equal(["a", "b", "a"], requests.Select(request => request.StationPersistenceId));
    }

    [Fact]
    public void RequestContainsPersistenceIdAndExactStandOff()
    {
        var controller = new StationCycleController(SystemSpaceState.SystemMapStationArrivalStandOffMeters);
        var requests = new List<StationCycleRequest>();

        Press(controller, new object(), [Station("Alpha", "station-alpha")], requests);

        StationCycleRequest request = Assert.Single(requests);
        Assert.Equal("station-alpha", request.StationPersistenceId);
        Assert.Equal(2_000.0, request.SurfaceStandOffMeters);
    }

    [Fact]
    public void FailedRequestDoesNotAdvanceCursor()
    {
        var controller = new StationCycleController(StandOffMeters);
        var requests = new List<StationCycleRequest>();
        object system = new();
        Station[] stations = [Station("Alpha", "a"), Station("Beta", "b")];
        bool accept = false;

        Handle(
            controller,
            system,
            stations,
            KeysDown(Keys.LeftControl, Keys.F12),
            KeysDown(),
            requests,
            request => accept);
        accept = true;
        Press(controller, system, stations, requests);

        StationCycleRequest request = Assert.Single(requests);
        Assert.Equal("a", request.StationPersistenceId);
    }

    [Fact]
    public void CycleControllerDoesNotCalculateStationWorldStateOrUseTargets()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "Input", "StationCycleController.cs"));

        Assert.DoesNotContain("GetStationPosition", source);
        Assert.DoesNotContain(".GetPosition", source);
        Assert.DoesNotContain("GetStationVelocity", source);
        Assert.DoesNotContain("OrbitParent", source);
        Assert.DoesNotContain("DVec3", source);
        Assert.DoesNotContain("Quaternion", source);
        Assert.DoesNotContain("TeleportShip", source);
        Assert.DoesNotContain("Targeting", source);
        Assert.DoesNotContain("Container", source);
    }

    [Fact]
    public void SystemSpaceBridgeUsesCanonicalRelocationAndNotTeleport()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "States", "SystemSpaceState.Ship.cs"));
        string bridgeBlock = source[
            source.IndexOf("private void HandleStationCycleInput", StringComparison.Ordinal)..
            source.IndexOf("private (DVec3? pos, Quaternion? ori) CaptureShipState", StringComparison.Ordinal)];

        Assert.Contains("RequestStationRelocation", bridgeBlock);
        Assert.DoesNotContain("TeleportShip", bridgeBlock);
        Assert.DoesNotContain("GetStationPosition", bridgeBlock);
        Assert.DoesNotContain(".GetPosition", bridgeBlock);
        Assert.DoesNotContain("GetStationVelocity", bridgeBlock);
        Assert.DoesNotContain("OrbitParent", bridgeBlock);
        Assert.DoesNotContain("_targeting", bridgeBlock);
        Assert.DoesNotContain("_testContainers", bridgeBlock);
    }

    private static void Press(
        StationCycleController controller,
        object systemKey,
        IReadOnlyList<Station> stations,
        List<StationCycleRequest> requests)
    {
        KeyboardState none = KeysDown();
        KeyboardState chord = KeysDown(Keys.LeftControl, Keys.F12);
        Handle(controller, systemKey, stations, none, chord, requests);
        Handle(controller, systemKey, stations, chord, none, requests);
    }

    private static StationCycleResult Handle(
        StationCycleController controller,
        object systemKey,
        IReadOnlyList<Station> stations,
        KeyboardState keys,
        KeyboardState prevKeys,
        List<StationCycleRequest> requests,
        Func<StationCycleRequest, bool>? requestRelocation = null)
        => controller.Handle(
            keys,
            prevKeys,
            systemKey,
            stations,
            request =>
            {
                if (requestRelocation != null)
                    return requestRelocation(request);

                requests.Add(request);
                return true;
            });

    private static Station Station(string name, string persistenceId)
        => new() { Name = name, PersistenceId = persistenceId };

    private static KeyboardState KeysDown(params Keys[] keys)
        => new(keys);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Inferior.slnx")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
