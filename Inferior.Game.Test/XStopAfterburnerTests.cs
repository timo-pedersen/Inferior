using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using Microsoft.Xna.Framework;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Inferior.Game.Test;

public sealed class XStopAfterburnerTests
{
    private const double Dt = 1.0 / 60.0;

    [Fact]
    public void XPressDuringAfterburnerSelectsXStopImmediately()
    {
        var (sim, _) = CreateNewtonianSim();
        EngageAfterburner(sim);

        sim.DebugTickPhysics(XStopEvent(1), Dt);

        Assert.True(sim.DebugXStopState.AfterburnerActive);
        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.Equal(1, sim.DebugXStopState.LastConsumedXStopToggleSequence);
    }

    [Fact]
    public void SnapshotDataBusAndHudSourcePublishSelectedStateDuringAfterburner()
    {
        var (sim, _) = CreateNewtonianSim();
        double published = -1;
        void Handler(double v) => published = v;
        DataBus.Drain();
        DataBus.Instruments.Subscribe(Topics.Flight.XStopActive, Handler);
        try
        {
            EngageAfterburner(sim);
            sim.DebugTickPhysics(XStopEvent(1), Dt);

            Assert.True(sim.ShipState!.XStopActive);
            sim.DebugPublish();
            DataBus.Drain();

            Assert.Equal(1.0, published);
        }
        finally
        {
            DataBus.Instruments.Unsubscribe(Topics.Flight.XStopActive, Handler);
        }
    }

    [Fact]
    public void AfterburnerSuppressesXStopDampingAndVelocitySnap()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = new DVec3(100, 0, 0);
        EngageAfterburner(sim);

        sim.DebugTickPhysics(XStopEvent(1), Dt);

        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.True(sim.DebugXStopState.AfterburnerActive);
        Assert.InRange(ship.Velocity.X, 99.99, 100.01);

        ship.Velocity = new DVec3(0.1, 0, ship.Velocity.Z);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);

        Assert.InRange(ship.Velocity.X, 0.09, 0.11);
    }

    [Fact]
    public void XStopCompleteMessageDoesNotPublishDuringAfterburnerInhibition()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = new DVec3(0.1, 0, 0);
        var messages = new List<string>();
        void Handler(SystemMessage msg) => messages.Add(msg.Text);
        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            EngageAfterburner(sim);
            sim.DebugTickPhysics(XStopEvent(1), Dt);
            DataBus.Drain();

            Assert.DoesNotContain("X-Stop complete", messages);
            Assert.False(sim.DebugXStopState.XStopCompleteAnnounced);
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }
    }

    [Fact]
    public void SelectedXStopBeginsDampingAutomaticallyAfterAfterburnerEnds()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = new DVec3(10_000, 0, 0);
        EngageAfterburner(sim);
        sim.DebugTickPhysics(XStopEvent(1), Dt);

        RunUntilAfterburnerEnds(sim);

        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.False(sim.DebugXStopState.AfterburnerActive);
        double xNoiseTolerance = XStopAxisNoiseTolerance();
        Assert.True(ship.Velocity.X < 10_000 + xNoiseTolerance);
    }

    [Fact]
    public void CancellingXStopDuringAfterburnerPreventsLaterDamping()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = new DVec3(100, 0, 0);
        EngageAfterburner(sim);
        sim.DebugTickPhysics(XStopEvent(1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        sim.DebugTickPhysics(XStopEvent(2), Dt);

        RunUntilAfterburnerEnds(sim);

        Assert.False(sim.DebugXStopState.XStopActive);
        Assert.False(sim.DebugXStopState.AfterburnerActive);
        Assert.InRange(ship.Velocity.X, 99, 101);
    }

    [Fact]
    public void XStopSelectedBeforeAfterburnerRemainsSelectedThroughBurn()
    {
        var (sim, _) = CreateNewtonianSim();
        sim.DebugTickPhysics(XStopEvent(1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);

        EngageAfterburner(sim);
        RunUntilAfterburnerEnds(sim);

        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.False(sim.DebugXStopState.AfterburnerActive);
    }

    [Fact]
    public void ReferenceVelocityChangedDuringBurnIsUsedWhenDampingStarts()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = DVec3.Zero;
        EngageAfterburner(sim);
        sim.DebugTickPhysics(XStopEvent(1), Dt);

        sim.DebugSetReferenceVelocity(new DVec3(10_000, 0, 0));
        RunUntilAfterburnerEnds(sim);

        Assert.True(ship.Velocity.X > -XStopAxisNoiseTolerance());
        Assert.Equal(10_000, sim.DebugXStopState.ReferenceVelocity.X, 12);
    }

    [Fact]
    public void CompletionMessagePublishesOnceAfterRealDampingCompletes()
    {
        var (sim, ship) = CreateNewtonianSim();
        ship.Velocity = new DVec3(0.1, 0, 0);
        int completeCount = 0;
        void Handler(SystemMessage msg)
        {
            if (msg.Text == "X-Stop complete") completeCount++;
        }
        DataBus.Drain();
        DataBus.System.Subscribe(Topics.System.All, Handler);
        try
        {
            sim.DebugTickPhysics(XStopEvent(1), Dt);
            sim.DebugTickPhysics(PlayerInput.Zero, Dt);
            sim.DebugTickPhysics(PlayerInput.Zero, Dt);
            DataBus.Drain();

            Assert.True(sim.DebugXStopState.XStopCompleteAnnounced);
            Assert.Equal(1, completeCount);
        }
        finally
        {
            DataBus.System.Unsubscribe(Topics.System.All, Handler);
        }
    }

    [Fact]
    public void RetainedXStopSnapshotTogglesOnlyOnceAndDistinctEventTogglesAgain()
    {
        var (sim, _) = CreateNewtonianSim();
        var retained = XStopEvent(1);

        sim.DebugTickPhysics(retained, Dt);
        sim.DebugTickPhysics(retained, Dt);

        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.Equal(1, sim.DebugXStopState.LastConsumedXStopToggleSequence);

        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        sim.DebugTickPhysics(XStopEvent(2), Dt);

        Assert.False(sim.DebugXStopState.XStopActive);
        Assert.Equal(2, sim.DebugXStopState.LastConsumedXStopToggleSequence);
    }

    [Fact]
    public void HoldingXDoesNotRepeat()
    {
        var (sim, _) = CreateNewtonianSim();
        var held = XStopEvent(1);

        for (int i = 0; i < 10; i++)
            sim.DebugTickPhysics(held, Dt);

        Assert.True(sim.DebugXStopState.XStopActive);
        Assert.Equal(1, sim.DebugXStopState.LastConsumedXStopToggleSequence);
    }

    [Fact]
    public void XStopToggleIsConsumedButIgnoredOutsideSystemNewtonian()
    {
        var (sim, _) = CreateNewtonianSim(FlightMode.SystemSlipstream);
        var retained = XStopEvent(1);

        sim.DebugTickPhysics(retained, Dt);
        Assert.False(sim.DebugXStopState.XStopActive);

        sim.DebugSetFlightModeImmediately(FlightMode.SystemNewtonian);
        sim.DebugTickPhysics(retained, Dt);

        Assert.False(sim.DebugXStopState.XStopActive);
        Assert.Equal(1, sim.DebugXStopState.LastConsumedXStopToggleSequence);
    }

    [Fact]
    public void EnteringSystemSlipstreamStillClearsXStop()
    {
        var (sim, _) = CreateNewtonianSim();
        sim.DebugTickPhysics(XStopEvent(1), Dt);
        Assert.True(sim.DebugXStopState.XStopActive);

        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        sim.DebugTickPhysics(PlayerInput.Zero with { SlipstreamToggle = true }, Dt);

        Assert.Equal(FlightMode.SystemSlipstream, sim.DebugXStopState.FlightMode);
        Assert.False(sim.DebugXStopState.XStopActive);
    }

    [Fact]
    public void AfterburnerDurationAndThrustRemainUnchanged()
    {
        var (sim, ship) = CreateNewtonianSim();
        double expectedDelta = -FlightConstants.Gear1AccelerationMs2
            * FlightConstants.AfterburnerAccelMultiplier * Dt;

        sim.DebugTickPhysics(PlayerInput.Zero with { AfterburnerToggle = true }, Dt);

        Assert.True(sim.DebugXStopState.AfterburnerActive);
        Assert.Equal(FlightConstants.AfterburnerDurationSeconds - Dt,
            sim.DebugXStopState.AfterburnerTimeRemaining, 12);
        Assert.InRange(ship.Velocity.Z, expectedDelta - 1e-4, expectedDelta + 1e-4);
    }

    private static (SpaceSimulation sim, Inferior.Gameplay.Ship.Ship ship) CreateNewtonianSim(
        FlightMode mode = FlightMode.SystemNewtonian)
    {
        var sim = new SpaceSimulation();
        var ship = ShipBuilder.NewShip("type1")
            .WithPosition(DVec3.Zero)
            .WithOrientation(Quaternion.Identity)
            .WithDefaultStartingComponents()
            .Build();
        sim.SetShip(ship);
        sim.DebugSetFlightModeImmediately(mode);
        return (sim, ship);
    }

    private static void EngageAfterburner(SpaceSimulation sim)
    {
        sim.DebugTickPhysics(PlayerInput.Zero with { AfterburnerToggle = true }, Dt);
        Assert.True(sim.DebugXStopState.AfterburnerActive);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
    }

    private static void RunUntilAfterburnerEnds(SpaceSimulation sim)
    {
        for (int i = 0; i < 240 && sim.DebugXStopState.AfterburnerActive; i++)
            sim.DebugTickPhysics(PlayerInput.Zero, Dt);
    }

    private static PlayerInput XStopEvent(long sequence)
        => PlayerInput.Zero with
        {
            XStopToggle = true,
            XStopToggleSequence = sequence
        };

    private static double XStopAxisNoiseTolerance()
        => 2.0 * FlightConstants.Gear1AccelerationMs2
            * FlightConstants.XStopBrakeFactor * Dt;
}
