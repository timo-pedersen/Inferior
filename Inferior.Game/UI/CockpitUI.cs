using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Game;
using Inferior.Game.States;
using Inferior.Gameplay.Sensors;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.UI;

/// <summary>
/// Owns the flight-instrumentation/HUD subsystem: the DataBus-driven UI control tree
/// (meters, panels, cockpit rail), targeting readouts, and 2D HUD drawing. Constructed
/// fresh per SystemSpaceState.OnEnter; disposed on OnExit.
///
/// Camera3D, Star, and ship snapshots are never cached — they're passed as parameters
/// on every call, since SystemSpaceState can reassign the camera (debug-cam Home reset)
/// or swap stars (EnterSystem) out from under any stored reference.
/// </summary>
public sealed partial class CockpitUI : IDisposable
{
    // ── Injected dependencies (stable for this instance's lifetime) ────────────
    private readonly GraphicsDevice         _gd;
    private readonly SpriteFont             _font;
    private readonly Texture2D              _pixel;
    private readonly TargetingSystem        _targeting;
    private readonly HudAlertDisplay        _hudAlert;
    private readonly Func<DVec3, DVec3>     _galaxyToEcliptic;
    private readonly Action<bool>           _onShieldToggle;
    private readonly Action                 _onShipCycle;

    // ── DataBus UI ────────────────────────────────────────────────────────────
    private UIManager?       _ui;
    private DriveInstrumentPanel? _drivePanel;
    private InstrumentMeter? _reactorPowerOutputMeter;
    private InstrumentMeter? _reactorDrawnMeter;
    private InstrumentMeter? _busConsumptionMeter;
    private InstrumentMeter? _connectorFlowMeter;
    private AnalogueNeedle?  _connectorNeedle;
    private InstrumentMeter? _shieldCapacitorMeter;
    private ToggleButton?    _shieldToggleButton;
    private SystemConsole?   _console;
    private DirectionBall?   _systemDirBall;
    private DirectionBall?   _cockpitDirBall;
    private RadarDisplay?    _radarDisplay;
    private EdgePanelHost?   _rightPanel;
    private EdgePanelHost?   _leftPanel;
    private CockpitRail?     _cockpitRail;
    // Disposed as a batch in Dispose — see BusSubscription<T>
    private readonly List<IDisposable> _subscriptions = new();
    private LedIndicator?          _stopLed;
    private LedIndicator?          _warnLed;

    // ── SCAN tab ──────────────────────────────────────────────────────────────
    private SpectrumGraph?   _spectrumGraph;
    private Button?          _spectrumScanButton;
    private InstrumentMeter? _atmPressureMeter;
    private double           _scanCooldown;  // seconds remaining before button re-enables

    // ── Ground radar (atmosphere-only instrument panel) ───────────────────────
    private double _pcAlt, _pcVs, _pcLat, _pcLon, _pcHdg, _pcGs, _pcTemp, _pcPress;

    // ── Targeting ─────────────────────────────────────────────────────────────
    private DirectionBall?      _targetingDirBall;
    private Label?              _targetLineShip;
    private Label?              _targetLineNav;
    private Label?              _targetLineHyp;
    private LandingRadarPanel?  _landingRadar;
    private DockingInstrument? _dockingInstrument;

    // ── Colours (deliberately duplicated from SystemSpaceState — same reasoning
    // as MouseSensitivity in the hyperspace brief: plain constants, cheap to
    // duplicate rather than plumb through as parameters) ───────────────────────
    private static readonly Color ColHUD    = new(180, 200, 220);
    private static readonly Color ColHUDDim = new(80, 90, 110);
    private static readonly Color ColPanel  = new(8, 12, 25, 200);
    private static readonly Color ColBorder = new(40, 60, 90);

    public CockpitUI(
        GraphicsDevice gd,
        SpriteFont font,
        Texture2D pixel,
        TargetingSystem targeting,
        HudAlertDisplay hudAlert,
        Func<DVec3, DVec3> galaxyToEcliptic,
        Action<bool> onShieldToggle,
        Action onShipCycle)
    {
        _gd               = gd;
        _font             = font;
        _pixel            = pixel;
        _targeting        = targeting;
        _hudAlert         = hudAlert;
        _galaxyToEcliptic = galaxyToEcliptic;
        _onShieldToggle   = onShieldToggle;
        _onShipCycle      = onShipCycle;

        // ── DataBus UI setup ──────────────────────────────────────────────────
        var theme = Theme.InferiorDark(_font);
        _ui = new UIManager(_gd, theme);

        // ── Right panel: INSTR tab (meters) + NAV tab (direction ball) ────────
        const int panelW   = 260;
        const int innerW   = panelW - 16; // 8px padding each side
        const int meterH   = 46;
        const int meterGap = 8;

        _reactorPowerOutputMeter = new InstrumentMeter
        { Label = "REACTOR OUT", MinValue = 0, MaxValue = 120,
            Topic = "Reactor.Output",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, 0, innerW, meterH)
        };
        _reactorDrawnMeter = new InstrumentMeter
        { Label = "REACTOR DRAW", MinValue = 0, MaxValue = 120,
            Topic = "Reactor.Drawn",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, meterH + meterGap, innerW, meterH)
        };
        _busConsumptionMeter = new InstrumentMeter
        { Label = "BUS DRAW", MinValue = 0, MaxValue = 120,
            Topic = "MainBus.Consumption",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW
            Format = "F1",
            Bounds = new Rectangle(0, (meterH + meterGap) * 2, innerW, meterH)
        };
        _connectorFlowMeter = new InstrumentMeter
        { Label = "SHIELD CONN", MinValue = 0, MaxValue = 1.0,
            Topic = "ShieldConnector.Flow",
            ScaleFactor = 1e-6,   // sensor publishes watts; meter displays MW (max 0.6 MW)
            Format = "F3",
            Bounds = new Rectangle(0, (meterH + meterGap) * 3, innerW, meterH)
        };
        const int needleH = 130;
        _connectorNeedle = new AnalogueNeedle
        { Label = "SHIELD CONN", MinValue = 0, MaxValue = 1.0,
            Topic = "ShieldConnector.Flow",
            ScaleFactor = 1e-6,   // sensor publishes watts; needle displays MW
            Format = "F3",
            AnimationSpeed = 5.0,
            Bounds = new Rectangle(0, (meterH + meterGap) * 4, innerW, needleH)
        };
        _shieldCapacitorMeter = new InstrumentMeter
        { Label = "SHIELD CAP", MinValue = 0, MaxValue = 100,
            Topic = $"Shield.{Topics.Shield.Capacitor}",
            ScaleFactor = 100.0,  // sensor publishes 0–1 fill; meter displays 0–100 %
            Format = "F0",
            Bounds = new Rectangle(0, (meterH + meterGap) * 4 + needleH + meterGap, innerW, meterH)
        };

        var instrPanel = new Panel { DrawBackground = false, DrawBorder = false };
        instrPanel.Add(_reactorPowerOutputMeter);
        instrPanel.Add(_reactorDrawnMeter);
        instrPanel.Add(_busConsumptionMeter);
        instrPanel.Add(_connectorFlowMeter);
        instrPanel.Add(_connectorNeedle);
        instrPanel.Add(_shieldCapacitorMeter);

        _systemDirBall = new DirectionBall
        {
            Header = "HEADING",
            Bounds = new Rectangle(0, 0, innerW, innerW),
        };

        var navPanel = new Panel { DrawBackground = false, DrawBorder = false };
        navPanel.Add(_systemDirBall);

        _rightPanel = new EdgePanelHost(PanelEdge.Right)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        _rightPanel.AddTab("INSTR", instrPanel);
        _rightPanel.AddTab("NAV",   navPanel);

        // Side panels stop at the CockpitRail wing top to avoid overlap
        int wingH       = 160; // matches CockpitRail.WingHeight
        int sidePanelH  = _gd.Viewport.Height - wingH;

        // ── Left panel: SCAN tab ──────────────────────────────────────────────
        const int scanBtnH    = 28;
        const int scanBtnGap  = 8;
        int       graphW      = innerW;
        int       graphH      = graphW / 5;  // 1:5 height:width ratio

        _spectrumScanButton = new Button("SCAN SPECTRUM",
            new Rectangle(0, 0, innerW, scanBtnH));
        _spectrumScanButton.Clicked += _ =>
        {
            if (_scanCooldown > 0) return;
            CommandBus.Send("SolarSpectrumSensor.Scan");
            _spectrumScanButton.Text    = "SCANNING...";
            _spectrumScanButton.Enabled = false;
            _scanCooldown = SolarSpectrumSensor.ScanDurationSeconds + 0.5;
        };

        _atmPressureMeter = new InstrumentMeter
        {
            Label       = "ATM PRESSURE",
            MinValue    = 0,
            MaxValue    = 120_000,  // Pa — up to ~1.2 atm
            ScaleFactor = 1.0,
            Format      = "F0",
            Topic       = "AtmosphericSensor.Pressure",
            Bounds      = new Rectangle(0, scanBtnH + scanBtnGap, innerW, meterH),
        };

        var atmScanButton = new Button("ATM SCAN",
            new Rectangle(0, scanBtnH + scanBtnGap + meterH + scanBtnGap, innerW, scanBtnH));
        atmScanButton.Clicked += _ => CommandBus.Send("AtmosphericSensor.Scan");

        int spectrumY = scanBtnH + scanBtnGap + meterH + scanBtnGap + scanBtnH + scanBtnGap;
        _spectrumGraph = new SpectrumGraph
        {
            Header = "SOLAR SPECTRUM",
            Topic  = "SolarSpectrumSensor.Data",
            Bounds = new Rectangle(0, spectrumY, graphW, graphH),
        };

        var scanPanel = new Panel { DrawBackground = false, DrawBorder = false };
        scanPanel.Add(_spectrumScanButton);
        scanPanel.Add(_atmPressureMeter);
        scanPanel.Add(atmScanButton);
        scanPanel.Add(_spectrumGraph);

        _leftPanel = new EdgePanelHost(PanelEdge.Left)
        {
            PanelSize     = panelW,
            HandleSize    = 28,
            HandleLength  = 80,
            CornerMargin  = 8,
            Bounds        = new Rectangle(0, 0, _gd.Viewport.Width, sidePanelH),
        };
        _leftPanel.AddTab("SCAN", scanPanel);
        _rightPanel.Bounds = new Rectangle(0, 0, _gd.Viewport.Width, sidePanelH);

        _ui.Add(_rightPanel);
        _ui.Add(_leftPanel);

        // ── CockpitRail: 4 tabs (RADAR, DIR BALL, ???, LOG) ──────────────────
        _console = new SystemConsole
        {
            Header    = "SYSTEM LOG",
            MaxLines  = 6,
            LineBreak = LineBreakMode.Wrap,
            Bounds    = new Rectangle(0, 0, 500, 200),
        };

        _cockpitDirBall = new DirectionBall
        {
            Header = "HEADING",
            Bounds = new Rectangle(0, 0, 300, 300),
        };

        _radarDisplay = new RadarDisplay();

        _shieldToggleButton = new ToggleButton("SHIELD", new Rectangle(4, 4, 120, 28))
        {
            FontScale = 0.72f,
        };
        _shieldToggleButton.SetState(false, false);
        _shieldToggleButton.Toggled += (_, on) => _onShieldToggle(on);

        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            $"Shield.{Topics.Shield.Capacitor}", fill =>
        {
            if (_shieldToggleButton == null) return;
            _shieldToggleButton.IsConfirmed = fill >= 1.0 ? true
                                            : fill <= 0.0 ? false
                                            : null;
        }));

        _landingRadar = new LandingRadarPanel
        {
            Bounds = new Rectangle(0, 0, 500, 220),
        };
        _landingRadar.Released += () =>
        {
            _targeting.ClearNavTarget();
        };

        _cockpitRail = new CockpitRail
        {
            Bounds = new Rectangle(0, 0, _gd.Viewport.Width, _gd.Viewport.Height),
        };
        // Left side (3 tabs): DIR BALL, RADAR, LANDING
        _cockpitRail.AddCenterTab("DIR BALL", _cockpitDirBall);
        _cockpitRail.AddCenterTab("RADAR",    _radarDisplay);
        _cockpitRail.AddCenterTab("LANDING",  _landingRadar);
        _dockingInstrument = new DockingInstrument
        {
            Bounds = new Rectangle(0, 0, 500, 220),
        };

        // Right side (3 tabs): DOCK, LOG, CTRL
        _cockpitRail.AddCenterTab("DOCK",     _dockingInstrument);
        _cockpitRail.AddCenterTab("LOG",      _console);
        var controlPanel = new Panel { DrawBackground = false, DrawBorder = false };
        var shipCycleButton = new Button("NEXT SHIP", new Rectangle(8, 8, 150, 32))
        {
            FontScale = 0.72f,
        };
        shipCycleButton.Clicked += _ => _onShipCycle();
        controlPanel.Add(shipCycleButton);
        _cockpitRail.AddCenterTab("CTRL", controlPanel);
        _drivePanel = new DriveInstrumentPanel();
        _cockpitRail.RightWing.Add(_drivePanel);     // drawn first, under shield button
        _cockpitRail.RightWing.Add(_shieldToggleButton);

        // ── LeftWing: targeting direction ball + 3-line target readout ────────
        // Ball has no header — use all 76px for the sphere so it matches text height.
        _targetingDirBall = new DirectionBall
        {
            Header = "",
            Bounds = new Rectangle(4, 6, 76, 76),
        };
        // Labels start just to the right of the ball; colours match DirectionBall dots.
        var tc = _ui!.Theme;
        _targetLineShip = new Label("Target: None", new Rectangle(88, 10, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetShip,
        };
        _targetLineNav = new Label("Nav: None", new Rectangle(88, 34, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetNav,
        };
        _targetLineHyp = new Label("Hyp: None", new Rectangle(88, 58, 280, 20))
        {
            FontScale = 0.72f,
            TextColor = tc.TargetHyp,
        };
        _cockpitRail.LeftWing.Add(_targetingDirBall);
        _cockpitRail.LeftWing.Add(_targetLineShip);
        _cockpitRail.LeftWing.Add(_targetLineNav);
        _cockpitRail.LeftWing.Add(_targetLineHyp);

        _ui.Add(_cockpitRail);

        // Meters subscribe themselves via Topic — only non-meter handlers need wiring here
        _subscriptions.Add(new BusSubscription<SystemMessage>(DataBus.System, Topics.System.All, msg =>
        {
            _console?.AddMessage(msg);
            _hudAlert.AddMessage(msg);
        }));

        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Altitude,      v => _pcAlt   = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.VerticalSpeed, v => _pcVs    = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Latitude,      v => _pcLat   = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Longitude,     v => _pcLon   = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Heading,       v => _pcHdg   = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.GroundSpeed,   v => _pcGs    = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Temperature,   v => _pcTemp  = v));
        _subscriptions.Add(new BusSubscription<double>(DataBus.Instruments,
            Topics.PlanetCoord.Pressure,      v => _pcPress = v));

        _subscriptions.Add(new BusSubscription<RadarContact>(DataBus.Radar, Topics.Radar.All, c =>
        {
            _targeting.OnContactUpdated(c);
            NotifyRadarContact(c);
        }));
        _subscriptions.Add(new BusSubscription<string>(DataBus.RadarLost, Topics.Radar.All, id =>
        {
            _targeting.OnContactLost(id);
            NotifyRadarContactLost(id);
        }));

        _stopLed = new LedIndicator(
            Topics.Flight.XStopActive,
            DataBus.Instruments,
            _gd,
            _font)
        {
            LabelText      = "STOP",
            LabelAnchor    = LabelAnchor.Bottom,
            LabelFontScale = 0.8f,
            Shape          = LedShape.Round,
            LampSize       = 28,
            MainColor      = new Color(255, 140, 0),
            OnRangeMin     = 0.5,
            OnRangeMax     = double.PositiveInfinity,
        };

        _warnLed = new LedIndicator(
            Topics.Ship.WarnLevel,
            DataBus.Instruments,
            _gd,
            _font)
        {
            LabelText         = "WARN",
            LabelAnchor       = LabelAnchor.Bottom,
            LabelFontScale    = 0.8f,
            Shape             = LedShape.Round,
            LampSize          = 28,
            MainColor         = new Color(200, 50, 50),
            OnRangeMin        = 0.5,
            OnRangeMax        = double.PositiveInfinity,
            ColorRanges       = new List<LedColorRange>
            {
                new(0.5, 1.5, new Color( 40, 110,  55)),
                new(1.5, 2.5, new Color(220, 175,   0)),
                new(2.5, 3.5, new Color(210,  45,  45)),
                new(3.5, double.PositiveInfinity, new Color(210, 45, 45)),
            },
            BlinkRangeMin     = 3.5,
            BlinkRangeMax     = double.PositiveInfinity,
            MinBlinkFrequency = 2.0,
            MaxBlinkFrequency = 2.0,
        };

        if (_cockpitRail != null)
        {
            _cockpitRail.LeftConnectorLed  = _stopLed;
            _cockpitRail.RightConnectorLed = _warnLed;
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void ApplyUiMode(bool active)
    {
        if (_rightPanel != null) _rightPanel.UiModeActive = active;
        if (_leftPanel  != null) _leftPanel.UiModeActive  = active;
        // CockpitRail is always interactable — peek strip tabs and toggle work in all modes.
    }

    public void OnResize(int width, int height)
    {
        int wingH      = _cockpitRail?.WingHeight ?? 160;
        var sideBounds = new Rectangle(0, 0, width, height - wingH);
        if (_rightPanel  != null) _rightPanel.Bounds  = sideBounds;
        if (_leftPanel   != null) _leftPanel.Bounds   = sideBounds;
        if (_cockpitRail != null) _cockpitRail.Bounds = new Rectangle(0, 0, width, height);
    }

    public CockpitLayout CaptureLayout()
    {
        var (rightTab, rightOpen) = _rightPanel?.CaptureState() ?? (-1, false);
        var (leftTab,  leftOpen)  = _leftPanel?.CaptureState()  ?? (-1, false);
        return new CockpitLayout(rightTab, rightOpen, leftTab, leftOpen);
    }

    public void ApplyLayout(CockpitLayout layout)
    {
        _rightPanel?.ApplyState(layout.RightActiveTab, layout.RightOpen);
        _leftPanel?.ApplyState(layout.LeftActiveTab,  layout.LeftOpen);
    }

    public void Dispose()
    {
        // Meters unsubscribe themselves when Topic is cleared
        if (_reactorPowerOutputMeter != null) _reactorPowerOutputMeter.Topic = "";
        if (_reactorDrawnMeter       != null) _reactorDrawnMeter.Topic       = "";
        if (_busConsumptionMeter     != null) _busConsumptionMeter.Topic     = "";
        if (_connectorFlowMeter      != null) _connectorFlowMeter.Topic      = "";
        if (_connectorNeedle         != null) _connectorNeedle.Topic         = "";
        if (_shieldCapacitorMeter    != null) _shieldCapacitorMeter.Topic    = "";
        if (_atmPressureMeter        != null) _atmPressureMeter.Topic        = "";
        if (_spectrumGraph           != null) _spectrumGraph.Topic           = "";

        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        _stopLed?.Dispose();
        _stopLed = null;
        _warnLed?.Dispose();
        _warnLed = null;

        _ui?.Dispose();
        _ui = null;
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    public void Tick(double dt)
    {
        _ui?.Animate(dt);
        _stopLed?.Update(dt);
        _warnLed?.Update(dt);

        // Re-enable scan button once cooldown expires
        if (_scanCooldown > 0)
        {
            _scanCooldown -= dt;
            if (_scanCooldown <= 0 && _spectrumScanButton != null)
            {
                _spectrumScanButton.Enabled = true;
                _spectrumScanButton.Text    = "SCAN SPECTRUM";
            }
        }

        // Keep drive panel filling the right wing (wing bounds are set by CockpitRail.Update)
        if (_drivePanel != null && _cockpitRail != null)
        {
            var rwb = _cockpitRail.RightWing.Bounds;
            _drivePanel.Bounds = new Rectangle(0, 0, rwb.Width, rwb.Height);
        }
    }

    public void HandleUiInput(double dt, InputState input)
        => _ui?.Update(dt, input);

    public void DrawUiTree()
        => _ui?.Draw();
}
