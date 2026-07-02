namespace Inferior.Gameplay;

public static class FlatHyperspaceConstants
{
    // ── Travel speed ──────────────────────────────────────────────────────────
    public const double SpeedLYPerSecond      = 1000.0 / 60.0;   // ≈16.7 ly/s → 1000 ly/min

    // ── Disturbance field ─────────────────────────────────────────────────────
    public const double MaxDisturbanceRadiusLY = 100.0;           // stars beyond this are invisible
    public const double DropoutRadiusLY        = 2.0;             // proximity that triggers dropout
    public const double DropoutConeHalfAngleDeg = 60.0;           // forward cone half-angle

    // ── Drop-out placement ────────────────────────────────────────────────────
    public const double PenaltyDropAU_Min      = 80.0;
    public const double PenaltyDropAU_Max      = 120.0;
    public const double ArrivalDropAU_Min      = 0.5;
    public const double ArrivalDropAU_Max      = 1.0;
    public const double ArrivalAngleToleranceDeg = 10.0;

    // ── Alignment preamble ────────────────────────────────────────────────────
    public const double AlignThresholdDeg      = 10.0;
    public const double AutoAlignRateRadPerSec = 1.2;             // radians/sec rotation speed

    // ── Preamble animation durations (seconds) ────────────────────────────────
    public const double DotFadeInDuration      = 1.5;
    public const double LineGrowDuration       = 2.0;
    public const double PauseDuration          = 1.0;
    public const double SheetsGrowDuration     = 1.0;

    // ── Nearby-system check on voluntary exit ────────────────────────────────
    public const double NearbySystemRadiusLY   = 1.0;
}
