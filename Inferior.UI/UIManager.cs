using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI;

/// <summary>
/// Root of the UI system. Owns all top-level controls, routes input,
/// tracks focus and hover, and drives the draw/update loop.
///
/// Usage in InferiorGame:
///   - Create once, pass Theme and GraphicsDevice.
///   - Call Update(dt, inputState) each frame.
///   - Call Draw(spriteBatch) each frame after game world is drawn.
///   - Add top-level controls (Windows, Panels) via Add().
/// </summary>
public sealed class UIManager : IDisposable
{
    private readonly GraphicsDevice  _gd;
    private readonly SpriteBatch     _sb;
    private readonly UIRenderer      _renderer;
    private readonly List<Control>   _roots    = [];

    // Focus and hover tracking
    private Control? _focused;
    private Control? _hovered;

    public Theme Theme { get; set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public UIManager(GraphicsDevice gd, Theme theme)
    {
        _gd       = gd;
        _sb       = new SpriteBatch(gd);
        _renderer = new UIRenderer(gd);
        Theme     = theme;
    }

    // ── Control management ────────────────────────────────────────────────────

    /// <summary>Add a top-level control (Window, Panel, etc.).</summary>
    public void Add(Control control)
    {
        control.Parent = null;
        _roots.Add(control);
    }

    /// <summary>Remove a top-level control.</summary>
    public void Remove(Control control)
    {
        _roots.Remove(control);
        if (_focused == control || IsDescendant(_focused, control))
            SetFocus(null);
        if (_hovered == control || IsDescendant(_hovered, control))
            _hovered = null;
    }

    /// <summary>Remove all top-level controls.</summary>
    public void Clear()
    {
        SetFocus(null);
        _hovered = null;
        _roots.Clear();
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    /// <summary>Call once per frame before Draw.</summary>
    public void Update(double dt, InputState input)
    {
        // Update all controls
        foreach (var root in _roots)
            root.Update(dt);

        // Update hover state
        UpdateHover(input);

        // Route mouse input — roots in reverse (topmost first)
        bool mouseConsumed = false;
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            if (!mouseConsumed && _roots[i].HandleInput(input))
                mouseConsumed = true;
        }

        // Click to focus — if left was just released and nothing consumed it,
        // check which root was hit and move it to front + set focus
        if (input.LeftReleased && !mouseConsumed)
        {
            var hit = FindAt(input.MousePosition);
            if (hit != null)
            {
                var root = FindRoot(hit);
                if (root != null) BringToFront(root);

                if (hit.TabIndex != 0)
                    SetFocus(hit);
            }
            else
            {
                // Click on empty space — unfocus
                SetFocus(null);
            }
        }
        else if (input.LeftReleased)
        {
            // Input was consumed — still handle focus and bring-to-front
            var hit = FindAt(input.MousePosition);
            if (hit != null)
            {
                var root = FindRoot(hit);
                if (root != null) BringToFront(root);

                if (hit.TabIndex != 0)
                    SetFocus(hit);
            }
        }

        // Keyboard: Tab / Shift+Tab cycle focus
        if (input.IsKeyPressed(Keys.Tab))
        {
            if (input.Shift) TabPrev();
            else             TabNext();
        }

        // Keyboard: Escape closes focused window
        if (input.IsKeyPressed(Keys.Escape))
        {
            var window = FindFocusedWindow();
            window?.Close();
        }

        // Route keyboard to focused control
        if (_focused != null)
            _focused.HandleInput(input);
    }

    /// <summary>Draw all controls. Starts and ends its own SpriteBatch.</summary>
    public void Draw()
    {
        _sb.Begin(blendState: BlendState.AlphaBlend);
        foreach (var root in _roots)
            root.Draw(_sb, _renderer, Theme);
        _sb.End();
    }

    // ── Focus management ──────────────────────────────────────────────────────

    public Control? FocusedControl => _focused;

    public void SetFocus(Control? control)
    {
        if (_focused == control) return;

        _focused?.RaiseLostFocus();
        _focused = control;
        if (_focused != null)
        {
            _focused.IsFocused = true;
            _focused.RaiseGotFocus();
        }
    }

    public void TabNext()
    {
        var focusable = CollectFocusable();
        if (focusable.Count == 0) return;

        int current = _focused == null ? -1 : focusable.IndexOf(_focused);
        int next    = (current + 1) % focusable.Count;
        SetFocus(focusable[next]);
    }

    public void TabPrev()
    {
        var focusable = CollectFocusable();
        if (focusable.Count == 0) return;

        int current = _focused == null ? 0 : focusable.IndexOf(_focused);
        int prev    = (current - 1 + focusable.Count) % focusable.Count;
        SetFocus(focusable[prev]);
    }

    // ── Z-order ───────────────────────────────────────────────────────────────

    public void BringToFront(Control root)
    {
        if (!_roots.Contains(root)) return;
        _roots.Remove(root);
        _roots.Add(root);
    }

    public void SendToBack(Control root)
    {
        if (!_roots.Contains(root)) return;
        _roots.Remove(root);
        _roots.Insert(0, root);
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    public Control? FindAt(Point screenPos)
    {
        // Search in reverse — topmost (last) first
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            var hit = _roots[i].FindAt(screenPos);
            if (hit != null) return hit;
        }
        return null;
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    private void UpdateHover(InputState input)
    {
        var newHover = FindAt(input.MousePosition);

        if (newHover != _hovered)
        {
            if (_hovered != null)
            {
                _hovered.IsHovered = false;
                _hovered.RaiseMouseLeave();
            }

            _hovered = newHover;

            if (_hovered != null)
            {
                _hovered.IsHovered = true;
                _hovered.RaiseMouseEnter();
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<Control> CollectFocusable()
    {
        var list = new List<Control>();
        foreach (var root in _roots)
            CollectFocusableRecursive(root, list);

        list.Sort((a, b) => a.TabIndex.CompareTo(b.TabIndex));
        return list;
    }

    private static void CollectFocusableRecursive(Control control, List<Control> list)
    {
        if (!control.Visible || !control.Enabled) return;
        if (control.TabIndex > 0) list.Add(control);
        foreach (var child in control.Children)
            CollectFocusableRecursive(child, list);
    }

    private Control? FindRoot(Control control)
    {
        var current = control;
        while (current.Parent != null)
            current = current.Parent;
        return _roots.Contains(current) ? current : null;
    }

    private static bool IsDescendant(Control? control, Control? ancestor)
    {
        if (control == null || ancestor == null) return false;
        var current = control.Parent;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    private Controls.Window? FindFocusedWindow()
    {
        if (_focused == null) return null;
        var current = _focused as Controls.Window
                   ?? _focused.Parent as Controls.Window;
        if (current != null) return current;

        // Walk up
        var parent = _focused.Parent;
        while (parent != null)
        {
            if (parent is Controls.Window w) return w;
            parent = parent.Parent;
        }
        return null;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _renderer.Dispose();
        _sb.Dispose();
    }
}
