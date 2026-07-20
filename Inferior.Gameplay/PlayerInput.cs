namespace Inferior.Gameplay;

/// <summary>
/// Immutable input snapshot. Written by the main thread, read once per sim tick.
/// Reference assignment is atomic on 64-bit .NET — no partial reads.
/// FlightAssist and Slipstream are sim-internal state; only rising-edge toggle signals
/// are sent here so the sim owns the actual enabled/disabled state.
/// XStopToggleSequence and GearChangeSequence/Steps identify distinct edge events so the
/// sim can consume a retained input snapshot once.
/// PitchInput, YawInput, and RollInput are normalized assisted-rate commands in [-1, 1].
/// UseLiftChannel selects the stronger positive-vertical engine channel without adding
/// another translation axis.
/// </summary>
public record PlayerInput(
    double ThrustForward,
    double ThrustLateral,
    double ThrustVertical,
    double RollInput,
    double PitchInput,
    double YawInput,
    bool   JumpRequested,
    bool   FlightAssistToggle  = false,
    bool   SlipstreamToggle    = false,
    bool   XStopToggle         = false,
    long   XStopToggleSequence = 0,
    bool   GearUp              = false,
    bool   GearDown            = false,
    bool   AfterburnerToggle   = false,
    long   GearChangeSequence  = 0,
    int    GearChangeSteps     = 0,
    bool   UseLiftChannel      = false)
{
    public static readonly PlayerInput Zero = new(0, 0, 0, 0, 0, 0, false);
}
