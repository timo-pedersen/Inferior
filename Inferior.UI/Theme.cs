using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI;

/// <summary>
/// Visual theme for the entire UI system.
/// UIManager owns one Theme; controls inherit from it.
/// Per-control color/font overrides use nullable properties — null = use theme.
/// </summary>
public sealed class Theme
{
    // ── Panel / Window ────────────────────────────────────────────────────────
    public Color PanelBackground   { get; set; }
    public Color PanelBorder       { get; set; }
    public Color WindowBackground  { get; set; }
    public Color WindowBorder      { get; set; }
    public Color WindowTitleBar    { get; set; }
    public Color WindowTitleText   { get; set; }
    public Color WindowBorderFocus { get; set; }  // border when window has focus

    // ── Button ────────────────────────────────────────────────────────────────
    public Color ButtonBackground        { get; set; }
    public Color ButtonBackgroundHover   { get; set; }
    public Color ButtonBackgroundPressed { get; set; }
    public Color ButtonBackgroundDisabled{ get; set; }
    public Color ButtonBorder            { get; set; }
    public Color ButtonBorderHover       { get; set; }
    public Color ButtonBorderFocus       { get; set; }

    // ── Text ──────────────────────────────────────────────────────────────────
    public Color TextNormal   { get; set; }
    public Color TextHover    { get; set; }
    public Color TextDisabled { get; set; }
    public Color TextTitle    { get; set; }

    // ── Accent / focus ────────────────────────────────────────────────────────
    public Color Accent       { get; set; }  // focus rings, highlights

    // ── Typography ────────────────────────────────────────────────────────────
    public SpriteFont Font       { get; set; }
    public float      FontScale  { get; set; } = 1.0f;
    public float      SmallScale { get; set; } = 0.8f;
    public float      LargeScale { get; set; } = 1.2f;

    // ── Geometry ─────────────────────────────────────────────────────────────
    public int BorderThickness { get; set; } = 1;
    public int Padding         { get; set; } = 8;
    public int TitleBarHeight  { get; set; } = 28;
    public int CloseButtonSize { get; set; } = 20;

    // ── Constructor ───────────────────────────────────────────────────────────

    // Font must be provided — no default
    public Theme(SpriteFont font) => Font = font;

    // ── Built-in themes ───────────────────────────────────────────────────────

    /// <summary>
    /// Inferior's space game dark theme — consistent with the existing map palette.
    /// </summary>
    public static Theme InferiorDark(SpriteFont font) => new(font)
    {
        // Panels
        PanelBackground  = new Color(10,  15,  30,  210),
        PanelBorder      = new Color(40,  60,  90),

        // Windows
        WindowBackground  = new Color(8,   12,  25,  230),
        WindowBorder      = new Color(40,  60,  90),
        WindowBorderFocus = new Color(60,  100, 150),
        WindowTitleBar    = new Color(15,  25,  50),
        WindowTitleText   = new Color(200, 210, 225),

        // Buttons
        ButtonBackground         = new Color(20,  30,  55),
        ButtonBackgroundHover    = new Color(35,  55,  90),
        ButtonBackgroundPressed  = new Color(15,  25,  45),
        ButtonBackgroundDisabled = new Color(15,  18,  28),
        ButtonBorder             = new Color(40,  60,  90),
        ButtonBorderHover        = new Color(80,  120, 180),
        ButtonBorderFocus        = new Color(80,  160, 220),

        // Text
        TextNormal   = new Color(200, 210, 225),
        TextHover    = new Color(240, 245, 255),
        TextDisabled = new Color(80,  85,  100),
        TextTitle    = new Color(220, 230, 245),

        // Accent
        Accent = new Color(80, 160, 220),

        // Typography
        Font       = font,
        FontScale  = 1.0f,
        SmallScale = 0.8f,
        LargeScale = 1.2f,

        // Geometry
        BorderThickness = 1,
        Padding         = 8,
        TitleBarHeight  = 28,
        CloseButtonSize = 18,
    };

    /// <summary>Clean light theme for testing / menus that want contrast.</summary>
    public static Theme Light(SpriteFont font) => new(font)
    {
        PanelBackground   = new Color(240, 240, 245, 230),
        PanelBorder       = new Color(160, 160, 180),
        WindowBackground  = new Color(230, 232, 240, 240),
        WindowBorder      = new Color(140, 150, 170),
        WindowBorderFocus = new Color(80,  120, 200),
        WindowTitleBar    = new Color(200, 210, 230),
        WindowTitleText   = new Color(30,  35,  50),

        ButtonBackground         = new Color(220, 225, 235),
        ButtonBackgroundHover    = new Color(200, 210, 230),
        ButtonBackgroundPressed  = new Color(180, 190, 215),
        ButtonBackgroundDisabled = new Color(210, 210, 215),
        ButtonBorder             = new Color(160, 170, 190),
        ButtonBorderHover        = new Color(80,  120, 200),
        ButtonBorderFocus        = new Color(60,  100, 190),

        TextNormal   = new Color(30,  35,  50),
        TextHover    = new Color(10,  15,  30),
        TextDisabled = new Color(160, 165, 175),
        TextTitle    = new Color(20,  25,  40),

        Accent = new Color(60, 100, 200),

        Font       = font,
        FontScale  = 1.0f,
        SmallScale = 0.8f,
        LargeScale = 1.2f,

        BorderThickness = 1,
        Padding         = 8,
        TitleBarHeight  = 28,
        CloseButtonSize = 18,
    };
}
