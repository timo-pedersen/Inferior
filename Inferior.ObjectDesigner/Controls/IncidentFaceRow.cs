using Inferior.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.ObjectDesigner.Controls;

public sealed class IncidentFaceRow : Control
{
    private bool _pressedInside;
    private bool _isPressed;

    public IncidentFaceRow()
    {
        TabIndex = 1;
        Overflow = OverflowMode.Clip;
    }

    public string FaceId { get; set; } = "";
    public string Metadata { get; set; } = "";
    public bool IsActiveFace { get; set; }

    public event Action<IncidentFaceRow>? Clicked;

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;

        bool inside = HitTest(input.MousePosition);
        if (input.LeftPressed && inside)
        {
            _isPressed = true;
            _pressedInside = true;
            return true;
        }

        if (input.LeftReleased)
        {
            bool wasPressed = _isPressed;
            _isPressed = false;
            if (wasPressed && _pressedInside && inside)
            {
                _pressedInside = false;
                Clicked?.Invoke(this);
                return true;
            }
            _pressedInside = false;
        }

        if (IsFocused && (input.IsKeyPressed(Keys.Space) || input.IsKeyPressed(Keys.Enter)))
        {
            Clicked?.Invoke(this);
            return true;
        }

        return inside && input.LeftHeld;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;

        Rectangle bounds = AbsoluteBounds;
        Color back = _isPressed
            ? theme.ButtonBackgroundPressed
            : IsHovered
                ? theme.ButtonBackgroundHover
                : theme.ButtonBackground;
        Color border = IsActiveFace
            ? theme.Accent
            : IsHovered
                ? theme.ButtonBorderHover
                : theme.ButtonBorder;
        renderer.FillRect(sb, bounds, back);
        renderer.DrawRect(sb, bounds, border, IsActiveFace ? 2 : 1);

        Rectangle clip = Rectangle.Intersect(bounds, EffectiveClipBounds);
        if (clip.Width <= 0 || clip.Height <= 0)
            return;

        renderer.DrawWithClip(sb, clip, () =>
        {
            var font = EffectiveFont(theme);
            float idScale = FontScale ?? 0.58f;
            float metaScale = Math.Max(0.48f, idScale - 0.08f);
            Color idColour = IsActiveFace ? new Color(220, 250, 255) : theme.TextNormal;
            Color metaColour = new Color(150, 176, 184);
            string marker = IsActiveFace ? "[>] " : "[ ] ";
            int textX = bounds.X + 6;
            int maxWidth = Math.Max(12, bounds.Width - 12);
            renderer.DrawText(sb, Ellipsize(marker + FaceId, renderer, font, idScale, maxWidth), new Vector2(textX, bounds.Y + 3), font, idScale, idColour);
            renderer.DrawText(sb, Ellipsize(Metadata, renderer, font, metaScale, maxWidth), new Vector2(textX + 18, bounds.Y + 20), font, metaScale, metaColour);
        });
    }

    public static string BuildMetadata(string role, string materialGroup, int vertexCount)
        => $"{role} / {materialGroup} / {vertexCount} vertices";

    private static string Ellipsize(string text, UIRenderer renderer, SpriteFont font, float scale, int maxWidth)
    {
        if (renderer.MeasureText(text, font, scale).X <= maxWidth)
            return text;

        const string suffix = "...";
        int length = text.Length;
        while (length > 0)
        {
            string candidate = text[..length] + suffix;
            if (renderer.MeasureText(candidate, font, scale).X <= maxWidth)
                return candidate;
            length--;
        }
        return suffix;
    }
}
