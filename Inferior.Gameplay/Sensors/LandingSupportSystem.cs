using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;

namespace Inferior.Gameplay.Sensors;

/// <summary>
/// Computes and publishes landing approach geometry relative to the targeted pad.
/// Standalone sensor — not a ShipComponent. Driven from SpaceSimulation.Publish().
///
/// Published topics (DataBus.Instruments, double):
///   LandingSupport.PadTargeted        — 1.0 when active, 0.0 otherwise
///   LandingSupport.PadSizeClass       — 0.0 = Small, 1.0 = Large
///   LandingSupport.PadDistance        — metres, Euclidean
///   LandingSupport.HeightAbovePad     — metres along pad normal; positive = correct approach side
///   LandingSupport.LateralOffset      — metres along pad short axis
///   LandingSupport.LongitudinalOffset — metres along pad forward axis
///   LandingSupport.HeadingDeviation   — degrees; 0 = aligned with pad forward
///   LandingSupport.PitchDeviation     — degrees; 0 = face-on to pad
///   LandingSupport.UpsideDown         — 1.0 when ship is inverted relative to pad
///
/// Noise model (keyed on Damage 0..1):
///   0.0–0.3  → clean
///   0.3–0.7  → linear Gaussian noise ramp (up to ±8%)
///   0.7–0.9  → heavy noise + occasional dropouts (publish 0 instead)
///   > 0.9    → system failed; publish 0.0 for PadTargeted (clears HUD)
/// </summary>
public sealed class LandingSupportSystem
{
    // Written and read on sim thread (via SelectPad in SpaceSimulation.Publish).
    // Volatile is a safety net in case SelectPad is ever called cross-thread.
    private volatile LandingPadData? _activePad;
    private bool _wasActive;

    public double Damage { get; set; }

    private const double ApproachNotifyRange = 2000.0;  // metres

    public void SelectPad(LandingPadData? data) => _activePad = data;

    /// <summary>
    /// Called once per sim tick from SpaceSimulation.Publish().
    /// All ship values come from the tick's snapshot — never from a live Ship reference.
    /// </summary>
    public void Tick(DVec3 shipPosition, DVec3 shipForward, DVec3 shipUp)
    {
        var pad = _activePad;

        if (pad == null || Damage > 0.9)
        {
            DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PadTargeted}", 0.0);
            if (_wasActive && pad == null)
                DataBus.System.Publish(Topics.System.All, new("Landing support: approach aborted", SystemMessagePriority.NB));
            _wasActive = false;
            return;
        }

        double t = GameClock.SimTime;

        // ── Geometry ─────────────────────────────────────────────────────────────

        // padToShip: vector from pad centre to ship.
        // Positive projection onto WorldNormal → ship is on the correct approach side.
        DVec3 padToShip = shipPosition - pad.WorldPosition;
        double dist     = padToShip.Length;

        DVec3 n   = pad.WorldNormal;
        DVec3 fwd = pad.ForwardAxis;
        // right is derived: cross(n, fwd) → pad short axis
        DVec3 right = DVec3.Normalize(DVec3.Cross(n, fwd));

        double heightAbovePad     = DVec3.Dot(padToShip, n);
        double lateralOffset      = DVec3.Dot(padToShip, right);
        double longitudinalOffset = DVec3.Dot(padToShip, fwd);

        // Heading deviation: signed angle of ship-forward (projected onto pad plane) vs pad forward.
        // +  = ship nose rotated clockwise  (when viewed from pad normal direction)
        // −  = ship nose rotated counter-clockwise
        // 0  = aligned with pad forward axis
        DVec3 shipFwdOnPad = shipForward - n * DVec3.Dot(shipForward, n);
        double shipFwdLen  = shipFwdOnPad.Length;
        double headingDev  = 0.0;
        if (shipFwdLen > 1e-6)
        {
            shipFwdOnPad = shipFwdOnPad / shipFwdLen;
            double cosH = DVec3.Dot(shipFwdOnPad, fwd);
            double sinH = DVec3.Dot(DVec3.Cross(shipFwdOnPad, fwd), n);  // sign = CW vs CCW around normal
            headingDev  = System.Math.Atan2(sinH, cosH) * (180.0 / System.Math.PI);
        }

        // Pitch deviation: 0 = face-on (ship forward parallel to pad normal); 90 = sideways
        double dotFwdN  = System.Math.Clamp(DVec3.Dot(shipForward, n), -1.0, 1.0);
        double pitchDev = System.Math.Asin(System.Math.Abs(dotFwdN)) * (180.0 / System.Math.PI);

        // Upside-down: ship up dot pad normal < 0 means inverted
        double upsideDown = DVec3.Dot(shipUp, n) < 0.0 ? 1.0 : 0.0;

        // ── Noise ─────────────────────────────────────────────────────────────────

        double noiseFraction = NoiseScale(t);
        double dropout       = ShouldDropout(t);

        double Apply(double value, double scale)
        {
            if (dropout > 0.0) return 0.0;
            if (noiseFraction <= 0.0) return value;
            double jitter = Noise.Scale(Noise.White(t * 0.31 + scale), System.Math.Abs(value) + 1.0, noiseFraction);
            return value + jitter;
        }

        // ── Captain's log ─────────────────────────────────────────────────────────

        if (!_wasActive)
        {
            if (dist <= ApproachNotifyRange)
                DataBus.System.Publish(Topics.System.All,
                    new($"Landing support: approach initiated — {pad.BayId} on {pad.StationName}"));
        }
        _wasActive = true;

        // ── Publish ───────────────────────────────────────────────────────────────

        double sizeClass = pad.PadSize == Inferior.Galaxy.PadSize.Large ? 1.0 : 0.0;

        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PadTargeted}",        1.0);
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PadSizeClass}",       sizeClass);
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PadDistance}",        Apply(dist,                0.1));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.HeightAbovePad}",     Apply(heightAbovePad,      0.2));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.LateralOffset}",      Apply(lateralOffset,       0.3));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.LongitudinalOffset}", Apply(longitudinalOffset,  0.4));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.HeadingDeviation}",   Apply(headingDev,          0.5));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.PitchDeviation}",     Apply(pitchDev,            0.6));
        DataBus.Instruments.Publish($"Ship.{Topics.LandingSupport.UpsideDown}",         upsideDown);
    }

    // ── Noise helpers ─────────────────────────────────────────────────────────────

    private double NoiseScale(double t)
    {
        if (Damage < 0.3) return 0.0;
        if (Damage < 0.7) return (Damage - 0.3) / 0.4 * 0.08;  // ramp 0→8%
        return 0.15;  // heavy noise above 0.7
    }

    // Returns 1.0 when a dropout occurs (sim value should be replaced with 0)
    private double ShouldDropout(double t)
    {
        if (Damage < 0.7) return 0.0;
        // Intermittent dropouts: white noise sample — drop out ~20% of ticks
        double sample = Noise.White(t * 7.3 + Damage * 31.1);
        return (sample > 0.8) ? 1.0 : 0.0;
    }
}
