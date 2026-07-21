using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.UI.Test;

public sealed class BasicControlInteractionTests
{
    [Fact]
    public void Command_button_invokes_once_on_valid_release_and_does_not_stay_pressed()
    {
        var button = new Button("Save", new Rectangle(0, 0, 80, 30));
        int clicks = 0;
        button.Clicked += _ => clicks++;

        button.HandleInput(Input(left: ButtonState.Pressed, previousLeft: ButtonState.Released, x: 10, y: 10));
        button.HandleInput(Input(left: ButtonState.Released, previousLeft: ButtonState.Pressed, x: 10, y: 10));

        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
    }

    [Fact]
    public void Command_button_does_not_invoke_on_drag_release_outside_or_disabled()
    {
        var button = new Button("Save", new Rectangle(0, 0, 80, 30));
        int clicks = 0;
        button.Clicked += _ => clicks++;

        button.HandleInput(Input(left: ButtonState.Pressed, previousLeft: ButtonState.Released, x: 10, y: 10));
        button.HandleInput(Input(left: ButtonState.Released, previousLeft: ButtonState.Pressed, x: 100, y: 10));
        button.Enabled = false;
        button.HandleInput(Input(left: ButtonState.Pressed, previousLeft: ButtonState.Released, x: 10, y: 10));
        button.HandleInput(Input(left: ButtonState.Released, previousLeft: ButtonState.Pressed, x: 10, y: 10));

        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Toggle_button_toggles_back_and_external_state_is_reflected()
    {
        var toggle = new ToggleButton("T", new Rectangle(0, 0, 40, 20));

        Click(toggle);
        Assert.True(toggle.IsOn);
        Click(toggle);
        Assert.False(toggle.IsOn);

        toggle.SetState(true, true);
        Assert.True(toggle.IsOn);
        Assert.True(toggle.IsConfirmed);
    }

    [Fact]
    public void Topmost_visible_textbox_receives_focus_region_and_clipped_portion_is_not_interactive()
    {
        var root = new Panel(new Rectangle(0, 0, 100, 100)) { Overflow = OverflowMode.Clip };
        var lower = new TextBox { Bounds = new Rectangle(10, 10, 60, 30) };
        var upper = new TextBox { Bounds = new Rectangle(20, 20, 60, 30) };
        var clipped = new TextBox { Bounds = new Rectangle(80, 80, 60, 30) };
        root.Add(lower);
        root.Add(upper);
        root.Add(clipped);

        Assert.Same(upper, root.FindAt(new Point(25, 25)));
        Assert.Same(clipped, root.FindAt(new Point(90, 90)));
        Assert.Null(root.FindAt(new Point(120, 90)));
    }

    private static void Click(Control control)
    {
        control.HandleInput(Input(left: ButtonState.Pressed, previousLeft: ButtonState.Released, x: 10, y: 10));
        control.HandleInput(Input(left: ButtonState.Released, previousLeft: ButtonState.Pressed, x: 10, y: 10));
    }

    private static InputState Input(ButtonState left, ButtonState previousLeft, int x, int y)
        => new(
            new MouseState(x, y, 0, left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
            new MouseState(x, y, 0, previousLeft, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
            new KeyboardState(),
            new KeyboardState());
}
