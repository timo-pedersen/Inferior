using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Inferior.UI.Controls;

public sealed class MenuBar : Control
{
    public int ItemWidth { get; set; } = 86;
    public int ItemHeight { get; set; } = 28;
    private PopupMenu? _openMenu;

    public MenuButton AddMenu(string text)
    {
        var button = new MenuButton(text);
        button.Clicked += menuButton =>
        {
            CloseAll();
            _openMenu = menuButton.Menu;
            _openMenu.Visible = true;
        };
        Add(button);
        Add(button.Menu);
        ArrangeChildren();
        return button;
    }

    public void CloseAll()
    {
        foreach (Control child in Children.OfType<MenuButton>())
            child.Visible = true;
        foreach (PopupMenu popup in Children.OfType<PopupMenu>())
            popup.Visible = false;
        _openMenu = null;
    }

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override bool HandleInput(InputState input)
    {
        if (_openMenu is not null && _openMenu.Visible)
        {
            if (_openMenu.HandleInput(input))
                return true;
            if (input.LeftReleased && !AbsoluteBounds.Contains(input.MousePosition) && !_openMenu.AbsoluteBounds.Contains(input.MousePosition))
            {
                CloseAll();
                return true;
            }
        }
        return base.HandleInput(input);
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        renderer.FillRect(sb, AbsoluteBounds, BackColor ?? theme.WindowTitleBar);
        foreach (Control child in Children.Where(child => child is not PopupMenu))
            child.Draw(sb, renderer, theme);
        foreach (PopupMenu popup in Children.OfType<PopupMenu>())
            popup.Draw(sb, renderer, theme);
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    private void ArrangeChildren()
    {
        int x = 0;
        foreach (MenuButton button in Children.OfType<MenuButton>())
        {
            button.Bounds = new Rectangle(x, 0, ItemWidth, ItemHeight);
            button.Menu.Bounds = new Rectangle(x, ItemHeight, Math.Max(ItemWidth + 70, button.Menu.DesiredSize.X), button.Menu.DesiredSize.Y);
            x += ItemWidth;
        }
    }
}

public sealed class MenuButton : Control
{
    private bool _pressedInside;

    public string Text { get; }
    public PopupMenu Menu { get; } = new() { Visible = false };
    public event Action<MenuButton>? Clicked;

    public MenuButton(string text)
    {
        Text = text;
        TabIndex = 1;
    }

    public MenuItem AddItem(string text, Action action)
    {
        var item = new MenuItem(text, action);
        Menu.AddItem(item);
        return item;
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;
        bool inside = HitTest(input.MousePosition);
        if (input.LeftPressed && inside)
        {
            IsPressed = true;
            _pressedInside = true;
            return true;
        }
        if (input.LeftReleased)
        {
            bool wasPressed = IsPressed;
            IsPressed = false;
            if (wasPressed && _pressedInside && inside)
            {
                Clicked?.Invoke(this);
                _pressedInside = false;
                return true;
            }
            _pressedInside = false;
        }
        if (IsFocused && (input.IsKeyPressed(Keys.Space) || input.IsKeyPressed(Keys.Enter)))
        {
            Clicked?.Invoke(this);
            return true;
        }
        return inside && input.LeftHeld;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
        => renderer.DrawButton(sb, AbsoluteBounds, Text, theme, IsHovered, IsPressed, IsFocused, Enabled, BackColor, TextColor, Font, FontScale);
}

public sealed class PopupMenu : Control
{
    public int ItemHeight { get; set; } = 26;

    public override Point DesiredSize
        => new(Bounds.Width <= 0 ? 160 : Bounds.Width, Math.Max(ItemHeight, Children.Count * ItemHeight));

    public void AddItem(MenuItem item)
    {
        Add(item);
        ArrangeChildren();
    }

    public override void Update(double dt)
    {
        ArrangeChildren();
        base.Update(dt);
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        renderer.FillRect(sb, AbsoluteBounds, BackColor ?? theme.WindowBackground);
        renderer.DrawRect(sb, AbsoluteBounds, ForeColor ?? theme.WindowBorderFocus, theme.BorderThickness);
        DrawChildren(sb, renderer, theme);
    }

    protected override void OnBoundsChanged() => ArrangeChildren();

    private void ArrangeChildren()
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].Bounds = new Rectangle(0, i * ItemHeight, Bounds.Width, ItemHeight);
    }
}

public sealed class MenuItem : Control
{
    private readonly Action _action;

    public string Text { get; }

    public MenuItem(string text, Action action)
    {
        Text = text;
        _action = action;
    }

    public override bool HandleInput(InputState input)
    {
        if (!Visible || !Enabled)
            return false;
        bool inside = HitTest(input.MousePosition);
        if (inside && input.LeftReleased)
        {
            _action();
            HideParentPopup();
            return true;
        }
        return false;
    }

    public override void Draw(SpriteBatch sb, UIRenderer renderer, Theme theme)
    {
        if (!Visible)
            return;
        if (IsHovered)
            renderer.FillRect(sb, AbsoluteBounds, theme.ButtonBackgroundHover);
        renderer.DrawTextLeft(sb, Text, AbsoluteBounds, theme.Font, theme.FontScale, TextColor ?? theme.TextNormal, theme.Padding);
    }

    private void HideParentPopup()
    {
        Control? current = Parent;
        while (current is not null)
        {
            if (current is PopupMenu popup)
            {
                popup.Visible = false;
                return;
            }
            current = current.Parent;
        }
    }
}
