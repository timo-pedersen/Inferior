using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Shared BasicEffect presets. The unlit/vertex-colour configuration below was four
/// independent, byte-for-byte-identical `new BasicEffect(gd) { ... }` constructions
/// (debug lines, grid overlays) across Inferior.Game/Inferior.Rendering/
/// Inferior.ObjectDesigner (A1 inventory finding). `SystemSpaceState.cs`'s celestial-body
/// effect (lit, no vertex colour) is a genuinely different configuration and does not
/// belong here.
/// </summary>
public static class BasicEffectPresets
{
    public static BasicEffect UnlitVertexColour(GraphicsDevice graphicsDevice)
        => new(graphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
        };
}
