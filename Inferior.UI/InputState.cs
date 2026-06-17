using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI;

/// <summary>
/// Immutable snapshot of input state for one frame.
/// Created by the game once per Update and passed through the UI system.
/// Centralises all "just pressed / just released" logic so controls
/// never need to track previous state themselves.
///
/// Text input:
///   Wire Window.TextInput to InputState.PushTypedChar() in the Game constructor.
///   TypedChars is populated from that buffer and cleared each Capture() call.
/// </summary>
public sealed class InputState
{
    private readonly MouseState    _mouse;
    private readonly MouseState    _prevMouse;
    private readonly KeyboardState _keys;
    private readonly KeyboardState _prevKeys;

    // ── Text input accumulator ─────────────────────────────────────────────────
    // Populated by the game's Window.TextInput handler; cleared by each Capture().
    private static readonly List<char> _pendingChars = [];

    /// <summary>
    /// Push a typed character into the accumulator. Wire to Window.TextInput:
    ///   Window.TextInput += (_, e) => InputState.PushTypedChar(e.Character);
    /// Control characters (Backspace, Enter, etc.) are filtered out here;
    /// handle those via IsKeyPressed() instead.
    /// </summary>
    public static void PushTypedChar(char c)
    {
        if (!char.IsControl(c))
            _pendingChars.Add(c);
    }

    /// <summary>Characters typed this frame (printable only, in order).</summary>
    public IReadOnlyList<char> TypedChars { get; }

    // ── Mouse position ────────────────────────────────────────────────────────
    public Point MousePosition => _mouse.Position;

    // ── Scroll ────────────────────────────────────────────────────────────────
    public int ScrollDelta => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    // ── Left button ───────────────────────────────────────────────────────────
    public bool LeftPressed  => _mouse.LeftButton == ButtonState.Pressed
                             && _prevMouse.LeftButton == ButtonState.Released;
    public bool LeftReleased => _mouse.LeftButton == ButtonState.Released
                             && _prevMouse.LeftButton == ButtonState.Pressed;
    public bool LeftHeld     => _mouse.LeftButton == ButtonState.Pressed;

    // ── Right button ──────────────────────────────────────────────────────────
    public bool RightPressed  => _mouse.RightButton == ButtonState.Pressed
                              && _prevMouse.RightButton == ButtonState.Released;
    public bool RightReleased => _mouse.RightButton == ButtonState.Released
                              && _prevMouse.RightButton == ButtonState.Pressed;
    public bool RightHeld     => _mouse.RightButton == ButtonState.Pressed;

    // ── Middle button ─────────────────────────────────────────────────────────
    public bool MiddlePressed  => _mouse.MiddleButton == ButtonState.Pressed
                               && _prevMouse.MiddleButton == ButtonState.Released;
    public bool MiddleReleased => _mouse.MiddleButton == ButtonState.Released
                               && _prevMouse.MiddleButton == ButtonState.Pressed;

    // ── Keyboard ──────────────────────────────────────────────────────────────
    public bool IsKeyPressed(Keys key)  =>  _keys.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
    public bool IsKeyReleased(Keys key) => !_keys.IsKeyDown(key) &&  _prevKeys.IsKeyDown(key);
    public bool IsKeyHeld(Keys key)     =>  _keys.IsKeyDown(key);

    public bool Shift => _keys.IsKeyDown(Keys.LeftShift)   || _keys.IsKeyDown(Keys.RightShift);
    public bool Ctrl  => _keys.IsKeyDown(Keys.LeftControl) || _keys.IsKeyDown(Keys.RightControl);
    public bool Alt   => _keys.IsKeyDown(Keys.LeftAlt)     || _keys.IsKeyDown(Keys.RightAlt);

    /// <summary>True if any keyboard key was pressed this frame (not held from last frame).</summary>
    public bool AnyKeyJustPressed
    {
        get
        {
            foreach (var key in _keys.GetPressedKeys())
                if (!_prevKeys.IsKeyDown(key)) return true;
            return false;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public InputState(MouseState mouse, MouseState prevMouse,
                      KeyboardState keys, KeyboardState prevKeys,
                      IReadOnlyList<char>? typedChars = null)
    {
        _mouse     = mouse;
        _prevMouse = prevMouse;
        _keys      = keys;
        _prevKeys  = prevKeys;
        TypedChars = typedChars ?? [];
    }

    /// <summary>
    /// For states that manage their own mouse/keyboard state and don't call Capture(),
    /// use this to take the pending typed chars and clear the buffer.
    /// </summary>
    public static IReadOnlyList<char> DrainTypedChars()
    {
        char[] chars = [.. _pendingChars];
        _pendingChars.Clear();
        return chars;
    }

    /// <summary>
    /// Convenience factory — call once per frame in InferiorGame,
    /// store previous state as fields.
    /// </summary>
    public static InputState Capture(ref MouseState prevMouse, ref KeyboardState prevKeys)
    {
        var mouse = Mouse.GetState();
        var keys  = Keyboard.GetState();

        // Drain the typed-char accumulator
        char[] chars = [.. _pendingChars];
        _pendingChars.Clear();

        var state = new InputState(mouse, prevMouse, keys, prevKeys, chars);
        prevMouse = mouse;
        prevKeys  = keys;
        return state;
    }
}
