using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game;
using Inferior.Game.States;
using Inferior.Gameplay;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StarterStationRelocationTests
{
    private static readonly DVec3 DefaultStarterSpawn = new(0, 0.5e11, 3e11);

    [Fact]
    public void InitialNewGameStarterEntryQueuesFarStationRelocation()
    {
        var (star, system, farStation) = StarterSystemWithFarStation();
        var payload = InitialPayload(star);

        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(payload, system.Stations);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal(farStation.PersistenceId, plan.StationPersistenceId);
        Assert.Null(plan.Diagnostic);
    }

    [Fact]
    public void InitialNewGameRelocationFirstSnapshotIsFiveHundredMetresFromFarStation()
    {
        var (star, system, farStation) = StarterSystemWithFarStation();
        var result = RunStarterRelocation(star, system, farStation);
        DVec3 stationGalaxy = EclipticToGalaxy(system, system.GetStationPosition(farStation, result.Snapshot.SimTime));
        double surfaceDistance = (result.Snapshot.Position - stationGalaxy).Length
            - SpaceSimulation.StationPhysicalRadius(farStation);
        double facingDot = DVec3.Dot(
            result.Snapshot.Forward,
            (stationGalaxy - result.Snapshot.Position).Normalized());

        Assert.InRange(System.Math.Abs(surfaceDistance - SystemSpaceState.InitialStarterStationStandOffMeters), 0.0, 0.01);
        Assert.True((result.Snapshot.Position - DefaultStarterSpawn).Length > 1_000_000.0);
        Assert.True(facingDot >= 0.9999, $"Ship forward dot to Far Station was {facingDot:R}");
        AssertVecClose(result.Snapshot.ReferenceVelocity, result.Snapshot.Velocity, 1e-6);
        Assert.InRange(result.Snapshot.RelativeSpeedMs, 0.0, 1e-6);
        Assert.Equal(FlightMode.SystemNewtonian, result.Snapshot.FlightMode);
        Assert.Equal("Far Station", result.Snapshot.ReferenceName);
        Assert.Equal(farStation.PersistenceId, result.Diagnostic.NearestStationId);
    }

    [Fact]
    public void InitialNewGameRelocationDoesNotPublishXStopComplete()
    {
        var (star, system, farStation) = StarterSystemWithFarStation();
        var messages = new List<SystemMessage>();
        void Handler(SystemMessage message) => messages.Add(message);

        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            RunStarterRelocation(star, system, farStation);
            DataBus.Drain();
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }

        Assert.DoesNotContain(messages, message => message.Text == "X-Stop complete");
    }

    [Fact]
    public void RelocationPlanCarriesFarStationPersistenceIdNotCalculatedPosition()
    {
        var station = new Station { Name = "Far Station", PersistenceId = "installed-far-id" };
        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            InitialPayload(StarterStar()),
            [station]);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal("installed-far-id", plan.StationPersistenceId);
        Assert.DoesNotContain(
            typeof(SystemSpaceState.StarterStationRelocationPlan).GetProperties(),
            property => property.PropertyType == typeof(DVec3));
    }

    [Fact]
    public void FarStationIsResolvedFromProvidedGeneratedSystemAndDoesNotRequireIndexZero()
    {
        var stationBefore = new Station { Name = "Near Station", PersistenceId = "not-far" };
        var farStation = new Station { Name = "Far Station", PersistenceId = "provided-system-far" };
        var stationAfter = new Station { Name = "Far Outpost", PersistenceId = "not-exact-far-station" };

        var plan = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            InitialPayload(StarterStar()),
            [stationBefore, farStation, stationAfter]);

        Assert.True(plan.ShouldRelocate);
        Assert.Equal("provided-system-far", plan.StationPersistenceId);
    }

    [Fact]
    public void ReturningFromMapsAndExplicitArrivalsDoNotTriggerStarterRelocation()
    {
        var star = StarterStar();
        var targetStation = new Station { Name = "Far Station", PersistenceId = "far" };
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
            TargetStation: targetStation,
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
    public void MissingOrAmbiguousFarStationPreservesDefaultSpawn()
    {
        var star = StarterStar();
        var payload = InitialPayload(star);
        var missing = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            payload,
            [new Station { Name = "Near Station", PersistenceId = "near" }]);
        var ambiguous = SystemSpaceState.CreateInitialStarterStationRelocationPlan(
            payload,
            [
                new Station { Name = "Far Station", PersistenceId = "far-1" },
                new Station { Name = "Far Station", PersistenceId = "far-2" },
            ]);

        Assert.False(missing.ShouldRelocate);
        Assert.Contains("not found", missing.Diagnostic);
        Assert.False(ambiguous.ShouldRelocate);
        Assert.Contains("ambiguous", ambiguous.Diagnostic);

        GameClock.Reset();
        var simulation = new SpaceSimulation();
        var ship = new Ship { Position = DefaultStarterSpawn };
        simulation.SetShip(ship);
        simulation.InstallSystem(star, StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star)));
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        Assert.NotNull(simulation.ShipState);
        AssertVecClose(DefaultStarterSpawn, simulation.ShipState!.Position, 0.0);
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

    private static RelocationResult RunStarterRelocation(Star star, StarSystem system, Station farStation)
    {
        GameClock.Reset();
        DataBus.Drain();

        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            Position = DefaultStarterSpawn,
            Velocity = new DVec3(123.0, -456.0, 789.0),
        };

        simulation.SetShip(ship);
        simulation.InstallSystem(star, system);
        simulation.RequestStationRelocation(
            farStation.PersistenceId!,
            SystemSpaceState.InitialStarterStationStandOffMeters);
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        Assert.NotNull(simulation.ShipState);
        Assert.NotNull(simulation.LastStationProximityTickDiagnostic);
        return new RelocationResult(simulation.ShipState!, simulation.LastStationProximityTickDiagnostic!);
    }

    private static (Star Star, StarSystem System, Station FarStation) StarterSystemWithFarStation()
    {
        Star star = StarterStar();
        StarSystem system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        Station farStation = Assert.Single(system.Stations, station => station.Name == "Far Station");
        return (star, system, farStation);
    }

    private static Star StarterStar()
        => InferiorGame.FindStartStar(GalaxyGenerator.Generate());

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
