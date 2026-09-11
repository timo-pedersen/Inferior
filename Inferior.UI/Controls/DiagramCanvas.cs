using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public enum DiagramNodeState
{
    Empty,
    Offline,
    Transitioning,
    Online,
    Warning,
    Fault,
    Unknown,
}

public enum DiagramValueSeverity
{
    Normal,
    Warning,
    Critical,
    Stale,
}

public sealed record DiagramValue(string Label, string Text, DiagramValueSeverity Severity = DiagramValueSeverity.Normal);

/// <summary>Mutable presentation model consumed by <see cref="DiagramCanvas"/>.</summary>
public sealed class DiagramNode
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public string Subtitle { get; set; } = "";
    public Rectangle Bounds { get; set; }
    public DiagramNodeState State { get; set; } = DiagramNodeState.Unknown;
    public string StateText { get; set; } = "UNKNOWN";
    public bool ShowActionToggle { get; set; }
    public bool ActionState { get; set; }
    public bool? ActionConfirmed { get; set; }
    public List<DiagramValue> Values { get; } = [];
}

public sealed class DiagramConnection
{
    public required string Id { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public string Label { get; set; } = "";
    public bool Active { get; set; }
    public double LoadFraction { get; set; }
}

/// <summary>
/// Generic value-driven box-and-line diagram. It knows nothing about ships, buses, topics,
/// commands, or units; callers own layout, values, and action semantics.
/// </summary>
public sealed class DiagramCanvas : Control
{
    private readonly Dictionary<string, DiagramNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiagramConnection> _connections = new(StringComparer.Ordinal);
    private string? _pressedActionNodeId;

    public IReadOnlyDictionary<string, DiagramNode> Nodes => _nodes;
    public IReadOnlyDictionary<string, DiagramConnection> Connections => _connections;

    /// <summary>Raised after the generic action toggle changes. The caller confirms externally.</summary>
    public event Action<string, bool>? ActionToggleRequested;

    public DiagramCanvas() => Overflow = OverflowMode.Clip;

    public void SetModel(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(connections);
        var nextNodes = new Dictionary<string, DiagramNode>(StringComparer.Ordinal);
        var nextConnections = new Dictionary<string, DiagramConnection>(StringComparer.Ordinal);

        foreach (DiagramNode node in nodes)
        {
            if (!nextNodes.TryAdd(node.Id, node))
                throw new ArgumentException($"Duplicate diagram node '{node.Id}'.", nameof(nodes));
        }
        foreach (DiagramConnection connection in connections)
        {
            if (!nextNodes.ContainsKey(connection.FromNodeId)
                || !nextNodes.ContainsKey(connection.ToNodeId))
            {
                throw new ArgumentException(
                    $"Diagram connection '{connection.Id}' references an unknown node.",
                    nameof(connections));
            }
            if (!nextConnections.TryAdd(connection.Id, connection))
                throw new ArgumentException(
                    $"Duplicate diagram connection '{connection.Id}'.",
                    nameof(connections));
        }

        _nodes.Clear();
        _connections.Clear();
        foreach ((string id, DiagramNode node) in nextNodes)
            _nodes.Add(id, node);
        foreach ((string id, DiagramConnection connection) in nextConnections)
            _connections.Add(id, connection);
        _pressedActionNodeId = null;
    }

    public void ClearModel()
    {
        _nodes.Clear();
        _connections.Clear();
        _pressedActionNodeId = null;
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;

        foreach (DiagramNode node in _nodes.Values.Reverse())
        {
            if (!node.ShowActionToggle)
                continue;
            Rectangle actionBounds = ActionBounds(node);
            bool inside = actionBounds.Contains(input.MousePosition);
            if (input.LeftPressed && inside)
            {
                _pressedActionNodeId = node.Id;
                return true;
            }
            if (input.LeftReleased && _pressedActionNodeId == node.Id)
            {
                _pressedActionNodeId = null;
                if (!inside)
                    return false;
                node.ActionState = !node.ActionState;
                node.ActionConfirmed = null;
                ActionToggleRequested?.Invoke(node.Id, node.ActionState);
                return true;
            }
            if (inside && input.LeftHeld)
                return true;
        }

        if (input.LeftReleased)
            _pressedActionNodeId = null;
        return AbsoluteBounds.Contains(input.MousePosition) && input.LeftHeld;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;

        renderer.FillRect(sb, AbsoluteBounds, new Color(5, 9, 18, 220));
        DrawConnections(sb, renderer, theme);
        foreach (DiagramNode node in _nodes.Values)
            DrawNode(sb, renderer, theme, node);
    }

    private void DrawConnections(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        foreach (DiagramConnection connection in _connections.Values)
        {
            if (!_nodes.TryGetValue(connection.FromNodeId, out DiagramNode? from)
                || !_nodes.TryGetValue(connection.ToNodeId, out DiagramNode? to))
            {
                continue;
            }

            Rectangle a = NodeBounds(from);
            Rectangle b = NodeBounds(to);
            Vector2 start = new(a.Right, a.Center.Y);
            Vector2 end = new(b.Left, b.Center.Y);
            float middleX = (start.X + end.X) * 0.5f;
            Color color = connection.Active
                ? new Color(70, 185, 235)
                : new Color(35, 55, 78);
            float thickness = connection.Active
                ? 1.5f + (float)Math.Clamp(connection.LoadFraction, 0.0, 1.0) * 3.0f
                : 1.0f;

            renderer.DrawLine(sb, start, new Vector2(middleX, start.Y), color, thickness);
            renderer.DrawLine(sb, new Vector2(middleX, start.Y), new Vector2(middleX, end.Y), color, thickness);
            renderer.DrawLine(sb, new Vector2(middleX, end.Y), end, color, thickness);

            if (!string.IsNullOrEmpty(connection.Label))
            {
                var labelBounds = new Rectangle((int)middleX - 55, (int)((start.Y + end.Y) * 0.5f) - 9, 110, 18);
                renderer.FillRect(sb, labelBounds, new Color(5, 9, 18, 235));
                renderer.DrawTextCentred(sb, connection.Label, labelBounds, theme.Font, theme.SmallScale * 0.72f, color);
            }
        }
    }

    private void DrawNode(SpriteBatch sb, UIRenderer renderer, Theme theme, DiagramNode node)
    {
        Rectangle bounds = NodeBounds(node);
        Color border = StateColor(node.State, theme);
        Color background = node.State == DiagramNodeState.Empty
            ? new Color(10, 14, 23, 150)
            : new Color(12, 20, 36, 240);
        renderer.FillRect(sb, bounds, background);
        renderer.DrawRect(sb, bounds, border, node.State == DiagramNodeState.Fault ? 2 : 1);

        var header = new Rectangle(bounds.X + 6, bounds.Y + 4, bounds.Width - 12, 19);
        renderer.DrawTextLeft(sb, node.Title, header, theme.Font, theme.SmallScale * 0.84f, theme.TextTitle);

        if (node.ShowActionToggle)
        {
            Rectangle action = ActionBounds(node);
            Color actionBack = node.ActionConfirmed switch
            {
                true => theme.ToggleOn,
                false => theme.ToggleOff,
                null => theme.TogglePending,
            };
            renderer.FillRect(sb, action, actionBack);
            renderer.DrawRect(sb, action, theme.ButtonBorder, 1);
            renderer.DrawTextCentred(sb, node.ActionState ? "ON" : "OFF", action,
                theme.Font, theme.SmallScale * 0.68f, theme.TextNormal);
        }

        var stateBounds = new Rectangle(bounds.X + 6, bounds.Y + 24, bounds.Width - 12, 16);
        renderer.DrawTextLeft(sb, node.StateText, stateBounds, theme.Font, theme.SmallScale * 0.68f, border);
        if (!string.IsNullOrWhiteSpace(node.Subtitle))
        {
            var subtitleBounds = new Rectangle(bounds.X + 6, bounds.Y + 39, bounds.Width - 12, 15);
            renderer.DrawTextLeft(sb, node.Subtitle, subtitleBounds, theme.Font,
                theme.SmallScale * 0.64f, theme.TextDisabled);
        }

        int y = bounds.Y + 57;
        foreach (DiagramValue value in node.Values.Take(4))
        {
            Color color = value.Severity switch
            {
                DiagramValueSeverity.Warning => new Color(235, 185, 65),
                DiagramValueSeverity.Critical => new Color(240, 75, 70),
                DiagramValueSeverity.Stale => theme.TextDisabled,
                _ => theme.TextNormal,
            };
            var labelBounds = new Rectangle(bounds.X + 7, y, bounds.Width / 2, 16);
            var valueBounds = new Rectangle(bounds.Center.X - 5, y, bounds.Width / 2 - 3, 16);
            renderer.DrawTextLeft(sb, value.Label, labelBounds, theme.Font, theme.SmallScale * 0.65f, theme.TextDisabled);
            Vector2 size = renderer.MeasureText(value.Text, theme.Font, theme.SmallScale * 0.65f);
            renderer.DrawText(sb, value.Text,
                new Vector2(valueBounds.Right - size.X, valueBounds.Y),
                theme.Font, theme.SmallScale * 0.65f, color);
            y += 17;
        }
    }

    private Rectangle NodeBounds(DiagramNode node)
    {
        Rectangle root = AbsoluteBounds;
        return new Rectangle(
            root.X + node.Bounds.X,
            root.Y + node.Bounds.Y,
            node.Bounds.Width,
            node.Bounds.Height);
    }

    private Rectangle ActionBounds(DiagramNode node)
    {
        Rectangle bounds = NodeBounds(node);
        return new Rectangle(bounds.Right - 43, bounds.Y + 4, 37, 18);
    }

    private static Color StateColor(DiagramNodeState state, Theme theme)
        => state switch
        {
            DiagramNodeState.Online => new Color(70, 190, 105),
            DiagramNodeState.Transitioning => new Color(230, 175, 55),
            DiagramNodeState.Warning => new Color(235, 145, 45),
            DiagramNodeState.Fault => new Color(235, 65, 65),
            DiagramNodeState.Offline => new Color(80, 95, 115),
            DiagramNodeState.Empty => new Color(48, 62, 82),
            _ => theme.PanelBorder,
        };
}
