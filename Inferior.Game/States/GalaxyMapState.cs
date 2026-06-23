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
    private double       _maxLyPerPixel = 5_000.0;  // recalculated in UpdateScreenCentre
    private const double ZoomFactor    = 1.15;

    // ── Screen ────────────────────────────────────────────────────────────────
    private Vector2 _screenCentre;

    // ── Selection & navigation ────────────────────────────────────────────────
    private Star?      _selectedStar;
    private Star?      _jumpTarget;
    private Star       _currentSystem    = null!;
    private double     _storedGameTime   = 0.0;
    private DVec3?     _spawnPos;
    private Quaternion? _spawnOrientation;
    private OrbitalBody? _navBody;
    private Station?     _navStation;

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

    // ── Search ────────────────────────────────────────────────────────────────
    private TextBox         _searchBox     = null!;
    private UIRenderer      _uiRenderer    = null!;
    private Theme           _uiTheme       = null!;
    private List<Star>      _searchResults = [];
    private int             _searchSelIdx  = -1;
    private MouseState      _prevMouseUI;
    private KeyboardState   _prevKeysUI;

    private const int SearchBoxW  = 240;
    private const int SearchBoxH  = 28;
    private const int SearchMargin = 16;
    private const int DropdownItemH = 24;
    private const int DropdownMaxItems = 10;

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
            _stars = GalaxyGenerator.Generate();
            // Fit galaxy diameter (×1.1 margin) to shortest screen dimension.
            float minDim = MathF.Min(_gd.Viewport.Width, _gd.Viewport.Height);
            _lyPerPixel  = (GalaxyGenerator.GalaxyRadiusLY * 2.0 * 1.1) / minDim;
        }

        // Arriving from in-flight (N key) or system map (N key)
        if (payload is GalaxyMapPayload gmp)
        {
            _currentSystem    = gmp.CurrentStar;
            _storedGameTime   = gmp.GameTime;
            _spawnPos         = gmp.SpawnPos;
            _spawnOrientation = gmp.SpawnOrientation;
            _navBody          = gmp.NavBody;
            _navStation       = gmp.NavStation;
            _visitedSystems.Add(_currentSystem.GalaxyIndex);
            _cameraPos = new Vector2(
                (float)_currentSystem.GalacticPos.X,
                (float)_currentSystem.GalacticPos.Z);
        }
        else if (_currentSystem == null!)
        {
            _currentSystem = FindStartingSystem();
            _visitedSystems.Add(_currentSystem.GalaxyIndex);
            _cameraPos = new Vector2(
                (float)_currentSystem.GalacticPos.X,
                (float)_currentSystem.GalacticPos.Z);
        }

        _pixel = new Texture2D(_gd, 1, 1);
        _pixel.SetData([Color.White]);
        _circle = CreateCircleTexture(32);

        // Search UI
        _uiRenderer = new UIRenderer(_gd);
        _uiTheme    = Theme.InferiorDark(_font);

        _searchBox = new TextBox
        {
            Multiline = false,
            Placeholder = "Search systems...",
            Bounds = new Rectangle(SearchMargin, SearchMargin, SearchBoxW, SearchBoxH),
            TabIndex = 1,
        };
        _searchBox.TextChanged += (_, text) => UpdateSearchResults(text);

        _prevMouseUI = Mouse.GetState();
        _prevKeysUI  = Keyboard.GetState();

        _pendingTransition = null;
        UpdateScreenCentre();
    }

    public override void OnExit()
    {
        _pixel?.Dispose();
        _circle?.Dispose();
        _uiRenderer?.Dispose();
    }

    public override void OnResize(int width, int height) => UpdateScreenCentre();

    // ── Update ────────────────────────────────────────────────────────────────

    public override StateTransition? Update(GameTime gameTime)
    {
        var mouse   = Mouse.GetState();
        var keys    = Keyboard.GetState();
        double elapsed = gameTime.TotalGameTime.TotalSeconds;
        double dt       = gameTime.ElapsedGameTime.TotalSeconds;

        // Build InputState for the UI search box
        var typedChars = InputState.DrainTypedChars();
        var uiInput    = new InputState(mouse, _prevMouseUI, keys, _prevKeysUI, typedChars);
        _prevMouseUI   = mouse;
        _prevKeysUI    = keys;

        // Animate search box
        _searchBox.Update(dt);

        // Handle dropdown keyboard nav before passing input to search box
        bool dropdownActive = _searchResults.Count > 0 && _searchBox.Text.Length > 0;
        bool inputConsumedByDropdown = false;

        if (dropdownActive && _searchBox.IsFocused)
        {
            if (uiInput.IsKeyPressed(Keys.Down))
            {
                _searchSelIdx = Math.Min(_searchSelIdx + 1, _searchResults.Count - 1);
                inputConsumedByDropdown = true;
            }
            else if (uiInput.IsKeyPressed(Keys.Up))
            {
                _searchSelIdx = Math.Max(_searchSelIdx - 1, 0);
                inputConsumedByDropdown = true;
            }
            else if (uiInput.IsKeyPressed(Keys.Enter) && _searchSelIdx >= 0)
            {
                SelectSearchResult(_searchSelIdx);
                inputConsumedByDropdown = true;
            }
            else if (uiInput.IsKeyPressed(Keys.Escape))
            {
                _searchBox.Text = "";
                _searchResults.Clear();
                _searchSelIdx = -1;
                inputConsumedByDropdown = true;
            }
        }

        if (!inputConsumedByDropdown)
            _searchBox.HandleInput(uiInput);

        // Focus/unfocus search box on Ctrl+F
        if (uiInput.IsKeyPressed(Keys.F) && uiInput.Ctrl)
        {
            _searchBox.IsFocused = !_searchBox.IsFocused;
        }

        // Click on a dropdown result
        if (uiInput.LeftPressed && dropdownActive)
        {
            var mx = uiInput.MousePosition.X;
            var my = uiInput.MousePosition.Y;
            int dropX = SearchMargin;
            int dropY = SearchMargin + SearchBoxH;

            for (int i = 0; i < Math.Min(_searchResults.Count, DropdownMaxItems); i++)
            {
                var itemRect = new Rectangle(dropX, dropY + i * DropdownItemH, SearchBoxW, DropdownItemH);
                if (itemRect.Contains(mx, my))
                {
                    SelectSearchResult(i);
                    break;
                }
            }
        }

        // Only handle game map input when search box is not focused
        if (!_searchBox.IsFocused)
        {
            HandleZoom(mouse);
            HandleLeftButton(mouse, elapsed);
            HandleRightClick(mouse);
            HandleKeyboard(keys);
        }
        else
        {
            // Still update previous state even if not processing game input
            _prevMouse = mouse;
            _prevKeys  = keys;
        }

        if (!_searchBox.IsFocused)
        {
            _prevMouse = mouse;
            _prevKeys  = keys;
        }

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
        DrawSearchHint(sb);

        sb.End();

        // Search UI — uses UIRenderer (its own SpriteBatch Begin/End)
        DrawSearchUI(sb);
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    private void HandleZoom(MouseState mouse)
    {
        int scroll = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (scroll == 0) return;

        Vector2 mouseScreen = new(mouse.X, mouse.Y);
        Vector2 mouseWorld  = ScreenToGalaxy(mouseScreen);

        _lyPerPixel *= scroll > 0 ? 1.0 / ZoomFactor : ZoomFactor;
        _lyPerPixel  = System.Math.Clamp(_lyPerPixel, MinLyPerPixel, _maxLyPerPixel);

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
                _pendingTransition = StateTransition.To(GameStateId.SystemMap, hit);
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

        _jumpTarget = hit;
        // deliberately not changing _selectedStar — right-click only sets jump target
    }

    private void HandleKeyboard(KeyboardState keys)
    {
        bool escPressed = keys.IsKeyDown(Keys.Escape) && !_prevKeys.IsKeyDown(Keys.Escape);
        bool mPressed   = keys.IsKeyDown(Keys.M)      && !_prevKeys.IsKeyDown(Keys.M);
        bool nPressed   = keys.IsKeyDown(Keys.N)      && !_prevKeys.IsKeyDown(Keys.N);

        if (escPressed || nPressed)
        {
            // Esc or N = back to flight (N toggles the galaxy map)
            _pendingTransition = StateTransition.To(GameStateId.SystemSpace,
                new SystemSpacePayload(_currentSystem, null, _storedGameTime, null, _spawnPos, _spawnOrientation,
                    _navBody, _navStation));
        }
        else if (mPressed)
        {
            // M = open system map for the current system (pass ship position through)
            _pendingTransition = StateTransition.To(GameStateId.SystemMap,
                new SystemMapPayload(_currentSystem, _storedGameTime, CockpitLayout.Default, _spawnPos, _spawnOrientation));
        }
    }

    // ── Search helpers ────────────────────────────────────────────────────────

    private void UpdateSearchResults(string query)
    {
        _searchSelIdx = -1;
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchResults.Clear();
            return;
        }

        _searchResults = _stars
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.Name)
            .Take(DropdownMaxItems)
            .ToList();
    }

    private void SelectSearchResult(int index)
    {
        if (index < 0 || index >= _searchResults.Count) return;
        var star = _searchResults[index];
        _selectedStar = star;
        // Centre camera on the selected star
        _cameraPos = new Vector2((float)star.GalacticPos.X, (float)star.GalacticPos.Z);
        _searchBox.Text = "";
        _searchResults.Clear();
        _searchSelIdx  = -1;
        _searchBox.IsFocused = false;
    }

    private void DrawSearchHint(SpriteBatch sb)
    {
        DrawText(sb, "Ctrl+F  search",
            new Vector2(SearchMargin, _gd.Viewport.Height - 100), ColTextDim, 0.72f);
    }

    private void DrawSearchUI(SpriteBatch sb)
    {
        // Search box
        sb.Begin(blendState: BlendState.AlphaBlend);
        _searchBox.Draw(sb, _uiRenderer, _uiTheme);

        // Dropdown results
        bool showDropdown = _searchResults.Count > 0 && _searchBox.Text.Length > 0;
        if (showDropdown)
        {
            int dropX = SearchMargin;
            int dropY = SearchMargin + SearchBoxH;
            int count = Math.Min(_searchResults.Count, DropdownMaxItems);

            // Dropdown background
            var dropBg = new Rectangle(dropX, dropY, SearchBoxW, count * DropdownItemH);
            _uiRenderer.FillRect(sb, dropBg, new Color(8, 12, 25, 240));
            _uiRenderer.DrawRect(sb, dropBg, new Color(40, 60, 90), 1);

            for (int i = 0; i < count; i++)
            {
                var star    = _searchResults[i];
                var itemRect = new Rectangle(dropX, dropY + i * DropdownItemH, SearchBoxW, DropdownItemH);

                if (i == _searchSelIdx)
                    _uiRenderer.FillRect(sb, itemRect, new Color(40, 80, 130, 200));

                // Star colour dot
                _uiRenderer.DrawDot(sb,
                    new Vector2(dropX + 10, dropY + i * DropdownItemH + DropdownItemH / 2f),
                    3f, star.MapColor);

                _uiRenderer.DrawText(sb, star.Name,
                    new Vector2(dropX + 22, dropY + i * DropdownItemH + (DropdownItemH - 14) / 2f),
                    _font, 0.85f, new Color(200, 210, 225));
            }
        }

        sb.End();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void DrawGrid(SpriteBatch sb)
    {
        const float gridSpacingLY = 10_000f;
        float pixelSpacing = (float)(gridSpacingLY / _lyPerPixel);
        if (pixelSpacing < 20f) return;

        // Start from the world X that maps to the left edge of the screen.
        float leftWorldX = (float)(_cameraPos.X - _screenCentre.X * _lyPerPixel);
        float startX = MathF.Floor(leftWorldX / gridSpacingLY) * gridSpacingLY;
        for (float worldX = startX; ; worldX += gridSpacingLY)
        {
            float sx = GalaxyToScreen(new Vector2(worldX, 0)).X;
            if (sx > _gd.Viewport.Width + pixelSpacing) break;
            DrawLine(sb, new Vector2(sx, 0), new Vector2(sx, _gd.Viewport.Height), ColGrid);
        }

        // Start from the world Z that maps to the top edge of the screen.
        float topWorldZ = (float)(_cameraPos.Y - _screenCentre.Y * _lyPerPixel);
        float startZ = MathF.Floor(topWorldZ / gridSpacingLY) * gridSpacingLY;
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
        int x = _gd.Viewport.Width  - 240;
        int y = _gd.Viewport.Height - 100;

        DrawText(sb, "Left-click    select",        new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Double-click  system map",    new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Right-click   jump target",   new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Scroll        zoom",           new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "M             current system", new Vector2(x, y), ColTextDim, 0.72f); y += 18;
        DrawText(sb, "Esc           back to flight", new Vector2(x, y), ColTextDim, 0.72f);
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
        => FontHelper.Draw(sb, _font, text, pos, color, scale);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateScreenCentre()
    {
        _screenCentre  = new Vector2(_gd.Viewport.Width * 0.5f, _gd.Viewport.Height * 0.5f);
        float minDim   = MathF.Min(_gd.Viewport.Width, _gd.Viewport.Height);
        _maxLyPerPixel = (GalaxyGenerator.GalaxyRadiusLY * 2.0 * 1.1) / minDim;
    }

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
