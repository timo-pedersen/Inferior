using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Gameplay;
using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.UI;

public sealed partial class CockpitUI
{
    public void DrawShipForwardReticle(
        SpriteBatch sb,
        ShipForwardReticleProjection projection)
    {
        int cx = (int)MathF.Round(projection.ScreenPosition.X);
        int cy = (int)MathF.Round(projection.ScreenPosition.Y);
        int arm = 10;
        int gap = 4;
        Color colour = projection.IsClampedToViewport
            ? new Color(255, 214, 92)
            : Color.White;

        // Four arms only — no centre dot (avoids obscuring distant targets)
        sb.Draw(_pixel, new Rectangle(cx - arm - gap, cy, arm, 1), colour);
        sb.Draw(_pixel, new Rectangle(cx + gap + 1, cy, arm, 1), colour);
        sb.Draw(_pixel, new Rectangle(cx, cy - arm - gap, 1, arm), colour);
        sb.Draw(_pixel, new Rectangle(cx, cy + gap + 1, 1, arm), colour);
    }

    public void DrawHud(SpriteBatch sb,
        bool debugCameraMode, DVec3 cameraActualVelocity, DVec3 refVelocity, string refName,
        SpaceSimulation.ShipSnapshot? shipSnap, double gameTimeSeconds,
        bool uiMouseMode, FlightMode hyperspaceMode, double cameraMoveSpeedMs,
        bool showPropulsionDiagnostics)
    {
        int bottom = _gd.Viewport.Height;

        // Relative speed — velocity relative to the reference frame object.
        // Debug mode: camera position delta / dt vs reference.
        // Ship mode: simulation velocity vs reference.
        DVec3 movingVel = debugCameraMode
            ? cameraActualVelocity
            : shipSnap?.Velocity ?? DVec3.Zero;
        double relSpeedMs = (movingVel - refVelocity).Length;

        if (debugCameraMode)
        {
            SpritePrimitives.DrawText(sb, _font, $"Set: {Units.FormatSpeed(cameraMoveSpeedMs)}",
                new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
        }
        else
        {
            var snap = shipSnap;
            if (snap != null)
            {
                // Hyperspace modes are driven locally, not by the sim snapshot
                var displayMode = hyperspaceMode is FlightMode.EnteringFlatHyperspace
                                           or FlightMode.FlatHyperspace
                    ? hyperspaceMode
                    : snap.FlightMode;
                string modeName = displayMode switch
                {
                    FlightMode.SystemNewtonian        => "NEWTON",
                    FlightMode.SystemSlipstream       => "SLIPSTREAM",
                    FlightMode.AtmosphericNewtonian   => "ATMO",
                    FlightMode.AtmosphericSlipstream  => "ATMO-SLIP",
                    FlightMode.Docked                 => "DOCKED",
                    FlightMode.EnteringFlatHyperspace => "HYPERSPACE PREAMBLE",
                    FlightMode.FlatHyperspace         => "HYPERSPACE",
                    _                                 => "—",
                };
                string flightLine;
                if (snap.FlightMode == FlightMode.SystemNewtonian)
                {
                    double gearSpeed = snap.Propulsion?.SpeedCeilingMps ?? 0.0;
                    string lkmStr   = snap.LkmZone > 0 ? $"  LKM-{snap.LkmZone}" : "";
                    flightLine = $"[{modeName}]  H{snap.NewtonianGear + 1} ({Units.FormatSpeed(gearSpeed)}){lkmStr}";
                }
                else if (snap.FlightMode == FlightMode.SystemSlipstream)
                {
                    flightLine = $"[{modeName}]  H{snap.SlipstreamHarmonicIndex + 1}";
                }
                else
                {
                    flightLine = $"[{modeName}]";
                }
                SpritePrimitives.DrawText(sb, _font, flightLine, new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
            }
        }

        SpritePrimitives.DrawText(sb, _font, $"Speed: {Units.FormatSpeed(relSpeedMs)}  (vs {refName})",
            new Vector2(16, bottom - 80), ColHUD);

        // Game time
        SpritePrimitives.DrawText(sb, _font, $"T+{Units.FormatTime(gameTimeSeconds)}", new Vector2(16, bottom - 58), ColHUDDim, 0.8f);

        DrawAtmosPanel(sb, shipSnap);
        if (showPropulsionDiagnostics && shipSnap?.Propulsion is { } propulsion)
            DrawPropulsionDiagnostics(
                sb,
                shipSnap.HullTypeId,
                propulsion,
                shipSnap.Rotation);

        // Controls hint — changes with mode
        if (uiMouseMode)
        {
            SpritePrimitives.DrawText(sb, _font, "UI MODE  —  TAB: return to flight",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(80, 160, 220), 0.72f);
        }
        else if (debugCameraMode)
        {
            SpritePrimitives.DrawText(sb, _font, "DEBUG CAM  —  Mouse: look   WASD: fwd/strafe   RF: up/down   QE: roll   Shift: fast   Ctrl: slow   F11: ship cam   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(220, 160, 80), 0.72f);
        }
        else
        {
            SpritePrimitives.DrawText(sb, _font, "Mouse Y: pitch   Mouse X: roll   Q/E: yaw   WASD: fwd/strafe   RF/Space: vertical   M: system map   N: galaxy map   F11: debug   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
        }
    }

    private void DrawPropulsionDiagnostics(
        SpriteBatch sb,
        string hullTypeId,
        Inferior.Gameplay.Ship.ShipPropulsionSnapshot propulsion,
        Inferior.Gameplay.Ship.ShipRotationSnapshot? rotation)
    {
        List<string> lines =
        [
            $"PROPULSION  {hullTypeId}",
            $"MASS       {propulsion.CurrentMassKg:N0} kg",
            $"HULL/COMP  {propulsion.HullMassKg:N0} / {propulsion.ComponentMassKg:N0} kg",
            $"ENGINE MASS {propulsion.InstalledEngineMassKg:N0} kg",
            $"ENGINES    {propulsion.InstalledEngineCount} installed / {propulsion.OperationalEngineCount} operational",
            $"SPEED LIMIT {propulsion.SpeedCeilingMps:N1} m/s",
            $"MAX FWD    {propulsion.AvailableForwardForceShipLocalN.Length / 1e6:F3} MN",
            $"MAX REV    {propulsion.AvailableReverseThrustN / 1e6:F3} MN",
            $"MAX LAT    {propulsion.AvailableLateralThrustN / 1e6:F3} MN",
            $"MAX LIFT   {propulsion.AvailableLiftThrustN / 1e6:F3} MN",
            $"MAX TORQUE {propulsion.AvailableRotationalTorqueNm / 1e6:F3} MN m",
            $"COMMAND    {propulsion.TranslationAllocation.Command.Longitudinal:F3} / {propulsion.TranslationAllocation.Command.Lateral:F3} / {propulsion.TranslationAllocation.Command.Vertical:F3}" +
                (propulsion.TranslationAllocation.Command.UseLiftChannel ? " LIFT" : ""),
            $"USAGE      {propulsion.TranslationAllocation.Usage:F3}",
            $"ALLOCATED  {propulsion.TranslationAllocation.Longitudinal:F3} / {propulsion.TranslationAllocation.Lateral:F3} / {propulsion.TranslationAllocation.Vertical:F3}",
            $"APPLIED    {propulsion.AppliedForceShipLocalN.ToString(0)} N",
            $"ACCEL      {propulsion.ResultingAccelerationShipLocalMps2.ToString(2)} m/s2",
            $"LIFT ACCEL {propulsion.MaximumLiftAccelerationMps2:F3} m/s2",
            $"HOVER EST  {propulsion.MaximumHoverGravityG:F3} g max / {propulsion.SafeLandingGravityG:F3} g safe",
        ];
        if (propulsion.Engines.FirstOrDefault() is { } engine)
        {
            lines.Insert(5, $"ENGINE 1/{propulsion.Engines.Count} {engine.FamilyId}");
            lines.Insert(6, $"HARMONY    {engine.SelectedHarmony} / {engine.HarmonyCount}");
            lines.Insert(7, $"HARMONY X  {engine.NormalizedPosition:F4}");
            lines.Insert(8, $"CURVE      {engine.Curve:F4}");
            lines.Insert(9, $"THRUST MULT {engine.ThrustMultiplier:F4}");
        }
        if (rotation is not null)
        {
            lines.Add("ROTATION");
            lines.Add($"BOUNDS      {rotation.ConfiguredDimensionsMeters.ToString(1)} m");
            lines.Add($"INERTIA     {rotation.AxisInertiaKgM2.X:E2} / {rotation.AxisInertiaKgM2.Y:E2} / {rotation.AxisInertiaKgM2.Z:E2}");
            lines.Add($"ANG ACCEL   {rotation.AvailableAngularAccelerationRadPerSec2.ToString(4)} rad/s2");
            lines.Add($"ANG VEL     {rotation.AngularVelocityLocalRadPerSec.ToString(3)} rad/s");
            lines.Add($"TARGET      {rotation.TargetAngularVelocityLocalRadPerSec.ToString(3)} rad/s");
            lines.Add($"ASSIST      {(rotation.FlightAssistOn ? "ON" : "OFF")}");
        }

        const int x = 14;
        const int y = 12;
        const int width = 600;
        const int lineHeight = 18;
        int height = lines.Count * lineHeight + 14;
        sb.Draw(_pixel, new Rectangle(x, y, width, height), new Color(5, 9, 16, 220));
        sb.Draw(_pixel, new Rectangle(x, y, width, 1), ColBorder);
        sb.Draw(_pixel, new Rectangle(x, y + height - 1, width, 1), ColBorder);
        for (int i = 0; i < lines.Count; i++)
        {
            SpritePrimitives.DrawText(
                sb,
                _font,
                lines[i],
                new Vector2(x + 8, y + 7 + i * lineHeight),
                i == 0 ? ColHUD : ColHUDDim,
                0.72f);
        }
    }

    // ── Ground radar panel (atmosphere-only) ──────────────────────────────────

    private void DrawAtmosPanel(SpriteBatch sb, SpaceSimulation.ShipSnapshot? shipSnap)
    {
        if (shipSnap?.FlightMode is not (FlightMode.AtmosphericNewtonian or FlightMode.AtmosphericSlipstream)) return;

        string altStr  = _pcAlt < 10_000.0
            ? $"{_pcAlt:N0} m"
            : $"{_pcAlt / 1000.0:F1} km";
        string vsStr   = (_pcVs >= 0 ? "+" : "") + $"{_pcVs:F0} m/s";
        string latStr  = $"{System.Math.Abs(_pcLat):F1}° {(_pcLat >= 0 ? "N" : "S")}";
        string lonStr  = $"{System.Math.Abs(_pcLon):F1}° {(_pcLon >= 0 ? "E" : "W")}";
        string hdgStr  = $"{(int)System.Math.Round(_pcHdg):D3}°";
        string gsStr   = $"{(int)_pcGs} m/s";
        string tempStr = $"{(int)_pcTemp} K";
        string presStr = _pcPress >= 0.01
            ? $"{_pcPress:F2} bar"
            : _pcPress > 0.0 ? $"{_pcPress * 1000.0:F1} mbar" : "--- bar";
        bool   presGreen = _pcPress >= FlightConstants.AtmoSlipstreamCutoffBar;

        const int ColW  = 200;
        const int LineH = 20;
        const float S   = 0.8f;
        int x = _gd.Viewport.Width - ColW - 12;
        int y = 10;

        (string label, string value, Color color)[] rows =
        [
            ("ALT",  altStr,  ColHUD),
            ("VS",   vsStr,   ColHUD),
            ("LAT",  latStr,  ColHUD),
            ("LON",  lonStr,  ColHUD),
            ("HDG",  hdgStr,  ColHUD),
            ("GS",   gsStr,   ColHUD),
            ("TEMP", tempStr, ColHUD),
            ("PRES", presStr, presGreen ? new Color(80, 220, 100) : ColHUD),
        ];

        var bgRect = new Rectangle(x - 6, y - 4, ColW + 10, rows.Length * LineH + 8);
        SpritePrimitives.DrawRect(sb, _pixel, bgRect, new Color(8, 12, 25, 190));
        SpritePrimitives.DrawRectBorder(sb, _pixel, bgRect, ColBorder);

        for (int i = 0; i < rows.Length; i++)
        {
            int ly = y + i * LineH;
            SpritePrimitives.DrawText(sb, _font, rows[i].label, new Vector2(x, ly), ColHUDDim, S);
            // Right-align value
            Vector2 valSize = FontHelper.Measure(_font, rows[i].value, S);
            SpritePrimitives.DrawText(sb, _font, rows[i].value, new Vector2(x + ColW - valSize.X, ly), rows[i].color, S);
        }
    }

    public void DrawHudAlert(SpriteBatch sb)
    {
        if (_ui == null) return;
        _hudAlert.Draw(sb, _ui.Renderer, _font, _gd.Viewport.Width, _gd.Viewport.Height);
    }
}
