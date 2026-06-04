namespace Inferior.Core.Simulation;

/// <summary>
/// Central time authority for the simulation.
///
/// SimTime    — monotonic seconds since session start. Drives physics and noise.
///              Written by the sim thread; read by the main thread for display.
///              Note: double reads are not guaranteed atomic on all architectures.
///              Worst case: one stale frame on a HUD display. Acceptable — do not
///              add volatile or Interlocked unless actual tearing is observed.
///
/// InGameDate — derived lore date. Never stored, always computed from SimTime.
///              Epoch and scale are placeholders pending design decision.
/// </summary>
public static class GameClock
{
    private static double _simTime;

    /// <summary>Accumulated simulation seconds this session.</summary>
    public static double SimTime => _simTime;

    // ── Lore date ─────────────────────────────────────────────────────────────

    private static readonly DateTime LoreEpoch = new(3200, 1, 1);

    // 1.0 = real time. Adjust when the in-game calendar scale is decided.
    private const double TimeScale = 1.0;

    /// <summary>Fictional in-universe date — derived from SimTime, never stored.</summary>
    public static DateTime InGameDate => LoreEpoch.AddSeconds(_simTime * TimeScale);

    // ── Simulation thread interface ───────────────────────────────────────────

    /// <summary>Called by the simulation thread every tick. Not thread-safe by design — see class doc.</summary>
    public static void Advance(double dt) => _simTime += dt;

    /// <summary>Reset at session start (new game or load).</summary>
    public static void Reset() => _simTime = 0;
}
