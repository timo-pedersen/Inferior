using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Inferior.Core;
using Inferior.Core.Math;
using Inferior.Galaxy;

namespace Inferior.Game.States;

/// <summary>
/// Galaxy map — top level view of all 2048 stars.
/// Left-click        — select star / show info
/// Double-click      — enter system view
/// Right-click       — set jump target
/// Click empty space — deselect
/// Escape            — clear jump target, then selection
/// Mouse wheel       — zoom
/// Middle drag       — pan
/// </summary>
public sealed class GalaxyMapState : GameState
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly GraphicsDevice _gd;
    private readonly SpriteFont     _font;
    private Star[]                  _stars = [];

    // ── Camera (galaxy space = light years) ───────────────────────────────────
    private Vector2 _cameraPos    = Vector2.Zero;
    private double  _lyPerPixel   = 800.0;

    private const double MinLyPerPixel = 0.5;
    private const double MaxLyPerPixel = 5_000.0;
    private const double ZoomFactor    = 1.15;

    // ── Screen ────────────────────────────────────────────────────────────────
    private Vector2 _screenCentre;

    // ── Selection & navigation ────────────────────────────────────────────────
    private Star?  _selectedStar;
    private Star?  _jumpTarget;
    private Star   _currentSystem = null!;

    // Pending transition — set in input handlers, consumed in Update
    private StateTransition? _pendingTransition;

    // ── Visited ───────────────────────────────────────────────────────────────
    private readonly HashSet<int> _visitedSystems = [];

    // ── Input state ───────────────────────────────────────────────────────────
    private MouseState    _prevMouse;
    private KeyboardState _prevKeys;

    // Left-button drag to pan
    private bool    _isDragging;
    private Vector2 _dragStartScreen;
    private Vector2 _cameraAtDragStart;
    private const float DragThreshold = 5f; // pixels before drag activates

    // Double-click detection
    private double _lastClickTime   = -1.0;
    private int    _lastClickedIdx  = -1;
    private const double DoubleClickSeconds = 0.35;

    // ── Rendering ─────────────────────────────────────────────────────────────
    private Texture2D _pixel  = null!;
    private Texture2D _circle = null!; // soft circle for star dots

    // ── Game constants ────────────────────────────────────────────────────────
    private const float JumpRangeLY        = 8.0f;
    private const float MaxZLinePixels     = 18f;
    private const float GalaxyThicknessLY  = 3_000f;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColBackground  = new(8,   8,  18);
    private static readonly Color ColGrid        = new(20, 25,  40);
    private static readonly Color ColJumpRange   = new(40, 80, 120);
    private static readonly Color ColSelected    = new(255, 220, 80);
    private static readonly Color ColJumpTarget  = new(80,  255, 150);
    private static readonly Color ColZLine       = new(80,  80,  100);
    private static readonly Color ColPanel       = new(10,  15,  30,  210);
    private static readonly Color ColPanelBorder = new(40,  60,  90);
    private static readonly Color ColText        = new(200, 210, 225);
    private static readonly Color ColTextDim     = new(100, 110, 130);

    // ── Constructor ───────────────────────────────────────────────────────────

    public GalaxyMapState(GraphicsDevice gd, SpriteFont font)
        : base(GameStateId.GalaxyMap)
    {
        _gd   = gd;
        _font = font;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnEnter(object? payload)
    {
        // Generate galaxy if first entry
        if (_stars.Length == 0)
        {
            _stars         = GalaxyGenerator.Generate();
            _currentSystem = FindStartingSystem();
            _visitedSystems.Add(_currentSystem.GalaxyIndex);

            _cameraPos = new Vector2(
                (float)_currentSystem.GalacticPos.X,
                (float)_currentSystem.GalacticPos.Z);
        }

        // Returning from system view — re-select the star we came from
        if (payload is Star returnedFrom)
        {
            _selectedStar  = returnedFrom;
            _currentSystem = returnedFrom;
            _visitedSystems.Add(returnedFrom.GalaxyIndex);
        }

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);
        _circle = CreateCircleTexture(32);

        _pendingTransition = null;
        UpdateScreenCentre();
    }

    public override void OnExit()
    {
        _pixel?.Dispose();
        _circle?.Dispose();
    }

    public override void OnResize(int width, int height) => UpdateScreenCentre();

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse   = Mouse.GetState();
        var keys    = Keyboard.GetState();
        double elapsed = gameTime.TotalGameTime.TotalSeconds;

        HandleZoom(mouse);
        HandleLeftButton(mouse, elapsed);
        HandleRightClick(mouse);
        HandleKeyboard(keys);

        _prevMouse = mouse;
        _prevKeys  = keys;

        // Consume and return any pending transition
        var transition     = _pendingTransition;
        _pendingTransition = null;
        return transition;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(GameTime gameTime, GraphicsDevice gd, SpriteBatch sb)
    {
        gd.Clear(ColBackground);

        sb.Begin(blendState: BlendState.AlphaBlend);

        DrawGrid(sb);
        DrawJumpRange(sb);
        DrawStars(sb);
        DrawCurrentSystemMarker(sb);
        DrawJumpTargetLine(sb);
        DrawInfoPanel(sb);
        DrawZoomIndicator(sb);
        DrawHints(sb);

        sb.End();
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    private void HandleZoom(MouseState mouse)
    {
        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll == 0) return;

        Vector2 mouseScreen = new(mouse.X, mouse.Y);
        Vector2 mouseWorld  = ScreenToGalaxy(mouseScreen);

        _lyPerPixel *= scroll > 0 ? 1.0 / ZoomFactor : ZoomFactor;
        _lyPerPixel  = System.Math.Clamp(_lyPerPixel, MinLyPerPixel, MaxLyPerPixel);

        Vector2 mouseWorldAfter = ScreenToGalaxy(mouseScreen);
        _cameraPos -= mouseWorldAfter - mouseWorld;
    }

    private void HandleLeftButton(MouseState mouse, double elapsed)
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
                _cameraPos = _cameraAtDragStart - delta * (float)_lyPerPixel;
        }

        if (justReleased && !_isDragging)
        {
            // It was a click — handle select / double-click
            Vector2 mouseWorld = ScreenToGalaxy(mousePos);
            Star?   hit        = HitTestStar(mouseWorld);

            if (hit == null)
            {
                _selectedStar = null;
                return;
            }

            bool isDouble = elapsed - _lastClickTime < DoubleClickSeconds
                         && _lastClickedIdx == hit.GalaxyIndex;

            if (isDouble)
            {
                _pendingTransition = StateTransition.To(GameStateId.SystemSpace, hit);
                return;
            }

            _selectedStar   = hit;
            _lastClickTime  = elapsed;
            _lastClickedIdx = hit.GalaxyIndex;
        }

        if (justReleased)
            _isDragging = false;
    }

    private void HandleRightClick(MouseState mouse)
    {
        bool clicked = mouse.RightButton      == ButtonState.Released
                    && _prevMouse.RightButton == ButtonState.Pressed;
        if (!clicked) return;

        Vector2 mouseWorld = ScreenToGalaxy(new Vector2(mouse.X, mouse.Y));
        Star?   hit        = HitTestStar(mouseWorld);

        if (hit == null || hit.GalaxyIndex == _currentSystem.GalaxyIndex)
        {
            _jumpTarget = null;
            return;
        }

        _jumpTarget   = hit;
    }

    private void HandleKeyboard(KeyboardState keys)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        if (!escPressed) return;

        if (_jumpTarget != null)
            _jumpTarget = null;
        else
            _selectedStar = null;
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void DrawGrid(SpriteBatch sb)
    {
        const float gridSpacingLY = 10_000f;
        float pixelSpacing = (float)(gridSpacingLY / _lyPerPixel);
        if (pixelSpacing < 20f) return;

        float startX = MathF.Floor(_cameraPos.X / gridSpacingLY) * gridSpacingLY;
        for (float worldX = startX; ; worldX += gridSpacingLY)
        {
            float sx = GalaxyToScreen(new Vector2(worldX, 0)).X;
            if (sx > _gd.Viewport.Width + pixelSpacing) break;
            DrawLine(sb, new Vector2(sx, 0), new Vector2(sx, _gd.Viewport.Height), ColGrid);
        }

        float startZ = MathF.Floor(_cameraPos.Y / gridSpacingLY) * gridSpacingLY;
        for (float worldZ = startZ; ; worldZ += gridSpacingLY)
        {
            float sy = GalaxyToScreen(new Vector2(0, worldZ)).Y;
            if (sy > _gd.Viewport.Height + pixelSpacing) break;
            DrawLine(sb, new Vector2(0, sy), new Vector2(_gd.Viewport.Width, sy), ColGrid);
        }
    }

    private void DrawJumpRange(SpriteBatch sb)
    {
        Vector2 centre = GalaxyToScreen(new Vector2(
            (float)_currentSystem.GalacticPos.X,
            (float)_currentSystem.GalacticPos.Z));

        float radiusPx = (float)(JumpRangeLY / _lyPerPixel);
        if (radiusPx < 5f) return;

        DrawCircle(sb, centre, radiusPx, ColJumpRange, 64);
    }

    private void DrawStars(SpriteBatch sb)
    {
        const float margin = 20f;

        foreach (var star in _stars)
        {
            Vector2 screen = GalaxyToScreen(new Vector2(
                (float)star.GalacticPos.X,
                (float)star.GalacticPos.Z));

            if (screen.X < -margin || screen.X > _gd.Viewport.Width  + margin) continue;
            if (screen.Y < -margin || screen.Y > _gd.Viewport.Height + margin) continue;

            bool isVisited  = _visitedSystems.Contains(star.GalaxyIndex);
            bool isSelected = _selectedStar?.GalaxyIndex == star.GalaxyIndex;
            bool isCurrent  = _currentSystem.GalaxyIndex == star.GalaxyIndex;
            bool isTarget   = _jumpTarget?.GalaxyIndex   == star.GalaxyIndex;

            float dotSize = star.MapDotSize * (isSelected || isCurrent ? 1.6f : 1.0f);

            Color dotColor = isVisited
                ? star.MapColor
                : new Color(
                    (int)(star.MapColor.R * 0.5f),
                    (int)(star.MapColor.G * 0.5f),
                    (int)(star.MapColor.B * 0.5f));

            // Z-line
            float zFrac   = (float)(star.GalacticPos.Y / GalaxyThicknessLY);
            float zPixels = zFrac * MaxZLinePixels;
            if (MathF.Abs(zPixels) > 1f)
            {
                Vector2 lineEnd = screen - new Vector2(0, zPixels);
                DrawLine(sb, screen, lineEnd, ColZLine);
                DrawDot(sb, lineEnd, 1.5f, ColZLine);
            }

            DrawDot(sb, screen, dotSize, dotColor);

            if (isSelected) DrawCircle(sb, screen, dotSize + 4f, ColSelected,   24);
            if (isCurrent)  DrawCircle(sb, screen, dotSize + 6f, Color.White,   32);
            if (isTarget)   DrawCircle(sb, screen, dotSize + 5f, ColJumpTarget, 24);
        }
    }

    private void DrawCurrentSystemMarker(SpriteBatch sb)
    {
        Vector2 pos = GalaxyToScreen(new Vector2(
            (float)_currentSystem.GalacticPos.X,
            (float)_currentSystem.GalacticPos.Z));

        const float arm = 8f;
        DrawLine(sb, pos - new Vector2(arm, 0), pos + new Vector2(arm, 0), Color.White);
        DrawLine(sb, pos - new Vector2(0, arm), pos + new Vector2(0, arm), Color.White);
    }

    private void DrawJumpTargetLine(SpriteBatch sb)
    {
        if (_jumpTarget == null) return;

        Vector2 from = GalaxyToScreen(new Vector2(
            (float)_currentSystem.GalacticPos.X,
            (float)_currentSystem.GalacticPos.Z));

        Vector2 to = GalaxyToScreen(new Vector2(
            (float)_jumpTarget.GalacticPos.X,
            (float)_jumpTarget.GalacticPos.Z));

        DrawDashedLine(sb, from, to, ColJumpTarget, 8f, 5f);
    }

    private void DrawInfoPanel(SpriteBatch sb)
    {
        Star? display = _selectedStar;
        if (display == null) return;

        int panelW = 280;
        int panelH = 210;
        int margin = 16;
        int x = _gd.Viewport.Width - panelW - margin;
        int y = margin;

        DrawRect(sb, new Rectangle(x, y, panelW, panelH), ColPanel);
        DrawRectBorder(sb, new Rectangle(x, y, panelW, panelH), ColPanelBorder, 1);

        int tx    = x + 12;
        int ty    = y + 12;
        int lineH = 22;

        DrawText(sb, display.Name, new Vector2(tx, ty), Color.White, 1.1f);
        ty += (int)(lineH * 1.3f);

        DrawText(sb, $"Class:  {display.SpectralClass}", new Vector2(tx, ty), display.MapColor);
        ty += lineH;

        DrawText(sb, $"Temp:   {display.Temperature:F0} K", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        double dx     = display.GalacticPos.X - _currentSystem.GalacticPos.X;
        double dy     = display.GalacticPos.Y - _currentSystem.GalacticPos.Y;
        double dz     = display.GalacticPos.Z - _currentSystem.GalacticPos.Z;
        double distLY = System.Math.Sqrt(dx*dx + dy*dy + dz*dz);

        DrawText(sb, $"Dist:   {distLY:F1} ly", new Vector2(tx, ty), ColTextDim);
        ty += lineH;

        bool visited   = _visitedSystems.Contains(display.GalaxyIndex);
        bool inRange   = distLY <= JumpRangeLY;
        bool isCurrent = display.GalaxyIndex == _currentSystem.GalaxyIndex;

        DrawText(sb, visited ? "Visited" : "Unexplored",
            new Vector2(tx, ty), visited ? Color.White : ColTextDim);
        ty += lineH;

        if (!isCurrent)
        {
            DrawText(sb, inRange ? "In jump range" : "Out of range",
                new Vector2(tx, ty), inRange ? ColJumpTarget : new Color(180, 80, 60));
            ty += lineH;
        }

        bool isTarget = _jumpTarget?.GalaxyIndex == display.GalaxyIndex;
        if (isTarget)
            DrawText(sb, "[ JUMP TARGET ]", new Vector2(tx, ty), ColJumpTarget);
        else if (isCurrent)
            DrawText(sb, "[ CURRENT SYSTEM ]", new Vector2(tx, ty), Color.White);
    }

    private void DrawZoomIndicator(SpriteBatch sb)
    {
        const float barLY = 1_000f;
        float barPx = (float)(barLY / _lyPerPixel);
        if (barPx < 10f || barPx > _gd.Viewport.Width * 0.4f) return;

        int bx = 20;
        int by = _gd.Viewport.Height - 30;

        DrawLine(sb, new Vector2(bx,         by),     new Vector2(bx + barPx, by),     ColTextDim);
        DrawLine(sb, new Vector2(bx,         by - 4), new Vector2(bx,         by + 4), ColTextDim);
        DrawLine(sb, new Vector2(bx + barPx, by - 4), new Vector2(bx + barPx, by + 4), ColTextDim);
        DrawText(sb, $"{barLY:F0} ly", new Vector2(bx, by - 20), ColTextDim, 0.75f);
    }

    private void DrawHints(SpriteBatch sb)
    {
        int x = _gd.Viewport.Width  - 220;
        int y = _gd.Viewport.Height - 80;

        DrawText(sb, "Left-click    select",        new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Double-click  enter system",  new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Right-click   jump target",   new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Scroll        zoom",           new Vector2(x, y), ColTextDim, 0.72f);
    }

    // ── Coordinate transforms ─────────────────────────────────────────────────

    private Vector2 GalaxyToScreen(Vector2 galaxyPos)
        => _screenCentre + (galaxyPos - _cameraPos) / (float)_lyPerPixel;

    private Vector2 ScreenToGalaxy(Vector2 screenPos)
        => _cameraPos + (screenPos - _screenCentre) * (float)_lyPerPixel;

    // ── Hit testing ───────────────────────────────────────────────────────────

    private Star? HitTestStar(Vector2 galaxyPos)
    {
        Star?  best     = null;
        double bestDist = double.MaxValue;

        foreach (var star in _stars)
        {
            double hitRadius = System.Math.Max(star.MapDotSize * _lyPerPixel, 4.0 * _lyPerPixel);
            double dx        = galaxyPos.X - star.GalacticPos.X;
            double dz        = galaxyPos.Y - star.GalacticPos.Z;
            double dist      = System.Math.Sqrt(dx*dx + dz*dz);

            if (dist < hitRadius && dist < bestDist)
            {
                bestDist = dist;
                best     = star;
            }
        }

        return best;
    }

    // ── Primitive drawing ─────────────────────────────────────────────────────

    private void DrawDot(SpriteBatch sb, Vector2 centre, float radius, Color color)
    {
        // Draw using soft circle texture — no more squares
        float size = radius * 2f;
        sb.Draw(_circle,
            new Rectangle((int)(centre.X - radius), (int)(centre.Y - radius),
                          (int)size, (int)size),
            color);
    }

    private Texture2D CreateCircleTexture(int diameter)
    {
        var    tex    = new Texture2D(_gd, diameter, diameter);
        var    data   = new Color[diameter * diameter];
        float  r      = diameter * 0.5f;
        float  cx     = r, cy = r;
        float  inner  = r * 0.6f; // fully opaque inside this radius

        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dist  = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float alpha;
            if (dist <= inner)
                alpha = 1f;
            else if (dist <= r)
                alpha = 1f - (dist - inner) / (r - inner); // soft edge falloff
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

    private void DrawDashedLine(SpriteBatch sb, Vector2 from, Vector2 to,
        Color color, float dashLength, float gapLength)
    {
        Vector2 dir   = to - from;
        float   total = dir.Length();
        if (total < 0.1f) return;
        dir /= total;

        float pos  = 0f;
        bool  draw = true;
        while (pos < total)
        {
            float segLen = System.Math.Min(draw ? dashLength : gapLength, total - pos);
            if (draw)
                DrawLine(sb, from + dir * pos, from + dir * (pos + segLen), color);
            pos  += segLen;
            draw  = !draw;
        }
    }

    private void DrawCircle(SpriteBatch sb, Vector2 centre, float radius, Color color, int segments)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float   a0 = i * step;
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
        => sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateScreenCentre()
        => _screenCentre = new Vector2(_gd.Viewport.Width * 0.5f, _gd.Viewport.Height * 0.5f);

    private Star FindStartingSystem()
    {
        Star?  best     = null;
        double bestDist = double.MaxValue;

        foreach (var star in _stars)
        {
            if (star.SpectralClass is not (SpectralClass.G or SpectralClass.K)) continue;
            double d = star.GalacticPos.Length;
            if (d >= bestDist) continue;
            bestDist = d;
            best     = star;
        }

        return best ?? _stars[0];
    }
}
