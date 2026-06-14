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
///   ────────╱  peek strip (tabs+toggle)╲────────  ← CenterTop
///   │ wing │   tab content              │ wing │
///   └──────┴─────────────────────────────────────┘  ← screen bottom
///   ←WingW →                            ←WingW →
///
/// The rail slides down when closed — only the PeekHeight strip (the top of the
/// center bump) remains visible.
///
/// Peek strip layout (left→right):
///   [Tab0][Tab1][Tab2]  [toggle▲/▼]  [Tab3][Tab4][Tab5]
///
/// No separate tab bar — tabs are selected directly from the peek strip.
/// Add content tabs via AddCenterTab(). Access wing content via LeftWing/RightWing.
///
/// Set Bounds = full screen rectangle each frame (or on resize).
/// </summary>
public sealed class CockpitRail : Control
{
    // ── Shape ─────────────────────────────────────────────────────────────────

    public int CenterWidth  { get; set; } = 520;
    public int CenterHeight { get; set; } = 240;
    public int WingHeight   { get; set; } = 160;
    public int RampWidth    { get; set; } = 52;
    public int PeekHeight   { get; set; } = 36;
    public int ContentPad   { get; set; } = 6;

    // ── Wing panels ───────────────────────────────────────────────────────────

    public Panel LeftWing  { get; }
    public Panel RightWing { get; }

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private readonly record struct TabEntry(string Label, Control Content);
    private readonly List<TabEntry> _tabs = [];
    private int _activeTab   = -1;
    private int _hoveredSeg  = -1;  // -1=none, 0-3=tab index, 4=toggle

    public void AddCenterTab(string label, Control content)
    {
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

    private double _slide        = 1.0;   // 0 = closed (only peek), 1 = fully open
    private bool   _isOpen       = true;
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

    private Rectangle PeekRect     => new(CenterLeft, CenterTopY, CenterWidth, PeekHeight);
    private Rectangle TabBodyRect  => new(CenterLeft, CenterTopY + PeekHeight,
                                          CenterWidth, CenterHeight - PeekHeight);
    private Rectangle LeftWingRect  => new(Sx,                                        WingTopY, WingWidth, WingHeight);
    private Rectangle RightWingRect => new(Sx + WingWidth + RampWidth + CenterWidth + RampWidth, WingTopY, WingWidth, WingHeight);

    // Peek strip is divided into 7 equal segments: [Tab0][Tab1][Tab2][Toggle][Tab3][Tab4][Tab5]
    private int PeekSegW => CenterWidth / 7;

    private Rectangle PeekSegRect(int seg) =>
        new(CenterLeft + seg * PeekSegW, CenterTopY, PeekSegW, PeekHeight);

    // Tab index → peek segment (0,1,2 → 0,1,2; 3,4,5 → 4,5,6; toggle is always seg 3)
    private static int TabToSeg(int tabIdx) => tabIdx < 3 ? tabIdx : tabIdx + 1;
    private static int SegToTab(int seg)    => seg    < 3 ? seg    : seg    - 1;

    private Rectangle PeekToggleRect => PeekSegRect(3);

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

        LeftWing.Bounds   = LeftWingRect;
        RightWing.Bounds  = RightWingRect;
        LeftWing.Visible  = contentVisible;
        RightWing.Visible = contentVisible;

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

        _hoveredSeg = -1;

        base.Update(dt);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        var mp = input.MousePosition;

        // Peek strip — always hittable regardless of open state
        if (PeekRect.Contains(mp))
        {
            // Determine which segment is hovered
            for (int seg = 0; seg < 7; seg++)
            {
                if (!PeekSegRect(seg).Contains(mp)) continue;

                if (seg == 3)
                {
                    // Toggle button
                    _hoveredSeg = 6; // special value for toggle (> any tab index)
                    if (input.LeftReleased) { Toggle(); return true; }
                    return input.LeftHeld || input.LeftPressed;
                }
                else
                {
                    int tabIdx = SegToTab(seg);
                    if (tabIdx < _tabs.Count)
                    {
                        _hoveredSeg = tabIdx;
                        if (input.LeftReleased)
                        {
                            SetActiveTab(tabIdx);
                            if (!_isOpen) Open();
                            return true;
                        }
                        return input.LeftHeld || input.LeftPressed;
                    }
                }
            }
            return true;
        }

        if (_slide <= 0.05) return false;

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

        // Left ramp — diagonal fill (cy→wt) + rectangular fill below the diagonal (wt→sh)
        FillRamp(sb, renderer, cx, cx - rw, cy, wt, back);
        renderer.FillRect(sb, new Rectangle(cx - rw, wt, rw, sh - wt), back);

        // Right ramp — diagonal fill (cy→wt) + rectangular fill below the diagonal (wt→sh)
        int rRampX = cx + cw;
        FillRamp(sb, renderer, rRampX, rRampX + rw, cy, wt, back);
        renderer.FillRect(sb, new Rectangle(rRampX, wt, rw, sh - wt), back);

        // ── Border lines ─────────────────────────────────────────────────────

        renderer.DrawLine(sb, V(sx,       wt), V(sx,           sh), border);
        renderer.DrawLine(sb, V(sx,       wt), V(cx - rw,      wt), border);
        renderer.DrawLine(sb, V(cx - rw,  wt), V(cx,           cy), border);
        renderer.DrawLine(sb, V(cx,       cy), V(cx + cw,      cy), border);
        renderer.DrawLine(sb, V(cx + cw,  cy), V(rRampX + rw,  wt), border);
        renderer.DrawLine(sb, V(rRampX + rw, wt), V(sx + sw,   wt), border);
        renderer.DrawLine(sb, V(sx + sw,  wt), V(sx + sw,      sh), border);

        // ── Peek strip: [Tab0][Tab1][Tab2][Toggle][Tab3][Tab4][Tab5] ─────────

        for (int seg = 0; seg < 7; seg++)
        {
            var segRect = PeekSegRect(seg);

            if (seg == 3)
            {
                // Toggle button
                bool hovToggle = _hoveredSeg == 6;
                Color tBack = hovToggle ? theme.ButtonBackgroundHover : theme.ButtonBackground;
                renderer.FillRect(sb, segRect, tBack);
                renderer.DrawRect(sb, segRect, hovToggle ? theme.ButtonBorderHover : border, theme.BorderThickness);

                string toggleLabel = _isOpen ? "v" : "^";
                renderer.DrawTextCentred(sb, toggleLabel, segRect,
                    theme.Font, theme.FontScale * 0.8f,
                    hovToggle ? theme.TextHover : theme.TextDisabled);
            }
            else
            {
                int tabIdx = SegToTab(seg);
                if (tabIdx >= _tabs.Count)
                {
                    renderer.FillRect(sb, segRect, back);
                    renderer.DrawRect(sb, segRect, border, theme.BorderThickness);
                    continue;
                }

                bool active  = tabIdx == _activeTab;
                bool hovered = _hoveredSeg == tabIdx;

                Color tabBack = active  ? theme.ButtonBackgroundHover
                              : hovered ? theme.ButtonBackgroundHover * 0.6f
                              :           theme.ButtonBackground;
                Color tabText = active  ? theme.Accent : theme.TextNormal;
                Color tabBord = active  ? theme.Accent : border;

                renderer.FillRect(sb, segRect, tabBack);
                renderer.DrawRect(sb, segRect, tabBord, theme.BorderThickness);
                renderer.DrawTextCentred(sb, _tabs[tabIdx].Label, segRect,
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
        if (TabBodyRect.Contains(screenPos))    return true;
        int cx = CenterLeft;
        int cw = CenterWidth;
        int rw = RampWidth;
        if (new Rectangle(cx - rw, CenterTopY, rw, WingHeight).Contains(screenPos)) return true;
        if (new Rectangle(cx + cw, CenterTopY, rw, WingHeight).Contains(screenPos)) return true;
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector2 V(int x, int y) => new(x, y);

    private static void FillRamp(SpriteBatch sb, UIRenderer renderer,
        int xNear, int xFar, int yTop, int yBot, Color color)
    {
        int height = yBot - yTop;
        if (height <= 0) return;

        for (int row = 0; row <= height; row++)
        {
            float t    = (float)row / height;
            int   xDiag  = xNear + (int)((xFar - xNear) * t);
            int   xLeft  = Math.Min(xDiag, xNear);
            int   xRight = Math.Max(xDiag, xNear);
            int   width  = xRight - xLeft;
            if (width > 0)
                renderer.FillRect(sb, new Rectangle(xLeft, yTop + row, width, 1), color);
        }
    }
}
