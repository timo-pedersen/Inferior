using Inferior.Core.Math;
using Inferior.Core.Random;
using Inferior.Core.Simulation;
using Inferior.Core.World;
using Inferior.Galaxy;
using Inferior.Gameplay;
using Inferior.Gameplay.Hull;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using Xunit;

namespace Inferior.Game.Test;

public sealed class WorldObjectFoundationTests
{
    [Fact]
    public void IdentitySupportsUniqueAndDeterministicCreation()
    {
        WorldObjectId first = WorldObjectId.New();
        WorldObjectId second = WorldObjectId.New();
        WorldObjectId deterministicA = WorldObjectId.CreateDeterministic("test", "alpha");
        WorldObjectId deterministicB = WorldObjectId.CreateDeterministic("test", "alpha");
        WorldObjectId deterministicOther = WorldObjectId.CreateDeterministic("test", "beta");

        Assert.NotEqual(first, second);
        Assert.Equal(deterministicA, deterministicB);
        Assert.Equal(deterministicA.GetHashCode(), deterministicB.GetHashCode());
        Assert.NotEqual(deterministicA, deterministicOther);
    }

    [Fact]
    public void RegistryAddsLooksUpAndRemovesTheIntendedObject()
    {
        var registry = new WorldObjectRegistry();
        WorldObjectId firstId = WorldObjectId.New();
        WorldObjectId secondId = WorldObjectId.New();
        var first = Object(firstId, new DVec3(1, 2, 3), DVec3.Zero);
        var second = Object(secondId, new DVec3(4, 5, 6), DVec3.Zero);

        Assert.True(registry.Add(first));
        Assert.True(registry.Add(second));
        Assert.False(registry.Add(first));
        Assert.True(registry.TryGet(secondId, out WorldObject? found));
        Assert.Same(second, found);

        Assert.True(registry.Remove(secondId));
        Assert.False(registry.TryGet(secondId, out _));
        Assert.False(registry.Remove(secondId));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void FreeMotionIntegratesPositionAndUniverseSpaceAngularVelocity()
    {
        var registry = new WorldObjectRegistry();
        var worldObject = new WorldObject(
            WorldObjectId.New(),
            new DVec3(10, -20, 30),
            Quaternion.Identity,
            new DVec3(4, 5, -6),
            new DVec3(0, 2, 0));
        registry.Add(worldObject);

        registry.IntegrateFreeMotion(0.25);

        Assert.Equal(new DVec3(11, -18.75, 28.5), worldObject.Position);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        AssertQuaternionClose(expected, worldObject.Orientation, 1e-6f);
    }

    [Fact]
    public void SnapshotIsAValueCopyRatherThanMutableSimulationState()
    {
        var registry = new WorldObjectRegistry();
        WorldObjectId id = WorldObjectId.New();
        registry.Add(Object(id, new DVec3(1, 2, 3), new DVec3(10, 0, 0)));

        IReadOnlyList<WorldObjectSnapshot> before = registry.CreateSnapshot();
        registry.IntegrateFreeMotion(2.0);
        IReadOnlyList<WorldObjectSnapshot> after = registry.CreateSnapshot();

        Assert.Equal(new DVec3(1, 2, 3), before[0].Position);
        Assert.Equal(new DVec3(21, 2, 3), after[0].Position);
        Assert.DoesNotContain(
            typeof(WorldObject),
            typeof(WorldObjectSnapshot).GetProperties().Select(property => property.PropertyType));
    }

    [Fact]
    public void SpaceSimulationPublishesIndependentContainerIdsAndIntegratedTransforms()
    {
        SpaceSimulation simulation = CreateSimulation();

        simulation.TickForTests(PlayerInput.Zero, 1.0 / 60.0);
        SpaceSimulation.SimulationPresentationSnapshot firstPresentation =
            Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState);
        AssertCoherent(firstPresentation);
        SpaceSimulation.WorldPresentationSnapshot first = firstPresentation.World;

        Assert.NotEmpty(first.Containers);
        Assert.Equal(first.Containers.Count, first.Containers.Select(item => item.State.Id).Distinct().Count());
        Assert.Equal(first.Objects.Count, first.Containers.Count);
        foreach (SpaceSimulation.ShippingContainerSnapshot container in first.Containers)
        {
            Assert.Equal(container.State.Id, container.Container.Id);
            WorldObjectSnapshot state = Assert.Single(first.Objects, item => item.Id == container.State.Id);
            Assert.Equal(state.Position, container.State.Position);
            Assert.Equal(state.Orientation, container.State.Orientation);
        }

        SpaceSimulation.ShippingContainerSnapshot firstContainer = first.Containers[0];
        simulation.TickForTests(PlayerInput.Zero, 0.5);
        SpaceSimulation.SimulationPresentationSnapshot secondPresentation =
            Assert.IsType<SpaceSimulation.SimulationPresentationSnapshot>(simulation.PresentationState);
        AssertCoherent(secondPresentation);
        Assert.True(secondPresentation.TickSequence > firstPresentation.TickSequence);
        SpaceSimulation.WorldPresentationSnapshot second = secondPresentation.World;
        SpaceSimulation.ShippingContainerSnapshot moved = Assert.Single(
            second.Containers, item => item.State.Id == firstContainer.State.Id);

        Assert.NotEqual(firstContainer.State.Position, moved.State.Position);
        Assert.NotEqual(firstContainer.State.LinearVelocity, moved.State.LinearVelocity);
        Assert.True(double.IsFinite(moved.State.Position.X));
        Assert.True(double.IsFinite(moved.State.Position.Y));
        Assert.True(double.IsFinite(moved.State.Position.Z));
        Assert.Equal(firstContainer.State.Position, first.Containers[0].State.Position);
        Assert.DoesNotContain(
            typeof(WorldObject),
            typeof(SpaceSimulation.ShippingContainerSnapshot)
                .GetProperties()
                .Select(property => property.PropertyType));
    }

    [Fact]
    public void BackgroundSimulationNeverPublishesAnInternallyTornPresentationGeneration()
    {
        SpaceSimulation simulation = CreateSimulation();
        var observedGenerations = new HashSet<long>();
        int reads = 0;
        var timeout = Stopwatch.StartNew();

        simulation.Start();
        try
        {
            while (timeout.Elapsed < TimeSpan.FromSeconds(2)
                   && (observedGenerations.Count < 5 || reads < 1_000))
            {
                SpaceSimulation.SimulationPresentationSnapshot? presentation =
                    simulation.PresentationState;
                if (presentation != null)
                {
                    AssertCoherent(presentation);
                    observedGenerations.Add(presentation.TickSequence);
                    reads++;
                }

                Thread.Yield();
            }
        }
        finally
        {
            simulation.Stop();
        }

        Assert.True(reads >= 1_000, $"Expected at least 1,000 reads, observed {reads}.");
        Assert.True(
            observedGenerations.Count >= 5,
            $"Expected at least five published generations, observed {observedGenerations.Count}.");
    }

    private static SpaceSimulation CreateSimulation()
    {
        GameClock.Reset();
        var star = new Star
        {
            GalaxyIndex = 4242,
            Name = "World Object Test",
            MassKg = 1.989e30,
            RadiusMeters = 6.957e8,
            Luminosity = 1.0,
            Temperature = 5772,
            SpectralClass = SpectralClass.G,
        };
        StarSystem system = StarSystem.Generate(star, new SeededRandom(8675309));
        var simulation = new SpaceSimulation();
        simulation.SetShip(new Ship
        {
            HullTypeId = AriesHullDefinitionFactory.HullId,
            Position = new DVec3(0, 0.5e11, 3e11),
        });
        simulation.InstallSystem(star, system);
        return simulation;
    }

    private static void AssertCoherent(
        SpaceSimulation.SimulationPresentationSnapshot presentation)
    {
        Assert.Equal(presentation.SimTime, presentation.World.SimTime);
        Assert.Equal(presentation.TickSequence, presentation.World.TickSequence);
        if (presentation.Ship != null)
        {
            Assert.Equal(presentation.SimTime, presentation.Ship.SimTime);
            Assert.Equal(presentation.TickSequence, presentation.Ship.TickSequence);
        }
    }

    private static WorldObject Object(WorldObjectId id, DVec3 position, DVec3 velocity)
        => new(id, position, Quaternion.Identity, velocity, DVec3.Zero);

    private static void AssertVecClose(DVec3 expected, DVec3 actual, double tolerance)
    {
        Assert.InRange(System.Math.Abs(expected.X - actual.X), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Y - actual.Y), 0.0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Z - actual.Z), 0.0, tolerance);
    }

    private static void AssertQuaternionClose(Quaternion expected, Quaternion actual, float tolerance)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0f, tolerance);
        Assert.InRange(MathF.Abs(expected.W - actual.W), 0f, tolerance);
    }
}
