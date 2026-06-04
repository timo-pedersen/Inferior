namespace Inferior.Gameplay;

/// <summary>
/// Immutable input snapshot. Written by the main thread, read once per sim tick.
/// Reference assignment is atomic on 64-bit .NET — no partial reads.
/// </summary>
public record PlayerInput(
    double ThrustForward,
    double ThrustLateral,
    double ThrustVertical,
    double RollInput,
    double PitchInput,
    double YawInput,
    bool   JumpRequested,
    bool   FlightAssist)
{
    public static readonly PlayerInput Zero = new(0, 0, 0, 0, 0, 0, false, true);
}
