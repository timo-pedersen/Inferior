using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

/// <summary>
/// Projects 3D direction vectors onto a 2D sphere display.
///
/// Each named vector appears as a dot inside the circle:
///   • Filled dot — vector is in the front hemisphere (visible ahead)
///   • Hollow dot on rim — vector is in the rear hemisphere (behind you)
///
/// The central + crosshair is the aim reference: fly until a dot reaches
/// centre to align your heading with that vector.
///
/// Orientation must be updated each frame via SetOrientation().
/// Vectors are updated via SetVector() — keyed by name so they can be
/// updated in place without clearing the whole set.
///
/// All directions use MonoGame Vector3. Callers with DVec3 should cast
/// to Vector3 after subtracting any large offset (values near origin are safe).
/// </summary>
public sealed class DirectionBall : Control
{
    // ── Configuration ─────────────────────────────────────────────────────────
    public string Header       { get; set; } = "";
    public bool   ShowLabels   { get; set; } = true;
    public float  CrosshairLen { get; set; } = 1.0f;  // fraction of radius (1.0 = full width)

    // ── Camera orientation — updated each frame ───────────────────────────────
    private Vector3 _forward = -Vector3.UnitZ;
    private Vector3 _right   =  Vector3.UnitX;
    private Vector3 _up      =  Vector3.UnitY;

    // ── Direction vectors ─────────────────────────────────────────────────────
    private readonly Dictionary<string, Entry> _vectors = new();

    private record Entry(Vector3 Direction, Color Color, string Label);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Set ship orientation axes — call once per frame before Draw.</summary>
    public void SetOrientation(Vector3 forward, Vector3 right, Vector3 up)
    {
        _forward = forward;
        _right   = right;
        _up      = up;
    }

    /// <summary>Add or update a named direction vector.</summary>
    public void SetVector(string key, Vector3 direction, Color color, string label = "")
        => _vectors[key] = new Entry(direction, color, label);

    public void RemoveVector(string key) => _vectors.Remove(key);

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        var ab = AbsoluteBounds;

        // Background + border
        renderer.FillRect(sb, ab, BackColor ?? theme.PanelBackground);
        renderer.DrawRect(sb, ab, ForeColor ?? theme.PanelBorder, 1);

        // Optional header
        int headerH = 0;
        if (!string.IsNullOrEmpty(Header))
        {
            renderer.DrawText(sb, Header,
                new Vector2(ab.X + 7, ab.Y + 5),
                theme.Font, theme.SmallScale * 0.88f,
                theme.TextDisabled);
            headerH = 20;
        }

        // Ball geometry
        int   ballSize   = System.Math.Min(ab.Width, ab.Height - headerH) - 16;
        float ballRadius = ballSize * 0.5f;
        var   center     = new Vector2(
            ab.X + ab.Width  * 0.5f,
            ab.Y + headerH + (ab.Height - headerH) * 0.5f);

        // Sphere outline
        var rimColor = ForeColor ?? theme.PanelBorder;
        DrawCircleOutline(sb, renderer, center, ballRadius, rimColor, 48);

        // Crosshair
        float arm        = ballRadius * CrosshairLen;
        var   crossColor = rimColor * 0.4f;
        renderer.DrawLine(sb,
            new Vector2(center.X - arm, center.Y),
            new Vector2(center.X + arm, center.Y),
            crossColor);
        renderer.DrawLine(sb,
            new Vector2(center.X, center.Y - arm),
            new Vector2(center.X, center.Y + arm),
            crossColor);

        // Direction dots
        foreach (var (_, entry) in _vectors)
            DrawEntry(sb, renderer, theme, entry, center, ballRadius);
    }

    private void DrawEntry(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Entry entry, Vector2 center, float ballRadius)
    {
        if (entry.Direction == Vector3.Zero) return;
        var dir = Vector3.Normalize(entry.Direction);

        float rightComp   = Vector3.Dot(dir, _right);
        float upComp      = Vector3.Dot(dir, _up);
        float forwardComp = Vector3.Dot(dir, _forward);

        // 2D projection — right maps to X, up maps to -Y (screen Y is down).
        // For a unit vector: rightComp² + upComp² = 1 − forwardComp²
        // This means the projected magnitude is always ≤ 1, so the dot naturally
        // sits inside the circle for both hemispheres — symmetric around the equator.
        // Front hemisphere: dot moves from centre (straight ahead) to rim (90° off).
        // Rear hemisphere:  same math, filled → hollow to distinguish.
        var     projected = new Vector2(rightComp, -upComp);
        Vector2 dotPos    = center + projected * ballRadius;
        bool    inFront   = forwardComp >= 0f;

        const float DotRadius = 3.5f;

        if (inFront)
        {
            renderer.DrawDot(sb, dotPos, DotRadius, entry.Color);
        }
        else
        {
            // Hollow ring — draw as a small circle outline
            DrawCircleOutline(sb, renderer, dotPos, DotRadius, entry.Color, 10);
        }

        if (ShowLabels && !string.IsNullOrEmpty(entry.Label))
        {
            renderer.DrawText(sb, entry.Label,
                dotPos + new Vector2(DotRadius + 2f, -(theme.Font.LineSpacing * theme.SmallScale * 0.72f * 0.5f)),
                theme.Font, theme.SmallScale * 0.72f,
                entry.Color);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void DrawCircleOutline(SpriteBatch sb, UIRenderer renderer,
        Vector2 center, float radius, Color color, int segments)
    {
        float step = MathF.PI * 2f / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i       * step;
            float a1 = (i + 1) * step;
            renderer.DrawLine(sb,
                center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius,
                center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius,
                color);
        }
    }
}
