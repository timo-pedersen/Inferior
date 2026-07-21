namespace Inferior.ObjectDesigner;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new ObjectDesignerGame();
        game.Run();
    }
}
