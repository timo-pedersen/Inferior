using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Galaxy;
using Inferior.UI;
using Inferior.UI.Controls;

namespace Inferior.Game.States;

/// <summary>
/// Star system view.
/// Star at centre, planets and moons orbiting in real time.
/// Zoom from full system to individual planet.
///
/// Left-click        — select body
/// Double-click body — enter system flight near that body
/// Double-click star — enter system flight near star
/// Escape / back     — return to galaxy map
/// [ / ]             — time compression
/// Home              — recentre on star
/// </summary>
public sealed class SystemMapState : GameState
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly GraphicsDevice _gd;
    private readonly SpriteFont     _font;

    // ── Current system ────────────────────────────────────────────────────────
    private Star       _star   = null!;
    private StarSystem _system = null!;

    // ── Camera (system space = metres) ────────────────────────────────────────
    private Vector2 _cameraPos     = Vector2.Zero;
    private double  _metersPerPixel = 1e10;

    private const double MinMetersPerPixel = 1e6;
    private const double MaxMetersPerPixel = 1e13;
    private const double ZoomFactor        = 1.15;

    // ── Time ──────────────────────────────────────────────────────────────────
    private double _gameTimeSeconds;

    private static readonly double[] TimeCompressions =
        [1, 100, 10_000, 1_000_000];
    private static readonly string[] TimeCompressionLabels =
        ["1x", "100x", "10,000x", "1,000,000x"];
    private int _timeCompIndex = 0;

    private double TimeCompression => TimeCompressions[_timeCompIndex];

    // ── Cached body positions (rebuilt each frame) ────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── Selection / hover ─────────────────────────────────────────────────────
    private OrbitalBody?     _selectedBody;
    private OrbitalBody?     _hoveredBody;
    private StateTransition? _pendingTransition;

    // ── Nav target (right-click selection, passed back to flight) ─────────────
    private OrbitalBody? _navBody;
    private Station?     _navStation;

    // Preserved across round-trips to SystemSpace
    private CockpitLayout _cockpitLayout    = CockpitLayout.Default;
    private DVec3?        _spawnPos;
    private Quaternion?   _spawnOrientation;

    // ── Input ─────────────────────────────────────────────────────────────────
    private MouseState    _prevMouse;
    private KeyboardState _prevKeys;

    private bool    _isDragging;
    private Vector2 _dragStartScreen;
    private Vector2 _cameraAtDragStart;
    private const float DragThreshold = 5f;

    // Double-click detection
    private double       _lastClickTime    = -1.0;
    private OrbitalBody? _lastClickBody;           // null means star or station was clicked
    private Station?     _lastClickStation;
    private bool         _lastClickWasStar;
    private const double DoubleClickSeconds = 0.35;

    // ── UI ────────────────────────────────────────────────────────────────────
    private UIManager? _ui;
    private Button?    _backButton;
    private Button?    _timeButton;
    private Vector2    _screenCentre;
    private Texture2D  _pixel  = null!;
    private Texture2D  _circle = null!;

    // ── Visual constants ──────────────────────────────────────────────────────
    private const float StarVisualRadius    = 28f; // Clamp min size for visibility, then scale up with zoom
    private const float MinOrbitRingPixels  = 6f;
    private const float NameAlphaDimmed     = 0.35f;
    private const float NameAlphaHovered    = 1.0f;
    private const float AsteroidDotSize     = 1.5f;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColBackground  = new(6, 6, 16);
    private static readonly Color ColOrbitRing   = new(30, 40, 60);
    private static readonly Color ColMoonOrbit   = new(20, 28, 44);
    private static readonly Color ColSelected    = new(255, 220, 80);
    private static readonly Color ColHovered     = new(180, 200, 255);
    private static readonly Color ColAsteroid    = new(80, 75, 70);
    private static readonly Color ColPanel       = new(10, 15, 30, 210);
    private static readonly Color ColPanelBorder = new(40, 60, 90);
    private static readonly Color ColButton      = new(20, 30, 50, 220);
    private static readonly Color ColButtonHover = new(40, 60, 90, 220);
    private static readonly Color ColText        = new(200, 210, 225);
    private static readonly Color ColTextDim     = new(100, 110, 130);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SystemMapState(GraphicsDevice gd, SpriteFont font)
        : base(GameStateId.SystemMap)
    {
        _gd   = gd;
        _font = font;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(object? payload)
    {
        if (payload is Star star)
        {
            _star            = star;
            _system          = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            _gameTimeSeconds = 0;
            // _cockpitLayout intentionally not reset — persists across galaxy map visits
        }
        else if (payload is SystemMapPayload mp)
        {
            // Returning from SystemSpace — restore star, game time, cockpit layout, and ship position
            _star             = mp.Star;
            _system           = StarSystem.Generate(mp.Star, GalaxyGenerator.SystemSeed(mp.Star));
            _gameTimeSeconds  = mp.GameTime;
            _cockpitLayout    = mp.Layout;
            _spawnPos         = mp.SpawnPos;
            _spawnOrientation = mp.SpawnOrientation;
            _navBody          = mp.NavBody;
            _navStation       = mp.NavStation;
        }
        else if (payload is (Star returnStar, double returnTime))
        {
            // Legacy tuple form — kept for safety
            _star            = returnStar;
            _system          = StarSystem.Generate(returnStar, GalaxyGenerator.SystemSeed(returnStar));
            _gameTimeSeconds = returnTime;
            _cockpitLayout   = CockpitLayout.Default;
        }

        _selectedBody = null;
        _hoveredBody  = null;
        _cameraPos    = Vector2.Zero;

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);
        _circle = CreateCircleTexture(64);

        UpdateScreenCentre();
        FitSystemToView();

        var theme = Theme.InferiorDark(_font);
        _ui?.Dispose();
        _ui = new UIManager(_gd, theme);

        _backButton = new Button("< BACK TO FLIGHT", new Rectangle(16, 16, 180, 36));
        _timeButton = new Button($"TIME: {TimeCompressionLabels[_timeCompIndex]}",
            new Rectangle(16, 60, 220, 36));

        _backButton.Clicked += _ =>
            _pendingTransition = StateTransition.To(GameStateId.SystemSpace,
                new SystemSpacePayload(_star, null, _gameTimeSeconds, _cockpitLayout, _spawnPos, _spawnOrientation,
                    _navBody, _navStation));

        _timeButton.Clicked += _ =>
        {
            _timeCompIndex = (_timeCompIndex + 1) % TimeCompressions.Length;
            _timeButton.Text = $"TIME: {TimeCompressionLabels[_timeCompIndex]}";
        };

        _ui.Add(_backButton);
        _ui.Add(_timeButton);

        _pendingTransition = null;
    }

    public override void OnExit()
    {
        _ui?.Dispose();
        _ui = null;
        _pixel?.Dispose();
        _circle?.Dispose();
    }

    public override void OnResize(int width, int height)
    {
        UpdateScreenCentre();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse   = Mouse.GetState();
        var keys    = Keyboard.GetState();
        double real = gameTime.ElapsedGameTime.TotalSeconds;
        double now  = gameTime.TotalGameTime.TotalSeconds;

        _gameTimeSeconds += real * TimeCompression;

        RebuildBodyPositions();

        // UI always gets input here (cursor always visible in system map)
        var input = new InputState(mouse, _prevMouse, keys, _prevKeys);
        _ui?.Animate(real);
        _ui?.Update(real, input);

        HandleZoom(mouse);
        HandleLeftButton(mouse, now);
        HandleRightButton(mouse);
        HandleHover(mouse);
        HandleKeyboard(keys, mouse);

        _prevMouse = mouse;
        _prevKeys  = keys;

        var transition     = _pendingTransition;
        _pendingTransition = null;
        return transition;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(GameTime gameTime, GraphicsDevice gd, SpriteBatch sb)
    {
        gd.Clear(ColBackground);

        sb.Begin(blendState: BlendState.AlphaBlend);

        DrawOrbitRings(sb);
        DrawAsteroidBelt(sb);
        DrawStar(sb);
        DrawBodies(sb);
        DrawStations(sb);
        DrawBodyNames(sb);
        DrawStationNames(sb);
        DrawInfoPanel(sb);
        DrawHints(sb);

        sb.End();

        _ui?.Draw();
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    private void HandleZoom(MouseState mouse)
    {
        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll == 0) return;

        Vector2 mouseScreen = new(mouse.X, mouse.Y);
        Vector2 mouseWorld  = ScreenToSystem(mouseScreen);

        _metersPerPixel *= scroll > 0 ? 1.0 / ZoomFactor : ZoomFactor;
        _metersPerPixel  = System.Math.Clamp(_metersPerPixel, MinMetersPerPixel, MaxMetersPerPixel);

        Vector2 mouseWorldAfter = ScreenToSystem(mouseScreen);
        _cameraPos -= mouseWorldAfter - mouseWorld;
    }

    private void HandleLeftButton(MouseState mouse, double now)
    {
        bool justPressed  = mouse.LeftButton == ButtonState.Pressed
                         && _prevMouse.LeftButton == ButtonState.Released;
        bool held         = mouse.LeftButton == ButtonState.Pressed;
        bool justReleased = mouse.LeftButton == ButtonState.Released
                         && _prevMouse.LeftButton == ButtonState.Pressed;

        var mousePos = new Vector2(mouse.X, mouse.Y);

        if (justPressed)
        {
            _dragStartScreen   = mousePos;
            _cameraAtDragStart = _cameraPos;
            _isDragging        = false;
        }

        if (held)
        {
            Vector2 delta = mousePos - _dragStartScreen;
            if (!_isDragging && delta.Length() > DragThreshold)
                _isDragging = true;

            if (_isDragging)
                _cameraPos = _cameraAtDragStart - delta * (float)_metersPerPixel;
        }

        if (justReleased && !_isDragging)
        {
            // Don't body-select if a UI button consumed this click
            if (_ui?.FindAt(new Point(mouse.X, mouse.Y)) != null) return;

            // Hit test: stations first (smaller targets), then body, then star
            Station?     hitStation = HitTestStation(mousePos);
            OrbitalBody? hitBody    = hitStation == null ? HitTestBody(mousePos) : null;
            bool         hitStar    = hitBody == null && hitStation == null && HitTestStarDot(mousePos);

            bool isDouble = (now - _lastClickTime) < DoubleClickSeconds
                         && hitBody    == _lastClickBody
                         && hitStation == _lastClickStation
                         && hitStar    == _lastClickWasStar;

            if (isDouble && hitStation != null)
            {
                // Launch into system flight near this station
                _pendingTransition = StateTransition.To(
                    GameStateId.SystemSpace,
                    new SystemSpacePayload(_star, null, _gameTimeSeconds, _cockpitLayout,
                        NavBody: _navBody, NavStation: _navStation, TargetStation: hitStation));
                return;
            }

            if (isDouble && (hitBody != null || hitStar))
            {
                // Launch into system flight near this body (or star)
                _pendingTransition = StateTransition.To(
                    GameStateId.SystemSpace,
                    new SystemSpacePayload(_star, hitBody, _gameTimeSeconds, _cockpitLayout,
                        NavBody: _navBody, NavStation: _navStation));
                return;
            }

            _selectedBody      = hitBody;
            _lastClickTime     = now;
            _lastClickBody     = hitBody;
            _lastClickStation  = hitStation;
            _lastClickWasStar  = hitStar;
        }

        if (justReleased)
            _isDragging = false;
    }

    private void HandleRightButton(MouseState mouse)
    {
        bool justReleased = mouse.RightButton == ButtonState.Released
                         && _prevMouse.RightButton == ButtonState.Pressed;
        if (!justReleased) return;

        var mousePos = new Vector2(mouse.X, mouse.Y);

        // Check stations first (they're smaller hit targets)
        foreach (var station in _system.Stations)
        {
            DVec3   stationPos = GetStationSystemPos(station);
            Vector2 screen     = SystemToScreen(new Vector2((float)stationPos.X, (float)stationPos.Z));
            float   dx = mousePos.X - screen.X;
            float   dy = mousePos.Y - screen.Y;
            if (MathF.Sqrt(dx*dx + dy*dy) <= 10f)
            {
                _navBody    = null;
                _navStation = station;
                return;
            }
        }

        // Then check bodies (planets and moons)
        OrbitalBody? hitBody = HitTestBody(mousePos);
        if (hitBody != null)
        {
            _navBody    = hitBody;
            _navStation = null;
            return;
        }

        // Right-click on empty space (or star) clears nav target
        _navBody    = null;
        _navStation = null;
    }

    private void HandleHover(MouseState mouse)
        => _hoveredBody = HitTestBody(new Vector2(mouse.X, mouse.Y));

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        bool mPressed   = keys.IsKeyDown(Keys.M)      && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed   = keys.IsKeyDown(Keys.N)      && !_prevKeys.IsKeyDown(Keys.N);

        if (escPressed || mPressed)
            // Esc or M = back to flight (M toggles the system map)
            _pendingTransition = StateTransition.To(GameStateId.SystemSpace,
                new SystemSpacePayload(_star, null, _gameTimeSeconds, _cockpitLayout, _spawnPos, _spawnOrientation,
                    _navBody, _navStation));
        else if (nPressed)
            // N = galaxy map (pass ship position through so galaxy map can hand it back to flight)
            _pendingTransition = StateTransition.To(GameStateId.GalaxyMap,
                new GalaxyMapPayload(_star, _gameTimeSeconds, _spawnPos, _spawnOrientation));

        if (keys.IsKeyDown(Keys.OemCloseBrackets) && !_prevKeys.IsKeyDown(Keys.OemCloseBrackets))
            _timeCompIndex = System.Math.Min(_timeCompIndex + 1, TimeCompressions.Length - 1);

        if (keys.IsKeyDown(Keys.OemOpenBrackets) && !_prevKeys.IsKeyDown(Keys.OemOpenBrackets))
            _timeCompIndex = System.Math.Max(_timeCompIndex - 1, 0);

        if (keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home))
        {
            _cameraPos = Vector2.Zero;
            FitSystemToView();
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void DrawOrbitRings(SpriteBatch sb)
    {
        foreach (var planet in _system.Planets)
        {
            float radiusPx = (float)(planet.OrbitalRadius / _metersPerPixel);
            if (radiusPx < MinOrbitRingPixels) continue;

            Vector2 centre = SystemToScreen(Vector2.Zero);
            DrawCircle(sb, centre, radiusPx, ColOrbitRing, CircleSegments(radiusPx));
        }

        foreach (var planet in _system.Planets)
        {
            if (planet.Children.Count == 0) continue;

            DVec3   planetPos    = planet.GetPosition(_gameTimeSeconds, DVec3.Zero);
            Vector2 parentScreen = SystemToScreen(new Vector2((float)planetPos.X, (float)planetPos.Z));

            foreach (var moon in planet.Children)
            {
                float radiusPx = (float)(moon.OrbitalRadius / _metersPerPixel);
                if (radiusPx >= MinOrbitRingPixels)
                    DrawCircle(sb, parentScreen, radiusPx, ColMoonOrbit, CircleSegments(radiusPx));
            }
        }

        // Station orbit rings — drawn around parent body or star
        var colStationOrbit = new Color(30, 50, 40, 100);
        foreach (var station in _system.Stations)
        {
            float radiusPx = (float)(station.OrbitalRadius / _metersPerPixel);
            if (radiusPx < MinOrbitRingPixels) continue;

            Vector2 parentScreen;
            if (station.OrbitParent == null)
            {
                parentScreen = SystemToScreen(Vector2.Zero);
            }
            else
            {
                DVec3 parentPos = station.OrbitParent.GetPosition(_gameTimeSeconds, DVec3.Zero);
                // Moons need their parent planet's position
                DVec3 parentOfParent = DVec3.Zero;
                foreach (var p in _system.Planets)
                    if (p.Children.Contains(station.OrbitParent))
                        parentOfParent = p.GetPosition(_gameTimeSeconds, DVec3.Zero);
                DVec3 resolvedPos = station.OrbitParent.GetPosition(_gameTimeSeconds, parentOfParent);
                parentScreen = SystemToScreen(new Vector2((float)resolvedPos.X, (float)resolvedPos.Z));
            }

            DrawCircle(sb, parentScreen, radiusPx, colStationOrbit, CircleSegments(radiusPx));
        }
    }

    private void DrawAsteroidBelt(SpriteBatch sb)
    {
        foreach (var asteroid in _system.AsteroidBelt)
        {
            DVec3 pos = asteroid.GetPosition(_gameTimeSeconds, DVec3.Zero);
            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));

            if (!IsOnScreen(screen, 4f)) continue;
            DrawDot(sb, screen, AsteroidDotSize, ColAsteroid);
        }
    }

    private void DrawStar(SpriteBatch sb)
    {
        Vector2 screen = SystemToScreen(Vector2.Zero);

        const double refMPP = 5e8;
        float scale = MathF.Log((float)(refMPP / _metersPerPixel) + 1f, 2f);
        float starR = System.Math.Clamp(StarVisualRadius * scale, 12f, 60f);

        DrawDot(sb, screen, starR * 1.8f, _star.GlowColor * 0.3f);
        DrawDot(sb, screen, starR * 1.3f, _star.GlowColor * 0.5f);
        DrawDot(sb, screen, starR,        _star.GlowColor);
        DrawDot(sb, screen, starR * 0.4f, Color.White);

        DrawText(sb, _star.Name,
            screen + new Vector2(starR + 4f, -8f),
            _star.GlowColor * 0.8f, 0.75f);
    }

    private void DrawBodies(SpriteBatch sb)
    {
        foreach (var (body, pos) in _bodyPositions)
        {
            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            if (!IsOnScreen(screen, 30f)) continue;

            float radius   = VisualRadius(body);
            Color color    = BodyColor(body);
            bool  selected = _selectedBody == body;
            bool  hovered  = _hoveredBody  == body;

            if (body.AtmosphereType != AtmosphereType.None)
                DrawDot(sb, screen, radius * 1.5f, body.AtmosphereColor * 0.3f);

            DrawDot(sb, screen, radius, color);

            if (selected)
                DrawCircle(sb, screen, radius + 5f, ColSelected, 24);
            else if (hovered)
                DrawCircle(sb, screen, radius + 4f, ColHovered, 24);

            if (_navBody == body)
                DrawCircle(sb, screen, radius + 8f, ColNavTarget, 24);
        }
    }

    private void DrawBodyNames(SpriteBatch sb)
    {
        foreach (var (body, pos) in _bodyPositions)
        {
            if (body.BodyType == BodyType.Asteroid) continue;

            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            if (!IsOnScreen(screen, 60f)) continue;

            float  radius  = VisualRadius(body);
            bool   hovered = _hoveredBody == body || _selectedBody == body;
            float  alpha   = hovered ? NameAlphaHovered : NameAlphaDimmed;
            float  scale   = body.BodyType == BodyType.Moon ? 0.65f : 0.8f;

            FontHelper.Draw(sb, _font, body.Name, screen + new Vector2(radius + 4f, -8f),
                ColText * alpha, scale);
        }
    }

    private void DrawInfoPanel(SpriteBatch sb)
    {
        var display = _selectedBody ?? _hoveredBody;
        if (display == null) return;

        int panelW = 290;
        int margin = 16;
        int x      = _gd.Viewport.Width - panelW - margin;
        int y      = margin;
        int lineH  = 22;
        int atmoExtra = display.AtmosphereType != AtmosphereType.None ? 1 : 0;
        int panelH    = (10 + atmoExtra + display.Children.Count) * lineH + 24;

        DrawRect(sb, new Rectangle(x, y, panelW, panelH), ColPanel);
        DrawRectBorder(sb, new Rectangle(x, y, panelW, panelH), ColPanelBorder, 1);

        int tx = x + 12;
        int ty = y + 12;

        DrawText(sb, display.Name, new Vector2(tx, ty), Color.White, 1.05f);
        ty += (int)(lineH * 1.3f);

        string typeLabel = IsMoon(display) ? "Moon" : $"{display.BodyType}";
        DrawText(sb, typeLabel, new Vector2(tx, ty), BodyColor(display));
        ty += lineH;

        if (display.AtmosphereType != AtmosphereType.None)
        {
            DrawText(sb, $"Atmosphere: {display.AtmosphereType}", new Vector2(tx, ty), ColText);
            ty += lineH;
            double surfaceAtm = Gameplay.SensorData.Environment.AtmosphericSurfacePressure(display) / 101_325.0;
            DrawText(sb, $"  Pressure: {surfaceAtm:F2} atm", new Vector2(tx, ty), ColTextDim);
            ty += lineH;
        }

        double radiusKm = Units.MetersToKM(display.RadiusMeters);
        double gravG    = display.SurfaceGravity / 9.81;
        DrawText(sb, $"Radius: {radiusKm:F0} km",  new Vector2(tx, ty), ColTextDim); ty += lineH;
        DrawText(sb, $"Gravity: {gravG:F2} g",      new Vector2(tx, ty), ColTextDim); ty += lineH;

        double periodDays = display.Period / Units.DayInSeconds;
        double orbitAU    = Units.MetersToAU(display.OrbitalRadius);
        DrawText(sb, $"Orbit: {orbitAU:F3} AU", new Vector2(tx, ty), ColTextDim); ty += lineH;

        if (periodDays < 1.0)
            DrawText(sb, $"Period: {display.Period / Units.HourInSeconds:F1} h",
                new Vector2(tx, ty), ColTextDim);
        else if (periodDays < 365)
            DrawText(sb, $"Period: {periodDays:F1} days", new Vector2(tx, ty), ColTextDim);
        else
            DrawText(sb, $"Period: {periodDays / 365.25:F2} years", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        double hillKm = Units.MetersToKM(display.HillSphereRadius);
        DrawText(sb, $"Hill sphere: {hillKm:F0} km", new Vector2(tx, ty), ColTextDim); ty += lineH;

        if (display.Children.Count > 0)
        {
            ty += 4;
            DrawText(sb, $"Moons ({display.Children.Count}):", new Vector2(tx, ty), ColText);
            ty += lineH;
            foreach (var moon in display.Children)
            {
                DrawText(sb, $"  {moon.Name}", new Vector2(tx, ty), ColTextDim, 0.8f);
                ty += (int)(lineH * 0.9f);
            }
        }

        DrawText(sb, "Double-click to approach", new Vector2(tx, ty + 4), ColHovered * 0.7f, 0.75f);
    }

    private static readonly Color ColStation     = new(80, 200, 140);
    private static readonly Color ColStationName = new(80, 180, 120);
    private static readonly Color ColNavTarget   = new(255, 200, 50);

    private void DrawStations(SpriteBatch sb)
    {
        foreach (var station in _system.Stations)
        {
            DVec3   stationPos = GetStationSystemPos(station);
            Vector2 screen     = SystemToScreen(new Vector2((float)stationPos.X, (float)stationPos.Z));
            if (!IsOnScreen(screen, 20f)) continue;

            // Diamond icon: draw two overlapping 45°-rotated rectangles
            float r = station.Size switch
            {
                Galaxy.StationSize.Small  => 4f,
                Galaxy.StationSize.Medium => 5f,
                Galaxy.StationSize.Large  => 7f,
                _                         => 4f,
            };

            bool isNavStation = _navStation == station;
            Color stColor = isNavStation ? ColNavTarget : ColStation;
            DrawDot(sb, screen, r, stColor);
            // Cross-hair lines to distinguish from bodies
            sb.Draw(_pixel, new Rectangle((int)(screen.X - r * 1.8f), (int)screen.Y, (int)(r * 3.6f), 1), stColor * 0.6f);
            sb.Draw(_pixel, new Rectangle((int)screen.X, (int)(screen.Y - r * 1.8f), 1, (int)(r * 3.6f)), stColor * 0.6f);
            if (isNavStation)
                DrawCircle(sb, screen, r + 8f, ColNavTarget, 24);
        }
    }

    private void DrawStationNames(SpriteBatch sb)
    {
        foreach (var station in _system.Stations)
        {
            DVec3   stationPos = GetStationSystemPos(station);
            Vector2 screen     = SystemToScreen(new Vector2((float)stationPos.X, (float)stationPos.Z));
            if (!IsOnScreen(screen, 60f)) continue;

            float r = 6f;
            FontHelper.Draw(sb, _font, station.Name,
                screen + new Vector2(r + 4f, -8f),
                ColStationName * NameAlphaDimmed, 0.7f);
        }
    }

    private DVec3 GetStationSystemPos(Galaxy.Station station)
    {
        DVec3 parentPos = DVec3.Zero;
        if (station.OrbitParent != null)
        {
            DVec3 grandparent = DVec3.Zero;
            foreach (var p in _system.Planets)
                if (p.Children.Contains(station.OrbitParent))
                    grandparent = p.GetPosition(_gameTimeSeconds, DVec3.Zero);
            parentPos = station.OrbitParent.GetPosition(_gameTimeSeconds, grandparent);
        }
        return station.GetPosition(_gameTimeSeconds, parentPos);
    }

    private void DrawHints(SpriteBatch sb)
    {
        int x = 16;
        int y = _gd.Viewport.Height - 68;
        DrawText(sb, "Double-click body/station/star - approach   Right-click - set nav target   Scroll - zoom   Home - recentre",
            new Vector2(x, y), ColTextDim, 0.72f);
        y += 18;
        DrawText(sb, "N - galaxy map   Esc - back to flight",
            new Vector2(x, y), ColTextDim, 0.72f);
    }

    // ── Coordinate transforms ─────────────────────────────────────────────────

    private Vector2 SystemToScreen(Vector2 systemPos)
        => _screenCentre + (systemPos - _cameraPos) / (float)_metersPerPixel;

    private Vector2 ScreenToSystem(Vector2 screenPos)
        => _cameraPos + (screenPos - _screenCentre) * (float)_metersPerPixel;

    // ── Hit testing ───────────────────────────────────────────────────────────

    private OrbitalBody? HitTestBody(Vector2 screenPos)
    {
        OrbitalBody? best     = null;
        float        bestDist = float.MaxValue;

        foreach (var (body, pos) in _bodyPositions)
        {
            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            float   hitR   = System.Math.Max(VisualRadius(body) + 4f, 8f);
            float   dx     = screenPos.X - screen.X;
            float   dy     = screenPos.Y - screen.Y;
            float   dist   = MathF.Sqrt(dx*dx + dy*dy);

            if (dist < hitR && dist < bestDist)
            {
                bestDist = dist;
                best     = body;
            }
        }

        return best;
    }

    private bool HitTestStarDot(Vector2 screenPos)
    {
        Vector2 starScreen = SystemToScreen(Vector2.Zero);
        float   dx         = screenPos.X - starScreen.X;
        float   dy         = screenPos.Y - starScreen.Y;
        float   dist       = MathF.Sqrt(dx*dx + dy*dy);
        return dist <= StarVisualRadius + 8f;
    }

    private Station? HitTestStation(Vector2 screenPos)
    {
        foreach (var station in _system.Stations)
        {
            DVec3   stationPos = GetStationSystemPos(station);
            Vector2 screen     = SystemToScreen(new Vector2((float)stationPos.X, (float)stationPos.Z));
            float   dx         = screenPos.X - screen.X;
            float   dy         = screenPos.Y - screen.Y;
            if (MathF.Sqrt(dx*dx + dy*dy) <= 10f)
                return station;
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RebuildBodyPositions()
    {
        _bodyPositions.Clear();
        foreach (var planet in _system.Planets)
            planet.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);
        foreach (var asteroid in _system.AsteroidBelt)
            asteroid.CollectPositions(_gameTimeSeconds, DVec3.Zero, _bodyPositions);
    }

    private void FitSystemToView()
    {
        double outermost = Units.AU * 2.0;
        foreach (var planet in _system.Planets)
            outermost = System.Math.Max(outermost, planet.OrbitalRadius);

        float screenMin = System.Math.Min(_gd.Viewport.Width, _gd.Viewport.Height);
        _metersPerPixel = outermost / (screenMin * 0.40);
        _metersPerPixel = System.Math.Clamp(_metersPerPixel, MinMetersPerPixel, MaxMetersPerPixel);
    }

    private void UpdateScreenCentre()
        => _screenCentre = new Vector2(_gd.Viewport.Width * 0.5f, _gd.Viewport.Height * 0.5f);



    private bool IsOnScreen(Vector2 screenPos, float margin)
        => screenPos.X >= -margin && screenPos.X <= _gd.Viewport.Width  + margin
        && screenPos.Y >= -margin && screenPos.Y <= _gd.Viewport.Height + margin;

    private bool IsMoon(OrbitalBody body) =>
        _system.Planets.Any(p => p.Children.Contains(body));

    private float VisualRadius(OrbitalBody body)
    {
        float baseSize = body.BodyType switch
        {
            BodyType.GasGiant    => 18f,
            BodyType.IceGiant    => 14f,
            BodyType.EarthLike   => 10f,
            BodyType.OceanPlanet => 10f,
            BodyType.Desert      => 8f,
            BodyType.Volcanic    => 8f,
            BodyType.RockyPlanet => 7f,
            BodyType.IcePlanet   => 7f,
            BodyType.Moon        => 4f,
            BodyType.Asteroid    => 2f,
            _                    => 6f,
        };

        const double referenceMetersPerPixel = 5e8;
        float scale = MathF.Log((float)(referenceMetersPerPixel / _metersPerPixel) + 1f, 2f);

        float minRadius = body.BodyType == BodyType.Moon ? 2f : 4f;
        float maxRadius = body.BodyType == BodyType.GasGiant ? 40f : 25f;

        return System.Math.Clamp(baseSize * scale, minRadius, maxRadius);
    }

    private static Color BodyColor(OrbitalBody body) => body.BodyType switch
    {
        BodyType.EarthLike   => new Color(80,  140, 200),
        BodyType.OceanPlanet => new Color(40,  100, 200),
        BodyType.Desert      => new Color(200, 160,  80),
        BodyType.Volcanic    => new Color(200,  60,  20),
        BodyType.RockyPlanet => new Color(140, 130, 120),
        BodyType.IcePlanet   => new Color(200, 220, 240),
        BodyType.IceGiant    => new Color(100, 180, 220),
        BodyType.GasGiant    => new Color(200, 160, 100),
        BodyType.Moon        => new Color(160, 155, 150),
        BodyType.Asteroid    => new Color(100,  95,  90),
        _                    => new Color(150, 150, 150),
    };

    private static int CircleSegments(float radiusPx)
        => System.Math.Clamp((int)(radiusPx * 0.5f), 16, 128);

    // ── Primitive drawing ─────────────────────────────────────────────────────

    private void DrawDot(SpriteBatch sb, Vector2 centre, float radius, Color color)
    {
        if (radius < 1f) radius = 1f;
        float size = radius * 2f;
        sb.Draw(_circle,
            new Rectangle((int)(centre.X - radius), (int)(centre.Y - radius),
                          (int)size, (int)size),
            color);
    }

    private Texture2D CreateCircleTexture(int diameter)
    {
        var   tex   = new Texture2D(_gd, diameter, diameter);
        var   data  = new Color[diameter * diameter];
        float r     = diameter * 0.5f;
        float cx    = r, cy = r;
        float inner = r * 0.6f;

        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dist  = MathF.Sqrt((x - cx)*(x - cx) + (y - cy)*(y - cy));
            float alpha;
            if (dist <= inner)       alpha = 1f;
            else if (dist <= r)      alpha = 1f - (dist - inner) / (r - inner);
            else                     alpha = 0f;
            data[y * diameter + x] = Color.White * alpha;
        }

        tex.SetData(data);
        return tex;
    }

    private void DrawLine(SpriteBatch sb, Vector2 from, Vector2 to, Color color, float thickness = 1f)
    {
        Vector2 delta  = to - from;
        float   length = delta.Length();
        if (length < 0.1f) return;

        float angle = MathF.Atan2(delta.Y, delta.X);
        sb.Draw(_pixel,
            new Rectangle((int)from.X, (int)from.Y, (int)length, (int)thickness),
            null, color, angle, Vector2.Zero, SpriteEffects.None, 0f);
    }

    private void DrawCircle(SpriteBatch sb, Vector2 centre, float radius, Color color, int segments)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float   a0 = i       * step;
            float   a1 = (i + 1) * step;
            Vector2 p0 = centre + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
            Vector2 p1 = centre + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            DrawLine(sb, p0, p1, color);
        }
    }

    private void DrawRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(_pixel, rect, color);

    private void DrawRectBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness)
    {
        DrawLine(sb, new Vector2(rect.Left,  rect.Top),    new Vector2(rect.Right, rect.Top),    color, thickness);
        DrawLine(sb, new Vector2(rect.Right, rect.Top),    new Vector2(rect.Right, rect.Bottom), color, thickness);
        DrawLine(sb, new Vector2(rect.Right, rect.Bottom), new Vector2(rect.Left,  rect.Bottom), color, thickness);
        DrawLine(sb, new Vector2(rect.Left,  rect.Bottom), new Vector2(rect.Left,  rect.Top),    color, thickness);
    }

    private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1.0f)
        => FontHelper.Draw(sb, _font, text, pos, color, scale);
}
