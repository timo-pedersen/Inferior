using Inferior.Game.Input;
using Inferior.Gameplay;
using Inferior.Gameplay.Ship;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.Game.Test;

public sealed class MouseLookRebaserTests
{
    private const double Sensitivity = 0.0012;
    private static readonly Point Center = new(400, 300);

    [Fact]
    public void FirstSampleAfterActivationProducesZeroLookDelta()
    {
        var rebaser = new MouseLookRebaser();

        MouseLookInput input = rebaser.Sample(Mouse(560, 210), Center, active: true, focused: true, Sensitivity);

        Assert.True(input.Rebased);
        Assert.Equal(0.0, input.HorizontalInput);
        Assert.Equal(0.0, input.PitchInput);
    }

    [Fact]
    public void SecondSampleProducesDelta()
    {
        var rebaser = new MouseLookRebaser();
        rebaser.Sample(Mouse(560, 210), Center, active: true, focused: true, Sensitivity);

        MouseLookInput input = rebaser.Sample(Mouse(410, 295), Center, active: true, focused: true, Sensitivity);

        Assert.False(input.Rebased);
        Assert.Equal(-10 * Sensitivity, input.HorizontalInput);
        Assert.Equal(5 * Sensitivity, input.PitchInput);
    }

    [Fact]
    public void ReactivationAfterSuppressionRebasesAgain()
    {
        var rebaser = new MouseLookRebaser();
        rebaser.Sample(Mouse(400, 300), Center, active: true, focused: true, Sensitivity);
        rebaser.Sample(Mouse(420, 320), Center, active: true, focused: true, Sensitivity);
        rebaser.Sample(Mouse(700, 100), Center, active: false, focused: true, Sensitivity);

        MouseLookInput reactivated = rebaser.Sample(Mouse(700, 100), Center, active: true, focused: true, Sensitivity);
        MouseLookInput following = rebaser.Sample(Mouse(405, 306), Center, active: true, focused: true, Sensitivity);

        Assert.True(reactivated.Rebased);
        Assert.Equal(0.0, reactivated.HorizontalInput);
        Assert.Equal(0.0, reactivated.PitchInput);
        Assert.Equal(-5 * Sensitivity, following.HorizontalInput);
        Assert.Equal(-6 * Sensitivity, following.PitchInput);
    }

    [Fact]
    public void RegainingFocusRebases()
    {
        var rebaser = new MouseLookRebaser();
        rebaser.Sample(Mouse(400, 300), Center, active: true, focused: true, Sensitivity);
        rebaser.Sample(Mouse(410, 300), Center, active: true, focused: true, Sensitivity);
        rebaser.Sample(Mouse(100, 100), Center, active: true, focused: false, Sensitivity);

        MouseLookInput regained = rebaser.Sample(Mouse(100, 100), Center, active: true, focused: true, Sensitivity);

        Assert.True(regained.Rebased);
        Assert.Equal(0.0, regained.HorizontalInput);
        Assert.Equal(0.0, regained.PitchInput);
    }

    [Fact]
    public void CursorRecenterOrCaptureDoesNotGenerateRotation()
    {
        var rebaser = new MouseLookRebaser();
        rebaser.Sample(Mouse(400, 300), Center, active: true, focused: true, Sensitivity);
        rebaser.Sample(Mouse(415, 320), Center, active: true, focused: true, Sensitivity);
        rebaser.RequestRebase();

        MouseLookInput recentered = rebaser.Sample(Mouse(400, 300), Center, active: true, focused: true, Sensitivity);

        Assert.True(recentered.Rebased);
        Assert.Equal(0.0, recentered.HorizontalInput);
        Assert.Equal(0.0, recentered.PitchInput);
    }

    [Fact]
    public void ContinuousNormalMovementIsNotSuppressed()
    {
        var rebaser = new MouseLookRebaser();
        rebaser.Sample(Mouse(400, 300), Center, active: true, focused: true, Sensitivity);

        MouseLookInput firstMove = rebaser.Sample(Mouse(401, 300), Center, active: true, focused: true, Sensitivity);
        MouseLookInput secondMove = rebaser.Sample(Mouse(402, 300), Center, active: true, focused: true, Sensitivity);

        Assert.False(firstMove.Rebased);
        Assert.False(secondMove.Rebased);
        Assert.Equal(-1 * Sensitivity, firstMove.HorizontalInput);
        Assert.Equal(-2 * Sensitivity, secondMove.HorizontalInput);
    }

    [Fact]
    public void RebasedSamplePreservesKeyboardAxesButtonsAndScroll()
    {
        var lookInput = new MouseLookInput(0.0, 0.0, Rebased: true);
        var keys = new KeyboardState(Keys.W, Keys.D, Keys.R, Keys.E, Keys.V, Keys.G, Keys.X, Keys.Z);
        var prevKeys = new KeyboardState();
        var mouse = Mouse(400, 300, scroll: 120);
        var prevMouse = Mouse(400, 300, scroll: 0);

        var input = ShipInputMapper.Build(keys, prevKeys, mouse, prevMouse, lookInput);

        Assert.Equal(1.0, input.ThrustForward);
        Assert.Equal(1.0, input.ThrustLateral);
        Assert.Equal(1.0, input.ThrustVertical);
        Assert.Equal(-1.0, input.YawInput);
        Assert.Equal(0.0, input.RollInput);
        Assert.Equal(0.0, input.PitchInput);
        Assert.True(input.FlightAssistToggle);
        Assert.True(input.SlipstreamToggle);
        Assert.True(input.XStopToggle);
        Assert.True(input.AfterburnerToggle);
        Assert.True(input.GearUp);
        Assert.False(input.GearDown);
    }

    [Fact]
    public void SpaceMatchesPositiveVerticalInputWithoutStackingWithR()
    {
        var lookInput = new MouseLookInput(0.0, 0.0, Rebased: false);
        var previous = new KeyboardState();
        MouseState mouse = Mouse(400, 300);

        var space = ShipInputMapper.Build(
            new KeyboardState(Keys.Space), previous, mouse, mouse, lookInput);
        var r = ShipInputMapper.Build(
            new KeyboardState(Keys.R), previous, mouse, mouse, lookInput);
        var both = ShipInputMapper.Build(
            new KeyboardState(Keys.R, Keys.Space), previous, mouse, mouse, lookInput);

        Assert.Equal(1.0, space.ThrustVertical);
        Assert.Equal(r.ThrustVertical, space.ThrustVertical);
        Assert.Equal(1.0, both.ThrustVertical);
        Assert.True(space.UseLiftChannel);
        Assert.False(r.UseLiftChannel);
        Assert.True(both.UseLiftChannel);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(-0.01)]
    public void ShipInputMapper_VerticalMouseProducesPitchOnlyWithUnchangedScaling(
        double legacyPitchInput)
    {
        var keys = new KeyboardState();
        var mouse = Mouse(400, 300);
        var lookInput = new MouseLookInput(legacyPitchInput, 0.0, Rebased: false);

        var input = ShipInputMapper.Build(
            keys,
            keys,
            mouse,
            mouse,
            lookInput);

        double maximumRate = legacyPitchInput >= 0.0
            ? FlightConstants.MaximumAssistedPitchUpRateRadPerSec
            : FlightConstants.MaximumAssistedPitchDownRateRadPerSec;
        Assert.Equal(
            legacyPitchInput * FlightConstants.RotationInputReferenceHz / maximumRate,
            input.PitchInput,
            12);
        Assert.Equal(0.0, input.YawInput);
        Assert.Equal(0.0, input.RollInput);
    }

    [Theory]
    [InlineData(-0.01, 0.01)]
    [InlineData(0.01, -0.01)]
    public void ShipInputMapper_HorizontalMouseProducesRollOnly(
        double horizontalInput,
        double expectedLegacyRollInput)
    {
        var keys = new KeyboardState();
        var mouse = Mouse(400, 300);

        PlayerInput input = ShipInputMapper.Build(
            keys,
            keys,
            mouse,
            mouse,
            new MouseLookInput(0.0, horizontalInput, Rebased: false));

        Assert.Equal(0.0, input.PitchInput);
        Assert.Equal(0.0, input.YawInput);
        Assert.Equal(
            expectedLegacyRollInput * FlightConstants.RotationInputReferenceHz
                / FlightConstants.MaximumAssistedRollRateRadPerSec,
            input.RollInput,
            12);
    }

    [Theory]
    [InlineData(Keys.Q, 1.0)]
    [InlineData(Keys.E, -1.0)]
    public void ShipInputMapper_QAndEProduceOppositeYawWithoutRoll(
        Keys key,
        double expectedYaw)
    {
        var mouse = Mouse(400, 300);

        PlayerInput input = ShipInputMapper.Build(
            new KeyboardState(key),
            new KeyboardState(),
            mouse,
            mouse,
            new MouseLookInput(0.0, 0.0, Rebased: false));

        Assert.Equal(expectedYaw, input.YawInput);
        Assert.Equal(0.0, input.RollInput);
        Assert.Equal(0.0, input.PitchInput);
    }

    [Fact]
    public void ShipInputMapper_MouseRollAndKeyboardYawRemainIndependent()
    {
        var mouse = Mouse(400, 300);

        PlayerInput input = ShipInputMapper.Build(
            new KeyboardState(Keys.Q),
            new KeyboardState(),
            mouse,
            mouse,
            new MouseLookInput(0.01, -0.01, Rebased: false));

        Assert.True(input.PitchInput > 0.0);
        Assert.Equal(1.0, input.YawInput);
        Assert.True(input.RollInput > 0.0);
    }

    [Fact]
    public void ShipInputMapper_ClampsFinalRotationCommandComponents()
    {
        var keys = new KeyboardState(Keys.Q);
        var mouse = Mouse(400, 300);

        var input = ShipInputMapper.Build(
            keys,
            keys,
            mouse,
            mouse,
            new MouseLookInput(10.0, -10.0, Rebased: false));

        Assert.Equal(1.0, input.PitchInput);
        Assert.Equal(1.0, input.YawInput);
        Assert.Equal(1.0, input.RollInput);
    }

    [Fact]
    public void MappedInputChangesTargetAngularVelocityWithoutChangingOrientation()
    {
        var mouse = Mouse(400, 300);
        PlayerInput input = ShipInputMapper.Build(
            new KeyboardState(Keys.Q),
            new KeyboardState(),
            mouse,
            mouse,
            new MouseLookInput(0.0, -0.01, Rebased: false));
        var ship = new Ship();
        Quaternion initialOrientation = ship.Orientation;

        var command = RotationCommand.Clamp(
            input.PitchInput,
            input.YawInput,
            input.RollInput);
        var target = ShipRotation.ResolveTargetAngularVelocity(ship, command);

        Assert.Equal(initialOrientation, ship.Orientation);
        Assert.True(target.Y > 0.0);
        Assert.True(target.Z < 0.0);
    }

    private static MouseState Mouse(int x, int y, int scroll = 0)
        => new(x, y, scroll,
            ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released);
}
