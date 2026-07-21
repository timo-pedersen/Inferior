namespace Inferior.UI;

public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    public Thickness(int uniform) : this(uniform, uniform, uniform, uniform) { }

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;

    public static Thickness Zero => new(0);
}
