using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// Star system view.
/// Star at centre, planets and moons orbiting in real time.
/// Zoom from full system to individual planet.
/// Escape or back button returns to galaxy map.
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

    private const double MinMetersPerPixel = 1e6;   // very close — planet surface scale
    private const double MaxMetersPerPixel = 1e13;  // full system view
    private const double ZoomFactor        = 1.15;

    // ── Time ──────────────────────────────────────────────────────────────────
    // Continuous game clock — advances even when not in system view
    // so planets are in the correct position relative to elapsed play time.
    private double _gameTimeSeconds;

    private static readonly double[] TimeCompressions =
        [1, 100, 10_000, 1_000_000];
    private static readonly string[] TimeCompressionLabels =
        ["1x", "100x", "10,000x", "1,000,000x"];
    private int _timeCompIndex = 0; // start at real time

    private double TimeCompression => TimeCompressions[_timeCompIndex];

    // ── Cached body positions (rebuilt each frame) ────────────────────────────
    private readonly List<(OrbitalBody body, DVec3 pos)> _bodyPositions = [];

    // ── Selection / hover ─────────────────────────────────────────────────────
    private OrbitalBody?     _selectedBody;
    private OrbitalBody?     _hoveredBody;
    private StateTransition? _pendingTransition;

    // ── Input ─────────────────────────────────────────────────────────────────
    private MouseState    _prevMouse;
    private KeyboardState _prevKeys;

    // Left-button drag to pan
    private bool    _isDragging;
    private Vector2 _dragStartScreen;
    private Vector2 _cameraAtDragStart;
    private const float DragThreshold = 5f;

    // ── UI ────────────────────────────────────────────────────────────────────
    private Rectangle _backButtonRect;
    private Rectangle _timeButtonRect;
    private Vector2   _screenCentre;
    private Texture2D _pixel  = null!;
    private Texture2D _circle = null!;

    // ── Visual constants ──────────────────────────────────────────────────────
    private const float StarVisualRadius    = 28f;
    private const float MinOrbitRingPixels  = 6f;   // don't draw orbit rings smaller than this
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
        : base(GameStateId.SystemSpace)
    {
        _gd   = gd;
        _font = font;
    }

    // ── Game time accessor (called by InferiorGame to advance the clock) ──────

    public void AdvanceTime(double realDeltaSeconds)
        => _gameTimeSeconds += realDeltaSeconds * TimeCompression;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(object? payload)
    {
        if (payload is Star star)
        {
            _star   = star;
            _system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
        }

        _selectedBody = null;
        _hoveredBody  = null;
        _cameraPos    = Vector2.Zero; // centre on star

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);
        _circle = CreateCircleTexture(64); // larger — planets are bigger dots

        UpdateScreenCentre();
        FitSystemToView();
        UpdateUI();
    }

    public override void OnExit()
    {
        _pixel?.Dispose();
        _circle?.Dispose();
    }

    public override void OnResize(int width, int height)
    {
        UpdateScreenCentre();
        UpdateUI();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse   = Mouse.GetState();
        var keys    = Keyboard.GetState();
        double real = gameTime.ElapsedGameTime.TotalSeconds;

        // Advance simulation clock
        _gameTimeSeconds += real * TimeCompression;

        // Rebuild body positions for this frame
        RebuildBodyPositions();

        HandleZoom(mouse);
        HandleLeftButton(mouse);
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
        DrawBodyNames(sb);
        DrawInfoPanel(sb);
        DrawBackButton(sb, Mouse.GetState());
        DrawTimeControls(sb, Mouse.GetState());

        sb.End();
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

    private void HandleLeftButton(MouseState mouse)
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
            if (_backButtonRect.Contains(mouse.X, mouse.Y)) return;

            if (_timeButtonRect.Contains(mouse.X, mouse.Y))
            {
                _timeCompIndex = (_timeCompIndex + 1) % TimeCompressions.Length;
                return;
            }

            _selectedBody = HitTestBody(mousePos);
        }

        if (justReleased)
            _isDragging = false;
    }

    private void HandleHover(MouseState mouse)
    {
        Vector2 mouseScreen = new(mouse.X, mouse.Y);
        _hoveredBody = HitTestBody(mouseScreen);
    }

    private void HandleKeyboard(KeyboardState keys, MouseState mouse)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        bool backClicked = mouse.LeftButton == ButtonState.Released
                        && _prevMouse.LeftButton == ButtonState.Pressed
                        && _backButtonRect.Contains(mouse.X, mouse.Y);

        if (escPressed || backClicked)
            _pendingTransition = StateTransition.To(GameStateId.GalaxyMap, _star);

        // Time compression keys
        if (keys.IsKeyDown(Keys.OemCloseBrackets) && !_prevKeys.IsKeyDown(Keys.OemCloseBrackets))
            _timeCompIndex = System.Math.Min(_timeCompIndex + 1, TimeCompressions.Length - 1);

        if (keys.IsKeyDown(Keys.OemOpenBrackets) && !_prevKeys.IsKeyDown(Keys.OemOpenBrackets))
            _timeCompIndex = System.Math.Max(_timeCompIndex - 1, 0);

        // Home key — recentre on star
        if (keys.IsKeyDown(Keys.Home) && !_prevKeys.IsKeyDown(Keys.Home))
        {
            _cameraPos = Vector2.Zero;
            FitSystemToView();
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void DrawOrbitRings(SpriteBatch sb)
    {
        // Planet orbit rings around star
        foreach (var planet in _system.Planets)
        {
            float radiusPx = (float)(planet.OrbitalRadius / _metersPerPixel);
            if (radiusPx < MinOrbitRingPixels) continue;

            Vector2 centre = SystemToScreen(Vector2.Zero); // star at origin
            DrawCircle(sb, centre, radiusPx, ColOrbitRing, CircleSegments(radiusPx));
        }

        // Moon orbit rings around their parent planet
        foreach (var (body, pos) in _bodyPositions)
        {
            if (body.BodyType != BodyType.Moon) continue;

            // Find parent planet position — moons are children of planets
            // We need parent's screen pos — approximate by finding the planet
            // whose child list contains this moon
            foreach (var planet in _system.Planets)
            {
                if (!planet.Children.Contains(body)) continue;

                // Get planet's current position
                DVec3 planetPos = planet.GetPosition(_gameTimeSeconds, DVec3.Zero);
                Vector2 parentScreen = SystemToScreen(
                    new Vector2((float)planetPos.X, (float)planetPos.Z));

                float radiusPx = (float)(body.OrbitalRadius / _metersPerPixel);
                if (radiusPx >= MinOrbitRingPixels)
                    DrawCircle(sb, parentScreen, radiusPx, ColMoonOrbit,
                        CircleSegments(radiusPx));
                break;
            }
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

        // Star scales with zoom — clamped between sensible min/max
        const double refMPP = 5e8;
        float scale    = MathF.Log((float)(refMPP / _metersPerPixel) + 1f, 2f);
        float starR    = System.Math.Clamp(StarVisualRadius * scale, 12f, 60f);

        // Outer glow
        DrawDot(sb, screen, starR * 1.8f, _star.GlowColor * 0.3f);
        DrawDot(sb, screen, starR * 1.3f, _star.GlowColor * 0.5f);

        // Star body
        DrawDot(sb, screen, starR, _star.GlowColor);

        // Bright centre
        DrawDot(sb, screen, starR * 0.4f, Color.White);
    }

    private void DrawBodies(SpriteBatch sb)
    {
        foreach (var (body, pos) in _bodyPositions)
        {
            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            if (!IsOnScreen(screen, 30f)) continue;

            float   radius   = VisualRadius(body);
            Color   color    = BodyColor(body);
            bool    selected = _selectedBody == body;
            bool    hovered  = _hoveredBody  == body;

            // Atmosphere halo
            if (body.AtmosphereType != AtmosphereType.None)
            {
                Color atmo = body.AtmosphereColor * 0.3f;
                DrawDot(sb, screen, radius * 1.5f, atmo);
            }

            // Body
            DrawDot(sb, screen, radius, color);

            // Highlight rings
            if (selected)
                DrawCircle(sb, screen, radius + 5f, ColSelected, 24);
            else if (hovered)
                DrawCircle(sb, screen, radius + 4f, ColHovered, 24);
        }
    }

    private void DrawBodyNames(SpriteBatch sb)
    {
        foreach (var (body, pos) in _bodyPositions)
        {
            if (body.BodyType == BodyType.Asteroid) continue;

            Vector2 screen = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            if (!IsOnScreen(screen, 60f)) continue;

            float   radius = VisualRadius(body);
            bool    hovered = _hoveredBody == body || _selectedBody == body;
            float   alpha   = hovered ? NameAlphaHovered : NameAlphaDimmed;
            float   scale   = body.BodyType == BodyType.Moon ? 0.65f : 0.8f;

            Color nameColor = ColText * alpha;
            Vector2 namePos = screen + new Vector2(radius + 4f, -8f);

            sb.DrawString(_font, body.Name, namePos, nameColor,
                0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }

    private void DrawInfoPanel(SpriteBatch sb)
    {
        var display = _selectedBody ?? _hoveredBody;
        if (display == null) return;

        int panelW = 290;
        int margin = 16;
        int x = _gd.Viewport.Width - panelW - margin;
        int y = margin;

        // Measure content height dynamically
        int lineH  = 22;
        int lines  = 10 + display.Children.Count;
        int panelH = lines * lineH + 24;

        DrawRect(sb, new Rectangle(x, y, panelW, panelH), ColPanel);
        DrawRectBorder(sb, new Rectangle(x, y, panelW, panelH), ColPanelBorder, 1);

        int tx = x + 12;
        int ty = y + 12;

        // Name and type
        DrawText(sb, display.Name, new Vector2(tx, ty), Color.White, 1.05f);
        ty += (int)(lineH * 1.3f);

        DrawText(sb, $"{display.BodyType}", new Vector2(tx, ty), BodyColor(display));
        ty += lineH;

        // Atmosphere
        if (display.AtmosphereType != AtmosphereType.None)
        {
            DrawText(sb, $"Atmosphere: {display.AtmosphereType}",
                new Vector2(tx, ty), ColText);
            ty += lineH;
        }

        // Physical
        double radiusKm = Units.MetersToKM(display.RadiusMeters);
        double gravG    = display.SurfaceGravity / 9.81;
        DrawText(sb, $"Radius: {radiusKm:F0} km", new Vector2(tx, ty), ColTextDim);
        ty += lineH;
        DrawText(sb, $"Gravity: {gravG:F2} g", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        // Orbital
        double periodDays = display.Period / Units.DayInSeconds;
        double orbitAU    = Units.MetersToAU(display.OrbitalRadius);
        DrawText(sb, $"Orbit: {orbitAU:F3} AU", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        if (periodDays < 1.0)
            DrawText(sb, $"Period: {display.Period / Units.HourInSeconds:F1} h",
                new Vector2(tx, ty), ColTextDim);
        else if (periodDays < 365)
            DrawText(sb, $"Period: {periodDays:F1} days", new Vector2(tx, ty), ColTextDim);
        else
            DrawText(sb, $"Period: {periodDays / 365.25:F2} years",
                new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        // Hill sphere
        double hillKm = Units.MetersToKM(display.HillSphereRadius);
        DrawText(sb, $"Hill sphere: {hillKm:F0} km", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        // Moons
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
    }

    private void DrawBackButton(SpriteBatch sb, MouseState mouse)
    {
        bool hovered = _backButtonRect.Contains(mouse.X, mouse.Y);
        Color bg = hovered ? ColButtonHover : ColButton;

        DrawRect(sb, _backButtonRect, bg);
        DrawRectBorder(sb, _backButtonRect, ColPanelBorder, 1);
        DrawText(sb, "< GALAXY MAP", new Vector2(_backButtonRect.X + 10, _backButtonRect.Y + 8),
            hovered ? Color.White : ColText, 0.85f);
    }

    private void DrawTimeControls(SpriteBatch sb, MouseState mouse)
    {
        bool hovered = _timeButtonRect.Contains(mouse.X, mouse.Y);
        Color bg = hovered ? ColButtonHover : ColButton;

        DrawRect(sb, _timeButtonRect, bg);
        DrawRectBorder(sb, _timeButtonRect, ColPanelBorder, 1);

        string label = $"TIME: {TimeCompressionLabels[_timeCompIndex]}";
        DrawText(sb, label,
            new Vector2(_timeButtonRect.X + 10, _timeButtonRect.Y + 8),
            hovered ? Color.White : ColText, 0.85f);

        DrawText(sb, "[ / ]",
            new Vector2(_timeButtonRect.Right - 40, _timeButtonRect.Y + 8),
            ColTextDim, 0.75f);
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
            Vector2 screen  = SystemToScreen(new Vector2((float)pos.X, (float)pos.Z));
            float   hitR    = System.Math.Max(VisualRadius(body) + 4f, 8f);
            float   dx      = screenPos.X - screen.X;
            float   dy      = screenPos.Y - screen.Y;
            float   dist    = MathF.Sqrt(dx*dx + dy*dy);

            if (dist < hitR && dist < bestDist)
            {
                bestDist = dist;
                best     = body;
            }
        }

        return best;
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
        // Find outermost planet orbit radius
        double outermost = Units.AU * 2.0; // minimum
        foreach (var planet in _system.Planets)
            outermost = System.Math.Max(outermost, planet.OrbitalRadius);

        // Set zoom so outermost orbit fills ~80% of the shorter screen dimension
        float screenMin = System.Math.Min(_gd.Viewport.Width, _gd.Viewport.Height);
        _metersPerPixel = outermost / (screenMin * 0.40);
        _metersPerPixel = System.Math.Clamp(_metersPerPixel, MinMetersPerPixel, MaxMetersPerPixel);
    }

    private void UpdateScreenCentre()
        => _screenCentre = new Vector2(_gd.Viewport.Width * 0.5f, _gd.Viewport.Height * 0.5f);

    private void UpdateUI()
    {
        _backButtonRect = new Rectangle(16, 16, 150, 36);
        _timeButtonRect = new Rectangle(16, 60, 190, 36);
    }

    private bool IsOnScreen(Vector2 screenPos, float margin)
        => screenPos.X >= -margin && screenPos.X <= _gd.Viewport.Width  + margin
        && screenPos.Y >= -margin && screenPos.Y <= _gd.Viewport.Height + margin;

    /// <summary>
    /// Visual radius in pixels, scaled with zoom level.
    /// Base sizes define relative planet sizes — zoom scales them within a clamped range
    /// so planets are always clickable but never absurdly large.
    /// </summary>
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

        // Reference zoom — sizes feel right at this scale
        const double referenceMetersPerPixel = 5e8;

        // Scale factor — larger when zoomed in, smaller when zoomed out
        float scale = (float)(referenceMetersPerPixel / _metersPerPixel);

        // Logarithmic scaling feels more natural than linear
        scale = MathF.Log(scale + 1f, 2f);

        // Clamp: min keeps them clickable, max prevents them swallowing the screen
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
            float dist  = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float alpha;
            if (dist <= inner)
                alpha = 1f;
            else if (dist <= r)
                alpha = 1f - (dist - inner) / (r - inner);
            else
                alpha = 0f;

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
            float a0 = i       * step;
            float a1 = (i + 1) * step;
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
        => sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
}
