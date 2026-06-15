using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.StationGen;

public static class StationTextureRegistry
{
    private static readonly Dictionary<SurfaceTexture, Texture2D> _textures = [];
    private static readonly Dictionary<SurfaceTexture, Vector3>   _colors   = [];
    private static bool _initialized;

    public static Texture2D White { get; private set; } = null!;

    public static void Initialize(GraphicsDevice gd)
    {
        if (_initialized) return;
        _initialized = true;

        White = MakeFlat(gd, Color.White);

        // Session A: flat placeholder colours — replaced with procedural content in Session B.
        Register(gd, SurfaceTexture.CleanPanel,      new Color(200, 195, 185));
        Register(gd, SurfaceTexture.TechPanel,       new Color(155, 165, 175));
        Register(gd, SurfaceTexture.IndustrialPanel, new Color(120, 115, 108));
        Register(gd, SurfaceTexture.CargoPanel,      new Color(148, 132, 108));
        Register(gd, SurfaceTexture.WornPanel,       new Color(130, 125, 118));
        // Glass passes vertex colour through unchanged — always pure white.
        _textures[SurfaceTexture.Glass] = White;
        _colors  [SurfaceTexture.Glass] = Vector3.One;
    }

    public static Texture2D Get(SurfaceTexture t)      => _textures[t];
    public static Vector3   GetColor(SurfaceTexture t) => _colors[t];

    private static void Register(GraphicsDevice gd, SurfaceTexture t, Color c)
    {
        _textures[t] = MakeFlat(gd, c);
        _colors[t]   = new Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    private static Texture2D MakeFlat(GraphicsDevice gd, Color c)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { c });
        return tex;
    }

    internal static void SetTexture(SurfaceTexture st, Texture2D texture)
    {
        _textures[st] = texture;
    }
}
