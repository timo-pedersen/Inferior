using Microsoft.Xna.Framework;

namespace Inferior.UI.Controls;

public sealed class ChoiceGroup<T> where T : notnull
{
    private readonly Dictionary<T, ToggleButton> _buttons = [];
    private bool _updating;
    private T _selectedValue;

    public ChoiceGroup(T selectedValue)
    {
        _selectedValue = selectedValue;
    }

    public T SelectedValue
    {
        get => _selectedValue;
        set => Select(value);
    }

    public event Action<T>? SelectionChanged;

    public ToggleButton AddChoice(T value, string text, Rectangle bounds)
    {
        var button = new ToggleButton(text, bounds);
        _buttons[value] = button;
        button.Toggled += (_, _) => Select(value);
        SyncButtons();
        return button;
    }

    public void Select(T value)
    {
        if (!_buttons.ContainsKey(value))
            _selectedValue = value;

        bool changed = !EqualityComparer<T>.Default.Equals(_selectedValue, value);
        _selectedValue = value;
        SyncButtons();
        if (changed)
            SelectionChanged?.Invoke(value);
    }

    private void SyncButtons()
    {
        if (_updating)
            return;
        _updating = true;
        foreach ((T value, ToggleButton button) in _buttons)
        {
            bool selected = EqualityComparer<T>.Default.Equals(value, _selectedValue);
            button.SetState(selected, selected);
        }
        _updating = false;
    }
}
