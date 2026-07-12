using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.Ships;
using Inferior.Gameplay;
using FlightConstantsAlias = Inferior.Gameplay.FlightConstants;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class SlipstreamHarmonyRetargetingTests
{
    private const double Dt = 1.0 / 60.0;

    [Fact]
    public void DistinctGearUpEventChangesTargetOnce()
    {
        var (sim, _) = CreateSlipstreamSim();
        var input = GearEvent(1, 1);

        sim.DebugTickPhysics(input, Dt);
        var afterFirst = sim.DebugSlipstreamState;
        sim.DebugTickPhysics(input, Dt);
        var afterRetained = sim.DebugSlipstreamState;

        Assert.Equal(1, afterFirst.HarmonicIndex);
        Assert.Equal(1, afterRetained.HarmonicIndex);
        Assert.Equal(1, afterRetained.LastConsumedGearChangeSequence);
    }

    [Fact]
    public void SecondDistinctEventDuringAccelerationChangesTargetAgain()
    {
        var (sim, ship) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        double currentBeforeRetarget = sim.DebugSlipstreamState.CurrentSpeed;

        sim.DebugTickPhysics(GearEvent(2, 1), Dt);
        var state = sim.DebugSlipstreamState;

        Assert.Equal(2, state.HarmonicIndex);
        Assert.Equal(currentBeforeRetarget, state.StartSpeed);
        Assert.Equal(ship.SlipstreamHarmonics[2], state.TargetSpeed);
        Assert.True(state.Transitioning);
    }

    [Fact]
    public void HigherHarmonyWhileAcceleratingUpwardKeepsSpeedContinuous()
    {
        var (sim, ship) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        double before = sim.DebugSlipstreamState.CurrentSpeed;

        sim.DebugTickPhysics(GearEvent(2, 1), Dt);
        var after = sim.DebugSlipstreamState;

        Assert.Equal(before, after.StartSpeed);
        Assert.Equal(ship.SlipstreamHarmonics[2], after.TargetSpeed);
        Assert.True(after.CurrentSpeed >= before);
    }

    [Fact]
    public void LowerHarmonyWhileAcceleratingUpwardRetargetsDown()
    {
        var (sim, ship) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        sim.DebugTickPhysics(GearEvent(2, 1), Dt);
        sim.DebugTickPhysics(GearEvent(3, 1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        double before = sim.DebugSlipstreamState.CurrentSpeed;

        sim.DebugTickPhysics(GearEvent(4, -1), Dt);
        var after = sim.DebugSlipstreamState;

        Assert.Equal(2, after.HarmonicIndex);
        Assert.Equal(before, after.StartSpeed);
        Assert.Equal(ship.SlipstreamHarmonics[2], after.TargetSpeed);
    }

    [Fact]
    public void HigherHarmonyWhileDeceleratingRetargetsUp()
    {
        var (sim, ship) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        sim.DebugTickPhysics(GearEvent(2, 1), Dt);
        sim.DebugTickPhysics(GearEvent(3, 1), Dt);
        sim.DebugTickPhysics(GearEvent(4, -1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        double before = sim.DebugSlipstreamState.CurrentSpeed;

        sim.DebugTickPhysics(GearEvent(5, 1), Dt);
        var after = sim.DebugSlipstreamState;

        Assert.Equal(3, after.HarmonicIndex);
        Assert.Equal(before, after.StartSpeed);
        Assert.Equal(ship.SlipstreamHarmonics[3], after.TargetSpeed);
    }

    [Fact]
    public void RapidSequenceEndsAtRequestedTarget()
    {
        var (sim, ship) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);  // H2
        sim.DebugTickPhysics(GearEvent(2, 1), Dt);  // H3
        sim.DebugTickPhysics(GearEvent(3, 2), Dt);  // H5
        sim.DebugTickPhysics(GearEvent(4, -1), Dt); // H4

        var state = sim.DebugSlipstreamState;
        Assert.Equal(3, state.HarmonicIndex);
        Assert.Equal(ship.SlipstreamHarmonics[3], state.TargetSpeed);
    }

    [Fact]
    public void RetargetRestartsExistingDurationAndPreservesSmoothStepFormula()
    {
        var (sim, _) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        sim.DebugTickPhysics(PlayerInput.Zero, Dt);
        sim.DebugTickPhysics(GearEvent(2, 1), Dt);

        var atRetarget = sim.DebugSlipstreamState;
        double expectedRawT = 1.0 - Math.Clamp(
            atRetarget.TransitionTimer / FlightConstantsAlias.SlipstreamAccelSeconds, 0, 1);
        double expectedT = expectedRawT * expectedRawT * (3.0 - 2.0 * expectedRawT);
        double expectedSpeed = atRetarget.StartSpeed
            + (atRetarget.TargetSpeed - atRetarget.StartSpeed) * expectedT;

        Assert.Equal(FlightConstantsAlias.SlipstreamAccelSeconds - Dt, atRetarget.TransitionTimer, 12);
        Assert.Equal(expectedSpeed, atRetarget.CurrentSpeed, 6);
    }

    [Fact]
    public void HarmonicSnapshotAndDataBusPublishSelectedTargetImmediately()
    {
        var (sim, _) = CreateSlipstreamSim();
        double published = -1;
        void Handler(double v) => published = v;
        DataBus.Instruments.Subscribe(Topics.Flight.HarmonicIndex, Handler);
        try
        {
            sim.DebugTickPhysics(GearEvent(1, 1), Dt);
            Assert.Equal(1, sim.ShipState!.SlipstreamHarmonicIndex);

            sim.DebugPublish();
            DataBus.Drain();

            Assert.Equal(2.0, published);
        }
        finally
        {
            DataBus.Instruments.Unsubscribe(Topics.Flight.HarmonicIndex, Handler);
        }
    }

    [Fact]
    public void MinimumAndMaximumBoundsDoNotRestartTransition()
    {
        var (sim, _) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, -1), Dt);
        var atMin = sim.DebugSlipstreamState;
        Assert.Equal(0, atMin.HarmonicIndex);
        Assert.False(atMin.Transitioning);
        Assert.Equal(0, atMin.TransitionTimer);

        for (long seq = 2; seq <= 20; seq++)
            sim.DebugTickPhysics(GearEvent(seq, 1), Dt);
        var atMax = sim.DebugSlipstreamState;

        sim.DebugTickPhysics(GearEvent(21, 1), Dt);
        var afterMaxAttempt = sim.DebugSlipstreamState;

        Assert.Equal(atMax.HarmonicIndex, afterMaxAttempt.HarmonicIndex);
        Assert.Equal(atMax.StartSpeed, afterMaxAttempt.StartSpeed);
        Assert.Equal(atMax.TargetSpeed, afterMaxAttempt.TargetSpeed);
        Assert.Equal(atMax.TransitionTimer - Dt, afterMaxAttempt.TransitionTimer, 12);
    }

    [Fact]
    public void HarmonyChangeDoesNotApplyOutsideSystemSlipstreamOrAfterReentryFromOldEvent()
    {
        var (sim, _) = CreateSlipstreamSim(FlightMode.SystemNewtonian);
        var retained = GearEvent(1, 1);

        sim.DebugTickPhysics(retained, Dt);
        Assert.Equal(0, sim.DebugSlipstreamState.HarmonicIndex);

        sim.DebugSetFlightModeImmediately(FlightMode.SystemSlipstream);
        sim.DebugTickPhysics(retained, Dt);

        Assert.Equal(0, sim.DebugSlipstreamState.HarmonicIndex);
    }

    [Fact]
    public void PlanetStationAndLkmDropoutRemainAuthoritative()
    {
        var (planetSim, _) = CreateSlipstreamSim();
        planetSim.DebugSetNearBodyAltitude(FlightConstantsAlias.SlipstreamPlanetDropoutAltitude - 1.0);
        planetSim.DebugTickPhysics(GearEvent(1, 1), Dt);
        Assert.Equal(FlightMode.SystemNewtonian, planetSim.DebugSlipstreamState.FlightMode);

        var (stationSim, _) = CreateSlipstreamSim();
        stationSim.DebugSetNearestStationDistance(FlightConstantsAlias.SlipstreamStationDropoutRange - 1.0);
        stationSim.DebugTickPhysics(GearEvent(1, 1), Dt);
        Assert.Equal(FlightMode.SystemNewtonian, stationSim.DebugSlipstreamState.FlightMode);

        var (lkmSim, _) = CreateSlipstreamSim();
        lkmSim.DebugSetNearestStationDistance(FlightConstantsAlias.StationLkmZones[0].radius - 1.0);
        lkmSim.DebugTickPhysics(GearEvent(1, 1), Dt);
        Assert.Equal(FlightMode.SystemNewtonian, lkmSim.DebugSlipstreamState.FlightMode);
    }

    [Fact]
    public void SlipstreamExitClearsTransitionStateAsBefore()
    {
        var (sim, _) = CreateSlipstreamSim();

        sim.DebugTickPhysics(GearEvent(1, 1), Dt);
        Assert.True(sim.DebugSlipstreamState.Transitioning);

        sim.DebugTickPhysics(PlayerInput.Zero with { SlipstreamToggle = true }, Dt);
        var state = sim.DebugSlipstreamState;

        Assert.Equal(FlightMode.SystemNewtonian, state.FlightMode);
        Assert.False(state.Transitioning);
        Assert.Equal(0, state.CurrentSpeed);
    }

    private static (SpaceSimulation sim, Inferior.Gameplay.Ship.Ship ship) CreateSlipstreamSim(
        FlightMode mode = FlightMode.SystemSlipstream)
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

    private static PlayerInput GearEvent(long sequence, int steps)
        => PlayerInput.Zero with
        {
            GearUp = steps > 0,
            GearDown = steps < 0,
            GearChangeSequence = sequence,
            GearChangeSteps = steps
        };
}
