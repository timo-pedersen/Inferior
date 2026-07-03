using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{

    // ── Flat Hyperspace ───────────────────────────────────────────────────────

    private enum HyperPreamblePhase { Aligning, DotFadeIn, LineGrow, Pause, SheetsGrow }

    private void HandleHyperspaceKey()
    {
        if (_hyperMode == FlightMode.EnteringFlatHyperspace ||
            _hyperMode == FlightMode.FlatHyperspace)
        {
            ExitHyperspace();
            return;
        }

        // Enter preamble — build the plane from current ship orientation
        var snap = _frameShipSnap;
        if (snap == null) return;

        // Use camera orientation vectors (already computed from quaternion by Camera3D)
        DVec3 up  = new(_camera.Up.X,      _camera.Up.Y,      _camera.Up.Z);
        DVec3 fwd = new(_camera.Forward.X, _camera.Forward.Y, _camera.Forward.Z);

        // Convert ship universe position (metres) to galactic ly
        DVec3 galPos = snap.Position / 9.4607e15 + _star.GalacticPos;

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

    private void UpdateEnteringHyperspace(double dt, KeyboardState keys)
    {
        if (_hyperPlane == null) return;

        var snap = _frameShipSnap;
        _simulation.SetInput(PlayerInput.Zero);  // no player ship input during preamble

        switch (_preamblePhase)
        {
            case HyperPreamblePhase.Aligning:
            {
                // Skip alignment if no hyperspace target set
                if (!_targeting.HasHyperspaceTarget)
                {
                    SyncHyperForwardToCamera();
                    AdvancePreamble(HyperPreamblePhase.DotFadeIn);
                    break;
                }

                // Auto-rotate the camera (and ship) toward the hyperspace target direction
                if (snap != null)
                {
                    DVec3 targetDir = _targeting.HyperspaceTargetDirection;
                    var   targetF   = new Vector3((float)targetDir.X, (float)targetDir.Y, (float)targetDir.Z);
                    float angle     = (float)Math.Acos(Math.Clamp(Vector3.Dot(_camera.Forward, targetF), -1f, 1f));

                    if (angle * MathHelper.ToDegrees(1f) <= (float)FlatHyperspaceConstants.AlignThresholdDeg)
                    {
                        SyncHyperForwardToCamera();
                        AdvancePreamble(HyperPreamblePhase.DotFadeIn);
                    }
                    else
                    {
                        // Slerp camera toward target; lock up to the hyperspace plane normal.
                        float step   = (float)(FlatHyperspaceConstants.AutoAlignRateRadPerSec * dt);
                        float t      = MathHelper.Clamp(step / angle, 0f, 1f);
                        var   newFwd = Vector3.Normalize(Vector3.Lerp(_camera.Forward, targetF, t));
                        var   planeUp = new Vector3(
                            (float)_hyperPlane.Normal.X,
                            (float)_hyperPlane.Normal.Y,
                            (float)_hyperPlane.Normal.Z);
                        var   newOri = QuaternionFromForwardUp(newFwd, planeUp);
                        _camera.SetPose(_camera.UniversePosition, newOri);
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
                _sheetRenderer?.Update(dt, _camera, GetPlaneBasis());
                if (raw >= 1.0)
                {
                    _sheetsProgress = 1.0;
                    _hyperMode      = FlightMode.FlatHyperspace;
                }
                break;
            }
        }
    }

    private void UpdateFlatHyperspace(double dt, MouseState mouse)
    {
        if (_hyperPlane == null) return;

        _simulation.SetInput(PlayerInput.Zero);
        _sheetRenderer?.Update(dt, _camera, GetPlaneBasis());

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
        DVec3 universePosMetres = (_hyperGalPos - _star.GalacticPos) * 9.4607e15;
        var   fwdF = new Vector3((float)_hyperForward.X, (float)_hyperForward.Y, (float)_hyperForward.Z);
        var   upF  = new Vector3((float)_hyperPlane.Normal.X, (float)_hyperPlane.Normal.Y, (float)_hyperPlane.Normal.Z);
        var   ori  = QuaternionFromForwardUp(fwdF, upF);
        _camera.SetPose(universePosMetres, ori);
        _simulation.TeleportShip(universePosMetres, ori);

        // Dropout check
        var hit = _hyperPlane.CheckDropout(_hyperGalPos, _hyperForward);
        if (hit != null)
        {
            bool isTarget = _targeting.HasHyperspaceTarget &&
                            string.Equals(hit.Star.Name, _targeting.HyperspaceTargetName,
                                          StringComparison.OrdinalIgnoreCase);
            DropOutOfHyperspace(hit.Star, isTarget);
        }
    }

    private void DropOutOfHyperspace(Star landingStar, bool isTarget)
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
            _camera.SetPose(spawnPos, ori);
            _simulation.TeleportShip(spawnPos, ori);
            // Enter the target system if different from current
            if (landingStar.GalaxyIndex != _star.GalaxyIndex)
                EnterSystem(landingStar, spawnPos, ori, FlightMode.SystemSlipstream);
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
            var ori        = _camera.Orientation;
            _camera.SetPose(spawnPos, ori);
            _simulation.TeleportShip(spawnPos, ori);
            if (landingStar.GalaxyIndex != _star.GalaxyIndex)
                EnterSystem(landingStar, spawnPos, ori, FlightMode.SystemNewtonian);
            else
                _simulation.SetFlightMode(FlightMode.SystemNewtonian);
        }
    }

    private void ExitHyperspace()
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
            && nearest.GalaxyIndex != _star.GalaxyIndex)
        {
            var pos = _camera.UniversePosition;
            var ori = _camera.Orientation;
            EnterSystem(nearest, pos, ori, FlightMode.SystemNewtonian);
        }
    }

    private void DrawHyperspaceOverlay(SpriteBatch sb)
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

    private PlaneBasis GetPlaneBasis()
    {
        // Always derived from the camera — it is the ground truth for current orientation
        // whether we are in preamble alignment or active hyperspace flight.
        // cross(forward, up) = right: cross(-Z, +Y) = +X (verified).
        var fwd   = _camera.Forward;
        var up    = _camera.Up;
        var right = Vector3.Normalize(Vector3.Cross(fwd, up));
        return new PlaneBasis(up, fwd, right);
    }

    // Called when alignment completes (or is skipped). Locks _hyperForward to the camera's
    // current facing so FlatHyperspace drives the ship in the correct direction.
    private void SyncHyperForwardToCamera()
    {
        // Project camera forward onto the hyperspace plane to ensure it stays in-plane.
        if (_hyperPlane != null)
        {
            DVec3 camFwd = new(_camera.Forward.X, _camera.Forward.Y, _camera.Forward.Z);
            DVec3 n      = _hyperPlane.Normal;
            DVec3 proj   = camFwd - n * DVec3.Dot(camFwd, n);
            _hyperForward = proj.LengthSquared > 1e-10 ? proj.Normalized() : _hyperPlane.Forward;
        }
        else
        {
            _hyperForward = new DVec3(_camera.Forward.X, _camera.Forward.Y, _camera.Forward.Z);
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

    // Enters a different star system, re-using OnEnter logic without a full state transition.
    private void EnterSystem(Star star, DVec3 spawnPos, Quaternion spawnOri, FlightMode mode)
    {
        _star   = star;
        _system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        _eclipticRotation = Matrix.Identity;  // reset until proper ecliptic rotation is set

        // Rebuild skybox for new star
        (_skyboxPoints, _skyboxGlowVerts, _targetableStars) = BuildSkybox(_star, GalaxyGenerator.Generate());

        _camera.SetPose(spawnPos, spawnOri);
        _simulation.TeleportShip(spawnPos, spawnOri);
        _simulation.SetFlightMode(mode);

        DataBus.System.Publish(Topics.System.All, new($"Arrived in {star.Name}"));
    }
}
