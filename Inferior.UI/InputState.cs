using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI;

/// <summary>
/// Immutable snapshot of input state for one frame.
/// Created by the game once per Update and passed through the UI system.
/// Centralises all "just pressed / just released" logic so controls
/// never need to track previous state themselves.
/// </summary>
public sealed class InputState
{
    private readonly MouseState    _mouse;
    private readonly MouseState    _prevMouse;
    private readonly KeyboardState _keys;
    private readonly KeyboardState _prevKeys;

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

    // ── Constructor ───────────────────────────────────────────────────────────

    public InputState(MouseState mouse, MouseState prevMouse,
                      KeyboardState keys, KeyboardState prevKeys)
    {
        _mouse     = mouse;
        _prevMouse = prevMouse;
        _keys      = keys;
        _prevKeys  = prevKeys;
    }

    /// <summary>
    /// Convenience factory — call once per frame in InferiorGame,
    /// store previous state as fields.
    /// </summary>
    public static InputState Capture(ref MouseState prevMouse, ref KeyboardState prevKeys)
    {
        var mouse = Mouse.GetState();
        var keys  = Keyboard.GetState();
        var state = new InputState(mouse, prevMouse, keys, prevKeys);
        prevMouse = mouse;
        prevKeys  = keys;
        return state;
    }
}
