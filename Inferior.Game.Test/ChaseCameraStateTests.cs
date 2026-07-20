using Inferior.Core.Math;
using Inferior.Game.States;
using Inferior.Gameplay;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class ChaseCameraStateTests
{
    [Fact]
    public void OrbitalMovement_PreservesRadiusAndStaysOnSphere()
    {
        var chase = new ChaseCameraState();
        chase.ToggleActive();
        chase.ToggleOrbitalEdit();
        double radius = chase.Radius;

        chase.ApplyEdit(new ChaseCameraEditInput(1, 1, 0, 0, false), 0.25);
        DVec3 renderedOffset = chase.ResolveWorldOffset(Quaternion.Identity);

        Assert.Equal(radius, chase.Radius, 9);
        Assert.Equal(radius, chase.DesiredHullLocalOffset.Length, 9);
        Assert.Equal(radius, renderedOffset.Length, 5);
    }

    [Fact]
    public void OppositeOrbitalInputs_ApproximatelyReverseMovement()
    {
        var chase = new ChaseCameraState();
        DVec3 initial = chase.HullLocalDirection;

        chase.ApplyEdit(new ChaseCameraEditInput(1, 0, 0, 0, false), 0.01);
        chase.ApplyEdit(new ChaseCameraEditInput(-1, 0, 0, 0, false), 0.01);

        Assert.True(DVec3.Dot(initial, chase.HullLocalDirection) > 0.999999);
    }

    [Fact]
    public void CameraRoll_ChangesScreenRelativeOrbitDirectionWithoutMovingCamera()
    {
        var unrolled = new ChaseCameraState();
        var rolled = new ChaseCameraState();
        DVec3 originalOffset = rolled.DesiredHullLocalOffset;
        double quarterTurnTime = MathHelper.PiOver2 / ChaseCameraState.RollSpeedRadiansPerSecond;

        rolled.ApplyEdit(new ChaseCameraEditInput(0, 0, 1, 0, false), quarterTurnTime);

        Assert.Equal(originalOffset, rolled.DesiredHullLocalOffset);
        Assert.Equal(MathHelper.PiOver2, rolled.RollRadians, 5);

        unrolled.ApplyEdit(new ChaseCameraEditInput(0, 1, 0, 0, false), 0.1);
        rolled.ApplyEdit(new ChaseCameraEditInput(0, 1, 0, 0, false), 0.1);

        Assert.True(
            DVec3.Dot(unrolled.HullLocalDirection, rolled.HullLocalDirection) < 0.999);
    }

    [Fact]
    public void RadiusEditing_ClampsAndPreservesDirection()
    {
        var chase = new ChaseCameraState();
        DVec3 direction = chase.HullLocalDirection;

        chase.ApplyEdit(new ChaseCameraEditInput(0, 0, 0, -1, false), 100);
        Assert.Equal(ChaseCameraState.MinimumRadiusMeters, chase.Radius);
        Assert.Equal(direction, chase.HullLocalDirection);

        chase.ApplyEdit(new ChaseCameraEditInput(0, 0, 0, 1, false), 100);
        Assert.Equal(ChaseCameraState.MaximumRadiusMeters, chase.Radius);
        Assert.Equal(direction, chase.HullLocalDirection);
    }

    [Fact]
    public void Reset_RestoresDefaultPoseAndZeroRoll()
    {
        var chase = new ChaseCameraState();
        chase.ApplyEdit(new ChaseCameraEditInput(1, 1, 1, 1, false), 0.5);

        chase.ApplyEdit(new ChaseCameraEditInput(0, 0, 0, 0, true), 0.0);

        Assert.Equal(Math.Sqrt(80 * 80 + 30 * 30), chase.Radius, 9);
        Assert.Equal(new DVec3(0, 30, 80).Normalized(), chase.HullLocalDirection);
        Assert.Equal(0.0, chase.RollRadians);
    }

    [Fact]
    public void CameraOrientation_AlwaysLooksAtShipCentre()
    {
        var chase = new ChaseCameraState();
        chase.ApplyEdit(new ChaseCameraEditInput(1, 1, 1, 0, false), 0.25);
        DVec3 offset = chase.ResolveWorldOffset(Quaternion.Identity);

        Quaternion orientation = chase.ResolveCameraOrientation(offset, DVec3.UnitY);
        Vector3 cameraForward = Vector3.Normalize(
            Vector3.Transform(-Vector3.UnitZ, orientation));
        Vector3 expected = Vector3.Normalize(new Vector3(
            (float)-offset.X,
            (float)-offset.Y,
            (float)-offset.Z));

        Assert.True(Vector3.Dot(cameraForward, expected) > 0.9999f);
    }

    [Fact]
    public void EditedPose_PersistsAcrossEditAndChaseModeToggles()
    {
        var chase = new ChaseCameraState();
        chase.ToggleActive();
        chase.ToggleOrbitalEdit();
        chase.ApplyEdit(new ChaseCameraEditInput(1, 1, 1, 1, false), 0.25);
        DVec3 editedDirection = chase.HullLocalDirection;
        double editedRadius = chase.Radius;
        double editedRoll = chase.RollRadians;

        chase.ToggleOrbitalEdit();
        chase.ToggleActive();
        chase.ToggleActive();

        Assert.True(chase.IsActive);
        Assert.False(chase.IsOrbitalEditActive);
        Assert.Equal(editedDirection, chase.HullLocalDirection);
        Assert.Equal(editedRadius, chase.Radius);
        Assert.Equal(editedRoll, chase.RollRadians);
    }

    [Fact]
    public void OrbitalEdit_CannotActivateOutsideChaseMode()
    {
        var chase = new ChaseCameraState();

        bool handled = chase.ToggleOrbitalEdit();

        Assert.False(handled);
        Assert.False(chase.IsOrbitalEditActive);
    }

    [Theory]
    [InlineData(FlightMode.SystemNewtonian)]
    [InlineData(FlightMode.AtmosphericNewtonian)]
    public void ChaseCamera_IsAvailableInNewtonianModes(FlightMode mode)
    {
        Assert.True(ChaseCameraState.IsAvailableIn(mode));
    }

    [Theory]
    [InlineData(FlightMode.Docked)]
    [InlineData(FlightMode.SystemSlipstream)]
    [InlineData(FlightMode.AtmosphericSlipstream)]
    [InlineData(FlightMode.EnteringFlatHyperspace)]
    [InlineData(FlightMode.FlatHyperspace)]
    public void ChaseCamera_IsUnavailableOutsideNewtonianModes(FlightMode mode)
    {
        Assert.False(ChaseCameraState.IsAvailableIn(mode));
    }

    [Fact]
    public void EnteringSlipstream_ExitsChaseAndPreservesSavedPose()
    {
        var chase = new ChaseCameraState();
        chase.ToggleActive();
        chase.ToggleOrbitalEdit();
        chase.ApplyEdit(new ChaseCameraEditInput(1, 1, 1, 1, false), 0.25);
        DVec3 direction = chase.HullLocalDirection;
        double radius = chase.Radius;
        double roll = chase.RollRadians;

        bool forcedOff = chase.EnforceFlightMode(FlightMode.SystemSlipstream);

        Assert.True(forcedOff);
        Assert.False(chase.IsActive);
        Assert.False(chase.IsOrbitalEditActive);
        Assert.Equal(direction, chase.HullLocalDirection);
        Assert.Equal(radius, chase.Radius);
        Assert.Equal(roll, chase.RollRadians);
    }
}
