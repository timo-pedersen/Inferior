using Inferior.UI;
using Inferior.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Inferior.UI.Test;

public sealed class DiagramCanvasTests
{
    [Fact]
    public void ModelRejectsConnectionsToUnknownNodes()
    {
        var canvas = new DiagramCanvas();
        var nodes = new[]
        {
            new DiagramNode { Id = "source", Title = "Source" },
        };
        var connections = new[]
        {
            new DiagramConnection
            {
                Id = "missing-target",
                FromNodeId = "source",
                ToNodeId = "consumer",
            },
        };

        canvas.SetModel(nodes, []);

        Assert.Throws<ArgumentException>(() => canvas.SetModel(nodes, connections));
        Assert.Contains("source", canvas.Nodes);
    }

    [Fact]
    public void ActionToggleRequestsChangeButRemainsUnconfirmed()
    {
        var canvas = new DiagramCanvas { Bounds = new Rectangle(0, 0, 200, 100) };
        var node = new DiagramNode
        {
            Id = "device",
            Title = "Device",
            Bounds = new Rectangle(0, 0, 100, 80),
            ShowActionToggle = true,
            ActionState = false,
            ActionConfirmed = false,
        };
        canvas.SetModel([node], []);

        (string id, bool value)? request = null;
        canvas.ActionToggleRequested += (id, value) => request = (id, value);

        canvas.HandleInput(Input(ButtonState.Pressed, ButtonState.Released, 75, 10));
        canvas.HandleInput(Input(ButtonState.Released, ButtonState.Pressed, 75, 10));

        Assert.Equal(("device", true), request);
        Assert.True(node.ActionState);
        Assert.Null(node.ActionConfirmed);
    }

    [Fact]
    public void EdgePanelReportsLogicalOpenAndCloseImmediately()
    {
        var host = new EdgePanelHost(PanelEdge.Top);
        host.AddTab("TEST", new Panel());
        var changes = new List<(bool open, int tab)>();
        host.StateChanged += (open, tab) => changes.Add((open, tab));

        host.ApplyState(0, true);
        host.ApplyState(0, false);

        Assert.Equal([(true, 0), (false, 0)], changes);
        Assert.False(host.IsOpen);
        Assert.Equal(0, host.ActiveTab);
    }

    private static InputState Input(
        ButtonState left,
        ButtonState previousLeft,
        int x,
        int y)
        => new(
            new MouseState(x, y, 0, left, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released),
            new MouseState(x, y, 0, previousLeft, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released),
            new KeyboardState(),
            new KeyboardState());
}
