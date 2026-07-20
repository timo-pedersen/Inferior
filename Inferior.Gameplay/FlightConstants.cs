namespace Inferior.Gameplay;

/// <summary>
/// All tunable flight-physics parameters in one place.
/// Change here and relaunch — nothing in flight physics is hard-coded elsewhere.
/// </summary>
public static class FlightConstants
{
    // ── NEWTONIAN GEAR TABLE ──────────────────────────────────────────────
    // Forward speed ceilings (m/s) for each gear, index 0 = gear 1.
    // Log-scaled ~×2 per step.
    public static readonly double[] NewtonianGearSpeeds =
    [
          50,    // G1
         100,    // G2
         200,    // G3
         400,    // G4
         800,    // G5
       1_600,    // G6
       3_200,    // G7
       6_400,    // G8
      12_800,    // G9
      25_600,    // G10
    ];

    public static int NewtonianGearCount => NewtonianGearSpeeds.Length;

    // Reverse speed ceiling as a fraction of the current gear's forward ceiling.
    public const double ReverseSpeedRatio = 0.25;

    // Thrust taper exponent: at speed fraction f of ceiling,
    // effectiveThrustFactor = Max(0, 1 − f^ThrustTaperExponent).
    // n=2: smooth; n=3: stays strong longer before tapering.
    public const double ThrustTaperExponent = 2.0;

    // ── ENGINE DEFAULT (placeholder until component system) ───────────────
    public const int    DefaultNodeCount = 10;

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

    // Random pitch/yaw jitter added on top of mouse-look each tick while active (radians).
    // Deliberately affects the ship's real orientation (and so its actual forward vector and
    // travel direction) rather than being a purely cosmetic camera overlay — a Brownian-ish
    // wobble, not a perfectly straight boosted line. Roughly 1-2 mouse-look pixels' worth
    // per tick (MouseSensitivity in SystemSpaceState.Ship.cs is 0.0012 rad/pixel).
    public const double AfterburnerShakeRadians = 0.0015;
}
