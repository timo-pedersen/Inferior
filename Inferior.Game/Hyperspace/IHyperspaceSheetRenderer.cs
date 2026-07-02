using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Inferior.Rendering;

namespace Inferior.Game.Hyperspace;

/// <summary>
/// Pluggable renderer for the two hyperspace sheets (upper and lower corridor walls).
/// Implement this to change the visual style without touching flight logic.
/// sheetsProgress: 0 = sheets not started, 1 = sheets fully cover the screen.
/// </summary>
public interface IHyperspaceSheetRenderer
{
    void Update(double dt, Camera3D camera);
    void Draw(GraphicsDevice gd, Camera3D camera, float sheetsProgress);
    void Dispose();
}
