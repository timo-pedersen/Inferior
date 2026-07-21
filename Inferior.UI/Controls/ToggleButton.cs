using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI.Controls;

/// <summary>
/// Toggle button with a visual "pending / confirmed" state indicator.
///
/// Interaction model:
///   IsOn         — what the player has requested (flips on each click).
///   IsConfirmed  — set externally when the system confirms the state.
///                  null   = pending (the indicator flashes).
///                  true   = confirmed on.
///                  false  = confirmed off.
///
/// Typical usage for a ship system toggle:
///   1. Player clicks → IsOn flips, IsConfirmed set to null, Toggled fires.
///   2. Game subscribes to DataBus, sets toggle.IsConfirmed = true when system comes online.
///   3. Indicator stops flashing and becomes solid.
///
/// If IsConfirmed is never set (not wired to anything), the toggle behaves as a
/// simple on/off button — no flashing, just IsOn state reflected in colour.
/// </summary>
public sealed class ToggleButton : Control
{
    public string Text { get; set; } = "";

    // ── State ─────────────────────────────────────────────────────────────────

    public bool  IsOn        { get; private set; }
    public bool? IsConfirmed { get; set; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when the button is clicked. Arg = new IsOn value.</summary>
    public event Action<ToggleButton, bool>? Toggled;

    // ── Animation ─────────────────────────────────────────────────────────────

    private double _flashTimer = 0.0;
    private bool   _flashState = true;
    private const double FlashInterval = 0.35;

    private bool IsPending => IsOn && IsConfirmed == null;

    // ── Input tracking ────────────────────────────────────────────────────────

    private bool _pressedInside;

    public ToggleButton() { TabIndex = 1; }

    public ToggleButton(string text, Rectangle bounds)
    {
        Text     = text;
        Bounds   = bounds;
        TabIndex = 1;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public override void Update(double dt)
    {
        if (IsPending)
        {
            _flashTimer += dt;
            if (_flashTimer >= FlashInterval)
            {
                _flashTimer -= FlashInterval;
                _flashState = !_flashState;
            }
        }
        else
        {
            _flashState = true;
            _flashTimer = 0;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled) return false;

        bool inside = HitTest(input.MousePosition);

        if (input.LeftPressed && inside)
        {
            IsPressed      = true;
            _pressedInside = true;
            return true;
        }

        if (input.LeftReleased)
        {
            bool wasPressed = IsPressed;
            IsPressed       = false;

            if (wasPressed && _pressedInside && inside)
            {
                _pressedInside = false;
                Toggle();
                return true;
            }
            _pressedInside = false;
        }

        if (IsFocused)
        {
            if (input.IsKeyPressed(Keys.Space) || input.IsKeyPressed(Keys.Enter))
            {
                Toggle();
                return true;
            }
        }

        return inside && input.LeftHeld;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;

        renderer.DrawToggleButton(sb, AbsoluteBounds, Text, theme,
            isOn:        IsOn,
            isConfirmed: IsConfirmed,
            flashState:  _flashState,
            hovered:     IsHovered,
            focused:     IsFocused,
            enabled:     Enabled,
            backOverride:  BackColor,
            textOverride:  TextColor,
            fontOverride:  Font,
            scaleOverride: FontScale);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Toggle()
    {
        IsOn        = !IsOn;
        IsConfirmed = null;  // reset confirmation — waiting for system response
        Toggled?.Invoke(this, IsOn);
    }

    /// <summary>
    /// Programmatically set the toggle state without firing the Toggled event.
    /// Use to sync UI with game state at startup.
    /// </summary>
    public void SetState(bool on, bool? confirmed = null)
    {
        IsOn        = on;
        IsConfirmed = confirmed;
    }
}

public sealed class ExclusiveButtonGroup
{
    private readonly List<ToggleButton> _buttons = [];
    private bool _updating;

    public ToggleButton? Selected { get; private set; }
    public event Action<ToggleButton>? SelectionChanged;

    public void Add(ToggleButton button, bool selected = false)
    {
        if (_buttons.Contains(button))
            return;
        _buttons.Add(button);
        button.Toggled += OnButtonToggled;
        if (selected || Selected is null)
            Select(button, notify: false);
        else
            button.SetState(false, false);
    }

    public void Select(ToggleButton button, bool notify = true)
    {
        if (!_buttons.Contains(button))
            Add(button);
        if (ReferenceEquals(Selected, button))
            return;

        _updating = true;
        foreach (ToggleButton candidate in _buttons)
            candidate.SetState(ReferenceEquals(candidate, button), ReferenceEquals(candidate, button));
        _updating = false;

        Selected = button;
        if (notify)
            SelectionChanged?.Invoke(button);
    }

    private void OnButtonToggled(ToggleButton button, bool isOn)
    {
        if (_updating)
            return;
        Select(button);
    }
}
