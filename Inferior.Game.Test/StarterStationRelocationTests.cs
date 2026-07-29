using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game;
using Inferior.Game.States;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StarterStationRelocationTests
{
    private static readonly DVec3 DefaultStarterSpawn = new(0, 0.5e11, 3e11);

    [Fact]
    public void DefaultStarterHullIsCosmo()
        => Assert.Equal(
            CosmoHullDefinitionFactory.HullId,
            SystemSpaceState.DefaultStarterHullTypeId);

    [Fact]
    public void InitialNewGameStarterEntryQueuesStarterStationRelocation()
    {
        var (star, system, starterStation) = StarterSystemWithStarterStation();
        var payload = InitialPayload(star);

        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(payload, system.Stations);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal(starterStation.PersistenceId, plan.StationPersistenceId);
        Assert.Null(plan.Diagnostic);
    }

    [Fact]
    public void SnapshotPublishedBeforeRelocationRequestedDoesNotSatisfyExpectedSequence()
    {
        GameClock.Reset();
        var (star, system, starterStation) = StarterSystemWithStarterStation();

        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = DefaultStarterSpawn,
        };
        simulation.SetShip(ship);
        simulation.InstallSystem(star, system);

        // A snapshot published before the relocation request even exists must not satisfy
        // a sequence expectation captured afterward — this is the exact bug: a mere
        // non-null ShipSnapshot was previously treated as "relocation resolved."
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);
        var preRequestSnapshot = simulation.ShipState;
        Assert.NotNull(preRequestSnapshot);

        int expectedSequence = simulation.RequestStationRelocation(
            starterStation.PersistenceId!, SystemSpaceState.InitialStarterStationStandOffMeters);

        Assert.True(preRequestSnapshot!.RelocationSequence < expectedSequence,
            "a snapshot published before the request existed must not satisfy it");

        // The first snapshot published once the sim thread actually resolves the request
        // must satisfy it, and must reflect the relocated position.
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);
        var postApplySnapshot = simulation.ShipState;

        Assert.NotNull(postApplySnapshot);
        Assert.True(postApplySnapshot!.RelocationSequence >= expectedSequence);
        Assert.True((postApplySnapshot.Position - DefaultStarterSpawn).Length > 1_000_000.0,
            "the post-relocation snapshot should no longer be at the default starter spawn");
    }

    [Fact]
    public void RejectedRelocationRequestStillAdvancesRelocationSequence()
    {
        GameClock.Reset();
        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = DefaultStarterSpawn,
        };
        simulation.SetShip(ship);
        // Deliberately no InstallSystem call — the request must be rejected (no system
        // context installed), not merely deferred, so a waiter can never hang forever.

        int expectedSequence = simulation.RequestStationRelocation("nonexistent:station:id", 500.0);
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        var snapshot = simulation.ShipState;
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.RelocationSequence >= expectedSequence,
            "a rejected request must still advance RelocationSequence, or a waiter hangs forever");
    }

    [Fact]
    public void InitialNewGameRelocationFirstSnapshotIsFiveHundredMetresFromStarterStation()
    {
        var (star, system, starterStation) = StarterSystemWithStarterStation();
        var result = RunStarterRelocation(star, system, starterStation);
        DVec3 stationGalaxy = EclipticToGalaxy(system, system.GetStationPosition(starterStation, result.Snapshot.SimTime));
        double surfaceDistance = (result.Snapshot.Position - stationGalaxy).Length
            - SpaceSimulation.StationPhysicalRadius(starterStation);
        double facingDot = DVec3.Dot(
            result.Snapshot.Forward,
            (stationGalaxy - result.Snapshot.Position).Normalized());

        Assert.InRange(System.Math.Abs(surfaceDistance - SystemSpaceState.InitialStarterStationStandOffMeters), 0.0, 0.01);
        Assert.True((result.Snapshot.Position - DefaultStarterSpawn).Length > 1_000_000.0);
        Assert.True(facingDot >= 0.9999, $"Ship forward dot to starter station was {facingDot:R}");
        AssertVecClose(result.Snapshot.ReferenceVelocity, result.Snapshot.Velocity, 1e-6);
        Assert.InRange(result.Snapshot.RelativeSpeedMs, 0.0, 1e-6);
        Assert.Equal(FlightMode.SystemNewtonian, result.Snapshot.FlightMode);
        Assert.Equal(starterStation.Name, result.Snapshot.ReferenceName);
        Assert.Equal(starterStation.PersistenceId, result.Diagnostic.NearestStationId);
    }

    [Fact]
    public void InitialNewGameRelocationDoesNotPublishXStopComplete()
    {
        var (star, system, starterStation) = StarterSystemWithStarterStation();
        var messages = new List<SystemMessage>();
        void Handler(SystemMessage message) => messages.Add(message);

        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            RunStarterRelocation(star, system, starterStation);
            DataBus.Drain();
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }

        Assert.DoesNotContain(messages, message => message.Text == "X-Stop complete");
    }

    [Fact]
    public void RelocationPlanCarriesStationPersistenceIdNotCalculatedPosition()
    {
        var station = new Station { Name = "Any Station", PersistenceId = "installed-id" };
        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            InitialPayload(StarterStar()),
            [station]);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal("installed-id", plan.StationPersistenceId);
        Assert.DoesNotContain(
            typeof(SystemSpaceState.StarterStationRelocationPlan).GetProperties(),
            property => property.PropertyType == typeof(DVec3));
    }

    [Fact]
    public void LargestStationWinsRegardlessOfListPosition()
    {
        var small  = new Station { Name = "Small",  Size = StationSize.Small,  PersistenceId = "small" };
        var large  = new Station { Name = "Large",  Size = StationSize.Large,  PersistenceId = "large" };
        var medium = new Station { Name = "Medium", Size = StationSize.Medium, PersistenceId = "medium" };

        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            InitialPayload(StarterStar()),
            [small, medium, large]);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal("large", plan.StationPersistenceId);
    }

    [Fact]
    public void ReturningFromMapsAndExplicitArrivalsDoNotTriggerStarterRelocation()
    {
        var star = StarterStar();
        var targetBody = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star)).Planets.FirstOrDefault();
        var returnOrientation = Quaternion.Identity;

        var mapReturn = new SystemSpacePayload(
            star,
            null,
            0.0,
            CockpitLayout.Default,
            DefaultStarterSpawn,
            returnOrientation,
            InitialNewGameStarterEntry: true);
        var explicitStationArrival = new SystemSpacePayload(
            star,
            null,
            0.0,
            StationArrival: new StationArrivalTarget(
                "some-id",
                SystemSpaceState.SystemMapStationArrivalStandOffMeters,
                "Test Station"),
            InitialNewGameStarterEntry: true);
        var normalSystemTransition = new SystemSpacePayload(star, null, 0.0, null);

        Assert.False(SystemSpaceState.IsInitialNewGameStarterEntry(mapReturn));
        Assert.False(SystemSpaceState.IsInitialNewGameStarterEntry(explicitStationArrival));
        Assert.False(SystemSpaceState.IsInitialNewGameStarterEntry(normalSystemTransition));

        if (targetBody != null)
        {
            var explicitBodyArrival = new SystemSpacePayload(
                star,
                targetBody,
                0.0,
                InitialNewGameStarterEntry: true);
            Assert.False(SystemSpaceState.IsInitialNewGameStarterEntry(explicitBodyArrival));
        }
    }

    [Fact]
    public void MissingStationsPreservesDefaultSpawn()
    {
        var star = StarterStar();
        var payload = InitialPayload(star);
        var missing = SystemSpaceState.CreateInitialStarterStationRelocationPlan(payload, []);

        Assert.False(missing.ShouldRelocate);
        Assert.Contains("no stations", missing.Diagnostic);

        GameClock.Reset();
        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = DefaultStarterSpawn,
        };
        simulation.SetShip(ship);
        simulation.InstallSystem(star, StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star)));
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        Assert.NotNull(simulation.ShipState);
        AssertVecClose(DefaultStarterSpawn, simulation.ShipState!.Position, 0.0);
    }

    [Fact]
    public void StationWithNoPersistenceIdPreservesDefaultSpawn()
    {
        var payload = InitialPayload(StarterStar());
        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            payload,
            [new Station { Name = "No Id Station" }]);

        Assert.False(plan.ShouldRelocate);
        Assert.Contains("no stable persistence id", plan.Diagnostic);
    }

    [Fact]
    public void StartupRelocationCodeDoesNotImplementStationPoseOrVelocity()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "States", "SystemSpaceState.Helpers.cs"));
        string startupBlock = source[
            source.IndexOf("CreateInitialStarterStationRelocationPlan", StringComparison.Ordinal)..
            source.IndexOf("private void ComputeEclipticRotation", StringComparison.Ordinal)];

        Assert.Contains("RequestStationRelocation", startupBlock);
        Assert.DoesNotContain("GetStationPosition", startupBlock);
        Assert.DoesNotContain("GetStationVelocity", startupBlock);
        Assert.DoesNotContain("OrbitParent", startupBlock);
        Assert.DoesNotContain("TeleportShip", startupBlock);
        Assert.DoesNotContain("QuatLookAt", startupBlock);
        Assert.DoesNotContain("Quaternion.Create", startupBlock);
        Assert.DoesNotContain("new DVec3", startupBlock);
    }

    private static RelocationResult RunStarterRelocation(Star star, StarSystem system, Station starterStation)
    {
        GameClock.Reset();
        DataBus.Drain();

        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = DefaultStarterSpawn,
            Velocity = new DVec3(123.0, -456.0, 789.0),
        };

        simulation.SetShip(ship);
        simulation.InstallSystem(star, system);
        simulation.RequestStationRelocation(
            starterStation.PersistenceId!,
            SystemSpaceState.InitialStarterStationStandOffMeters);
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        Assert.NotNull(simulation.ShipState);
        Assert.NotNull(simulation.LastStationProximityTickDiagnostic);
        return new RelocationResult(simulation.ShipState!, simulation.LastStationProximityTickDiagnostic!);
    }

    private static (Star Star, StarSystem System, Station StarterStation) StarterSystemWithStarterStation()
    {
        Star star = StarterStar();
        StarSystem system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        Station starterStation = StarterSystemSelector.SelectStarterStation(system.Stations)!;
        return (star, system, starterStation);
    }

    private static Star StarterStar()
        => StarterSystemSelector.SelectStar(GalaxyGenerator.Generate()).Star;

    private static SystemSpacePayload InitialPayload(Star star)
        => new(star, null, 0.0, null, InitialNewGameStarterEntry: true);

    private static DVec3 EclipticToGalaxy(StarSystem system, DVec3 ecliptic)
        => CoordinateTransforms.EclipticToGalaxy(
            ecliptic,
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);

    private static void AssertVecClose(DVec3 expected, DVec3 actual, double tolerance)
    {
        Assert.InRange(System.Math.Abs(expected.X - actual.X), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Y - actual.Y), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Z - actual.Z), 0.0, tolerance);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Inferior.slnx")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }

    private sealed record RelocationResult(
        SpaceSimulation.ShipSnapshot Snapshot,
        SpaceSimulation.StationProximityTickDiagnostic Diagnostic);
}
