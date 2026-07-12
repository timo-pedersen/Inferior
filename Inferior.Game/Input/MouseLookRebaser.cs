using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Inferior.Game.Input;

internal readonly record struct MouseLookInput(double PitchInput, double YawInput, bool Rebased);

internal sealed class MouseLookRebaser
{
    private bool _rebaseNext = true;
    private bool _wasActive;
    private bool _wasFocused = true;
    private Point _lastCenter;

    public void RequestRebase()
        => _rebaseNext = true;

    public MouseLookInput Sample(
        MouseState mouse,
        Point center,
        bool active,
        bool focused,
        double sensitivity)
    {
        if (!focused)
        {
            _wasFocused = false;
            _wasActive = active;
            _rebaseNext = true;
            _lastCenter = center;
            return new MouseLookInput(0.0, 0.0, Rebased: true);
        }

        if (!active)
        {
            _wasFocused = true;
            _wasActive = false;
            _rebaseNext = true;
            _lastCenter = center;
            return new MouseLookInput(0.0, 0.0, Rebased: true);
        }

        bool activated = !_wasActive;
        bool focusRegained = !_wasFocused;
        bool centerChanged = center != _lastCenter;
        bool shouldRebase = _rebaseNext || activated || focusRegained || centerChanged;

        _wasFocused = true;
        _wasActive = true;
        _lastCenter = center;

        if (shouldRebase)
        {
            _rebaseNext = false;
            return new MouseLookInput(0.0, 0.0, Rebased: true);
        }

        double yawInput = -(mouse.X - center.X) * sensitivity;
        double pitchInput = -(mouse.Y - center.Y) * sensitivity;
        return new MouseLookInput(pitchInput, yawInput, Rebased: false);
    }
}
