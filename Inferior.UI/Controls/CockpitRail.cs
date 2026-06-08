using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Bottom cockpit rail — a shaped instrument panel anchored to the screen bottom.
///
/// Cross-section (fully open):
///
///           ←RampW→←──CenterWidth──→←RampW→
///            ╱──────────────────────╲
///   ────────╱  peek strip + tabs     ╲────────  ← CenterTop
///   │ wing │   tab content            │ wing │
///   └──────┴───────────────────────────────────┘  ← screen bottom
///   ←WingW →                          ←WingW →
///
/// The rail slides down when closed — only the PeekHeight strip (the top of the
/// center bump) remains visible. A toggle button in the peek strip opens/closes.
///
/// Add content tabs via AddCenterTab(). Access wing content via LeftWing/RightWing.
///
/// Set Bounds = full screen rectangle each frame (or on resize).
/// </summary>
public sealed class CockpitRail : Control
{
    // ── Shape ─────────────────────────────────────────────────────────────────

    public int CenterWidth  { get; set; } = 520;
    public int CenterHeight { get; set; } = 160;
    public int WingHeight   { get; set; } = 100;
    public int RampWidth    { get; set; } = 52;
    public int PeekHeight   { get; set; } = 28;
    public int TabBarHeight { get; set; } = 24;
    public int ContentPad   { get; set; } = 6;

    // ── Wing panels ───────────────────────────────────────────────────────────

    public Panel LeftWing  { get; }
    public Panel RightWing { get; }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private readonly record struct TabEntry(string Label, Control Content);
    private readonly List<TabEntry> _tabs = [];
    private int _activeTab    = -1;
    private int _hoveredTab   = -1;
    private bool _hoveredPeek = false;

    public void AddCenterTab(string label, Control content)
    {
        int idx = _tabs.Count;
        content.Visible = false;
        if (_activeTab < 0)
        {
            _activeTab = 0;
            content.Visible = true;
        }
        _tabs.Add(new TabEntry(label, content));
        Add(content);
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    private double _slide        = 0.0;   // 0 = closed (only peek), 1 = fully open
    private bool   _isOpen       = false;
    private const double SlideDuration = 0.2;

    public bool IsOpen => _isOpen;

    public void Toggle()  => _isOpen = !_isOpen;
    public void Open()    => _isOpen = true;
    public void Close()   => _isOpen = false;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CockpitRail()
    {
        LeftWing  = new Panel { DrawBackground = false, DrawBorder = false };
        RightWing = new Panel { DrawBackground = false, DrawBorder = false };
        Add(LeftWing);
        Add(RightWing);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private int Sw => AbsoluteBounds.Width;
    private int Sh => AbsoluteBounds.Bottom;
    private int Sx => AbsoluteBounds.Left;

    private int WingWidth => Math.Max(0, (Sw - CenterWidth - 2 * RampWidth) / 2);

    private int SlideOffset => (int)((CenterHeight - PeekHeight) * (1.0 - EaseOut(_slide)));

    private int CenterLeft => Sx + WingWidth + RampWidth;
    private int CenterTopY => Sh - CenterHeight + SlideOffset;
    private int WingTopY   => Sh - WingHeight   + SlideOffset;

    private Rectangle PeekRect     => new(CenterLeft,          CenterTopY,                   CenterWidth,   PeekHeight);
    private Rectangle TabBarRect   => new(CenterLeft,          CenterTopY + PeekHeight,      CenterWidth,   TabBarHeight);
    private Rectangle TabBodyRect  => new(CenterLeft,          CenterTopY + PeekHeight + TabBarHeight,
                                          CenterWidth, CenterHeight - PeekHeight - TabBarHeight);
    private Rectangle LeftWingRect  => new(Sx,                                        WingTopY, WingWidth, WingHeight);
    private Rectangle RightWingRect => new(Sx + WingWidth + RampWidth + CenterWidth + RampWidth, WingTopY, WingWidth, WingHeight);

    private Rectangle TabRect(int i)
    {
        if (_tabs.Count == 0) return Rectangle.Empty;
        int w = CenterWidth / _tabs.Count;
        return new Rectangle(CenterLeft + i * w, CenterTopY + PeekHeight, w, TabBarHeight);
    }

    private static float EaseOut(double t)
        => 1f - (float)Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), 3.0);

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(double dt)
    {
        double target = _isOpen ? 1.0 : 0.0;
        _slide = target > _slide
            ? Math.Min(target, _slide + dt / SlideDuration)
            : Math.Max(target, _slide - dt / SlideDuration);

        bool contentVisible = _slide > 0.05;

        // Wing bounds (relative to CockpitRail.ContentBounds which starts at screen origin)
        LeftWing.Bounds   = LeftWingRect;
        RightWing.Bounds  = RightWingRect;
        LeftWing.Visible  = contentVisible;
        RightWing.Visible = contentVisible;

        // Tab content bounds
        var tb = TabBodyRect;
        var tabContent = new Rectangle(
            tb.X + ContentPad, tb.Y + ContentPad,
            Math.Max(0, tb.Width  - ContentPad * 2),
            Math.Max(0, tb.Height - ContentPad * 2));

        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Content.Visible = contentVisible && (i == _activeTab);
            if (i == _activeTab)
                _tabs[i].Content.Bounds = tabContent;
        }

        // Reset hover flags — refreshed each frame by HandleInput
        _hoveredTab  = -1;
        _hoveredPeek = false;

        base.Update(dt);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        var mp = input.MousePosition;

        // Peek strip — toggle button, always hittable
        if (PeekRect.Contains(mp))
        {
            _hoveredPeek = true;
            if (input.LeftReleased)
            {
                Toggle();
                return true;
            }
            return input.LeftHeld || input.LeftPressed;
        }

        if (_slide <= 0.05) return false;

        // Tab bar
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!TabRect(i).Contains(mp)) continue;
            _hoveredTab = i;
            if (input.LeftReleased) { SetActiveTab(i); return true; }
            return input.LeftHeld || input.LeftPressed;
        }

        // Tab body — forward to active tab content
        if (TabBodyRect.Contains(mp) && _activeTab >= 0)
        {
            for (int i = _children.Count - 1; i >= 0; i--)
                _children[i].HandleInput(input);
            return true;
        }

        // Wings
        if (LeftWingRect.Contains(mp) || RightWingRect.Contains(mp))
        {
            for (int i = _children.Count - 1; i >= 0; i--)
                _children[i].HandleInput(input);
            return true;
        }

        return false;
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

        var back   = theme.PanelBackground;
        var border = theme.PanelBorder;

        int cx  = CenterLeft;
        int cy  = CenterTopY;
        int wt  = WingTopY;
        int rw  = RampWidth;
        int cw  = CenterWidth;
        int sh  = Sh;
        int sx  = Sx;
        int sw  = Sw;
        int ww  = WingWidth;

        // ── Background fills ─────────────────────────────────────────────────

        renderer.FillRect(sb, LeftWingRect,  back);
        renderer.FillRect(sb, RightWingRect, back);
        renderer.FillRect(sb, new Rectangle(cx, cy, cw, CenterHeight), back);

        // Left ramp: triangle from (cx, cy) sweeping left to (cx-rw, wt)
        FillRamp(sb, renderer, cx, cx - rw, cy, wt, back);

        // Right ramp: mirror
        int rRampX = cx + cw;
        FillRamp(sb, renderer, rRampX, rRampX + rw, cy, wt, back);

        // ── Border lines ─────────────────────────────────────────────────────

        renderer.DrawLine(sb, V(sx,       wt), V(sx,           sh), border);       // left edge
        renderer.DrawLine(sb, V(sx,       wt), V(cx - rw,      wt), border);       // left wing top
        renderer.DrawLine(sb, V(cx - rw,  wt), V(cx,           cy), border);       // left ramp diagonal
        renderer.DrawLine(sb, V(cx,       cy), V(cx + cw,      cy), border);       // center top
        renderer.DrawLine(sb, V(cx + cw,  cy), V(rRampX + rw,  wt), border);       // right ramp diagonal
        renderer.DrawLine(sb, V(rRampX + rw, wt), V(sx + sw,   wt), border);       // right wing top
        renderer.DrawLine(sb, V(sx + sw,  wt), V(sx + sw,      sh), border);       // right edge

        // ── Peek strip (toggle button) ────────────────────────────────────────

        var peek = PeekRect;
        Color peekBack = _hoveredPeek
            ? theme.ButtonBackgroundHover
            : theme.ButtonBackground;
        renderer.FillRect(sb, peek, peekBack);
        renderer.DrawRect(sb, peek, _hoveredPeek ? theme.ButtonBorderHover : border, theme.BorderThickness);

        string toggleLabel = _isOpen ? "v" : "^";
        renderer.DrawTextCentred(sb, toggleLabel, peek,
            theme.Font, theme.FontScale * 0.8f,
            _hoveredPeek ? theme.TextHover : theme.TextDisabled);

        // ── Tab bar ───────────────────────────────────────────────────────────

        if (_slide > 0.01 && _tabs.Count > 0)
        {
            renderer.FillRect(sb, TabBarRect, back);
            renderer.DrawLine(sb,
                V(cx, cy + PeekHeight),
                V(cx + cw, cy + PeekHeight),
                border);  // line between peek and tabs

            for (int i = 0; i < _tabs.Count; i++)
            {
                var tr     = TabRect(i);
                bool active  = i == _activeTab;
                bool hovered = i == _hoveredTab;

                Color tabBack = active  ? theme.ButtonBackgroundHover
                              : hovered ? theme.ButtonBackgroundHover * 0.6f
                              :           theme.ButtonBackground;
                Color tabText = active  ? theme.Accent : theme.TextNormal;

                renderer.FillRect(sb, tr, tabBack);
                renderer.DrawRect(sb, tr, active ? theme.Accent : border, theme.BorderThickness);
                renderer.DrawTextCentred(sb, _tabs[i].Label, tr,
                    theme.Font, theme.FontScale * 0.8f, tabText);
            }
        }

        // ── Children (wing panels + tab content) ──────────────────────────────

        base.Draw(sb, renderer, theme);
    }

    // ── HitTest ───────────────────────────────────────────────────────────────

    public override bool HitTest(Point screenPos)
    {
        if (!Visible) return false;
        if (PeekRect.Contains(screenPos)) return true;
        if (_slide <= 0.01) return false;
        if (LeftWingRect.Contains(screenPos))   return true;
        if (RightWingRect.Contains(screenPos))  return true;
        if (TabBarRect.Contains(screenPos))     return true;
        if (TabBodyRect.Contains(screenPos))    return true;
        // Ramp bounding boxes (generous hit area)
        int cx = CenterLeft;
        int cw = CenterWidth;
        int rw = RampWidth;
        if (new Rectangle(cx - rw, CenterTopY, rw, WingHeight).Contains(screenPos)) return true;
        if (new Rectangle(cx + cw, CenterTopY, rw, WingHeight).Contains(screenPos)) return true;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector2 V(int x, int y) => new(x, y);

    /// <summary>
    /// Scan-line fills the triangle formed between the diagonal edge and the vertical
    /// edge at xNear. The diagonal runs from (xNear, yTop) to (xFar, yBot).
    /// </summary>
    private static void FillRamp(SpriteBatch sb, UIRenderer renderer,
        int xNear, int xFar, int yTop, int yBot, Color color)
    {
        int height = yBot - yTop;
        if (height <= 0) return;

        for (int row = 0; row <= height; row++)
        {
            float t    = (float)row / height;
            int   xDiag = xNear + (int)((xFar - xNear) * t);
            int   xLeft  = Math.Min(xDiag, xNear);
            int   xRight = Math.Max(xDiag, xNear);
            int   width  = xRight - xLeft;
            if (width > 0)
                renderer.FillRect(sb, new Rectangle(xLeft, yTop + row, width, 1), color);
        }
    }
}
