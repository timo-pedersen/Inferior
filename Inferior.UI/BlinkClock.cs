namespace Inferior.UI;

/// <summary>
/// Global blink clock shared by all LedIndicator instances.
/// All LEDs of the same frequency blink in perfect sync.
/// Call Update once per frame from whatever Update drives the UI.
/// </summary>
public static class BlinkClock
{
    private static double _time = 0.0;

    public static void Update(double dt) => _time += dt;

    /// <summary>
    /// Returns true when LEDs at the given frequency should be in the
    /// illuminated phase (first half of the period).
    /// </summary>
    public static bool IsOnPhase(double frequencyHz)
    {
        if (frequencyHz <= 0) return true;
        double period = 1.0 / frequencyHz;
        return (_time % period) < (period * 0.5);
    }

    public static double Time => _time;
}
