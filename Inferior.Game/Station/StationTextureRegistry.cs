using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

public static class StationTextureRegistry
{
    private static readonly Dictionary<SurfaceTexture, Texture2D> _textures = [];
    private static bool _initialized;

    public static Texture2D White { get; private set; } = null!;

    public static void Initialize(GraphicsDevice gd)
    {
        if (_initialized) return;
        _initialized = true;

        White = MakeFlat(gd, Color.White);

        // Session A: flat placeholder colours — replaced with procedural content in Session B.
        _textures[SurfaceTexture.CleanPanel]      = MakeFlat(gd, new Color(200, 195, 185));
        _textures[SurfaceTexture.TechPanel]       = MakeFlat(gd, new Color(155, 165, 175));
        _textures[SurfaceTexture.IndustrialPanel] = MakeFlat(gd, new Color(120, 115, 108));
        _textures[SurfaceTexture.CargoPanel]      = MakeFlat(gd, new Color(148, 132, 108));
        _textures[SurfaceTexture.WornPanel]       = MakeFlat(gd, new Color(130, 125, 118));
    }

    public static Texture2D Get(SurfaceTexture t) => _textures[t];

    private static Texture2D MakeFlat(GraphicsDevice gd, Color c)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { c });
        return tex;
    }
}
