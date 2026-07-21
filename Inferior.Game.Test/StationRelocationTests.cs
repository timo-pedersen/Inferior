using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game;
using Inferior.Game.States;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class StationRelocationTests
{
    private const double StandOffMeters = 2_000.0;

    [Fact]
    public void RelocationPlacesShipAtRequestedStandOffForStarStation()
    {
        var sample = FindStation(station => station.OrbitParent == null);
        AssertRelocatedToStation(sample);
    }

    [Fact]
    public void RelocationPlacesShipAtRequestedStandOffForPlanetStation()
    {
        var sample = FindStation((system, station) =>
            station.OrbitParent != null && system.Planets.Contains(station.OrbitParent));
        AssertRelocatedToStation(sample);
    }

    [Fact]
    public void RelocationPlacesShipAtRequestedStandOffForMoonStation()
    {
        var sample = FindStation((system, station) =>
            station.OrbitParent != null
            && system.Planets.Any(planet => planet.Children.Contains(station.OrbitParent)));
        AssertRelocatedToStation(sample);
    }

    [Fact]
    public void RelocationResolvesIdentityAgainstInstalledSystemInstance()
    {
        var source = FindStation(station => station.PersistenceId != null);
        var installedSystem = StarSystem.Generate(source.Star, GalaxyGenerator.SystemSeed(source.Star));
        var installedStation = installedSystem.Stations.Single(station => station.PersistenceId == source.Station.PersistenceId);

        var result = RunRelocation(source.Star, installedSystem, source.Station.PersistenceId!, OldShipPosition());

        Assert.Same(installedStation, result.Diagnostic.NearestStation);
        Assert.Equal(installedStation.PersistenceId, result.Diagnostic.NearestStationId);
    }

    [Fact]
    public void StationArrivalPayloadCarriesStableIdentityAndStandOffOnly()
    {
        var payload = new SystemSpacePayload(
            GalaxyGenerator.Generate().First(),
            null,
            12.5,
            StationArrival: new StationArrivalTarget(
                "station-id",
                SystemSpaceState.SystemMapStationArrivalStandOffMeters,
                "Display Station"));

        Assert.Equal("station-id", payload.StationArrival?.PersistenceId);
        Assert.Equal(2_000.0, payload.StationArrival?.SurfaceStandOffMeters);
        Assert.Equal("Display Station", payload.StationArrival?.DisplayName);
        Assert.DoesNotContain(
            typeof(StationArrivalTarget).GetProperties(),
            property => property.PropertyType == typeof(Station)
                || property.PropertyType == typeof(DVec3)
                || property.PropertyType == typeof(Quaternion));
    }

    [Fact]
    public void StationMapArrivalUsesInstalledSystemAndCurrentSimTime()
    {
        var source = FindStation(station => station.PersistenceId != null);
        var installedSystem = StarSystem.Generate(source.Star, GalaxyGenerator.SystemSeed(source.Star));
        var installedStation = installedSystem.Stations.Single(station => station.PersistenceId == source.Station.PersistenceId);

        var result = RunRelocation(
            source.Star,
            installedSystem,
            source.Station.PersistenceId!,
            OldShipPosition(),
            preAdvanceSeconds: 37.0);

        DVec3 stationGalaxy = EclipticToGalaxy(
            installedSystem,
            installedSystem.GetStationPosition(installedStation, result.Snapshot.SimTime));
        double surfaceDistance = (result.Snapshot.Position - stationGalaxy).Length
            - SpaceSimulation.StationPhysicalRadius(installedStation);

        Assert.Same(installedStation, result.Diagnostic.NearestStation);
        Assert.Equal(installedStation.PersistenceId, result.Diagnostic.NearestStationId);
        Assert.True(result.Snapshot.SimTime > 37.0);
        Assert.InRange(System.Math.Abs(surfaceDistance - SystemSpaceState.SystemMapStationArrivalStandOffMeters), 0.0, 0.01);
    }

    [Fact]
    public void BodyArrivalPayloadRemainsBodyArrival()
    {
        var star = GalaxyGenerator.Generate().First();
        var body = new OrbitalBody { Name = "Target Body" };
        var payload = new SystemSpacePayload(star, body, 12.5);

        Assert.Same(body, payload.TargetBody);
        Assert.Null(payload.StationArrival);
    }

    [Fact]
    public void FacingOrientationHandlesParallelAndAntiparallelCases()
    {
        AssertFacing(Quaternion.Identity, new DVec3(0, 0, -1));
        AssertFacing(Quaternion.Identity, new DVec3(0, 0, 1));
        AssertFacing(Quaternion.Identity, DVec3.UnitY);
        AssertFacing(Quaternion.Identity, -DVec3.UnitY);
    }

    [Fact]
    public void FacingOrientationMatchesShipCameraAndRenderedMeshConventions()
    {
        DVec3[] directions =
        [
            DVec3.UnitX,
            -DVec3.UnitX,
            DVec3.UnitY,
            -DVec3.UnitY,
            DVec3.UnitZ,
            -DVec3.UnitZ,
            new DVec3(0.37, -0.21, 0.91).Normalized(),
        ];

        foreach (DVec3 direction in directions)
            AssertFacing(Quaternion.Identity, direction);
    }

    [Fact]
    public void OppositeDirectionDoesNotPassFacingConvention()
    {
        DVec3 desiredForward = new DVec3(0.37, -0.21, 0.91).Normalized();
        Quaternion orientation = SpaceSimulation.CreateShipFacingOrientation(
            Quaternion.Identity,
            desiredForward);

        Assert.False(ShipForwardPasses(orientation, -desiredForward));
        Assert.False(CameraForwardPasses(orientation, -desiredForward));
        Assert.False(RenderedMeshNosePasses(orientation, -desiredForward));
    }

    [Fact]
    public void MatchShipVelocityToReferenceIsSharedOperation()
    {
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Velocity = new DVec3(1, 2, 3),
        };
        var referenceVelocity = new DVec3(-4, 5, -6);

        SpaceSimulation.MatchShipVelocityToReference(ship, referenceVelocity);

        AssertVecClose(referenceVelocity, ship.Velocity, 0.0);
    }

    [Fact]
    public void RelocationDoesNotPublishXStopComplete()
    {
        var sample = FindStation(station => station.PersistenceId != null);
        var messages = new List<SystemMessage>();
        void Handler(SystemMessage message) => messages.Add(message);

        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            RunRelocation(sample.Star, sample.System, sample.Station.PersistenceId!, OldShipPosition());
            DataBus.Drain();
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }

        Assert.DoesNotContain(messages, message => message.Text == "X-Stop complete");
    }

    [Fact]
    public void StationMapArrivalStateCodeDoesNotReimplementStationRelocation()
    {
        string stateSource = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "States", "SystemSpaceState.cs"));
        string mapSource = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "States", "SystemMapState.cs"));
        string arrivalBlock = stateSource[
            stateSource.IndexOf("else if (p.StationArrival != null)", StringComparison.Ordinal)..
            stateSource.IndexOf("else if (_simulation.ShipState is", StringComparison.Ordinal)];

        Assert.Contains("StationArrival", mapSource);
        Assert.DoesNotContain("TargetStation:", mapSource);
        Assert.DoesNotContain("GetStationPosition", arrivalBlock);
        Assert.DoesNotContain(".GetPosition", arrivalBlock);
        Assert.DoesNotContain("GetStationVelocity", arrivalBlock);
        Assert.DoesNotContain("OrbitParent", arrivalBlock);
        Assert.DoesNotContain("QuatLookAt", arrivalBlock);
        Assert.DoesNotContain("TeleportShip", arrivalBlock);
        Assert.DoesNotContain("EclipticToGalaxy", arrivalBlock);
        Assert.DoesNotContain("stationGalaxy", arrivalBlock);
        Assert.DoesNotContain("spawnOri", arrivalBlock);
    }

    [Fact]
    public void RelocationSnapshotIsNotDisturbedByHeldInput()
    {
        var sample = FindStation(station => station.PersistenceId != null);
        var noisyInput = PlayerInput.Zero with
        {
            PitchInput = 2.0,
            YawInput = -1.0,
            RollInput = 1.0,
            ThrustForward = 1.0,
            XStopToggle = true,
            AfterburnerToggle = true,
        };

        var result = RunRelocation(
            sample.Star,
            sample.System,
            sample.Station.PersistenceId!,
            OldShipPosition(),
            noisyInput);
        DVec3 stationGalaxy = EclipticToGalaxy(
            sample.System,
            sample.System.GetStationPosition(sample.Station, result.Snapshot.SimTime));
        double facingDot = DVec3.Dot(
            result.Snapshot.Forward,
            (stationGalaxy - result.Snapshot.Position).Normalized());

        Assert.True(facingDot >= 0.9999, $"Ship forward dot to station was {facingDot:R}");
        AssertVecClose(result.Snapshot.ReferenceVelocity, result.Snapshot.Velocity, 1e-6);
        Assert.InRange(result.Snapshot.RelativeSpeedMs, 0.0, 1e-6);
    }

    [Fact]
    public void RelocationAndXStopUseSharedVelocityMatchAndNoStationParentTraversal()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "SpaceSimulation.cs"));

        Assert.Contains("MatchShipVelocityToReference(ship, refVel);", source);
        Assert.Contains("MatchShipVelocityToReference(ship, GetRefVelocity());", source);
        Assert.DoesNotContain("ship.Velocity = refVel;", source);
        Assert.DoesNotContain("OrbitParent", source);
    }

    [Fact]
    public void GenericTeleportPathStillZeroesVelocity()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Inferior.Game", "SpaceSimulation.cs"));

        Assert.Contains("private sealed record TeleportRequest(DVec3 Position, Quaternion Orientation);", source);
        Assert.Contains("var teleport = _teleportRequest;", source);
        Assert.Contains("ship.Position = teleport.Position;", source);
        Assert.Contains("ship.Velocity = DVec3.Zero;", source);
        Assert.Contains("ship.SetOrientation(teleport.Orientation);", source);
    }

    private static void AssertRelocatedToStation(StationSample sample)
    {
        var result = RunRelocation(sample.Star, sample.System, sample.Station.PersistenceId!, OldShipPosition());
        var snapshot = result.Snapshot;
        var diagnostic = result.Diagnostic;
        DVec3 stationGalaxy = EclipticToGalaxy(sample.System, sample.System.GetStationPosition(sample.Station, snapshot.SimTime));
        double radius = SpaceSimulation.StationPhysicalRadius(sample.Station);
        double centreDistance = (snapshot.Position - stationGalaxy).Length;
        double surfaceDistance = centreDistance - radius;
        DVec3 desiredForward = (stationGalaxy - snapshot.Position).Normalized();
        double facingDot = DVec3.Dot(snapshot.Forward, desiredForward);

        Assert.InRange(System.Math.Abs(surfaceDistance - StandOffMeters), 0.0, 0.01);
        Assert.True(facingDot >= 0.9999, $"Ship forward dot to station was {facingDot:R}");
        AssertFiniteNormalized(snapshot.Orientation);
        AssertVecClose(snapshot.ReferenceVelocity, snapshot.Velocity, 1e-6);
        Assert.InRange(snapshot.RelativeSpeedMs, 0.0, 1e-6);
        Assert.Equal(sample.Station.Name, snapshot.ReferenceName);
        Assert.Equal("station:" + sample.Station.Name, snapshot.ReferenceSourceId);

        Assert.Equal(snapshot.SimTime, diagnostic.SnapshotSimTime);
        AssertVecClose(snapshot.Position, diagnostic.SnapshotShipPosition, 0.0);
        Assert.Equal(sample.Station.PersistenceId, diagnostic.NearestStationId);
        Assert.InRange(System.Math.Abs(diagnostic.SurfaceDistance - StandOffMeters), 0.0, 0.01);
        Assert.Equal(snapshot.LkmZone, diagnostic.PublishedLkmZone);
        Assert.Equal(snapshot.LkmMaxGear, diagnostic.PublishedMaxGearIndex);
    }

    private static RelocationResult RunRelocation(
        Star star,
        StarSystem system,
        string stationPersistenceId,
        DVec3 oldShipPosition,
        PlayerInput? input = null,
        double preAdvanceSeconds = 0.0)
    {
        GameClock.Reset();
        DataBus.Drain();

        var simulation = new SpaceSimulation();
        var ship = new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = oldShipPosition,
            Velocity = new DVec3(123.0, -456.0, 789.0),
        };

        simulation.SetShip(ship);
        simulation.InstallSystem(star, system);
        if (preAdvanceSeconds > 0.0)
            simulation.TickForTests(PlayerInput.Zero, preAdvanceSeconds);
        simulation.RequestStationRelocation(stationPersistenceId, StandOffMeters);
        simulation.TickForTests(input ?? PlayerInput.Zero, 1.0 / 60.0);

        Assert.NotNull(simulation.ShipState);
        Assert.NotNull(simulation.LastStationProximityTickDiagnostic);
        return new RelocationResult(simulation.ShipState!, simulation.LastStationProximityTickDiagnostic!);
    }

    private static void AssertFacing(Quaternion currentOrientation, DVec3 desiredForward)
    {
        Quaternion orientation = SpaceSimulation.CreateShipFacingOrientation(currentOrientation, desiredForward);

        Assert.True(ShipForwardPasses(orientation, desiredForward), "Ship.Forward did not match desired forward.");
        Assert.True(CameraForwardPasses(orientation, desiredForward), "Camera forward did not match desired forward.");
        Assert.True(RenderedMeshNosePasses(orientation, desiredForward), "Rendered mesh nose did not match desired forward.");
        AssertFiniteNormalized(orientation);
    }

    private static bool ShipForwardPasses(Quaternion orientation, DVec3 desiredForward)
    {
        var ship = new Ship();
        ship.SetOrientation(orientation);
        double dot = DVec3.Dot(ship.Forward, desiredForward.Normalized());
        return dot >= 0.9999;
    }

    private static bool CameraForwardPasses(Quaternion orientation, DVec3 desiredForward)
    {
        var camera = new Camera3D(DVec3.Zero, 16f / 9f);
        camera.SetPose(DVec3.Zero, orientation);
        var forward = new DVec3(camera.Forward.X, camera.Forward.Y, camera.Forward.Z);
        double dot = DVec3.Dot(forward.Normalized(), desiredForward.Normalized());
        return dot >= 0.9999;
    }

    private static bool RenderedMeshNosePasses(Quaternion orientation, DVec3 desiredForward)
    {
        var meshNoseV = Vector3.Transform(
            Vector3.UnitZ,
            Matrix.CreateRotationY(MathF.PI) * Matrix.CreateFromQuaternion(orientation));
        var meshNose = new DVec3(meshNoseV.X, meshNoseV.Y, meshNoseV.Z);
        double dot = DVec3.Dot(meshNose.Normalized(), desiredForward.Normalized());
        return dot >= 0.9999;
    }

    private static StationSample FindStation(Func<Station, bool> predicate)
        => FindStation((_, station) => predicate(station));

    private static StationSample FindStation(Func<StarSystem, Station, bool> predicate)
    {
        foreach (var star in GalaxyGenerator.Generate())
        {
            var system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            foreach (var station in system.Stations)
            {
                if (station.PersistenceId != null && predicate(system, station))
                    return new StationSample(star, system, station);
            }
        }

        throw new InvalidOperationException("No generated station matched the requested case.");
    }

    private static DVec3 OldShipPosition()
        => new(1.0e9, 2.0e9, -3.0e9);

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

    private static void AssertFiniteNormalized(Quaternion quaternion)
    {
        Assert.True(float.IsFinite(quaternion.X));
        Assert.True(float.IsFinite(quaternion.Y));
        Assert.True(float.IsFinite(quaternion.Z));
        Assert.True(float.IsFinite(quaternion.W));

        double length = System.Math.Sqrt(
            quaternion.X * quaternion.X
            + quaternion.Y * quaternion.Y
            + quaternion.Z * quaternion.Z
            + quaternion.W * quaternion.W);
        Assert.InRange(System.Math.Abs(length - 1.0), 0.0, 1e-5);
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

    private sealed record StationSample(Star Star, StarSystem System, Station Station);
    private sealed record RelocationResult(
        SpaceSimulation.ShipSnapshot Snapshot,
        SpaceSimulation.StationProximityTickDiagnostic Diagnostic);
}
