using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// A draggable window with a title bar and optional close button.
/// Children are positioned in the content area below the title bar.
///
/// Dragging: left-click and drag on the title bar moves the window.
/// Close: click the X button or press Escape when the window has focus
///        (Escape is handled by UIManager which calls Close()).
/// Focus: UIManager brings window to front and marks it focused on click.
/// </summary>
public sealed class Window : Control
{
    public string Title       { get; set; } = "";
    public bool   ShowClose   { get; set; } = true;
    public bool   Resizable   { get; set; } = false; // future

    /// <summary>Fired when the window is closed via the X button or Escape.</summary>
    public event Action<Window>? Closed;

    // Drag state
    private bool  _dragging;
    private Point _dragOffset; // offset from window top-left to mouse at drag start

    // Close button hover
    private bool _closeHovered;

    public Window() { TabIndex = 0; } // windows themselves aren't tab-focused

    public Window(string title, Rectangle bounds)
    {
        Title  = title;
        Bounds = bounds;
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    public void Close()
    {
        Visible = false;
        Closed?.Invoke(this);
    }

    public void Show()
    {
        Visible = true;
    }

    public void Toggle()
    {
        if (Visible) Close();
        else         Show();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    /// <summary>Title bar in absolute screen coordinates.</summary>
    private Rectangle TitleBarBounds
    {
        get
        {
            var ab = AbsoluteBounds;
            return new Rectangle(ab.X, ab.Y, ab.Width, Theme_TitleBarHeight);
        }
    }

    /// <summary>Close button in absolute screen coordinates.</summary>
    private Rectangle CloseButtonBounds
    {
        get
        {
            var tb   = TitleBarBounds;
            int size = Theme_CloseButtonSize;
            int pad  = (Theme_TitleBarHeight - size) / 2;
            return new Rectangle(tb.Right - size - pad, tb.Y + pad, size, size);
        }
    }

    // Theme values cached from last draw — windows don't carry a Theme reference.
    // They're updated on Draw before input is processed next frame.
    // Good enough for a single-threaded game loop.
    private int Theme_TitleBarHeight  = 28;
    private int Theme_CloseButtonSize = 18;

    public override Rectangle ContentBounds
    {
        get
        {
            var ab = AbsoluteBounds;
            int tb = Theme_TitleBarHeight;
            int bw = 1;
            return new Rectangle(
                ab.X + bw,
                ab.Y + tb,
                ab.Width  - bw * 2,
                ab.Height - tb - bw);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        var mousePos   = input.MousePosition;
        var titleBar   = TitleBarBounds;
        var closeBtn   = CloseButtonBounds;
        bool inWindow  = HitTest(mousePos);
        bool inTitle   = titleBar.Contains(mousePos);
        bool inClose   = ShowClose && closeBtn.Contains(mousePos);

        _closeHovered = inClose;

        // Close button click
        if (inClose && input.LeftReleased && !_dragging)
        {
            Close();
            return true;
        }

        // Drag start — on title bar, not on close button
        if (inTitle && !inClose && input.LeftPressed)
        {
            _dragging  = true;
            _dragOffset = new Point(
                mousePos.X - AbsoluteBounds.X,
                mousePos.Y - AbsoluteBounds.Y);
            return true;
        }

        // Dragging
        if (_dragging)
        {
            if (input.LeftHeld)
            {
                Bounds = new Rectangle(
                    mousePos.X - _dragOffset.X,
                    mousePos.Y - _dragOffset.Y,
                    Bounds.Width,
                    Bounds.Height);
                return true;
            }
            else
            {
                _dragging = false;
            }
        }

        // Pass to children if mouse is inside content area
        if (inWindow)
        {
            // Children get input first
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i].HandleInput(input))
                    return true;
            }
            return true; // window absorbs all input inside its bounds
        }

        return false;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        // Cache theme geometry for use in ContentBounds / input
        Theme_TitleBarHeight  = theme.TitleBarHeight;
        Theme_CloseButtonSize = theme.CloseButtonSize;

        renderer.DrawWindow(sb, AbsoluteBounds, Title, theme, IsFocused);

        if (ShowClose)
            renderer.DrawCloseButton(sb, CloseButtonBounds, _closeHovered, theme);

        // Children drawn in content area
        foreach (var child in Children)
            child.Draw(sb, renderer, theme);
    }
}
