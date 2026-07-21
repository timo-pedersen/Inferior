using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.UI.Controls;

public enum GridLengthMode
{
    Fixed,
    Auto,
    Star,
}

public readonly record struct GridLength(GridLengthMode Mode, float Value)
{
    public static GridLength Fixed(float pixels) => new(GridLengthMode.Fixed, pixels);
    public static GridLength Auto() => new(GridLengthMode.Auto, 1);
    public static GridLength Star(float weight = 1) => new(GridLengthMode.Star, weight);
}

public sealed class GridPanel : Control
{
    private readonly Dictionary<Control, GridPlacement> _placements = [];

    public List<GridLength> Columns { get; } = [];
    public List<GridLength> Rows { get; } = [];
    public int ContentPadding { get; set; }
    public bool DrawBackground { get; set; }
    public bool DrawBorder { get; set; }

    public override Rectangle ContentBounds
    {
        get
        {
            Rectangle ab = AbsoluteBounds;
            int pad = ContentPadding;
            return new Rectangle(ab.X + pad, ab.Y + pad, Math.Max(0, ab.Width - pad * 2), Math.Max(0, ab.Height - pad * 2));
        }
    }

    public void SetPlacement(Control child, int column, int row, int columnSpan = 1, int rowSpan = 1)
        => _placements[child] = new GridPlacement(column, row, Math.Max(1, columnSpan), Math.Max(1, rowSpan));

    public void Add(Control child, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Add(child);
        SetPlacement(child, column, row, columnSpan, rowSpan);
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible) return;
        if (DrawBackground)
            renderer.FillRect(sb, AbsoluteBounds, BackColor ?? theme.PanelBackground);
        if (DrawBorder)
            renderer.DrawRect(sb, AbsoluteBounds, ForeColor ?? theme.PanelBorder, theme.BorderThickness);
        DrawChildren(sb, renderer, theme);
    }

    private void ArrangeChildren()
    {
        if (Columns.Count == 0 || Rows.Count == 0)
            return;

        Rectangle content = ContentBounds;
        int[] widths = Resolve(Columns, content.Width, axisIsColumn: true);
        int[] heights = Resolve(Rows, content.Height, axisIsColumn: false);
        int[] x = Prefix(widths);
        int[] y = Prefix(heights);

        foreach (Control child in Children.Where(child => child.Visible))
        {
            GridPlacement placement = _placements.TryGetValue(child, out GridPlacement p)
                ? p
                : new GridPlacement(0, 0, 1, 1);
            int column = Math.Clamp(placement.Column, 0, Columns.Count - 1);
            int row = Math.Clamp(placement.Row, 0, Rows.Count - 1);
            int columnSpan = Math.Clamp(placement.ColumnSpan, 1, Columns.Count - column);
            int rowSpan = Math.Clamp(placement.RowSpan, 1, Rows.Count - row);
            int width = widths.Skip(column).Take(columnSpan).Sum();
            int height = heights.Skip(row).Take(rowSpan).Sum();
            child.Bounds = new Rectangle(
                x[column] + child.Margin.Left,
                y[row] + child.Margin.Top,
                Math.Max(0, width - child.Margin.Horizontal),
                Math.Max(0, height - child.Margin.Vertical));
        }
    }

    private int[] Resolve(IReadOnlyList<GridLength> definitions, int available, bool axisIsColumn)
    {
        var result = new int[definitions.Count];
        float starWeight = 0;
        int used = 0;
        for (int i = 0; i < definitions.Count; i++)
        {
            GridLength def = definitions[i];
            if (def.Mode == GridLengthMode.Fixed)
            {
                result[i] = Math.Max(0, (int)MathF.Round(def.Value));
                used += result[i];
            }
            else if (def.Mode == GridLengthMode.Auto)
            {
                int size = MeasureAuto(i, axisIsColumn);
                result[i] = size;
                used += size;
            }
            else
            {
                starWeight += Math.Max(0, def.Value);
            }
        }

        int remaining = Math.Max(0, available - used);
        for (int i = 0; i < definitions.Count; i++)
        {
            GridLength def = definitions[i];
            if (def.Mode != GridLengthMode.Star)
                continue;
            result[i] = starWeight <= 0 ? 0 : (int)MathF.Round(remaining * (Math.Max(0, def.Value) / starWeight));
        }
        return result;
    }

    private int MeasureAuto(int index, bool axisIsColumn)
    {
        int max = 0;
        foreach (Control child in Children.Where(child => child.Visible))
        {
            GridPlacement placement = _placements.TryGetValue(child, out GridPlacement p)
                ? p
                : new GridPlacement(0, 0, 1, 1);
            if (axisIsColumn && placement.Column == index && placement.ColumnSpan == 1)
                max = Math.Max(max, child.DesiredSize.X + child.Margin.Horizontal);
            if (!axisIsColumn && placement.Row == index && placement.RowSpan == 1)
                max = Math.Max(max, child.DesiredSize.Y + child.Margin.Vertical);
        }
        return max;
    }

    private static int[] Prefix(IReadOnlyList<int> values)
    {
        var result = new int[values.Count];
        int cursor = 0;
        for (int i = 0; i < values.Count; i++)
        {
            result[i] = cursor;
            cursor += values[i];
        }
        return result;
    }

    private readonly record struct GridPlacement(int Column, int Row, int ColumnSpan, int RowSpan);
}
