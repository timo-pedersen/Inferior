using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Inferior.Gameplay.Engines;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class EngineExhaustGlowTests
{
    [Fact]
    public void Definitions_ProvideEngineSpecificGlowAndExhaustAnchors()
    {
        EngineDefinition mule = MuleEngineDefinitionFactory.CreateDefinition();
        EngineDefinition needle = NeedleEngineDefinitionFactory.CreateDefinition();
        EngineExhaustDefinition muleAnchor =
            Assert.Single(mule.VisualGeometry!.Exhausts);
        EngineExhaustDefinition needleAnchor =
            Assert.Single(needle.VisualGeometry!.Exhausts);

        Assert.Equal(new DVec3(1.0, 0.24, 0.035), mule.VisualDefinition!.GlowColour);
        Assert.Equal(0.15f, mule.VisualDefinition.IdleIntensity);
        Assert.Equal(2.0f, mule.VisualDefinition.BoostIntensity);
        Assert.Equal(new DVec3(0.0, 0.0, 3.80), muleAnchor.Position);
        Assert.Equal(0.50, muleAnchor.RadiusMeters);
        Assert.Equal(DVec3.UnitZ, muleAnchor.Direction);

        Assert.Equal(new DVec3(0.48, 0.82, 1.0), needle.VisualDefinition!.GlowColour);
        Assert.Equal(0.10f, needle.VisualDefinition.IdleIntensity);
        Assert.Equal(3.0f, needle.VisualDefinition.BoostIntensity);
        Assert.Equal(new DVec3(-0.04, 0.12, 3.62), needleAnchor.Position);
        Assert.Equal(0.50, needleAnchor.RadiusMeters);
        Assert.Equal(DVec3.UnitZ, needleAnchor.Direction);
    }

    [Fact]
    public void SimulationDerivesIdleAndForwardThrottleForBothEngines()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);

        simulation.DebugTickPhysics(PlayerInput.Zero, 1.0 / 60.0);
        AssertVisualState(ship, EngineVisualState.Idle);
        AssertSnapshotState(simulation, EngineVisualState.Idle);

        simulation.DebugTickPhysics(
            PlayerInput.Zero with { ThrustForward = 0.4 },
            1.0 / 60.0);

        var expected = new EngineVisualState(EngineVisualMode.Thrust, 0.4f);
        AssertVisualState(ship, expected);
        AssertSnapshotState(simulation, expected);
    }

    [Fact]
    public void DirectionalCommandsDriveThrustModeIndependentOfVelocity()
    {
        var commands = new[]
        {
            (PlayerInput.Zero with { ThrustForward = 1.0 }, 1f),
            (PlayerInput.Zero with { ThrustForward = -1.0 }, 1f),
            (PlayerInput.Zero with { ThrustLateral = 0.6 }, 0.6f),
            (PlayerInput.Zero with { ThrustVertical = -0.7 }, 0.7f),
        };

        foreach ((PlayerInput input, float expectedOutput) in commands)
        {
            var simulation = new SpaceSimulation();
            Ship ship = ShipBuilder.NewShip("type-1").Build();
            ship.Velocity = ship.Forward
                * MuleEngineDefinitionFactory.CreateDefinition().MinimumSpeedCeilingMps;
            simulation.SetShip(ship);
            simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);

            simulation.DebugTickPhysics(input, 1.0 / 60.0);

            AssertVisualState(
                ship,
                new EngineVisualState(EngineVisualMode.Thrust, expectedOutput));
        }
    }

    [Fact]
    public void OnlyActiveMovingXStopUsesVelocityCorrectionMode()
    {
        var xStopSimulation = new SpaceSimulation();
        Ship xStopShip = ShipBuilder.NewShip("type-1").Build();
        xStopShip.Velocity = xStopShip.Forward * 10.0;
        xStopSimulation.SetShip(xStopShip);
        xStopSimulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);
        xStopSimulation.DebugTickPhysics(
            PlayerInput.Zero with
            {
                XStopToggle = true,
                XStopToggleSequence = 1
            },
            1.0 / 60.0);
        AssertVisualState(xStopShip, EngineVisualState.VelocityCorrection);

        var stoppedSimulation = new SpaceSimulation();
        Ship stoppedShip = ShipBuilder.NewShip("type-1").Build();
        stoppedSimulation.SetShip(stoppedShip);
        stoppedSimulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);
        stoppedSimulation.DebugTickPhysics(
            PlayerInput.Zero with
            {
                XStopToggle = true,
                XStopToggleSequence = 1
            },
            1.0 / 60.0);
        AssertVisualState(stoppedShip, EngineVisualState.Idle);
    }

    [Fact]
    public void SimulationDerivesAfterburnerBoostState()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);

        simulation.DebugTickPhysics(
            PlayerInput.Zero with { AfterburnerToggle = true },
            1.0 / 60.0);

        AssertVisualState(ship, EngineVisualState.Boost);
        AssertSnapshotState(simulation, EngineVisualState.Boost);
    }

    [Fact]
    public void VisualState_RemainsIndependentPerEngineInstance()
    {
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        EngineInstance[] engines = InstalledEngines(ship);

        engines[0].SetVisualState(EngineVisualState.Boost);

        Assert.Equal(EngineVisualState.Boost, engines[0].VisualState);
        Assert.Equal(EngineVisualState.Idle, engines[1].VisualState);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            engines[0].SetVisualState(
                new EngineVisualState(EngineVisualMode.Thrust, 1.1f)));
    }

    [Fact]
    public void ReplacementPairReceivesCurrentSimulationStateInSameTick()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);

        simulation.RequestDebugCycleEngineConfiguration();
        simulation.DebugTickPhysics(
            PlayerInput.Zero with { ThrustForward = 0.5 },
            1.0 / 60.0);

        EngineInstance[] engines = InstalledEngines(ship);
        Assert.All(engines, engine =>
            Assert.Equal(NeedleEngineDefinitionFactory.H2VariantId, engine.Variant.VariantId));
        Assert.All(engines, engine =>
            Assert.Equal(
                new EngineVisualState(EngineVisualMode.Thrust, 0.5f),
                engine.VisualState));
    }

    [Fact]
    public void GlowDraws_UseMirroredEngineAnchorsAndDisappearWithEngines()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1")
            .WithEngineVariant(NeedleEngineDefinitionFactory.H2VariantId)
            .Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.Docked);
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        IReadOnlyList<EngineExhaustGlowDraw> draws =
            ShipMeshRenderer.BuildEngineExhaustGlowDraws(
                simulation.ShipState!.EngineMounts!,
                Matrix.Identity,
                metresToRenderScale: 1f);

        Assert.Equal(2, draws.Count);
        Assert.All(draws, draw => Assert.Equal(0.50f, draw.Radius));
        Assert.Equal(-draws[1].Center.X, draws[0].Center.X, 5);
        Assert.Equal(draws[1].Center.Y, draws[0].Center.Y, 5);
        Assert.Equal(draws[1].Center.Z, draws[0].Center.Z, 5);
        Assert.All(draws, draw => Assert.Equal(EngineVisualState.Idle, draw.VisualState));

        simulation.RequestDebugCycleEngineConfiguration();
        simulation.DebugTickPhysics(PlayerInput.Zero, 0.0);

        Assert.Empty(ShipMeshRenderer.BuildEngineExhaustGlowDraws(
            simulation.ShipState!.EngineMounts!,
            Matrix.Identity,
            metresToRenderScale: 1f));
    }

    [Fact]
    public void RemovedEngineHasNoSnapshotWhileRemainingEngineTracksState()
    {
        var simulation = new SpaceSimulation();
        Ship ship = ShipBuilder.NewShip("type-1").Build();
        simulation.SetShip(ship);
        simulation.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);
        simulation.RequestDebugRemoveEngine(EngineMountSide.Port);
        simulation.DebugTickPhysics(
            PlayerInput.Zero with { ThrustForward = 0.6 },
            1.0 / 60.0);

        EngineMountPresentationSnapshot[] mounts =
            simulation.ShipState!.EngineMounts!.ToArray();
        Assert.Null(mounts.Single(mount =>
            mount.Side == EngineMountSide.Port).InstalledEngine);
        EnginePresentationSnapshot remaining = Assert.IsType<EnginePresentationSnapshot>(
            mounts.Single(mount =>
                mount.Side == EngineMountSide.Starboard).InstalledEngine);
        Assert.Equal(
            new EngineVisualState(EngineVisualMode.Thrust, 0.6f),
            remaining.VisualState);
    }

    private static void AssertVisualState(Ship ship, EngineVisualState expected)
        => Assert.All(InstalledEngines(ship), engine =>
            Assert.Equal(expected, engine.VisualState));

    private static void AssertSnapshotState(
        SpaceSimulation simulation,
        EngineVisualState expected)
    {
        EnginePresentationSnapshot[] engines = simulation.ShipState!.EngineMounts!
            .Select(mount => mount.InstalledEngine)
            .OfType<EnginePresentationSnapshot>()
            .ToArray();
        Assert.Equal(2, engines.Length);
        Assert.All(engines, engine => Assert.Equal(expected, engine.VisualState));
    }

    private static EngineInstance[] InstalledEngines(Ship ship)
    {
        EngineInstance[] engines = ship.EngineMounts
            .Select(mount => mount.InstalledEngine)
            .OfType<EngineInstance>()
            .ToArray();
        Assert.Equal(2, engines.Length);
        Assert.NotSame(engines[0], engines[1]);
        return engines;
    }
}
