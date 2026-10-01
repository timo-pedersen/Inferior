using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Core.Simulation;
using Inferior.Core.World;
using Inferior.Galaxy;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class WorldObjectGravityTests
{
    [Fact]
    public void GeneratedMoonAndStationPeriodsMatchTheirParentGravity()
    {
        Star star = CreateStar();
        StarSystem system = FindSystem(
            star,
            candidate => candidate.Planets.Any(planet => planet.Children.Count > 0)
                      && candidate.Stations.Count > 0);

        foreach (OrbitalBody planet in system.Planets)
        {
            foreach (OrbitalBody moon in planet.Children)
            {
                double expected = Units.OrbitalPeriod(moon.OrbitalRadius, planet.MassKg);
                AssertRelativeClose(expected, moon.Period, 2e-15);
            }
        }

        foreach (Station station in system.Stations)
        {
            double parentMass = station.OrbitParent?.MassKg ?? star.MassKg;
            double expected = Units.OrbitalPeriod(station.OrbitalRadius, parentMass);
            AssertRelativeClose(expected, station.Period, 2e-15);
        }
    }

    [Fact]
    public void RailAccelerationMatchesHierarchyForPlanetsMoonsAndStations()
    {
        Star star = CreateStar();
        (StarSystem moonSystem, Station moonStation) = FindStation(star, parentIsMoon: true);
        OrbitalBody moon = Assert.IsType<OrbitalBody>(moonStation.OrbitParent);
        OrbitalBody moonPlanet = Assert.Single(
            moonSystem.Planets,
            planet => planet.Children.Any(child => ReferenceEquals(child, moon)));
        const double time = 128_765.4321;

        DVec3 planetPosition = moonSystem.GetBodyPosition(moonPlanet, time);
        DVec3 expectedPlanet = PointMass(planetPosition, DVec3.Zero, star.MassKg);
        AssertVecClose(expectedPlanet, moonSystem.GetRailAcceleration(moonPlanet, time), 1e-13);

        DVec3 moonPosition = moonSystem.GetBodyPosition(moon, time);
        DVec3 expectedMoon = expectedPlanet + PointMass(moonPosition, planetPosition, moonPlanet.MassKg);
        AssertVecClose(expectedMoon, moonSystem.GetRailAcceleration(moon, time), 1e-13);

        DVec3 moonStationPosition = moonSystem.GetStationPosition(moonStation, time);
        DVec3 expectedMoonStation = expectedMoon
            + PointMass(moonStationPosition, moonPosition, moon.MassKg);
        AssertVecClose(
            expectedMoonStation,
            moonSystem.GetStationRailAcceleration(moonStation, time),
            1e-13);

        (StarSystem planetSystem, Station planetStation) = FindStation(star, parentIsMoon: false);
        OrbitalBody planetParent = Assert.IsType<OrbitalBody>(planetStation.OrbitParent);
        DVec3 parentPosition = planetSystem.GetBodyPosition(planetParent, time);
        DVec3 expectedParent = PointMass(parentPosition, DVec3.Zero, star.MassKg);
        DVec3 planetStationPosition = planetSystem.GetStationPosition(planetStation, time);
        DVec3 expectedPlanetStation = expectedParent
            + PointMass(planetStationPosition, parentPosition, planetParent.MassKg);
        AssertVecClose(
            expectedPlanetStation,
            planetSystem.GetStationRailAcceleration(planetStation, time),
            1e-13);
    }

    [Fact]
    public void ZeroOffsetParticleTracksStationRailAndConvergesWithTimestep()
    {
        Star star = CreateStar();
        (StarSystem system, Station station) = FindStation(star, parentIsMoon: false);

        TrajectoryErrors atSixtyHertz = IntegrateStationMatchedParticle(
            system,
            station,
            1.0 / 60.0,
            6.0 * Units.HourInSeconds);
        TrajectoryErrors atThirtyHertz = IntegrateStationMatchedParticle(
            system,
            station,
            1.0 / 30.0,
            6.0 * Units.HourInSeconds);

        Assert.InRange(atSixtyHertz.AfterTenMinutes, 0.0, 2.0);
        Assert.InRange(atSixtyHertz.AfterOneHour, 0.0, 10.0);
        Assert.InRange(atSixtyHertz.AfterSixHours, 0.0, 100.0);
        Assert.True(
            atSixtyHertz.AfterSixHours < atThirtyHertz.AfterSixHours,
            $"Expected 60 Hz error ({atSixtyHertz.AfterSixHours:R} m) to improve on 30 Hz " +
            $"({atThirtyHertz.AfterSixHours:R} m). 10 min={atSixtyHertz.AfterTenMinutes:R} m, " +
            $"1 h={atSixtyHertz.AfterOneHour:R} m.");
    }

    [Fact]
    public void SimulationEvaluatesAccelerationAtTheObjectsCurrentStateTime()
    {
        GameClock.Reset();
        Star star = CreateStar();
        StarSystem system = FindSystem(star, candidate => candidate.Stations.Count > 0);
        SpaceSimulation simulation = CreateSimulation(star, system);

        const double initialStep = 1.0 / 60.0;
        simulation.TickForTests(PlayerInput.Zero, initialStep);
        SpaceSimulation.SimulationPresentationSnapshot beforePresentation =
            Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState);
        WorldObjectSnapshot before = beforePresentation.World.Objects[0];
        OrbitalBody? currentParent = simulation.DebugGetWorldObjectDynamicsParent(before.Id);

        const double integrationStep = 60.0;
        double stateTime = beforePresentation.SimTime;
        DVec3 eclipticPosition = CoordinateTransforms.GalaxyToEcliptic(
            before.Position,
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);
        OrbitalBody? selectedParent = system.SelectDynamicsParent(
            eclipticPosition,
            currentParent,
            stateTime);
        DVec3 acceleration = CoordinateTransforms.EclipticToGalaxy(
            system.GetRailCoherentAcceleration(eclipticPosition, selectedParent, stateTime),
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);
        DVec3 expectedVelocity = before.LinearVelocity + acceleration * integrationStep;
        DVec3 expectedPosition = before.Position + expectedVelocity * integrationStep;

        DVec3 wrongTimeAcceleration = CoordinateTransforms.EclipticToGalaxy(
            system.GetRailCoherentAcceleration(
                eclipticPosition,
                selectedParent,
                stateTime + integrationStep),
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);
        DVec3 wrongTimeVelocity = before.LinearVelocity + wrongTimeAcceleration * integrationStep;

        simulation.TickForTests(PlayerInput.Zero, integrationStep);
        WorldObjectSnapshot after = Assert.Single(
            Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState)
                .World.Objects,
            item => item.Id == before.Id);

        Assert.Equal(stateTime, simulation.DebugLastWorldObjectAccelerationStateTime, 12);
        AssertVecClose(expectedVelocity, after.LinearVelocity, 1e-9);
        AssertVecClose(expectedPosition, after.Position, 1e-4);
        Assert.True(
            DVec3.Distance(after.LinearVelocity, wrongTimeVelocity) > 1e-6,
            "The chosen sample must distinguish current-state time from the already-advanced clock.");
    }

    [Fact]
    public void DynamicsParentHillHysteresisIsScaleFreeAndKinematicallyContinuous()
    {
        Star star = CreateStar();
        StarSystem system = FindSystem(
            star,
            candidate => candidate.Planets.Any(planet => planet.Children.Count > 0));
        OrbitalBody planet = system.Planets.First(candidate => candidate.Children.Count > 0);
        OrbitalBody moon = planet.Children[0];
        const double time = 42_000.0;
        DVec3 moonPosition = system.GetBodyPosition(moon, time);
        DVec3 velocity = new(123.0, -45.0, 67.0);

        DVec3 beforeEnter = moonPosition
            + DVec3.UnitY * (moon.HillSphereRadius * 0.95);
        Assert.Same(planet, system.SelectDynamicsParent(beforeEnter, planet, time));

        DVec3 enteredPosition = moonPosition
            + DVec3.UnitY * (moon.HillSphereRadius * 0.89);
        var state = new WorldObject(
            WorldObjectId.New(),
            enteredPosition,
            Quaternion.Identity,
            velocity,
            DVec3.Zero);
        DVec3 positionBeforeTransition = state.Position;
        DVec3 velocityBeforeTransition = state.LinearVelocity;
        OrbitalBody? entered = system.SelectDynamicsParent(state.Position, planet, time);
        Assert.Same(moon, entered);
        Assert.Equal(positionBeforeTransition, state.Position);
        Assert.Equal(velocityBeforeTransition, state.LinearVelocity);

        DVec3 retainedPosition = moonPosition
            + DVec3.UnitY * (moon.HillSphereRadius * 1.05);
        Assert.Same(moon, system.SelectDynamicsParent(retainedPosition, entered, time));

        DVec3 exitedPosition = moonPosition
            + DVec3.UnitY * (moon.HillSphereRadius * 1.11);
        Assert.Same(planet, system.SelectDynamicsParent(exitedPosition, moon, time));

        Assert.Equal(0.90, StarSystem.DynamicsParentEnterHillFraction);
        Assert.Equal(1.10, StarSystem.DynamicsParentExitHillFraction);
    }

    [Fact]
    public void StationReleasePointVelocityIncludesOmegaCrossOffset()
    {
        Star star = CreateStar();
        StarSystem system = FindSystem(star, candidate => candidate.Stations.Count > 0);
        Station station = system.Stations[0];
        const double time = 9_876.5;
        DVec3 localOffset = new(350.0, 17.0, -125.0);
        DVec3 worldOffset = StarSystem.GetStationWorldOffset(station, localOffset, time);
        DVec3 expected = system.GetStationVelocity(station, time)
            + DVec3.Cross(DVec3.UnitY * station.SlowRotation, worldOffset);

        DVec3 actual = system.GetStationPointVelocity(station, localOffset, time);
        // Station orientation is stored as a float quaternion, so use a wide enough
        // central-difference interval to stay above float phase quantization.
        const double derivativeStep = 10.0;
        DVec3 finiteDifferencePointVelocity = system.GetStationVelocity(station, time)
            + (StarSystem.GetStationWorldOffset(station, localOffset, time + derivativeStep)
               - StarSystem.GetStationWorldOffset(station, localOffset, time - derivativeStep))
            / (2.0 * derivativeStep);

        AssertVecClose(expected, actual, 1e-12);
        AssertVecClose(finiteDifferencePointVelocity, actual, 1e-5);
        Assert.True(DVec3.Distance(actual, system.GetStationVelocity(station, time)) > 0.0);
    }

    [Fact]
    public void StationSpawnInheritsDynamicsParentAndPointVelocity()
    {
        GameClock.Reset();
        Star star = CreateStar();
        (StarSystem system, Station station) = FindStation(star, parentIsMoon: false);
        SpaceSimulation simulation = CreateSimulation(star, system);
        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);
        SpaceSimulation.SimulationPresentationSnapshot presentation =
            Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState);

        WorldObjectId id = WorldObjectId.CreateDeterministic(
            "shipping-container",
            $"system:{star.GalaxyIndex}|station:{station.PersistenceId}|index:0");
        WorldObjectSnapshot state = Assert.Single(
            presentation.World.Objects,
            candidate => candidate.Id == id);
        Assert.Same(station.OrbitParent, simulation.DebugGetWorldObjectDynamicsParent(id));

        double time = presentation.SimTime;
        DVec3 stationPosition = system.GetStationPosition(station, time);
        DVec3 objectPosition = CoordinateTransforms.GalaxyToEcliptic(
            state.Position,
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);
        DVec3 objectVelocity = CoordinateTransforms.GalaxyToEcliptic(
            state.LinearVelocity,
            system.EclipticTiltAzimuthRadians,
            system.EclipticTiltRadians);
        DVec3 expectedVelocity = system.GetStationVelocity(station, time)
            + DVec3.Cross(
                DVec3.UnitY * station.SlowRotation,
                objectPosition - stationPosition);
        AssertVecClose(expectedVelocity, objectVelocity, 1e-8);
    }

    [Fact]
    public void XStopReferenceSelectionCannotAffectWorldObjectTrajectory()
    {
        IReadOnlyList<WorldObjectSnapshot> baseline = RunSimulationWithReference(DVec3.Zero);
        IReadOnlyList<WorldObjectSnapshot> changedReference = RunSimulationWithReference(
            new DVec3(125_000.0, -75_000.0, 25_000.0));

        Assert.Equal(baseline.Count, changedReference.Count);
        foreach (WorldObjectSnapshot expected in baseline)
        {
            WorldObjectSnapshot actual = Assert.Single(
                changedReference,
                item => item.Id == expected.Id);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.LinearVelocity, actual.LinearVelocity);
            Assert.Equal(expected.Orientation, actual.Orientation);
            Assert.Equal(expected.AngularVelocity, actual.AngularVelocity);
        }
    }

    private static IReadOnlyList<WorldObjectSnapshot> RunSimulationWithReference(
        DVec3 referenceVelocity)
    {
        GameClock.Reset();
        Star star = CreateStar();
        StarSystem system = FindSystem(star, candidate => candidate.Stations.Count > 0);
        SpaceSimulation simulation = CreateSimulation(star, system);
        simulation.DebugSetReferenceVelocity(referenceVelocity);
        for (int i = 0; i < 121; i++)
            simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);

        return Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState)
            .World.Objects;
    }

    private static SpaceSimulation CreateSimulation(Star star, StarSystem system)
    {
        var simulation = new SpaceSimulation();
        simulation.SetShip(new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = new DVec3(0.0, 0.5e11, 3.0e11),
        });
        simulation.InstallSystem(star, system);
        return simulation;
    }

    private static TrajectoryErrors IntegrateStationMatchedParticle(
        StarSystem system,
        Station station,
        double dt,
        double duration)
    {
        DVec3 position = system.GetStationPosition(station, 0.0);
        DVec3 velocity = system.GetStationVelocity(station, 0.0);
        double time = 0.0;
        double tenMinuteError = double.NaN;
        double oneHourError = double.NaN;
        int stepCount = (int)System.Math.Round(duration / dt);

        for (int step = 0; step < stepCount; step++)
        {
            DVec3 acceleration = system.GetRailCoherentAcceleration(
                position,
                station.OrbitParent,
                time);
            velocity += acceleration * dt;
            position += velocity * dt;
            time += dt;

            if (step + 1 == (int)System.Math.Round(600.0 / dt))
                tenMinuteError = DVec3.Distance(position, system.GetStationPosition(station, time));
            if (step + 1 == (int)System.Math.Round(Units.HourInSeconds / dt))
                oneHourError = DVec3.Distance(position, system.GetStationPosition(station, time));
        }

        return new TrajectoryErrors(
            tenMinuteError,
            oneHourError,
            DVec3.Distance(position, system.GetStationPosition(station, time)));
    }

    private static (StarSystem System, Station Station) FindStation(
        Star star,
        bool parentIsMoon)
    {
        for (int seed = 1; seed <= 2_000; seed++)
        {
            StarSystem system = StarSystem.Generate(star, new SeededRandom(seed));
            foreach (Station station in system.Stations)
            {
                if (station.OrbitParent == null)
                    continue;

                bool isMoon = system.Planets.Any(
                    planet => planet.Children.Any(
                        child => ReferenceEquals(child, station.OrbitParent)));
                if (isMoon == parentIsMoon)
                    return (system, station);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a generated station with parentIsMoon={parentIsMoon}.");
    }

    private static StarSystem FindSystem(Star star, Func<StarSystem, bool> predicate)
    {
        for (int seed = 1; seed <= 2_000; seed++)
        {
            StarSystem system = StarSystem.Generate(star, new SeededRandom(seed));
            if (predicate(system))
                return system;
        }

        throw new InvalidOperationException("Could not find a generated system matching the test predicate.");
    }

    private static Star CreateStar() => new()
    {
        GalaxyIndex = 8_181,
        Name = "W1g Test Star",
        MassKg = Units.SolarMass,
        RadiusMeters = Units.SolarRadius,
        Luminosity = 1.0,
        Temperature = 5_772.0,
        SpectralClass = SpectralClass.G,
    };

    private static DVec3 PointMass(DVec3 position, DVec3 sourcePosition, double massKg)
    {
        DVec3 delta = sourcePosition - position;
        double distance = delta.Length;
        return delta * (Units.G * massKg / (distance * distance * distance));
    }

    private static void AssertRelativeClose(double expected, double actual, double tolerance)
        => Assert.InRange(
            System.Math.Abs(expected - actual) / System.Math.Max(System.Math.Abs(expected), 1.0),
            0.0,
            tolerance);

    private static void AssertVecClose(DVec3 expected, DVec3 actual, double tolerance)
    {
        Assert.InRange(System.Math.Abs(expected.X - actual.X), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Y - actual.Y), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Z - actual.Z), 0.0, tolerance);
    }

    private readonly record struct TrajectoryErrors(
        double AfterTenMinutes,
        double AfterOneHour,
        double AfterSixHours);
}
