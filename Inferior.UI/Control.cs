using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

/// <summary>
/// Base class for all UI controls.
///
/// Coordinate model:
///   - Bounds are relative to the parent's ContentBounds (or screen if no parent).
///   - AbsoluteBounds is computed by walking up the parent chain.
///   - ContentBounds is where children are placed — base returns AbsoluteBounds,
///     containers (Panel, Window) override to account for borders and title bars.
///
/// Input model:
///   - HandleInput returns true if the input was consumed (stops propagation).
///   - Children are processed first (topmost child last in draw order = first in input).
///   - UIManager routes keyboard input to the focused control.
///
/// Theme model:
///   - Per-control color/font overrides are nullable — null means "use theme value."
///   - Controls resolve effective color via ResolveColor() helpers.
/// </summary>
public abstract class Control
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string Name     { get; set; } = "";
    public int    TabIndex { get; set; } = 0;  // 0 = not focusable via Tab
    public Thickness Margin { get; set; } = Thickness.Zero;
    public OverflowMode Overflow { get; set; } = OverflowMode.Visible;

    // ── Layout ────────────────────────────────────────────────────────────────

    private Rectangle _bounds;

    /// <summary>Bounds relative to parent's ContentBounds, or screen if no parent.</summary>
    public Rectangle Bounds
    {
        get => _bounds;
        set { _bounds = value; OnBoundsChanged(); }
    }

    /// <summary>Absolute screen-space bounds. Computed from parent chain.</summary>
    public Rectangle AbsoluteBounds
    {
        get
        {
            if (Parent == null) return _bounds;
            var origin = Parent.ContentBounds;
            return new Rectangle(
                origin.X + _bounds.X,
                origin.Y + _bounds.Y,
                _bounds.Width,
                _bounds.Height);
        }
    }

    /// <summary>
    /// Area where children are placed.
    /// Base implementation returns AbsoluteBounds.
    /// Containers override to account for borders, title bars, padding.
    /// </summary>
    public virtual Rectangle ContentBounds => AbsoluteBounds;

    public virtual Point DesiredSize => Bounds.Size;

    public Rectangle EffectiveClipBounds
    {
        get
        {
            Rectangle clip = new(0, 0, int.MaxValue / 4, int.MaxValue / 4);
            bool hasClip = false;
            Control? current = this;
            while (current is not null)
            {
                if (current.Overflow == OverflowMode.Clip)
                {
                    clip = hasClip
                        ? Rectangle.Intersect(clip, current.AbsoluteBounds)
                        : current.AbsoluteBounds;
                    hasClip = true;
                }
                current = current.Parent;
            }
            return hasClip ? clip : new Rectangle(0, 0, int.MaxValue / 4, int.MaxValue / 4);
        }
    }

    // ── State ─────────────────────────────────────────────────────────────────
    public bool Visible   { get; set; } = true;
    public bool Enabled   { get; set; } = true;
    public bool IsHovered { get; internal set; }
    /// <summary>
    /// Whether this control has keyboard focus.
    /// Normally managed by UIManager.SetFocus(). The setter is public to support
    /// game states that manage controls directly without a UIManager.
    /// </summary>
    public bool IsFocused { get; set; }
    public bool IsPressed { get; internal set; }

    // ── Theme overrides ───────────────────────────────────────────────────────
    // Null = inherit from theme. Set to override for this control only.
    public Color?      ForeColor  { get; set; }   // border / outline
    public Color?      BackColor  { get; set; }   // background
    public Color?      TextColor  { get; set; }   // text
    public SpriteFont? Font       { get; set; }
    public float?      FontScale  { get; set; }

    // ── Hierarchy ─────────────────────────────────────────────────────────────
    public Control?            Parent   { get; internal set; }
    public IReadOnlyList<Control> Children => _children;
    protected readonly List<Control> _children = new();

    public void Add(Control child)
    {
        if (child.Parent != null)
            child.Parent.Remove(child);
        child.Parent = this;
        _children.Add(child);
    }

    public void Remove(Control child)
    {
        if (_children.Remove(child))
            child.Parent = null;
    }

    public void Clear()
    {
        foreach (var child in _children) child.Parent = null;
        _children.Clear();
    }

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<Control>? MouseEnter;
    public event Action<Control>? MouseLeave;
    public event Action<Control>? GotFocus;
    public event Action<Control>? LostFocus;

    internal void RaiseMouseEnter() => MouseEnter?.Invoke(this);
    internal void RaiseMouseLeave() => MouseLeave?.Invoke(this);
    internal void RaiseGotFocus()   => GotFocus?.Invoke(this);
    internal void RaiseLostFocus()  => LostFocus?.Invoke(this);

    // ── Virtuals ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Process input. Return true to consume (stop further propagation).
    /// Children are processed before this method is called.
    /// When overriding, call base first to process children.
    /// </summary>
    public virtual bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        // Children get input first — iterate in reverse (topmost drawn = first hit)
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HandleInput(input))
                return true;
        }

        return false;
    }

    /// <summary>Draw this control and all children.</summary>
    public virtual void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        DrawChildren(sb, renderer, theme);
    }

    protected void DrawChildren(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (Overflow == OverflowMode.Clip)
        {
            Rectangle clip = EffectiveClipBounds;
            if (clip.Width <= 0 || clip.Height <= 0)
                return;
            renderer.DrawWithClip(sb, clip, () =>
            {
                foreach (var child in _children)
                    child.Draw(sb, renderer, theme);
            });
            return;
        }

        foreach (var child in _children)
            child.Draw(sb, renderer, theme);
    }

    /// <summary>Update animation or time-based state. dt = real seconds.</summary>
    public virtual void Update(double dt)
    {
        if (!Visible) return;
        foreach (var child in _children)
            child.Update(dt);
    }

    /// <summary>Hit test against absolute screen position.</summary>
    public virtual bool HitTest(Point screenPos)
    {
        if (!Visible || !AbsoluteBounds.Contains(screenPos))
            return false;
        Rectangle clip = EffectiveClipBounds;
        return clip.Width > int.MaxValue / 8 || clip.Contains(screenPos);
    }

    /// <summary>
    /// Find the topmost visible control (or self) at screen position.
    /// Returns null if nothing hit.
    /// </summary>
    public virtual Control? FindAt(Point screenPos)
    {
        if (!HitTest(screenPos)) return null;

        // Check children in reverse (last drawn = topmost)
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            var hit = _children[i].FindAt(screenPos);
            if (hit != null) return hit;
        }

        return this;
    }

    /// <summary>Called when Bounds property changes.</summary>
    protected virtual void OnBoundsChanged() { }

    // ── Helpers for subclasses ────────────────────────────────────────────────

    /// <summary>Effective font — own override or theme.</summary>
    protected SpriteFont EffectiveFont(Theme theme) => Font ?? theme.Font;

    /// <summary>Effective font scale — own override or theme.</summary>
    protected float EffectiveFontScale(Theme theme) => FontScale ?? theme.FontScale;

    /// <summary>Effective text color — own override or theme default.</summary>
    protected Color EffectiveTextColor(Theme theme)
        => !Enabled ? theme.TextDisabled
         : TextColor.HasValue ? TextColor.Value
         : IsHovered ? theme.TextHover
         : theme.TextNormal;

    /// <summary>Centred text position within a rectangle.</summary>
    protected static Vector2 CentreText(string text, SpriteFont font, float scale, Rectangle bounds)
    {
        var size = FontHelper.Measure(font, text, scale);
        return new Vector2(
            bounds.X + (bounds.Width  - size.X) * 0.5f,
            bounds.Y + (bounds.Height - size.Y) * 0.5f);
    }

    /// <summary>Left-aligned text position with padding.</summary>
    protected static Vector2 LeftAlignText(string text, SpriteFont font, float scale,
        Rectangle bounds, int padding)
    {
        var size = FontHelper.Measure(font, text, scale);
        return new Vector2(
            bounds.X + padding,
            bounds.Y + (bounds.Height - size.Y) * 0.5f);
    }

    /// <summary>Right-aligned text position with padding.</summary>
    protected static Vector2 RightAlignText(string text, SpriteFont font, float scale,
        Rectangle bounds, int padding)
    {
        var size = FontHelper.Measure(font, text, scale);
        return new Vector2(
            bounds.X + bounds.Width - size.X - padding,
            bounds.Y + (bounds.Height - size.Y) * 0.5f);
    }
}
