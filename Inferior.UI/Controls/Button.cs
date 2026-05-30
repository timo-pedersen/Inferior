using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI.Controls;

/// <summary>
/// Clickable button.
/// Fires Clicked when released inside bounds (not after a drag).
/// Keyboard: Space or Enter fires Clicked when focused.
/// </summary>
public sealed class Button : Control
{
    public string Text { get; set; } = "";

    /// <summary>Fired when the button is clicked or activated via keyboard.</summary>
    public event Action<Button>? Clicked;

    // Track press started inside to avoid firing on drag-release
    private bool _pressedInside;

    public Button() { TabIndex = 1; }

    public Button(string text, Rectangle bounds)
    {
        Text     = text;
        Bounds   = bounds;
        TabIndex = 1;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        bool inside = HitTest(input.MousePosition);

        // Track press start
        if (input.LeftPressed && inside)
        {
            IsPressed      = true;
            _pressedInside = true;
            return true;
        }

        // Release
        if (input.LeftReleased)
        {
            bool wasPressed = IsPressed;
            IsPressed       = false;

            if (wasPressed && _pressedInside && inside)
            {
                _pressedInside = false;
                Clicked?.Invoke(this);
                return true;
            }
            _pressedInside = false;
        }

        // Keyboard activation when focused
        if (IsFocused)
        {
            if (input.IsKeyPressed(Keys.Space) || input.IsKeyPressed(Keys.Enter))
            {
                Clicked?.Invoke(this);
                return true;
            }
        }

        return inside && input.LeftHeld;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        renderer.DrawButton(sb, AbsoluteBounds, Text, theme,
            hovered:  IsHovered,
            pressed:  IsPressed,
            focused:  IsFocused,
            enabled:  Enabled,
            backOverride: BackColor,
            textOverride: TextColor,
            fontOverride:  Font,
            scaleOverride: FontScale);
    }
}
