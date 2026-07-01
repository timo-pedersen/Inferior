using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI.Controls;

/// <summary>
/// Full-featured text input control.
///
/// Features:
///   - Blinking cursor (vertical bar between characters)
///   - Click to place cursor; click-drag to select
///   - Left/Right/Home/End arrow key navigation
///   - Shift+arrow, Shift+Home/End for selection
///   - Ctrl+A (select all), Ctrl+C/X/V (copy/cut/paste — game-internal clipboard)
///   - Backspace (delete backwards), Delete (delete forwards)
///   - Multiline mode: Enter inserts newline, Up/Down moves between lines
///   - Multiline: vertical scrollbar and mouse-wheel scrolling
///   - MaxLength enforcement
///   - Placeholder text when empty
///   - TextChanged event fires on every character change
///   - Submitted event fires on Enter (single-line) or Ctrl+Enter (multiline)
///
/// Font/scale are resolved from the control overrides then the theme, so each TextBox
/// can have an independent appearance.
/// </summary>
public sealed class TextBox : Control
{
    // ── Configuration ─────────────────────────────────────────────────────────
    public bool   Multiline   { get; set; }
    public int    MaxLength   { get; set; } = int.MaxValue;
    public string Placeholder { get; set; } = "";

    /// <summary>
    /// Applied to the full text whenever a word boundary is typed (space, newline, punctuation).
    /// Set to null to disable filtering. Defaults to <see cref="TextFilters.NastyWordFilter"/>.
    /// </summary>
    public Func<string, string>? TextFilter { get; set; } = TextFilters.NastyWordFilter;

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired whenever the text content changes. Arg = new full text.</summary>
    public event Action<TextBox, string>? TextChanged;
    /// <summary>Fired on Enter (single-line) or Ctrl+Enter (multiline).</summary>
    public event Action<TextBox>? Submitted;

    // ── Text state ────────────────────────────────────────────────────────────
    private readonly StringBuilder _text    = new();
    private int   _cursor   = 0;   // caret position in _text (0 = before first char)
    private int   _selAnchor = -1; // selection anchor; -1 = no selection

    // ── Scroll ────────────────────────────────────────────────────────────────
    private int _scrollX = 0;  // pixels scrolled right (single-line)
    private int _scrollY = 0;  // pixels scrolled down (multiline)

    // ── Cached font from last Draw — used for cursor placement on click ────────
    private SpriteFont? _cachedFont;
    private float       _cachedScale;

    // ── Cursor blink ──────────────────────────────────────────────────────────
    private double _blinkTimer = 0.0;
    private bool   _blinkState = true;
    private const double BlinkInterval = 0.53;

    // ── Guard against double-processing TypedChars per frame ──────────────────
    private bool _charsProcessed;

    // ── Internal clipboard (game-scoped) ──────────────────────────────────────
    private static string _clipboard = "";

    // ── Scrollbar geometry ────────────────────────────────────────────────────
    private const int ScrollbarWidth = 8;

    public TextBox() { TabIndex = 1; }

    // ── Text API ──────────────────────────────────────────────────────────────

    public string Text
    {
        get => _text.ToString();
        set
        {
            _text.Clear();
            _text.Append(value ?? "");
            _cursor    = Math.Clamp(_cursor, 0, _text.Length);
            _selAnchor = -1;
            TextChanged?.Invoke(this, Text);
        }
    }

    // ── Update (animation) ───────────────────────────────────────────────────

    public override void Update(double dt)
    {
        _charsProcessed = false;

        if (IsFocused)
        {
            _blinkTimer += dt;
            if (_blinkTimer >= BlinkInterval)
            {
                _blinkTimer -= BlinkInterval;
                _blinkState = !_blinkState;
            }
        }
        else
        {
            _blinkState = true;
            _blinkTimer = 0;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        bool consumed = false;

        // Mouse — click to place cursor / drag to select
        if (input.LeftPressed && HitTest(input.MousePosition))
        {
            if (_cachedFont != null)
                PlaceCursorAt(input.MousePosition, _cachedFont, _cachedScale, input.Shift);
            else
            {
                _selAnchor = input.Shift ? _selAnchor : -1;
                ResetBlink();
            }
            consumed = true;
        }
        else if (input.LeftHeld && HitTest(input.MousePosition) && _cachedFont != null)
        {
            // Drag extends selection
            if (_selAnchor < 0) _selAnchor = _cursor;
            PlaceCursorAt(input.MousePosition, _cachedFont, _cachedScale, true);
            consumed = true;
        }

        // Scroll wheel (multiline)
        if (Multiline && input.ScrollDelta != 0 && HitTest(input.MousePosition))
        {
            _scrollY = Math.Max(0, _scrollY - input.ScrollDelta / 3);
            consumed = true;
        }

        // Keyboard (only when focused)
        if (!IsFocused) return consumed;

        // Typed characters
        if (!_charsProcessed && input.TypedChars.Count > 0)
        {
            _charsProcessed = true;
            foreach (var c in input.TypedChars)
                InsertChar(c);
            consumed = true;
        }

        // Control shortcuts
        if (input.Ctrl)
        {
            if (input.IsKeyPressed(Keys.A)) { SelectAll(); return true; }
            if (input.IsKeyPressed(Keys.C)) { CopySelection(); return true; }
            if (input.IsKeyPressed(Keys.X)) { CutSelection(); return true; }
            if (input.IsKeyPressed(Keys.V)) { Paste(); return true; }
        }

        // Enter
        if (input.IsKeyPressed(Keys.Enter))
        {
            if (Multiline && !input.Ctrl) { InsertChar('\n'); return true; }
            Submitted?.Invoke(this);
            return true;
        }

        // Navigation
        if (input.IsKeyPressed(Keys.Left))
        {
            MoveCursor(-1, input.Shift, input.Ctrl);
            return true;
        }
        if (input.IsKeyPressed(Keys.Right))
        {
            MoveCursor(+1, input.Shift, input.Ctrl);
            return true;
        }
        if (input.IsKeyPressed(Keys.Home))
        {
            MoveToLineStart(input.Shift);
            return true;
        }
        if (input.IsKeyPressed(Keys.End))
        {
            MoveToLineEnd(input.Shift);
            return true;
        }
        if (Multiline && input.IsKeyPressed(Keys.Up))
        {
            MoveCursorLine(-1, input.Shift);
            return true;
        }
        if (Multiline && input.IsKeyPressed(Keys.Down))
        {
            MoveCursorLine(+1, input.Shift);
            return true;
        }

        // Backspace / Delete
        if (input.IsKeyPressed(Keys.Back))  { DeleteBack();    return true; }
        if (input.IsKeyPressed(Keys.Delete)) { DeleteForward(); return true; }

        return consumed;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        var abs   = AbsoluteBounds;
        var font  = EffectiveFont(theme);
        float scale = EffectiveFontScale(theme);
        _cachedFont  = font;
        _cachedScale = scale;
        int pad   = theme.Padding;

        // Background + border
        renderer.DrawTextBoxFrame(sb, abs, theme, IsFocused, Enabled);

        int sbW      = Multiline ? ScrollbarWidth + 2 : 0;
        var textArea = new Rectangle(abs.X + pad, abs.Y + pad,
                                     abs.Width - pad * 2 - sbW,
                                     abs.Height - pad * 2);
        if (textArea.Width <= 0 || textArea.Height <= 0) return;

        renderer.DrawWithClip(sb, textArea, () =>
        {
            string text = _text.ToString();

            if (text.Length == 0 && Placeholder.Length > 0 && !IsFocused)
            {
                renderer.DrawText(sb, Placeholder,
                    new Vector2(textArea.X, textArea.Y),
                    font, scale, theme.TextBoxPlaceholder);
                return;
            }

            float lineH = font.MeasureString("A").Y * scale;

            if (Multiline)
                DrawMultiline(sb, renderer, theme, textArea, font, scale, lineH);
            else
                DrawSingleLine(sb, renderer, theme, textArea, font, scale, lineH);
        });

        // Scrollbar (multiline)
        if (Multiline)
            DrawScrollbar(sb, renderer, theme, abs, sbW);
    }

    // ── Draw helpers ──────────────────────────────────────────────────────────

    private void DrawSingleLine(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Rectangle textArea, SpriteFont font, float scale, float lineH)
    {
        string text = _text.ToString();

        // Ensure cursor is visible (scroll to keep cursor in view)
        float cursorX = MeasureWidth(text[.._cursor], font, scale);
        if (cursorX - _scrollX > textArea.Width - 4)
            _scrollX = (int)(cursorX - textArea.Width + 4);
        else if (cursorX < _scrollX)
            _scrollX = Math.Max(0, (int)cursorX - 4);

        float textY = textArea.Y + (textArea.Height - lineH) * 0.5f;

        // Selection highlight
        if (_selAnchor >= 0 && _selAnchor != _cursor)
        {
            int s = Math.Min(_selAnchor, _cursor);
            int e = Math.Max(_selAnchor, _cursor);
            float sx = MeasureWidth(text[..s], font, scale) - _scrollX + textArea.X;
            float ex = MeasureWidth(text[..e], font, scale) - _scrollX + textArea.X;
            renderer.DrawTextBoxSelection(sb,
                new Rectangle((int)sx, textArea.Y, (int)(ex - sx), textArea.Height), theme);
        }

        // Text
        renderer.DrawText(sb, text,
            new Vector2(textArea.X - _scrollX, textY), font, scale,
            Enabled ? EffectiveTextColor(theme) : theme.TextDisabled);

        // Cursor
        if (IsFocused && _blinkState)
        {
            float cx = textArea.X + cursorX - _scrollX;
            renderer.DrawTextBoxCursor(sb, (int)cx, textArea.Y, textArea.Height, theme);
        }
    }

    private void DrawMultiline(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Rectangle textArea, SpriteFont font, float scale, float lineH)
    {
        string   text  = _text.ToString();
        string[] lines = text.Split('\n');

        int totalH     = (int)(lines.Length * lineH);
        int maxScrollY = Math.Max(0, totalH - textArea.Height);
        _scrollY       = Math.Clamp(_scrollY, 0, maxScrollY);

        int cursorLine = GetLineIndex(_cursor, lines);
        int cursorCol  = _cursor - LineStartIndex(cursorLine, lines);
        float cursorAbsY = cursorLine * lineH - _scrollY + textArea.Y;

        // Scroll cursor into view
        if (cursorAbsY < textArea.Y)
            _scrollY = Math.Max(0, (int)(cursorLine * lineH));
        else if (cursorAbsY + lineH > textArea.Bottom)
            _scrollY = (int)(cursorLine * lineH - textArea.Height + lineH);

        var (selS, selE) = SelectionRange();
        int selOffset = 0;

        for (int li = 0; li < lines.Length; li++)
        {
            float y = textArea.Y + li * lineH - _scrollY;
            if (y + lineH < textArea.Y) { selOffset += lines[li].Length + 1; continue; }
            if (y > textArea.Bottom)    { selOffset += lines[li].Length + 1; break; }

            // Selection on this line
            if (selS >= 0 && selS != selE)
            {
                int lineStart = selOffset;
                int lineEnd   = selOffset + lines[li].Length;
                int overlapS  = Math.Max(selS, lineStart) - lineStart;
                int overlapE  = Math.Min(selE, lineEnd)   - lineStart;
                if (overlapS < overlapE)
                {
                    float sx = MeasureWidth(lines[li][..overlapS], font, scale) + textArea.X;
                    float ex = MeasureWidth(lines[li][..overlapE], font, scale) + textArea.X;
                    renderer.DrawTextBoxSelection(sb,
                        new Rectangle((int)sx, (int)y, (int)(ex - sx), (int)lineH), theme);
                }
            }

            renderer.DrawText(sb, lines[li], new Vector2(textArea.X, y), font, scale,
                Enabled ? EffectiveTextColor(theme) : theme.TextDisabled);

            selOffset += lines[li].Length + 1;
        }

        // Cursor
        if (IsFocused && _blinkState)
        {
            string cursorLineText = lines[cursorLine][..Math.Min(cursorCol, lines[cursorLine].Length)];
            float cx = textArea.X + MeasureWidth(cursorLineText, font, scale);
            float cy = textArea.Y + cursorLine * lineH - _scrollY;
            renderer.DrawTextBoxCursor(sb, (int)cx, (int)cy, (int)lineH, theme);
        }
    }

    private void DrawScrollbar(SpriteBatch sb, UIRenderer renderer, Theme theme,
        Rectangle abs, int sbW)
    {
        if (sbW <= 0) return;

        string[] lines = _text.ToString().Split('\n');
        var font = EffectiveFont(theme);
        float scale = EffectiveFontScale(theme);
        float lineH = font.MeasureString("A").Y * scale;
        int pad     = theme.Padding;

        int totalH   = (int)(lines.Length * lineH);
        int viewH    = abs.Height - pad * 2;
        if (totalH <= viewH) return;

        var track = new Rectangle(abs.Right - sbW - pad / 2, abs.Y + pad, sbW - 2, viewH);

        float thumbFrac   = (float)viewH    / totalH;
        float thumbOffset = (float)_scrollY / totalH;

        int thumbH   = Math.Max(16, (int)(track.Height * thumbFrac));
        int thumbTop = track.Y + (int)(track.Height * thumbOffset);

        renderer.DrawTextBoxScrollbar(sb, track,
            new Rectangle(track.X, thumbTop, track.Width, thumbH), theme);
    }

    // ── Editing helpers ───────────────────────────────────────────────────────

    private void InsertChar(char c)
    {
        if (!Multiline && c == '\n') return;

        DeleteSelection();  // replace selection if any

        if (_text.Length >= MaxLength) return;

        _text.Insert(_cursor, c);
        _cursor++;
        _selAnchor = -1;
        ResetBlink();

        if (TextFilter != null && (char.IsWhiteSpace(c) || char.IsPunctuation(c)))
            ApplyTextFilter();

        TextChanged?.Invoke(this, Text);
    }

    private void ApplyTextFilter()
    {
        if (TextFilter == null) return;
        string current  = _text.ToString();
        string filtered = TextFilter(current);
        if (filtered == current) return;
        int delta = filtered.Length - current.Length;
        _text.Clear();
        _text.Append(filtered);
        _cursor = Math.Clamp(_cursor + delta, 0, _text.Length);
    }

    private void DeleteBack()
    {
        if (DeleteSelection()) return;
        if (_cursor <= 0) return;
        _cursor--;
        _text.Remove(_cursor, 1);
        TextChanged?.Invoke(this, Text);
    }

    private void DeleteForward()
    {
        if (DeleteSelection()) return;
        if (_cursor >= _text.Length) return;
        _text.Remove(_cursor, 1);
        TextChanged?.Invoke(this, Text);
    }

    private bool DeleteSelection()
    {
        if (_selAnchor < 0 || _selAnchor == _cursor) return false;
        var (s, e) = SelectionRange();
        _text.Remove(s, e - s);
        _cursor    = s;
        _selAnchor = -1;
        TextChanged?.Invoke(this, Text);
        return true;
    }

    private void SelectAll()
    {
        _selAnchor = 0;
        _cursor    = _text.Length;
    }

    private void CopySelection()
    {
        var (s, e) = SelectionRange();
        if (s < e)
            _clipboard = _text.ToString(s, e - s);
    }

    private void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    private void Paste()
    {
        if (_clipboard.Length == 0) return;
        DeleteSelection();
        foreach (char c in _clipboard)
            InsertChar(c);
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    private void MoveCursor(int dir, bool shift, bool ctrl)
    {
        if (!shift && _selAnchor >= 0)
        {
            // If selection exists and no shift, collapse to selection edge
            var (s, e) = SelectionRange();
            _cursor    = dir < 0 ? s : e;
            _selAnchor = -1;
            return;
        }

        if (shift && _selAnchor < 0)
            _selAnchor = _cursor;
        else if (!shift)
            _selAnchor = -1;

        if (ctrl)
        {
            // Word jump
            _cursor = dir < 0
                ? FindWordBoundaryLeft(_cursor)
                : FindWordBoundaryRight(_cursor);
        }
        else
        {
            _cursor = Math.Clamp(_cursor + dir, 0, _text.Length);
        }

        ResetBlink();
    }

    private void MoveToLineStart(bool shift)
    {
        if (shift && _selAnchor < 0) _selAnchor = _cursor;
        else if (!shift)              _selAnchor = -1;

        if (Multiline)
        {
            string[] lines = _text.ToString().Split('\n');
            int li    = GetLineIndex(_cursor, lines);
            _cursor   = LineStartIndex(li, lines);
        }
        else
        {
            _cursor = 0;
        }
        ResetBlink();
    }

    private void MoveToLineEnd(bool shift)
    {
        if (shift && _selAnchor < 0) _selAnchor = _cursor;
        else if (!shift)              _selAnchor = -1;

        if (Multiline)
        {
            string[] lines = _text.ToString().Split('\n');
            int li    = GetLineIndex(_cursor, lines);
            _cursor   = LineStartIndex(li, lines) + lines[li].Length;
        }
        else
        {
            _cursor = _text.Length;
        }
        ResetBlink();
    }

    private void MoveCursorLine(int dir, bool shift)
    {
        if (shift && _selAnchor < 0) _selAnchor = _cursor;
        else if (!shift)              _selAnchor = -1;

        string[] lines = _text.ToString().Split('\n');
        int li  = GetLineIndex(_cursor, lines);
        int col = _cursor - LineStartIndex(li, lines);
        int newLi = Math.Clamp(li + dir, 0, lines.Length - 1);
        int newCol = Math.Min(col, lines[newLi].Length);
        _cursor = LineStartIndex(newLi, lines) + newCol;
        ResetBlink();
    }

    // ── Public cursor/selection placement ────────────────────────────────────

    /// <summary>
    /// Place the cursor at the character nearest to screenPos.
    /// Requires the theme (for font) to be passed.
    /// </summary>
    public void PlaceCursorAt(Point screenPos, SpriteFont font, float scale, bool extend)
    {
        var abs   = AbsoluteBounds;
        int pad   = 8;  // match theme.Padding default
        int sbW   = Multiline ? ScrollbarWidth + 2 : 0;
        var textArea = new Rectangle(abs.X + pad, abs.Y + pad,
                                     abs.Width - pad * 2 - sbW,
                                     abs.Height - pad * 2);

        if (extend && _selAnchor < 0)
            _selAnchor = _cursor;
        else if (!extend)
            _selAnchor = -1;

        string text = _text.ToString();
        float lineH = font.MeasureString("A").Y * scale;

        if (!Multiline)
        {
            float relX = screenPos.X - textArea.X + _scrollX;
            _cursor = FindCharIndexAtX(text, relX, font, scale);
        }
        else
        {
            string[] lines = text.Split('\n');
            float relY = screenPos.Y - textArea.Y + _scrollY;
            int li = Math.Clamp((int)(relY / lineH), 0, lines.Length - 1);
            float relX = screenPos.X - textArea.X;
            int col = FindCharIndexAtX(lines[li], relX, font, scale);
            _cursor = LineStartIndex(li, lines) + col;
        }

        _cursor = Math.Clamp(_cursor, 0, _text.Length);
        ResetBlink();
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private (int s, int e) SelectionRange()
    {
        if (_selAnchor < 0 || _selAnchor == _cursor) return (-1, -1);
        int s = Math.Min(_selAnchor, _cursor);
        int e = Math.Max(_selAnchor, _cursor);
        return (s, e);
    }

    private void ResetBlink()
    {
        _blinkTimer = 0;
        _blinkState = true;
    }

    private static float MeasureWidth(string text, SpriteFont font, float scale)
        => text.Length == 0 ? 0f : font.MeasureString(text).X * scale;

    private static int FindCharIndexAtX(string text, float targetX, SpriteFont font, float scale)
    {
        if (targetX <= 0) return 0;
        for (int i = 1; i <= text.Length; i++)
        {
            float mid = (MeasureWidth(text[..(i - 1)], font, scale)
                       + MeasureWidth(text[..i],       font, scale)) * 0.5f;
            if (targetX < mid) return i - 1;
        }
        return text.Length;
    }

    private static int GetLineIndex(int charPos, string[] lines)
    {
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (charPos <= offset + lines[i].Length) return i;
            offset += lines[i].Length + 1;
        }
        return lines.Length - 1;
    }

    private static int LineStartIndex(int lineIndex, string[] lines)
    {
        int offset = 0;
        for (int i = 0; i < lineIndex; i++)
            offset += lines[i].Length + 1;
        return offset;
    }

    private int FindWordBoundaryLeft(int pos)
    {
        if (pos <= 0) return 0;
        pos--;
        while (pos > 0 && char.IsWhiteSpace(_text[pos - 1])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(_text[pos - 1])) pos--;
        return pos;
    }

    private int FindWordBoundaryRight(int pos)
    {
        int len = _text.Length;
        while (pos < len && !char.IsWhiteSpace(_text[pos])) pos++;
        while (pos < len && char.IsWhiteSpace(_text[pos]))  pos++;
        return pos;
    }
}
