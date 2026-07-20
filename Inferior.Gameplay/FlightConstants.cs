namespace Inferior.Gameplay;

/// <summary>
/// All tunable flight-physics parameters in one place.
/// Change here and relaunch — nothing in flight physics is hard-coded elsewhere.
/// </summary>
public static class FlightConstants
{
    // Reverse Newtonian speed ceiling relative to the selected engine harmony ceiling.
    public const double ReverseSpeedRatio = 0.25;

    // Thrust taper exponent: at speed fraction f of ceiling,
    // effectiveThrustFactor = Max(0, 1 − f^ThrustTaperExponent).
    // n=2: smooth; n=3: stays strong longer before tapering.
    public const double ThrustTaperExponent = 2.0;

    // ── ENGINE DEFAULT (placeholder until component system) ───────────────
    public const int    DefaultNodeCount = 10;

    public const double MaximumAssistedPitchUpRateRadPerSec = 1.4;
    public const double MaximumAssistedPitchDownRateRadPerSec = 1.0;
    public const double MaximumAssistedYawRateRadPerSec = 1.0;
    public const double MaximumAssistedRollRateRadPerSec = 1.5;
    public const double RotationInputReferenceHz = 60.0;

    // ── X-STOP ───────────────────────────────────────────────────────────
    // Snap-to-reference threshold — below this, X-stop considers braking complete.
    public const double XStopSnapThreshold = 0.5;  // m/s
    // Braking thrust multiplier — X-stop applies this factor × normal acceleration.
    public const double XStopBrakeFactor   = 4.0;

    // ── LKM STATION ZONES ────────────────────────────────────────────────
    // (radius in metres from station centre, maxGearIndex is 0-based)
    public static readonly (double radius, int maxGearIndex)[] StationLkmZones =
    [
        (8_000, 5),   // outer zone: 8 km,  max gear 6 (1 600 m/s)
        (2_000, 3),   // middle zone: 2 km, max gear 4 (400 m/s)
        (  500, 1),   // inner zone: 500 m, max gear 2 (100 m/s)
    ];

    // Seconds pilot has to comply before violation is flagged.
    public const double LkmComplianceWindow = 6.0;

    // ── SLIPSTREAM (SYSTEM SPACE) ─────────────────────────────────────────
    public const double SlipstreamMinSpeed =          1_000.0;  // 1 km/s
    public const double SlipstreamMaxSpeed = 30_000_000_000.0;  // ~100 C

    // Smooth ramp time when shifting between harmonics (seconds).
    public const double SlipstreamAccelSeconds = 2.5;

    // Gear-shift clunk duration (Newtonian only — slipstream gets its own effect later).
    // Actual = ClunkBaseDurationMs + ClunkNodePenaltyMs × (24 - nodeCount)
    // Default 10-node ship: 150 + 30 × 14 = 570 ms
    public const double ClunkBaseDurationMs = 150.0;
    public const double ClunkNodePenaltyMs  =  30.0;  // per missing node vs. 24

    // Roll oscillation during clunk animation (degrees, each way).
    public const float ClunkRollDegrees = 1.5f;

    // Slipstream drop-out distance from planet surface (metres).
    // Must be less than SlipstreamProximityDropoffM so the ship has already slowed
    // significantly before dropout fires (and the position snap is close to real position).
    public const double SlipstreamPlanetDropoutAltitude = 100_000.0;  // 100 km

    // Slipstream drop-out distance from station centre (metres).
    // At 20 km the proximity scale has already damped the ship to near zero.
    public const double SlipstreamStationDropoutRange = 20_000.0;

    // Distance at which proximity scale begins damping Slipstream speed.
    // Scale = (dist / dropoff)³ — cubic dropoff.  Must be larger than both
    // SlipstreamPlanetDropoutAltitude and SlipstreamStationDropoutRange so the
    // ship visibly decelerates well before the forced exit fires.
    // At 500 km: scale=1.0; at 250 km: scale=0.125; at 100 km: scale=0.008 → near-stopped.
    public const double SlipstreamProximityDropoffM = 500_000.0;  // 500 km

    // ── ATMOSPHERIC NEWTONIAN ─────────────────────────────────────────────
    // Top Newtonian gear speed is multiplied by this factor in-atmosphere.
    // Defined here but not yet applied — atmospheric brief pending.
    public const double AtmoGearSpeedScale = 0.05;  // top gear ≈ 1 280 m/s

    // ── ATMOSPHERIC SLIPSTREAM ────────────────────────────────────────────
    public const double AtmoSlipstreamCutoffBar = 0.1;
    public const double AtmoSlipstreamMinSpeed  =   200;  // m/s
    public const double AtmoSlipstreamMaxSpeed  = 2_000;  // m/s
    public const int    AtmoSlipstreamGearCount = 6;

    // ── AFTERBURNER (SYSTEM NEWTONIAN) ────────────────────────────────────
    public const double AfterburnerDurationSeconds = 2.0;

    // Constant forward accel while active = current full-throttle accel × this.
    public const double AfterburnerAccelMultiplier = 5.0;

    // Legacy angle-scale jitter converted into a torque-limited assisted target perturbation.
    public const double AfterburnerShakeRadians = 0.0015;
}
