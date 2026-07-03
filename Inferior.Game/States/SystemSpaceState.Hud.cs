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

    // ── 2D HUD ────────────────────────────────────────────────────────────────

    private void DrawCrosshair(SpriteBatch sb)
    {
        int cx  = _gd.Viewport.Width  / 2;
        int cy  = _gd.Viewport.Height / 2;
        int arm = 10;
        int gap = 4;

        // Four arms only — no centre dot (avoids obscuring distant targets)
        sb.Draw(_pixel, new Rectangle(cx - arm - gap, cy, arm, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(cx + gap + 1,   cy, arm, 1), Color.White);
        sb.Draw(_pixel, new Rectangle(cx, cy - arm - gap, 1, arm), Color.White);
        sb.Draw(_pixel, new Rectangle(cx, cy + gap + 1,   1, arm), Color.White);
    }

    private void DrawHUD(SpriteBatch sb)
    {
        int bottom = _gd.Viewport.Height;

        // Relative speed — velocity relative to the reference frame object.
        // Debug mode: camera position delta / dt vs reference.
        // Ship mode: simulation velocity vs reference.
        DVec3 movingVel = _debugCameraMode
            ? _cameraActualVelocity
            : _frameShipSnap?.Velocity ?? DVec3.Zero;
        double relSpeedMs = (movingVel - _refVelocity).Length;

        if (_debugCameraMode)
        {
            DrawText(sb, $"Set: {Units.FormatSpeed(_camera.MoveSpeedMs)}",
                new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
        }
        else
        {
            var snap = _frameShipSnap;
            if (snap != null)
            {
                // Hyperspace modes are driven locally, not by the sim snapshot
                var displayMode = _hyperspace.Mode is FlightMode.EnteringFlatHyperspace
                                           or FlightMode.FlatHyperspace
                    ? _hyperspace.Mode
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
                    double gearSpeed = snap.NewtonianGear < FlightConstants.NewtonianGearSpeeds.Length
                        ? FlightConstants.NewtonianGearSpeeds[snap.NewtonianGear] : 0;
                    string lkmStr   = snap.LkmZone > 0 ? $"  LKM-{snap.LkmZone}" : "";
                    flightLine = $"[{modeName}]  G{snap.NewtonianGear + 1} ({Units.FormatSpeed(gearSpeed)}){lkmStr}";
                }
                else if (snap.FlightMode == FlightMode.SystemSlipstream)
                {
                    flightLine = $"[{modeName}]  H{snap.SlipstreamHarmonicIndex + 1}";
                }
                else
                {
                    flightLine = $"[{modeName}]";
                }
                DrawText(sb, flightLine, new Vector2(16, bottom - 98), ColHUDDim, 0.8f);
            }
        }

        DrawText(sb, $"Speed: {Units.FormatSpeed(relSpeedMs)}  (vs {_refName})",
            new Vector2(16, bottom - 80), ColHUD);

        // Game time
        DrawText(sb, $"T+{Units.FormatTime(_gameTimeSeconds)}", new Vector2(16, bottom - 58), ColHUDDim, 0.8f);

        DrawAtmosPanel(sb);

        // Controls hint — changes with mode
        if (_uiMouseMode)
        {
            DrawText(sb, "UI MODE  —  TAB: return to flight",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(80, 160, 220), 0.72f);
        }
        else if (_debugCameraMode)
        {
            DrawText(sb, "DEBUG CAM  —  Mouse: look   WASD: fwd/strafe   RF: up/down   QE: roll   Shift: fast   Ctrl: slow   F11: ship cam   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), new Color(220, 160, 80), 0.72f);
        }
        else
        {
            DrawText(sb, "Mouse: look   WASD: fwd/strafe   QE: roll   RF: up/down   M: system map   N: galaxy map   F11: debug   TAB: UI",
                new Vector2(16, _gd.Viewport.Height - 30), ColHUDDim, 0.72f);
        }
    }

    // ── Ground radar panel (atmosphere-only) ──────────────────────────────────

    private void DrawAtmosPanel(SpriteBatch sb)
    {
        if (_frameShipSnap?.FlightMode is not (FlightMode.AtmosphericNewtonian or FlightMode.AtmosphericSlipstream)) return;

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
        DrawRect(sb, bgRect, new Color(8, 12, 25, 190));
        DrawRectBorder(sb, bgRect, ColBorder);

        for (int i = 0; i < rows.Length; i++)
        {
            int ly = y + i * LineH;
            DrawText(sb, rows[i].label, new Vector2(x, ly), ColHUDDim, S);
            // Right-align value
            Vector2 valSize = FontHelper.Measure(_font, rows[i].value, S);
            DrawText(sb, rows[i].value, new Vector2(x + ColW - valSize.X, ly), rows[i].color, S);
        }
    }
}
