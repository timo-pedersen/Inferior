using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Gameplay;
using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.UI;

/// <summary>
/// Right-wing cockpit instrument showing current drive mode, gear/harmonic,
/// speed values, and stub bars for future systems (fuel, power, heat).
///
/// Data sourced from DataBus Topics.Flight.* — no snapshot reference needed.
/// Add to CockpitRail.RightWing; bounds updated by SystemSpaceState each frame.
/// </summary>
public sealed class DriveInstrumentPanel : Control
{
    // ── DataBus subscriptions ─────────────────────────────────────────────────

    private double _mode;
    private double _gear;
    private double _gearCount;
    private double _gearCeiling;
    private double _maxGear;
    private double _harmonicIndex;
    private double _harmonicCount;
    private double _lkmZone;
    private double _xStopActive;
    private double _relSpeedMs;
    private double _fwdSpeedMs;
    private double _accelMs2;

    // Handlers stored to allow future unsubscription if needed
    private readonly Action<double>[] _handlers;

    public DriveInstrumentPanel()
    {
        _handlers =
        [
            v => _mode          = v,
            v => _gear          = v,
            v => _gearCount     = v,
            v => _gearCeiling   = v,
            v => _maxGear       = v,
            v => _harmonicIndex = v,
            v => _harmonicCount = v,
            v => _lkmZone       = v,
            v => _xStopActive   = v,
            v => _relSpeedMs    = v,
            v => _fwdSpeedMs    = v,
            v => _accelMs2      = v,
        ];
        DataBus.Instruments.Subscribe(Topics.Flight.Mode,            _handlers[0]);
        DataBus.Instruments.Subscribe(Topics.Flight.Gear,            _handlers[1]);
        DataBus.Instruments.Subscribe(Topics.Flight.GearCount,       _handlers[2]);
        DataBus.Instruments.Subscribe(Topics.Flight.GearCeilingMs,   _handlers[3]);
        DataBus.Instruments.Subscribe(Topics.Flight.MaxGear,         _handlers[4]);
        DataBus.Instruments.Subscribe(Topics.Flight.HarmonicIndex,   _handlers[5]);
        DataBus.Instruments.Subscribe(Topics.Flight.HarmonicCount,   _handlers[6]);
        DataBus.Instruments.Subscribe(Topics.Flight.LkmZone,         _handlers[7]);
        DataBus.Instruments.Subscribe(Topics.Flight.XStopActive,     _handlers[8]);
        DataBus.Instruments.Subscribe(Topics.Flight.RelativeSpeedMs, _handlers[9]);
        DataBus.Instruments.Subscribe(Topics.Flight.ForwardSpeedMs,  _handlers[10]);
        DataBus.Instruments.Subscribe(Topics.Flight.AccelerationMs2, _handlers[11]);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    private const int    PanelW  = 210;  // fixed panel width
    private const int    PanelPad = 5;   // inset from right edge of wing
    private const int    LineH   = 18;
    private const float  S       = 0.78f;

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var ab = AbsoluteBounds;
        if (ab.Width < 10 || ab.Height < 10) return;

        // Panel anchored to the right side of the wing
        int px = ab.Right - PanelW - PanelPad;
        int py = ab.Top;
        int pw = PanelW;
        int ph = ab.Height;
        var panelRect = new Rectangle(px, py, pw, ph);

        var colBack   = theme.PanelBackground;
        var colBorder = theme.PanelBorder;
        var colDim    = theme.TextDisabled;
        var colNorm   = theme.TextNormal;
        var colAccent = theme.Accent;

        renderer.FillRect(sb, panelRect, colBack);
        renderer.DrawRect(sb, panelRect, colBorder, 1);

        var fm = (FlightMode)(int)_mode;

        // ── Header ───────────────────────────────────────────────────────────

        int y = py + 4;
        string modeLabel = fm switch
        {
            FlightMode.SystemNewtonian       => "NEWTON",
            FlightMode.SystemSlipstream      => "SLIP",
            FlightMode.AtmosphericNewtonian  => "ATMO-N",
            FlightMode.AtmosphericSlipstream => "ATMO-S",
            FlightMode.Docked                => "DOCKED",
            _                                => "---",
        };

        renderer.DrawText(sb, "DRIVE", new Vector2(px + 4, y), theme.Font, S, colDim);
        DrawRight(sb, renderer, theme, modeLabel, px, y, pw, S, colAccent);

        // Separator
        y += LineH;
        renderer.DrawLine(sb, new Vector2(px + 4, y), new Vector2(px + pw - 4, y), colBorder);
        y += 3;

        // ── Mode-specific data rows ───────────────────────────────────────────

        if (fm == FlightMode.SystemSlipstream)
        {
            int   hi    = (int)_harmonicIndex;
            int   hc    = (int)_harmonicCount;
            string harmStr  = $"H{hi:D2} / H{hc:D2}";
            string speedStr = Units.FormatSpeed(System.Math.Abs(_fwdSpeedMs));

            DrawRow(sb, renderer, theme, "HARM",  harmStr,  px, ref y, pw, S, colDim, colNorm);
            DrawRow(sb, renderer, theme, "SPEED", speedStr, px, ref y, pw, S, colDim, colNorm);
        }
        else
        {
            int    g   = (int)_gear;
            int    gc  = (int)_gearCount;
            int    mg  = (int)(_maxGear < 0 ? gc : _maxGear);
            string lkm = _lkmZone > 0.5 ? $" LKM{(int)_lkmZone}" : "";
            string gearStr = $"G{g:D2} / G{mg:D2}{lkm}";
            string ceilStr = Units.FormatSpeed(_gearCeiling);

            // Signed forward speed: "+" prefix when positive
            double fwd    = _fwdSpeedMs;
            string fwdStr = (fwd >= 0 ? "+" : "") + Units.FormatSpeed(System.Math.Abs(fwd));
            if (fwd < 0) fwdStr = "-" + Units.FormatSpeed(-fwd);
            string relStr = Units.FormatSpeed(_relSpeedMs);

            double accel   = _accelMs2;
            string accelStr = (accel >= 0 ? "+" : "") + $"{accel:F2} m/s²";

            DrawRow(sb, renderer, theme, "GEAR",  gearStr,  px, ref y, pw, S, colDim, colNorm);
            DrawRow(sb, renderer, theme, "CEIL",  ceilStr,  px, ref y, pw, S, colDim, colNorm);
            DrawRow(sb, renderer, theme, "FWD",   fwdStr,   px, ref y, pw, S, colDim, colNorm);
            DrawRow(sb, renderer, theme, "ACC.",  accelStr, px, ref y, pw, S, colDim, colNorm);
            DrawRow(sb, renderer, theme, "REL",   relStr,   px, ref y, pw, S, colDim, colNorm);

            if (_xStopActive > 0.5)
            {
                y -= LineH;  // replace last row with X-STOP indicator
                DrawRow(sb, renderer, theme, "", "X-STOP", px, ref y, pw, S, colDim,
                    new Color(255, 80, 80));
            }
        }

        // ── Separator before systems stubs ────────────────────────────────────

        // Align separator + stubs to the bottom of the panel
        const int StubRows = 3;
        int stubBlockH = StubRows * LineH + 4;  // 3 rows + top gap
        int sepY = py + ph - stubBlockH - 1;
        if (sepY > y + 4)  // only draw if there's space
        {
            renderer.DrawLine(sb, new Vector2(px + 4, sepY), new Vector2(px + pw - 4, sepY), colBorder);
            y = sepY + 4;
            DrawStubRow(sb, renderer, theme, "FUEL", px, ref y, pw, S, colDim);
            DrawStubRow(sb, renderer, theme, "PWR",  px, ref y, pw, S, colDim);
            DrawStubRow(sb, renderer, theme, "HEAT", px, ref y, pw, S, colDim);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void DrawRow(SpriteBatch sb, UIRenderer renderer, Theme theme,
        string label, string value, int px, ref int y, int pw,
        float scale, Color labelColor, Color valueColor)
    {
        renderer.DrawText(sb, label, new Vector2(px + 4, y), theme.Font, scale, labelColor);
        DrawRight(sb, renderer, theme, value, px, y, pw, scale, valueColor);
        y += LineH;
    }

    private static void DrawStubRow(SpriteBatch sb, UIRenderer renderer, Theme theme,
        string label, int px, ref int y, int pw, float scale, Color labelColor)
    {
        renderer.DrawText(sb, label, new Vector2(px + 4, y), theme.Font, scale, labelColor);

        // Empty bar (stub — no backing data yet)
        const int BarW = 70;
        const int BarH = 7;
        int bx = px + 44;
        int by = y + (LineH - BarH) / 2;
        renderer.DrawRect(sb, new Rectangle(bx, by, BarW, BarH), theme.PanelBorder, 1);

        // "---" value placeholder
        DrawRight(sb, renderer, theme, "---", px, y, pw, scale, theme.TextDisabled);
        y += LineH;
    }

    private static void DrawRight(SpriteBatch sb, UIRenderer renderer, Theme theme,
        string text, int panelX, int y, int panelW, float scale, Color color)
    {
        var size = renderer.MeasureText(text, theme.Font, scale);
        renderer.DrawText(sb, text,
            new Vector2(panelX + panelW - (int)size.X - 5, y),
            theme.Font, scale, color);
    }
}
