using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public enum PanelEdge { Left, Right, Top, Bottom }

/// <summary>
/// Sliding panel that lives on one screen edge with a persistent tab-handle strip.
///
/// Usage:
///   1. Set Bounds = viewport rectangle each frame (or on resize).
///   2. Call AddTab(label, contentControl) for each tab.
///   3. Add to UIManager as a top-level root.
///
/// Content controls are normal children in the Control hierarchy.
/// ContentBounds returns the animated inner panel area, so children follow
/// the slide animation automatically via AbsoluteBounds.
/// Only the active tab's content has Visible = true.
///
/// Interaction rules:
///   - Clicking a closed tab opens the panel to that tab.
///   - Clicking the active tab closes the panel.
///   - Clicking a different tab while open switches content without re-animating.
/// </summary>
public sealed class EdgePanelHost : Control
{
    private readonly record struct TabEntry(string Label, Control Content);
    private readonly List<TabEntry> _tabs = [];

    // ── Configuration ─────────────────────────────────────────────────────────

    public PanelEdge Edge         { get; }
    public int PanelSize          { get; set; } = 280;  // content width (L/R) or height (T/B)
    public int HandleSize         { get; set; } = 28;   // tab strip width (L/R) or height (T/B)
    public int HandleLength       { get; set; } = 80;   // per-tab height (L/R) or width (T/B)
    public int HandleGap          { get; set; } = 2;    // pixels between tab handles
    public int CornerMargin       { get; set; } = 8;    // inset from screen corners
    public int ContentPad         { get; set; } = 8;    // padding inside panel body

    // ── State ─────────────────────────────────────────────────────────────────

    private int    _activeTab     = -1;
    private bool   _isOpen        = false;
    private double _slideProgress = 0.0;   // 0 = fully retracted, 1 = fully extended
    private double _edgeShift     = 1.0;   // 0 = normal (handles visible gap), 1 = flush with screen edge
    private int    _hoveredHandle = -1;    // set by HandleInput (UI mode only), cleared by Update
    private const double SlideDuration = 0.15;

    /// <summary>When false, tab handles are hidden and the panel slides flush with the screen edge.</summary>
    public bool UiModeActive { get; set; } = true;

    // ── Constructor ───────────────────────────────────────────────────────────

    public EdgePanelHost(PanelEdge edge) => Edge = edge;

    // ── Public API ────────────────────────────────────────────────────────────

    public void AddTab(string label, Control content)
    {
        content.Visible = false;
        _tabs.Add(new TabEntry(label, content));
        Add(content);
    }

    /// <summary>Returns the current open/tab state so it can be saved and restored later.</summary>
    public (int activeTab, bool isOpen) CaptureState() => (_activeTab, _isOpen);

    /// <summary>
    /// Restores a previously captured state. The panel animates open from retracted
    /// if isOpen is true — it does not snap to the final position.
    /// </summary>
    public void ApplyState(int activeTab, bool isOpen)
    {
        // Hide all content first
        foreach (var tab in _tabs)
            tab.Content.Visible = false;

        _activeTab     = activeTab >= 0 && activeTab < _tabs.Count ? activeTab : -1;
        _isOpen        = isOpen && _activeTab >= 0;
        _slideProgress = 0.0;

        if (_activeTab >= 0)
            _tabs[_activeTab].Content.Visible = true;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The animated inner area of the panel body.
    /// Children placed at (0,0) relative to this will follow the slide animation.
    /// </summary>
    public override Rectangle ContentBounds
    {
        get
        {
            var panel = PanelBodyRect();
            return new Rectangle(
                panel.X + ContentPad,
                panel.Y + ContentPad,
                System.Math.Max(0, panel.Width  - ContentPad * 2),
                System.Math.Max(0, panel.Height - ContentPad * 2));
        }
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    private Rectangle HandleRect(int index)
    {
        var s      = AbsoluteBounds;
        int offset = CornerMargin + index * (HandleLength + HandleGap);
        return Edge switch
        {
            PanelEdge.Right  => new Rectangle(s.Right - HandleSize,       s.Y + offset, HandleSize,   HandleLength),
            PanelEdge.Left   => new Rectangle(s.Left,                     s.Y + offset, HandleSize,   HandleLength),
            PanelEdge.Top    => new Rectangle(s.X + offset,               s.Top,        HandleLength, HandleSize),
            PanelEdge.Bottom => new Rectangle(s.X + offset, s.Bottom - HandleSize,      HandleLength, HandleSize),
            _                => Rectangle.Empty,
        };
    }

    private Rectangle PanelBodyRect()
    {
        var   s     = AbsoluteBounds;
        float ep    = EaseOut(_slideProgress);
        int   shift = (int)(_edgeShift * HandleSize);  // nudges panel flush with screen edge in ship mode

        return Edge switch
        {
            PanelEdge.Right => new Rectangle(
                s.Right - (int)((HandleSize + PanelSize) * ep) + shift,
                s.Y + CornerMargin,
                PanelSize,
                s.Height - CornerMargin * 2),

            PanelEdge.Left => new Rectangle(
                s.Left + (int)((HandleSize + PanelSize) * ep) - PanelSize - shift,
                s.Y + CornerMargin,
                PanelSize,
                s.Height - CornerMargin * 2),

            PanelEdge.Top => new Rectangle(
                s.X + CornerMargin,
                s.Top + (int)((HandleSize + PanelSize) * ep) - PanelSize - shift,
                s.Width - CornerMargin * 2,
                PanelSize),

            PanelEdge.Bottom => new Rectangle(
                s.X + CornerMargin,
                s.Bottom - (int)((HandleSize + PanelSize) * ep) + shift,
                s.Width - CornerMargin * 2,
                PanelSize),

            _ => Rectangle.Empty,
        };
    }

    // Cubic ease-out: fast start, brakes smoothly into final position
    private static float EaseOut(double t)
        => 1f - (float)System.Math.Pow(1.0 - System.Math.Clamp(t, 0.0, 1.0), 3.0);

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(double dt)
    {
        // Drive panel open/close animation
        double target = _isOpen ? 1.0 : 0.0;
        if (_slideProgress < target)
            _slideProgress = System.Math.Min(target, _slideProgress + dt / SlideDuration);
        else if (_slideProgress > target)
            _slideProgress = System.Math.Max(target, _slideProgress - dt / SlideDuration);

        // Drive edge-flush animation: 0 = normal gap for handles, 1 = panel flush with screen edge
        double edgeTarget = UiModeActive ? 0.0 : 1.0;
        if (_edgeShift < edgeTarget)
            _edgeShift = System.Math.Min(edgeTarget, _edgeShift + dt / SlideDuration);
        else if (_edgeShift > edgeTarget)
            _edgeShift = System.Math.Max(edgeTarget, _edgeShift - dt / SlideDuration);

        // Keep active content sized to fill the current ContentBounds so HitTest works
        if (_activeTab >= 0)
        {
            var cb = ContentBounds;
            _tabs[_activeTab].Content.Bounds = new Rectangle(0, 0, cb.Width, cb.Height);
        }

        // Hover state is set by HandleInput (UI mode only); clear each frame so
        // it resets automatically when leaving UI mode without an explicit mouse-leave.
        _hoveredHandle = -1;

        // Base class updates all visible children
        base.Update(dt);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        // Tab handle interaction — track hover and clicks
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!HandleRect(i).Contains(input.MousePosition)) continue;

            _hoveredHandle = i;

            if (input.LeftReleased)
            {
                ToggleTab(i);
                return true;
            }

            // Block pointer from passing through handle strip
            return input.LeftHeld || input.LeftPressed;
        }

        // Forward to visible children when mouse is inside the panel body
        if (_activeTab >= 0 && _slideProgress > 0.05)
        {
            if (PanelBodyRect().Contains(input.MousePosition))
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                    _children[i].HandleInput(input);
                return true; // always consume — prevent click-through to game
            }
        }

        return false;
    }

    private void ToggleTab(int index)
    {
        if (!_isOpen)
        {
            SetActiveTab(index);
            _isOpen = true;
        }
        else if (_activeTab == index)
        {
            _isOpen = false;
            // Content stays visible while animating out (it's going off-screen)
        }
        else
        {
            SetActiveTab(index); // switch without re-animating
        }
    }

    private void SetActiveTab(int index)
    {
        if (_activeTab >= 0 && _activeTab < _tabs.Count)
            _tabs[_activeTab].Content.Visible = false;

        _activeTab = index;

        if (index >= 0 && index < _tabs.Count)
            _tabs[index].Content.Visible = true;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        float ease = EaseOut(_slideProgress);

        // Panel body background — drawn before children
        if (ease > 0.001f && _activeTab >= 0)
        {
            renderer.DrawPanel(sb, PanelBodyRect(),
                theme.PanelBackground, theme.PanelBorder, theme.BorderThickness);
        }

        // Children (only visible ones — i.e. active tab content)
        base.Draw(sb, renderer, theme);

        // Handles drawn on top — only visible in UI mode
        if (UiModeActive)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool active  = _isOpen && _activeTab == i;
                bool hovered = _hoveredHandle == i;
                DrawHandle(sb, renderer, theme, HandleRect(i), _tabs[i].Label, active, hovered);
            }
        }
    }

    private void DrawHandle(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Rectangle rect, string label, bool active, bool hovered = false)
    {
        Color back   = active  ? theme.ButtonBackgroundHover
                     : hovered ? theme.ButtonBackgroundHover * 0.6f
                     :           theme.ButtonBackground;
        Color border = active  ? theme.Accent
                     : hovered ? theme.ButtonBorderHover
                     :           theme.ButtonBorder;

        renderer.FillRect(sb, rect, back);
        renderer.DrawRect(sb, rect, border, theme.BorderThickness);

        if (string.IsNullOrEmpty(label)) return;

        var   font  = theme.Font;
        float scale = theme.FontScale * 0.75f;
        Color tcol  = active ? theme.TextHover : theme.TextNormal;

        if (Edge is PanelEdge.Left or PanelEdge.Right)
        {
            float rotation = Edge == PanelEdge.Right ? -MathF.PI * 0.5f : MathF.PI * 0.5f;
            var   center   = new Vector2(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
            FontHelper.DrawRotated(sb, font, label, center, tcol, rotation, scale);
        }
        else
        {
            renderer.DrawTextCentred(sb, label, rect, font, scale, tcol);
        }
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    public override bool HitTest(Point screenPos)
    {
        if (!Visible) return false;
        for (int i = 0; i < _tabs.Count; i++)
            if (HandleRect(i).Contains(screenPos)) return true;
        if (_slideProgress > 0.001 && _activeTab >= 0 && PanelBodyRect().Contains(screenPos))
            return true;
        return false;
    }
}
