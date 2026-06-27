namespace Inferior.Gameplay;

/// <summary>
/// Immutable input snapshot. Written by the main thread, read once per sim tick.
/// Reference assignment is atomic on 64-bit .NET — no partial reads.
/// FlightAssist and Slipstream are sim-internal state; only rising-edge toggle signals
/// are sent here so the sim owns the actual enabled/disabled state.
/// GearUp/GearDown are set for one tick when the scroll wheel fires — no rising-edge needed.
/// </summary>
public record PlayerInput(
    double ThrustForward,
    double ThrustLateral,
    double ThrustVertical,
    double RollInput,
    double PitchInput,
    double YawInput,
    bool   JumpRequested,
    bool   FlightAssistToggle = false,
    bool   SlipstreamToggle   = false,
    bool   XStopToggle        = false,
    bool   GearUp             = false,
    bool   GearDown           = false)
{
    public static readonly PlayerInput Zero = new(0, 0, 0, 0, 0, 0, false);
}
