using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.Game;
using Inferior.Gameplay;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// Owns flat-hyperspace flight: preamble alignment, in-hyperspace travel, dropout, and
/// the associated 3D sheet / 2D overlay drawing. Driven from SystemSpaceState, which
/// supplies the camera, current star, and ship snapshot on every call — none of those
/// are cached here since they can be reassigned out from under a stored reference
/// (see HandleKeyboard's debug-camera Home-key reset and EnterSystem's star swap).
/// </summary>
public sealed class FlatHyperspaceController
{
    private readonly GraphicsDevice _gd;
    private readonly Texture2D      _pixel;
    private readonly SpaceSimulation _simulation;
    private readonly TargetingSystem _targeting;
    private readonly Action<Star, DVec3, Quaternion, FlightMode> _enterSystem;

    // Hyperspace is driven entirely from FlatHyperspaceController — the sim is frozen at PlayerInput.Zero.
    private HyperspacePlane?          _hyperPlane;
    private DVec3                     _hyperGalPos;      // player position in galactic ly (moves each tick)
    private DVec3                     _hyperForward;     // current travel direction (unit vector, in-plane)
    private IHyperspaceSheetRenderer? _sheetRenderer;
    private FlightMode                _hyperMode = FlightMode.SystemNewtonian;  // local hyperspace state

    // Preamble state machine
    private HyperPreamblePhase _preamblePhase;
    private double             _preambleTimer;   // seconds into current phase
    private double             _dotBrightness;   // 0..1 — dot fade-in progress
    private double             _lineProgress;    // 0..1 — line spread progress
    private double             _sheetsProgress;  // 0..1 — sheet spread progress

    private const float MouseSensitivity = 0.0012f;

    private enum HyperPreamblePhase { Aligning, DotFadeIn, LineGrow, Pause, SheetsGrow }

    public FlatHyperspaceController(
        GraphicsDevice gd,
        Texture2D pixel,
        SpaceSimulation simulation,
        TargetingSystem targeting,
        Action<Star, DVec3, Quaternion, FlightMode> enterSystem)
    {
        _gd          = gd;
        _pixel       = pixel;
        _simulation  = simulation;
        _targeting   = targeting;
        _enterSystem = enterSystem;
    }

    public FlightMode Mode             => _hyperMode;
    public DVec3      GalacticPosition => _hyperGalPos;

    // Called from SystemSpaceState.HandleKeyboard when H is freshly pressed.
    public void HandleKey(Camera3D camera, Star currentStar, SpaceSimulation.ShipSnapshot? shipSnap)
    {
        if (_hyperMode == FlightMode.EnteringFlatHyperspace ||
            _hyperMode == FlightMode.FlatHyperspace)
        {
            ExitHyperspace(camera, currentStar);
            return;
        }

        // Enter preamble — build the plane from current ship orientation
        var snap = shipSnap;
        if (snap == null) return;

        // Use camera orientation vectors (already computed from quaternion by Camera3D)
        DVec3 up  = new(camera.Up.X,      camera.Up.Y,      camera.Up.Z);
        DVec3 fwd = new(camera.Forward.X, camera.Forward.Y, camera.Forward.Z);

        // Convert ship universe position (metres) to galactic ly
        DVec3 galPos = snap.Position / 9.4607e15 + currentStar.GalacticPos;

        _hyperGalPos  = galPos;
        _hyperForward = fwd; // will be re-projected to plane in HyperspacePlane ctor

        var allStars = GalaxyGenerator.Generate();
        _hyperPlane = new HyperspacePlane(galPos, up, fwd, allStars);
        _hyperForward = _hyperPlane.Forward;  // normalised in-plane forward

        _sheetRenderer ??= new GridHyperspaceSheetRenderer(_gd);

        _preamblePhase  = HyperPreamblePhase.Aligning;
        _preambleTimer  = 0;
        _dotBrightness  = 0;
        _lineProgress   = 0;
        _sheetsProgress = 0;
        _hyperMode      = FlightMode.EnteringFlatHyperspace;

        // Freeze sim input for the duration of hyperspace
        _simulation.SetInput(PlayerInput.Zero);
    }

    // Called once per Update() tick, unconditionally — no-ops internally unless
    // Mode is EnteringFlatHyperspace or FlatHyperspace.
    public void Update(double dt, MouseState mouse, Camera3D camera,
                        Star currentStar, SpaceSimulation.ShipSnapshot? shipSnap)
    {
        if (_hyperMode == FlightMode.EnteringFlatHyperspace)
            UpdateEnteringHyperspace(dt, camera, shipSnap);
        else if (_hyperMode == FlightMode.FlatHyperspace)
            UpdateFlatHyperspace(dt, mouse, camera, currentStar);
    }

    // 3D pass — call once before sb.Begin(), same spot as the old _sheetRenderer
    // call in Draw(). No-ops internally if not in a hyperspace mode.
    public void DrawSheets(GraphicsDevice gd, Camera3D camera)
    {
        if (_hyperMode is FlightMode.EnteringFlatHyperspace or FlightMode.FlatHyperspace)
            _sheetRenderer?.Draw(gd, camera, (float)_sheetsProgress, GetPlaneBasis(camera));
    }

    // 2D pass — call inside the existing sb.Begin()/End() block, same position
    // as the old DrawHyperspaceOverlay call. No-ops internally if not in a
    // hyperspace mode.
    public void DrawOverlay(SpriteBatch sb)
    {
        if (_hyperMode != FlightMode.EnteringFlatHyperspace &&
            _hyperMode != FlightMode.FlatHyperspace) return;

        int w  = _gd.Viewport.Width;
        int h  = _gd.Viewport.Height;
        int cx = w / 2;
        int cy = h / 2;

        // ── Dot (centre, appears first) ─────────────────────────────────
        if (_dotBrightness > 0)
        {
            int  dotAlpha = (int)(255 * _dotBrightness);
            var  dotCol   = new Color(dotAlpha, dotAlpha, dotAlpha, dotAlpha);
            DrawDot(sb, cx, cy, 3, dotCol);
        }

        // ── Horizon line (stretches left-right) ──────────────────────────
        if (_lineProgress > 0)
        {
            int halfLen = (int)(_lineProgress * (w / 2 + 4));
            int lineAlpha = (int)(200 * Math.Min(_dotBrightness + _lineProgress, 1.0));
            var lineCol = new Color(lineAlpha, lineAlpha, lineAlpha, lineAlpha);
            sb.Draw(_pixel, new Rectangle(cx - halfLen, cy, halfLen * 2, 1), lineCol);
            sb.Draw(_pixel, new Rectangle(cx - halfLen, cy + 1, halfLen * 2, 1),
                new Color(lineCol.R / 2, lineCol.G / 2, lineCol.B / 2, lineCol.A / 2));
        }
    }

    // NOTE: UpdateEnteringHyperspace originally took a KeyboardState parameter that was
    // never read in its body — dropped rather than carried forward unused. Flag for Timo:
    // this may have been intended for a not-yet-built "cancel preamble on keypress" feature.
    private void UpdateEnteringHyperspace(double dt, Camera3D camera, SpaceSimulation.ShipSnapshot? shipSnap)
    {
        if (_hyperPlane == null) return;

        var snap = shipSnap;
        _simulation.SetInput(PlayerInput.Zero);  // no player ship input during preamble

        switch (_preamblePhase)
        {
            case HyperPreamblePhase.Aligning:
            {
                // Skip alignment if no hyperspace target set
                if (!_targeting.HasHyperspaceTarget)
                {
                    SyncHyperForwardToCamera(camera);
                    AdvancePreamble(HyperPreamblePhase.DotFadeIn);
                    break;
                }

                // Auto-rotate the camera (and ship) toward the hyperspace target direction
                if (snap != null)
                {
                    DVec3 targetDir = _targeting.HyperspaceTargetDirection;
                    var   targetF   = new Vector3((float)targetDir.X, (float)targetDir.Y, (float)targetDir.Z);
                    float angle     = (float)Math.Acos(Math.Clamp(Vector3.Dot(camera.Forward, targetF), -1f, 1f));

                    if (angle * MathHelper.ToDegrees(1f) <= (float)FlatHyperspaceConstants.AlignThresholdDeg)
                    {
                        SyncHyperForwardToCamera(camera);
                        AdvancePreamble(HyperPreamblePhase.DotFadeIn);
                    }
                    else
                    {
                        // Slerp camera toward target; lock up to the hyperspace plane normal.
                        float step   = (float)(FlatHyperspaceConstants.AutoAlignRateRadPerSec * dt);
                        float t      = MathHelper.Clamp(step / angle, 0f, 1f);
                        var   newFwd = Vector3.Normalize(Vector3.Lerp(camera.Forward, targetF, t));
                        var   planeUp = new Vector3(
                            (float)_hyperPlane.Normal.X,
                            (float)_hyperPlane.Normal.Y,
                            (float)_hyperPlane.Normal.Z);
                        var   newOri = QuaternionFromForwardUp(newFwd, planeUp);
                        camera.SetPose(camera.UniversePosition, newOri);
                    }
                }
                break;
            }

            case HyperPreamblePhase.DotFadeIn:
            {
                _preambleTimer += dt;
                _dotBrightness  = Math.Min(_preambleTimer / FlatHyperspaceConstants.DotFadeInDuration, 1.0);
                if (_dotBrightness >= 1.0)
                    AdvancePreamble(HyperPreamblePhase.LineGrow);
                break;
            }

            case HyperPreamblePhase.LineGrow:
            {
                _preambleTimer += dt;
                double raw     = _preambleTimer / FlatHyperspaceConstants.LineGrowDuration;
                _lineProgress  = EaseIn(Math.Min(raw, 1.0));
                if (raw >= 1.0)
                    AdvancePreamble(HyperPreamblePhase.Pause);
                break;
            }

            case HyperPreamblePhase.Pause:
            {
                _preambleTimer += dt;
                if (_preambleTimer >= FlatHyperspaceConstants.PauseDuration)
                    AdvancePreamble(HyperPreamblePhase.SheetsGrow);
                break;
            }

            case HyperPreamblePhase.SheetsGrow:
            {
                _preambleTimer  += dt;
                double raw       = _preambleTimer / FlatHyperspaceConstants.SheetsGrowDuration;
                _sheetsProgress  = EaseIn(Math.Min(raw, 1.0));
                _sheetRenderer?.Update(dt, camera, GetPlaneBasis(camera));
                if (raw >= 1.0)
                {
                    _sheetsProgress = 1.0;
                    _hyperMode      = FlightMode.FlatHyperspace;
                }
                break;
            }
        }
    }

    private void UpdateFlatHyperspace(double dt, MouseState mouse, Camera3D camera, Star currentStar)
    {
        if (_hyperPlane == null) return;

        _simulation.SetInput(PlayerInput.Zero);
        _sheetRenderer?.Update(dt, camera, GetPlaneBasis(camera));

        // Steer — mouse yaw only; pitch/roll ignored. Uses the focus-corrected lookMouse.
        int    cx       = _gd.Viewport.Width / 2;
        double yawDelta = -(mouse.X - cx) * MouseSensitivity;

        if (Math.Abs(yawDelta) > 1e-6)
        {
            // Rotate _hyperForward around the plane normal
            double cosA = Math.Cos(yawDelta);
            double sinA = Math.Sin(yawDelta);
            DVec3 n     = _hyperPlane.Normal;
            // Rodrigues rotation around n
            _hyperForward = (_hyperForward * cosA
                           + DVec3.Cross(n, _hyperForward) * sinA
                           + n * DVec3.Dot(n, _hyperForward) * (1 - cosA)).Normalized();
        }

        // Advance position
        _hyperGalPos = _hyperGalPos + _hyperForward * (FlatHyperspaceConstants.SpeedLYPerSecond * dt);

        // Move ship / camera to match — convert ly to metres offset from current star
        DVec3 universePosMetres = (_hyperGalPos - currentStar.GalacticPos) * 9.4607e15;
        var   fwdF = new Vector3((float)_hyperForward.X, (float)_hyperForward.Y, (float)_hyperForward.Z);
        var   upF  = new Vector3((float)_hyperPlane.Normal.X, (float)_hyperPlane.Normal.Y, (float)_hyperPlane.Normal.Z);
        var   ori  = QuaternionFromForwardUp(fwdF, upF);
        camera.SetPose(universePosMetres, ori);
        _simulation.TeleportShip(universePosMetres, ori);

        // Dropout check
        var hit = _hyperPlane.CheckDropout(_hyperGalPos, _hyperForward);
        if (hit != null)
        {
            bool isTarget = _targeting.HasHyperspaceTarget &&
                            string.Equals(hit.Star.Name, _targeting.HyperspaceTargetName,
                                          StringComparison.OrdinalIgnoreCase);
            DropOutOfHyperspace(hit.Star, isTarget, camera, currentStar);
        }
    }

    private void DropOutOfHyperspace(Star landingStar, bool isTarget, Camera3D camera, Star currentStar)
    {
        _hyperMode      = FlightMode.SystemNewtonian;
        _sheetsProgress = 0;
        _lineProgress   = 0;
        _dotBrightness  = 0;
        _hyperPlane     = null;

        var rng = new System.Random();
        if (isTarget)
        {
            // Precision arrival: 0.5–1 AU from star, ship roughly pointing at star
            double distAU  = FlatHyperspaceConstants.ArrivalDropAU_Min
                           + rng.NextDouble() * (FlatHyperspaceConstants.ArrivalDropAU_Max
                                                - FlatHyperspaceConstants.ArrivalDropAU_Min);
            double distM   = distAU * Units.AU;
            double azimuth = rng.NextDouble() * Math.Tau;
            double elev    = (rng.NextDouble() - 0.5) * Math.Tau
                           * FlatHyperspaceConstants.ArrivalAngleToleranceDeg / 360.0;
            var spawnPos   = new DVec3(
                Math.Cos(azimuth) * Math.Cos(elev) * distM,
                Math.Sin(elev) * distM,
                Math.Sin(azimuth) * Math.Cos(elev) * distM);
            var toStar     = (-spawnPos).Normalized();
            var fwdF       = new Vector3((float)toStar.X, (float)toStar.Y, (float)toStar.Z);
            var ori        = QuaternionFromForwardUp(fwdF, Vector3.UnitY);
            camera.SetPose(spawnPos, ori);
            _simulation.TeleportShip(spawnPos, ori);
            // Enter the target system if different from current
            if (landingStar.GalaxyIndex != currentStar.GalaxyIndex)
                _enterSystem(landingStar, spawnPos, ori, FlightMode.SystemSlipstream);
            else
                _simulation.SetFlightMode(FlightMode.SystemSlipstream);
        }
        else
        {
            // Penalty drop: 80–120 AU from the disrupting star
            double distAU  = FlatHyperspaceConstants.PenaltyDropAU_Min
                           + rng.NextDouble() * (FlatHyperspaceConstants.PenaltyDropAU_Max
                                                - FlatHyperspaceConstants.PenaltyDropAU_Min);
            double distM   = distAU * Units.AU;
            double azimuth = rng.NextDouble() * Math.Tau;
            var spawnPos   = new DVec3(Math.Cos(azimuth) * distM, 0, Math.Sin(azimuth) * distM);
            var ori        = camera.Orientation;
            camera.SetPose(spawnPos, ori);
            _simulation.TeleportShip(spawnPos, ori);
            if (landingStar.GalaxyIndex != currentStar.GalaxyIndex)
                _enterSystem(landingStar, spawnPos, ori, FlightMode.SystemNewtonian);
            else
                _simulation.SetFlightMode(FlightMode.SystemNewtonian);
        }
    }

    private void ExitHyperspace(Camera3D camera, Star currentStar)
    {
        _hyperMode      = FlightMode.SystemNewtonian;
        _sheetsProgress = 0;
        _lineProgress   = 0;
        _dotBrightness  = 0;
        _hyperPlane     = null;
        _simulation.SetFlightMode(FlightMode.SystemNewtonian);

        // Check for nearby star — generate if within NearbySystemRadiusLY
        var allStars = GalaxyGenerator.Generate();
        Star? nearest = null;
        double nearestDist = double.MaxValue;
        foreach (var star in allStars)
        {
            double d = DVec3.Distance(_hyperGalPos, star.GalacticPos);
            if (d < nearestDist) { nearestDist = d; nearest = star; }
        }
        if (nearest != null && nearestDist <= FlatHyperspaceConstants.NearbySystemRadiusLY
            && nearest.GalaxyIndex != currentStar.GalaxyIndex)
        {
            var pos = camera.UniversePosition;
            var ori = camera.Orientation;
            _enterSystem(nearest, pos, ori, FlightMode.SystemNewtonian);
        }
    }

    private void DrawDot(SpriteBatch sb, int x, int y, int r, Color c)
    {
        sb.Draw(_pixel, new Rectangle(x - r, y - r, r * 2 + 1, r * 2 + 1), c);
    }

    private void AdvancePreamble(HyperPreamblePhase next)
    {
        _preamblePhase = next;
        _preambleTimer = 0;
    }

    private static double EaseIn(double t) => t * t;  // quadratic ease-in

    private PlaneBasis GetPlaneBasis(Camera3D camera)
    {
        // Always derived from the camera — it is the ground truth for current orientation
        // whether we are in preamble alignment or active hyperspace flight.
        // cross(forward, up) = right: cross(-Z, +Y) = +X (verified).
        var fwd   = camera.Forward;
        var up    = camera.Up;
        var right = Vector3.Normalize(Vector3.Cross(fwd, up));
        return new PlaneBasis(up, fwd, right);
    }

    // Called when alignment completes (or is skipped). Locks _hyperForward to the camera's
    // current facing so FlatHyperspace drives the ship in the correct direction.
    private void SyncHyperForwardToCamera(Camera3D camera)
    {
        // Project camera forward onto the hyperspace plane to ensure it stays in-plane.
        if (_hyperPlane != null)
        {
            DVec3 camFwd = new(camera.Forward.X, camera.Forward.Y, camera.Forward.Z);
            DVec3 n      = _hyperPlane.Normal;
            DVec3 proj   = camFwd - n * DVec3.Dot(camFwd, n);
            _hyperForward = proj.LengthSquared > 1e-10 ? proj.Normalized() : _hyperPlane.Forward;
        }
        else
        {
            _hyperForward = new DVec3(camera.Forward.X, camera.Forward.Y, camera.Forward.Z);
        }
    }

    // Builds a Quaternion from a forward vector and a reference up, same as CreateLookAt logic.
    private static Quaternion QuaternionFromForwardUp(Vector3 forward, Vector3 up)
    {
        forward = Vector3.Normalize(forward);
        var right = Vector3.Normalize(Vector3.Cross(up, forward));
        up = Vector3.Cross(forward, right);
        var m = new Matrix(
            right.X,   right.Y,   right.Z,   0,
            up.X,      up.Y,      up.Z,      0,
            -forward.X, -forward.Y, -forward.Z, 0,
            0, 0, 0, 1);
        return Quaternion.CreateFromRotationMatrix(m);
    }
}
